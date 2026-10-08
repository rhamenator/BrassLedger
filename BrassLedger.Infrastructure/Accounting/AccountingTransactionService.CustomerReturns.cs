using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<TransactionResult> AuthorizeCustomerReturnAsync(AuthorizeCustomerReturnRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to approve customer returns.");
        var requested = request.Lines?.Where(x => x.Quantity != 0m).ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(request.ReturnNumber) || string.IsNullOrWhiteSpace(request.Reason) || requested.Length == 0 || requested.Any(x => x.InventoryShipmentLineId == Guid.Empty || RoundQuantity(x.Quantity) <= 0m) || requested.Select(x => x.InventoryShipmentLineId).Distinct().Count() != requested.Length)
            return TransactionResult.Failure("A return number, reason, and one positive quantity per selected shipment line are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var shipment = await db.InventoryShipments.SingleOrDefaultAsync(x => x.Id == request.InventoryShipmentId && x.CompanyId == companyId, cancellationToken);
        if (shipment is null) return TransactionResult.Failure("Inventory shipment not found.");
        if (shipment.Status != "Posted") return TransactionResult.Failure("Only a posted shipment can be returned.");
        if (!string.Equals(shipment.ConcurrencyToken, request.ShipmentConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The shipment changed after it was opened. Refresh and review it again.");
        if (request.AuthorizedOn < shipment.ShippedOn) return TransactionResult.Failure("The return authorization date cannot precede the shipment date.");
        var returnNumber = request.ReturnNumber.Trim();
        if (await db.CustomerReturnAuthorizations.AnyAsync(x => x.CompanyId == companyId && x.ReturnNumber == returnNumber, cancellationToken)) return TransactionResult.Failure("Return authorization number already exists.");
        var shipmentLines = await db.InventoryShipmentLines.Where(x => x.InventoryShipmentId == shipment.Id).OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        var requestedIds = requested.Select(x => x.InventoryShipmentLineId).ToArray();
        if (shipmentLines.Count(x => requestedIds.Contains(x.Id)) != requestedIds.Length) return TransactionResult.Failure("Every return line must belong to this shipment.");
        var activeAuthorizations = await (from line in db.CustomerReturnAuthorizationLines join existingAuthorization in db.CustomerReturnAuthorizations on line.CustomerReturnAuthorizationId equals existingAuthorization.Id where existingAuthorization.CompanyId == companyId && existingAuthorization.Status != "Cancelled" && requestedIds.Contains(line.InventoryShipmentLineId) select new { line.InventoryShipmentLineId, line.AuthorizedQuantity }).ToListAsync(cancellationToken);
        foreach (var line in requested)
        {
            var shipped = shipmentLines.Single(x => x.Id == line.InventoryShipmentLineId);
            var alreadyAuthorized = activeAuthorizations.Where(x => x.InventoryShipmentLineId == line.InventoryShipmentLineId).Sum(x => x.AuthorizedQuantity);
            if (RoundQuantity(line.Quantity) > shipped.Quantity - alreadyAuthorized) return TransactionResult.Failure($"Return quantity exceeds the unreserved shipped quantity for line {shipped.Sequence}.");
        }
        var order = await db.SalesOrders.SingleAsync(x => x.Id == shipment.SalesOrderId && x.CompanyId == companyId, cancellationToken);
        var authorization = new CustomerReturnAuthorization { Id = Guid.NewGuid(), CompanyId = companyId, InventoryShipmentId = shipment.Id, SalesOrderId = order.Id, CustomerId = order.CustomerId, ReturnNumber = returnNumber, AuthorizedOn = request.AuthorizedOn, Reason = request.Reason.Trim(), AuthorizedByUserId = ResolveUserId(), AuthorizedAtUtc = DateTimeOffset.UtcNow };
        db.CustomerReturnAuthorizations.Add(authorization);
        db.CustomerReturnAuthorizationLines.AddRange(requested.OrderBy(x => shipmentLines.Single(y => y.Id == x.InventoryShipmentLineId).Sequence).Select((x, index) => { var source = shipmentLines.Single(y => y.Id == x.InventoryShipmentLineId); return new CustomerReturnAuthorizationLine { Id = Guid.NewGuid(), CustomerReturnAuthorizationId = authorization.Id, InventoryShipmentLineId = source.Id, SalesOrderLineId = source.SalesOrderLineId, InventoryItemId = source.InventoryItemId, Sequence = index + 1, AuthorizedQuantity = RoundQuantity(x.Quantity) }; }));
        shipment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesFulfillmentAudit(db, companyId, "customer-return.authorized", nameof(CustomerReturnAuthorization), authorization.Id, new { authorization.ReturnNumber, shipment.ShipmentNumber, authorization.Reason, lines = requested });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The shipment changed while the return was being authorized. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The return number or authorized quantities changed concurrently. Refresh and try again."); }
        return TransactionResult.Success(authorization.Id);
    }

    public async Task<TransactionResult> CancelCustomerReturnAsync(CancelCustomerReturnRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to cancel customer returns.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A cancellation reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var authorization = await db.CustomerReturnAuthorizations.SingleOrDefaultAsync(x => x.Id == request.CustomerReturnAuthorizationId && x.CompanyId == companyId, cancellationToken);
        if (authorization is null) return TransactionResult.Failure("Return authorization not found.");
        if (authorization.Status is not ("Open" or "PartiallyReceived")) return TransactionResult.Failure("Only an open return authorization can be cancelled.");
        if (!string.Equals(authorization.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The return authorization changed after it was opened. Refresh and review it again.");
        if (await db.CustomerReturnReceipts.AnyAsync(x => x.CompanyId == companyId && x.CustomerReturnAuthorizationId == authorization.Id && x.Status == "Posted", cancellationToken)) return TransactionResult.Failure("Reverse all posted return receipts before cancelling the authorization.");
        authorization.Status = "Cancelled"; authorization.CancelledByUserId = ResolveUserId(); authorization.CancelledAtUtc = DateTimeOffset.UtcNow; authorization.CancellationReason = request.Reason.Trim(); authorization.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var shipment = await db.InventoryShipments.SingleAsync(x => x.Id == authorization.InventoryShipmentId && x.CompanyId == companyId, cancellationToken); shipment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesFulfillmentAudit(db, companyId, "customer-return.cancelled", nameof(CustomerReturnAuthorization), authorization.Id, new { authorization.ReturnNumber, authorization.CancellationReason });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The return authorization changed while it was being cancelled. Refresh and try again."); }
        return TransactionResult.Success(authorization.Id);
    }

    public async Task<TransactionResult> ReceiveCustomerReturnAsync(ReceiveCustomerReturnRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.FulfillmentManage)) return TransactionResult.Failure("You are not authorized to receive customer returns.");
        var requested = request.Lines?.Where(x => x.Quantity != 0m).ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(request.ReceiptNumber) || requested.Length == 0 || requested.Any(x => x.CustomerReturnAuthorizationLineId == Guid.Empty || RoundQuantity(x.Quantity) <= 0m) || requested.Select(x => x.CustomerReturnAuthorizationLineId).Distinct().Count() != requested.Length)
            return TransactionResult.Failure("A receipt number and one positive quantity per selected return line are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var authorization = await db.CustomerReturnAuthorizations.SingleOrDefaultAsync(x => x.Id == request.CustomerReturnAuthorizationId && x.CompanyId == companyId, cancellationToken);
        if (authorization is null) return TransactionResult.Failure("Return authorization not found.");
        if (authorization.Status is not ("Open" or "PartiallyReceived")) return TransactionResult.Failure("Only an open return authorization can be received.");
        if (!string.Equals(authorization.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The return authorization changed after it was opened. Refresh and review it again.");
        if (request.ReceivedOn < authorization.AuthorizedOn) return TransactionResult.Failure("The receipt date cannot precede the authorization date.");
        var receiptNumber = request.ReceiptNumber.Trim();
        if (await db.CustomerReturnReceipts.AnyAsync(x => x.CompanyId == companyId && x.ReceiptNumber == receiptNumber, cancellationToken)) return TransactionResult.Failure("Customer return receipt number already exists.");
        var lines = await db.CustomerReturnAuthorizationLines.Where(x => x.CustomerReturnAuthorizationId == authorization.Id).OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        var requestedIds = requested.Select(x => x.CustomerReturnAuthorizationLineId).ToArray();
        if (lines.Count(x => requestedIds.Contains(x.Id)) != requestedIds.Length) return TransactionResult.Failure("Every receipt line must belong to this return authorization.");
        foreach (var item in requested) { var line = lines.Single(x => x.Id == item.CustomerReturnAuthorizationLineId); if (RoundQuantity(item.Quantity) > line.AuthorizedQuantity - line.ReceivedQuantity) return TransactionResult.Failure($"Receipt quantity exceeds the remaining authorized quantity for line {line.Sequence}."); }
        var location = await ResolveInventoryLocationAsync(db, companyId, request.WarehouseId, request.BinId, cancellationToken); if (location is null) return TransactionResult.Failure("Select an active receiving warehouse and bin.");
        var shipmentLines = await db.InventoryShipmentLines.Where(x => lines.Select(y => y.InventoryShipmentLineId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var items = await db.InventoryItems.Where(x => x.CompanyId == companyId && lines.Select(y => y.InventoryItemId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var orderLines = await db.SalesOrderLines.Where(x => x.SalesOrderId == authorization.SalesOrderId).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (shipmentLines.Count != lines.Count || items.Count != lines.Select(x => x.InventoryItemId).Distinct().Count()) return TransactionResult.Failure("One or more return source records are unavailable in this company.");
        var returnedOrderLines = requested.Select(item => orderLines[lines.Single(line => line.Id == item.CustomerReturnAuthorizationLineId).SalesOrderLineId]).ToArray();
        if (!await AreActiveTrackingDimensionsAsync(db, companyId, request.ReceivedOn, returnedOrderLines.Select(line => (line.DepartmentId, line.ClassId)), cancellationToken, allowHistorical: true)) return TransactionResult.Failure("One or more original sale departments or classes are unavailable or incorrectly typed.");
        var totalCost = requested.Sum(x => RoundCurrency(RoundQuantity(x.Quantity) * shipmentLines[lines.Single(y => y.Id == x.CustomerReturnAuthorizationLineId).InventoryShipmentLineId].UnitCost));
        if (totalCost <= 0m) return TransactionResult.Failure("The returned inventory cost must be greater than zero.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var receiptId = Guid.NewGuid();
        var postingLines = new List<JournalLineRequest> { new(OperationalRoleReference(AccountingAccountRoles.InventoryAsset), totalCost, 0m, "Returned inventory") };
        postingLines.AddRange(requested.Select(item =>
        {
            var authorizationLine = lines.Single(line => line.Id == item.CustomerReturnAuthorizationLineId);
            return new
            {
                orderLines[authorizationLine.SalesOrderLineId].ProjectJobId,
                orderLines[authorizationLine.SalesOrderLineId].ProjectPhaseId,
                orderLines[authorizationLine.SalesOrderLineId].ProjectCostCodeId,
                orderLines[authorizationLine.SalesOrderLineId].DepartmentId,
                orderLines[authorizationLine.SalesOrderLineId].ClassId,
                Cost = RoundCurrency(RoundQuantity(item.Quantity) * shipmentLines[authorizationLine.InventoryShipmentLineId].UnitCost)
            };
        }).GroupBy(line => new { line.ProjectJobId, line.ProjectPhaseId, line.ProjectCostCodeId, line.DepartmentId, line.ClassId }).Select(group => new JournalLineRequest(OperationalRoleReference(AccountingAccountRoles.CostOfGoodsSold), 0m, group.Sum(line => line.Cost), "Reverse cost of goods sold", group.Key.ProjectJobId, group.Key.ProjectPhaseId, group.Key.ProjectCostCodeId, group.Key.DepartmentId, group.Key.ClassId)));
        var posting = await PostAsync(db, companyId, request.ReceivedOn, "Sales Fulfillment", receiptNumber, $"Customer return {authorization.ReturnNumber}", postingLines, cancellationToken, allowControlAccounts: true, sourceDocumentId: receiptId, sourceDocumentType: "CustomerReturnReceipt", resolveOperationalRoles: true, allowClosedProjects: true);
        if (!posting.Succeeded) return posting;
        var receipt = new CustomerReturnReceipt { Id = receiptId, CompanyId = companyId, CustomerReturnAuthorizationId = authorization.Id, WarehouseId = location.Value.Warehouse.Id, BinId = location.Value.Bin.Id, ReceiptNumber = receiptNumber, ReceivedOn = request.ReceivedOn, TotalCost = totalCost, JournalEntryId = posting.Id!.Value, ReceivedByUserId = ResolveUserId(), ReceivedAtUtc = DateTimeOffset.UtcNow };
        db.CustomerReturnReceipts.Add(receipt);
        var sequence = 0;
        foreach (var item in requested.OrderBy(x => lines.Single(y => y.Id == x.CustomerReturnAuthorizationLineId).Sequence))
        {
            var line = lines.Single(x => x.Id == item.CustomerReturnAuthorizationLineId); var source = shipmentLines[line.InventoryShipmentLineId]; var inventoryItem = items[line.InventoryItemId]; var quantity = RoundQuantity(item.Quantity); var cost = RoundCurrency(quantity * source.UnitCost);
            line.ReceivedQuantity += quantity; inventoryItem.QuantityOnHand += quantity; inventoryItem.ConcurrencyToken = Guid.NewGuid().ToString("N"); orderLines[line.SalesOrderLineId].ReturnedQuantity += quantity;
            var balance = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, inventoryItem.Id, location.Value.Warehouse.Id, location.Value.Bin.Id, cancellationToken); balance.QuantityOnHand += quantity; balance.ConcurrencyToken = Guid.NewGuid().ToString("N");
            db.CustomerReturnReceiptLines.Add(new CustomerReturnReceiptLine { Id = Guid.NewGuid(), CustomerReturnReceiptId = receipt.Id, CustomerReturnAuthorizationLineId = line.Id, InventoryShipmentLineId = source.Id, SalesOrderLineId = line.SalesOrderLineId, InventoryItemId = inventoryItem.Id, Sequence = ++sequence, Quantity = quantity, UnitCost = source.UnitCost, TotalCost = cost });
            db.InventoryTransactions.Add(new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = inventoryItem.Id, WarehouseId = location.Value.Warehouse.Id, BinId = location.Value.Bin.Id, OccurredOn = request.ReceivedOn, TransactionType = "Customer return", QuantityChange = quantity, UnitCost = source.UnitCost, TotalCost = cost, Reference = receiptNumber, JournalEntryId = posting.Id.Value });
        }
        authorization.Status = lines.All(x => x.ReceivedQuantity == x.AuthorizedQuantity) ? "Received" : "PartiallyReceived"; authorization.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var order = await db.SalesOrders.SingleAsync(x => x.Id == authorization.SalesOrderId && x.CompanyId == companyId, cancellationToken); order.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesFulfillmentAudit(db, companyId, "customer-return.received", nameof(CustomerReturnReceipt), receipt.Id, new { receipt.ReceiptNumber, authorization.ReturnNumber, warehouse = location.Value.Warehouse.Code, bin = location.Value.Bin.Code, receipt.TotalCost, lines = requested });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The return authorization or inventory changed while receiving. Refresh and try again."); } catch (DbUpdateException) { return TransactionResult.Failure("The receipt number or return quantities changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken); return TransactionResult.Success(receipt.Id);
    }

    public async Task<TransactionResult> ReverseCustomerReturnReceiptAsync(ReverseCustomerReturnReceiptRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.FulfillmentManage) || !HasPermission(BrassLedgerPermissions.PaymentReverse)) return TransactionResult.Failure("You are not authorized to reverse customer return receipts.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var receipt = await db.CustomerReturnReceipts.SingleOrDefaultAsync(x => x.Id == request.CustomerReturnReceiptId && x.CompanyId == companyId, cancellationToken);
        if (receipt is null || receipt.Status != "Posted") return TransactionResult.Failure("Only a posted customer return receipt can be reversed.");
        if (!string.Equals(receipt.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The return receipt changed after it was opened. Refresh and review it again.");
        if (request.ReversalDate < receipt.ReceivedOn) return TransactionResult.Failure("The reversal date cannot precede the receipt date.");
        if (await db.CustomerReturnCredits.AnyAsync(x => x.CompanyId == companyId && x.CustomerReturnReceiptId == receipt.Id && x.Status == "Posted", cancellationToken)) return TransactionResult.Failure("Reverse the customer return credit before reversing the physical receipt.");
        var lines = await db.CustomerReturnReceiptLines.Where(x => x.CustomerReturnReceiptId == receipt.Id).ToListAsync(cancellationToken); var authorization = await db.CustomerReturnAuthorizations.SingleAsync(x => x.Id == receipt.CustomerReturnAuthorizationId && x.CompanyId == companyId, cancellationToken); var authorizationLines = await db.CustomerReturnAuthorizationLines.Where(x => x.CustomerReturnAuthorizationId == authorization.Id).ToDictionaryAsync(x => x.Id, cancellationToken); var items = await db.InventoryItems.Where(x => x.CompanyId == companyId && lines.Select(y => y.InventoryItemId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken); var orderLines = await db.SalesOrderLines.Where(x => x.SalesOrderId == authorization.SalesOrderId).ToDictionaryAsync(x => x.Id, cancellationToken); var balances = new Dictionary<Guid, InventoryLocationBalance>(); foreach (var itemId in lines.Select(x => x.InventoryItemId).Distinct()) balances[itemId] = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, itemId, receipt.WarehouseId, receipt.BinId, cancellationToken);
        foreach (var group in lines.GroupBy(x => x.InventoryItemId)) if (items[group.Key].QuantityOnHand < group.Sum(x => x.Quantity) || balances[group.Key].QuantityOnHand < group.Sum(x => x.Quantity)) return TransactionResult.Failure("Returned inventory has already been consumed or moved; restore it to the receipt location before reversal.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken); var reversal = await PostInverseAsync(db, companyId, receipt.JournalEntryId, request.ReversalDate, $"REV-{receipt.ReceiptNumber}", request.Reason.Trim(), receipt.Id, "CustomerReturnReceiptReversal", null, cancellationToken, "Sales Fulfillment"); if (!reversal.Succeeded) return reversal;
        foreach (var line in lines) { items[line.InventoryItemId].QuantityOnHand -= line.Quantity; items[line.InventoryItemId].ConcurrencyToken = Guid.NewGuid().ToString("N"); balances[line.InventoryItemId].QuantityOnHand -= line.Quantity; balances[line.InventoryItemId].ConcurrencyToken = Guid.NewGuid().ToString("N"); authorizationLines[line.CustomerReturnAuthorizationLineId].ReceivedQuantity -= line.Quantity; orderLines[line.SalesOrderLineId].ReturnedQuantity -= line.Quantity; db.InventoryTransactions.Add(new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = line.InventoryItemId, WarehouseId = receipt.WarehouseId, BinId = receipt.BinId, OccurredOn = request.ReversalDate, TransactionType = "Customer return reversal", QuantityChange = -line.Quantity, UnitCost = line.UnitCost, TotalCost = -line.TotalCost, Reference = $"REV-{receipt.ReceiptNumber}", JournalEntryId = reversal.Id }); }
        authorization.Status = authorizationLines.Values.All(x => x.ReceivedQuantity == 0m) ? "Open" : authorizationLines.Values.All(x => x.ReceivedQuantity == x.AuthorizedQuantity) ? "Received" : "PartiallyReceived"; authorization.ConcurrencyToken = Guid.NewGuid().ToString("N"); receipt.Status = "Reversed"; receipt.ReversalJournalEntryId = reversal.Id; receipt.ReversedByUserId = ResolveUserId(); receipt.ReversedAtUtc = DateTimeOffset.UtcNow; receipt.ReversalDate = request.ReversalDate; receipt.ReversalReason = request.Reason.Trim(); receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesFulfillmentAudit(db, companyId, "customer-return.receipt.reversed", nameof(CustomerReturnReceipt), receipt.Id, new { receipt.ReceiptNumber, receipt.ReversalDate, receipt.ReversalReason });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The return receipt or inventory changed while reversing. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken); return TransactionResult.Success(receipt.Id);
    }
}
