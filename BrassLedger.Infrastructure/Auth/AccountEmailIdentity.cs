using System.Security.Cryptography;
using System.Text;
using MimeKit;

namespace BrassLedger.Infrastructure.Auth;

internal static class AccountEmailIdentity
{
    public static bool TryNormalize(string? value, out string normalizedAddress, out string lookupHash)
    {
        normalizedAddress = string.Empty;
        lookupHash = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !MailboxAddress.TryParse(value.Trim(), out var parsed)) return false;

        normalizedAddress = parsed.Address.Trim();
        lookupHash = ComputeLookupHash(normalizedAddress);
        return true;
    }

    public static string ComputeLookupHash(string address)
    {
        var normalized = address.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
