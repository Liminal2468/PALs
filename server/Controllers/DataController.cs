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
    private readonly PendingLoginStore pendingLogins;
    private readonly IWebHostEnvironment environment;
    private readonly string hostKey;
    private const string DevelopmentLoginEmail = "student.123456@cubao.sti.edu.ph";
    private const string TermsVersion = "pals-terms-v1";

    public DataController(
        PalsDataStore store,
        DeveloperIdentityStore identity,
        PendingLoginStore pendingLogins,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        this.store = store;
        this.identity = identity;
        this.pendingLogins = pendingLogins;
        this.environment = environment;
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
#if DEBUG
        if (!environment.IsDevelopment())
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("verification_delivery_unavailable", "Email verification is not configured."));
        if (!email!.Equals(DevelopmentLoginEmail, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(Error("account_not_recognized", "Use the fixed Development test account."));

        var pending = pendingLogins.Create(email, termsAccepted);
        if (pending is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("verification_capacity_reached", "Sign-in is temporarily unavailable. Try again later."));
        Console.WriteLine($"TODO: Replace this Development-only verification-code output with AWS SES. Code: {pending.Code}");
        return Ok(new { attemptId = pending.AttemptId, expiresInSeconds = pending.ExpiresInSeconds });
#else
        return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("verification_delivery_unavailable", "Email verification is not configured."));
#endif
    }

    [HttpPost("auth/verify")]
    public async Task<IActionResult> VerifyLogin([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var attemptId = String(body, "attemptId");
        var code = String(body, "code");
        if (string.IsNullOrWhiteSpace(attemptId) || code is null || code.Length != 6 || !code.All(char.IsAsciiDigit))
            return BadRequest(Error("invalid_verification_code", "Verification code must contain exactly six digits."));

        var verification = pendingLogins.Verify(attemptId, code);
        if (verification.Status != PendingLoginVerificationStatus.Verified)
        {
            var (statusCode, errorCode, message) = verification.Status switch
            {
                PendingLoginVerificationStatus.InvalidCode =>
                    (StatusCodes.Status400BadRequest, "invalid_verification_code", "The verification code is incorrect."),
                PendingLoginVerificationStatus.Expired =>
                    (StatusCodes.Status410Gone, "verification_attempt_expired", "The verification attempt has expired."),
                PendingLoginVerificationStatus.AttemptsExceeded =>
                    (StatusCodes.Status410Gone, "verification_attempts_exceeded", "Too many incorrect codes were entered."),
                _ =>
                    (StatusCodes.Status410Gone, "verification_attempt_not_found", "The verification attempt is no longer available."),
            };
            return StatusCode(statusCode, Error(errorCode, message));
        }

        if (verification.Email is null || !verification.TermsAccepted)
            return StatusCode(StatusCodes.Status502BadGateway, Error("invalid_login_attempt", "The verification attempt could not be completed."));

        await store.ProvisionStudentAsync(verification.Email, cancellationToken);
        await store.RecordTermsAcceptanceAsync(verification.Email, TermsVersion, cancellationToken);
        var user = await store.FindUserAsync(verification.Email, cancellationToken);
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
        var account = await store.GetProfileAsync(current.User!.Id, cancellationToken);
        return account is null ? Unauthorized(Error("authentication_required", "The account is not active.")) : Ok(new { account });
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
        var displayName = String(body, "displayName")?.Trim();
        if (body.TryGetProperty("displayName", out _) && string.IsNullOrWhiteSpace(displayName))
            return BadRequest(Error("display_name_required", "A display name is required."));
        if (displayName?.Length > 120) return BadRequest(Error("display_name_too_long", "The display name is too long."));

        var gradeText = String(body, "grade")?.Replace("Grade ", "", StringComparison.OrdinalIgnoreCase).Trim();
        int? grade = null;
        if (body.TryGetProperty("grade", out _))
        {
            if (!int.TryParse(gradeText, out var parsedGrade) || parsedGrade is < 11 or > 12)
                return BadRequest(Error("invalid_grade", "Grade must be Grade 11 or Grade 12."));
            grade = parsedGrade;
        }

        var section = String(body, "section")?.Trim();
        if (section?.Length > 40) return BadRequest(Error("section_too_long", "The section is too long."));
        var bio = String(body, "bio");
        if (bio?.Length > 300) return BadRequest(Error("bio_too_long", "The bio is too long."));

        var roles = Strings(body, "roles");
        if (InvalidArray(body, "roles") || InvalidArray(body, "subjects"))
            return BadRequest(Error("invalid_profile_selection", "Roles and subjects must be arrays."));
        if (roles is not null && roles.Any(role => role is not ("tutor" or "tutee")))
            return BadRequest(Error("invalid_profile_role", "Only tutor and tutee roles can be selected here."));
        var subjects = Strings(body, "subjects");
        if (subjects is not null && (subjects.Count > 20 || subjects.Any(subject => subject.Length is 0 or > 160)))
            return BadRequest(Error("invalid_subject_selection", "The subject selection is invalid."));

        try
        {
            await store.UpdateProfileAsync(current.User!.Id, displayName, grade, section, bio, roles, subjects, cancellationToken);
        }
        catch (ProfileRequirementsException exception)
        {
            return BadRequest(Error("profile_fields_required", exception.Message));
        }
        return await Me(cancellationToken);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (!Privileged(current.User!)) return Forbidden("Only administrators can list accounts.");
        var accounts = await store.ListAccountSummariesAsync(cancellationToken);
        return Ok(new { items = accounts.Select(AccountDto) });
    }

    [HttpPatch("users/{id:long}")]
    public async Task<IActionResult> UpdateUser(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (!Privileged(current.User!)) return Forbidden("Only administrators can manage accounts.");

        var target = await store.FindAccountSummaryAsync(id, cancellationToken);
        if (target is null) return NotFound(Error("account_not_found", "The account was not found."));
        var status = String(body, "accountStatus") ?? String(body, "status");
        if (status is not null && status is not ("pending" or "active" or "suspended" or "disabled"))
            return BadRequest(Error("invalid_account_status", "The account status is invalid."));
        var roles = Strings(body, "roles");
        if (InvalidArray(body, "roles")) return BadRequest(Error("invalid_account_role", "Roles must be an array."));
        if (roles is not null && roles.Any(role => role is not ("tutor" or "tutee")))
            return BadRequest(Error("invalid_account_role", "Only tutor and tutee roles can be managed here."));
        if (status is null && roles is null)
            return BadRequest(Error("account_update_empty", "Provide an account status or roles to update."));
        if (target.Roles.Any(role => role is "admin" or "developer") && roles is not null)
            return Forbidden("Privileged roles cannot be changed through this account-management route.");

        var updated = await store.UpdateAccountAsync(current.User!.Id, id, status, roles, cancellationToken);
        return updated is null ? NotFound(Error("account_not_found", "The account was not found.")) : Ok(new { account = AccountDto(updated) });
    }

    [HttpGet("pages")]
    public async Task<IActionResult> Pages(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var pages = await store.ListPagesAsync(cancellationToken);
        if (!Privileged(current.User!))
        {
            var currentRole = current.User!.Role;
            pages = pages.Where(page =>
            {
                var enabled = page["isEnabled"] is true;
                var allowed = page["allowedRoles"] as string[] ?? [];
                return enabled && (allowed.Length == 0 || allowed.Contains(currentRole, StringComparer.OrdinalIgnoreCase));
            }).ToArray();
        }
        return Ok(new { items = pages });
    }

    [HttpPatch("pages/{id:long}")]
    public async Task<IActionResult> UpdatePage(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (!Privileged(current.User!)) return Forbidden("Only administrators can manage page metadata.");
        var title = String(body, "title");
        if (title?.Length > 120) return BadRequest(Error("page_title_too_long", "The page title is too long."));
        var enabled = Boolean(body, "isEnabled", null);
        var showInNavigation = Boolean(body, "showInNavigation", null);
        var allowedRoles = Strings(body, "allowedRoles");
        if (InvalidArray(body, "allowedRoles")) return BadRequest(Error("invalid_page_roles", "Allowed roles must be an array."));
        if (allowedRoles is not null && allowedRoles.Any(role => role is not ("student" or "tutor" or "tutee" or "admin" or "developer")))
            return BadRequest(Error("invalid_page_roles", "The page role visibility is invalid."));
        if (title is null && enabled is null && showInNavigation is null && allowedRoles is null)
            return BadRequest(Error("page_update_empty", "Provide page metadata to update."));

        var page = await store.UpdatePageAsync(current.User!.Id, id, title, enabled, showInNavigation, allowedRoles, cancellationToken);
        return page is null ? NotFound(Error("page_not_found", "The page was not found.")) : Ok(new { page });
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
        var scheduledStart = ParseDate(start);
        var scheduledEnd = ParseDate(end);
        if (scheduledStart is null || scheduledEnd is null || scheduledEnd <= scheduledStart)
            return BadRequest(Error("invalid_session_time", "The session end must be after the start."));
        var subjectId = await SubjectId(subject!, cancellationToken);
        var sessionId = await store.ScalarAsync("""
            INSERT INTO study_sessions (session_type, subject_id, created_by_user_id, scheduled_start_utc, scheduled_end_utc)
            VALUES ('one_to_one', @subject, @user, @start, @end); SELECT LAST_INSERT_ID();
            """, cancellationToken, ("@subject", subjectId), ("@user", current.User!.Id), ("@start", scheduledStart), ("@end", scheduledEnd));
        await store.ExecuteAsync("INSERT INTO session_participants (study_session_id, user_id, participant_role, participation_status) VALUES (@session, @user, 'tutee', 'accepted')", cancellationToken, ("@session", sessionId), ("@user", current.User.Id));
        return Ok(new { id = sessionId, status = "created" });
    }

    [HttpPatch("sessions/{id:long}")]
    public async Task<IActionResult> UpdateSession(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var status = String(body, "status");
        if (status is not ("confirmed" or "cancelled" or "completed" or "no_show"))
            return BadRequest(Error("invalid_session_status", "The session status is invalid."));
        var existing = await store.QueryAsync("""
            SELECT ss.session_status
            FROM study_sessions ss
            WHERE ss.id = @id AND (ss.created_by_user_id = @user OR EXISTS (SELECT 1 FROM session_participants sp WHERE sp.study_session_id = ss.id AND sp.user_id = @user AND sp.participation_status IN ('invited', 'accepted')))
            LIMIT 1
            """, cancellationToken, ("@id", id), ("@user", current.User!.Id));
        if (existing.Count == 0) return NotFound(Error("session_not_found", "The session was not found or is not accessible to this account."));
        var previous = Convert.ToString(existing[0]["session_status"]);
        var validTransition = previous switch
        {
            "requested" => status is "confirmed" or "cancelled",
            "confirmed" => status is "completed" or "cancelled" or "no_show",
            _ => false,
        };
        if (!validTransition) return Conflict(Error("invalid_session_transition", "The session cannot move from its current status to the requested status."));
        var changed = await store.ExecuteAsync("UPDATE study_sessions SET session_status = @status WHERE id = @id AND session_status = @previous", cancellationToken, ("@status", status), ("@previous", previous), ("@id", id));
        if (changed == 0) return Conflict(Error("session_update_conflict", "The session changed before this update completed."));
        await store.ExecuteAsync("INSERT INTO session_audits (actor_user_id, session_id, previous_status, next_status) VALUES (@actor, @session, @previous, @next)", cancellationToken, ("@actor", current.User!.Id), ("@session", id), ("@previous", previous), ("@next", status));
        return Ok(new { status = "updated" });
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
        if (title.Length > 180) return BadRequest(Error("title_too_long", "The resource title is too long."));
        var description = String(body, "description");
        if (description?.Length > 4000) return BadRequest(Error("description_too_long", "The resource description is too long."));
        var resourceUrl = String(body, "resourceUrl");
        if (!string.IsNullOrWhiteSpace(resourceUrl) && (!Uri.TryCreate(resourceUrl, UriKind.Absolute, out var parsedUrl) || parsedUrl.Scheme is not ("https" or "http")))
            return BadRequest(Error("invalid_resource_url", "The resource URL must be an absolute HTTP or HTTPS URL."));
        var resourceType = String(body, "resourceType") ?? "link";
        var visibility = String(body, "visibility") ?? "private";
        if (resourceType.Length > 40 || visibility is not ("private" or "community"))
            return BadRequest(Error("invalid_resource_metadata", "The resource type or visibility is invalid."));
        var resourceId = await store.ScalarAsync("INSERT INTO resources (owner_user_id, title, description, resource_type, resource_url, visibility, moderation_status) VALUES (@user, @title, @description, @type, @url, @visibility, 'pending'); SELECT LAST_INSERT_ID();", cancellationToken, ("@user", current.User!.Id), ("@title", title.Trim()), ("@description", description), ("@type", resourceType), ("@url", resourceUrl), ("@visibility", visibility));
        await store.ExecuteAsync("INSERT INTO resource_audits (resource_id, reviewer_user_id, audit_action, notes) VALUES (@resource, @reviewer, 'created', 'Resource submitted for moderation.')", cancellationToken, ("@resource", resourceId), ("@reviewer", current.User.Id));
        return Ok(new { id = resourceId, status = "created", moderation = "pending" });
    }

    [HttpPatch("resources/{id:long}")]
    public async Task<IActionResult> UpdateResource(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var title = String(body, "title");
        var description = String(body, "description");
        var resourceUrl = String(body, "resourceUrl");
        if (title?.Length > 180 || description?.Length > 4000) return BadRequest(Error("resource_field_too_long", "The resource data is too long."));
        if (!string.IsNullOrWhiteSpace(resourceUrl) && (!Uri.TryCreate(resourceUrl, UriKind.Absolute, out var parsedUrl) || parsedUrl.Scheme is not ("https" or "http")))
            return BadRequest(Error("invalid_resource_url", "The resource URL must be an absolute HTTP or HTTPS URL."));
        var changed = await store.ExecuteAsync("UPDATE resources SET title = COALESCE(@title, title), description = COALESCE(@description, description), resource_url = COALESCE(@url, resource_url), moderation_status = 'pending' WHERE id = @id AND owner_user_id = @user", cancellationToken, ("@title", title), ("@description", description), ("@url", resourceUrl), ("@id", id), ("@user", current.User!.Id));
        if (changed == 0) return NotFound(Error("resource_not_found", "The resource was not found or is not owned by this account."));
        await store.ExecuteAsync("INSERT INTO resource_audits (resource_id, reviewer_user_id, audit_action, notes) VALUES (@resource, @reviewer, 'updated', 'Owner updated the resource; moderation returned to pending.')", cancellationToken, ("@resource", id), ("@reviewer", current.User.Id));
        return Ok(new { status = "updated", moderation = "pending" });
    }

    [HttpDelete("resources/{id:long}")]
    public async Task<IActionResult> DeleteResource(long id, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var changed = await store.ExecuteAsync("UPDATE resources SET moderation_status = 'removed' WHERE id = @id AND owner_user_id = @user", cancellationToken, ("@id", id), ("@user", current.User!.Id));
        if (changed == 0) return NotFound(Error("resource_not_found", "The resource was not found or is not owned by this account."));
        await store.ExecuteAsync("INSERT INTO resource_audits (resource_id, reviewer_user_id, audit_action, notes) VALUES (@resource, @reviewer, 'removed', 'Owner removed the resource.')", cancellationToken, ("@resource", id), ("@reviewer", current.User.Id));
        return Ok(new { status = "deleted" });
    }

    [HttpPatch("resources/{id:long}/moderation")]
    public async Task<IActionResult> ModerateResource(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (!Privileged(current.User!)) return Forbidden("Only administrators can moderate resources.");
        var moderationStatus = String(body, "moderationStatus") ?? String(body, "status");
        if (moderationStatus is not ("visible" or "flagged" or "removed"))
            return BadRequest(Error("invalid_moderation_status", "The moderation status is invalid."));
        var changed = await store.ExecuteAsync("UPDATE resources SET moderation_status = @status WHERE id = @id", cancellationToken, ("@status", moderationStatus), ("@id", id));
        if (changed == 0) return NotFound(Error("resource_not_found", "The resource was not found."));
        var auditAction = moderationStatus switch { "visible" => "approved", "removed" => "removed", _ => "flagged" };
        await store.ExecuteAsync("INSERT INTO resource_audits (resource_id, reviewer_user_id, audit_action, notes) VALUES (@resource, @reviewer, @action, @notes)", cancellationToken, ("@resource", id), ("@reviewer", current.User!.Id), ("@action", auditAction), ("@notes", $"Administrator set moderation status to {moderationStatus}."));
        return Ok(new { status = "updated", moderation = moderationStatus });
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
        var title = String(body, "title")?.Trim();
        var category = String(body, "category")?.Trim() ?? "Other";
        var description = String(body, "description")?.Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description)) return BadRequest(Error("feedback_fields_required", "A feedback title and description are required."));
        if (title.Length > 120 || category.Length > 80 || description.Length > 2000) return BadRequest(Error("feedback_field_too_long", "The feedback data is too long."));
        await store.ExecuteAsync("INSERT INTO feedback_posts (author_user_id, title, category, description) VALUES (@user, @title, @category, @description)", cancellationToken, ("@user", current.User!.Id), ("@title", title), ("@category", category), ("@description", description));
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
        var category = String(body, "category")?.Trim() ?? "Other";
        var subject = String(body, "subject")?.Trim();
        var message = String(body, "message")?.Trim();
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message)) return BadRequest(Error("support_fields_required", "A support subject and message are required."));
        if (category.Length > 80 || subject.Length > 180 || message.Length > 4000) return BadRequest(Error("support_field_too_long", "The support request is too long."));
        await store.ExecuteAsync("INSERT INTO support_tickets (requester_user_id, category, subject, message) VALUES (@user, @category, @subject, @message)", cancellationToken, ("@user", current.User!.Id), ("@category", category), ("@subject", subject), ("@message", message));
        return Ok(new { status = "received" });
    }

    [HttpPost("analytics/events")]
    public async Task<IActionResult> AnalyticsEvent([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var eventType = String(body, "eventType") ?? "page_view";
        var featureKey = String(body, "featureKey") ?? "unknown";
        var duration = Number(body, "durationSeconds");
        var allowedEvents = new[] { "page_view", "feature_view", "interaction", "session_action", "resource_submit", "feedback_submit", "support_submit" };
        if (!allowedEvents.Contains(eventType, StringComparer.Ordinal) || featureKey.Length > 80 || duration is < 0 or > 86400)
            return BadRequest(Error("invalid_analytics_event", "The analytics event is not allowed or contains invalid values."));
        var participant = await store.ScalarAsync("SELECT participant_key FROM research_participants rp JOIN research_consents rc ON rc.user_id = rp.user_id WHERE rp.user_id = @user AND rp.withdrawn_at IS NULL AND rc.withdrawn_at IS NULL LIMIT 1", cancellationToken, ("@user", current.User!.Id));
        if (participant is null) return StatusCode(403, Error("analytics_consent_required", "Research analytics requires active consent."));
        await store.ExecuteAsync("INSERT INTO analytics_events (participant_key, event_type, feature_key, duration_seconds) VALUES (@participant, @event, @feature, @duration)", cancellationToken, ("@participant", participant), ("@event", eventType), ("@feature", featureKey), ("@duration", duration));
        return Ok(new { status = "recorded" });
    }

    [HttpPost("analytics/consent")]
    public async Task<IActionResult> AnalyticsConsent([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var version = String(body, "consentVersion") ?? "phase2-v1";
        if (version.Length > 64) return BadRequest(Error("invalid_consent_version", "The consent version is too long."));
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
    private static List<string>? Strings(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()?.Trim() ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static bool InvalidArray(JsonElement body, string name) => body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Array;
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
    private static object AccountDto(PalsAccountSummary account) => new { id = account.Id, email = account.Email, displayName = account.DisplayName, accountStatus = account.Status, role = account.Roles.FirstOrDefault() ?? "student", roles = account.Roles };
}
