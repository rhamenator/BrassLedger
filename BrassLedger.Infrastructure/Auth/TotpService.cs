using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BrassLedger.Infrastructure.Auth;

public sealed class TotpService
{
    public const int TimeStepSeconds = 30;
    public const int CodeDigits = 6;

    public string GenerateSecret()
    {
        return EncodeBase32(RandomNumberGenerator.GetBytes(20));
    }

    public string BuildOtpAuthUri(string userName, string secret)
    {
        var label = Uri.EscapeDataString($"BrassLedger:{userName}");
        return $"otpauth://totp/{label}?secret={secret}&issuer=BrassLedger&algorithm=SHA1&digits={CodeDigits}&period={TimeStepSeconds}";
    }

    public long? VerifyCode(string secret, string code, DateTimeOffset now, long? lastAcceptedTimeStep = null)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != CodeDigits || code.Any(character => character is < '0' or > '9')) return null;

        byte[] secretBytes;
        try
        {
            secretBytes = DecodeBase32(secret);
        }
        catch (FormatException)
        {
            return null;
        }

        var currentStep = now.ToUnixTimeSeconds() / TimeStepSeconds;
        foreach (var offset in new long[] { 0, -1, 1 })
        {
            var candidateStep = currentStep + offset;
            if (candidateStep < 0 || (lastAcceptedTimeStep.HasValue && candidateStep <= lastAcceptedTimeStep.Value)) continue;
            var expected = ComputeCode(secretBytes, candidateStep, CodeDigits);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code)))
            {
                return candidateStep;
            }
        }

        return null;
    }

    public static string ComputeCode(ReadOnlySpan<byte> secret, long timeStep, int digits = CodeDigits)
    {
        if (digits is < 6 or > 8) throw new ArgumentOutOfRangeException(nameof(digits));
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0f;
        var binaryCode = BinaryPrimitives.ReadInt32BigEndian(hash.Slice(offset, 4)) & 0x7fffffff;
        var modulus = digits switch { 6 => 1_000_000, 7 => 10_000_000, _ => 100_000_000 };
        return (binaryCode % modulus).ToString($"D{digits}", CultureInfo.InvariantCulture);
    }

    public static string EncodeBase32(ReadOnlySpan<byte> value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        if (value.IsEmpty) return string.Empty;
        var output = new StringBuilder((value.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var item in value)
        {
            buffer = (buffer << 8) | item;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                output.Append(alphabet[(buffer >> bitsLeft) & 31]);
            }
        }

        if (bitsLeft > 0) output.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);
        return output.ToString();
    }

    public static byte[] DecodeBase32(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("A TOTP secret is required.");
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in normalized)
        {
            var item = character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= '2' and <= '7' => character - '2' + 26,
                _ => throw new FormatException("The TOTP secret is not valid Base32 data.")
            };
            buffer = (buffer << 5) | item;
            bitsLeft += 5;
            if (bitsLeft < 8) continue;
            bitsLeft -= 8;
            output.Add((byte)((buffer >> bitsLeft) & 0xff));
        }

        return output.ToArray();
    }
}
