using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Palsys.Server.Data;
using Palsys.Server.Identity;

namespace Palsys.Server.Controllers;

[ApiController]
[Route("internal/v1")]
public sealed class DataController : ControllerBase
{
    private readonly PalsDataStore store;
    private readonly DeveloperIdentityStore identity;
    private readonly string hostKey;

    public DataController(PalsDataStore store, DeveloperIdentityStore identity, IConfiguration configuration)
    {
        this.store = store;
        this.identity = identity;
        hostKey = configuration["HOST_SHARED_KEY"] ?? "";
    }

    [HttpPost("auth/login")]
    public async Task<IActionResult> Login([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var email = String(body, "email")?.Trim().ToLowerInvariant();
        if (!IsSchoolEmail(email)) return Unauthorized(Error("account_not_recognized", "The account format is not recognized."));
        var termsAccepted = body.TryGetProperty("termsAccepted", out var terms) && terms.ValueKind == JsonValueKind.True;
        if (!termsAccepted) return BadRequest(Error("terms_required", "Accept the PALs terms before signing in."));
        await store.ProvisionStudentAsync(email!, cancellationToken);
        var user = await store.FindUserAsync(email!, cancellationToken);
        if (user is null) return Unauthorized(Error("account_not_recognized", "The account is not active."));
        var sessionKey = await store.CreateSessionAsync(user, cancellationToken);
        return Ok(new { sessionKey, account = new { email = user.Email, role = user.Role, status = user.Status } });
    }

    [HttpPost("auth/admin/login")]
    public async Task<IActionResult> AdminLogin([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var email = String(body, "email");
        var password = String(body, "password");
        var credential = string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)
            ? null
            : await identity.AuthenticateAsync(email, password, cancellationToken);
        if (credential is null || credential.Role is not ("admin" or "developer"))
            return Unauthorized(Error("invalid_credentials", "The administrator email or password is incorrect."));
        if (identity.RequiresMfa(credential) && !credential.TwoFactorEnabled)
            return StatusCode(StatusCodes.Status428PreconditionRequired, Error("mfa_setup_required", "Set up an authenticator app before signing in to this privileged account."));
        if (credential.TwoFactorEnabled)
        {
            var code = String(body, "mfaCode");
            if (string.IsNullOrWhiteSpace(code))
                return Unauthorized(Error("mfa_required", "Enter the code from your authenticator app."));
            if (!await identity.VerifyAuthenticatorCodeAsync(credential.User, code, cancellationToken))
                return Unauthorized(Error("invalid_mfa_code", "The authenticator code is incorrect or has expired."));
        }
        var user = await store.FindUserAsync(credential.Email, cancellationToken);
        if (user is null) return Unauthorized(Error("invalid_credentials", "The administrator account is not active."));
        var sessionKey = await store.CreateSessionAsync(user, cancellationToken);
        return Ok(new { sessionKey, account = new { email = user.Email, role = credential.Role, status = user.Status } });
    }

    [HttpGet("auth/session")]
    public async Task<IActionResult> AuthSession(CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var user = await SessionUser(cancellationToken);
        return user is null ? Unauthorized(Error("authentication_required", "Sign in before requesting the current session.")) : Ok(new { authenticated = true, account = user });
    }

    [HttpDelete("auth/session")]
    public async Task<IActionResult> AuthLogout(CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var token = Request.Headers["X-Pals-Session"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token)) await store.RevokeSessionAsync(token, cancellationToken);
        return Ok(new { authenticated = false });
    }

