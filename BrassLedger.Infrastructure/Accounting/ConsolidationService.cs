using System.Security.Claims;
using System.Text.Json;
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
        var reportingCurrency = NormalizeCurrency(request.ReportingCurrency);
        if (string.IsNullOrWhiteSpace(request.Name) || reportingCurrency is null) return TransactionResult.Failure("Provide a group name and a three-letter reporting currency.");
        if (request.Id is null && (request.Members.Count == 0 || !ValidOwnershipPeriods(request.Members))) return TransactionResult.Failure("Provide at least one valid, non-overlapping ownership period with ownership above 0% and no more than 100%.");
        if (request.Id is not null && request.Members.Count > 0) return TransactionResult.Failure("Edit ownership periods separately so previously effective ownership is not replaced.");
        var companyId = CurrentCompanyId(); if (companyId is null) return TransactionResult.Failure("An active company is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userId = CurrentUserId(); if (userId is null || !await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsOwner && member.IsActive, cancellationToken)) return TransactionResult.Failure("Only the active-company owner can maintain consolidation groups.");
        var allowedCompanies = await db.CompanyMemberships.Where(member => member.UserId == userId && member.IsOwner && member.IsActive).Select(member => member.CompanyId).ToListAsync(cancellationToken);
        if (request.Members.Any(member => !allowedCompanies.Contains(member.CompanyId))) return TransactionResult.Failure("The current user must be an active owner of every consolidated company.");
        var entity = request.Id is { } id ? await db.ConsolidationGroups.SingleOrDefaultAsync(group => group.CompanyId == companyId && group.Id == id, cancellationToken) : null;
        if (request.Id is not null && entity is null) return TransactionResult.Failure("The consolidation group was not found in the active company.");
        if (entity is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(entity.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))) return TransactionResult.Failure("The consolidation group changed after it was displayed. Refresh before saving it.");
        entity ??= new ConsolidationGroup { Id = Guid.NewGuid(), CompanyId = companyId.Value };
        entity.Name = request.Name.Trim(); entity.ReportingCurrency = reportingCurrency; entity.IsActive = request.IsActive; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(entity).State == EntityState.Detached)
        {
            db.ConsolidationGroups.Add(entity);
            db.ConsolidationGroupCompanies.AddRange(request.Members.Select(member => new ConsolidationGroupCompany
            {
                Id = Guid.NewGuid(), ConsolidationGroupId = entity.Id, MemberCompanyId = member.CompanyId,
                OwnershipPercentage = member.OwnershipPercentage, EffectiveFrom = member.EffectiveFrom ?? DateOnly.MinValue,
                EffectiveThrough = member.EffectiveThrough, ConcurrencyToken = Guid.NewGuid().ToString("N")
            }));
        }
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-group.created" : "consolidation-group.updated", EntityType = nameof(ConsolidationGroup), EntityId = entity.Id, DetailJson = JsonSerializer.Serialize(new { entity.Name, entity.ReportingCurrency, entity.IsActive, initialOwnershipPeriods = request.Members.Count }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation group changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The consolidation group name or ownership schedule conflicts with another retained record."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> SaveOwnershipPeriodAsync(SaveConsolidationOwnershipPeriodRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ConsolidationGroupId == Guid.Empty || request.MemberCompanyId == Guid.Empty || request.OwnershipPercentage is <= 0 or > 1 || request.EffectiveThrough < request.EffectiveFrom)
            return TransactionResult.Failure("Provide a member company, ownership above 0% and no more than 100%, and a valid effective period.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.Id == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (group is null) return TransactionResult.Failure("The consolidation group was not found in the active company.");
        if (!await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsOwner && member.IsActive, cancellationToken)
            || !await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == request.MemberCompanyId && member.IsOwner && member.IsActive, cancellationToken))
            return TransactionResult.Failure("The current user must be an active owner of the consolidation group and member company.");
        var period = request.Id is { } id ? await db.ConsolidationGroupCompanies.SingleOrDefaultAsync(item => item.Id == id && item.ConsolidationGroupId == group.Id, cancellationToken) : null;
        if (request.Id is not null && period is null) return TransactionResult.Failure("The ownership period was not found in this consolidation group.");
        if (period is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(period.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))) return TransactionResult.Failure("The ownership period changed after it was displayed. Refresh before saving it.");
        if (period is not null && period.MemberCompanyId != request.MemberCompanyId) return TransactionResult.Failure("An ownership period cannot be moved to another company. Add a separate period for that company.");
        var requestedEnd = request.EffectiveThrough ?? DateOnly.MaxValue;
        var overlaps = await db.ConsolidationGroupCompanies.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.MemberCompanyId == request.MemberCompanyId && item.Id != request.Id && item.EffectiveFrom <= requestedEnd && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom), cancellationToken);
        if (overlaps) return TransactionResult.Failure("Ownership periods for the same company cannot overlap.");
        period ??= new ConsolidationGroupCompany { Id = Guid.NewGuid(), ConsolidationGroupId = group.Id };
        period.MemberCompanyId = request.MemberCompanyId; period.OwnershipPercentage = request.OwnershipPercentage; period.EffectiveFrom = request.EffectiveFrom; period.EffectiveThrough = request.EffectiveThrough; period.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(period).State == EntityState.Detached) db.ConsolidationGroupCompanies.Add(period);
        group.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-ownership.created" : "consolidation-ownership.updated", EntityType = nameof(ConsolidationGroupCompany), EntityId = period.Id, DetailJson = JsonSerializer.Serialize(new { group.Id, period.MemberCompanyId, period.OwnershipPercentage, period.EffectiveFrom, period.EffectiveThrough }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation group or ownership period changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The ownership period conflicts with another retained record."); }
        return TransactionResult.Success(period.Id);
    }

    public async Task<TransactionResult> SaveAccountMappingAsync(SaveConsolidationAccountMappingRequest request, CancellationToken cancellationToken = default)
    {
        var reportingNumber = request.ReportingAccountNumber?.Trim() ?? string.Empty; var reportingName = request.ReportingAccountName?.Trim() ?? string.Empty;
        if (request.ConsolidationGroupId == Guid.Empty || request.MemberCompanyId == Guid.Empty || request.MemberAccountId == Guid.Empty || string.IsNullOrWhiteSpace(reportingNumber) || reportingNumber.Length > 64 || string.IsNullOrWhiteSpace(reportingName) || reportingName.Length > 160 || request.EffectiveThrough < request.EffectiveFrom || (!request.IsActive && !request.EffectiveThrough.HasValue))
            return TransactionResult.Failure("Provide a source account, reporting account number and name, and valid effective period; an inactive mapping requires an effective-through date so historical reports remain reproducible.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.Id == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (group is null) return TransactionResult.Failure("The consolidation group was not found in the active company.");
        if (!await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsOwner && member.IsActive, cancellationToken)
            || !await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == request.MemberCompanyId && member.IsOwner && member.IsActive, cancellationToken))
            return TransactionResult.Failure("The current user must be an active owner of the consolidation group and member company.");
        var requestedEnd = request.EffectiveThrough ?? DateOnly.MaxValue;
        if (!await db.ConsolidationGroupCompanies.AnyAsync(period => period.ConsolidationGroupId == group.Id && period.MemberCompanyId == request.MemberCompanyId && period.EffectiveFrom <= requestedEnd && (period.EffectiveThrough == null || period.EffectiveThrough >= request.EffectiveFrom), cancellationToken))
            return TransactionResult.Failure("The mapping period must overlap retained ownership of the member company.");
        var sourceAccount = await db.Accounts.SingleOrDefaultAsync(account => account.Id == request.MemberAccountId && account.CompanyId == request.MemberCompanyId, cancellationToken);
        if (sourceAccount is null) return TransactionResult.Failure("The source account was not found in the selected member company.");
        var mapping = request.Id is { } id ? await db.ConsolidationAccountMappings.SingleOrDefaultAsync(item => item.Id == id && item.ConsolidationGroupId == group.Id, cancellationToken) : null;
        if (request.Id is not null && mapping is null) return TransactionResult.Failure("The account mapping was not found in this consolidation group.");
        if (mapping is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(mapping.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))) return TransactionResult.Failure("The account mapping changed after it was displayed. Refresh before saving it.");
        if (mapping is not null && (mapping.MemberCompanyId != request.MemberCompanyId || mapping.MemberAccountId != request.MemberAccountId)) return TransactionResult.Failure("A retained mapping cannot be moved to another source account. Add a separate mapping instead.");
        if (await db.ConsolidationAccountMappings.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.MemberCompanyId == request.MemberCompanyId && item.MemberAccountId == request.MemberAccountId && item.Id != request.Id && item.EffectiveFrom <= requestedEnd && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom), cancellationToken))
            return TransactionResult.Failure("Active mappings for the same source account cannot overlap.");
        if (await db.ConsolidationAccountMappings.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.Id != request.Id && item.ReportingAccountNumber == reportingNumber && item.EffectiveFrom <= requestedEnd && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom) && (item.ReportingAccountName != reportingName || item.ReportingAccountType != sourceAccount.Type), cancellationToken))
            return TransactionResult.Failure("A reporting account number must retain one name and account type throughout overlapping mapping periods.");
        mapping ??= new ConsolidationAccountMapping { Id = Guid.NewGuid(), ConsolidationGroupId = group.Id, MemberCompanyId = request.MemberCompanyId, MemberAccountId = request.MemberAccountId };
        mapping.ReportingAccountNumber = reportingNumber; mapping.ReportingAccountName = reportingName; mapping.ReportingAccountType = sourceAccount.Type; mapping.EffectiveFrom = request.EffectiveFrom; mapping.EffectiveThrough = request.EffectiveThrough; mapping.IsActive = request.IsActive; mapping.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(mapping).State == EntityState.Detached) db.ConsolidationAccountMappings.Add(mapping);
        group.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-account-mapping.created" : "consolidation-account-mapping.updated", EntityType = nameof(ConsolidationAccountMapping), EntityId = mapping.Id, DetailJson = JsonSerializer.Serialize(new { group.Id, mapping.MemberCompanyId, mapping.MemberAccountId, mapping.ReportingAccountNumber, mapping.ReportingAccountName, reportingAccountType = mapping.ReportingAccountType.ToString(), mapping.EffectiveFrom, mapping.EffectiveThrough, mapping.IsActive }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation group or account mapping changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The account mapping conflicts with another retained mapping."); }
        return TransactionResult.Success(mapping.Id);
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
        return groups.Select(group => new ConsolidationGroupSnapshot(group.Id, group.Name, group.ReportingCurrency, group.IsActive, group.ConcurrencyToken,
            members.Where(member => member.ConsolidationGroupId == group.Id).OrderBy(member => companies[member.MemberCompanyId].Name).ThenBy(member => member.EffectiveFrom).Select(member => new ConsolidationGroupMemberSnapshot(member.Id, member.MemberCompanyId, companies[member.MemberCompanyId].Name, companies[member.MemberCompanyId].BaseCurrency, member.OwnershipPercentage, member.EffectiveFrom, member.EffectiveThrough, member.ConcurrencyToken)).ToArray())).ToArray();
    }

    public async Task<ConsolidationAccountMappingWorkspace?> GetAccountMappingWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == groupId && item.CompanyId == companyId, cancellationToken); if (group is null) return null;
        var memberCompanyIds = await db.ConsolidationGroupCompanies.AsNoTracking().Where(period => period.ConsolidationGroupId == group.Id).Select(period => period.MemberCompanyId).Distinct().ToArrayAsync(cancellationToken);
        var ownedCompanyIds = await db.CompanyMemberships.AsNoTracking().Where(member => member.UserId == userId && member.IsOwner && member.IsActive).Select(member => member.CompanyId).ToArrayAsync(cancellationToken);
        if (!ownedCompanyIds.Contains(companyId.Value) || memberCompanyIds.Any(memberCompanyId => !ownedCompanyIds.Contains(memberCompanyId))) return null;
        var companies = await db.Companies.AsNoTracking().Where(company => memberCompanyIds.Contains(company.Id)).ToDictionaryAsync(company => company.Id, cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().Where(account => memberCompanyIds.Contains(account.CompanyId)).OrderBy(account => account.CompanyId).ThenBy(account => account.Number).ToListAsync(cancellationToken);
        var mappings = await db.ConsolidationAccountMappings.AsNoTracking().Where(mapping => mapping.ConsolidationGroupId == group.Id).OrderBy(mapping => mapping.ReportingAccountNumber).ThenBy(mapping => mapping.EffectiveFrom).ToListAsync(cancellationToken);
        return new ConsolidationAccountMappingWorkspace(group.Id, group.Name,
            accounts.Select(account => new ConsolidationSourceAccountSnapshot(account.CompanyId, companies[account.CompanyId].Name, account.Id, account.Number, account.Name, account.Type.ToString())).ToArray(),
            mappings.Select(mapping =>
            {
                var account = accounts.Single(item => item.Id == mapping.MemberAccountId && item.CompanyId == mapping.MemberCompanyId);
                return new ConsolidationAccountMappingSnapshot(mapping.Id, mapping.MemberCompanyId, companies[mapping.MemberCompanyId].Name, mapping.MemberAccountId, account.Number, account.Name, account.Type.ToString(), mapping.ReportingAccountNumber, mapping.ReportingAccountName, mapping.ReportingAccountType.ToString(), mapping.EffectiveFrom, mapping.EffectiveThrough, mapping.IsActive, mapping.ConcurrencyToken);
            }).ToArray());
    }

    public async Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == groupId && item.IsActive, cancellationToken); if (group is null) return null;
        var members = await db.ConsolidationGroupCompanies.Where(item => item.ConsolidationGroupId == group.Id && item.EffectiveFrom <= asOf && (item.EffectiveThrough == null || item.EffectiveThrough >= asOf)).ToListAsync(cancellationToken); var permitted = await db.CompanyMemberships.Where(item => item.UserId == userId && item.IsActive).Select(item => item.CompanyId).ToListAsync(cancellationToken); if (members.Any(member => !permitted.Contains(member.MemberCompanyId))) return null;
        var companies = await db.Companies.Where(company => members.Select(member => member.MemberCompanyId).Contains(company.Id)).ToDictionaryAsync(company => company.Id, cancellationToken);
        var rates = await db.CurrencyExchangeRates.Where(rate => rate.CompanyId == companyId && rate.EffectiveOn <= asOf).OrderByDescending(rate => rate.EffectiveOn).ToListAsync(cancellationToken); var mappings = await db.ConsolidationAccountMappings.AsNoTracking().Where(mapping => mapping.ConsolidationGroupId == group.Id && mapping.EffectiveFrom <= asOf && (mapping.EffectiveThrough == null || mapping.EffectiveThrough >= asOf)).ToListAsync(cancellationToken); var warnings = new List<string>(); var totals = new Dictionary<(string Number, string Name, string Type), decimal>();
        foreach (var member in members)
        {
            var company = companies[member.MemberCompanyId]; var factor = ResolveRate(company.BaseCurrency, group.ReportingCurrency, rates); if (factor is null) { warnings.Add($"No {company.BaseCurrency}/{group.ReportingCurrency} rate is effective for {company.Name} on {asOf:yyyy-MM-dd}."); continue; }
            var postedLines = await (from line in db.JournalEntryLines.AsNoTracking()
                                     join journal in db.JournalEntries.AsNoTracking() on line.JournalEntryId equals journal.Id
                                     join account in db.Accounts.AsNoTracking() on line.AccountId equals account.Id
                                     where journal.CompanyId == company.Id
                                           && account.CompanyId == company.Id
                                           && journal.IsPosted
                                           && journal.PostedOn <= asOf
                                     select new { account.Id, account.Number, account.Name, account.Type, line.Debit, line.Credit }).ToListAsync(cancellationToken);
            // SQLite stores decimals as text and cannot translate decimal Sum. Aggregate the bounded,
            // already-filtered posted lines in .NET so SQLite and PostgreSQL use identical arithmetic.
            var accountActivity = postedLines.GroupBy(line => new { line.Id, line.Number, line.Name, line.Type }).Select(activity => new
            {
                activity.Key.Id,
                activity.Key.Number,
                activity.Key.Name,
                activity.Key.Type,
                Debit = activity.Sum(line => line.Debit),
                Credit = activity.Sum(line => line.Credit)
            });
            foreach (var account in accountActivity)
            {
                var naturalBalance = account.Type is AccountType.Asset or AccountType.Expense
                    ? account.Debit - account.Credit
                    : account.Credit - account.Debit;
                if (naturalBalance == 0m) continue;
                var accountMappings = mappings.Where(mapping => mapping.MemberCompanyId == company.Id && mapping.MemberAccountId == account.Id).ToArray();
                if (accountMappings.Length != 1)
                {
                    warnings.Add(accountMappings.Length == 0
                        ? $"{company.Name} account {account.Number} · {account.Name} has no active consolidation mapping on {asOf:yyyy-MM-dd}; its {naturalBalance:N2} {company.BaseCurrency} balance was excluded."
                        : $"{company.Name} account {account.Number} · {account.Name} has overlapping consolidation mappings on {asOf:yyyy-MM-dd}; its balance was excluded.");
                    continue;
                }
                var accountMapping = accountMappings[0];
                if (accountMapping.ReportingAccountType != account.Type) { warnings.Add($"{company.Name} account {account.Number} · {account.Name} has a reporting type inconsistent with its source type; its balance was excluded."); continue; }
                var key = (accountMapping.ReportingAccountNumber, accountMapping.ReportingAccountName, accountMapping.ReportingAccountType.ToString());
                totals[key] = totals.GetValueOrDefault(key) + decimal.Round(naturalBalance * factor.Value * member.OwnershipPercentage, 2, MidpointRounding.AwayFromZero);
            }
        }
        return new ConsolidatedBalanceReport(group.Id, group.Name, group.ReportingCurrency, asOf, totals.OrderBy(item => item.Key.Number).Select(item => new ConsolidatedAccountBalance(item.Key.Number, item.Key.Name, item.Key.Type, item.Value)).ToArray(), warnings);
    }

    private static decimal? ResolveRate(string from, string to, IReadOnlyList<CurrencyExchangeRate> rates) => string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ? 1m : rates.FirstOrDefault(rate => rate.BaseCurrency == from && rate.QuoteCurrency == to)?.Rate ?? (rates.FirstOrDefault(rate => rate.BaseCurrency == to && rate.QuoteCurrency == from) is { } reverse ? 1m / reverse.Rate : null);
    private static string? NormalizeCurrency(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().ToUpperInvariant() is { Length: 3 } currency && currency.All(character => character is >= 'A' and <= 'Z') ? currency : null;
    private static bool ValidOwnershipPeriods(IReadOnlyList<ConsolidationMemberRequest> members) =>
        members.All(member => member.CompanyId != Guid.Empty && member.OwnershipPercentage is > 0 and <= 1 && (!member.EffectiveThrough.HasValue || member.EffectiveThrough.Value >= (member.EffectiveFrom ?? DateOnly.MinValue)))
        && members.GroupBy(member => member.CompanyId).All(group =>
        {
            var ordered = group.OrderBy(member => member.EffectiveFrom ?? DateOnly.MinValue).ToArray();
            return ordered.Zip(ordered.Skip(1), (left, right) => (left.EffectiveThrough ?? DateOnly.MaxValue) < (right.EffectiveFrom ?? DateOnly.MinValue)).All(value => value);
        });
    private Guid? CurrentCompanyId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedger.Infrastructure.Auth.BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId) ? companyId : null;
    private Guid? CurrentUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
