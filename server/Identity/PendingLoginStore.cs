using System.Collections.Concurrent;
using System.Globalization;
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

public sealed record PendingLoginStart(string AttemptId, string Code, int ExpiresInSeconds);

public sealed record PendingLoginVerification(
    PendingLoginVerificationStatus Status,
    string? Email = null,
    bool TermsAccepted = false);

public sealed class PendingLoginStore
{
    private static readonly TimeSpan AttemptLifetime = TimeSpan.FromMinutes(5);
    private const int MaximumAttempts = 5;
    private const int MaximumPendingAttempts = 10_000;
    private readonly ConcurrentDictionary<string, PendingAttempt> attempts = new(StringComparer.Ordinal);

    public PendingLoginStart? Create(string email, bool termsAccepted)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in attempts)
        {
            if (entry.Value.ExpiresAt <= now)
                attempts.TryRemove(entry.Key, out _);
        }
        if (attempts.Count >= MaximumPendingAttempts)
            return null;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var attemptId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var codeHash = SHA256.HashData(Encoding.ASCII.GetBytes(code));
        attempts[attemptId] = new PendingAttempt(
            email,
            codeHash,
            now.Add(AttemptLifetime),
            termsAccepted);
        return new PendingLoginStart(attemptId, code, (int)AttemptLifetime.TotalSeconds);
    }

    public PendingLoginVerification Verify(string attemptId, string code)
    {
        if (!attempts.TryGetValue(attemptId, out var attempt))
            return new PendingLoginVerification(PendingLoginVerificationStatus.NotFound);

        lock (attempt)
        {
            if (attempt.Used || !attempts.ContainsKey(attemptId))
                return new PendingLoginVerification(PendingLoginVerificationStatus.NotFound);

            if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
            {
                attempts.TryRemove(attemptId, out _);
                return new PendingLoginVerification(PendingLoginVerificationStatus.Expired);
            }

            if (attempt.FailedAttempts >= MaximumAttempts)
            {
                attempts.TryRemove(attemptId, out _);
                return new PendingLoginVerification(PendingLoginVerificationStatus.AttemptsExceeded);
            }

            var candidateHash = SHA256.HashData(Encoding.ASCII.GetBytes(code));
            if (!CryptographicOperations.FixedTimeEquals(attempt.CodeHash, candidateHash))
            {
                attempt.FailedAttempts++;
                if (attempt.FailedAttempts >= MaximumAttempts)
                {
                    attempts.TryRemove(attemptId, out _);
                    return new PendingLoginVerification(PendingLoginVerificationStatus.AttemptsExceeded);
                }

                return new PendingLoginVerification(PendingLoginVerificationStatus.InvalidCode);
            }

            attempt.Used = true;
            attempts.TryRemove(attemptId, out _);
            return new PendingLoginVerification(
                PendingLoginVerificationStatus.Verified,
                attempt.Email,
                attempt.TermsAccepted);
        }
    }

    private sealed class PendingAttempt(
        string email,
        byte[] codeHash,
        DateTimeOffset expiresAt,
        bool termsAccepted)
    {
        public string Email { get; } = email;
        public byte[] CodeHash { get; } = codeHash;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public bool TermsAccepted { get; } = termsAccepted;
        public int FailedAttempts { get; set; }
        public bool Used { get; set; }
    }
}
