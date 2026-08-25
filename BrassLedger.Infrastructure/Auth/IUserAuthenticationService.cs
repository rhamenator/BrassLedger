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
}

public sealed record AccountSecuritySnapshot(
    string UserName,
    string DisplayName,
    string Email,
    DateTimeOffset? LastPasswordChangedUtc,
    DateTimeOffset? LastSuccessfulSignInUtc,
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
