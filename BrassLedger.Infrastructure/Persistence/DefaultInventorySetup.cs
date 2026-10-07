using BrassLedger.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Persistence;

internal static class DefaultInventorySetup
{
    public static async Task EnsureAsync(BrassLedgerDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var companies = await db.Companies.Select(company => company.Id).ToArrayAsync(cancellationToken);
        foreach (var companyId in companies)
        {
            var warehouses = await db.InventoryWarehouses.Where(warehouse => warehouse.CompanyId == companyId).ToListAsync(cancellationToken);
            if (warehouses.Count(warehouse => warehouse.IsDefault) > 1)
                throw new InvalidOperationException("Inventory setup has more than one default warehouse for a company. Correct the configuration before startup.");
            var warehouse = warehouses.SingleOrDefault(candidate => candidate.IsDefault)
                ?? warehouses.OrderBy(candidate => candidate.Code).FirstOrDefault()
                ?? new InventoryWarehouse { Id = Guid.NewGuid(), CompanyId = companyId, Code = "MAIN", Name = "Main warehouse", IsDefault = true, DefaultMarker = "DEFAULT", IsActive = true };
            if (db.Entry(warehouse).State == EntityState.Detached) db.InventoryWarehouses.Add(warehouse);
            if (!warehouse.IsDefault) warehouse.IsDefault = true;
            warehouse.DefaultMarker = "DEFAULT";
            warehouse.IsActive = true;
            if (string.IsNullOrWhiteSpace(warehouse.ConcurrencyToken)) warehouse.ConcurrencyToken = Guid.NewGuid().ToString("N");

            var bins = await db.InventoryBins.Where(bin => bin.CompanyId == companyId && bin.WarehouseId == warehouse.Id).ToListAsync(cancellationToken);
            if (bins.Count(bin => bin.IsDefault) > 1)
                throw new InvalidOperationException("Inventory setup has more than one default bin in a warehouse. Correct the configuration before startup.");
            var bin = bins.SingleOrDefault(candidate => candidate.IsDefault)
                ?? bins.OrderBy(candidate => candidate.Code).FirstOrDefault()
                ?? new InventoryBin { Id = Guid.NewGuid(), CompanyId = companyId, WarehouseId = warehouse.Id, Code = "STOCK", Name = "General stock", IsDefault = true, DefaultMarker = "DEFAULT", IsActive = true };
            if (db.Entry(bin).State == EntityState.Detached) db.InventoryBins.Add(bin);
            if (!bin.IsDefault) bin.IsDefault = true;
            bin.DefaultMarker = "DEFAULT";
            bin.IsActive = true;
            if (string.IsNullOrWhiteSpace(bin.ConcurrencyToken)) bin.ConcurrencyToken = Guid.NewGuid().ToString("N");

            var items = await db.InventoryItems.Where(item => item.CompanyId == companyId).ToListAsync(cancellationToken);
            var itemIds = items.Select(item => item.Id).ToArray();
            var balances = await db.InventoryLocationBalances.Where(balance => balance.CompanyId == companyId && itemIds.Contains(balance.InventoryItemId)).ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.ConcurrencyToken)) item.ConcurrencyToken = Guid.NewGuid().ToString("N");
                var itemBalances = balances.Where(balance => balance.InventoryItemId == item.Id).ToArray();
                if (itemBalances.Length == 0)
                {
                    db.InventoryLocationBalances.Add(new InventoryLocationBalance { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, WarehouseId = warehouse.Id, BinId = bin.Id, QuantityOnHand = item.QuantityOnHand });
                    continue;
                }
                if (itemBalances.Sum(balance => balance.QuantityOnHand) != item.QuantityOnHand)
                    throw new InvalidOperationException($"Location balances for inventory item {item.Sku} do not reconcile to company on-hand quantity. Startup refused to alter inventory silently.");
            }

            var legacyMovements = await db.InventoryTransactions.Where(movement => movement.CompanyId == companyId && (!movement.WarehouseId.HasValue || !movement.BinId.HasValue)).ToListAsync(cancellationToken);
            foreach (var movement in legacyMovements) { movement.WarehouseId = warehouse.Id; movement.BinId = bin.Id; }
            var legacyReceipts = await db.InventoryReceipts.Where(receipt => receipt.CompanyId == companyId && (!receipt.WarehouseId.HasValue || !receipt.BinId.HasValue)).ToListAsync(cancellationToken);
            foreach (var receipt in legacyReceipts) { receipt.WarehouseId = warehouse.Id; receipt.BinId = bin.Id; }
            var legacyShipments = await db.InventoryShipments.Where(shipment => shipment.CompanyId == companyId && (!shipment.WarehouseId.HasValue || !shipment.BinId.HasValue)).ToListAsync(cancellationToken);
            foreach (var shipment in legacyShipments) { shipment.WarehouseId = warehouse.Id; shipment.BinId = bin.Id; }
            var legacyAllocations = await (
                from line in db.SalesOrderLines
                join order in db.SalesOrders on line.SalesOrderId equals order.Id
                where order.CompanyId == companyId && line.AllocatedQuantity > 0m && (!line.AllocationWarehouseId.HasValue || !line.AllocationBinId.HasValue)
                select line).ToListAsync(cancellationToken);
            foreach (var line in legacyAllocations) { line.AllocationWarehouseId = warehouse.Id; line.AllocationBinId = bin.Id; }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
