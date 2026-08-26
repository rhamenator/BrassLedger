using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace BrassLedger.Infrastructure.Tests;

public sealed class PostgresSchemaUpgradeTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.Postgres.Tests", Guid.NewGuid().ToString("N"));

    [PostgresFact]
    public async Task PostgreSql_InitializesAndAppliesMissingOrderedMigrationWithoutDataLoss()
    {
        var connectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Contains("brassledger_test", parsed.Database, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(_contentRootPath);

        await using (var reset = new NpgsqlConnection(connectionString))
        {
            await reset.OpenAsync();
            await using var command = reset.CreateCommand();
            command.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
            await command.ExecuteNonQueryAsync();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString
        }).Build();
        var collection = new ServiceCollection();
        collection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        using var services = collection.BuildServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using (var scope = services.CreateScope())
        {
            var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
            var signedIn = await authentication.AuthenticateAsync(
                "controller",
                BrassLedgerAuthenticationDefaults.SeededPassword,
                "127.0.0.1",
                "postgres-test");
            Assert.Equal(AuthenticationOutcome.Succeeded, signedIn.Outcome);
            var security = await authentication.GetAccountSecurityAsync(signedIn.User!.UserId);
            Assert.NotNull(security);
            Assert.Contains(security.RecentEvents, entry => entry.EventType == "login_succeeded" && entry.Succeeded);
        }

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            Assert.Equal(13L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"BrassLedgerSchemaVersions\";"));
            Assert.Equal(13L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"BrassLedgerSchemaVersions\" WHERE \"Description\" LIKE 'Compatibility checkpoint recorded by EF migration baseline%';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826014843_InitialCurrentSchema';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826025706_AddAccountingSchedules';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826033501_AddFixedAssetDisposals';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826124133_AddLandedCostAllocations';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826133409_SeparateSupplierReturnCreditValue';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826141942_AddControlledPurchaseInvoiceMatching';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826151008_ScopeVendorBillNumbersByVendor';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826160427_ScopeSubledgerVendorBillNumbersByVendor';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826164332_AddSubledgerRejectionWorkflow';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"Accounts\" WHERE \"Number\" = '1100' AND \"OperationalRole\" = 'AccountsReceivable';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"Companies\" WHERE \"Name\" = 'Brass Ledger Manufacturing';"));
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM "BrassLedgerSchemaVersions" WHERE "VersionId" LIKE '2026082513-%' OR "VersionId" LIKE '2026082512-%' OR "VersionId" LIKE '2026082511-%' OR "VersionId" LIKE '2026082510-%' OR "VersionId" LIKE '2026082509-%' OR "VersionId" LIKE '2026082508-%' OR "VersionId" LIKE '2026082507-%' OR "VersionId" LIKE '2026082506-%' OR "VersionId" LIKE '2026082505-%' OR "VersionId" LIKE '2026082504-%' OR "VersionId" LIKE '2026082503-%' OR "VersionId" LIKE '2026082502-%';
                DROP TABLE "__EFMigrationsHistory";
                DROP TABLE "AccountingScheduleInstallments";
                DROP TABLE "AccountingSchedules";
                ALTER TABLE "Accounts" DROP COLUMN "OperationalRole";
                ALTER TABLE "PayrollEarningLines" DROP COLUMN "W2ReportingJson";
                DROP TABLE "MfaRecoveryCodes";
                DROP TABLE "MfaSignInChallenges";
                DROP TABLE "UserSessions";
                DROP TABLE "OAuthAuthorizationAttempts";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialVersion", DROP COLUMN "CredentialOperationLeaseId", DROP COLUMN "CredentialOperation", DROP COLUMN "CredentialOperationLeaseExpiresAtUtc";
                DROP TABLE "ExternalEntityLinks";
                DROP TABLE "IntegrationSyncRuns";
                ALTER TABLE "Users" DROP COLUMN "MfaEnabled", DROP COLUMN "MfaSecret", DROP COLUMN "MfaEnrolledAtUtc", DROP COLUMN "MfaLastAcceptedTimeStep", DROP COLUMN "MfaFailedAttemptCount", DROP COLUMN "MfaLockoutEndUtc";
                ALTER TABLE "AccessRoles" DROP COLUMN "RequiresMfa";
                DROP TABLE "SecurityEmailOutboxMessages";
                DROP TABLE "AccountActionTokens";
                ALTER TABLE "Users" DROP COLUMN "EmailConfirmedAtUtc";
                ALTER TABLE "Users" DROP COLUMN "EmailLookupHash";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await services.InitializeBrassLedgerAsync();

        await using var verified = new NpgsqlConnection(connectionString);
        await verified.OpenAsync();
        Assert.Equal(13L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"BrassLedgerSchemaVersions\";"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826014843_InitialCurrentSchema';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826025706_AddAccountingSchedules';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826033501_AddFixedAssetDisposals';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826124133_AddLandedCostAllocations';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826133409_SeparateSupplierReturnCreditValue';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826141942_AddControlledPurchaseInvoiceMatching';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826151008_ScopeVendorBillNumbersByVendor';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826160427_ScopeSubledgerVendorBillNumbersByVendor';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260826164332_AddSubledgerRejectionWorkflow';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'AccountingSchedules';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'AccountingSchedules' AND column_name = 'DisposalJournalEntryId';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'PayrollEarningLines' AND column_name = 'W2ReportingJson';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'AccountingInterchangeBatches';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'LandedCostAllocations';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'MfaRecoveryCodes';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'Users' AND column_name = 'MfaSecret';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'AccessRoles' AND column_name = 'RequiresMfa';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"AccessRoles\" WHERE \"Name\" = 'Administrator' AND \"RequiresMfa\" = true;"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'AccountActionTokens';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'UserSessions';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'OAuthAuthorizationAttempts';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'IntegrationConnections' AND column_name = 'CredentialVersion';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'IntegrationConnections' AND column_name = 'CredentialOperationLeaseId';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'IntegrationConnections' AND column_name = 'CredentialOperationLeaseExpiresAtUtc';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'ExternalEntityLinks';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'IntegrationSyncRuns';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'Accounts' AND column_name = 'OperationalRole';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"Accounts\" WHERE \"Number\" = '1100' AND \"OperationalRole\" = 'AccountsReceivable';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'IX_Accounts_CompanyId_OperationalRole';"));
        Assert.Equal(64L, await ScalarLongAsync(verified, "SELECT length(\"EmailLookupHash\") FROM \"Users\" WHERE \"UserName\" = 'controller';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"Companies\" WHERE \"Name\" = 'Brass Ledger Manufacturing';"));

        await using (var futureMigration = verified.CreateCommand())
        {
            futureMigration.CommandText = """INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('99999999999999_FutureMigration', '99.0.0');""";
            await futureMigration.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => services.InitializeBrassLedgerAsync());
        Assert.Contains("unsupported or newer EF migration", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automatic downgrade is prohibited", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"Companies\" WHERE \"Name\" = 'Brass Ledger Manufacturing';"));
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public void Dispose()
    {
        if (!Directory.Exists(_contentRootPath)) return;
        try { Directory.Delete(_contentRootPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")))
            Skip = "Set BRASSLEDGER_TEST_POSTGRES to an isolated database whose name contains brassledger_test.";
    }
}
