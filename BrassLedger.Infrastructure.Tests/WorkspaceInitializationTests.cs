using BrassLedger.Application.Accounting;
using BrassLedger.Application.Taxation;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.SecurityAdministration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        Assert.Contains(workspace.GeneralLedger.Accounts, account => account.Number == "4300" && account.Name == "Foreign Exchange Gain");
        Assert.Contains(workspace.GeneralLedger.Accounts, account => account.Number == "6300" && account.Name == "Foreign Exchange Loss");
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_CreatesSqliteDatabaseInAppData()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");

        Assert.True(File.Exists(databasePath));
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        Assert.Equal("3", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM BrassLedgerSchemaVersions;"));
        Assert.StartsWith("2026082503-", await ReadScalarAsync(connection, "SELECT VersionId FROM BrassLedgerSchemaVersions ORDER BY VersionId DESC LIMIT 1;"));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_UpgradesPreLedgerDatabaseOnceAndRetainsData()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE "BrassLedgerSchemaVersions";
                ALTER TABLE "PayrollTimeEntries" DROP COLUMN "W2ReportingJson";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await services.InitializeBrassLedgerAsync();

        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("3", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM BrassLedgerSchemaVersions;"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AccountingInterchangeBatches';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('PayrollTimeEntries') WHERE name = 'W2ReportingJson';"));
        Assert.Equal("Brass Ledger Manufacturing", await ReadScalarAsync(verified, "SELECT Name FROM Companies WHERE Name = 'Brass Ledger Manufacturing';"));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_AppliesMissingOrderedMigrationWithoutLegacyReplay()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM "BrassLedgerSchemaVersions" WHERE "VersionId" LIKE '2026082503-%' OR "VersionId" LIKE '2026082502-%';
                ALTER TABLE "PayrollEarningLines" DROP COLUMN "W2ReportingJson";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await services.InitializeBrassLedgerAsync();

        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("3", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM BrassLedgerSchemaVersions;"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('PayrollEarningLines') WHERE name = 'W2ReportingJson';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AccountingInterchangeBatches';"));
        Assert.Equal("Brass Ledger Manufacturing", await ReadScalarAsync(verified, "SELECT Name FROM Companies WHERE Name = 'Brass Ledger Manufacturing';"));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_RejectsMigrationWhosePrerequisiteIsMissing()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """DELETE FROM "BrassLedgerSchemaVersions" WHERE "VersionId" LIKE '2026082502-%';""";
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => services.InitializeBrassLedgerAsync());

        Assert.Contains("without prerequisite", exception.Message, StringComparison.OrdinalIgnoreCase);
        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("Brass Ledger Manufacturing", await ReadScalarAsync(verified, "SELECT Name FROM Companies WHERE Name = 'Brass Ledger Manufacturing';"));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_RejectsUnknownNewerSchemaWithoutChangingData()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """INSERT INTO "BrassLedgerSchemaVersions" ("VersionId", "AppliedAtUtc", "ProductVersion", "Description", "Provider") VALUES ('9999999999-future', '2099-01-01T00:00:00Z', '99.0', 'Future schema', 'test');""";
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => services.InitializeBrassLedgerAsync());

        Assert.Contains("automatic downgrade is prohibited", exception.Message, StringComparison.OrdinalIgnoreCase);
        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("Brass Ledger Manufacturing", await ReadScalarAsync(verified, "SELECT Name FROM Companies WHERE Name = 'Brass Ledger Manufacturing';"));
    }

    [Fact]
    public async Task BackupService_CreatesAndVerifiesConsistentSqliteSnapshot()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var backup = await backupService.CreateBackupAsync();
        Assert.True(backup.Succeeded, backup.ErrorMessage);
        var verification = await backupService.VerifyBackupAsync(backup.BackupId!);
        Assert.True(verification.Succeeded, verification.ErrorMessage);
        var rehearsal = await backupService.RehearseRestoreAsync(backup.BackupId!);
        Assert.True(rehearsal.Succeeded, rehearsal.ErrorMessage);
        Assert.True(rehearsal.CompanyCount > 0);
        Assert.True(rehearsal.JournalEntryCount > 0);
        Assert.True(rehearsal.DataProtectionKeyCount > 0);
    }

    [Fact]
    public async Task Consolidation_UsesEffectiveInverseRateAndExposesConfiguredGroup()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var signedInOwner = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "xunit");
        Assert.Equal(AuthenticationOutcome.Succeeded, signedInOwner.Outcome);
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var ownerContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ownerContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, signedInOwner.User!.UserId.ToString()), new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, signedInOwner.User.CompanyId.ToString())], "test"));
        accessor.HttpContext = ownerContext;
        var companies = scope.ServiceProvider.GetRequiredService<ICompanyManagementService>();
        var consolidation = scope.ServiceProvider.GetRequiredService<IConsolidationService>();
        var secondCompany = await companies.CreateCompanyAsync(new CreateCompanyRequest("Canadian subsidiary", "Canadian subsidiary Ltd.", "CA-TEST", "CAD", 1));
        Assert.True(secondCompany.Succeeded, secondCompany.ErrorMessage);
        var memberships = await companies.GetMyCompaniesAsync();
        var currentCompanyId = memberships.Single(company => company.Name == "Brass Ledger Manufacturing").CompanyId;
        var canadianCompanyId = secondCompany.CompanyId!.Value;
        var rate = await consolidation.SaveExchangeRateAsync(new SaveExchangeRateRequest("USD", "CAD", 1.25m, new DateOnly(2026, 1, 1), "Test rate"));
        Assert.True(rate.Succeeded, rate.ErrorMessage);
        var group = await consolidation.SaveGroupAsync(new SaveConsolidationGroupRequest(null, "North America", "USD", [new ConsolidationMemberRequest(currentCompanyId), new ConsolidationMemberRequest(canadianCompanyId, .8m)]));
        Assert.True(group.Succeeded, group.ErrorMessage);
        var configuredGroups = await consolidation.GetGroupsAsync();
        Assert.Contains(configuredGroups, item => item.Id == group.Id && item.Members.Count == 2);
        var report = await consolidation.GetBalanceReportAsync(group.Id!.Value, new DateOnly(2026, 5, 1));
        Assert.NotNull(report);
        Assert.Empty(report!.Warnings);
        Assert.NotEmpty(report.Accounts);
    }

    [Fact]
    public async Task IntegrationConnection_EncryptsCredentialsAndDoesNotReturnThemInSnapshots()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var signedInOwner = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "xunit");
        Assert.Equal(AuthenticationOutcome.Succeeded, signedInOwner.Outcome);
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, signedInOwner.User!.UserId.ToString()), new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, signedInOwner.User.CompanyId.ToString())], "test"));
        accessor.HttpContext = context;
        var integrations = scope.ServiceProvider.GetRequiredService<IIntegrationService>();
        var catalog = await integrations.GetCatalogAsync();
        var quickBooks = catalog.Single(provider => provider.Code == "quickbooks-online");
        Assert.Equal("File interchange available", quickBooks.ImplementationStatus); Assert.False(quickBooks.LiveSynchronizationAvailable);
        Assert.Contains("zero-tax invoice draft interchange", quickBooks.SupportedCapabilities, StringComparison.OrdinalIgnoreCase);
        Assert.All(catalog.Where(provider => provider.Code != "quickbooks-online"), provider => Assert.Equal("Profile only", provider.ImplementationStatus));
        var saved = await integrations.SaveConnectionAsync(new SaveIntegrationConnectionRequest(null, "stripe", "Primary Stripe", "{\"mode\":\"test\"}", "{\"apiKey\":\"super-secret\"}", false));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var connections = await integrations.GetConnectionsAsync();
        Assert.Contains(connections, connection => connection.Id == saved.Id && connection.Name == "Primary Stripe");
        var disabled = await integrations.SaveConnectionAsync(new SaveIntegrationConnectionRequest(saved.Id, "stripe", "Primary Stripe", "{\"mode\":\"test\"}", string.Empty, false));
        Assert.True(disabled.Succeeded, disabled.ErrorMessage);
        Assert.Contains(await integrations.GetConnectionsAsync(), connection => connection.Id == saved.Id && connection.Status == "Disabled");
        var dbPath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        var stored = await ReadScalarAsync(connection, "SELECT CredentialsJson FROM IntegrationConnections WHERE Name = 'Primary Stripe';");
        Assert.DoesNotContain("super-secret", stored);
        Assert.StartsWith("enc::", stored);
    }

    [Fact]
    public async Task QuickBooksInvoiceImport_RequiresReceivablesAndDraftPreparationPermissions()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(company => company.Id).FirstAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.LedgerManage)
            ], "test"))
        };
        var interchange = scope.ServiceProvider.GetRequiredService<IAccountingInterchangeService>();
        await using var csv = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Invoice No.,Customer,Invoice Date,Due Date,Item Amount\r\nDENIED-1,C-1001,2026-05-10,2026-06-09,50.00"));

        var result = await interchange.ImportQuickBooksOnlineCsvAsync("invoices", csv, new(true, "denied.csv"));

        Assert.False(result.Succeeded);
        Assert.Contains("not authorized", result.Errors.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.AccountingInterchangeBatches.AnyAsync(batch => batch.CompanyId == companyId && batch.FileName == "denied.csv"));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_UsesConfiguredDataRootWhenProvided()
    {
        var configuredDataRoot = Path.Combine(_contentRootPath, "CustomDataRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataRoot"] = configuredDataRoot
            })
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);

        using var services = serviceCollection.BuildServiceProvider();
        await services.InitializeBrassLedgerAsync();

        Assert.True(File.Exists(Path.Combine(configuredDataRoot, "brassledger.db")));
        Assert.True(Directory.Exists(Path.Combine(configuredDataRoot, "keys")));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_SeedsAuthenticationCredentials()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var authenticationService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        var authenticationResult = await authenticationService.AuthenticateAsync(
            "controller",
            BrassLedgerAuthenticationDefaults.SeededPassword,
            "127.0.0.1",
            "xunit");

        Assert.Equal(AuthenticationOutcome.Succeeded, authenticationResult.Outcome);
        Assert.NotNull(authenticationResult.User);
        Assert.Equal("Controller", authenticationResult.User!.Role);
        Assert.Equal("controller", authenticationResult.User.UserName);
        Assert.Contains(BrassLedgerPermissions.LedgerManage, authenticationResult.User.Permissions);
        Assert.DoesNotContain(BrassLedgerPermissions.RoleManage, authenticationResult.User.Permissions);
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

    [Fact]
    public async Task InitializeBrassLedgerAsync_InNonDevelopmentMode_AllowsFirstRunWithoutBootstrapPassword()
    {
        var configuration = new ConfigurationBuilder().Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: false);

        using var services = serviceCollection.BuildServiceProvider();
        await services.InitializeBrassLedgerAsync();

        await using var scope = services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        Assert.False(await dbContext.Companies.AnyAsync());
        Assert.False(await dbContext.Users.AnyAsync());
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_InNonDevelopmentMode_CreatesBootstrapAdministrator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:CompanyName"] = "Northwind Fabrication",
                ["Bootstrap:LegalName"] = "Northwind Fabrication LLC",
                ["Bootstrap:AdminUserName"] = "admin",
                ["Bootstrap:AdminDisplayName"] = "Release Admin",
                ["Bootstrap:AdminEmail"] = "admin@northwind.example",
                ["Bootstrap:AdminPassword"] = "S3cure!Release!2026"
            })
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: false);

        using var services = serviceCollection.BuildServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var authenticationService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var authenticationResult = await authenticationService.AuthenticateAsync(
            "admin",
            "S3cure!Release!2026",
            "127.0.0.1",
            "xunit");

        Assert.Equal(AuthenticationOutcome.Succeeded, authenticationResult.Outcome);
        Assert.NotNull(authenticationResult.User);
        Assert.Equal("Administrator", authenticationResult.User!.Role);
        Assert.Contains(BrassLedgerPermissions.RoleManage, authenticationResult.User.Permissions);
        Assert.Contains(BrassLedgerPermissions.UserManage, authenticationResult.User.Permissions);

        var membershipFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var membershipDb = await membershipFactory.CreateDbContextAsync();
        var membership = await membershipDb.CompanyMemberships.SingleAsync();
        Assert.Equal(authenticationResult.User.UserId, membership.UserId);
        Assert.Equal(authenticationResult.User.CompanyId, membership.CompanyId);
        Assert.True(membership.IsOwner);

        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        Assert.Equal("Northwind Fabrication", workspace.Company.Name);
        Assert.Equal(1, workspace.Company.ActiveUsers);
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_SeedsBuiltInAccessRoles()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        var roles = await dbContext.AccessRoles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .ToListAsync();

        Assert.Contains(roles, role => role.Name == "Administrator" && role.IsSystemRole);
        Assert.Contains(roles, role => role.Name == "Owner/CEO" && role.IsSystemRole);
        Assert.Contains(roles, role => role.Name == "Requisitioning Clerk");
        Assert.Contains(roles, role => role.Name == "Purchasing Manager");
        Assert.Contains(roles, role => role.Name == "Cash Disbursements");
        Assert.Contains(roles, role => role.Name == "Payroll Preparer");
        Assert.Contains(roles, role => role.Name == "Payroll Approver");
        Assert.Contains(roles, role => role.Name == "Payroll Poster");
    }

    [Fact]
    public async Task CreateInitialWorkspaceAsync_RejectsMismatchedPasswordConfirmation()
    {
        var configuration = new ConfigurationBuilder().Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: false);

        using var services = serviceCollection.BuildServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var bootstrapService = scope.ServiceProvider.GetRequiredService<IBootstrapWorkspaceService>();

        var result = await bootstrapService.CreateInitialWorkspaceAsync(new BootstrapWorkspaceRequest(
            "Northwind Fabrication",
            "Northwind Fabrication LLC",
            "84-3182457",
            "USD",
            1,
            "admin",
            "Jordan Ellis",
            "admin@northwind.example",
            "BrassLedger!2026",
            "BrassLedger!WRONG"));

        Assert.Equal(BootstrapWorkspaceOutcome.Invalid, result.Outcome);
        Assert.Equal("The administrator password confirmation does not match.", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_LocksOperatorAfterRepeatedFailures_AndWritesAuditEntries()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var authenticationService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();

        for (var attempt = 0; attempt < BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts; attempt++)
        {
            var result = await authenticationService.AuthenticateAsync(
                "controller",
                "bad-password",
                "127.0.0.1",
                "xunit");

            if (attempt < BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts - 1)
            {
                Assert.Equal(AuthenticationOutcome.InvalidCredentials, result.Outcome);
            }
            else
            {
                Assert.Equal(AuthenticationOutcome.LockedOut, result.Outcome);
                Assert.NotNull(result.LockoutEndUtc);
            }
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var user = await dbContext.Users.SingleAsync(x => x.UserName == "controller");

        Assert.True(user.LockoutEndUtc > DateTimeOffset.UtcNow);
        Assert.Equal(BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts, user.FailedSignInCount);
        Assert.True(await dbContext.AuthenticationAuditEntries.CountAsync(x => x.UserName == "controller") >= BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts);
    }

    [Fact]
    public async Task TransactionService_PostsInvoiceAndPaymentAsBalancedLedgerActivity()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var before = await workspaceService.GetWorkspaceAsync();
        var customer = before.Receivables.Customers.First();
        var bank = before.Treasury.BankAccounts.First();

        var invoiceResult = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(
            customer.Id, "INV-TEST-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m, 0m, "4000", "Test invoice"));
        Assert.True(invoiceResult.Succeeded, invoiceResult.ErrorMessage);

        var afterInvoice = await workspaceService.GetWorkspaceAsync();
        var invoice = afterInvoice.Receivables.Invoices.Single(x => x.Id == invoiceResult.Id);
        Assert.Equal(100m, invoice.BalanceDue);
        Assert.Equal(before.Receivables.OpenBalance + 100m, afterInvoice.Receivables.OpenBalance);

        var paymentResult = await transactions.ApplyInvoicePaymentAsync(new ApplyInvoicePaymentRequest(invoice.Id, bank.Id, new DateOnly(2026, 5, 2), 100m, "DEP-TEST-1"));
        Assert.True(paymentResult.Succeeded, paymentResult.ErrorMessage);
        var afterPayment = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.Receivables.OpenBalance, afterPayment.Receivables.OpenBalance);
        Assert.Equal(before.Treasury.CashOnHand + 100m, afterPayment.Treasury.CashOnHand);
        Assert.Contains(afterPayment.GeneralLedger.RecentEntries, entry => entry.Description == "Customer payment");
    }

    [Fact]
    public async Task CustomerPayment_AppliesMultipleInvoices_PreservesDeposit_AndReturnsAuditably()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var initial = await workspaceService.GetWorkspaceAsync();
        var customer = initial.Receivables.Customers.First();
        var bank = initial.Treasury.BankAccounts.First();
        var first = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(customer.Id, "INV-PAY-MULTI-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m, 0m, "4000", "First payment invoice"));
        var second = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(customer.Id, "INV-PAY-MULTI-2", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 50m, 0m, "4000", "Second payment invoice"));
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(second.Succeeded, second.ErrorMessage);
        var beforePayment = await workspaceService.GetWorkspaceAsync();

        var result = await transactions.RecordCustomerPaymentAsync(new RecordCustomerPaymentRequest(
            customer.Id, bank.Id, new DateOnly(2026, 5, 2), 180m, "DEP-MULTI-1", "ACH",
            [new PaymentDocumentApplicationRequest(first.Id!.Value, 100m), new PaymentDocumentApplicationRequest(second.Id!.Value, 50m)]));

        Assert.True(result.Succeeded, result.ErrorMessage);
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            var payment = await db.SubledgerPayments.SingleAsync(item => item.Id == result.Id);
            Assert.Equal(150m, payment.AppliedAmount);
            Assert.Equal(30m, payment.UnappliedAmount);
            Assert.Equal("Posted", payment.Status);
            Assert.Equal(2, await db.SubledgerPaymentApplications.CountAsync(item => item.SubledgerPaymentId == payment.Id));
            var postings = await (from line in db.JournalEntryLines join account in db.Accounts on line.AccountId equals account.Id where line.JournalEntryId == payment.JournalEntryId select new { account.Number, line.Debit, line.Credit }).ToListAsync();
            Assert.Contains(postings, line => line.Number == bank.LedgerAccountNumber && line.Debit == 180m);
            Assert.Contains(postings, line => line.Number == "1100" && line.Credit == 150m);
            Assert.Contains(postings, line => line.Number == "2150" && line.Credit == 30m);
            Assert.Equal(postings.Sum(line => line.Debit), postings.Sum(line => line.Credit));
            Assert.True(await db.BusinessAuditEntries.AnyAsync(entry => entry.EntityId == payment.Id && entry.Action == "payment.posted"));
        }
        var afterPayment = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(0m, afterPayment.Receivables.Invoices.Single(invoice => invoice.Id == first.Id).BalanceDue);
        Assert.Equal(0m, afterPayment.Receivables.Invoices.Single(invoice => invoice.Id == second.Id).BalanceDue);
        Assert.Equal(beforePayment.Receivables.OpenBalance - 150m, afterPayment.Receivables.OpenBalance);
        Assert.Equal(beforePayment.Treasury.CashOnHand + 180m, afterPayment.Treasury.CashOnHand);
        Assert.Contains(afterPayment.Receivables.Payments ?? [], payment => payment.Id == result.Id && payment.Applications.Count == 2 && payment.UnappliedAmount == 30m);

        var returned = await transactions.ReverseSubledgerPaymentAsync(new ReverseSubledgerPaymentRequest(result.Id!.Value, new DateOnly(2026, 5, 3), "ACH was returned", "Returned"));
        Assert.True(returned.Succeeded, returned.ErrorMessage);
        var afterReturn = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(100m, afterReturn.Receivables.Invoices.Single(invoice => invoice.Id == first.Id).BalanceDue);
        Assert.Equal(50m, afterReturn.Receivables.Invoices.Single(invoice => invoice.Id == second.Id).BalanceDue);
        Assert.Equal(beforePayment.Receivables.OpenBalance, afterReturn.Receivables.OpenBalance);
        Assert.Equal(beforePayment.Treasury.CashOnHand, afterReturn.Treasury.CashOnHand);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            var payment = await db.SubledgerPayments.SingleAsync(item => item.Id == result.Id);
            Assert.Equal("Returned", payment.Status);
            Assert.NotNull(payment.ReversalJournalEntryId);
            Assert.True(await db.BusinessAuditEntries.AnyAsync(entry => entry.EntityId == payment.Id && entry.Action == "payment.reversed"));
        }
        var repeated = await transactions.ReverseSubledgerPaymentAsync(new ReverseSubledgerPaymentRequest(result.Id.Value, new DateOnly(2026, 5, 4), "Repeat", "Returned"));
        Assert.False(repeated.Succeeded);
        var duplicateReference = await transactions.RecordCustomerPaymentAsync(new RecordCustomerPaymentRequest(customer.Id, bank.Id, new DateOnly(2026, 5, 4), 1m, "DEP-MULTI-1", "Cash", []));
        Assert.False(duplicateReference.Succeeded);
    }

    [Fact]
    public async Task VendorPayment_AppliesMultipleBills_PreservesAdvance_AndVoidsAuditably()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var initial = await workspaceService.GetWorkspaceAsync();
        var vendor = initial.Payables.Vendors.First();
        var bank = initial.Treasury.BankAccounts.First();
        var first = await transactions.CreateVendorBillAsync(new CreateVendorBillRequest(vendor.Id, "B-PAY-MULTI-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 40m, "5100", "First payment bill"));
        var second = await transactions.CreateVendorBillAsync(new CreateVendorBillRequest(vendor.Id, "B-PAY-MULTI-2", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 60m, "5100", "Second payment bill"));
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(second.Succeeded, second.ErrorMessage);
        var beforePayment = await workspaceService.GetWorkspaceAsync();

        var result = await transactions.RecordVendorPaymentAsync(new RecordVendorPaymentRequest(
            vendor.Id, bank.Id, new DateOnly(2026, 5, 2), 120m, "CHK-MULTI-1", "Check",
            [new PaymentDocumentApplicationRequest(first.Id!.Value, 40m), new PaymentDocumentApplicationRequest(second.Id!.Value, 60m)]));

        Assert.True(result.Succeeded, result.ErrorMessage);
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            var payment = await db.SubledgerPayments.SingleAsync(item => item.Id == result.Id);
            Assert.Equal(100m, payment.AppliedAmount);
            Assert.Equal(20m, payment.UnappliedAmount);
            var postings = await (from line in db.JournalEntryLines join account in db.Accounts on line.AccountId equals account.Id where line.JournalEntryId == payment.JournalEntryId select new { account.Number, line.Debit, line.Credit }).ToListAsync();
            Assert.Contains(postings, line => line.Number == "2000" && line.Debit == 100m);
            Assert.Contains(postings, line => line.Number == "1300" && line.Debit == 20m);
            Assert.Contains(postings, line => line.Number == bank.LedgerAccountNumber && line.Credit == 120m);
            Assert.Equal(postings.Sum(line => line.Debit), postings.Sum(line => line.Credit));
        }
        var afterPayment = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(beforePayment.Payables.OpenBalance - 100m, afterPayment.Payables.OpenBalance);
        Assert.Equal(beforePayment.Treasury.CashOnHand - 120m, afterPayment.Treasury.CashOnHand);

        var voided = await transactions.ReverseSubledgerPaymentAsync(new ReverseSubledgerPaymentRequest(result.Id!.Value, new DateOnly(2026, 5, 3), "Check spoiled", "Voided"));
        Assert.True(voided.Succeeded, voided.ErrorMessage);
        var afterVoid = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(beforePayment.Payables.OpenBalance, afterVoid.Payables.OpenBalance);
        Assert.Equal(beforePayment.Treasury.CashOnHand, afterVoid.Treasury.CashOnHand);
        Assert.Contains(afterVoid.Payables.Payments ?? [], payment => payment.Id == result.Id && payment.Status == "Voided");
    }

    [Fact]
    public async Task PaymentService_RejectsCrossCounterpartyApplicationsAndOverapplicationWithoutPosting()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var customer = workspace.Receivables.Customers.First();
        var otherInvoice = workspace.Receivables.Invoices.First(invoice => invoice.CustomerId != customer.Id);
        var bank = workspace.Treasury.BankAccounts.First();

        var wrongCustomer = await transactions.RecordCustomerPaymentAsync(new RecordCustomerPaymentRequest(customer.Id, bank.Id, new DateOnly(2026, 5, 2), 10m, "BAD-PAY-1", "ACH", [new PaymentDocumentApplicationRequest(otherInvoice.Id, 10m)]));
        var overapplied = await transactions.RecordCustomerPaymentAsync(new RecordCustomerPaymentRequest(customer.Id, bank.Id, new DateOnly(2026, 5, 2), 10m, "BAD-PAY-2", "ACH", [new PaymentDocumentApplicationRequest(workspace.Receivables.Invoices.First(invoice => invoice.CustomerId == customer.Id).Id, 11m)]));

        Assert.False(wrongCustomer.Succeeded);
        Assert.False(overapplied.Succeeded);
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        Assert.False(await db.SubledgerPayments.AnyAsync(payment => payment.Reference.StartsWith("BAD-PAY")));
        Assert.False(await db.JournalEntries.AnyAsync(entry => entry.Reference.StartsWith("BAD-PAY")));
    }

    [Fact]
    public async Task PaymentLifecycle_EnforcesRecordingAndReversalPermissionsSeparately()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(company => company.Id).FirstAsync();
        var customerId = await db.Customers.Where(customer => customer.CompanyId == companyId).Select(customer => customer.Id).FirstAsync();
        var vendorId = await db.Vendors.Where(vendor => vendor.CompanyId == companyId).Select(vendor => vendor.Id).FirstAsync();
        var bankId = await db.BankAccounts.Where(bank => bank.CompanyId == companyId).Select(bank => bank.Id).FirstAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        void ActAs(params string[] permissions)
        {
            var claims = new List<System.Security.Claims.Claim>
            {
                new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString())
            };
            claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test"))
            };
        }

        ActAs(BrassLedgerPermissions.ReceivablesManage);
        var invoice = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(customerId, "INV-PAY-SOD-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 25m, 0m, "4000", "Payment authority test"));
        Assert.True(invoice.Succeeded, invoice.ErrorMessage);
        var receipt = await transactions.RecordCustomerPaymentAsync(new RecordCustomerPaymentRequest(customerId, bankId, new DateOnly(2026, 5, 2), 25m, "DEP-PAY-SOD-1", "ACH", [new PaymentDocumentApplicationRequest(invoice.Id!.Value, 25m)]));
        Assert.True(receipt.Succeeded, receipt.ErrorMessage);
        Assert.False((await transactions.RecordVendorPaymentAsync(new RecordVendorPaymentRequest(vendorId, bankId, new DateOnly(2026, 5, 2), 1m, "CHK-PAY-SOD-1", "Check", []))).Succeeded);
        Assert.False((await transactions.ReverseSubledgerPaymentAsync(new ReverseSubledgerPaymentRequest(receipt.Id!.Value, new DateOnly(2026, 5, 3), "Unauthorized return", "Returned"))).Succeeded);

        ActAs(BrassLedgerPermissions.PaymentReverse);
        var returned = await transactions.ReverseSubledgerPaymentAsync(new ReverseSubledgerPaymentRequest(receipt.Id.Value, new DateOnly(2026, 5, 3), "Authorized return", "Returned"));
        Assert.True(returned.Succeeded, returned.ErrorMessage);
    }

    [Fact]
    public async Task CustomerAdjustments_PostCreditWriteOffRefundAndReversalsAuditably()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var before = await workspaceService.GetWorkspaceAsync();
        var customer = before.Receivables.Customers.First();
        var bank = before.Treasury.BankAccounts.First();
        var invoice = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(customer.Id, "INV-ADJ-1", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), 100m, 0m, "4000", "Adjustment test"));
        Assert.True(invoice.Succeeded, invoice.ErrorMessage);

        var credit = await transactions.RecordCustomerAdjustmentAsync(new RecordCustomerAdjustmentRequest(invoice.Id!.Value, new DateOnly(2026, 6, 2), 20m, "CM-ADJ-1", "4000", "Price concession"));
        var writeOff = await transactions.RecordCustomerAdjustmentAsync(new RecordCustomerAdjustmentRequest(invoice.Id.Value, new DateOnly(2026, 6, 3), 30m, "WO-ADJ-1", "5100", "Uncollectible balance", "WriteOff"));
        Assert.True(credit.Succeeded, credit.ErrorMessage);
        Assert.True(writeOff.Succeeded, writeOff.ErrorMessage);
        var adjusted = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(50m, adjusted.Receivables.Invoices.Single(item => item.Id == invoice.Id).BalanceDue);
        Assert.Contains(adjusted.Receivables.Adjustments!, item => item.Id == credit.Id && item.Kind == "CreditMemo");
        Assert.Contains(adjusted.Receivables.Adjustments!, item => item.Id == writeOff.Id && item.Kind == "WriteOff");

        var deposit = await transactions.RecordCustomerPaymentAsync(new RecordCustomerPaymentRequest(customer.Id, bank.Id, new DateOnly(2026, 6, 4), 25m, "DEP-ADJ-1", "ACH", []));
        Assert.True(deposit.Succeeded, deposit.ErrorMessage);
        var refund = await transactions.RefundUnappliedPaymentAsync(new RefundUnappliedPaymentRequest(deposit.Id!.Value, bank.Id, new DateOnly(2026, 6, 5), 10m, "RF-ADJ-1", "Return excess deposit"));
        Assert.True(refund.Succeeded, refund.ErrorMessage);
        var refunded = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(15m, refunded.Receivables.Payments!.Single(item => item.Id == deposit.Id).UnappliedAmount);
        Assert.Contains(refunded.Receivables.Adjustments!, item => item.Id == refund.Id && item.Kind == "CustomerDepositRefund");
        Assert.False((await transactions.ReverseSubledgerPaymentAsync(new ReverseSubledgerPaymentRequest(deposit.Id.Value, new DateOnly(2026, 6, 6), "Cannot bypass refund", "Reversed"))).Succeeded);

        Assert.True((await transactions.ReverseSubledgerAdjustmentAsync(new ReverseSubledgerAdjustmentRequest(refund.Id!.Value, new DateOnly(2026, 6, 6), "Refund entered twice"))).Succeeded);
        Assert.True((await transactions.ReverseSubledgerAdjustmentAsync(new ReverseSubledgerAdjustmentRequest(writeOff.Id!.Value, new DateOnly(2026, 6, 6), "Collection resumed"))).Succeeded);
        Assert.True((await transactions.ReverseSubledgerAdjustmentAsync(new ReverseSubledgerAdjustmentRequest(credit.Id!.Value, new DateOnly(2026, 6, 6), "Credit withdrawn"))).Succeeded);
        var restored = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(100m, restored.Receivables.Invoices.Single(item => item.Id == invoice.Id).BalanceDue);
        Assert.Equal(25m, restored.Receivables.Payments!.Single(item => item.Id == deposit.Id).UnappliedAmount);
    }

    [Fact]
    public async Task DocumentVoidsAndVendorCredits_PreserveExactReversibleLedgerHistory()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var before = await workspaceService.GetWorkspaceAsync();
        var customer = before.Receivables.Customers.First();
        var vendor = before.Payables.Vendors.First();
        var invoice = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(customer.Id, "INV-VOID-1", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 80m, 4m, "4000", "Void invoice"));
        var bill = await transactions.CreateVendorBillAsync(new CreateVendorBillRequest(vendor.Id, "B-VOID-1", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 60m, "5100", "Void bill"));
        Assert.True(invoice.Succeeded, invoice.ErrorMessage); Assert.True(bill.Succeeded, bill.ErrorMessage);
        var vendorCredit = await transactions.RecordVendorCreditAsync(new RecordVendorCreditRequest(bill.Id!.Value, new DateOnly(2026, 7, 2), 10m, "VC-VOID-1", "5100", "Vendor allowance"));
        Assert.True(vendorCredit.Succeeded, vendorCredit.ErrorMessage);
        Assert.False((await transactions.VoidVendorBillAsync(new VoidSubledgerDocumentRequest(bill.Id.Value, new DateOnly(2026, 7, 3), "Cannot void adjusted bill"))).Succeeded);
        Assert.True((await transactions.ReverseSubledgerAdjustmentAsync(new ReverseSubledgerAdjustmentRequest(vendorCredit.Id!.Value, new DateOnly(2026, 7, 3), "Allowance withdrawn"))).Succeeded);
        Assert.False((await transactions.VoidVendorBillAsync(new VoidSubledgerDocumentRequest(bill.Id.Value, new DateOnly(2026, 7, 4), "Historical adjustment still prevents void"))).Succeeded);

        var invoiceVoid = await transactions.VoidInvoiceAsync(new VoidSubledgerDocumentRequest(invoice.Id!.Value, new DateOnly(2026, 7, 2), "Invoice issued in error"));
        Assert.True(invoiceVoid.Succeeded, invoiceVoid.ErrorMessage);
        var voided = await workspaceService.GetWorkspaceAsync();
        Assert.Equal("Voided", voided.Receivables.Invoices.Single(item => item.Id == invoice.Id).Status);
        Assert.Equal(0m, voided.Receivables.Invoices.Single(item => item.Id == invoice.Id).BalanceDue);
        Assert.True((await transactions.ReverseSubledgerAdjustmentAsync(new ReverseSubledgerAdjustmentRequest(invoiceVoid.Id!.Value, new DateOnly(2026, 7, 3), "Invoice reinstated"))).Succeeded);
        var restored = await workspaceService.GetWorkspaceAsync();
        Assert.Equal("Open", restored.Receivables.Invoices.Single(item => item.Id == invoice.Id).Status);
        Assert.Equal(84m, restored.Receivables.Invoices.Single(item => item.Id == invoice.Id).BalanceDue);
    }

    [Fact]
    public async Task SubledgerWorkflow_DraftsApprovesPostsAndGeneratesRecurringDocumentsWithoutPrematureLedgerChanges()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var before = await workspaceService.GetWorkspaceAsync();
        var customer = before.Receivables.Customers.First();
        var vendor = before.Payables.Vendors.First();
        var invoiceRequest = new CreateInvoiceRequest(customer.Id, "INV-WF-1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 50m, 0m, "4000", "Workflow invoice");

        var draft = await transactions.SaveInvoiceDraftAsync(invoiceRequest);
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        var afterDraft = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.Receivables.OpenBalance, afterDraft.Receivables.OpenBalance);
        Assert.DoesNotContain(afterDraft.Receivables.Invoices, item => item.InvoiceNumber == "INV-WF-1");
        Assert.Contains(afterDraft.Receivables.Workflows!, item => item.Id == draft.Id && item.Status == "Draft");
        Assert.False((await transactions.PostApprovedSubledgerDocumentAsync(draft.Id!.Value)).Succeeded);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(draft.Id.Value)).Succeeded);
        var posted = await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value);
        Assert.True(posted.Succeeded, posted.ErrorMessage);
        var afterPost = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.Receivables.OpenBalance + 50m, afterPost.Receivables.OpenBalance);
        Assert.Contains(afterPost.Receivables.Invoices, item => item.Id == posted.Id && item.InvoiceNumber == "INV-WF-1");
        Assert.Contains(afterPost.Receivables.Workflows!, item => item.Id == draft.Id && item.Status == "Posted" && item.PostedDocumentId == posted.Id);

        var template = await transactions.SaveRecurringVendorBillTemplateAsync(new SaveRecurringVendorBillTemplateRequest(
            new CreateVendorBillRequest(vendor.Id, "RB-WF", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 16), 25m, "5100", "Monthly service"),
            "Monthly", 1, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)));
        Assert.True(template.Succeeded, template.ErrorMessage);
        Assert.True((await transactions.GenerateDueRecurringDocumentsAsync(new DateOnly(2026, 10, 1))).Succeeded);
        var generatedWorkspace = await workspaceService.GetWorkspaceAsync();
        var generated = generatedWorkspace.Payables.Workflows!.Where(item => item.SourceTemplateId == template.Id && item.Status == "Draft").OrderBy(item => item.DocumentNumber).ToArray();
        Assert.Equal(2, generated.Length);
        Assert.Equal(["RB-WF-20260901", "RB-WF-20261001"], generated.Select(item => item.DocumentNumber));
        Assert.Equal(before.Payables.OpenBalance, generatedWorkspace.Payables.OpenBalance);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(generated[0].Id)).Succeeded);
        Assert.True((await transactions.PostApprovedSubledgerDocumentAsync(generated[0].Id)).Succeeded);
        Assert.Equal(before.Payables.OpenBalance + 25m, (await workspaceService.GetWorkspaceAsync()).Payables.OpenBalance);
    }

    [Fact]
    public async Task SubledgerWorkflow_EnforcesPreparationApprovalAndPostingPermissionsSeparately()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(item => item.Id).FirstAsync(); var customerId = await db.Customers.Where(item => item.CompanyId == companyId).Select(item => item.Id).FirstAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(); var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        void ActAs(params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        var request = new CreateInvoiceRequest(customerId, "INV-WF-SOD-1", new DateOnly(2026, 8, 2), new DateOnly(2026, 9, 1), 10m, 0m, "4000", "Workflow permissions");
        ActAs(BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var draft = await transactions.SaveInvoiceDraftAsync(request); Assert.True(draft.Succeeded, draft.ErrorMessage); Assert.False((await transactions.ApproveSubledgerDocumentAsync(draft.Id!.Value)).Succeeded);
        ActAs(BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(draft.Id.Value)).Succeeded); Assert.False((await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value)).Succeeded);
        ActAs(BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost);
        Assert.True((await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value)).Succeeded);
    }

    [Fact]
    public async Task TransactionService_PostsInvoiceTaxToSalesTaxPayable_NotRevenue()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var customer = workspace.Receivables.Customers.First();

        var result = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(
            customer.Id, "INV-TAX-TEST-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m, 8m, "4000", "Tax posting test"));
        Assert.True(result.Succeeded, result.ErrorMessage);

        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var lines = await (from line in db.JournalEntryLines
                           join entry in db.JournalEntries on line.JournalEntryId equals entry.Id
                           join account in db.Accounts on line.AccountId equals account.Id
                           where entry.Reference == "INV-TAX-TEST-1"
                           select new { account.Number, line.Debit, line.Credit }).ToListAsync();
        Assert.Contains(lines, line => line.Number == "1100" && line.Debit == 108m);
        Assert.Contains(lines, line => line.Number == "4000" && line.Credit == 100m);
        Assert.Contains(lines, line => line.Number == "2100" && line.Credit == 8m);
    }

    [Fact]
    public async Task TransactionService_PostsAuthoritativeInvoiceLinesAcrossRevenueAccounts()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var setupDb = await dbContextFactory.CreateDbContextAsync())
        {
            var companyId = await setupDb.Companies.Select(company => company.Id).SingleAsync();
            setupDb.Accounts.Add(new GeneralLedgerAccount { Id = Guid.NewGuid(), CompanyId = companyId, Number = "4100", Name = "Service Revenue", Type = AccountType.Revenue, IsActive = true });
            await setupDb.SaveChangesAsync();
        }

        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var customer = (await workspaceService.GetWorkspaceAsync()).Receivables.Customers.First();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var result = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(
            customer.Id, "INV-LINES-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 9999m, 9999m, "invalid-summary-account", "Itemized invoice",
            [
                new SalesInvoiceLineRequest("Equipment", 2m, 50m, 5m, 7m, "4000"),
                new SalesInvoiceLineRequest("Installation", 3m, 20m, 0m, 3m, "4100")
            ]));

        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var invoice = await db.SalesInvoices.SingleAsync(item => item.Id == result.Id);
        Assert.Equal(155m, invoice.Subtotal);
        Assert.Equal(10m, invoice.TaxAmount);
        Assert.Equal(165m, invoice.TotalAmount);
        var documentLines = await db.SalesInvoiceLines.Where(line => line.SalesInvoiceId == invoice.Id).OrderBy(line => line.Sequence).ToListAsync();
        Assert.Collection(documentLines,
            line => { Assert.Equal(102m, line.LineTotal); Assert.Equal(5m, line.DiscountAmount); },
            line => Assert.Equal(63m, line.LineTotal));
        var postings = await (from line in db.JournalEntryLines
                              join entry in db.JournalEntries on line.JournalEntryId equals entry.Id
                              join account in db.Accounts on line.AccountId equals account.Id
                              where entry.SourceDocumentId == invoice.Id
                              select new { account.Number, line.Debit, line.Credit }).ToListAsync();
        Assert.Contains(postings, line => line.Number == "1100" && line.Debit == 165m);
        Assert.Contains(postings, line => line.Number == "4000" && line.Credit == 95m);
        Assert.Contains(postings, line => line.Number == "4100" && line.Credit == 60m);
        Assert.Contains(postings, line => line.Number == "2100" && line.Credit == 10m);
        Assert.Equal(postings.Sum(line => line.Debit), postings.Sum(line => line.Credit));
        var snapshot = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(2, snapshot.Receivables.Invoices.Single(item => item.Id == invoice.Id).Lines?.Count);
    }

    [Fact]
    public async Task TransactionService_PostsAuthoritativeBillLinesAcrossExpenseAccounts()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var setupDb = await dbContextFactory.CreateDbContextAsync())
        {
            var companyId = await setupDb.Companies.Select(company => company.Id).SingleAsync();
            setupDb.Accounts.Add(new GeneralLedgerAccount { Id = Guid.NewGuid(), CompanyId = companyId, Number = "6200", Name = "Office Expense", Type = AccountType.Expense, IsActive = true });
            await setupDb.SaveChangesAsync();
        }

        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var vendor = (await workspaceService.GetWorkspaceAsync()).Payables.Vendors.First();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var result = await transactions.CreateVendorBillAsync(new CreateVendorBillRequest(
            vendor.Id, "B-LINES-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 9999m, "invalid-summary-account", "Itemized bill",
            [
                new VendorBillLineRequest("Materials", 2m, 25m, 5m, 3m, "5100"),
                new VendorBillLineRequest("Supplies", 1m, 40m, 0m, 2m, "6200")
            ]));

        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var bill = await db.VendorBills.SingleAsync(item => item.Id == result.Id);
        Assert.Equal(90m, bill.TotalAmount);
        var documentLines = await db.VendorBillLines.Where(line => line.VendorBillId == bill.Id).OrderBy(line => line.Sequence).ToListAsync();
        Assert.Collection(documentLines, line => Assert.Equal(48m, line.LineTotal), line => Assert.Equal(42m, line.LineTotal));
        var postings = await (from line in db.JournalEntryLines
                              join entry in db.JournalEntries on line.JournalEntryId equals entry.Id
                              join account in db.Accounts on line.AccountId equals account.Id
                              where entry.SourceDocumentId == bill.Id
                              select new { account.Number, line.Debit, line.Credit }).ToListAsync();
        Assert.Contains(postings, line => line.Number == "5100" && line.Debit == 48m);
        Assert.Contains(postings, line => line.Number == "6200" && line.Debit == 42m);
        Assert.Contains(postings, line => line.Number == "2000" && line.Credit == 90m);
        Assert.Equal(postings.Sum(line => line.Debit), postings.Sum(line => line.Credit));
        var snapshot = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(2, snapshot.Payables.Bills.Single(item => item.Id == bill.Id).Lines?.Count);
    }

    [Fact]
    public async Task TransactionService_RejectsInvalidDocumentLinesWithoutPosting()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        var result = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(
            workspace.Receivables.Customers.First().Id, "INV-BAD-LINE", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 0m, 0m, "4000", "Invalid line",
            [new SalesInvoiceLineRequest("Over-discounted", 1m, 10m, 11m, 0m, "4000")]));

        Assert.False(result.Succeeded);
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        Assert.False(await db.SalesInvoices.AnyAsync(invoice => invoice.InvoiceNumber == "INV-BAD-LINE"));
        Assert.False(await db.JournalEntries.AnyAsync(entry => entry.Reference == "INV-BAD-LINE"));
    }

    [Fact]
    public async Task TransactionService_RejectsInvoiceThatExceedsCustomerCreditLimit()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var customer = workspace.Receivables.Customers.First();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        var result = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(customer.Id, "INV-CREDIT-TEST-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), customer.CreditLimit, 1m, "4000", "Credit limit test"));

        Assert.False(result.Succeeded);
        Assert.Contains("credit limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransactionService_PostsBalancedJournalEntry_AndRejectsAnImbalance()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var before = await workspaceService.GetWorkspaceAsync();

        var posted = await transactions.PostJournalEntryAsync(new PostJournalEntryRequest(new DateOnly(2026, 5, 1), "JE-TEST-1", "Journal test",
            [new JournalLineRequest("1000", 50m, 0m, "Cash adjustment"), new JournalLineRequest("4000", 0m, 50m, "Revenue adjustment")]));
        Assert.True(posted.Succeeded, posted.ErrorMessage);
        var invalid = await transactions.PostJournalEntryAsync(new PostJournalEntryRequest(new DateOnly(2026, 5, 1), "JE-TEST-2", "Invalid journal test",
            [new JournalLineRequest("1000", 50m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 40m, "Credit")]));
        Assert.False(invalid.Succeeded);
        var controlAccountJournal = await transactions.PostJournalEntryAsync(new PostJournalEntryRequest(new DateOnly(2026, 5, 1), "JE-TEST-3", "Control account journal",
            [new JournalLineRequest("1100", 50m, 0m, "Receivable adjustment"), new JournalLineRequest("4000", 0m, 50m, "Revenue adjustment")]));
        Assert.False(controlAccountJournal.Succeeded);
        Assert.Contains("control accounts", controlAccountJournal.ErrorMessage);

        var after = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance + 50m, after.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "4000").Balance + 50m, after.GeneralLedger.Accounts.Single(account => account.Number == "4000").Balance);
    }

    [Fact]
    public async Task JournalLifecycle_DraftsApprovesPostsAndReversesWithoutMutatingAuditHistory()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var before = await workspaceService.GetWorkspaceAsync();
        var date = new DateOnly(2026, 5, 2);

        var draft = await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(null, date, "JE-LIFECYCLE-1", "Lifecycle test",
            [new JournalLineRequest("1000", 75m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 70m, "Unbalanced credit")]));
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        Assert.NotNull(draft.Id);
        Assert.False((await transactions.ApproveJournalEntryAsync(draft.Id!.Value)).Succeeded);

        var balancedDraft = await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(draft.Id, date, "JE-LIFECYCLE-1", "Lifecycle test",
            [new JournalLineRequest("1000", 75m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 75m, "Balanced credit")]));
        Assert.True(balancedDraft.Succeeded, balancedDraft.ErrorMessage);
        var afterDraft = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance, afterDraft.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);
        Assert.Contains(afterDraft.GeneralLedger.RecentEntries, entry => entry.Id == draft.Id && entry.Status == "Draft");

        var approved = await transactions.ApproveJournalEntryAsync(draft.Id.Value);
        Assert.True(approved.Succeeded, approved.ErrorMessage);
        Assert.False((await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(draft.Id, date, "JE-LIFECYCLE-1", "Changed after approval",
            [new JournalLineRequest("1000", 75m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 75m, "Credit")]))).Succeeded);
        var posted = await transactions.PostApprovedJournalEntryAsync(draft.Id.Value);
        Assert.True(posted.Succeeded, posted.ErrorMessage);
        Assert.False((await transactions.PostApprovedJournalEntryAsync(draft.Id.Value)).Succeeded);

        var afterPosting = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance + 75m, afterPosting.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "4000").Balance + 75m, afterPosting.GeneralLedger.Accounts.Single(account => account.Number == "4000").Balance);

        var reversal = await transactions.ReverseJournalEntryAsync(new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 3), "Correcting lifecycle test"));
        Assert.True(reversal.Succeeded, reversal.ErrorMessage);
        Assert.False((await transactions.ReverseJournalEntryAsync(new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 3), "Duplicate reversal"))).Succeeded);
        var afterReversal = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance, afterReversal.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "4000").Balance, afterReversal.GeneralLedger.Accounts.Single(account => account.Number == "4000").Balance);

        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var original = await db.JournalEntries.SingleAsync(entry => entry.Id == draft.Id.Value);
        var reversingEntry = await db.JournalEntries.SingleAsync(entry => entry.Id == reversal.Id);
        Assert.Equal("Reversed", original.Status);
        Assert.Equal(reversingEntry.Id, original.ReversedByJournalEntryId);
        Assert.Equal(original.Id, reversingEntry.ReversalOfJournalEntryId);
        var auditActions = await db.BusinessAuditEntries.Where(entry => entry.EntityId == original.Id).Select(entry => entry.Action).ToListAsync();
        Assert.Contains("journal.draft.saved", auditActions);
        Assert.Contains("journal.approved", auditActions);
        Assert.Contains("journal.posted", auditActions);
        Assert.Contains("journal.reversed", auditActions);
    }

    [Fact]
    public async Task JournalLifecycle_EnforcesPreparationApprovalPostingAndReversalPermissionsSeparately()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(company => company.Id).FirstAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        void ActAs(string permission)
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)
            ], "test"));
            accessor.HttpContext = context;
        }

        ActAs(BrassLedgerPermissions.JournalPrepare);
        var draft = await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(null, new DateOnly(2026, 5, 6), "JE-SOD-1", "Separation of duties",
            [new JournalLineRequest("1000", 20m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 20m, "Credit")]));
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        Assert.False((await transactions.ApproveJournalEntryAsync(draft.Id!.Value)).Succeeded);

        ActAs(BrassLedgerPermissions.JournalApprove);
        Assert.True((await transactions.ApproveJournalEntryAsync(draft.Id.Value)).Succeeded);
        Assert.False((await transactions.PostApprovedJournalEntryAsync(draft.Id.Value)).Succeeded);

        ActAs(BrassLedgerPermissions.JournalPost);
        Assert.True((await transactions.PostApprovedJournalEntryAsync(draft.Id.Value)).Succeeded);
        Assert.False((await transactions.ReverseJournalEntryAsync(new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 7), "Not authorized"))).Succeeded);

        ActAs(BrassLedgerPermissions.JournalReverse);
        Assert.True((await transactions.ReverseJournalEntryAsync(new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 7), "Authorized reversal"))).Succeeded);
    }

    [Fact]
    public async Task TransactionService_PostsCashToTheSelectedBankLedgerAccount()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspace = await workspaceService.GetWorkspaceAsync();
        var customer = workspace.Receivables.Customers.First();
        var payrollBank = workspace.Treasury.BankAccounts.Single(bank => bank.LedgerAccountNumber == "1010");
        var invoice = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(customer.Id, "INV-BANK-MAP-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m, 0m, "4000", "Bank mapping"));
        Assert.True(invoice.Succeeded, invoice.ErrorMessage);
        var payment = await transactions.ApplyInvoicePaymentAsync(new ApplyInvoicePaymentRequest(invoice.Id!.Value, payrollBank.Id, new DateOnly(2026, 5, 2), 100m, "DEP-BANK-MAP-1"));
        Assert.True(payment.Succeeded, payment.ErrorMessage);

        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var postedLines = await (from line in db.JournalEntryLines
                                          join entry in db.JournalEntries on line.JournalEntryId equals entry.Id
                                          join account in db.Accounts on line.AccountId equals account.Id
                                          where entry.Reference == "DEP-BANK-MAP-1"
                                          select new { account.Number, entry.BankAccountId }).ToListAsync();
        Assert.Contains(postedLines, line => line.Number == "1010");
        Assert.DoesNotContain(postedLines, line => line.Number == "1000");
        Assert.All(postedLines, line => Assert.Equal(payrollBank.Id, line.BankAccountId));
    }

    [Fact]
    public async Task TransactionService_CalculatesPayrollTaxesFromEffectiveProfiles_WhenOverridesAreOmitted()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var before = await workspaceService.GetWorkspaceAsync();
        var bank = before.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010");

        var result = await transactions.PostPayrollRunAsync(new PostPayrollRunRequest(bank.Id, new DateOnly(2026, 5, 15), "PAY-CALCULATED", 1_000m));

        Assert.True(result.Succeeded, result.ErrorMessage);
        var after = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "2200").Balance + 226m, after.GeneralLedger.Accounts.Single(account => account.Number == "2200").Balance);
        Assert.Equal(bank.CurrentBalance - 780m, after.Treasury.BankAccounts.Single(account => account.Id == bank.Id).CurrentBalance);
        Assert.Contains(after.GeneralLedger.RecentEntries, entry => entry.SourceModule == "Payroll" && entry.TotalAmount == 1_006m);
    }

    [Fact]
    public async Task InventoryAdjustment_RecordsMovementAndBalancedInventoryPosting()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var before = await factory.CreateDbContextAsync();
        var item = await before.InventoryItems.FirstAsync(); var originalQuantity = item.QuantityOnHand;
        var result = await transactions.RecordInventoryAdjustmentAsync(new RecordInventoryAdjustmentRequest(item.Id, new DateOnly(2026, 5, 16), 3m, 12m, "INV-ADJ-1", "Cycle count increase"));
        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(originalQuantity + 3m, (await after.InventoryItems.SingleAsync(candidate => candidate.Id == item.Id)).QuantityOnHand);
        var movement = await after.InventoryTransactions.SingleAsync(transaction => transaction.JournalEntryId == result.Id);
        Assert.Equal(36m, movement.TotalCost);
        var lines = await after.JournalEntryLines.Where(line => line.JournalEntryId == result.Id).ToListAsync();
        Assert.Equal(lines.Sum(line => line.Debit), lines.Sum(line => line.Credit));
    }

    [Fact]
    public async Task EmployeePayroll_UsesWorkStateDeductionsAndWageBase_AndPersistsTraceableRun()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.Single(item => item.State == "AZ");
        var bank = workspace.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010");
        var setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Head of household", 2, 15m, 100m, 25m));
        Assert.True(setup.Succeeded, setup.ErrorMessage);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var futa = db.TaxProfiles.Single(profile => profile.TaxType == "FUTA"); futa.AnnualWageBase = 500m; futa.IsActive = true; futa.IsVerified = true;
            var arizona = db.TaxProfiles.Single(profile => profile.Jurisdiction == "Arizona"); arizona.AnnualWageBase = 500m; arizona.IsActive = true; arizona.IsVerified = true;
            await db.SaveChangesAsync();
        }

        var request = new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 5, 15), "EMP-PR-1", [new EmployeePayrollInput(employee.Id, 1_000m)]);
        var preview = await transactions.PreviewEmployeePayrollRunAsync(request);
        Assert.NotNull(preview);
        var line = Assert.Single(preview!.Employees);
        Assert.Equal("AZ", line.WorkState);
        Assert.Equal(100m, line.PreTaxDeductions);
        Assert.Equal(91.50m, line.EmployeeWithholdings); // 2026 FIT is $0 at this wage/election, plus $15 additional withholding and $76.50 employee FICA.
        Assert.Equal(25m, line.PostTaxDeductions);
        Assert.Equal(91.75m, line.EmployerPayrollTaxes); // $76.50 employer FICA plus FUTA and Arizona SUI capped at $500.
        Assert.Equal(783.50m, line.NetPay);

        var result = await transactions.PostEmployeePayrollRunAsync(request);
        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var verification = await factory.CreateDbContextAsync();
        var run = await verification.PayrollRuns.SingleAsync(run => run.Id == result.Id);
        var persistedLine = await verification.PayrollRunEmployeeLines.SingleAsync(item => item.PayrollRunId == run.Id);
        var journal = await verification.JournalEntries.SingleAsync(entry => entry.SourceDocumentId == run.Id && entry.SourceDocumentType == "PayrollRun");
        Assert.Equal(783.50m, run.NetPay);
        Assert.Equal(employee.Id, persistedLine.EmployeeId);
        Assert.Equal(run.Id, journal.SourceDocumentId);
    }

    [Fact]
    public async Task PayrollRun_RequiresLifecycleAndPersistsDetailedAuditableReversal()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.Single(item => item.State == "AZ");
        var bankId = workspace.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010").Id;
        decimal originalBankBalance;
        await using (var before = await factory.CreateDbContextAsync()) originalBankBalance = await before.BankAccounts.Where(account => account.Id == bankId).Select(account => account.CurrentBalance).SingleAsync();

        var request = new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 5), "PR-LIFECYCLE-1",
        [
            new EmployeePayrollInput(employee.Id, 0,
            [
                new PayrollEarningInput("REG", "Regular", 40, 25, 1_000m, true, new DateOnly(2026, 5, 29), "AZ", "Maricopa", "Phoenix"),
                new PayrollEarningInput("OT", "Overtime", 5, 37.50m, 187.50m, true, new DateOnly(2026, 5, 30), "NV", "Clark", "Las Vegas")
            ],
            [
                new PayrollDeductionInput("401K", "Retirement", 50m, 25m, true),
                new PayrollDeductionInput("GARN", "Garnishment", 25m)
            ])
        ], new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 30));

        var invalidLiabilityAccount = await transactions.SaveEmployeePayrollRunDraftAsync(request with { Reference = "PR-INVALID-LIABILITY", Employees = [new EmployeePayrollInput(employee.Id, 1_000m, Deductions: [new PayrollDeductionInput("BAD", "Invalid account", 10m, LiabilityAccountNumber: "1000")])] });
        Assert.False(invalidLiabilityAccount.Succeeded);
        Assert.Contains("liability account", invalidLiabilityAccount.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var draftResult = await transactions.SaveEmployeePayrollRunDraftAsync(request);
        Assert.True(draftResult.Succeeded, draftResult.ErrorMessage);
        PayrollRun draft;
        await using (var verification = await factory.CreateDbContextAsync())
        {
            draft = await verification.PayrollRuns.SingleAsync(run => run.Id == draftResult.Id);
            Assert.Equal("Draft", draft.Status);
            Assert.Null(draft.JournalEntryId);
            Assert.Equal(new DateOnly(2026, 5, 24), draft.PeriodStart);
            Assert.Equal(1_187.50m, draft.GrossPayroll);
            Assert.Equal(25m, draft.EmployerBenefitContributions);
            var employeeLine = await verification.PayrollRunEmployeeLines.SingleAsync(line => line.PayrollRunId == draft.Id);
            Assert.Equal(2, await verification.PayrollEarningLines.CountAsync(line => line.PayrollRunEmployeeLineId == employeeLine.Id));
            Assert.Equal(2, await verification.PayrollDeductionLines.CountAsync(line => line.PayrollRunEmployeeLineId == employeeLine.Id));
            Assert.NotEmpty(await verification.PayrollTaxLines.Where(line => line.PayrollRunEmployeeLineId == employeeLine.Id).ToListAsync());
            Assert.Equal(originalBankBalance, await verification.BankAccounts.Where(account => account.Id == bankId).Select(account => account.CurrentBalance).SingleAsync());
        }

        var staleApproval = await transactions.ApprovePayrollRunAsync(new ApprovePayrollRunRequest(draft.Id, "stale"));
        Assert.False(staleApproval.Succeeded);
        var approval = await transactions.ApprovePayrollRunAsync(new ApprovePayrollRunRequest(draft.Id, draft.ConcurrencyToken));
        Assert.True(approval.Succeeded, approval.ErrorMessage);

        string approvedToken;
        await using (var verification = await factory.CreateDbContextAsync())
        {
            var approved = await verification.PayrollRuns.SingleAsync(run => run.Id == draft.Id);
            Assert.Equal("Approved", approved.Status);
            approvedToken = approved.ConcurrencyToken;
        }
        Assert.False((await transactions.PostApprovedPayrollRunAsync(new PostApprovedPayrollRunRequest(draft.Id, "stale"))).Succeeded);
        var posting = await transactions.PostApprovedPayrollRunAsync(new PostApprovedPayrollRunRequest(draft.Id, approvedToken));
        Assert.True(posting.Succeeded, posting.ErrorMessage);

        string postedToken;
        decimal netPay;
        Guid journalId;
        await using (var verification = await factory.CreateDbContextAsync())
        {
            var posted = await verification.PayrollRuns.SingleAsync(run => run.Id == draft.Id);
            Assert.Equal("Posted", posted.Status);
            journalId = posted.JournalEntryId!.Value;
            netPay = posted.NetPay;
            postedToken = posted.ConcurrencyToken;
            Assert.Equal(originalBankBalance - netPay, await verification.BankAccounts.Where(account => account.Id == bankId).Select(account => account.CurrentBalance).SingleAsync());
            var journalLines = await verification.JournalEntryLines.Where(line => line.JournalEntryId == journalId).ToListAsync();
            Assert.Equal(journalLines.Sum(line => line.Debit), journalLines.Sum(line => line.Credit));
            var liabilities = await verification.PayrollLiabilities.Where(liability => liability.PayrollRunId == draft.Id).ToListAsync();
            Assert.NotEmpty(liabilities);
            Assert.All(liabilities, liability => Assert.Equal((liability.OriginalAmount, "Open"), (liability.OutstandingAmount, liability.Status)));
            Assert.Equal(draft.PreTaxDeductions + draft.EmployeeWithholdings + draft.PostTaxDeductions + draft.EmployerPayrollTaxes + draft.EmployerBenefitContributions, liabilities.Sum(liability => liability.OriginalAmount));
            var expenseAccountId = await verification.Accounts.Where(account => account.Number == "6100").Select(account => account.Id).SingleAsync();
            Assert.Equal(draft.GrossPayroll + draft.EmployerPayrollTaxes + draft.EmployerBenefitContributions, journalLines.Single(line => line.AccountId == expenseAccountId).Debit);
            var employeePayment = await verification.PayrollEmployeePayments.SingleAsync(payment => payment.PayrollRunId == draft.Id);
            Assert.Equal((employee.Id, netPay, "Issued"), (employeePayment.EmployeeId, employeePayment.Amount, employeePayment.Status));
            Assert.Equal(draft.GrossPayroll, employeePayment.YearToDateGross);
            Assert.Equal(draft.EmployeeWithholdings, employeePayment.YearToDateEmployeeTaxes);
            await verification.Database.OpenConnectionAsync();
            await using var paymentCommand = verification.Database.GetDbConnection().CreateCommand();
            paymentCommand.CommandText = "SELECT EmployeeName FROM PayrollEmployeePayments WHERE Id = $id";
            var paymentParameter = paymentCommand.CreateParameter(); paymentParameter.ParameterName = "$id"; paymentParameter.Value = employeePayment.Id; paymentCommand.Parameters.Add(paymentParameter);
            var storedEmployeeName = (await paymentCommand.ExecuteScalarAsync())?.ToString() ?? string.Empty;
            Assert.StartsWith("enc::", storedEmployeeName);
            Assert.DoesNotContain(employeePayment.EmployeeName, storedEmployeeName);
        }

        var reporting = scope.ServiceProvider.GetRequiredService<IPayrollReportingService>();
        var register = await reporting.GetRegisterAsync(draft.Id);
        Assert.NotNull(register);
        Assert.Equal(register!.NetPay, register.Employees.Sum(item => item.NetPay));
        Assert.Equal(draft.GrossPayroll, register.GrossPayroll);
        var statement = await reporting.GetPayStatementAsync(draft.Id, employee.Id);
        Assert.NotNull(statement);
        Assert.Equal(statement!.GrossPay, statement.Earnings.Sum(item => item.Amount));
        Assert.Equal(statement.NetPay, statement.GrossPay - statement.PreTaxDeductions - statement.EmployeeWithholdings - statement.PostTaxDeductions);
        Assert.Equal(statement.EmployeeWithholdings, statement.Taxes.Sum(item => item.EmployeeAmount));
        var registerCsv = await reporting.ExportRegisterCsvAsync(draft.Id);
        Assert.Contains("\"TOTAL\"", registerCsv);
        Assert.Contains(draft.NetPay.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), registerCsv);

        var postedWorkspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var payable = postedWorkspace.Payroll.Liabilities!.First(liability => liability.Status == "Open");
        var overpayment = await transactions.RecordPayrollLiabilityPaymentAsync(new RecordPayrollLiabilityPaymentRequest(bankId, new DateOnly(2026, 6, 6), "LIABILITY-OVERPAY", "Tax agency", "EFT", [new PayrollLiabilityPaymentApplicationInput(payable.Id, payable.OutstandingAmount + .01m)]));
        Assert.False(overpayment.Succeeded);
        var remittance = await transactions.RecordPayrollLiabilityPaymentAsync(new RecordPayrollLiabilityPaymentRequest(bankId, new DateOnly(2026, 6, 6), "LIABILITY-PAY-1", "Tax agency", "EFT", [new PayrollLiabilityPaymentApplicationInput(payable.Id, payable.OutstandingAmount)]));
        Assert.True(remittance.Succeeded, remittance.ErrorMessage);
        var afterRemittance = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        Assert.Equal("Paid", afterRemittance.Payroll.Liabilities!.Single(liability => liability.Id == payable.Id).Status);
        var payment = afterRemittance.Payroll.LiabilityPayments!.Single(item => item.Id == remittance.Id);
        Assert.Equal(payable.OutstandingAmount, payment.Amount);
        Assert.False((await transactions.ReversePayrollRunAsync(new ReversePayrollRunRequest(draft.Id, new DateOnly(2026, 6, 6), "Blocked while remitted", postedToken))).Succeeded);
        var paymentReversal = await transactions.ReversePayrollLiabilityPaymentAsync(new ReversePayrollLiabilityPaymentRequest(payment.Id, new DateOnly(2026, 6, 6), "Correct remittance selection", payment.ConcurrencyToken));
        Assert.True(paymentReversal.Succeeded, paymentReversal.ErrorMessage);
        var afterPaymentReversal = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        Assert.Equal("Open", afterPaymentReversal.Payroll.Liabilities!.Single(liability => liability.Id == payable.Id).Status);
        Assert.Equal("Reversed", afterPaymentReversal.Payroll.LiabilityPayments!.Single(item => item.Id == payment.Id).Status);

        var reversal = await transactions.ReversePayrollRunAsync(new ReversePayrollRunRequest(draft.Id, new DateOnly(2026, 6, 6), "Incorrect overtime location", postedToken));
        Assert.True(reversal.Succeeded, reversal.ErrorMessage);
        await using (var verification = await factory.CreateDbContextAsync())
        {
            var reversed = await verification.PayrollRuns.SingleAsync(run => run.Id == draft.Id);
            Assert.Equal("Reversed", reversed.Status);
            Assert.NotNull(reversed.ReversalJournalEntryId);
            Assert.Equal(originalBankBalance, await verification.BankAccounts.Where(account => account.Id == bankId).Select(account => account.CurrentBalance).SingleAsync());
            var reversalLines = await verification.JournalEntryLines.Where(line => line.JournalEntryId == reversed.ReversalJournalEntryId).ToListAsync();
            Assert.Equal(reversalLines.Sum(line => line.Debit), reversalLines.Sum(line => line.Credit));
            Assert.All(await verification.PayrollLiabilities.Where(liability => liability.PayrollRunId == draft.Id).ToListAsync(), liability => Assert.Equal((0m, "Reversed"), (liability.OutstandingAmount, liability.Status)));
            Assert.All(await verification.PayrollEmployeePayments.Where(payment => payment.PayrollRunId == draft.Id).ToListAsync(), payment => Assert.Equal("Reversed", payment.Status));
            Assert.Equal(4, await verification.BusinessAuditEntries.CountAsync(entry => entry.EntityType == "PayrollRun" && entry.EntityId == draft.Id));
        }
    }

    [Fact]
    public async Task PayrollFilings_ReconcileProtectDataDetectSourceChangesAndLockClosedPeriods()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var filings = scope.ServiceProvider.GetRequiredService<IPayrollFilingService>();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.First();
        var bankId = workspace.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010").Id;
        var protectedDetails = await transactions.SaveEmployeeEmploymentDetailsAsync(new SaveEmployeeEmploymentDetailsRequest(employee.Id, "1 Main St", "", "85001", "Maricopa", "", "Maricopa", "", new DateOnly(2024, 1, 1), null, 25m, 37.5m, false, "", "123-45-6789", "", "", ConcurrencyToken: employee.ConcurrencyToken, AddressCity: "Phoenix", AddressState: "AZ"));
        Assert.True(protectedDetails.Succeeded, protectedDetails.ErrorMessage);

        var firstRun = await transactions.PostEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 4, 10), "FILING-Q2-1", [new EmployeePayrollInput(employee.Id, 1_000m,
        [
            new PayrollEarningInput("REG", "Regular", 0, 0, 800m),
            new PayrollEarningInput("TIPS", "Tips", 0, 0, 100m, W2Reporting: new(100m, 100m, TreasuryTippedOccupationCodes: ["101"])),
            new PayrollEarningInput("OT", "Overtime", 0, 0, 100m, W2Reporting: new(QualifiedOvertimeCompensation: 33.33m))
        ])], new DateOnly(2026, 3, 29), new DateOnly(2026, 4, 4)));
        Assert.True(firstRun.Succeeded, firstRun.ErrorMessage);
        var filingDraft = await filings.SaveDraftAsync(new SavePayrollFilingDraftRequest(null, "941", 2026, 2));
        Assert.True(filingDraft.Succeeded, filingDraft.ErrorMessage);
        var draft = await filings.GetFilingAsync(filingDraft.Id!.Value);
        Assert.NotNull(draft);
        Assert.Equal("Draft", draft!.Status);
        Assert.Equal(2, draft.Data.GetProperty("Quarter").GetInt32());
        Assert.True(draft.Data.GetProperty("WagesTipsAndOtherCompensation").GetDecimal() > 0);
        Assert.Equal(draft.Data.GetProperty("TotalTaxesBeforeAdjustments").GetDecimal(), draft.Data.GetProperty("BalanceDue").GetDecimal());

        var changedSource = await transactions.PostEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 5, 8), "FILING-Q2-2", [new EmployeePayrollInput(employee.Id, 750m)], new DateOnly(2026, 4, 26), new DateOnly(2026, 5, 2)));
        Assert.True(changedSource.Succeeded, changedSource.ErrorMessage);
        var staleApproval = await filings.ApproveAsync(new ApprovePayrollFilingRequest(draft.Id, draft.ConcurrencyToken));
        Assert.False(staleApproval.Succeeded);
        Assert.Contains("changed", staleApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var regenerated = await filings.SaveDraftAsync(new SavePayrollFilingDraftRequest(draft.Id, "941", 2026, 2, draft.ConcurrencyToken));
        Assert.True(regenerated.Succeeded, regenerated.ErrorMessage);
        draft = await filings.GetFilingAsync(draft.Id);
        var approved = await filings.ApproveAsync(new ApprovePayrollFilingRequest(draft!.Id, draft.ConcurrencyToken));
        Assert.True(approved.Succeeded, approved.ErrorMessage);
        var lockedPayroll = await transactions.SaveEmployeePayrollRunDraftAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 5), "FILING-Q2-LOCKED", [new EmployeePayrollInput(employee.Id, 500m)]));
        Assert.False(lockedPayroll.Succeeded);
        Assert.Contains("filing", lockedPayroll.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var close = await filings.ClosePeriodAsync(new ClosePayrollPeriodRequest("Quarter", 2026, 2));
        Assert.True(close.Succeeded, close.ErrorMessage);
        var closePeriod = Assert.Single(await filings.GetClosePeriodsAsync());
        Assert.Equal("Closed", closePeriod.Status);
        var approvedFiling = await filings.GetFilingAsync(draft.Id);
        Assert.False((await filings.ReopenFilingAsync(new ReopenPayrollFilingRequest(draft.Id, "Correction required", approvedFiling!.ConcurrencyToken))).Succeeded);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT DataJson FROM PayrollFilings WHERE Id = $id";
            var parameter = command.CreateParameter(); parameter.ParameterName = "$id"; parameter.Value = draft.Id; command.Parameters.Add(parameter);
            var stored = (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty;
            Assert.StartsWith("enc::", stored);
            Assert.DoesNotContain("123456789", stored);
        }

        var reopenedPeriod = await filings.ReopenPeriodAsync(new ReopenPayrollPeriodRequest(closePeriod.Id, "Post approved correction", closePeriod.ConcurrencyToken));
        Assert.True(reopenedPeriod.Succeeded, reopenedPeriod.ErrorMessage);
        approvedFiling = await filings.GetFilingAsync(draft.Id);
        Assert.True((await filings.ReopenFilingAsync(new ReopenPayrollFilingRequest(draft.Id, "Regenerate after correction", approvedFiling!.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = await db.PayrollRuns.SingleAsync(item => item.Id == firstRun.Id);
            Assert.True((await transactions.ReversePayrollRunAsync(new ReversePayrollRunRequest(run.Id, new DateOnly(2026, 6, 10), "Quarter correction", run.ConcurrencyToken))).Succeeded);
        }

        var invalidClaim = await filings.SaveForm941CorrectionDraftAsync(new SaveForm941CorrectionDraftRequest(null, draft.Id, "Claim", new DateOnly(2026, 6, 10), "Correct payroll taxes after reversing the duplicated payroll run.", "None", "UnderreportedOnly", "", false, ""));
        Assert.False(invalidClaim.Succeeded);
        Assert.Contains("federal-income-tax", invalidClaim.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var correctionRequest = new SaveForm941CorrectionDraftRequest(null, draft.Id, "Claim", new DateOnly(2026, 6, 10), "Correct payroll taxes after reversing the duplicated payroll run.", "SameYearRepaid", "RepaidOrReimbursed", "EMPLOYEE-REFUND-BATCH-20260610", true, "W2C-BATCH-2026-Q2");
        var correctionDraft = await filings.SaveForm941CorrectionDraftAsync(correctionRequest);
        Assert.True(correctionDraft.Succeeded, correctionDraft.ErrorMessage);
        var correction = await filings.GetCorrectionAsync(correctionDraft.Id!.Value);
        Assert.NotNull(correction); Assert.Equal("Draft", correction!.Status); Assert.Equal("Claim", correction.Process);
        Assert.True(correction.Data.GetProperty("CreditOrRefund").GetDecimal() > 0m);
        Assert.All(correction.Data.GetProperty("Lines").EnumerateArray(), line => Assert.Equal(line.GetProperty("CorrectedAmount").GetDecimal() - line.GetProperty("OriginallyReported").GetDecimal(), line.GetProperty("Difference").GetDecimal()));
        await using (var db = await factory.CreateDbContextAsync()) { var changedEmployee = await db.Employees.SingleAsync(item => item.Id == employee.Id); changedEmployee.AddressLine2 = "Suite 2"; changedEmployee.ConcurrencyToken = Guid.NewGuid().ToString("N"); await db.SaveChangesAsync(); }
        var staleCorrection = await filings.ApproveForm941CorrectionAsync(new ApproveForm941CorrectionRequest(correction.Id, correction.ConcurrencyToken));
        Assert.False(staleCorrection.Succeeded); Assert.Contains("changed", staleCorrection.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True((await filings.SaveForm941CorrectionDraftAsync(correctionRequest with { CorrectionId = correction.Id, ConcurrencyToken = correction.ConcurrencyToken })).Succeeded);
        correction = await filings.GetCorrectionAsync(correction.Id);
        Assert.True((await filings.ApproveForm941CorrectionAsync(new ApproveForm941CorrectionRequest(correction!.Id, correction.ConcurrencyToken))).Succeeded);
        correction = Assert.Single(await filings.GetCorrectionsAsync()); Assert.Equal("Approved", correction.Status); Assert.Equal(1, correction.Sequence);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.OpenConnectionAsync(); await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT DataJson FROM PayrollFilingCorrections WHERE Id = $id"; var parameter = command.CreateParameter(); parameter.ParameterName = "$id"; parameter.Value = correction.Id; command.Parameters.Add(parameter);
            Assert.StartsWith("enc::", (await command.ExecuteScalarAsync())?.ToString());
        }
        var additionalCorrectionRun = await transactions.PostEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 12), "FILING-Q2-CORRECTION-2", [new EmployeePayrollInput(employee.Id, 2_000m,
        [
            new PayrollEarningInput("REG", "Regular", 0, 0, 1_800m),
            new PayrollEarningInput("TIPS", "Tips", 0, 0, 100m, W2Reporting: new(100m, 100m, TreasuryTippedOccupationCodes: ["101"])),
            new PayrollEarningInput("OT", "Overtime", 0, 0, 100m, W2Reporting: new(QualifiedOvertimeCompensation: 33.33m))
        ])], new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 6)));
        Assert.True(additionalCorrectionRun.Succeeded, additionalCorrectionRun.ErrorMessage);
        var mixedClaim = await filings.SaveForm941CorrectionDraftAsync(correctionRequest with { CorrectionId = null, Explanation = "Report the subsequently identified underreported payroll taxes." });
        Assert.False(mixedClaim.Succeeded); Assert.Contains("claim process", mixedClaim.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var secondDraft = await filings.SaveForm941CorrectionDraftAsync(correctionRequest with { CorrectionId = null, Process = "Adjustment", Explanation = "Report the subsequently identified underreported payroll taxes.", EmployeeCertificationCode = "UnderreportedOnly", EmployeeCertificationEvidenceReference = "", ConcurrencyToken = "" });
        Assert.True(secondDraft.Succeeded, secondDraft.ErrorMessage);
        var secondCorrection = await filings.GetCorrectionAsync(secondDraft.Id!.Value); Assert.Equal(2, secondCorrection!.Sequence);
        Assert.True((await filings.VoidForm941CorrectionAsync(new VoidForm941CorrectionRequest(secondCorrection.Id, "Incorrect explanation selected", secondCorrection.ConcurrencyToken))).Succeeded);
        var replacementDraft = await filings.SaveForm941CorrectionDraftAsync(correctionRequest with { CorrectionId = null, Process = "Adjustment", Explanation = "Report the subsequently identified underreported payroll taxes with corrected support.", EmployeeCertificationCode = "UnderreportedOnly", EmployeeCertificationEvidenceReference = "", ConcurrencyToken = "" });
        Assert.True(replacementDraft.Succeeded, replacementDraft.ErrorMessage);
        var replacementCorrection = await filings.GetCorrectionAsync(replacementDraft.Id!.Value); Assert.Equal(3, replacementCorrection!.Sequence);
        Assert.True((await filings.ApproveForm941CorrectionAsync(new ApproveForm941CorrectionRequest(replacementCorrection.Id, replacementCorrection.ConcurrencyToken))).Succeeded);
        var correctionHistory = await filings.GetCorrectionsAsync(); Assert.Equal(3, correctionHistory.Count); Assert.Contains(correctionHistory, item => item.Sequence == 2 && item.Status == "Voided" && item.VoidReason == "Incorrect explanation selected");

        var w2 = await filings.SaveDraftAsync(new SavePayrollFilingDraftRequest(null, "W2/W3", 2026));
        Assert.True(w2.Succeeded, w2.ErrorMessage);
        var w2Filing = await filings.GetFilingAsync(w2.Id!.Value);
        var w2Employees = w2Filing!.Data.GetProperty("Employees");
        Assert.NotEmpty(w2Employees.EnumerateArray());
        Assert.Equal("123456789", w2Employees.EnumerateArray().First().GetProperty("SocialSecurityNumber").GetString());
        Assert.Equal(w2Filing.Data.GetProperty("W3Box1Total").GetDecimal(), w2Employees.EnumerateArray().Sum(item => item.GetProperty("Box1WagesTipsOtherCompensation").GetDecimal()));
        var tippedEmployee = w2Employees.EnumerateArray().First();
        Assert.Equal(100m, tippedEmployee.GetProperty("Box7SocialSecurityTips").GetDecimal());
        Assert.Equal(100m, tippedEmployee.GetProperty("Box12Amounts").EnumerateArray().Single(item => item.GetProperty("Code").GetString() == "TP").GetProperty("Amount").GetDecimal());
        Assert.Equal(33.33m, tippedEmployee.GetProperty("Box12Amounts").EnumerateArray().Single(item => item.GetProperty("Code").GetString() == "TT").GetProperty("Amount").GetDecimal());
        Assert.Equal("101", tippedEmployee.GetProperty("TreasuryTippedOccupationCodes")[0].GetString());
        Assert.Equal(100m, w2Filing.Data.GetProperty("W3Box7Total").GetDecimal());
        Assert.True((await filings.ApproveAsync(new ApprovePayrollFilingRequest(w2Filing.Id, w2Filing.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var changedEmployee = await db.Employees.SingleAsync(item => item.Id == employee.Id);
            changedEmployee.LastName = "Corrected";
            changedEmployee.AddressLine2 = "Suite 300";
            changedEmployee.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }
        var w2cRequest = new SaveW2CorrectionDraftRequest(null, w2Filing.Id, new DateOnly(2026, 8, 25), "Correct the employee name and address retained in the approved wage statement baseline.", true, "EMPLOYEE-W2C-DELIVERY-20260825");
        var w2cDraft = await filings.SaveW2CorrectionDraftAsync(w2cRequest);
        Assert.True(w2cDraft.Succeeded, w2cDraft.ErrorMessage);
        var w2c = await filings.GetCorrectionAsync(w2cDraft.Id!.Value);
        Assert.NotNull(w2c); Assert.Equal("W-2c/W-3c", w2c!.FormCode); Assert.Equal(0, w2c.Quarter); Assert.Equal("Draft", w2c.Status);
        var correctedEmployee = Assert.Single(w2c.Data.GetProperty("Employees").EnumerateArray());
        Assert.True(correctedEmployee.GetProperty("SubmitToSsa").GetBoolean());
        Assert.Equal("Corrected", correctedEmployee.GetProperty("CorrectInformation").GetProperty("EmployeeName").GetString()!.Split(' ').Last());
        Assert.Equal(w2c.Data.GetProperty("W3cCorrectBox1Total").GetDecimal(), correctedEmployee.GetProperty("CorrectInformation").GetProperty("Box1WagesTipsOtherCompensation").GetDecimal());
        Assert.True((await filings.ApproveW2CorrectionAsync(new ApproveW2CorrectionRequest(w2c.Id, w2c.ConcurrencyToken))).Succeeded);
        Assert.False((await filings.SaveW2CorrectionDraftAsync(w2cRequest)).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var addressCorrection = await db.Employees.SingleAsync(item => item.Id == employee.Id);
            addressCorrection.AddressLine2 = "Suite 301";
            addressCorrection.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }
        var addressOnlyDraft = await filings.SaveW2CorrectionDraftAsync(w2cRequest with { Explanation = "Correct only the employee delivery address after the prior identity correction." });
        Assert.True(addressOnlyDraft.Succeeded, addressOnlyDraft.ErrorMessage);
        var addressOnly = await filings.GetCorrectionAsync(addressOnlyDraft.Id!.Value);
        Assert.Equal(2, addressOnly!.Sequence);
        var addressOnlyEmployee = Assert.Single(addressOnly.Data.GetProperty("Employees").EnumerateArray());
        Assert.False(addressOnlyEmployee.GetProperty("SubmitToSsa").GetBoolean());
        Assert.Contains("address-only", addressOnlyEmployee.GetProperty("SubmissionReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmployeeProtectedDetails_AreValidatedEncryptedMaskedAndConcurrencyControlled()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var employee = (await workspaceService.GetWorkspaceAsync()).Payroll.Employees.First();

        var invalid = await transactions.SaveEmployeeEmploymentDetailsAsync(new SaveEmployeeEmploymentDetailsRequest(employee.Id, "1 Main St", "", "48201", "Wayne", "Detroit", "Wayne", "Detroit", new DateOnly(2024, 1, 1), null, 30m, 45m, true, "Checking", "123-45-6789", "123456789", "12345678", ConcurrencyToken: employee.ConcurrencyToken));
        Assert.False(invalid.Succeeded);
        Assert.Contains("ABA routing", invalid.ErrorMessage);

        var saved = await transactions.SaveEmployeeEmploymentDetailsAsync(new SaveEmployeeEmploymentDetailsRequest(employee.Id, "1 Main St", "Unit 2", "48201", "Wayne", "Detroit", "Oakland", "Royal Oak", new DateOnly(2024, 1, 1), null, 30m, 45m, true, "Checking", "123-45-6789", "021000021", "12345678", ConcurrencyToken: employee.ConcurrencyToken, DirectDepositAuthorizationOn: new DateOnly(2026, 1, 15), DirectDepositAuthorizationReference: "Signed authorization EMP-001", AddressCity: "Detroit", AddressState: "MI"));
        Assert.True(saved.Succeeded, saved.ErrorMessage);

        var refreshed = (await workspaceService.GetWorkspaceAsync()).Payroll.Employees.Single(candidate => candidate.Id == employee.Id);
        Assert.True(refreshed.HasSocialSecurityNumber);
        Assert.True(refreshed.HasBankAccount);
        Assert.True(refreshed.DirectDepositEnabled);
        Assert.Equal("Wayne", refreshed.ResidenceCounty);
        Assert.NotEqual(employee.ConcurrencyToken, refreshed.ConcurrencyToken);
        var stale = await transactions.SaveEmployeeEmploymentDetailsAsync(new SaveEmployeeEmploymentDetailsRequest(employee.Id, "1 Main St", "", "48201", "Wayne", "", "", "", null, null, 30m, 45m, false, "", ConcurrencyToken: employee.ConcurrencyToken));
        Assert.False(stale.Succeeded);
        Assert.Contains("changed", stale.ErrorMessage);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT SocialSecurityNumber || '|' || BankRoutingNumber || '|' || BankAccountNumber || '|' || AddressLine1 || '|' || AddressLine2 || '|' || AddressCity || '|' || AddressState || '|' || PostalCode FROM Employees WHERE EmployeeNumber = $employeeNumber";
        var parameter = command.CreateParameter(); parameter.ParameterName = "$employeeNumber"; parameter.Value = employee.EmployeeNumber; command.Parameters.Add(parameter);
        var stored = (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty;
        Assert.DoesNotContain("123-45-6789", stored);
        Assert.DoesNotContain("021000021", stored);
        Assert.DoesNotContain("1 Main St", stored);
        Assert.DoesNotContain("Detroit", stored);
        Assert.All(stored.Split('|'), value => Assert.StartsWith("enc::", value));
        var audit = await db.BusinessAuditEntries.SingleAsync(entry => entry.EntityType == "Employee" && entry.EntityId == employee.Id && entry.Action == "employee.protected-details.updated");
        Assert.DoesNotContain("123456789", audit.DetailJson);
        Assert.DoesNotContain("021000021", audit.DetailJson);
        Assert.DoesNotContain("12345678", audit.DetailJson);

        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.WorkspaceView);
        var restrictedPayroll = (await workspaceService.GetWorkspaceAsync()).Payroll;
        Assert.Empty(restrictedPayroll.Employees);
        Assert.Empty(restrictedPayroll.Runs!);
        Assert.Empty(restrictedPayroll.Timecards!);
        Assert.Empty(restrictedPayroll.Liabilities!);

        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.PayrollSensitiveData);
        var authorized = (await workspaceService.GetWorkspaceAsync()).Payroll.Employees.Single(candidate => candidate.Id == employee.Id);
        Assert.Equal("1 Main St", authorized.AddressLine1);
        Assert.Equal("Detroit", authorized.AddressCity);
        Assert.Equal("MI", authorized.AddressState);
        Assert.True(authorized.HasSocialSecurityNumber);
        Assert.True(authorized.HasBankAccount);
    }

    [Fact]
    public void SsaEfw2Builder_UsesExact2026RecordPositionsZeroFillAndTotals()
    {
        var employee = new W2EmployeeData(Guid.NewGuid(), "E-001", "Jane Employee", "123-45-6789", "10 Home St", "Unit 2", "48201-1234", 1100m, 110m, 1000m, 62m, 1100m, 15.95m, [], "Jane", "Q", "Employee", "Detroit", "MI");
        var package = new W2PackageData(TaxYear: 2026, EmployerLegalName: "Brass Ledger Test Company", EmployerEin: "12-3456789", Employees: [employee], W3Box1Total: 1100m, W3Box2Total: 110m, W3Box3Total: 1000m, W3Box4Total: 62m, W3Box5Total: 1100m, W3Box6Total: 15.95m);
        var submitter = new SsaEfw2Submitter(2026, "EFW2 TY2026 initial publication (2026-07-07)", "https://www.ssa.gov/employer/efw/26efw2.pdf", "12-3456789", "AB123456", "Brass Ledger Test Company", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com", "L", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com");

        var result = SsaEfw2FileBuilder.Build(package, submitter);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors)); Assert.Equal(5, result.RecordCount); Assert.Equal(1, result.EmployeeRecordCount);
        var records = System.Text.Encoding.ASCII.GetString(result.Content).Split("\r\n"); Assert.All(records, record => Assert.Equal(512, record.Length));
        Assert.Equal("RA", records[0][..2]); Assert.Equal("123456789", records[0][2..11]); Assert.Equal("AB123456", records[0][11..19]); Assert.Equal("98", records[0][35..37]); Assert.Equal("L", records[0][499..500]);
        Assert.Equal("RE", records[1][..2]); Assert.Equal("2026", records[1][2..6]); Assert.Equal("123456789", records[1][7..16]); Assert.Equal("N", records[1][173..174]); Assert.Equal("R", records[1][218..219]); Assert.Equal(new string(' ', 10), records[1][318..328]);
        Assert.Equal("RW", records[2][..2]); Assert.Equal("123456789", records[2][2..11]); Assert.Equal("JANE", records[2][11..26].Trim()); Assert.Equal("EMPLOYEE", records[2][41..61].Trim()); Assert.Equal("00000110000", records[2][187..198]); Assert.Equal("00000011000", records[2][198..209]); Assert.Equal("00000000000", records[2][253..264]); Assert.Equal(new string(' ', 6), records[2][489..495]);
        Assert.Equal("RT", records[3][..2]); Assert.Equal("0000001", records[3][2..9]); Assert.Equal("000000000110000", records[3][9..24]); Assert.Equal("000000000011000", records[3][24..39]); Assert.Equal("000000000000000", records[3][99..114]);
        Assert.Equal("RF", records[4][..2]); Assert.Equal("000000001", records[4][7..16]);
        Assert.False(SsaEfw2FileBuilder.Build(package with { W3Box1Total = 1099m }, submitter).Succeeded);
        Assert.False(SsaEfw2FileBuilder.Build(package with { Employees = [employee with { SocialSecurityNumber = "900000000" }] }, submitter).Succeeded);
        Assert.False(SsaEfw2FileBuilder.Build(package with { TaxYear = 2025 }, submitter).Succeeded);
    }

    [Fact]
    public void SsaEfw2Builder_Emits2026TipAndOvertimeOptionalRecordsAndTotals()
    {
        var employee = new W2EmployeeData(Guid.NewGuid(), "E-001", "Jane Employee", "123-45-6789", "10 Home St", "", "48201", 1200m, 110m, 1000m, 62m, 1100m, 15.95m, [], "Jane", "", "Employee", "Detroit", "MI", 100m, [new("TP", 100m), new("TT", 50m)], ["101", "102"]);
        var package = new W2PackageData(TaxYear: 2026, EmployerLegalName: "Brass Ledger Test Company", EmployerEin: "12-3456789", Employees: [employee], W3Box1Total: 1200m, W3Box2Total: 110m, W3Box3Total: 1000m, W3Box4Total: 62m, W3Box5Total: 1100m, W3Box6Total: 15.95m, W3Box7Total: 100m);
        var submitter = new SsaEfw2Submitter(2026, "EFW2 TY2026 initial publication (2026-07-07)", "https://www.ssa.gov/employer/efw/26efw2.pdf", "12-3456789", "AB123456", "Brass Ledger Test Company", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com", "L", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com");

        var result = SsaEfw2FileBuilder.Build(package, submitter);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors)); Assert.Equal(7, result.RecordCount);
        var records = System.Text.Encoding.ASCII.GetString(result.Content).Split("\r\n"); Assert.All(records, record => Assert.Equal(512, record.Length));
        Assert.Equal("RW", records[2][..2]); Assert.Equal("00000010000", records[2][253..264]); Assert.Equal("101102", records[2][489..495]);
        Assert.Equal("RO", records[3][..2]); Assert.Equal(new string(' ', 9), records[3][2..11]); Assert.Equal("00000010000", records[3][231..242]); Assert.Equal("00000005000", records[3][242..253]);
        Assert.Equal("RT", records[4][..2]); Assert.Equal("000000000010000", records[4][99..114]);
        Assert.Equal("RU", records[5][..2]); Assert.Equal("0000001", records[5][2..9]); Assert.Equal("000000000010000", records[5][309..324]); Assert.Equal("000000000005000", records[5][324..339]);
        Assert.Equal("RF", records[6][..2]);
        Assert.False(SsaEfw2FileBuilder.Build(package with { Employees = [employee with { TreasuryTippedOccupationCodes = [] }] }, submitter).Succeeded);
    }

    [Fact]
    public void SsaEfw2cBuilder_UsesExactRecordPositionsTotalsAndTaxYearGate()
    {
        var previous = new W2EmployeeData(Guid.NewGuid(), "E-001", "Jane Old", "123-45-6789", "1 Main St", "", "48201", 1000m, 100m, 1000m, 62m, 1000m, 14.50m, [], "Jane", "", "Old", "Detroit", "MI");
        var corrected = previous with { EmployeeName = "Jane Corrected", LastName = "Corrected", Box1WagesTipsOtherCompensation = 1100m, Box2FederalIncomeTaxWithheld = 110m };
        var package = new W2cPackageData(TaxYear: 2025, EmployerLegalName: "Brass Ledger Test Company", EmployerEin: "12-3456789", Employees: [new W2cEmployeeData(previous, corrected, true, "Federal wage and identity correction")]);
        var submitter = new SsaEfw2cSubmitter(2025, "EFW2C TY2025 v2.3 (2026-01-20)", "https://www.ssa.gov/employer/efw/25efw2c.pdf", "12-3456789", "AB123456", "Brass Ledger Test Company", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com", "L", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com");
        var result = SsaEfw2cFileBuilder.Build(package, submitter);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors)); Assert.Equal(5, result.RecordCount); Assert.Equal(1, result.EmployeeRecordCount);
        var records = System.Text.Encoding.ASCII.GetString(result.Content).Split("\r\n"); Assert.All(records, record => Assert.Equal(1024, record.Length));
        Assert.Equal("RCA", records[0][..3]); Assert.Equal("RCE", records[1][..3]); Assert.Equal("2025", records[1][3..7]);
        Assert.Equal("RCW", records[2][..3]); Assert.Equal("123456789", records[2][12..21]); Assert.Equal("CORRECTED", records[2][101..121].Trim());
        Assert.Equal("00000100000", records[2][243..254]); Assert.Equal("00000110000", records[2][254..265]);
        Assert.Equal("RCT", records[3][..3]); Assert.Equal("0000001", records[3][3..10]); Assert.Equal("000000000100000", records[3][10..25]); Assert.Equal("000000000110000", records[3][25..40]);
        Assert.Equal("RCF", records[4][..3]); Assert.Equal("000000001", records[4][3..12]);
        Assert.False(SsaEfw2cFileBuilder.Build(package with { TaxYear = 2026 }, submitter).Succeeded);
        var currentSubmitter = submitter with { SpecificationTaxYear = 2026, SpecificationVersion = "EFW2C TY2026 initial publication (2026-07-10)", OfficialSpecificationUrl = "https://www.ssa.gov/employer/efw/26efw2c.pdf" };
        var current = SsaEfw2cFileBuilder.Build(package with { TaxYear = 2026 }, currentSubmitter);
        Assert.True(current.Succeeded, string.Join("; ", current.Errors));
        var currentRecords = System.Text.Encoding.ASCII.GetString(current.Content).Split("\r\n");
        Assert.Equal("2026", currentRecords[1][3..7]);
        Assert.Equal(new string(' ', 10), currentRecords[1][324..334]);
        Assert.Equal(new string(' ', 12), currentRecords[2][1008..1020]);
        Assert.False(SsaEfw2cFileBuilder.Build(package with { Employees = [new W2cEmployeeData(previous, corrected, false, "Address only")] }, submitter).Succeeded);
    }

    [Fact]
    public void SsaEfw2cBuilder_Emits2026TipAndOvertimeCorrectionRecordsAndTotals()
    {
        var previous = new W2EmployeeData(Guid.NewGuid(), "E-001", "Jane Employee", "123-45-6789", "1 Main St", "", "48201", 1200m, 110m, 1000m, 62m, 1100m, 15.95m, [], "Jane", "", "Employee", "Detroit", "MI", 100m, [new("TP", 100m), new("TT", 20m)], ["101"]);
        var corrected = previous with { Box7SocialSecurityTips = 75m, Box12Amounts = [new("TP", 75m), new("TT", 30m)], TreasuryTippedOccupationCodes = ["102"] };
        var package = new W2cPackageData(TaxYear: 2026, EmployerLegalName: "Brass Ledger Test Company", EmployerEin: "12-3456789", Employees: [new(previous, corrected, true, "Tip and overtime correction")]);
        var submitter = new SsaEfw2cSubmitter(2026, "EFW2C TY2026 initial publication (2026-07-10)", "https://www.ssa.gov/employer/efw/26efw2c.pdf", "12-3456789", "AB123456", "Brass Ledger Test Company", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com", "L", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com");

        var result = SsaEfw2cFileBuilder.Build(package, submitter);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors)); Assert.Equal(7, result.RecordCount);
        var records = System.Text.Encoding.ASCII.GetString(result.Content).Split("\r\n"); Assert.All(records, record => Assert.Equal(1024, record.Length));
        Assert.Equal(["RCA", "RCE", "RCW", "RCO", "RCT", "RCU", "RCF"], records.Select(record => record[..3]).ToArray());
        Assert.Equal("00000010000", records[2][375..386]); Assert.Equal("00000007500", records[2][386..397]);
        Assert.Equal("101102", records[2][1008..1014]);
        Assert.Equal("00000010000", records[3][452..463]); Assert.Equal("00000007500", records[3][463..474]);
        Assert.Equal("00000002000", records[3][474..485]); Assert.Equal("00000003000", records[3][485..496]);
        Assert.Equal("000000000010000", records[4][190..205]); Assert.Equal("000000000007500", records[4][205..220]);
        Assert.Equal("0000001", records[5][3..10]); Assert.Equal("000000000010000", records[5][610..625]); Assert.Equal("000000000007500", records[5][625..640]);
        Assert.Equal("000000000002000", records[5][640..655]); Assert.Equal("000000000003000", records[5][655..670]);
    }

    [Fact]
    public async Task SsaWageFileWorkflow_RequiresApprovedExactYearEncryptsAndRecordsAccuWageOnce()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); Guid correctionId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var companyId = await db.Companies.Select(item => item.Id).FirstAsync(); var filingId = Guid.NewGuid(); correctionId = Guid.NewGuid();
            db.PayrollFilings.Add(new PayrollFiling { Id = filingId, CompanyId = companyId, FormCode = "W2", TaxYear = 2025, PeriodKey = "2025-YEAR", PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), Status = "Reopened", DataJson = "{}", SummaryJson = "{}", SourcePayrollRunIdsJson = "[]", SourceDigestSha256 = new string('a', 64), OfficialSourceUrl = "https://www.irs.gov/instructions/iw2w3", ContentVersion = "2025", PreparedAtUtc = DateTimeOffset.UtcNow, ApprovedDataJson = "{}", ApprovedSourceDigestSha256 = new string('a', 64), ConcurrencyToken = Guid.NewGuid().ToString("N") });
            var previous = new W2EmployeeData(Guid.NewGuid(), "E-001", "Jane Old", "123456789", "1 Main St", "", "48201", 1000, 100, 1000, 62, 1000, 14.5m, [], "Jane", "", "Old", "Detroit", "MI"); var corrected = previous with { LastName = "Corrected", EmployeeName = "Jane Corrected", Box1WagesTipsOtherCompensation = 1100 };
            var package = new W2cPackageData(TaxYear: 2025, EmployerLegalName: "Brass Ledger Test Company", EmployerEin: "123456789", Employees: [new(previous, corrected, true, "Federal wage correction")]);
            db.PayrollFilingCorrections.Add(new PayrollFilingCorrection { Id = correctionId, CompanyId = companyId, OriginalPayrollFilingId = filingId, Sequence = 1, FormCode = "W-2c/W-3c", TaxYear = 2025, Quarter = 0, Process = "Correction", DiscoveredOn = new DateOnly(2026, 1, 20), Explanation = "Correct wage statement test values.", WageStatementsCorrected = true, WageStatementEvidenceReference = "EVIDENCE", Status = "Approved", DataJson = System.Text.Json.JsonSerializer.Serialize(package), CorrectedSourceDigestSha256 = new string('b', 64), OfficialSourceUrl = "https://www.irs.gov/instructions/iw2w3", ContentVersion = "2025-W2C", PreparedAtUtc = DateTimeOffset.UtcNow, ApprovedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") }); await db.SaveChangesAsync();
        }
        var workflow = scope.ServiceProvider.GetRequiredService<ISsaWageFileService>();
        var future = SsaConfigurationRequest(2027, true); Assert.False((await workflow.SaveConfigurationAsync(future)).Succeeded);
        var predatesPublication = SsaConfigurationRequest(2026, true) with { SourceRetrievedOn = new DateOnly(2026, 7, 9) }; Assert.False((await workflow.SaveConfigurationAsync(predatesPublication)).Succeeded);
        var current = await workflow.SaveConfigurationAsync(SsaConfigurationRequest(2026, true)); Assert.True(current.Succeeded, current.ErrorMessage);
        Assert.False((await workflow.GenerateAsync(new(correctionId))).Succeeded);
        var configured = await workflow.SaveConfigurationAsync(SsaConfigurationRequest(2025, true)); Assert.True(configured.Succeeded, configured.ErrorMessage);
        var generated = await workflow.GenerateAsync(new(correctionId)); Assert.True(generated.Succeeded, generated.ErrorMessage);
        var workspace = await workflow.GetAsync(); var file = Assert.Single(workspace.Files); Assert.Equal("GeneratedForAccuWage", file.Status); Assert.Equal(5, file.RecordCount);
        var download = await workflow.DownloadAsync(file.Id); Assert.NotNull(download); Assert.Equal(file.ContentSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(download!.Content)).ToLowerInvariant());
        var validation = await workflow.RecordValidationAsync(new(file.Id, true, "ACCUWAGE-RESULT-20260120", "AccuWage Online reported no errors for the immutable file.", file.ConcurrencyToken)); Assert.True(validation.Succeeded, validation.ErrorMessage);
        file = Assert.Single((await workflow.GetAsync()).Files); Assert.Equal("AccuWageValidated", file.Status); Assert.False((await workflow.RecordValidationAsync(new(file.Id, false, "SECOND", "A second result must not rewrite prior validation evidence.", file.ConcurrencyToken))).Succeeded);
        await using var verify = await factory.CreateDbContextAsync(); await verify.Database.OpenConnectionAsync(); await using var command = verify.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT ContentBase64 || '|' || SubmitterEin || '|' || BsoUserId FROM PayrollSsaWageFiles f JOIN PayrollSsaWageFileConfigurations c ON c.Id = f.PayrollSsaWageFileConfigurationId WHERE f.Id = $id"; var parameter = command.CreateParameter(); parameter.ParameterName = "$id"; parameter.Value = file.Id; command.Parameters.Add(parameter); Assert.All(((await command.ExecuteScalarAsync())?.ToString() ?? "").Split('|'), item => Assert.StartsWith("enc::", item));
    }

    private static SaveSsaWageFileConfigurationRequest SsaConfigurationRequest(int year, bool approved) => new(null, year, $"EFW2C TY{year} reviewed", year == 2025 ? SsaWageFileService.Supported2025LayoutCode : SsaWageFileService.SupportedLayoutCode, $"https://www.ssa.gov/employer/efw/{year % 100:00}efw2c.pdf", new string('c', 64), year >= 2026 ? new DateOnly(2026, 7, 10) : new DateOnly(2026, 1, 20), "Reviewer compared every implemented record and field position with the official SSA publication.", "123456789", "AB123456", "Brass Ledger Test Company", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com", "L", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com", approved, approved);

    [Fact]
    public async Task SsaOriginalWageFileWorkflow_AllowsSeparateExactSpecEncryptsAndRecordsAccuWageOnce()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); Guid filingId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var companyId = await db.Companies.Select(item => item.Id).FirstAsync(); filingId = Guid.NewGuid();
            var employee = new W2EmployeeData(Guid.NewGuid(), "E-001", "Jane Employee", "123456789", "10 Home St", "", "48201", 1100m, 110m, 1000m, 62m, 1100m, 15.95m, [], "Jane", "", "Employee", "Detroit", "MI");
            var package = new W2PackageData(TaxYear: 2026, EmployerLegalName: "Brass Ledger Test Company", EmployerEin: "123456789", Employees: [employee], W3Box1Total: 1100m, W3Box2Total: 110m, W3Box3Total: 1000m, W3Box4Total: 62m, W3Box5Total: 1100m, W3Box6Total: 15.95m);
            db.PayrollFilings.Add(new PayrollFiling { Id = filingId, CompanyId = companyId, FormCode = "W2", TaxYear = 2026, PeriodKey = "2026-YEAR", PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 12, 31), Status = "Approved", DataJson = System.Text.Json.JsonSerializer.Serialize(package), SummaryJson = "{}", SourcePayrollRunIdsJson = "[]", SourceDigestSha256 = new string('a', 64), OfficialSourceUrl = "https://www.irs.gov/instructions/iw2w3", ContentVersion = "2026", PreparedAtUtc = DateTimeOffset.UtcNow, ApprovedAtUtc = DateTimeOffset.UtcNow, ApprovedDataJson = System.Text.Json.JsonSerializer.Serialize(package), ApprovedSourceDigestSha256 = new string('a', 64), ApprovedBaselineAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") }); await db.SaveChangesAsync();
        }
        var workflow = scope.ServiceProvider.GetRequiredService<ISsaOriginalWageFileService>();
        Assert.False((await workflow.SaveConfigurationAsync(SsaOriginalConfigurationRequest(2025, true))).Succeeded);
        Assert.False((await workflow.SaveConfigurationAsync(SsaOriginalConfigurationRequest(2026, true) with { SourceRetrievedOn = new DateOnly(2026, 7, 6) })).Succeeded);
        var configured = await workflow.SaveConfigurationAsync(SsaOriginalConfigurationRequest(2026, true)); Assert.True(configured.Succeeded, configured.ErrorMessage);
        var correctionConfiguration = await scope.ServiceProvider.GetRequiredService<ISsaWageFileService>().SaveConfigurationAsync(SsaConfigurationRequest(2026, true)); Assert.True(correctionConfiguration.Succeeded, correctionConfiguration.ErrorMessage);
        var generated = await workflow.GenerateAsync(new(filingId)); Assert.True(generated.Succeeded, generated.ErrorMessage);
        var workspace = await workflow.GetAsync(); var file = Assert.Single(workspace.Files); Assert.Equal("GeneratedForAccuWage", file.Status); Assert.Equal(5, file.RecordCount);
        var download = await workflow.DownloadAsync(file.Id); Assert.NotNull(download); var records = System.Text.Encoding.ASCII.GetString(download!.Content).Split("\r\n"); Assert.All(records, record => Assert.Equal(512, record.Length)); Assert.Equal(file.ContentSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(download.Content)).ToLowerInvariant());
        var validation = await workflow.RecordValidationAsync(new(file.Id, true, "ACCUWAGE-EFW2-20260825", "AccuWage Online reported no structural errors for this immutable original file.", file.ConcurrencyToken)); Assert.True(validation.Succeeded, validation.ErrorMessage);
        file = Assert.Single((await workflow.GetAsync()).Files); Assert.Equal("AccuWageValidated", file.Status); Assert.False((await workflow.RecordValidationAsync(new(file.Id, false, "SECOND", "A second result must not rewrite the original evidence.", file.ConcurrencyToken))).Succeeded);
        await using var verify = await factory.CreateDbContextAsync(); await verify.Database.OpenConnectionAsync(); await using var command = verify.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT f.ContentBase64 || '|' || c.SubmitterEin || '|' || c.BsoUserId || '|' || c.EmployerSignaturePin FROM PayrollSsaOriginalWageFiles f JOIN PayrollSsaWageFileConfigurations c ON c.Id = f.PayrollSsaWageFileConfigurationId WHERE f.Id = $id"; var parameter = command.CreateParameter(); parameter.ParameterName = "$id"; parameter.Value = file.Id; command.Parameters.Add(parameter); Assert.All(((await command.ExecuteScalarAsync())?.ToString() ?? "").Split('|'), item => Assert.StartsWith("enc::", item));
    }

    private static SaveSsaOriginalWageFileConfigurationRequest SsaOriginalConfigurationRequest(int year, bool approved) => new(null, year, $"EFW2 TY{year} reviewed", SsaOriginalWageFileService.SupportedLayoutCode, $"https://www.ssa.gov/employer/efw/{year % 100:00}efw2.pdf", new string('d', 64), new DateOnly(2026, 7, 7), "Reviewer compared every implemented record and field position with the official SSA EFW2 publication.", "123456789", "AB123456", "Brass Ledger Test Company", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com", "L", "", "10 Office Rd", "Detroit", "MI", "48201", "Payroll Contact", "3135551212", "payroll@example.com", "N", "R", "1234567890", approved, approved);

    [Fact]
    public async Task PayrollTimecards_RequireValidAuditableWorkflowAndPreventOverlappingHours()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var workspace = await workspaceService.GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.First();
        var project = workspace.Projects.Jobs.First();
        var request = new SavePayrollTimecardDraftRequest(null, employee.Id, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16),
        [
            new PayrollTimeEntryInput(new DateOnly(2026, 8, 10), "REG", "Regular", 8m, 30m, 240m, true, employee.State, "Maricopa", "Phoenix", "", project.Id, "Production shift"),
            new PayrollTimeEntryInput(new DateOnly(2026, 8, 10), "OT", "Overtime", 2m, 45m, 90m, true, employee.State, "Maricopa", "Phoenix", W2Reporting: new(QualifiedOvertimeCompensation: 30m))
        ], "Approved source schedule");

        var saved = await transactions.SavePayrollTimecardDraftAsync(request);
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var overlap = await transactions.SavePayrollTimecardDraftAsync(request with { Entries = [new PayrollTimeEntryInput(new DateOnly(2026, 8, 11), "REG", "Regular", 8m, 30m, 240m)] });
        Assert.False(overlap.Succeeded);
        Assert.Contains("overlapping", overlap.ErrorMessage);
        var excessiveHours = await transactions.SavePayrollTimecardDraftAsync(new SavePayrollTimecardDraftRequest(null, workspace.Payroll.Employees.Skip(1).First().Id, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16), [new PayrollTimeEntryInput(new DateOnly(2026, 8, 10), "REG", "Regular", 25m, 20m, 500m)]));
        Assert.False(excessiveHours.Succeeded);
        Assert.Contains("24 hours", excessiveHours.ErrorMessage);
        var invalidW2Reporting = await transactions.SavePayrollTimecardDraftAsync(new SavePayrollTimecardDraftRequest(null, workspace.Payroll.Employees.Skip(1).First().Id, new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 23), [new PayrollTimeEntryInput(new DateOnly(2026, 8, 17), "REG", "Regular", 8m, 20m, 160m, W2Reporting: new(QualifiedOvertimeCompensation: 20m))]));
        Assert.False(invalidW2Reporting.Succeeded); Assert.Contains("overtime earning type", invalidW2Reporting.ErrorMessage);
        var duplicateOccupation = await transactions.SavePayrollTimecardDraftAsync(new SavePayrollTimecardDraftRequest(null, workspace.Payroll.Employees.Skip(1).First().Id, new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 23), [new PayrollTimeEntryInput(new DateOnly(2026, 8, 17), "TIPS", "Tips", 8m, 20m, 160m, W2Reporting: new(100m, 100m, TreasuryTippedOccupationCodes: ["101", "101"]))]));
        Assert.False(duplicateOccupation.Succeeded); Assert.Contains("duplicates", duplicateOccupation.ErrorMessage);

        var timecard = (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(card => card.Id == saved.Id);
        Assert.Equal("Draft", timecard.Status);
        Assert.Equal(10m, timecard.TotalHours);
        Assert.Equal(330m, timecard.TotalAmount);
        Assert.Equal(2, timecard.Entries.Count);
        Assert.Equal(30m, timecard.Entries.Single(entry => entry.EarningType == "Overtime").W2Reporting.QualifiedOvertimeCompensation);
        Assert.False((await transactions.SubmitPayrollTimecardAsync(new SubmitPayrollTimecardRequest(timecard.Id, "stale"))).Succeeded);
        Assert.True((await transactions.SubmitPayrollTimecardAsync(new SubmitPayrollTimecardRequest(timecard.Id, timecard.ConcurrencyToken))).Succeeded);
        timecard = (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(card => card.Id == saved.Id);
        Assert.Equal("Submitted", timecard.Status);
        Assert.False((await transactions.SavePayrollTimecardDraftAsync(request with { TimecardId = timecard.Id, ConcurrencyToken = timecard.ConcurrencyToken })).Succeeded);
        Assert.True((await transactions.ApprovePayrollTimecardAsync(new ApprovePayrollTimecardRequest(timecard.Id, timecard.ConcurrencyToken))).Succeeded);
        timecard = (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(card => card.Id == saved.Id);
        Assert.Equal("Approved", timecard.Status);
        Assert.False((await transactions.VoidPayrollTimecardAsync(new VoidPayrollTimecardRequest(timecard.Id, "", timecard.ConcurrencyToken))).Succeeded);
        Assert.True((await transactions.VoidPayrollTimecardAsync(new VoidPayrollTimecardRequest(timecard.Id, "Duplicate source schedule", timecard.ConcurrencyToken))).Succeeded);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var stored = await db.PayrollTimecards.SingleAsync(card => card.Id == saved.Id);
        Assert.Equal("Voided", stored.Status);
        Assert.Equal(4, await db.BusinessAuditEntries.CountAsync(entry => entry.EntityType == "PayrollTimecard" && entry.EntityId == stored.Id));
        Assert.Equal(2, await db.PayrollTimeEntries.CountAsync(entry => entry.PayrollTimecardId == stored.Id));
    }

    [Fact]
    public async Task ApprovedTimecards_AreServerCalculatedConsumedAtomicallyAndRetainEntryProvenance()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var workspace = await workspaceService.GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.First(candidate => candidate.PayType == "Hourly");
        var periodStart = new DateOnly(2026, 8, 10);
        var periodEnd = new DateOnly(2026, 8, 16);
        var cardResult = await transactions.SavePayrollTimecardDraftAsync(new SavePayrollTimecardDraftRequest(null, employee.Id, periodStart, periodEnd,
        [
            new PayrollTimeEntryInput(periodStart, "REG", "Regular", 8m, 30m, 240m, true, "AZ", "Maricopa", "Phoenix"),
            new PayrollTimeEntryInput(periodStart.AddDays(1), "OT", "Overtime", 2m, 45m, 90m, true, "NV", "Clark", "Las Vegas")
        ]));
        Assert.True(cardResult.Succeeded, cardResult.ErrorMessage);
        var card = (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(candidate => candidate.Id == cardResult.Id);
        Assert.True((await transactions.SubmitPayrollTimecardAsync(new SubmitPayrollTimecardRequest(card.Id, card.ConcurrencyToken))).Succeeded);
        card = (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(candidate => candidate.Id == card.Id);
        Assert.True((await transactions.ApprovePayrollTimecardAsync(new ApprovePayrollTimecardRequest(card.Id, card.ConcurrencyToken))).Succeeded);
        card = (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(candidate => candidate.Id == card.Id);

        var bankId = workspace.Treasury.BankAccounts.First().Id;
        var request = new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 8, 21), "TIMECARD-PAYROLL-1", [new EmployeePayrollInput(employee.Id, 9_999m)], periodStart, periodEnd, ApprovedTimecardIds: [card.Id]);
        var preview = await transactions.PreviewEmployeePayrollRunAsync(request);
        Assert.NotNull(preview);
        Assert.Equal(330m, preview.GrossPayroll);
        Assert.Equal("Approved", (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(candidate => candidate.Id == card.Id).Status);

        var failed = await transactions.SaveEmployeePayrollRunDraftAsync(request with { BankAccountId = Guid.NewGuid(), Reference = "TIMECARD-FAILED" });
        Assert.False(failed.Succeeded);
        Assert.Equal("Approved", (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(candidate => candidate.Id == card.Id).Status);

        var saved = await transactions.SaveEmployeePayrollRunDraftAsync(request);
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var consumed = (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(candidate => candidate.Id == card.Id);
        Assert.Equal("Consumed", consumed.Status);
        Assert.Equal(saved.Id, consumed.PayrollRunId);
        var reused = await transactions.SaveEmployeePayrollRunDraftAsync(request with { Reference = "TIMECARD-PAYROLL-REUSE" });
        Assert.False(reused.Succeeded);
        Assert.Contains("approved", reused.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var savedRun = (await workspaceService.GetWorkspaceAsync()).Payroll.Runs!.Single(run => run.Id == saved.Id);
        Assert.False((await transactions.CancelPayrollRunAsync(new CancelPayrollRunRequest(savedRun.Id, "", savedRun.ConcurrencyToken))).Succeeded);
        var cancelled = await transactions.CancelPayrollRunAsync(new CancelPayrollRunRequest(savedRun.Id, "Incorrect draft configuration", savedRun.ConcurrencyToken));
        Assert.True(cancelled.Succeeded, cancelled.ErrorMessage);
        var afterCancellation = await workspaceService.GetWorkspaceAsync();
        Assert.Equal("Cancelled", afterCancellation.Payroll.Runs!.Single(run => run.Id == saved.Id).Status);
        Assert.Equal("Approved", afterCancellation.Payroll.Timecards!.Single(candidate => candidate.Id == card.Id).Status);

        var replacement = await transactions.SaveEmployeePayrollRunDraftAsync(request with { Reference = "TIMECARD-PAYROLL-REPLACEMENT" });
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        consumed = (await workspaceService.GetWorkspaceAsync()).Payroll.Timecards!.Single(candidate => candidate.Id == card.Id);
        Assert.Equal("Consumed", consumed.Status);
        Assert.Equal(replacement.Id, consumed.PayrollRunId);
        var correctionCard = await transactions.SavePayrollTimecardDraftAsync(new SavePayrollTimecardDraftRequest(null, employee.Id, periodStart, periodEnd,
            [new PayrollTimeEntryInput(periodStart, "CORR", "Correction", 1m, 30m, 30m, true, "AZ", "Maricopa", "Phoenix")], "Correction after the original card was consumed"));
        Assert.True(correctionCard.Succeeded, correctionCard.ErrorMessage);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var runLineId = await db.PayrollRunEmployeeLines.Where(line => line.PayrollRunId == replacement.Id).Select(line => line.Id).SingleAsync();
        var earningLines = await db.PayrollEarningLines.Where(line => line.PayrollRunEmployeeLineId == runLineId).OrderBy(line => line.Sequence).ToListAsync();
        var sourceEntries = await db.PayrollTimeEntries.Where(entry => entry.PayrollTimecardId == card.Id).OrderBy(entry => entry.WorkDate).ThenBy(entry => entry.Sequence).ToListAsync();
        Assert.Equal(sourceEntries.Select(entry => entry.Id), earningLines.Select(line => line.PayrollTimeEntryId!.Value));
        Assert.Equal(sourceEntries.Select(entry => entry.Amount), earningLines.Select(line => line.Amount));
        Assert.Equal(2, await db.BusinessAuditEntries.CountAsync(entry => entry.EntityType == "PayrollTimecard" && entry.EntityId == card.Id && entry.Action == "payroll-timecard.consumed"));
        Assert.Single(await db.BusinessAuditEntries.Where(entry => entry.EntityType == "PayrollTimecard" && entry.EntityId == card.Id && entry.Action == "payroll-timecard.released").ToListAsync());
    }

    [Fact]
    public async Task FederalPayroll2026_UsesPublication15TSelectionsAndFicaYearToDateBoundaries()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.First();
        var bankId = workspace.Treasury.BankAccounts.First().Id;

        var setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0, 0, 0, PayrollFrequency: "Biweekly", FederalFormW4Year: 2026));
        Assert.True(setup.Succeeded, setup.ErrorMessage);
        var preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 1), "FEDERAL-BASE", [new EmployeePayrollInput(employee.Id, 1_000m)]));
        var taxes = Assert.Single(preview!.Employees).Taxes!;
        Assert.Equal(38.08m, taxes.Single(tax => tax.ObligationCode == "US-FIT").EmployeeAmount);
        Assert.Equal(62m, taxes.Single(tax => tax.ObligationCode == "US-OASDI-EMPLOYEE").EmployeeAmount);
        Assert.Equal(14.50m, taxes.Single(tax => tax.ObligationCode == "US-MEDICARE-EMPLOYEE").EmployeeAmount);

        setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0, 0, 0, PayrollFrequency: "Biweekly", FederalFormW4Year: 2026, FederalStep2MultipleJobs: true));
        Assert.True(setup.Succeeded, setup.ErrorMessage);
        preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 1), "FEDERAL-STEP2", [new EmployeePayrollInput(employee.Id, 1_000m)]));
        Assert.Equal(78.08m, Assert.Single(preview!.Employees).Taxes!.Single(tax => tax.ObligationCode == "US-FIT").EmployeeAmount);

        setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 25m, 0, 0, PayrollFrequency: "Biweekly", FederalFormW4Year: 2026, FederalWithholdingExempt: true));
        Assert.True(setup.Succeeded, setup.ErrorMessage);
        preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 1), "FEDERAL-EXEMPT", [new EmployeePayrollInput(employee.Id, 1_000m)]));
        taxes = Assert.Single(preview!.Employees).Taxes!;
        Assert.Equal(0m, taxes.Single(tax => tax.ObligationCode == "US-FIT").EmployeeAmount);
        Assert.DoesNotContain(taxes, tax => tax.ObligationCode == "FEDERAL-ADDITIONAL-WITHHOLDING");

        setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0, 0, 0, PayrollFrequency: "Biweekly", FederalFormW4Year: 2026));
        Assert.True(setup.Succeeded, setup.ErrorMessage);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            var priorRun = new PayrollRun { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = bankId, PayDate = new DateOnly(2026, 5, 1), PeriodStart = new DateOnly(2026, 4, 16), PeriodEnd = new DateOnly(2026, 4, 30), Reference = "FEDERAL-YTD-SEED", Status = "Posted", RunType = "Adjustment", PreparedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
            var priorLine = new PayrollRunEmployeeLine { Id = Guid.NewGuid(), PayrollRunId = priorRun.Id, EmployeeId = employee.Id, WorkState = employee.State, FilingStatus = "Single", PayrollFrequency = "Biweekly", GrossPay = 199_500m, TaxableWages = 199_500m, YearToDateGrossAfter = 199_500m };
            db.PayrollRuns.Add(priorRun); db.PayrollRunEmployeeLines.Add(priorLine);
            db.PayrollTaxLines.AddRange(
                new PayrollTaxLine { Id = Guid.NewGuid(), PayrollRunEmployeeLineId = priorLine.Id, Sequence = 1, ObligationCode = "US-OASDI-EMPLOYEE", JurisdictionCode = "US", JurisdictionName = "Federal", TaxType = "Social Security employee", TaxableWages = 184_000m },
                new PayrollTaxLine { Id = Guid.NewGuid(), PayrollRunEmployeeLineId = priorLine.Id, Sequence = 2, ObligationCode = "US-MEDICARE-EMPLOYEE", JurisdictionCode = "US", JurisdictionName = "Federal", TaxType = "Medicare employee", TaxableWages = 199_500m });
            await db.SaveChangesAsync();
        }
        preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 2), "FEDERAL-YTD", [new EmployeePayrollInput(employee.Id, 1_000m)]));
        taxes = Assert.Single(preview!.Employees).Taxes!;
        var socialSecurity = taxes.Single(tax => tax.ObligationCode == "US-OASDI-EMPLOYEE");
        Assert.Equal(500m, socialSecurity.TaxableWages);
        Assert.Equal(31m, socialSecurity.EmployeeAmount);
        var additionalMedicare = taxes.Single(tax => tax.ObligationCode == "US-ADDITIONAL-MEDICARE");
        Assert.Equal(500m, additionalMedicare.TaxableWages);
        Assert.Equal(4.50m, additionalMedicare.EmployeeAmount);
    }

    [Fact]
    public async Task EmployeePayroll_AppliesDistinctResidenceAndWorkCityProfiles()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.Single(item => item.State == "NV");
        var bank = workspace.Treasury.BankAccounts.First();
        var setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0m, 0m, 0m, "OH", "Residenceville", "NV", "Worktown"));
        Assert.True(setup.Succeeded, setup.ErrorMessage);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            db.TaxProfiles.AddRange(
                new BrassLedger.Domain.Accounting.TaxProfile { Id = Guid.NewGuid(), CompanyId = companyId, Jurisdiction = "Worktown", TaxType = "Local withholding", Rate = .01m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "Test", IsEmployerSpecific = false, IsActive = true, IsVerified = true },
                new BrassLedger.Domain.Accounting.TaxProfile { Id = Guid.NewGuid(), CompanyId = companyId, Jurisdiction = "Residenceville", TaxType = "Local withholding", Rate = .02m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "Test", IsEmployerSpecific = false, IsActive = true, IsVerified = true });
            await db.SaveChangesAsync();
        }
        var rule = await transactions.SavePayrollJurisdictionRuleAsync(new SavePayrollJurisdictionRuleRequest(null, "Residenceville", "Worktown", true, .5m, true, "Test reciprocity and resident credit"));
        Assert.True(rule.Succeeded, rule.ErrorMessage);
        var preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 5, 15), "LOCATION-PREVIEW", [new EmployeePayrollInput(employee.Id, 1_000m)]));
        Assert.NotNull(preview);
        var employeeEstimate = Assert.Single(preview!.Employees);
        Assert.Equal(124.58m, employeeEstimate.EmployeeWithholdings); // $38.08 verified 2026 FIT, $76.50 employee FICA, and $10 residence tax after the configured credit.
        Assert.Equal(employeeEstimate.EmployeeWithholdings, employeeEstimate.Taxes!.Sum(tax => tax.EmployeeAmount));
    }

    [Fact]
    public async Task EmployeePayroll_AllocatesWorkTaxesByDetailedEarningLocationButTaxesResidentWagesAsAWhole()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.Single(item => item.State == "NV");
        Assert.True((await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0m, 0m, 0m, "OH", "Residenceville", "NV", "Worktown"))).Succeeded);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            db.TaxProfiles.AddRange(
                new TaxProfile { Id = Guid.NewGuid(), CompanyId = companyId, Jurisdiction = "Worktown", TaxType = "Local withholding", Rate = .01m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "Allocation test", IsActive = true, IsVerified = true },
                new TaxProfile { Id = Guid.NewGuid(), CompanyId = companyId, Jurisdiction = "Othercity", TaxType = "Local withholding", Rate = .03m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "Allocation test", IsActive = true, IsVerified = true },
                new TaxProfile { Id = Guid.NewGuid(), CompanyId = companyId, Jurisdiction = "Residenceville", TaxType = "Local withholding", Rate = .02m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "Allocation test", IsActive = true, IsVerified = true });
            await db.SaveChangesAsync();
        }

        var request = new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "MULTI-LOCATION-PREVIEW",
        [
            new EmployeePayrollInput(employee.Id, 0,
            [
                new PayrollEarningInput("REG-WORKTOWN", "Regular", 24m, 25m, 600m, WorkState: "NV", WorkCity: "Worktown"),
                new PayrollEarningInput("REG-OTHER", "Regular", 16m, 25m, 400m, WorkState: "NV", WorkCity: "Othercity")
            ])
        ]);
        var preview = await transactions.PreviewEmployeePayrollRunAsync(request);
        Assert.NotNull(preview);
        var taxes = Assert.Single(preview!.Employees).Taxes!;
        var worktown = taxes.Single(tax => tax.JurisdictionCode == "Worktown");
        var othercity = taxes.Single(tax => tax.JurisdictionCode == "Othercity");
        var residence = taxes.Single(tax => tax.JurisdictionCode == "Residenceville");
        Assert.Equal((600m, 6m), (worktown.TaxableWages, worktown.EmployeeAmount));
        Assert.Equal((400m, 12m), (othercity.TaxableWages, othercity.EmployeeAmount));
        Assert.Equal((1_000m, 20m), (residence.TaxableWages, residence.EmployeeAmount));
        Assert.Equal(152.58m, Assert.Single(preview.Employees).EmployeeWithholdings);
    }

    [Fact]
    public async Task EmployeePayroll_ExecutesApprovedTaxContentRuleInsteadOfStaticProfileFallback()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var employee = workspace.Payroll.Employees.First();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            var package = new BrassLedger.Domain.Accounting.TaxContentPackage { Id = Guid.NewGuid(), CompanyId = companyId, PackageCode = "US-TEST", Version = "1.0", EffectiveOn = new DateOnly(2026, 1, 1), Status = "Approved", MinimumEngineVersion = "1.0", ManifestJson = "{}", CreatedAtUtc = DateTimeOffset.UtcNow };
            var rule = new BrassLedger.Domain.Accounting.TaxRuleSet { Id = Guid.NewGuid(), CompanyId = companyId, TaxContentPackageId = package.Id, Code = "US-TEST-WH", JurisdictionCode = "US", JurisdictionName = "Federal", JurisdictionType = "Federal", TaxType = "Employee withholding", CalculationMethod = "employer-rate-wage-base", WithholdingFrequency = "Per payroll", EffectiveOn = new DateOnly(2026, 1, 1), ContentVersion = "1.0", MinimumEngineVersion = "1.0", IsActive = true };
            db.TaxContentPackages.Add(package); db.TaxRuleSets.Add(rule); db.TaxRuleParameters.Add(new BrassLedger.Domain.Accounting.TaxRuleParameter { Id = Guid.NewGuid(), TaxRuleSetId = rule.Id, ParameterCode = "rate", Label = "Rate", ValueType = "number", NumericValue = .10m });
            await db.SaveChangesAsync();
        }
        var preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "PACKAGE-TEST", [new EmployeePayrollInput(employee.Id, 1_000m)]));
        Assert.NotNull(preview);
        Assert.Equal(176.50m, Assert.Single(preview!.Employees).EmployeeWithholdings); // Content rule replaces FIT only; employee FICA remains independently applicable.
        var posting = await transactions.PostEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "PACKAGE-POST", [new EmployeePayrollInput(employee.Id, 1_000m)]));
        Assert.True(posting.Succeeded, posting.ErrorMessage);
        await using var verification = await factory.CreateDbContextAsync();
        Assert.Contains("US-TEST", (await verification.PayrollRuns.SingleAsync(run => run.Id == posting.Id)).TaxContentSnapshotJson);
    }

    [Fact]
    public async Task EmployeePayroll_AddsNewYorkStateAndNycObligationsButSelectsOneVariantPerObligation()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var taxAdministration = scope.ServiceProvider.GetRequiredService<ITaxAdministrationService>();
        var packagePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ny/2026-runtime-package.json"));
        var import = await taxAdministration.ImportTaxContentDocumentAsync(await File.ReadAllTextAsync(packagePath));
        Assert.True(import.Succeeded, import.ErrorMessage);
        var activation = await taxAdministration.ActivateContentPackageAsync(import.SavedId!.Value);
        Assert.True(activation.Succeeded, activation.ErrorMessage);

        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.First();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 3, 0m, 0m, 0m, "NY", "New York City", "NY", "New York City", "Weekly"));
        Assert.True(setup.Succeeded, setup.ErrorMessage);

        var preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "NYC-OBLIGATION-PREVIEW", [new EmployeePayrollInput(employee.Id, 400m)]));

        Assert.NotNull(preview);
        Assert.Equal(53.76m, Assert.Single(preview!.Employees).EmployeeWithholdings); // Verified FIT $9.04 + employee FICA $30.60 + NY Method II $8.01 + NYC resident Method II $6.11.

        setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0m, 0m, 0m, "NY", "New York City", "NY", "New York City", "Annual"));
        Assert.True(setup.Succeeded, setup.ErrorMessage);
        preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "NYC-WHOLE-WAGE-PREVIEW", [new EmployeePayrollInput(employee.Id, 1_207_400m)]));
        Assert.NotNull(preview);
        Assert.Equal(610_939.15m, Assert.Single(preview!.Employees).EmployeeWithholdings); // Verified FIT/FICA plus NY Method III and NYC resident exact withholding; Method II is excluded.

        setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0m, 0m, 0m, "NY", "Albany", "NY", "Yonkers", "Weekly"));
        Assert.True(setup.Succeeded, setup.ErrorMessage);
        preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "YONKERS-NONRESIDENT-PREVIEW", [new EmployeePayrollInput(employee.Id, 200m)]));
        Assert.NotNull(preview);
        Assert.Equal(18.36m, Assert.Single(preview!.Employees).EmployeeWithholdings); // Employee FICA $15.30 + NY State $2.25 + Yonkers nonresident earnings tax $0.81; FIT is $0 at this weekly wage.

        preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "YONKERS-ALLOCATED-PREVIEW",
            [new EmployeePayrollInput(employee.Id, 0, [new PayrollEarningInput("YON", "Regular", 4m, 25m, 100m, WorkState: "NY", WorkCity: "Yonkers"), new PayrollEarningInput("ALB", "Regular", 4m, 25m, 100m, WorkState: "NY", WorkCity: "Albany")])]));
        Assert.NotNull(preview);
        var yonkers = Assert.Single(preview!.Employees).Taxes!.Single(tax => tax.ObligationCode == "YONKERS-NONRESIDENT-EARNINGS");
        Assert.Equal(100m, yonkers.TaxableWages);
        Assert.Equal(.21m, yonkers.EmployeeAmount); // The verified annualized exclusion applies to the $100 earned in Yonkers, not the $200 total check.
    }

    [Fact]
    public async Task SecurityAdministration_SeparatesRoleAndUserManagementPermissions()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<BrassLedger.Application.Security.ISecurityAdministrationService>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();

        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.UserManage);
        var roleAttempt = await administration.CreateRoleAsync(new BrassLedger.Application.Security.CreateAccessRoleRequest("Escalation", "Should fail", [BrassLedgerPermissions.LedgerManage]));
        Assert.False(roleAttempt.Succeeded);
        Assert.Contains("not authorized", roleAttempt.ErrorMessage);

        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.RoleManage);
        var operatorAttempt = await administration.CreateOperatorAsync(new BrassLedger.Application.Security.CreateOperatorRequest("role-only", "Role Only", "role@example.test", "A secure password 123", "A secure password 123", "Controller"));
        Assert.False(operatorAttempt.Succeeded);
        Assert.Contains("not authorized", operatorAttempt.ErrorMessage);
    }

    [Fact]
    public async Task PayrollLifecycle_EnforcesSeparatePreparationApprovalPostingAndReversalPermissions()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();

        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.PayrollApprove);
        var prepare = await transactions.SaveEmployeePayrollRunDraftAsync(new PostEmployeePayrollRunRequest(Guid.NewGuid(), new DateOnly(2026, 6, 1), "DENIED", [new EmployeePayrollInput(Guid.NewGuid(), 1m)]));
        Assert.False(prepare.Succeeded);
        Assert.Contains("not authorized", prepare.ErrorMessage);

        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.PayrollPrepare);
        var approve = await transactions.ApprovePayrollRunAsync(new ApprovePayrollRunRequest(Guid.NewGuid(), "token"));
        Assert.False(approve.Succeeded);
        Assert.Contains("not authorized", approve.ErrorMessage);

        var post = await transactions.PostApprovedPayrollRunAsync(new PostApprovedPayrollRunRequest(Guid.NewGuid(), "token"));
        Assert.False(post.Succeeded);
        Assert.Contains("not authorized", post.ErrorMessage);

        var reverse = await transactions.ReversePayrollRunAsync(new ReversePayrollRunRequest(Guid.NewGuid(), new DateOnly(2026, 6, 1), "reason", "token"));
        Assert.False(reverse.Succeeded);
        Assert.Contains("not authorized", reverse.ErrorMessage);
    }

    [Fact]
    public async Task TransactionService_RejectsHttpRequestsWithoutACompanyClaim()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transactions.PostJournalEntryAsync(new PostJournalEntryRequest(
            new DateOnly(2026, 5, 1), "NO-COMPANY", "Must fail closed", [new JournalLineRequest("1000", 1m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 1m, "Credit")])));
    }

    [Fact]
    public async Task TransactionService_PostsBillPaymentPayrollAndReconciliation()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var before = await workspaceService.GetWorkspaceAsync();
        var vendor = before.Payables.Vendors.First();
        var operatingBank = before.Treasury.BankAccounts.First();
        var payrollBank = before.Treasury.BankAccounts.Last();

        var billResult = await transactions.CreateVendorBillAsync(new CreateVendorBillRequest(
            vendor.Id, "B-TEST-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m, "5100", "Workflow bill"));
        Assert.True(billResult.Succeeded, billResult.ErrorMessage);
        var paymentResult = await transactions.ApplyBillPaymentAsync(new ApplyBillPaymentRequest(
            billResult.Id!.Value, operatingBank.Id, new DateOnly(2026, 5, 2), 100m, "CHK-TEST-1"));
        Assert.True(paymentResult.Succeeded, paymentResult.ErrorMessage);

        var payrollResult = await transactions.PostPayrollRunAsync(new PostPayrollRunRequest(
            payrollBank.Id, new DateOnly(2026, 5, 3), "PAY-TEST-1", 250m, 200m, 50m, 25m));
        Assert.True(payrollResult.Succeeded, payrollResult.ErrorMessage);

        var beforeReconciliation = await workspaceService.GetWorkspaceAsync();
        var reconcileBank = beforeReconciliation.Treasury.BankAccounts.First();
        var reconciliationResult = await transactions.ReconcileBankAccountAsync(new ReconcileBankAccountRequest(
            reconcileBank.Id, new DateOnly(2026, 5, 31), reconcileBank.CurrentBalance));
        Assert.True(reconciliationResult.Succeeded, reconciliationResult.ErrorMessage);

        var after = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.Payables.OpenBalance, after.Payables.OpenBalance);
        Assert.Equal(0m, after.Treasury.BankAccounts.Single(bank => bank.Id == reconcileBank.Id).UnreconciledAmount);
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "2200").Balance + 75m, after.GeneralLedger.Accounts.Single(account => account.Number == "2200").Balance);
        Assert.Contains(after.GeneralLedger.RecentEntries, entry => entry.Description == "Vendor payment");
        Assert.Contains(after.GeneralLedger.RecentEntries, entry => entry.SourceModule == "Payroll" && entry.TotalAmount == 275m);
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var reconciliation = await db.BankReconciliations.SingleAsync(item => item.BankAccountId == reconcileBank.Id);
        Assert.Equal(reconcileBank.CurrentBalance, reconciliation.StatementClosingBalance);
        Assert.NotEmpty(await db.BankReconciliationItems.Where(item => item.BankReconciliationId == reconciliation.Id).ToListAsync());
        Assert.Equal(1, await db.JournalEntries.CountAsync(entry => entry.SourceDocumentType == "VendorBill" && entry.SourceDocumentId == billResult.Id));
        var durablePayment = await db.SubledgerPayments.SingleAsync(payment => payment.Id == paymentResult.Id);
        Assert.Equal("SubledgerPayment", await db.JournalEntries.Where(entry => entry.Id == durablePayment.JournalEntryId).Select(entry => entry.SourceDocumentType).SingleAsync());
        Assert.True(await db.SubledgerPaymentApplications.AnyAsync(application => application.SubledgerPaymentId == durablePayment.Id && application.DocumentId == billResult.Id));
    }

    [Fact]
    public async Task TransactionService_RejectsUnbalancedBankReconciliation()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var bank = workspace.Treasury.BankAccounts.First();

        var result = await transactions.ReconcileBankAccountAsync(new ReconcileBankAccountRequest(bank.Id, bank.LastReconciledOn.AddDays(1), bank.CurrentBalance + 1m));

        Assert.False(result.Succeeded);
        Assert.Contains("differs from the cleared book activity", result.ErrorMessage);
    }

    [Fact]
    public async Task BankingWorkflow_ImportsMatchesTransfersReconcilesAndReopensAuditably()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>(); var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var before = await workspaceService.GetWorkspaceAsync(); var customer = before.Receivables.Customers.First(); var fromBank = before.Treasury.BankAccounts.First(); var toBank = before.Treasury.BankAccounts.Last();
        var invoice = await transactions.CreateInvoiceAsync(new CreateInvoiceRequest(customer.Id, "INV-BANK-WF-1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 100m, 0m, "4000", "Bank match"));
        var payment = await transactions.RecordCustomerPaymentAsync(new RecordCustomerPaymentRequest(customer.Id, fromBank.Id, new DateOnly(2026, 8, 2), 100m, "DEP-BANK-WF-1", "ACH", [new PaymentDocumentApplicationRequest(invoice.Id!.Value, 100m)])); Assert.True(payment.Succeeded, payment.ErrorMessage);
        const string csv = "ExternalId,Date,Amount,Type,Payee,Memo,Reference\nFIT-100,2026-08-02,100.00,CREDIT,Customer,Invoice receipt,DEP-BANK-WF-1\nFIT-BAD,not-a-date,0,OTHER,,,";
        var dryRun = await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(fromBank.Id, "statement.csv", "CSV", csv, true)); Assert.True(dryRun.Succeeded, dryRun.ErrorMessage); Assert.Equal(1, dryRun.ImportedCount); Assert.Equal(1, dryRun.RejectedCount);
        var imported = await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(fromBank.Id, "statement.csv", "CSV", csv)); Assert.True(imported.Succeeded, imported.ErrorMessage); Assert.Equal(1, imported.ImportedCount);
        Assert.False((await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(fromBank.Id, "statement-copy.csv", "CSV", csv))).Succeeded);
        const string ofx = "<OFX><BANKTRANLIST><STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260804120000<FITID>OFX-1<TRNAMT>-12.34<NAME>Utility<MEMO>Monthly bill</STMTTRN></BANKTRANLIST></OFX>";
        var ofxPreview = await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(fromBank.Id, "statement.ofx", "OFX", ofx, true)); Assert.True(ofxPreview.Succeeded, ofxPreview.ErrorMessage); Assert.Equal(12.34m, ofxPreview.DebitTotal);
        const string camt = "<Document xmlns='urn:iso:std:iso:20022:tech:xsd:camt.053.001.08'><BkToCstmrStmt><Stmt><Ntry><Amt Ccy='USD'>22.50</Amt><CdtDbtInd>CRDT</CdtDbtInd><BookgDt><Dt>2026-08-05</Dt></BookgDt><AcctSvcrRef>CAMT-1</AcctSvcrRef><AddtlNtryInf>Deposit</AddtlNtryInf></Ntry></Stmt></BkToCstmrStmt></Document>";
        var camtPreview = await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(fromBank.Id, "statement.xml", "CAMT.053", camt, true)); Assert.True(camtPreview.Succeeded, camtPreview.ErrorMessage); Assert.Equal(22.50m, camtPreview.CreditTotal);
        const string mt940 = ":20:START\n:25:12345\n:61:260806D7,89NTRFNONREF//MT940-1\n:86:Bank fee\n:62F:C260806USD0,00";
        var mt940Preview = await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(fromBank.Id, "statement.sta", "MT940", mt940, true)); Assert.True(mt940Preview.Succeeded, mt940Preview.ErrorMessage); Assert.Equal(7.89m, mt940Preview.DebitTotal);
        const string mt940Signs = ":20:SIGNS\n:61:260806D1,00NTRF//D-1\n:61:260806RC2,00NTRF//RC-1\n:61:260806C3,00NTRF//C-1\n:61:260806RD4,00NTRF//RD-1";
        var signPreview = await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(fromBank.Id, "signs.sta", "MT940", mt940Signs, true)); Assert.True(signPreview.Succeeded, signPreview.ErrorMessage); Assert.Equal(3m, signPreview.DebitTotal); Assert.Equal(7m, signPreview.CreditTotal);
        const string malformedCamt = "<Document><Ntry><Amt>1.00</Amt><BookgDt><Dt>1</Dt></BookgDt><AcctSvcrRef>BAD-DATE</AcctSvcrRef></Ntry></Document>";
        var malformedCamtPreview = await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(fromBank.Id, "malformed.xml", "CAMT.053", malformedCamt, true)); Assert.True(malformedCamtPreview.Succeeded, malformedCamtPreview.ErrorMessage); Assert.Equal(1, malformedCamtPreview.RejectedCount);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
        var statementItem = await db.BankStatementTransactions.SingleAsync(item => item.ExternalId == "FIT-100"); var paymentJournalId = await db.JournalEntries.Where(item => item.Reference == "DEP-BANK-WF-1").Select(item => item.Id).SingleAsync();
        Assert.True((await transactions.MatchBankTransactionAsync(new MatchBankTransactionRequest(statementItem.Id, paymentJournalId, "Exact amount match"))).Succeeded);
        Assert.False((await transactions.MatchBankTransactionAsync(new MatchBankTransactionRequest(statementItem.Id, paymentJournalId))).Succeeded);
        Assert.True((await transactions.UnmatchBankTransactionAsync(statementItem.Id, "Testing correction")).Succeeded); Assert.True((await transactions.MatchBankTransactionAsync(new MatchBankTransactionRequest(statementItem.Id, paymentJournalId))).Succeeded);

        var offsetAccount = before.GeneralLedger.Accounts.First(item => item.Type == "Expense" && !item.IsControlAccount).Number;
        var adjustment = await transactions.CreateReconciliationAdjustmentAsync(new CreateReconciliationAdjustmentRequest(fromBank.Id, new DateOnly(2026, 8, 2), 12m, offsetAccount, "ADJ-BANK-WF-1", "Statement interest")); Assert.True(adjustment.Succeeded, adjustment.ErrorMessage);
        Assert.False((await transactions.CreateReconciliationAdjustmentAsync(new CreateReconciliationAdjustmentRequest(fromBank.Id, new DateOnly(2026, 8, 2), 12m, offsetAccount, "ADJ-BANK-WF-1", "Duplicate"))).Succeeded);
        Assert.True((await transactions.ReverseReconciliationAdjustmentAsync(new ReverseReconciliationAdjustmentRequest(adjustment.Id!.Value, new DateOnly(2026, 8, 2), "Incorrect statement line"))).Succeeded);
        Assert.Equal("Reversed", (await workspaceService.GetWorkspaceAsync()).Treasury.Adjustments!.Single(item => item.Id == adjustment.Id).Status);

        var transfer = await transactions.CreateBankTransferAsync(new CreateBankTransferRequest(fromBank.Id, toBank.Id, new DateOnly(2026, 8, 3), 50m, "TR-BANK-WF-1", "Move operating cash")); Assert.True(transfer.Succeeded, transfer.ErrorMessage);
        var afterTransfer = await workspaceService.GetWorkspaceAsync(); Assert.Equal(0m, afterTransfer.GeneralLedger.Accounts.Single(item => item.Number == "1050").Balance);
        Assert.Equal(before.Treasury.BankAccounts.Single(item => item.Id == fromBank.Id).CurrentBalance + 50m, afterTransfer.Treasury.BankAccounts.Single(item => item.Id == fromBank.Id).CurrentBalance);
        Assert.Equal(before.Treasury.BankAccounts.Single(item => item.Id == toBank.Id).CurrentBalance + 50m, afterTransfer.Treasury.BankAccounts.Single(item => item.Id == toBank.Id).CurrentBalance);
        var transferRecord = await db.BankTransfers.SingleAsync(item => item.Id == transfer.Id); var reconciliation = await transactions.ReconcileBankAccountAsync(new ReconcileBankAccountRequest(fromBank.Id, new DateOnly(2026, 8, 3), fromBank.LastReconciledBalance + 50m, [paymentJournalId, transferRecord.JournalEntryId])); Assert.True(reconciliation.Succeeded, reconciliation.ErrorMessage);
        var completed = (await workspaceService.GetWorkspaceAsync()).Treasury.Reconciliations!.Single(item => item.Id == reconciliation.Id); Assert.Equal("Completed", completed.Status); Assert.Equal(50m, completed.ClearedAmount); Assert.Equal(2, completed.ItemCount);
        Assert.False((await transactions.UnmatchBankTransactionAsync(statementItem.Id, "Cannot change closed report")).Succeeded);
        Assert.False((await transactions.ReverseBankTransferAsync(new ReverseBankTransferRequest(transfer.Id!.Value, new DateOnly(2026, 8, 4), "Incorrect transfer"))).Succeeded);
        Assert.True((await transactions.ReopenBankReconciliationAsync(new ReopenBankReconciliationRequest(reconciliation.Id!.Value, "Statement correction"))).Succeeded);
        Assert.Equal("Reopened", (await workspaceService.GetWorkspaceAsync()).Treasury.Reconciliations!.Single(item => item.Id == reconciliation.Id).Status);
        Assert.True((await transactions.UnmatchBankTransactionAsync(statementItem.Id, "Correct reopened statement")).Succeeded);
        Assert.True((await transactions.ReverseBankTransferAsync(new ReverseBankTransferRequest(transfer.Id!.Value, new DateOnly(2026, 8, 4), "Incorrect transfer"))).Succeeded);
        var afterReversal = await workspaceService.GetWorkspaceAsync(); Assert.Equal("Reversed", afterReversal.Treasury.Transfers!.Single(item => item.Id == transfer.Id).Status); Assert.Equal(0m, afterReversal.GeneralLedger.Accounts.Single(item => item.Number == "1050").Balance);
        Assert.Equal(before.Treasury.BankAccounts.Single(item => item.Id == fromBank.Id).CurrentBalance + 100m, afterReversal.Treasury.BankAccounts.Single(item => item.Id == fromBank.Id).CurrentBalance);
        Assert.Equal(before.Treasury.BankAccounts.Single(item => item.Id == toBank.Id).CurrentBalance, afterReversal.Treasury.BankAccounts.Single(item => item.Id == toBank.Id).CurrentBalance);
        await using var auditDb = await factory.CreateDbContextAsync(); Assert.True(await auditDb.BusinessAuditEntries.AnyAsync(item => item.Action == "bank-transfer.reversed" && item.EntityId == transfer.Id)); Assert.True(await auditDb.BusinessAuditEntries.AnyAsync(item => item.Action == "bank-reconciliation.adjustment-reversed" && item.EntityId == adjustment.Id));
    }

    [Fact]
    public async Task BankingWorkflow_EnforcesCompanyIsolationAndSeparateReversalAuthority()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(item => item.Id).FirstAsync();
        var foreignCompanyId = Guid.NewGuid(); var foreignBankId = Guid.NewGuid();
        db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Foreign company", LegalName = "Foreign company", TaxId = "foreign", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
        db.BankAccounts.Add(new BankAccount { Id = foreignBankId, CompanyId = foreignCompanyId, Name = "Foreign bank", AccountNumberMasked = "****9999", LedgerAccountId = Guid.NewGuid(), CurrentBalance = 100m, LastReconciledOn = new DateOnly(2026, 3, 31), LastReconciledBalance = 100m, ConcurrencyToken = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync();

        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        void ActAs(params string[] permissions)
        {
            var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()) };
            claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }

        ActAs(BrassLedgerPermissions.LedgerManage);
        var banks = workspace.Treasury.BankAccounts;
        var transfer = await transactions.CreateBankTransferAsync(new CreateBankTransferRequest(banks.First().Id, banks.Last().Id, new DateOnly(2026, 8, 10), 5m, "TR-SOD-BANK-1", "Permission test"));
        Assert.True(transfer.Succeeded, transfer.ErrorMessage);
        Assert.False((await transactions.ReverseBankTransferAsync(new ReverseBankTransferRequest(transfer.Id!.Value, new DateOnly(2026, 8, 11), "Requires reversal permission"))).Succeeded);
        Assert.False((await transactions.CreateBankTransferAsync(new CreateBankTransferRequest(banks.First().Id, foreignBankId, new DateOnly(2026, 8, 10), 5m, "TR-FOREIGN-BANK-1", "Isolation test"))).Succeeded);
        Assert.False((await transactions.ImportBankStatementAsync(new ImportBankStatementRequest(foreignBankId, "foreign.csv", "CSV", "ExternalId,Date,Amount\nFOREIGN-1,2026-08-10,1.00"))).Succeeded);

        ActAs(BrassLedgerPermissions.LedgerManage, BrassLedgerPermissions.JournalReverse);
        Assert.True((await transactions.ReverseBankTransferAsync(new ReverseBankTransferRequest(transfer.Id.Value, new DateOnly(2026, 8, 11), "Authorized correction"))).Succeeded);
    }

    [Fact]
    public async Task TransactionService_ReconcilesOnlySelectedClearedBankActivity()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var initial = await workspaceService.GetWorkspaceAsync();
        var bank = initial.Treasury.BankAccounts.First();
        var invoice = initial.Receivables.Invoices.First();
        var invoicePayment = await transactions.ApplyInvoicePaymentAsync(new ApplyInvoicePaymentRequest(invoice.Id, bank.Id, new DateOnly(2026, 4, 1), 100m, "DEP-CLEARED"));
        Assert.True(invoicePayment.Succeeded, invoicePayment.ErrorMessage);
        var bill = await transactions.CreateVendorBillAsync(new CreateVendorBillRequest(initial.Payables.Vendors.First().Id, "B-REC-1", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), 40m, "5100", "Reconciliation test bill"));
        Assert.True(bill.Succeeded, bill.ErrorMessage);
        var billPayment = await transactions.ApplyBillPaymentAsync(new ApplyBillPaymentRequest(bill.Id!.Value, bank.Id, new DateOnly(2026, 4, 2), 40m, "CHK-OUTSTANDING"));
        Assert.True(billPayment.Succeeded, billPayment.ErrorMessage);

        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var clearedEntryId = (await db.JournalEntries.SingleAsync(entry => entry.Reference == "DEP-CLEARED")).Id;
        var reconciliation = await transactions.ReconcileBankAccountAsync(new ReconcileBankAccountRequest(
            bank.Id, new DateOnly(2026, 4, 2), bank.LastReconciledBalance + 100m, [clearedEntryId], "April statement"));

        Assert.True(reconciliation.Succeeded, reconciliation.ErrorMessage);
        var reconciledBank = await db.BankAccounts.SingleAsync(account => account.Id == bank.Id);
        Assert.Equal(40m, reconciledBank.UnreconciledAmount);
        Assert.Equal(bank.LastReconciledBalance + 100m, reconciledBank.LastReconciledBalance);
        var recorded = await db.BankReconciliations.SingleAsync(item => item.BankAccountId == bank.Id);
        Assert.Equal(bank.LastReconciledBalance + 60m, recorded.BookBalance);
        Assert.Equal("April statement", recorded.Notes);
        var clearedIds = await db.BankReconciliationItems.Where(item => item.BankReconciliationId == recorded.Id).Select(item => item.JournalEntryId).ToListAsync();
        Assert.Equal([clearedEntryId], clearedIds);
    }

    [Fact]
    public async Task PayrollDeductionPlans_AreEffectiveDatedAuditableAndApplyFederalGarnishmentLimit()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IPayrollDeductionConfigurationService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var employee = await db.Employees.FirstAsync(item => item.IsActive);
        employee.PayrollFrequency = "Weekly"; employee.PreTaxBenefitDeductions = 0; employee.PostTaxBenefitDeductions = 0; employee.ConcurrencyToken = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
        var bankId = await db.BankAccounts.Where(item => item.CompanyId == employee.CompanyId).Select(item => item.Id).FirstAsync();

        var invalid = await configuration.SavePlanAsync(new SavePayrollDeductionPlanRequest(null, "GARN-BAD", "Unverified garnishment", "OrdinaryGarnishment", "Fixed", 500m, 0, false, false, false, false, false, "2200", 10, null, null, 0, "OrdinaryGarnishmentFederal", "{}", "", null, new DateOnly(2026, 1, 1), null, true));
        Assert.False(invalid.Succeeded);
        Assert.Contains("official source", invalid.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var planResult = await configuration.SavePlanAsync(new SavePayrollDeductionPlanRequest(null, "GARN-ORD", "Ordinary creditor garnishment", "OrdinaryGarnishment", "Fixed", 500m, 0, false, false, false, false, false, "2200", 10, null, null, 0, "OrdinaryGarnishmentFederal", "{\"maxDisposablePercent\":0.25,\"protectedMinimumHourlyRate\":7.25,\"protectedHoursPerWeek\":30}", "https://www.dol.gov/agencies/whd/fact-sheets/30-cppa", new DateOnly(2026, 8, 25), new DateOnly(2026, 1, 1), null, true));
        Assert.True(planResult.Succeeded, planResult.ErrorMessage);
        var electionResult = await configuration.SaveElectionAsync(new SaveEmployeePayrollDeductionElectionRequest(null, employee.Id, planResult.Id!.Value, null, null, null, "{\"court\":\"Example County Court\",\"caseNumber\":\"TEST-123\"}", new DateOnly(2026, 1, 1), null, true));
        Assert.True(electionResult.Succeeded, electionResult.ErrorMessage);
        var overlapping = await configuration.SaveElectionAsync(new SaveEmployeePayrollDeductionElectionRequest(null, employee.Id, planResult.Id.Value, 100m, null, null, "{}", new DateOnly(2026, 6, 1), null, true));
        Assert.False(overlapping.Succeeded);
        Assert.Contains("overlapping", overlapping.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var request = new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 8, 28), "PR-GARN-1", [new EmployeePayrollInput(employee.Id, 500m)], new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 28));
        var preview = await transactions.PreviewEmployeePayrollRunAsync(request);
        Assert.NotNull(preview);
        var estimate = Assert.Single(preview!.Employees);
        var deduction = Assert.Single(estimate.Deductions!, item => item.PayrollDeductionPlanId == planResult.Id);
        var disposable = estimate.GrossPay - estimate.EmployeeWithholdings;
        var expectedLimit = decimal.Round(Math.Min(disposable * .25m, Math.Max(0, disposable - 217.50m)), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(500m, deduction.RequestedEmployeeAmount);
        Assert.Equal(expectedLimit, deduction.EmployeeAmount);
        Assert.True(deduction.LimitApplied);
        Assert.Equal("OrdinaryGarnishmentFederal", deduction.LimitRuleCode);
        Assert.Contains("officialSourceUrl", deduction.CalculationTraceJson);

        var draft = await transactions.SaveEmployeePayrollRunDraftAsync(request);
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        await using var persistedDb = await factory.CreateDbContextAsync();
        var persisted = await persistedDb.PayrollDeductionLines.SingleAsync(item => item.PayrollDeductionPlanId == planResult.Id && item.EmployeePayrollDeductionElectionId == electionResult.Id);
        Assert.Equal(500m, persisted.RequestedEmployeeAmount);
        Assert.Equal(expectedLimit, persisted.EmployeeAmount);
        Assert.True(persisted.LimitApplied);
        Assert.True(await persistedDb.BusinessAuditEntries.AnyAsync(item => item.Action == "payroll-deduction-plan.created" && item.EntityId == planResult.Id));
        Assert.True(await persistedDb.BusinessAuditEntries.AnyAsync(item => item.Action == "employee-payroll-deduction-election.created" && item.EntityId == electionResult.Id));
    }

    [Fact]
    public async Task PayrollPaymentFiles_AreProtectedReconciledFixedWidthAndVoidedWithReversal()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var paymentFiles = scope.ServiceProvider.GetRequiredService<IPayrollPaymentFileService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var employee = await db.Employees.FirstAsync(item => item.IsActive);
        employee.DirectDepositEnabled = true; employee.DirectDepositAuthorizationOn = new DateOnly(2026, 1, 15); employee.DirectDepositAuthorizationReference = "Signed authorization ACH-TEST"; employee.BankRoutingNumber = "021000021"; employee.BankAccountNumber = "1234567890"; employee.BankAccountType = "Checking"; employee.PreTaxBenefitDeductions = 0; employee.PostTaxBenefitDeductions = 0; employee.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.PayrollDepositScheduleConfigurations.Add(new PayrollDepositScheduleConfiguration { Id = Guid.NewGuid(), CompanyId = employee.CompanyId, TaxYear = 2026, ScheduleType = "Monthly", LookbackLiability = 40000m, LookbackPeriodStart = new DateOnly(2024, 7, 1), LookbackPeriodEnd = new DateOnly(2025, 6, 30), MonthlyThreshold = 50000m, NextDayThreshold = 100000m, LegalHolidaysJson = "[\"2026-09-07\"]", OfficialRulesUrl = "https://www.irs.gov/publications/p15", OfficialCalendarUrl = "https://www.irs.gov/publications/p509", SourceRetrievedOn = new DateOnly(2026, 8, 25), IsApproved = true, IsActive = true, ConcurrencyToken = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync();
        var bank = await db.BankAccounts.FirstAsync(item => item.CompanyId == employee.CompanyId);
        var payroll = await transactions.PostEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 8, 28), "PR-ACH-1", [new EmployeePayrollInput(employee.Id, 1000m)], new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 28)));
        Assert.True(payroll.Succeeded, payroll.ErrorMessage);
        var federalDueDates = await db.PayrollLiabilities.Where(item => item.PayrollRunId == payroll.Id && item.JurisdictionCode == "US").Select(item => item.DueDate).Distinct().ToListAsync();
        Assert.Equal([new DateOnly(2026, 9, 15)], federalDueDates);

        var unvalidatedOrigin = await paymentFiles.SaveBankOriginAsync(new SavePayrollBankOriginConfigurationRequest(null, bank.Id, "021000021", "123456789", "EXAMPLE ODFI", "BRASS LEDGER MFG", "1123456789", "PAYROLL", "02100002", new DateOnly(2026, 1, 1), null, true, false, "Awaiting bank test"));
        Assert.True(unvalidatedOrigin.Succeeded, unvalidatedOrigin.ErrorMessage);
        var blocked = await paymentFiles.GenerateAsync(new GeneratePayrollPaymentFileRequest(payroll.Id!.Value, "NachaPpd", new DateOnly(2026, 8, 28)));
        Assert.False(blocked.Succeeded);
        Assert.Contains("bank-validated", blocked.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var configuration = (await paymentFiles.GetAsync()).BankOrigins.Single(item => item.Id == unvalidatedOrigin.Id);
        var validatedOrigin = await paymentFiles.SaveBankOriginAsync(new SavePayrollBankOriginConfigurationRequest(configuration.Id, bank.Id, "021000021", "123456789", "EXAMPLE ODFI", "BRASS LEDGER MFG", "1123456789", "PAYROLL", "02100002", new DateOnly(2026, 1, 1), null, true, true, "Validated against ODFI test file on 2026-08-25", configuration.ConcurrencyToken));
        Assert.True(validatedOrigin.Succeeded, validatedOrigin.ErrorMessage);

        var generated = await paymentFiles.GenerateAsync(new GeneratePayrollPaymentFileRequest(payroll.Id.Value, "NachaPpd", new DateOnly(2026, 8, 28)));
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        Assert.False((await paymentFiles.GenerateAsync(new GeneratePayrollPaymentFileRequest(payroll.Id.Value, "NachaPpd"))).Succeeded);
        var download = await paymentFiles.DownloadAsync(generated.Id!.Value);
        Assert.NotNull(download);
        var content = System.Text.Encoding.UTF8.GetString(download!.Content);
        var records = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, records.Length % 10);
        Assert.All(records, record => Assert.Equal(94, System.Text.Encoding.ASCII.GetByteCount(record)));
        Assert.StartsWith("1", records[0]); Assert.StartsWith("5", records[1]); Assert.StartsWith("6", records[2]); Assert.StartsWith("8", records[3]); Assert.StartsWith("9", records[4]);
        Assert.Equal("220", records[1][1..4]); Assert.Equal("PPD", records[1][50..53]); Assert.Equal("22", records[2][1..3]);
        var workspace = await paymentFiles.GetAsync(); var file = Assert.Single(workspace.Files, item => item.Id == generated.Id);
        Assert.Equal(1, file.EntryCount); Assert.Equal(1000m - (await db.PayrollRunEmployeeLines.SingleAsync(item => item.PayrollRunId == payroll.Id)).EmployeeWithholdings, file.CreditTotal);
        Assert.Equal(file.ContentSha256, download.ContentSha256); Assert.Equal("GeneratedForBankValidation", file.Status);

        var run = await db.PayrollRuns.SingleAsync(item => item.Id == payroll.Id);
        var reversal = await transactions.ReversePayrollRunAsync(new ReversePayrollRunRequest(run.Id, run.PayDate, "Test ACH reversal", run.ConcurrencyToken));
        Assert.True(reversal.Succeeded, reversal.ErrorMessage);
        var voided = Assert.Single((await paymentFiles.GetAsync()).Files, item => item.Id == generated.Id);
        Assert.Equal("Voided", voided.Status); Assert.Contains("Payroll reversed", voided.VoidReason);
        Assert.True(await db.BusinessAuditEntries.AnyAsync(item => item.Action == "payroll-payment-file.generated" && item.EntityId == generated.Id));
    }

    [Fact]
    public async Task FederalDepositSchedule_AssignsHolidayAdjustedAndNextDayDueDates()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IPayrollDepositScheduleService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        const string holidays = "[\"2026-01-01\",\"2026-01-19\",\"2026-02-16\",\"2026-04-16\",\"2026-05-25\",\"2026-06-19\",\"2026-07-03\",\"2026-09-07\",\"2026-10-12\",\"2026-11-11\",\"2026-11-26\",\"2026-12-25\"]";
        var saved = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(null, 2026, "Monthly", 40000m, new DateOnly(2024, 7, 1), new DateOnly(2025, 6, 30), 50000m, 100000m, 2500m, "[]", holidays, "https://www.irs.gov/publications/p15", "https://www.irs.gov/publications/p509", new DateOnly(2026, 8, 25), "Controller verified the 2026 lookback and official calendar.", true, true));
        Assert.True(saved.Succeeded, saved.ErrorMessage);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var employee = await db.Employees.FirstAsync();
            AddFederalLiability(db, employee, new DateOnly(2026, 1, 30), "PR-DEP-MONTHLY", 1000m);
            AddFederalLiability(db, employee, new DateOnly(2026, 5, 8), "PR-DEP-NEXT-1", 60000m);
            AddFederalLiability(db, employee, new DateOnly(2026, 5, 12), "PR-DEP-NEXT-2", 40000m);
            await db.SaveChangesAsync();
        }

        var current = Assert.Single((await schedules.GetAsync()).Configurations);
        var recalculated = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(current.Id, current.TaxYear, current.ScheduleType, current.LookbackLiability, current.LookbackPeriodStart, current.LookbackPeriodEnd, current.MonthlyThreshold, current.NextDayThreshold, current.SmallLiabilityThreshold, "[]", holidays, current.OfficialRulesUrl, current.OfficialCalendarUrl, current.SourceRetrievedOn, current.ReviewNotes, true, true, current.ConcurrencyToken));
        Assert.True(recalculated.Succeeded, recalculated.ErrorMessage);

        await using var verifiedDb = await factory.CreateDbContextAsync();
        var byReference = await verifiedDb.PayrollLiabilities.Join(verifiedDb.PayrollRuns, liability => liability.PayrollRunId, run => run.Id, (liability, run) => new { run.Reference, Liability = liability }).Where(item => item.Reference.StartsWith("PR-DEP-")).ToDictionaryAsync(item => item.Reference, item => item.Liability);
        Assert.Equal(new DateOnly(2026, 2, 17), byReference["PR-DEP-MONTHLY"].DueDate);
        Assert.Equal(new DateOnly(2026, 5, 13), byReference["PR-DEP-NEXT-1"].DueDate);
        Assert.Equal(new DateOnly(2026, 5, 13), byReference["PR-DEP-NEXT-2"].DueDate);
        var summary = Assert.Single((await schedules.GetAsync()).Summaries);
        Assert.True(summary.NextDayRuleTriggered);
        Assert.Contains("Semiweekly", summary.EffectiveScheduleType);
        Assert.True(await verifiedDb.BusinessAuditEntries.AnyAsync(item => item.Action == "payroll-deposit-schedule.updated" && item.EntityId == current.Id));
        byReference["PR-DEP-MONTHLY"].Status = "Paid";
        await verifiedDb.SaveChangesAsync();
        var refreshed = Assert.Single((await schedules.GetAsync()).Configurations);
        var deactivated = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(refreshed.Id, refreshed.TaxYear, refreshed.ScheduleType, refreshed.LookbackLiability, refreshed.LookbackPeriodStart, refreshed.LookbackPeriodEnd, refreshed.MonthlyThreshold, refreshed.NextDayThreshold, refreshed.SmallLiabilityThreshold, "[]", holidays, refreshed.OfficialRulesUrl, refreshed.OfficialCalendarUrl, refreshed.SourceRetrievedOn, "Schedule deactivated for immutability test", false, false, refreshed.ConcurrencyToken));
        Assert.True(deactivated.Succeeded, deactivated.ErrorMessage);
        await verifiedDb.Entry(byReference["PR-DEP-MONTHLY"]).ReloadAsync(); await verifiedDb.Entry(byReference["PR-DEP-NEXT-1"]).ReloadAsync();
        Assert.Equal(new DateOnly(2026, 2, 17), byReference["PR-DEP-MONTHLY"].DueDate);
        Assert.Null(byReference["PR-DEP-NEXT-1"].DueDate);
    }

    [Fact]
    public async Task FederalDepositSchedule_UsesThreeBusinessDaysForSemiweeklyPeriods()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IPayrollDepositScheduleService>();
        var disasterRelief = scope.ServiceProvider.GetRequiredService<IPayrollDisasterReliefService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var invalidLookback = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(null, 2025, "Semiweekly", 60000m, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), 50000m, 100000m, 2500m, "[]", "[\"2025-01-01\"]", "https://www.irs.gov/publications/p15", "https://www.irs.gov/publications/p509", new DateOnly(2025, 1, 1), "", true, true));
        Assert.False(invalidLookback.Succeeded);
        Assert.Contains("lookback", invalidLookback.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var spoofedSource = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(null, 2025, "Semiweekly", 60000m, new DateOnly(2023, 7, 1), new DateOnly(2024, 6, 30), 50000m, 100000m, 2500m, "[]", "[\"2025-01-01\"]", "https://notirs.gov/publications/p15", "https://www.irs.gov/publications/p509", new DateOnly(2025, 1, 1), "", true, true));
        Assert.False(spoofedSource.Succeeded);
        Assert.Contains("official", spoofedSource.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var saved = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(null, 2025, "Semiweekly", 60000m, new DateOnly(2023, 7, 1), new DateOnly(2024, 6, 30), 50000m, 100000m, 2500m, "[]", "[\"2025-01-01\"]", "https://www.irs.gov/publications/p15", "https://www.irs.gov/publications/p509", new DateOnly(2025, 1, 1), "", true, true));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { AddFederalLiability(db, await db.Employees.FirstAsync(), new DateOnly(2025, 5, 30), "PR-DEP-SEMI", 1000m); await db.SaveChangesAsync(); }
        var current = Assert.Single((await schedules.GetAsync()).Configurations);
        Assert.True((await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(current.Id, current.TaxYear, current.ScheduleType, current.LookbackLiability, current.LookbackPeriodStart, current.LookbackPeriodEnd, current.MonthlyThreshold, current.NextDayThreshold, current.SmallLiabilityThreshold, "[]", "[\"2025-01-01\"]", current.OfficialRulesUrl, current.OfficialCalendarUrl, current.SourceRetrievedOn, current.ReviewNotes, true, true, current.ConcurrencyToken))).Succeeded);
        await using var verifiedDb = await factory.CreateDbContextAsync();
        var liability = await verifiedDb.PayrollLiabilities.Join(verifiedDb.PayrollRuns, liability => liability.PayrollRunId, run => run.Id, (liability, run) => new { run.Reference, Liability = liability }).Where(item => item.Reference == "PR-DEP-SEMI").Select(item => item.Liability).SingleAsync();
        Assert.Equal(new DateOnly(2025, 6, 4), liability.DueDate);
        Assert.Equal("Semiweekly", liability.DepositScheduleType); Assert.Equal("Semiweekly", liability.DepositRuleCode); Assert.Equal(saved.Id, liability.DepositScheduleConfigurationId);
        var bankId = await verifiedDb.BankAccounts.Select(item => item.Id).FirstAsync();
        Assert.True((await transactions.RecordPayrollLiabilityPaymentAsync(new RecordPayrollLiabilityPaymentRequest(bankId, new DateOnly(2025, 6, 4), "DEP-SHORTFALL-INITIAL", "United States Treasury", "EFT", [new PayrollLiabilityPaymentApplicationInput(liability.Id, 950m)]))).Succeeded);
        Assert.True((await transactions.RecordPayrollLiabilityPaymentAsync(new RecordPayrollLiabilityPaymentRequest(bankId, new DateOnly(2025, 7, 16), "DEP-SHORTFALL-MAKEUP", "United States Treasury", "EFT", [new PayrollLiabilityPaymentApplicationInput(liability.Id, 50m)]))).Succeeded);
        var shortfall = Assert.Single((await schedules.GetAsync()).Shortfalls!);
        Assert.Equal(1000m, shortfall.RequiredAmount); Assert.Equal(950m, shortfall.PaidByDueDate); Assert.Equal(50m, shortfall.ShortfallAtDueDate);
        Assert.Equal(100m, shortfall.SafeHarborTolerance); Assert.Equal(new DateOnly(2025, 7, 16), shortfall.MakeupDueDate); Assert.Equal(1000m, shortfall.PaidByMakeupDate); Assert.Equal("MadeUpWithinTolerance", shortfall.Status);
        const string reliefActions = "[{\"ActionType\":\"DepositPenaltyAbatement\",\"OriginalDueOnOrAfter\":\"2025-06-01\",\"OriginalDueBefore\":\"2025-06-05\",\"ReliefDeadline\":\"2025-07-16\",\"Notes\":\"Deposit penalties abated only when deposited by the announcement deadline.\"},{\"ActionType\":\"ReturnFilingPostponement\",\"OriginalDueOnOrAfter\":\"2025-04-01\",\"OriginalDueBefore\":\"2025-08-01\",\"ReliefDeadline\":\"2025-08-15\",\"Notes\":\"Return deadline relief is tracked separately from deposits.\"}]";
        const string futureAction = "[{\"ActionType\":\"FutureReliefNotYetSupported\",\"OriginalDueOnOrAfter\":\"2025-06-01\",\"OriginalDueBefore\":\"2025-06-05\",\"ReliefDeadline\":\"2025-07-16\"}]";
        var unsupportedApproval = await disasterRelief.SaveAsync(new SavePayrollDisasterReliefRequest(null, "ZZ-2025-FUTURE", "Future relief example", "DR-9998", "[\"Test County, ZZ\"]", "PrincipalPlaceOfBusiness", "Eligibility proof", futureAction, "https://www.irs.gov/newsroom/tax-relief-in-disaster-situations", new DateOnly(2025, 6, 1), "Reviewer captured a new relief action that the runtime does not execute yet.", true, true));
        Assert.False(unsupportedApproval.Succeeded); Assert.Contains("unsupported action", unsupportedApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var relief = await disasterRelief.SaveAsync(new SavePayrollDisasterReliefRequest(null, "ZZ-2025-01", "Test severe storms", "DR-9999", "[\"Test County, ZZ\"]", "PrincipalPlaceOfBusiness", "IRS address-of-record confirmation 2025-06-01", reliefActions, "https://www.irs.gov/newsroom/tax-relief-in-disaster-situations", new DateOnly(2025, 6, 1), "Controller compared the exact covered area and separate deposit penalty window to the IRS announcement.", true, true));
        Assert.True(relief.Succeeded, relief.ErrorMessage);
        var reliefWorkspace = await disasterRelief.GetAsync(); var impact = Assert.Single(reliefWorkspace.DepositImpacts);
        Assert.Equal(new DateOnly(2025, 6, 4), impact.OriginalDueDate); Assert.Equal(new DateOnly(2025, 7, 16), impact.PenaltyReliefDeadline);
        Assert.Equal(950m, impact.PaidByOriginalDueDate); Assert.Equal(1000m, impact.PaidByReliefDeadline); Assert.Equal("PenaltyReliefConditionsMet", impact.Status);
        await verifiedDb.Entry(liability).ReloadAsync(); Assert.Equal(new DateOnly(2025, 6, 4), liability.DueDate);
    }

    [Fact]
    public async Task FederalDepositSchedule_AppliesOnlyEligibleSmallLiabilityReturnPaymentElections()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IPayrollDepositScheduleService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        const string holidays = "[\"2026-01-01\",\"2026-01-19\",\"2026-02-16\",\"2026-04-16\",\"2026-05-25\",\"2026-06-19\",\"2026-07-03\",\"2026-09-07\",\"2026-10-12\",\"2026-11-11\",\"2026-11-26\",\"2026-12-25\"]";
        await using (var db = await factory.CreateDbContextAsync())
        {
            var employee = await db.Employees.FirstAsync();
            AddFederalLiability(db, employee, new DateOnly(2025, 12, 15), "PR-DEP-PRIOR-SMALL", 1000m);
            AddFederalLiability(db, employee, new DateOnly(2026, 2, 13), "PR-DEP-RETURN-Q1", 10000m);
            await db.SaveChangesAsync();
        }
        var saved = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(null, 2026, "Monthly", 40000m, new DateOnly(2024, 7, 1), new DateOnly(2025, 6, 30), 50000m, 100000m, 2500m, "[1]", holidays, "https://www.irs.gov/publications/p15", "https://www.irs.gov/publications/p509", new DateOnly(2026, 8, 25), "Prior-quarter Form 941 liability verified below $2,500.", true, true));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var dueDate = await db.PayrollLiabilities.Join(db.PayrollRuns, liability => liability.PayrollRunId, run => run.Id, (liability, run) => new { run.Reference, liability.DueDate }).Where(item => item.Reference == "PR-DEP-RETURN-Q1").Select(item => item.DueDate).SingleAsync();
            Assert.Equal(new DateOnly(2026, 4, 30), dueDate);
            var employee = await db.Employees.FirstAsync();
            AddFederalLiability(db, employee, new DateOnly(2026, 5, 8), "PR-DEP-RETURN-Q2-A", 60000m);
            AddFederalLiability(db, employee, new DateOnly(2026, 5, 12), "PR-DEP-RETURN-Q2-B", 40000m);
            await db.SaveChangesAsync();
        }
        var current = Assert.Single((await schedules.GetAsync()).Configurations);
        var blocked = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(current.Id, current.TaxYear, current.ScheduleType, current.LookbackLiability, current.LookbackPeriodStart, current.LookbackPeriodEnd, current.MonthlyThreshold, current.NextDayThreshold, current.SmallLiabilityThreshold, "[1,2]", holidays, current.OfficialRulesUrl, current.OfficialCalendarUrl, current.SourceRetrievedOn, "Attempted Q2 election", true, true, current.ConcurrencyToken));
        Assert.False(blocked.Succeeded);
        Assert.Contains("next-day", blocked.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await schedules.GetAsync()).Shortfalls!);
    }

    [Fact]
    public async Task FederalDepositSchedule_DoesNotCombineSemiweeklyLiabilityAcrossQuarterBoundary()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IPayrollDepositScheduleService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var employee = await db.Employees.FirstAsync();
            AddFederalLiability(db, employee, new DateOnly(2026, 9, 30), "PR-DEP-Q3-END", 60000m);
            AddFederalLiability(db, employee, new DateOnly(2026, 10, 2), "PR-DEP-Q4-START", 60000m);
            await db.SaveChangesAsync();
        }
        const string holidays = "[\"2026-01-01\",\"2026-01-19\",\"2026-02-16\",\"2026-04-16\",\"2026-05-25\",\"2026-06-19\",\"2026-07-03\",\"2026-09-07\",\"2026-10-12\",\"2026-11-11\",\"2026-11-26\",\"2026-12-25\"]";
        var saved = await schedules.SaveAsync(new SavePayrollDepositScheduleRequest(null, 2026, "Semiweekly", 60000m, new DateOnly(2024, 7, 1), new DateOnly(2025, 6, 30), 50000m, 100000m, 2500m, "[]", holidays, "https://www.irs.gov/publications/p15", "https://www.irs.gov/publications/p509", new DateOnly(2026, 8, 25), "IRS cross-quarter semiweekly example.", true, true));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        await using var verifiedDb = await factory.CreateDbContextAsync();
        var dueDates = await verifiedDb.PayrollLiabilities.Join(verifiedDb.PayrollRuns, liability => liability.PayrollRunId, run => run.Id, (liability, run) => new { run.Reference, liability.DueDate }).Where(item => item.Reference == "PR-DEP-Q3-END" || item.Reference == "PR-DEP-Q4-START").ToDictionaryAsync(item => item.Reference, item => item.DueDate);
        Assert.Equal(new DateOnly(2026, 10, 7), dueDates["PR-DEP-Q3-END"]); Assert.Equal(new DateOnly(2026, 10, 7), dueDates["PR-DEP-Q4-START"]);
        Assert.False(Assert.Single((await schedules.GetAsync()).Summaries).NextDayRuleTriggered);
    }

    private static void AddFederalLiability(BrassLedgerDbContext db, Employee employee, DateOnly payDate, string reference, decimal amount)
    {
        var runId = Guid.NewGuid(); var lineId = Guid.NewGuid();
        db.PayrollRuns.Add(new PayrollRun { Id = runId, CompanyId = employee.CompanyId, BankAccountId = db.BankAccounts.First(item => item.CompanyId == employee.CompanyId).Id, PayDate = payDate, PeriodStart = payDate, PeriodEnd = payDate, RunType = "Regular", Status = "Posted", Reference = reference, GrossPayroll = amount, EmployeeWithholdings = amount, NetPay = 0, PreparedAtUtc = DateTimeOffset.UtcNow, PostedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") });
        db.PayrollRunEmployeeLines.Add(new PayrollRunEmployeeLine { Id = lineId, PayrollRunId = runId, EmployeeId = employee.Id, GrossPay = amount, TaxableWages = amount, EmployeeWithholdings = amount, NetPay = 0 });
        db.PayrollLiabilities.Add(new PayrollLiability { Id = Guid.NewGuid(), CompanyId = employee.CompanyId, PayrollRunId = runId, PayrollRunEmployeeLineId = lineId, SourceType = "Tax", SourceLineId = Guid.NewGuid(), ObligationCode = "US-FIT", JurisdictionCode = "US", JurisdictionName = "Federal", Description = "Federal income tax withholding", OriginalAmount = amount, OutstandingAmount = amount, Status = "Open", ConcurrencyToken = Guid.NewGuid().ToString("N") });
    }

    private ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        return serviceCollection.BuildServiceProvider();
    }

    private static Microsoft.AspNetCore.Http.HttpContext CreatePermissionContext(string permission)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission),
                new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, "0e561f1b-47b0-4c33-bd9f-1a3298ed29c6")
            ], "test"));
        return context;
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
