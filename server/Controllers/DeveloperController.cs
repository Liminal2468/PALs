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
            account = await identityStore.AuthenticateAsync(request.Email, request.Password, cancellationToken);
        }
        catch (MySqlConnector.MySqlException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = new { code = "database_identity_unavailable", message = "Developer identity storage is unavailable." } });
        }
        if (account is null) return Unauthorized(new { error = new { code = "invalid_credentials", message = "Developer email or password is incorrect." } });
        var user = await userManager.FindByEmailAsync(account.Email);
        if (user is null) return Unauthorized(new { error = new { code = "invalid_credentials", message = "Developer email or password is incorrect." } });
        await signInManager.SignInAsync(user, isPersistent: false);
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

    public sealed record LoginRequest(string? Email, string? Password);
}
