using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<TransactionResult> AmendSalesOrderAsync(AmendSalesOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to amend sales orders.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A sales-order amendment reason is required.");
        var requestedLines = request.Lines?.ToArray() ?? [];
        if (requestedLines.Length == 0) return TransactionResult.Failure("At least one amended sales-order line is required.");
        if (request.RequestedShipOn.HasValue && request.RequestedShipOn.Value < request.OrderedOn) return TransactionResult.Failure("The requested ship date cannot precede the order date.");
        if (requestedLines.Any(line => line.InventoryItemId == Guid.Empty || string.IsNullOrWhiteSpace(line.Description) || RoundQuantity(line.Quantity) <= 0m || line.UnitPrice < 0m || line.DiscountAmount < 0m || line.DiscountAmount > RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice) || line.TaxAmount < 0m || string.IsNullOrWhiteSpace(line.RevenueAccountNumber)))
            return TransactionResult.Failure("Every amended line requires an item, description, positive quantity, valid price and discount, non-negative tax, and revenue account.");
        if (requestedLines.Select(line => line.InventoryItemId).Distinct().Count() != requestedLines.Length) return TransactionResult.Failure("Combine duplicate inventory items into one amended line.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var order = await db.SalesOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.SalesOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (order is null) return TransactionResult.Failure("Sales order not found.");
        if (order.Status is not ("Approved" or "Allocated")) return TransactionResult.Failure("Only an approved or allocated order with no shipment history can be amended.");
        if (order.SalesQuoteId.HasValue) return TransactionResult.Failure("Quote-derived order terms are immutable. Prepare and approve a replacement quote or a separate order.");
        if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The sales order changed after it was opened. Refresh and review it again.");
        var existingLines = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        if (existingLines.Any(line => line.ShippedQuantity != 0m || line.InvoicedQuantity != 0m || line.ReturnedQuantity != 0m || line.CancelledQuantity != 0m)) return TransactionResult.Failure("An order with shipment, invoice, return, or cancellation history cannot be amended. Use a compensating workflow.");
        if (await db.InventoryPicks.AnyAsync(pick => pick.SalesOrderId == order.Id, cancellationToken) || await db.SalesOrderBackorderPromises.AnyAsync(backorder => backorder.SalesOrderId == order.Id, cancellationToken)) return TransactionResult.Failure("An order with pick, pack, or backorder-promise history cannot be amended. Cancel the open demand or create a replacement order.");
        var itemIds = requestedLines.Select(line => line.InventoryItemId).ToArray();
        if (await db.InventoryItems.CountAsync(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id), cancellationToken) != itemIds.Length) return TransactionResult.Failure("Every amended item must be active in the current company.");
        var revenueNumbers = requestedLines.Select(line => line.RevenueAccountNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var revenueAccounts = await db.Accounts.Where(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Revenue && !account.IsControlAccount && revenueNumbers.Contains(account.Number)).ToDictionaryAsync(account => account.Number, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (revenueAccounts.Count != revenueNumbers.Length) return TransactionResult.Failure("Every amended distribution must use an active, non-control revenue account.");
        var amendedTotal = requestedLines.Sum(line => RoundCurrency(RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice) - RoundCurrency(line.DiscountAmount) + RoundCurrency(line.TaxAmount)));
        if (amendedTotal <= 0m) return TransactionResult.Failure("The amended sales-order total must be greater than zero.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var revisionNumber = await db.SalesOrderAmendments.CountAsync(amendment => amendment.SalesOrderId == order.Id, cancellationToken) + 1;
        var beforeTotal = order.TotalAmount;
        var beforeJson = JsonSerializer.Serialize(new
        {
            order.OrderedOn, order.RequestedShipOn, order.Notes, order.Status, order.TotalAmount,
            Lines = existingLines.Select(line => new { line.Sequence, line.InventoryItemId, line.RevenueAccountId, line.Description, Quantity = line.OrderedQuantity, line.AllocatedQuantity, line.UnitPrice, line.DiscountAmount, line.TaxAmount, line.LineTotal })
        });
        var amendedLines = requestedLines.Select((line, index) => new SalesOrderLine
        {
            Id = Guid.NewGuid(), SalesOrderId = order.Id, Sequence = index + 1, InventoryItemId = line.InventoryItemId,
            RevenueAccountId = revenueAccounts[line.RevenueAccountNumber.Trim()].Id, Description = line.Description.Trim(), OrderedQuantity = RoundQuantity(line.Quantity),
            UnitPrice = RoundCurrency(line.UnitPrice), DiscountAmount = RoundCurrency(line.DiscountAmount), TaxAmount = RoundCurrency(line.TaxAmount),
            LineTotal = RoundCurrency(RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice) - RoundCurrency(line.DiscountAmount) + RoundCurrency(line.TaxAmount))
        }).ToArray();
        var afterJson = JsonSerializer.Serialize(new
        {
            request.OrderedOn, request.RequestedShipOn, Notes = request.Notes?.Trim() ?? string.Empty, Status = "Draft", TotalAmount = amendedTotal,
            Lines = amendedLines.Select(line => new { line.Sequence, line.InventoryItemId, line.RevenueAccountId, line.Description, Quantity = line.OrderedQuantity, line.UnitPrice, line.DiscountAmount, line.TaxAmount, line.LineTotal })
        });
        db.SalesOrderAmendments.Add(new SalesOrderAmendment { Id = Guid.NewGuid(), CompanyId = companyId, SalesOrderId = order.Id, RevisionNumber = revisionNumber, Reason = request.Reason.Trim(), BeforeJson = beforeJson, AfterJson = afterJson, AmendedByUserId = ResolveUserId(), AmendedAtUtc = DateTimeOffset.UtcNow });
        db.SalesOrderLines.RemoveRange(existingLines);
        db.SalesOrderLines.AddRange(amendedLines);
        order.OrderedOn = request.OrderedOn;
        order.RequestedShipOn = request.RequestedShipOn;
        order.Notes = request.Notes?.Trim() ?? string.Empty;
        order.TotalAmount = amendedTotal;
        order.Status = "Draft";
        order.ApprovedByUserId = null;
        order.ApprovedAtUtc = null;
        order.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesFulfillmentAudit(db, companyId, "sales-order.amended", nameof(SalesOrder), order.Id, new { order.OrderNumber, revisionNumber, reason = request.Reason.Trim(), beforeTotal, afterTotal = amendedTotal, approvalRequired = true });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The sales order changed while it was being amended. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The sales-order amendment changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(order.Id);
    }

    public async Task<TransactionResult> CancelSalesOrderAsync(CancelSalesOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to cancel sales orders.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A sales-order cancellation reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var order = await db.SalesOrders.SingleOrDefaultAsync(candidate => candidate.Id == request.SalesOrderId && candidate.CompanyId == companyId, cancellationToken);
        if (order is null) return TransactionResult.Failure("Sales order not found.");
        if (order.Status is not ("Draft" or "Approved" or "Allocated" or "PartiallyShipped")) return TransactionResult.Failure("Only a draft, approved, allocated, or partially shipped order with open quantity can be cancelled.");
        if (!string.Equals(order.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The sales order changed after it was opened. Refresh and review it again.");
        var lines = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        if (lines.Count == 0) return TransactionResult.Failure("The sales order has no lines to cancel.");
        var cancellations = lines.Select(line => new { Line = line, Quantity = line.OrderedQuantity - line.ShippedQuantity - line.CancelledQuantity }).Where(item => item.Quantity > 0m).ToArray();
        if (cancellations.Length == 0) return TransactionResult.Failure("The sales order has no open quantity to cancel.");
        var activeInvoiceTotals = await LoadActiveSalesOrderInvoiceTotalsAsync(db, companyId, lines.Select(line => line.Id), cancellationToken);
        if (await db.InventoryPicks.AnyAsync(pick => pick.SalesOrderId == order.Id && pick.Status != "Cancelled" && pick.Status != "Shipped", cancellationToken) || await db.InventoryPackingSlips.AnyAsync(pack => pack.SalesOrderId == order.Id && pack.Status == "Packed", cancellationToken)) return TransactionResult.Failure("Cancel every active pick and packing slip before cancelling open order quantity.");
        var openBackorders = await db.SalesOrderBackorderPromises.Where(backorder => backorder.CompanyId == companyId && backorder.SalesOrderId == order.Id && backorder.Status != "Cancelled").ToListAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var beforeTotal = order.TotalAmount;
        foreach (var cancellation in cancellations)
        {
            cancellation.Line.CancelledQuantity += cancellation.Quantity;
            cancellation.Line.AllocatedQuantity = 0m;
            cancellation.Line.AllocationWarehouseId = null;
            cancellation.Line.AllocationBinId = null;
        }
        foreach (var backorder in openBackorders) { backorder.Status = "Cancelled"; backorder.CancelledByUserId = ResolveUserId(); backorder.CancelledAtUtc = DateTimeOffset.UtcNow; backorder.CancellationReason = request.Reason.Trim(); backorder.ConcurrencyToken = Guid.NewGuid().ToString("N"); }
        order.TotalAmount = CalculateRetainedSalesOrderTotal(lines, activeInvoiceTotals);
        order.CancelledByUserId = ResolveUserId();
        order.CancelledAtUtc = DateTimeOffset.UtcNow;
        order.CancellationReason = request.Reason.Trim();
        UpdateSalesOrderFulfillmentStatus(order, lines);
        AddSalesFulfillmentAudit(db, companyId, "sales-order.cancelled", nameof(SalesOrder), order.Id, new { order.OrderNumber, order.Status, order.CancellationReason, beforeTotal, order.TotalAmount, lines = cancellations.Select(item => new { item.Line.Sequence, item.Quantity }), cancelledBackorderIds = openBackorders.Select(backorder => backorder.Id) });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The sales order changed while it was being cancelled. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(order.Id);
    }
}
