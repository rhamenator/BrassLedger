namespace BrassLedger.Infrastructure.Auth;

public static class BrassLedgerAuthenticationDefaults
{
    public const string Scheme = "BrassLedgerCookie";
    public const string CookieName = "BrassLedger.Auth";
    public const string MfaChallengeCookieName = "BrassLedger.MfaChallenge";
    public const string SeededPassword = "BrassLedger!2026";
    public const string SecurityStampClaimType = "security_stamp";
    public const string CompanyIdClaimType = "company_id";
    public const string DisplayNameClaimType = "display_name";
    public const string PermissionClaimType = "permission";
    public const string AuthenticationMethodClaimType = "amr";
    public const string MfaEnrollmentRequiredClaimType = "mfa_enrollment_required";
    public const string LoginRateLimitPolicy = "login";
    public const int SessionMinutes = 20;
    public const int MaxFailedSignInAttempts = 5;
    public const int LockoutMinutes = 15;
    public const int LoginRequestsPerMinute = 60;
    public const int MfaChallengeMinutes = 5;
    public const int MaxMfaAttempts = 5;
    public const int RecoveryCodeCount = 10;
}
