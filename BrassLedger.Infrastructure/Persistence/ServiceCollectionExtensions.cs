using System.Data;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Application.Accounting;
using BrassLedger.Application.Catalog;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Catalog;
using BrassLedger.Infrastructure.SecurityAdministration;
using BrassLedger.Infrastructure.Security;
using BrassLedger.Infrastructure.Taxation;
using BrassLedger.Application.Security;
using BrassLedger.Application.Taxation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BrassLedger.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBrassLedgerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath,
        bool seedSampleData = false)
    {
        var dataDirectory = BuildDataDirectory(configuration, contentRootPath);
        var keysDirectory = Path.Combine(dataDirectory, "keys");
        Directory.CreateDirectory(keysDirectory);

        var postgresConnectionString =
            configuration.GetConnectionString("Postgres")
            ?? configuration.GetConnectionString("PostgreSql")
            ?? configuration.GetConnectionString("BrassLedgerPostgres");

        var sqliteConnectionString =
            configuration.GetConnectionString("Sqlite")
            ?? configuration.GetConnectionString("BrassLedgerSqlite")
            ?? BuildDefaultSqliteConnectionString(dataDirectory);

        var dataProtectionBuilder = services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
            .SetApplicationName("BrassLedger");

        if (OperatingSystem.IsWindows())
        {
            dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        services.AddDbContextFactory<BrassLedgerDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(postgresConnectionString))
            {
                options.UseNpgsql(postgresConnectionString);
            }
            else
            {
                options.UseSqlite(sqliteConnectionString);
            }
        });

        services.AddHttpContextAccessor();
        services.AddHttpClient("TaxSourceCapture", client => client.Timeout = TimeSpan.FromSeconds(45))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton(new BrassLedgerStoragePaths(dataDirectory, keysDirectory));
        services.AddSingleton(Options.Create(BuildBootstrapOptions(configuration, seedSampleData)));
        services.AddSingleton<ISensitiveDataProtector, SensitiveDataProtector>();
        services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IBootstrapWorkspaceService, BootstrapWorkspaceService>();
        services.AddScoped<IBusinessWorkspaceService, BusinessWorkspaceService>();
        services.AddScoped<ICompanyManagementService, CompanyManagementService>();
        services.AddScoped<IConsolidationService, ConsolidationService>();
        services.AddScoped<IAccountingPeriodService, AccountingPeriodService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IIntegrationService, IntegrationService>();
        services.AddScoped<IAccountingTransactionService, AccountingTransactionService>();
        services.AddScoped<IAccountingInterchangeService, QuickBooksOnlineInterchangeService>();
        services.AddScoped<ISecurityAdministrationService, SecurityAdministrationService>();
        services.AddScoped<ITaxAdministrationService, TaxAdministrationService>();
        services.AddScoped<IFakeDataPopulationService, FakeDataPopulationService>();
        services.AddSingleton<IProductCatalogService, StaticProductCatalogService>();

        return services;
    }

    public static async Task InitializeBrassLedgerAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var bootstrapOptions = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        // Force creation of the persisted key ring during initialization, before the first backup can run.
        _ = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("BrassLedger.KeyRingInitialization.v1")
            .Protect("initialized");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureLegacySchemaCompatibilityAsync(dbContext, cancellationToken);
        await EnsureCaseInsensitiveUserNameUniquenessAsync(dbContext, cancellationToken);
        await BrassLedgerSeedData.SeedAsync(dbContext, passwordHasher, bootstrapOptions, cancellationToken);
        await DefaultAccountingSetup.EnsureMinimumSetupAsync(dbContext, cancellationToken);
    }

    private static BootstrapOptions BuildBootstrapOptions(IConfiguration configuration, bool seedSampleData)
    {
        var options = configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new BootstrapOptions();
        options.SeedSampleData = options.SeedSampleData || seedSampleData;
        return options;
    }

    private static string BuildDataDirectory(IConfiguration configuration, string contentRootPath)
    {
        var configuredDataRoot =
            configuration["Storage:DataRoot"]
            ?? configuration["BrassLedger:DataRoot"];

        if (!string.IsNullOrWhiteSpace(configuredDataRoot))
        {
            var explicitDataDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredDataRoot));
            Directory.CreateDirectory(explicitDataDirectory);
            return explicitDataDirectory;
        }

        var contentRootDataDirectory = Path.Combine(contentRootPath, "App_Data");
        if (TryEnsureWritableDirectory(contentRootDataDirectory))
        {
            return contentRootDataDirectory;
        }

        var localApplicationDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationDataRoot))
        {
            localApplicationDataRoot = Path.GetTempPath();
        }

        var fallbackDataDirectory = Path.Combine(localApplicationDataRoot, "BrassLedger", "App_Data");
        Directory.CreateDirectory(fallbackDataDirectory);
        return fallbackDataDirectory;
    }

    private static bool TryEnsureWritableDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);

            var probeFilePath = Path.Combine(directoryPath, $".write-test-{Guid.NewGuid():N}.tmp");
            using var probeStream = new FileStream(
                probeFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.DeleteOnClose);

            probeStream.WriteByte(0);
            probeStream.Flush();

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string BuildDefaultSqliteConnectionString(string dataDirectory)
    {
        return $"Data Source={Path.Combine(dataDirectory, "brassledger.db")}";
    }

    private static async Task EnsureLegacySchemaCompatibilityAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""AccessRoles"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AccessRoles"" PRIMARY KEY,
                    ""CompanyId"" TEXT NOT NULL,
                    ""Name"" TEXT NOT NULL,
                    ""Description"" TEXT NOT NULL,
                    ""TemplateCode"" TEXT NOT NULL,
                    ""Permissions"" TEXT NOT NULL,
                    ""IsSystemRole"" INTEGER NOT NULL,
                    ""IsActive"" INTEGER NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccessRoles_CompanyId_Name"" ON ""AccessRoles"" (""CompanyId"", ""Name"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""CompanyMemberships"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""UserId"" TEXT NOT NULL, ""CompanyId"" TEXT NOT NULL, ""Role"" TEXT NOT NULL, ""IsOwner"" INTEGER NOT NULL, ""IsActive"" INTEGER NOT NULL, ""GrantedAtUtc"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CompanyMemberships_UserId_CompanyId"" ON ""CompanyMemberships"" (""UserId"", ""CompanyId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""AccountingPeriods"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""StartsOn"" TEXT NOT NULL, ""EndsOn"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""ClosedByUserId"" TEXT NULL, ""ClosedAtUtc"" TEXT NULL, ""Notes"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccountingPeriods_CompanyId_StartsOn_EndsOn"" ON ""AccountingPeriods"" (""CompanyId"", ""StartsOn"", ""EndsOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BusinessAuditEntries"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""UserId"" TEXT NULL, ""Action"" TEXT NOT NULL, ""EntityType"" TEXT NOT NULL, ""EntityId"" TEXT NULL, ""DetailJson"" TEXT NOT NULL, ""OccurredAtUtc"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_BusinessAuditEntries_CompanyId_OccurredAtUtc"" ON ""BusinessAuditEntries"" (""CompanyId"", ""OccurredAtUtc"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""IntegrationConnections"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""ProviderCode"" TEXT NOT NULL, ""Name"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""SettingsJson"" TEXT NOT NULL, ""CredentialsJson"" TEXT NOT NULL, ""LastValidatedAtUtc"" TEXT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_IntegrationConnections_CompanyId_ProviderCode_Name"" ON ""IntegrationConnections"" (""CompanyId"", ""ProviderCode"", ""Name"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""InventoryTransactions"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""InventoryItemId"" TEXT NOT NULL, ""OccurredOn"" TEXT NOT NULL, ""TransactionType"" TEXT NOT NULL, ""QuantityChange"" TEXT NOT NULL, ""UnitCost"" TEXT NOT NULL, ""TotalCost"" TEXT NOT NULL, ""Reference"" TEXT NOT NULL, ""JournalEntryId"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_InventoryTransactions_CompanyId_InventoryItemId_OccurredOn"" ON ""InventoryTransactions"" (""CompanyId"", ""InventoryItemId"", ""OccurredOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""CurrencyExchangeRates"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""BaseCurrency"" TEXT NOT NULL, ""QuoteCurrency"" TEXT NOT NULL, ""Rate"" TEXT NOT NULL, ""EffectiveOn"" TEXT NOT NULL, ""Source"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CurrencyExchangeRates_CompanyId_BaseCurrency_QuoteCurrency_EffectiveOn"" ON ""CurrencyExchangeRates"" (""CompanyId"", ""BaseCurrency"", ""QuoteCurrency"", ""EffectiveOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""ConsolidationGroups"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""Name"" TEXT NOT NULL, ""ReportingCurrency"" TEXT NOT NULL, ""IsActive"" INTEGER NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ConsolidationGroups_CompanyId_Name"" ON ""ConsolidationGroups"" (""CompanyId"", ""Name"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""ConsolidationGroupCompanies"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""ConsolidationGroupId"" TEXT NOT NULL, ""MemberCompanyId"" TEXT NOT NULL, ""OwnershipPercentage"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ConsolidationGroupCompanies_ConsolidationGroupId_MemberCompanyId"" ON ""ConsolidationGroupCompanies"" (""ConsolidationGroupId"", ""MemberCompanyId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BankReconciliations"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""BankAccountId"" TEXT NOT NULL, ""StatementDate"" TEXT NOT NULL, ""StatementClosingBalance"" TEXT NOT NULL, ""BookBalance"" TEXT NOT NULL, ""ReconciledByUserId"" TEXT NULL, ""ReconciledAtUtc"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BankReconciliations_BankAccountId_StatementDate"" ON ""BankReconciliations"" (""BankAccountId"", ""StatementDate"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BankReconciliationItems"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""BankReconciliationId"" TEXT NOT NULL, ""JournalEntryId"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BankReconciliationItems_BankReconciliationId_JournalEntryId"" ON ""BankReconciliationItems"" (""BankReconciliationId"", ""JournalEntryId"");", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "UserName", @"ALTER TABLE ""Users"" ADD COLUMN ""UserName"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "PasswordHash", @"ALTER TABLE ""Users"" ADD COLUMN ""PasswordHash"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "SecurityStamp", @"ALTER TABLE ""Users"" ADD COLUMN ""SecurityStamp"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "FailedSignInCount", @"ALTER TABLE ""Users"" ADD COLUMN ""FailedSignInCount"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "LastFailedSignInUtc", @"ALTER TABLE ""Users"" ADD COLUMN ""LastFailedSignInUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "LockoutEndUtc", @"ALTER TABLE ""Users"" ADD COLUMN ""LockoutEndUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "LastSuccessfulSignInUtc", @"ALTER TABLE ""Users"" ADD COLUMN ""LastSuccessfulSignInUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "LastPasswordChangedUtc", @"ALTER TABLE ""Users"" ADD COLUMN ""LastPasswordChangedUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankAccounts", "LedgerAccountId", @"ALTER TABLE ""BankAccounts"" ADD COLUMN ""LedgerAccountId"" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankAccounts", "LastReconciledBalance", @"ALTER TABLE ""BankAccounts"" ADD COLUMN ""LastReconciledBalance"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"UPDATE ""BankAccounts"" SET ""LastReconciledBalance"" = ""CurrentBalance"" WHERE ""LastReconciledBalance"" = 0 AND ""CurrentBalance"" <> 0 AND NOT EXISTS (SELECT 1 FROM ""BankReconciliations"" WHERE ""BankReconciliations"".""BankAccountId"" = ""BankAccounts"".""Id"");", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "PostedByUserId", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""PostedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "PostedAtUtc", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""PostedAtUtc"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "BankAccountId", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""BankAccountId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "SourceDocumentId", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""SourceDocumentId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "SourceDocumentType", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""SourceDocumentType"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "Status", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""Status"" TEXT NOT NULL DEFAULT 'Posted';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "CreatedByUserId", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""CreatedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "CreatedAtUtc", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""CreatedAtUtc"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "ApprovedByUserId", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""ApprovedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "ApprovedAtUtc", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""ApprovedAtUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "ReversalOfJournalEntryId", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""ReversalOfJournalEntryId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "ReversedByJournalEntryId", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""ReversedByJournalEntryId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "JournalEntries", "ConcurrencyToken", @"ALTER TABLE ""JournalEntries"" ADD COLUMN ""ConcurrencyToken"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_JournalEntries_CompanyId_Status_PostedOn"" ON ""JournalEntries"" (""CompanyId"", ""Status"", ""PostedOn"");", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "SalesInvoices", "ConcurrencyToken", @"ALTER TABLE ""SalesInvoices"" ADD COLUMN ""ConcurrencyToken"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "VendorBills", "ConcurrencyToken", @"ALTER TABLE ""VendorBills"" ADD COLUMN ""ConcurrencyToken"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""SalesInvoiceLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""SalesInvoiceId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""RevenueAccountId"" TEXT NOT NULL, ""Description"" TEXT NOT NULL, ""Quantity"" TEXT NOT NULL, ""UnitPrice"" TEXT NOT NULL, ""DiscountAmount"" TEXT NOT NULL, ""TaxAmount"" TEXT NOT NULL, ""LineTotal"" TEXT NOT NULL, FOREIGN KEY (""SalesInvoiceId"") REFERENCES ""SalesInvoices"" (""Id"") ON DELETE CASCADE, FOREIGN KEY (""RevenueAccountId"") REFERENCES ""Accounts"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SalesInvoiceLines_SalesInvoiceId_Sequence"" ON ""SalesInvoiceLines"" (""SalesInvoiceId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""VendorBillLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""VendorBillId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""ExpenseAccountId"" TEXT NOT NULL, ""Description"" TEXT NOT NULL, ""Quantity"" TEXT NOT NULL, ""UnitCost"" TEXT NOT NULL, ""DiscountAmount"" TEXT NOT NULL, ""TaxAmount"" TEXT NOT NULL, ""LineTotal"" TEXT NOT NULL, FOREIGN KEY (""VendorBillId"") REFERENCES ""VendorBills"" (""Id"") ON DELETE CASCADE, FOREIGN KEY (""ExpenseAccountId"") REFERENCES ""Accounts"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_VendorBillLines_VendorBillId_Sequence"" ON ""VendorBillLines"" (""VendorBillId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""SubledgerPayments"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""Direction"" TEXT NOT NULL, ""CounterpartyId"" TEXT NOT NULL, ""BankAccountId"" TEXT NOT NULL, ""PaymentDate"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""AppliedAmount"" TEXT NOT NULL, ""UnappliedAmount"" TEXT NOT NULL, ""Reference"" TEXT NOT NULL, ""Method"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""JournalEntryId"" TEXT NOT NULL, ""ReversalJournalEntryId"" TEXT NULL, ""CreatedByUserId"" TEXT NULL, ""CreatedAtUtc"" TEXT NOT NULL, ""ReversedByUserId"" TEXT NULL, ""ReversedAtUtc"" TEXT NULL, ""ReversalReason"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL, FOREIGN KEY (""BankAccountId"") REFERENCES ""BankAccounts"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""JournalEntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""ReversalJournalEntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SubledgerPayments_CompanyId_Direction_Reference"" ON ""SubledgerPayments"" (""CompanyId"", ""Direction"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_SubledgerPayments_CompanyId_CounterpartyId_PaymentDate"" ON ""SubledgerPayments"" (""CompanyId"", ""CounterpartyId"", ""PaymentDate"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""SubledgerPaymentApplications"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""SubledgerPaymentId"" TEXT NOT NULL, ""DocumentId"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, FOREIGN KEY (""SubledgerPaymentId"") REFERENCES ""SubledgerPayments"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SubledgerPaymentApplications_SubledgerPaymentId_DocumentId"" ON ""SubledgerPaymentApplications"" (""SubledgerPaymentId"", ""DocumentId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""SubledgerAdjustments"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""Subledger"" TEXT NOT NULL, ""Kind"" TEXT NOT NULL, ""CounterpartyId"" TEXT NOT NULL, ""DocumentId"" TEXT NULL, ""PaymentId"" TEXT NULL, ""BankAccountId"" TEXT NULL, ""AdjustmentDate"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""Reference"" TEXT NOT NULL, ""Reason"" TEXT NOT NULL, ""OffsetAccountNumber"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""JournalEntryId"" TEXT NOT NULL, ""ReversalJournalEntryId"" TEXT NULL, ""CreatedByUserId"" TEXT NULL, ""CreatedAtUtc"" TEXT NOT NULL, ""ReversedByUserId"" TEXT NULL, ""ReversedAtUtc"" TEXT NULL, ""ReversalReason"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL, FOREIGN KEY (""PaymentId"") REFERENCES ""SubledgerPayments"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""BankAccountId"") REFERENCES ""BankAccounts"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""JournalEntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""ReversalJournalEntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SubledgerAdjustments_CompanyId_Subledger_Reference"" ON ""SubledgerAdjustments"" (""CompanyId"", ""Subledger"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_SubledgerAdjustments_CompanyId_DocumentId"" ON ""SubledgerAdjustments"" (""CompanyId"", ""DocumentId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""SubledgerDocumentWorkflows"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""DocumentType"" TEXT NOT NULL, ""DocumentNumber"" TEXT NOT NULL, ""PayloadJson"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""IsRecurringTemplate"" INTEGER NOT NULL, ""Frequency"" TEXT NOT NULL, ""FrequencyInterval"" INTEGER NOT NULL, ""NextOccurrenceDate"" TEXT NULL, ""EndDate"" TEXT NULL, ""SourceTemplateId"" TEXT NULL, ""PostedDocumentId"" TEXT NULL, ""CreatedByUserId"" TEXT NULL, ""CreatedAtUtc"" TEXT NOT NULL, ""ApprovedByUserId"" TEXT NULL, ""ApprovedAtUtc"" TEXT NULL, ""PostedByUserId"" TEXT NULL, ""PostedAtUtc"" TEXT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentNumber_IsRecurringTemplate"" ON ""SubledgerDocumentWorkflows"" (""CompanyId"", ""DocumentType"", ""DocumentNumber"", ""IsRecurringTemplate"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_SubledgerDocumentWorkflows_CompanyId_Status_NextOccurrenceDate"" ON ""SubledgerDocumentWorkflows"" (""CompanyId"", ""Status"", ""NextOccurrenceDate"");", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankAccounts", "ConcurrencyToken", @"ALTER TABLE ""BankAccounts"" ADD COLUMN ""ConcurrencyToken"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "OpeningBalance", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""OpeningBalance"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "ClearedAmount", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""ClearedAmount"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "Variance", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""Variance"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "Status", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""Status"" TEXT NOT NULL DEFAULT 'Completed';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "Notes", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""Notes"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "ReopenedByUserId", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""ReopenedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "ReopenedAtUtc", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""ReopenedAtUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "ReopenReason", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""ReopenReason"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankReconciliations", "ConcurrencyToken", @"ALTER TABLE ""BankReconciliations"" ADD COLUMN ""ConcurrencyToken"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BankStatementImportBatches"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""BankAccountId"" TEXT NOT NULL, ""FileName"" TEXT NOT NULL, ""Format"" TEXT NOT NULL, ""ContentSha256"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""ImportedCount"" INTEGER NOT NULL, ""DuplicateCount"" INTEGER NOT NULL, ""RejectedCount"" INTEGER NOT NULL, ""DebitTotal"" TEXT NOT NULL, ""CreditTotal"" TEXT NOT NULL, ""RejectionJson"" TEXT NOT NULL, ""ImportedByUserId"" TEXT NULL, ""ImportedAtUtc"" TEXT NOT NULL, FOREIGN KEY (""BankAccountId"") REFERENCES ""BankAccounts"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BankStatementImportBatches_CompanyId_BankAccountId_ContentSha256"" ON ""BankStatementImportBatches"" (""CompanyId"", ""BankAccountId"", ""ContentSha256"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BankStatementTransactions"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""BankAccountId"" TEXT NOT NULL, ""ImportBatchId"" TEXT NOT NULL, ""ExternalId"" TEXT NOT NULL, ""TransactionDate"" TEXT NOT NULL, ""PostedDate"" TEXT NULL, ""Amount"" TEXT NOT NULL, ""TransactionType"" TEXT NOT NULL, ""Payee"" TEXT NOT NULL, ""Memo"" TEXT NOT NULL, ""Reference"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""MatchedJournalEntryId"" TEXT NULL, ""MatchedAtUtc"" TEXT NULL, ""MatchedByUserId"" TEXT NULL, ""MatchNote"" TEXT NOT NULL, ""RawJson"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL, FOREIGN KEY (""BankAccountId"") REFERENCES ""BankAccounts"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""ImportBatchId"") REFERENCES ""BankStatementImportBatches"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""MatchedJournalEntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BankStatementTransactions_CompanyId_BankAccountId_ExternalId"" ON ""BankStatementTransactions"" (""CompanyId"", ""BankAccountId"", ""ExternalId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_BankStatementTransactions_CompanyId_BankAccountId_Status_TransactionDate"" ON ""BankStatementTransactions"" (""CompanyId"", ""BankAccountId"", ""Status"", ""TransactionDate"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BankTransfers"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""FromBankAccountId"" TEXT NOT NULL, ""ToBankAccountId"" TEXT NOT NULL, ""TransferDate"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""Reference"" TEXT NOT NULL, ""Memo"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""JournalEntryId"" TEXT NOT NULL, ""InboundJournalEntryId"" TEXT NOT NULL, ""ReversalJournalEntryId"" TEXT NULL, ""CreatedByUserId"" TEXT NULL, ""CreatedAtUtc"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL, FOREIGN KEY (""FromBankAccountId"") REFERENCES ""BankAccounts"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""ToBankAccountId"") REFERENCES ""BankAccounts"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""JournalEntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""InboundJournalEntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""ReversalJournalEntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankTransfers", "InboundReversalJournalEntryId", @"ALTER TABLE ""BankTransfers"" ADD COLUMN ""InboundReversalJournalEntryId"" TEXT NULL REFERENCES ""JournalEntries"" (""Id"") ON DELETE RESTRICT;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankTransfers", "ReversedByUserId", @"ALTER TABLE ""BankTransfers"" ADD COLUMN ""ReversedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankTransfers", "ReversedAtUtc", @"ALTER TABLE ""BankTransfers"" ADD COLUMN ""ReversedAtUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankTransfers", "ReversalDate", @"ALTER TABLE ""BankTransfers"" ADD COLUMN ""ReversalDate"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "BankTransfers", "ReversalReason", @"ALTER TABLE ""BankTransfers"" ADD COLUMN ""ReversalReason"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BankTransfers_CompanyId_Reference"" ON ""BankTransfers"" (""CompanyId"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""AuthenticationAuditEntries"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AuthenticationAuditEntries"" PRIMARY KEY,
                    ""UserId"" TEXT NULL,
                    ""CompanyId"" TEXT NULL,
                    ""UserName"" TEXT NOT NULL,
                    ""EventType"" TEXT NOT NULL,
                    ""Succeeded"" INTEGER NOT NULL,
                    ""OccurredUtc"" TEXT NOT NULL,
                    ""IpAddress"" TEXT NOT NULL,
                    ""UserAgent"" TEXT NOT NULL,
                    ""Detail"" TEXT NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleSets"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TaxRuleSets"" PRIMARY KEY,
                    ""CompanyId"" TEXT NOT NULL,
                    ""Code"" TEXT NOT NULL,
                    ""JurisdictionCode"" TEXT NOT NULL,
                    ""JurisdictionName"" TEXT NOT NULL,
                    ""JurisdictionType"" TEXT NOT NULL,
                    ""TaxType"" TEXT NOT NULL,
                    ""CalculationMethod"" TEXT NOT NULL,
                    ""WithholdingFrequency"" TEXT NOT NULL,
                    ""EffectiveOn"" TEXT NOT NULL,
                    ""Source"" TEXT NOT NULL,
                    ""Notes"" TEXT NOT NULL,
                    ""IsEmployerSpecific"" INTEGER NOT NULL,
                    ""SupportsBracketTable"" INTEGER NOT NULL,
                    ""SupportsParameterEditing"" INTEGER NOT NULL,
                    ""IsActive"" INTEGER NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleSets_CompanyId_Code"" ON ""TaxRuleSets"" (""CompanyId"", ""Code"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleParameters"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TaxRuleParameters"" PRIMARY KEY,
                    ""TaxRuleSetId"" TEXT NOT NULL,
                    ""ParameterCode"" TEXT NOT NULL,
                    ""Label"" TEXT NOT NULL,
                    ""ValueType"" TEXT NOT NULL,
                    ""NumericValue"" TEXT NULL,
                    ""TextValue"" TEXT NOT NULL,
                    ""BooleanValue"" INTEGER NULL,
                    ""Notes"" TEXT NOT NULL,
                    ""DisplayOrder"" INTEGER NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleParameters_TaxRuleSetId_ParameterCode"" ON ""TaxRuleParameters"" (""TaxRuleSetId"", ""ParameterCode"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleBrackets"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TaxRuleBrackets"" PRIMARY KEY,
                    ""TaxRuleSetId"" TEXT NOT NULL,
                    ""Sequence"" INTEGER NOT NULL,
                    ""UpperBoundAmount"" TEXT NOT NULL,
                    ""FixedAmount"" TEXT NOT NULL,
                    ""Rate"" TEXT NOT NULL,
                    ""Notes"" TEXT NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleBrackets_TaxRuleSetId_Sequence"" ON ""TaxRuleBrackets"" (""TaxRuleSetId"", ""Sequence"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxFormRequirements"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TaxFormRequirements"" PRIMARY KEY,
                    ""TaxRuleSetId"" TEXT NOT NULL,
                    ""FormCode"" TEXT NOT NULL,
                    ""Name"" TEXT NOT NULL,
                    ""FilingFrequency"" TEXT NOT NULL,
                    ""DeliveryChannel"" TEXT NOT NULL,
                    ""DueRule"" TEXT NOT NULL,
                    ""Notes"" TEXT NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxFormRequirements_TaxRuleSetId_FormCode"" ON ""TaxFormRequirements"" (""TaxRuleSetId"", ""FormCode"");",
                cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "TaxContentPackageId", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""TaxContentPackageId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "ContentVersion", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""ContentVersion"" TEXT NOT NULL DEFAULT '1.0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "MinimumEngineVersion", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""MinimumEngineVersion"" TEXT NOT NULL DEFAULT '1.0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "ParentJurisdictionCode", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""ParentJurisdictionCode"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "ObligationCode", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""ObligationCode"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "CalculationVariant", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""CalculationVariant"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "ExclusiveGroup", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""ExclusiveGroup"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "VariantPriority", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""VariantPriority"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxRuleSets", "ApplicabilityJson", @"ALTER TABLE ""TaxRuleSets"" ADD COLUMN ""ApplicabilityJson"" TEXT NOT NULL DEFAULT '{}';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""TaxContentPackages"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""PackageCode"" TEXT NOT NULL, ""Version"" TEXT NOT NULL, ""EffectiveOn"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""MinimumEngineVersion"" TEXT NOT NULL, ""ManifestJson"" TEXT NOT NULL, ""Source"" TEXT NOT NULL, ""ChangeSummary"" TEXT NOT NULL, ""CreatedAtUtc"" TEXT NOT NULL, ""ApprovedAtUtc"" TEXT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxContentPackages_CompanyId_PackageCode_Version"" ON ""TaxContentPackages"" (""CompanyId"", ""PackageCode"", ""Version"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""TaxSourceCaptures"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""TaxContentPackageId"" TEXT NULL, ""SourceKind"" TEXT NOT NULL, ""JurisdictionCode"" TEXT NOT NULL, ""SourceUrl"" TEXT NOT NULL, ""ContentType"" TEXT NOT NULL, ""ContentSha256"" TEXT NOT NULL, ""RawContent"" TEXT NOT NULL, ""CapturedAtUtc"" TEXT NOT NULL, ""Notes"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_TaxSourceCaptures_CompanyId_CapturedAtUtc"" ON ""TaxSourceCaptures"" (""CompanyId"", ""CapturedAtUtc"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""TaxRuleFieldDefinitions"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""TaxRuleSetId"" TEXT NOT NULL, ""FieldCode"" TEXT NOT NULL, ""Label"" TEXT NOT NULL, ""DataType"" TEXT NOT NULL, ""IsRequired"" INTEGER NOT NULL, ""DefaultValueJson"" TEXT NOT NULL, ""ValidationJson"" TEXT NOT NULL, ""DisplayOrder"" INTEGER NOT NULL, ""HelpText"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleFieldDefinitions_TaxRuleSetId_FieldCode"" ON ""TaxRuleFieldDefinitions"" (""TaxRuleSetId"", ""FieldCode"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""TaxRuleTestCases"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""TaxRuleSetId"" TEXT NOT NULL, ""Name"" TEXT NOT NULL, ""InputJson"" TEXT NOT NULL, ""ExpectedOutputJson"" TEXT NOT NULL, ""IsRequiredForActivation"" INTEGER NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleTestCases_TaxRuleSetId_Name"" ON ""TaxRuleTestCases"" (""TaxRuleSetId"", ""Name"");", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "FilingStatus", @"ALTER TABLE ""Employees"" ADD COLUMN ""FilingStatus"" TEXT NOT NULL DEFAULT 'Single';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "PayrollFrequency", @"ALTER TABLE ""Employees"" ADD COLUMN ""PayrollFrequency"" TEXT NOT NULL DEFAULT 'Biweekly';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "Allowances", @"ALTER TABLE ""Employees"" ADD COLUMN ""Allowances"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "AdditionalWithholding", @"ALTER TABLE ""Employees"" ADD COLUMN ""AdditionalWithholding"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "PreTaxBenefitDeductions", @"ALTER TABLE ""Employees"" ADD COLUMN ""PreTaxBenefitDeductions"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "PostTaxBenefitDeductions", @"ALTER TABLE ""Employees"" ADD COLUMN ""PostTaxBenefitDeductions"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "ResidenceState", @"ALTER TABLE ""Employees"" ADD COLUMN ""ResidenceState"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "ResidenceCity", @"ALTER TABLE ""Employees"" ADD COLUMN ""ResidenceCity"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "WorkCity", @"ALTER TABLE ""Employees"" ADD COLUMN ""WorkCity"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "ResidenceCounty", @"ALTER TABLE ""Employees"" ADD COLUMN ""ResidenceCounty"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "ResidenceSchoolDistrict", @"ALTER TABLE ""Employees"" ADD COLUMN ""ResidenceSchoolDistrict"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "WorkCounty", @"ALTER TABLE ""Employees"" ADD COLUMN ""WorkCounty"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "WorkSchoolDistrict", @"ALTER TABLE ""Employees"" ADD COLUMN ""WorkSchoolDistrict"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "AddressLine1", @"ALTER TABLE ""Employees"" ADD COLUMN ""AddressLine1"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "AddressLine2", @"ALTER TABLE ""Employees"" ADD COLUMN ""AddressLine2"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "PostalCode", @"ALTER TABLE ""Employees"" ADD COLUMN ""PostalCode"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "SocialSecurityNumber", @"ALTER TABLE ""Employees"" ADD COLUMN ""SocialSecurityNumber"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "BankRoutingNumber", @"ALTER TABLE ""Employees"" ADD COLUMN ""BankRoutingNumber"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "BankAccountNumber", @"ALTER TABLE ""Employees"" ADD COLUMN ""BankAccountNumber"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "BankAccountType", @"ALTER TABLE ""Employees"" ADD COLUMN ""BankAccountType"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "DirectDepositEnabled", @"ALTER TABLE ""Employees"" ADD COLUMN ""DirectDepositEnabled"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "EmploymentStartedOn", @"ALTER TABLE ""Employees"" ADD COLUMN ""EmploymentStartedOn"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "EmploymentEndedOn", @"ALTER TABLE ""Employees"" ADD COLUMN ""EmploymentEndedOn"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "HourlyRate", @"ALTER TABLE ""Employees"" ADD COLUMN ""HourlyRate"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "OvertimeRate", @"ALTER TABLE ""Employees"" ADD COLUMN ""OvertimeRate"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "ConcurrencyToken", @"ALTER TABLE ""Employees"" ADD COLUMN ""ConcurrencyToken"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "FederalFormW4Year", @"ALTER TABLE ""Employees"" ADD COLUMN ""FederalFormW4Year"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "FederalStep2MultipleJobs", @"ALTER TABLE ""Employees"" ADD COLUMN ""FederalStep2MultipleJobs"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "FederalStep3Credits", @"ALTER TABLE ""Employees"" ADD COLUMN ""FederalStep3Credits"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "FederalStep4OtherIncome", @"ALTER TABLE ""Employees"" ADD COLUMN ""FederalStep4OtherIncome"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "FederalStep4Deductions", @"ALTER TABLE ""Employees"" ADD COLUMN ""FederalStep4Deductions"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "FederalWithholdingExempt", @"ALTER TABLE ""Employees"" ADD COLUMN ""FederalWithholdingExempt"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTimecards"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""EmployeeId"" TEXT NOT NULL, ""PeriodStart"" TEXT NOT NULL, ""PeriodEnd"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""Notes"" TEXT NOT NULL, ""PreparedByUserId"" TEXT NULL, ""PreparedAtUtc"" TEXT NOT NULL, ""SubmittedByUserId"" TEXT NULL, ""SubmittedAtUtc"" TEXT NULL, ""ApprovedByUserId"" TEXT NULL, ""ApprovedAtUtc"" TEXT NULL, ""VoidedByUserId"" TEXT NULL, ""VoidedAtUtc"" TEXT NULL, ""VoidReason"" TEXT NOT NULL, ""PayrollRunId"" TEXT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_PayrollTimecards_CompanyId_EmployeeId_PeriodStart_PeriodEnd_Status"";", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollTimecards_CompanyId_EmployeeId_PeriodStart_PeriodEnd_Status"" ON ""PayrollTimecards"" (""CompanyId"", ""EmployeeId"", ""PeriodStart"", ""PeriodEnd"", ""Status"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTimeEntries"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollTimecardId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""WorkDate"" TEXT NOT NULL, ""EarningCode"" TEXT NOT NULL, ""EarningType"" TEXT NOT NULL, ""Hours"" TEXT NOT NULL, ""Rate"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""IsTaxable"" INTEGER NOT NULL, ""WorkState"" TEXT NOT NULL, ""WorkCounty"" TEXT NOT NULL, ""WorkCity"" TEXT NOT NULL, ""WorkSchoolDistrict"" TEXT NOT NULL, ""ProjectJobId"" TEXT NULL, ""Notes"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollTimeEntries_PayrollTimecardId_Sequence"" ON ""PayrollTimeEntries"" (""PayrollTimecardId"", ""Sequence"");", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxProfiles", "AnnualWageBase", @"ALTER TABLE ""TaxProfiles"" ADD COLUMN ""AnnualWageBase"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxProfiles", "IsActive", @"ALTER TABLE ""TaxProfiles"" ADD COLUMN ""IsActive"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxProfiles", "IsVerified", @"ALTER TABLE ""TaxProfiles"" ADD COLUMN ""IsVerified"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "TaxProfiles", "VerificationNotes", @"ALTER TABLE ""TaxProfiles"" ADD COLUMN ""VerificationNotes"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollRuns"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""BankAccountId"" TEXT NOT NULL, ""PayDate"" TEXT NOT NULL, ""Reference"" TEXT NOT NULL, ""GrossPayroll"" TEXT NOT NULL, ""PreTaxDeductions"" TEXT NOT NULL, ""EmployeeWithholdings"" TEXT NOT NULL, ""PostTaxDeductions"" TEXT NOT NULL, ""EmployerPayrollTaxes"" TEXT NOT NULL, ""NetPay"" TEXT NOT NULL, ""PostedAtUtc"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "TaxContentSnapshotJson", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""TaxContentSnapshotJson"" TEXT NOT NULL DEFAULT '[]';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "PeriodStart", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""PeriodStart"" TEXT NOT NULL DEFAULT '0001-01-01';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "PeriodEnd", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""PeriodEnd"" TEXT NOT NULL DEFAULT '0001-01-01';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "RunType", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""RunType"" TEXT NOT NULL DEFAULT 'Regular';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "Status", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""Status"" TEXT NOT NULL DEFAULT 'Posted';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "JournalEntryId", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""JournalEntryId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ReversalJournalEntryId", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ReversalJournalEntryId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "PreparedByUserId", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""PreparedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "PreparedAtUtc", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""PreparedAtUtc"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ApprovedByUserId", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ApprovedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ApprovedAtUtc", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ApprovedAtUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "PostedByUserId", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""PostedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "CancelledByUserId", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""CancelledByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "CancelledAtUtc", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""CancelledAtUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "CancellationReason", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""CancellationReason"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ReversedByUserId", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ReversedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ReversedAtUtc", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ReversedAtUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ReversalDate", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ReversalDate"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ReversalReason", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ReversalReason"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "CalculationWarningsJson", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""CalculationWarningsJson"" TEXT NOT NULL DEFAULT '[]';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ConcurrencyToken", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ConcurrencyToken"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollRuns_CompanyId_Reference"" ON ""PayrollRuns"" (""CompanyId"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollRunEmployeeLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollRunId"" TEXT NOT NULL, ""EmployeeId"" TEXT NOT NULL, ""WorkState"" TEXT NOT NULL, ""FilingStatus"" TEXT NOT NULL, ""GrossPay"" TEXT NOT NULL, ""PreTaxDeductions"" TEXT NOT NULL, ""EmployeeWithholdings"" TEXT NOT NULL, ""PostTaxDeductions"" TEXT NOT NULL, ""EmployerPayrollTaxes"" TEXT NOT NULL, ""NetPay"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "PayrollFrequency", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""PayrollFrequency"" TEXT NOT NULL DEFAULT 'Biweekly';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollRunEmployeeLines_PayrollRunId_EmployeeId"" ON ""PayrollRunEmployeeLines"" (""PayrollRunId"", ""EmployeeId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollJurisdictionRules"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""ResidenceJurisdiction"" TEXT NOT NULL, ""WorkJurisdiction"" TEXT NOT NULL, ""ExemptWorkWithholding"" INTEGER NOT NULL, ""ResidentCreditRate"" TEXT NOT NULL, ""IsActive"" INTEGER NOT NULL, ""Notes"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollJurisdictionRules_CompanyId_ResidenceJurisdiction_WorkJurisdiction"" ON ""PayrollJurisdictionRules"" (""CompanyId"", ""ResidenceJurisdiction"", ""WorkJurisdiction"");", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "WorkCity", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""WorkCity"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "ResidenceState", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""ResidenceState"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "ResidenceCity", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""ResidenceCity"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "TaxableWages", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""TaxableWages"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "YearToDateGrossBefore", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""YearToDateGrossBefore"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "YearToDateGrossAfter", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""YearToDateGrossAfter"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "CalculationTraceJson", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""CalculationTraceJson"" TEXT NOT NULL DEFAULT '[]';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollEarningLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" TEXT NOT NULL, ""PayrollTimeEntryId"" TEXT NULL, ""Sequence"" INTEGER NOT NULL, ""EarningCode"" TEXT NOT NULL, ""EarningType"" TEXT NOT NULL, ""Hours"" TEXT NOT NULL, ""Rate"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""IsTaxable"" INTEGER NOT NULL, ""WorkedOn"" TEXT NULL, ""WorkState"" TEXT NOT NULL, ""WorkCounty"" TEXT NOT NULL, ""WorkCity"" TEXT NOT NULL, ""WorkSchoolDistrict"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollEarningLines", "PayrollTimeEntryId", @"ALTER TABLE ""PayrollEarningLines"" ADD COLUMN ""PayrollTimeEntryId"" TEXT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollEarningLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollEarningLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_PayrollEarningLines_PayrollTimeEntryId"";", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollEarningLines_PayrollTimeEntryId"" ON ""PayrollEarningLines"" (""PayrollTimeEntryId"") WHERE ""PayrollTimeEntryId"" IS NOT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDeductionLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""DeductionCode"" TEXT NOT NULL, ""DeductionType"" TEXT NOT NULL, ""EmployeeAmount"" TEXT NOT NULL, ""EmployerAmount"" TEXT NOT NULL, ""IsPreTax"" INTEGER NOT NULL, ""LiabilityAccountNumber"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "ExemptFromFederalIncomeTax", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""ExemptFromFederalIncomeTax"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "ExemptFromFica", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""ExemptFromFica"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "ExemptFromFuta", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""ExemptFromFuta"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDeductionLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollDeductionLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTaxLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""ObligationCode"" TEXT NOT NULL, ""JurisdictionCode"" TEXT NOT NULL, ""JurisdictionName"" TEXT NOT NULL, ""TaxType"" TEXT NOT NULL, ""TaxableWages"" TEXT NOT NULL, ""YearToDateTaxableWagesBefore"" TEXT NOT NULL, ""EmployeeAmount"" TEXT NOT NULL, ""EmployerAmount"" TEXT NOT NULL, ""TaxRuleSetId"" TEXT NULL, ""TaxContentPackageId"" TEXT NULL, ""ContentVersion"" TEXT NOT NULL, ""Source"" TEXT NOT NULL, ""CalculationTraceJson"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollTaxLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollTaxLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""AccessRoles"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""CompanyId"" uuid NOT NULL,
                    ""Name"" text NOT NULL,
                    ""Description"" text NOT NULL,
                    ""TemplateCode"" text NOT NULL,
                    ""Permissions"" text NOT NULL,
                    ""IsSystemRole"" boolean NOT NULL,
                    ""IsActive"" boolean NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccessRoles_CompanyId_Name"" ON ""AccessRoles"" (""CompanyId"", ""Name"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""CompanyMemberships"" (""Id"" uuid NOT NULL PRIMARY KEY, ""UserId"" uuid NOT NULL, ""CompanyId"" uuid NOT NULL, ""Role"" text NOT NULL, ""IsOwner"" boolean NOT NULL, ""IsActive"" boolean NOT NULL, ""GrantedAtUtc"" timestamptz NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CompanyMemberships_UserId_CompanyId"" ON ""CompanyMemberships"" (""UserId"", ""CompanyId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""AccountingPeriods"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""StartsOn"" date NOT NULL, ""EndsOn"" date NOT NULL, ""Status"" text NOT NULL, ""ClosedByUserId"" uuid NULL, ""ClosedAtUtc"" timestamptz NULL, ""Notes"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccountingPeriods_CompanyId_StartsOn_EndsOn"" ON ""AccountingPeriods"" (""CompanyId"", ""StartsOn"", ""EndsOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BusinessAuditEntries"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""UserId"" uuid NULL, ""Action"" text NOT NULL, ""EntityType"" text NOT NULL, ""EntityId"" uuid NULL, ""DetailJson"" text NOT NULL, ""OccurredAtUtc"" timestamptz NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_BusinessAuditEntries_CompanyId_OccurredAtUtc"" ON ""BusinessAuditEntries"" (""CompanyId"", ""OccurredAtUtc"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""IntegrationConnections"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""ProviderCode"" text NOT NULL, ""Name"" text NOT NULL, ""Status"" text NOT NULL, ""SettingsJson"" text NOT NULL, ""CredentialsJson"" text NOT NULL, ""LastValidatedAtUtc"" timestamptz NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_IntegrationConnections_CompanyId_ProviderCode_Name"" ON ""IntegrationConnections"" (""CompanyId"", ""ProviderCode"", ""Name"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""InventoryTransactions"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""InventoryItemId"" uuid NOT NULL, ""OccurredOn"" date NOT NULL, ""TransactionType"" text NOT NULL, ""QuantityChange"" numeric(18,2) NOT NULL, ""UnitCost"" numeric(18,2) NOT NULL, ""TotalCost"" numeric(18,2) NOT NULL, ""Reference"" text NOT NULL, ""JournalEntryId"" uuid NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_InventoryTransactions_CompanyId_InventoryItemId_OccurredOn"" ON ""InventoryTransactions"" (""CompanyId"", ""InventoryItemId"", ""OccurredOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""CurrencyExchangeRates"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""BaseCurrency"" text NOT NULL, ""QuoteCurrency"" text NOT NULL, ""Rate"" numeric(18,8) NOT NULL, ""EffectiveOn"" date NOT NULL, ""Source"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CurrencyExchangeRates_CompanyId_BaseCurrency_QuoteCurrency_EffectiveOn"" ON ""CurrencyExchangeRates"" (""CompanyId"", ""BaseCurrency"", ""QuoteCurrency"", ""EffectiveOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""ConsolidationGroups"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""Name"" text NOT NULL, ""ReportingCurrency"" text NOT NULL, ""IsActive"" boolean NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ConsolidationGroups_CompanyId_Name"" ON ""ConsolidationGroups"" (""CompanyId"", ""Name"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""ConsolidationGroupCompanies"" (""Id"" uuid NOT NULL PRIMARY KEY, ""ConsolidationGroupId"" uuid NOT NULL, ""MemberCompanyId"" uuid NOT NULL, ""OwnershipPercentage"" numeric(9,6) NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ConsolidationGroupCompanies_ConsolidationGroupId_MemberCompanyId"" ON ""ConsolidationGroupCompanies"" (""ConsolidationGroupId"", ""MemberCompanyId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BankReconciliations"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""BankAccountId"" uuid NOT NULL, ""StatementDate"" date NOT NULL, ""StatementClosingBalance"" numeric(18,2) NOT NULL, ""BookBalance"" numeric(18,2) NOT NULL, ""ReconciledByUserId"" uuid NULL, ""ReconciledAtUtc"" timestamptz NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BankReconciliations_BankAccountId_StatementDate"" ON ""BankReconciliations"" (""BankAccountId"", ""StatementDate"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""BankReconciliationItems"" (""Id"" uuid NOT NULL PRIMARY KEY, ""BankReconciliationId"" uuid NOT NULL, ""JournalEntryId"" uuid NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BankReconciliationItems_BankReconciliationId_JournalEntryId"" ON ""BankReconciliationItems"" (""BankReconciliationId"", ""JournalEntryId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "UserName" text NOT NULL DEFAULT '';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "BankAccounts" ADD COLUMN IF NOT EXISTS "LastReconciledBalance" numeric(18,2) NOT NULL DEFAULT 0;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "SourceDocumentId" uuid NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "SourceDocumentType" text NOT NULL DEFAULT '';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'Posted';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamptz NOT NULL DEFAULT '0001-01-01T00:00:00+00:00';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "ApprovedByUserId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "ApprovedAtUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "ReversalOfJournalEntryId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "ReversedByJournalEntryId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_JournalEntries_CompanyId_Status_PostedOn" ON "JournalEntries" ("CompanyId", "Status", "PostedOn");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """UPDATE "BankAccounts" SET "LastReconciledBalance" = "CurrentBalance" WHERE "LastReconciledBalance" = 0 AND "CurrentBalance" <> 0 AND NOT EXISTS (SELECT 1 FROM "BankReconciliations" WHERE "BankReconciliations"."BankAccountId" = "BankAccounts"."Id");""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordHash" text NOT NULL DEFAULT '';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SecurityStamp" text NOT NULL DEFAULT '';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "FailedSignInCount" integer NOT NULL DEFAULT 0;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastFailedSignInUtc" timestamptz NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LockoutEndUtc" timestamptz NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastSuccessfulSignInUtc" timestamptz NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastPasswordChangedUtc" timestamptz NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "BankAccounts" ADD COLUMN IF NOT EXISTS "LedgerAccountId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "PostedByUserId" uuid NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "PostedAtUtc" timestamptz NOT NULL DEFAULT '0001-01-01T00:00:00+00:00';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "JournalEntries" ADD COLUMN IF NOT EXISTS "BankAccountId" uuid NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "SalesInvoices" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "VendorBills" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "SalesInvoiceLines" ("Id" uuid NOT NULL PRIMARY KEY, "SalesInvoiceId" uuid NOT NULL REFERENCES "SalesInvoices" ("Id") ON DELETE CASCADE, "Sequence" integer NOT NULL, "RevenueAccountId" uuid NOT NULL REFERENCES "Accounts" ("Id") ON DELETE RESTRICT, "Description" text NOT NULL, "Quantity" numeric(18,4) NOT NULL, "UnitPrice" numeric(18,2) NOT NULL, "DiscountAmount" numeric(18,2) NOT NULL, "TaxAmount" numeric(18,2) NOT NULL, "LineTotal" numeric(18,2) NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_SalesInvoiceLines_SalesInvoiceId_Sequence" ON "SalesInvoiceLines" ("SalesInvoiceId", "Sequence");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "VendorBillLines" ("Id" uuid NOT NULL PRIMARY KEY, "VendorBillId" uuid NOT NULL REFERENCES "VendorBills" ("Id") ON DELETE CASCADE, "Sequence" integer NOT NULL, "ExpenseAccountId" uuid NOT NULL REFERENCES "Accounts" ("Id") ON DELETE RESTRICT, "Description" text NOT NULL, "Quantity" numeric(18,4) NOT NULL, "UnitCost" numeric(18,2) NOT NULL, "DiscountAmount" numeric(18,2) NOT NULL, "TaxAmount" numeric(18,2) NOT NULL, "LineTotal" numeric(18,2) NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_VendorBillLines_VendorBillId_Sequence" ON "VendorBillLines" ("VendorBillId", "Sequence");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "SubledgerPayments" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "Direction" text NOT NULL, "CounterpartyId" uuid NOT NULL, "BankAccountId" uuid NOT NULL REFERENCES "BankAccounts" ("Id") ON DELETE RESTRICT, "PaymentDate" date NOT NULL, "Amount" numeric(18,2) NOT NULL, "AppliedAmount" numeric(18,2) NOT NULL, "UnappliedAmount" numeric(18,2) NOT NULL, "Reference" text NOT NULL, "Method" text NOT NULL, "Status" text NOT NULL, "JournalEntryId" uuid NOT NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT, "ReversalJournalEntryId" uuid NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT, "CreatedByUserId" uuid NULL, "CreatedAtUtc" timestamptz NOT NULL, "ReversedByUserId" uuid NULL, "ReversedAtUtc" timestamptz NULL, "ReversalReason" text NOT NULL, "ConcurrencyToken" text NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubledgerPayments_CompanyId_Direction_Reference" ON "SubledgerPayments" ("CompanyId", "Direction", "Reference");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_SubledgerPayments_CompanyId_CounterpartyId_PaymentDate" ON "SubledgerPayments" ("CompanyId", "CounterpartyId", "PaymentDate");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "SubledgerPaymentApplications" ("Id" uuid NOT NULL PRIMARY KEY, "SubledgerPaymentId" uuid NOT NULL REFERENCES "SubledgerPayments" ("Id") ON DELETE RESTRICT, "DocumentId" uuid NOT NULL, "Amount" numeric(18,2) NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubledgerPaymentApplications_SubledgerPaymentId_DocumentId" ON "SubledgerPaymentApplications" ("SubledgerPaymentId", "DocumentId");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "SubledgerAdjustments" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "Subledger" text NOT NULL, "Kind" text NOT NULL, "CounterpartyId" uuid NOT NULL, "DocumentId" uuid NULL, "PaymentId" uuid NULL REFERENCES "SubledgerPayments" ("Id") ON DELETE RESTRICT, "BankAccountId" uuid NULL REFERENCES "BankAccounts" ("Id") ON DELETE RESTRICT, "AdjustmentDate" date NOT NULL, "Amount" numeric(18,2) NOT NULL, "Reference" text NOT NULL, "Reason" text NOT NULL, "OffsetAccountNumber" text NOT NULL, "Status" text NOT NULL, "JournalEntryId" uuid NOT NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT, "ReversalJournalEntryId" uuid NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT, "CreatedByUserId" uuid NULL, "CreatedAtUtc" timestamptz NOT NULL, "ReversedByUserId" uuid NULL, "ReversedAtUtc" timestamptz NULL, "ReversalReason" text NOT NULL, "ConcurrencyToken" text NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubledgerAdjustments_CompanyId_Subledger_Reference" ON "SubledgerAdjustments" ("CompanyId", "Subledger", "Reference");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_SubledgerAdjustments_CompanyId_DocumentId" ON "SubledgerAdjustments" ("CompanyId", "DocumentId");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "SubledgerDocumentWorkflows" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "DocumentType" text NOT NULL, "DocumentNumber" text NOT NULL, "PayloadJson" text NOT NULL, "Status" text NOT NULL, "IsRecurringTemplate" boolean NOT NULL, "Frequency" text NOT NULL, "FrequencyInterval" integer NOT NULL, "NextOccurrenceDate" date NULL, "EndDate" date NULL, "SourceTemplateId" uuid NULL, "PostedDocumentId" uuid NULL, "CreatedByUserId" uuid NULL, "CreatedAtUtc" timestamptz NOT NULL, "ApprovedByUserId" uuid NULL, "ApprovedAtUtc" timestamptz NULL, "PostedByUserId" uuid NULL, "PostedAtUtc" timestamptz NULL, "ConcurrencyToken" text NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentNumber_IsRecurringTemplate" ON "SubledgerDocumentWorkflows" ("CompanyId", "DocumentType", "DocumentNumber", "IsRecurringTemplate");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_SubledgerDocumentWorkflows_CompanyId_Status_NextOccurrenceDate" ON "SubledgerDocumentWorkflows" ("CompanyId", "Status", "NextOccurrenceDate");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "BankAccounts" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "OpeningBalance" numeric(18,2) NOT NULL DEFAULT 0; ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "ClearedAmount" numeric(18,2) NOT NULL DEFAULT 0; ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "Variance" numeric(18,2) NOT NULL DEFAULT 0; ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'Completed'; ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "Notes" text NOT NULL DEFAULT ''; ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "ReopenedByUserId" uuid NULL; ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "ReopenedAtUtc" timestamptz NULL; ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "ReopenReason" text NOT NULL DEFAULT ''; ALTER TABLE "BankReconciliations" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "BankStatementImportBatches" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "BankAccountId" uuid NOT NULL REFERENCES "BankAccounts" ("Id") ON DELETE RESTRICT, "FileName" text NOT NULL, "Format" text NOT NULL, "ContentSha256" text NOT NULL, "Status" text NOT NULL, "ImportedCount" integer NOT NULL, "DuplicateCount" integer NOT NULL, "RejectedCount" integer NOT NULL, "DebitTotal" numeric(18,2) NOT NULL, "CreditTotal" numeric(18,2) NOT NULL, "RejectionJson" text NOT NULL, "ImportedByUserId" uuid NULL, "ImportedAtUtc" timestamptz NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS "IX_BankStatementImportBatches_CompanyId_BankAccountId_ContentSha256" ON "BankStatementImportBatches" ("CompanyId", "BankAccountId", "ContentSha256");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "BankStatementTransactions" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "BankAccountId" uuid NOT NULL REFERENCES "BankAccounts" ("Id") ON DELETE RESTRICT, "ImportBatchId" uuid NOT NULL REFERENCES "BankStatementImportBatches" ("Id") ON DELETE RESTRICT, "ExternalId" text NOT NULL, "TransactionDate" date NOT NULL, "PostedDate" date NULL, "Amount" numeric(18,2) NOT NULL, "TransactionType" text NOT NULL, "Payee" text NOT NULL, "Memo" text NOT NULL, "Reference" text NOT NULL, "Status" text NOT NULL, "MatchedJournalEntryId" uuid NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT, "MatchedAtUtc" timestamptz NULL, "MatchedByUserId" uuid NULL, "MatchNote" text NOT NULL, "RawJson" text NOT NULL, "ConcurrencyToken" text NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS "IX_BankStatementTransactions_CompanyId_BankAccountId_ExternalId" ON "BankStatementTransactions" ("CompanyId", "BankAccountId", "ExternalId"); CREATE INDEX IF NOT EXISTS "IX_BankStatementTransactions_CompanyId_BankAccountId_Status_TransactionDate" ON "BankStatementTransactions" ("CompanyId", "BankAccountId", "Status", "TransactionDate");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "BankTransfers" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "FromBankAccountId" uuid NOT NULL REFERENCES "BankAccounts" ("Id") ON DELETE RESTRICT, "ToBankAccountId" uuid NOT NULL REFERENCES "BankAccounts" ("Id") ON DELETE RESTRICT, "TransferDate" date NOT NULL, "Amount" numeric(18,2) NOT NULL, "Reference" text NOT NULL, "Memo" text NOT NULL, "Status" text NOT NULL, "JournalEntryId" uuid NOT NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT, "InboundJournalEntryId" uuid NOT NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT, "ReversalJournalEntryId" uuid NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT, "CreatedByUserId" uuid NULL, "CreatedAtUtc" timestamptz NOT NULL, "ConcurrencyToken" text NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS "IX_BankTransfers_CompanyId_Reference" ON "BankTransfers" ("CompanyId", "Reference");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "BankTransfers" ADD COLUMN IF NOT EXISTS "InboundReversalJournalEntryId" uuid NULL REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT; ALTER TABLE "BankTransfers" ADD COLUMN IF NOT EXISTS "ReversedByUserId" uuid NULL; ALTER TABLE "BankTransfers" ADD COLUMN IF NOT EXISTS "ReversedAtUtc" timestamptz NULL; ALTER TABLE "BankTransfers" ADD COLUMN IF NOT EXISTS "ReversalDate" date NULL; ALTER TABLE "BankTransfers" ADD COLUMN IF NOT EXISTS "ReversalReason" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""AuthenticationAuditEntries"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""UserId"" uuid NULL,
                    ""CompanyId"" uuid NULL,
                    ""UserName"" text NOT NULL,
                    ""EventType"" text NOT NULL,
                    ""Succeeded"" boolean NOT NULL,
                    ""OccurredUtc"" timestamptz NOT NULL,
                    ""IpAddress"" text NOT NULL,
                    ""UserAgent"" text NOT NULL,
                    ""Detail"" text NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleSets"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""CompanyId"" uuid NOT NULL,
                    ""Code"" text NOT NULL,
                    ""JurisdictionCode"" text NOT NULL,
                    ""JurisdictionName"" text NOT NULL,
                    ""JurisdictionType"" text NOT NULL,
                    ""TaxType"" text NOT NULL,
                    ""CalculationMethod"" text NOT NULL,
                    ""WithholdingFrequency"" text NOT NULL,
                    ""EffectiveOn"" date NOT NULL,
                    ""Source"" text NOT NULL,
                    ""Notes"" text NOT NULL,
                    ""IsEmployerSpecific"" boolean NOT NULL,
                    ""SupportsBracketTable"" boolean NOT NULL,
                    ""SupportsParameterEditing"" boolean NOT NULL,
                    ""IsActive"" boolean NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleSets_CompanyId_Code"" ON ""TaxRuleSets"" (""CompanyId"", ""Code"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleParameters"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""TaxRuleSetId"" uuid NOT NULL,
                    ""ParameterCode"" text NOT NULL,
                    ""Label"" text NOT NULL,
                    ""ValueType"" text NOT NULL,
                    ""NumericValue"" numeric(18,4) NULL,
                    ""TextValue"" text NOT NULL,
                    ""BooleanValue"" boolean NULL,
                    ""Notes"" text NOT NULL,
                    ""DisplayOrder"" integer NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleParameters_TaxRuleSetId_ParameterCode"" ON ""TaxRuleParameters"" (""TaxRuleSetId"", ""ParameterCode"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleBrackets"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""TaxRuleSetId"" uuid NOT NULL,
                    ""Sequence"" integer NOT NULL,
                    ""UpperBoundAmount"" numeric(18,2) NOT NULL,
                    ""FixedAmount"" numeric(18,2) NOT NULL,
                    ""Rate"" numeric(9,5) NOT NULL,
                    ""Notes"" text NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleBrackets_TaxRuleSetId_Sequence"" ON ""TaxRuleBrackets"" (""TaxRuleSetId"", ""Sequence"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxFormRequirements"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""TaxRuleSetId"" uuid NOT NULL,
                    ""FormCode"" text NOT NULL,
                    ""Name"" text NOT NULL,
                    ""FilingFrequency"" text NOT NULL,
                    ""DeliveryChannel"" text NOT NULL,
                    ""DueRule"" text NOT NULL,
                    ""Notes"" text NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxFormRequirements_TaxRuleSetId_FormCode"" ON ""TaxFormRequirements"" (""TaxRuleSetId"", ""FormCode"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "TaxContentPackageId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "ContentVersion" text NOT NULL DEFAULT '1.0';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "MinimumEngineVersion" text NOT NULL DEFAULT '1.0';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "ParentJurisdictionCode" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "ObligationCode" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "CalculationVariant" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "ExclusiveGroup" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "VariantPriority" integer NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxRuleSets" ADD COLUMN IF NOT EXISTS "ApplicabilityJson" text NOT NULL DEFAULT '{}';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""TaxContentPackages"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""PackageCode"" text NOT NULL, ""Version"" text NOT NULL, ""EffectiveOn"" date NOT NULL, ""Status"" text NOT NULL, ""MinimumEngineVersion"" text NOT NULL, ""ManifestJson"" text NOT NULL, ""Source"" text NOT NULL, ""ChangeSummary"" text NOT NULL, ""CreatedAtUtc"" timestamptz NOT NULL, ""ApprovedAtUtc"" timestamptz NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxContentPackages_CompanyId_PackageCode_Version"" ON ""TaxContentPackages"" (""CompanyId"", ""PackageCode"", ""Version"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""TaxSourceCaptures"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""TaxContentPackageId"" uuid NULL, ""SourceKind"" text NOT NULL, ""JurisdictionCode"" text NOT NULL, ""SourceUrl"" text NOT NULL, ""ContentType"" text NOT NULL, ""ContentSha256"" text NOT NULL, ""RawContent"" text NOT NULL, ""CapturedAtUtc"" timestamptz NOT NULL, ""Notes"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_TaxSourceCaptures_CompanyId_CapturedAtUtc"" ON ""TaxSourceCaptures"" (""CompanyId"", ""CapturedAtUtc"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""TaxRuleFieldDefinitions"" (""Id"" uuid NOT NULL PRIMARY KEY, ""TaxRuleSetId"" uuid NOT NULL, ""FieldCode"" text NOT NULL, ""Label"" text NOT NULL, ""DataType"" text NOT NULL, ""IsRequired"" boolean NOT NULL, ""DefaultValueJson"" text NOT NULL, ""ValidationJson"" text NOT NULL, ""DisplayOrder"" integer NOT NULL, ""HelpText"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleFieldDefinitions_TaxRuleSetId_FieldCode"" ON ""TaxRuleFieldDefinitions"" (""TaxRuleSetId"", ""FieldCode"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""TaxRuleTestCases"" (""Id"" uuid NOT NULL PRIMARY KEY, ""TaxRuleSetId"" uuid NOT NULL, ""Name"" text NOT NULL, ""InputJson"" text NOT NULL, ""ExpectedOutputJson"" text NOT NULL, ""IsRequiredForActivation"" boolean NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleTestCases_TaxRuleSetId_Name"" ON ""TaxRuleTestCases"" (""TaxRuleSetId"", ""Name"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "FilingStatus" text NOT NULL DEFAULT 'Single';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "PayrollFrequency" text NOT NULL DEFAULT 'Biweekly';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "Allowances" integer NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "AdditionalWithholding" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "PreTaxBenefitDeductions" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "PostTaxBenefitDeductions" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "ResidenceState" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "ResidenceCity" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "WorkCity" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "ResidenceCounty" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "ResidenceSchoolDistrict" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "WorkCounty" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "WorkSchoolDistrict" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "AddressLine1" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "AddressLine2" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "PostalCode" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "SocialSecurityNumber" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "BankRoutingNumber" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "BankAccountNumber" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "BankAccountType" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "DirectDepositEnabled" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "EmploymentStartedOn" date NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "EmploymentEndedOn" date NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "HourlyRate" numeric(18,4) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "OvertimeRate" numeric(18,4) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "FederalFormW4Year" integer NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "FederalStep2MultipleJobs" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "FederalStep3Credits" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "FederalStep4OtherIncome" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "FederalStep4Deductions" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "FederalWithholdingExempt" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTimecards"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""EmployeeId"" uuid NOT NULL, ""PeriodStart"" date NOT NULL, ""PeriodEnd"" date NOT NULL, ""Status"" text NOT NULL, ""Notes"" text NOT NULL, ""PreparedByUserId"" uuid NULL, ""PreparedAtUtc"" timestamptz NOT NULL, ""SubmittedByUserId"" uuid NULL, ""SubmittedAtUtc"" timestamptz NULL, ""ApprovedByUserId"" uuid NULL, ""ApprovedAtUtc"" timestamptz NULL, ""VoidedByUserId"" uuid NULL, ""VoidedAtUtc"" timestamptz NULL, ""VoidReason"" text NOT NULL, ""PayrollRunId"" uuid NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_PayrollTimecards_CompanyId_EmployeeId_PeriodStart_PeriodEnd_Status"";", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollTimecards_CompanyId_EmployeeId_PeriodStart_PeriodEnd_Status"" ON ""PayrollTimecards"" (""CompanyId"", ""EmployeeId"", ""PeriodStart"", ""PeriodEnd"", ""Status"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTimeEntries"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollTimecardId"" uuid NOT NULL, ""Sequence"" integer NOT NULL, ""WorkDate"" date NOT NULL, ""EarningCode"" text NOT NULL, ""EarningType"" text NOT NULL, ""Hours"" numeric(18,4) NOT NULL, ""Rate"" numeric(18,4) NOT NULL, ""Amount"" numeric(18,2) NOT NULL, ""IsTaxable"" boolean NOT NULL, ""WorkState"" text NOT NULL, ""WorkCounty"" text NOT NULL, ""WorkCity"" text NOT NULL, ""WorkSchoolDistrict"" text NOT NULL, ""ProjectJobId"" uuid NULL, ""Notes"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollTimeEntries_PayrollTimecardId_Sequence"" ON ""PayrollTimeEntries"" (""PayrollTimecardId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxProfiles" ADD COLUMN IF NOT EXISTS "AnnualWageBase" numeric(18,2) NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxProfiles" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxProfiles" ADD COLUMN IF NOT EXISTS "IsVerified" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaxProfiles" ADD COLUMN IF NOT EXISTS "VerificationNotes" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollRuns"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""BankAccountId"" uuid NOT NULL, ""PayDate"" date NOT NULL, ""Reference"" text NOT NULL, ""GrossPayroll"" numeric(18,2) NOT NULL, ""PreTaxDeductions"" numeric(18,2) NOT NULL, ""EmployeeWithholdings"" numeric(18,2) NOT NULL, ""PostTaxDeductions"" numeric(18,2) NOT NULL, ""EmployerPayrollTaxes"" numeric(18,2) NOT NULL, ""NetPay"" numeric(18,2) NOT NULL, ""PostedAtUtc"" timestamptz NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "TaxContentSnapshotJson" text NOT NULL DEFAULT '[]';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "PeriodStart" date NOT NULL DEFAULT DATE '0001-01-01';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "PeriodEnd" date NOT NULL DEFAULT DATE '0001-01-01';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "RunType" text NOT NULL DEFAULT 'Regular';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'Posted';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "JournalEntryId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ReversalJournalEntryId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "PreparedByUserId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "PreparedAtUtc" timestamptz NOT NULL DEFAULT '-infinity';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ApprovedByUserId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ApprovedAtUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "PostedByUserId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "CancelledByUserId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "CancelledAtUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "CancellationReason" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ReversedByUserId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ReversedAtUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ReversalDate" date NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ReversalReason" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "CalculationWarningsJson" text NOT NULL DEFAULT '[]';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollRuns_CompanyId_Reference"" ON ""PayrollRuns"" (""CompanyId"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollRunEmployeeLines"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollRunId"" uuid NOT NULL, ""EmployeeId"" uuid NOT NULL, ""WorkState"" text NOT NULL, ""FilingStatus"" text NOT NULL, ""GrossPay"" numeric(18,2) NOT NULL, ""PreTaxDeductions"" numeric(18,2) NOT NULL, ""EmployeeWithholdings"" numeric(18,2) NOT NULL, ""PostTaxDeductions"" numeric(18,2) NOT NULL, ""EmployerPayrollTaxes"" numeric(18,2) NOT NULL, ""NetPay"" numeric(18,2) NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "PayrollFrequency" text NOT NULL DEFAULT 'Biweekly';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollRunEmployeeLines_PayrollRunId_EmployeeId"" ON ""PayrollRunEmployeeLines"" (""PayrollRunId"", ""EmployeeId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollJurisdictionRules"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""ResidenceJurisdiction"" text NOT NULL, ""WorkJurisdiction"" text NOT NULL, ""ExemptWorkWithholding"" boolean NOT NULL, ""ResidentCreditRate"" numeric(9,5) NOT NULL, ""IsActive"" boolean NOT NULL, ""Notes"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollJurisdictionRules_CompanyId_ResidenceJurisdiction_WorkJurisdiction"" ON ""PayrollJurisdictionRules"" (""CompanyId"", ""ResidenceJurisdiction"", ""WorkJurisdiction"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "WorkCity" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "ResidenceState" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "ResidenceCity" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "TaxableWages" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "YearToDateGrossBefore" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "YearToDateGrossAfter" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "CalculationTraceJson" text NOT NULL DEFAULT '[]';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollEarningLines"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" uuid NOT NULL, ""PayrollTimeEntryId"" uuid NULL, ""Sequence"" integer NOT NULL, ""EarningCode"" text NOT NULL, ""EarningType"" text NOT NULL, ""Hours"" numeric(18,4) NOT NULL, ""Rate"" numeric(18,4) NOT NULL, ""Amount"" numeric(18,2) NOT NULL, ""IsTaxable"" boolean NOT NULL, ""WorkedOn"" date NULL, ""WorkState"" text NOT NULL, ""WorkCounty"" text NOT NULL, ""WorkCity"" text NOT NULL, ""WorkSchoolDistrict"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollEarningLines" ADD COLUMN IF NOT EXISTS "PayrollTimeEntryId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollEarningLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollEarningLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_PayrollEarningLines_PayrollTimeEntryId"";", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollEarningLines_PayrollTimeEntryId"" ON ""PayrollEarningLines"" (""PayrollTimeEntryId"") WHERE ""PayrollTimeEntryId"" IS NOT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDeductionLines"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" uuid NOT NULL, ""Sequence"" integer NOT NULL, ""DeductionCode"" text NOT NULL, ""DeductionType"" text NOT NULL, ""EmployeeAmount"" numeric(18,2) NOT NULL, ""EmployerAmount"" numeric(18,2) NOT NULL, ""IsPreTax"" boolean NOT NULL, ""LiabilityAccountNumber"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "ExemptFromFederalIncomeTax" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "ExemptFromFica" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "ExemptFromFuta" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDeductionLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollDeductionLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTaxLines"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" uuid NOT NULL, ""Sequence"" integer NOT NULL, ""ObligationCode"" text NOT NULL, ""JurisdictionCode"" text NOT NULL, ""JurisdictionName"" text NOT NULL, ""TaxType"" text NOT NULL, ""TaxableWages"" numeric(18,2) NOT NULL, ""YearToDateTaxableWagesBefore"" numeric(18,2) NOT NULL, ""EmployeeAmount"" numeric(18,2) NOT NULL, ""EmployerAmount"" numeric(18,2) NOT NULL, ""TaxRuleSetId"" uuid NULL, ""TaxContentPackageId"" uuid NULL, ""ContentVersion"" text NOT NULL, ""Source"" text NOT NULL, ""CalculationTraceJson"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollTaxLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollTaxLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
        }
    }

    private static async Task EnsureCaseInsensitiveUserNameUniquenessAsync(
        BrassLedgerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var duplicateUserNames = await dbContext.Users
            .AsNoTracking()
            .GroupBy(user => user.UserName.ToUpper())
            .AnyAsync(group => group.Count() > 1, cancellationToken);

        if (duplicateUserNames)
        {
            throw new InvalidOperationException(
                "BrassLedger cannot start because two operator usernames differ only by letter case. Rename one of the duplicate accounts before restarting.");
        }

        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_UserName_CaseInsensitive" ON "Users" ("UserName" COLLATE NOCASE);""",
                cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_UserName_CaseInsensitive" ON "Users" (LOWER("UserName"));""",
                cancellationToken);
        }
    }

    private static async Task EnsureSqliteColumnAsync(
        BrassLedgerDbContext dbContext,
        string tableName,
        string columnName,
        string alterSql,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{tableName}');";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = alterSql;
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
