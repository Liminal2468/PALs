using Microsoft.AspNetCore.Mvc;

namespace Mentos.Host.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class HostController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ready" });
}
