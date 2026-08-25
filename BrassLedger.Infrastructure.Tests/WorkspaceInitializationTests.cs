using BrassLedger.Application.Accounting;
using BrassLedger.Application.Taxation;
using BrassLedger.Infrastructure.Auth;
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
            db.TaxProfiles.Single(profile => profile.TaxType == "FUTA").AnnualWageBase = 500m;
            db.TaxProfiles.Single(profile => profile.Jurisdiction == "Arizona").AnnualWageBase = 500m;
            await db.SaveChangesAsync();
        }

        var request = new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 5, 15), "EMP-PR-1", [new EmployeePayrollInput(employee.Id, 1_000m)]);
        var preview = await transactions.PreviewEmployeePayrollRunAsync(request);
        Assert.NotNull(preview);
        var line = Assert.Single(preview!.Employees);
        Assert.Equal("AZ", line.WorkState);
        Assert.Equal(100m, line.PreTaxDeductions);
        Assert.Equal(213m, line.EmployeeWithholdings); // 22% of $900 plus $15 additional withholding.
        Assert.Equal(25m, line.PostTaxDeductions);
        Assert.Equal(15.25m, line.EmployerPayrollTaxes); // FUTA and Arizona SUI are each capped at $500.
        Assert.Equal(662m, line.NetPay);

        var result = await transactions.PostEmployeePayrollRunAsync(request);
        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var verification = await factory.CreateDbContextAsync();
        var run = await verification.PayrollRuns.SingleAsync(run => run.Id == result.Id);
        var persistedLine = await verification.PayrollRunEmployeeLines.SingleAsync(item => item.PayrollRunId == run.Id);
        var journal = await verification.JournalEntries.SingleAsync(entry => entry.SourceDocumentId == run.Id && entry.SourceDocumentType == "PayrollRun");
        Assert.Equal(662m, run.NetPay);
        Assert.Equal(employee.Id, persistedLine.EmployeeId);
        Assert.Equal(run.Id, journal.SourceDocumentId);
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
                new BrassLedger.Domain.Accounting.TaxProfile { Id = Guid.NewGuid(), CompanyId = companyId, Jurisdiction = "Worktown", TaxType = "Local withholding", Rate = .01m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "Test", IsEmployerSpecific = false },
                new BrassLedger.Domain.Accounting.TaxProfile { Id = Guid.NewGuid(), CompanyId = companyId, Jurisdiction = "Residenceville", TaxType = "Local withholding", Rate = .02m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "Test", IsEmployerSpecific = false });
            await db.SaveChangesAsync();
        }
        var rule = await transactions.SavePayrollJurisdictionRuleAsync(new SavePayrollJurisdictionRuleRequest(null, "Residenceville", "Worktown", true, .5m, true, "Test reciprocity and resident credit"));
        Assert.True(rule.Succeeded, rule.ErrorMessage);
        var preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 5, 15), "LOCATION-PREVIEW", [new EmployeePayrollInput(employee.Id, 1_000m)]));
        Assert.NotNull(preview);
        Assert.Equal(230m, Assert.Single(preview!.Employees).EmployeeWithholdings); // Work withholding is exempt; the residence tax receives the configured 50% credit.
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
        Assert.Equal(100m, Assert.Single(preview!.Employees).EmployeeWithholdings);
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
        Assert.Equal(14.12m, Assert.Single(preview!.Employees).EmployeeWithholdings); // NY Method II $8.01 + NYC resident Method II $6.11.

        setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0m, 0m, 0m, "NY", "New York City", "NY", "New York City", "Annual"));
        Assert.True(setup.Succeeded, setup.ErrorMessage);
        preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "NYC-WHOLE-WAGE-PREVIEW", [new EmployeePayrollInput(employee.Id, 1_207_400m)]));
        Assert.NotNull(preview);
        Assert.Equal(176_188m, Assert.Single(preview!.Employees).EmployeeWithholdings); // NY Method III $125,400 + NYC resident exact $50,788; Method II is excluded.

        setup = await transactions.SaveEmployeePayrollSetupAsync(new SaveEmployeePayrollSetupRequest(employee.Id, "Single", 0, 0m, 0m, 0m, "NY", "Albany", "NY", "Yonkers", "Weekly"));
        Assert.True(setup.Succeeded, setup.ErrorMessage);
        preview = await transactions.PreviewEmployeePayrollRunAsync(new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "YONKERS-NONRESIDENT-PREVIEW", [new EmployeePayrollInput(employee.Id, 200m)]));
        Assert.NotNull(preview);
        Assert.Equal(3.06m, Assert.Single(preview!.Employees).EmployeeWithholdings); // NY State $2.25 + Yonkers nonresident earnings tax $0.81.
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
        Assert.Equal(2, await db.JournalEntries.CountAsync(entry => entry.SourceDocumentType == "VendorBill" && entry.SourceDocumentId == billResult.Id));
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
            bank.Id, new DateOnly(2026, 4, 2), bank.LastReconciledBalance + 100m, [clearedEntryId]));

        Assert.True(reconciliation.Succeeded, reconciliation.ErrorMessage);
        var reconciledBank = await db.BankAccounts.SingleAsync(account => account.Id == bank.Id);
        Assert.Equal(40m, reconciledBank.UnreconciledAmount);
        Assert.Equal(bank.LastReconciledBalance + 100m, reconciledBank.LastReconciledBalance);
        var recorded = await db.BankReconciliations.SingleAsync(item => item.BankAccountId == bank.Id);
        var clearedIds = await db.BankReconciliationItems.Where(item => item.BankReconciliationId == recorded.Id).Select(item => item.JournalEntryId).ToListAsync();
        Assert.Equal([clearedEntryId], clearedIds);
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
            [new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)], "test"));
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
