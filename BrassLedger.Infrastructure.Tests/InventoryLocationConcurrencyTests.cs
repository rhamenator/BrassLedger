using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BrassLedger.Infrastructure.Tests;

public sealed class InventoryLocationConcurrencyTests
{
    [PostgresFact]
    public async Task PostgreSql_ConcurrentTransfersCannotOversubscribeOrDesynchronizeLocations()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_inventory_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync();
            await using var create = administration.CreateCommand(); create.CommandText = $"CREATE DATABASE {quotedDatabase}"; await create.ExecuteNonQueryAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "BrassLedger.InventoryConcurrency.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = testBuilder.ConnectionString }).Build();
            var services = new ServiceCollection(); services.AddBrassLedgerInfrastructure(configuration, contentRoot, seedSampleData: true);
            using var provider = services.BuildServiceProvider(); await provider.InitializeBrassLedgerAsync();

            Guid companyId; Guid itemId; Guid mainWarehouseId; Guid mainBinId; decimal companyQuantity;
            using (var setupScope = provider.CreateScope())
            {
                var factory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
                await using var db = await factory.CreateDbContextAsync(); companyId = await db.Companies.Select(company => company.Id).SingleAsync();
                var item = await db.InventoryItems.SingleAsync(candidate => candidate.Sku == "RM-220"); itemId = item.Id; companyQuantity = item.QuantityOnHand;
                var main = await db.InventoryWarehouses.SingleAsync(warehouse => warehouse.CompanyId == companyId && warehouse.IsDefault); mainWarehouseId = main.Id;
                mainBinId = await db.InventoryBins.Where(bin => bin.WarehouseId == main.Id && bin.IsDefault).Select(bin => bin.Id).SingleAsync();
            }

            Guid destinationWarehouseId; Guid destinationBinId;
            using (var setupScope = provider.CreateScope())
            {
                SetCompanyContext(setupScope, companyId, BrassLedgerPermissions.PurchasingManage);
                var setup = setupScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
                var warehouse = await setup.SaveInventoryWarehouseAsync(new(null, "RACE", "Concurrency destination", "", "", "", "", "", "US", false, true));
                Assert.True(warehouse.Succeeded, warehouse.ErrorMessage); destinationWarehouseId = warehouse.Id!.Value;
                var factory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                destinationBinId = await db.InventoryBins.Where(bin => bin.WarehouseId == destinationWarehouseId && bin.IsDefault).Select(bin => bin.Id).SingleAsync();
            }

            var competingQuantity = decimal.Round(companyQuantity * .75m, 4, MidpointRounding.AwayFromZero);
            using var firstScope = provider.CreateScope(); using var secondScope = provider.CreateScope();
            var first = Task.Run(async () => { SetCompanyContext(firstScope, companyId, BrassLedgerPermissions.FulfillmentManage); return await firstScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().TransferInventoryAsync(new(itemId, mainWarehouseId, mainBinId, destinationWarehouseId, destinationBinId, competingQuantity, new DateOnly(2026, 8, 26), "XFER-RACE-A", "Concurrent transfer A")); });
            var second = Task.Run(async () => { SetCompanyContext(secondScope, companyId, BrassLedgerPermissions.FulfillmentManage); return await secondScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().TransferInventoryAsync(new(itemId, mainWarehouseId, mainBinId, destinationWarehouseId, destinationBinId, competingQuantity, new DateOnly(2026, 8, 26), "XFER-RACE-B", "Concurrent transfer B")); });
            var results = await Task.WhenAll(first, second);
            Assert.Single(results, result => result.Succeeded); Assert.Single(results, result => !result.Succeeded);

            using var verifyScope = provider.CreateScope(); var verifyFactory = verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var verify = await verifyFactory.CreateDbContextAsync();
            var balances = await verify.InventoryLocationBalances.Where(balance => balance.CompanyId == companyId && balance.InventoryItemId == itemId).Select(balance => balance.QuantityOnHand).ToListAsync();
            Assert.All(balances, balance => Assert.True(balance >= 0m)); Assert.Equal(companyQuantity, balances.Sum());
            Assert.Equal(companyQuantity, await verify.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync());
            var postedTransferId = results.Single(result => result.Succeeded).Id; Assert.Equal(2, await verify.InventoryTransactions.CountAsync(movement => movement.InventoryTransferId == postedTransferId && movement.JournalEntryId == null));

