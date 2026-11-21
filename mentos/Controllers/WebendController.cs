using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mentos.Host.Webend;

namespace Mentos.Host.Controllers;

[Authorize]
public abstract class WebendController : ControllerBase
{
    private const string SessionCookie = PalsSessionAuthenticationHandler.SessionCookie;
    private const string PendingCookie = "pals_host_pending";
    private const string SchoolDomain = "@cubao.sti.edu.ph";
    protected readonly HostDbservClient dbserv;
    private readonly IWebHostEnvironment environment;

    protected WebendController(HostDbservClient dbserv, IWebHostEnvironment environment)
    {
        this.dbserv = dbserv;
        this.environment = environment;
    }

    [AllowAnonymous]
    [HttpGet("auth/options")]
    public IActionResult AuthenticationOptions() => Ok(new { providers = new[] { "local-school-account" }, schoolDomain = SchoolDomain, termsRequired = true });

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("auth/login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        Response.Cookies.Delete(PendingCookie, HostCookieOptions.Create(Request, environment));
        var email = request.Email?.Trim().ToLowerInvariant();
        if (!request.TermsAccepted) return BadRequest(Error("terms_required", "Accept the PALs terms before signing in."));
        if (!IsSchoolEmail(email)) return Unauthorized(Error("account_not_recognized", "Use a recognized STI College Cubao school account."));
        var response = await dbserv.SendAsync(HttpMethod.Post, "/internal/v1/auth/login", email!, new { email, termsAccepted = true }, cancellationToken);
        return SetPendingFromDbserv(response);
    }

