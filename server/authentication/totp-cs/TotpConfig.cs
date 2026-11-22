using System.Globalization;
using System.Text;

namespace TotpPrototype;

public sealed record TotpConfig(
    byte[] SharedSecret,
    int Step,
    int AtomicAllowedWindow,
    int CodeLength,
    int MaxAttempts,
    int PendingTtl)
{
    public static TotpConfig Load(string? envFilePath = null)
    {
        var fileValues = ReadEnvFile(
            envFilePath ?? Path.Combine(Environment.CurrentDirectory, ".env"));
        string Get(string name, string fallback)
        {
            var processValue = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(processValue)
                ? processValue
                : fileValues.GetValueOrDefault(name, fallback);
        }

        var rawSecret = Get("SHARED_SECRET", "SECRET");
        if ((string.IsNullOrWhiteSpace(rawSecret) || rawSecret == "SECRET") &&
            Environment.GetEnvironmentVariable("PAL_TOTP_TESTING") != "1")
        {
            Console.Error.WriteLine(
                "SHARED_SECRET is empty or still the placeholder; configure a private secret.");
            throw new InvalidOperationException(
                "SHARED_SECRET must be non-empty and non-placeholder outside tests.");
        }
        var result = new TotpConfig(
            Encoding.UTF8.GetBytes(rawSecret),
            ParseInt("STEP", 30, Get),
            ParseInt("ATOMIC_ALLOWEDWINDOW", 1, Get),
            ParseInt("CODE_LENGTH", 6, Get),
            ParseInt("MAX_ATTEMPTS", 5, Get),
            ParseInt("PENDING_TTL", 300, Get));
        if (result.Step <= 0 ||
            result.AtomicAllowedWindow < 0 ||
            result.CodeLength is < 6 or > 8 ||
            result.MaxAttempts <= 0 ||
            result.PendingTtl <= 0)
        {
            throw new InvalidOperationException("TOTP configuration values are out of range.");
        }
        return result;
    }

    private static int ParseInt(
        string name,
        int fallback,
        Func<string, string, string> get)
    {
        var raw = get(name, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{name} must be an integer.");
        }
        return value;
    }

    private static Dictionary<string, string> ReadEnvFile(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return values;
        }
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }
            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }
        return values;
    }
}
