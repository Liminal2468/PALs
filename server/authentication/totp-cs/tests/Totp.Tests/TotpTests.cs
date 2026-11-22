using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TotpPrototype;

namespace TotpPrototype.Tests;

public sealed class TotpTests
{
    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void HotpMatchesRfc4226Vectors(ulong counter, string expected)
    {
        Assert.Equal(
            expected,
            Hotp.Generate(Encoding.ASCII.GetBytes("12345678901234567890"), counter));
    }

    [Fact]
    public void HotpSupportsSha256()
    {
        Assert.Equal(
            "150664",
            Hotp.Generate(
                Encoding.ASCII.GetBytes("sha256-test-secret"),
                123456,
                algorithm: System.Security.Cryptography.HashAlgorithmName.SHA256));
    }

    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1111111109, "07081804")]
    [InlineData(1111111111, "14050471")]
    [InlineData(1234567890, "89005924")]
    [InlineData(2000000000, "69279037")]
    [InlineData(20000000000, "65353130")]
    public void TotpMatchesRfc6238Sha1Vectors(double timestamp, string expected)
    {
        Assert.Equal(
            expected,
            Totp.Generate(
                Encoding.ASCII.GetBytes("12345678901234567890"),
                length: 8,
                forTime: timestamp));
    }

    [Fact]
    public void SharedPythonFixturesMatch()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "parity.json");
        var fixtures = JsonSerializer.Deserialize<List<ParityFixture>>(
            File.ReadAllText(fixturePath))!;
        foreach (var fixture in fixtures)
        {
            var actual = Totp.Generate(
                Convert.FromBase64String(fixture.Secret),
                forTime: fixture.Time,
                context: Convert.FromBase64String(fixture.Context));
            Assert.Equal(fixture.ExpectedCode, actual);
        }
    }

    [Fact]
    public void CorrectCodeVerifiesOnceAndReplayFails()
    {
        var config = TestConfig();
        var store = new PendingLoginStore(config);
        const double now = 1_700_000_000;
        var attemptId = StartWithoutCodeOutput(store, "student@example.edu", now);
        var code = Totp.Generate(
            Totp.DeriveUserSecret(config.SharedSecret, "student@example.edu"),
            forTime: now,
            context: Encoding.ASCII.GetBytes(attemptId));
        var (firstOk, matchedStep) = Atomic.Verify(
            code,
            Totp.DeriveUserSecret(config.SharedSecret, "student@example.edu"),
            context: Encoding.ASCII.GetBytes(attemptId),
            forTime: now);
        Assert.True(firstOk);
        Assert.False(Atomic.Verify(
            code,
            Totp.DeriveUserSecret(config.SharedSecret, "student@example.edu"),
            context: Encoding.ASCII.GetBytes(attemptId),
            lastUsedStep: matchedStep,
            forTime: now).Ok);

        Assert.True(store.VerifyLogin(attemptId, code, now));
        Assert.False(store.VerifyLogin(attemptId, code, now));
    }

    [Fact]
    public void ContextAndUserSecretAreBound()
    {
        const double now = 1_700_000_000;
        var userSecret = Totp.DeriveUserSecret(Encoding.UTF8.GetBytes("master"), "user-x");
        var code = Totp.Generate(userSecret, forTime: now, context: Encoding.UTF8.GetBytes("attempt-a"));
        Assert.False(Atomic.Verify(
            code, userSecret, context: Encoding.UTF8.GetBytes("attempt-b"), forTime: now).Ok);

        var anotherUser = Totp.DeriveUserSecret(Encoding.UTF8.GetBytes("master"), "user-y");
        Assert.False(Atomic.Verify(
            code, anotherUser, context: Encoding.UTF8.GetBytes("attempt-a"), forTime: now).Ok);
    }

    [Fact]
    public void PreviousStepRequiresDriftWindow()
    {
        const double now = 1_700_000_000;
        var currentStep = (long)(now / 30);
        var secret = Encoding.UTF8.GetBytes("window-test-secret");
        var previousCode = Hotp.Generate(secret, (ulong)(currentStep - 1));

        Assert.Equal(
            (true, (long?)(currentStep - 1)),
            Atomic.Verify(previousCode, secret, allowedWindow: 1, forTime: now));
        Assert.Equal(
            (false, (long?)null),
            Atomic.Verify(previousCode, secret, allowedWindow: 0, forTime: now));
    }

    [Fact]
    public void MaxWrongAttemptsInvalidateLogin()
    {
        var config = TestConfig() with { MaxAttempts = 2 };
        var store = new PendingLoginStore(config);
        const double now = 1_700_000_000;
        var userId = "student@example.edu";
        var attemptId = StartWithoutCodeOutput(store, userId, now);
        var code = Totp.Generate(
            Totp.DeriveUserSecret(config.SharedSecret, userId),
            forTime: now,
            context: Encoding.ASCII.GetBytes(attemptId));

        Assert.False(store.VerifyLogin(attemptId, "000000", now));
        Assert.False(store.VerifyLogin(attemptId, "000001", now));
        Assert.False(store.VerifyLogin(attemptId, code, now));
    }

    [Fact]
    public void ExpiredLoginIsRejected()
    {
        var store = new PendingLoginStore(TestConfig());
        var attemptId = StartWithoutCodeOutput(store, "student@example.edu", 100);
        Assert.False(store.VerifyLogin(attemptId, "000000", 400));
    }

    [Fact]
    public void DeriveUserSecretIsStableAndDistinct()
    {
        var master = Encoding.UTF8.GetBytes("master");
        Assert.Equal(
            Totp.DeriveUserSecret(master, "user-x"),
            Totp.DeriveUserSecret(master, "user-x"));
        Assert.NotEqual(
            Totp.DeriveUserSecret(master, "user-x"),
            Totp.DeriveUserSecret(master, "user-y"));
    }

    private static TotpConfig TestConfig() =>
        new(
            Encoding.UTF8.GetBytes("test-only-master-secret-not-for-production"),
            Step: 30,
            AtomicAllowedWindow: 1,
            CodeLength: 6,
            MaxAttempts: 5,
            PendingTtl: 300);

    private static string StartWithoutCodeOutput(
        PendingLoginStore store,
        string userId,
        double now)
    {
        var originalOutput = Console.Out;
        using var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            return store.StartLogin(userId, now);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    private sealed record ParityFixture(
        [property: JsonPropertyName("secret")] string Secret,
        [property: JsonPropertyName("context")] string Context,
        [property: JsonPropertyName("time")] double Time,
        [property: JsonPropertyName("expected_code")] string ExpectedCode);
}
