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

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            Assert.Equal(3L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"BrassLedgerSchemaVersions\";"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM \"Companies\" WHERE \"Name\" = 'Brass Ledger Manufacturing';"));
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM "BrassLedgerSchemaVersions" WHERE "VersionId" LIKE '2026082503-%' OR "VersionId" LIKE '2026082502-%';
                ALTER TABLE "PayrollEarningLines" DROP COLUMN "W2ReportingJson";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await services.InitializeBrassLedgerAsync();

        await using var verified = new NpgsqlConnection(connectionString);
        await verified.OpenAsync();
        Assert.Equal(3L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM \"BrassLedgerSchemaVersions\";"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'PayrollEarningLines' AND column_name = 'W2ReportingJson';"));
        Assert.Equal(1L, await ScalarLongAsync(verified, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'AccountingInterchangeBatches';"));
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
