using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class ConsolidationService
{
    private const string BalanceSheetCode = "BALANCE-SHEET";
    private const string IncomeStatementCode = "INCOME-STATEMENT";

    public async Task<TransactionResult> SaveStatementPresentationAsync(SaveConsolidationStatementPresentationRequest request, CancellationToken cancellationToken = default)
    {
        var statementCode = request.StatementCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var accountNumber = request.ReportingAccountNumber?.Trim() ?? string.Empty;
        var accountName = request.ReportingAccountName?.Trim() ?? string.Empty;
        var sectionCode = request.SectionCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var sectionName = request.SectionName?.Trim() ?? string.Empty;
        var lineCaption = request.LineCaption?.Trim() ?? string.Empty;
        var rationale = request.Rationale?.Trim() ?? string.Empty;
        if (request.ConsolidationGroupId == Guid.Empty || statementCode is not (BalanceSheetCode or IncomeStatementCode)
            || !Enum.TryParse<AccountType>(request.ReportingAccountType, true, out var accountType) || !Enum.IsDefined(accountType)
            || string.IsNullOrWhiteSpace(accountNumber) || accountNumber.Length > 64 || string.IsNullOrWhiteSpace(accountName) || accountName.Length > 160
            || string.IsNullOrWhiteSpace(sectionCode) || sectionCode.Length > 64 || string.IsNullOrWhiteSpace(sectionName) || sectionName.Length > 160
            || string.IsNullOrWhiteSpace(lineCaption) || lineCaption.Length > 160 || request.SectionSortOrder is < 0 or > 1_000_000 || request.LineSortOrder is < 0 or > 1_000_000
            || string.IsNullOrWhiteSpace(rationale) || rationale.Length > 1000 || request.ReviewedOn > DateOnly.FromDateTime(DateTime.UtcNow)
            || request.EffectiveThrough < request.EffectiveFrom || (!request.IsActive && !request.EffectiveThrough.HasValue))
            return TransactionResult.Failure("Provide a supported statement, retained reporting account, section and line captions, nonnegative sort orders, reviewed rationale, and valid effective period; an inactive policy requires an end date.");
        if ((statementCode == BalanceSheetCode) != (accountType is AccountType.Asset or AccountType.Liability or AccountType.Equity))
            return TransactionResult.Failure("Balance-sheet presentation accepts asset, liability, and equity accounts; income-statement presentation accepts revenue and expense accounts.");

        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.Id == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (group is null) return TransactionResult.Failure("The consolidation group was not found in the active company.");
        if (!await OwnsEntireGroupAsync(db, group.Id, companyId.Value, userId.Value, cancellationToken))
            return TransactionResult.Failure("The current user must be an active owner of the consolidation group and every retained member company.");
        var requestedEnd = request.EffectiveThrough ?? DateOnly.MaxValue;
        var retainedMappings = await db.ConsolidationAccountMappings.AsNoTracking().Where(mapping => mapping.ConsolidationGroupId == group.Id).ToListAsync(cancellationToken);
        var supportedByMapping = retainedMappings.Any(mapping => mapping.ReportingAccountType == accountType
            && string.Equals(mapping.ReportingAccountNumber.Trim(), accountNumber, StringComparison.Ordinal)
            && string.Equals(mapping.ReportingAccountName.Trim(), accountName, StringComparison.Ordinal));
        var supportedSystemAccount = accountType == AccountType.Equity
            && ((accountNumber == group.CtaAccountNumber && accountName == group.CtaAccountName)
                || (accountNumber == group.NciAccountNumber && accountName == group.NciAccountName)
                || (accountNumber == "CURRENT-EARNINGS" && accountName == "Current-period earnings"));
        if (!supportedByMapping && !supportedSystemAccount)
            return TransactionResult.Failure($"The reporting account {accountNumber} · {accountName} ({accountType}) is not retained by a source mapping or a supported system-generated equity line in this consolidation group. Reload the presentation workspace before choosing an account.");

        var entity = request.Id is { } id ? await db.ConsolidationStatementPresentations.SingleOrDefaultAsync(item => item.Id == id && item.ConsolidationGroupId == group.Id, cancellationToken) : null;
        if (request.Id is not null && entity is null) return TransactionResult.Failure("The statement-presentation policy was not found in this consolidation group.");
        if (entity is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || entity.ConcurrencyToken != request.ConcurrencyToken))
            return TransactionResult.Failure("The statement-presentation policy changed after it was displayed. Refresh before saving it.");
        if (entity is not null && (entity.StatementCode != statementCode || entity.ReportingAccountNumber != accountNumber || entity.ReportingAccountName != accountName || entity.ReportingAccountType != accountType))
            return TransactionResult.Failure("A retained statement-presentation policy cannot be moved to another statement or reporting account. Add a successor policy instead.");
        if (await db.ConsolidationStatementPresentations.AsNoTracking().AnyAsync(item => item.ConsolidationGroupId == group.Id && item.Id != request.Id
            && item.StatementCode == statementCode && item.ReportingAccountNumber == accountNumber && item.EffectiveFrom <= requestedEnd
            && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom), cancellationToken))
            return TransactionResult.Failure("Presentation periods for the same statement account cannot overlap.");
        if (await db.ConsolidationStatementPresentations.AsNoTracking().AnyAsync(item => item.ConsolidationGroupId == group.Id && item.Id != request.Id
            && item.StatementCode == statementCode && item.SectionCode == sectionCode && item.EffectiveFrom <= requestedEnd
            && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom)
            && (item.SectionName != sectionName || item.SectionSortOrder != request.SectionSortOrder), cancellationToken))
            return TransactionResult.Failure("A section code must retain one caption and sort order throughout overlapping presentation periods.");

        entity ??= new ConsolidationStatementPresentation { Id = Guid.NewGuid(), ConsolidationGroupId = group.Id };
        entity.StatementCode = statementCode; entity.ReportingAccountNumber = accountNumber; entity.ReportingAccountName = accountName; entity.ReportingAccountType = accountType;
        entity.SectionCode = sectionCode; entity.SectionName = sectionName; entity.SectionSortOrder = request.SectionSortOrder; entity.LineCaption = lineCaption; entity.LineSortOrder = request.LineSortOrder;
        entity.Rationale = rationale; entity.ReviewedOn = request.ReviewedOn; entity.EffectiveFrom = request.EffectiveFrom; entity.EffectiveThrough = request.EffectiveThrough; entity.IsActive = request.IsActive; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(entity).State == EntityState.Detached) db.ConsolidationStatementPresentations.Add(entity);
        group.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-statement-presentation.created" : "consolidation-statement-presentation.updated",
            EntityType = nameof(ConsolidationStatementPresentation), EntityId = entity.Id,
            DetailJson = JsonSerializer.Serialize(new { group.Id, entity.StatementCode, entity.ReportingAccountNumber, entity.ReportingAccountName, reportingAccountType = entity.ReportingAccountType.ToString(), entity.SectionCode, entity.SectionName, entity.SectionSortOrder, entity.LineCaption, entity.LineSortOrder, entity.Rationale, entity.ReviewedOn, entity.EffectiveFrom, entity.EffectiveThrough, entity.IsActive }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation group or statement-presentation policy changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The statement-presentation policy conflicts with another retained policy."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<ConsolidationStatementPresentationWorkspace?> GetStatementPresentationWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == groupId && item.CompanyId == companyId, cancellationToken);
        if (group is null || !await OwnsEntireGroupAsync(db, group.Id, companyId.Value, userId.Value, cancellationToken)) return null;
        var mapped = await db.ConsolidationAccountMappings.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id)
            .Select(item => new { item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType }).Distinct().ToListAsync(cancellationToken);
        var candidates = mapped.Select(item => Candidate(item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType)).ToList();
        if (!string.IsNullOrWhiteSpace(group.CtaAccountNumber)) candidates.Add(Candidate(group.CtaAccountNumber, group.CtaAccountName, AccountType.Equity));
        if (!string.IsNullOrWhiteSpace(group.NciAccountNumber)) candidates.Add(Candidate(group.NciAccountNumber, group.NciAccountName, AccountType.Equity));
        candidates.Add(Candidate("CURRENT-EARNINGS", "Current-period earnings", AccountType.Equity));
        var presentations = await db.ConsolidationStatementPresentations.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id)
            .OrderBy(item => item.StatementCode).ThenBy(item => item.SectionSortOrder).ThenBy(item => item.LineSortOrder).ThenBy(item => item.ReportingAccountNumber).ThenBy(item => item.EffectiveFrom).ToListAsync(cancellationToken);
        return new(group.Id, group.Name, candidates.Distinct().OrderBy(item => item.StatementCode).ThenBy(item => item.ReportingAccountNumber).ToArray(),
            presentations.Select(item => new ConsolidationStatementPresentationSnapshot(item.Id, item.StatementCode, item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType.ToString(), item.SectionCode, item.SectionName, item.SectionSortOrder, item.LineCaption, item.LineSortOrder, item.Rationale, item.ReviewedOn, item.EffectiveFrom, item.EffectiveThrough, item.IsActive, item.ConcurrencyToken)).ToArray());
    }

    private static ConsolidationStatementPresentationCandidate Candidate(string number, string name, AccountType type) =>
        new(type is AccountType.Asset or AccountType.Liability or AccountType.Equity ? BalanceSheetCode : IncomeStatementCode, number, name, type.ToString());

    private static async Task<bool> OwnsEntireGroupAsync(BrassLedgerDbContext db, Guid groupId, Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        var requiredCompanyIds = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == groupId).Select(item => item.MemberCompanyId).Distinct().ToListAsync(cancellationToken);
        if (!requiredCompanyIds.Contains(companyId)) requiredCompanyIds.Add(companyId);
        var owned = await db.CompanyMemberships.AsNoTracking().Where(item => item.UserId == userId && item.IsOwner && item.IsActive && requiredCompanyIds.Contains(item.CompanyId)).Select(item => item.CompanyId).Distinct().CountAsync(cancellationToken);
        return owned == requiredCompanyIds.Count;
    }
}
