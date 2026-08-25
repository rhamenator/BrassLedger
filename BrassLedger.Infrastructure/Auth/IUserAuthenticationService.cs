namespace BrassLedger.Infrastructure.Auth;

public interface IUserAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
    Task<AuthenticatedUser?> SwitchCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default);
    Task<AccountSecuritySnapshot?> GetAccountSecurityAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AccountSecurityResult> ChangePasswordAsync(
        Guid userId,
        Guid companyId,
        string currentPassword,
        string newPassword,
        string confirmPassword,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
    Task<AccountSecurityResult> RevokeOtherSessionsAsync(
        Guid userId,
        Guid companyId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
    Task<MfaEnrollmentResult> BeginMfaEnrollmentAsync(
        Guid userId,
        Guid companyId,
        string currentPassword,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
    Task<MfaOperationResult> EnableMfaAsync(
        Guid userId,
        Guid companyId,
        string verificationCode,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
    Task<MfaEnrollmentResult> RegenerateMfaRecoveryCodesAsync(
        Guid userId,
        Guid companyId,
        string currentPassword,
        string verificationCode,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
    Task<AccountSecurityResult> DisableMfaAsync(
        Guid userId,
        Guid companyId,
        string currentPassword,
        string verificationCode,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
    Task<MfaChallengeResult> CompleteMfaChallengeAsync(
        string challengeToken,
        string verificationCode,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
}

public sealed record AccountSecuritySnapshot(
    string UserName,
    string DisplayName,
    string Email,
    DateTimeOffset? LastPasswordChangedUtc,
    DateTimeOffset? LastSuccessfulSignInUtc,
    bool MfaEnabled,
    DateTimeOffset? MfaEnrolledAtUtc,
    int RecoveryCodesRemaining,
    bool MfaRequiredByRole,
    IReadOnlyList<AccountSecurityEventSnapshot> RecentEvents);

public sealed record AccountSecurityEventSnapshot(
    string EventType,
    bool Succeeded,
    DateTimeOffset OccurredUtc,
    string IpAddress,
    string UserAgent,
    string Detail);

public enum AccountSecurityOutcome
{
    Succeeded,
    InvalidRequest,
    InvalidCurrentPassword,
    PasswordReused,
    Unauthorized
}

public sealed record AccountSecurityResult(
    AccountSecurityOutcome Outcome,
    AuthenticatedUser? User = null);

public enum MfaOperationOutcome
{
    Succeeded,
    InvalidRequest,
    InvalidPassword,
    InvalidCode,
    Expired,
    AlreadyEnabled,
    NotEnabled,
    LockedOut,
    Unauthorized
}

public sealed record MfaEnrollmentResult(
    MfaOperationOutcome Outcome,
    string Secret = "",
    string OtpAuthUri = "",
    IReadOnlyList<string>? RecoveryCodes = null,
    DateTimeOffset? LockoutEndUtc = null);

public sealed record MfaOperationResult(
    MfaOperationOutcome Outcome,
    DateTimeOffset? LockoutEndUtc = null);

public sealed record MfaChallengeResult(
    MfaOperationOutcome Outcome,
    AuthenticatedUser? User = null,
    DateTimeOffset? LockoutEndUtc = null,
    bool UsedRecoveryCode = false);
