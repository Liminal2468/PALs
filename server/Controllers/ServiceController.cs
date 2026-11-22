using Microsoft.AspNetCore.Mvc;

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
