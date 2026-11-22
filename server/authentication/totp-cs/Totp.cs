using System.Security.Cryptography;

namespace TotpPrototype;

public static class Totp
{
    public static string Generate(
        byte[] secret,
        int length = 6,
        int step = 30,
        double? forTime = null,
        byte[]? context = null)
    {
        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }
        var timestamp = forTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(forTime));
        }
        var counter = (ulong)Math.Floor(timestamp / step);
        return Hotp.Generate(secret, counter, length, HashAlgorithmName.SHA1, context);
    }

    public static byte[] DeriveUserSecret(byte[] masterSecret, string userId)
    {
        ArgumentNullException.ThrowIfNull(masterSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return HMACSHA256.HashData(
            masterSecret,
            System.Text.Encoding.UTF8.GetBytes(userId));
    }
}
