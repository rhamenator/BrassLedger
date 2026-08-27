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
        var baseCurrency = NormalizeCurrency(request.BaseCurrency); var quoteCurrency = NormalizeCurrency(request.QuoteCurrency); var source = request.Source?.Trim() ?? string.Empty; var sourceReference = request.SourceReference?.Trim() ?? string.Empty;
        if (request.Rate <= 0 || baseCurrency is null || quoteCurrency is null || baseCurrency == quoteCurrency || !Enum.TryParse<CurrencyRateType>(request.RateType, true, out var rateType) || string.IsNullOrWhiteSpace(source) || source.Length > 240 || sourceReference.Length > 1000)
            return TransactionResult.Failure("Provide two different three-letter currencies, a positive rate, a valid rate type, and a concise source.");
        if ((rateType == CurrencyRateType.Average) != request.PeriodStartOn.HasValue || request.PeriodStartOn > request.EffectiveOn)
            return TransactionResult.Failure("An average rate requires a period start on or before its period end; closing and historical rates use one effective date.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsOwner && member.IsActive, cancellationToken)) return TransactionResult.Failure("Only the active-company owner can maintain consolidation exchange rates.");
        var entity = request.Id is { } id ? await db.CurrencyExchangeRates.SingleOrDefaultAsync(rate => rate.CompanyId == companyId && rate.Id == id, cancellationToken) : null;
        if (request.Id is not null && entity is null) return TransactionResult.Failure("The exchange rate was not found in the active company.");
        if (entity is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(entity.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))) return TransactionResult.Failure("The exchange rate changed after it was displayed. Refresh before saving it.");
        if (entity is not null && (entity.BaseCurrency != baseCurrency || entity.QuoteCurrency != quoteCurrency || entity.RateType != rateType || entity.EffectiveOn != request.EffectiveOn || entity.PeriodStartOn != request.PeriodStartOn)) return TransactionResult.Failure("A retained exchange rate cannot be moved to another currency pair, type, or period. Add a separate rate instead.");
        if (request.IsActive && await db.CurrencyExchangeRates.AnyAsync(rate => rate.CompanyId == companyId && rate.Id != request.Id && rate.IsActive && rate.BaseCurrency == quoteCurrency && rate.QuoteCurrency == baseCurrency && rate.RateType == rateType && (rateType != CurrencyRateType.Average || (rate.PeriodStartOn <= request.EffectiveOn && rate.EffectiveOn >= request.PeriodStartOn)), cancellationToken))
            return TransactionResult.Failure("Deactivate the inverse rate series before activating this direction; reports do not silently choose between direct and inverse rates.");
        if (rateType == CurrencyRateType.Average && await db.CurrencyExchangeRates.AnyAsync(rate => rate.CompanyId == companyId && rate.Id != request.Id && rate.IsActive && rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency && rate.RateType == CurrencyRateType.Average && rate.PeriodStartOn <= request.EffectiveOn && rate.EffectiveOn >= request.PeriodStartOn, cancellationToken))
            return TransactionResult.Failure("Active average-rate periods for the same currency pair cannot overlap.");
        entity ??= new CurrencyExchangeRate { Id = Guid.NewGuid(), CompanyId = companyId.Value, BaseCurrency = baseCurrency, QuoteCurrency = quoteCurrency, RateType = rateType, PeriodStartOn = request.PeriodStartOn, EffectiveOn = request.EffectiveOn };
        var previous = entity.Id == request.Id ? new { entity.Rate, entity.Source, entity.SourceReference, entity.RetrievedOn, entity.IsActive } : null;
        entity.Rate = request.Rate; entity.Source = source; entity.SourceReference = sourceReference; entity.RetrievedOn = request.RetrievedOn; entity.IsActive = request.IsActive; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(entity).State == EntityState.Detached) db.CurrencyExchangeRates.Add(entity);
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "currency-exchange-rate.created" : "currency-exchange-rate.updated", EntityType = nameof(CurrencyExchangeRate), EntityId = entity.Id, DetailJson = JsonSerializer.Serialize(new { previous, current = new { entity.BaseCurrency, entity.QuoteCurrency, rateType = entity.RateType.ToString(), entity.PeriodStartOn, entity.EffectiveOn, entity.Rate, entity.Source, entity.SourceReference, entity.RetrievedOn, entity.IsActive } }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The exchange rate changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The exchange rate conflicts with another retained rate for this currency pair, type, and date."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<IReadOnlyList<ExchangeRateSnapshot>> GetExchangeRatesAsync(CancellationToken cancellationToken = default)
    {
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return [];
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsOwner && member.IsActive, cancellationToken)) return [];
        return await db.CurrencyExchangeRates.AsNoTracking().Where(rate => rate.CompanyId == companyId).OrderBy(rate => rate.BaseCurrency).ThenBy(rate => rate.QuoteCurrency).ThenBy(rate => rate.RateType).ThenByDescending(rate => rate.EffectiveOn).Select(rate => new ExchangeRateSnapshot(rate.Id, rate.BaseCurrency, rate.QuoteCurrency, rate.Rate, rate.RateType.ToString(), rate.PeriodStartOn, rate.EffectiveOn, rate.Source, rate.SourceReference, rate.RetrievedOn, rate.IsActive, rate.ConcurrencyToken)).ToArrayAsync(cancellationToken);
    }

    public async Task<TransactionResult> SaveGroupAsync(SaveConsolidationGroupRequest request, CancellationToken cancellationToken = default)
    {
        var reportingCurrency = NormalizeCurrency(request.ReportingCurrency);
        var ctaNumber = request.CtaAccountNumber?.Trim() ?? string.Empty; var ctaName = request.CtaAccountName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.Name) || reportingCurrency is null || ctaNumber.Length > 64 || ctaName.Length > 160 || (string.IsNullOrWhiteSpace(ctaNumber) != string.IsNullOrWhiteSpace(ctaName))) return TransactionResult.Failure("Provide a group name, a three-letter reporting currency, and either both CTA account fields or neither.");
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
        entity.Name = request.Name.Trim(); entity.ReportingCurrency = reportingCurrency; entity.CtaAccountNumber = ctaNumber; entity.CtaAccountName = ctaName; entity.IsActive = request.IsActive; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
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
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-group.created" : "consolidation-group.updated", EntityType = nameof(ConsolidationGroup), EntityId = entity.Id, DetailJson = JsonSerializer.Serialize(new { entity.Name, entity.ReportingCurrency, entity.CtaAccountNumber, entity.CtaAccountName, entity.IsActive, initialOwnershipPeriods = request.Members.Count }), OccurredAtUtc = DateTimeOffset.UtcNow });
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
        var defaultMethod = sourceAccount.Type switch { AccountType.Asset or AccountType.Liability => ConsolidationTranslationMethod.Closing, AccountType.Revenue or AccountType.Expense => ConsolidationTranslationMethod.Average, _ => ConsolidationTranslationMethod.Historical };
        if (!string.IsNullOrWhiteSpace(request.TranslationMethod) && !Enum.TryParse<ConsolidationTranslationMethod>(request.TranslationMethod, true, out _)) return TransactionResult.Failure("Choose Closing, Average, or Historical as the translation method.");
        var translationMethod = string.IsNullOrWhiteSpace(request.TranslationMethod) ? defaultMethod : Enum.Parse<ConsolidationTranslationMethod>(request.TranslationMethod, true);
        var mapping = request.Id is { } id ? await db.ConsolidationAccountMappings.SingleOrDefaultAsync(item => item.Id == id && item.ConsolidationGroupId == group.Id, cancellationToken) : null;
        if (request.Id is not null && mapping is null) return TransactionResult.Failure("The account mapping was not found in this consolidation group.");
        if (mapping is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(mapping.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))) return TransactionResult.Failure("The account mapping changed after it was displayed. Refresh before saving it.");
        if (mapping is not null && (mapping.MemberCompanyId != request.MemberCompanyId || mapping.MemberAccountId != request.MemberAccountId)) return TransactionResult.Failure("A retained mapping cannot be moved to another source account. Add a separate mapping instead.");
        if (await db.ConsolidationAccountMappings.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.MemberCompanyId == request.MemberCompanyId && item.MemberAccountId == request.MemberAccountId && item.Id != request.Id && item.EffectiveFrom <= requestedEnd && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom), cancellationToken))
            return TransactionResult.Failure("Active mappings for the same source account cannot overlap.");
        if (await db.ConsolidationAccountMappings.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.Id != request.Id && item.ReportingAccountNumber == reportingNumber && item.EffectiveFrom <= requestedEnd && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom) && (item.ReportingAccountName != reportingName || item.ReportingAccountType != sourceAccount.Type || item.TranslationMethod != translationMethod), cancellationToken))
            return TransactionResult.Failure("A reporting account number must retain one name, account type, and translation method throughout overlapping mapping periods.");
        mapping ??= new ConsolidationAccountMapping { Id = Guid.NewGuid(), ConsolidationGroupId = group.Id, MemberCompanyId = request.MemberCompanyId, MemberAccountId = request.MemberAccountId };
        mapping.ReportingAccountNumber = reportingNumber; mapping.ReportingAccountName = reportingName; mapping.ReportingAccountType = sourceAccount.Type; mapping.TranslationMethod = translationMethod; mapping.EffectiveFrom = request.EffectiveFrom; mapping.EffectiveThrough = request.EffectiveThrough; mapping.IsActive = request.IsActive; mapping.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(mapping).State == EntityState.Detached) db.ConsolidationAccountMappings.Add(mapping);
        group.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-account-mapping.created" : "consolidation-account-mapping.updated", EntityType = nameof(ConsolidationAccountMapping), EntityId = mapping.Id, DetailJson = JsonSerializer.Serialize(new { group.Id, mapping.MemberCompanyId, mapping.MemberAccountId, mapping.ReportingAccountNumber, mapping.ReportingAccountName, reportingAccountType = mapping.ReportingAccountType.ToString(), translationMethod = mapping.TranslationMethod.ToString(), mapping.EffectiveFrom, mapping.EffectiveThrough, mapping.IsActive }), OccurredAtUtc = DateTimeOffset.UtcNow });
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
            members.Where(member => member.ConsolidationGroupId == group.Id).OrderBy(member => companies[member.MemberCompanyId].Name).ThenBy(member => member.EffectiveFrom).Select(member => new ConsolidationGroupMemberSnapshot(member.Id, member.MemberCompanyId, companies[member.MemberCompanyId].Name, companies[member.MemberCompanyId].BaseCurrency, member.OwnershipPercentage, member.EffectiveFrom, member.EffectiveThrough, member.ConcurrencyToken)).ToArray(), group.CtaAccountNumber, group.CtaAccountName)).ToArray();
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
                return new ConsolidationAccountMappingSnapshot(mapping.Id, mapping.MemberCompanyId, companies[mapping.MemberCompanyId].Name, mapping.MemberAccountId, account.Number, account.Name, account.Type.ToString(), mapping.ReportingAccountNumber, mapping.ReportingAccountName, mapping.ReportingAccountType.ToString(), mapping.TranslationMethod.ToString(), mapping.EffectiveFrom, mapping.EffectiveThrough, mapping.IsActive, mapping.ConcurrencyToken);
            }).ToArray());
    }

    public async Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var companyId = CurrentCompanyId(); if (companyId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var fiscalMonth = await db.Companies.AsNoTracking().Where(company => company.Id == companyId).Select(company => (int?)company.FiscalYearStartMonth).SingleOrDefaultAsync(cancellationToken);
        if (fiscalMonth is not (>= 1 and <= 12)) return null;
        var fiscalYear = asOf.Month < fiscalMonth.Value ? asOf.Year - 1 : asOf.Year;
        return await GetBalanceReportAsync(groupId, new DateOnly(fiscalYear, fiscalMonth.Value, 1), asOf, cancellationToken);
    }

    public async Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        if (periodStart > asOf) return null;
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == groupId && item.IsActive, cancellationToken); if (group is null) return null;
        var members = await db.ConsolidationGroupCompanies.Where(item => item.ConsolidationGroupId == group.Id && item.EffectiveFrom <= asOf && (item.EffectiveThrough == null || item.EffectiveThrough >= asOf)).ToListAsync(cancellationToken); var permitted = await db.CompanyMemberships.Where(item => item.UserId == userId && item.IsActive).Select(item => item.CompanyId).ToListAsync(cancellationToken); if (members.Any(member => !permitted.Contains(member.MemberCompanyId))) return null;
        var companies = await db.Companies.Where(company => members.Select(member => member.MemberCompanyId).Contains(company.Id)).ToDictionaryAsync(company => company.Id, cancellationToken);
        var rates = await db.CurrencyExchangeRates.AsNoTracking().Where(rate => rate.CompanyId == companyId && rate.IsActive && (rate.EffectiveOn <= asOf || (rate.RateType == CurrencyRateType.Average && rate.PeriodStartOn <= asOf))).OrderByDescending(rate => rate.EffectiveOn).ToListAsync(cancellationToken);
        var mappings = await db.ConsolidationAccountMappings.AsNoTracking().Where(mapping => mapping.ConsolidationGroupId == group.Id && mapping.EffectiveFrom <= asOf && (mapping.EffectiveThrough == null || mapping.EffectiveThrough >= asOf)).ToListAsync(cancellationToken);
        var warnings = new List<string>(); var totals = new Dictionary<(string Number, string Name, string Type, string Method), decimal>(); var complete = true;
        foreach (var member in members)
        {
            var company = companies[member.MemberCompanyId];
            decimal sourceSignedBalance = 0m;
            var postedLines = await (from line in db.JournalEntryLines.AsNoTracking()
                                     join journal in db.JournalEntries.AsNoTracking() on line.JournalEntryId equals journal.Id
                                     join account in db.Accounts.AsNoTracking() on line.AccountId equals account.Id
                                     where journal.CompanyId == company.Id
                                           && account.CompanyId == company.Id
                                           && journal.IsPosted
                                           && journal.PostedOn <= asOf
                                     select new { account.Id, account.Number, account.Name, account.Type, journal.PostedOn, line.Debit, line.Credit }).ToListAsync(cancellationToken);
            // SQLite stores decimals as text and cannot translate decimal Sum. Aggregate the bounded,
            // already-filtered posted lines in .NET so SQLite and PostgreSQL use identical arithmetic.
            var accountActivity = postedLines.GroupBy(line => new { line.Id, line.Number, line.Name, line.Type }).Select(activity => new
            {
                activity.Key.Id,
                activity.Key.Number,
                activity.Key.Name,
                activity.Key.Type,
                Lines = activity.ToArray()
            });
            foreach (var account in accountActivity)
            {
                var naturalBalance = account.Lines.Sum(line => NaturalAmount(account.Type, line.Debit, line.Credit));
                var accountMappings = mappings.Where(mapping => mapping.MemberCompanyId == company.Id && mapping.MemberAccountId == account.Id).ToArray();
                if (accountMappings.Length != 1)
                {
                    var relevantUnmappedBalance = account.Type is AccountType.Revenue or AccountType.Expense
                        ? account.Lines.Where(line => line.PostedOn >= periodStart).Sum(line => NaturalAmount(account.Type, line.Debit, line.Credit))
                        : naturalBalance;
                    if (relevantUnmappedBalance == 0m) continue;
                    complete = false;
                    warnings.Add(accountMappings.Length == 0
                        ? $"{company.Name} account {account.Number} · {account.Name} has no active consolidation mapping on {asOf:yyyy-MM-dd}; its {relevantUnmappedBalance:N2} {company.BaseCurrency} balance was excluded."
                        : $"{company.Name} account {account.Number} · {account.Name} has overlapping consolidation mappings on {asOf:yyyy-MM-dd}; its balance was excluded.");
                    continue;
                }
                var accountMapping = accountMappings[0];
                if (accountMapping.ReportingAccountType != account.Type) { complete = false; warnings.Add($"{company.Name} account {account.Number} · {account.Name} has a reporting type inconsistent with its source type; its balance was excluded."); continue; }
                var applicableLines = accountMapping.TranslationMethod == ConsolidationTranslationMethod.Average ? account.Lines.Where(line => line.PostedOn >= periodStart).ToArray() : account.Lines;
                if (applicableLines.Length == 0) continue;
                var applicableNaturalBalance = applicableLines.Sum(line => NaturalAmount(account.Type, line.Debit, line.Credit));
                if (applicableNaturalBalance == 0m) continue;
                sourceSignedBalance += (account.Type is AccountType.Asset or AccountType.Expense ? applicableNaturalBalance : -applicableNaturalBalance) * member.OwnershipPercentage;
                decimal translated = 0m; string? rateError = null;
                if (accountMapping.TranslationMethod == ConsolidationTranslationMethod.Closing)
                {
                    var resolution = ResolveRate(company.BaseCurrency, group.ReportingCurrency, CurrencyRateType.Closing, asOf, rates);
                    rateError = resolution.Error;
                    if (resolution.Factor is { } factor) translated = applicableLines.Sum(line => NaturalAmount(account.Type, line.Debit, line.Credit)) * factor;
                }
                else
                {
                    var requiredType = accountMapping.TranslationMethod == ConsolidationTranslationMethod.Average ? CurrencyRateType.Average : CurrencyRateType.Historical;
                    foreach (var line in applicableLines)
                    {
                        var resolution = ResolveRate(company.BaseCurrency, group.ReportingCurrency, requiredType, line.PostedOn, rates);
                        if (resolution.Factor is null) { rateError = resolution.Error; break; }
                        translated += NaturalAmount(account.Type, line.Debit, line.Credit) * resolution.Factor.Value;
                    }
                }
                if (rateError is not null)
                {
                    complete = false;
                    warnings.Add($"{company.Name} account {account.Number} · {account.Name} requires {accountMapping.TranslationMethod.ToString().ToLowerInvariant()} translation, but {rateError}; its {applicableNaturalBalance:N2} {company.BaseCurrency} report-period balance was excluded.");
                    continue;
                }
                var key = (accountMapping.ReportingAccountNumber, accountMapping.ReportingAccountName, accountMapping.ReportingAccountType.ToString(), accountMapping.TranslationMethod.ToString());
                totals[key] = totals.GetValueOrDefault(key) + decimal.Round(translated * member.OwnershipPercentage, 2, MidpointRounding.AwayFromZero);
            }
            if (decimal.Round(sourceSignedBalance, 2, MidpointRounding.AwayFromZero) != 0m)
            {
                complete = false;
                warnings.Add($"{company.Name}'s selected report-period balances do not balance in {company.BaseCurrency}. Close pre-period nominal activity or choose the correct reporting-period start; CTA was not used to conceal the {sourceSignedBalance:N2} source imbalance.");
            }
        }
        var translationAdjustment = 0m;
        if (complete)
        {
            translationAdjustment = decimal.Round(totals.Sum(item => IsDebitNormal(item.Key.Type) ? item.Value : -item.Value), 2, MidpointRounding.AwayFromZero);
            if (translationAdjustment != 0m)
            {
                if (string.IsNullOrWhiteSpace(group.CtaAccountNumber) || string.IsNullOrWhiteSpace(group.CtaAccountName))
                {
                    warnings.Add($"Translation created a {translationAdjustment:N2} {group.ReportingCurrency} imbalance. Configure a dedicated CTA equity account on the consolidation group before relying on this report.");
                }
                else if (totals.Keys.Any(key => key.Number == group.CtaAccountNumber))
                {
                    warnings.Add($"CTA account {group.CtaAccountNumber} is also used by a source-account mapping. Configure a dedicated reporting account; no CTA was inserted.");
                }
                else
                {
                    totals[(group.CtaAccountNumber, group.CtaAccountName, AccountType.Equity.ToString(), "CTA")] = translationAdjustment;
                }
            }
        }
        else warnings.Add("CTA was not calculated because one or more material source balances were excluded.");
        return new ConsolidatedBalanceReport(group.Id, group.Name, group.ReportingCurrency, periodStart, asOf, totals.OrderBy(item => item.Key.Number).Select(item => new ConsolidatedAccountBalance(item.Key.Number, item.Key.Name, item.Key.Type, item.Value, item.Key.Method)).ToArray(), warnings, translationAdjustment);
    }

    private static RateResolution ResolveRate(string from, string to, CurrencyRateType type, DateOnly on, IReadOnlyList<CurrencyExchangeRate> rates)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return new(1m, null);
        CurrencyExchangeRate? Find(string baseCurrency, string quoteCurrency) => type == CurrencyRateType.Average
            ? rates.Where(rate => rate.Rate > 0m && rate.RateType == type && rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency && rate.PeriodStartOn <= on && rate.EffectiveOn >= on).OrderByDescending(rate => rate.EffectiveOn).FirstOrDefault()
            : rates.Where(rate => rate.Rate > 0m && rate.RateType == type && rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency && rate.EffectiveOn <= on).OrderByDescending(rate => rate.EffectiveOn).FirstOrDefault();
        var direct = Find(from, to); var reverse = Find(to, from);
        if (direct is not null && reverse is not null)
        {
            var inverse = 1m / reverse.Rate;
            if (decimal.Abs(direct.Rate - inverse) > 0.00000001m) return new(null, $"conflicting direct and inverse {type.ToString().ToLowerInvariant()} rates cover {on:yyyy-MM-dd}");
            return new(direct.Rate, null);
        }
        if (direct is not null) return new(direct.Rate, null);
        if (reverse is not null) return new(1m / reverse.Rate, null);
        return new(null, $"no {from}/{to} {type.ToString().ToLowerInvariant()} rate covers {on:yyyy-MM-dd}");
    }
    private static decimal NaturalAmount(AccountType type, decimal debit, decimal credit) => type is AccountType.Asset or AccountType.Expense ? debit - credit : credit - debit;
    private static bool IsDebitNormal(string accountType) => accountType is nameof(AccountType.Asset) or nameof(AccountType.Expense);
    private sealed record RateResolution(decimal? Factor, string? Error);
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
