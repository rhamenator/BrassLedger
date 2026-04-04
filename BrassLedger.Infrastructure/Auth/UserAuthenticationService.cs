using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Auth;

public sealed class UserAuthenticationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IPasswordHasher<AppUser> passwordHasher) : IUserAuthenticationService
{
    public async Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var normalizedUserName = userName.Trim().ToUpperInvariant();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.IsActive)
            .SingleOrDefaultAsync(candidate => candidate.UserName.ToUpper() == normalizedUserName, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return null;
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return null;
        }

        return new AuthenticatedUser(
            user.Id,
            user.CompanyId,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.Role);
    }
}
