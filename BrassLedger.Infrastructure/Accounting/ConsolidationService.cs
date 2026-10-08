using System.Security.Claims;
using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class ConsolidationService(IDbContextFactory<BrassLedgerDbContext> dbContextFactory, IHttpContextAccessor httpContextAccessor) : IConsolidationService
{
    public async Task<TransactionResult> SaveExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken cancellationToken = default)
    {
        var baseCurrency = NormalizeCurrency(request.BaseCurrency); var quoteCurrency = NormalizeCurrency(request.QuoteCurrency); var source = request.Source?.Trim() ?? string.Empty; var sourceReference = request.SourceReference?.Trim() ?? string.Empty;
        if (request.Rate <= 0 || baseCurrency is null || quoteCurrency is null || baseCurrency == quoteCurrency || !Enum.TryParse<CurrencyRateType>(request.RateType, true, out var rateType) || !Enum.IsDefined(rateType) || string.IsNullOrWhiteSpace(source) || source.Length > 240 || sourceReference.Length > 1000)
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
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsActive, cancellationToken)) return [];
        return await db.CurrencyExchangeRates.AsNoTracking().Where(rate => rate.CompanyId == companyId).OrderBy(rate => rate.BaseCurrency).ThenBy(rate => rate.QuoteCurrency).ThenBy(rate => rate.RateType).ThenByDescending(rate => rate.EffectiveOn).Select(rate => new ExchangeRateSnapshot(rate.Id, rate.BaseCurrency, rate.QuoteCurrency, rate.Rate, rate.RateType.ToString(), rate.PeriodStartOn, rate.EffectiveOn, rate.Source, rate.SourceReference, rate.RetrievedOn, rate.IsActive, rate.ConcurrencyToken)).ToArrayAsync(cancellationToken);
    }

    public async Task<TransactionResult> SaveGroupAsync(SaveConsolidationGroupRequest request, CancellationToken cancellationToken = default)
    {
        var reportingCurrency = NormalizeCurrency(request.ReportingCurrency);
        var ctaNumber = request.CtaAccountNumber?.Trim() ?? string.Empty; var ctaName = request.CtaAccountName?.Trim() ?? string.Empty;
        var nciNumber = request.NciAccountNumber?.Trim() ?? string.Empty; var nciName = request.NciAccountName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.Name) || reportingCurrency is null || ctaNumber.Length > 64 || ctaName.Length > 160 || nciNumber.Length > 64 || nciName.Length > 160
            || (string.IsNullOrWhiteSpace(ctaNumber) != string.IsNullOrWhiteSpace(ctaName)) || (string.IsNullOrWhiteSpace(nciNumber) != string.IsNullOrWhiteSpace(nciName))
            || (!string.IsNullOrWhiteSpace(ctaNumber) && ctaNumber == nciNumber)) return TransactionResult.Failure("Provide a group name, a three-letter reporting currency, paired CTA and NCI account fields, and different CTA and NCI account numbers.");
        if (request.Id is null && (request.Members.Count == 0 || !ValidOwnershipPeriods(request.Members))) return TransactionResult.Failure("Provide at least one valid, non-overlapping ownership period with ownership above 0% and no more than 100%.");
        if (request.Id is not null && request.Members.Count > 0) return TransactionResult.Failure("Edit ownership periods separately so previously effective ownership is not replaced.");
        var companyId = CurrentCompanyId(); if (companyId is null) return TransactionResult.Failure("An active company is required.");
        var parsedMembers = new List<(ConsolidationMemberRequest Request, ConsolidationBasis Basis)>();
        foreach (var member in request.Members)
        {
            if (!TryConsolidationBasis(member.ConsolidationBasis, out var basis) || !ValidBasisEvidence(basis, member.BasisRationale, member.BasisReviewedOn))
                return TransactionResult.Failure("Choose a supported consolidation basis and retain a concise rationale and review date for every controlled subsidiary, combined affiliate, or newly classified proportionate interest.");
            parsedMembers.Add((member, basis));
        }
        var initialSchedule = parsedMembers.Select(member => new ConsolidationPeriodPolicy(member.Request.CompanyId, member.Request.OwnershipPercentage, member.Basis,
            member.Request.BasisRationale ?? string.Empty, member.Request.BasisReviewedOn, member.Request.EffectiveFrom ?? DateOnly.MinValue, member.Request.EffectiveThrough)).ToArray();
        if (!ValidClassifiedGroupSchedule(initialSchedule, companyId.Value))
            return TransactionResult.Failure("Every effective classified consolidation schedule requires exactly one 100% reporting-parent period for the active company.");
        if (parsedMembers.Any(member => member.Basis == ConsolidationBasis.ControlledSubsidiary && member.Request.OwnershipPercentage < 1m) && string.IsNullOrWhiteSpace(nciNumber))
            return TransactionResult.Failure("Configure a dedicated NCI equity reporting account before adding a partially owned controlled subsidiary.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userId = CurrentUserId(); if (userId is null || !await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsOwner && member.IsActive, cancellationToken)) return TransactionResult.Failure("Only the active-company owner can maintain consolidation groups.");
        var allowedCompanies = await db.CompanyMemberships.Where(member => member.UserId == userId && member.IsOwner && member.IsActive).Select(member => member.CompanyId).ToListAsync(cancellationToken);
        if (request.Members.Any(member => !allowedCompanies.Contains(member.CompanyId))) return TransactionResult.Failure("The current user must be an active owner of every consolidated company.");
        var entity = request.Id is { } id ? await db.ConsolidationGroups.SingleOrDefaultAsync(group => group.CompanyId == companyId && group.Id == id, cancellationToken) : null;
        if (request.Id is not null && entity is null) return TransactionResult.Failure("The consolidation group was not found in the active company.");
        if (entity is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(entity.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))) return TransactionResult.Failure("The consolidation group changed after it was displayed. Refresh before saving it.");
        if (entity is not null && (entity.NciAccountNumber != nciNumber || entity.NciAccountName != nciName) && await db.ConsolidationAdjustmentBatches.AnyAsync(batch => batch.ConsolidationGroupId == entity.Id && batch.Kind == ConsolidationAdjustmentKind.NoncontrollingInterest, cancellationToken))
            return TransactionResult.Failure("The NCI reporting account cannot change after an NCI reclassification is retained. Create a successor consolidation policy instead.");
        if (entity is not null && string.IsNullOrWhiteSpace(nciNumber) && await db.ConsolidationGroupCompanies.AnyAsync(member => member.ConsolidationGroupId == entity.Id && member.ConsolidationBasis == ConsolidationBasis.ControlledSubsidiary && member.OwnershipPercentage < 1m, cancellationToken))
            return TransactionResult.Failure("The NCI reporting account is required while a partially owned controlled-subsidiary period is retained.");
        if (entity is not null && !string.IsNullOrWhiteSpace(nciNumber) && await db.ConsolidationAccountMappings.AnyAsync(mapping => mapping.ConsolidationGroupId == entity.Id && mapping.ReportingAccountNumber == nciNumber, cancellationToken))
            return TransactionResult.Failure("The NCI reporting account must be dedicated and cannot also be used by a source-account mapping.");
        entity ??= new ConsolidationGroup { Id = Guid.NewGuid(), CompanyId = companyId.Value };
        entity.Name = request.Name.Trim(); entity.ReportingCurrency = reportingCurrency; entity.CtaAccountNumber = ctaNumber; entity.CtaAccountName = ctaName; entity.NciAccountNumber = nciNumber; entity.NciAccountName = nciName; entity.IsActive = request.IsActive; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(entity).State == EntityState.Detached)
        {
            db.ConsolidationGroups.Add(entity);
            db.ConsolidationGroupCompanies.AddRange(parsedMembers.Select(member => new ConsolidationGroupCompany
            {
                Id = Guid.NewGuid(), ConsolidationGroupId = entity.Id, MemberCompanyId = member.Request.CompanyId,
                OwnershipPercentage = member.Request.OwnershipPercentage, ConsolidationBasis = member.Basis,
                BasisRationale = member.Request.BasisRationale?.Trim() ?? string.Empty, BasisReviewedOn = member.Request.BasisReviewedOn,
                EffectiveFrom = member.Request.EffectiveFrom ?? DateOnly.MinValue,
                EffectiveThrough = member.Request.EffectiveThrough, ConcurrencyToken = Guid.NewGuid().ToString("N")
            }));
        }
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-group.created" : "consolidation-group.updated", EntityType = nameof(ConsolidationGroup), EntityId = entity.Id, DetailJson = JsonSerializer.Serialize(new { entity.Name, entity.ReportingCurrency, entity.CtaAccountNumber, entity.CtaAccountName, entity.NciAccountNumber, entity.NciAccountName, entity.IsActive, initialOwnershipPeriods = request.Members.Count }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation group changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The consolidation group name or ownership schedule conflicts with another retained record."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> SaveOwnershipPeriodAsync(SaveConsolidationOwnershipPeriodRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ConsolidationGroupId == Guid.Empty || request.MemberCompanyId == Guid.Empty || request.OwnershipPercentage is <= 0 or > 1 || request.EffectiveThrough < request.EffectiveFrom
            || !TryConsolidationBasis(request.ConsolidationBasis, out var basis) || !ValidBasisEvidence(basis, request.BasisRationale, request.BasisReviewedOn))
            return TransactionResult.Failure("Provide a member company, ownership above 0% and no more than 100%, a valid effective period, and reviewed consolidation-basis evidence.");
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
        if (basis == ConsolidationBasis.ReportingParent && (request.MemberCompanyId != companyId || request.OwnershipPercentage != 1m)) return TransactionResult.Failure("The reporting-parent basis is reserved for a 100% period of the active parent company.");
        if (basis == ConsolidationBasis.ControlledSubsidiary && request.MemberCompanyId == companyId) return TransactionResult.Failure("Classify the active parent company as ReportingParent, not ControlledSubsidiary.");
        if (basis == ConsolidationBasis.ControlledSubsidiary && request.OwnershipPercentage < 1m && string.IsNullOrWhiteSpace(group.NciAccountNumber)) return TransactionResult.Failure("Configure the group's dedicated NCI equity reporting account before adding a partially owned controlled subsidiary.");
        var requestedEnd = request.EffectiveThrough ?? DateOnly.MaxValue;
        var overlaps = await db.ConsolidationGroupCompanies.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.MemberCompanyId == request.MemberCompanyId && item.Id != request.Id && item.EffectiveFrom <= requestedEnd && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom), cancellationToken);
        if (overlaps) return TransactionResult.Failure("Ownership periods for the same company cannot overlap.");
        var proposedSchedule = (await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id && item.Id != request.Id).ToListAsync(cancellationToken))
            .Select(item => new ConsolidationPeriodPolicy(item.MemberCompanyId, item.OwnershipPercentage, item.ConsolidationBasis, item.BasisRationale, item.BasisReviewedOn, item.EffectiveFrom, item.EffectiveThrough)).ToList();
        proposedSchedule.Add(new(request.MemberCompanyId, request.OwnershipPercentage, basis, request.BasisRationale ?? string.Empty, request.BasisReviewedOn, request.EffectiveFrom, request.EffectiveThrough));
        if (!ValidClassifiedGroupSchedule(proposedSchedule, companyId.Value, request.EffectiveFrom, request.EffectiveThrough))
            return TransactionResult.Failure("Every effective classified consolidation schedule requires exactly one 100% reporting-parent period for the active company.");
        period ??= new ConsolidationGroupCompany { Id = Guid.NewGuid(), ConsolidationGroupId = group.Id };
        period.MemberCompanyId = request.MemberCompanyId; period.OwnershipPercentage = request.OwnershipPercentage; period.ConsolidationBasis = basis; period.BasisRationale = request.BasisRationale?.Trim() ?? string.Empty; period.BasisReviewedOn = request.BasisReviewedOn; period.EffectiveFrom = request.EffectiveFrom; period.EffectiveThrough = request.EffectiveThrough; period.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(period).State == EntityState.Detached) db.ConsolidationGroupCompanies.Add(period);
        group.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-ownership.created" : "consolidation-ownership.updated", EntityType = nameof(ConsolidationGroupCompany), EntityId = period.Id, DetailJson = JsonSerializer.Serialize(new { group.Id, period.MemberCompanyId, period.OwnershipPercentage, consolidationBasis = period.ConsolidationBasis.ToString(), period.BasisRationale, period.BasisReviewedOn, period.EffectiveFrom, period.EffectiveThrough }), OccurredAtUtc = DateTimeOffset.UtcNow });
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
        if (reportingNumber == group.CtaAccountNumber || reportingNumber == group.NciAccountNumber)
            return TransactionResult.Failure("CTA and NCI reporting accounts are system-controlled and cannot be assigned to a source-account mapping.");
        if (!await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == companyId && member.IsOwner && member.IsActive, cancellationToken)
            || !await db.CompanyMemberships.AnyAsync(member => member.UserId == userId && member.CompanyId == request.MemberCompanyId && member.IsOwner && member.IsActive, cancellationToken))
            return TransactionResult.Failure("The current user must be an active owner of the consolidation group and member company.");
        var requestedEnd = request.EffectiveThrough ?? DateOnly.MaxValue;
        if (!await db.ConsolidationGroupCompanies.AnyAsync(period => period.ConsolidationGroupId == group.Id && period.MemberCompanyId == request.MemberCompanyId && period.EffectiveFrom <= requestedEnd && (period.EffectiveThrough == null || period.EffectiveThrough >= request.EffectiveFrom), cancellationToken))
            return TransactionResult.Failure("The mapping period must overlap retained ownership of the member company.");
        var sourceAccount = await db.Accounts.SingleOrDefaultAsync(account => account.Id == request.MemberAccountId && account.CompanyId == request.MemberCompanyId, cancellationToken);
        if (sourceAccount is null) return TransactionResult.Failure("The source account was not found in the selected member company.");
        if (!Enum.TryParse<ConsolidationCashFlowActivity>(request.CashFlowActivity, true, out var cashFlowActivity) || !Enum.IsDefined(cashFlowActivity))
            return TransactionResult.Failure("Choose Unclassified, Operating, Investing, or Financing as the cash-flow activity.");
        var cashFlowRationale = request.CashFlowRationale?.Trim() ?? string.Empty;
        if (cashFlowRationale.Length > 1000 || request.CashFlowReviewedOn > DateOnly.FromDateTime(DateTime.UtcNow)
            || (cashFlowActivity != ConsolidationCashFlowActivity.Unclassified && (string.IsNullOrWhiteSpace(cashFlowRationale) || !request.CashFlowReviewedOn.HasValue)))
            return TransactionResult.Failure("A classified cash-flow counterpart requires a concise rationale and a review date that is not in the future.");
        if (cashFlowActivity != ConsolidationCashFlowActivity.Unclassified && await db.BankAccounts.AnyAsync(bank => bank.CompanyId == request.MemberCompanyId && bank.LedgerAccountId == sourceAccount.Id, cancellationToken))
            return TransactionResult.Failure("Classify the noncash counterpart account, not the bank ledger account; bank mappings identify cash and cash equivalents.");
        var defaultMethod = sourceAccount.Type switch { AccountType.Asset or AccountType.Liability => ConsolidationTranslationMethod.Closing, AccountType.Revenue or AccountType.Expense => ConsolidationTranslationMethod.Average, _ => ConsolidationTranslationMethod.Historical };
        var translationMethod = defaultMethod;
        if (!string.IsNullOrWhiteSpace(request.TranslationMethod))
        {
            if (!Enum.TryParse<ConsolidationTranslationMethod>(request.TranslationMethod, true, out var requestedMethod) || !Enum.IsDefined(requestedMethod)) return TransactionResult.Failure("Choose Closing, Average, or Historical as the translation method.");
            translationMethod = requestedMethod;
        }
        var mapping = request.Id is { } id ? await db.ConsolidationAccountMappings.SingleOrDefaultAsync(item => item.Id == id && item.ConsolidationGroupId == group.Id, cancellationToken) : null;
        if (request.Id is not null && mapping is null) return TransactionResult.Failure("The account mapping was not found in this consolidation group.");
        if (mapping is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(mapping.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))) return TransactionResult.Failure("The account mapping changed after it was displayed. Refresh before saving it.");
        if (mapping is not null && (mapping.MemberCompanyId != request.MemberCompanyId || mapping.MemberAccountId != request.MemberAccountId)) return TransactionResult.Failure("A retained mapping cannot be moved to another source account. Add a separate mapping instead.");
        if (await db.ConsolidationAccountMappings.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.MemberCompanyId == request.MemberCompanyId && item.MemberAccountId == request.MemberAccountId && item.Id != request.Id && item.EffectiveFrom <= requestedEnd && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom), cancellationToken))
            return TransactionResult.Failure("Active mappings for the same source account cannot overlap.");
        if (await db.ConsolidationAccountMappings.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.Id != request.Id && item.ReportingAccountNumber == reportingNumber && item.EffectiveFrom <= requestedEnd && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom) && (item.ReportingAccountName != reportingName || item.ReportingAccountType != sourceAccount.Type || item.TranslationMethod != translationMethod), cancellationToken))
            return TransactionResult.Failure("A reporting account number must retain one name, account type, and translation method throughout overlapping mapping periods.");
        mapping ??= new ConsolidationAccountMapping { Id = Guid.NewGuid(), ConsolidationGroupId = group.Id, MemberCompanyId = request.MemberCompanyId, MemberAccountId = request.MemberAccountId };
        mapping.ReportingAccountNumber = reportingNumber; mapping.ReportingAccountName = reportingName; mapping.ReportingAccountType = sourceAccount.Type; mapping.TranslationMethod = translationMethod; mapping.CashFlowActivity = cashFlowActivity; mapping.CashFlowRationale = cashFlowRationale; mapping.CashFlowReviewedOn = request.CashFlowReviewedOn; mapping.EffectiveFrom = request.EffectiveFrom; mapping.EffectiveThrough = request.EffectiveThrough; mapping.IsActive = request.IsActive; mapping.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(mapping).State == EntityState.Detached) db.ConsolidationAccountMappings.Add(mapping);
        group.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-account-mapping.created" : "consolidation-account-mapping.updated", EntityType = nameof(ConsolidationAccountMapping), EntityId = mapping.Id, DetailJson = JsonSerializer.Serialize(new { group.Id, mapping.MemberCompanyId, mapping.MemberAccountId, mapping.ReportingAccountNumber, mapping.ReportingAccountName, reportingAccountType = mapping.ReportingAccountType.ToString(), translationMethod = mapping.TranslationMethod.ToString(), cashFlowActivity = mapping.CashFlowActivity.ToString(), mapping.CashFlowRationale, mapping.CashFlowReviewedOn, mapping.EffectiveFrom, mapping.EffectiveThrough, mapping.IsActive }), OccurredAtUtc = DateTimeOffset.UtcNow });
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
            members.Where(member => member.ConsolidationGroupId == group.Id).OrderBy(member => companies[member.MemberCompanyId].Name).ThenBy(member => member.EffectiveFrom).Select(member => new ConsolidationGroupMemberSnapshot(member.Id, member.MemberCompanyId, companies[member.MemberCompanyId].Name, companies[member.MemberCompanyId].BaseCurrency, member.OwnershipPercentage, member.EffectiveFrom, member.EffectiveThrough, member.ConcurrencyToken, member.ConsolidationBasis.ToString(), member.BasisRationale, member.BasisReviewedOn)).ToArray(), group.CtaAccountNumber, group.CtaAccountName, group.NciAccountNumber, group.NciAccountName)).ToArray();
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
                return new ConsolidationAccountMappingSnapshot(mapping.Id, mapping.MemberCompanyId, companies[mapping.MemberCompanyId].Name, mapping.MemberAccountId, account.Number, account.Name, account.Type.ToString(), mapping.ReportingAccountNumber, mapping.ReportingAccountName, mapping.ReportingAccountType.ToString(), mapping.TranslationMethod.ToString(), mapping.EffectiveFrom, mapping.EffectiveThrough, mapping.IsActive, mapping.ConcurrencyToken, mapping.CashFlowActivity.ToString(), mapping.CashFlowRationale, mapping.CashFlowReviewedOn);
            }).ToArray());
    }

    public async Task<TransactionResult> SaveAdjustmentAsync(SaveConsolidationAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.JournalPrepare))
            return TransactionResult.Failure("You are not authorized to prepare consolidation adjustments.");
        if (request.ConsolidationGroupId == Guid.Empty || request.PeriodStart > request.AsOf || !Enum.TryParse<ConsolidationAdjustmentKind>(request.Kind, true, out var kind) || !Enum.IsDefined(kind))
            return TransactionResult.Failure("Choose a consolidation group, a valid reporting period, and a supported adjustment kind.");
        var reference = request.Reference?.Trim() ?? string.Empty; var description = request.Description?.Trim() ?? string.Empty; var matchReference = request.MatchReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 64 || description.Length > 1000 || matchReference.Length > 160 || request.Lines is null || request.Lines.Count < 2)
            return TransactionResult.Failure("Provide a concise reference, at least two lines, and an optional concise description and match reference.");
        if (kind == ConsolidationAdjustmentKind.IntercompanyElimination && string.IsNullOrWhiteSpace(matchReference))
            return TransactionResult.Failure("An intercompany elimination requires a match reference.");
        if (kind != ConsolidationAdjustmentKind.IntercompanyElimination && !string.IsNullOrWhiteSpace(matchReference))
            return TransactionResult.Failure("Only an intercompany elimination can carry an intercompany match reference.");
        if (kind == ConsolidationAdjustmentKind.NoncontrollingInterest && !request.SubjectCompanyId.HasValue)
            return TransactionResult.Failure("A noncontrolling-interest reclassification requires its controlled subsidiary.");
        if (kind != ConsolidationAdjustmentKind.NoncontrollingInterest && request.SubjectCompanyId.HasValue)
            return TransactionResult.Failure("A subject company is reserved for noncontrolling-interest reclassification.");
        var parsedLines = new List<(ConsolidationAdjustmentLineRequest Request, AccountType Type)>();
        foreach (var line in request.Lines)
        {
            if (!Enum.TryParse<AccountType>(line.ReportingAccountType, true, out var type) || !Enum.IsDefined(type)
                || string.IsNullOrWhiteSpace(line.ReportingAccountNumber) || line.ReportingAccountNumber.Trim().Length > 64
                || string.IsNullOrWhiteSpace(line.ReportingAccountName) || line.ReportingAccountName.Trim().Length > 160
                || line.Description?.Trim().Length > 1000 || line.Debit < 0m || line.Credit < 0m
                || line.Debit > 9999999999999999.99m || line.Credit > 9999999999999999.99m
                || decimal.Round(line.Debit, 2) != line.Debit || decimal.Round(line.Credit, 2) != line.Credit
                || (line.Debit == 0m) == (line.Credit == 0m))
                return TransactionResult.Failure("Every adjustment line requires a valid reporting account and exactly one positive debit or credit within the supported 18-digit currency range and with no more than two decimal places.");
            parsedLines.Add((line, type));
        }
        if (parsedLines.Sum(line => line.Request.Debit) != parsedLines.Sum(line => line.Request.Credit))
            return TransactionResult.Failure("Consolidation adjustment debits and credits must balance exactly.");

        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.Id == request.ConsolidationGroupId && item.CompanyId == companyId && item.IsActive, cancellationToken);
        if (group is null) return TransactionResult.Failure("The active consolidation group was not found in the active company.");
        var membershipError = await ValidateGroupAccessAsync(db, group.Id, userId.Value, request.AsOf, cancellationToken);
        if (membershipError is not null) return TransactionResult.Failure(membershipError);
        var effectiveMembers = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id && item.EffectiveFrom <= request.AsOf && (item.EffectiveThrough == null || item.EffectiveThrough >= request.AsOf)).ToListAsync(cancellationToken);
        var memberCompanyIds = effectiveMembers.Select(item => item.MemberCompanyId).ToHashSet();
        var reportingAccounts = await EffectiveReportingAccountsAsync(db, group.Id, request.AsOf, cancellationToken);
        if (kind == ConsolidationAdjustmentKind.NoncontrollingInterest)
        {
            var subject = effectiveMembers.SingleOrDefault(item => item.MemberCompanyId == request.SubjectCompanyId);
            if (subject is null || subject.ConsolidationBasis != ConsolidationBasis.ControlledSubsidiary || subject.OwnershipPercentage >= 1m)
                return TransactionResult.Failure("The NCI subject must be a partially owned controlled subsidiary effective on the report date.");
            if (string.IsNullOrWhiteSpace(group.NciAccountNumber) || string.IsNullOrWhiteSpace(group.NciAccountName))
                return TransactionResult.Failure("Configure the group's dedicated NCI equity reporting account before preparing an NCI reclassification.");
        }
        foreach (var (line, type) in parsedLines)
        {
            var number = line.ReportingAccountNumber.Trim(); var name = line.ReportingAccountName.Trim();
            if (number == group.CtaAccountNumber)
                return TransactionResult.Failure("The configured CTA account is system-controlled and cannot be used in a manual adjustment or elimination.");
            var isNciAccount = kind == ConsolidationAdjustmentKind.NoncontrollingInterest && number == group.NciAccountNumber && name == group.NciAccountName && type == AccountType.Equity;
            if (!isNciAccount && !reportingAccounts.Contains((number, name, type)))
                return TransactionResult.Failure($"Reporting account {number} · {name} is not established by an effective consolidation mapping for {request.AsOf:yyyy-MM-dd}.");
            if (kind == ConsolidationAdjustmentKind.IntercompanyElimination)
            {
                if (!line.SourceCompanyId.HasValue || !line.CounterpartyCompanyId.HasValue || line.SourceCompanyId == line.CounterpartyCompanyId
                    || !memberCompanyIds.Contains(line.SourceCompanyId.Value) || !memberCompanyIds.Contains(line.CounterpartyCompanyId.Value))
                    return TransactionResult.Failure("Every elimination line requires two different companies that are members of the consolidation group on the report date.");
            }
            else if (kind == ConsolidationAdjustmentKind.NoncontrollingInterest)
            {
                if (type != AccountType.Equity || line.SourceCompanyId != request.SubjectCompanyId || line.CounterpartyCompanyId.HasValue)
                    return TransactionResult.Failure("Every NCI reclassification line must use an equity account, retain the exact controlled subsidiary as its subject, and omit counterparty provenance.");
            }
            else if (line.SourceCompanyId.HasValue || line.CounterpartyCompanyId.HasValue)
                return TransactionResult.Failure("Company-pair provenance is reserved for intercompany elimination lines.");
        }
        if (kind == ConsolidationAdjustmentKind.NoncontrollingInterest && parsedLines.Count(line => line.Request.ReportingAccountNumber.Trim() == group.NciAccountNumber && line.Request.ReportingAccountName.Trim() == group.NciAccountName && line.Type == AccountType.Equity) != 1)
            return TransactionResult.Failure("An NCI reclassification requires exactly one line to the group's dedicated NCI equity account.");

        var entity = request.Id is { } id ? await db.ConsolidationAdjustmentBatches.SingleOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId && item.ConsolidationGroupId == group.Id, cancellationToken) : null;
        if (request.Id is not null && entity is null) return TransactionResult.Failure("The consolidation adjustment was not found in this group.");
        if (entity is not null && entity.Status is not ("Draft" or "Rejected")) return TransactionResult.Failure("Only a draft or rejected consolidation adjustment can be edited.");
        if (entity is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(entity.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)))
            return TransactionResult.Failure("The consolidation adjustment changed after it was displayed. Refresh before saving it.");
        if (entity is not null && (entity.ConsolidationGroupId != request.ConsolidationGroupId || entity.PeriodStart != request.PeriodStart || entity.AsOf != request.AsOf || entity.Kind != kind || entity.SubjectCompanyId != request.SubjectCompanyId))
            return TransactionResult.Failure("A retained adjustment cannot be moved to another group, period, or kind. Create a separate adjustment instead.");
        entity ??= new ConsolidationAdjustmentBatch { Id = Guid.NewGuid(), CompanyId = companyId.Value, ConsolidationGroupId = group.Id, PeriodStart = request.PeriodStart, AsOf = request.AsOf, Kind = kind, SubjectCompanyId = request.SubjectCompanyId };
        var matchError = await ControlIntercompanyMatchAsync(db, entity, kind, matchReference, parsedLines.Select(item => item.Request).ToArray(), userId.Value, cancellationToken);
        if (matchError is not null) return TransactionResult.Failure(matchError);
        entity.SubjectCompanyId = request.SubjectCompanyId;
        entity.ControlKey = kind == ConsolidationAdjustmentKind.NoncontrollingInterest ? BuildNciControlKey(group.Id, request.PeriodStart, request.AsOf, request.SubjectCompanyId!.Value) : null;
        entity.Reference = reference; entity.Description = description; entity.MatchReference = matchReference; entity.Status = "Draft";
        entity.PreparedByUserId = userId; entity.PreparedAtUtc = DateTimeOffset.UtcNow; entity.ApprovedByUserId = null; entity.ApprovedAtUtc = null; entity.RejectedByUserId = null; entity.RejectedAtUtc = null; entity.DecisionReason = string.Empty; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(entity).State == EntityState.Detached) db.ConsolidationAdjustmentBatches.Add(entity);
        else db.ConsolidationAdjustmentLines.RemoveRange(await db.ConsolidationAdjustmentLines.Where(line => line.ConsolidationAdjustmentBatchId == entity.Id).ToListAsync(cancellationToken));
        db.ConsolidationAdjustmentLines.AddRange(parsedLines.Select((line, index) => new ConsolidationAdjustmentLine
        {
            Id = Guid.NewGuid(), ConsolidationAdjustmentBatchId = entity.Id, Sequence = index + 1,
            ReportingAccountNumber = line.Request.ReportingAccountNumber.Trim(), ReportingAccountName = line.Request.ReportingAccountName.Trim(), ReportingAccountType = line.Type,
            Debit = line.Request.Debit, Credit = line.Request.Credit, Description = line.Request.Description?.Trim() ?? string.Empty,
            SourceCompanyId = line.Request.SourceCompanyId, CounterpartyCompanyId = line.Request.CounterpartyCompanyId
        }));
        AddAdjustmentAudit(db, companyId.Value, userId, request.Id is null ? "consolidation-adjustment.prepared" : "consolidation-adjustment.updated", entity, new { entity.Kind, entity.PeriodStart, entity.AsOf, entity.Reference, entity.MatchReference, entity.SubjectCompanyId, entity.ControlKey, lineCount = parsedLines.Count });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation adjustment changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) when (kind == ConsolidationAdjustmentKind.NoncontrollingInterest) { return TransactionResult.Failure("An NCI reclassification for this controlled subsidiary and exact reporting period is already retained."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The adjustment reference conflicts with another retained adjustment for this consolidation period."); }
        return TransactionResult.Success(entity.Id);
    }

    public Task<TransactionResult> ApproveAdjustmentAsync(ConsolidationAdjustmentActionRequest request, CancellationToken cancellationToken = default) =>
        DecideAdjustmentAsync(request.ConsolidationGroupId, request.AdjustmentBatchId, request.ConcurrencyToken, approve: true, string.Empty, cancellationToken);

    public Task<TransactionResult> RejectAdjustmentAsync(ConsolidationAdjustmentDecisionRequest request, CancellationToken cancellationToken = default) =>
        DecideAdjustmentAsync(request.ConsolidationGroupId, request.AdjustmentBatchId, request.ConcurrencyToken, approve: false, request.Reason, cancellationToken);

    private async Task<TransactionResult> DecideAdjustmentAsync(Guid groupId, Guid batchId, string concurrencyToken, bool approve, string reason, CancellationToken cancellationToken)
    {
        if (!HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.JournalApprove))
            return TransactionResult.Failure("You are not authorized to review consolidation adjustments.");
        if (!approve && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)) return TransactionResult.Failure("A concise rejection reason is required.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ConsolidationAdjustmentBatches.SingleOrDefaultAsync(item => item.Id == batchId && item.ConsolidationGroupId == groupId && item.CompanyId == companyId, cancellationToken);
        if (entity is null) return TransactionResult.Failure("The consolidation adjustment was not found in this group.");
        if (entity.Status != "Draft") return TransactionResult.Failure("Only a draft consolidation adjustment can be approved or rejected.");
        if (string.IsNullOrWhiteSpace(concurrencyToken) || entity.ConcurrencyToken != concurrencyToken) return TransactionResult.Failure("The consolidation adjustment changed after it was displayed. Refresh before reviewing it.");
        if (entity.PreparedByUserId == userId) return TransactionResult.Failure("The person who prepared a consolidation adjustment cannot approve or reject it.");
        var membershipError = await ValidateGroupAccessAsync(db, groupId, userId.Value, entity.AsOf, cancellationToken); if (membershipError is not null) return TransactionResult.Failure(membershipError);
        if (!await db.ConsolidationGroups.AnyAsync(group => group.Id == groupId && group.CompanyId == companyId && group.IsActive, cancellationToken)) return TransactionResult.Failure("An inactive consolidation group cannot accept new review decisions.");
        var validationError = await ValidateRetainedAdjustmentAsync(db, entity, cancellationToken); if (validationError is not null) return TransactionResult.Failure(validationError);
        var now = DateTimeOffset.UtcNow;
        entity.Status = approve ? "Approved" : "Rejected"; entity.DecisionReason = approve ? string.Empty : reason.Trim(); entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (approve) { entity.ApprovedByUserId = userId; entity.ApprovedAtUtc = now; entity.RejectedByUserId = null; entity.RejectedAtUtc = null; }
        else { entity.RejectedByUserId = userId; entity.RejectedAtUtc = now; entity.ApprovedByUserId = null; entity.ApprovedAtUtc = null; }
        AddAdjustmentAudit(db, companyId.Value, userId, approve ? "consolidation-adjustment.approved" : "consolidation-adjustment.rejected", entity, new { reason = entity.DecisionReason });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation adjustment changed concurrently. Refresh and try again."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> PostAdjustmentAsync(ConsolidationAdjustmentActionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.JournalPost))
            return TransactionResult.Failure("You are not authorized to post consolidation adjustments.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ConsolidationAdjustmentBatches.SingleOrDefaultAsync(item => item.Id == request.AdjustmentBatchId && item.ConsolidationGroupId == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (entity is null) return TransactionResult.Failure("The approved consolidation adjustment was not found in this group.");
        if (entity.Status != "Approved") return TransactionResult.Failure("Only an approved consolidation adjustment can be posted.");
        if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || entity.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The consolidation adjustment changed after it was displayed. Refresh before posting it.");
        if (entity.ApprovedByUserId == userId) return TransactionResult.Failure("The person who approved a consolidation adjustment cannot post it.");
        var membershipError = await ValidateGroupAccessAsync(db, entity.ConsolidationGroupId, userId.Value, entity.AsOf, cancellationToken); if (membershipError is not null) return TransactionResult.Failure(membershipError);
        if (!await db.ConsolidationGroups.AnyAsync(group => group.Id == entity.ConsolidationGroupId && group.CompanyId == companyId && group.IsActive, cancellationToken)) return TransactionResult.Failure("An inactive consolidation group cannot accept new postings.");
        if (await db.AccountingPeriods.AnyAsync(period => period.CompanyId == companyId && period.Status == "Closed" && period.StartsOn <= entity.AsOf && period.EndsOn >= entity.PeriodStart, cancellationToken)) return TransactionResult.Failure("The consolidation reporting period overlaps a closed parent-company accounting period. Reopen the period before posting.");
        var validationError = await ValidateRetainedAdjustmentAsync(db, entity, cancellationToken); if (validationError is not null) return TransactionResult.Failure(validationError);
        entity.Status = "Posted"; entity.PostedByUserId = userId; entity.PostedAtUtc = DateTimeOffset.UtcNow; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAdjustmentAudit(db, companyId.Value, userId, "consolidation-adjustment.posted", entity, new { entity.ApprovedByUserId, entity.ApprovedAtUtc });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation adjustment changed concurrently. Refresh and try again."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> ReverseAdjustmentAsync(ReverseConsolidationAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.JournalReverse))
            return TransactionResult.Failure("You are not authorized to reverse consolidation adjustments.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000) return TransactionResult.Failure("A concise reversal reason is required.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var original = await db.ConsolidationAdjustmentBatches.SingleOrDefaultAsync(item => item.Id == request.AdjustmentBatchId && item.ConsolidationGroupId == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (original is null) return TransactionResult.Failure("The posted consolidation adjustment was not found in this group.");
        if (original.Status != "Posted" || original.ReversedByBatchId.HasValue || original.ReversalOfBatchId.HasValue) return TransactionResult.Failure("Only an unreversed original posted consolidation adjustment can be reversed.");
        if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || original.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The consolidation adjustment changed after it was displayed. Refresh before reversing it.");
        var membershipError = await ValidateGroupAccessAsync(db, original.ConsolidationGroupId, userId.Value, original.AsOf, cancellationToken); if (membershipError is not null) return TransactionResult.Failure(membershipError);
        if (await db.AccountingPeriods.AnyAsync(period => period.CompanyId == companyId && period.Status == "Closed" && period.StartsOn <= original.AsOf && period.EndsOn >= original.PeriodStart, cancellationToken)) return TransactionResult.Failure("The consolidation reporting period overlaps a closed parent-company accounting period. Reopen the period before reversing.");
        var validationError = await ValidateRetainedAdjustmentAsync(db, original, cancellationToken); if (validationError is not null) return TransactionResult.Failure(validationError);
        var originalLines = await db.ConsolidationAdjustmentLines.AsNoTracking().Where(line => line.ConsolidationAdjustmentBatchId == original.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow; var reversalId = Guid.NewGuid(); var reversalReference = $"REV-{original.Reference}-{reversalId:N}"[..Math.Min(64, 5 + original.Reference.Length + 32)];
        var reversal = new ConsolidationAdjustmentBatch
        {
            Id = reversalId, CompanyId = original.CompanyId, ConsolidationGroupId = original.ConsolidationGroupId, PeriodStart = original.PeriodStart, AsOf = original.AsOf, Kind = original.Kind,
            Reference = reversalReference, Description = Truncate($"Reversal of {original.Reference}: {request.Reason.Trim()}", 1000), MatchReference = original.MatchReference, SubjectCompanyId = original.SubjectCompanyId, Status = "Posted",
            PreparedByUserId = userId, PreparedAtUtc = now, ApprovedByUserId = userId, ApprovedAtUtc = now, PostedByUserId = userId, PostedAtUtc = now,
            ReversalOfBatchId = original.Id, ReversalReason = request.Reason.Trim(), ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        db.ConsolidationAdjustmentBatches.Add(reversal);
        db.ConsolidationAdjustmentLines.AddRange(originalLines.Select(line => new ConsolidationAdjustmentLine
        {
            Id = Guid.NewGuid(), ConsolidationAdjustmentBatchId = reversal.Id, Sequence = line.Sequence, ReportingAccountNumber = line.ReportingAccountNumber,
            ReportingAccountName = line.ReportingAccountName, ReportingAccountType = line.ReportingAccountType, Debit = line.Credit, Credit = line.Debit,
            Description = Truncate($"Reversal: {line.Description}", 1000), SourceCompanyId = line.SourceCompanyId, CounterpartyCompanyId = line.CounterpartyCompanyId
        }));
        var releasedControlKey = original.ControlKey;
        original.Status = "Reversed"; original.ReversedByBatchId = reversal.Id; original.ReversalReason = request.Reason.Trim(); original.ControlKey = null; original.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAdjustmentAudit(db, companyId.Value, userId, "consolidation-adjustment.reversed", original, new { reversalId, releasedControlKey, reason = request.Reason.Trim() });
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation adjustment changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The consolidation adjustment was already reversed or conflicts with another retained reversal."); }
        return TransactionResult.Success(reversal.Id);
    }

    public async Task<ConsolidationAdjustmentWorkspace?> GetAdjustmentWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedger.Infrastructure.Auth.BrassLedgerPermissions.ReportingManage)) return null;
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == groupId && item.CompanyId == companyId, cancellationToken); if (group is null) return null;
        var members = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id).ToListAsync(cancellationToken);
        var companyIds = members.Select(item => item.MemberCompanyId).Distinct().ToArray(); var companies = await db.Companies.AsNoTracking().Where(item => companyIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var permittedCompanyIds = await db.CompanyMemberships.AsNoTracking().Where(item => item.UserId == userId && item.IsActive && companyIds.Contains(item.CompanyId)).Select(item => item.CompanyId).Distinct().ToArrayAsync(cancellationToken);
        if (permittedCompanyIds.Length != companyIds.Length) return null;
        var batches = await db.ConsolidationAdjustmentBatches.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id && item.CompanyId == companyId).OrderByDescending(item => item.AsOf).ThenBy(item => item.Reference).ToListAsync(cancellationToken);
        var batchIds = batches.Select(item => item.Id).ToArray(); var lines = await db.ConsolidationAdjustmentLines.AsNoTracking().Where(item => batchIds.Contains(item.ConsolidationAdjustmentBatchId)).OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var userIds = batches.SelectMany(item => new[] { item.PreparedByUserId, item.ApprovedByUserId, item.RejectedByUserId, item.PostedByUserId }).Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(item => userIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => string.IsNullOrWhiteSpace(item.DisplayName) ? item.UserName : item.DisplayName, cancellationToken);
        var mappedReportingAccounts = await db.ConsolidationAccountMappings.AsNoTracking()
            .Where(item => item.ConsolidationGroupId == group.Id)
            .Select(item => new { item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType })
            .ToListAsync(cancellationToken);
        var reportingAccounts = mappedReportingAccounts
            .Select(item => new ConsolidationReportingAccountSnapshot(item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType.ToString()))
            .Distinct()
            .OrderBy(item => item.AccountNumber)
            .ToList();
        if (!string.IsNullOrWhiteSpace(group.NciAccountNumber) && !reportingAccounts.Any(item => item.AccountNumber == group.NciAccountNumber)) reportingAccounts.Add(new(group.NciAccountNumber, group.NciAccountName, nameof(AccountType.Equity)));
        return new ConsolidationAdjustmentWorkspace(group.Id, group.Name, group.ReportingCurrency,
            reportingAccounts.OrderBy(item => item.AccountNumber).ToArray(),
            members.OrderBy(item => companies[item.MemberCompanyId].Name).ThenBy(item => item.EffectiveFrom).Select(item => new ConsolidationGroupMemberSnapshot(item.Id, item.MemberCompanyId, companies[item.MemberCompanyId].Name, companies[item.MemberCompanyId].BaseCurrency, item.OwnershipPercentage, item.EffectiveFrom, item.EffectiveThrough, item.ConcurrencyToken, item.ConsolidationBasis.ToString(), item.BasisRationale, item.BasisReviewedOn)).ToArray(),
            batches.Select(batch => new ConsolidationAdjustmentSnapshot(batch.Id, batch.PeriodStart, batch.AsOf, batch.Kind.ToString(), batch.Reference, batch.Description, batch.MatchReference, batch.Status,
                batch.PreparedByUserId.HasValue ? users.GetValueOrDefault(batch.PreparedByUserId.Value, "Unavailable user") : "Unavailable user", batch.PreparedAtUtc,
                batch.ApprovedByUserId.HasValue ? users.GetValueOrDefault(batch.ApprovedByUserId.Value, "Unavailable user") : null, batch.ApprovedAtUtc,
                batch.RejectedByUserId.HasValue ? users.GetValueOrDefault(batch.RejectedByUserId.Value, "Unavailable user") : null, batch.RejectedAtUtc,
                batch.PostedByUserId.HasValue ? users.GetValueOrDefault(batch.PostedByUserId.Value, "Unavailable user") : null, batch.PostedAtUtc, batch.DecisionReason, batch.ReversalOfBatchId, batch.ReversedByBatchId, batch.ReversalReason, batch.ConcurrencyToken,
                lines.Where(line => line.ConsolidationAdjustmentBatchId == batch.Id).Select(line => new ConsolidationAdjustmentLineSnapshot(line.Id, line.Sequence, line.ReportingAccountNumber, line.ReportingAccountName, line.ReportingAccountType.ToString(), line.Debit, line.Credit, line.Description, line.SourceCompanyId, line.SourceCompanyId.HasValue ? companies.GetValueOrDefault(line.SourceCompanyId.Value)?.Name : null, line.CounterpartyCompanyId, line.CounterpartyCompanyId.HasValue ? companies.GetValueOrDefault(line.CounterpartyCompanyId.Value)?.Name : null)).ToArray(), batch.SubjectCompanyId, batch.SubjectCompanyId.HasValue ? companies.GetValueOrDefault(batch.SubjectCompanyId.Value)?.Name : null)).ToArray());
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
        var warnings = new List<string>(); var totals = new Dictionary<(string Number, string Name, string Type, string Method), decimal>();
        var contributions = new Dictionary<(string Number, string Name, string Type, string Method), List<ConsolidatedAccountContribution>>(); var complete = true;
        var effectiveSchedule = members.Select(member => new ConsolidationPeriodPolicy(member.MemberCompanyId, member.OwnershipPercentage, member.ConsolidationBasis, member.BasisRationale, member.BasisReviewedOn, member.EffectiveFrom, member.EffectiveThrough)).ToArray();
        if (!ValidClassifiedGroupSchedule(effectiveSchedule, group.CompanyId, asOf, asOf))
        {
            complete = false;
            warnings.Add("The effective classified schedule does not contain exactly one 100% reporting parent. Correct the retained basis periods before relying on this report.");
        }
        foreach (var member in members)
        {
            var company = companies[member.MemberCompanyId];
            var inclusionFactor = member.ConsolidationBasis == ConsolidationBasis.ProportionateInterest ? member.OwnershipPercentage : 1m;
            if (member.ConsolidationBasis == ConsolidationBasis.ReportingParent && (member.MemberCompanyId != group.CompanyId || member.OwnershipPercentage != 1m))
            {
                complete = false;
                warnings.Add($"{company.Name} has an invalid reporting-parent classification; it must be the 100% active parent company.");
            }
            if (member.ConsolidationBasis == ConsolidationBasis.ProportionateInterest && string.IsNullOrWhiteSpace(member.BasisRationale) && !member.BasisReviewedOn.HasValue)
            {
                complete = false;
                warnings.Add($"{company.Name} retains the legacy proportionate-interest basis without reviewed evidence. Classify the relationship before relying on this report.");
            }
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
                sourceSignedBalance += (account.Type is AccountType.Asset or AccountType.Expense ? applicableNaturalBalance : -applicableNaturalBalance) * inclusionFactor;
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
                var converted = decimal.Round(translated * inclusionFactor, 2, MidpointRounding.AwayFromZero);
                totals[key] = totals.GetValueOrDefault(key) + converted;
                AddContribution(contributions, key, new(member.MemberCompanyId, company.Name, account.Number, account.Name, "MemberLedger", string.Empty, converted, accountMapping.TranslationMethod.ToString()));
            }
            if (decimal.Round(sourceSignedBalance, 2, MidpointRounding.AwayFromZero) != 0m)
            {
                complete = false;
                warnings.Add($"{company.Name}'s selected report-period balances do not balance in {company.BaseCurrency}. Close pre-period nominal activity or choose the correct reporting-period start; CTA was not used to conceal the {sourceSignedBalance:N2} source imbalance.");
            }
        }
        var adjustmentBatches = await db.ConsolidationAdjustmentBatches.AsNoTracking().Where(batch => batch.CompanyId == companyId && batch.ConsolidationGroupId == group.Id && batch.PeriodStart == periodStart && batch.AsOf == asOf && (batch.Status == "Posted" || batch.Status == "Reversed")).ToListAsync(cancellationToken);
        var adjustmentIds = adjustmentBatches.Select(batch => batch.Id).ToArray();
        var adjustmentLines = await db.ConsolidationAdjustmentLines.AsNoTracking().Where(line => adjustmentIds.Contains(line.ConsolidationAdjustmentBatchId)).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(group.NciAccountNumber) && mappings.Any(mapping => mapping.ReportingAccountNumber == group.NciAccountNumber))
        {
            complete = false;
            warnings.Add($"NCI account {group.NciAccountNumber} is also used by a source-account mapping. Configure a dedicated equity reporting account.");
        }
        foreach (var member in members.Where(item => item.ConsolidationBasis == ConsolidationBasis.ControlledSubsidiary && item.OwnershipPercentage < 1m))
        {
            if (!adjustmentBatches.Any(batch => batch.Kind == ConsolidationAdjustmentKind.NoncontrollingInterest && batch.SubjectCompanyId == member.MemberCompanyId && batch.Status == "Posted" && !batch.ReversalOfBatchId.HasValue))
            {
                complete = false;
                warnings.Add($"{companies[member.MemberCompanyId].Name} is fully consolidated with {(1m - member.OwnershipPercentage):P2} noncontrolling ownership, but no posted NCI reclassification exists for this exact reporting period.");
            }
        }
        foreach (var batch in adjustmentBatches)
        {
            var retainedError = ValidateReportAdjustment(batch, adjustmentLines.Where(line => line.ConsolidationAdjustmentBatchId == batch.Id).ToArray(), mappings, members, group.CtaAccountNumber, group.NciAccountNumber, group.NciAccountName);
            if (retainedError is not null) { complete = false; warnings.Add($"Consolidation adjustment {batch.Reference} was excluded: {retainedError}"); continue; }
            foreach (var line in adjustmentLines.Where(line => line.ConsolidationAdjustmentBatchId == batch.Id))
            {
                var key = (line.ReportingAccountNumber, line.ReportingAccountName, line.ReportingAccountType.ToString(), batch.Kind == ConsolidationAdjustmentKind.NoncontrollingInterest ? "NCI" : "Adjustment");
                var amount = NaturalAmount(line.ReportingAccountType, line.Debit, line.Credit);
                totals[key] = totals.GetValueOrDefault(key) + amount;
                var sourceName = line.SourceCompanyId.HasValue && companies.TryGetValue(line.SourceCompanyId.Value, out var sourceCompany) ? sourceCompany.Name : group.Name;
                AddContribution(contributions, key, new(line.SourceCompanyId, sourceName, line.ReportingAccountNumber, line.ReportingAccountName, batch.Kind.ToString(), batch.Reference, amount, key.Item4));
            }
        }
        var retainedOwnershipEvents = await db.ConsolidationOwnershipEvents.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.ConsolidationGroupId == group.Id && item.EventDate <= asOf)
            .OrderBy(item => item.EventDate).ThenBy(item => item.Reference).ToArrayAsync(cancellationToken);
        foreach (var pendingEvent in retainedOwnershipEvents.Where(item => item.Status is not ("Posted" or "Reversed" or "Rejected")))
        {
            complete = false;
            warnings.Add($"Ownership event {pendingEvent.Reference} dated {pendingEvent.EventDate:yyyy-MM-dd} is {pendingEvent.Status} and was excluded until independently approved and posted.");
        }
        var ownershipEvents = retainedOwnershipEvents.Where(item => item.Status is "Posted" or "Reversed");
        foreach (var ownershipEvent in ownershipEvents)
        {
            var retainedError = await ValidateRetainedOwnershipEventAsync(db, ownershipEvent, cancellationToken);
            var content = DeserializeOwnershipEvent(ownershipEvent);
            if (retainedError is not null || content is null)
            {
                complete = false;
                warnings.Add($"Ownership event {ownershipEvent.Reference} was excluded: {retainedError ?? "its retained JSON is invalid."}");
                continue;
            }
            decimal priorPeriodEarnings = 0m;
            foreach (var line in content.PostingLines)
            {
                _ = Enum.TryParse<AccountType>(line.ReportingAccountType, true, out var accountType);
                var amount = NaturalAmount(accountType, line.Debit, line.Credit);
                if (ownershipEvent.EventDate < periodStart && accountType is AccountType.Revenue or AccountType.Expense)
                {
                    priorPeriodEarnings += accountType == AccountType.Revenue ? amount : -amount;
                    continue;
                }
                var key = (line.ReportingAccountNumber.Trim(), line.ReportingAccountName.Trim(), accountType.ToString(), "OwnershipEvent");
                totals[key] = totals.GetValueOrDefault(key) + amount;
                AddContribution(contributions, key, new(ownershipEvent.SubjectCompanyId, companies.GetValueOrDefault(ownershipEvent.SubjectCompanyId)?.Name ?? "Unavailable company", line.ReportingAccountNumber.Trim(), line.ReportingAccountName.Trim(), ownershipEvent.EventType.ToString(), ownershipEvent.Reference, amount, "OwnershipEvent"));
            }
            if (priorPeriodEarnings != 0m)
            {
                var key = (content.PriorPeriodEquityAccountNumber.Trim(), content.PriorPeriodEquityAccountName.Trim(), AccountType.Equity.ToString(), "OwnershipEventCarryforward");
                totals[key] = totals.GetValueOrDefault(key) + priorPeriodEarnings;
                AddContribution(contributions, key, new(ownershipEvent.SubjectCompanyId, companies.GetValueOrDefault(ownershipEvent.SubjectCompanyId)?.Name ?? "Unavailable company", content.PriorPeriodEquityAccountNumber.Trim(), content.PriorPeriodEquityAccountName.Trim(), "OwnershipEventCarryforward", ownershipEvent.Reference, priorPeriodEarnings, "OwnershipEventCarryforward"));
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
                    var key = (group.CtaAccountNumber, group.CtaAccountName, AccountType.Equity.ToString(), "CTA");
                    totals[key] = translationAdjustment;
                    AddContribution(contributions, key, new(null, group.Name, group.CtaAccountNumber, group.CtaAccountName, "TranslationAdjustment", $"{periodStart:yyyy-MM-dd}:{asOf:yyyy-MM-dd}", translationAdjustment, "CTA"));
                }
            }
        }
        else warnings.Add("CTA was not calculated because one or more material source balances were excluded.");
        var reportAccounts = totals.GroupBy(item => (item.Key.Number, item.Key.Name, item.Key.Type)).OrderBy(grouping => grouping.Key.Number).Select(grouping =>
        {
            var methods = grouping.Select(item => item.Key.Method).Distinct().OrderBy(item => item).ToArray();
            var accountContributions = grouping.SelectMany(item => contributions.GetValueOrDefault(item.Key) ?? []).OrderBy(item => item.CompanyName).ThenBy(item => item.SourceAccountNumber).ThenBy(item => item.Reference).ToArray();
            return new ConsolidatedAccountBalance(grouping.Key.Number, grouping.Key.Name, grouping.Key.Type, grouping.Sum(item => item.Value), methods.Length == 1 ? methods[0] : "Mixed", accountContributions);
        }).ToArray();
        return new ConsolidatedBalanceReport(group.Id, group.Name, group.ReportingCurrency, periodStart, asOf, reportAccounts, warnings, translationAdjustment);
    }

    private async Task<string?> ValidateRetainedAdjustmentAsync(BrassLedgerDbContext db, ConsolidationAdjustmentBatch batch, CancellationToken cancellationToken)
    {
        var lines = await db.ConsolidationAdjustmentLines.Where(line => line.ConsolidationAdjustmentBatchId == batch.Id).ToListAsync(cancellationToken);
        var mappings = await db.ConsolidationAccountMappings.AsNoTracking().Where(mapping => mapping.ConsolidationGroupId == batch.ConsolidationGroupId && mapping.EffectiveFrom <= batch.AsOf && (mapping.EffectiveThrough == null || mapping.EffectiveThrough >= batch.AsOf)).ToListAsync(cancellationToken);
        var members = await db.ConsolidationGroupCompanies.AsNoTracking().Where(member => member.ConsolidationGroupId == batch.ConsolidationGroupId && member.EffectiveFrom <= batch.AsOf && (member.EffectiveThrough == null || member.EffectiveThrough >= batch.AsOf)).ToListAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleAsync(group => group.Id == batch.ConsolidationGroupId, cancellationToken);
        return ValidateReportAdjustment(batch, lines, mappings, members, group.CtaAccountNumber, group.NciAccountNumber, group.NciAccountName);
    }

    private static string? ValidateReportAdjustment(ConsolidationAdjustmentBatch batch, IReadOnlyList<ConsolidationAdjustmentLine> lines, IReadOnlyList<ConsolidationAccountMapping> mappings, IReadOnlyList<ConsolidationGroupCompany> members, string ctaAccountNumber, string nciAccountNumber, string nciAccountName)
    {
        if (batch.PeriodStart > batch.AsOf || lines.Count < 2 || lines.Sum(line => line.Debit) != lines.Sum(line => line.Credit)) return "its retained lines are not a balanced adjustment for a valid period.";
        var memberCompanyIds = members.Select(member => member.MemberCompanyId).ToHashSet();
        if (batch.Kind == ConsolidationAdjustmentKind.NoncontrollingInterest)
        {
            var subject = members.SingleOrDefault(member => member.MemberCompanyId == batch.SubjectCompanyId);
            if (subject is null || subject.ConsolidationBasis != ConsolidationBasis.ControlledSubsidiary || subject.OwnershipPercentage >= 1m) return "its NCI subject is not a partially owned controlled subsidiary effective on the report date.";
            var expectedControlKey = BuildNciControlKey(batch.ConsolidationGroupId, batch.PeriodStart, batch.AsOf, subject.MemberCompanyId);
            var activeOriginal = !batch.ReversalOfBatchId.HasValue && !batch.ReversedByBatchId.HasValue;
            if ((activeOriginal && batch.ControlKey != expectedControlKey) || (!activeOriginal && batch.ControlKey is not null)) return "its retained NCI control identity is invalid.";
            if (string.IsNullOrWhiteSpace(nciAccountNumber) || lines.Count(line => line.ReportingAccountNumber == nciAccountNumber && line.ReportingAccountName == nciAccountName && line.ReportingAccountType == AccountType.Equity) != 1) return "it does not contain exactly one line to the configured NCI equity account.";
        }
        else if (batch.SubjectCompanyId.HasValue || batch.ControlKey is not null) return "a non-NCI adjustment contains NCI-only control provenance.";
        foreach (var line in lines)
        {
            if (line.Debit < 0m || line.Credit < 0m || (line.Debit == 0m) == (line.Credit == 0m)) return "a retained line does not contain exactly one positive debit or credit.";
            if (line.ReportingAccountNumber == ctaAccountNumber) return "it targets the system-controlled CTA account.";
            var isNciAccount = batch.Kind == ConsolidationAdjustmentKind.NoncontrollingInterest && line.ReportingAccountNumber == nciAccountNumber && line.ReportingAccountName == nciAccountName && line.ReportingAccountType == AccountType.Equity;
            if (!isNciAccount && !mappings.Any(mapping => mapping.ReportingAccountNumber == line.ReportingAccountNumber && mapping.ReportingAccountName == line.ReportingAccountName && mapping.ReportingAccountType == line.ReportingAccountType)) return $"reporting account {line.ReportingAccountNumber} is no longer supported by an effective mapping.";
            if (batch.Kind == ConsolidationAdjustmentKind.IntercompanyElimination)
            {
                if (string.IsNullOrWhiteSpace(batch.MatchReference) || !line.SourceCompanyId.HasValue || !line.CounterpartyCompanyId.HasValue || line.SourceCompanyId == line.CounterpartyCompanyId || !memberCompanyIds.Contains(line.SourceCompanyId.Value) || !memberCompanyIds.Contains(line.CounterpartyCompanyId.Value)) return "its intercompany provenance is incomplete or outside the effective membership set.";
            }
            else if (batch.Kind == ConsolidationAdjustmentKind.NoncontrollingInterest)
            {
                if (!string.IsNullOrWhiteSpace(batch.MatchReference) || line.ReportingAccountType != AccountType.Equity || line.SourceCompanyId != batch.SubjectCompanyId || line.CounterpartyCompanyId.HasValue) return "its NCI line account type or subsidiary provenance is invalid.";
            }
            else if (!string.IsNullOrWhiteSpace(batch.MatchReference) || line.SourceCompanyId.HasValue || line.CounterpartyCompanyId.HasValue) return "a manual adjustment contains intercompany-only provenance.";
        }
        return null;
    }

    private static async Task<HashSet<Guid>> EffectiveMemberCompanyIdsAsync(BrassLedgerDbContext db, Guid groupId, DateOnly asOf, CancellationToken cancellationToken) =>
        (await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == groupId && item.EffectiveFrom <= asOf && (item.EffectiveThrough == null || item.EffectiveThrough >= asOf)).Select(item => item.MemberCompanyId).ToListAsync(cancellationToken)).ToHashSet();

    private static async Task<HashSet<(string Number, string Name, AccountType Type)>> EffectiveReportingAccountsAsync(BrassLedgerDbContext db, Guid groupId, DateOnly asOf, CancellationToken cancellationToken) =>
        (await db.ConsolidationAccountMappings.AsNoTracking().Where(item => item.ConsolidationGroupId == groupId && item.EffectiveFrom <= asOf && (item.EffectiveThrough == null || item.EffectiveThrough >= asOf)).Select(item => new { item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType }).ToListAsync(cancellationToken)).Select(item => (item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType)).ToHashSet();

    private static async Task<string?> ValidateGroupAccessAsync(BrassLedgerDbContext db, Guid groupId, Guid userId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var memberIds = await EffectiveMemberCompanyIdsAsync(db, groupId, asOf, cancellationToken);
        if (memberIds.Count == 0) return "The consolidation group has no effective member companies on the report date.";
        var permitted = await db.CompanyMemberships.AsNoTracking().Where(item => item.UserId == userId && item.IsActive && memberIds.Contains(item.CompanyId)).Select(item => item.CompanyId).Distinct().ToListAsync(cancellationToken);
        return permitted.Count == memberIds.Count ? null : "The current user must have active access to every effective member company.";
    }

    private static void AddAdjustmentAudit(BrassLedgerDbContext db, Guid companyId, Guid? userId, string action, ConsolidationAdjustmentBatch batch, object details) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = userId, Action = action, EntityType = nameof(ConsolidationAdjustmentBatch), EntityId = batch.Id, DetailJson = JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow });

    private static string BuildNciControlKey(Guid groupId, DateOnly periodStart, DateOnly asOf, Guid subjectCompanyId) => $"NCI:{groupId:N}:{periodStart:yyyyMMdd}:{asOf:yyyyMMdd}:{subjectCompanyId:N}";
    private static void AddContribution(Dictionary<(string Number, string Name, string Type, string Method), List<ConsolidatedAccountContribution>> contributions, (string Number, string Name, string Type, string Method) key, ConsolidatedAccountContribution contribution)
    {
        if (!contributions.TryGetValue(key, out var items)) contributions[key] = items = [];
        items.Add(contribution);
    }
    private static string Truncate(string value, int maximumLength) => value.Length <= maximumLength ? value : value[..maximumLength];

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
    private static bool TryConsolidationBasis(string value, out ConsolidationBasis basis) => Enum.TryParse(value, true, out basis) && Enum.IsDefined(basis);
    private static bool ValidBasisEvidence(ConsolidationBasis basis, string? rationale, DateOnly? reviewedOn)
    {
        var normalized = rationale?.Trim() ?? string.Empty;
        if (basis == ConsolidationBasis.ReportingParent) return normalized.Length <= 1000 && (!reviewedOn.HasValue || reviewedOn <= DateOnly.FromDateTime(DateTime.UtcNow));
        if (basis == ConsolidationBasis.ProportionateInterest && normalized.Length == 0 && !reviewedOn.HasValue) return true; // Legacy rows remain explicitly proportionate until reviewed.
        return normalized is { Length: > 0 and <= 1000 } && reviewedOn.HasValue && reviewedOn <= DateOnly.FromDateTime(DateTime.UtcNow);
    }
    private static bool ValidClassifiedGroupSchedule(IReadOnlyCollection<ConsolidationPeriodPolicy> periods, Guid parentCompanyId, DateOnly? coverageFrom = null, DateOnly? coverageThrough = null)
    {
        static bool IsClassified(ConsolidationPeriodPolicy period) => period.Basis != ConsolidationBasis.ProportionateInterest || !string.IsNullOrWhiteSpace(period.Rationale) || period.ReviewedOn.HasValue;
        if (!periods.Any(IsClassified)) return true;
        var boundaries = periods.Select(period => period.EffectiveFrom)
            .Concat(periods.Where(period => period.EffectiveThrough.HasValue && period.EffectiveThrough.Value < DateOnly.MaxValue).Select(period => period.EffectiveThrough!.Value.AddDays(1)))
            .Concat(coverageFrom.HasValue ? [coverageFrom.Value] : [])
            .Where(value => (!coverageFrom.HasValue || value >= coverageFrom.Value) && (!coverageThrough.HasValue || value <= coverageThrough.Value))
            .Distinct().OrderBy(value => value);
        foreach (var boundary in boundaries)
        {
            var effective = periods.Where(period => period.EffectiveFrom <= boundary && (!period.EffectiveThrough.HasValue || period.EffectiveThrough.Value >= boundary)).ToArray();
            if (!effective.Any(IsClassified)) continue;
            var parents = effective.Where(period => period.Basis == ConsolidationBasis.ReportingParent).ToArray();
            if (parents.Length != 1 || parents[0].CompanyId != parentCompanyId || parents[0].OwnershipPercentage != 1m) return false;
        }
        return true;
    }
    private sealed record ConsolidationPeriodPolicy(Guid CompanyId, decimal OwnershipPercentage, ConsolidationBasis Basis, string Rationale, DateOnly? ReviewedOn, DateOnly EffectiveFrom, DateOnly? EffectiveThrough);
    private static bool ValidOwnershipPeriods(IReadOnlyList<ConsolidationMemberRequest> members) =>
        members.All(member => member.CompanyId != Guid.Empty && member.OwnershipPercentage is > 0 and <= 1 && (!member.EffectiveThrough.HasValue || member.EffectiveThrough.Value >= (member.EffectiveFrom ?? DateOnly.MinValue)))
        && members.GroupBy(member => member.CompanyId).All(group =>
        {
            var ordered = group.OrderBy(member => member.EffectiveFrom ?? DateOnly.MinValue).ToArray();
            return ordered.Zip(ordered.Skip(1), (left, right) => (left.EffectiveThrough ?? DateOnly.MaxValue) < (right.EffectiveFrom ?? DateOnly.MinValue)).All(value => value);
        });
    private Guid? CurrentCompanyId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedger.Infrastructure.Auth.BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId) ? companyId : null;
    private Guid? CurrentUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    private bool HasPermission(string permission)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is null) return true;
        if (!Guid.TryParse(principal.FindFirstValue(BrassLedger.Infrastructure.Auth.BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out _)) return false;
        return !principal.HasClaim(BrassLedger.Infrastructure.Auth.BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")
            && (principal.IsInRole("Administrator") || principal.IsInRole("Owner/CEO") || principal.HasClaim(BrassLedger.Infrastructure.Auth.BrassLedgerAuthenticationDefaults.PermissionClaimType, permission));
    }
}
