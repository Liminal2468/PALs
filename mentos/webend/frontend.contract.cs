namespace Mentos.Host.Webend;

public sealed record WebendError(string Code, string Message);

public sealed record WebendErrorResponse(WebendError Error);

public sealed record WebendStatus(string Status, string Service, bool Configured);
