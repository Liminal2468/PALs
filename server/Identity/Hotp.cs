using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Palsys.Server.Identity;

public static class Hotp
{
    public static string Generate(
        byte[] secret,
        ulong counter,
        int length = 6,
        HashAlgorithmName algorithm = default,
        byte[]? context = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (length is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(length));

        Span<byte> counterBytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(counterBytes, counter);
        context ??= [];
        var message = new byte[counterBytes.Length + context.Length];
        counterBytes.CopyTo(message);
        context.CopyTo(message, counterBytes.Length);

        var digest = algorithm.Name switch
        {
            null or "SHA1" => HMACSHA1.HashData(secret, message),
            "SHA256" => HMACSHA256.HashData(secret, message),
            _ => throw new ArgumentException("Only SHA-1 and SHA-256 are supported.", nameof(algorithm)),
        };
        var offset = digest[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(offset, 4))
            & 0x7FFFFFFF;
        var code = (ulong)binary % (ulong)Math.Pow(10, length);
        return code.ToString($"D{length}", System.Globalization.CultureInfo.InvariantCulture);
    }
}
