using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<TransactionResult> SavePurchaseRequisitionAsync(SavePurchaseRequisitionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.RequisitionManage)) return TransactionResult.Failure("You are not authorized to prepare purchase requisitions.");
        var requestedLines = request.Lines?.ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(request.RequisitionNumber) || string.IsNullOrWhiteSpace(request.Purpose) || requestedLines.Length == 0)
            return TransactionResult.Failure("A requisition number, business purpose, and at least one line are required.");
        if (request.NeededBy.HasValue && request.NeededBy.Value < request.RequestedOn)
            return TransactionResult.Failure("The needed-by date cannot precede the request date.");
        if (requestedLines.Any(line => line.InventoryItemId == Guid.Empty || string.IsNullOrWhiteSpace(line.Description) || RoundQuantity(line.Quantity) <= 0m || RoundCurrency(line.EstimatedUnitCost) < 0m))
            return TransactionResult.Failure("Every requisition line requires an inventory item, description, positive quantity, and non-negative estimated unit cost.");
        if (requestedLines.Select(line => line.InventoryItemId).Distinct().Count() != requestedLines.Length)
            return TransactionResult.Failure("Combine duplicate inventory items into one requisition line.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (request.RequestedVendorId.HasValue && !await db.Vendors.AnyAsync(vendor => vendor.Id == request.RequestedVendorId.Value && vendor.CompanyId == companyId, cancellationToken))
            return TransactionResult.Failure("Requested vendor not found in the active company.");
        var itemIds = requestedLines.Select(line => line.InventoryItemId).ToArray();
        if (await db.InventoryItems.CountAsync(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id), cancellationToken) != itemIds.Length)
            return TransactionResult.Failure("Every requisition item must be active in the current company.");
        if (!await AreActiveProjectDimensionsAsync(db, companyId, requestedLines.Select(line => (line.ProjectJobId, line.ProjectPhaseId, line.ProjectCostCodeId)), cancellationToken))
            return TransactionResult.Failure("Every requisition project must be active and belong to this company.");
        if (!await AreActiveTrackingDimensionsAsync(db, companyId, request.RequestedOn, requestedLines.Select(line => (line.DepartmentId, line.ClassId)), cancellationToken))
            return TransactionResult.Failure("Every requisition department and class must be active, effective on the request date, correctly typed, and belong to this company.");
        var number = request.RequisitionNumber.Trim();
        if (await db.PurchaseRequisitions.AnyAsync(requisition => requisition.CompanyId == companyId && requisition.RequisitionNumber == number && requisition.Id != request.Id, cancellationToken))
            return TransactionResult.Failure("Purchase-requisition number already exists.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        PurchaseRequisition requisition;
        if (request.Id.HasValue)
        {
            requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(candidate => candidate.Id == request.Id.Value && candidate.CompanyId == companyId, cancellationToken) ?? new PurchaseRequisition();
            if (requisition.Id == Guid.Empty) return TransactionResult.Failure("Purchase requisition not found.");
            if (requisition.Status != "Draft") return TransactionResult.Failure("Only a draft purchase requisition can be edited.");
            if (!string.Equals(requisition.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The purchase requisition changed after it was opened. Refresh and review it again.");
            db.PurchaseRequisitionLines.RemoveRange(await db.PurchaseRequisitionLines.Where(line => line.PurchaseRequisitionId == requisition.Id).ToListAsync(cancellationToken));
        }
        else
        {
            requisition = new PurchaseRequisition { Id = Guid.NewGuid(), CompanyId = companyId, PreparedByUserId = ResolveUserId(), PreparedAtUtc = DateTimeOffset.UtcNow };
            db.PurchaseRequisitions.Add(requisition);
        }

        requisition.RequestedVendorId = request.RequestedVendorId;
        requisition.RequisitionNumber = number;
        requisition.RequestedOn = request.RequestedOn;
        requisition.NeededBy = request.NeededBy;
        requisition.Purpose = request.Purpose.Trim();
        requisition.TotalEstimatedAmount = requestedLines.Sum(line => RoundCurrency(RoundQuantity(line.Quantity) * RoundCurrency(line.EstimatedUnitCost)));
        requisition.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.PurchaseRequisitionLines.AddRange(requestedLines.Select((line, index) => new PurchaseRequisitionLine
        {
            Id = Guid.NewGuid(),
            PurchaseRequisitionId = requisition.Id,
            Sequence = index + 1,
            InventoryItemId = line.InventoryItemId,
            ProjectJobId = line.ProjectJobId,
            ProjectPhaseId = line.ProjectPhaseId,
            ProjectCostCodeId = line.ProjectCostCodeId,
            DepartmentId = line.DepartmentId,
            ClassId = line.ClassId,
            Description = line.Description.Trim(),
            RequestedQuantity = RoundQuantity(line.Quantity),
            EstimatedUnitCost = RoundCurrency(line.EstimatedUnitCost),
            EstimatedLineTotal = RoundCurrency(RoundQuantity(line.Quantity) * RoundCurrency(line.EstimatedUnitCost))
        }));
        AddPurchasingAudit(db, companyId, "purchase-requisition.draft.saved", nameof(PurchaseRequisition), requisition.Id, new { requisition.RequisitionNumber, requisition.RequestedVendorId, lineCount = requestedLines.Length, requisition.TotalEstimatedAmount });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The purchase requisition changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The requisition number or lines changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(requisition.Id);
    }

    public async Task<TransactionResult> SubmitPurchaseRequisitionAsync(SubmitPurchaseRequisitionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.RequisitionManage)) return TransactionResult.Failure("You are not authorized to submit purchase requisitions.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(candidate => candidate.Id == request.PurchaseRequisitionId && candidate.CompanyId == companyId, cancellationToken);
        if (requisition is null) return TransactionResult.Failure("Purchase requisition not found.");
        if (requisition.Status != "Draft") return TransactionResult.Failure("Only a draft purchase requisition can be submitted.");
        if (!string.Equals(requisition.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The purchase requisition changed after it was opened. Refresh and review it again.");
        if (!await db.PurchaseRequisitionLines.AnyAsync(line => line.PurchaseRequisitionId == requisition.Id, cancellationToken)) return TransactionResult.Failure("A purchase requisition must contain at least one line before submission.");
        requisition.Status = "Submitted"; requisition.SubmittedByUserId = ResolveUserId(); requisition.SubmittedAtUtc = DateTimeOffset.UtcNow; requisition.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(db, companyId, "purchase-requisition.submitted", nameof(PurchaseRequisition), requisition.Id, new { requisition.RequisitionNumber, requisition.TotalEstimatedAmount });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The purchase requisition changed while it was being submitted. Refresh and try again."); }
        return TransactionResult.Success(requisition.Id);
    }

    public async Task<TransactionResult> DecidePurchaseRequisitionAsync(DecidePurchaseRequisitionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to decide purchase requisitions.");
        if (!request.Approve && string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A rejection reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(candidate => candidate.Id == request.PurchaseRequisitionId && candidate.CompanyId == companyId, cancellationToken);
        if (requisition is null) return TransactionResult.Failure("Purchase requisition not found.");
        if (requisition.Status != "Submitted") return TransactionResult.Failure("Only a submitted purchase requisition can be approved or rejected.");
        if (!string.Equals(requisition.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The purchase requisition changed after it was opened. Refresh and review it again.");
        requisition.DecisionReason = request.Reason?.Trim() ?? string.Empty;
        var decisionLines = request.Approve ? await db.PurchaseRequisitionLines.Where(line => line.PurchaseRequisitionId == requisition.Id).ToListAsync(cancellationToken) : [];
        if (request.Approve && !await AreActiveProjectDimensionsAsync(db, companyId, decisionLines.Select(line => (line.ProjectJobId, line.ProjectPhaseId, line.ProjectCostCodeId)), cancellationToken)) return TransactionResult.Failure("One or more requisition project dimensions are closed or unavailable.");
        if (request.Approve && !await AreActiveTrackingDimensionsAsync(db, companyId, requisition.RequestedOn, decisionLines.Select(line => (line.DepartmentId, line.ClassId)), cancellationToken)) return TransactionResult.Failure("One or more requisition departments or classes are inactive, out of period, unavailable, or incorrectly typed.");
        if (request.Approve) { requisition.Status = "Approved"; requisition.ApprovedByUserId = ResolveUserId(); requisition.ApprovedAtUtc = DateTimeOffset.UtcNow; }
        else { requisition.Status = "Rejected"; requisition.RejectedByUserId = ResolveUserId(); requisition.RejectedAtUtc = DateTimeOffset.UtcNow; }
        requisition.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(db, companyId, request.Approve ? "purchase-requisition.approved" : "purchase-requisition.rejected", nameof(PurchaseRequisition), requisition.Id, new { requisition.RequisitionNumber, requisition.TotalEstimatedAmount, requisition.DecisionReason });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The purchase requisition changed while it was being decided. Refresh and try again."); }
        return TransactionResult.Success(requisition.Id);
    }

    public async Task<TransactionResult> CancelPurchaseRequisitionAsync(CancelPurchaseRequisitionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.RequisitionManage) && !HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to cancel purchase requisitions.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A cancellation reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(candidate => candidate.Id == request.PurchaseRequisitionId && candidate.CompanyId == companyId, cancellationToken);
        if (requisition is null) return TransactionResult.Failure("Purchase requisition not found.");
        if (requisition.Status is not ("Draft" or "Submitted" or "Approved")) return TransactionResult.Failure("Only an unconverted purchase requisition can be cancelled.");
        if (requisition.Status == "Approved" && !HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("Purchasing authority is required to cancel an approved requisition.");
        if (!string.Equals(requisition.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The purchase requisition changed after it was opened. Refresh and review it again.");
        requisition.Status = "Cancelled"; requisition.CancelledByUserId = ResolveUserId(); requisition.CancelledAtUtc = DateTimeOffset.UtcNow; requisition.CancellationReason = request.Reason.Trim(); requisition.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(db, companyId, "purchase-requisition.cancelled", nameof(PurchaseRequisition), requisition.Id, new { requisition.RequisitionNumber, requisition.CancellationReason });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The purchase requisition changed while it was being cancelled. Refresh and try again."); }
        return TransactionResult.Success(requisition.Id);
    }

    public async Task<TransactionResult> ConvertPurchaseRequisitionAsync(ConvertPurchaseRequisitionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to convert purchase requisitions.");
        if (request.VendorId == Guid.Empty || string.IsNullOrWhiteSpace(request.OrderNumber)) return TransactionResult.Failure("A vendor and purchase-order number are required.");
        if (request.ExpectedOn.HasValue && request.ExpectedOn.Value < request.OrderedOn) return TransactionResult.Failure("The expected date cannot precede the order date.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(candidate => candidate.Id == request.PurchaseRequisitionId && candidate.CompanyId == companyId, cancellationToken);
        if (requisition is null) return TransactionResult.Failure("Purchase requisition not found.");
        if (requisition.Status != "Approved") return TransactionResult.Failure("Only an approved purchase requisition can be converted.");
        if (!string.Equals(requisition.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The purchase requisition changed after it was opened. Refresh and review it again.");
        if (request.OrderedOn < requisition.RequestedOn) return TransactionResult.Failure("The purchase-order date cannot precede the requisition date.");
        if (!await db.Vendors.AnyAsync(vendor => vendor.Id == request.VendorId && vendor.CompanyId == companyId, cancellationToken)) return TransactionResult.Failure("Vendor not found in the active company.");
        var orderNumber = request.OrderNumber.Trim();
        if (await db.PurchaseOrders.AnyAsync(order => order.CompanyId == companyId && (order.OrderNumber == orderNumber || order.PurchaseRequisitionId == requisition.Id), cancellationToken)) return TransactionResult.Failure("The purchase-order number already exists or this requisition was already converted.");
        var lines = await db.PurchaseRequisitionLines.Where(line => line.PurchaseRequisitionId == requisition.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        if (lines.Count == 0 || lines.Sum(line => line.EstimatedLineTotal) != requisition.TotalEstimatedAmount) return TransactionResult.Failure("The approved requisition lines do not reconcile to its reviewed total.");
        var itemIds = lines.Select(line => line.InventoryItemId).ToArray();
        if (await db.InventoryItems.CountAsync(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id), cancellationToken) != itemIds.Distinct().Count()) return TransactionResult.Failure("One or more approved requisition items are no longer active in this company.");
        if (!await AreActiveProjectDimensionsAsync(db, companyId, lines.Select(line => (line.ProjectJobId, line.ProjectPhaseId, line.ProjectCostCodeId)), cancellationToken)) return TransactionResult.Failure("One or more approved requisition project dimensions are closed or unavailable.");
        if (!await AreActiveTrackingDimensionsAsync(db, companyId, request.OrderedOn, lines.Select(line => (line.DepartmentId, line.ClassId)), cancellationToken)) return TransactionResult.Failure("One or more approved requisition departments or classes are inactive, out of period, unavailable, or incorrectly typed for the purchase-order date.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = new PurchaseOrder { Id = Guid.NewGuid(), CompanyId = companyId, PurchaseRequisitionId = requisition.Id, VendorId = request.VendorId, OrderNumber = orderNumber, OrderedOn = request.OrderedOn, ExpectedOn = request.ExpectedOn, Status = "Draft", TotalAmount = requisition.TotalEstimatedAmount, Notes = string.IsNullOrWhiteSpace(request.Notes) ? requisition.Purpose : request.Notes.Trim(), PreparedByUserId = ResolveUserId(), PreparedAtUtc = DateTimeOffset.UtcNow };
        db.PurchaseOrders.Add(order);
        db.PurchaseOrderLines.AddRange(lines.Select(line => new PurchaseOrderLine { Id = Guid.NewGuid(), PurchaseOrderId = order.Id, Sequence = line.Sequence, InventoryItemId = line.InventoryItemId, ProjectJobId = line.ProjectJobId, ProjectPhaseId = line.ProjectPhaseId, ProjectCostCodeId = line.ProjectCostCodeId, DepartmentId = line.DepartmentId, ClassId = line.ClassId, Description = line.Description, OrderedQuantity = line.RequestedQuantity, UnitCost = line.EstimatedUnitCost, LineTotal = line.EstimatedLineTotal }));
        requisition.Status = "Converted"; requisition.ConvertedByUserId = ResolveUserId(); requisition.ConvertedAtUtc = DateTimeOffset.UtcNow; requisition.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(db, companyId, "purchase-requisition.converted", nameof(PurchaseRequisition), requisition.Id, new { requisition.RequisitionNumber, order.Id, order.OrderNumber, order.VendorId, order.TotalAmount });
        AddPurchasingAudit(db, companyId, "purchase-order.created-from-requisition", nameof(PurchaseOrder), order.Id, new { order.OrderNumber, requisition.Id, requisition.RequisitionNumber, order.TotalAmount });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The purchase requisition changed while it was being converted. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The purchase-order number or requisition conversion changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(order.Id);
    }
}
