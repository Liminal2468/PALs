using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Palsys.Server.Configuration;
using Palsys.Server.Data;
using Palsys.Server.Identity;

var processEnvironmentKeys = Environment.GetEnvironmentVariables()
    .Keys
    .Cast<object>()
    .Select(key => key.ToString())
    .Where(key => !string.IsNullOrWhiteSpace(key)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
    .Cast<string>()
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
LoadDotEnv(YamlEnvironment.ResolvePath(".env"), processEnvironmentKeys);
YamlEnvironment.Load("config.yaml", processEnvironmentKeys);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "public"
});

builder.WebHost.UseUrls(
    $"http://{Environment.GetEnvironmentVariable("BIND_ADDRESS") ?? "127.0.0.1"}:{Environment.GetEnvironmentVariable("PORT") ?? "8432"}");
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
var dataProtectionPath = Environment.GetEnvironmentVariable("DATA_PROTECTION_KEY_PATH") ?? Path.Combine("storage", "dataprotection");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("Palsys.Server");
builder.Services.AddDbContext<PalIdentityDbContext>(options =>
    options.UseMySQL(IdentityConnection.Build(builder.Configuration)));
builder.Services.AddIdentity<PalIdentityUser, PalIdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddEntityFrameworkStores<PalIdentityDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "palsys_identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/";
});
builder.Services.AddScoped<DeveloperIdentityStore>();
builder.Services.AddScoped<MfaReplayStore>();
builder.Services.AddScoped<PalsDataStore>();

var app = builder.Build();

if (args.Length == 3 && args[0].Equals("--seed-developer", StringComparison.Ordinal))
{
    Console.Write("Developer password: ");
    var password = ReadPassword();
    if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("A non-empty password is required.");
    await using var seedScope = app.Services.CreateAsyncScope();
    await DeveloperIdentitySeed.RunAsync(args[1], password, args[2], seedScope.ServiceProvider, builder.Configuration);
    return;
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var blockedPaths = new[] { "/controllers/", "/.git/", "/obj/", "/bin/" };
    if (blockedPaths.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var developerPage = path.Equals("/pals.html", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/pages/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/components/telemetry/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/scripts/pals.js", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/scripts/live.js", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/styles/pals.css", StringComparison.OrdinalIgnoreCase);
    if (developerPage && !(context.User.Identity?.IsAuthenticated ?? false))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (path.StartsWith("/dev", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/oauth", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";
    }
    if (path.Equals("/pals.html", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/pages/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/scripts/pals.js", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/scripts/live.js", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers["Cache-Control"] = "no-store";
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Run();

static void LoadDotEnv(string? path, ISet<string> processEnvironmentKeys)
{
    if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;
    foreach (var line in System.IO.File.ReadLines(path))
    {
        var value = line.Trim();
        if (value.Length == 0 || value.StartsWith('#')) continue;
        var separator = value.IndexOf('=');
        if (separator <= 0) continue;
        var key = value[..separator].Trim();
        var setting = value[(separator + 1)..].Trim().Trim('"', '\'');
        // Keep one non-secret configuration source: server/config.yaml.
        // The database password is the only value allowed in server/.env.
        if (string.IsNullOrWhiteSpace(key)
            || string.IsNullOrWhiteSpace(setting)
            || !key.Equals("DB_PASSWORD", StringComparison.OrdinalIgnoreCase)
            || processEnvironmentKeys.Contains(key)) continue;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            Environment.SetEnvironmentVariable(key, setting);
    }
}

static string ReadPassword()
{
    var characters = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return characters.ToString();
        }
        if (key.Key == ConsoleKey.Backspace)
        {
            if (characters.Length > 0) characters.Length--;
            continue;
        }
        if (!char.IsControl(key.KeyChar)) characters.Append(key.KeyChar);
    }
}
