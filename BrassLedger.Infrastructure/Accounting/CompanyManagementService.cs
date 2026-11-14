using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.SecurityAdministration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class CompanyManagementService(IDbContextFactory<BrassLedgerDbContext> dbContextFactory, IHttpContextAccessor httpContextAccessor) : ICompanyManagementService
{
    public async Task<CompanyManagementResult> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.LegalName) || request.FiscalYearStartMonth is < 1 or > 12)
            return CompanyManagementResult.Failure("Enter company and legal names and a valid fiscal-year start month.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var actor = await CurrentActorAsync(db, cancellationToken);
        if (actor is null || !actor.Value.IsOwner) return CompanyManagementResult.Failure("An authenticated owner of the active company is required.");
        var name = request.Name.Trim(); if (await db.Companies.AnyAsync(company => company.Name == name, cancellationToken)) return CompanyManagementResult.Failure("A company with that name already exists.");
        var company = new Company { Id = Guid.NewGuid(), Name = name, LegalName = request.LegalName.Trim(), TaxId = request.TaxId.Trim(), BaseCurrency = string.IsNullOrWhiteSpace(request.BaseCurrency) ? "USD" : request.BaseCurrency.Trim().ToUpperInvariant(), FiscalYearStartMonth = request.FiscalYearStartMonth };
        db.Companies.Add(company);
        await SecurityAdministrationService.EnsureBuiltInRolesAsync(db, company.Id, cancellationToken);
        var adminRole = await db.AccessRoles.SingleAsync(role => role.CompanyId == company.Id && role.Name == "Administrator", cancellationToken);
        db.CompanyMemberships.Add(new CompanyMembership { Id = Guid.NewGuid(), UserId = actor.Value.UserId, CompanyId = company.Id, Role = adminRole.Name, IsOwner = true, IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow });
        var accounts = DefaultAccountingSetup.CreateAccounts(company.Id); db.Accounts.AddRange(accounts); db.BankAccounts.Add(DefaultAccountingSetup.CreateOperatingBankAccount(company.Id, accounts.Single(account => account.OperationalRole == AccountingAccountRoles.OperatingCash).Id));
        await db.SaveChangesAsync(cancellationToken); return CompanyManagementResult.Success(company.Id);
    }

    public async Task<IReadOnlyList<CompanyMembershipSnapshot>> GetMyCompaniesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var actor = await CurrentActorAsync(db, cancellationToken); if (actor is null) return [];
        return await db.CompanyMemberships.Where(membership => membership.UserId == actor.Value.UserId && membership.IsActive)
            .Join(db.Companies, membership => membership.CompanyId, company => company.Id, (membership, company) => new { membership, company })
            .OrderBy(item => item.company.Name)
            .Select(item => new CompanyMembershipSnapshot(item.company.Id, item.company.Name, item.company.LegalName, item.company.BaseCurrency, item.membership.Role, item.membership.IsOwner, item.membership.IsActive))
            .ToArrayAsync(cancellationToken);
    }

    private async Task<(Guid UserId, bool IsOwner)?> CurrentActorAsync(BrassLedgerDbContext db, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId)) return null;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        var membership = await db.CompanyMemberships.AsNoTracking().SingleOrDefaultAsync(
            item => item.UserId == userId && item.CompanyId == companyId && item.IsActive, cancellationToken);
        if (user is null || membership is null
            || !await db.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId, cancellationToken)) return null;
        var role = await db.AccessRoles.AsNoTracking().SingleOrDefaultAsync(
            item => item.CompanyId == companyId && item.Name == membership.Role && item.IsActive, cancellationToken);
        if (role is null || (role.RequiresMfa && !user.MfaEnabled)
            || (user.MfaEnabled && !string.Equals(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.AuthenticationMethodClaimType), "mfa", StringComparison.Ordinal))) return null;
        var claimedRole = principal.FindFirstValue(ClaimTypes.Role);
        if (claimedRole is not null && !string.Equals(claimedRole, membership.Role, StringComparison.Ordinal)) return null;
        return (userId, membership.IsOwner);
    }
}
