using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<TransactionResult> SavePurchaseOrderAsync(SavePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to prepare purchase orders.");
        var requestedLines = request.Lines?.ToArray() ?? [];
        if (request.VendorId == Guid.Empty || string.IsNullOrWhiteSpace(request.OrderNumber) || requestedLines.Length == 0)
            return TransactionResult.Failure("A vendor, order number, and at least one line are required.");
        if (request.ExpectedOn.HasValue && request.ExpectedOn.Value < request.OrderedOn)
            return TransactionResult.Failure("The expected date cannot precede the order date.");
        if (requestedLines.Any(line => line.InventoryItemId == Guid.Empty || string.IsNullOrWhiteSpace(line.Description) || line.Quantity <= 0m || line.UnitCost < 0m))
            return TransactionResult.Failure("Every purchase-order line requires an inventory item, description, positive quantity, and non-negative unit cost.");
        if (requestedLines.Select(line => line.InventoryItemId).Distinct().Count() != requestedLines.Length)
            return TransactionResult.Failure("Combine duplicate inventory items into one purchase-order line.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.Vendors.AnyAsync(vendor => vendor.Id == request.VendorId && vendor.CompanyId == companyId, cancellationToken))
            return TransactionResult.Failure("Vendor not found in the active company.");
        var itemIds = requestedLines.Select(line => line.InventoryItemId).ToArray();
        if (await db.InventoryItems.CountAsync(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id), cancellationToken) != itemIds.Length)
            return TransactionResult.Failure("Every purchase-order item must be active in the current company.");
        var number = request.OrderNumber.Trim();
        if (await db.PurchaseOrders.AnyAsync(order => order.CompanyId == companyId && order.OrderNumber == number && order.Id != request.Id, cancellationToken))
            return TransactionResult.Failure("Purchase-order number already exists.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        PurchaseOrder order;
        if (request.Id.HasValue)
        {
            order = await db.PurchaseOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.Id.Value && candidate.CompanyId == companyId, cancellationToken)
                ?? new PurchaseOrder();
            if (order.Id == Guid.Empty) return TransactionResult.Failure("Purchase order not found.");
            if (order.Status != "Draft") return TransactionResult.Failure("Only a draft purchase order can be edited.");
            if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
                return TransactionResult.Failure("The purchase order changed after it was opened. Refresh and review it again.");
            db.PurchaseOrderLines.RemoveRange(await db.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).ToListAsync(cancellationToken));
        }
        else
        {
            order = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Status = "Draft",
                PreparedByUserId = ResolveUserId(),
                PreparedAtUtc = DateTimeOffset.UtcNow
            };
            db.PurchaseOrders.Add(order);
        }

        order.VendorId = request.VendorId;
        order.OrderNumber = number;
        order.OrderedOn = request.OrderedOn;
        order.ExpectedOn = request.ExpectedOn;
        order.Notes = request.Notes?.Trim() ?? string.Empty;
        order.TotalAmount = requestedLines.Sum(line => RoundCurrency(RoundQuantity(line.Quantity) * RoundCurrency(line.UnitCost)));
        order.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.PurchaseOrderLines.AddRange(requestedLines.Select((line, index) => new PurchaseOrderLine
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = order.Id,
            Sequence = index + 1,
            InventoryItemId = line.InventoryItemId,
            Description = line.Description.Trim(),
            OrderedQuantity = RoundQuantity(line.Quantity),
            UnitCost = RoundCurrency(line.UnitCost),
            LineTotal = RoundCurrency(RoundQuantity(line.Quantity) * RoundCurrency(line.UnitCost))
        }));
        AddPurchasingAudit(db, companyId, "purchase-order.draft.saved", nameof(PurchaseOrder), order.Id, new { order.OrderNumber, order.VendorId, lineCount = requestedLines.Length, order.TotalAmount });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The purchase order changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The purchase-order number or lines changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(order.Id);
    }

    public async Task<TransactionResult> ApprovePurchaseOrderAsync(ApprovePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to approve purchase orders.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.PurchaseOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (order is null) return TransactionResult.Failure("Purchase order not found.");
        if (order.Status != "Draft") return TransactionResult.Failure("Only a draft purchase order can be approved.");
        if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The purchase order changed after it was opened. Refresh and review it again.");
        if (!await db.PurchaseOrderLines.AnyAsync(line => line.PurchaseOrderId == order.Id, cancellationToken)) return TransactionResult.Failure("A purchase order must contain at least one line before approval.");
        order.Status = "Approved";
        order.ApprovedByUserId = ResolveUserId();
        order.ApprovedAtUtc = DateTimeOffset.UtcNow;
        order.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(db, companyId, "purchase-order.approved", nameof(PurchaseOrder), order.Id, new { order.OrderNumber, order.TotalAmount });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The purchase order changed while it was being approved. Refresh and try again."); }
        return TransactionResult.Success(order.Id);
    }

    public async Task<TransactionResult> ReceivePurchaseOrderAsync(ReceivePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to receive purchase orders.");
        var requestedLines = request.Lines?.Where(line => line.Quantity != 0m).ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(request.ReceiptNumber) || requestedLines.Length == 0 || requestedLines.Any(line => line.PurchaseOrderLineId == Guid.Empty || line.Quantity <= 0m))
            return TransactionResult.Failure("A receipt number and at least one positive receipt quantity are required.");
        if (requestedLines.Select(line => line.PurchaseOrderLineId).Distinct().Count() != requestedLines.Length)
            return TransactionResult.Failure("Combine duplicate purchase-order receipt lines.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.PurchaseOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (order is null) return TransactionResult.Failure("Purchase order not found.");
        if (order.Status is not ("Approved" or "PartiallyReceived")) return TransactionResult.Failure("Only an approved or partially received purchase order can be received.");
        if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The purchase order changed after it was opened. Refresh and review it again.");
        if (request.ReceivedOn < order.OrderedOn) return TransactionResult.Failure("The receipt date cannot precede the order date.");
        var receiptNumber = request.ReceiptNumber.Trim();
        if (await db.InventoryReceipts.AnyAsync(receipt => receipt.CompanyId == companyId && receipt.ReceiptNumber == receiptNumber, cancellationToken)) return TransactionResult.Failure("Receipt number already exists.");

        var orderLines = await db.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var requestedIds = requestedLines.Select(line => line.PurchaseOrderLineId).ToArray();
        if (orderLines.Count(line => requestedIds.Contains(line.Id)) != requestedIds.Length) return TransactionResult.Failure("Every receipt line must belong to this purchase order.");
        foreach (var requested in requestedLines)
        {
            var line = orderLines.Single(candidate => candidate.Id == requested.PurchaseOrderLineId);
            if (RoundQuantity(requested.Quantity) > line.OrderedQuantity - (line.ReceivedQuantity - line.ReturnedQuantity)) return TransactionResult.Failure($"Receipt quantity exceeds the remaining net quantity for line {line.Sequence}.");
        }
        var itemIds = orderLines.Where(line => requestedIds.Contains(line.Id)).Select(line => line.InventoryItemId).ToArray();
        var items = await db.InventoryItems.Where(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (items.Count != itemIds.Distinct().Count()) return TransactionResult.Failure("One or more purchase-order items are no longer active in this company.");
        var location = await ResolveInventoryLocationAsync(db, companyId, request.WarehouseId, request.BinId, cancellationToken); if (location is null) return TransactionResult.Failure("Select an active receiving warehouse and bin.");
        var locationBalances = new Dictionary<Guid, InventoryLocationBalance>();
        foreach (var itemId in itemIds.Distinct()) locationBalances[itemId] = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, itemId, location.Value.Warehouse.Id, location.Value.Bin.Id, cancellationToken);
        var total = requestedLines.Sum(requested =>
        {
            var line = orderLines.Single(candidate => candidate.Id == requested.PurchaseOrderLineId);
            return RoundCurrency(RoundQuantity(requested.Quantity) * line.UnitCost);
        });
        if (total <= 0m) return TransactionResult.Failure("The receipt value must be greater than zero.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var receiptId = Guid.NewGuid();
        var posting = await PostAsync(db, companyId, request.ReceivedOn, "Purchasing", receiptNumber, $"Inventory receipt for purchase order {order.OrderNumber}",
            [new(OperationalRoleReference(AccountingAccountRoles.InventoryAsset), total, 0m, "Inventory received"), new(OperationalRoleReference(AccountingAccountRoles.GoodsReceivedNotInvoiced), 0m, total, "Goods received not invoiced")],
            cancellationToken, allowControlAccounts: true, sourceDocumentId: receiptId, sourceDocumentType: "InventoryReceipt", resolveOperationalRoles: true);
        if (!posting.Succeeded) return posting;

        var receipt = new InventoryReceipt
        {
            Id = receiptId,
            CompanyId = companyId,
            PurchaseOrderId = order.Id,
            WarehouseId = location.Value.Warehouse.Id,
            BinId = location.Value.Bin.Id,
            ReceiptNumber = receiptNumber,
            ReceivedOn = request.ReceivedOn,
            TotalAmount = total,
            JournalEntryId = posting.Id!.Value,
            ReceivedByUserId = ResolveUserId(),
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        db.InventoryReceipts.Add(receipt);
        var sequence = 0;
        foreach (var requested in requestedLines.OrderBy(line => orderLines.Single(candidate => candidate.Id == line.PurchaseOrderLineId).Sequence))
        {
            var orderLine = orderLines.Single(candidate => candidate.Id == requested.PurchaseOrderLineId);
            var item = items[orderLine.InventoryItemId];
            var quantity = RoundQuantity(requested.Quantity);
            var priorQuantity = item.QuantityOnHand;
            var priorCost = item.UnitCost;
            var resultingQuantity = priorQuantity + quantity;
            var resultingCost = RoundCurrency(((priorQuantity * priorCost) + (quantity * orderLine.UnitCost)) / resultingQuantity);
            item.QuantityOnHand = resultingQuantity;
            item.ConcurrencyToken = Guid.NewGuid().ToString("N");
            locationBalances[item.Id].QuantityOnHand += quantity;
            locationBalances[item.Id].ConcurrencyToken = Guid.NewGuid().ToString("N");
            item.UnitCost = resultingCost;
            orderLine.ReceivedQuantity += quantity;
            var lineTotal = RoundCurrency(quantity * orderLine.UnitCost);
            db.InventoryReceiptLines.Add(new InventoryReceiptLine { Id = Guid.NewGuid(), InventoryReceiptId = receipt.Id, PurchaseOrderLineId = orderLine.Id, InventoryItemId = item.Id, Sequence = ++sequence, Quantity = quantity, UnitCost = orderLine.UnitCost, LineTotal = lineTotal, PriorQuantityOnHand = priorQuantity, PriorUnitCost = priorCost, ResultingUnitCost = resultingCost });
            db.InventoryTransactions.Add(new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, WarehouseId = location.Value.Warehouse.Id, BinId = location.Value.Bin.Id, OccurredOn = request.ReceivedOn, TransactionType = "Purchase receipt", QuantityChange = quantity, UnitCost = orderLine.UnitCost, TotalCost = lineTotal, Reference = receiptNumber, JournalEntryId = posting.Id.Value });
        }
        SetPurchaseOrderReturnStatus(order, orderLines);
        AddPurchasingAudit(db, companyId, "inventory-receipt.posted", nameof(InventoryReceipt), receipt.Id, new { receipt.ReceiptNumber, order.Id, order.OrderNumber, receipt.TotalAmount, lineCount = requestedLines.Length });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The purchase order or inventory changed while the receipt was posting. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The receipt number or purchase-order quantities changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(receipt.Id);
    }

    public async Task<TransactionResult> UnmatchPurchaseOrderReceiptBillAsync(UnmatchPurchaseOrderReceiptBillRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage) || !HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to void a matched receipt bill.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A bill void reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var receipt = await db.InventoryReceipts.SingleOrDefaultAsync(candidate => candidate.Id == request.InventoryReceiptId && candidate.CompanyId == companyId, cancellationToken);
        if (receipt is null || receipt.Status != "Posted") return TransactionResult.Failure("A matched posted receipt is required.");
        if (!string.Equals(receipt.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The receipt changed after it was opened. Refresh and review it again.");
        if (await db.PurchaseInvoiceMatches.AnyAsync(candidate => candidate.InventoryReceiptId == receipt.Id && candidate.CompanyId == companyId, cancellationToken))
            return TransactionResult.Failure("Reverse controlled invoice matches from the invoice-match review instead of using the legacy receipt correction.");
        var legacyBills = await db.VendorBills.Where(candidate => candidate.InventoryReceiptId == receipt.Id && candidate.CompanyId == companyId).ToListAsync(cancellationToken);
        if (legacyBills.Count > 1) return TransactionResult.Failure("This receipt has multiple bills and cannot use the legacy one-bill correction. Reverse each controlled invoice match instead.");
        var bill = legacyBills.SingleOrDefault();
        if (bill is null) return TransactionResult.Failure("A matched posted receipt is required.");
        if (bill.Status != "Open" || bill.BalanceDue != bill.TotalAmount) return TransactionResult.Failure("Only a fully open, unapplied matched bill can be voided.");
        if (request.VoidDate < bill.BillDate) return TransactionResult.Failure("The void date cannot precede the bill date.");
        if (await db.SubledgerPaymentApplications.AnyAsync(application => application.DocumentId == bill.Id, cancellationToken) || await db.SubledgerAdjustments.AnyAsync(adjustment => adjustment.CompanyId == companyId && adjustment.DocumentId == bill.Id, cancellationToken) || await db.SupplierReturnShipments.AnyAsync(shipment => shipment.CompanyId == companyId && shipment.SourceVendorBillId == bill.Id && shipment.Status == "Posted", cancellationToken) || await db.SupplierReturnCreditApplications.AnyAsync(application => application.CompanyId == companyId && application.VendorBillId == bill.Id && application.Status == "Posted", cancellationToken))
            return TransactionResult.Failure("A matched bill with payment or adjustment history cannot be voided until that history is reversed.");
        var journalId = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && entry.SourceDocumentType == "VendorBill" && entry.SourceDocumentId == bill.Id && entry.IsPosted).Select(entry => (Guid?)entry.Id).SingleOrDefaultAsync(cancellationToken);
        if (!journalId.HasValue) return TransactionResult.Failure("The matched bill posting could not be found.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reversal = await PostInverseAsync(db, companyId, journalId.Value, request.VoidDate, $"VOID-{bill.BillNumber}", request.Reason.Trim(), bill.Id, "PurchaseReceiptBillVoid", null, cancellationToken, "Accounts Payable");
        if (!reversal.Succeeded) return reversal;
        var order = await db.PurchaseOrders.SingleAsync(candidate => candidate.Id == receipt.PurchaseOrderId && candidate.CompanyId == companyId, cancellationToken);
        var billLines = await db.VendorBillLines.Where(line => line.VendorBillId == bill.Id).ToListAsync(cancellationToken);
        if (billLines.Any(line => !line.InventoryReceiptLineId.HasValue)) return TransactionResult.Failure("The matched bill is missing receipt-line provenance and cannot be safely voided.");
        var receiptLines = await db.InventoryReceiptLines.Where(line => line.InventoryReceiptId == receipt.Id).ToDictionaryAsync(line => line.Id, cancellationToken);
        var poLines = await db.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).ToDictionaryAsync(line => line.Id, cancellationToken);
        foreach (var line in billLines) poLines[receiptLines[line.InventoryReceiptLineId!.Value].PurchaseOrderLineId].InvoicedQuantity -= line.Quantity;
        var vendor = await db.Vendors.SingleAsync(candidate => candidate.Id == bill.VendorId && candidate.CompanyId == companyId, cancellationToken);
        vendor.OpenBalance -= bill.TotalAmount;
        bill.Status = "Voided";
        bill.BalanceDue = 0m;
        bill.InventoryReceiptId = null;
        bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
        receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        SetPurchaseOrderReturnStatus(order, poLines.Values);
        AddPurchasingAudit(db, companyId, "purchase-receipt.bill.unmatched", nameof(InventoryReceipt), receipt.Id, new { purchaseOrderId = order.Id, vendorBillId = bill.Id, reversalJournalEntryId = reversal.Id, reason = request.Reason.Trim() });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The matched bill changed while it was being voided. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(bill.Id);
    }

    public async Task<TransactionResult> ReverseInventoryReceiptAsync(ReverseInventoryReceiptRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to reverse inventory receipts.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A receipt reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var receipt = await db.InventoryReceipts.SingleOrDefaultAsync(candidate => candidate.Id == request.InventoryReceiptId && candidate.CompanyId == companyId, cancellationToken);
        if (receipt is null || receipt.Status != "Posted") return TransactionResult.Failure("Only a posted inventory receipt can be reversed.");
        if (await db.VendorBills.AnyAsync(bill => bill.InventoryReceiptId == receipt.Id && bill.Status != "Voided", cancellationToken)) return TransactionResult.Failure("Void the matched vendor bill before reversing this receipt.");
        if (await db.SupplierReturnAuthorizations.AnyAsync(authorization => authorization.CompanyId == companyId && authorization.InventoryReceiptId == receipt.Id && authorization.Status != "Cancelled", cancellationToken)) return TransactionResult.Failure("Cancel every supplier-return authorization before reversing this receipt.");
        if (!string.Equals(receipt.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The receipt changed after it was opened. Refresh and review it again.");
        if (request.ReversalDate < receipt.ReceivedOn) return TransactionResult.Failure("The reversal date cannot precede the receipt date.");
        var receiptLines = await db.InventoryReceiptLines.Where(line => line.InventoryReceiptId == receipt.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var itemIds = receiptLines.Select(line => line.InventoryItemId).ToArray();
        var items = await db.InventoryItems.Where(item => item.CompanyId == companyId && itemIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (items.Count != itemIds.Distinct().Count()) return TransactionResult.Failure("One or more received inventory items could not be found.");
        var location = await ResolveInventoryLocationAsync(db, companyId, receipt.WarehouseId, receipt.BinId, cancellationToken); if (location is null) return TransactionResult.Failure("The receipt warehouse or bin is unavailable.");
        var locationBalances = new Dictionary<Guid, InventoryLocationBalance>();
        foreach (var itemId in itemIds.Distinct()) locationBalances[itemId] = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, itemId, location.Value.Warehouse.Id, location.Value.Bin.Id, cancellationToken);
        var finalLineByItem = receiptLines.GroupBy(line => line.InventoryItemId).Select(group => group.OrderByDescending(line => line.Sequence).First());
        if (finalLineByItem.Any(line => items[line.InventoryItemId].QuantityOnHand != line.PriorQuantityOnHand + line.Quantity || items[line.InventoryItemId].UnitCost != line.ResultingUnitCost))
            return TransactionResult.Failure("This receipt is no longer the latest valuation event for every item. Post a dated compensating inventory adjustment instead of reversing historical stock movement.");
        foreach (var itemGroup in receiptLines.GroupBy(line => line.InventoryItemId))
        {
            var reserved = await db.SalesOrderLines.Where(line => line.InventoryItemId == itemGroup.Key && line.AllocationWarehouseId == location.Value.Warehouse.Id && line.AllocationBinId == location.Value.Bin.Id).Select(line => line.AllocatedQuantity).ToListAsync(cancellationToken);
            if (locationBalances[itemGroup.Key].QuantityOnHand - itemGroup.Sum(line => line.Quantity) < reserved.Sum()) return TransactionResult.Failure("The receiving bin does not have enough unreserved stock to reverse this receipt. Post a compensating transfer or adjustment instead.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reversal = await PostInverseAsync(db, companyId, receipt.JournalEntryId, request.ReversalDate, $"REV-{receipt.ReceiptNumber}", request.Reason.Trim(), receipt.Id, "InventoryReceiptReversal", null, cancellationToken, "Purchasing");
        if (!reversal.Succeeded) return reversal;
        var order = await db.PurchaseOrders.SingleAsync(candidate => candidate.Id == receipt.PurchaseOrderId && candidate.CompanyId == companyId, cancellationToken);
        var poLines = await db.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).ToDictionaryAsync(line => line.Id, cancellationToken);
        foreach (var line in receiptLines.OrderByDescending(line => line.Sequence))
        {
            var item = items[line.InventoryItemId];
            item.QuantityOnHand = line.PriorQuantityOnHand;
            item.ConcurrencyToken = Guid.NewGuid().ToString("N");
            locationBalances[item.Id].QuantityOnHand -= line.Quantity;
            locationBalances[item.Id].ConcurrencyToken = Guid.NewGuid().ToString("N");
            item.UnitCost = line.PriorUnitCost;
            poLines[line.PurchaseOrderLineId].ReceivedQuantity -= line.Quantity;
            db.InventoryTransactions.Add(new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, WarehouseId = location.Value.Warehouse.Id, BinId = location.Value.Bin.Id, OccurredOn = request.ReversalDate, TransactionType = "Purchase receipt reversal", QuantityChange = -line.Quantity, UnitCost = line.UnitCost, TotalCost = -line.LineTotal, Reference = $"REV-{receipt.ReceiptNumber}", JournalEntryId = reversal.Id!.Value });
        }
        receipt.Status = "Reversed";
        receipt.ReversalJournalEntryId = reversal.Id;
        receipt.ReversedByUserId = ResolveUserId();
        receipt.ReversedAtUtc = DateTimeOffset.UtcNow;
        receipt.ReversalDate = request.ReversalDate;
        receipt.ReversalReason = request.Reason.Trim();
        receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        SetPurchaseOrderReturnStatus(order, poLines.Values);
        AddPurchasingAudit(db, companyId, "inventory-receipt.reversed", nameof(InventoryReceipt), receipt.Id, new { order.Id, reversalJournalEntryId = reversal.Id, reason = request.Reason.Trim() });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The receipt, purchase order, or inventory changed while it was being reversed. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(receipt.Id);
    }

    private void AddPurchasingAudit(BrassLedgerDbContext db, Guid companyId, string action, string entityType, Guid entityId, object details) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserId = ResolveUserId(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailJson = JsonSerializer.Serialize(details),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

    private static decimal RoundQuantity(decimal quantity) => decimal.Round(quantity, 4, MidpointRounding.AwayFromZero);
}
