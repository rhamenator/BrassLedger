using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<ProjectWipPreview> PreviewProjectWipScheduleAsync(ProjectWipPreviewRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectWipPrepare)) return ProjectWipPreview.Failure("You are not authorized to prepare project WIP schedules.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        return await BuildProjectWipPreviewAsync(db, companyId, request, cancellationToken);
    }

    public async Task<TransactionResult> SaveProjectWipScheduleAsync(SaveProjectWipScheduleRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectWipPrepare)) return TransactionResult.Failure("You are not authorized to prepare project WIP schedules.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required to prepare project WIP.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var preview = await BuildProjectWipPreviewAsync(db, companyId, request.PreviewRequest with { ExistingScheduleId = request.Id }, cancellationToken);
        if (!preview.Succeeded) return TransactionResult.Failure(preview.ErrorMessage);
        if (!string.Equals(preview.Fingerprint, request.PreviewFingerprint, StringComparison.Ordinal) || !string.Equals(preview.ProjectConcurrencyToken, request.ProjectConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The project, billings, costs, or prior WIP changed after preview. Preview the schedule again before saving it.");
        if (await db.ProjectWipSchedules.AnyAsync(x => x.CompanyId == companyId && x.ProjectJobId == preview.ProjectJobId && x.Id != request.Id && (x.Status == "Draft" || x.Status == "Submitted" || x.Status == "Approved" || x.Status == "Rejected"), cancellationToken))
            return TransactionResult.Failure("This project already has an open WIP schedule. Complete or cancel it before preparing another.");

        ProjectWipSchedule schedule;
        object? prior = null;
        if (request.Id.HasValue)
        {
            schedule = await db.ProjectWipSchedules.SingleOrDefaultAsync(x => x.Id == request.Id.Value && x.CompanyId == companyId, cancellationToken) ?? new ProjectWipSchedule();
            if (schedule.Id == Guid.Empty) return TransactionResult.Failure("Project WIP schedule not found.");
            if (schedule.Status is not ("Draft" or "Rejected")) return TransactionResult.Failure("Only a draft or rejected WIP schedule can be corrected.");
            if (!string.Equals(schedule.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The WIP schedule changed after it was displayed. Refresh before saving it.");
            prior = WipAuditState(schedule);
        }
        else
        {
            schedule = new ProjectWipSchedule { Id = Guid.NewGuid(), CompanyId = companyId };
            db.ProjectWipSchedules.Add(schedule);
        }

        // Advance the project token in the same transaction as the schedule so two
        // preparers cannot both reserve the same cumulative WIP starting point.
        var project = await db.ProjectJobs.SingleAsync(x => x.Id == preview.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        project.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var retainedPreview = await BuildProjectWipPreviewAsync(db, companyId, request.PreviewRequest with { ExistingScheduleId = request.Id }, cancellationToken);
        if (!retainedPreview.Succeeded) return TransactionResult.Failure(retainedPreview.ErrorMessage);
        CopyPreviewToSchedule(schedule, request.PreviewRequest, retainedPreview);
        schedule.Status = "Draft";
        schedule.PreparedByUserId = userId.Value;
        schedule.PreparedAtUtc = DateTimeOffset.UtcNow;
        schedule.SubmittedByUserId = null; schedule.SubmittedAtUtc = null;
        schedule.ApprovedByUserId = null; schedule.ApprovedAtUtc = null;
        schedule.RejectedByUserId = null; schedule.RejectedAtUtc = null; schedule.DecisionReason = string.Empty;
        schedule.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWipAudit(db, companyId, prior is null ? "project-wip.created" : "project-wip.corrected", schedule, new { prior, current = WipAuditState(schedule) });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The WIP schedule changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("Project WIP changed concurrently. No partial schedule was saved; refresh and try again."); }
        return TransactionResult.Success(schedule.Id);
    }

    public async Task<TransactionResult> SubmitProjectWipScheduleAsync(SubmitProjectWipScheduleRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectWipPrepare)) return TransactionResult.Failure("You are not authorized to submit project WIP schedules.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var schedule = await db.ProjectWipSchedules.SingleOrDefaultAsync(x => x.Id == request.ProjectWipScheduleId && x.CompanyId == companyId, cancellationToken);
        if (schedule is null || schedule.Status != "Draft") return TransactionResult.Failure("Only a draft project WIP schedule can be submitted.");
        if (!string.Equals(schedule.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The WIP schedule changed after it was displayed. Refresh before submitting it.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required to submit project WIP.");
        var validation = await ValidateStoredWipPreviewAsync(db, companyId, schedule, cancellationToken);
        if (validation is not null) return TransactionResult.Failure(validation);
        schedule.Status = "Submitted"; schedule.SubmittedByUserId = userId.Value; schedule.SubmittedAtUtc = DateTimeOffset.UtcNow; schedule.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWipAudit(db, companyId, "project-wip.submitted", schedule, WipAuditState(schedule));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The WIP schedule changed while it was being submitted. Refresh and try again."); }
        return TransactionResult.Success(schedule.Id);
    }

    public async Task<TransactionResult> DecideProjectWipScheduleAsync(DecideProjectWipScheduleRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectWipApprove)) return TransactionResult.Failure("You are not authorized to approve or reject project WIP schedules.");
        var reason = request.Reason.Trim();
        if (reason.Length is < 1 or > 1000) return TransactionResult.Failure("A decision reason of at most 1,000 characters is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var schedule = await db.ProjectWipSchedules.SingleOrDefaultAsync(x => x.Id == request.ProjectWipScheduleId && x.CompanyId == companyId, cancellationToken);
        if (schedule is null || schedule.Status != "Submitted") return TransactionResult.Failure("Only a submitted project WIP schedule can be decided.");
        if (!string.Equals(schedule.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The WIP schedule changed after it was displayed. Refresh before deciding it.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated reviewer identity is required.");
        if (schedule.PreparedByUserId == userId.Value || schedule.SubmittedByUserId == userId.Value) return TransactionResult.Failure("The preparer or submitter cannot decide the same project WIP schedule.");
        if (request.Approve)
        {
            var validation = await ValidateStoredWipPreviewAsync(db, companyId, schedule, cancellationToken);
            if (validation is not null) return TransactionResult.Failure(validation);
            schedule.Status = "Approved"; schedule.ApprovedByUserId = userId.Value; schedule.ApprovedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            schedule.Status = "Rejected"; schedule.RejectedByUserId = userId.Value; schedule.RejectedAtUtc = DateTimeOffset.UtcNow;
        }
        schedule.DecisionReason = reason; schedule.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWipAudit(db, companyId, request.Approve ? "project-wip.approved" : "project-wip.rejected", schedule, new { reason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The WIP schedule changed while it was being decided. Refresh and try again."); }
        return TransactionResult.Success(schedule.Id);
    }

    public async Task<TransactionResult> PostProjectWipScheduleAsync(PostProjectWipScheduleRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectWipPost)) return TransactionResult.Failure("You are not authorized to post project WIP schedules.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await db.ProjectWipSchedules.SingleOrDefaultAsync(x => x.Id == request.ProjectWipScheduleId && x.CompanyId == companyId, cancellationToken);
        if (schedule is null || schedule.Status != "Approved") return TransactionResult.Failure("Only an approved project WIP schedule can be posted.");
        if (!string.Equals(schedule.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The WIP schedule changed after it was displayed. Refresh before posting it.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated posting identity is required.");
        if (schedule.ApprovedByUserId == userId.Value) return TransactionResult.Failure("The person who approved a project WIP schedule cannot post it.");
        var validation = await ValidateStoredWipPreviewAsync(db, companyId, schedule, cancellationToken);
        if (validation is not null) return TransactionResult.Failure(validation);

        var lines = BuildWipJournalLines(schedule);
        if (lines.Count > 0)
        {
            var posting = await PostAsync(db, companyId, schedule.PostingDate, "Project WIP", $"WIP-{schedule.ThroughDate:yyyyMMdd}", schedule.Description, lines, cancellationToken, allowControlAccounts: true, sourceDocumentId: schedule.Id, sourceDocumentType: nameof(ProjectWipSchedule), resolveOperationalRoles: true, allowClosedProjects: true);
            if (!posting.Succeeded) return posting;
            schedule.JournalEntryId = posting.Id;
        }
        schedule.Status = "Posted"; schedule.PostedByUserId = userId.Value; schedule.PostedAtUtc = DateTimeOffset.UtcNow; schedule.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWipAudit(db, companyId, "project-wip.posted", schedule, new { schedule.JournalEntryId, lineCount = lines.Count });
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The WIP schedule changed while it was posting. The entire operation was rolled back; refresh and try again."); }
        return TransactionResult.Success(schedule.Id);
    }

    public async Task<TransactionResult> CancelProjectWipScheduleAsync(CancelProjectWipScheduleRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectWipPrepare)) return TransactionResult.Failure("You are not authorized to cancel project WIP schedules.");
        var reason = request.Reason.Trim();
        if (reason.Length is < 1 or > 1000) return TransactionResult.Failure("A cancellation reason of at most 1,000 characters is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var schedule = await db.ProjectWipSchedules.SingleOrDefaultAsync(x => x.Id == request.ProjectWipScheduleId && x.CompanyId == companyId, cancellationToken);
        if (schedule is null || schedule.Status is not ("Draft" or "Submitted" or "Rejected")) return TransactionResult.Failure("Only a draft, submitted, or rejected project WIP schedule can be cancelled.");
        if (!string.Equals(schedule.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The WIP schedule changed after it was displayed. Refresh before cancelling it.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required.");
        schedule.Status = "Cancelled"; schedule.CancelledByUserId = userId.Value; schedule.CancelledAtUtc = DateTimeOffset.UtcNow; schedule.CancellationReason = reason; schedule.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWipAudit(db, companyId, "project-wip.cancelled", schedule, new { reason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The WIP schedule changed while it was being cancelled. Refresh and try again."); }
        return TransactionResult.Success(schedule.Id);
    }

    public async Task<TransactionResult> ReverseProjectWipScheduleAsync(ReverseProjectWipScheduleRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectWipReverse)) return TransactionResult.Failure("You are not authorized to reverse project WIP schedules.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated reversing identity is required.");
        var reason = request.Reason.Trim();
        if (reason.Length is < 1 or > 1000) return TransactionResult.Failure("A reversal reason of at most 1,000 characters is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await db.ProjectWipSchedules.SingleOrDefaultAsync(x => x.Id == request.ProjectWipScheduleId && x.CompanyId == companyId, cancellationToken);
        if (schedule is null || schedule.Status != "Posted") return TransactionResult.Failure("Only a posted project WIP schedule can be reversed.");
        if (!string.Equals(schedule.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The WIP schedule changed after it was displayed. Refresh before reversing it.");
        if (request.ReversalDate < schedule.PostingDate) return TransactionResult.Failure("The reversal date cannot precede the WIP posting date.");
        var laterPosted = await db.ProjectWipSchedules.AsNoTracking().Where(x => x.CompanyId == companyId && x.ProjectJobId == schedule.ProjectJobId && x.Status == "Posted" && x.Id != schedule.Id).Select(x => new { x.ThroughDate, x.PostedAtUtc }).ToListAsync(cancellationToken);
        if (laterPosted.Any(x => x.ThroughDate > schedule.ThroughDate || x.ThroughDate == schedule.ThroughDate && x.PostedAtUtc > schedule.PostedAtUtc))
            return TransactionResult.Failure("Reverse later posted WIP schedules for this project before reversing this one.");
        Guid? reversalId = null;
        if (schedule.JournalEntryId.HasValue)
        {
            var reversal = await PostInverseAsync(db, companyId, schedule.JournalEntryId.Value, request.ReversalDate, $"REV-WIP-{schedule.ThroughDate:yyyyMMdd}", reason, schedule.Id, "ProjectWipScheduleReversal", null, cancellationToken, "Project WIP");
            if (!reversal.Succeeded) return reversal;
            reversalId = reversal.Id;
        }
        schedule.Status = "Reversed"; schedule.ReversalJournalEntryId = reversalId; schedule.ReversedByUserId = userId.Value; schedule.ReversedAtUtc = DateTimeOffset.UtcNow; schedule.ReversalDate = request.ReversalDate; schedule.ReversalReason = reason; schedule.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWipAudit(db, companyId, "project-wip.reversed", schedule, new { request.ReversalDate, reason, reversalId });
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The WIP schedule changed while it was being reversed. The entire operation was rolled back; refresh and try again."); }
        return TransactionResult.Success(schedule.Id);
    }

    private async Task<ProjectWipPreview> BuildProjectWipPreviewAsync(BrassLedgerDbContext db, Guid companyId, ProjectWipPreviewRequest request, CancellationToken cancellationToken)
    {
        var revenueAccountNumber = request.RevenueAccountNumber.Trim().ToUpperInvariant();
        var description = request.Description.Trim();
        if (request.ProjectJobId == Guid.Empty) return ProjectWipPreview.Failure("Select a project for the WIP schedule.");
        if (description.Length is < 1 or > 500) return ProjectWipPreview.Failure("A WIP description of at most 500 characters is required.");
        if (request.PostingDate < request.ThroughDate) return ProjectWipPreview.Failure("The WIP posting date cannot precede its through date.");
        var project = await db.ProjectJobs.SingleOrDefaultAsync(x => x.Id == request.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        if (project is null) return ProjectWipPreview.Failure("Project not found in this company.");
        if (project.StartDate.HasValue && request.ThroughDate < project.StartDate.Value) return ProjectWipPreview.Failure("The WIP through date cannot precede the project start date.");
        if (project.RevenueRecognitionMethod == "AsBilled") return ProjectWipPreview.Failure("This project recognizes revenue as billed and does not use WIP true-up schedules.");
        if (project.RevenueRecognitionMethod == "CompletedContract" && project.Status != "Closed") return ProjectWipPreview.Failure("Completed-contract revenue can be recognized only after the project is closed.");
        if (project.RevenueRecognitionMethod != "CompletedContract" && project.Status != "Active") return ProjectWipPreview.Failure("Only an active project can prepare this revenue-recognition schedule.");
        if (project.ContractAmount <= 0m) return ProjectWipPreview.Failure("The project requires a positive authorized contract amount before revenue can be recognized.");
        if (!await db.Accounts.AnyAsync(x => x.CompanyId == companyId && x.Number == revenueAccountNumber && x.IsActive && x.Type == AccountType.Revenue && !x.IsControlAccount, cancellationToken)) return ProjectWipPreview.Failure("Select an active, non-control revenue account.");
        if (!await HasValidWipControlAccountsAsync(db, companyId, cancellationToken)) return ProjectWipPreview.Failure("Configure active contract-asset and contract-liability control accounts before preparing project WIP.");
        var postedSchedules = await db.ProjectWipSchedules.AsNoTracking().Where(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.Status == "Posted" && x.Id != request.ExistingScheduleId).Select(x => new { x.ThroughDate, x.PostingDate, x.PostedAtUtc, x.RevenueAccountNumber }).ToListAsync(cancellationToken);
        var later = postedSchedules.OrderByDescending(x => x.ThroughDate).ThenByDescending(x => x.PostedAtUtc).FirstOrDefault();
        if (later is not null && (request.ThroughDate < later.ThroughDate || request.PostingDate < later.PostingDate)) return ProjectWipPreview.Failure("A WIP schedule cannot precede the latest posted schedule for this project.");
        if (later is not null && !string.Equals(revenueAccountNumber, later.RevenueAccountNumber, StringComparison.OrdinalIgnoreCase)) return ProjectWipPreview.Failure($"Use revenue account {later.RevenueAccountNumber}, which is retained by the project's posted WIP history. Reverse all posted WIP before changing its revenue classification.");

        var costRows = await (from line in db.JournalEntryLines
                              join entry in db.JournalEntries on line.JournalEntryId equals entry.Id
                              join account in db.Accounts on line.AccountId equals account.Id
                              where entry.CompanyId == companyId && entry.IsPosted && entry.PostedOn <= request.ThroughDate && line.ProjectJobId == project.Id && account.Type == AccountType.Expense
                              select new { LineId = line.Id, EntryId = entry.Id, line.Debit, line.Credit }).ToListAsync(cancellationToken);
        var actualCost = RoundCurrency(costRows.Sum(x => x.Debit - x.Credit));
        if (actualCost < 0m) return ProjectWipPreview.Failure("Posted project expense activity produces a negative cost-to-date balance. Correct or classify the project costs before preparing WIP.");
        decimal completion;
        if (project.RevenueRecognitionMethod == "CostToCost")
        {
            if (project.BudgetAmount <= 0m) return ProjectWipPreview.Failure("Cost-to-cost recognition requires a positive current estimated total cost in the project budget.");
            completion = Math.Min(1m, actualCost / project.BudgetAmount);
        }
        else if (project.RevenueRecognitionMethod == "ManualPercent")
        {
            if (request.ManualCompletionPercent is < 0m or > 1m) return ProjectWipPreview.Failure("Manual completion must be from 0% through 100%.");
            completion = request.ManualCompletionPercent;
        }
        else completion = 1m;
        completion = decimal.Round(completion, 6, MidpointRounding.AwayFromZero);
        var earned = RoundCurrency(project.ContractAmount * completion);
        var billedRows = await db.ProjectBillingProposals.AsNoTracking().Where(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.Status == "Posted" && x.BillingBasis != "RetainageRelease" && x.InvoiceDate <= request.ThroughDate).Select(x => new { x.Id, x.GrossAmount }).ToListAsync(cancellationToken);
        var controlledInvoiceIds = from proposal in db.ProjectBillingProposals.AsNoTracking()
                                   join workflow in db.SubledgerDocumentWorkflows.AsNoTracking() on proposal.SubledgerDocumentWorkflowId equals workflow.Id
                                   where proposal.CompanyId == companyId && workflow.PostedDocumentId.HasValue
                                   select workflow.PostedDocumentId!.Value;
        var ordinaryBilledRows = await (from line in db.SalesInvoiceLines.AsNoTracking()
                                        join invoice in db.SalesInvoices.AsNoTracking() on line.SalesInvoiceId equals invoice.Id
                                        where invoice.CompanyId == companyId
                                              && invoice.Status != "Voided"
                                              && invoice.InvoiceDate <= request.ThroughDate
                                              && line.ProjectJobId == project.Id
                                              && !controlledInvoiceIds.Contains(invoice.Id)
                                        select new { InvoiceId = invoice.Id, LineId = line.Id, NetAmount = line.LineTotal - line.TaxAmount }).ToListAsync(cancellationToken);
        var billed = RoundCurrency(billedRows.Sum(x => x.GrossAmount) + ordinaryBilledRows.Sum(x => x.NetAmount));

        var roleAccounts = await db.Accounts.AsNoTracking().Where(x => x.CompanyId == companyId && (x.OperationalRole == AccountingAccountRoles.ContractAsset || x.OperationalRole == AccountingAccountRoles.ContractLiability)).ToDictionaryAsync(x => x.OperationalRole!, x => x.Id, cancellationToken);
        var assetId = roleAccounts[AccountingAccountRoles.ContractAsset];
        var liabilityId = roleAccounts[AccountingAccountRoles.ContractLiability];
        var controlRows = await (from line in db.JournalEntryLines
                                 join entry in db.JournalEntries on line.JournalEntryId equals entry.Id
                                 where entry.CompanyId == companyId && entry.IsPosted && entry.PostedOn <= request.ThroughDate && line.ProjectJobId == project.Id && (line.AccountId == assetId || line.AccountId == liabilityId)
                                 select new { LineId = line.Id, EntryId = entry.Id, line.AccountId, line.Debit, line.Credit }).ToListAsync(cancellationToken);
        var priorAsset = RoundCurrency(controlRows.Where(x => x.AccountId == assetId).Sum(x => x.Debit - x.Credit));
        var priorLiability = RoundCurrency(controlRows.Where(x => x.AccountId == liabilityId).Sum(x => x.Credit - x.Debit));
        if (priorAsset < 0m || priorLiability < 0m) return ProjectWipPreview.Failure("Project WIP control activity has an abnormal balance. Reverse or correct the affected WIP schedule before continuing.");
        var netPosition = earned - billed;
        var desiredAsset = netPosition > 0m ? netPosition : 0m;
        var desiredLiability = netPosition < 0m ? -netPosition : 0m;
        var adjustment = RoundCurrency((desiredAsset - desiredLiability) - (priorAsset - priorLiability));
        var payload = JsonSerializer.Serialize(new { request.ProjectJobId, request.ThroughDate, request.PostingDate, RevenueAccountNumber = revenueAccountNumber, Description = description, project.ConcurrencyToken, project.RevenueRecognitionMethod, project.ContractAmount, project.BudgetAmount, actualCost, completion, earned, billed, priorAsset, priorLiability, desiredAsset, desiredLiability, adjustment, CostSources = costRows.OrderBy(x => x.LineId).Select(x => new { x.LineId, x.EntryId, x.Debit, x.Credit }), ControlledBillingSources = billedRows.OrderBy(x => x.Id), OrdinaryBillingSources = ordinaryBilledRows.OrderBy(x => x.LineId), ControlSources = controlRows.OrderBy(x => x.LineId).Select(x => new { x.LineId, x.EntryId, x.AccountId, x.Debit, x.Credit }) });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return new(true, string.Empty, project.Id, project.ConcurrencyToken, project.RevenueRecognitionMethod, project.ContractAmount, project.BudgetAmount, actualCost, completion, earned, billed, priorAsset, priorLiability, desiredAsset, desiredLiability, adjustment, fingerprint);
    }

    private static IReadOnlyList<JournalLineRequest> BuildWipJournalLines(ProjectWipSchedule schedule)
    {
        var lines = new List<JournalLineRequest>();
        var assetDelta = schedule.DesiredContractAsset - schedule.PriorContractAsset;
        var liabilityDelta = schedule.DesiredContractLiability - schedule.PriorContractLiability;
        if (assetDelta > 0m) lines.Add(new(OperationalRoleReference(AccountingAccountRoles.ContractAsset), assetDelta, 0m, "Increase project contract asset", schedule.ProjectJobId));
        else if (assetDelta < 0m) lines.Add(new(OperationalRoleReference(AccountingAccountRoles.ContractAsset), 0m, -assetDelta, "Reduce project contract asset", schedule.ProjectJobId));
        if (liabilityDelta > 0m) lines.Add(new(OperationalRoleReference(AccountingAccountRoles.ContractLiability), 0m, liabilityDelta, "Increase project contract liability", schedule.ProjectJobId));
        else if (liabilityDelta < 0m) lines.Add(new(OperationalRoleReference(AccountingAccountRoles.ContractLiability), -liabilityDelta, 0m, "Reduce project contract liability", schedule.ProjectJobId));
        if (schedule.RevenueAdjustment > 0m) lines.Add(new(schedule.RevenueAccountNumber, 0m, schedule.RevenueAdjustment, "Recognize earned project revenue", schedule.ProjectJobId));
        else if (schedule.RevenueAdjustment < 0m) lines.Add(new(schedule.RevenueAccountNumber, -schedule.RevenueAdjustment, 0m, "Defer billed project revenue", schedule.ProjectJobId));
        return lines;
    }

    private async Task<string?> ValidateStoredWipPreviewAsync(BrassLedgerDbContext db, Guid companyId, ProjectWipSchedule schedule, CancellationToken cancellationToken)
    {
        var preview = await BuildProjectWipPreviewAsync(db, companyId, new(schedule.ProjectJobId, schedule.ThroughDate, schedule.PostingDate, schedule.RevenueAccountNumber, schedule.Description, schedule.ManualCompletionPercent, schedule.Id), cancellationToken);
        if (!preview.Succeeded) return preview.ErrorMessage;
        if (!string.Equals(preview.Fingerprint, schedule.PreviewFingerprint, StringComparison.Ordinal) || !string.Equals(preview.ProjectConcurrencyToken, schedule.PreparedProjectConcurrencyToken, StringComparison.Ordinal))
            return "The project, billings, costs, or prior WIP changed after this schedule was prepared. Reject and correct it from a fresh preview.";
        if (!string.Equals(schedule.RecognitionMethod, preview.RecognitionMethod, StringComparison.Ordinal)
            || schedule.ContractAmountSnapshot != preview.ContractAmount
            || schedule.EstimatedCostSnapshot != preview.EstimatedCost
            || schedule.ActualCostToDate != preview.ActualCostToDate
            || schedule.CompletionPercent != preview.CompletionPercent
            || schedule.EarnedRevenueToDate != preview.EarnedRevenueToDate
            || schedule.BilledRevenueToDate != preview.BilledRevenueToDate
            || schedule.PriorContractAsset != preview.PriorContractAsset
            || schedule.PriorContractLiability != preview.PriorContractLiability
            || schedule.DesiredContractAsset != preview.DesiredContractAsset
            || schedule.DesiredContractLiability != preview.DesiredContractLiability
            || schedule.RevenueAdjustment != preview.RevenueAdjustment)
            return "The retained WIP calculation no longer matches its verified preview. Reject and correct the schedule before posting.";
        return null;
    }

    private static async Task<bool> HasValidWipControlAccountsAsync(BrassLedgerDbContext db, Guid companyId, CancellationToken cancellationToken) =>
        await db.Accounts.CountAsync(x => x.CompanyId == companyId && x.IsActive && x.IsControlAccount && ((x.OperationalRole == AccountingAccountRoles.ContractAsset && x.Type == AccountType.Asset) || (x.OperationalRole == AccountingAccountRoles.ContractLiability && x.Type == AccountType.Liability)), cancellationToken) == 2;

    private static void CopyPreviewToSchedule(ProjectWipSchedule schedule, ProjectWipPreviewRequest request, ProjectWipPreview preview)
    {
        schedule.ProjectJobId = preview.ProjectJobId; schedule.ThroughDate = request.ThroughDate; schedule.PostingDate = request.PostingDate; schedule.RecognitionMethod = preview.RecognitionMethod; schedule.ManualCompletionPercent = request.ManualCompletionPercent; schedule.ContractAmountSnapshot = preview.ContractAmount; schedule.EstimatedCostSnapshot = preview.EstimatedCost; schedule.ActualCostToDate = preview.ActualCostToDate; schedule.CompletionPercent = preview.CompletionPercent; schedule.EarnedRevenueToDate = preview.EarnedRevenueToDate; schedule.BilledRevenueToDate = preview.BilledRevenueToDate; schedule.PriorContractAsset = preview.PriorContractAsset; schedule.PriorContractLiability = preview.PriorContractLiability; schedule.DesiredContractAsset = preview.DesiredContractAsset; schedule.DesiredContractLiability = preview.DesiredContractLiability; schedule.RevenueAdjustment = preview.RevenueAdjustment; schedule.RevenueAccountNumber = request.RevenueAccountNumber.Trim().ToUpperInvariant(); schedule.Description = request.Description.Trim(); schedule.PreviewFingerprint = preview.Fingerprint; schedule.PreparedProjectConcurrencyToken = preview.ProjectConcurrencyToken;
    }

    private static object WipAuditState(ProjectWipSchedule schedule) => new { schedule.ProjectJobId, schedule.ThroughDate, schedule.PostingDate, schedule.RecognitionMethod, schedule.ContractAmountSnapshot, schedule.EstimatedCostSnapshot, schedule.ActualCostToDate, schedule.CompletionPercent, schedule.EarnedRevenueToDate, schedule.BilledRevenueToDate, schedule.PriorContractAsset, schedule.PriorContractLiability, schedule.DesiredContractAsset, schedule.DesiredContractLiability, schedule.RevenueAdjustment, schedule.RevenueAccountNumber, schedule.Status };

    private void AddWipAudit(BrassLedgerDbContext db, Guid companyId, string action, ProjectWipSchedule schedule, object detail) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = nameof(ProjectWipSchedule), EntityId = schedule.Id, DetailJson = JsonSerializer.Serialize(detail), OccurredAtUtc = DateTimeOffset.UtcNow });
}
