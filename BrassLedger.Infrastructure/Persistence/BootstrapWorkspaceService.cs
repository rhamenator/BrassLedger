using BrassLedger.Domain.Accounting;
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

        var adminUser = new AppUser
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserName = request.AdminUserName.Trim(),
            DisplayName = request.AdminDisplayName.Trim(),
            Email = request.AdminEmail.Trim(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = "Administrator",
            IsActive = true,
            LastPasswordChangedUtc = DateTimeOffset.UtcNow
        };

        adminUser.PasswordHash = passwordHasher.HashPassword(adminUser, request.AdminPassword);

        await dbContext.Companies.AddAsync(company, cancellationToken);
        await dbContext.Users.AddAsync(adminUser, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BootstrapWorkspaceResult.Created(new AuthenticatedUser(
            adminUser.Id,
            companyId,
            adminUser.UserName,
            adminUser.DisplayName,
            adminUser.Email,
            adminUser.Role,
            adminUser.SecurityStamp));
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

        if (string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            return "Enter an administrator email address.";
        }

        if (string.IsNullOrWhiteSpace(request.AdminPassword) || request.AdminPassword.Length < 12)
        {
            return "Choose an administrator password with at least 12 characters.";
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
    string AdminPassword);

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
