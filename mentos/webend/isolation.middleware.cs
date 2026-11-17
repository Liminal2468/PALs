namespace Mentos.Host.Webend;

public sealed class IsolationMiddleware
{
    private static readonly string[] BlockedPathSegments =
    [
        "/controllers/", "/webend/", "/obj/", "/bin/", "/.git/"
    ];

    private static readonly string[] ProtectedPublicPages =
    [
        "/pals.html", "/pages/"
    ];

    private readonly RequestDelegate next;

    public IsolationMiddleware(RequestDelegate next) => this.next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (BlockedPathSegments.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (ProtectedPublicPages.Any(page => path.Equals(page, StringComparison.OrdinalIgnoreCase) ||
            (page.EndsWith('/') && path.StartsWith(page, StringComparison.OrdinalIgnoreCase))))
        {
            if (!context.Request.Cookies.ContainsKey("pals_host_session"))
            {
                context.Response.Redirect("/");
                return;
            }
        }

        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
        await next(context);
    }
}

public static class IsolationMiddlewareExtensions
{
    public static IApplicationBuilder UseWebendIsolation(this IApplicationBuilder app) =>
        app.UseMiddleware<IsolationMiddleware>();
}
