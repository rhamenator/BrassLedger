namespace BrassLedger.Infrastructure.Auth;

public interface IUserAuthenticationService
{
    Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default);
}
