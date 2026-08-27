using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BrassLedger.Infrastructure.Tests;

public sealed class ProjectWipPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_ConcurrentWipPreparationReservesOneCumulativeStartingPoint()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_project_wip_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync(); await using var create = administration.CreateCommand(); create.CommandText = $"CREATE DATABASE {quotedDatabase}"; await create.ExecuteNonQueryAsync();
        }
        var contentRoot = Path.Combine("/home/rich/temp", "BrassLedger.ProjectWip.Postgres.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = testBuilder.ConnectionString }).Build();
            var services = new ServiceCollection(); services.AddBrassLedgerInfrastructure(configuration, contentRoot, seedSampleData: true);
            using var provider = services.BuildServiceProvider(); await provider.InitializeBrassLedgerAsync();
            Guid companyId; Guid projectId;
            using (var setupScope = provider.CreateScope())
            {
                var factory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                companyId = await db.Companies.Select(x => x.Id).SingleAsync(); var customerId = await db.Customers.OrderBy(x => x.CustomerNumber).Select(x => x.Id).FirstAsync();
                var project = new ProjectJob { Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = customerId, JobNumber = "JOB-PG-WIP", Name = "PostgreSQL WIP race", CustomerName = "Customer", Status = "Active", StartDate = new DateOnly(2026, 8, 1), BillingMethod = "FixedPrice", RevenueRecognitionMethod = "ManualPercent", ContractAmount = 10_000m, BudgetAmount = 6_000m, CreatedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") }; projectId = project.Id;
                db.ProjectJobs.Add(project); await db.SaveChangesAsync();
            }
            using var firstScope = provider.CreateScope(); using var secondScope = provider.CreateScope();
            SetContext(firstScope, companyId, Guid.NewGuid()); SetContext(secondScope, companyId, Guid.NewGuid());
            var firstService = firstScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var secondService = secondScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
            var request = new ProjectWipPreviewRequest(projectId, new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), "4000", "Concurrent WIP preparation", 0.5m);
            var firstPreview = await firstService.PreviewProjectWipScheduleAsync(request); var secondPreview = await secondService.PreviewProjectWipScheduleAsync(request);
            Assert.True(firstPreview.Succeeded, firstPreview.ErrorMessage); Assert.True(secondPreview.Succeeded, secondPreview.ErrorMessage); Assert.Equal(firstPreview.ProjectConcurrencyToken, secondPreview.ProjectConcurrencyToken);
            var attempts = await Task.WhenAll(firstService.SaveProjectWipScheduleAsync(new(null, request, firstPreview.Fingerprint, firstPreview.ProjectConcurrencyToken)), secondService.SaveProjectWipScheduleAsync(new(null, request, secondPreview.Fingerprint, secondPreview.ProjectConcurrencyToken)));
            Assert.Single(attempts, result => result.Succeeded); Assert.Single(attempts, result => !result.Succeeded);
            using var verificationScope = provider.CreateScope(); var verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var verification = await verificationFactory.CreateDbContextAsync();
            Assert.Equal(1, await verification.ProjectWipSchedules.CountAsync(x => x.ProjectJobId == projectId));
            Assert.Equal(5_000m, await verification.ProjectWipSchedules.Where(x => x.ProjectJobId == projectId).Select(x => x.EarnedRevenueToDate).SingleAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools(); await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString); await administration.OpenAsync(); await using var drop = administration.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)"; await drop.ExecuteNonQueryAsync();
            try { Directory.Delete(contentRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void SetContext(IServiceScope scope, Guid companyId, Guid userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()), new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectWipPrepare) };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
    }
}
