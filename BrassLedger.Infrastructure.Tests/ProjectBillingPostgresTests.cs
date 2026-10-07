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

public sealed class ProjectBillingPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_ConcurrentBillingOfSameSourceCreatesExactlyOneControlledDraft()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_project_billing_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync(); await using var create = administration.CreateCommand(); create.CommandText = $"CREATE DATABASE {quotedDatabase}"; await create.ExecuteNonQueryAsync();
        }
        var contentRoot = Path.Combine("/home/rich/temp", "BrassLedger.ProjectBilling.Postgres.Tests", Guid.NewGuid().ToString("N"));
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
                companyId = await db.Companies.Select(x => x.Id).SingleAsync(); var customerId = await db.Customers.OrderBy(x => x.CustomerNumber).Select(x => x.Id).FirstAsync(); var employeeId = await db.Employees.OrderBy(x => x.EmployeeNumber).Select(x => x.Id).FirstAsync();
                var project = new ProjectJob { Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = customerId, JobNumber = "JOB-PG-BILL", Name = "PostgreSQL billing race", CustomerName = "Customer", Status = "Active", StartDate = new DateOnly(2026, 8, 1), BillingMethod = "TimeAndMaterials", ContractAmount = 10_000m, BudgetAmount = 5_000m, CreatedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") }; projectId = project.Id;
                var card = new PayrollTimecard { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = employeeId, PeriodStart = new DateOnly(2026, 8, 17), PeriodEnd = new DateOnly(2026, 8, 23), Status = "Approved", PreparedAtUtc = DateTimeOffset.UtcNow, ApprovedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
                db.ProjectJobs.Add(project); db.ProjectBillingRates.Add(new ProjectBillingRate { Id = Guid.NewGuid(), CompanyId = companyId, ProjectJobId = project.Id, EarningCode = "*", HourlyRate = 100m, EffectiveOn = new DateOnly(2026, 8, 1), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") }); db.PayrollTimecards.Add(card);
                db.PayrollTimeEntries.Add(new PayrollTimeEntry { Id = Guid.NewGuid(), PayrollTimecardId = card.Id, Sequence = 1, WorkDate = new DateOnly(2026, 8, 20), EarningCode = "REGULAR", EarningType = "Regular", Hours = 2m, Rate = 30m, Amount = 60m, ProjectJobId = project.Id }); await db.SaveChangesAsync();
            }

            using var firstScope = provider.CreateScope(); using var secondScope = provider.CreateScope();
            SetContext(firstScope, companyId, Guid.NewGuid()); SetContext(secondScope, companyId, Guid.NewGuid());
            var firstService = firstScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var secondService = secondScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
            var firstRequest = new ProjectBillingPreviewRequest(projectId, "PG-BILL-1", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30), "4000", "Concurrent project billing", IncludeCosts: false);
            var secondRequest = firstRequest with { InvoiceNumber = "PG-BILL-2" };
            var firstPreview = await firstService.PreviewProjectBillingAsync(firstRequest); var secondPreview = await secondService.PreviewProjectBillingAsync(secondRequest);
            Assert.True(firstPreview.Succeeded, firstPreview.ErrorMessage); Assert.True(secondPreview.Succeeded, secondPreview.ErrorMessage); Assert.Equal(firstPreview.ProjectConcurrencyToken, secondPreview.ProjectConcurrencyToken);
            var attempts = await Task.WhenAll(firstService.SaveProjectBillingProposalAsync(new(null, firstRequest, firstPreview.Fingerprint, firstPreview.ProjectConcurrencyToken)), secondService.SaveProjectBillingProposalAsync(new(null, secondRequest, secondPreview.Fingerprint, secondPreview.ProjectConcurrencyToken)));
            Assert.Single(attempts, result => result.Succeeded); Assert.Single(attempts, result => !result.Succeeded);

            using var verificationScope = provider.CreateScope(); var verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var verification = await verificationFactory.CreateDbContextAsync();
            Assert.Equal(1, await verification.ProjectBillingProposals.CountAsync(x => x.ProjectJobId == projectId));
            Assert.Equal(1, await verification.ProjectBillingSourceReservations.CountAsync(x => x.ProjectJobId == projectId && x.Status == "Reserved"));
            Assert.Equal(1, await verification.SubledgerDocumentWorkflows.CountAsync(x => x.CompanyId == companyId && x.DocumentType == "Invoice" && (x.DocumentNumber == "PG-BILL-1" || x.DocumentNumber == "PG-BILL-2")));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools(); await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString); await administration.OpenAsync(); await using var drop = administration.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)"; await drop.ExecuteNonQueryAsync();
            try { Directory.Delete(contentRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void SetContext(IServiceScope scope, Guid companyId, Guid userId)
    {
        var permissions = new[] { BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare };
        var claims = permissions.Select(permission => new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)).ToList(); claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.ToString())); claims.Add(new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()));
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
    }
}
