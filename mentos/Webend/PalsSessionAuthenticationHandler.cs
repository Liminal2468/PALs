using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Mentos.Host.Webend;

namespace Mentos.Host.Webend;

public sealed class PalsSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    HostDbservClient dbserv,
    IWebHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PalsSession";
    public const string SessionCookie = "pals_host_session";
    public const string ValidationUnavailableItem = "PalsSessionValidationUnavailable";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!IsSessionProtectedPath(Request.Path))
            return AuthenticateResult.NoResult();

        if (!Request.Cookies.TryGetValue(SessionCookie, out var sessionKey) || string.IsNullOrWhiteSpace(sessionKey))
            return AuthenticateResult.NoResult();

        var response = await dbserv.SendAsync(
            HttpMethod.Get,
            "/internal/v1/auth/session",
            "",
            null,
            Context.RequestAborted,
            sessionKey);

        if ((int)response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            Response.Cookies.Delete(SessionCookie, HostCookieOptions.Create(Request, environment));
            return AuthenticateResult.NoResult();
        }

        if ((int)response.StatusCode is < 200 or >= 300)
        {
            Context.Items[ValidationUnavailableItem] = true;
            return AuthenticateResult.Fail("The session service could not validate the current session.");
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (!document.RootElement.TryGetProperty("account", out var account)
                || !account.TryGetProperty("email", out var emailElement)
                || !account.TryGetProperty("role", out var roleElement)
                || emailElement.ValueKind != JsonValueKind.String
                || roleElement.ValueKind != JsonValueKind.String)
            {
                Context.Items[ValidationUnavailableItem] = true;
                return AuthenticateResult.Fail("The session service returned an invalid account.");
            }

            var email = emailElement.GetString();
            var role = roleElement.GetString();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(role))
            {
                Context.Items[ValidationUnavailableItem] = true;
                return AuthenticateResult.Fail("The session service returned an invalid account.");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, email),
                new Claim(ClaimTypes.Name, email),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role),
            };
            var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
        catch (JsonException)
        {
            Context.Items[ValidationUnavailableItem] = true;
            return AuthenticateResult.Fail("The session service returned an invalid response.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (Context.Items.ContainsKey(ValidationUnavailableItem))
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Response.WriteAsJsonAsync(new
            {
                error = new { code = "session_validation_unavailable", message = "The session service is temporarily unavailable." },
            });
        }

        if (Request.Path.StartsWithSegments("/api/v1")
            || Request.Path.StartsWithSegments("/pals/partial"))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Response.WriteAsJsonAsync(new
            {
                error = new { code = "authentication_required", message = "Sign in before requesting this resource." },
            });
        }

        Response.Redirect("/");
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.Headers.CacheControl = "no-store";
        return Response.WriteAsJsonAsync(new
        {
            error = new { code = "forbidden", message = "Your account cannot access this resource." },
        });
    }

    private static bool IsSessionProtectedPath(PathString path) =>
        path == "/"
        || path.StartsWithSegments("/pals")
        || path.StartsWithSegments("/api/v1");
}
