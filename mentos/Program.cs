using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Mentos.Host.Webend;
using Mentos.Host.Configuration;

YamlEnvironment.Load("config.yaml");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "public"
});

builder.WebHost.UseUrls(
    $"http://{Environment.GetEnvironmentVariable("BIND_ADDRESS") ?? "127.0.0.1"}:{Environment.GetEnvironmentVariable("PORT") ?? "8000"}");
builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services
    .AddAuthentication(PalsSessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, PalsSessionAuthenticationHandler>(
        PalsSessionAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PalsUser", policy =>
        policy.RequireAuthenticatedUser().RequireRole("student", "tutor", "tutee", "admin", "developer"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});
builder.Services.AddHttpClient("dbserv", client =>
{
    client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("DBSERV_URL") ?? "http://127.0.0.1:8100");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<HostDbservClient>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseRouting();
app.UseWebendIsolation();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToController("NotFoundPage", "Site");

app.Run();
