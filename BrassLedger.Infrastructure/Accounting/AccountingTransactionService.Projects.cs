using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    private static readonly string[] SupportedProjectBillingMethods = ["FixedPrice", "TimeAndMaterials", "CostPlus", "Internal"];
    private static readonly string[] SupportedRevenueRecognitionMethods = ["AsBilled", "CostToCost", "ManualPercent", "CompletedContract"];
    private const decimal MaxProjectAmount = 9_999_999_999_999_999.99m;

    public async Task<TransactionResult> SaveProjectJobAsync(SaveProjectJobRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectsManage)) return TransactionResult.Failure("You are not authorized to manage projects.");
        var jobNumber = request.JobNumber.Trim();
        var name = request.Name.Trim();
        var billingMethod = request.BillingMethod.Trim();
        var recognitionMethod = request.RevenueRecognitionMethod.Trim();
        if (jobNumber.Length is < 1 or > 50 || name.Length is < 1 or > 200) return TransactionResult.Failure("A project number of at most 50 characters and a name of at most 200 characters are required.");
        if (request.CustomerId == Guid.Empty) return TransactionResult.Failure("Select a customer for this project.");
        if (request.ExpectedEndDate.HasValue && request.ExpectedEndDate < request.StartDate) return TransactionResult.Failure("The expected end date cannot precede the project start date.");
        if (!SupportedProjectBillingMethods.Contains(billingMethod, StringComparer.Ordinal)) return TransactionResult.Failure("Billing method must be FixedPrice, TimeAndMaterials, CostPlus, or Internal.");
        if (!SupportedRevenueRecognitionMethods.Contains(recognitionMethod, StringComparer.Ordinal)) return TransactionResult.Failure("Revenue recognition must be AsBilled, CostToCost, ManualPercent, or CompletedContract.");
        if (billingMethod == "Internal" && recognitionMethod != "AsBilled") return TransactionResult.Failure("Internal projects must use as-billed recognition because they have no customer contract revenue.");
        if (request.ContractAmount < 0m || request.BudgetAmount < 0m || request.ContractAmount > MaxProjectAmount || request.BudgetAmount > MaxProjectAmount || request.RetainagePercent is < 0m or > 1m) return TransactionResult.Failure("Contract and budget values must fit an 18-digit currency amount, cannot be negative, and retainage must be from 0% through 100%.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var customer = await db.Customers.SingleOrDefaultAsync(candidate => candidate.Id == request.CustomerId && candidate.CompanyId == companyId, cancellationToken);
        if (customer is null) return TransactionResult.Failure("Active project customer not found in this company.");
        if (await db.ProjectJobs.AnyAsync(candidate => candidate.CompanyId == companyId && candidate.JobNumber == jobNumber && candidate.Id != request.Id, cancellationToken)) return TransactionResult.Failure("Project number already exists in this company.");

        var now = DateTimeOffset.UtcNow;
        ProjectJob job;
        object? prior = null;
        if (request.Id.HasValue)
        {
            job = await db.ProjectJobs.SingleOrDefaultAsync(candidate => candidate.Id == request.Id.Value && candidate.CompanyId == companyId, cancellationToken) ?? new ProjectJob();
            if (job.Id == Guid.Empty) return TransactionResult.Failure("Project not found.");
            if (job.Status == "Closed") return TransactionResult.Failure("Reopen a closed project before changing its setup or budget.");
            if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(job.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project changed after it was displayed. Refresh before saving it.");
            var contractChanged = RoundCurrency(request.ContractAmount) != job.ContractAmount;
            var budgetChanged = RoundCurrency(request.BudgetAmount) != job.BudgetAmount;
            var billingTermsChanged = request.CustomerId != job.CustomerId
                || !string.Equals(billingMethod, job.BillingMethod, StringComparison.Ordinal)
                || request.RetainagePercent != job.RetainagePercent;
            var recognitionChanged = !string.Equals(recognitionMethod, job.RevenueRecognitionMethod, StringComparison.Ordinal);
            if ((contractChanged || budgetChanged) && (await db.ProjectChangeOrders.AnyAsync(change => change.ProjectJobId == job.Id, cancellationToken) || await HasProjectScopeActivityAsync(db, job.Id, cancellationToken)))
                return TransactionResult.Failure("Use a controlled project change order to revise contract or budget amounts after project activity begins.");
            if (billingTermsChanged && await db.ProjectBillingProposals.AnyAsync(proposal => proposal.ProjectJobId == job.Id, cancellationToken))
                return TransactionResult.Failure("Customer, billing method, and retainage cannot be changed after project billing history exists. Create a new project or use the applicable controlled billing workflow.");
            if (recognitionChanged && await db.ProjectWipSchedules.AnyAsync(schedule => schedule.ProjectJobId == job.Id, cancellationToken))
                return TransactionResult.Failure("Revenue-recognition method cannot be changed after WIP schedule history exists. Reverse and retain the historical method, or create a new project.");
            prior = new { job.JobNumber, job.Name, job.CustomerId, job.CustomerName, job.StartDate, job.ExpectedEndDate, job.BillingMethod, job.RevenueRecognitionMethod, job.ContractAmount, job.BudgetAmount, job.RetainagePercent, job.Status };
        }
        else
        {
            job = new ProjectJob { Id = Guid.NewGuid(), CompanyId = companyId, Status = "Active", CreatedByUserId = ResolveUserId(), CreatedAtUtc = now };
            db.ProjectJobs.Add(job);
        }

        job.JobNumber = jobNumber;
        job.Name = name;
        job.CustomerId = customer.Id;
        job.CustomerName = customer.Name;
        job.StartDate = request.StartDate;
        job.ExpectedEndDate = request.ExpectedEndDate;
        job.BillingMethod = billingMethod;
        job.RevenueRecognitionMethod = recognitionMethod;
        job.ContractAmount = RoundCurrency(request.ContractAmount);
        job.BudgetAmount = RoundCurrency(request.BudgetAmount);
        job.RetainagePercent = request.RetainagePercent;
        job.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectAudit(db, companyId, prior is null ? "project.created" : "project.updated", job, new { prior, current = new { job.JobNumber, job.Name, job.CustomerId, job.StartDate, job.ExpectedEndDate, job.BillingMethod, job.RevenueRecognitionMethod, job.ContractAmount, job.BudgetAmount, job.RetainagePercent, job.Status } });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The project number is already in use or its related records changed. Refresh and try again."); }
        return TransactionResult.Success(job.Id);
    }

    public async Task<TransactionResult> CloseProjectJobAsync(CloseProjectJobRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectsManage)) return TransactionResult.Failure("You are not authorized to close projects.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A project close reason is required.");
        if (request.Reason.Trim().Length > 1000) return TransactionResult.Failure("The project close reason cannot exceed 1,000 characters.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var job = await db.ProjectJobs.SingleOrDefaultAsync(candidate => candidate.Id == request.ProjectJobId && candidate.CompanyId == companyId, cancellationToken);
        if (job is null) return TransactionResult.Failure("Project not found.");
        if (job.Status != "Active") return TransactionResult.Failure("Only an active project can be closed.");
        if (job.StartDate.HasValue && request.ClosedOn < job.StartDate) return TransactionResult.Failure("The close date cannot precede the project start date.");
        if (!string.Equals(job.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project changed after it was displayed. Refresh before closing it.");
        if (await HasOpenProjectActivityAsync(db, companyId, job.Id, cancellationToken)) return TransactionResult.Failure("Resolve open journals, quotes, orders, requisitions, purchase commitments, timecards, and payroll runs before closing this project.");
        job.Status = "Closed";
        job.ClosedOn = request.ClosedOn;
        job.ClosedByUserId = ResolveUserId();
        job.ClosedAtUtc = DateTimeOffset.UtcNow;
        job.CloseReason = request.Reason.Trim();
        job.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectAudit(db, companyId, "project.closed", job, new { job.ClosedOn, job.CloseReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project changed while it was being closed. Refresh and try again."); }
        return TransactionResult.Success(job.Id);
    }

    public async Task<TransactionResult> ReopenProjectJobAsync(ReopenProjectJobRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectsManage)) return TransactionResult.Failure("You are not authorized to reopen projects.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A project reopen reason is required.");
        if (request.Reason.Trim().Length > 1000) return TransactionResult.Failure("The project reopen reason cannot exceed 1,000 characters.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var job = await db.ProjectJobs.SingleOrDefaultAsync(candidate => candidate.Id == request.ProjectJobId && candidate.CompanyId == companyId, cancellationToken);
        if (job is null) return TransactionResult.Failure("Project not found.");
        if (job.Status != "Closed") return TransactionResult.Failure("Only a closed project can be reopened.");
        if (!string.Equals(job.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project changed after it was displayed. Refresh before reopening it.");
        if (job.RevenueRecognitionMethod == "CompletedContract" && await db.ProjectWipSchedules.AnyAsync(schedule => schedule.CompanyId == companyId && schedule.ProjectJobId == job.Id && schedule.Status == "Posted", cancellationToken))
            return TransactionResult.Failure("Reverse posted completed-contract WIP before reopening the project so recognized revenue does not remain based on a completed status that is no longer true.");
        var priorClosedOn = job.ClosedOn;
        var priorCloseReason = job.CloseReason;
        job.Status = "Active";
        job.ClosedOn = null;
        job.ClosedByUserId = null;
        job.ClosedAtUtc = null;
        job.CloseReason = string.Empty;
        job.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectAudit(db, companyId, "project.reopened", job, new { reason = request.Reason.Trim(), priorClosedOn, priorCloseReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project changed while it was being reopened. Refresh and try again."); }
        return TransactionResult.Success(job.Id);
    }

    public async Task<TransactionResult> SaveProjectChangeOrderDraftAsync(SaveProjectChangeOrderDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectChangeOrderPrepare)) return TransactionResult.Failure("You are not authorized to prepare project change orders.");
        var number = request.ChangeOrderNumber.Trim().ToUpperInvariant();
        var description = request.Description.Trim();
        var reason = request.Reason.Trim();
        if (request.ProjectJobId == Guid.Empty) return TransactionResult.Failure("Select a project for the change order.");
        if (number.Length is < 1 or > 50) return TransactionResult.Failure("A change-order number of at most 50 characters is required.");
        if (description.Length is < 1 or > 500) return TransactionResult.Failure("A change-order description of at most 500 characters is required.");
        if (reason.Length is < 1 or > 1000) return TransactionResult.Failure("A change-order reason of at most 1,000 characters is required.");
        if (request.EffectiveOn < request.RequestedOn) return TransactionResult.Failure("The change-order effective date cannot precede its request date.");
        var contractChange = RoundCurrency(request.ContractAmountChange);
        var budgetChange = RoundCurrency(request.BudgetAmountChange);
        if (contractChange == 0m && budgetChange == 0m) return TransactionResult.Failure("Enter a contract or budget change amount.");
        if (contractChange is < -MaxProjectAmount or > MaxProjectAmount || budgetChange is < -MaxProjectAmount or > MaxProjectAmount) return TransactionResult.Failure("Contract and budget changes must fit an 18-digit currency amount.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var project = await db.ProjectJobs.SingleOrDefaultAsync(candidate => candidate.Id == request.ProjectJobId && candidate.CompanyId == companyId, cancellationToken);
        if (project is null) return TransactionResult.Failure("Project not found in this company.");
        if (project.Status != "Active") return TransactionResult.Failure("Only an active project can accept a change order.");
        if (project.StartDate.HasValue && request.EffectiveOn < project.StartDate) return TransactionResult.Failure("The change-order effective date cannot precede the project start date.");
        if (await db.ProjectChangeOrders.AnyAsync(candidate => candidate.CompanyId == companyId && candidate.ProjectJobId == project.Id && candidate.ChangeOrderNumber == number && candidate.Id != request.Id, cancellationToken)) return TransactionResult.Failure("Change-order number already exists for this project.");

        var now = DateTimeOffset.UtcNow;
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required to prepare a project change order.");
        ProjectChangeOrder changeOrder;
        object? prior = null;
        if (request.Id.HasValue)
        {
            changeOrder = await db.ProjectChangeOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.Id.Value && candidate.CompanyId == companyId && candidate.ProjectJobId == request.ProjectJobId, cancellationToken) ?? new ProjectChangeOrder();
            if (changeOrder.Id == Guid.Empty) return TransactionResult.Failure("Project change order not found.");
            if (changeOrder.Status is not ("Draft" or "Rejected")) return TransactionResult.Failure("Only a draft or rejected change order can be corrected.");
            if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(changeOrder.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project change order changed after it was displayed. Refresh before saving it.");
            prior = ProjectChangeOrderAuditState(changeOrder);
        }
        else
        {
            changeOrder = new ProjectChangeOrder { Id = Guid.NewGuid(), CompanyId = companyId, ProjectJobId = project.Id };
            db.ProjectChangeOrders.Add(changeOrder);
        }

        changeOrder.ChangeOrderNumber = number;
        changeOrder.Description = description;
        changeOrder.Reason = reason;
        changeOrder.RequestedOn = request.RequestedOn;
        changeOrder.EffectiveOn = request.EffectiveOn;
        changeOrder.ContractAmountChange = contractChange;
        changeOrder.BudgetAmountChange = budgetChange;
        changeOrder.Status = "Draft";
        changeOrder.PreparedByUserId = userId.Value;
        changeOrder.PreparedAtUtc = now;
        changeOrder.SubmittedByUserId = null;
        changeOrder.SubmittedAtUtc = null;
        changeOrder.SubmittedProjectConcurrencyToken = string.Empty;
        changeOrder.DecidedByUserId = null;
        changeOrder.DecidedAtUtc = null;
        changeOrder.DecisionReason = string.Empty;
        changeOrder.ContractAmountBefore = null;
        changeOrder.ContractAmountAfter = null;
        changeOrder.BudgetAmountBefore = null;
        changeOrder.BudgetAmountAfter = null;
        changeOrder.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectChangeOrderAudit(db, companyId, prior is null ? "project-change-order.created" : "project-change-order.corrected", changeOrder, new { prior, current = ProjectChangeOrderAuditState(changeOrder) });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project change order changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The change-order number is already in use or related project data changed. Refresh and try again."); }
        return TransactionResult.Success(changeOrder.Id);
    }

    public async Task<TransactionResult> SubmitProjectChangeOrderAsync(SubmitProjectChangeOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectChangeOrderPrepare)) return TransactionResult.Failure("You are not authorized to submit project change orders.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var changeOrder = await db.ProjectChangeOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.ProjectChangeOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (changeOrder is null) return TransactionResult.Failure("Project change order not found.");
        if (changeOrder.Status != "Draft") return TransactionResult.Failure("Only a draft project change order can be submitted.");
        if (!string.Equals(changeOrder.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project change order changed after it was displayed. Refresh before submitting it.");
        var submittingUserId = ResolveUserId();
        if (!submittingUserId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required to submit a project change order.");
        var project = await db.ProjectJobs.SingleAsync(candidate => candidate.Id == changeOrder.ProjectJobId && candidate.CompanyId == companyId, cancellationToken);
        if (project.Status != "Active") return TransactionResult.Failure("Only an active project can accept a submitted change order.");
        var submittedContractAmount = project.ContractAmount + changeOrder.ContractAmountChange;
        var submittedBudgetAmount = project.BudgetAmount + changeOrder.BudgetAmountChange;
        if (submittedContractAmount is < 0m or > MaxProjectAmount || submittedBudgetAmount is < 0m or > MaxProjectAmount) return TransactionResult.Failure("This change order would make the project contract or budget negative or exceed the supported currency range.");
        changeOrder.Status = "Submitted";
        changeOrder.SubmittedByUserId = submittingUserId.Value;
        changeOrder.SubmittedAtUtc = DateTimeOffset.UtcNow;
        changeOrder.SubmittedProjectConcurrencyToken = project.ConcurrencyToken;
        changeOrder.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectChangeOrderAudit(db, companyId, "project-change-order.submitted", changeOrder, ProjectChangeOrderAuditState(changeOrder));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project change order changed while it was being submitted. Refresh and try again."); }
        return TransactionResult.Success(changeOrder.Id);
    }

    public async Task<TransactionResult> DecideProjectChangeOrderAsync(DecideProjectChangeOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectChangeOrderApprove)) return TransactionResult.Failure("You are not authorized to decide project change orders.");
        var decisionReason = request.Reason.Trim();
        if (decisionReason.Length is < 1 or > 1000) return TransactionResult.Failure("A decision reason of at most 1,000 characters is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var changeOrder = await db.ProjectChangeOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.ProjectChangeOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (changeOrder is null) return TransactionResult.Failure("Project change order not found.");
        if (changeOrder.Status != "Submitted") return TransactionResult.Failure("Only a submitted project change order can be decided.");
        if (!string.Equals(changeOrder.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project change order changed after it was displayed. Refresh before deciding it.");
        var userId = ResolveUserId();
        if (!userId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required to decide a project change order.");
        if (changeOrder.PreparedByUserId == userId.Value || changeOrder.SubmittedByUserId == userId.Value) return TransactionResult.Failure("The preparer or submitter cannot approve or reject the same project change order.");
        var project = await db.ProjectJobs.SingleAsync(candidate => candidate.Id == changeOrder.ProjectJobId && candidate.CompanyId == companyId, cancellationToken);

        changeOrder.DecidedByUserId = userId.Value;
        changeOrder.DecidedAtUtc = DateTimeOffset.UtcNow;
        changeOrder.DecisionReason = decisionReason;
        if (request.Approve)
        {
            if (project.Status != "Active") return TransactionResult.Failure("Only an active project can accept an approved change order.");
            if (!string.Equals(project.ConcurrencyToken, changeOrder.SubmittedProjectConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project changed after this change order was submitted. Reject it for correction before reconsidering it.");
            var contractAfter = RoundCurrency(project.ContractAmount + changeOrder.ContractAmountChange);
            var budgetAfter = RoundCurrency(project.BudgetAmount + changeOrder.BudgetAmountChange);
            if (contractAfter is < 0m or > MaxProjectAmount || budgetAfter is < 0m or > MaxProjectAmount) return TransactionResult.Failure("This change order would make the project contract or budget negative or exceed the supported currency range.");
            changeOrder.ContractAmountBefore = project.ContractAmount;
            changeOrder.ContractAmountAfter = contractAfter;
            changeOrder.BudgetAmountBefore = project.BudgetAmount;
            changeOrder.BudgetAmountAfter = budgetAfter;
            changeOrder.Status = "Approved";
            project.ContractAmount = contractAfter;
            project.BudgetAmount = budgetAfter;
            project.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }
        else
        {
            changeOrder.Status = "Rejected";
        }
        changeOrder.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectChangeOrderAudit(db, companyId, request.Approve ? "project-change-order.approved" : "project-change-order.rejected", changeOrder, new { decisionReason, changeOrder.ContractAmountBefore, changeOrder.ContractAmountAfter, changeOrder.BudgetAmountBefore, changeOrder.BudgetAmountAfter });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project or change order changed while the decision was being saved. Refresh and try again."); }
        return TransactionResult.Success(changeOrder.Id);
    }

    public async Task<TransactionResult> CancelProjectChangeOrderAsync(CancelProjectChangeOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectChangeOrderPrepare)) return TransactionResult.Failure("You are not authorized to cancel project change orders.");
        var cancellationReason = request.Reason.Trim();
        if (cancellationReason.Length is < 1 or > 1000) return TransactionResult.Failure("A cancellation reason of at most 1,000 characters is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var changeOrder = await db.ProjectChangeOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.ProjectChangeOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (changeOrder is null) return TransactionResult.Failure("Project change order not found.");
        if (changeOrder.Status is not ("Draft" or "Submitted" or "Rejected")) return TransactionResult.Failure("Only a draft, submitted, or rejected change order can be cancelled.");
        if (!string.Equals(changeOrder.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project change order changed after it was displayed. Refresh before cancelling it.");
        var cancellingUserId = ResolveUserId();
        if (!cancellingUserId.HasValue) return TransactionResult.Failure("An authenticated operator identity is required to cancel a project change order.");
        changeOrder.Status = "Cancelled";
        changeOrder.CancelledByUserId = cancellingUserId.Value;
        changeOrder.CancelledAtUtc = DateTimeOffset.UtcNow;
        changeOrder.CancellationReason = cancellationReason;
        changeOrder.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectChangeOrderAudit(db, companyId, "project-change-order.cancelled", changeOrder, new { cancellationReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project change order changed while it was being cancelled. Refresh and try again."); }
        return TransactionResult.Success(changeOrder.Id);
    }

    private static async Task<bool> HasOpenProjectActivityAsync(BrassLedgerDbContext db, Guid companyId, Guid projectJobId, CancellationToken cancellationToken)
    {
        if (await db.ProjectChangeOrders.AnyAsync(changeOrder => changeOrder.CompanyId == companyId && changeOrder.ProjectJobId == projectJobId && changeOrder.Status != "Approved" && changeOrder.Status != "Cancelled", cancellationToken)) return true;
        if (await db.ProjectBillingProposals.AnyAsync(proposal => proposal.CompanyId == companyId && proposal.ProjectJobId == projectJobId && proposal.Status != "Posted" && proposal.Status != "Cancelled" && proposal.Status != "Voided", cancellationToken)) return true;
        if (await db.ProjectWipSchedules.AnyAsync(schedule => schedule.CompanyId == companyId && schedule.ProjectJobId == projectJobId && schedule.Status != "Posted" && schedule.Status != "Cancelled" && schedule.Status != "Reversed", cancellationToken)) return true;
        if (await db.JournalEntryLines.Where(line => line.ProjectJobId == projectJobId).Join(db.JournalEntries.Where(entry => entry.CompanyId == companyId && !entry.IsPosted && entry.Status != "Cancelled"), line => line.JournalEntryId, entry => entry.Id, (_, _) => true).AnyAsync(cancellationToken)) return true;
        if (await db.SalesQuoteLines.Where(line => line.ProjectJobId == projectJobId).Join(db.SalesQuotes.Where(quote => quote.CompanyId == companyId && (quote.Status == "Draft" || quote.Status == "Approved")), line => line.SalesQuoteId, quote => quote.Id, (_, _) => true).AnyAsync(cancellationToken)) return true;
        if (await db.SalesOrderLines.Where(line => line.ProjectJobId == projectJobId).Join(db.SalesOrders.Where(order => order.CompanyId == companyId && order.Status != "Closed" && order.Status != "Cancelled"), line => line.SalesOrderId, order => order.Id, (_, _) => true).AnyAsync(cancellationToken)) return true;
        if (await db.PurchaseOrderLines.Where(line => line.ProjectJobId == projectJobId).Join(db.PurchaseOrders.Where(order => order.CompanyId == companyId && order.Status != "Closed" && order.Status != "Cancelled"), line => line.PurchaseOrderId, order => order.Id, (_, _) => true).AnyAsync(cancellationToken)) return true;
        if (await db.PurchaseRequisitionLines.Where(line => line.ProjectJobId == projectJobId).Join(db.PurchaseRequisitions.Where(requisition => requisition.CompanyId == companyId && requisition.Status != "Cancelled" && requisition.Status != "Converted"), line => line.PurchaseRequisitionId, requisition => requisition.Id, (_, _) => true).AnyAsync(cancellationToken)) return true;
        if (await db.PayrollTimeEntries.Where(entry => entry.ProjectJobId == projectJobId).Join(db.PayrollTimecards.Where(card => card.CompanyId == companyId && (card.Status == "Draft" || card.Status == "Submitted" || card.Status == "Approved")), entry => entry.PayrollTimecardId, card => card.Id, (_, _) => true).AnyAsync(cancellationToken)) return true;
        return await db.PayrollEarningLines.Where(line => line.ProjectJobId == projectJobId)
            .Join(db.PayrollRunEmployeeLines, line => line.PayrollRunEmployeeLineId, employeeLine => employeeLine.Id, (_, employeeLine) => employeeLine.PayrollRunId)
            .Join(db.PayrollRuns.Where(run => run.CompanyId == companyId && (run.Status == "Draft" || run.Status == "Approved" || run.Status == "Rejected")), runId => runId, run => run.Id, (_, _) => true)
            .AnyAsync(cancellationToken);
    }

    private static async Task<bool> HasProjectScopeActivityAsync(BrassLedgerDbContext db, Guid projectJobId, CancellationToken cancellationToken)
    {
        if (await db.JournalEntryLines.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.SalesInvoiceLines.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.VendorBillLines.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.SalesQuoteLines.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.SalesOrderLines.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.PurchaseRequisitionLines.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.PurchaseOrderLines.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.PayrollTimeEntries.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.ProjectBillingProposals.AnyAsync(proposal => proposal.ProjectJobId == projectJobId, cancellationToken)) return true;
        if (await db.ProjectWipSchedules.AnyAsync(schedule => schedule.ProjectJobId == projectJobId, cancellationToken)) return true;
        return await db.PayrollEarningLines.AnyAsync(line => line.ProjectJobId == projectJobId, cancellationToken);
    }

    private static async Task<bool> AreActiveProjectsAsync(BrassLedgerDbContext db, Guid companyId, IEnumerable<Guid?> requestedProjectIds, CancellationToken cancellationToken)
    {
        var ids = requestedProjectIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        return ids.Length == 0 || await db.ProjectJobs.CountAsync(project => project.CompanyId == companyId && project.Status == "Active" && ids.Contains(project.Id), cancellationToken) == ids.Length;
    }

    private void AddProjectAudit(BrassLedgerDbContext db, Guid companyId, string action, ProjectJob job, object detail) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = nameof(ProjectJob), EntityId = job.Id, DetailJson = JsonSerializer.Serialize(detail), OccurredAtUtc = DateTimeOffset.UtcNow });

    private static object ProjectChangeOrderAuditState(ProjectChangeOrder changeOrder) => new { changeOrder.ProjectJobId, changeOrder.ChangeOrderNumber, changeOrder.Description, changeOrder.Reason, changeOrder.RequestedOn, changeOrder.EffectiveOn, changeOrder.ContractAmountChange, changeOrder.BudgetAmountChange, changeOrder.Status };

    private void AddProjectChangeOrderAudit(BrassLedgerDbContext db, Guid companyId, string action, ProjectChangeOrder changeOrder, object detail) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = nameof(ProjectChangeOrder), EntityId = changeOrder.Id, DetailJson = JsonSerializer.Serialize(new { changeOrder.ProjectJobId, detail }), OccurredAtUtc = DateTimeOffset.UtcNow });
}