            var postedTransfer = await verify.InventoryTransfers.SingleAsync(transfer => transfer.Id == postedTransferId); var customerId = await verify.Customers.Where(customer => customer.CompanyId == companyId).Select(customer => customer.Id).FirstAsync();
            using (var reverseScope = provider.CreateScope())
            {
                SetCompanyContext(reverseScope, companyId, BrassLedgerPermissions.FulfillmentManage);
                var reversed = await reverseScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().ReverseInventoryTransferAsync(new(postedTransfer.Id, new DateOnly(2026, 8, 26), "Restore race-test stock", postedTransfer.ConcurrencyToken));
                Assert.True(reversed.Succeeded, reversed.ErrorMessage);
            }
            Guid salesOrderId;
            using (var salesScope = provider.CreateScope())
            {
                SetCompanyContext(salesScope, companyId, BrassLedgerPermissions.SalesManage);
                var sales = salesScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
                var saved = await sales.SaveSalesOrderAsync(new(null, customerId, "SO-RACE-ALLOC", new DateOnly(2026, 8, 26), null, "Allocation race", [new SalesOrderLineRequest(itemId, "Concurrent reservation", competingQuantity, 1m, 0m, 0m, "4000")]));
                Assert.True(saved.Succeeded, saved.ErrorMessage); salesOrderId = saved.Id!.Value;
                var factory = salesScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync(); var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == salesOrderId);
                var approved = await sales.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken)); Assert.True(approved.Succeeded, approved.ErrorMessage);
            }
            Guid salesOrderLineId; string salesOrderToken;
            using (var readScope = provider.CreateScope())
            {
                var factory = readScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                salesOrderLineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == salesOrderId).Select(line => line.Id).SingleAsync(); salesOrderToken = await db.SalesOrders.Where(order => order.Id == salesOrderId).Select(order => order.ConcurrencyToken).SingleAsync();
            }
            using var allocationScope = provider.CreateScope(); using var movementScope = provider.CreateScope();
            var allocationTask = Task.Run(async () => { SetCompanyContext(allocationScope, companyId, BrassLedgerPermissions.FulfillmentManage); return await allocationScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().AllocateSalesOrderAsync(new(salesOrderId, [new AllocateSalesOrderLineRequest(salesOrderLineId, competingQuantity)], salesOrderToken, mainWarehouseId, mainBinId)); });
            var movementTask = Task.Run(async () => { SetCompanyContext(movementScope, companyId, BrassLedgerPermissions.FulfillmentManage); return await movementScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().TransferInventoryAsync(new(itemId, mainWarehouseId, mainBinId, destinationWarehouseId, destinationBinId, competingQuantity, new DateOnly(2026, 8, 26), "XFER-RACE-ALLOCATION", "Compete with reservation")); });
            var competingResults = await Task.WhenAll(allocationTask, movementTask);
            Assert.Single(competingResults, result => result.Succeeded); Assert.Single(competingResults, result => !result.Succeeded);
            await verify.Entry(await verify.InventoryItems.SingleAsync(item => item.Id == itemId)).ReloadAsync();
            var finalBalances = await verify.InventoryLocationBalances.Where(balance => balance.CompanyId == companyId && balance.InventoryItemId == itemId).AsNoTracking().Select(balance => balance.QuantityOnHand).ToListAsync();
            Assert.All(finalBalances, balance => Assert.True(balance >= 0m)); Assert.Equal(companyQuantity, finalBalances.Sum());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString); await administration.OpenAsync();
            await using var drop = administration.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)"; await drop.ExecuteNonQueryAsync();
            try { Directory.Delete(contentRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void SetCompanyContext(IServiceScope scope, Guid companyId, string permission)
    {
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)
            ], "test"))
        };
    }
}
