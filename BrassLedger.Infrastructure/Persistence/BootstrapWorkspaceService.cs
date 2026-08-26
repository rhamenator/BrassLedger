using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.SecurityAdministration;
using BrassLedger.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Persistence;

public interface IBootstrapWorkspaceService
{
    Task<bool> RequiresSetupAsync(CancellationToken cancellationToken = default);
    Task<BootstrapWorkspaceResult> CreateInitialWorkspaceAsync(BootstrapWorkspaceRequest request, CancellationToken cancellationToken = default);
}

public sealed class BootstrapWorkspaceService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IPasswordHasher<AppUser> passwordHasher) : IBootstrapWorkspaceService
{
    public async Task<bool> RequiresSetupAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return !await dbContext.Companies.AnyAsync(cancellationToken);
    }

    public async Task<BootstrapWorkspaceResult> CreateInitialWorkspaceAsync(BootstrapWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = Validate(request);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BootstrapWorkspaceResult.Invalid(validationError);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.Companies.AnyAsync(cancellationToken))
        {
            return BootstrapWorkspaceResult.AlreadyConfigured();
        }

        var normalizedUserName = request.AdminUserName.Trim().ToUpperInvariant();
        if (await dbContext.Users.AnyAsync(user => user.UserName.ToUpper() == normalizedUserName, cancellationToken))
        {
            return BootstrapWorkspaceResult.Invalid("That administrator username is already in use.");
        }

        var companyId = Guid.NewGuid();
        var company = new Company
        {
            Id = companyId,
            Name = request.CompanyName.Trim(),
            LegalName = request.LegalName.Trim(),
            TaxId = request.TaxId.Trim(),
            BaseCurrency = string.IsNullOrWhiteSpace(request.BaseCurrency) ? "USD" : request.BaseCurrency.Trim().ToUpperInvariant(),
            FiscalYearStartMonth = request.FiscalYearStartMonth
        };

        await dbContext.Companies.AddAsync(company, cancellationToken);
        await SecurityAdministrationService.EnsureBuiltInRolesAsync(dbContext, companyId, cancellationToken);

        var adminRole = await dbContext.AccessRoles
            .AsNoTracking()
            .SingleAsync(role => role.CompanyId == companyId && role.Name == "Administrator", cancellationToken);

        _ = AccountEmailIdentity.TryNormalize(request.AdminEmail, out var normalizedAdminEmail, out var adminEmailLookupHash);

        var adminUser = new AppUser
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserName = request.AdminUserName.Trim(),
            DisplayName = request.AdminDisplayName.Trim(),
            Email = normalizedAdminEmail,
            EmailLookupHash = adminEmailLookupHash,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = adminRole.Name,
            IsActive = true,
            LastPasswordChangedUtc = DateTimeOffset.UtcNow
        };

        adminUser.PasswordHash = passwordHasher.HashPassword(adminUser, request.AdminPassword);

        await dbContext.Users.AddAsync(adminUser, cancellationToken);
        await dbContext.CompanyMemberships.AddAsync(new CompanyMembership { Id = Guid.NewGuid(), UserId = adminUser.Id, CompanyId = companyId, Role = adminRole.Name, IsOwner = true, IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow }, cancellationToken);
        var accounts = DefaultAccountingSetup.CreateAccounts(companyId);
        await dbContext.Accounts.AddRangeAsync(accounts, cancellationToken);
        await dbContext.BankAccounts.AddAsync(DefaultAccountingSetup.CreateOperatingBankAccount(companyId, accounts.Single(account => account.OperationalRole == AccountingAccountRoles.OperatingCash).Id), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BootstrapWorkspaceResult.Created(new AuthenticatedUser(
            adminUser.Id,
            companyId,
            adminUser.UserName,
            adminUser.DisplayName,
            adminUser.Email,
            adminUser.Role,
            adminUser.SecurityStamp,
            adminRole.RequiresMfa ? [] : adminRole.Permissions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            MfaEnrollmentRequired: adminRole.RequiresMfa));
    }


    private static string Validate(BootstrapWorkspaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return "Enter a company name.";
        }

        if (string.IsNullOrWhiteSpace(request.LegalName))
        {
            return "Enter a legal name.";
        }

        if (string.IsNullOrWhiteSpace(request.AdminUserName))
        {
            return "Enter an administrator username.";
        }

        if (string.IsNullOrWhiteSpace(request.AdminDisplayName))
        {
            return "Enter an administrator display name.";
        }

        if (!AccountEmailIdentity.TryNormalize(request.AdminEmail, out _, out _))
        {
            return "Enter a valid administrator email address.";
        }

        if (string.IsNullOrWhiteSpace(request.AdminPassword) || request.AdminPassword.Length < 12)
        {
            return "Choose an administrator password with at least 12 characters.";
        }

        if (!string.Equals(request.AdminPassword, request.ConfirmAdminPassword, StringComparison.Ordinal))
        {
            return "The administrator password confirmation does not match.";
        }

        if (request.FiscalYearStartMonth is < 1 or > 12)
        {
            return "Fiscal year start month must be between 1 and 12.";
        }

        return string.Empty;
    }
}

public sealed record BootstrapWorkspaceRequest(
    string CompanyName,
    string LegalName,
    string TaxId,
    string BaseCurrency,
    int FiscalYearStartMonth,
    string AdminUserName,
    string AdminDisplayName,
    string AdminEmail,
    string AdminPassword,
    string ConfirmAdminPassword);

public sealed record BootstrapWorkspaceResult(
    BootstrapWorkspaceOutcome Outcome,
    string ErrorMessage,
    AuthenticatedUser? User)
{
    public static BootstrapWorkspaceResult Created(AuthenticatedUser user) =>
        new(BootstrapWorkspaceOutcome.Created, string.Empty, user);

    public static BootstrapWorkspaceResult Invalid(string errorMessage) =>
        new(BootstrapWorkspaceOutcome.Invalid, errorMessage, null);

    public static BootstrapWorkspaceResult AlreadyConfigured() =>
        new(BootstrapWorkspaceOutcome.AlreadyConfigured, "BrassLedger has already been configured.", null);
}

public enum BootstrapWorkspaceOutcome
{
    Created,
    Invalid,
    AlreadyConfigured
}
