using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    private static readonly string[] SupportedProjectPhaseKinds = ["Phase", "Task", "WorkPackage"];

    public async Task<TransactionResult> SaveProjectPhaseAsync(SaveProjectPhaseRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectsManage)) return TransactionResult.Failure("You are not authorized to manage project phases.");
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var kind = request.Kind.Trim();
        var description = request.Description.Trim();
        if (request.ProjectJobId == Guid.Empty) return TransactionResult.Failure("Select a project for the phase.");
        if (code.Length is < 1 or > 50 || name.Length is < 1 or > 200) return TransactionResult.Failure("A phase code of at most 50 characters and a name of at most 200 characters are required.");
        if (!SupportedProjectPhaseKinds.Contains(kind, StringComparer.Ordinal)) return TransactionResult.Failure("Phase kind must be Phase, Task, or WorkPackage.");
        if (description.Length > 1000) return TransactionResult.Failure("The phase description cannot exceed 1,000 characters.");
        if (request.StartsOn.HasValue && request.EndsOn.HasValue && request.EndsOn < request.StartsOn) return TransactionResult.Failure("The phase end date cannot precede its start date.");
        if (request.Id.HasValue && request.ParentProjectPhaseId == request.Id) return TransactionResult.Failure("A phase cannot be its own parent.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var project = await db.ProjectJobs.SingleOrDefaultAsync(x => x.Id == request.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        if (project is null) return TransactionResult.Failure("Project not found in this company.");
        if (project.Status != "Active") return TransactionResult.Failure("Reopen the project before changing its phase structure.");
        if (await db.ProjectPhases.AnyAsync(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.Code == code && x.Id != request.Id, cancellationToken)) return TransactionResult.Failure("Phase code already exists for this project.");

        ProjectPhase? parent = null;
        if (request.ParentProjectPhaseId.HasValue)
        {
            parent = await db.ProjectPhases.SingleOrDefaultAsync(x => x.Id == request.ParentProjectPhaseId && x.CompanyId == companyId && x.ProjectJobId == project.Id, cancellationToken);
            if (parent is null) return TransactionResult.Failure("The parent phase does not belong to this project.");
            if (request.IsActive && !parent.IsActive) return TransactionResult.Failure("An active phase cannot be placed under an inactive parent.");
            var ancestorId = parent.ParentProjectPhaseId;
            while (ancestorId.HasValue)
            {
                if (ancestorId == request.Id) return TransactionResult.Failure("The selected parent would create a phase hierarchy cycle.");
                ancestorId = await db.ProjectPhases.Where(x => x.Id == ancestorId.Value && x.CompanyId == companyId && x.ProjectJobId == project.Id).Select(x => x.ParentProjectPhaseId).SingleOrDefaultAsync(cancellationToken);
            }
            if (parent.StartsOn.HasValue && request.StartsOn.HasValue && request.StartsOn < parent.StartsOn) return TransactionResult.Failure("A child phase cannot start before its parent phase.");
            if (parent.EndsOn.HasValue && request.EndsOn.HasValue && request.EndsOn > parent.EndsOn) return TransactionResult.Failure("A child phase cannot end after its parent phase.");
        }

        ProjectPhase phase;
        object? prior = null;
        var now = DateTimeOffset.UtcNow;
        if (request.Id.HasValue)
        {
            phase = await db.ProjectPhases.SingleOrDefaultAsync(x => x.Id == request.Id.Value && x.CompanyId == companyId && x.ProjectJobId == project.Id, cancellationToken) ?? new ProjectPhase();
            if (phase.Id == Guid.Empty) return TransactionResult.Failure("Project phase not found.");
            if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(phase.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project phase changed after it was displayed. Refresh before saving it.");
            if (!request.IsActive && await db.ProjectPhases.AnyAsync(x => x.ParentProjectPhaseId == phase.Id && x.IsActive, cancellationToken)) return TransactionResult.Failure("Deactivate active child phases before deactivating their parent.");
            if (request.StartsOn.HasValue && await db.ProjectPhases.AnyAsync(x => x.ParentProjectPhaseId == phase.Id && x.StartsOn.HasValue && x.StartsOn < request.StartsOn, cancellationToken)) return TransactionResult.Failure("The phase cannot start after one of its child phases.");
            if (request.EndsOn.HasValue && await db.ProjectPhases.AnyAsync(x => x.ParentProjectPhaseId == phase.Id && x.EndsOn.HasValue && x.EndsOn > request.EndsOn, cancellationToken)) return TransactionResult.Failure("The phase cannot end before one of its child phases.");
            prior = ProjectPhaseAuditState(phase);
            phase.UpdatedByUserId = ResolveUserId();
            phase.UpdatedAtUtc = now;
        }
        else
        {
            phase = new ProjectPhase { Id = Guid.NewGuid(), CompanyId = companyId, ProjectJobId = project.Id, CreatedByUserId = ResolveUserId(), CreatedAtUtc = now };
            db.ProjectPhases.Add(phase);
        }

        phase.ParentProjectPhaseId = parent?.Id;
        phase.Code = code;
        phase.Name = name;
        phase.Kind = kind;
        phase.Description = description;
        phase.StartsOn = request.StartsOn;
        phase.EndsOn = request.EndsOn;
        phase.IsActive = request.IsActive;
        phase.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectDimensionAudit(db, companyId, prior is null ? "project-phase.created" : "project-phase.updated", nameof(ProjectPhase), phase.Id, new { prior, current = ProjectPhaseAuditState(phase) });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project phase changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The phase code is already in use or related project data changed. Refresh and try again."); }
        return TransactionResult.Success(phase.Id);
    }

    public async Task<TransactionResult> SaveProjectCostCodeAsync(SaveProjectCostCodeRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectsManage)) return TransactionResult.Failure("You are not authorized to manage project cost codes.");
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var category = request.Category.Trim();
        var description = request.Description.Trim();
        if (code.Length is < 1 or > 50 || name.Length is < 1 or > 200) return TransactionResult.Failure("A cost code of at most 50 characters and a name of at most 200 characters are required.");
        if (category.Length > 100 || description.Length > 1000) return TransactionResult.Failure("Cost-code category cannot exceed 100 characters and description cannot exceed 1,000 characters.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.ProjectCostCodes.AnyAsync(x => x.CompanyId == companyId && x.Code == code && x.Id != request.Id, cancellationToken)) return TransactionResult.Failure("Cost code already exists in this company.");
        var now = DateTimeOffset.UtcNow;
        ProjectCostCode costCode;
        object? prior = null;
        if (request.Id.HasValue)
        {
            costCode = await db.ProjectCostCodes.SingleOrDefaultAsync(x => x.Id == request.Id.Value && x.CompanyId == companyId, cancellationToken) ?? new ProjectCostCode();
            if (costCode.Id == Guid.Empty) return TransactionResult.Failure("Project cost code not found.");
            if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(costCode.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project cost code changed after it was displayed. Refresh before saving it.");
            prior = ProjectCostCodeAuditState(costCode);
            costCode.UpdatedByUserId = ResolveUserId();
            costCode.UpdatedAtUtc = now;
        }
        else
        {
            costCode = new ProjectCostCode { Id = Guid.NewGuid(), CompanyId = companyId, CreatedByUserId = ResolveUserId(), CreatedAtUtc = now };
            db.ProjectCostCodes.Add(costCode);
        }

        costCode.Code = code;
        costCode.Name = name;
        costCode.Category = category;
        costCode.Description = description;
        costCode.IsActive = request.IsActive;
        costCode.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectDimensionAudit(db, companyId, prior is null ? "project-cost-code.created" : "project-cost-code.updated", nameof(ProjectCostCode), costCode.Id, new { prior, current = ProjectCostCodeAuditState(costCode) });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project cost code changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The cost code is already in use or related company data changed. Refresh and try again."); }
        return TransactionResult.Success(costCode.Id);
    }

    public async Task<TransactionResult> SaveProjectBudgetAllocationAsync(SaveProjectBudgetAllocationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ProjectsManage)) return TransactionResult.Failure("You are not authorized to manage project budgets and forecasts.");
        if (request.ProjectJobId == Guid.Empty) return TransactionResult.Failure("Select a project for the budget allocation.");
        if (request.PeriodEnd < request.PeriodStart) return TransactionResult.Failure("The budget period end cannot precede its start.");
        var budget = RoundCurrency(request.BudgetAmount);
        var forecast = RoundCurrency(request.ForecastAmount);
        if (budget < 0m || forecast < 0m || budget > MaxProjectAmount || forecast > MaxProjectAmount) return TransactionResult.Failure("Budget and forecast amounts must be nonnegative 18-digit currency values.");
        if (request.Notes.Trim().Length > 1000) return TransactionResult.Failure("Budget notes cannot exceed 1,000 characters.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var project = await db.ProjectJobs.SingleOrDefaultAsync(x => x.Id == request.ProjectJobId && x.CompanyId == companyId, cancellationToken);
        if (project is null) return TransactionResult.Failure("Project not found in this company.");
        if (project.Status != "Active") return TransactionResult.Failure("Reopen the project before changing its budget or forecast.");

        if (request.ProjectPhaseId.HasValue && !await db.ProjectPhases.AnyAsync(x => x.Id == request.ProjectPhaseId && x.CompanyId == companyId && x.ProjectJobId == project.Id && x.IsActive, cancellationToken)) return TransactionResult.Failure("Select an active phase belonging to this project.");
        if (request.ProjectCostCodeId.HasValue && !await db.ProjectCostCodes.AnyAsync(x => x.Id == request.ProjectCostCodeId && x.CompanyId == companyId && x.IsActive, cancellationToken)) return TransactionResult.Failure("Select an active cost code belonging to this company.");
        var accountNumber = request.AccountNumber.Trim().ToUpperInvariant();
        Guid? accountId = null;
        if (!string.IsNullOrEmpty(accountNumber))
        {
            accountId = await db.Accounts.Where(x => x.CompanyId == companyId && x.Number == accountNumber && x.Type == AccountType.Expense && x.IsActive && !x.IsControlAccount).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
            if (!accountId.HasValue) return TransactionResult.Failure("Budget accounts must be active, non-control expense accounts in this company.");
        }
        if (await db.ProjectBudgetAllocations.AnyAsync(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.ProjectPhaseId == request.ProjectPhaseId && x.ProjectCostCodeId == request.ProjectCostCodeId && x.AccountId == accountId && x.PeriodStart == request.PeriodStart && x.PeriodEnd == request.PeriodEnd && x.Id != request.Id, cancellationToken)) return TransactionResult.Failure("An allocation already exists for this project, phase, cost code, account, and period.");
        var otherBudget = (await db.ProjectBudgetAllocations.Where(x => x.CompanyId == companyId && x.ProjectJobId == project.Id && x.Id != request.Id).Select(x => x.BudgetAmount).ToListAsync(cancellationToken)).Sum();
        if (otherBudget + budget > project.BudgetAmount) return TransactionResult.Failure($"Allocated budget would exceed the project's authorized budget by {otherBudget + budget - project.BudgetAmount:C}.");

        ProjectBudgetAllocation allocation;
        object? prior = null;
        var now = DateTimeOffset.UtcNow;
        if (request.Id.HasValue)
        {
            allocation = await db.ProjectBudgetAllocations.SingleOrDefaultAsync(x => x.Id == request.Id.Value && x.CompanyId == companyId && x.ProjectJobId == project.Id, cancellationToken) ?? new ProjectBudgetAllocation();
            if (allocation.Id == Guid.Empty) return TransactionResult.Failure("Project budget allocation not found.");
            if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(allocation.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The project budget allocation changed after it was displayed. Refresh before saving it.");
            prior = ProjectBudgetAllocationAuditState(allocation);
            allocation.UpdatedByUserId = ResolveUserId();
            allocation.UpdatedAtUtc = now;
        }
        else
        {
            allocation = new ProjectBudgetAllocation { Id = Guid.NewGuid(), CompanyId = companyId, ProjectJobId = project.Id, CreatedByUserId = ResolveUserId(), CreatedAtUtc = now };
            db.ProjectBudgetAllocations.Add(allocation);
        }

        allocation.ProjectPhaseId = request.ProjectPhaseId;
        allocation.ProjectCostCodeId = request.ProjectCostCodeId;
        allocation.AccountId = accountId;
        allocation.PeriodStart = request.PeriodStart;
        allocation.PeriodEnd = request.PeriodEnd;
        allocation.BudgetAmount = budget;
        allocation.ForecastAmount = forecast;
        allocation.Notes = request.Notes.Trim();
        allocation.ConcurrencyToken = Guid.NewGuid().ToString("N");
        // Every allocation write also advances the parent project token so two
        // independently valid allocations cannot race past the authorized total.
        project.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddProjectDimensionAudit(db, companyId, prior is null ? "project-budget-allocation.created" : "project-budget-allocation.updated", nameof(ProjectBudgetAllocation), allocation.Id, new { prior, current = ProjectBudgetAllocationAuditState(allocation) });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The project budget allocation changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The allocation already exists or related project data changed. Refresh and try again."); }
        return TransactionResult.Success(allocation.Id);
    }

    private static object ProjectPhaseAuditState(ProjectPhase value) => new { value.ProjectJobId, value.ParentProjectPhaseId, value.Code, value.Name, value.Kind, value.Description, value.StartsOn, value.EndsOn, value.IsActive };
    private static object ProjectCostCodeAuditState(ProjectCostCode value) => new { value.Code, value.Name, value.Category, value.Description, value.IsActive };
    private static object ProjectBudgetAllocationAuditState(ProjectBudgetAllocation value) => new { value.ProjectJobId, value.ProjectPhaseId, value.ProjectCostCodeId, value.AccountId, value.PeriodStart, value.PeriodEnd, value.BudgetAmount, value.ForecastAmount, value.Notes };

    private void AddProjectDimensionAudit(BrassLedgerDbContext db, Guid companyId, string action, string entityType, Guid entityId, object detail) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = entityType, EntityId = entityId, DetailJson = JsonSerializer.Serialize(detail), OccurredAtUtc = DateTimeOffset.UtcNow });
}
