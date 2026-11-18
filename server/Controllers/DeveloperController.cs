using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Palsys.Server.Identity;

namespace Palsys.Server.Controllers;

[ApiController]
[Route("dev")]
public sealed class DeveloperController : ControllerBase
{
    private readonly DeveloperIdentityStore identityStore;
    private readonly SignInManager<PalIdentityUser> signInManager;
    private readonly UserManager<PalIdentityUser> userManager;

    public DeveloperController(
        DeveloperIdentityStore identityStore,
        SignInManager<PalIdentityUser> signInManager,
        UserManager<PalIdentityUser> userManager)
    {
        this.identityStore = identityStore;
        this.signInManager = signInManager;
        this.userManager = userManager;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return Unauthorized(new { error = new { code = "invalid_credentials", message = "Developer email or password is incorrect." } });
        DeveloperCredential? account;
        try
        {
            account = await identityStore.FindEligibleAsync(request.Email, cancellationToken);
        }
        catch (MySqlConnector.MySqlException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = new { code = "database_identity_unavailable", message = "Developer identity storage is unavailable." } });
        }
        if (account is null) return Unauthorized(new { error = new { code = "invalid_credentials", message = "Developer email or password is incorrect." } });

        if (identityStore.RequiresMfa(account) && !account.TwoFactorEnabled)
            return StatusCode(StatusCodes.Status428PreconditionRequired, new { error = new { code = "mfa_setup_required", message = "Set up an authenticator app before signing in to this privileged account." } });

        var result = await signInManager.PasswordSignInAsync(account.User, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
            return Ok(new { data = new { status = "mfa_required", email = account.Email } });
        if (result.IsLockedOut)
            return Unauthorized(new { error = new { code = "invalid_credentials", message = "Developer email or password is incorrect." } });
        if (!result.Succeeded)
            return Unauthorized(new { error = new { code = "invalid_credentials", message = "Developer email or password is incorrect." } });
        return Ok(new { data = new { status = "authenticated", email = account.Email, role = account.Role } });
    }

    [HttpPost("mfa/verify")]
    public async Task<IActionResult> VerifyMfa([FromBody] MfaCodeRequest? request, CancellationToken cancellationToken)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null || request is null)
            return Unauthorized(new { error = new { code = "mfa_session_expired", message = "Start developer sign-in again." } });

        DeveloperCredential? account;
        try
        {
            account = await identityStore.FindEligibleAsync(user.Email ?? "", cancellationToken);
            if (account is null || !await identityStore.VerifyAuthenticatorCodeAsync(user, request.Code, cancellationToken))
                return Unauthorized(new { error = new { code = "invalid_mfa_code", message = "The authenticator code is incorrect or has expired." } });
        }
        catch (MySqlConnector.MySqlException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = new { code = "database_identity_unavailable", message = "Developer identity storage is unavailable." } });
        }

        var result = await signInManager.TwoFactorSignInAsync(
            TokenOptions.DefaultAuthenticatorProvider,
            DeveloperIdentityStore.NormalizeCode(request.Code)!,
            isPersistent: false,
            rememberClient: false);
        if (!result.Succeeded)
            return Unauthorized(new { error = new { code = "invalid_mfa_code", message = "The authenticator code is incorrect or has expired." } });

        return Ok(new { data = new { status = "authenticated", email = account.Email, role = account.Role } });
    }

    [HttpGet("session")]
    public IActionResult Session()
    {
        return CurrentIdentity(out var email, out var role)
            ? Ok(new { data = new { status = "authenticated", email, role } })
            : Unauthorized(new { error = new { code = "authentication_required", message = "Developer sign-in is required." } });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return Ok(new { data = new { status = "signed_out" } });
    }

    [HttpGet("profile")]
    public IActionResult Profile()
    {
        if (!CurrentIdentity(out var email, out var role)) return Unauthorized(new { error = new { code = "authentication_required", message = "Developer sign-in is required." } });
        return Ok(new { data = new { email, display_name = email, role, environment = "local" } });
    }

    [HttpGet("mfa")]
    public async Task<IActionResult> MfaStatus()
    {
        if (!CurrentIdentity(out var email, out var role)) return AuthenticationRequired();
        var user = await userManager.FindByEmailAsync(email);
        return user is null
            ? AuthenticationRequired()
            : Ok(new { data = new { email, role, enabled = user.TwoFactorEnabled, provider = "totp" } });
    }

    [HttpPost("mfa/setup")]
    public async Task<IActionResult> StartMfaSetup()
    {
        if (!CurrentIdentity(out var email, out _)) return AuthenticationRequired();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return AuthenticationRequired();

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            var reset = await userManager.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded)
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = new { code = "mfa_setup_failed", message = "The authenticator setup could not be initialized." } });
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrWhiteSpace(key))
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = new { code = "mfa_setup_failed", message = "The authenticator setup could not be initialized." } });

        return Ok(new
        {
            data = new
            {
                provider = "totp",
                issuer = "PALs",
                account = email,
                manualKey = key.Replace(" ", "", StringComparison.Ordinal),
                otpauthUri = BuildAuthenticatorUri(email, key),
                enabled = user.TwoFactorEnabled,
            },
        });
    }

    [HttpPost("mfa/confirm")]
    public async Task<IActionResult> ConfirmMfa([FromBody] MfaCodeRequest? request, CancellationToken cancellationToken)
    {
        if (!CurrentIdentity(out var email, out _)) return AuthenticationRequired();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return AuthenticationRequired();
        if (!await identityStore.VerifyAuthenticatorCodeAsync(user, request?.Code, cancellationToken, requireEnabled: false))
            return Unauthorized(new { error = new { code = "invalid_mfa_code", message = "The authenticator code is incorrect or has expired." } });

        var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = new { code = "mfa_setup_failed", message = "The authenticator could not be enabled." } });

        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Ok(new { data = new { enabled = true, recoveryCodes } });
    }

    [HttpPost("mfa/disable")]
    public async Task<IActionResult> DisableMfa([FromBody] MfaCodeRequest? request, CancellationToken cancellationToken)
    {
        if (!CurrentIdentity(out var email, out _)) return AuthenticationRequired();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return AuthenticationRequired();
        if (!await identityStore.VerifyAuthenticatorCodeAsync(user, request?.Code, cancellationToken))
            return Unauthorized(new { error = new { code = "invalid_mfa_code", message = "The authenticator code is incorrect or has expired." } });

        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disabled.Succeeded)
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = new { code = "mfa_update_failed", message = "The authenticator could not be disabled." } });
        await userManager.ResetAuthenticatorKeyAsync(user);
        return Ok(new { data = new { enabled = false } });
    }

    [HttpGet("system/matching-algorithms")]
    public IActionResult MatchingAlgorithms()
    {
        if (!CurrentIdentity(out _, out _)) return Unauthorized(new { error = new { code = "authentication_required", message = "Developer sign-in is required." } });
        return Ok(new { data = Array.Empty<object>() });
    }

    private bool CurrentIdentity(out string email, out string role)
    {
        email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;
        role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return User.Identity?.IsAuthenticated == true && email.Length > 0 && role.Length > 0;
    }

    private IActionResult AuthenticationRequired() =>
        Unauthorized(new { error = new { code = "authentication_required", message = "Developer sign-in is required." } });

    private static string BuildAuthenticatorUri(string email, string key)
    {
        var normalizedKey = key.Replace(" ", "", StringComparison.Ordinal);
        var label = Uri.EscapeDataString($"PALs:{email}");
        var issuer = Uri.EscapeDataString("PALs");
        return $"otpauth://totp/{label}?secret={Uri.EscapeDataString(normalizedKey)}&issuer={issuer}&digits=6&period=30";
    }

    public sealed record LoginRequest(string? Email, string? Password);
    public sealed record MfaCodeRequest(string? Code);
}
