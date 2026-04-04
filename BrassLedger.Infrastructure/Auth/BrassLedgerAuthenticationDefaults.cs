namespace BrassLedger.Infrastructure.Auth;

public static class BrassLedgerAuthenticationDefaults
{
    public const string Scheme = "BrassLedgerCookie";
    public const string CookieName = "BrassLedger.Auth";
    public const string SeededPassword = "BrassLedger!2026";
    public const string SecurityStampClaimType = "security_stamp";
    public const string CompanyIdClaimType = "company_id";
    public const string DisplayNameClaimType = "display_name";
    public const int SessionMinutes = 20;
    public const int MaxFailedSignInAttempts = 5;
    public const int LockoutMinutes = 15;
}
