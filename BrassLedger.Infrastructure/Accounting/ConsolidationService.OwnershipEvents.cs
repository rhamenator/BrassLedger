using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class ConsolidationService
{
    private const int CurrentOwnershipEventSchemaVersion = 2;
    private const int MaximumOwnershipEventJsonBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions OwnershipEventJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TransactionResult> SaveOwnershipEventAsync(SaveConsolidationOwnershipEventRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedgerPermissions.JournalPrepare))
            return TransactionResult.Failure("You are not authorized to prepare consolidation ownership events.");
        var validationError = ValidateOwnershipEventRequest(request);
        if (validationError is not null) return TransactionResult.Failure(validationError);
        _ = Enum.TryParse<ConsolidationOwnershipEventType>(request.EventType, true, out var eventType);
        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.Id == request.ConsolidationGroupId && item.CompanyId == companyId && item.IsActive, cancellationToken);
        if (group is null) return TransactionResult.Failure("The active consolidation group was not found in the active company.");
        var accessError = await ValidateOwnershipEventAccessAsync(db, group.Id, userId.Value, cancellationToken);
        if (accessError is not null) return TransactionResult.Failure(accessError);
        var subjectPeriods = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id && item.MemberCompanyId == request.SubjectCompanyId).ToArrayAsync(cancellationToken);
        if (subjectPeriods.Length == 0) return TransactionResult.Failure("The ownership-event subject is not retained in this consolidation group's ownership history.");
        var effectiveSubject = subjectPeriods.SingleOrDefault(item => item.EffectiveFrom <= request.EventDate && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EventDate));
        if (effectiveSubject is null) return TransactionResult.Failure("The ownership-event subject has no ownership period effective on the event date.");
        if (effectiveSubject.ConsolidationBasis != ConsolidationBasis.ControlledSubsidiary) return TransactionResult.Failure("Acquisition, disposal, ownership-change, and parent/NCI attribution schedules require a reviewed controlled-subsidiary ownership period on the event date.");
        var expectedOwnership = eventType == ConsolidationOwnershipEventType.LossOfControl ? request.Content.OwnershipBefore : request.Content.OwnershipAfter;
        if (effectiveSubject.OwnershipPercentage != expectedOwnership)
            return TransactionResult.Failure($"The subject's effective ownership period must equal the event's {(eventType == ConsolidationOwnershipEventType.LossOfControl ? "before" : "after")} ownership percentage.");
        var transitionError = ValidateOwnershipTransition(subjectPeriods, effectiveSubject, eventType, request.EventDate, request.Content);
        if (transitionError is not null) return TransactionResult.Failure(transitionError);
        var accountError = await ValidateOwnershipPostingAccountsAsync(db, group, request.EventDate, request.Content, cancellationToken);
        if (accountError is not null) return TransactionResult.Failure(accountError);

        var frameworkCode = request.FrameworkCode.Trim().ToUpperInvariant(); var frameworkEdition = request.FrameworkEdition.Trim(); var reference = request.Reference.Trim();
        var contentJson = JsonSerializer.Serialize(request.Content, OwnershipEventJsonOptions);
        if (Encoding.UTF8.GetByteCount(contentJson) > MaximumOwnershipEventJsonBytes) return TransactionResult.Failure("The ownership-event document exceeds the supported 2 MiB retained-document limit.");
        var entity = request.Id is { } id ? await db.ConsolidationOwnershipEvents.SingleOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId && item.ConsolidationGroupId == group.Id, cancellationToken) : null;
        if (request.Id is not null && entity is null) return TransactionResult.Failure("The consolidation ownership event was not found in this group.");
        if ((eventType is ConsolidationOwnershipEventType.AcquisitionOfControl or ConsolidationOwnershipEventType.StepAcquisition) && request.Content.SchemaVersion < CurrentOwnershipEventSchemaVersion)
            return TransactionResult.Failure($"New or corrected acquisition schedules require ownership-event schema version {CurrentOwnershipEventSchemaVersion} with line-item purchase-price-allocation detail.");
        if (entity is not null && entity.Status is not ("Draft" or "Rejected")) return TransactionResult.Failure("Only a draft or rejected ownership event can be edited.");
        if (entity is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || entity.ConcurrencyToken != request.ConcurrencyToken)) return TransactionResult.Failure("The ownership event changed after it was displayed. Refresh before saving it.");
        if (entity is not null && (entity.EventDate != request.EventDate || entity.EventType != eventType || entity.SubjectCompanyId != request.SubjectCompanyId)) return TransactionResult.Failure("A retained ownership event cannot be moved to another date, type, or subject. Create a separate event instead.");
        entity ??= new ConsolidationOwnershipEvent { Id = Guid.NewGuid(), CompanyId = companyId.Value, ConsolidationGroupId = group.Id, SubjectCompanyId = request.SubjectCompanyId, EventDate = request.EventDate, EventType = eventType };
        entity.Reference = reference; entity.FrameworkCode = frameworkCode; entity.FrameworkEdition = frameworkEdition; entity.SchemaVersion = request.Content.SchemaVersion; entity.ContentJson = contentJson;
        entity.Status = "Draft"; entity.PreparedByUserId = userId; entity.PreparedAtUtc = DateTimeOffset.UtcNow; entity.ApprovedByUserId = null; entity.ApprovedAtUtc = null; entity.RejectedByUserId = null; entity.RejectedAtUtc = null; entity.PostedByUserId = null; entity.PostedAtUtc = null; entity.DecisionReason = string.Empty; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(entity).State == EntityState.Detached) db.ConsolidationOwnershipEvents.Add(entity);
        AddOwnershipEventAudit(db, companyId.Value, userId, request.Id is null ? "consolidation-ownership-event.prepared" : "consolidation-ownership-event.updated", entity, new { entity.EventType, entity.EventDate, entity.SubjectCompanyId, entity.FrameworkCode, entity.FrameworkEdition, contentSha256 = OwnershipEventSha256(contentJson), postingLineCount = request.Content.PostingLines.Count });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The ownership event changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The ownership-event reference conflicts with another retained event in this group."); }
        return TransactionResult.Success(entity.Id);
    }

    public Task<TransactionResult> ApproveOwnershipEventAsync(ConsolidationOwnershipEventActionRequest request, CancellationToken cancellationToken = default) =>
        DecideOwnershipEventAsync(request.ConsolidationGroupId, request.OwnershipEventId, request.ConcurrencyToken, true, string.Empty, cancellationToken);

    public Task<TransactionResult> RejectOwnershipEventAsync(ConsolidationOwnershipEventDecisionRequest request, CancellationToken cancellationToken = default) =>
        DecideOwnershipEventAsync(request.ConsolidationGroupId, request.OwnershipEventId, request.ConcurrencyToken, false, request.Reason, cancellationToken);

    private async Task<TransactionResult> DecideOwnershipEventAsync(Guid groupId, Guid eventId, string concurrencyToken, bool approve, string reason, CancellationToken cancellationToken)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedgerPermissions.JournalApprove)) return TransactionResult.Failure("You are not authorized to review consolidation ownership events.");
        if (!approve && !ConciseOwnershipText(reason, 1000)) return TransactionResult.Failure("A concise rejection reason is required.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ConsolidationOwnershipEvents.SingleOrDefaultAsync(item => item.Id == eventId && item.ConsolidationGroupId == groupId && item.CompanyId == companyId, cancellationToken);
        if (entity is null) return TransactionResult.Failure("The ownership event was not found in this group.");
        if (entity.Status != "Draft") return TransactionResult.Failure("Only a draft ownership event can be approved or rejected.");
        if (string.IsNullOrWhiteSpace(concurrencyToken) || entity.ConcurrencyToken != concurrencyToken) return TransactionResult.Failure("The ownership event changed after it was displayed. Refresh before reviewing it.");
        if (entity.PreparedByUserId == userId) return TransactionResult.Failure("The person who prepared an ownership event cannot approve or reject it.");
        if (approve && (entity.EventType is ConsolidationOwnershipEventType.AcquisitionOfControl or ConsolidationOwnershipEventType.StepAcquisition) && entity.SchemaVersion < CurrentOwnershipEventSchemaVersion)
            return TransactionResult.Failure($"Convert the acquisition schedule to schema version {CurrentOwnershipEventSchemaVersion} with line-item purchase-price-allocation detail before approval.");
        if (!await db.ConsolidationGroups.AnyAsync(group => group.Id == groupId && group.CompanyId == companyId && group.IsActive, cancellationToken)) return TransactionResult.Failure("An inactive consolidation group cannot accept ownership-event review decisions.");
        var accessError = await ValidateOwnershipEventAccessAsync(db, groupId, userId.Value, cancellationToken); if (accessError is not null) return TransactionResult.Failure(accessError);
        var retainedError = await ValidateRetainedOwnershipEventAsync(db, entity, cancellationToken); if (retainedError is not null) return TransactionResult.Failure(retainedError);
        var now = DateTimeOffset.UtcNow; entity.Status = approve ? "Approved" : "Rejected"; entity.DecisionReason = approve ? string.Empty : reason.Trim(); entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (approve) { entity.ApprovedByUserId = userId; entity.ApprovedAtUtc = now; entity.RejectedByUserId = null; entity.RejectedAtUtc = null; }
        else { entity.RejectedByUserId = userId; entity.RejectedAtUtc = now; entity.ApprovedByUserId = null; entity.ApprovedAtUtc = null; }
        AddOwnershipEventAudit(db, companyId.Value, userId, approve ? "consolidation-ownership-event.approved" : "consolidation-ownership-event.rejected", entity, new { entity.DecisionReason, contentSha256 = OwnershipEventSha256(entity.ContentJson) });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The ownership event changed concurrently. Refresh and try again."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> PostOwnershipEventAsync(ConsolidationOwnershipEventActionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedgerPermissions.JournalPost)) return TransactionResult.Failure("You are not authorized to post consolidation ownership events.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ConsolidationOwnershipEvents.SingleOrDefaultAsync(item => item.Id == request.OwnershipEventId && item.ConsolidationGroupId == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (entity is null) return TransactionResult.Failure("The approved ownership event was not found in this group.");
        if (entity.Status != "Approved") return TransactionResult.Failure("Only an approved ownership event can be posted.");
        if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || entity.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The ownership event changed after it was displayed. Refresh before posting it.");
        if (entity.ApprovedByUserId == userId) return TransactionResult.Failure("The person who approved an ownership event cannot post it.");
        if ((entity.EventType is ConsolidationOwnershipEventType.AcquisitionOfControl or ConsolidationOwnershipEventType.StepAcquisition) && entity.SchemaVersion < CurrentOwnershipEventSchemaVersion)
            return TransactionResult.Failure($"Convert the acquisition schedule to schema version {CurrentOwnershipEventSchemaVersion} before posting.");
        var accessError = await ValidateOwnershipEventAccessAsync(db, entity.ConsolidationGroupId, userId.Value, cancellationToken); if (accessError is not null) return TransactionResult.Failure(accessError);
        if (await db.AccountingPeriods.AnyAsync(period => period.CompanyId == companyId && period.Status == "Closed" && period.StartsOn <= entity.EventDate && period.EndsOn >= entity.EventDate, cancellationToken)) return TransactionResult.Failure("The ownership-event date is in a closed parent-company accounting period. Reopen the period before posting.");
        var retainedError = await ValidateRetainedOwnershipEventAsync(db, entity, cancellationToken); if (retainedError is not null) return TransactionResult.Failure(retainedError);
        entity.Status = "Posted"; entity.PostedByUserId = userId; entity.PostedAtUtc = DateTimeOffset.UtcNow; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddOwnershipEventAudit(db, companyId.Value, userId, "consolidation-ownership-event.posted", entity, new { entity.ApprovedByUserId, entity.ApprovedAtUtc, contentSha256 = OwnershipEventSha256(entity.ContentJson) });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The ownership event changed concurrently. Refresh and try again."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> ReverseOwnershipEventAsync(ReverseConsolidationOwnershipEventRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedgerPermissions.JournalReverse)) return TransactionResult.Failure("You are not authorized to reverse consolidation ownership events.");
        if (!ConciseOwnershipText(request.Reason, 1000)) return TransactionResult.Failure("A concise reversal reason is required.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var original = await db.ConsolidationOwnershipEvents.SingleOrDefaultAsync(item => item.Id == request.OwnershipEventId && item.ConsolidationGroupId == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (original is null) return TransactionResult.Failure("The posted ownership event was not found in this group.");
        if (original.Status != "Posted" || original.ReversedByEventId.HasValue || original.ReversalOfEventId.HasValue) return TransactionResult.Failure("Only an unreversed original posted ownership event can be reversed.");
        if (request.ReversalDate < original.EventDate) return TransactionResult.Failure("The reversal date cannot precede the original ownership-event date.");
        if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || original.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The ownership event changed after it was displayed. Refresh before reversing it.");
        var accessError = await ValidateOwnershipEventAccessAsync(db, original.ConsolidationGroupId, userId.Value, cancellationToken); if (accessError is not null) return TransactionResult.Failure(accessError);
        if (await db.AccountingPeriods.AnyAsync(period => period.CompanyId == companyId && period.Status == "Closed" && period.StartsOn <= request.ReversalDate && period.EndsOn >= request.ReversalDate, cancellationToken)) return TransactionResult.Failure("The ownership-event reversal date is in a closed parent-company accounting period. Reopen the period before reversing.");
        var content = DeserializeOwnershipEvent(original); if (content is null) return TransactionResult.Failure("The retained ownership-event JSON is invalid and cannot be reversed.");
        var reversalContent = content with { PostingLines = content.PostingLines.Select(line => line with { Debit = line.Credit, Credit = line.Debit, Description = Truncate($"Reversal: {line.Description}", 1000) }).ToArray() };
        var reversalId = Guid.NewGuid(); var reversalReference = Truncate($"REV-{original.Reference}-{reversalId:N}", 64); var now = DateTimeOffset.UtcNow;
        var reversal = new ConsolidationOwnershipEvent { Id = reversalId, CompanyId = original.CompanyId, ConsolidationGroupId = original.ConsolidationGroupId, SubjectCompanyId = original.SubjectCompanyId, EventDate = request.ReversalDate, EventType = original.EventType, Reference = reversalReference, FrameworkCode = original.FrameworkCode, FrameworkEdition = original.FrameworkEdition, SchemaVersion = original.SchemaVersion, ContentJson = JsonSerializer.Serialize(reversalContent, OwnershipEventJsonOptions), Status = "Posted", PreparedByUserId = userId, PreparedAtUtc = now, ApprovedByUserId = userId, ApprovedAtUtc = now, PostedByUserId = userId, PostedAtUtc = now, ReversalOfEventId = original.Id, ReversalReason = request.Reason.Trim(), ConcurrencyToken = Guid.NewGuid().ToString("N") };
        original.Status = "Reversed"; original.ReversedByEventId = reversal.Id; original.ReversalReason = request.Reason.Trim(); original.ConcurrencyToken = Guid.NewGuid().ToString("N"); db.ConsolidationOwnershipEvents.Add(reversal);
        AddOwnershipEventAudit(db, companyId.Value, userId, "consolidation-ownership-event.reversed", original, new { reversalId, request.ReversalDate, reason = request.Reason.Trim() });
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The ownership event changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The ownership event was already reversed or conflicts with another retained reversal."); }
        return TransactionResult.Success(reversal.Id);
    }

    public async Task<ConsolidationOwnershipEventWorkspace?> GetOwnershipEventWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage)) return null;
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == groupId && item.CompanyId == companyId, cancellationToken); if (group is null) return null;
        if (await ValidateOwnershipEventAccessAsync(db, groupId, userId.Value, cancellationToken) is not null) return null;
        var members = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == groupId).ToArrayAsync(cancellationToken);
        var companyIds = members.Select(item => item.MemberCompanyId).Distinct().ToArray(); var companies = await db.Companies.AsNoTracking().Where(item => companyIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var entities = await db.ConsolidationOwnershipEvents.AsNoTracking().Where(item => item.CompanyId == companyId && item.ConsolidationGroupId == groupId).OrderByDescending(item => item.EventDate).ThenBy(item => item.Reference).ToArrayAsync(cancellationToken);
        var userIds = entities.SelectMany(item => new[] { item.PreparedByUserId, item.ApprovedByUserId, item.RejectedByUserId, item.PostedByUserId }).Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(item => userIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => string.IsNullOrWhiteSpace(item.DisplayName) ? item.UserName : item.DisplayName, cancellationToken);
        var mappedAccounts = await db.ConsolidationAccountMappings.AsNoTracking().Where(item => item.ConsolidationGroupId == groupId).Select(item => new { item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType }).ToArrayAsync(cancellationToken);
        var mappings = mappedAccounts.Select(item => new ConsolidationReportingAccountSnapshot(item.ReportingAccountNumber, item.ReportingAccountName, item.ReportingAccountType.ToString())).Distinct().ToList();
        if (!string.IsNullOrWhiteSpace(group.NciAccountNumber) && !mappings.Any(item => item.AccountNumber == group.NciAccountNumber)) mappings.Add(new(group.NciAccountNumber, group.NciAccountName, nameof(AccountType.Equity)));
        var snapshots = new List<ConsolidationOwnershipEventSnapshot>();
        foreach (var entity in entities) { var snapshot = ToOwnershipEventSnapshot(entity, companies, users); if (snapshot is null) return null; snapshots.Add(snapshot); }
        return new(group.Id, group.Name, group.ReportingCurrency, members.OrderBy(item => companies[item.MemberCompanyId].Name).ThenBy(item => item.EffectiveFrom).Select(item => new ConsolidationGroupMemberSnapshot(item.Id, item.MemberCompanyId, companies[item.MemberCompanyId].Name, companies[item.MemberCompanyId].BaseCurrency, item.OwnershipPercentage, item.EffectiveFrom, item.EffectiveThrough, item.ConcurrencyToken, item.ConsolidationBasis.ToString(), item.BasisRationale, item.BasisReviewedOn)).ToArray(), mappings.OrderBy(item => item.AccountNumber).ToArray(), snapshots);
    }

    private static string? ValidateOwnershipEventRequest(SaveConsolidationOwnershipEventRequest request)
    {
        if (request.ConsolidationGroupId == Guid.Empty || request.SubjectCompanyId == Guid.Empty || request.EventDate == DateOnly.MinValue || request.Content is null || !Enum.TryParse<ConsolidationOwnershipEventType>(request.EventType, true, out var eventType) || !Enum.IsDefined(eventType)) return "Choose a consolidation group, subject, event date, and supported ownership-event type.";
        if (!ConciseOwnershipText(request.Reference, 64) || !ConciseOwnershipText(request.FrameworkCode, 32) || !ConciseOwnershipText(request.FrameworkEdition, 80) || !ConciseOwnershipText(request.Content.MeasurementRationale, 4000) || !ConciseOwnershipText(request.Content.SourceReference, 1000)) return "Provide concise event identity, accounting framework, measurement rationale, and source evidence.";
        if (request.Content.SchemaVersion is < 1 or > CurrentOwnershipEventSchemaVersion) return $"Ownership-event schema version {request.Content.SchemaVersion} is not supported. Convert it to a supported version from 1 through {CurrentOwnershipEventSchemaVersion}.";
        if (!ValidOwnershipPercentage(request.Content.OwnershipBefore) || !ValidOwnershipPercentage(request.Content.OwnershipAfter)) return "Before and after ownership percentages must be from 0% through 100%.";
        var measurementCount = new object?[] { request.Content.Acquisition, request.Content.OwnershipChange, request.Content.LossOfControl, request.Content.ProfitAttribution }.Count(item => item is not null);
        if (measurementCount != 1) return "Retain exactly one measurement schedule matching the ownership-event type.";
        if (request.Content.PostingLines is null || request.Content.PostingLines.Count is < 2 or > 500) return "An ownership event requires 2 through 500 balanced posting lines.";
        foreach (var line in request.Content.PostingLines)
            if (!Enum.TryParse<AccountType>(line.ReportingAccountType, true, out var accountType) || !Enum.IsDefined(accountType) || !ConciseOwnershipText(line.ReportingAccountNumber, 64) || !ConciseOwnershipText(line.ReportingAccountName, 160) || line.Description?.Trim().Length > 1000 || !ValidOwnershipMoney(line.Debit, line.Credit) || line.Debit < 0m || line.Credit < 0m || (line.Debit == 0m) == (line.Credit == 0m)) return "Every ownership-event posting line requires a valid account and exactly one positive debit or credit with cent precision.";
        if (request.Content.PostingLines.Sum(line => line.Debit) != request.Content.PostingLines.Sum(line => line.Credit)) return "Ownership-event posting debits and credits must balance exactly.";
        var hasNominalLines = request.Content.PostingLines.Any(line => line.ReportingAccountType is nameof(AccountType.Revenue) or nameof(AccountType.Expense));
        if (hasNominalLines && (!ConciseOwnershipText(request.Content.PriorPeriodEquityAccountNumber, 64) || !ConciseOwnershipText(request.Content.PriorPeriodEquityAccountName, 160))) return "Events with income-statement postings require a retained-earnings account for later reporting periods.";
        if (!hasNominalLines && ((request.Content.PriorPeriodEquityAccountNumber?.Trim().Length ?? 0) > 64 || (request.Content.PriorPeriodEquityAccountName?.Trim().Length ?? 0) > 160)) return "The optional retained-earnings account is too long.";
        return eventType switch
        {
            ConsolidationOwnershipEventType.AcquisitionOfControl or ConsolidationOwnershipEventType.StepAcquisition => ValidateAcquisitionMeasurement(request.Content, request.FrameworkCode, eventType, request.EventDate),
            ConsolidationOwnershipEventType.OwnershipChangeWithoutLossOfControl => ValidateOwnershipChangeMeasurement(request.Content),
            ConsolidationOwnershipEventType.LossOfControl => ValidateLossOfControlMeasurement(request.Content),
            ConsolidationOwnershipEventType.ProfitAttribution => ValidateProfitAttributionMeasurement(request.Content),
            _ => "The ownership-event type is unsupported."
        };
    }

    private static string? ValidateAcquisitionMeasurement(ConsolidationOwnershipEventDocument content, string frameworkCode, ConsolidationOwnershipEventType eventType, DateOnly eventDate)
    {
        var value = content.Acquisition; if (value is null || content.OwnershipChange is not null || content.LossOfControl is not null || content.ProfitAttribution is not null || content.OwnershipAfter <= content.OwnershipBefore) return "An acquisition requires its acquisition measurement and an increase in ownership.";
        if (!ValidOwnershipMoney(value.ConsiderationTransferred, value.PreviousInterestFairValue, value.NoncontrollingInterestRecognized, value.IdentifiableNetAssetsFairValue, value.Goodwill, value.BargainPurchaseGain) || value.ConsiderationTransferred < 0m || value.PreviousInterestFairValue < 0m || value.NoncontrollingInterestRecognized < 0m || value.Goodwill < 0m || value.BargainPurchaseGain < 0m || (value.Goodwill > 0m && value.BargainPurchaseGain > 0m)) return "Acquisition measurements must use valid nonnegative consideration, prior-interest, NCI, goodwill, and bargain-gain amounts.";
        if (eventType == ConsolidationOwnershipEventType.AcquisitionOfControl && value.PreviousInterestFairValue != 0m) return "An acquisition from 0% ownership cannot include a previously held interest; use a step-acquisition schedule.";
        if (eventType == ConsolidationOwnershipEventType.StepAcquisition && value.PreviousInterestFairValue <= 0m) return "A step acquisition requires the fair value of the positive previously held interest.";
        var residual = decimal.Round(value.ConsiderationTransferred + value.PreviousInterestFairValue + value.NoncontrollingInterestRecognized - value.IdentifiableNetAssetsFairValue, 2, MidpointRounding.AwayFromZero);
        if (value.Goodwill != decimal.Max(residual, 0m) || value.BargainPurchaseGain != decimal.Max(-residual, 0m)) return "Goodwill or bargain purchase gain does not reconcile to consideration, prior interest, NCI, and identifiable net assets.";
        var method = content.NciMeasurementMethod?.Trim() ?? string.Empty; if (method is not ("FullFairValue" or "ProportionateShare" or "NotApplicable")) return "Use FullFairValue, ProportionateShare, or NotApplicable for the NCI measurement method.";
        if (content.OwnershipAfter == 1m && (value.NoncontrollingInterestRecognized != 0m || method != "NotApplicable")) return "A wholly owned acquisition must recognize no NCI and use the NotApplicable NCI measurement method.";
        if (content.OwnershipAfter < 1m && method == "NotApplicable") return "A partially owned acquisition requires an explicit FullFairValue or ProportionateShare NCI measurement method.";
        if (frameworkCode.Trim().Equals("US-GAAP", StringComparison.OrdinalIgnoreCase) && content.OwnershipAfter < 1m && method != "FullFairValue") return "US GAAP acquisitions with NCI require the FullFairValue measurement method.";
        if (content.SchemaVersion >= 2)
        {
            var detailError = ValidateAcquisitionDetail(value, frameworkCode, eventDate); if (detailError is not null) return detailError;
        }
        return null;
    }

    private static string? ValidateAcquisitionDetail(AcquisitionOfControlMeasurement value, string frameworkCode, DateOnly eventDate)
    {
        var components = value.ConsiderationComponents ?? []; var items = value.IdentifiableItems ?? []; var adjustments = value.MeasurementPeriodAdjustments ?? [];
        if (components.Count is < 1 or > 200) return "Schema-v2 acquisition schedules require 1 through 200 consideration components.";
        if (items.Count is < 1 or > 500) return "Schema-v2 acquisition schedules require 1 through 500 identifiable asset or liability items.";
        if (components.Any(item => item is null) || items.Any(item => item is null) || adjustments.Any(item => item is null)) return "Schema-v2 acquisition detail cannot contain null line items.";
        if (value.MeasurementPeriodEndsOn is not { } measurementPeriodEnd || measurementPeriodEnd < eventDate) return "Schema-v2 acquisition schedules require a measurement-period end date on or after the acquisition date.";
        var framework = frameworkCode.Trim().ToUpperInvariant();
        if (framework is "US-GAAP" or "IFRS" or "IFRS-SME" && eventDate <= DateOnly.MaxValue.AddYears(-1) && measurementPeriodEnd > eventDate.AddYears(1))
            return "The retained US GAAP or IFRS measurement period cannot extend beyond one year after the acquisition date.";
        var componentTypes = new[] { "Cash", "EquityIssued", "ContingentConsideration", "DeferredConsideration", "ReplacementAward", "Other" };
        if (components.Select(item => item.Code?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != components.Count) return "Consideration-component codes must be unique within the acquisition schedule.";
        foreach (var item in components)
            if (!ConciseOwnershipText(item.Code, 64) || !ConciseOwnershipText(item.Description, 300) || !ConciseOwnershipText(item.SourceReference, 1000) || !componentTypes.Contains(item.ComponentType, StringComparer.Ordinal) || !ValidOwnershipMoney(item.FairValue) || item.FairValue < 0m)
                return "Every consideration component requires a unique concise code, description, supported type, nonnegative fair value, and source reference.";
        if (components.Sum(item => item.FairValue) != value.ConsiderationTransferred) return "Consideration-component fair values must equal total consideration transferred.";
        if (items.Select(item => item.Code?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count) return "Identifiable-item codes must be unique within the acquisition schedule.";
        foreach (var item in items)
            if (!ConciseOwnershipText(item.Code, 64) || !ConciseOwnershipText(item.Description, 300) || !ConciseOwnershipText(item.SourceReference, 1000) || item.ItemType is not ("Asset" or "Liability")
                || !ValidOwnershipMoney(item.FairValue, item.DeferredTaxAsset, item.DeferredTaxLiability) || item.FairValue < 0m || item.DeferredTaxAsset < 0m || item.DeferredTaxLiability < 0m || (item.DeferredTaxAsset > 0m && item.DeferredTaxLiability > 0m))
                return "Every identifiable item requires a unique concise code, description, Asset or Liability type, nonnegative fair value, at most one deferred-tax effect, and source reference.";
        var netAssets = items.Sum(item => item.ItemType == "Asset" ? item.FairValue : -item.FairValue) + items.Sum(item => item.DeferredTaxAsset - item.DeferredTaxLiability);
        if (netAssets != value.IdentifiableNetAssetsFairValue) return "Identifiable asset, liability, and deferred-tax detail must equal total identifiable net assets at fair value.";
        if (adjustments.Count > 200) return "An acquisition schedule supports at most 200 measurement-period adjustments.";
        if (adjustments.Select(item => item.Code?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != adjustments.Count) return "Measurement-period adjustment codes must be unique within the acquisition schedule.";
        foreach (var item in adjustments)
        {
            if (item.RecognizedOn < eventDate || item.RecognizedOn > measurementPeriodEnd || !ConciseOwnershipText(item.Code, 64) || !ConciseOwnershipText(item.Description, 500) || !ConciseOwnershipText(item.SourceReference, 1000)
                || !ValidOwnershipMoney(item.ConsiderationChange, item.PreviousInterestFairValueChange, item.NoncontrollingInterestChange, item.IdentifiableNetAssetsChange, item.GoodwillChange, item.BargainPurchaseGainChange))
                return "Every measurement-period adjustment requires an in-period date, unique concise code, description, valid signed currency changes, and source reference.";
            var residualChange = item.ConsiderationChange + item.PreviousInterestFairValueChange + item.NoncontrollingInterestChange - item.IdentifiableNetAssetsChange;
            if (residualChange != item.GoodwillChange - item.BargainPurchaseGainChange) return "Each measurement-period adjustment must reconcile its consideration, prior-interest, NCI, net-asset, goodwill, and bargain-gain changes.";
        }
        return null;
    }

    private static string? ValidateOwnershipChangeMeasurement(ConsolidationOwnershipEventDocument content)
    {
        var value = content.OwnershipChange; if (value is null || content.Acquisition is not null || content.LossOfControl is not null || content.ProfitAttribution is not null || content.OwnershipAfter == content.OwnershipBefore || content.OwnershipAfter == 0m) return "An ownership change without loss of control requires its equity measurement and a nonzero changed ownership percentage.";
        if (!ValidOwnershipMoney(value.ConsiderationPaid, value.ConsiderationReceived, value.NoncontrollingInterestIncrease, value.NoncontrollingInterestDecrease, value.ParentEquityDebit, value.ParentEquityCredit) || new[] { value.ConsiderationPaid, value.ConsiderationReceived, value.NoncontrollingInterestIncrease, value.NoncontrollingInterestDecrease, value.ParentEquityDebit, value.ParentEquityCredit }.Any(amount => amount < 0m)) return "Ownership-change measurements must be nonnegative currency amounts with cent precision.";
        if (value.NoncontrollingInterestDecrease + value.ParentEquityDebit + value.ConsiderationReceived != value.NoncontrollingInterestIncrease + value.ParentEquityCredit + value.ConsiderationPaid) return "The ownership-change NCI, parent-equity, and consideration measurements do not reconcile.";
        return null;
    }

    private static string? ValidateLossOfControlMeasurement(ConsolidationOwnershipEventDocument content)
    {
        var value = content.LossOfControl; if (value is null || content.Acquisition is not null || content.OwnershipChange is not null || content.ProfitAttribution is not null || content.OwnershipAfter >= content.OwnershipBefore) return "A loss-of-control event requires its derecognition measurement and reduced ownership.";
        if (!ValidOwnershipMoney(value.ConsiderationReceived, value.RetainedInterestFairValue, value.NoncontrollingInterestDerecognized, value.NetAssetsDerecognized, value.GoodwillDerecognized, value.OciReclassification, value.GainOrLoss) || new[] { value.ConsiderationReceived, value.RetainedInterestFairValue, value.NoncontrollingInterestDerecognized, value.NetAssetsDerecognized, value.GoodwillDerecognized }.Any(amount => amount < 0m)) return "Loss-of-control measurements require valid nonnegative proceeds, retained interest, NCI, net-assets, and goodwill amounts; OCI and gain/loss may be signed.";
        var expected = decimal.Round(value.ConsiderationReceived + value.RetainedInterestFairValue + value.NoncontrollingInterestDerecognized - value.NetAssetsDerecognized - value.GoodwillDerecognized + value.OciReclassification, 2, MidpointRounding.AwayFromZero);
        return expected == value.GainOrLoss ? null : "The loss-of-control gain or loss does not reconcile to proceeds, retained interest, NCI, derecognized net assets/goodwill, and OCI reclassification.";
    }

    private static string? ValidateProfitAttributionMeasurement(ConsolidationOwnershipEventDocument content)
    {
        var value = content.ProfitAttribution; if (value is null || content.Acquisition is not null || content.OwnershipChange is not null || content.LossOfControl is not null || content.OwnershipAfter != content.OwnershipBefore || content.OwnershipAfter <= 0m) return "Profit attribution requires its attribution measurement and unchanged positive ownership.";
        if (!ValidOwnershipMoney(value.SubsidiaryProfitOrLoss, value.ParentProfitOrLoss, value.NoncontrollingInterestProfitOrLoss, value.SubsidiaryOtherComprehensiveIncome, value.ParentOtherComprehensiveIncome, value.NoncontrollingInterestOtherComprehensiveIncome)) return "Profit-attribution measurements require currency amounts with cent precision.";
        if (value.SubsidiaryProfitOrLoss != value.ParentProfitOrLoss + value.NoncontrollingInterestProfitOrLoss || value.SubsidiaryOtherComprehensiveIncome != value.ParentOtherComprehensiveIncome + value.NoncontrollingInterestOtherComprehensiveIncome) return "Subsidiary profit/loss and OCI must each reconcile exactly between parent and NCI attribution.";
        return null;
    }

    private static async Task<string?> ValidateOwnershipPostingAccountsAsync(BrassLedgerDbContext db, ConsolidationGroup group, DateOnly eventDate, ConsolidationOwnershipEventDocument content, CancellationToken cancellationToken)
    {
        var accounts = await EffectiveReportingAccountsAsync(db, group.Id, eventDate, cancellationToken);
        foreach (var line in content.PostingLines)
        {
            _ = Enum.TryParse<AccountType>(line.ReportingAccountType, true, out var type); var account = (line.ReportingAccountNumber.Trim(), line.ReportingAccountName.Trim(), type);
            if (account.Item1 == group.CtaAccountNumber) return "The system-controlled CTA account cannot be used by an ownership-event posting.";
            var isNci = account == (group.NciAccountNumber, group.NciAccountName, AccountType.Equity);
            if (!isNci && !accounts.Contains(account)) return $"Ownership-event reporting account {account.Item1} · {account.Item2} is not established by an effective mapping on {eventDate:yyyy-MM-dd}.";
        }
        if (content.PostingLines.Any(line => line.ReportingAccountType is nameof(AccountType.Revenue) or nameof(AccountType.Expense)))
        {
            var carryforward = (content.PriorPeriodEquityAccountNumber.Trim(), content.PriorPeriodEquityAccountName.Trim(), AccountType.Equity);
            if (!accounts.Contains(carryforward) && carryforward != (group.NciAccountNumber, group.NciAccountName, AccountType.Equity)) return "The retained-earnings carryforward account is not an effective mapped equity account.";
        }
        return null;
    }

    private static async Task<string?> ValidateRetainedOwnershipEventAsync(BrassLedgerDbContext db, ConsolidationOwnershipEvent entity, CancellationToken cancellationToken)
    {
        var content = DeserializeOwnershipEvent(entity); if (content is null) return "The retained ownership-event JSON is invalid or disagrees with its schema metadata.";
        var validation = ValidateOwnershipEventRequest(new(entity.Id, entity.ConsolidationGroupId, entity.SubjectCompanyId, entity.EventDate, entity.EventType.ToString(), entity.Reference, entity.FrameworkCode, entity.FrameworkEdition, content, entity.ConcurrencyToken)); if (validation is not null && !entity.ReversalOfEventId.HasValue) return validation;
        if (!entity.ReversalOfEventId.HasValue)
        {
            var subjectPeriods = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == entity.ConsolidationGroupId && item.MemberCompanyId == entity.SubjectCompanyId).ToArrayAsync(cancellationToken);
            var effectiveSubject = subjectPeriods.SingleOrDefault(item => item.EffectiveFrom <= entity.EventDate && (item.EffectiveThrough == null || item.EffectiveThrough >= entity.EventDate));
            if (effectiveSubject is null || effectiveSubject.ConsolidationBasis != ConsolidationBasis.ControlledSubsidiary) return "The retained event no longer has a reviewed controlled-subsidiary ownership period effective on its accounting date.";
            var transitionError = ValidateOwnershipTransition(subjectPeriods, effectiveSubject, entity.EventType, entity.EventDate, content); if (transitionError is not null) return transitionError;
        }
        var accountValidationDate = entity.EventDate;
        if (entity.ReversalOfEventId.HasValue)
            accountValidationDate = await db.ConsolidationOwnershipEvents.AsNoTracking().Where(item => item.Id == entity.ReversalOfEventId.Value).Select(item => item.EventDate).SingleOrDefaultAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleAsync(item => item.Id == entity.ConsolidationGroupId, cancellationToken); return await ValidateOwnershipPostingAccountsAsync(db, group, accountValidationDate, content, cancellationToken);
    }

    private static string? ValidateOwnershipTransition(IReadOnlyList<ConsolidationGroupCompany> periods, ConsolidationGroupCompany effectiveSubject,
        ConsolidationOwnershipEventType eventType, DateOnly eventDate, ConsolidationOwnershipEventDocument content)
    {
        var priorDate = eventDate == DateOnly.MinValue ? (DateOnly?)null : eventDate.AddDays(-1);
        var prior = priorDate.HasValue ? periods.SingleOrDefault(item => item.EffectiveFrom <= priorDate.Value && (item.EffectiveThrough == null || item.EffectiveThrough >= priorDate.Value)) : null;
        switch (eventType)
        {
            case ConsolidationOwnershipEventType.AcquisitionOfControl:
                if (effectiveSubject.EffectiveFrom != eventDate || content.OwnershipBefore != 0m)
                    return "An acquisition of control must be dated on the controlled-subsidiary period's first day and begin from 0% ownership. Use a step-acquisition schedule for a previously held interest.";
                if (prior is not null)
                    return "An acquisition of control cannot have an ownership period effective immediately before control begins. Close the prior interest and use a step-acquisition schedule when appropriate.";
                break;
            case ConsolidationOwnershipEventType.StepAcquisition:
                if (effectiveSubject.EffectiveFrom != eventDate || content.OwnershipBefore <= 0m || prior is null || prior.OwnershipPercentage != content.OwnershipBefore || prior.ConsolidationBasis == ConsolidationBasis.ControlledSubsidiary)
                    return "A step acquisition must be dated on the controlled-subsidiary period's first day and match a positive, immediately preceding noncontrolled ownership period.";
                break;
            case ConsolidationOwnershipEventType.OwnershipChangeWithoutLossOfControl:
                if (effectiveSubject.EffectiveFrom != eventDate || prior is null || prior.ConsolidationBasis != ConsolidationBasis.ControlledSubsidiary || prior.OwnershipPercentage != content.OwnershipBefore)
                    return "An ownership change without loss of control must be dated on a successor controlled-subsidiary period whose immediately preceding controlled period matches the before ownership.";
                break;
            case ConsolidationOwnershipEventType.LossOfControl:
                if (effectiveSubject.EffectiveThrough != eventDate)
                    return "A loss-of-control event must be dated on the final day of the controlled-subsidiary ownership period.";
                break;
            case ConsolidationOwnershipEventType.ProfitAttribution:
                break;
        }
        return null;
    }

    private static ConsolidationOwnershipEventDocument? DeserializeOwnershipEvent(ConsolidationOwnershipEvent entity)
    {
        try { var content = JsonSerializer.Deserialize<ConsolidationOwnershipEventDocument>(entity.ContentJson, OwnershipEventJsonOptions); return content?.SchemaVersion == entity.SchemaVersion ? content : null; }
        catch (JsonException) { return null; }
    }

    internal static ConsolidationOwnershipEventSnapshot? ToOwnershipEventSnapshot(ConsolidationOwnershipEvent entity, IReadOnlyDictionary<Guid, Company> companies, IReadOnlyDictionary<Guid, string> users)
    {
        var content = DeserializeOwnershipEvent(entity); if (content is null || !companies.TryGetValue(entity.SubjectCompanyId, out var subject)) return null;
        return new(entity.Id, entity.SubjectCompanyId, subject.Name, entity.EventDate, entity.EventType.ToString(), entity.Reference, entity.FrameworkCode, entity.FrameworkEdition, entity.SchemaVersion, OwnershipEventSha256(entity.ContentJson), content, entity.Status, UserName(entity.PreparedByUserId, users) ?? "Unavailable user", entity.PreparedAtUtc, UserName(entity.ApprovedByUserId, users), entity.ApprovedAtUtc, UserName(entity.RejectedByUserId, users), entity.RejectedAtUtc, UserName(entity.PostedByUserId, users), entity.PostedAtUtc, entity.DecisionReason, entity.ReversalOfEventId, entity.ReversedByEventId, entity.ReversalReason, entity.ConcurrencyToken);
    }

    private static string? UserName(Guid? userId, IReadOnlyDictionary<Guid, string> users) => userId.HasValue ? users.GetValueOrDefault(userId.Value, "Unavailable user") : null;
    private static bool ValidOwnershipPercentage(decimal value) => value is >= 0m and <= 1m && decimal.Round(value, 6) == value;
    private static bool ValidOwnershipMoney(params decimal[] values) => values.All(value => value is >= -9999999999999999.99m and <= 9999999999999999.99m && decimal.Round(value, 2) == value);
    private static bool ConciseOwnershipText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum;
    private static string OwnershipEventSha256(string contentJson) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contentJson))).ToLowerInvariant();
    private static async Task<string?> ValidateOwnershipEventAccessAsync(BrassLedgerDbContext db, Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var memberIds = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == groupId).Select(item => item.MemberCompanyId).Distinct().ToArrayAsync(cancellationToken);
        if (memberIds.Length == 0) return "The consolidation group has no retained member-company history.";
        var permitted = await db.CompanyMemberships.AsNoTracking().Where(item => item.UserId == userId && item.IsActive && memberIds.Contains(item.CompanyId)).Select(item => item.CompanyId).Distinct().CountAsync(cancellationToken);
        return permitted == memberIds.Length ? null : "The current user must have active access to every company retained in the group's ownership history.";
    }

    private static void AddOwnershipEventAudit(BrassLedgerDbContext db, Guid companyId, Guid? userId, string action, ConsolidationOwnershipEvent entity, object details) => db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = userId, Action = action, EntityType = nameof(ConsolidationOwnershipEvent), EntityId = entity.Id, DetailJson = JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow });
}
