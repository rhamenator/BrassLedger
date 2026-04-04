using Microsoft.AspNetCore.DataProtection;

namespace BrassLedger.Infrastructure.Security;

public sealed class SensitiveDataProtector(IDataProtectionProvider dataProtectionProvider) : ISensitiveDataProtector
{
    private const string ProtectedPrefix = "enc::";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("BrassLedger.SensitiveData.v1");

    public bool IsProtected(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith(ProtectedPrefix, StringComparison.Ordinal);
    }

    public string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (IsProtected(value))
        {
            return value;
        }

        return ProtectedPrefix + _protector.Protect(value);
    }

    public string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (!IsProtected(value))
        {
            return value;
        }

        var payload = value[ProtectedPrefix.Length..];
        return _protector.Unprotect(payload);
    }
}
