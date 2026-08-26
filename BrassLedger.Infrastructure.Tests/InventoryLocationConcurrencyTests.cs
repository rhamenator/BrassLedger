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

            Guid pickRaceOrderId;
            using (var salesScope = provider.CreateScope())
            {
                SetCompanyContext(salesScope, companyId, BrassLedgerPermissions.SalesManage);
                var sales = salesScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
                var saved = await sales.SaveSalesOrderAsync(new(null, customerId, "SO-RACE-PICK", new DateOnly(2026, 8, 26), null, "Pick commitment race", [new SalesOrderLineRequest(itemId, "Concurrent pick", 2m, 1m, 0m, 0m, "4000")]));
                Assert.True(saved.Succeeded, saved.ErrorMessage); pickRaceOrderId = saved.Id!.Value;
                var factory = salesScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync(); var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == pickRaceOrderId);
                var approved = await sales.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken)); Assert.True(approved.Succeeded, approved.ErrorMessage);
            }
            Guid pickRaceLineId; string pickRaceOrderToken;
            using (var fulfillmentScope = provider.CreateScope())
            {
                SetCompanyContext(fulfillmentScope, companyId, BrassLedgerPermissions.FulfillmentManage); var fulfillment = fulfillmentScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = fulfillmentScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                pickRaceLineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == pickRaceOrderId).Select(line => line.Id).SingleAsync(); pickRaceOrderToken = await db.SalesOrders.Where(order => order.Id == pickRaceOrderId).Select(order => order.ConcurrencyToken).SingleAsync();
                var allocated = await fulfillment.AllocateSalesOrderAsync(new(pickRaceOrderId, [new AllocateSalesOrderLineRequest(pickRaceLineId, 2m)], pickRaceOrderToken, mainWarehouseId, mainBinId)); Assert.True(allocated.Succeeded, allocated.ErrorMessage);
                pickRaceOrderToken = await db.SalesOrders.Where(order => order.Id == pickRaceOrderId).Select(order => order.ConcurrencyToken).SingleAsync();
            }
            using var firstPickScope = provider.CreateScope(); using var secondPickScope = provider.CreateScope();
            var firstPickTask = Task.Run(async () => { SetCompanyContext(firstPickScope, companyId, BrassLedgerPermissions.FulfillmentManage); return await firstPickScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().CreateInventoryPickAsync(new(pickRaceOrderId, "PICK-RACE-A", new DateOnly(2026, 8, 26), [new CreateInventoryPickLineRequest(pickRaceLineId, 1.5m)], pickRaceOrderToken)); });
            var secondPickTask = Task.Run(async () => { SetCompanyContext(secondPickScope, companyId, BrassLedgerPermissions.FulfillmentManage); return await secondPickScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().CreateInventoryPickAsync(new(pickRaceOrderId, "PICK-RACE-B", new DateOnly(2026, 8, 26), [new CreateInventoryPickLineRequest(pickRaceLineId, 1.5m)], pickRaceOrderToken)); });
            var pickResults = await Task.WhenAll(firstPickTask, secondPickTask); Assert.Single(pickResults, result => result.Succeeded); Assert.Single(pickResults, result => !result.Succeeded);
            Guid winningPickId = pickResults.Single(result => result.Succeeded).Id!.Value; Guid winningPickLineId; string winningPickToken;
            using (var completionScope = provider.CreateScope())
            {
                SetCompanyContext(completionScope, companyId, BrassLedgerPermissions.FulfillmentManage); var factory = completionScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync(); winningPickLineId = await db.InventoryPickLines.Where(line => line.InventoryPickId == winningPickId).Select(line => line.Id).SingleAsync(); winningPickToken = await db.InventoryPicks.Where(pick => pick.Id == winningPickId).Select(pick => pick.ConcurrencyToken).SingleAsync();
                var completed = await completionScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().CompleteInventoryPickAsync(new(winningPickId, [new CompleteInventoryPickLineRequest(winningPickLineId, 1.5m)], winningPickToken)); Assert.True(completed.Succeeded, completed.ErrorMessage);
                winningPickToken = await db.InventoryPicks.Where(pick => pick.Id == winningPickId).Select(pick => pick.ConcurrencyToken).SingleAsync();
            }
            using var firstPackScope = provider.CreateScope(); using var secondPackScope = provider.CreateScope();
            var firstPackTask = Task.Run(async () => { SetCompanyContext(firstPackScope, companyId, BrassLedgerPermissions.FulfillmentManage); return await firstPackScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().PackInventoryPickAsync(new(winningPickId, "PACK-RACE-A", new DateOnly(2026, 8, 26), [new PackInventoryPickLineRequest(winningPickLineId, 1m)], winningPickToken)); });
            var secondPackTask = Task.Run(async () => { SetCompanyContext(secondPackScope, companyId, BrassLedgerPermissions.FulfillmentManage); return await secondPackScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().PackInventoryPickAsync(new(winningPickId, "PACK-RACE-B", new DateOnly(2026, 8, 26), [new PackInventoryPickLineRequest(winningPickLineId, 1m)], winningPickToken)); });
            var packResults = await Task.WhenAll(firstPackTask, secondPackTask); Assert.Single(packResults, result => result.Succeeded); Assert.Single(packResults, result => !result.Succeeded);
            await using var raceVerify = await verifyFactory.CreateDbContextAsync(); Assert.Equal(1.5m, await raceVerify.InventoryPickLines.Where(line => line.InventoryPickId == winningPickId).Select(line => line.PickedQuantity).SingleAsync()); Assert.Equal(1m, await raceVerify.InventoryPackingSlipLines.Where(line => line.InventoryPickLineId == winningPickLineId).SumAsync(line => line.Quantity));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString); await administration.OpenAsync();
            await using var drop = administration.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)"; await drop.ExecuteNonQueryAsync();
            try { Directory.Delete(contentRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [PostgresFact]
    public async Task PostgreSql_ConcurrentCustomerReturnAuthorizationsCannotOverreserveShipment()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_returns_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync();
            await using var create = administration.CreateCommand(); create.CommandText = $"CREATE DATABASE {quotedDatabase}"; await create.ExecuteNonQueryAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "BrassLedger.ReturnConcurrency.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = testBuilder.ConnectionString }).Build();
            var services = new ServiceCollection(); services.AddBrassLedgerInfrastructure(configuration, contentRoot, seedSampleData: true);
            using var provider = services.BuildServiceProvider(); await provider.InitializeBrassLedgerAsync();

            Guid companyId; Guid customerId; Guid itemId;
            using (var readScope = provider.CreateScope())
            {
                var factory = readScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                companyId = await db.Companies.Select(company => company.Id).SingleAsync(); customerId = await db.Customers.Where(customer => customer.CompanyId == companyId).Select(customer => customer.Id).FirstAsync(); itemId = await db.InventoryItems.Where(item => item.CompanyId == companyId && item.Sku == "RM-220").Select(item => item.Id).SingleAsync();
            }

            Guid orderId;
            using (var salesScope = provider.CreateScope())
            {
                SetCompanyContext(salesScope, companyId, BrassLedgerPermissions.SalesManage); var sales = salesScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
                var saved = await sales.SaveSalesOrderAsync(new(null, customerId, "SO-RACE-RETURN", new DateOnly(2026, 8, 26), null, "Return authorization race", [new SalesOrderLineRequest(itemId, "Concurrent return", 1m, 10m, 0m, 0m, "4000")])); Assert.True(saved.Succeeded, saved.ErrorMessage); orderId = saved.Id!.Value;
                var factory = salesScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync(); var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == orderId);
                var approved = await sales.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken)); Assert.True(approved.Succeeded, approved.ErrorMessage);
            }

            Guid orderLineId; string orderToken;
            using (var readScope = provider.CreateScope())
            {
                var factory = readScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync(); orderLineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == orderId).Select(line => line.Id).SingleAsync(); orderToken = await db.SalesOrders.Where(order => order.Id == orderId).Select(order => order.ConcurrencyToken).SingleAsync();
            }
            Guid shipmentId;
            using (var fulfillmentScope = provider.CreateScope())
            {
                SetCompanyContext(fulfillmentScope, companyId, BrassLedgerPermissions.FulfillmentManage); var fulfillment = fulfillmentScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
                var allocated = await fulfillment.AllocateSalesOrderAsync(new(orderId, [new AllocateSalesOrderLineRequest(orderLineId, 1m)], orderToken)); Assert.True(allocated.Succeeded, allocated.ErrorMessage);
                var factory = fulfillmentScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync(); orderToken = await db.SalesOrders.Where(order => order.Id == orderId).Select(order => order.ConcurrencyToken).SingleAsync();
                var shipped = await fulfillment.ShipSalesOrderAsync(new(orderId, "SHIP-RACE-RETURN", new DateOnly(2026, 8, 26), [new ShipSalesOrderLineRequest(orderLineId, 1m)], orderToken)); Assert.True(shipped.Succeeded, shipped.ErrorMessage); shipmentId = shipped.Id!.Value;
            }

            Guid shipmentLineId; string shipmentToken;
            using (var readScope = provider.CreateScope())
            {
                var factory = readScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync(); shipmentLineId = await db.InventoryShipmentLines.Where(line => line.InventoryShipmentId == shipmentId).Select(line => line.Id).SingleAsync(); shipmentToken = await db.InventoryShipments.Where(shipment => shipment.Id == shipmentId).Select(shipment => shipment.ConcurrencyToken).SingleAsync();
            }
            using var firstScope = provider.CreateScope(); using var secondScope = provider.CreateScope();
            var first = Task.Run(async () => { SetCompanyContext(firstScope, companyId, BrassLedgerPermissions.SalesManage); return await firstScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().AuthorizeCustomerReturnAsync(new(shipmentId, "RMA-RACE-A", new DateOnly(2026, 8, 26), "Concurrent authorization A", [new AuthorizeCustomerReturnLineRequest(shipmentLineId, .75m)], shipmentToken)); });
            var second = Task.Run(async () => { SetCompanyContext(secondScope, companyId, BrassLedgerPermissions.SalesManage); return await secondScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().AuthorizeCustomerReturnAsync(new(shipmentId, "RMA-RACE-B", new DateOnly(2026, 8, 26), "Concurrent authorization B", [new AuthorizeCustomerReturnLineRequest(shipmentLineId, .75m)], shipmentToken)); });
            var results = await Task.WhenAll(first, second); Assert.Single(results, result => result.Succeeded); Assert.Single(results, result => !result.Succeeded);

            using var verifyScope = provider.CreateScope(); var verifyFactory = verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var verify = await verifyFactory.CreateDbContextAsync();
            Assert.Equal(.75m, await (from line in verify.CustomerReturnAuthorizationLines join authorization in verify.CustomerReturnAuthorizations on line.CustomerReturnAuthorizationId equals authorization.Id where authorization.InventoryShipmentId == shipmentId && authorization.Status != "Cancelled" select line.AuthorizedQuantity).SumAsync());
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