    [HttpPost("accounts/provision")]
    public async Task<IActionResult> Provision([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var email = String(body, "email");
        if (!IsSchoolEmail(email)) return Unauthorized(Error("account_not_recognized", "The account format is not recognized."));
        var id = await store.ProvisionStudentAsync(email!, cancellationToken);
        return Ok(new { id, email = email!.ToLowerInvariant(), role = "student" });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var rows = await store.QueryAsync("""
            SELECT u.id, u.email, u.display_name, u.account_status, sp.grade_level, sp.strand, sp.timezone
            FROM users u LEFT JOIN student_profiles sp ON sp.user_id = u.id WHERE u.id = @id LIMIT 1
            """, cancellationToken, ("@id", current.User!.Id));
        var account = rows.SingleOrDefault() ?? new Dictionary<string, object?>
        {
            ["id"] = current.User.Id,
            ["email"] = current.User.Email,
            ["display_name"] = current.User.DisplayName,
            ["role"] = current.User.Role,
            ["account_status"] = current.User.Status
        };
        return Ok(new { account });
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var rows = await store.QueryAsync("""
            SELECT
              (SELECT COUNT(*) FROM study_sessions WHERE created_by_user_id = @user AND session_status IN ('confirmed', 'completed') AND scheduled_start_utc >= DATE_FORMAT(UTC_TIMESTAMP(3), '%Y-%m-01')) AS sessions_this_month,
              (SELECT COALESCE(SUM(TIMESTAMPDIFF(MINUTE, scheduled_start_utc, scheduled_end_utc)) / 60, 0) FROM study_sessions WHERE created_by_user_id = @user AND session_status = 'completed') AS hours_logged,
              (SELECT COUNT(DISTINCT sp.user_id) FROM session_participants sp JOIN study_sessions ss ON ss.id = sp.study_session_id WHERE ss.created_by_user_id = @user AND sp.user_id <> @user) AS tutor_connections,
              (SELECT COUNT(*) FROM learning_requests WHERE tutee_user_id = @user AND request_status = 'open') AS open_requests
            """, cancellationToken, ("@user", current.User!.Id));
        return Ok(rows.SingleOrDefault() ?? new Dictionary<string, object?>());
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var displayName = String(body, "displayName");
        if (!string.IsNullOrWhiteSpace(displayName))
            await store.ExecuteAsync("UPDATE users SET display_name = @name WHERE id = @id", cancellationToken, ("@name", displayName.Trim()), ("@id", current.User!.Id));
        var grade = String(body, "grade")?.Replace("Grade ", "", StringComparison.OrdinalIgnoreCase);
        var strand = String(body, "section");
        if (byte.TryParse(grade, out var gradeLevel) && !string.IsNullOrWhiteSpace(strand))
        {
            await store.ExecuteAsync("""
                INSERT INTO student_profiles (user_id, grade_level, strand)
                VALUES (@user, @grade, @strand)
                ON DUPLICATE KEY UPDATE grade_level = VALUES(grade_level), strand = VALUES(strand), updated_at = CURRENT_TIMESTAMP(3)
                """, cancellationToken, ("@user", current.User!.Id), ("@grade", gradeLevel), ("@strand", strand.Trim()));
        }
        return await Me(cancellationToken);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (!Privileged(current.User!)) return Forbidden("Only administrators can list accounts.");
        return Ok(new { items = await store.QueryAsync("SELECT id, email, display_name, account_status, created_at FROM users ORDER BY email", cancellationToken) });
    }

    [HttpGet("tutors")]
    public async Task<IActionResult> Tutors(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var rows = await store.QueryAsync("""
            SELECT u.id, u.email, u.display_name, sp.grade_level, sp.strand,
                   GROUP_CONCAT(DISTINCT s.name ORDER BY s.name SEPARATOR ', ') AS subjects
            FROM users u JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id
            LEFT JOIN student_profiles sp ON sp.user_id = u.id
            LEFT JOIN tutor_subjects ts ON ts.tutor_user_id = u.id
            LEFT JOIN subjects s ON s.id = ts.subject_id
            WHERE u.account_status = 'active' AND r.role_key = 'tutor'
            GROUP BY u.id, u.email, u.display_name, sp.grade_level, sp.strand ORDER BY u.display_name
            """, cancellationToken);
        return Ok(new { items = rows });
    }

    [HttpGet("tutees")]
    public async Task<IActionResult> Tutees(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var rows = await store.QueryAsync("""
            SELECT lr.id AS request_id, u.id AS user_id, u.display_name, sp.grade_level, sp.strand,
                   s.name AS subject, lr.requested_help, lr.current_proficiency, lr.created_at
            FROM learning_requests lr JOIN users u ON u.id = lr.tutee_user_id
            JOIN subjects s ON s.id = lr.subject_id LEFT JOIN student_profiles sp ON sp.user_id = u.id
            WHERE lr.request_status = 'open' AND u.account_status = 'active' ORDER BY lr.created_at DESC
            """, cancellationToken);
        return Ok(new { items = rows });
    }

    [HttpGet("learning-requests")]
    public async Task<IActionResult> LearningRequests(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var rows = await store.QueryAsync("""
            SELECT lr.id, s.name AS subject, lr.topic_id, lr.current_proficiency, lr.requested_help,
                   lr.preferred_format, lr.request_status, lr.created_at, lr.updated_at
            FROM learning_requests lr JOIN subjects s ON s.id = lr.subject_id
            WHERE lr.tutee_user_id = @id ORDER BY lr.created_at DESC
            """, cancellationToken, ("@id", current.User!.Id));
        return Ok(new { items = rows });
    }

    [HttpPost("learning-requests")]
    public async Task<IActionResult> CreateLearningRequest([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var subject = String(body, "subject");
        if (string.IsNullOrWhiteSpace(subject)) return BadRequest(Error("subject_required", "A subject is required."));
        var subjectId = await SubjectId(subject!, cancellationToken);
        await store.ExecuteAsync("""
            INSERT INTO learning_requests (tutee_user_id, subject_id, current_proficiency, requested_help, preferred_format)
            VALUES (@user, @subject, @proficiency, @help, @format)
            """, cancellationToken,
            ("@user", current.User!.Id), ("@subject", subjectId),
            ("@proficiency", Number(body, "currentProficiency")),
            ("@help", String(body, "requestedHelp")),
            ("@format", String(body, "preferredFormat") ?? "either"));
        return Ok(new { status = "created" });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var rows = await store.QueryAsync("""
            SELECT ss.id, s.name AS subject, ss.scheduled_start_utc, ss.scheduled_end_utc,
                   ss.session_status, ss.created_by_user_id
            FROM study_sessions ss JOIN session_participants sp ON sp.study_session_id = ss.id
            JOIN subjects s ON s.id = ss.subject_id
            WHERE sp.user_id = @id OR ss.created_by_user_id = @id
            GROUP BY ss.id, s.name, ss.scheduled_start_utc, ss.scheduled_end_utc, ss.session_status, ss.created_by_user_id
            ORDER BY ss.scheduled_start_utc DESC
            """, cancellationToken, ("@id", current.User!.Id));
        return Ok(new { items = rows });
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var subject = String(body, "subject");
        var start = String(body, "scheduledStartUtc");
        var end = String(body, "scheduledEndUtc");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
            return BadRequest(Error("session_fields_required", "Subject, start, and end are required."));
        var subjectId = await SubjectId(subject!, cancellationToken);
        var sessionId = await store.ScalarAsync("""
            INSERT INTO study_sessions (session_type, subject_id, created_by_user_id, scheduled_start_utc, scheduled_end_utc)
            VALUES ('one_to_one', @subject, @user, @start, @end); SELECT LAST_INSERT_ID();
            """, cancellationToken, ("@subject", subjectId), ("@user", current.User!.Id), ("@start", start), ("@end", end));
        await store.ExecuteAsync("INSERT INTO session_participants (study_session_id, user_id, participant_role, participation_status) VALUES (@session, @user, 'tutee', 'accepted')", cancellationToken, ("@session", sessionId), ("@user", current.User.Id));
        return Ok(new { id = sessionId, status = "created" });
    }

    [HttpPatch("sessions/{id:long}")]
    public async Task<IActionResult> UpdateSession(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var status = String(body, "status");
        var changed = await store.ExecuteAsync("UPDATE study_sessions SET session_status = @status WHERE id = @id AND created_by_user_id = @user", cancellationToken, ("@status", status), ("@id", id), ("@user", current.User!.Id));
        return changed == 0 ? NotFound(Error("session_not_found", "The session was not found or is not owned by this account.")) : Ok(new { status = "updated" });
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var start = ParseDate(from) ?? DateTime.UtcNow.AddMonths(-1);
        var end = ParseDate(to) ?? DateTime.UtcNow.AddMonths(12);
        if (end < start) return BadRequest(Error("invalid_date_range", "The schedule end must be after the start."));
        var rows = await store.QueryAsync("""
            SELECT event_id, on_date, after_date, event_name, description, is_active
            FROM schedule_events
            WHERE is_active = TRUE AND on_date <= @to AND after_date >= @from
            ORDER BY on_date, event_id
            """, cancellationToken, ("@from", start), ("@to", end));
        return Ok(new { items = rows });
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> CreateSchedule([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var onDate = ParseDate(String(body, "onDate"));
        var afterDate = ParseDate(String(body, "afterDate"));
        var name = String(body, "eventName");
        if (onDate is null || afterDate is null || string.IsNullOrWhiteSpace(name) || afterDate < onDate)
            return BadRequest(Error("invalid_schedule_event", "eventName, OnDate, and AfterDate are required; AfterDate must not precede OnDate."));
        var id = await store.ScalarAsync("INSERT INTO schedule_events (created_by_user_id, on_date, after_date, event_name, description, is_active) VALUES (@user, @on, @after, @name, @description, @active); SELECT LAST_INSERT_ID();", cancellationToken, ("@user", current.User!.Id), ("@on", onDate), ("@after", afterDate), ("@name", name.Trim()), ("@description", String(body, "description")), ("@active", Boolean(body, "isActive", true)));
        return Ok(new { eventId = id, status = "created" });
    }

    [HttpPatch("schedule/{id:long}")]
    public async Task<IActionResult> UpdateSchedule(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (!Privileged(current.User!)) return Forbidden("Only administrators can modify schedule events.");
        var onDate = ParseDate(String(body, "onDate"));
        var afterDate = ParseDate(String(body, "afterDate"));
        var changed = await store.ExecuteAsync("UPDATE schedule_events SET on_date = COALESCE(@on, on_date), after_date = COALESCE(@after, after_date), event_name = COALESCE(@name, event_name), description = COALESCE(@description, description), is_active = COALESCE(@active, is_active) WHERE event_id = @id", cancellationToken, ("@on", onDate), ("@after", afterDate), ("@name", String(body, "eventName")), ("@description", String(body, "description")), ("@active", Boolean(body, "isActive", null)), ("@id", id));
        return changed == 0 ? NotFound(Error("schedule_event_not_found", "The schedule event was not found.")) : Ok(new { status = "updated" });
    }

    [HttpDelete("schedule/{id:long}")]
    public async Task<IActionResult> DeleteSchedule(long id, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (!Privileged(current.User!)) return Forbidden("Only administrators can delete schedule events.");
        var changed = await store.ExecuteAsync("UPDATE schedule_events SET is_active = FALSE WHERE event_id = @id", cancellationToken, ("@id", id));
        return changed == 0 ? NotFound(Error("schedule_event_not_found", "The schedule event was not found.")) : Ok(new { status = "deleted" });
    }

    [HttpGet("resources")]
    public async Task<IActionResult> Resources(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        return Ok(new { items = await store.QueryAsync("SELECT id, owner_user_id, title, description, resource_type, resource_url, visibility, moderation_status, created_at, updated_at FROM resources WHERE moderation_status = 'visible' OR owner_user_id = @user ORDER BY created_at DESC", cancellationToken, ("@user", current.User!.Id)) });
    }

    [HttpPost("resources")]
    public async Task<IActionResult> CreateResource([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var title = String(body, "title");
        if (string.IsNullOrWhiteSpace(title)) return BadRequest(Error("title_required", "A resource title is required."));
        await store.ExecuteAsync("INSERT INTO resources (owner_user_id, title, description, resource_type, resource_url, visibility, moderation_status) VALUES (@user, @title, @description, @type, @url, @visibility, 'pending')", cancellationToken, ("@user", current.User!.Id), ("@title", title), ("@description", String(body, "description")), ("@type", String(body, "resourceType") ?? "link"), ("@url", String(body, "resourceUrl")), ("@visibility", String(body, "visibility") ?? "private"));
        return Ok(new { status = "created", moderation = "pending" });
    }

    [HttpPatch("resources/{id:long}")]
    public async Task<IActionResult> UpdateResource(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var changed = await store.ExecuteAsync("UPDATE resources SET title = COALESCE(@title, title), description = COALESCE(@description, description), resource_url = COALESCE(@url, resource_url) WHERE id = @id AND owner_user_id = @user", cancellationToken, ("@title", String(body, "title")), ("@description", String(body, "description")), ("@url", String(body, "resourceUrl")), ("@id", id), ("@user", current.User!.Id));
        return changed == 0 ? NotFound(Error("resource_not_found", "The resource was not found or is not owned by this account.")) : Ok(new { status = "updated" });
    }

    [HttpDelete("resources/{id:long}")]
    public async Task<IActionResult> DeleteResource(long id, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var changed = await store.ExecuteAsync("DELETE FROM resources WHERE id = @id AND owner_user_id = @user", cancellationToken, ("@id", id), ("@user", current.User!.Id));
        return changed == 0 ? NotFound(Error("resource_not_found", "The resource was not found or is not owned by this account.")) : Ok(new { status = "deleted" });
    }

    [HttpGet("feedback")]
    public async Task<IActionResult> Feedback(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        return Ok(new { items = await store.QueryAsync("SELECT fp.id, fp.author_user_id, u.display_name AS author, fp.title, fp.category, fp.description, fp.created_at, COUNT(fv.voter_user_id) AS votes FROM feedback_posts fp JOIN users u ON u.id = fp.author_user_id LEFT JOIN feedback_votes fv ON fv.post_id = fp.id WHERE fp.moderation_status = 'visible' GROUP BY fp.id, fp.author_user_id, u.display_name, fp.title, fp.category, fp.description, fp.created_at ORDER BY fp.created_at DESC", cancellationToken) });
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> CreateFeedback([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        await store.ExecuteAsync("INSERT INTO feedback_posts (author_user_id, title, category, description) VALUES (@user, @title, @category, @description)", cancellationToken, ("@user", current.User!.Id), ("@title", String(body, "title")), ("@category", String(body, "category") ?? "Other"), ("@description", String(body, "description")));
        return Ok(new { status = "created" });
    }

    [HttpPost("feedback/{id:long}/votes")]
    public async Task<IActionResult> Vote(long id, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        await store.ExecuteAsync("INSERT INTO feedback_votes (post_id, voter_user_id) VALUES (@post, @user) ON DUPLICATE KEY UPDATE post_id = post_id", cancellationToken, ("@post", id), ("@user", current.User!.Id));
        return Ok(new { status = "voted" });
    }

    [HttpPost("support")]
    public async Task<IActionResult> Support([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        await store.ExecuteAsync("INSERT INTO support_tickets (requester_user_id, category, subject, message) VALUES (@user, @category, @subject, @message)", cancellationToken, ("@user", current.User!.Id), ("@category", String(body, "category") ?? "Other"), ("@subject", String(body, "subject")), ("@message", String(body, "message")));
        return Ok(new { status = "received" });
    }

    [HttpPost("analytics/events")]
    public async Task<IActionResult> AnalyticsEvent([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var participant = await store.ScalarAsync("SELECT participant_key FROM research_participants rp JOIN research_consents rc ON rc.user_id = rp.user_id WHERE rp.user_id = @user AND rp.withdrawn_at IS NULL AND rc.withdrawn_at IS NULL LIMIT 1", cancellationToken, ("@user", current.User!.Id));
        if (participant is null) return StatusCode(403, Error("analytics_consent_required", "Research analytics requires active consent."));
        await store.ExecuteAsync("INSERT INTO analytics_events (participant_key, event_type, feature_key, duration_seconds) VALUES (@participant, @event, @feature, @duration)", cancellationToken, ("@participant", participant), ("@event", String(body, "eventType") ?? "page_view"), ("@feature", String(body, "featureKey") ?? "unknown"), ("@duration", Number(body, "durationSeconds")));
        return Ok(new { status = "recorded" });
    }

    [HttpPost("analytics/consent")]
    public async Task<IActionResult> AnalyticsConsent([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var version = String(body, "consentVersion") ?? "phase2-v1";
        await store.ExecuteAsync("INSERT INTO research_consents (user_id, consent_type, consent_version) VALUES (@user, 'phase2_usage_analytics', @version)", cancellationToken, ("@user", current.User!.Id), ("@version", version));
        await store.ExecuteAsync("INSERT INTO research_participants (user_id, participant_key) VALUES (@user, UUID()) ON DUPLICATE KEY UPDATE withdrawn_at = NULL", cancellationToken, ("@user", current.User.Id));
        return Ok(new { consented = true, consentVersion = version });
    }

    [HttpGet("analytics/summary")]
    public async Task<IActionResult> AnalyticsSummary(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        return Ok(new { items = await store.QueryAsync("SELECT event_type, feature_key, COUNT(*) AS events, COALESCE(SUM(duration_seconds), 0) AS duration_seconds FROM analytics_events ae JOIN research_participants rp ON rp.participant_key = ae.participant_key WHERE rp.user_id = @user GROUP BY event_type, feature_key ORDER BY events DESC", cancellationToken, ("@user", current.User!.Id)) });
    }

    private async Task<(PalsUser? User, IActionResult? Error)> CurrentUser(CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return (null, Forbid());
        var user = await SessionUser(cancellationToken);
        return user is null ? (null, Unauthorized(Error("authentication_required", "The account is not active."))) : (user, null);
    }

    private async Task<PalsUser?> SessionUser(CancellationToken cancellationToken)
    {
        var session = Request.Headers["X-Pals-Session"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(session) ? null : await store.FindSessionUserAsync(session, cancellationToken);
    }

    private bool TrustedHost()
    {
        var provided = Request.Headers["X-Pals-Host-Key"].FirstOrDefault();
        return !string.IsNullOrEmpty(hostKey) && !string.IsNullOrEmpty(provided) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hostKey), Encoding.UTF8.GetBytes(provided));
    }

    private static bool Privileged(PalsUser user) => user.Role is "admin" or "developer";
    private static IActionResult Forbidden(string message) => new ObjectResult(Error("forbidden", message)) { StatusCode = StatusCodes.Status403Forbidden };
    private static object Error(string code, string message) => new { error = new { code, message } };
    private static string? String(JsonElement body, string name) => body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Number(JsonElement body, string name) => body.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static bool? Boolean(JsonElement body, string name, bool? fallback) => body.TryGetProperty(name, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False) ? value.GetBoolean() : fallback;
    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { "MM-dd-yy:HH-mm", "MM-dd-yy:HH-mm-", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "O" };
        return DateTime.TryParseExact(value.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
    }
    private async Task<long> SubjectId(string name, CancellationToken cancellationToken)
    {
        await store.ExecuteAsync("INSERT INTO subjects (name, is_active) VALUES (@name, TRUE) ON DUPLICATE KEY UPDATE is_active = TRUE", cancellationToken, ("@name", name.Trim()));
        return Convert.ToInt64(await store.ScalarAsync("SELECT id FROM subjects WHERE name = @name LIMIT 1", cancellationToken, ("@name", name.Trim())));
    }
    private static bool IsSchoolEmail(string? email) => email is not null && email.EndsWith("@cubao.sti.edu.ph", StringComparison.OrdinalIgnoreCase) && System.Text.RegularExpressions.Regex.IsMatch(email[..^"@cubao.sti.edu.ph".Length], @"^[a-z]+\.[0-9]{6,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
