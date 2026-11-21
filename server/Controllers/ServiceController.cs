using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace Palsys.Server.Controllers;

[ApiController]
[Route("{service}")]
public sealed class ServiceController : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> Services =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["db"] = "database",
            ["acc"] = "accounts",
            ["sched"] = "scheduler",
            ["fb"] = "feedback",
            ["adata"] = "analyst",
            ["connect"] = "connect",
            ["chat"] = "chat",
        };

    [HttpGet("health")]
    public IActionResult Health(string service) => Services.TryGetValue(service, out var name)
        ? Ok(new { service = name, status = "ready" })
        : NotFound(new { error = new { code = "not_found", message = "API endpoint not found." } });
}

[ApiController]
[Route("/verify")]
public sealed class VerificationController : ControllerBase
{
    private readonly string hostKey;

    public VerificationController(IConfiguration configuration) => hostKey = configuration["HOST_SHARED_KEY"] ?? "";

    [HttpGet]
    public IActionResult Verify([FromQuery] string? code)
    {
        if (string.IsNullOrWhiteSpace(hostKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = new { code = "verification_not_configured", message = "Verification is not configured." } });

        var suppliedKey = Request.Headers["X-Pals-Host-Key"].FirstOrDefault() ?? "";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hostKey), Encoding.UTF8.GetBytes(suppliedKey)))
            return Forbid();

        var normalized = code?.Trim();
        if (normalized is null || normalized.Length != 6 || !normalized.All(char.IsDigit))
            return BadRequest(new { error = new { code = "invalid_verification_code", message = "Verification code must contain exactly six digits." } });

        // TODO: Replace format-only acceptance with the approved global TOTP generator
        // and verification-email transaction after bindoc/testdoc verification testing.
        return Ok(new { verified = true, verification = "temporary_format_only" });
    }
}
