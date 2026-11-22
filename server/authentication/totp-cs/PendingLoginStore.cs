using System.Security.Cryptography;

namespace TotpPrototype;

public sealed class PendingLoginStore(TotpConfig config)
{
    private sealed class PendingLogin(
        string attemptId,
        string userId,
        double expiresAt)
    {
        public string AttemptId { get; } = attemptId;
        public string UserId { get; } = userId;
        public double ExpiresAt { get; } = expiresAt;
        public int Attempts { get; set; }
        public long? LastUsedStep { get; set; }
        public bool Used { get; set; }
    }

    private readonly Dictionary<string, PendingLogin> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string StartLogin(string userId, double? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var createdAt = now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        var attemptId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var code = Totp.Generate(
            Totp.DeriveUserSecret(config.SharedSecret, userId),
            config.CodeLength,
            config.Step,
            createdAt,
            System.Text.Encoding.ASCII.GetBytes(attemptId));
        lock (_gate)
        {
            _pending.Add(
                attemptId,
                new PendingLogin(attemptId, userId, createdAt + config.PendingTtl));
        }
        // TODO: Replace prototype output with the AWS SES email-delivery call.
        Console.WriteLine($"Prototype email code: {code}");
        return attemptId;
    }

    public bool VerifyLogin(string attemptId, string code, double? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        lock (_gate)
        {
            if (!_pending.TryGetValue(attemptId, out var pending) ||
                pending.Used ||
                currentTime >= pending.ExpiresAt)
            {
                if (pending is not null)
                {
                    pending.Used = true;
                }
                return false;
            }
            if (pending.Attempts >= config.MaxAttempts)
            {
                pending.Used = true;
                return false;
            }

            pending.Attempts++;
            var (ok, matchedStep) = Atomic.Verify(
                code,
                Totp.DeriveUserSecret(config.SharedSecret, pending.UserId),
                config.CodeLength,
                config.Step,
                config.AtomicAllowedWindow,
                System.Text.Encoding.ASCII.GetBytes(pending.AttemptId),
                pending.LastUsedStep,
                currentTime);
            if (!ok)
            {
                if (pending.Attempts >= config.MaxAttempts)
                {
                    pending.Used = true;
                }
                return false;
            }

            pending.LastUsedStep = matchedStep;
            pending.Used = true;
            return true;
        }
    }
}
