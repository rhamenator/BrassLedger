namespace BrassLedger.Infrastructure.Security;

public interface ISensitiveDataProtector
{
    bool IsProtected(string value);
    string Protect(string value);
    string Unprotect(string value);
}
