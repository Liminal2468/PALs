using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Palsys.Server.Identity;

public enum PendingLoginVerificationStatus
{
    Verified,
    InvalidCode,
    Expired,
    AttemptsExceeded,
    NotFound,
}

public sealed record PendingLoginStart(
    string AttemptId,
    string Email,
    string Code,
    int ExpiresInSeconds,
    int CodeLength);

public sealed record PendingLoginVerification(
    PendingLoginVerificationStatus Status,
    string? Email = null,
    bool TermsAccepted = false);

public sealed class PendingLoginStore(TotpConfig config)
{
    private const int MaximumPendingAttempts = 10_000;
    private readonly ConcurrentDictionary<string, PendingAttempt> attempts = new(StringComparer.Ordinal);

    public PendingLoginStart? Create(string email, bool termsAccepted)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        foreach (var entry in attempts)
        {
            if (entry.Value.ExpiresAt <= now)
                attempts.TryRemove(entry.Key, out _);
        }
        if (attempts.Count >= MaximumPendingAttempts)
            return null;

        var attemptId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var code = Totp.Generate(
            Totp.DeriveUserSecret(config.SharedSecret, email),
            config.CodeLength,
            config.Step,
            now,
            Encoding.ASCII.GetBytes(attemptId));
        var currentStep = (long)Math.Floor(now / config.Step);
        var codeExpiresAt = (currentStep + config.AtomicAllowedWindow + 1) * config.Step;
        var expiresAt = Math.Min(now + config.PendingTtl, codeExpiresAt);
        var expiresInSeconds = Math.Max(1, (int)Math.Ceiling(expiresAt - now));
        var pending = new PendingAttempt(
            email,
            expiresAt,
            termsAccepted);
        attempts[attemptId] = pending;
        return new PendingLoginStart(attemptId, email, code, expiresInSeconds, config.CodeLength);
    }

    public PendingLoginStart? Resend(string attemptId)
    {
        if (!attempts.TryGetValue(attemptId, out var attempt))
            return null;

        lock (attempt)
        {
            if (attempt.Used || !attempts.ContainsKey(attemptId))
                return null;

            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d >= attempt.ExpiresAt)
            {
                attempts.TryRemove(attemptId, out _);
                return null;
            }

            return Create(attempt.Email, attempt.TermsAccepted);
        }
    }

    public PendingLoginVerification Verify(string attemptId, string code)
    {
        if (!attempts.TryGetValue(attemptId, out var attempt))
            return new PendingLoginVerification(PendingLoginVerificationStatus.NotFound);

        lock (attempt)
        {
            if (attempt.Used || !attempts.ContainsKey(attemptId))
                return new PendingLoginVerification(PendingLoginVerificationStatus.NotFound);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            if (now >= attempt.ExpiresAt)
            {
                attempts.TryRemove(attemptId, out _);
                return new PendingLoginVerification(PendingLoginVerificationStatus.Expired);
            }

            if (attempt.Attempts >= config.MaxAttempts)
            {
                attempts.TryRemove(attemptId, out _);
                return new PendingLoginVerification(PendingLoginVerificationStatus.AttemptsExceeded);
            }

            attempt.Attempts++;
            var (ok, matchedStep) = Atomic.Verify(
                code,
                Totp.DeriveUserSecret(config.SharedSecret, attempt.Email),
                config.CodeLength,
                config.Step,
                config.AtomicAllowedWindow,
                Encoding.ASCII.GetBytes(attemptId),
                attempt.LastUsedStep,
                now);
            if (!ok)
            {
                if (attempt.Attempts >= config.MaxAttempts)
                {
                    attempts.TryRemove(attemptId, out _);
                    return new PendingLoginVerification(PendingLoginVerificationStatus.AttemptsExceeded);
                }

                return new PendingLoginVerification(PendingLoginVerificationStatus.InvalidCode);
            }

            attempt.LastUsedStep = matchedStep;
            attempt.Used = true;
            attempts.TryRemove(attemptId, out _);
            return new PendingLoginVerification(
                PendingLoginVerificationStatus.Verified,
                attempt.Email,
                attempt.TermsAccepted);
        }
    }

    public void Cancel(string attemptId)
    {
        if (attempts.TryGetValue(attemptId, out var attempt))
        {
            lock (attempt)
            {
                attempt.Used = true;
                attempts.TryRemove(attemptId, out _);
            }
        }
    }

    private sealed class PendingAttempt(string email, double expiresAt, bool termsAccepted)
    {
        public string Email { get; } = email;
        public double ExpiresAt { get; } = expiresAt;
        public bool TermsAccepted { get; } = termsAccepted;
        public int Attempts { get; set; }
        public long? LastUsedStep { get; set; }
        public bool Used { get; set; }
    }
}