    [HttpGet("session")]
    public Task<IActionResult> Session(CancellationToken cancellationToken) => ProxyAuthSession(cancellationToken);

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("auth/admin/login")]
    public async Task<IActionResult> AdminLogin([FromBody] AdminLoginRequest request, CancellationToken cancellationToken)
    {
        var response = await dbserv.SendAsync(HttpMethod.Post, "/internal/v1/auth/admin/login", request.Email ?? "", request, cancellationToken);
        return SetSessionFromDbserv(response);
    }

    [HttpDelete("session")]
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(SessionCookie, out var token))
            await dbserv.SendAsync(HttpMethod.Delete, "/internal/v1/auth/session", "", null, cancellationToken, token);
        Response.Cookies.Delete(SessionCookie, HostCookieOptions.Create(Request, environment));
        Response.Cookies.Delete(PendingCookie, HostCookieOptions.Create(Request, environment));
        return Ok(new { authenticated = false });
    }

    [HttpGet("me")]
    public Task<IActionResult> Me(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/me", null, cancellationToken);

    [HttpPatch("me")]
    public Task<IActionResult> UpdateMe([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Patch, "/internal/v1/me", body, cancellationToken);

    [HttpGet("users")]
    public Task<IActionResult> Users(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/users", null, cancellationToken);

    [HttpGet("pages")]
    public Task<IActionResult> Pages(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/pages", null, cancellationToken);

    [HttpPatch("pages/{id:long}")]
    public Task<IActionResult> UpdatePage(long id, [FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Patch, $"/internal/v1/pages/{id}", body, cancellationToken);

    [HttpGet("tutors")]
    public Task<IActionResult> Tutors(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/tutors", null, cancellationToken);

    [HttpGet("tutees")]
    public Task<IActionResult> Tutees(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/tutees", null, cancellationToken);

    [HttpGet("learning-requests")]
    public Task<IActionResult> LearningRequests(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/learning-requests", null, cancellationToken);

    [HttpPost("learning-requests")]
    public Task<IActionResult> CreateLearningRequest([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, "/internal/v1/learning-requests", body, cancellationToken);

    [HttpGet("sessions")]
    public Task<IActionResult> Sessions(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/sessions", null, cancellationToken);

    [HttpPost("sessions")]
    public Task<IActionResult> CreateSession([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, "/internal/v1/sessions", body, cancellationToken);

    [HttpPatch("sessions/{id:long}")]
    public Task<IActionResult> UpdateSession(long id, [FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Patch, $"/internal/v1/sessions/{id}", body, cancellationToken);

    [HttpGet("dashboard")]
    public Task<IActionResult> Dashboard(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/dashboard", null, cancellationToken);

    [HttpGet("schedule")]
    public Task<IActionResult> Schedule([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to) ? "" : $"?from={Uri.EscapeDataString(from ?? "")}&to={Uri.EscapeDataString(to ?? "")}";
        return ProxyForSession(HttpMethod.Get, $"/internal/v1/schedule{query}", null, cancellationToken);
    }

    [HttpPost("schedule")]
    public Task<IActionResult> CreateSchedule([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, "/internal/v1/schedule", body, cancellationToken);

    [HttpPatch("schedule/{id:long}")]
    public Task<IActionResult> UpdateSchedule(long id, [FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Patch, $"/internal/v1/schedule/{id}", body, cancellationToken);

    [HttpDelete("schedule/{id:long}")]
    public Task<IActionResult> DeleteSchedule(long id, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Delete, $"/internal/v1/schedule/{id}", null, cancellationToken);

    [HttpGet("resources")]
    public Task<IActionResult> Resources(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/resources", null, cancellationToken);

    [HttpPost("resources")]
    public Task<IActionResult> CreateResource([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, "/internal/v1/resources", body, cancellationToken);

    [HttpPatch("resources/{id:long}")]
    public Task<IActionResult> UpdateResource(long id, [FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Patch, $"/internal/v1/resources/{id}", body, cancellationToken);

    [HttpDelete("resources/{id:long}")]
    public Task<IActionResult> DeleteResource(long id, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Delete, $"/internal/v1/resources/{id}", null, cancellationToken);

    [HttpPatch("resources/{id:long}/moderation")]
    public Task<IActionResult> ModerateResource(long id, [FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Patch, $"/internal/v1/resources/{id}/moderation", body, cancellationToken);

    [HttpGet("feedback")]
    public Task<IActionResult> Feedback(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/feedback", null, cancellationToken);

    [HttpPost("feedback")]
    public Task<IActionResult> CreateFeedback([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, "/internal/v1/feedback", body, cancellationToken);

    [HttpPost("feedback/{id:long}/votes")]
    public Task<IActionResult> Vote(long id, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, $"/internal/v1/feedback/{id}/votes", new { }, cancellationToken);

    [HttpPost("support")]
    public Task<IActionResult> Support([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, "/internal/v1/support", body, cancellationToken);

    [HttpPost("analytics/events")]
    public Task<IActionResult> AnalyticsEvent([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, "/internal/v1/analytics/events", body, cancellationToken);

    [HttpPost("analytics/consent")]
    public Task<IActionResult> AnalyticsConsent([FromBody] object body, CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Post, "/internal/v1/analytics/consent", body, cancellationToken);

    [HttpGet("analytics/summary")]
    public Task<IActionResult> AnalyticsSummary(CancellationToken cancellationToken) => ProxyForSession(HttpMethod.Get, "/internal/v1/analytics/summary", null, cancellationToken);

    [HttpGet("rec-peer-tutor")]
    [HttpGet("recommended-peer-tutors")]
    [HttpGet("tutors/recommended")]
    public IActionResult RecommendedPeerTutors() => StatusCode(StatusCodes.Status501NotImplemented, Error("connect_not_implemented", "Tutor recommendations remain pending until the /connect algorithm is approved."));

    [HttpGet("frontend/status")]
    public IActionResult Status() => Ok(new { status = "ready", service = "mentos-webend", configured = true });

    private async Task<IActionResult> ProxyForSession(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        if (!TrySessionKey(out var sessionKey)) return Unauthorized(Error("authentication_required", "Sign in before requesting this resource."));
        var response = await dbserv.SendAsync(method, path, "", body, cancellationToken, sessionKey);
        return DbservResponse(response);
    }

    private async Task<IActionResult> ProxyAuthSession(CancellationToken cancellationToken)
    {
        if (!TrySessionKey(out var sessionKey)) return Unauthorized(Error("authentication_required", "Sign in before opening PALs."));
        var response = await dbserv.SendAsync(HttpMethod.Get, "/internal/v1/auth/session", "", null, cancellationToken, sessionKey);
        return DbservResponse(response);
    }

    protected IActionResult SetSessionFromDbserv(DbservResponse response, bool clearPending = false)
    {
        if ((int)response.StatusCode >= 400) return DbservResponse(response);
        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (!document.RootElement.TryGetProperty("sessionKey", out var sessionKey)
                || sessionKey.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(sessionKey.GetString())
                || !document.RootElement.TryGetProperty("account", out var account))
                return StatusCode(StatusCodes.Status502BadGateway, Error("invalid_auth_response", "The authentication service returned an invalid session."));

            Response.Cookies.Append(SessionCookie, sessionKey.GetString()!, HostCookieOptions.Create(Request, environment, TimeSpan.FromHours(8)));
            if (clearPending)
                Response.Cookies.Delete(PendingCookie, HostCookieOptions.Create(Request, environment));
            return Ok(new { authenticated = true, account = account.Clone() });
        }
        catch (JsonException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, Error("invalid_auth_response", "The authentication service returned an invalid session."));
        }
    }

    private IActionResult SetPendingFromDbserv(DbservResponse response)
    {
        if ((int)response.StatusCode >= 400) return DbservResponse(response);
        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (!document.RootElement.TryGetProperty("attemptId", out var attemptId)
                || attemptId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(attemptId.GetString())
                || !document.RootElement.TryGetProperty("expiresInSeconds", out var expires)
                || !expires.TryGetInt32(out var expiresInSeconds)
                || expiresInSeconds is <= 0 or > 600)
                return StatusCode(StatusCodes.Status502BadGateway, Error("invalid_auth_response", "The authentication service returned an invalid verification attempt."));

            Response.Cookies.Append(
                PendingCookie,
                attemptId.GetString()!,
                HostCookieOptions.Create(Request, environment, TimeSpan.FromSeconds(expiresInSeconds)));
            return Ok(new { verificationRequired = true, expiresInSeconds });
        }
        catch (JsonException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, Error("invalid_auth_response", "The authentication service returned an invalid verification attempt."));
        }
    }

    protected string? PendingAttemptId => Request.Cookies[PendingCookie];
    protected void ClearPendingAttempt() =>
        Response.Cookies.Delete(PendingCookie, HostCookieOptions.Create(Request, environment));

    protected bool IsPendingAttemptExpired(DbservResponse response) =>
        (int)response.StatusCode == StatusCodes.Status410Gone;

    protected static IActionResult DbservResponse(DbservResponse response) => new ContentResult { StatusCode = (int)response.StatusCode, ContentType = response.ContentType, Content = response.Content };
    private bool TrySessionKey(out string sessionKey) { sessionKey = Request.Cookies[SessionCookie] ?? ""; return sessionKey.Length > 0; }
    private static bool IsSchoolEmail(string? email) => email is not null && email.EndsWith(SchoolDomain, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(email[..^SchoolDomain.Length], @"^[a-z]+\.[0-9]{6,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static object Error(string code, string message) => new { error = new { code, message } };
    public sealed record LoginRequest(string? Email, bool TermsAccepted);
    public sealed record AdminLoginRequest(string? Email, string? Password, string? MfaCode);
}
