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

public sealed class ProjectChangeOrderPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_ConcurrentChangeOrderApprovalAppliesAuthorizedTotalsExactlyOnce()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_project_change_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync();
            await using var create = administration.CreateCommand(); create.CommandText = $"CREATE DATABASE {quotedDatabase}"; await create.ExecuteNonQueryAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "BrassLedger.ProjectChangeOrder.Postgres.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = testBuilder.ConnectionString }).Build();
            var services = new ServiceCollection(); services.AddBrassLedgerInfrastructure(configuration, contentRoot, seedSampleData: true);
            using var provider = services.BuildServiceProvider(); await provider.InitializeBrassLedgerAsync();
            Guid companyId; Guid projectId; decimal contractBefore; decimal budgetBefore;
            using (var readScope = provider.CreateScope())
            {
                var factory = readScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                companyId = await db.Companies.Select(company => company.Id).SingleAsync();
                var project = await db.ProjectJobs.Where(candidate => candidate.Status == "Active").FirstAsync(); projectId = project.Id; contractBefore = project.ContractAmount; budgetBefore = project.BudgetAmount;
            }

            Guid changeOrderId; string submittedToken;
            using (var preparationScope = provider.CreateScope())
            {
                SetContext(preparationScope, companyId, Guid.NewGuid(), BrassLedgerPermissions.ProjectChangeOrderPrepare);
                var transactions = preparationScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
                var saved = await transactions.SaveProjectChangeOrderDraftAsync(new(null, projectId, "CO-PG-RACE", "Concurrent approval", "Prove exactly-once project authorization", new DateOnly(2026, 8, 26), new DateOnly(2026, 9, 1), 1_000m, 600m));
                Assert.True(saved.Succeeded, saved.ErrorMessage); changeOrderId = saved.Id!.Value;
                var factory = preparationScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                var draftToken = await db.ProjectChangeOrders.Where(change => change.Id == changeOrderId).Select(change => change.ConcurrencyToken).SingleAsync();
                Assert.True((await transactions.SubmitProjectChangeOrderAsync(new(changeOrderId, draftToken))).Succeeded);
                submittedToken = await db.ProjectChangeOrders.Where(change => change.Id == changeOrderId).Select(change => change.ConcurrencyToken).SingleAsync();
            }

            using var firstScope = provider.CreateScope(); using var secondScope = provider.CreateScope();
            SetContext(firstScope, companyId, Guid.NewGuid(), BrassLedgerPermissions.ProjectChangeOrderApprove);
            SetContext(secondScope, companyId, Guid.NewGuid(), BrassLedgerPermissions.ProjectChangeOrderApprove);
            var attempts = await Task.WhenAll(
                firstScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().DecideProjectChangeOrderAsync(new(changeOrderId, true, "First independent approval", submittedToken)),
                secondScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().DecideProjectChangeOrderAsync(new(changeOrderId, true, "Competing independent approval", submittedToken)));
            Assert.Single(attempts, result => result.Succeeded);
            Assert.Single(attempts, result => !result.Succeeded);

            using var verificationScope = provider.CreateScope(); var verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var verification = await verificationFactory.CreateDbContextAsync();
            var finalProject = await verification.ProjectJobs.SingleAsync(project => project.Id == projectId);
            var finalChange = await verification.ProjectChangeOrders.SingleAsync(change => change.Id == changeOrderId);
            Assert.Equal(contractBefore + 1_000m, finalProject.ContractAmount);
            Assert.Equal(budgetBefore + 600m, finalProject.BudgetAmount);
            Assert.Equal("Approved", finalChange.Status);
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry => entry.EntityId == changeOrderId && entry.Action == "project-change-order.approved"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString); await administration.OpenAsync();
            await using var drop = administration.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)"; await drop.ExecuteNonQueryAsync();
            try { Directory.Delete(contentRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void SetContext(IServiceScope scope, Guid companyId, Guid userId, string permission)
    {
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)
            ], "test"))
        };
    }
}
