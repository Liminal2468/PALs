using System.Globalization;
using System.Text;

namespace Palsys.Server.Identity;

public sealed class TotpConfig
{
    private TotpConfig(
        byte[] sharedSecret,
        int step,
        int atomicAllowedWindow,
        int codeLength,
        int maxAttempts,
        int pendingTtl)
    {
        SharedSecret = sharedSecret;
        Step = step;
        AtomicAllowedWindow = atomicAllowedWindow;
        CodeLength = codeLength;
        MaxAttempts = maxAttempts;
        PendingTtl = pendingTtl;
    }

    public byte[] SharedSecret { get; }
    public int Step { get; }
    public int AtomicAllowedWindow { get; }
    public int CodeLength { get; }
    public int MaxAttempts { get; }
    public int PendingTtl { get; }

    public static TotpConfig Load(IConfiguration configuration)
    {
        var rawSecret = configuration["SHARED_SECRET"]?.Trim();
        if (string.IsNullOrWhiteSpace(rawSecret) || rawSecret == "SECRET")
            throw new InvalidOperationException("SHARED_SECRET must be configured with a private value.");

        var result = new TotpConfig(
            Encoding.UTF8.GetBytes(rawSecret),
            ReadInt(configuration, "STEP", 30),
            ReadInt(configuration, "ATOMIC_ALLOWEDWINDOW", 1),
            ReadInt(configuration, "CODE_LENGTH", 6),
            ReadInt(configuration, "MAX_ATTEMPTS", 5),
            ReadInt(configuration, "PENDING_TTL", 300));
        if (result.Step <= 0
            || result.AtomicAllowedWindow < 0
            || result.CodeLength != 6
            || result.MaxAttempts <= 0
            || result.PendingTtl <= 0)
        {
            throw new InvalidOperationException("TOTP configuration values are out of range.");
        }

        return result;
    }

    private static int ReadInt(IConfiguration configuration, string name, int fallback)
    {
        var raw = configuration[name];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"{name} must be an integer.");
        return value;
    }
}
