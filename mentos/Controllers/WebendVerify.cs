using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mentos.Host.Webend;

namespace Mentos.Host.Controllers;

[Controller]
[ApiController]
[Route("api/v1")]
public sealed class WebendVerify : WebendController
{
    public WebendVerify(HostDbservClient dbserv, IWebHostEnvironment environment) : base(dbserv, environment) { }

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Code?.Trim();
        if (normalized is null || normalized.Length != 6 || !normalized.All(char.IsAsciiDigit))
            return BadRequest(new { error = new { code = "invalid_verification_code", message = "Verification code must contain exactly six digits." } });
        var attemptId = PendingAttemptId;
        if (string.IsNullOrWhiteSpace(attemptId))
            return Unauthorized(new { error = new { code = "verification_attempt_missing", message = "Start a new sign-in before entering a verification code." } });

        var response = await dbserv.SendAsync(
            HttpMethod.Post,
            "/internal/v1/auth/verify",
            "",
            new { attemptId, code = normalized },
            cancellationToken);
        if ((int)response.StatusCode >= 400)
        {
            if (IsPendingAttemptExpired(response))
                ClearPendingAttempt();
            return DbservResponse(response);
        }
        return SetSessionFromDbserv(response, clearPending: true);
    }

    public sealed record VerifyRequest(string? Code);
}
