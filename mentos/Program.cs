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
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient("dbserv", client =>
{
    client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("DBSERV_URL") ?? "http://127.0.0.1:8100");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<HostDbservClient>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseWebendIsolation();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Run();
