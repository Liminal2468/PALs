using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Mentos.Host.Webend;

public sealed class HostDbservClient
{
    private readonly IHttpClientFactory clients;
    private readonly string sharedKey;

    public HostDbservClient(IHttpClientFactory clients, IConfiguration configuration)
    {
        this.clients = clients;
        sharedKey = configuration["HOST_SHARED_KEY"] ?? "";
    }

    public async Task<DbservResponse> SendAsync(HttpMethod method, string path, string email, object? body, CancellationToken cancellationToken, string? sessionKey = null)
    {
        if (string.IsNullOrWhiteSpace(sharedKey))
            return new DbservResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":\"dbserv_not_configured\",\"message\":\"The HOST-to-DBSERV shared key is not configured.\"}}", "application/json");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Pals-Host-Key", sharedKey);
        if (!string.IsNullOrWhiteSpace(email)) request.Headers.Add("X-Pals-User-Email", email);
        if (!string.IsNullOrWhiteSpace(sessionKey)) request.Headers.Add("X-Pals-Session", sessionKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await clients.CreateClient("dbserv").SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return new DbservResponse(response.StatusCode, content, response.Content.Headers.ContentType?.ToString() ?? "application/json");
        }
        catch (HttpRequestException)
        {
            return new DbservResponse(HttpStatusCode.BadGateway, "{\"error\":{\"code\":\"dbserv_unreachable\",\"message\":\"The database service could not be reached.\"}}", "application/json");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DbservResponse(HttpStatusCode.GatewayTimeout, "{\"error\":{\"code\":\"dbserv_timeout\",\"message\":\"The database service did not respond in time.\"}}", "application/json");
        }
    }

    public static IActionResult ToActionResult(ControllerBase controller, DbservResponse response) =>
        new ContentResult { StatusCode = (int)response.StatusCode, ContentType = response.ContentType, Content = response.Content };
}

public sealed record DbservResponse(HttpStatusCode StatusCode, string Content, string ContentType);
