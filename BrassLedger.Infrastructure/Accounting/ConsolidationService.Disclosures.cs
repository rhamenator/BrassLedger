using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class ConsolidationService
{
    private const int CurrentDisclosureSchemaVersion = 1;
    private const int MaximumDisclosureJsonBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions DisclosureJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TransactionResult> SaveDisclosurePackageAsync(SaveConsolidationDisclosurePackageRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedgerPermissions.JournalPrepare))
            return TransactionResult.Failure("You are not authorized to prepare consolidated disclosures.");
        var validationError = ValidateDisclosureRequest(request);
        if (validationError is not null) return TransactionResult.Failure(validationError);
        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.Id == request.ConsolidationGroupId && item.CompanyId == companyId && item.IsActive, cancellationToken);
        if (group is null) return TransactionResult.Failure("The active consolidation group was not found in the active company.");
        var accessError = await ValidateGroupAccessAsync(db, group.Id, userId.Value, request.AsOf, cancellationToken);
        if (accessError is not null) return TransactionResult.Failure(accessError);

        var frameworkCode = request.FrameworkCode.Trim().ToUpperInvariant();
        var frameworkEdition = request.FrameworkEdition.Trim();
        var contentJson = JsonSerializer.Serialize(request.Content, DisclosureJsonOptions);
        var contentSha256 = DisclosureSha256(contentJson);
        if (Encoding.UTF8.GetByteCount(contentJson) > MaximumDisclosureJsonBytes)
            return TransactionResult.Failure("The disclosure document exceeds the supported 2 MiB retained-document limit. Split supporting detail into referenced attachments.");

        var entity = request.Id is { } id
            ? await db.ConsolidationDisclosurePackages.SingleOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId && item.ConsolidationGroupId == group.Id, cancellationToken)
            : null;
        if (request.Id is not null && entity is null) return TransactionResult.Failure("The disclosure package was not found in this consolidation group.");
        if (entity is not null && entity.Status is not ("Draft" or "Rejected")) return TransactionResult.Failure("Only a draft or rejected disclosure package can be edited.");
        if (entity is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || entity.ConcurrencyToken != request.ConcurrencyToken))
            return TransactionResult.Failure("The disclosure package changed after it was displayed. Refresh before saving it.");
        if (entity is not null && (entity.PeriodStart != request.PeriodStart || entity.AsOf != request.AsOf || entity.FrameworkCode != frameworkCode))
            return TransactionResult.Failure("A retained disclosure package cannot be moved to another period or framework. Create a separate package instead.");

        entity ??= new ConsolidationDisclosurePackage
        {
            Id = Guid.NewGuid(), CompanyId = companyId.Value, ConsolidationGroupId = group.Id,
            PeriodStart = request.PeriodStart, AsOf = request.AsOf, FrameworkCode = frameworkCode
        };
        entity.FrameworkEdition = frameworkEdition;
        entity.SchemaVersion = request.Content.SchemaVersion;
        entity.ContentJson = contentJson;
        entity.Status = "Draft";
        entity.PreparedByUserId = userId;
        entity.PreparedAtUtc = DateTimeOffset.UtcNow;
        entity.ApprovedByUserId = null; entity.ApprovedAtUtc = null;
        entity.RejectedByUserId = null; entity.RejectedAtUtc = null;
        entity.DecisionReason = string.Empty;
        entity.ReviewNotes = request.ReviewNotes.Trim();
        entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(entity).State == EntityState.Detached) db.ConsolidationDisclosurePackages.Add(entity);
        AddDisclosureAudit(db, companyId.Value, userId, request.Id is null ? "consolidation-disclosure.prepared" : "consolidation-disclosure.updated", entity,
            new { entity.PeriodStart, entity.AsOf, entity.FrameworkCode, entity.FrameworkEdition, entity.SchemaVersion, contentSha256, financingLiabilities = request.Content.FinancingLiabilities.Count, supplierFinanceArrangements = request.Content.SupplierFinanceArrangements.Count, narrativeDisclosures = request.Content.NarrativeDisclosures.Count });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The disclosure package changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("A disclosure package for this exact group, period, and framework is already retained."); }
        return TransactionResult.Success(entity.Id);
    }

    public Task<TransactionResult> ApproveDisclosurePackageAsync(ConsolidationDisclosureActionRequest request, CancellationToken cancellationToken = default) =>
        DecideDisclosurePackageAsync(request.ConsolidationGroupId, request.DisclosurePackageId, request.ConcurrencyToken, true, string.Empty, cancellationToken);

    public Task<TransactionResult> RejectDisclosurePackageAsync(ConsolidationDisclosureDecisionRequest request, CancellationToken cancellationToken = default) =>
        DecideDisclosurePackageAsync(request.ConsolidationGroupId, request.DisclosurePackageId, request.ConcurrencyToken, false, request.Reason, cancellationToken);

    private async Task<TransactionResult> DecideDisclosurePackageAsync(Guid groupId, Guid packageId, string concurrencyToken, bool approve, string reason, CancellationToken cancellationToken)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedgerPermissions.JournalApprove))
            return TransactionResult.Failure("You are not authorized to review consolidated disclosures.");
        if (!approve && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)) return TransactionResult.Failure("A concise rejection reason is required.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ConsolidationDisclosurePackages.SingleOrDefaultAsync(item => item.Id == packageId && item.ConsolidationGroupId == groupId && item.CompanyId == companyId, cancellationToken);
        if (entity is null) return TransactionResult.Failure("The disclosure package was not found in this consolidation group.");
        if (entity.Status != "Draft") return TransactionResult.Failure("Only a draft disclosure package can be approved or rejected.");
        if (string.IsNullOrWhiteSpace(concurrencyToken) || entity.ConcurrencyToken != concurrencyToken) return TransactionResult.Failure("The disclosure package changed after it was displayed. Refresh before reviewing it.");
        if (entity.PreparedByUserId == userId) return TransactionResult.Failure("The person who prepared a disclosure package cannot approve or reject it.");
        if (!await db.ConsolidationGroups.AnyAsync(group => group.Id == groupId && group.CompanyId == companyId && group.IsActive, cancellationToken)) return TransactionResult.Failure("An inactive consolidation group cannot accept disclosure review decisions.");
        var accessError = await ValidateGroupAccessAsync(db, groupId, userId.Value, entity.AsOf, cancellationToken);
        if (accessError is not null) return TransactionResult.Failure(accessError);
        var retainedError = ValidateRetainedDisclosure(entity);
        if (retainedError is not null) return TransactionResult.Failure(retainedError);

        var now = DateTimeOffset.UtcNow;
        entity.Status = approve ? "Approved" : "Rejected";
        entity.DecisionReason = approve ? string.Empty : reason.Trim();
        entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (approve) { entity.ApprovedByUserId = userId; entity.ApprovedAtUtc = now; entity.RejectedByUserId = null; entity.RejectedAtUtc = null; }
        else { entity.RejectedByUserId = userId; entity.RejectedAtUtc = now; entity.ApprovedByUserId = null; entity.ApprovedAtUtc = null; }
        AddDisclosureAudit(db, companyId.Value, userId, approve ? "consolidation-disclosure.approved" : "consolidation-disclosure.rejected", entity, new { reason = entity.DecisionReason, contentSha256 = DisclosureSha256(entity.ContentJson) });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The disclosure package changed concurrently. Refresh and try again."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<ConsolidationDisclosureWorkspace?> GetDisclosureWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage)) return null;
        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == groupId && item.CompanyId == companyId, cancellationToken);
        if (group is null) return null;
        var memberIds = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == groupId).Select(item => item.MemberCompanyId).Distinct().ToArrayAsync(cancellationToken);
        var permitted = await db.CompanyMemberships.AsNoTracking().Where(item => item.UserId == userId && item.IsActive && memberIds.Contains(item.CompanyId)).Select(item => item.CompanyId).Distinct().CountAsync(cancellationToken);
        if (memberIds.Length == 0 || permitted != memberIds.Length) return null;
        var entities = await db.ConsolidationDisclosurePackages.AsNoTracking().Where(item => item.CompanyId == companyId && item.ConsolidationGroupId == groupId).OrderByDescending(item => item.AsOf).ThenBy(item => item.FrameworkCode).ToArrayAsync(cancellationToken);
        var userIds = entities.SelectMany(item => new[] { item.PreparedByUserId, item.ApprovedByUserId, item.RejectedByUserId }).Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(item => userIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => string.IsNullOrWhiteSpace(item.DisplayName) ? item.UserName : item.DisplayName, cancellationToken);
        var snapshots = new List<ConsolidationDisclosurePackageSnapshot>(entities.Length);
        foreach (var entity in entities)
        {
            var snapshot = ToDisclosureSnapshot(entity, users);
            if (snapshot is null) return null; // Corrupt retained JSON must fail closed instead of disappearing from review.
            snapshots.Add(snapshot);
        }
        return new(group.Id, group.Name, group.ReportingCurrency, snapshots);
    }

    private static string? ValidateDisclosureRequest(SaveConsolidationDisclosurePackageRequest request)
    {
        if (request.ConsolidationGroupId == Guid.Empty || request.PeriodStart == DateOnly.MinValue || request.PeriodStart > request.AsOf || request.Content is null)
            return "Choose a consolidation group and a valid exact reporting period.";
        var frameworkCode = request.FrameworkCode?.Trim() ?? string.Empty;
        var frameworkEdition = request.FrameworkEdition?.Trim() ?? string.Empty;
        if (frameworkCode is { Length: < 1 or > 32 } || frameworkEdition is { Length: < 1 or > 80 } || request.ReviewNotes?.Trim().Length > 2000)
            return "Provide a concise framework code, framework edition, and optional review notes.";
        if (request.Content.SchemaVersion != CurrentDisclosureSchemaVersion)
            return $"Disclosure schema version {request.Content.SchemaVersion} is not supported by this application. Import or convert it to schema version {CurrentDisclosureSchemaVersion} before approval.";
        if (request.Content.FinancingLiabilities is null || request.Content.SupplierFinanceArrangements is null || request.Content.NarrativeDisclosures is null
            || request.Content.FinancingLiabilities.Count + request.Content.SupplierFinanceArrangements.Count + request.Content.NarrativeDisclosures.Count == 0
            || request.Content.FinancingLiabilities.Count > 500 || request.Content.SupplierFinanceArrangements.Count > 500 || request.Content.NarrativeDisclosures.Count > 500)
            return "Provide at least one and no more than 500 entries in each disclosure section.";
        if (request.Content.FinancingLiabilities.Select(item => item.LiabilityCode?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Content.FinancingLiabilities.Count)
            return "Financing-liability codes must be unique within the disclosure package.";
        foreach (var row in request.Content.FinancingLiabilities)
        {
            if (!Concise(row.LiabilityCode, 64) || !Concise(row.LiabilityName, 160) || !Concise(row.BalanceSheetLine, 160) || !Concise(row.SourceReference, 1000)
                || row.OtherNonCashExplanation?.Trim().Length > 2000 || !ValidMoney(row.OpeningBalance, row.FinancingCashFlows, row.Acquisitions, row.Disposals, row.ForeignExchangeChanges, row.FairValueChanges, row.OtherNonCashChanges, row.ClosingBalance))
                return "Every financing-liability row requires concise identity, balance-sheet presentation, official or working-paper source, and currency amounts with no more than two decimal places.";
            var expectedClosing = row.OpeningBalance + row.FinancingCashFlows + row.Acquisitions + row.Disposals + row.ForeignExchangeChanges + row.FairValueChanges + row.OtherNonCashChanges;
            if (decimal.Round(expectedClosing, 2, MidpointRounding.AwayFromZero) != row.ClosingBalance)
                return $"Financing liability {row.LiabilityCode} does not reconcile from opening to closing balance.";
            if (row.OtherNonCashChanges != 0m && !Concise(row.OtherNonCashExplanation, 2000))
                return $"Financing liability {row.LiabilityCode} requires an explanation for other noncash changes.";
        }
        if (request.Content.SupplierFinanceArrangements.Select(item => item.ArrangementCode?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Content.SupplierFinanceArrangements.Count)
            return "Supplier-finance arrangement codes must be unique within the disclosure package.";
        foreach (var row in request.Content.SupplierFinanceArrangements)
        {
            if (!Concise(row.ArrangementCode, 64) || !Concise(row.ArrangementName, 160) || !Concise(row.KeyTerms, 4000) || !Concise(row.BalanceSheetLine, 160)
                || !Concise(row.LiquidityRiskNotes, 4000) || !Concise(row.SourceReference, 1000) || row.SecurityOrGuarantees?.Trim().Length > 2000
                || !ValidMoney(row.OpeningOutstanding, row.ObligationsConfirmed, row.ObligationsPaid, row.ClosingOutstanding, row.SuppliersAlreadyPaid)
                || row.OpeningOutstanding < 0m || row.ObligationsConfirmed < 0m || row.ObligationsPaid < 0m || row.ClosingOutstanding < 0m || row.SuppliersAlreadyPaid < 0m)
                return "Every supplier-finance row requires identity, substantive terms, balance-sheet presentation, liquidity-risk notes, source evidence, and nonnegative currency amounts.";
            if (decimal.Round(row.OpeningOutstanding + row.ObligationsConfirmed - row.ObligationsPaid, 2, MidpointRounding.AwayFromZero) != row.ClosingOutstanding)
                return $"Supplier-finance arrangement {row.ArrangementCode} does not reconcile from opening to closing outstanding obligations.";
            if (!ValidDayRange(row.PaymentDueMinimumDays, row.PaymentDueMaximumDays) || !ValidDayRange(row.ComparablePayablesDueMinimumDays, row.ComparablePayablesDueMaximumDays))
                return $"Supplier-finance arrangement {row.ArrangementCode} contains an invalid payment-due range.";
        }
        if (request.Content.NarrativeDisclosures.Select(item => $"{item.Category?.Trim()}|{item.Code?.Trim()}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Content.NarrativeDisclosures.Count)
            return "Narrative disclosure category/code pairs must be unique within the package.";
        foreach (var row in request.Content.NarrativeDisclosures)
            if (!Concise(row.Category, 64) || !Concise(row.Code, 64) || !Concise(row.Title, 200) || !Concise(row.Narrative, 20000) || !Concise(row.SourceReference, 1000) || row.SortOrder is < 0 or > 1_000_000)
                return "Every narrative disclosure requires a category, code, title, narrative, source reference, and a valid sort order.";
        return null;
    }

    private static string? ValidateRetainedDisclosure(ConsolidationDisclosurePackage entity)
    {
        ConsolidationDisclosureDocument? content;
        try { content = JsonSerializer.Deserialize<ConsolidationDisclosureDocument>(entity.ContentJson, DisclosureJsonOptions); }
        catch (JsonException) { return "The retained disclosure JSON is invalid and cannot be approved."; }
        if (content is null || content.SchemaVersion != entity.SchemaVersion) return "The retained disclosure document and schema metadata do not agree.";
        return ValidateDisclosureRequest(new(entity.Id, entity.ConsolidationGroupId, entity.PeriodStart, entity.AsOf, entity.FrameworkCode, entity.FrameworkEdition, content, entity.ReviewNotes, entity.ConcurrencyToken));
    }

    internal static ConsolidationDisclosurePackageSnapshot? ToDisclosureSnapshot(ConsolidationDisclosurePackage entity, IReadOnlyDictionary<Guid, string> users)
    {
        ConsolidationDisclosureDocument? content;
        try { content = JsonSerializer.Deserialize<ConsolidationDisclosureDocument>(entity.ContentJson, DisclosureJsonOptions); }
        catch (JsonException) { return null; }
        if (content is null || content.SchemaVersion != entity.SchemaVersion) return null;
        return new(entity.Id, entity.PeriodStart, entity.AsOf, entity.FrameworkCode, entity.FrameworkEdition, entity.SchemaVersion, DisclosureSha256(entity.ContentJson), content, entity.Status,
            entity.PreparedByUserId.HasValue ? users.GetValueOrDefault(entity.PreparedByUserId.Value, "Unavailable user") : "Unavailable user", entity.PreparedAtUtc,
            entity.ApprovedByUserId.HasValue ? users.GetValueOrDefault(entity.ApprovedByUserId.Value, "Unavailable user") : null, entity.ApprovedAtUtc,
            entity.RejectedByUserId.HasValue ? users.GetValueOrDefault(entity.RejectedByUserId.Value, "Unavailable user") : null, entity.RejectedAtUtc,
            entity.DecisionReason, entity.ReviewNotes, entity.ConcurrencyToken);
    }

    private static bool Concise(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum;
    private static bool ValidMoney(params decimal[] values) => values.All(value => value is >= -9999999999999999.99m and <= 9999999999999999.99m && decimal.Round(value, 2) == value);
    private static bool ValidDayRange(int? minimum, int? maximum) => (!minimum.HasValue && !maximum.HasValue) || (minimum is >= 0 and <= 36500 && maximum is >= 0 and <= 36500 && minimum <= maximum);
    private static string DisclosureSha256(string contentJson) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contentJson))).ToLowerInvariant();
    private static void AddDisclosureAudit(BrassLedgerDbContext db, Guid companyId, Guid? userId, string action, ConsolidationDisclosurePackage entity, object details) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = userId, Action = action, EntityType = nameof(ConsolidationDisclosurePackage), EntityId = entity.Id, DetailJson = JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow });
}
