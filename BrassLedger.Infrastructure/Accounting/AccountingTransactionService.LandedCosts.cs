using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    private static readonly HashSet<string> SupportedLandedCostChargeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Freight",
        "CustomsDuty",
        "Brokerage",
        "Insurance",
        "Handling",
        "PortFees",
        "Inspection",
        "Storage",
        "Demurrage",
        "Other"
    };

    public async Task<TransactionResult> SaveLandedCostAllocationAsync(SaveLandedCostAllocationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to prepare landed-cost allocations.");

        var charges = request.Charges?.Where(charge => charge.Amount != 0m).ToArray() ?? [];
        if (request.InventoryReceiptId == Guid.Empty
            || request.VendorId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.AllocationNumber)
            || string.IsNullOrWhiteSpace(request.BillNumber)
            || string.IsNullOrWhiteSpace(request.Description)
            || request.DueDate < request.BillDate
            || charges.Length == 0)
            return TransactionResult.Failure("A receipt, vendor, allocation number, bill number, description, valid dates, and at least one charge are required.");

        if (charges.Any(charge => RoundCurrency(charge.Amount) <= 0m
            || !SupportedLandedCostChargeTypes.Contains(charge.ChargeType?.Trim() ?? string.Empty)
            || string.IsNullOrWhiteSpace(charge.Description)))
            return TransactionResult.Failure("Every landed-cost charge requires a supported type, description, and positive amount.");

        var allocationMethod = request.AllocationMethod?.Trim() ?? string.Empty;
        if (allocationMethod is not ("Quantity" or "ReceiptValue" or "Manual"))
            return TransactionResult.Failure("Allocation method must be Quantity, ReceiptValue, or Manual.");

        var total = charges.Sum(charge => RoundCurrency(charge.Amount));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var receipt = await db.InventoryReceipts.SingleOrDefaultAsync(item => item.Id == request.InventoryReceiptId && item.CompanyId == companyId, cancellationToken);
        if (receipt is null || receipt.Status != "Posted")
            return TransactionResult.Failure("Select a posted inventory receipt in this company.");
        if (!string.Equals(receipt.ConcurrencyToken, request.ReceiptConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The receipt changed after the allocation was opened. Refresh and review it again.");
        if (request.BillDate < receipt.ReceivedOn)
            return TransactionResult.Failure("The landed-cost bill date cannot precede the inventory receipt date.");
        if (!await db.Vendors.AnyAsync(item => item.Id == request.VendorId && item.CompanyId == companyId, cancellationToken))
            return TransactionResult.Failure("Landed-cost vendor not found.");

        var allocationNumber = request.AllocationNumber.Trim();
        var billNumber = request.BillNumber.Trim();
        if (await db.LandedCostAllocations.AnyAsync(
                item => item.CompanyId == companyId && item.AllocationNumber == allocationNumber && item.Id != request.Id,
                cancellationToken))
            return TransactionResult.Failure("Landed-cost allocation number already exists.");
        if (await db.VendorBills.AnyAsync(item => item.CompanyId == companyId && item.VendorId == request.VendorId && item.BillNumber == billNumber, cancellationToken)
            || await db.LandedCostAllocations.AnyAsync(
                item => item.CompanyId == companyId && item.VendorId == request.VendorId && item.BillNumber == billNumber && item.Id != request.Id,
                cancellationToken)
            || await db.PurchaseInvoiceMatches.AnyAsync(
                item => item.CompanyId == companyId && item.VendorId == request.VendorId && item.BillNumber == billNumber,
                cancellationToken))
            return TransactionResult.Failure("Vendor bill number already exists for this vendor.");

        var receiptLines = await db.InventoryReceiptLines
            .Where(line => line.InventoryReceiptId == receipt.Id && line.Quantity > line.ReturnedQuantity)
            .OrderBy(line => line.Sequence)
            .ToListAsync(cancellationToken);
        if (receiptLines.Count == 0)
            return TransactionResult.Failure("The receipt has no retained quantity to receive landed cost.");

        var itemIds = receiptLines.Select(line => line.InventoryItemId).ToArray();
        var items = await db.InventoryItems
            .Where(item => item.CompanyId == companyId && itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (items.Count != itemIds.Distinct().Count() || items.Values.Any(item => item.QuantityOnHand <= 0m))
            return TransactionResult.Failure("Every allocated item must have positive on-hand quantity.");
        if (await db.SupplierReturnAuthorizations.AnyAsync(
                item => item.CompanyId == companyId && item.InventoryReceiptId == receipt.Id && item.Status != "Cancelled",
                cancellationToken))
            return TransactionResult.Failure("Complete or cancel supplier-return activity before preparing landed cost for this receipt.");

        var negativeMovement = await db.InventoryTransactions.AnyAsync(
            item => item.CompanyId == companyId
                && itemIds.Contains(item.InventoryItemId)
                && item.OccurredOn >= receipt.ReceivedOn
                && item.QuantityChange < 0m,
            cancellationToken);
        if (negativeMovement)
            return TransactionResult.Failure(
                "Landed cost cannot be capitalized after affected inventory has moved out. "
                + "Use a reviewed current-period inventory/COGS adjustment instead.");

        Dictionary<Guid, decimal> allocated;
        if (allocationMethod == "Manual")
        {
            var manual = request.ManualLines?.ToArray() ?? [];
            if (manual.Length != receiptLines.Count
                || manual.Any(line => line.InventoryReceiptLineId == Guid.Empty || RoundCurrency(line.AllocatedAmount) < 0m)
                || manual.Select(line => line.InventoryReceiptLineId).Distinct().Count() != manual.Length
                || manual.Any(line => receiptLines.All(source => source.Id != line.InventoryReceiptLineId))
                || manual.Sum(line => RoundCurrency(line.AllocatedAmount)) != total)
                return TransactionResult.Failure("Manual landed-cost allocations must identify each line once, be nonnegative, and reconcile exactly to total charges.");

            allocated = receiptLines.ToDictionary(
                line => line.Id,
                line => manual.Where(item => item.InventoryReceiptLineId == line.Id).Sum(item => RoundCurrency(item.AllocatedAmount)));
        }
        else
        {
            var bases = receiptLines
                .Select(line => new
                {
                    line.Id,
                    Basis = allocationMethod == "Quantity"
                        ? line.Quantity - line.ReturnedQuantity
                        : RoundCurrency((line.Quantity - line.ReturnedQuantity) * line.UnitCost)
                })
                .ToArray();
            var basisTotal = bases.Sum(item => item.Basis);
            if (basisTotal <= 0m)
                return TransactionResult.Failure("The selected allocation basis is zero.");

            allocated = [];
            var remaining = total;
            for (var index = 0; index < bases.Length; index++)
            {
                var amount = index == bases.Length - 1
                    ? remaining
                    : RoundCurrency(total * bases[index].Basis / basisTotal);
                allocated[bases[index].Id] = amount;
                remaining -= amount;
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        LandedCostAllocation allocation;
        if (request.Id.HasValue)
        {
            allocation = await db.LandedCostAllocations.SingleOrDefaultAsync(
                    item => item.Id == request.Id && item.CompanyId == companyId,
                    cancellationToken)
                ?? new();
            if (allocation.Id == Guid.Empty)
                return TransactionResult.Failure("Landed-cost allocation not found.");
            if (allocation.Status is not ("Draft" or "Rejected"))
                return TransactionResult.Failure("Only a draft or rejected landed-cost allocation can be edited.");
            if (!string.Equals(allocation.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
                return TransactionResult.Failure("The landed-cost allocation changed after it was opened. Refresh and review it again.");

            db.LandedCostCharges.RemoveRange(await db.LandedCostCharges
                .Where(item => item.LandedCostAllocationId == allocation.Id)
                .ToListAsync(cancellationToken));
            db.LandedCostAllocationLines.RemoveRange(await db.LandedCostAllocationLines
                .Where(item => item.LandedCostAllocationId == allocation.Id)
                .ToListAsync(cancellationToken));
        }
        else
        {
            allocation = new LandedCostAllocation { Id = Guid.NewGuid(), CompanyId = companyId };
            db.LandedCostAllocations.Add(allocation);
        }

        allocation.InventoryReceiptId = receipt.Id;
        allocation.VendorId = request.VendorId;
        allocation.AllocationNumber = allocationNumber;
        allocation.BillNumber = billNumber;
        allocation.BillDate = request.BillDate;
        allocation.DueDate = request.DueDate;
        allocation.AllocationMethod = allocationMethod;
        allocation.Description = request.Description.Trim();
        allocation.Status = "Draft";
        allocation.TotalAmount = total;
        allocation.SourceReceiptConcurrencyToken = receipt.ConcurrencyToken;
        allocation.PreparedByUserId = ResolveUserId();
        allocation.PreparedAtUtc = DateTimeOffset.UtcNow;
        allocation.SubmittedByUserId = null;
        allocation.SubmittedAtUtc = null;
        allocation.DecidedByUserId = null;
        allocation.DecidedAtUtc = null;
        allocation.DecisionReason = string.Empty;
        allocation.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.LandedCostCharges.AddRange(charges.Select((charge, index) => new LandedCostCharge
        {
            Id = Guid.NewGuid(),
            LandedCostAllocationId = allocation.Id,
            Sequence = index + 1,
            ChargeType = charge.ChargeType.Trim(),
            Description = charge.Description.Trim(),
            Amount = RoundCurrency(charge.Amount)
        }));
        db.LandedCostAllocationLines.AddRange(receiptLines.Select((line, index) => new LandedCostAllocationLine
        {
            Id = Guid.NewGuid(),
            LandedCostAllocationId = allocation.Id,
            InventoryReceiptLineId = line.Id,
            PurchaseOrderLineId = line.PurchaseOrderLineId,
            InventoryItemId = line.InventoryItemId,
            Sequence = index + 1,
            BasisQuantity = line.Quantity - line.ReturnedQuantity,
            BasisAmount = RoundCurrency((line.Quantity - line.ReturnedQuantity) * line.UnitCost),
            AllocatedAmount = allocated[line.Id],
            PreparedItemConcurrencyToken = items[line.InventoryItemId].ConcurrencyToken
        }));
        AddPurchasingAudit(
            db,
            companyId,
            request.Id.HasValue ? "landed-cost.draft.updated" : "landed-cost.draft.created",
            nameof(LandedCostAllocation),
            allocation.Id,
            new
            {
                allocation.AllocationNumber,
                receipt.ReceiptNumber,
                allocation.AllocationMethod,
                allocation.TotalAmount,
                chargeCount = charges.Length
            });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The landed-cost draft changed while saving. Refresh and try again.");
        }
        catch (DbUpdateException)
        {
            return TransactionResult.Failure("The landed-cost or bill number changed concurrently. Refresh and try again.");
        }

        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(allocation.Id);
    }

    public async Task<TransactionResult> SubmitLandedCostAllocationAsync(SubmitLandedCostAllocationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to submit landed-cost allocations.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var allocation = await db.LandedCostAllocations.SingleOrDefaultAsync(
            item => item.Id == request.LandedCostAllocationId && item.CompanyId == companyId,
            cancellationToken);
        if (allocation is null)
            return TransactionResult.Failure("Landed-cost allocation not found.");
        if (allocation.Status != "Draft")
            return TransactionResult.Failure("Only a draft landed-cost allocation can be submitted.");
        if (!string.Equals(allocation.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The landed-cost allocation changed after it was opened. Refresh and review it again.");

        allocation.Status = "Submitted";
        allocation.SubmittedByUserId = ResolveUserId();
        allocation.SubmittedAtUtc = DateTimeOffset.UtcNow;
        allocation.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(
            db,
            companyId,
            "landed-cost.submitted",
            nameof(LandedCostAllocation),
            allocation.Id,
            new { allocation.AllocationNumber, allocation.TotalAmount });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The landed-cost allocation changed while submitting. Refresh and try again.");
        }

        return TransactionResult.Success(allocation.Id);
    }

    public async Task<TransactionResult> DecideLandedCostAllocationAsync(DecideLandedCostAllocationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage))
            return TransactionResult.Failure("You are not authorized to approve landed-cost allocations.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return TransactionResult.Failure("An approval or rejection reason is required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var allocation = await db.LandedCostAllocations.SingleOrDefaultAsync(
            item => item.Id == request.LandedCostAllocationId && item.CompanyId == companyId,
            cancellationToken);
        if (allocation is null)
            return TransactionResult.Failure("Landed-cost allocation not found.");
        if (allocation.Status != "Submitted")
            return TransactionResult.Failure("Only a submitted landed-cost allocation can be reviewed.");
        if (!string.Equals(allocation.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The landed-cost allocation changed after it was opened. Refresh and review it again.");

        var decidingUserId = ResolveUserId();
        if (decidingUserId.HasValue && allocation.PreparedByUserId == decidingUserId)
            return TransactionResult.Failure("The person who prepared a landed-cost allocation cannot approve or reject it.");

        allocation.Status = request.Approve ? "Approved" : "Rejected";
        allocation.DecidedByUserId = decidingUserId;
        allocation.DecidedAtUtc = DateTimeOffset.UtcNow;
        allocation.DecisionReason = request.Reason.Trim();
        allocation.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(
            db,
            companyId,
            request.Approve ? "landed-cost.approved" : "landed-cost.rejected",
            nameof(LandedCostAllocation),
            allocation.Id,
            new { allocation.AllocationNumber, allocation.TotalAmount, allocation.DecisionReason });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The landed-cost allocation changed while reviewing. Refresh and try again.");
        }

        return TransactionResult.Success(allocation.Id);
    }

    public async Task<TransactionResult> CancelLandedCostAllocationAsync(CancelLandedCostAllocationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to cancel landed-cost allocations.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return TransactionResult.Failure("A cancellation reason is required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var allocation = await db.LandedCostAllocations.SingleOrDefaultAsync(
            item => item.Id == request.LandedCostAllocationId && item.CompanyId == companyId,
            cancellationToken);
        if (allocation is null)
            return TransactionResult.Failure("Landed-cost allocation not found.");
        if (allocation.Status is not ("Draft" or "Submitted" or "Approved" or "Rejected"))
            return TransactionResult.Failure("This landed-cost allocation can no longer be cancelled.");
        if (!string.Equals(allocation.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The landed-cost allocation changed after it was opened. Refresh and review it again.");

        allocation.Status = "Cancelled";
        allocation.CancelledByUserId = ResolveUserId();
        allocation.CancelledAtUtc = DateTimeOffset.UtcNow;
        allocation.CancellationReason = request.Reason.Trim();
        allocation.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(
            db,
            companyId,
            "landed-cost.cancelled",
            nameof(LandedCostAllocation),
            allocation.Id,
            new { allocation.AllocationNumber, allocation.CancellationReason });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The landed-cost allocation changed while cancelling. Refresh and try again.");
        }

        return TransactionResult.Success(allocation.Id);
    }

    public async Task<TransactionResult> PostLandedCostAllocationAsync(PostLandedCostAllocationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to post landed-cost allocations.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var allocation = await db.LandedCostAllocations.SingleOrDefaultAsync(
            item => item.Id == request.LandedCostAllocationId && item.CompanyId == companyId,
            cancellationToken);
        if (allocation is null)
            return TransactionResult.Failure("Landed-cost allocation not found.");
        if (allocation.Status != "Approved")
            return TransactionResult.Failure("Only an approved landed-cost allocation can be posted.");
        if (!string.Equals(allocation.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The landed-cost allocation changed after it was opened. Refresh and review it again.");

        var postingUserId = ResolveUserId();
        if (postingUserId.HasValue && allocation.DecidedByUserId == postingUserId)
            return TransactionResult.Failure("The person who reviewed a landed-cost allocation cannot post it.");

        var receipt = await db.InventoryReceipts.SingleAsync(
            item => item.Id == allocation.InventoryReceiptId && item.CompanyId == companyId,
            cancellationToken);
        if (receipt.Status != "Posted")
            return TransactionResult.Failure("The source receipt is no longer posted. Return the allocation to draft and review it again.");
        if (await db.VendorBills.AnyAsync(
                item => item.CompanyId == companyId && item.VendorId == allocation.VendorId && item.BillNumber == allocation.BillNumber,
                cancellationToken))
            return TransactionResult.Failure("Vendor bill number already exists for this vendor.");

        var charges = await db.LandedCostCharges
            .Where(charge => charge.LandedCostAllocationId == allocation.Id)
            .ToListAsync(cancellationToken);
        var lines = await db.LandedCostAllocationLines
            .Where(line => line.LandedCostAllocationId == allocation.Id)
            .OrderBy(line => line.Sequence)
            .ToListAsync(cancellationToken);
        var itemIds = lines
            .Where(line => line.AllocatedAmount > 0m)
            .Select(line => line.InventoryItemId)
            .ToArray();
        var items = await db.InventoryItems
            .Where(item => item.CompanyId == companyId && itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (charges.Count == 0
            || lines.Count == 0
            || charges.Sum(charge => charge.Amount) != allocation.TotalAmount
            || lines.Sum(line => line.AllocatedAmount) != allocation.TotalAmount
            || allocation.TotalAmount <= 0m)
            return TransactionResult.Failure("The landed-cost detail no longer reconciles to its approved total. Return it to draft and review it again.");
        if (items.Count != itemIds.Distinct().Count()
            || lines.Where(line => line.AllocatedAmount > 0m).Any(line =>
                !string.Equals(items[line.InventoryItemId].ConcurrencyToken, line.PreparedItemConcurrencyToken, StringComparison.Ordinal)
                || items[line.InventoryItemId].QuantityOnHand <= 0m))
            return TransactionResult.Failure("Affected inventory changed after this allocation was prepared. Return it to draft and review current quantities and valuation.");

        var inventory = await db.Accounts.SingleOrDefaultAsync(
            account => account.CompanyId == companyId
                && account.IsActive
                && account.OperationalRole == AccountingAccountRoles.InventoryAsset,
            cancellationToken);
        if (inventory is null)
            return TransactionResult.Failure("Configure an active inventory asset account before posting landed cost.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var billId = Guid.NewGuid();
        var posting = await PostAsync(
            db,
            companyId,
            allocation.BillDate,
            "Accounts Payable",
            allocation.BillNumber,
            allocation.Description,
            [
                new JournalLineRequest(
                    OperationalRoleReference(AccountingAccountRoles.InventoryAsset),
                    allocation.TotalAmount,
                    0m,
                    "Capitalized landed cost"),
                new JournalLineRequest(
                    OperationalRoleReference(AccountingAccountRoles.AccountsPayable),
                    0m,
                    allocation.TotalAmount,
                    "Landed-cost vendor bill")
            ],
            cancellationToken,
            allowControlAccounts: true,
            sourceDocumentId: billId,
            sourceDocumentType: "VendorBill",
            resolveOperationalRoles: true);
        if (!posting.Succeeded)
            return posting;

        var bill = new VendorBill
        {
            Id = billId,
            CompanyId = companyId,
            VendorId = allocation.VendorId,
            BillNumber = allocation.BillNumber,
            BillDate = allocation.BillDate,
            DueDate = allocation.DueDate,
            Status = "Open",
            TotalAmount = allocation.TotalAmount,
            BalanceDue = allocation.TotalAmount,
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        db.VendorBills.Add(bill);

        var postedLines = lines.Where(line => line.AllocatedAmount > 0m).ToArray();
        db.VendorBillLines.AddRange(postedLines.Select((line, index) => new VendorBillLine
        {
            Id = Guid.NewGuid(),
            VendorBillId = bill.Id,
            InventoryReceiptLineId = line.InventoryReceiptLineId,
            Sequence = index + 1,
            ExpenseAccountId = inventory.Id,
            Description = $"{allocation.Description} — allocation line {line.Sequence}",
            Quantity = 1m,
            UnitCost = line.AllocatedAmount,
            LineTotal = line.AllocatedAmount
        }));
        foreach (var line in postedLines)
        {
            var item = items[line.InventoryItemId];
            line.PriorQuantityOnHand = item.QuantityOnHand;
            line.PriorUnitCost = item.UnitCost;
            line.ResultingUnitCost = RoundCurrency(
                ((item.QuantityOnHand * item.UnitCost) + line.AllocatedAmount) / item.QuantityOnHand);
            item.UnitCost = line.ResultingUnitCost;
            item.ConcurrencyToken = Guid.NewGuid().ToString("N");
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                InventoryItemId = item.Id,
                WarehouseId = receipt.WarehouseId,
                BinId = receipt.BinId,
                OccurredOn = allocation.BillDate,
                TransactionType = "Landed cost",
                QuantityChange = 0m,
                UnitCost = item.UnitCost,
                TotalCost = line.AllocatedAmount,
                Reference = allocation.AllocationNumber,
                JournalEntryId = posting.Id
            });
        }

        var vendor = await db.Vendors.SingleAsync(
            item => item.Id == allocation.VendorId && item.CompanyId == companyId,
            cancellationToken);
        vendor.OpenBalance += allocation.TotalAmount;
        allocation.VendorBillId = bill.Id;
        allocation.JournalEntryId = posting.Id;
        allocation.Status = "Posted";
        allocation.PostedByUserId = postingUserId;
        allocation.PostedAtUtc = DateTimeOffset.UtcNow;
        allocation.ConcurrencyToken = Guid.NewGuid().ToString("N");
        receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(
            db,
            companyId,
            "landed-cost.posted",
            nameof(LandedCostAllocation),
            allocation.Id,
            new
            {
                allocation.AllocationNumber,
                allocation.BillNumber,
                allocation.TotalAmount,
                allocation.AllocationMethod,
                vendorBillId = bill.Id,
                journalEntryId = posting.Id
            });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The landed-cost allocation, receipt, or inventory changed while posting. Refresh and try again.");
        }
        catch (DbUpdateException)
        {
            return TransactionResult.Failure("The landed-cost bill changed concurrently. Refresh and try again.");
        }

        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(allocation.Id);
    }

    public async Task<TransactionResult> ReverseLandedCostAllocationAsync(ReverseLandedCostAllocationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)
            || !HasPermission(BrassLedgerPermissions.PaymentReverse))
            return TransactionResult.Failure("You are not authorized to reverse landed-cost allocations.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return TransactionResult.Failure("A reversal reason is required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var allocation = await db.LandedCostAllocations.SingleOrDefaultAsync(
            item => item.Id == request.LandedCostAllocationId && item.CompanyId == companyId,
            cancellationToken);
        if (allocation is null
            || allocation.Status != "Posted"
            || !allocation.JournalEntryId.HasValue
            || !allocation.VendorBillId.HasValue)
            return TransactionResult.Failure("Only a posted landed-cost allocation can be reversed.");
        if (!string.Equals(allocation.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The landed-cost allocation changed after it was opened. Refresh and review it again.");
        if (request.ReversalDate < allocation.BillDate)
            return TransactionResult.Failure("The reversal date cannot precede the landed-cost bill date.");

        var bill = await db.VendorBills.SingleAsync(
            item => item.Id == allocation.VendorBillId && item.CompanyId == companyId,
            cancellationToken);
        if (bill.Status != "Open"
            || bill.BalanceDue != bill.TotalAmount
            || await db.SubledgerPaymentApplications.AnyAsync(item => item.DocumentId == bill.Id, cancellationToken)
            || await db.SubledgerAdjustments.AnyAsync(
                item => item.CompanyId == companyId && item.DocumentId == bill.Id,
                cancellationToken))
            return TransactionResult.Failure("Reverse all payment or credit activity before reversing this landed-cost allocation.");

        var lines = await db.LandedCostAllocationLines
            .Where(line => line.LandedCostAllocationId == allocation.Id && line.AllocatedAmount > 0m)
            .OrderBy(line => line.Sequence)
            .ToListAsync(cancellationToken);
        var itemIds = lines.Select(line => line.InventoryItemId).ToArray();
        var items = await db.InventoryItems
            .Where(item => item.CompanyId == companyId && itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (items.Count != itemIds.Distinct().Count())
            return TransactionResult.Failure("An affected inventory item is no longer available in this company.");

        var finalLineByItem = lines
            .GroupBy(line => line.InventoryItemId)
            .Select(group => group.OrderByDescending(line => line.Sequence).First());
        if (finalLineByItem.Any(line =>
                items[line.InventoryItemId].QuantityOnHand != line.PriorQuantityOnHand
                || items[line.InventoryItemId].UnitCost != line.ResultingUnitCost))
            return TransactionResult.Failure(
                "This landed-cost allocation is no longer the latest valuation event. "
                + "Post a reviewed current-period inventory/COGS adjustment instead.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reversal = await PostInverseAsync(
            db,
            companyId,
            allocation.JournalEntryId.Value,
            request.ReversalDate,
            $"REV-{allocation.AllocationNumber}",
            request.Reason.Trim(),
            allocation.Id,
            "LandedCostAllocationReversal",
            null,
            cancellationToken,
            "Purchasing");
        if (!reversal.Succeeded)
            return reversal;

        var receipt = await db.InventoryReceipts.SingleAsync(
            item => item.Id == allocation.InventoryReceiptId && item.CompanyId == companyId,
            cancellationToken);
        foreach (var line in lines.OrderByDescending(line => line.Sequence))
        {
            var item = items[line.InventoryItemId];
            item.UnitCost = line.PriorUnitCost;
            item.ConcurrencyToken = Guid.NewGuid().ToString("N");
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                InventoryItemId = item.Id,
                WarehouseId = receipt.WarehouseId,
                BinId = receipt.BinId,
                OccurredOn = request.ReversalDate,
                TransactionType = "Landed cost reversal",
                QuantityChange = 0m,
                UnitCost = line.PriorUnitCost,
                TotalCost = -line.AllocatedAmount,
                Reference = $"REV-{allocation.AllocationNumber}",
                JournalEntryId = reversal.Id
            });
        }

        var vendor = await db.Vendors.SingleAsync(
            item => item.Id == allocation.VendorId && item.CompanyId == companyId,
            cancellationToken);
        vendor.OpenBalance -= allocation.TotalAmount;
        bill.Status = "Voided";
        bill.BalanceDue = 0m;
        bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
        allocation.Status = "Reversed";
        allocation.ReversalJournalEntryId = reversal.Id;
        allocation.ReversedByUserId = ResolveUserId();
        allocation.ReversedAtUtc = DateTimeOffset.UtcNow;
        allocation.ReversalDate = request.ReversalDate;
        allocation.ReversalReason = request.Reason.Trim();
        allocation.ConcurrencyToken = Guid.NewGuid().ToString("N");
        receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(
            db,
            companyId,
            "landed-cost.reversed",
            nameof(LandedCostAllocation),
            allocation.Id,
            new
            {
                allocation.AllocationNumber,
                allocation.TotalAmount,
                allocation.ReversalDate,
                allocation.ReversalReason,
                reversalJournalEntryId = reversal.Id
            });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The landed-cost allocation, bill, or inventory changed while reversing. Refresh and try again.");
        }

        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(allocation.Id);
    }
}
