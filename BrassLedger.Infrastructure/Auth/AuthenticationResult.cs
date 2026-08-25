namespace BrassLedger.Infrastructure.Auth;

public enum AuthenticationOutcome
{
    Succeeded,
    MfaRequired,
    InvalidCredentials,
    LockedOut
}

public sealed record AuthenticationResult(
    AuthenticationOutcome Outcome,
    AuthenticatedUser? User = null,
    DateTimeOffset? LockoutEndUtc = null,
    string MfaChallengeToken = "");
