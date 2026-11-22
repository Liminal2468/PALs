using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
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
    private readonly TotpConfig totpConfig;
    private readonly VerificationEmailSender verificationEmailSender;
    private readonly MatchingAlgorithmRegistry matchingAlgorithms;
    private readonly ILogger<DataController> logger;
    private readonly string hostKey;
    private const string TermsVersion = "pals-terms-v1";

    public DataController(
        PalsDataStore store,
        DeveloperIdentityStore identity,
        PendingLoginStore pendingLogins,
        TotpConfig totpConfig,
        VerificationEmailSender verificationEmailSender,
        MatchingAlgorithmRegistry matchingAlgorithms,
        IConfiguration configuration,
        ILogger<DataController> logger)
    {
        this.store = store;
        this.identity = identity;
        this.pendingLogins = pendingLogins;
        this.totpConfig = totpConfig;
        this.verificationEmailSender = verificationEmailSender;
        this.matchingAlgorithms = matchingAlgorithms;
        this.logger = logger;
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

        var existingUser = await store.FindUserAsync(email!, cancellationToken);
        if (existingUser is not null
            && (existingUser.Role is "student" or "tutor" or "tutee")
            && await identity.HasStudentPasswordAsync(existingUser.Id))
            return Ok(new { passwordRequired = true });

        return await SendVerificationCodeAsync(email!, termsAccepted, cancellationToken);
    }

    [HttpPost("auth/resend")]
    public async Task<IActionResult> ResendVerification([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var attemptId = String(body, "attemptId");
        var pending = string.IsNullOrWhiteSpace(attemptId) ? null : pendingLogins.Resend(attemptId);
        if (pending is null)
            return StatusCode(StatusCodes.Status410Gone, Error("verification_attempt_expired", "Start sign-in again to request a new code."));

        return await SendVerificationCodeAsync(pending, cancellationToken);
    }

    [HttpPost("auth/verify")]
    public async Task<IActionResult> VerifyLogin([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var attemptId = String(body, "attemptId");
        var code = String(body, "code");
        if (string.IsNullOrWhiteSpace(attemptId)
            || code is null
            || code.Length != totpConfig.CodeLength
            || !code.All(char.IsAsciiDigit))
            return BadRequest(Error("invalid_verification_code", $"Verification code must contain exactly {totpConfig.CodeLength} digits."));

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
        var passwordSetupRequired = (user.Role is "student" or "tutor" or "tutee")
            && !await identity.HasStudentPasswordAsync(user.Id);
        return Ok(new
        {
            sessionKey,
            account = new { email = user.Email, role = user.Role, status = user.Status },
            passwordSetupRequired,
        });
    }

    [HttpPost("auth/password")]
    public async Task<IActionResult> SetStudentPassword([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (current.User!.Role is not ("student" or "tutor" or "tutee"))
            return Forbidden("Only student accounts can set a student password.");

        var password = String(body, "password");
        if (string.IsNullOrEmpty(password))
            return BadRequest(Error("password_required", "Enter a password."));

        var result = await identity.SetStudentPasswordAsync(current.User.Id, current.User.Email, password);
        if (!result.Succeeded)
        {
            var message = string.Join(" ", result.Errors.Select(error => error.Description));
            return BadRequest(Error("password_setup_failed", message));
        }

        return Ok(new { passwordSet = true });
    }

    [HttpPost("auth/password/login")]
    public async Task<IActionResult> StudentPasswordLogin([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (!TrustedHost()) return Forbid();
        var email = String(body, "email")?.Trim().ToLowerInvariant();
        var password = String(body, "password");
        var termsAccepted = body.TryGetProperty("termsAccepted", out var terms) && terms.ValueKind == JsonValueKind.True;
        if (!termsAccepted)
            return BadRequest(Error("terms_required", "Accept the PALs terms before signing in."));
        if (!IsSchoolEmail(email) || string.IsNullOrEmpty(password))
            return Unauthorized(Error("invalid_credentials", "The school email or password is incorrect."));

        var user = await store.FindUserAsync(email!, cancellationToken);
        if (user is null
            || user.Role is not ("student" or "tutor" or "tutee")
            || await identity.AuthenticateStudentAsync(user.Id, email!, password) is null)
        {
            return Unauthorized(Error("invalid_credentials", "The school email or password is incorrect."));
        }

        return await SendVerificationCodeAsync(email!, termsAccepted, cancellationToken);
    }

    private async Task<IActionResult> SendVerificationCodeAsync(
        string email,
        bool termsAccepted,
        CancellationToken cancellationToken)
    {
        var pending = pendingLogins.Create(email, termsAccepted);
        if (pending is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("verification_capacity_reached", "Sign-in is temporarily unavailable. Try again later."));

        return await SendVerificationCodeAsync(pending, cancellationToken);
    }

    private async Task<IActionResult> SendVerificationCodeAsync(
        PendingLoginStart pending,
        CancellationToken cancellationToken)
    {
        try
        {
            await verificationEmailSender.SendAsync(pending.Email, pending.Code, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            pendingLogins.Cancel(pending.AttemptId);
            throw;
        }
        catch (AmazonServiceException exception)
        {
            pendingLogins.Cancel(pending.AttemptId);
            logger.LogError("SES verification email delivery failed with AWS error code {ErrorCode}.", exception.ErrorCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("verification_delivery_failed", "The verification email could not be sent. Try again later."));
        }
        catch (AmazonClientException exception)
        {
            pendingLogins.Cancel(pending.AttemptId);
            logger.LogError("SES verification email delivery failed with client error type {ErrorType}.", exception.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("verification_delivery_failed", "The verification email could not be sent. Try again later."));
        }
        catch (InvalidOperationException exception)
        {
            pendingLogins.Cancel(pending.AttemptId);
            logger.LogError("Verification email delivery configuration is invalid: {Message}", exception.Message);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("verification_delivery_unavailable", "Email verification is not configured."));
        }

        return Ok(new
        {
            attemptId = pending.AttemptId,
            expiresInSeconds = pending.ExpiresInSeconds,
            codeLength = pending.CodeLength,
        });
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
              (SELECT COUNT(DISTINCT ss.id) FROM study_sessions ss
               JOIN session_participants mine ON mine.study_session_id = ss.id
               WHERE mine.user_id = @user AND ss.session_status IN ('confirmed', 'completed')
                 AND ss.scheduled_start_utc >= DATE_FORMAT(UTC_TIMESTAMP(3), '%Y-%m-01')
                 AND ss.scheduled_start_utc < DATE_ADD(DATE_FORMAT(UTC_TIMESTAMP(3), '%Y-%m-01'), INTERVAL 1 MONTH)) AS sessions_this_month,
              (SELECT COALESCE(SUM(TIMESTAMPDIFF(MINUTE, ss.scheduled_start_utc, ss.scheduled_end_utc)) / 60, 0)
               FROM study_sessions ss JOIN session_participants mine ON mine.study_session_id = ss.id
               WHERE mine.user_id = @user AND ss.session_status = 'completed') AS hours_logged,
              (SELECT COUNT(DISTINCT peer.user_id) FROM study_sessions ss
               JOIN session_participants mine ON mine.study_session_id = ss.id AND mine.user_id = @user
               JOIN session_participants peer ON peer.study_session_id = ss.id AND peer.user_id <> @user
               WHERE ss.session_status IN ('confirmed', 'completed')) AS tutor_connections,
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
            SELECT u.id, u.display_name, sp.grade_level, sp.strand,
                   GROUP_CONCAT(DISTINCT s.name ORDER BY s.name SEPARATOR ', ') AS subjects
            FROM users u JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id
            LEFT JOIN student_profiles sp ON sp.user_id = u.id
            LEFT JOIN tutor_subjects ts ON ts.tutor_user_id = u.id
            LEFT JOIN subjects s ON s.id = ts.subject_id
            WHERE u.account_status = 'active' AND r.role_key = 'tutor'
            GROUP BY u.id, u.display_name, sp.grade_level, sp.strand ORDER BY u.display_name
            """, cancellationToken);
        return Ok(new { items = rows });
    }

    [HttpGet("tutees")]
    public async Task<IActionResult> Tutees(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (current.User!.Role != "tutor")
            return Forbidden("Only tutors can browse students seeking peer support.");
        var rows = await store.QueryAsync("""
            SELECT lr.id AS request_id, u.id AS user_id, u.display_name, sp.grade_level, sp.strand,
                   s.name AS subject, lr.requested_help, lr.current_proficiency, lr.created_at
            FROM learning_requests lr JOIN users u ON u.id = lr.tutee_user_id
            JOIN subjects s ON s.id = lr.subject_id LEFT JOIN student_profiles sp ON sp.user_id = u.id
            WHERE lr.request_status = 'open' AND u.account_status = 'active' ORDER BY lr.created_at DESC
            """, cancellationToken);
        return Ok(new { items = rows });
    }

    [HttpGet("connect/recommendations")]
    public async Task<IActionResult> ConnectRecommendations([FromQuery] long? requestId, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        if (requestId is <= 0) return BadRequest(Error("invalid_request_id", "The learning request ID must be positive."));
        if (current.User!.Role is not ("tutor" or "student" or "tutee"))
            return Forbidden("Only tutors and students can request matching recommendations.");

        return Ok(new
        {
            algorithm = matchingAlgorithms.ActiveAlgorithm,
            direction = current.User!.Role == "tutor" ? "tutees" : "tutors",
            items = await GetMatchingRecommendationsAsync(current.User, requestId, null, cancellationToken),
        });
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var user = current.User!;
        var isTutor = user.Role == "tutor";
        if (!isTutor && user.Role is not ("student" or "tutee"))
            return Forbidden("Only tutors and students can request a connection.");

        var requestId = Long(body, "requestId");
        var candidateUserId = Long(body, "candidateUserId");
        var scheduledStart = ParseDate(String(body, "scheduledStartUtc"));
        var scheduledEnd = ParseDate(String(body, "scheduledEndUtc"));
        if (requestId is null or <= 0 || candidateUserId is null or <= 0)
            return BadRequest(Error("invalid_match", "A valid learning request and recommended user are required."));
        if (scheduledStart is null || scheduledEnd is null || scheduledStart <= DateTime.UtcNow || scheduledEnd <= scheduledStart)
            return BadRequest(Error("invalid_session_time", "Choose a future start time and an end time after it."));

        var algorithm = matchingAlgorithms.ActiveAlgorithm;
        var recommendation = (await GetMatchingRecommendationsAsync(
            user, requestId, candidateUserId, cancellationToken)).SingleOrDefault();
        if (recommendation is null)
            return Conflict(Error("match_unavailable", "This user is no longer an eligible match for the learning request."));

        var tuteeUserId = isTutor ? candidateUserId.Value : user.Id;
        var tutorUserId = isTutor ? user.Id : candidateUserId.Value;
        var subjectId = await SubjectId(recommendation.Subject, cancellationToken);
        try
        {
            var sessionId = await store.CreateConnectionSessionAsync(
                requestId.Value,
                tuteeUserId,
                tutorUserId,
                user.Id,
                subjectId,
                algorithm.Id,
                recommendation.Score,
                recommendation.ScoreComponents,
                recommendation.Explanation,
                scheduledStart.Value,
                scheduledEnd.Value,
                cancellationToken);
            return Ok(new { sessionId, requestId, status = "requested" });
        }
        catch (ConnectionRequestConflictException exception)
        {
            return Conflict(Error("connection_conflict", exception.Message));
        }
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
        if (current.User!.Role is not ("student" or "tutee"))
            return Forbidden("Only students and tutees can create a learning request.");
        var subject = String(body, "subject");
        if (string.IsNullOrWhiteSpace(subject)) return BadRequest(Error("subject_required", "A subject is required."));
        var requestedHelp = String(body, "requestedHelp")?.Trim();
        var proficiency = Number(body, "currentProficiency");
        var preferredFormat = String(body, "preferredFormat") ?? "either";
        if (requestedHelp?.Length > 4000)
            return BadRequest(Error("requested_help_too_long", "The learning request details are too long."));
        if (proficiency is < 1 or > 5)
            return BadRequest(Error("invalid_proficiency", "Current proficiency must be between 1 and 5."));
        if (preferredFormat is not ("one_to_one" or "group" or "either"))
            return BadRequest(Error("invalid_preferred_format", "The preferred session format is invalid."));
        var subjectId = await SubjectId(subject!, cancellationToken);
        var requestId = await store.CreateLearningRequestAsync(
            current.User.Id, subjectId, proficiency, requestedHelp, preferredFormat, cancellationToken);
        return Ok(new { requestId, status = "created" });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var rows = await store.QueryAsync("""
            SELECT ss.id, s.name AS subject, ss.scheduled_start_utc, ss.scheduled_end_utc,
                   ss.session_status, ss.created_by_user_id,
                   mine.participation_status AS current_participation_status,
                   MAX(CASE WHEN peer.user_id <> @id THEN peer_user.display_name END) AS peer_name,
                   MAX(CASE WHEN peer.user_id <> @id THEN peer.participant_role END) AS peer_role
            FROM study_sessions ss
            JOIN session_participants mine ON mine.study_session_id = ss.id AND mine.user_id = @id
            LEFT JOIN session_participants peer ON peer.study_session_id = ss.id
            LEFT JOIN users peer_user ON peer_user.id = peer.user_id
            JOIN subjects s ON s.id = ss.subject_id
            GROUP BY ss.id, s.name, ss.scheduled_start_utc, ss.scheduled_end_utc, ss.session_status,
                     ss.created_by_user_id, mine.participation_status
            ORDER BY ss.scheduled_start_utc DESC
            """, cancellationToken, ("@id", current.User!.Id));
        return Ok(new { items = rows });
    }

    [HttpPost("sessions")]
    public Task<IActionResult> CreateSession([FromBody] JsonElement body, CancellationToken cancellationToken) =>
        Connect(body, cancellationToken);

    [HttpPatch("sessions/{id:long}")]
    public async Task<IActionResult> UpdateSession(long id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var status = String(body, "status");
        if (status is not ("confirmed" or "cancelled" or "completed" or "no_show"))
            return BadRequest(Error("invalid_session_status", "The session status is invalid."));
        var result = await store.UpdateConnectionSessionAsync(id, current.User!.Id, status, cancellationToken);
        return result switch
        {
            SessionStatusUpdateResult.Updated => Ok(new { status = "updated" }),
            SessionStatusUpdateResult.NotFound => NotFound(Error("session_not_found", "The session was not found or is not accessible to this account.")),
            SessionStatusUpdateResult.InviteeResponseRequired => Forbidden("Only the invited peer can confirm a requested session."),
            _ => Conflict(Error("invalid_session_transition", "The session cannot move from its current status to the requested status.")),
        };
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
        return Ok(new
        {
            items = await store.QueryAsync("""
                SELECT fp.id, fp.author_user_id, u.display_name AS author, fp.title, fp.category,
                       fp.description, fp.created_at, COUNT(fv.voter_user_id) AS votes,
                       MAX(CASE WHEN fv.voter_user_id = @user THEN 1 ELSE 0 END) AS voted_by_current_user
                FROM feedback_posts fp
                JOIN users u ON u.id = fp.author_user_id
                LEFT JOIN feedback_votes fv ON fv.post_id = fp.id
                WHERE fp.moderation_status = 'visible'
                GROUP BY fp.id, fp.author_user_id, u.display_name, fp.title, fp.category, fp.description, fp.created_at
                ORDER BY fp.created_at DESC
                """, cancellationToken, ("@user", current.User!.Id))
        });
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

    [HttpGet("analytics/consent")]
    public async Task<IActionResult> AnalyticsConsentStatus(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        var consented = Convert.ToInt32(await store.ScalarAsync("""
            SELECT COUNT(*)
            FROM research_participants rp
            JOIN research_consents rc ON rc.user_id = rp.user_id
            WHERE rp.user_id = @user
              AND rp.withdrawn_at IS NULL
              AND rc.consent_type = 'phase2_usage_analytics'
              AND rc.withdrawn_at IS NULL
            """, cancellationToken, ("@user", current.User!.Id))) > 0;
        return Ok(new { consented });
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
        if (!allowedEvents.Contains(eventType, StringComparer.Ordinal) || featureKey.Length > 64 || duration is < 0 or > 86400)
            return BadRequest(Error("invalid_analytics_event", "The analytics event is not allowed or contains invalid values."));
        var participant = await store.ScalarAsync("""
            SELECT rp.participant_key
            FROM research_participants rp
            WHERE rp.user_id = @user AND rp.withdrawn_at IS NULL
              AND EXISTS (
                  SELECT 1 FROM research_consents rc
                  WHERE rc.user_id = rp.user_id
                    AND rc.consent_type = 'phase2_usage_analytics'
                    AND rc.withdrawn_at IS NULL
              )
            LIMIT 1
            """, cancellationToken, ("@user", current.User!.Id));
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
        var consented = Boolean(body, "consented", true) ?? true;
        await store.SetAnalyticsConsentAsync(current.User!.Id, version, consented, cancellationToken);
        return Ok(new { consented, consentVersion = version });
    }

    [HttpGet("analytics/summary")]
    public async Task<IActionResult> AnalyticsSummary(CancellationToken cancellationToken)
    {
        var current = await CurrentUser(cancellationToken);
        if (current.Error is not null) return current.Error;
        return Ok(new
        {
            items = await store.QueryAsync("""
                SELECT ae.event_type, ae.feature_key, COUNT(*) AS events,
                       COALESCE(SUM(ae.duration_seconds), 0) AS duration_seconds
                FROM analytics_events ae
                JOIN research_participants rp
                  ON rp.participant_key = ae.participant_key
                 AND rp.user_id = @user
                 AND rp.withdrawn_at IS NULL
                WHERE EXISTS (
                    SELECT 1 FROM research_consents rc
                    WHERE rc.user_id = rp.user_id
                      AND rc.consent_type = 'phase2_usage_analytics'
                      AND rc.withdrawn_at IS NULL
                )
                GROUP BY ae.event_type, ae.feature_key
                ORDER BY events DESC
                """, cancellationToken, ("@user", current.User!.Id))
        });
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
    private static long? Long(JsonElement body, string name) => body.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;
    private static int? OptionalInt(IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) && value is not null
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    private static string? OptionalString(IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) ? Convert.ToString(value) : null;
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

    private async Task<IReadOnlyList<MatchingRecommendation>> GetMatchingRecommendationsAsync(
        PalsUser user,
        long? requestId,
        long? candidateUserId,
        CancellationToken cancellationToken)
    {
        var isTutor = user.Role == "tutor";
        if (!isTutor && user.Role is not ("student" or "tutee"))
            throw new InvalidOperationException("The account role cannot request matching recommendations.");

        var rows = await store.QueryAsync("""
            SELECT lr.id AS learning_request_id, lr.tutee_user_id, tutee.display_name AS tutee_display_name,
                   tutee_profile.grade_level AS tutee_grade, tutee_profile.strand AS tutee_strand,
                   lr.current_proficiency, lr.requested_help,
                   ts.tutor_user_id, tutor.display_name AS tutor_display_name,
                   tutor_profile.grade_level AS tutor_grade, tutor_profile.strand AS tutor_strand,
                   ts.proficiency_level AS tutor_proficiency, ts.verification_status AS tutor_verification_status,
                   s.name AS subject
            FROM learning_requests lr
            JOIN users tutee ON tutee.id = lr.tutee_user_id AND tutee.account_status = 'active'
            JOIN subjects s ON s.id = lr.subject_id AND s.is_active = TRUE
            JOIN tutor_subjects ts ON ts.subject_id = lr.subject_id AND ts.verification_status <> 'rejected'
            JOIN users tutor ON tutor.id = ts.tutor_user_id AND tutor.account_status = 'active'
            JOIN user_roles tutor_role ON tutor_role.user_id = tutor.id
            JOIN roles tutor_role_type ON tutor_role_type.id = tutor_role.role_id AND tutor_role_type.role_key = 'tutor'
            LEFT JOIN student_profiles tutee_profile ON tutee_profile.user_id = tutee.id
            LEFT JOIN student_profiles tutor_profile ON tutor_profile.user_id = tutor.id
            WHERE lr.request_status = 'open'
              AND lr.preferred_format IN ('one_to_one', 'either')
              AND ts.tutor_user_id <> lr.tutee_user_id
              AND (@is_tutor = TRUE AND ts.tutor_user_id = @user_id
                   OR @is_tutor = FALSE AND lr.tutee_user_id = @user_id)
              AND (@request_id IS NULL OR lr.id = @request_id)
              AND (@candidate_id IS NULL OR
                   (@is_tutor = TRUE AND lr.tutee_user_id = @candidate_id)
                   OR (@is_tutor = FALSE AND ts.tutor_user_id = @candidate_id))
              AND NOT EXISTS (
                  SELECT 1
                  FROM match_recommendations mr
                  JOIN session_match_recommendations smr ON smr.match_recommendation_id = mr.id
                  JOIN study_sessions ss ON ss.id = smr.study_session_id
                  WHERE mr.learning_request_id = lr.id
                    AND mr.tutor_user_id = ts.tutor_user_id
                    AND ss.session_status IN ('requested', 'confirmed')
              )
              AND NOT EXISTS (
                  SELECT 1
                  FROM match_recommendations declined
                  WHERE declined.learning_request_id = lr.id
                    AND declined.tutor_user_id = ts.tutor_user_id
                    AND declined.recommendation_status = 'declined'
              )
            ORDER BY lr.created_at DESC, tutor.display_name
            """, cancellationToken,
            ("@is_tutor", isTutor),
            ("@user_id", user.Id),
            ("@request_id", requestId),
            ("@candidate_id", candidateUserId));

        var algorithm = matchingAlgorithms.ActiveAlgorithm;
        var recommendations = new List<MatchingRecommendation>();
        foreach (var row in rows)
        {
            var pair = new MatchingPair(
                Convert.ToInt64(row["learning_request_id"], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToInt64(row["tutee_user_id"], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToString(row["tutee_display_name"]) ?? "",
                OptionalInt(row, "tutee_grade"),
                OptionalString(row, "tutee_strand"),
                OptionalInt(row, "current_proficiency"),
                OptionalString(row, "requested_help"),
                Convert.ToInt64(row["tutor_user_id"], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToString(row["tutor_display_name"]) ?? "",
                OptionalInt(row, "tutor_grade"),
                OptionalString(row, "tutor_strand"),
                Convert.ToInt32(row["tutor_proficiency"], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToString(row["tutor_verification_status"]) ?? "",
                Convert.ToString(row["subject"]) ?? "");

            var score = MatchingScorer.Score(pair, algorithm.Id);
            if (score is null) continue;

            recommendations.Add(new MatchingRecommendation(
                pair.LearningRequestId,
                isTutor ? pair.TuteeUserId : pair.TutorUserId,
                isTutor ? pair.TuteeDisplayName : pair.TutorDisplayName,
                isTutor ? "tutee" : "tutor",
                pair.Subject,
                pair.RequestedHelp,
                pair.CurrentProficiency,
                pair.TutorProficiency,
                isTutor ? pair.TuteeGrade : pair.TutorGrade,
                isTutor ? pair.TuteeStrand : pair.TutorStrand,
                pair.TutorVerificationStatus,
                Math.Round(score.Value, 2),
                score.Components,
                score.Explanation));
        }

        return recommendations
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.CandidateName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSchoolEmail(string? email) => email is not null && email.EndsWith("@cubao.sti.edu.ph", StringComparison.OrdinalIgnoreCase) && System.Text.RegularExpressions.Regex.IsMatch(email[..^"@cubao.sti.edu.ph".Length], @"^[a-z]+\.[0-9]{6,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static object AccountDto(PalsAccountSummary account) => new { id = account.Id, email = account.Email, displayName = account.DisplayName, accountStatus = account.Status, role = account.Roles.FirstOrDefault() ?? "student", roles = account.Roles };
}
