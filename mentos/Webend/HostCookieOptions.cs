namespace Mentos.Host.Webend;

public static class HostCookieOptions
{
    public static CookieOptions Create(
        HttpRequest request,
        IWebHostEnvironment environment,
        TimeSpan? maxAge = null) =>
        new()
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = !environment.IsDevelopment() || request.IsHttps,
            IsEssential = true,
            MaxAge = maxAge,
            Path = "/",
        };
}
