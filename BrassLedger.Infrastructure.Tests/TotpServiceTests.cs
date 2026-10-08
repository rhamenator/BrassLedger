using System.Text;
using BrassLedger.Infrastructure.Auth;

namespace BrassLedger.Infrastructure.Tests;

public sealed class TotpServiceTests
{
    public static TheoryData<long, string> Rfc6238Sha1Vectors => new()
    {
        { 59, "94287082" },
        { 1_111_111_109, "07081804" },
        { 1_111_111_111, "14050471" },
        { 1_234_567_890, "89005924" },
        { 2_000_000_000, "69279037" },
        { 20_000_000_000, "65353130" }
    };

    [Theory]
    [MemberData(nameof(Rfc6238Sha1Vectors))]
    public void ComputeCode_MatchesRfc6238Sha1Vectors(long unixTime, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");

        var actual = TotpService.ComputeCode(secret, unixTime / TotpService.TimeStepSeconds, 8);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Base32_RoundTripsAuthenticatorSecret_AndBuildsStandardsUri()
    {
        var service = new TotpService();
        var source = Enumerable.Range(0, 20).Select(value => (byte)value).ToArray();
        var encoded = TotpService.EncodeBase32(source);

        Assert.Equal(source, TotpService.DecodeBase32(encoded.ToLowerInvariant()));
        var uri = service.BuildOtpAuthUri("owner@example.test", encoded);
        Assert.StartsWith("otpauth://totp/BrassLedger%3Aowner%40example.test?", uri, StringComparison.Ordinal);
        Assert.Contains($"secret={encoded}", uri, StringComparison.Ordinal);
        Assert.Contains("algorithm=SHA1&digits=6&period=30", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyCode_AllowsOneStepOfClockSkew_ButRejectsReplay()
    {
        var service = new TotpService();
        var secret = TotpService.EncodeBase32(Encoding.ASCII.GetBytes("12345678901234567890"));
        var now = DateTimeOffset.FromUnixTimeSeconds(1_234_567_890);
        var previousStep = now.ToUnixTimeSeconds() / TotpService.TimeStepSeconds - 1;
        var code = TotpService.ComputeCode(TotpService.DecodeBase32(secret), previousStep);

        Assert.Equal(previousStep, service.VerifyCode(secret, code, now));
        Assert.Null(service.VerifyCode(secret, code, now, previousStep));
        Assert.Null(service.VerifyCode(secret, "not-a-code", now));
    }
}
