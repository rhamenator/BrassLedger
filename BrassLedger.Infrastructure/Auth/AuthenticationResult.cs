namespace BrassLedger.Infrastructure.Auth;

public enum AuthenticationOutcome
{
    Succeeded,
    InvalidCredentials,
    LockedOut
}

public sealed record AuthenticationResult(
    AuthenticationOutcome Outcome,
    AuthenticatedUser? User = null,
    DateTimeOffset? LockoutEndUtc = null);
