using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Mentos.Host.Webend;

public sealed class IsolationMiddleware
{
    private static readonly string[] BlockedPathSegments =
    [
        "/controllers/", "/webend/", "/configuration/", "/data/", "/identity/",
        "/obj/", "/bin/", "/.git/", "/.history/", "/storage/"
    ];

    private static readonly string[] BlockedFileSuffixes =
    [
        ".cs", ".csproj", ".sln", ".env", ".yaml", ".yml", ".pem", ".key", ".p12", ".pdb"
    ];

    private readonly RequestDelegate next;

    public IsolationMiddleware(RequestDelegate next) => this.next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Contains("..", StringComparison.Ordinal)
            || BlockedPathSegments.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase))
            || BlockedFileSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            || path.Equals("/pages/readme.md", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/z/readme.md", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api/v1")
            && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method))
        {
            if (!HasSameOrigin(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new { code = "invalid_origin", message = "This request must come from the PALs site." },
                });
                return;
            }

            var hasBody = context.Request.ContentLength is > 0
                || context.Request.Headers.ContainsKey("Transfer-Encoding");
            if (hasBody && !IsJsonRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new { code = "json_required", message = "This endpoint accepts JSON requests only." },
                });
                return;
            }
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        context.Items["CspNonce"] = nonce;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] =
            $"default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self' 'nonce-{nonce}'; script-src 'self' 'nonce-{nonce}'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
        if (path.Equals("/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/faqs", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/contactsupport", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/pals", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/pals/partial/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Pragma"] = "no-cache";
        }
        if (context.Request.Path.StartsWithSegments("/api/v1"))
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Pragma"] = "no-cache";
        }
        await next(context);
    }

    private static bool IsJsonRequest(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType))
            return false;
        var value = mediaType.MediaType;
        return string.Equals(value, "application/json", StringComparison.OrdinalIgnoreCase)
            || value?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool HasSameOrigin(HttpContext context)
    {
        var originHeader = context.Request.Headers.Origin;
        if (originHeader.Count != 1
            || !Uri.TryCreate(originHeader[0], UriKind.Absolute, out var origin))
            return false;
        return (origin.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrEmpty(origin.UserInfo)
            && origin.AbsolutePath == "/"
            && string.IsNullOrEmpty(origin.Query)
            && string.IsNullOrEmpty(origin.Fragment)
            && string.Equals(origin.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

public static class IsolationMiddlewareExtensions
{
    public static IApplicationBuilder UseWebendIsolation(this IApplicationBuilder app) =>
        app.UseMiddleware<IsolationMiddleware>();
}
