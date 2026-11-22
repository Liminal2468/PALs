using System.Security.Cryptography;
using System.Text;

namespace Palsys.Server.Identity;

public static class Atomic
{
    public static (bool Ok, long? MatchedStep) Verify(
        string code,
        byte[] secret,
        int length = 6,
        int step = 30,
        int allowedWindow = 1,
        byte[]? context = null,
        long? lastUsedStep = null,
        double? forTime = null)
    {
        if (step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step));
        if (allowedWindow < 0)
            throw new ArgumentOutOfRangeException(nameof(allowedWindow));
        if (length is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(length));

        var timestamp = forTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        if (timestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(forTime));
        var currentStep = (long)Math.Floor(timestamp / step);
        if (code.Length != length || code.Any(character => character is < '0' or > '9'))
            return (false, null);

        long? matchedStep = null;
        var firstStep = Math.Max(0, currentStep - allowedWindow);
        var lastStep = currentStep + allowedWindow;
        for (var candidateStep = firstStep; candidateStep <= lastStep; candidateStep++)
        {
            var candidate = Hotp.Generate(
                secret,
                (ulong)candidateStep,
                length,
                HashAlgorithmName.SHA1,
                context);
            var matches = CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(code),
                Encoding.ASCII.GetBytes(candidate));
            if (matches && (lastUsedStep is null || candidateStep > lastUsedStep))
                matchedStep = candidateStep;
        }

        return (matchedStep.HasValue, matchedStep);
    }
}
