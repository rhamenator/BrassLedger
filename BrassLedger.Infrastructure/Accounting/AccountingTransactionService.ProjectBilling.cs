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
    public async Task<TransactionResult> SaveProjectBillingRateAsync(SaveProjectBillingRateRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectBillingPrepare)) return TransactionResult.Failure("You are not authorized to maintain project billing rates.");
        var earningCode = request.EarningCode.Trim().ToUpperInvariant();
        if (earningCode.Length is < 1 or > 50) return TransactionResult.Failure("An earning code of at most 50 characters is required; use * as the project default.");
        if (request.HourlyRate is < 0m or > MaxProjectAmount) return TransactionResult.Failure("The billing rate must be non-negative and fit the supported amount range.");
        if (request.EffectiveThrough.HasValue && request.EffectiveThrough < request.EffectiveOn) return TransactionResult.Failure("The billing-rate end date cannot precede its start date.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var project = await db.ProjectJobs.SingleOrDefaultAsync(x => x.Id == request.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        if (project is null) return TransactionResult.Failure("Project not found in this company.");
        if (project.Status != "Active") return TransactionResult.Failure("Reopen the project before changing billing rates.");
        var overlaps = await db.ProjectBillingRates.AnyAsync(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.EarningCode == earningCode && x.Id != request.Id
            && x.EffectiveOn <= (request.EffectiveThrough ?? DateOnly.MaxValue)
            && (x.EffectiveThrough == null || x.EffectiveThrough >= request.EffectiveOn), cancellationToken);
        if (overlaps) return TransactionResult.Failure("That earning code already has an overlapping effective-dated billing rate.");

        var now = DateTimeOffset.UtcNow;
        ProjectBillingRate rate;
        object? prior = null;
        if (request.Id.HasValue)
        {
            rate = await db.ProjectBillingRates.SingleOrDefaultAsync(x => x.Id == request.Id.Value && x.CompanyId == companyId && x.ProjectJobId == project.Id, cancellationToken) ?? new ProjectBillingRate();
            if (rate.Id == Guid.Empty) return TransactionResult.Failure("Project billing rate not found.");
            if (!string.Equals(rate.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The billing rate changed after it was displayed. Refresh before saving it.");
            prior = new { rate.EarningCode, rate.HourlyRate, rate.EffectiveOn, rate.EffectiveThrough, rate.IsActive };
            rate.UpdatedByUserId = ResolveUserId();
            rate.UpdatedAtUtc = now;
        }
        else
        {
            rate = new ProjectBillingRate { Id = Guid.NewGuid(), CompanyId = companyId, ProjectJobId = project.Id, CreatedByUserId = ResolveUserId(), CreatedAtUtc = now };
            db.ProjectBillingRates.Add(rate);
        }
        rate.EarningCode = earningCode;
        rate.HourlyRate = decimal.Round(request.HourlyRate, 4, MidpointRounding.AwayFromZero);
        rate.EffectiveOn = request.EffectiveOn;
        rate.EffectiveThrough = request.EffectiveThrough;
        rate.IsActive = request.IsActive;
        rate.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectBillingAudit(db, companyId, prior is null ? "project-billing-rate.created" : "project-billing-rate.updated", rate.Id, project.Id, new { prior, current = new { rate.EarningCode, rate.HourlyRate, rate.EffectiveOn, rate.EffectiveThrough, rate.IsActive } });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The billing rate changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The billing rate overlaps an existing rate or changed concurrently."); }
        return TransactionResult.Success(rate.Id);
    }

    public async Task<ProjectBillingPreview> PreviewProjectBillingAsync(ProjectBillingPreviewRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectBillingPrepare) || !HasPermission(BrassLedgerPermissions.ReceivablesManage) || !HasPermission(BrassLedgerPermissions.SubledgerPrepare))
            return ProjectBillingPreview.Failure("You are not authorized to prepare project billing and customer invoice drafts.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (request.ExistingProposalId.HasValue && !await db.ProjectBillingProposals.AnyAsync(x => x.Id == request.ExistingProposalId && x.CompanyId == companyId && x.ProjectJobId == request.ProjectJobId && x.Status == "Rejected", cancellationToken))
            return ProjectBillingPreview.Failure("Only a rejected project billing proposal can be previewed for correction.");
        return await BuildProjectBillingPreviewAsync(db, companyId, request, request.ExistingProposalId, cancellationToken);
    }

    public async Task<TransactionResult> SaveProjectBillingProposalAsync(SaveProjectBillingProposalRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectBillingPrepare) || !HasPermission(BrassLedgerPermissions.ReceivablesManage) || !HasPermission(BrassLedgerPermissions.SubledgerPrepare))
            return TransactionResult.Failure("You are not authorized to prepare project billing and customer invoice drafts.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required to prepare project billing.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        ProjectBillingProposal? existing = null;
        if (request.Id.HasValue)
        {
            existing = await db.ProjectBillingProposals.SingleOrDefaultAsync(x => x.Id == request.Id.Value && x.CompanyId == companyId, cancellationToken);
            if (existing is null) return TransactionResult.Failure("Project billing proposal not found.");
            if (existing.Status != "Rejected") return TransactionResult.Failure("Only a rejected project billing proposal can be corrected in place.");
            if (!string.Equals(existing.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The billing proposal changed after it was displayed. Refresh before correcting it.");
        }
        if (request.PreviewRequest.ExistingProposalId != request.Id) return TransactionResult.Failure("The preview correction identity does not match the billing proposal being saved.");

        var preview = await BuildProjectBillingPreviewAsync(db, companyId, request.PreviewRequest, existing?.Id, cancellationToken);
        if (!preview.Succeeded) return TransactionResult.Failure(preview.ErrorMessage);
        if (!string.Equals(preview.ProjectConcurrencyToken, request.ProjectConcurrencyToken, StringComparison.Ordinal) || !string.Equals(preview.Fingerprint, request.PreviewFingerprint, StringComparison.Ordinal))
            return TransactionResult.Failure("The project, billing rates, eligible source transactions, or prior billings changed after preview. Preview the billing again before saving it.");
        var project = await db.ProjectJobs.SingleAsync(x => x.Id == preview.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        if (!string.Equals(project.ConcurrencyToken, request.ProjectConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project changed after preview. Refresh and preview the billing again.");

        var invoiceLines = preview.Lines.Select(line => new SalesInvoiceLineRequest(line.Description, line.Quantity, line.UnitPrice, line.RetainageAmount, 0m, line.RevenueAccountNumber, project.Id)).ToArray();
        var invoiceRequest = new CreateInvoiceRequest(project.CustomerId!.Value, request.PreviewRequest.InvoiceNumber.Trim(), request.PreviewRequest.InvoiceDate, request.PreviewRequest.DueDate, preview.GrossAmount, 0m, request.PreviewRequest.RevenueAccountNumber.Trim(), request.PreviewRequest.Description.Trim(), invoiceLines);
        var payloadJson = JsonSerializer.Serialize(invoiceRequest);
        var validationContext = new ProjectBillingProposal
        {
            CompanyId = companyId,
            ProjectJobId = project.Id,
            CustomerId = project.CustomerId.Value,
            InvoiceNumber = invoiceRequest.InvoiceNumber,
            BillingBasis = preview.BillingBasis,
            GrossAmount = preview.GrossAmount,
            RetainageAmount = preview.RetainageAmount,
            InvoiceAmount = preview.InvoiceAmount
        };
        var validation = await ValidateSubledgerPostingAsync(companyId, "Invoice", "company", payloadJson, cancellationToken, validationContext);
        if (!validation.Succeeded) return TransactionResult.Failure($"The project billing invoice draft is not postable: {validation.ErrorMessage}");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        ProjectBillingProposal proposal;
        SubledgerDocumentWorkflow workflow;
        object? prior = null;
        if (existing is null)
        {
            if (await db.SubledgerDocumentWorkflows.AnyAsync(x => x.CompanyId == companyId && x.DocumentType == "Invoice" && x.DocumentScope == "company" && x.DocumentNumber == invoiceRequest.InvoiceNumber && !x.IsRecurringTemplate, cancellationToken))
                return TransactionResult.Failure("That customer invoice draft number already exists.");
            workflow = new SubledgerDocumentWorkflow { Id = Guid.NewGuid(), CompanyId = companyId, DocumentType = "Invoice", DocumentScope = "company", DocumentNumber = invoiceRequest.InvoiceNumber, PayloadJson = payloadJson, Status = "Draft", CreatedByUserId = userId, CreatedAtUtc = now, ConcurrencyToken = Guid.NewGuid().ToString("N") };
            proposal = new ProjectBillingProposal { Id = Guid.NewGuid(), CompanyId = companyId, ProjectJobId = project.Id, CustomerId = project.CustomerId.Value, SubledgerDocumentWorkflowId = workflow.Id, PreparedByUserId = userId.Value, PreparedAtUtc = now };
            db.SubledgerDocumentWorkflows.Add(workflow);
            db.ProjectBillingProposals.Add(proposal);
        }
        else
        {
            proposal = existing;
            workflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == proposal.SubledgerDocumentWorkflowId && x.CompanyId == companyId, cancellationToken);
            if (workflow.Status != "Rejected") return TransactionResult.Failure("The linked customer invoice draft is no longer rejected. Refresh before correcting project billing.");
            prior = new { proposal.InvoiceNumber, proposal.BillingThrough, proposal.BillingBasis, proposal.GrossAmount, proposal.RetainageAmount, proposal.InvoiceAmount, workflow.PayloadJson, Lines = await db.ProjectBillingLines.Where(x => x.ProjectBillingProposalId == proposal.Id).OrderBy(x => x.Sequence).Select(x => new { x.SourceKey, x.GrossAmount, x.RetainageAmount, x.InvoiceAmount }).ToArrayAsync(cancellationToken) };
            workflow.DocumentNumber = invoiceRequest.InvoiceNumber;
            workflow.PayloadJson = payloadJson;
            workflow.Status = "Draft";
            workflow.CreatedByUserId = userId;
            workflow.CreatedAtUtc = now;
            workflow.ApprovedByUserId = null; workflow.ApprovedAtUtc = null; workflow.RejectedByUserId = null; workflow.RejectedAtUtc = null; workflow.DecisionReason = string.Empty;
            workflow.PostedByUserId = null; workflow.PostedAtUtc = null; workflow.PostedDocumentId = null; workflow.ConcurrencyToken = Guid.NewGuid().ToString("N");
            var oldLines = await db.ProjectBillingLines.Where(x => x.ProjectBillingProposalId == proposal.Id).ToListAsync(cancellationToken);
            db.ProjectBillingLines.RemoveRange(oldLines);
        }

        proposal.InvoiceNumber = invoiceRequest.InvoiceNumber;
        proposal.RetainageReleaseOfProposalId = request.PreviewRequest.RetainageReleaseOfProposalId;
        proposal.BillingThrough = request.PreviewRequest.BillingThrough;
        proposal.InvoiceDate = request.PreviewRequest.InvoiceDate;
        proposal.DueDate = request.PreviewRequest.DueDate;
        proposal.BillingBasis = preview.BillingBasis;
        proposal.ProgressPercentToDate = request.PreviewRequest.ProgressPercentToDate;
        proposal.CostMarkupPercent = request.PreviewRequest.CostMarkupPercent;
        proposal.ContractAmountSnapshot = preview.ContractAmount;
        proposal.RetainagePercentSnapshot = project.RetainagePercent;
        proposal.GrossAmount = preview.GrossAmount;
        proposal.RetainageAmount = preview.RetainageAmount;
        proposal.InvoiceAmount = preview.InvoiceAmount;
        proposal.RevenueAccountNumber = request.PreviewRequest.RevenueAccountNumber.Trim().ToUpperInvariant();
        proposal.Description = request.PreviewRequest.Description.Trim();
        proposal.PreviewFingerprint = preview.Fingerprint;
        var preparedProjectConcurrencyToken = Guid.NewGuid().ToString("N");
        proposal.PreparedProjectConcurrencyToken = preparedProjectConcurrencyToken;
        proposal.Status = "Draft";
        proposal.PreparedByUserId = userId.Value;
        proposal.PreparedAtUtc = now;
        proposal.CancelledByUserId = null; proposal.CancelledAtUtc = null; proposal.CancellationReason = string.Empty;
        proposal.ConcurrencyToken = Guid.NewGuid().ToString("N");

        db.ProjectBillingLines.AddRange(preview.Lines.Select((line, index) => new ProjectBillingLine { Id = Guid.NewGuid(), ProjectBillingProposalId = proposal.Id, Sequence = index + 1, SourceType = line.SourceType, SourceId = line.SourceId, SourceKey = line.SourceKey, Description = line.Description, Quantity = line.Quantity, UnitPrice = line.UnitPrice, SourceCost = line.SourceCost, MarkupAmount = line.MarkupAmount, GrossAmount = line.GrossAmount, RetainageAmount = line.RetainageAmount, InvoiceAmount = line.InvoiceAmount, RevenueAccountNumber = line.RevenueAccountNumber }));
        var sourceKeys = preview.Lines.Where(x => x.SourceId.HasValue).Select(x => x.SourceKey).ToHashSet(StringComparer.Ordinal);
        var reservations = await db.ProjectBillingSourceReservations.Where(x => x.CompanyId == companyId && (sourceKeys.Contains(x.SourceKey) || x.ProjectBillingProposalId == proposal.Id)).ToListAsync(cancellationToken);
        foreach (var reservation in reservations.Where(x => x.ProjectBillingProposalId == proposal.Id && !sourceKeys.Contains(x.SourceKey))) { reservation.Status = "Released"; reservation.UpdatedAtUtc = now; reservation.ConcurrencyToken = Guid.NewGuid().ToString("N"); }
        foreach (var line in preview.Lines.Where(x => x.SourceId.HasValue))
        {
            var reservation = reservations.SingleOrDefault(x => x.SourceKey == line.SourceKey);
            if (reservation is null)
            {
                db.ProjectBillingSourceReservations.Add(new ProjectBillingSourceReservation { Id = Guid.NewGuid(), CompanyId = companyId, ProjectJobId = project.Id, SourceKey = line.SourceKey, ProjectBillingProposalId = proposal.Id, Status = "Reserved", UpdatedAtUtc = now, ConcurrencyToken = Guid.NewGuid().ToString("N") });
            }
            else
            {
                if (reservation.Status != "Released" && reservation.ProjectBillingProposalId != proposal.Id) return TransactionResult.Failure("An eligible source transaction was reserved by another billing proposal. Preview again.");
                reservation.ProjectBillingProposalId = proposal.Id; reservation.Status = "Reserved"; reservation.UpdatedAtUtc = now; reservation.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
        }
        var timeEntryIds = preview.Lines.Where(x => x.SourceType == "ApprovedTime" && x.SourceId.HasValue).Select(x => x.SourceId!.Value).ToArray();
        if (timeEntryIds.Length > 0)
        {
            var sourceTimecards = await db.PayrollTimeEntries.Where(x => timeEntryIds.Contains(x.Id)).Select(x => x.PayrollTimecardId).Distinct()
                .Join(db.PayrollTimecards, id => id, card => card.Id, (_, card) => card).ToListAsync(cancellationToken);
            foreach (var sourceTimecard in sourceTimecards) sourceTimecard.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }
        var costLineIds = preview.Lines.Where(x => x.SourceType == "PostedCost" && x.SourceId.HasValue).Select(x => x.SourceId!.Value).ToArray();
        if (costLineIds.Length > 0)
        {
            var sourceJournals = await db.JournalEntryLines.Where(x => costLineIds.Contains(x.Id)).Select(x => x.JournalEntryId).Distinct()
                .Join(db.JournalEntries, id => id, journal => journal.Id, (_, journal) => journal).ToListAsync(cancellationToken);
            foreach (var sourceJournal in sourceJournals) sourceJournal.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }
        project.ConcurrencyToken = preparedProjectConcurrencyToken;
        AddWorkflowAudit(db, workflow, prior is null ? "subledger-document.project-billing-draft.saved" : "subledger-document.project-billing-draft.revised");
        AddProjectBillingAudit(db, companyId, prior is null ? "project-billing-proposal.created" : "project-billing-proposal.corrected", proposal.Id, project.Id, new { prior, current = new { proposal.InvoiceNumber, proposal.BillingThrough, proposal.BillingBasis, proposal.GrossAmount, proposal.RetainageAmount, proposal.InvoiceAmount, SourceKeys = preview.Lines.Select(x => x.SourceKey) } });
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project or an eligible source changed while billing was being saved. The entire operation was rolled back; preview again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The invoice number or a source reservation changed concurrently. The entire operation was rolled back; preview again."); }
        return TransactionResult.Success(proposal.Id);
    }

    public async Task<TransactionResult> CancelProjectBillingProposalAsync(CancelProjectBillingProposalRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectBillingPrepare)) return TransactionResult.Failure("You are not authorized to cancel project billing proposals.");
        var reason = request.Reason.Trim();
        if (reason.Length is < 1 or > 1000) return TransactionResult.Failure("A cancellation reason of at most 1,000 characters is required.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required to cancel project billing.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var proposal = await db.ProjectBillingProposals.SingleOrDefaultAsync(x => x.Id == request.ProjectBillingProposalId && x.CompanyId == companyId, cancellationToken);
        if (proposal is null) return TransactionResult.Failure("Project billing proposal not found.");
        if (proposal.Status is not ("Draft" or "Rejected")) return TransactionResult.Failure("Only a draft or rejected project billing proposal can be cancelled; an approved invoice must first be rejected by its reviewer.");
        if (!string.Equals(proposal.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The billing proposal changed after it was displayed. Refresh before cancelling it.");
        var workflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == proposal.SubledgerDocumentWorkflowId && x.CompanyId == companyId, cancellationToken);
        if (workflow.Status is not ("Draft" or "Rejected")) return TransactionResult.Failure("The linked invoice draft changed. Refresh before cancelling project billing.");
        proposal.Status = "Cancelled"; proposal.CancelledByUserId = userId.Value; proposal.CancelledAtUtc = DateTimeOffset.UtcNow; proposal.CancellationReason = reason; proposal.ConcurrencyToken = Guid.NewGuid().ToString("N");
        workflow.Status = "Cancelled"; workflow.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var reservations = await db.ProjectBillingSourceReservations.Where(x => x.ProjectBillingProposalId == proposal.Id && x.Status == "Reserved").ToListAsync(cancellationToken);
        foreach (var reservation in reservations) { reservation.Status = "Released"; reservation.UpdatedAtUtc = DateTimeOffset.UtcNow; reservation.ConcurrencyToken = Guid.NewGuid().ToString("N"); }
        var project = await db.ProjectJobs.SingleAsync(x => x.Id == proposal.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        project.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWorkflowAudit(db, workflow, "subledger-document.project-billing-cancelled", new { reason });
        AddProjectBillingAudit(db, companyId, "project-billing-proposal.cancelled", proposal.Id, proposal.ProjectJobId, new { reason, releasedSources = reservations.Count });
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The billing proposal changed while it was being cancelled. The entire operation was rolled back; refresh and try again."); }
        return TransactionResult.Success(proposal.Id);
    }

    private async Task<ProjectBillingPreview> BuildProjectBillingPreviewAsync(BrassLedgerDbContext db, Guid companyId, ProjectBillingPreviewRequest request, Guid? ignoredProposalId, CancellationToken cancellationToken)
    {
        var invoiceNumber = request.InvoiceNumber.Trim();
        var revenueAccount = request.RevenueAccountNumber.Trim().ToUpperInvariant();
        var description = request.Description.Trim();
        if (request.ProjectJobId == Guid.Empty) return ProjectBillingPreview.Failure("Select a project to bill.");
        if (invoiceNumber.Length is < 1 or > 50 || description.Length is < 1 or > 500) return ProjectBillingPreview.Failure("An invoice number of at most 50 characters and description of at most 500 characters are required.");
        if (request.DueDate < request.InvoiceDate) return ProjectBillingPreview.Failure("The invoice due date cannot precede the invoice date.");
        if (request.BillingThrough > request.InvoiceDate) return ProjectBillingPreview.Failure("The billing-through date cannot be after the invoice date.");
        if (request.ProgressPercentToDate is < 0m or > 1m || request.MilestoneAmount < 0m || request.CostMarkupPercent is < 0m or > 100m) return ProjectBillingPreview.Failure("Progress must be from 0% through 100%, milestone amount cannot be negative, and cost markup must be from 0% through 10,000%.");
        if (!await db.Accounts.AnyAsync(x => x.CompanyId == companyId && x.Number == revenueAccount && x.IsActive && x.Type == AccountType.Revenue && !x.IsControlAccount, cancellationToken)) return ProjectBillingPreview.Failure("Select an active, non-control revenue account.");
        var project = await db.ProjectJobs.SingleOrDefaultAsync(x => x.Id == request.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        if (project is null) return ProjectBillingPreview.Failure("Project not found in this company.");
        if (project.Status != "Active") return ProjectBillingPreview.Failure("Only an active project can be billed.");
        if (project.StartDate.HasValue && request.BillingThrough < project.StartDate.Value) return ProjectBillingPreview.Failure("The billing-through date cannot precede the project start date.");
        if (!project.CustomerId.HasValue || !await db.Customers.AnyAsync(x => x.Id == project.CustomerId && x.CompanyId == companyId, cancellationToken)) return ProjectBillingPreview.Failure("The project must have a customer in this company.");
        if (project.BillingMethod == "Internal") return ProjectBillingPreview.Failure("Internal projects cannot create customer billings.");
        var ignoredWorkflowId = ignoredProposalId.HasValue ? await db.ProjectBillingProposals.Where(x => x.Id == ignoredProposalId && x.CompanyId == companyId).Select(x => (Guid?)x.SubledgerDocumentWorkflowId).SingleOrDefaultAsync(cancellationToken) : null;
        var numberConflict = await db.ProjectBillingProposals.AnyAsync(x => x.CompanyId == companyId && x.InvoiceNumber == invoiceNumber && x.Id != ignoredProposalId, cancellationToken)
            || await db.SubledgerDocumentWorkflows.AnyAsync(x => x.CompanyId == companyId && x.DocumentType == "Invoice" && x.DocumentScope == "company" && x.DocumentNumber == invoiceNumber && !x.IsRecurringTemplate && x.Id != ignoredWorkflowId, cancellationToken)
            || await db.SalesInvoices.AnyAsync(x => x.CompanyId == companyId && x.InvoiceNumber == invoiceNumber, cancellationToken);
        if (numberConflict) return ProjectBillingPreview.Failure("That invoice number is already in use.");

        var priorGrossAmounts = await db.ProjectBillingProposals.Where(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.BillingBasis != "RetainageRelease" && x.Status != "Cancelled" && x.Status != "Voided" && x.Id != ignoredProposalId).Select(x => x.GrossAmount).ToListAsync(cancellationToken);
        var previousGross = priorGrossAmounts.Sum();
        var blockedSources = await db.ProjectBillingSourceReservations.Where(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.Status != "Released" && x.ProjectBillingProposalId != ignoredProposalId).Select(x => x.SourceKey).ToListAsync(cancellationToken);
        var blocked = blockedSources.ToHashSet(StringComparer.Ordinal);
        var lines = new List<ProjectBillingPreviewLine>();
        string basis;
        if (request.RetainageReleaseOfProposalId.HasValue)
        {
            basis = "RetainageRelease";
            var source = await db.ProjectBillingProposals.SingleOrDefaultAsync(x => x.Id == request.RetainageReleaseOfProposalId && x.CompanyId == companyId && x.ProjectJobId == project.Id && x.Status == "Posted" && x.BillingBasis != "RetainageRelease", cancellationToken);
            if (source is null) return ProjectBillingPreview.Failure("Retainage can be released only from a posted billing proposal for this project.");
            var releasedAmounts = await db.ProjectBillingProposals.Where(x => x.RetainageReleaseOfProposalId == source.Id && x.Status != "Cancelled" && x.Status != "Voided" && x.Id != ignoredProposalId).Select(x => x.InvoiceAmount).ToListAsync(cancellationToken);
            var available = source.RetainageAmount - releasedAmounts.Sum();
            var releaseAmount = RoundCurrency(request.RetainageReleaseAmount);
            if (releaseAmount <= 0m || releaseAmount > available) return ProjectBillingPreview.Failure($"Enter a positive retainage release no greater than the remaining {available:C}.");
            lines.Add(new("RetainageRelease", source.Id, $"RETAINAGE:{source.Id:N}:{invoiceNumber}", $"Retainage release from {source.InvoiceNumber} — {description}", 1m, releaseAmount, 0m, 0m, releaseAmount, 0m, releaseAmount, revenueAccount));
        }
        else if (project.BillingMethod == "FixedPrice")
        {
            if (request.ProgressPercentToDate > 0m && request.MilestoneAmount > 0m) return ProjectBillingPreview.Failure("Choose either cumulative progress billing or a milestone amount, not both.");
            decimal gross;
            if (request.ProgressPercentToDate > 0m)
            {
                basis = "FixedPriceProgress";
                var cumulativeAuthorized = RoundCurrency(project.ContractAmount * request.ProgressPercentToDate);
                gross = cumulativeAuthorized - previousGross;
                if (gross <= 0m) return ProjectBillingPreview.Failure("The requested cumulative progress does not exceed prior active or posted project billing.");
            }
            else if (request.MilestoneAmount > 0m) { basis = "FixedPriceMilestone"; gross = RoundCurrency(request.MilestoneAmount); }
            else return ProjectBillingPreview.Failure("Fixed-price billing requires a cumulative progress percentage or milestone amount.");
            lines.Add(new(basis, null, $"{basis}:{invoiceNumber}", description, 1m, gross, 0m, 0m, gross, 0m, gross, revenueAccount));
        }
        else if (project.BillingMethod == "TimeAndMaterials")
        {
            basis = "TimeAndMaterials";
            if (request.IncludeLabor)
            {
                var candidates = await (from entry in db.PayrollTimeEntries
                                        join card in db.PayrollTimecards on entry.PayrollTimecardId equals card.Id
                                        where card.CompanyId == companyId && (card.Status == "Approved" || card.Status == "Consumed") && entry.ProjectJobId == project.Id && entry.WorkDate <= request.BillingThrough && entry.Hours > 0m
                                        orderby entry.WorkDate, entry.Sequence
                                        select entry).ToListAsync(cancellationToken);
                if (request.SelectedTimeEntryIds is not null) candidates = candidates.Where(x => request.SelectedTimeEntryIds.Contains(x.Id)).ToList();
                var rates = await db.ProjectBillingRates.Where(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.IsActive).ToListAsync(cancellationToken);
                foreach (var entry in candidates.Where(x => !blocked.Contains($"TIME:{x.Id:N}")))
                {
                    var rate = rates.Where(x => (x.EarningCode == entry.EarningCode.ToUpper() || x.EarningCode == "*") && x.EffectiveOn <= entry.WorkDate && (x.EffectiveThrough == null || x.EffectiveThrough >= entry.WorkDate)).OrderBy(x => x.EarningCode == "*" ? 1 : 0).ThenByDescending(x => x.EffectiveOn).FirstOrDefault();
                    if (rate is null) return ProjectBillingPreview.Failure($"No effective billing rate covers {entry.EarningCode} time on {entry.WorkDate:yyyy-MM-dd}. Add a specific or * default rate.");
                    var gross = RoundCurrency(entry.Hours * rate.HourlyRate);
                    if (gross > 0m) lines.Add(new("ApprovedTime", entry.Id, $"TIME:{entry.Id:N}", $"{entry.WorkDate:yyyy-MM-dd} {entry.EarningCode} — {entry.Hours:0.####} hours", entry.Hours, rate.HourlyRate, entry.Amount, gross - entry.Amount, gross, 0m, gross, revenueAccount));
                }
            }
            if (request.IncludeCosts) await AddEligibleCostLinesAsync(db, companyId, project.Id, request, blocked, revenueAccount, false, lines, cancellationToken);
        }
        else
        {
            basis = "CostPlus";
            if (!request.IncludeCosts) return ProjectBillingPreview.Failure("Cost-plus billing requires eligible posted project costs.");
            await AddEligibleCostLinesAsync(db, companyId, project.Id, request, blocked, revenueAccount, true, lines, cancellationToken);
        }
        if (lines.Count == 0) return ProjectBillingPreview.Failure("No eligible, unbilled source activity was found for this billing request.");
        var grossTotal = RoundCurrency(lines.Sum(x => x.GrossAmount));
        if (basis != "RetainageRelease" && project.ContractAmount > 0m && previousGross + grossTotal > project.ContractAmount) return ProjectBillingPreview.Failure($"This proposal would bill {previousGross + grossTotal:C} against the authorized {project.ContractAmount:C} contract. Reduce the billing or approve a change order first.");
        var retainageTotal = basis == "RetainageRelease" ? 0m : RoundCurrency(grossTotal * project.RetainagePercent);
        var allocated = 0m;
        for (var index = 0; index < lines.Count; index++)
        {
            var retainage = index == lines.Count - 1 ? retainageTotal - allocated : RoundCurrency(lines[index].GrossAmount * project.RetainagePercent);
            allocated += retainage;
            lines[index] = lines[index] with { RetainageAmount = retainage, InvoiceAmount = lines[index].GrossAmount - retainage };
        }
        var invoiceTotal = grossTotal - retainageTotal;
        if (invoiceTotal <= 0m) return ProjectBillingPreview.Failure("Retainage leaves no amount to invoice. Reduce retainage or use a future retainage-release workflow.");
        var fingerprintPayload = JsonSerializer.Serialize(new { Request = request, project.Id, project.ConcurrencyToken, project.ContractAmount, project.RetainagePercent, previousGross, basis, Lines = lines.Select(x => new { x.SourceKey, x.Quantity, x.UnitPrice, x.SourceCost, x.MarkupAmount, x.GrossAmount, x.RetainageAmount, x.InvoiceAmount }) });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintPayload))).ToLowerInvariant();
        return new(true, string.Empty, project.Id, project.ConcurrencyToken, basis, project.ContractAmount, previousGross, grossTotal, retainageTotal, invoiceTotal, fingerprint, lines);
    }

    private static async Task AddEligibleCostLinesAsync(BrassLedgerDbContext db, Guid companyId, Guid projectId, ProjectBillingPreviewRequest request, HashSet<string> blocked, string revenueAccount, bool includePayroll, List<ProjectBillingPreviewLine> lines, CancellationToken cancellationToken)
    {
        var costs = await (from line in db.JournalEntryLines
                           join entry in db.JournalEntries on line.JournalEntryId equals entry.Id
                           join account in db.Accounts on line.AccountId equals account.Id
                           where entry.CompanyId == companyId && entry.IsPosted && entry.Status == "Posted" && entry.ReversedByJournalEntryId == null && entry.PostedOn <= request.BillingThrough && line.ProjectJobId == projectId && account.Type == AccountType.Expense && (line.Debit - line.Credit) > 0m && (includePayroll || entry.SourceModule != "Payroll")
                           orderby entry.PostedOn, entry.EntryNumber, line.Id
                           select new { Line = line, Entry = entry, Cost = line.Debit - line.Credit }).ToListAsync(cancellationToken);
        if (request.SelectedJournalEntryLineIds is not null) costs = costs.Where(x => request.SelectedJournalEntryLineIds.Contains(x.Line.Id)).ToList();
        foreach (var item in costs.Where(x => !blocked.Contains($"COST:{x.Line.Id:N}")))
        {
            var markup = RoundCurrency(item.Cost * request.CostMarkupPercent);
            var gross = RoundCurrency(item.Cost + markup);
            lines.Add(new("PostedCost", item.Line.Id, $"COST:{item.Line.Id:N}", $"{item.Entry.PostedOn:yyyy-MM-dd} {item.Entry.Reference} — {item.Line.Description}", 1m, gross, item.Cost, markup, gross, 0m, gross, revenueAccount));
        }
    }

    private static async Task<string?> ValidateProjectBillingApprovalAsync(BrassLedgerDbContext db, Guid companyId, ProjectBillingProposal proposal, CancellationToken cancellationToken)
    {
        var project = await db.ProjectJobs.SingleOrDefaultAsync(x => x.Id == proposal.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        if (project is null || project.Status != "Active") return "The billed project is no longer active or available.";
        if (string.IsNullOrWhiteSpace(proposal.PreparedProjectConcurrencyToken)
            || !string.Equals(project.ConcurrencyToken, proposal.PreparedProjectConcurrencyToken, StringComparison.Ordinal))
            return "The project or its billing history changed after this proposal was prepared. Reject and correct the proposal from a fresh preview.";
        if ((proposal.RetainageAmount > 0m || proposal.BillingBasis == "RetainageRelease")
            && !await db.Accounts.AnyAsync(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Asset && account.IsControlAccount && account.OperationalRole == AccountingAccountRoles.RetainageReceivable, cancellationToken))
            return "Configure an active retainage-receivable control account before approving project billing with retained or released amounts.";

        var lines = await db.ProjectBillingLines.Where(x => x.ProjectBillingProposalId == proposal.Id).ToListAsync(cancellationToken);
        if (lines.Count == 0) return "The project billing proposal has no retained billing lines.";
        var sourcedLines = lines.Where(x => x.SourceId.HasValue).ToArray();
        var reservations = await db.ProjectBillingSourceReservations
            .Where(x => x.CompanyId == companyId && x.ProjectBillingProposalId == proposal.Id && x.Status == "Reserved")
            .ToListAsync(cancellationToken);
        if (reservations.Count != sourcedLines.Length
            || sourcedLines.Any(line => !reservations.Any(reservation => reservation.ProjectJobId == proposal.ProjectJobId && reservation.SourceKey == line.SourceKey)))
            return "One or more project billing source reservations changed after this proposal was prepared. Reject and correct it from a fresh preview.";

        foreach (var line in sourcedLines)
        {
            var sourceId = line.SourceId!.Value;
            if (line.SourceType == "ApprovedTime")
            {
                var source = await (from entry in db.PayrollTimeEntries
                                    join card in db.PayrollTimecards on entry.PayrollTimecardId equals card.Id
                                    where entry.Id == sourceId && entry.ProjectJobId == proposal.ProjectJobId && card.CompanyId == companyId
                                        && (card.Status == "Approved" || card.Status == "Consumed")
                                    select new { entry.Hours, entry.Amount, entry.WorkDate }).SingleOrDefaultAsync(cancellationToken);
                if (source is null || source.WorkDate > proposal.BillingThrough || source.Hours != line.Quantity || source.Amount != line.SourceCost)
                    return "Approved time used by this proposal changed or was voided. Reject and correct the proposal from a fresh preview.";
            }
            else if (line.SourceType == "PostedCost")
            {
                var source = await (from journalLine in db.JournalEntryLines
                                    join journal in db.JournalEntries on journalLine.JournalEntryId equals journal.Id
                                    join account in db.Accounts on journalLine.AccountId equals account.Id
                                    where journalLine.Id == sourceId && journalLine.ProjectJobId == proposal.ProjectJobId && journal.CompanyId == companyId
                                        && journal.IsPosted && journal.Status == "Posted" && journal.ReversedByJournalEntryId == null && account.Type == AccountType.Expense
                                    select new { Cost = journalLine.Debit - journalLine.Credit, journal.PostedOn }).SingleOrDefaultAsync(cancellationToken);
                if (source is null || source.PostedOn > proposal.BillingThrough || source.Cost <= 0m || source.Cost != line.SourceCost)
                    return "A posted project cost used by this proposal changed or was reversed. Reject and correct the proposal from a fresh preview.";
            }
            else if (line.SourceType == "RetainageRelease")
            {
                var valid = await db.ProjectBillingProposals.AnyAsync(source => source.Id == sourceId && source.CompanyId == companyId
                    && source.ProjectJobId == proposal.ProjectJobId && source.Status == "Posted" && source.BillingBasis != "RetainageRelease", cancellationToken);
                if (!valid) return "The retainage source billing is no longer posted and eligible. Reject and correct the proposal from a fresh preview.";
            }
            else return $"Project billing source type '{line.SourceType}' is not supported for approval.";
        }

        return null;
    }

    private void AddProjectBillingAudit(BrassLedgerDbContext db, Guid companyId, string action, Guid entityId, Guid projectId, object detail) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = nameof(ProjectBillingProposal), EntityId = entityId, DetailJson = JsonSerializer.Serialize(new { projectId, detail }), OccurredAtUtc = DateTimeOffset.UtcNow });
}
