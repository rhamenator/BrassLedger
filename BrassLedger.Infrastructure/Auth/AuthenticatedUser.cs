namespace BrassLedger.Infrastructure.Auth;

public sealed record AuthenticatedUser(
    Guid UserId,
    Guid CompanyId,
    string UserName,
    string DisplayName,
    string Email,
    string Role);
