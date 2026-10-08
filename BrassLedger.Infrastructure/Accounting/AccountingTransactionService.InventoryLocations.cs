using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<TransactionResult> SaveInventoryWarehouseAsync(SaveInventoryWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to configure warehouses.");
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var countryCode = request.CountryCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length is < 1 or > 30 || name.Length is < 1 or > 200 || countryCode.Length != 2)
            return TransactionResult.Failure("Warehouse code, name, and a two-letter country code are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var warehouse = request.Id.HasValue
            ? await db.InventoryWarehouses.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == request.Id.Value, cancellationToken)
            : null;
        if (request.Id.HasValue && warehouse is null) return TransactionResult.Failure("Warehouse not found.");
        if (warehouse is not null && !string.Equals(warehouse.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The warehouse changed after it was opened. Refresh and review it again.");
        if (await db.InventoryWarehouses.AnyAsync(candidate => candidate.CompanyId == companyId && candidate.Code == code && candidate.Id != request.Id, cancellationToken)) return TransactionResult.Failure("Warehouse code already exists.");
        if (warehouse?.IsDefault == true && !request.IsDefault) return TransactionResult.Failure("Assign another default warehouse before removing the current default designation.");
        if (!request.IsActive && request.IsDefault) return TransactionResult.Failure("The default warehouse must remain active.");
        if (warehouse is not null && !request.IsActive && (await db.InventoryLocationBalances.AnyAsync(balance => balance.WarehouseId == warehouse.Id && balance.QuantityOnHand != 0m, cancellationToken) || await db.SalesOrderLines.AnyAsync(line => line.AllocationWarehouseId == warehouse.Id && line.AllocatedQuantity != 0m, cancellationToken)))
            return TransactionResult.Failure("A warehouse with stock or reservations cannot be deactivated.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        warehouse ??= new InventoryWarehouse { Id = Guid.NewGuid(), CompanyId = companyId };
        warehouse.Code = code; warehouse.Name = name; warehouse.AddressLine1 = request.AddressLine1?.Trim() ?? string.Empty; warehouse.AddressLine2 = request.AddressLine2?.Trim() ?? string.Empty; warehouse.City = request.City?.Trim() ?? string.Empty; warehouse.StateOrProvince = request.StateOrProvince?.Trim() ?? string.Empty; warehouse.PostalCode = request.PostalCode?.Trim() ?? string.Empty; warehouse.CountryCode = countryCode; warehouse.IsDefault = request.IsDefault; warehouse.DefaultMarker = request.IsDefault ? "DEFAULT" : null; warehouse.IsActive = request.IsActive; warehouse.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(warehouse).State == EntityState.Detached)
        {
            db.InventoryWarehouses.Add(warehouse);
            db.InventoryBins.Add(new InventoryBin { Id = Guid.NewGuid(), CompanyId = companyId, WarehouseId = warehouse.Id, Code = "STOCK", Name = "General stock", IsDefault = true, DefaultMarker = "DEFAULT", IsActive = true });
        }
        if (request.IsDefault)
        {
            var previousDefaults = await db.InventoryWarehouses.Where(candidate => candidate.CompanyId == companyId && candidate.Id != warehouse.Id && candidate.IsDefault).ToListAsync(cancellationToken);
            foreach (var previous in previousDefaults) { previous.IsDefault = false; previous.DefaultMarker = null; previous.ConcurrencyToken = Guid.NewGuid().ToString("N"); }
        }
        AddSalesFulfillmentAudit(db, companyId, "inventory-warehouse.saved", nameof(InventoryWarehouse), warehouse.Id, new { warehouse.Code, warehouse.Name, warehouse.IsDefault, warehouse.IsActive });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The warehouse changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The warehouse code or default configuration changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(warehouse.Id);
    }

    public async Task<TransactionResult> SaveInventoryBinAsync(SaveInventoryBinRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)) return TransactionResult.Failure("You are not authorized to configure inventory bins.");
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty; var name = request.Name?.Trim() ?? string.Empty;
        if (request.WarehouseId == Guid.Empty || code.Length is < 1 or > 30 || name.Length is < 1 or > 200) return TransactionResult.Failure("An active warehouse, bin code, and bin name are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var warehouse = await db.InventoryWarehouses.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == request.WarehouseId && candidate.IsActive, cancellationToken); if (warehouse is null) return TransactionResult.Failure("Active warehouse not found.");
        var bin = request.Id.HasValue ? await db.InventoryBins.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == request.Id.Value, cancellationToken) : null;
        if (request.Id.HasValue && bin is null) return TransactionResult.Failure("Inventory bin not found.");
        if (bin is not null && bin.WarehouseId != warehouse.Id) return TransactionResult.Failure("An inventory bin cannot be moved to another warehouse. Create a destination bin and transfer its stock.");
        if (bin is not null && !string.Equals(bin.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The inventory bin changed after it was opened. Refresh and review it again.");
        if (await db.InventoryBins.AnyAsync(candidate => candidate.WarehouseId == warehouse.Id && candidate.Code == code && candidate.Id != request.Id, cancellationToken)) return TransactionResult.Failure("Bin code already exists in this warehouse.");
        if (bin?.IsDefault == true && !request.IsDefault) return TransactionResult.Failure("Assign another default bin before removing the current default designation.");
        if (!request.IsActive && request.IsDefault) return TransactionResult.Failure("The default bin must remain active.");
        if (bin is not null && !request.IsActive && (await db.InventoryLocationBalances.AnyAsync(balance => balance.BinId == bin.Id && balance.QuantityOnHand != 0m, cancellationToken) || await db.SalesOrderLines.AnyAsync(line => line.AllocationBinId == bin.Id && line.AllocatedQuantity != 0m, cancellationToken))) return TransactionResult.Failure("A bin with stock or reservations cannot be deactivated.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        bin ??= new InventoryBin { Id = Guid.NewGuid(), CompanyId = companyId, WarehouseId = warehouse.Id };
        bin.Code = code; bin.Name = name; bin.IsDefault = request.IsDefault; bin.DefaultMarker = request.IsDefault ? "DEFAULT" : null; bin.IsActive = request.IsActive; bin.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(bin).State == EntityState.Detached) db.InventoryBins.Add(bin);
        if (request.IsDefault)
        {
            var previousDefaults = await db.InventoryBins.Where(candidate => candidate.WarehouseId == warehouse.Id && candidate.Id != bin.Id && candidate.IsDefault).ToListAsync(cancellationToken);
            foreach (var previous in previousDefaults) { previous.IsDefault = false; previous.DefaultMarker = null; previous.ConcurrencyToken = Guid.NewGuid().ToString("N"); }
        }
        AddSalesFulfillmentAudit(db, companyId, "inventory-bin.saved", nameof(InventoryBin), bin.Id, new { warehouseCode = warehouse.Code, binCode = bin.Code, bin.Name, bin.IsDefault, bin.IsActive });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The inventory bin changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The bin code or default configuration changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(bin.Id);
    }

    public async Task<TransactionResult> TransferInventoryAsync(TransferInventoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.FulfillmentManage)) return TransactionResult.Failure("You are not authorized to transfer inventory.");
        var quantity = RoundQuantity(request.Quantity);
        if (request.InventoryItemId == Guid.Empty || quantity <= 0m || request.SourceWarehouseId == request.DestinationWarehouseId && request.SourceBinId == request.DestinationBinId || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("Provide an item, distinct source and destination bins, positive quantity, reference, and reason.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var item = await db.InventoryItems.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == request.InventoryItemId && candidate.IsActive, cancellationToken); if (item is null) return TransactionResult.Failure("Active inventory item not found.");
        var source = await ResolveInventoryLocationAsync(db, companyId, request.SourceWarehouseId, request.SourceBinId, cancellationToken); if (source is null) return TransactionResult.Failure("Active source warehouse and bin not found.");
        var destination = await ResolveInventoryLocationAsync(db, companyId, request.DestinationWarehouseId, request.DestinationBinId, cancellationToken); if (destination is null) return TransactionResult.Failure("Active destination warehouse and bin not found.");
        var reference = request.Reference.Trim(); if (await db.InventoryTransfers.AnyAsync(transfer => transfer.CompanyId == companyId && transfer.Reference == reference, cancellationToken)) return TransactionResult.Failure("Inventory transfer reference already exists.");
        var sourceBalance = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, item.Id, source.Value.Warehouse.Id, source.Value.Bin.Id, cancellationToken);
        var destinationBalance = await GetOrCreateInventoryLocationBalanceAsync(db, companyId, item.Id, destination.Value.Warehouse.Id, destination.Value.Bin.Id, cancellationToken);
        var reserved = await db.SalesOrderLines.Where(line => line.AllocationWarehouseId == source.Value.Warehouse.Id && line.AllocationBinId == source.Value.Bin.Id && line.InventoryItemId == item.Id).Select(line => line.AllocatedQuantity).ToListAsync(cancellationToken);
        if (quantity > sourceBalance.QuantityOnHand - reserved.Sum()) return TransactionResult.Failure("Transfer quantity exceeds unreserved stock in the source bin.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var transfer = new InventoryTransfer { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, SourceWarehouseId = source.Value.Warehouse.Id, SourceBinId = source.Value.Bin.Id, DestinationWarehouseId = destination.Value.Warehouse.Id, DestinationBinId = destination.Value.Bin.Id, Quantity = quantity, UnitCost = item.UnitCost, TransferDate = request.TransferDate, Reference = reference, Reason = request.Reason.Trim(), TransferredByUserId = ResolveUserId(), TransferredAtUtc = DateTimeOffset.UtcNow };
        sourceBalance.QuantityOnHand -= quantity; sourceBalance.ConcurrencyToken = Guid.NewGuid().ToString("N"); destinationBalance.QuantityOnHand += quantity; destinationBalance.ConcurrencyToken = Guid.NewGuid().ToString("N"); db.InventoryTransfers.Add(transfer);
        db.InventoryTransactions.AddRange(
            new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, WarehouseId = source.Value.Warehouse.Id, BinId = source.Value.Bin.Id, InventoryTransferId = transfer.Id, OccurredOn = request.TransferDate, TransactionType = "Stock transfer out", QuantityChange = -quantity, UnitCost = item.UnitCost, TotalCost = -RoundCurrency(quantity * item.UnitCost), Reference = reference },
            new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, WarehouseId = destination.Value.Warehouse.Id, BinId = destination.Value.Bin.Id, InventoryTransferId = transfer.Id, OccurredOn = request.TransferDate, TransactionType = "Stock transfer in", QuantityChange = quantity, UnitCost = item.UnitCost, TotalCost = RoundCurrency(quantity * item.UnitCost), Reference = reference });
        AddSalesFulfillmentAudit(db, companyId, "inventory-transfer.posted", nameof(InventoryTransfer), transfer.Id, new { transfer.Reference, item.Sku, transfer.Quantity, sourceWarehouse = source.Value.Warehouse.Code, sourceBin = source.Value.Bin.Code, destinationWarehouse = destination.Value.Warehouse.Code, destinationBin = destination.Value.Bin.Code });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("A location balance changed while the transfer was posting. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The transfer reference or location balance changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(transfer.Id);
    }

    public async Task<TransactionResult> ReverseInventoryTransferAsync(ReverseInventoryTransferRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.FulfillmentManage)) return TransactionResult.Failure("You are not authorized to reverse inventory transfers.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A transfer reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var transfer = await db.InventoryTransfers.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == request.InventoryTransferId, cancellationToken); if (transfer is null) return TransactionResult.Failure("Inventory transfer not found.");
        if (transfer.Status != "Posted") return TransactionResult.Failure("Only a posted inventory transfer can be reversed.");
        if (!string.Equals(transfer.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The inventory transfer changed after it was opened. Refresh and review it again.");
        if (request.ReversalDate < transfer.TransferDate) return TransactionResult.Failure("The reversal date cannot precede the transfer date.");
        var sourceBalance = await db.InventoryLocationBalances.SingleAsync(balance => balance.CompanyId == companyId && balance.InventoryItemId == transfer.InventoryItemId && balance.BinId == transfer.SourceBinId, cancellationToken);
        var destinationBalance = await db.InventoryLocationBalances.SingleAsync(balance => balance.CompanyId == companyId && balance.InventoryItemId == transfer.InventoryItemId && balance.BinId == transfer.DestinationBinId, cancellationToken);
        var destinationReserved = await db.SalesOrderLines.Where(line => line.AllocationBinId == transfer.DestinationBinId && line.InventoryItemId == transfer.InventoryItemId).Select(line => line.AllocatedQuantity).ToListAsync(cancellationToken);
        if (transfer.Quantity > destinationBalance.QuantityOnHand - destinationReserved.Sum()) return TransactionResult.Failure("The destination bin no longer has enough unreserved stock to reverse this transfer.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        sourceBalance.QuantityOnHand += transfer.Quantity; sourceBalance.ConcurrencyToken = Guid.NewGuid().ToString("N"); destinationBalance.QuantityOnHand -= transfer.Quantity; destinationBalance.ConcurrencyToken = Guid.NewGuid().ToString("N"); transfer.Status = "Reversed"; transfer.ReversedByUserId = ResolveUserId(); transfer.ReversedAtUtc = DateTimeOffset.UtcNow; transfer.ReversalDate = request.ReversalDate; transfer.ReversalReason = request.Reason.Trim(); transfer.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.InventoryTransactions.AddRange(
            new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = transfer.InventoryItemId, WarehouseId = transfer.DestinationWarehouseId, BinId = transfer.DestinationBinId, InventoryTransferId = transfer.Id, OccurredOn = request.ReversalDate, TransactionType = "Stock transfer reversal out", QuantityChange = -transfer.Quantity, UnitCost = transfer.UnitCost, TotalCost = -RoundCurrency(transfer.Quantity * transfer.UnitCost), Reference = $"REV-{transfer.Reference}" },
            new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = transfer.InventoryItemId, WarehouseId = transfer.SourceWarehouseId, BinId = transfer.SourceBinId, InventoryTransferId = transfer.Id, OccurredOn = request.ReversalDate, TransactionType = "Stock transfer reversal in", QuantityChange = transfer.Quantity, UnitCost = transfer.UnitCost, TotalCost = RoundCurrency(transfer.Quantity * transfer.UnitCost), Reference = $"REV-{transfer.Reference}" });
        AddSalesFulfillmentAudit(db, companyId, "inventory-transfer.reversed", nameof(InventoryTransfer), transfer.Id, new { transfer.Reference, transfer.ReversalDate, transfer.ReversalReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("A transfer or location balance changed while the reversal was posting. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(transfer.Id);
    }

    private static async Task<(InventoryWarehouse Warehouse, InventoryBin Bin)?> ResolveInventoryLocationAsync(BrassLedgerDbContext db, Guid companyId, Guid? warehouseId, Guid? binId, CancellationToken cancellationToken)
    {
        InventoryBin? bin = null;
        InventoryWarehouse? warehouse = null;
        if (binId.HasValue) bin = await db.InventoryBins.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == binId.Value && candidate.IsActive, cancellationToken);
        if (bin is not null) warehouseId = bin.WarehouseId;
        if (warehouseId.HasValue) warehouse = await db.InventoryWarehouses.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == warehouseId.Value && candidate.IsActive, cancellationToken);
        else warehouse = await db.InventoryWarehouses.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.IsDefault && candidate.IsActive, cancellationToken);
        if (warehouse is null) return null;
        if (bin is null) bin = await db.InventoryBins.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.WarehouseId == warehouse.Id && candidate.IsDefault && candidate.IsActive, cancellationToken);
        if (bin is null || bin.WarehouseId != warehouse.Id) return null;
        return (warehouse, bin);
    }

    private static async Task<InventoryLocationBalance> GetOrCreateInventoryLocationBalanceAsync(BrassLedgerDbContext db, Guid companyId, Guid itemId, Guid warehouseId, Guid binId, CancellationToken cancellationToken)
    {
        var balance = await db.InventoryLocationBalances.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.InventoryItemId == itemId && candidate.BinId == binId, cancellationToken);
        if (balance is not null) return balance;
        balance = new InventoryLocationBalance { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = itemId, WarehouseId = warehouseId, BinId = binId, QuantityOnHand = 0m };
        db.InventoryLocationBalances.Add(balance);
        return balance;
    }
}
