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

    public async Task<TransactionResult> SaveProjectJobAsync(SaveProjectJobRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectsManage)) return TransactionResult.Failure("You are not authorized to manage projects.");
        var jobNumber = request.JobNumber.Trim();
        var name = request.Name.Trim();
        var billingMethod = request.BillingMethod.Trim();
        if (jobNumber.Length is < 1 or > 50 || name.Length is < 1 or > 200) return TransactionResult.Failure("A project number of at most 50 characters and a name of at most 200 characters are required.");
        if (request.CustomerId == Guid.Empty) return TransactionResult.Failure("Select a customer for this project.");
        if (request.ExpectedEndDate.HasValue && request.ExpectedEndDate < request.StartDate) return TransactionResult.Failure("The expected end date cannot precede the project start date.");
        if (!SupportedProjectBillingMethods.Contains(billingMethod, StringComparer.Ordinal)) return TransactionResult.Failure("Billing method must be FixedPrice, TimeAndMaterials, CostPlus, or Internal.");
        if (request.ContractAmount < 0m || request.BudgetAmount < 0m || request.RetainagePercent is < 0m or > 1m) return TransactionResult.Failure("Contract, budget, and retainage values cannot be negative, and retainage cannot exceed 100%.");

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
            prior = new { job.JobNumber, job.Name, job.CustomerId, job.CustomerName, job.StartDate, job.ExpectedEndDate, job.BillingMethod, job.ContractAmount, job.BudgetAmount, job.RetainagePercent, job.Status };
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
        job.ContractAmount = RoundCurrency(request.ContractAmount);
        job.BudgetAmount = RoundCurrency(request.BudgetAmount);
        job.RetainagePercent = request.RetainagePercent;
        job.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectAudit(db, companyId, prior is null ? "project.created" : "project.updated", job, new { prior, current = new { job.JobNumber, job.Name, job.CustomerId, job.StartDate, job.ExpectedEndDate, job.BillingMethod, job.ContractAmount, job.BudgetAmount, job.RetainagePercent, job.Status } });
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

    private static async Task<bool> HasOpenProjectActivityAsync(BrassLedgerDbContext db, Guid companyId, Guid projectJobId, CancellationToken cancellationToken)
    {
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

    private static async Task<bool> AreActiveProjectsAsync(BrassLedgerDbContext db, Guid companyId, IEnumerable<Guid?> requestedProjectIds, CancellationToken cancellationToken)
    {
        var ids = requestedProjectIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        return ids.Length == 0 || await db.ProjectJobs.CountAsync(project => project.CompanyId == companyId && project.Status == "Active" && ids.Contains(project.Id), cancellationToken) == ids.Length;
    }

    private void AddProjectAudit(BrassLedgerDbContext db, Guid companyId, string action, ProjectJob job, object detail) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = nameof(ProjectJob), EntityId = job.Id, DetailJson = JsonSerializer.Serialize(detail), OccurredAtUtc = DateTimeOffset.UtcNow });
}
