using BrassLedger.Application.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Infrastructure.Tests;

public sealed class WorkspaceInitializationTests : IDisposable
{
    private readonly string _contentRootPath;

    public WorkspaceInitializationTests()
    {
        _contentRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRootPath);
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_SeedsWorkspaceSnapshot()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider
            .GetRequiredService<IBusinessWorkspaceService>()
            .GetWorkspaceAsync();

        Assert.Equal("Brass Ledger Manufacturing", workspace.Company.Name);
        Assert.Equal(14, workspace.Modules.Count);
        Assert.Equal(112540.32m, workspace.Dashboard.CashOnHand);
        Assert.Equal(34715.75m, workspace.Receivables.OpenBalance);
        Assert.Equal(31844.77m, workspace.Payables.OpenBalance);
        Assert.Equal(24367m, workspace.Payroll.MonthlyGross);
        Assert.Equal(5, workspace.Operations.InventoryItemCount);
        Assert.Equal(6, workspace.Reporting.ReportCount);
        Assert.Equal(3, workspace.Reporting.LabelCount);
        Assert.Equal(4, workspace.Taxes.ProfileCount);
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_CreatesSqliteDatabaseInAppData()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");

        Assert.True(File.Exists(databasePath));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_SeedsAuthenticationCredentials()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var authenticationService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        var user = await authenticationService.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword);

        Assert.NotNull(user);
        Assert.Equal("Controller", user.Role);
        Assert.Equal("controller", user.UserName);
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_ProtectsSensitiveFieldsAtRest()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var rawTaxId = await ReadScalarAsync(connection, "SELECT TaxId FROM Companies LIMIT 1;");
        var rawUserEmail = await ReadScalarAsync(connection, "SELECT Email FROM Users LIMIT 1;");
        var rawCustomerName = await ReadScalarAsync(connection, "SELECT Name FROM Customers LIMIT 1;");

        Assert.StartsWith("enc::", rawTaxId);
        Assert.StartsWith("enc::", rawUserEmail);
        Assert.StartsWith("enc::", rawCustomerName);
        Assert.DoesNotContain("84-9923145", rawTaxId, StringComparison.Ordinal);
        Assert.DoesNotContain("erin@brassledger.local", rawUserEmail, StringComparison.Ordinal);
        Assert.DoesNotContain("Red Mesa Builders", rawCustomerName, StringComparison.Ordinal);
    }

    private ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBrassLedgerInfrastructure(configuration, _contentRootPath);
        return serviceCollection.BuildServiceProvider();
    }

    private static async Task<string> ReadScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty;
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRootPath))
        {
            try
            {
                Directory.Delete(_contentRootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
