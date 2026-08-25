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
        var userId = CurrentUserId(); if (userId is null) return CompanyManagementResult.Failure("An authenticated owner is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.CompanyMemberships.AnyAsync(membership => membership.UserId == userId && membership.IsOwner && membership.IsActive, cancellationToken)) return CompanyManagementResult.Failure("Only a company owner can create another company.");
        var name = request.Name.Trim(); if (await db.Companies.AnyAsync(company => company.Name == name, cancellationToken)) return CompanyManagementResult.Failure("A company with that name already exists.");
        var company = new Company { Id = Guid.NewGuid(), Name = name, LegalName = request.LegalName.Trim(), TaxId = request.TaxId.Trim(), BaseCurrency = string.IsNullOrWhiteSpace(request.BaseCurrency) ? "USD" : request.BaseCurrency.Trim().ToUpperInvariant(), FiscalYearStartMonth = request.FiscalYearStartMonth };
        db.Companies.Add(company);
        await SecurityAdministrationService.EnsureBuiltInRolesAsync(db, company.Id, cancellationToken);
        var adminRole = await db.AccessRoles.SingleAsync(role => role.CompanyId == company.Id && role.Name == "Administrator", cancellationToken);
        db.CompanyMemberships.Add(new CompanyMembership { Id = Guid.NewGuid(), UserId = userId.Value, CompanyId = company.Id, Role = adminRole.Name, IsOwner = true, IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow });
        var accounts = DefaultAccountingSetup.CreateAccounts(company.Id); db.Accounts.AddRange(accounts); db.BankAccounts.Add(DefaultAccountingSetup.CreateOperatingBankAccount(company.Id, accounts.Single(account => account.Number == "1000").Id));
        await db.SaveChangesAsync(cancellationToken); return CompanyManagementResult.Success(company.Id);
    }

    public async Task<IReadOnlyList<CompanyMembershipSnapshot>> GetMyCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId(); if (userId is null) return [];
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CompanyMemberships.Where(membership => membership.UserId == userId && membership.IsActive)
            .Join(db.Companies, membership => membership.CompanyId, company => company.Id, (membership, company) => new { membership, company })
            .OrderBy(item => item.company.Name)
            .Select(item => new CompanyMembershipSnapshot(item.company.Id, item.company.Name, item.company.LegalName, item.company.BaseCurrency, item.membership.Role, item.membership.IsOwner, item.membership.IsActive))
            .ToArrayAsync(cancellationToken);
    }

    private Guid? CurrentUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
