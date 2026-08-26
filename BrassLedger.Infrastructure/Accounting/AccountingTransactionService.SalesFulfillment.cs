using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<TransactionResult> SaveSalesOrderAsync(SaveSalesOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to prepare sales orders.");
        var requestedLines = request.Lines?.ToArray() ?? [];
        if (request.CustomerId == Guid.Empty || string.IsNullOrWhiteSpace(request.OrderNumber) || requestedLines.Length == 0)
            return TransactionResult.Failure("A customer, order number, and at least one line are required.");
        if (request.RequestedShipOn.HasValue && request.RequestedShipOn.Value < request.OrderedOn)
            return TransactionResult.Failure("The requested ship date cannot precede the order date.");
        if (requestedLines.Any(line => line.InventoryItemId == Guid.Empty || string.IsNullOrWhiteSpace(line.Description) || RoundQuantity(line.Quantity) <= 0m || line.UnitPrice < 0m || line.DiscountAmount < 0m || line.DiscountAmount > RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice) || line.TaxAmount < 0m || string.IsNullOrWhiteSpace(line.RevenueAccountNumber)))
            return TransactionResult.Failure("Every sales-order line requires an item, description, positive quantity, valid price and discount, non-negative tax, and revenue account.");
        if (requestedLines.Select(line => line.InventoryItemId).Distinct().Count() != requestedLines.Length)
            return TransactionResult.Failure("Combine duplicate inventory items into one sales-order line.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.Customers.AnyAsync(customer => customer.Id == request.CustomerId && customer.CompanyId == companyId, cancellationToken))
            return TransactionResult.Failure("Customer not found in the active company.");
        var itemIds = requestedLines.Select(line => line.InventoryItemId).ToArray();
        if (await db.InventoryItems.CountAsync(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id), cancellationToken) != itemIds.Length)
            return TransactionResult.Failure("Every sales-order item must be active in the current company.");
        var revenueNumbers = requestedLines.Select(line => line.RevenueAccountNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var revenueAccounts = await db.Accounts.Where(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Revenue && !account.IsControlAccount && revenueNumbers.Contains(account.Number)).ToDictionaryAsync(account => account.Number, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (revenueAccounts.Count != revenueNumbers.Length) return TransactionResult.Failure("Every sales-order distribution must use an active, non-control revenue account.");
        var orderNumber = request.OrderNumber.Trim();
        if (await db.SalesOrders.AnyAsync(order => order.CompanyId == companyId && order.OrderNumber == orderNumber && order.Id != request.Id, cancellationToken))
            return TransactionResult.Failure("Sales-order number already exists.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        SalesOrder order;
        if (request.Id.HasValue)
        {
            order = await db.SalesOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.Id.Value && candidate.CompanyId == companyId, cancellationToken) ?? new SalesOrder();
            if (order.Id == Guid.Empty) return TransactionResult.Failure("Sales order not found.");
            if (order.Status != "Draft") return TransactionResult.Failure("Only a draft sales order can be edited.");
            if (order.SalesQuoteId.HasValue) return TransactionResult.Failure("A quote-derived sales order preserves its approved commercial terms and cannot be edited. Prepare and approve a replacement quote or a separate order for changed terms.");
            if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The sales order changed after it was opened. Refresh and review it again.");
            db.SalesOrderLines.RemoveRange(await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).ToListAsync(cancellationToken));
        }
        else
        {
            order = new SalesOrder { Id = Guid.NewGuid(), CompanyId = companyId, Status = "Draft", PreparedByUserId = ResolveUserId(), PreparedAtUtc = DateTimeOffset.UtcNow };
            db.SalesOrders.Add(order);
        }

        order.CustomerId = request.CustomerId;
        order.OrderNumber = orderNumber;
        order.OrderedOn = request.OrderedOn;
        order.RequestedShipOn = request.RequestedShipOn;
        order.Notes = request.Notes?.Trim() ?? string.Empty;
        order.TotalAmount = requestedLines.Sum(line => RoundCurrency((RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice)) - RoundCurrency(line.DiscountAmount) + RoundCurrency(line.TaxAmount)));
        if (order.TotalAmount <= 0m) return TransactionResult.Failure("The sales-order total must be greater than zero.");
        order.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.SalesOrderLines.AddRange(requestedLines.Select((line, index) => new SalesOrderLine
        {
            Id = Guid.NewGuid(),
            SalesOrderId = order.Id,
            Sequence = index + 1,
            InventoryItemId = line.InventoryItemId,
            RevenueAccountId = revenueAccounts[line.RevenueAccountNumber.Trim()].Id,
            Description = line.Description.Trim(),
            OrderedQuantity = RoundQuantity(line.Quantity),
            UnitPrice = RoundCurrency(line.UnitPrice),
            DiscountAmount = RoundCurrency(line.DiscountAmount),
            TaxAmount = RoundCurrency(line.TaxAmount),
            LineTotal = RoundCurrency((RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice)) - RoundCurrency(line.DiscountAmount) + RoundCurrency(line.TaxAmount))
        }));
        AddSalesFulfillmentAudit(db, companyId, "sales-order.draft.saved", nameof(SalesOrder), order.Id, new { order.OrderNumber, order.CustomerId, lineCount = requestedLines.Length, order.TotalAmount });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The sales order changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The sales-order number or lines changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(order.Id);
    }

    public async Task<TransactionResult> ApproveSalesOrderAsync(ApproveSalesOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to approve sales orders.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var order = await db.SalesOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.SalesOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (order is null) return TransactionResult.Failure("Sales order not found.");
        if (order.Status != "Draft") return TransactionResult.Failure("Only a draft sales order can be approved.");
        if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The sales order changed after it was opened. Refresh and review it again.");
        if (!await db.SalesOrderLines.AnyAsync(line => line.SalesOrderId == order.Id, cancellationToken)) return TransactionResult.Failure("A sales order must contain at least one line before approval.");
        order.Status = "Approved";
        order.ApprovedByUserId = ResolveUserId();
        order.ApprovedAtUtc = DateTimeOffset.UtcNow;
        order.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesFulfillmentAudit(db, companyId, "sales-order.approved", nameof(SalesOrder), order.Id, new { order.OrderNumber, order.TotalAmount });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The sales order changed while it was being approved. Refresh and try again."); }
        return TransactionResult.Success(order.Id);
    }

    public async Task<TransactionResult> AllocateSalesOrderAsync(AllocateSalesOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.FulfillmentManage)) return TransactionResult.Failure("You are not authorized to allocate inventory.");
        var requestedLines = request.Lines?.ToArray() ?? [];
        if (requestedLines.Length == 0 || requestedLines.Any(line => line.SalesOrderLineId == Guid.Empty || line.Quantity < 0m || (line.Quantity > 0m && RoundQuantity(line.Quantity) <= 0m)) || requestedLines.Select(line => line.SalesOrderLineId).Distinct().Count() != requestedLines.Length)
            return TransactionResult.Failure("Provide one non-negative allocation quantity for each selected sales-order line.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var order = await db.SalesOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.SalesOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (order is null) return TransactionResult.Failure("Sales order not found.");
        if (order.Status is not ("Approved" or "Allocated" or "PartiallyShipped")) return TransactionResult.Failure("Only an approved, allocated, or partially shipped sales order can be allocated.");
        if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The sales order changed after it was opened. Refresh and review it again.");
        var orderLines = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        if (requestedLines.Any(requested => orderLines.All(line => line.Id != requested.SalesOrderLineId))) return TransactionResult.Failure("Every allocation line must belong to this sales order.");
        var requestedById = requestedLines.ToDictionary(line => line.SalesOrderLineId, line => RoundQuantity(line.Quantity));
        foreach (var line in orderLines.Where(line => requestedById.ContainsKey(line.Id)))
        {
            if (requestedById[line.Id] > line.OrderedQuantity - line.ShippedQuantity - line.CancelledQuantity) return TransactionResult.Failure($"Allocation exceeds the open, uncancelled quantity for line {line.Sequence}.");
        }
        var existingAllocationLocations = orderLines.Where(line => requestedById.ContainsKey(line.Id) && line.AllocatedQuantity > 0m && line.AllocationWarehouseId.HasValue && line.AllocationBinId.HasValue).Select(line => new { WarehouseId = line.AllocationWarehouseId!.Value, BinId = line.AllocationBinId!.Value }).Distinct().ToArray();
        if (!request.WarehouseId.HasValue && !request.BinId.HasValue && existingAllocationLocations.Length > 1 && requestedById.Values.Any(quantity => quantity > 0m)) return TransactionResult.Failure("Select a warehouse and bin when changing lines that currently span multiple locations.");
        var requestedWarehouseId = request.WarehouseId ?? (existingAllocationLocations.Length == 1 ? existingAllocationLocations[0].WarehouseId : null);
        var requestedBinId = request.BinId ?? (existingAllocationLocations.Length == 1 ? existingAllocationLocations[0].BinId : null);
        var location = await ResolveInventoryLocationAsync(db, companyId, requestedWarehouseId, requestedBinId, cancellationToken); if (location is null) return TransactionResult.Failure("Select an active allocation warehouse and bin.");
        var itemIds = orderLines.Where(line => requestedById.ContainsKey(line.Id)).Select(line => line.InventoryItemId).Distinct().ToArray();
        var items = await db.InventoryItems.Where(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (items.Count != itemIds.Length) return TransactionResult.Failure("One or more sales-order items are no longer active in this company.");
        var locationBalances = new Dictionary<Guid, InventoryLocationBalance>();
        foreach (var itemId in itemIds) locationBalances[itemId] = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, itemId, location.Value.Warehouse.Id, location.Value.Bin.Id, cancellationToken);
        var priorLocationBalances = new List<InventoryLocationBalance>();
        foreach (var prior in orderLines.Where(line => requestedById.ContainsKey(line.Id) && line.AllocatedQuantity > 0m && line.AllocationWarehouseId.HasValue && line.AllocationBinId.HasValue && (line.AllocationWarehouseId != location.Value.Warehouse.Id || line.AllocationBinId != location.Value.Bin.Id)).Select(line => new { line.InventoryItemId, WarehouseId = line.AllocationWarehouseId!.Value, BinId = line.AllocationBinId!.Value }).Distinct())
            priorLocationBalances.Add(await GetOrCreateInventoryLocationBalanceAsync(db, companyId, prior.InventoryItemId, prior.WarehouseId, prior.BinId, cancellationToken));
        var otherAllocationLines = await db.SalesOrderLines.Where(line => itemIds.Contains(line.InventoryItemId) && line.SalesOrderId != order.Id && line.AllocationWarehouseId == location.Value.Warehouse.Id && line.AllocationBinId == location.Value.Bin.Id).Select(line => new { line.InventoryItemId, line.AllocatedQuantity }).ToListAsync(cancellationToken);
        var otherAllocations = otherAllocationLines.GroupBy(line => line.InventoryItemId).ToDictionary(group => group.Key, group => group.Sum(line => line.AllocatedQuantity));
        foreach (var itemId in itemIds)
        {
            var requestedForItem = orderLines.Where(line => line.InventoryItemId == itemId && requestedById.ContainsKey(line.Id)).Sum(line => requestedById[line.Id]);
            var unchangedForItem = orderLines.Where(line => line.InventoryItemId == itemId && !requestedById.ContainsKey(line.Id) && line.AllocationWarehouseId == location.Value.Warehouse.Id && line.AllocationBinId == location.Value.Bin.Id).Sum(line => line.AllocatedQuantity);
            if (requestedForItem + unchangedForItem + otherAllocations.GetValueOrDefault(itemId) > locationBalances[itemId].QuantityOnHand)
                return TransactionResult.Failure($"Not enough unreserved inventory is available for {items[itemId].Sku} in {location.Value.Warehouse.Code}/{location.Value.Bin.Code}.");
        }
        foreach (var line in orderLines.Where(line => requestedById.ContainsKey(line.Id)))
        {
            line.AllocatedQuantity = requestedById[line.Id];
            line.AllocationWarehouseId = line.AllocatedQuantity > 0m ? location.Value.Warehouse.Id : null;
            line.AllocationBinId = line.AllocatedQuantity > 0m ? location.Value.Bin.Id : null;
        }
        foreach (var balance in locationBalances.Values.Concat(priorLocationBalances).DistinctBy(balance => balance.Id)) balance.ConcurrencyToken = Guid.NewGuid().ToString("N");
        order.Status = orderLines.Any(line => line.ShippedQuantity > 0m) ? "PartiallyShipped" : orderLines.Any(line => line.AllocatedQuantity > 0m) ? "Allocated" : "Approved";
        order.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesFulfillmentAudit(db, companyId, "sales-order.inventory.allocated", nameof(SalesOrder), order.Id, new { order.OrderNumber, warehouse = location.Value.Warehouse.Code, bin = location.Value.Bin.Code, allocations = requestedLines });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The sales order or inventory allocation changed concurrently. Refresh and try again."); }
        return TransactionResult.Success(order.Id);
    }

    public async Task<TransactionResult> ShipSalesOrderAsync(ShipSalesOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.FulfillmentManage)) return TransactionResult.Failure("You are not authorized to ship sales orders.");
        var requestedLines = request.Lines?.Where(line => line.Quantity != 0m).ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(request.ShipmentNumber) || requestedLines.Length == 0 || requestedLines.Any(line => line.SalesOrderLineId == Guid.Empty || RoundQuantity(line.Quantity) <= 0m) || requestedLines.Select(line => line.SalesOrderLineId).Distinct().Count() != requestedLines.Length)
            return TransactionResult.Failure("A shipment number and one positive quantity per selected order line are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var order = await db.SalesOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.SalesOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (order is null) return TransactionResult.Failure("Sales order not found.");
        if (order.Status is not ("Allocated" or "PartiallyShipped")) return TransactionResult.Failure("Only allocated inventory can be shipped.");
        if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The sales order changed after it was opened. Refresh and review it again.");
        if (request.ShippedOn < order.OrderedOn) return TransactionResult.Failure("The shipment date cannot precede the order date.");
        var shipmentNumber = request.ShipmentNumber.Trim();
        if (await db.InventoryShipments.AnyAsync(shipment => shipment.CompanyId == companyId && shipment.ShipmentNumber == shipmentNumber, cancellationToken)) return TransactionResult.Failure("Shipment number already exists.");
        var orderLines = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var requestedIds = requestedLines.Select(line => line.SalesOrderLineId).ToArray();
        if (orderLines.Count(line => requestedIds.Contains(line.Id)) != requestedIds.Length) return TransactionResult.Failure("Every shipment line must belong to this sales order.");
        foreach (var requested in requestedLines)
        {
            var line = orderLines.Single(candidate => candidate.Id == requested.SalesOrderLineId);
            if (RoundQuantity(requested.Quantity) > line.AllocatedQuantity) return TransactionResult.Failure($"Shipment quantity exceeds the allocated quantity for line {line.Sequence}.");
        }
        var allocationLocations = orderLines.Where(line => requestedIds.Contains(line.Id)).Select(line => new { line.AllocationWarehouseId, line.AllocationBinId }).Distinct().ToArray();
        if (allocationLocations.Length != 1 || !allocationLocations[0].AllocationWarehouseId.HasValue || !allocationLocations[0].AllocationBinId.HasValue) return TransactionResult.Failure("Every shipment line must be allocated from the same warehouse and bin.");
        var location = await ResolveInventoryLocationAsync(db, companyId, allocationLocations[0].AllocationWarehouseId, allocationLocations[0].AllocationBinId, cancellationToken); if (location is null) return TransactionResult.Failure("The allocated warehouse or bin is no longer active.");
        var itemIds = orderLines.Where(line => requestedIds.Contains(line.Id)).Select(line => line.InventoryItemId).ToArray();
        var items = await db.InventoryItems.Where(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (items.Count != itemIds.Distinct().Count()) return TransactionResult.Failure("One or more allocated items are no longer active in this company.");
        var locationBalances = new Dictionary<Guid, InventoryLocationBalance>();
        foreach (var itemId in itemIds.Distinct()) locationBalances[itemId] = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, itemId, location.Value.Warehouse.Id, location.Value.Bin.Id, cancellationToken);
        foreach (var itemGroup in requestedLines.GroupBy(requested => orderLines.Single(candidate => candidate.Id == requested.SalesOrderLineId).InventoryItemId))
        {
            var quantity = itemGroup.Sum(requested => RoundQuantity(requested.Quantity));
            if (quantity > locationBalances[itemGroup.Key].QuantityOnHand) return TransactionResult.Failure($"On-hand inventory for {items[itemGroup.Key].Sku} in {location.Value.Warehouse.Code}/{location.Value.Bin.Code} is lower than the shipment quantity.");
        }
        var totalCost = requestedLines.Sum(requested => RoundCurrency(RoundQuantity(requested.Quantity) * items[orderLines.Single(line => line.Id == requested.SalesOrderLineId).InventoryItemId].UnitCost));
        if (totalCost <= 0m) return TransactionResult.Failure("The shipment cost must be greater than zero.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var shipmentId = Guid.NewGuid();
        var posting = await PostAsync(db, companyId, request.ShippedOn, "Sales Fulfillment", shipmentNumber, $"Inventory shipment for sales order {order.OrderNumber}",
            [new(OperationalRoleReference(AccountingAccountRoles.CostOfGoodsSold), totalCost, 0m, "Cost of goods sold"), new(OperationalRoleReference(AccountingAccountRoles.InventoryAsset), 0m, totalCost, "Inventory shipped")],
            cancellationToken, allowControlAccounts: true, sourceDocumentId: shipmentId, sourceDocumentType: "InventoryShipment", resolveOperationalRoles: true);
        if (!posting.Succeeded) return posting;
        var shipment = new InventoryShipment { Id = shipmentId, CompanyId = companyId, SalesOrderId = order.Id, WarehouseId = location.Value.Warehouse.Id, BinId = location.Value.Bin.Id, ShipmentNumber = shipmentNumber, ShippedOn = request.ShippedOn, TotalCost = totalCost, JournalEntryId = posting.Id!.Value, ShippedByUserId = ResolveUserId(), ShippedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.InventoryShipments.Add(shipment);
        var sequence = 0;
        foreach (var requested in requestedLines.OrderBy(line => orderLines.Single(candidate => candidate.Id == line.SalesOrderLineId).Sequence))
        {
            var orderLine = orderLines.Single(candidate => candidate.Id == requested.SalesOrderLineId);
            var item = items[orderLine.InventoryItemId];
            var quantity = RoundQuantity(requested.Quantity);
            var cost = RoundCurrency(quantity * item.UnitCost);
            item.QuantityOnHand -= quantity;
            item.ConcurrencyToken = Guid.NewGuid().ToString("N");
            locationBalances[item.Id].QuantityOnHand -= quantity;
            locationBalances[item.Id].ConcurrencyToken = Guid.NewGuid().ToString("N");
            orderLine.AllocatedQuantity -= quantity;
            if (orderLine.AllocatedQuantity == 0m) { orderLine.AllocationWarehouseId = null; orderLine.AllocationBinId = null; }
            orderLine.ShippedQuantity += quantity;
            db.InventoryShipmentLines.Add(new InventoryShipmentLine { Id = Guid.NewGuid(), InventoryShipmentId = shipment.Id, SalesOrderLineId = orderLine.Id, InventoryItemId = item.Id, Sequence = ++sequence, Quantity = quantity, UnitCost = item.UnitCost, TotalCost = cost });
            db.InventoryTransactions.Add(new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, WarehouseId = location.Value.Warehouse.Id, BinId = location.Value.Bin.Id, OccurredOn = request.ShippedOn, TransactionType = "Customer shipment", QuantityChange = -quantity, UnitCost = item.UnitCost, TotalCost = -cost, Reference = shipmentNumber, JournalEntryId = posting.Id.Value });
        }
        UpdateSalesOrderFulfillmentStatus(order, orderLines);
        AddSalesFulfillmentAudit(db, companyId, "inventory-shipment.posted", nameof(InventoryShipment), shipment.Id, new { shipment.ShipmentNumber, order.Id, order.OrderNumber, warehouse = location.Value.Warehouse.Code, bin = location.Value.Bin.Code, shipment.TotalCost, lineCount = requestedLines.Length });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The sales order or inventory changed while the shipment was posting. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The shipment number or fulfillment quantities changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(shipment.Id);
    }

    public async Task<TransactionResult> InvoiceInventoryShipmentAsync(InvoiceInventoryShipmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReceivablesManage)) return TransactionResult.Failure("You are not authorized to invoice customer shipments.");
        if (string.IsNullOrWhiteSpace(request.InvoiceNumber) || string.IsNullOrWhiteSpace(request.Description) || request.DueDate < request.InvoiceDate)
            return TransactionResult.Failure("An invoice number, description, and valid invoice and due dates are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var shipment = await db.InventoryShipments.SingleOrDefaultAsync(candidate => candidate.Id == request.InventoryShipmentId && candidate.CompanyId == companyId, cancellationToken);
        if (shipment is null) return TransactionResult.Failure("Inventory shipment not found.");
        if (shipment.Status != "Posted" || shipment.SalesInvoiceId.HasValue) return TransactionResult.Failure("Only an uninvoiced posted shipment can be invoiced.");
        if (!string.Equals(shipment.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The shipment changed after it was opened. Refresh and review it again.");
        if (request.InvoiceDate < shipment.ShippedOn) return TransactionResult.Failure("The invoice date cannot precede the shipment date.");
        var invoiceNumber = request.InvoiceNumber.Trim();
        if (await db.SalesInvoices.AnyAsync(invoice => invoice.CompanyId == companyId && invoice.InvoiceNumber == invoiceNumber, cancellationToken)) return TransactionResult.Failure("Invoice number already exists.");
        var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == shipment.SalesOrderId && candidate.CompanyId == companyId, cancellationToken);
        var customer = await db.Customers.SingleAsync(candidate => candidate.Id == order.CustomerId && candidate.CompanyId == companyId, cancellationToken);
        var shipmentLines = await db.InventoryShipmentLines.Where(line => line.InventoryShipmentId == shipment.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        if (shipmentLines.Count == 0) return TransactionResult.Failure("The shipment has no lines to invoice.");
        var orderLineIds = shipmentLines.Select(line => line.SalesOrderLineId).ToArray();
        var orderLines = await db.SalesOrderLines.Where(line => orderLineIds.Contains(line.Id) && line.SalesOrderId == order.Id).ToDictionaryAsync(line => line.Id, cancellationToken);
        if (orderLines.Count != orderLineIds.Distinct().Count()) return TransactionResult.Failure("One or more shipment source lines are unavailable.");
        var priorInvoiceLines = await db.SalesInvoiceLines.Where(line => line.SalesOrderLineId.HasValue && orderLineIds.Contains(line.SalesOrderLineId.Value) && db.SalesInvoices.Any(invoice => invoice.Id == line.SalesInvoiceId && invoice.Status != "Voided")).ToListAsync(cancellationToken);
        var invoiceAmounts = shipmentLines.Select(line =>
        {
            var source = orderLines[line.SalesOrderLineId];
            var fulfilledQuantity = source.OrderedQuantity - source.CancelledQuantity;
            var isFinal = source.InvoicedQuantity + line.Quantity == fulfilledQuantity;
            var prior = priorInvoiceLines.Where(invoiceLine => invoiceLine.SalesOrderLineId == source.Id).ToArray();
            var fulfilledDiscount = RoundCurrency(source.DiscountAmount * fulfilledQuantity / source.OrderedQuantity);
            var fulfilledTax = RoundCurrency(source.TaxAmount * fulfilledQuantity / source.OrderedQuantity);
            var discount = isFinal ? fulfilledDiscount - prior.Sum(invoiceLine => invoiceLine.DiscountAmount) : RoundCurrency(source.DiscountAmount * line.Quantity / source.OrderedQuantity);
            var tax = isFinal ? fulfilledTax - prior.Sum(invoiceLine => invoiceLine.TaxAmount) : RoundCurrency(source.TaxAmount * line.Quantity / source.OrderedQuantity);
            var net = RoundCurrency(line.Quantity * source.UnitPrice - discount);
            return new { ShipmentLine = line, Source = source, Discount = discount, Tax = tax, Net = net, Total = net + tax };
        }).ToArray();
        var subtotal = invoiceAmounts.Sum(line => line.Net);
        var taxAmount = invoiceAmounts.Sum(line => line.Tax);
        var total = subtotal + taxAmount;
        if (total <= 0m) return TransactionResult.Failure("The shipment invoice total must be greater than zero.");
        if (customer.CreditLimit > 0m && customer.OpenBalance + total > customer.CreditLimit) return TransactionResult.Failure("Posting this shipment invoice would exceed the customer's credit limit.");
        var revenueAccountIds = invoiceAmounts.Select(line => line.Source.RevenueAccountId).Distinct().ToArray();
        var revenueAccountNumbers = await db.Accounts
            .Where(account => account.CompanyId == companyId && revenueAccountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => account.Number, cancellationToken);
        if (revenueAccountNumbers.Count != revenueAccountIds.Length) return TransactionResult.Failure("One or more shipment revenue accounts are unavailable in this company.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invoiceId = Guid.NewGuid();
        var journalLines = new List<JournalLineRequest> { new(OperationalRoleReference(AccountingAccountRoles.AccountsReceivable), total, 0m, "Shipment invoice receivable") };
        journalLines.AddRange(invoiceAmounts.GroupBy(line => line.Source.RevenueAccountId).Select(group => new JournalLineRequest(revenueAccountNumbers[group.Key], 0m, group.Sum(line => line.Net), "Shipment revenue")));
        if (taxAmount > 0m) journalLines.Add(new JournalLineRequest(OperationalRoleReference(AccountingAccountRoles.SalesTaxPayable), 0m, taxAmount, "Sales tax payable"));
        var posting = await PostAsync(db, companyId, request.InvoiceDate, "Accounts Receivable", invoiceNumber, request.Description.Trim(), journalLines, cancellationToken, allowControlAccounts: true, sourceDocumentId: invoiceId, sourceDocumentType: "SalesInvoice", resolveOperationalRoles: true);
        if (!posting.Succeeded) return posting;
        var invoice = new SalesInvoice { Id = invoiceId, CompanyId = companyId, CustomerId = customer.Id, SalesOrderId = order.Id, InventoryShipmentId = shipment.Id, InvoiceNumber = invoiceNumber, InvoiceDate = request.InvoiceDate, DueDate = request.DueDate, Status = "Open", Subtotal = subtotal, TaxAmount = taxAmount, TotalAmount = total, BalanceDue = total, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.SalesInvoices.Add(invoice);
        db.SalesInvoiceLines.AddRange(invoiceAmounts.Select((line, index) => new SalesInvoiceLine { Id = Guid.NewGuid(), SalesInvoiceId = invoice.Id, Sequence = index + 1, RevenueAccountId = line.Source.RevenueAccountId, SalesOrderLineId = line.Source.Id, InventoryShipmentLineId = line.ShipmentLine.Id, InventoryItemId = line.Source.InventoryItemId, Description = line.Source.Description, Quantity = line.ShipmentLine.Quantity, UnitPrice = line.Source.UnitPrice, DiscountAmount = line.Discount, TaxAmount = line.Tax, LineTotal = line.Total }));
        foreach (var line in invoiceAmounts) line.Source.InvoicedQuantity += line.ShipmentLine.Quantity;
        shipment.SalesInvoiceId = invoice.Id;
        shipment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        customer.OpenBalance += total;
        UpdateSalesOrderFulfillmentStatus(order, await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).ToListAsync(cancellationToken));
        AddSalesFulfillmentAudit(db, companyId, "inventory-shipment.invoiced", nameof(InventoryShipment), shipment.Id, new { shipment.ShipmentNumber, invoice.Id, invoice.InvoiceNumber, invoice.TotalAmount });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The shipment changed while it was being invoiced. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The shipment was invoiced concurrently or the invoice number already exists. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(invoice.Id);
    }

    public async Task<TransactionResult> ReverseInventoryShipmentAsync(ReverseInventoryShipmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.FulfillmentManage)) return TransactionResult.Failure("You are not authorized to reverse inventory shipments.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A shipment reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var shipment = await db.InventoryShipments.SingleOrDefaultAsync(candidate => candidate.Id == request.InventoryShipmentId && candidate.CompanyId == companyId, cancellationToken);
        if (shipment is null) return TransactionResult.Failure("Inventory shipment not found.");
        if (shipment.Status != "Posted" || shipment.ReversalJournalEntryId.HasValue) return TransactionResult.Failure("Only an unreversed posted shipment can be reversed.");
        if (shipment.SalesInvoiceId.HasValue) return TransactionResult.Failure("Void the shipment invoice before reversing the physical shipment.");
        if (!string.Equals(shipment.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The shipment changed after it was opened. Refresh and review it again.");
        if (request.ReversalDate < shipment.ShippedOn) return TransactionResult.Failure("The reversal date cannot precede the shipment date.");
        var shipmentLines = await db.InventoryShipmentLines.Where(line => line.InventoryShipmentId == shipment.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var location = await ResolveInventoryLocationAsync(db, companyId, shipment.WarehouseId, shipment.BinId, cancellationToken); if (location is null) return TransactionResult.Failure("The shipment warehouse or bin is unavailable. Reactivate it before reversing this shipment.");
        foreach (var line in shipmentLines)
        {
            var hasAmbiguousOrLaterMovement = await db.InventoryTransactions.AnyAsync(entry =>
                entry.CompanyId == companyId &&
                entry.InventoryItemId == line.InventoryItemId &&
                entry.JournalEntryId != shipment.JournalEntryId &&
                entry.OccurredOn >= shipment.ShippedOn,
                cancellationToken);
            if (hasAmbiguousOrLaterMovement) return TransactionResult.Failure("A same-day or later inventory valuation event exists for a shipped item. Post a compensating return or inventory adjustment instead of rewriting valuation history.");
        }
        var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == shipment.SalesOrderId && candidate.CompanyId == companyId, cancellationToken);
        var orderLines = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).ToDictionaryAsync(line => line.Id, cancellationToken);
        var activeInvoiceTotals = order.CancelledAtUtc.HasValue
            ? await LoadActiveSalesOrderInvoiceTotalsAsync(db, companyId, orderLines.Keys, cancellationToken)
            : null;
        var items = await db.InventoryItems.Where(item => item.CompanyId == companyId && shipmentLines.Select(line => line.InventoryItemId).Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (orderLines.Count(line => shipmentLines.Any(shipmentLine => shipmentLine.SalesOrderLineId == line.Key)) != shipmentLines.Count || items.Count != shipmentLines.Select(line => line.InventoryItemId).Distinct().Count()) return TransactionResult.Failure("Shipment source data is incomplete.");
        var locationBalances = new Dictionary<Guid, InventoryLocationBalance>();
        foreach (var itemId in shipmentLines.Select(line => line.InventoryItemId).Distinct()) locationBalances[itemId] = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, itemId, location.Value.Warehouse.Id, location.Value.Bin.Id, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reversal = await PostInverseAsync(db, companyId, shipment.JournalEntryId, request.ReversalDate, $"REV-{shipment.ShipmentNumber}", request.Reason.Trim(), shipment.Id, "InventoryShipmentReversal", null, cancellationToken, "Sales Fulfillment");
        if (!reversal.Succeeded) return reversal;
        foreach (var line in shipmentLines)
        {
            var item = items[line.InventoryItemId];
            var orderLine = orderLines[line.SalesOrderLineId];
            item.QuantityOnHand += line.Quantity;
            item.ConcurrencyToken = Guid.NewGuid().ToString("N");
            locationBalances[item.Id].QuantityOnHand += line.Quantity;
            locationBalances[item.Id].ConcurrencyToken = Guid.NewGuid().ToString("N");
            orderLine.ShippedQuantity -= line.Quantity;
            if (order.CancelledAtUtc.HasValue) orderLine.CancelledQuantity += line.Quantity;
            else { orderLine.AllocatedQuantity += line.Quantity; orderLine.AllocationWarehouseId = location.Value.Warehouse.Id; orderLine.AllocationBinId = location.Value.Bin.Id; }
            db.InventoryTransactions.Add(new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, WarehouseId = location.Value.Warehouse.Id, BinId = location.Value.Bin.Id, OccurredOn = request.ReversalDate, TransactionType = "Customer shipment reversal", QuantityChange = line.Quantity, UnitCost = line.UnitCost, TotalCost = line.TotalCost, Reference = $"REV-{shipment.ShipmentNumber}", JournalEntryId = reversal.Id!.Value });
        }
        if (order.CancelledAtUtc.HasValue) order.TotalAmount = CalculateRetainedSalesOrderTotal(orderLines.Values, activeInvoiceTotals);
        shipment.Status = "Reversed";
        shipment.ReversalJournalEntryId = reversal.Id;
        shipment.ReversedByUserId = ResolveUserId();
        shipment.ReversedAtUtc = DateTimeOffset.UtcNow;
        shipment.ReversalDate = request.ReversalDate;
        shipment.ReversalReason = request.Reason.Trim();
        shipment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        UpdateSalesOrderFulfillmentStatus(order, orderLines.Values.ToList());
        AddSalesFulfillmentAudit(db, companyId, "inventory-shipment.reversed", nameof(InventoryShipment), shipment.Id, new { shipment.ShipmentNumber, shipment.ReversalJournalEntryId, shipment.ReversalDate, shipment.ReversalReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The shipment or inventory changed while the reversal was posting. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(shipment.Id);
    }

    private static void UpdateSalesOrderFulfillmentStatus(SalesOrder order, IReadOnlyCollection<SalesOrderLine> lines)
    {
        var hasCancellation = lines.Any(line => line.CancelledQuantity > 0m);
        var fullyClosed = lines.All(line => line.ShippedQuantity + line.CancelledQuantity == line.OrderedQuantity);
        if (hasCancellation && lines.All(line => line.CancelledQuantity == line.OrderedQuantity))
            order.Status = "Cancelled";
        else if (hasCancellation && fullyClosed && lines.All(line => line.InvoicedQuantity == line.ShippedQuantity))
            order.Status = "Closed";
        else if (hasCancellation && fullyClosed)
            order.Status = "ClosedPendingInvoice";
        else if (lines.All(line => line.ShippedQuantity == line.OrderedQuantity && line.InvoicedQuantity == line.OrderedQuantity))
            order.Status = "Completed";
        else if (lines.All(line => line.ShippedQuantity == line.OrderedQuantity))
            order.Status = "Shipped";
        else if (lines.Any(line => line.ShippedQuantity > 0m))
            order.Status = "PartiallyShipped";
        else if (lines.Any(line => line.AllocatedQuantity > 0m))
            order.Status = "Allocated";
        else
            order.Status = "Approved";
        order.ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    private static decimal CalculateRetainedSalesOrderTotal(IEnumerable<SalesOrderLine> lines, IReadOnlyDictionary<Guid, decimal>? activeInvoiceTotals = null) => lines.Sum(line =>
    {
        if (activeInvoiceTotals is not null && line.InvoicedQuantity == line.ShippedQuantity)
            return activeInvoiceTotals.GetValueOrDefault(line.Id);
        var retainedQuantity = line.OrderedQuantity - line.CancelledQuantity;
        var retainedDiscount = RoundCurrency(line.DiscountAmount * retainedQuantity / line.OrderedQuantity);
        var retainedTax = RoundCurrency(line.TaxAmount * retainedQuantity / line.OrderedQuantity);
        return RoundCurrency(retainedQuantity * line.UnitPrice - retainedDiscount + retainedTax);
    });

    private static async Task<IReadOnlyDictionary<Guid, decimal>> LoadActiveSalesOrderInvoiceTotalsAsync(
        BrassLedgerDbContext db,
        Guid companyId,
        IEnumerable<Guid> salesOrderLineIds,
        CancellationToken cancellationToken)
    {
        var lineIds = salesOrderLineIds.Distinct().ToArray();
        var amounts = await (
            from line in db.SalesInvoiceLines
            join invoice in db.SalesInvoices on line.SalesInvoiceId equals invoice.Id
            where invoice.CompanyId == companyId
                && invoice.Status != "Voided"
                && line.SalesOrderLineId.HasValue
                && lineIds.Contains(line.SalesOrderLineId.Value)
            select new { SalesOrderLineId = line.SalesOrderLineId ?? Guid.Empty, line.LineTotal })
            .ToListAsync(cancellationToken);
        return amounts
            .GroupBy(amount => amount.SalesOrderLineId)
            .ToDictionary(group => group.Key, group => group.Sum(amount => amount.LineTotal));
    }

    private void AddSalesFulfillmentAudit(BrassLedgerDbContext db, Guid companyId, string action, string entityType, Guid entityId, object details) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = entityType, EntityId = entityId, DetailJson = JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow });
}
