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

public sealed class ConsolidationPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_ConcurrentOverlappingOwnershipPeriodsRetainExactlyOneSuccessor()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_consolidation_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync();
            await using var create = administration.CreateCommand(); create.CommandText = $"CREATE DATABASE {quotedDatabase}"; await create.ExecuteNonQueryAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "BrassLedger.Consolidation.Postgres.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = testBuilder.ConnectionString }).Build();
            var services = new ServiceCollection(); services.AddBrassLedgerInfrastructure(configuration, contentRoot, seedSampleData: true);
            using var provider = services.BuildServiceProvider(); await provider.InitializeBrassLedgerAsync();
            Guid companyId; Guid ownerId;
            using (var readScope = provider.CreateScope())
            {
                var factory = readScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                companyId = await db.Companies.Select(company => company.Id).SingleAsync();
                var owner = await db.Users.Where(user => user.UserName == "controller").SingleAsync(); ownerId = owner.Id;
                if (!await db.CompanyMemberships.AnyAsync(membership => membership.UserId == ownerId && membership.CompanyId == companyId))
                {
                    db.CompanyMemberships.Add(new BrassLedger.Domain.Accounting.CompanyMembership { Id = Guid.NewGuid(), UserId = ownerId, CompanyId = companyId, Role = owner.Role, IsOwner = true, IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow });
                    await db.SaveChangesAsync();
                }
            }

            Guid groupId;
            using (var setupScope = provider.CreateScope())
            {
                SetContext(setupScope, companyId, ownerId);
                var consolidation = setupScope.ServiceProvider.GetRequiredService<IConsolidationService>();
                var created = await consolidation.SaveGroupAsync(new SaveConsolidationGroupRequest(null, "Concurrent ownership", "USD", [new ConsolidationMemberRequest(companyId, 1m, new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 31))]));
                Assert.True(created.Succeeded, created.ErrorMessage); groupId = created.Id!.Value;
            }

            using var firstScope = provider.CreateScope(); using var secondScope = provider.CreateScope();
            SetContext(firstScope, companyId, ownerId); SetContext(secondScope, companyId, ownerId);
            var attempts = await Task.WhenAll(
                firstScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveOwnershipPeriodAsync(new(null, groupId, companyId, .5m, new DateOnly(2026, 6, 1), null)),
                secondScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveOwnershipPeriodAsync(new(null, groupId, companyId, .6m, new DateOnly(2026, 7, 1), null)));
            Assert.Single(attempts, result => result.Succeeded);
            Assert.Single(attempts, result => !result.Succeeded);

            using var verificationScope = provider.CreateScope(); var verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var verification = await verificationFactory.CreateDbContextAsync();
            Assert.Equal(2, await verification.ConsolidationGroupCompanies.CountAsync(period => period.ConsolidationGroupId == groupId));
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry => entry.Action == "consolidation-ownership.created" && entry.EntityType == "ConsolidationGroupCompany"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString); await administration.OpenAsync();
            await using var drop = administration.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)"; await drop.ExecuteNonQueryAsync();
            try { Directory.Delete(contentRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void SetContext(IServiceScope scope, Guid companyId, Guid userId)
    {
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ReportingManage)
            ], "test"))
        };
    }
}
