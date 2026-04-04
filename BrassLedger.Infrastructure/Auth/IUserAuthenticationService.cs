namespace BrassLedger.Infrastructure.Auth;

public interface IUserAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);
}
