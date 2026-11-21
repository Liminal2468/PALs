using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mentos.Host.Webend;

namespace Mentos.Host.Controllers;

public sealed class SiteController : Controller
{
    private static readonly IReadOnlyDictionary<string, string> PalsPartials =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = "/Views/Pals/Partials/home.cshtml",
            ["tutors"] = "/Views/Pals/Partials/tutors.cshtml",
            ["tutees"] = "/Views/Pals/Partials/tutees.cshtml",
            ["sessions"] = "/Views/Pals/Partials/sessions.cshtml",
            ["resources"] = "/Views/Pals/Partials/resources.cshtml",
            ["analytics"] = "/Views/Pals/Partials/analytics.cshtml",
            ["settings"] = "/Views/Pals/Partials/settings.cshtml",
            ["help"] = "/Views/Pals/Partials/help.cshtml",
            ["feedback"] = "/Views/Pals/Partials/feedback.cshtml",
            ["user"] = "/Views/Pals/Partials/user.cshtml",
        };

    [HttpGet("/")]
    public IActionResult Index()
    {
        if (HttpContext.Items.ContainsKey(PalsSessionAuthenticationHandler.ValidationUnavailableItem))
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (User.Identity?.IsAuthenticated == true && HasPalsRole())
            return Redirect("/pals");
        return View();
    }

    [HttpGet("/faqs")]
    public IActionResult Faqs() => View();

    [HttpGet("/contactsupport")]
    public IActionResult ContactSupport() => View();

    [Authorize(Policy = "PalsUser")]
    [HttpGet("/pals")]
    public IActionResult Pals() => View("/Views/Pals/Index.cshtml");

    [Authorize(Policy = "PalsUser")]
    [HttpGet("/pals/partial/{name}")]
    public IActionResult PalsPartial(string name)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (!PalsPartials.TryGetValue(name, out var viewPath))
        {
            return NotFound();
        }
        return PartialView(viewPath);
    }

    public IActionResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }

    private bool HasPalsRole() =>
        User.IsInRole("student")
        || User.IsInRole("tutor")
        || User.IsInRole("tutee")
        || User.IsInRole("admin")
        || User.IsInRole("developer");
}
