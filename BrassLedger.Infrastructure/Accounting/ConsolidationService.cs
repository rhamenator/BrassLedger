using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class ConsolidationService(IDbContextFactory<BrassLedgerDbContext> dbContextFactory, IHttpContextAccessor httpContextAccessor) : IConsolidationService
{
    public async Task<TransactionResult> SaveExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rate <= 0 || string.IsNullOrWhiteSpace(request.BaseCurrency) || string.IsNullOrWhiteSpace(request.QuoteCurrency)) return TransactionResult.Failure("Provide two currencies and a positive exchange rate.");
        var companyId = CurrentCompanyId(); if (companyId is null) return TransactionResult.Failure("An active company is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var baseCurrency = request.BaseCurrency.Trim().ToUpperInvariant(); var quoteCurrency = request.QuoteCurrency.Trim().ToUpperInvariant();
        var entity = await db.CurrencyExchangeRates.SingleOrDefaultAsync(rate => rate.CompanyId == companyId && rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency && rate.EffectiveOn == request.EffectiveOn, cancellationToken);
        entity ??= new CurrencyExchangeRate { Id = Guid.NewGuid(), CompanyId = companyId.Value, BaseCurrency = baseCurrency, QuoteCurrency = quoteCurrency, EffectiveOn = request.EffectiveOn };
        entity.Rate = request.Rate; entity.Source = request.Source.Trim(); if (db.Entry(entity).State == EntityState.Detached) db.CurrencyExchangeRates.Add(entity);
        await db.SaveChangesAsync(cancellationToken); return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> SaveGroupAsync(SaveConsolidationGroupRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Members.Count == 0 || request.Members.Any(member => member.CompanyId == Guid.Empty || member.OwnershipPercentage is <= 0 or > 1) || request.Members.Select(member => member.CompanyId).Distinct().Count() != request.Members.Count) return TransactionResult.Failure("Provide a name and distinct member companies with ownership between 0% and 100%.");
        var companyId = CurrentCompanyId(); if (companyId is null) return TransactionResult.Failure("An active company is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userId = CurrentUserId(); if (userId is null || !await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsOwner && member.IsActive, cancellationToken)) return TransactionResult.Failure("Only the active-company owner can maintain consolidation groups.");
        var allowedCompanies = await db.CompanyMemberships.Where(member => member.UserId == userId && member.IsActive).Select(member => member.CompanyId).ToListAsync(cancellationToken);
        if (request.Members.Any(member => !allowedCompanies.Contains(member.CompanyId))) return TransactionResult.Failure("Every consolidated company must be accessible to the current owner.");
        var entity = request.Id is { } id ? await db.ConsolidationGroups.SingleOrDefaultAsync(group => group.CompanyId == companyId && group.Id == id, cancellationToken) : null;
        entity ??= new ConsolidationGroup { Id = Guid.NewGuid(), CompanyId = companyId.Value }; entity.Name = request.Name.Trim(); entity.ReportingCurrency = request.ReportingCurrency.Trim().ToUpperInvariant(); entity.IsActive = request.IsActive;
        if (db.Entry(entity).State == EntityState.Detached) db.ConsolidationGroups.Add(entity);
        var existing = await db.ConsolidationGroupCompanies.Where(member => member.ConsolidationGroupId == entity.Id).ToListAsync(cancellationToken); db.ConsolidationGroupCompanies.RemoveRange(existing);
        db.ConsolidationGroupCompanies.AddRange(request.Members.Select(member => new ConsolidationGroupCompany { Id = Guid.NewGuid(), ConsolidationGroupId = entity.Id, MemberCompanyId = member.CompanyId, OwnershipPercentage = member.OwnershipPercentage }));
        await db.SaveChangesAsync(cancellationToken); return TransactionResult.Success(entity.Id);
    }

    public async Task<IReadOnlyList<ConsolidationGroupSnapshot>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = CurrentCompanyId();
        if (companyId is null) return [];
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var groups = await db.ConsolidationGroups.AsNoTracking().Where(group => group.CompanyId == companyId).OrderBy(group => group.Name).ToListAsync(cancellationToken);
        var groupIds = groups.Select(group => group.Id).ToArray();
        var members = await db.ConsolidationGroupCompanies.AsNoTracking().Where(member => groupIds.Contains(member.ConsolidationGroupId)).ToListAsync(cancellationToken);
        var companyIds = members.Select(member => member.MemberCompanyId).Distinct().ToArray();
        var companies = await db.Companies.AsNoTracking().Where(company => companyIds.Contains(company.Id)).ToDictionaryAsync(company => company.Id, cancellationToken);
        return groups.Select(group => new ConsolidationGroupSnapshot(group.Id, group.Name, group.ReportingCurrency, group.IsActive,
            members.Where(member => member.ConsolidationGroupId == group.Id).OrderBy(member => companies[member.MemberCompanyId].Name).Select(member => new ConsolidationGroupMemberSnapshot(member.MemberCompanyId, companies[member.MemberCompanyId].Name, companies[member.MemberCompanyId].BaseCurrency, member.OwnershipPercentage)).ToArray())).ToArray();
    }

    public async Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == groupId && item.IsActive, cancellationToken); if (group is null) return null;
        var members = await db.ConsolidationGroupCompanies.Where(item => item.ConsolidationGroupId == group.Id).ToListAsync(cancellationToken); var permitted = await db.CompanyMemberships.Where(item => item.UserId == userId && item.IsActive).Select(item => item.CompanyId).ToListAsync(cancellationToken); if (members.Any(member => !permitted.Contains(member.MemberCompanyId))) return null;
        var companies = await db.Companies.Where(company => members.Select(member => member.MemberCompanyId).Contains(company.Id)).ToDictionaryAsync(company => company.Id, cancellationToken);
        var rates = await db.CurrencyExchangeRates.Where(rate => rate.CompanyId == companyId && rate.EffectiveOn <= asOf).OrderByDescending(rate => rate.EffectiveOn).ToListAsync(cancellationToken); var warnings = new List<string>(); var totals = new Dictionary<(string Number, string Name, string Type), decimal>();
        foreach (var member in members)
        {
            var company = companies[member.MemberCompanyId]; var factor = ResolveRate(company.BaseCurrency, group.ReportingCurrency, rates); if (factor is null) { warnings.Add($"No {company.BaseCurrency}/{group.ReportingCurrency} rate is effective for {company.Name} on {asOf:yyyy-MM-dd}."); continue; }
            var accounts = await db.Accounts.Where(account => account.CompanyId == company.Id && account.IsActive).ToListAsync(cancellationToken);
            foreach (var account in accounts) { var key = (account.Number, account.Name, account.Type.ToString()); totals[key] = totals.GetValueOrDefault(key) + decimal.Round(account.CurrentBalance * factor.Value * member.OwnershipPercentage, 2); }
        }
        return new ConsolidatedBalanceReport(group.Id, group.Name, group.ReportingCurrency, asOf, totals.OrderBy(item => item.Key.Number).Select(item => new ConsolidatedAccountBalance(item.Key.Number, item.Key.Name, item.Key.Type, item.Value)).ToArray(), warnings);
    }

    private static decimal? ResolveRate(string from, string to, IReadOnlyList<CurrencyExchangeRate> rates) => string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ? 1m : rates.FirstOrDefault(rate => rate.BaseCurrency == from && rate.QuoteCurrency == to)?.Rate ?? (rates.FirstOrDefault(rate => rate.BaseCurrency == to && rate.QuoteCurrency == from) is { } reverse ? 1m / reverse.Rate : null);
    private Guid? CurrentCompanyId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedger.Infrastructure.Auth.BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId) ? companyId : null;
    private Guid? CurrentUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
