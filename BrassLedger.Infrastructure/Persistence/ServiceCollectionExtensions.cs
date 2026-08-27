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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BrassLedger.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    internal const string BaselineSchemaVersion = "2026082501-versioned-schema-baseline";
    internal const string W2ReportingSchemaVersion = "2026082502-w2-reporting-metadata";
    internal const string AccountingInterchangeSchemaVersion = "2026082503-accounting-interchange-batches";
    internal const string MultiFactorAuthenticationSchemaVersion = "2026082504-multi-factor-authentication";
    internal const string PrivilegedRoleMfaSchemaVersion = "2026082505-privileged-role-mfa";
    internal const string AccountRecoverySchemaVersion = "2026082506-account-invitations-and-recovery";
    internal const string AccountEmailLookupSchemaVersion = "2026082507-account-email-lookup";
    internal const string SecurityEmailActionValiditySchemaVersion = "2026082508-security-email-action-validity";
    internal const string NamedUserSessionSchemaVersion = "2026082509-named-user-sessions";
    internal const string QuickBooksOAuthSchemaVersion = "2026082510-quickbooks-oauth-connections";
    internal const string ExternalEntitySyncSchemaVersion = "2026082511-external-entity-sync";
    internal const string QuickBooksCredentialOperationLeaseSchemaVersion = "2026082512-quickbooks-credential-operation-leases";
    internal const string CurrentSchemaVersion = "2026082513-operational-account-roles";
    internal const string SqliteMigrationAssemblyName = "BrassLedger.Migrations.Sqlite";
    internal const string PostgreSqlMigrationAssemblyName = "BrassLedger.Migrations.PostgreSql";
    internal const string SqliteMigrationBaselineId = "20260826014829_InitialCurrentSchema";
    internal const string PostgreSqlMigrationBaselineId = "20260826014843_InitialCurrentSchema";
    private static readonly string[] OrderedCompatibilitySchemaVersions = [BaselineSchemaVersion, W2ReportingSchemaVersion, AccountingInterchangeSchemaVersion, MultiFactorAuthenticationSchemaVersion, PrivilegedRoleMfaSchemaVersion, AccountRecoverySchemaVersion, AccountEmailLookupSchemaVersion, SecurityEmailActionValiditySchemaVersion, NamedUserSessionSchemaVersion, QuickBooksOAuthSchemaVersion, ExternalEntitySyncSchemaVersion, QuickBooksCredentialOperationLeaseSchemaVersion, CurrentSchemaVersion];
    private static readonly HashSet<string> SupportedSchemaVersions = new(OrderedCompatibilitySchemaVersions, StringComparer.Ordinal);

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
                options.UseNpgsql(postgresConnectionString, postgres => postgres.MigrationsAssembly(PostgreSqlMigrationAssemblyName));
            }
            else
            {
                options.UseSqlite(sqliteConnectionString, sqlite => sqlite.MigrationsAssembly(SqliteMigrationAssemblyName));
            }
        });

        services.AddHttpContextAccessor();
        services.AddHttpClient("TaxSourceCapture", client => client.Timeout = TimeSpan.FromSeconds(45))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("QuickBooksOnline", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton(new BrassLedgerStoragePaths(dataDirectory, keysDirectory));
        services.AddSingleton(Options.Create(BuildBootstrapOptions(configuration, seedSampleData)));
        services.AddOptions<AccountEmailOptions>()
            .Bind(configuration.GetSection("AccountEmail"))
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.Host), "AccountEmail:Host is required when security email is enabled.")
            .Validate(options => !options.Enabled || options.Port is > 0 and <= 65535, "AccountEmail:Port must be between 1 and 65535.")
            .Validate(options => !options.Enabled || options.Security.Trim().ToUpperInvariant() is "STARTTLS" or "SSL" or "SSLONCONNECT", "AccountEmail:Security must be StartTls, Ssl, or SslOnConnect; downgrade-capable SMTP modes are prohibited.")
            .Validate(options => !options.Enabled || AccountEmailIdentity.TryNormalize(options.FromAddress, out _, out _), "AccountEmail:FromAddress must be a valid mailbox.")
            .Validate(options => !options.Enabled || Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment), "AccountEmail:PublicBaseUrl must be an absolute HTTPS URL without credentials, a query, or a fragment.")
            .Validate(options => options.InvitationLifetimeHours is >= 1 and <= 168, "AccountEmail:InvitationLifetimeHours must be between 1 and 168.")
            .Validate(options => options.EmailVerificationLifetimeHours is >= 1 and <= 168, "AccountEmail:EmailVerificationLifetimeHours must be between 1 and 168.")
            .Validate(options => options.PasswordResetLifetimeMinutes is >= 10 and <= 120, "AccountEmail:PasswordResetLifetimeMinutes must be between 10 and 120.")
            .Validate(options => options.MaximumDeliveryAttempts is >= 1 and <= 20, "AccountEmail:MaximumDeliveryAttempts must be between 1 and 20.")
            .Validate(options => options.DeliveryTimeoutSeconds is >= 5 and <= 120, "AccountEmail:DeliveryTimeoutSeconds must be between 5 and 120.")
            .ValidateOnStart();
        services.AddOptions<QuickBooksOnlineOptions>()
            .Bind(configuration.GetSection("QuickBooksOnline"))
            .Validate(options => (options.Environment ?? string.Empty).Trim().ToUpperInvariant() is "SANDBOX" or "PRODUCTION", "QuickBooksOnline:Environment must be Sandbox or Production.")
            .Validate(options => options.AuthorizationStateLifetimeMinutes is >= 5 and <= 30, "QuickBooksOnline:AuthorizationStateLifetimeMinutes must be between 5 and 30.")
            .Validate(options => IsSecureProviderUri(options.AuthorizationEndpoint), "QuickBooksOnline:AuthorizationEndpoint must be an absolute HTTPS URL without credentials or a fragment.")
            .Validate(options => IsSecureProviderUri(options.TokenEndpoint), "QuickBooksOnline:TokenEndpoint must be an absolute HTTPS URL without credentials, a query, or a fragment.")
            .Validate(options => IsSecureProviderUri(options.RevocationEndpoint), "QuickBooksOnline:RevocationEndpoint must be an absolute HTTPS URL without credentials, a query, or a fragment.")
            .Validate(options => IsSecureProviderUri(options.SandboxApiBaseUrl), "QuickBooksOnline:SandboxApiBaseUrl must be an absolute HTTPS URL without credentials, a query, or a fragment.")
            .Validate(options => IsSecureProviderUri(options.ProductionApiBaseUrl), "QuickBooksOnline:ProductionApiBaseUrl must be an absolute HTTPS URL without credentials, a query, or a fragment.")
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ClientId), "QuickBooksOnline:ClientId is required when QuickBooks OAuth is enabled.")
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ClientSecret), "QuickBooksOnline:ClientSecret is required when QuickBooks OAuth is enabled.")
            .Validate(options => !options.Enabled || IsSecureRedirectUri(options.RedirectUri, options.Environment), "QuickBooksOnline:RedirectUri must be an absolute HTTPS URL; Sandbox may use HTTP only for a loopback host.")
            .ValidateOnStart();
        services.AddSingleton<ISensitiveDataProtector, SensitiveDataProtector>();
        services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TotpService>();
        services.TryAddSingleton<ISecurityEmailTransport, MailKitSecurityEmailTransport>();
        services.AddSingleton<ISecurityEmailOutboxDispatcher, SecurityEmailOutboxDispatcher>();
        services.AddHostedService<SecurityEmailOutboxWorker>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IUserSessionService, UserSessionService>();
        services.AddScoped<IAccountActionService, AccountActionService>();
        services.AddScoped<IBootstrapWorkspaceService, BootstrapWorkspaceService>();
        services.AddScoped<IBusinessWorkspaceService, BusinessWorkspaceService>();
        services.AddScoped<ICompanyManagementService, CompanyManagementService>();
        services.AddScoped<IConsolidationService, ConsolidationService>();
        services.AddScoped<IAccountingPeriodService, AccountingPeriodService>();
        services.AddScoped<IAccountingAccountRoleService, AccountingAccountRoleService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IIntegrationService, IntegrationService>();
        services.AddSingleton<IQuickBooksOnlineClient, QuickBooksOnlineClient>();
        services.AddScoped<IQuickBooksOnlineConnectionService, QuickBooksOnlineConnectionService>();
        services.AddScoped<IQuickBooksOnlineSyncService, QuickBooksOnlineSyncService>();
        services.AddScoped<IAccountingTransactionService, AccountingTransactionService>();
        services.AddScoped<IPayrollReportingService, PayrollReportingService>();
        services.AddScoped<IPayrollFilingService, PayrollFilingService>();
        services.AddScoped<IPayrollDeductionConfigurationService, PayrollDeductionConfigurationService>();
        services.AddScoped<IPayrollPaymentFileService, PayrollPaymentFileService>();
        services.AddScoped<IPayrollDepositScheduleService, PayrollDepositScheduleService>();
        services.AddScoped<IPayrollDisasterReliefService, PayrollDisasterReliefService>();
        services.AddScoped<ISsaWageFileService, SsaWageFileService>();
        services.AddScoped<ISsaOriginalWageFileService, SsaOriginalWageFileService>();
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
        await MigrateDatabaseAsync(dbContext, cancellationToken);
        await EnsureAccountEmailLookupHashesAsync(dbContext, cancellationToken);
        await BrassLedgerSeedData.SeedAsync(dbContext, passwordHasher, bootstrapOptions, cancellationToken);
        await DefaultAccountingSetup.EnsureMinimumSetupAsync(dbContext, cancellationToken);
        await DefaultInventorySetup.EnsureAsync(dbContext, cancellationToken);
    }

    private static async Task MigrateDatabaseAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        var availableMigrations = dbContext.Database.GetMigrations().ToArray();
        var baselineId = dbContext.Database.IsNpgsql() ? PostgreSqlMigrationBaselineId : SqliteMigrationBaselineId;
        if (!availableMigrations.Contains(baselineId, StringComparer.Ordinal))
            throw new InvalidOperationException($"The configured migration assembly does not contain required baseline {baselineId}. Installation or deployment is incomplete.");

        var creator = dbContext.Database.GetService<IRelationalDatabaseCreator>();
        var databaseExists = await creator.ExistsAsync(cancellationToken);
        var hasTables = databaseExists && await creator.HasTablesAsync(cancellationToken);
        if (!hasTables)
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
            await RecordCompatibilityBaselineAsync(dbContext, cancellationToken);
            return;
        }

        var appliedMigrations = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        var unknownMigrations = appliedMigrations.Except(availableMigrations, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (unknownMigrations.Length > 0)
            throw new InvalidOperationException($"This database contains unsupported or newer EF migration(s): {string.Join(", ", unknownMigrations)}. Upgrade BrassLedger before opening this database; automatic downgrade is prohibited.");

        if (appliedMigrations.Length > 0 && !appliedMigrations.Contains(baselineId, StringComparer.Ordinal))
            throw new InvalidOperationException($"The EF migration history is inconsistent: required baseline {baselineId} is absent. Restore a verified backup or repair migration history under controlled support supervision.");
        if (!await HasBrassLedgerSchemaFingerprintAsync(dbContext, cancellationToken))
            throw new InvalidOperationException("The configured database is not empty but does not contain the required BrassLedger Companies, Users, and Accounts tables. Startup refused to modify an unknown or incomplete database.");

        // Existing BrassLedger databases predate EF migration history. Bring their
        // ordered compatibility ledger to the migration baseline once, then adopt
        // that already-present schema without replaying destructive CREATE steps.
        await ApplySchemaUpgradesAsync(dbContext, cancellationToken);
        if (appliedMigrations.Length == 0)
            await AdoptMigrationBaselineAsync(dbContext, baselineId, availableMigrations, cancellationToken);

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task RecordCompatibilityBaselineAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE TABLE IF NOT EXISTS "BrassLedgerSchemaVersions" ("VersionId" text NOT NULL PRIMARY KEY, "AppliedAtUtc" text NOT NULL, "ProductVersion" text NOT NULL, "Description" text NOT NULL, "Provider" text NOT NULL);""",
            cancellationToken);
        foreach (var version in OrderedCompatibilitySchemaVersions)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "BrassLedgerSchemaVersions" ("VersionId", "AppliedAtUtc", "ProductVersion", "Description", "Provider") VALUES ({version}, {DateTimeOffset.UtcNow.ToString("O")}, {"2026.08.25"}, {$"Compatibility checkpoint recorded by EF migration baseline for {version}."}, {dbContext.Database.ProviderName ?? "Unknown"}) ON CONFLICT ("VersionId") DO NOTHING;""",
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> HasBrassLedgerSchemaFingerprintAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = dbContext.Database.IsNpgsql()
                ? """SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('Companies', 'Users', 'Accounts');"""
                : """SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Companies', 'Users', 'Accounts');""";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 3;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task AdoptMigrationBaselineAsync(BrassLedgerDbContext dbContext, string baselineId, IReadOnlyCollection<string> availableMigrations, CancellationToken cancellationToken)
    {
        if (!availableMigrations.Contains(baselineId, StringComparer.Ordinal))
            throw new InvalidOperationException($"The configured migration assembly does not contain required baseline {baselineId}. Installation or deployment is incomplete.");

        var historyRepository = dbContext.Database.GetService<IHistoryRepository>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(historyRepository.GetCreateIfNotExistsScript(), cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(historyRepository.GetInsertScript(new HistoryRow(baselineId, "8.0.30")), cancellationToken);
        foreach (var migrationId in availableMigrations.Where(id => !string.Equals(id, baselineId, StringComparison.Ordinal)).OrderBy(id => id, StringComparer.Ordinal))
        {
            if (await IsMigrationSchemaAlreadyPresentAsync(dbContext, migrationId, cancellationToken))
                await dbContext.Database.ExecuteSqlRawAsync(historyRepository.GetInsertScript(new HistoryRow(migrationId, "8.0.30")), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> IsMigrationSchemaAlreadyPresentAsync(BrassLedgerDbContext dbContext, string migrationId, CancellationToken cancellationToken)
    {
        if (migrationId.EndsWith("_AddAccountingSchedules", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "AccountingSchedules", cancellationToken)
                && await HasTableAsync(dbContext, "AccountingScheduleInstallments", cancellationToken);
        if (migrationId.EndsWith("_AddFixedAssetDisposals", StringComparison.Ordinal))
            return await HasColumnAsync(dbContext, "AccountingSchedules", "DisposalJournalEntryId", cancellationToken);
        if (migrationId.EndsWith("_AddPurchaseReceiving", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "PurchaseOrderLines", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryReceipts", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryReceiptLines", cancellationToken)
                && await HasColumnAsync(dbContext, "InventoryItems", "UnitCost", cancellationToken)
                && await HasColumnAsync(dbContext, "VendorBills", "InventoryReceiptId", cancellationToken);
        if (migrationId.EndsWith("_AddSalesFulfillment", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "SalesOrderLines", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryShipments", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryShipmentLines", cancellationToken)
                && await HasColumnAsync(dbContext, "SalesOrders", "ConcurrencyToken", cancellationToken)
                && await HasColumnAsync(dbContext, "SalesInvoices", "InventoryShipmentId", cancellationToken);
        if (migrationId.EndsWith("_AddSalesQuotes", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "SalesQuotes", cancellationToken)
                && await HasTableAsync(dbContext, "SalesQuoteLines", cancellationToken)
                && await HasColumnAsync(dbContext, "SalesOrders", "SalesQuoteId", cancellationToken);
        if (migrationId.EndsWith("_AddSalesOrderChangeControls", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "SalesOrderAmendments", cancellationToken)
                && await HasColumnAsync(dbContext, "SalesOrderLines", "CancelledQuantity", cancellationToken)
                && await HasColumnAsync(dbContext, "SalesOrders", "CancellationReason", cancellationToken);
        if (migrationId.EndsWith("_AddInventoryLocations", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "InventoryWarehouses", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryBins", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryLocationBalances", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryTransfers", cancellationToken)
                && await HasColumnAsync(dbContext, "InventoryTransactions", "WarehouseId", cancellationToken);
        if (migrationId.EndsWith("_AddPickPackBackorders", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "InventoryPicks", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryPickLines", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryPackingSlips", cancellationToken)
                && await HasTableAsync(dbContext, "InventoryPackingSlipLines", cancellationToken)
                && await HasTableAsync(dbContext, "SalesOrderBackorderPromises", cancellationToken)
                && await HasColumnAsync(dbContext, "InventoryShipments", "InventoryPackingSlipId", cancellationToken);
        if (migrationId.EndsWith("_AddCustomerReturns", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "CustomerReturnAuthorizations", cancellationToken)
                && await HasTableAsync(dbContext, "CustomerReturnAuthorizationLines", cancellationToken)
                && await HasTableAsync(dbContext, "CustomerReturnReceipts", cancellationToken)
                && await HasTableAsync(dbContext, "CustomerReturnReceiptLines", cancellationToken)
                && await HasTableAsync(dbContext, "CustomerReturnCredits", cancellationToken)
                && await HasTableAsync(dbContext, "CustomerReturnCreditLines", cancellationToken)
                && await HasTableAsync(dbContext, "CustomerReturnCreditApplications", cancellationToken)
                && await HasTableAsync(dbContext, "CustomerReturnCreditRefunds", cancellationToken);
        if (migrationId.EndsWith("_AddPurchaseRequisitions", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "PurchaseRequisitions", cancellationToken)
                && await HasTableAsync(dbContext, "PurchaseRequisitionLines", cancellationToken)
                && await HasColumnAsync(dbContext, "PurchaseOrders", "PurchaseRequisitionId", cancellationToken);
        if (migrationId.EndsWith("_AddSupplierReturns", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "SupplierReturnAuthorizations", cancellationToken)
                && await HasTableAsync(dbContext, "SupplierReturnAuthorizationLines", cancellationToken)
                && await HasTableAsync(dbContext, "SupplierReturnShipments", cancellationToken)
                && await HasTableAsync(dbContext, "SupplierReturnShipmentLines", cancellationToken)
                && await HasTableAsync(dbContext, "SupplierReturnCreditApplications", cancellationToken)
                && await HasTableAsync(dbContext, "SupplierReturnCreditRefunds", cancellationToken)
                && await HasColumnAsync(dbContext, "InventoryReceiptLines", "ReturnedQuantity", cancellationToken)
                && await HasColumnAsync(dbContext, "PurchaseOrderLines", "CreditedQuantity", cancellationToken)
                && await HasColumnAsync(dbContext, "VendorBillLines", "InventoryReceiptLineId", cancellationToken);
        if (migrationId.EndsWith("_AddLandedCostAllocations", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "LandedCostAllocations", cancellationToken)
                && await HasTableAsync(dbContext, "LandedCostCharges", cancellationToken)
                && await HasTableAsync(dbContext, "LandedCostAllocationLines", cancellationToken);
        if (migrationId.EndsWith("_SeparateSupplierReturnCreditValue", StringComparison.Ordinal))
            return await HasColumnAsync(dbContext, "SupplierReturnAuthorizations", "Id", cancellationToken)
                && await HasColumnAsync(dbContext, "SupplierReturnAuthorizationLines", "ReceiptUnitCost", cancellationToken)
                && await HasColumnAsync(dbContext, "SupplierReturnShipments", "VendorCreditAmount", cancellationToken)
                && await HasColumnAsync(dbContext, "SupplierReturnShipmentLines", "VendorCreditUnitCost", cancellationToken)
                && await HasColumnAsync(dbContext, "SupplierReturnShipmentLines", "VendorCreditAmount", cancellationToken);
        if (migrationId.EndsWith("_AddControlledPurchaseInvoiceMatching", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "PurchaseInvoiceMatches", cancellationToken)
                && await HasTableAsync(dbContext, "PurchaseInvoiceMatchLines", cancellationToken)
                && await HasColumnAsync(dbContext, "VendorBillLines", "MatchedQuantity", cancellationToken)
                && await HasColumnAsync(dbContext, "VendorBillLines", "AccrualAmount", cancellationToken)
                && await HasColumnAsync(dbContext, "SupplierReturnShipmentLines", "InvoicedQuantity", cancellationToken)
                && await HasColumnAsync(dbContext, "SupplierReturnShipmentLines", "GrniReductionAmount", cancellationToken);
        if (migrationId.EndsWith("_ScopeVendorBillNumbersByVendor", StringComparison.Ordinal))
            return await HasIndexAsync(dbContext, "IX_VendorBills_CompanyId_VendorId_BillNumber", cancellationToken)
                && await HasIndexAsync(dbContext, "IX_PurchaseInvoiceMatches_CompanyId_VendorId_BillNumber", cancellationToken)
                && await HasIndexAsync(dbContext, "IX_LandedCostAllocations_CompanyId_VendorId_BillNumber", cancellationToken);
        if (migrationId.EndsWith("_ScopeSubledgerVendorBillNumbersByVendor", StringComparison.Ordinal))
            return await HasColumnAsync(dbContext, "SubledgerDocumentWorkflows", "DocumentScope", cancellationToken)
                && await HasIndexAsync(dbContext, dbContext.Database.IsNpgsql()
                    ? "IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentS~"
                    : "IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentScope_DocumentNumber_IsRecurringTemplate", cancellationToken);
        if (migrationId.EndsWith("_AddSubledgerRejectionWorkflow", StringComparison.Ordinal))
            return await HasColumnAsync(dbContext, "SubledgerDocumentWorkflows", "RejectedByUserId", cancellationToken)
                && await HasColumnAsync(dbContext, "SubledgerDocumentWorkflows", "RejectedAtUtc", cancellationToken)
                && await HasColumnAsync(dbContext, "SubledgerDocumentWorkflows", "DecisionReason", cancellationToken);
        if (migrationId.EndsWith("_AddControlledJournalReview", StringComparison.Ordinal))
            return await HasColumnAsync(dbContext, "JournalEntries", "RejectedByUserId", cancellationToken)
                && await HasColumnAsync(dbContext, "JournalEntries", "RejectedAtUtc", cancellationToken)
                && await HasColumnAsync(dbContext, "JournalEntries", "DecisionReason", cancellationToken);
        if (migrationId.EndsWith("_AddControlledPayrollReview", StringComparison.Ordinal))
            return await HasColumnAsync(dbContext, "PayrollRuns", "RejectedByUserId", cancellationToken)
                && await HasColumnAsync(dbContext, "PayrollRuns", "RejectedAtUtc", cancellationToken)
                && await HasColumnAsync(dbContext, "PayrollRuns", "RejectionReason", cancellationToken)
                && await HasTableAsync(dbContext, "PayrollRunRevisions", cancellationToken)
                && await HasColumnAsync(dbContext, "PayrollRunRevisions", "PayloadJson", cancellationToken)
                && await HasIndexAsync(dbContext, "IX_PayrollRunRevisions_PayrollRunId_RevisionNumber", cancellationToken);
        if (migrationId.EndsWith("_AddProjectLedgerDimensions", StringComparison.Ordinal))
            return await HasColumnAsync(dbContext, "ProjectJobs", "CustomerId", cancellationToken)
                && await HasColumnAsync(dbContext, "ProjectJobs", "BillingMethod", cancellationToken)
                && await HasColumnAsync(dbContext, "ProjectJobs", "ConcurrencyToken", cancellationToken)
                && await HasColumnAsync(dbContext, "JournalEntryLines", "ProjectJobId", cancellationToken)
                && await HasColumnAsync(dbContext, "PayrollEarningLines", "ProjectJobId", cancellationToken)
                && await HasColumnAsync(dbContext, "SalesInvoiceLines", "ProjectJobId", cancellationToken)
                && await HasColumnAsync(dbContext, "VendorBillLines", "ProjectJobId", cancellationToken)
                && await HasColumnAsync(dbContext, "SalesOrderLines", "ProjectJobId", cancellationToken)
                && await HasColumnAsync(dbContext, "PurchaseOrderLines", "ProjectJobId", cancellationToken)
                && await HasIndexAsync(dbContext, "IX_JournalEntryLines_ProjectJobId_JournalEntryId", cancellationToken);
        if (migrationId.EndsWith("_AddControlledProjectChangeOrders", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "ProjectChangeOrders", cancellationToken)
                && await HasColumnAsync(dbContext, "ProjectChangeOrders", "SubmittedProjectConcurrencyToken", cancellationToken)
                && await HasColumnAsync(dbContext, "ProjectChangeOrders", "ContractAmountAfter", cancellationToken)
                && await HasIndexAsync(dbContext, "IX_ProjectChangeOrders_CompanyId_ProjectJobId_ChangeOrderNumber", cancellationToken);
        if (migrationId.EndsWith("_AddControlledProjectBilling", StringComparison.Ordinal))
            return await HasTableAsync(dbContext, "ProjectBillingRates", cancellationToken)
                && await HasTableAsync(dbContext, "ProjectBillingProposals", cancellationToken)
                && await HasTableAsync(dbContext, "ProjectBillingLines", cancellationToken)
                && await HasTableAsync(dbContext, "ProjectBillingSourceReservations", cancellationToken)
                && await HasColumnAsync(dbContext, "ProjectBillingProposals", "PreviewFingerprint", cancellationToken)
                && await HasColumnAsync(dbContext, "ProjectBillingProposals", "PreparedProjectConcurrencyToken", cancellationToken)
                && await HasIndexAsync(dbContext, "IX_ProjectBillingProposals_SubledgerDocumentWorkflowId", cancellationToken)
                && await HasIndexAsync(dbContext, "IX_ProjectBillingSourceReservations_CompanyId_SourceKey", cancellationToken);
        return false;
    }

    private static async Task<bool> HasTableAsync(BrassLedgerDbContext dbContext, string tableName, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = dbContext.Database.IsNpgsql()
            ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @name;"
            : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = tableName; command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<bool> HasColumnAsync(BrassLedgerDbContext dbContext, string tableName, string columnName, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        if (dbContext.Database.IsNpgsql())
        {
            command.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table AND column_name = @column;";
            var table = command.CreateParameter(); table.ParameterName = "@table"; table.Value = tableName; command.Parameters.Add(table);
            var column = command.CreateParameter(); column.ParameterName = "@column"; column.Value = columnName; command.Parameters.Add(column);
        }
        else
        {
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName.Replace("'", "''", StringComparison.Ordinal)}') WHERE name = @column;";
            var column = command.CreateParameter(); column.ParameterName = "@column"; column.Value = columnName; command.Parameters.Add(column);
        }
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<bool> HasIndexAsync(BrassLedgerDbContext dbContext, string indexName, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = dbContext.Database.IsNpgsql()
            ? "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = @name;"
            : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = @name;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = indexName; command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static BootstrapOptions BuildBootstrapOptions(IConfiguration configuration, bool seedSampleData)
    {
        var options = configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new BootstrapOptions();
        options.SeedSampleData = options.SeedSampleData || seedSampleData;
        return options;
    }

    private static bool IsSecureProviderUri(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsSecureRedirectUri(string value, string environment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host)) return true;
        return string.Equals(environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback;
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

    private static async Task ApplySchemaUpgradesAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE TABLE IF NOT EXISTS "BrassLedgerSchemaVersions" ("VersionId" text NOT NULL PRIMARY KEY, "AppliedAtUtc" text NOT NULL, "ProductVersion" text NOT NULL, "Description" text NOT NULL, "Provider" text NOT NULL);""",
            cancellationToken);

        var applied = new HashSet<string>(StringComparer.Ordinal);
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT "VersionId" FROM "BrassLedgerSchemaVersions" ORDER BY "VersionId";""";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) applied.Add(reader.GetString(0));
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }

        var unsupported = applied.Where(version => !SupportedSchemaVersions.Contains(version)).OrderBy(version => version, StringComparer.Ordinal).ToArray();
        if (unsupported.Length > 0)
            throw new InvalidOperationException($"This database contains unsupported or newer BrassLedger schema version(s): {string.Join(", ", unsupported)}. Upgrade the application before opening this database; automatic downgrade is prohibited.");
        if (applied.Contains(W2ReportingSchemaVersion) && !applied.Contains(BaselineSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {W2ReportingSchemaVersion} is recorded without prerequisite {BaselineSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(AccountingInterchangeSchemaVersion) && !applied.Contains(W2ReportingSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {AccountingInterchangeSchemaVersion} is recorded without prerequisite {W2ReportingSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(MultiFactorAuthenticationSchemaVersion) && !applied.Contains(AccountingInterchangeSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {MultiFactorAuthenticationSchemaVersion} is recorded without prerequisite {AccountingInterchangeSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(PrivilegedRoleMfaSchemaVersion) && !applied.Contains(MultiFactorAuthenticationSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {PrivilegedRoleMfaSchemaVersion} is recorded without prerequisite {MultiFactorAuthenticationSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(AccountRecoverySchemaVersion) && !applied.Contains(PrivilegedRoleMfaSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {AccountRecoverySchemaVersion} is recorded without prerequisite {PrivilegedRoleMfaSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(AccountEmailLookupSchemaVersion) && !applied.Contains(AccountRecoverySchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {AccountEmailLookupSchemaVersion} is recorded without prerequisite {AccountRecoverySchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(SecurityEmailActionValiditySchemaVersion) && !applied.Contains(AccountEmailLookupSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {SecurityEmailActionValiditySchemaVersion} is recorded without prerequisite {AccountEmailLookupSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(NamedUserSessionSchemaVersion) && !applied.Contains(SecurityEmailActionValiditySchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {NamedUserSessionSchemaVersion} is recorded without prerequisite {SecurityEmailActionValiditySchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(QuickBooksOAuthSchemaVersion) && !applied.Contains(NamedUserSessionSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {QuickBooksOAuthSchemaVersion} is recorded without prerequisite {NamedUserSessionSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(ExternalEntitySyncSchemaVersion) && !applied.Contains(QuickBooksOAuthSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {ExternalEntitySyncSchemaVersion} is recorded without prerequisite {QuickBooksOAuthSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(QuickBooksCredentialOperationLeaseSchemaVersion) && !applied.Contains(ExternalEntitySyncSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {QuickBooksCredentialOperationLeaseSchemaVersion} is recorded without prerequisite {ExternalEntitySyncSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (applied.Contains(CurrentSchemaVersion) && !applied.Contains(QuickBooksCredentialOperationLeaseSchemaVersion))
            throw new InvalidOperationException($"The BrassLedger schema ledger is inconsistent: {CurrentSchemaVersion} is recorded without prerequisite {QuickBooksCredentialOperationLeaseSchemaVersion}. Restore a verified backup or repair the ledger under controlled support supervision.");
        if (!applied.Contains(BaselineSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, BaselineSchemaVersion, "Established the ordered schema ledger after upgrading any pre-ledger database to the current model.", async () =>
            {
                // Only databases that existed before the EF baseline reach this
                // compatibility path; fresh databases are migration-created.
                await EnsureLegacySchemaCompatibilityAsync(dbContext, cancellationToken);
                await EnsureCaseInsensitiveUserNameUniquenessAsync(dbContext, cancellationToken);
            }, cancellationToken);

        if (!applied.Contains(W2ReportingSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, W2ReportingSchemaVersion, "Added durable W-2 reporting metadata to time entries and posted earning lines.", () => EnsureW2ReportingMetadataSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(AccountingInterchangeSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, AccountingInterchangeSchemaVersion, "Added durable accounting-interchange validation, import, duplicate, and rejection batch history.", () => EnsureAccountingInterchangeBatchSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(MultiFactorAuthenticationSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, MultiFactorAuthenticationSchemaVersion, "Added protected TOTP enrollment, recovery codes, and bounded sign-in challenges.", () => EnsureMultiFactorAuthenticationSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(PrivilegedRoleMfaSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, PrivilegedRoleMfaSchemaVersion, "Added configurable MFA enforcement for privileged access roles.", () => EnsurePrivilegedRoleMfaSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(AccountRecoverySchemaVersion))
            await ApplySchemaVersionAsync(dbContext, AccountRecoverySchemaVersion, "Added expiring account-action tokens and a protected security-email outbox.", () => EnsureAccountRecoverySchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(AccountEmailLookupSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, AccountEmailLookupSchemaVersion, "Added deterministic unique account-email lookup hashes without weakening encrypted email storage.", () => EnsureAccountEmailLookupSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(SecurityEmailActionValiditySchemaVersion))
            await ApplySchemaVersionAsync(dbContext, SecurityEmailActionValiditySchemaVersion, "Prevented delivery of links whose one-use account action expired or was invalidated.", () => EnsureSecurityEmailActionValiditySchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(NamedUserSessionSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, NamedUserSessionSchemaVersion, "Added durable, individually revocable named user sessions.", () => EnsureNamedUserSessionSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(QuickBooksOAuthSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, QuickBooksOAuthSchemaVersion, "Added one-use QuickBooks OAuth authorization state and credential concurrency metadata.", () => EnsureQuickBooksOAuthSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(ExternalEntitySyncSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, ExternalEntitySyncSchemaVersion, "Added durable external-entity links and auditable dry-run or committed synchronization history.", () => EnsureExternalEntitySyncSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(QuickBooksCredentialOperationLeaseSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, QuickBooksCredentialOperationLeaseSchemaVersion, "Added expiring distributed leases for QuickBooks credential-mutating operations.", () => EnsureQuickBooksCredentialOperationLeaseSchemaAsync(dbContext, cancellationToken), cancellationToken);

        if (!applied.Contains(CurrentSchemaVersion))
            await ApplySchemaVersionAsync(dbContext, CurrentSchemaVersion, "Added company-scoped operational account roles for configurable accounting routing.", () => EnsureOperationalAccountRoleSchemaAsync(dbContext, cancellationToken), cancellationToken);
    }

    private static async Task ApplySchemaVersionAsync(BrassLedgerDbContext dbContext, string version, string description, Func<Task> apply, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await apply();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "BrassLedgerSchemaVersions" ("VersionId", "AppliedAtUtc", "ProductVersion", "Description", "Provider") VALUES ({version}, {DateTimeOffset.UtcNow.ToString("O")}, {"2026.08.25"}, {description}, {dbContext.Database.ProviderName ?? "Unknown"});""",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task EnsureW2ReportingMetadataSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "PayrollTimeEntries", "W2ReportingJson", "ALTER TABLE \"PayrollTimeEntries\" ADD COLUMN \"W2ReportingJson\" TEXT NOT NULL DEFAULT '{}';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollEarningLines", "W2ReportingJson", "ALTER TABLE \"PayrollEarningLines\" ADD COLUMN \"W2ReportingJson\" TEXT NOT NULL DEFAULT '{}';", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollTimeEntries" ADD COLUMN IF NOT EXISTS "W2ReportingJson" text NOT NULL DEFAULT '{{}}';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollEarningLines" ADD COLUMN IF NOT EXISTS "W2ReportingJson" text NOT NULL DEFAULT '{{}}';""", cancellationToken);
        }
    }

    private static async Task EnsureAccountingInterchangeBatchSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "AccountingInterchangeBatches" ("Id" TEXT NOT NULL PRIMARY KEY, "CompanyId" TEXT NOT NULL, "ProviderCode" TEXT NOT NULL, "EntityType" TEXT NOT NULL, "FileName" TEXT NOT NULL, "ContentSha256" TEXT NOT NULL, "CommittedImportKey" TEXT NULL, "Status" TEXT NOT NULL, "IsDryRun" INTEGER NOT NULL, "RowCount" INTEGER NOT NULL, "ImportedCount" INTEGER NOT NULL, "DuplicateCount" INTEGER NOT NULL, "RejectedCount" INTEGER NOT NULL, "RejectionJson" TEXT NOT NULL, "ProcessedByUserId" TEXT NULL, "ProcessedAtUtc" TEXT NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_AccountingInterchangeBatches_CompanyId_ProviderCode_EntityType_ProcessedAtUtc" ON "AccountingInterchangeBatches" ("CompanyId", "ProviderCode", "EntityType", "ProcessedAtUtc");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountingInterchangeBatches_CompanyId_CommittedImportKey" ON "AccountingInterchangeBatches" ("CompanyId", "CommittedImportKey");""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "AccountingInterchangeBatches" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "ProviderCode" text NOT NULL, "EntityType" text NOT NULL, "FileName" text NOT NULL, "ContentSha256" text NOT NULL, "CommittedImportKey" text NULL, "Status" text NOT NULL, "IsDryRun" boolean NOT NULL, "RowCount" integer NOT NULL, "ImportedCount" integer NOT NULL, "DuplicateCount" integer NOT NULL, "RejectedCount" integer NOT NULL, "RejectionJson" text NOT NULL, "ProcessedByUserId" uuid NULL, "ProcessedAtUtc" timestamptz NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_AccountingInterchangeBatches_CompanyId_ProviderCode_EntityType_ProcessedAtUtc" ON "AccountingInterchangeBatches" ("CompanyId", "ProviderCode", "EntityType", "ProcessedAtUtc");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountingInterchangeBatches_CompanyId_CommittedImportKey" ON "AccountingInterchangeBatches" ("CompanyId", "CommittedImportKey");""", cancellationToken);
        }
    }

    private static async Task EnsureMultiFactorAuthenticationSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "Users", "MfaEnabled", "ALTER TABLE \"Users\" ADD COLUMN \"MfaEnabled\" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "MfaSecret", "ALTER TABLE \"Users\" ADD COLUMN \"MfaSecret\" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "MfaEnrolledAtUtc", "ALTER TABLE \"Users\" ADD COLUMN \"MfaEnrolledAtUtc\" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "MfaLastAcceptedTimeStep", "ALTER TABLE \"Users\" ADD COLUMN \"MfaLastAcceptedTimeStep\" INTEGER NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "MfaFailedAttemptCount", "ALTER TABLE \"Users\" ADD COLUMN \"MfaFailedAttemptCount\" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "MfaLockoutEndUtc", "ALTER TABLE \"Users\" ADD COLUMN \"MfaLockoutEndUtc\" TEXT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "MfaRecoveryCodes" ("Id" TEXT NOT NULL PRIMARY KEY, "UserId" TEXT NOT NULL, "CodeHash" TEXT NOT NULL, "CreatedAtUtc" TEXT NOT NULL, "UsedAtUtc" TEXT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_MfaRecoveryCodes_CodeHash" ON "MfaRecoveryCodes" ("CodeHash");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_MfaRecoveryCodes_UserId_UsedAtUtc" ON "MfaRecoveryCodes" ("UserId", "UsedAtUtc");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "MfaSignInChallenges" ("Id" TEXT NOT NULL PRIMARY KEY, "UserId" TEXT NOT NULL, "CompanyId" TEXT NOT NULL, "TokenHash" TEXT NOT NULL, "SecurityStamp" TEXT NOT NULL, "CreatedAtUtc" TEXT NOT NULL, "ExpiresAtUtc" TEXT NOT NULL, "ConsumedAtUtc" TEXT NULL, "FailedAttemptCount" INTEGER NOT NULL, "IpAddress" TEXT NOT NULL, "UserAgent" TEXT NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_MfaSignInChallenges_TokenHash" ON "MfaSignInChallenges" ("TokenHash");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_MfaSignInChallenges_UserId_ExpiresAtUtc" ON "MfaSignInChallenges" ("UserId", "ExpiresAtUtc");""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MfaEnabled" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MfaSecret" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MfaEnrolledAtUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MfaLastAcceptedTimeStep" bigint NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MfaFailedAttemptCount" integer NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MfaLockoutEndUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "MfaRecoveryCodes" ("Id" uuid NOT NULL PRIMARY KEY, "UserId" uuid NOT NULL, "CodeHash" text NOT NULL, "CreatedAtUtc" timestamptz NOT NULL, "UsedAtUtc" timestamptz NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_MfaRecoveryCodes_CodeHash" ON "MfaRecoveryCodes" ("CodeHash");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_MfaRecoveryCodes_UserId_UsedAtUtc" ON "MfaRecoveryCodes" ("UserId", "UsedAtUtc");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "MfaSignInChallenges" ("Id" uuid NOT NULL PRIMARY KEY, "UserId" uuid NOT NULL, "CompanyId" uuid NOT NULL, "TokenHash" text NOT NULL, "SecurityStamp" text NOT NULL, "CreatedAtUtc" timestamptz NOT NULL, "ExpiresAtUtc" timestamptz NOT NULL, "ConsumedAtUtc" timestamptz NULL, "FailedAttemptCount" integer NOT NULL, "IpAddress" text NOT NULL, "UserAgent" text NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_MfaSignInChallenges_TokenHash" ON "MfaSignInChallenges" ("TokenHash");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_MfaSignInChallenges_UserId_ExpiresAtUtc" ON "MfaSignInChallenges" ("UserId", "ExpiresAtUtc");""", cancellationToken);
        }
    }

    private static async Task EnsurePrivilegedRoleMfaSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "AccessRoles", "RequiresMfa", "ALTER TABLE \"AccessRoles\" ADD COLUMN \"RequiresMfa\" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""UPDATE "AccessRoles" SET "RequiresMfa" = 1 WHERE "Name" IN ('Administrator', 'Owner/CEO');""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "AccessRoles" ADD COLUMN IF NOT EXISTS "RequiresMfa" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""UPDATE "AccessRoles" SET "RequiresMfa" = true WHERE "Name" IN ('Administrator', 'Owner/CEO');""", cancellationToken);
        }
    }

    private static async Task EnsureAccountRecoverySchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "Users", "EmailConfirmedAtUtc", "ALTER TABLE \"Users\" ADD COLUMN \"EmailConfirmedAtUtc\" TEXT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "AccountActionTokens" ("Id" TEXT NOT NULL PRIMARY KEY, "UserId" TEXT NOT NULL, "CompanyId" TEXT NULL, "Purpose" TEXT NOT NULL, "TokenHash" TEXT NOT NULL, "SecurityStamp" TEXT NOT NULL, "CreatedAtUtc" TEXT NOT NULL, "ExpiresAtUtc" TEXT NOT NULL, "ConsumedAtUtc" TEXT NULL, "CreatedByUserId" TEXT NULL, "RequestedIpAddress" TEXT NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountActionTokens_TokenHash" ON "AccountActionTokens" ("TokenHash");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_AccountActionTokens_UserId_Purpose_ExpiresAtUtc" ON "AccountActionTokens" ("UserId", "Purpose", "ExpiresAtUtc");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "SecurityEmailOutboxMessages" ("Id" TEXT NOT NULL PRIMARY KEY, "AccountActionTokenId" TEXT NOT NULL, "RecipientEmail" TEXT NOT NULL, "Subject" TEXT NOT NULL, "Body" TEXT NOT NULL, "Status" TEXT NOT NULL, "AttemptCount" INTEGER NOT NULL, "CreatedAtUtc" TEXT NOT NULL, "NextAttemptAtUtc" TEXT NOT NULL, "LeaseExpiresAtUtc" TEXT NULL, "DeliveredAtUtc" TEXT NULL, "LastError" TEXT NOT NULL, "ProviderMessageId" TEXT NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_SecurityEmailOutboxMessages_Status_NextAttemptAtUtc_LeaseExpiresAtUtc" ON "SecurityEmailOutboxMessages" ("Status", "NextAttemptAtUtc", "LeaseExpiresAtUtc");""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "EmailConfirmedAtUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "AccountActionTokens" ("Id" uuid NOT NULL PRIMARY KEY, "UserId" uuid NOT NULL, "CompanyId" uuid NULL, "Purpose" text NOT NULL, "TokenHash" text NOT NULL, "SecurityStamp" text NOT NULL, "CreatedAtUtc" timestamptz NOT NULL, "ExpiresAtUtc" timestamptz NOT NULL, "ConsumedAtUtc" timestamptz NULL, "CreatedByUserId" uuid NULL, "RequestedIpAddress" text NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountActionTokens_TokenHash" ON "AccountActionTokens" ("TokenHash");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_AccountActionTokens_UserId_Purpose_ExpiresAtUtc" ON "AccountActionTokens" ("UserId", "Purpose", "ExpiresAtUtc");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "SecurityEmailOutboxMessages" ("Id" uuid NOT NULL PRIMARY KEY, "AccountActionTokenId" uuid NOT NULL, "RecipientEmail" text NOT NULL, "Subject" text NOT NULL, "Body" text NOT NULL, "Status" text NOT NULL, "AttemptCount" integer NOT NULL, "CreatedAtUtc" timestamptz NOT NULL, "NextAttemptAtUtc" timestamptz NOT NULL, "LeaseExpiresAtUtc" timestamptz NULL, "DeliveredAtUtc" timestamptz NULL, "LastError" text NOT NULL, "ProviderMessageId" text NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_SecurityEmailOutboxMessages_Status_NextAttemptAtUtc_LeaseExpiresAtUtc" ON "SecurityEmailOutboxMessages" ("Status", "NextAttemptAtUtc", "LeaseExpiresAtUtc");""", cancellationToken);
        }
    }

    private static async Task EnsureAccountEmailLookupSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "Users", "EmailLookupHash", "ALTER TABLE \"Users\" ADD COLUMN \"EmailLookupHash\" TEXT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_EmailLookupHash" ON "Users" ("EmailLookupHash");""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "EmailLookupHash" text NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_EmailLookupHash" ON "Users" ("EmailLookupHash");""", cancellationToken);
        }
    }

    private static async Task EnsureSecurityEmailActionValiditySchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "SecurityEmailOutboxMessages", "RequiresUsableAction", "ALTER TABLE \"SecurityEmailOutboxMessages\" ADD COLUMN \"RequiresUsableAction\" INTEGER NOT NULL DEFAULT 1;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""UPDATE "SecurityEmailOutboxMessages" SET "RequiresUsableAction" = 0 WHERE "Subject" = 'Your BrassLedger password was changed';""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "SecurityEmailOutboxMessages" ADD COLUMN IF NOT EXISTS "RequiresUsableAction" boolean NOT NULL DEFAULT true;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""UPDATE "SecurityEmailOutboxMessages" SET "RequiresUsableAction" = false WHERE "Subject" = 'Your BrassLedger password was changed';""", cancellationToken);
        }
    }

    private static async Task EnsureNamedUserSessionSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "UserSessions" ("Id" TEXT NOT NULL PRIMARY KEY, "UserId" TEXT NOT NULL, "SecurityStamp" TEXT NOT NULL, "AuthenticationMethod" TEXT NOT NULL, "CreatedAtUtc" TEXT NOT NULL, "LastSeenAtUtc" TEXT NOT NULL, "ExpiresAtUtc" TEXT NOT NULL, "RevokedAtUtc" TEXT NULL, "IpAddress" TEXT NOT NULL, "UserAgent" TEXT NOT NULL, CONSTRAINT "FK_UserSessions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_UserSessions_UserId_RevokedAtUtc_ExpiresAtUtc" ON "UserSessions" ("UserId", "RevokedAtUtc", "ExpiresAtUtc");""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "UserSessions" ("Id" uuid NOT NULL PRIMARY KEY, "UserId" uuid NOT NULL, "SecurityStamp" text NOT NULL, "AuthenticationMethod" text NOT NULL, "CreatedAtUtc" timestamptz NOT NULL, "LastSeenAtUtc" timestamptz NOT NULL, "ExpiresAtUtc" timestamptz NOT NULL, "RevokedAtUtc" timestamptz NULL, "IpAddress" text NOT NULL, "UserAgent" text NOT NULL, CONSTRAINT "FK_UserSessions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_UserSessions_UserId_RevokedAtUtc_ExpiresAtUtc" ON "UserSessions" ("UserId", "RevokedAtUtc", "ExpiresAtUtc");""", cancellationToken);
        }
    }

    private static async Task EnsureQuickBooksOAuthSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "IntegrationConnections", "CredentialVersion", "ALTER TABLE \"IntegrationConnections\" ADD COLUMN \"CredentialVersion\" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "OAuthAuthorizationAttempts" ("Id" TEXT NOT NULL PRIMARY KEY, "CompanyId" TEXT NOT NULL, "UserId" TEXT NOT NULL, "ConnectionId" TEXT NULL, "ProviderCode" TEXT NOT NULL, "ConnectionName" TEXT NOT NULL, "Environment" TEXT NOT NULL, "StateHash" TEXT NOT NULL, "CreatedAtUtc" TEXT NOT NULL, "ExpiresAtUtc" TEXT NOT NULL, "ConsumedAtUtc" TEXT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_OAuthAuthorizationAttempts_StateHash" ON "OAuthAuthorizationAttempts" ("StateHash");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_OAuthAuthorizationAttempts_CompanyId_UserId_ExpiresAtUtc" ON "OAuthAuthorizationAttempts" ("CompanyId", "UserId", "ExpiresAtUtc");""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "IntegrationConnections" ADD COLUMN IF NOT EXISTS "CredentialVersion" integer NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "OAuthAuthorizationAttempts" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "UserId" uuid NOT NULL, "ConnectionId" uuid NULL, "ProviderCode" text NOT NULL, "ConnectionName" text NOT NULL, "Environment" text NOT NULL, "StateHash" text NOT NULL, "CreatedAtUtc" timestamptz NOT NULL, "ExpiresAtUtc" timestamptz NOT NULL, "ConsumedAtUtc" timestamptz NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_OAuthAuthorizationAttempts_StateHash" ON "OAuthAuthorizationAttempts" ("StateHash");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_OAuthAuthorizationAttempts_CompanyId_UserId_ExpiresAtUtc" ON "OAuthAuthorizationAttempts" ("CompanyId", "UserId", "ExpiresAtUtc");""", cancellationToken);
        }
    }

    private static async Task EnsureExternalEntitySyncSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "ExternalEntityLinks" ("Id" TEXT NOT NULL PRIMARY KEY, "CompanyId" TEXT NOT NULL, "IntegrationConnectionId" TEXT NOT NULL, "ProviderCode" TEXT NOT NULL, "EntityType" TEXT NOT NULL, "ProviderEntityId" TEXT NOT NULL, "LocalEntityId" TEXT NOT NULL, "ProviderSyncToken" TEXT NOT NULL, "LastRemoteFingerprint" TEXT NOT NULL, "LastLocalFingerprint" TEXT NOT NULL, "LastSynchronizedAtUtc" TEXT NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExternalEntityLinks_IntegrationConnectionId_EntityType_ProviderEntityId" ON "ExternalEntityLinks" ("IntegrationConnectionId", "EntityType", "ProviderEntityId");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExternalEntityLinks_IntegrationConnectionId_EntityType_LocalEntityId" ON "ExternalEntityLinks" ("IntegrationConnectionId", "EntityType", "LocalEntityId");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "IntegrationSyncRuns" ("Id" TEXT NOT NULL PRIMARY KEY, "CompanyId" TEXT NOT NULL, "IntegrationConnectionId" TEXT NOT NULL, "ProviderCode" TEXT NOT NULL, "EntityType" TEXT NOT NULL, "Direction" TEXT NOT NULL, "IsDryRun" INTEGER NOT NULL, "Status" TEXT NOT NULL, "FetchedCount" INTEGER NOT NULL, "CreatedCount" INTEGER NOT NULL, "UpdatedCount" INTEGER NOT NULL, "UnchangedCount" INTEGER NOT NULL, "ConflictCount" INTEGER NOT NULL, "RejectedCount" INTEGER NOT NULL, "SnapshotSha256" TEXT NOT NULL, "DetailJson" TEXT NOT NULL, "InitiatedByUserId" TEXT NULL, "StartedAtUtc" TEXT NOT NULL, "CompletedAtUtc" TEXT NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_IntegrationSyncRuns_CompanyId_IntegrationConnectionId_CompletedAtUtc" ON "IntegrationSyncRuns" ("CompanyId", "IntegrationConnectionId", "CompletedAtUtc");""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "ExternalEntityLinks" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "IntegrationConnectionId" uuid NOT NULL, "ProviderCode" text NOT NULL, "EntityType" text NOT NULL, "ProviderEntityId" text NOT NULL, "LocalEntityId" uuid NOT NULL, "ProviderSyncToken" text NOT NULL, "LastRemoteFingerprint" text NOT NULL, "LastLocalFingerprint" text NOT NULL, "LastSynchronizedAtUtc" timestamptz NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExternalEntityLinks_IntegrationConnectionId_EntityType_ProviderEntityId" ON "ExternalEntityLinks" ("IntegrationConnectionId", "EntityType", "ProviderEntityId");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExternalEntityLinks_IntegrationConnectionId_EntityType_LocalEntityId" ON "ExternalEntityLinks" ("IntegrationConnectionId", "EntityType", "LocalEntityId");""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "IntegrationSyncRuns" ("Id" uuid NOT NULL PRIMARY KEY, "CompanyId" uuid NOT NULL, "IntegrationConnectionId" uuid NOT NULL, "ProviderCode" text NOT NULL, "EntityType" text NOT NULL, "Direction" text NOT NULL, "IsDryRun" boolean NOT NULL, "Status" text NOT NULL, "FetchedCount" integer NOT NULL, "CreatedCount" integer NOT NULL, "UpdatedCount" integer NOT NULL, "UnchangedCount" integer NOT NULL, "ConflictCount" integer NOT NULL, "RejectedCount" integer NOT NULL, "SnapshotSha256" text NOT NULL, "DetailJson" text NOT NULL, "InitiatedByUserId" uuid NULL, "StartedAtUtc" timestamptz NOT NULL, "CompletedAtUtc" timestamptz NOT NULL);""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_IntegrationSyncRuns_CompanyId_IntegrationConnectionId_CompletedAtUtc" ON "IntegrationSyncRuns" ("CompanyId", "IntegrationConnectionId", "CompletedAtUtc");""", cancellationToken);
        }
    }

    private static async Task EnsureQuickBooksCredentialOperationLeaseSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "IntegrationConnections", "CredentialOperationLeaseId", "ALTER TABLE \"IntegrationConnections\" ADD COLUMN \"CredentialOperationLeaseId\" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "IntegrationConnections", "CredentialOperation", "ALTER TABLE \"IntegrationConnections\" ADD COLUMN \"CredentialOperation\" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "IntegrationConnections", "CredentialOperationLeaseExpiresAtUtc", "ALTER TABLE \"IntegrationConnections\" ADD COLUMN \"CredentialOperationLeaseExpiresAtUtc\" TEXT NULL;", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "IntegrationConnections" ADD COLUMN IF NOT EXISTS "CredentialOperationLeaseId" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "IntegrationConnections" ADD COLUMN IF NOT EXISTS "CredentialOperation" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "IntegrationConnections" ADD COLUMN IF NOT EXISTS "CredentialOperationLeaseExpiresAtUtc" timestamptz NULL;""", cancellationToken);
        }
    }

    private static async Task EnsureOperationalAccountRoleSchemaAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "Accounts", "OperationalRole", "ALTER TABLE \"Accounts\" ADD COLUMN \"OperationalRole\" TEXT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""
                UPDATE "Accounts" AS target
                SET "OperationalRole" = CASE "Number"
                    WHEN '1000' THEN 'OperatingCash'
                    WHEN '1010' THEN 'PayrollClearing'
                    WHEN '1050' THEN 'BankTransferClearing'
                    WHEN '1100' THEN 'AccountsReceivable'
                    WHEN '1110' THEN 'RetainageReceivable'
                    WHEN '1200' THEN 'InventoryAsset'
                    WHEN '1300' THEN 'VendorAdvances'
                    WHEN '2000' THEN 'AccountsPayable'
                    WHEN '2100' THEN 'SalesTaxPayable'
                    WHEN '2150' THEN 'CustomerDeposits'
                    WHEN '2200' THEN 'PayrollLiabilities'
                    WHEN '3000' THEN 'OwnerEquity'
                    WHEN '4000' THEN 'DefaultRevenue'
                    WHEN '4300' THEN 'ForeignExchangeGain'
                    WHEN '5100' THEN 'CostOfGoodsSold'
                    WHEN '6100' THEN 'PayrollExpense'
                    WHEN '6300' THEN 'ForeignExchangeLoss'
                    ELSE NULL END
                WHERE "OperationalRole" IS NULL
                  AND "Number" IN ('1000','1010','1050','1100','1110','1200','1300','2000','2100','2150','2200','3000','4000','4300','5100','6100','6300')
                  AND (("Number" IN ('1000','1010','1050') AND "Type" = 'Asset' AND "IsControlAccount" = 0)
                    OR ("Number" IN ('1100','1110','1200','1300') AND "Type" = 'Asset' AND "IsControlAccount" = 1)
                    OR ("Number" IN ('2000','2100','2150','2200') AND "Type" = 'Liability' AND "IsControlAccount" = 1)
                    OR ("Number" = '3000' AND "Type" = 'Equity' AND "IsControlAccount" = 0)
                    OR ("Number" IN ('4000','4300') AND "Type" = 'Revenue' AND "IsControlAccount" = 0)
                    OR ("Number" IN ('5100','6100','6300') AND "Type" = 'Expense' AND "IsControlAccount" = 0))
                  AND NOT EXISTS (
                      SELECT 1 FROM "Accounts" AS existing
                      WHERE existing."CompanyId" = target."CompanyId"
                        AND existing."OperationalRole" = CASE target."Number"
                            WHEN '1000' THEN 'OperatingCash' WHEN '1010' THEN 'PayrollClearing' WHEN '1050' THEN 'BankTransferClearing'
                            WHEN '1100' THEN 'AccountsReceivable' WHEN '1110' THEN 'RetainageReceivable' WHEN '1200' THEN 'InventoryAsset' WHEN '1300' THEN 'VendorAdvances'
                            WHEN '2000' THEN 'AccountsPayable' WHEN '2100' THEN 'SalesTaxPayable' WHEN '2150' THEN 'CustomerDeposits'
                            WHEN '2200' THEN 'PayrollLiabilities' WHEN '3000' THEN 'OwnerEquity' WHEN '4000' THEN 'DefaultRevenue'
                            WHEN '4300' THEN 'ForeignExchangeGain' WHEN '5100' THEN 'CostOfGoodsSold' WHEN '6100' THEN 'PayrollExpense'
                            WHEN '6300' THEN 'ForeignExchangeLoss' ELSE NULL END);
                """, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Accounts_CompanyId_OperationalRole" ON "Accounts" ("CompanyId", "OperationalRole");""", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Accounts" ADD COLUMN IF NOT EXISTS "OperationalRole" text NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""
                UPDATE "Accounts" AS target
                SET "OperationalRole" = CASE "Number"
                    WHEN '1000' THEN 'OperatingCash'
                    WHEN '1010' THEN 'PayrollClearing'
                    WHEN '1050' THEN 'BankTransferClearing'
                    WHEN '1100' THEN 'AccountsReceivable'
                    WHEN '1110' THEN 'RetainageReceivable'
                    WHEN '1200' THEN 'InventoryAsset'
                    WHEN '1300' THEN 'VendorAdvances'
                    WHEN '2000' THEN 'AccountsPayable'
                    WHEN '2100' THEN 'SalesTaxPayable'
                    WHEN '2150' THEN 'CustomerDeposits'
                    WHEN '2200' THEN 'PayrollLiabilities'
                    WHEN '3000' THEN 'OwnerEquity'
                    WHEN '4000' THEN 'DefaultRevenue'
                    WHEN '4300' THEN 'ForeignExchangeGain'
                    WHEN '5100' THEN 'CostOfGoodsSold'
                    WHEN '6100' THEN 'PayrollExpense'
                    WHEN '6300' THEN 'ForeignExchangeLoss'
                    ELSE NULL END
                WHERE "OperationalRole" IS NULL
                  AND "Number" IN ('1000','1010','1050','1100','1110','1200','1300','2000','2100','2150','2200','3000','4000','4300','5100','6100','6300')
                  AND (("Number" IN ('1000','1010','1050') AND "Type" = 'Asset' AND "IsControlAccount" = false)
                    OR ("Number" IN ('1100','1110','1200','1300') AND "Type" = 'Asset' AND "IsControlAccount" = true)
                    OR ("Number" IN ('2000','2100','2150','2200') AND "Type" = 'Liability' AND "IsControlAccount" = true)
                    OR ("Number" = '3000' AND "Type" = 'Equity' AND "IsControlAccount" = false)
                    OR ("Number" IN ('4000','4300') AND "Type" = 'Revenue' AND "IsControlAccount" = false)
                    OR ("Number" IN ('5100','6100','6300') AND "Type" = 'Expense' AND "IsControlAccount" = false))
                  AND NOT EXISTS (
                      SELECT 1 FROM "Accounts" AS existing
                      WHERE existing."CompanyId" = target."CompanyId"
                        AND existing."OperationalRole" = CASE target."Number"
                            WHEN '1000' THEN 'OperatingCash' WHEN '1010' THEN 'PayrollClearing' WHEN '1050' THEN 'BankTransferClearing'
                            WHEN '1100' THEN 'AccountsReceivable' WHEN '1110' THEN 'RetainageReceivable' WHEN '1200' THEN 'InventoryAsset' WHEN '1300' THEN 'VendorAdvances'
                            WHEN '2000' THEN 'AccountsPayable' WHEN '2100' THEN 'SalesTaxPayable' WHEN '2150' THEN 'CustomerDeposits'
                            WHEN '2200' THEN 'PayrollLiabilities' WHEN '3000' THEN 'OwnerEquity' WHEN '4000' THEN 'DefaultRevenue'
                            WHEN '4300' THEN 'ForeignExchangeGain' WHEN '5100' THEN 'CostOfGoodsSold' WHEN '6100' THEN 'PayrollExpense'
                            WHEN '6300' THEN 'ForeignExchangeLoss' ELSE NULL END);
                """, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Accounts_CompanyId_OperationalRole" ON "Accounts" ("CompanyId", "OperationalRole");""", cancellationToken);
        }
    }

    private static async Task EnsureAccountEmailLookupHashesAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        var users = await dbContext.Users.Where(user => user.EmailLookupHash == null).ToListAsync(cancellationToken);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var existing in await dbContext.Users.AsNoTracking().Where(user => user.EmailLookupHash != null).Select(user => new { user.UserName, user.EmailLookupHash }).ToListAsync(cancellationToken))
            hashes[existing.EmailLookupHash!] = existing.UserName;

        foreach (var user in users)
        {
            if (!AccountEmailIdentity.TryNormalize(user.Email, out _, out var hash)) continue;
            if (hashes.TryGetValue(hash, out var otherUserName))
                throw new InvalidOperationException($"Account email uniqueness cannot be established because operators '{otherUserName}' and '{user.UserName}' have the same normalized email address. Resolve the duplicate before upgrading.");
            hashes.Add(hash, user.UserName);
            user.EmailLookupHash = hash;
        }

        if (users.Any(user => user.EmailLookupHash is not null)) await dbContext.SaveChangesAsync(cancellationToken);
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
            await EnsureSqliteColumnAsync(dbContext, "Employees", "AddressCity", @"ALTER TABLE ""Employees"" ADD COLUMN ""AddressCity"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "AddressState", @"ALTER TABLE ""Employees"" ADD COLUMN ""AddressState"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "PostalCode", @"ALTER TABLE ""Employees"" ADD COLUMN ""PostalCode"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "SocialSecurityNumber", @"ALTER TABLE ""Employees"" ADD COLUMN ""SocialSecurityNumber"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "BankRoutingNumber", @"ALTER TABLE ""Employees"" ADD COLUMN ""BankRoutingNumber"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "BankAccountNumber", @"ALTER TABLE ""Employees"" ADD COLUMN ""BankAccountNumber"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "BankAccountType", @"ALTER TABLE ""Employees"" ADD COLUMN ""BankAccountType"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "DirectDepositEnabled", @"ALTER TABLE ""Employees"" ADD COLUMN ""DirectDepositEnabled"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "DirectDepositAuthorizationOn", @"ALTER TABLE ""Employees"" ADD COLUMN ""DirectDepositAuthorizationOn"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Employees", "DirectDepositAuthorizationReference", @"ALTER TABLE ""Employees"" ADD COLUMN ""DirectDepositAuthorizationReference"" TEXT NOT NULL DEFAULT '';", cancellationToken);
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
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTimeEntries"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollTimecardId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""WorkDate"" TEXT NOT NULL, ""EarningCode"" TEXT NOT NULL, ""EarningType"" TEXT NOT NULL, ""Hours"" TEXT NOT NULL, ""Rate"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""IsTaxable"" INTEGER NOT NULL, ""WorkState"" TEXT NOT NULL, ""WorkCounty"" TEXT NOT NULL, ""WorkCity"" TEXT NOT NULL, ""WorkSchoolDistrict"" TEXT NOT NULL, ""ProjectJobId"" TEXT NULL, ""Notes"" TEXT NOT NULL, ""W2ReportingJson"" TEXT NOT NULL DEFAULT '{{}}');", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollTimeEntries", "W2ReportingJson", @"ALTER TABLE ""PayrollTimeEntries"" ADD COLUMN ""W2ReportingJson"" TEXT NOT NULL DEFAULT '{}';", cancellationToken);
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
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "EmployerBenefitContributions", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""EmployerBenefitContributions"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRuns", "ConcurrencyToken", @"ALTER TABLE ""PayrollRuns"" ADD COLUMN ""ConcurrencyToken"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollRuns_CompanyId_Reference"" ON ""PayrollRuns"" (""CompanyId"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollRunEmployeeLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollRunId"" TEXT NOT NULL, ""EmployeeId"" TEXT NOT NULL, ""WorkState"" TEXT NOT NULL, ""FilingStatus"" TEXT NOT NULL, ""GrossPay"" TEXT NOT NULL, ""PreTaxDeductions"" TEXT NOT NULL, ""EmployeeWithholdings"" TEXT NOT NULL, ""PostTaxDeductions"" TEXT NOT NULL, ""EmployerPayrollTaxes"" TEXT NOT NULL, ""NetPay"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "PayrollFrequency", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""PayrollFrequency"" TEXT NOT NULL DEFAULT 'Biweekly';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollRunEmployeeLines_PayrollRunId_EmployeeId"" ON ""PayrollRunEmployeeLines"" (""PayrollRunId"", ""EmployeeId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollJurisdictionRules"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""ResidenceJurisdiction"" TEXT NOT NULL, ""WorkJurisdiction"" TEXT NOT NULL, ""ExemptWorkWithholding"" INTEGER NOT NULL, ""ResidentCreditRate"" TEXT NOT NULL, ""IsActive"" INTEGER NOT NULL, ""Notes"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollJurisdictionRules_CompanyId_ResidenceJurisdiction_WorkJurisdiction"" ON ""PayrollJurisdictionRules"" (""CompanyId"", ""ResidenceJurisdiction"", ""WorkJurisdiction"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDeductionPlans"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""Code"" TEXT NOT NULL, ""Name"" TEXT NOT NULL, ""Category"" TEXT NOT NULL, ""CalculationMethod"" TEXT NOT NULL, ""DefaultEmployeeValue"" TEXT NOT NULL, ""DefaultEmployerValue"" TEXT NOT NULL, ""IsPreTax"" INTEGER NOT NULL, ""ExemptFromFederalIncomeTax"" INTEGER NOT NULL, ""ExemptFromFica"" INTEGER NOT NULL, ""ExemptFromFuta"" INTEGER NOT NULL, ""ReducesDisposableEarnings"" INTEGER NOT NULL, ""LiabilityAccountNumber"" TEXT NOT NULL, ""Priority"" INTEGER NOT NULL, ""EmployeeLimitPerPay"" TEXT NULL, ""EmployeeAnnualLimit"" TEXT NULL, ""MinimumNetPay"" TEXT NOT NULL, ""LimitRuleCode"" TEXT NOT NULL, ""LimitRuleJson"" TEXT NOT NULL, ""OfficialSourceUrl"" TEXT NOT NULL, ""SourceRetrievedOn"" TEXT NULL, ""EffectiveOn"" TEXT NOT NULL, ""ExpiresOn"" TEXT NULL, ""IsActive"" INTEGER NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDeductionPlans_CompanyId_Code"" ON ""PayrollDeductionPlans"" (""CompanyId"", ""Code"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""EmployeePayrollDeductionElections"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""EmployeeId"" TEXT NOT NULL, ""PayrollDeductionPlanId"" TEXT NOT NULL, ""EmployeeValueOverride"" TEXT NULL, ""EmployerValueOverride"" TEXT NULL, ""EmployeeAnnualLimitOverride"" TEXT NULL, ""OrderDetailsJson"" TEXT NOT NULL, ""EffectiveOn"" TEXT NOT NULL, ""ExpiresOn"" TEXT NULL, ""IsActive"" INTEGER NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_EmployeePayrollDeductionElections_CompanyId_EmployeeId_PayrollDeductionPlanId_EffectiveOn"" ON ""EmployeePayrollDeductionElections"" (""CompanyId"", ""EmployeeId"", ""PayrollDeductionPlanId"", ""EffectiveOn"");", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "WorkCity", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""WorkCity"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "ResidenceState", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""ResidenceState"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "ResidenceCity", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""ResidenceCity"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "TaxableWages", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""TaxableWages"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "YearToDateGrossBefore", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""YearToDateGrossBefore"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "YearToDateGrossAfter", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""YearToDateGrossAfter"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "CalculationTraceJson", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""CalculationTraceJson"" TEXT NOT NULL DEFAULT '[]';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollRunEmployeeLines", "EmployerBenefitContributions", @"ALTER TABLE ""PayrollRunEmployeeLines"" ADD COLUMN ""EmployerBenefitContributions"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollEarningLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" TEXT NOT NULL, ""PayrollTimeEntryId"" TEXT NULL, ""Sequence"" INTEGER NOT NULL, ""EarningCode"" TEXT NOT NULL, ""EarningType"" TEXT NOT NULL, ""Hours"" TEXT NOT NULL, ""Rate"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""IsTaxable"" INTEGER NOT NULL, ""WorkedOn"" TEXT NULL, ""WorkState"" TEXT NOT NULL, ""WorkCounty"" TEXT NOT NULL, ""WorkCity"" TEXT NOT NULL, ""WorkSchoolDistrict"" TEXT NOT NULL, ""W2ReportingJson"" TEXT NOT NULL DEFAULT '{{}}');", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollEarningLines", "PayrollTimeEntryId", @"ALTER TABLE ""PayrollEarningLines"" ADD COLUMN ""PayrollTimeEntryId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollEarningLines", "W2ReportingJson", @"ALTER TABLE ""PayrollEarningLines"" ADD COLUMN ""W2ReportingJson"" TEXT NOT NULL DEFAULT '{}';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollEarningLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollEarningLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_PayrollEarningLines_PayrollTimeEntryId"";", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollEarningLines_PayrollTimeEntryId"" ON ""PayrollEarningLines"" (""PayrollTimeEntryId"") WHERE ""PayrollTimeEntryId"" IS NOT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDeductionLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""DeductionCode"" TEXT NOT NULL, ""DeductionType"" TEXT NOT NULL, ""EmployeeAmount"" TEXT NOT NULL, ""EmployerAmount"" TEXT NOT NULL, ""IsPreTax"" INTEGER NOT NULL, ""LiabilityAccountNumber"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "ExemptFromFederalIncomeTax", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""ExemptFromFederalIncomeTax"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "ExemptFromFica", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""ExemptFromFica"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "ExemptFromFuta", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""ExemptFromFuta"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "PayrollDeductionPlanId", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""PayrollDeductionPlanId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "EmployeePayrollDeductionElectionId", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""EmployeePayrollDeductionElectionId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "RequestedEmployeeAmount", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""RequestedEmployeeAmount"" TEXT NOT NULL DEFAULT '0';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "LimitApplied", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""LimitApplied"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "LimitRuleCode", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""LimitRuleCode"" TEXT NOT NULL DEFAULT 'None';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDeductionLines", "CalculationTraceJson", @"ALTER TABLE ""PayrollDeductionLines"" ADD COLUMN ""CalculationTraceJson"" TEXT NOT NULL DEFAULT '{}';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDeductionLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollDeductionLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTaxLines"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""ObligationCode"" TEXT NOT NULL, ""JurisdictionCode"" TEXT NOT NULL, ""JurisdictionName"" TEXT NOT NULL, ""TaxType"" TEXT NOT NULL, ""TaxableWages"" TEXT NOT NULL, ""YearToDateTaxableWagesBefore"" TEXT NOT NULL, ""EmployeeAmount"" TEXT NOT NULL, ""EmployerAmount"" TEXT NOT NULL, ""TaxRuleSetId"" TEXT NULL, ""TaxContentPackageId"" TEXT NULL, ""ContentVersion"" TEXT NOT NULL, ""Source"" TEXT NOT NULL, ""CalculationTraceJson"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollTaxLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollTaxLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDepositScheduleConfigurations"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""JurisdictionCode"" TEXT NOT NULL, ""ReturnFormCode"" TEXT NOT NULL, ""TaxYear"" INTEGER NOT NULL, ""ScheduleType"" TEXT NOT NULL, ""LookbackLiability"" TEXT NOT NULL, ""LookbackPeriodStart"" TEXT NOT NULL, ""LookbackPeriodEnd"" TEXT NOT NULL, ""MonthlyThreshold"" TEXT NOT NULL, ""NextDayThreshold"" TEXT NOT NULL, ""SmallLiabilityThreshold"" TEXT NOT NULL, ""SmallLiabilityElectionQuartersJson"" TEXT NOT NULL, ""LegalHolidaysJson"" TEXT NOT NULL, ""OfficialRulesUrl"" TEXT NOT NULL, ""OfficialCalendarUrl"" TEXT NOT NULL, ""SourceRetrievedOn"" TEXT NOT NULL, ""ReviewNotes"" TEXT NOT NULL, ""IsApproved"" INTEGER NOT NULL, ""ApprovedByUserId"" TEXT NULL, ""ApprovedAtUtc"" TEXT NULL, ""IsActive"" INTEGER NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDepositScheduleConfigurations", "SmallLiabilityThreshold", @"ALTER TABLE ""PayrollDepositScheduleConfigurations"" ADD COLUMN ""SmallLiabilityThreshold"" TEXT NOT NULL DEFAULT '2500';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollDepositScheduleConfigurations", "SmallLiabilityElectionQuartersJson", @"ALTER TABLE ""PayrollDepositScheduleConfigurations"" ADD COLUMN ""SmallLiabilityElectionQuartersJson"" TEXT NOT NULL DEFAULT '[]';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDepositScheduleConfigurations_CompanyId_JurisdictionCode_ReturnFormCode_TaxYear"" ON ""PayrollDepositScheduleConfigurations"" (""CompanyId"", ""JurisdictionCode"", ""ReturnFormCode"", ""TaxYear"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDisasterReliefConfigurations"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""AnnouncementCode"" TEXT NOT NULL, ""DisasterName"" TEXT NOT NULL, ""FemaDeclarationNumber"" TEXT NOT NULL, ""CoveredAreasJson"" TEXT NOT NULL, ""AffectedTaxpayerBasis"" TEXT NOT NULL, ""EligibilityEvidenceReference"" TEXT NOT NULL, ""ReliefActionsJson"" TEXT NOT NULL, ""OfficialSourceUrl"" TEXT NOT NULL, ""SourceRetrievedOn"" TEXT NOT NULL, ""ReviewNotes"" TEXT NOT NULL, ""IsApproved"" INTEGER NOT NULL, ""ApprovedByUserId"" TEXT NULL, ""ApprovedAtUtc"" TEXT NULL, ""IsActive"" INTEGER NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDisasterReliefConfigurations_CompanyId_AnnouncementCode"" ON ""PayrollDisasterReliefConfigurations"" (""CompanyId"", ""AnnouncementCode"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollLiabilities"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""PayrollRunId"" TEXT NOT NULL, ""PayrollRunEmployeeLineId"" TEXT NOT NULL, ""SourceType"" TEXT NOT NULL, ""SourceLineId"" TEXT NOT NULL, ""ObligationCode"" TEXT NOT NULL, ""JurisdictionCode"" TEXT NOT NULL, ""JurisdictionName"" TEXT NOT NULL, ""Description"" TEXT NOT NULL, ""LiabilityAccountNumber"" TEXT NOT NULL, ""OriginalAmount"" TEXT NOT NULL, ""OutstandingAmount"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""DueDate"" TEXT NULL, ""DepositScheduleType"" TEXT NOT NULL, ""DepositRuleCode"" TEXT NOT NULL, ""DepositRuleSource"" TEXT NOT NULL, ""DepositScheduleConfigurationId"" TEXT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollLiabilities", "DepositScheduleType", @"ALTER TABLE ""PayrollLiabilities"" ADD COLUMN ""DepositScheduleType"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollLiabilities", "DepositRuleCode", @"ALTER TABLE ""PayrollLiabilities"" ADD COLUMN ""DepositRuleCode"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollLiabilities", "DepositRuleSource", @"ALTER TABLE ""PayrollLiabilities"" ADD COLUMN ""DepositRuleSource"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollLiabilities", "DepositScheduleConfigurationId", @"ALTER TABLE ""PayrollLiabilities"" ADD COLUMN ""DepositScheduleConfigurationId"" TEXT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollLiabilities_SourceType_SourceLineId"" ON ""PayrollLiabilities"" (""SourceType"", ""SourceLineId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollLiabilities_CompanyId_Status_DueDate"" ON ""PayrollLiabilities"" (""CompanyId"", ""Status"", ""DueDate"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollLiabilityPayments"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""BankAccountId"" TEXT NOT NULL, ""PaymentDate"" TEXT NOT NULL, ""Reference"" TEXT NOT NULL, ""Payee"" TEXT NOT NULL, ""Method"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""JournalEntryId"" TEXT NOT NULL, ""ReversalJournalEntryId"" TEXT NULL, ""CreatedByUserId"" TEXT NULL, ""CreatedAtUtc"" TEXT NOT NULL, ""ReversedByUserId"" TEXT NULL, ""ReversedAtUtc"" TEXT NULL, ""ReversalDate"" TEXT NULL, ""ReversalReason"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollLiabilityPayments_CompanyId_Reference"" ON ""PayrollLiabilityPayments"" (""CompanyId"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollLiabilityPaymentApplications"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""PayrollLiabilityPaymentId"" TEXT NOT NULL, ""PayrollLiabilityId"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollLiabilityPaymentApplications_PayrollLiabilityPaymentId_PayrollLiabilityId"" ON ""PayrollLiabilityPaymentApplications"" (""PayrollLiabilityPaymentId"", ""PayrollLiabilityId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollEmployeePayments"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""PayrollRunId"" TEXT NOT NULL, ""PayrollRunEmployeeLineId"" TEXT NOT NULL, ""EmployeeId"" TEXT NOT NULL, ""EmployeeNumber"" TEXT NOT NULL, ""EmployeeName"" TEXT NOT NULL, ""Method"" TEXT NOT NULL, ""Reference"" TEXT NOT NULL, ""BankRoutingNumber"" TEXT NOT NULL, ""BankAccountNumber"" TEXT NOT NULL, ""BankAccountType"" TEXT NOT NULL, ""DestinationLastFour"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""YearToDateGross"" TEXT NOT NULL, ""YearToDateEmployeeTaxes"" TEXT NOT NULL, ""YearToDateEmployeeDeductions"" TEXT NOT NULL, ""YearToDateNetPay"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""IssuedAtUtc"" TEXT NOT NULL, ""ReversedAtUtc"" TEXT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollEmployeePayments_PayrollRunId_EmployeeId"" ON ""PayrollEmployeePayments"" (""PayrollRunId"", ""EmployeeId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollEmployeePayments_CompanyId_Status_IssuedAtUtc"" ON ""PayrollEmployeePayments"" (""CompanyId"", ""Status"", ""IssuedAtUtc"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollBankOriginConfigurations"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""BankAccountId"" TEXT NOT NULL, ""ImmediateDestinationRoutingNumber"" TEXT NOT NULL, ""ImmediateOrigin"" TEXT NOT NULL, ""DestinationBankName"" TEXT NOT NULL, ""OriginName"" TEXT NOT NULL, ""CompanyIdentification"" TEXT NOT NULL, ""CompanyEntryDescription"" TEXT NOT NULL, ""OriginatingDfiIdentification"" TEXT NOT NULL, ""EffectiveOn"" TEXT NOT NULL, ""ExpiresOn"" TEXT NULL, ""IsActive"" INTEGER NOT NULL, ""IsBankValidated"" INTEGER NOT NULL, ""BankValidationNotes"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollBankOriginConfigurations_CompanyId_BankAccountId_EffectiveOn"" ON ""PayrollBankOriginConfigurations"" (""CompanyId"", ""BankAccountId"", ""EffectiveOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollPaymentFiles"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""PayrollRunId"" TEXT NOT NULL, ""PayrollBankOriginConfigurationId"" TEXT NULL, ""Format"" TEXT NOT NULL, ""FileName"" TEXT NOT NULL, ""ContentType"" TEXT NOT NULL, ""Content"" TEXT NOT NULL, ""ContentSha256"" TEXT NOT NULL, ""SourceDigestSha256"" TEXT NOT NULL, ""EntryCount"" INTEGER NOT NULL, ""CreditTotal"" TEXT NOT NULL, ""RoutingHash"" INTEGER NOT NULL, ""FileIdModifier"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""SpecificationVersion"" TEXT NOT NULL, ""GeneratedByUserId"" TEXT NULL, ""GeneratedAtUtc"" TEXT NOT NULL, ""VoidedAtUtc"" TEXT NULL, ""VoidReason"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollPaymentFiles_CompanyId_PayrollRunId_Format"" ON ""PayrollPaymentFiles"" (""CompanyId"", ""PayrollRunId"", ""Format"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollPaymentFiles_CompanyId_GeneratedAtUtc"" ON ""PayrollPaymentFiles"" (""CompanyId"", ""GeneratedAtUtc"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollFilings"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""FormCode"" TEXT NOT NULL, ""TaxYear"" INTEGER NOT NULL, ""Quarter"" INTEGER NULL, ""PeriodKey"" TEXT NOT NULL, ""PeriodStart"" TEXT NOT NULL, ""PeriodEnd"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""DataJson"" TEXT NOT NULL, ""SummaryJson"" TEXT NOT NULL, ""SourcePayrollRunIdsJson"" TEXT NOT NULL, ""SourceDigestSha256"" TEXT NOT NULL, ""OfficialSourceUrl"" TEXT NOT NULL, ""ContentVersion"" TEXT NOT NULL, ""PreparedByUserId"" TEXT NULL, ""PreparedAtUtc"" TEXT NOT NULL, ""ApprovedByUserId"" TEXT NULL, ""ApprovedAtUtc"" TEXT NULL, ""ApprovedDataJson"" TEXT NOT NULL, ""ApprovedSourceDigestSha256"" TEXT NOT NULL, ""ApprovedBaselineAtUtc"" TEXT NULL, ""ReopenedByUserId"" TEXT NULL, ""ReopenedAtUtc"" TEXT NULL, ""ReopenReason"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollFilings", "ApprovedDataJson", @"ALTER TABLE ""PayrollFilings"" ADD COLUMN ""ApprovedDataJson"" TEXT NOT NULL DEFAULT '{}';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollFilings", "ApprovedSourceDigestSha256", @"ALTER TABLE ""PayrollFilings"" ADD COLUMN ""ApprovedSourceDigestSha256"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollFilings", "ApprovedBaselineAtUtc", @"ALTER TABLE ""PayrollFilings"" ADD COLUMN ""ApprovedBaselineAtUtc"" TEXT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"UPDATE ""PayrollFilings"" SET ""ApprovedDataJson"" = ""DataJson"", ""ApprovedSourceDigestSha256"" = ""SourceDigestSha256"", ""ApprovedBaselineAtUtc"" = ""ApprovedAtUtc"" WHERE ""Status"" = 'Approved' AND ""ApprovedSourceDigestSha256"" = '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollFilings_CompanyId_FormCode_PeriodKey"" ON ""PayrollFilings"" (""CompanyId"", ""FormCode"", ""PeriodKey"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollFilingCorrections"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""OriginalPayrollFilingId"" TEXT NOT NULL, ""Sequence"" INTEGER NOT NULL, ""FormCode"" TEXT NOT NULL, ""TaxYear"" INTEGER NOT NULL, ""Quarter"" INTEGER NOT NULL, ""Process"" TEXT NOT NULL, ""DiscoveredOn"" TEXT NOT NULL, ""Explanation"" TEXT NOT NULL, ""FederalWithholdingCorrectionType"" TEXT NOT NULL, ""EmployeeCertificationCode"" TEXT NOT NULL, ""EmployeeCertificationEvidenceReference"" TEXT NOT NULL, ""WageStatementsCorrected"" INTEGER NOT NULL, ""WageStatementEvidenceReference"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""DataJson"" TEXT NOT NULL, ""CorrectedSourceDigestSha256"" TEXT NOT NULL, ""OfficialSourceUrl"" TEXT NOT NULL, ""ContentVersion"" TEXT NOT NULL, ""PreparedByUserId"" TEXT NULL, ""PreparedAtUtc"" TEXT NOT NULL, ""ApprovedByUserId"" TEXT NULL, ""ApprovedAtUtc"" TEXT NULL, ""VoidedByUserId"" TEXT NULL, ""VoidedAtUtc"" TEXT NULL, ""VoidReason"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollFilingCorrections", "VoidedByUserId", @"ALTER TABLE ""PayrollFilingCorrections"" ADD COLUMN ""VoidedByUserId"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollFilingCorrections", "VoidedAtUtc", @"ALTER TABLE ""PayrollFilingCorrections"" ADD COLUMN ""VoidedAtUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollFilingCorrections", "VoidReason", @"ALTER TABLE ""PayrollFilingCorrections"" ADD COLUMN ""VoidReason"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollFilingCorrections_CompanyId_OriginalPayrollFilingId_Sequence"" ON ""PayrollFilingCorrections"" (""CompanyId"", ""OriginalPayrollFilingId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollSsaWageFileConfigurations"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""FileKind"" TEXT NOT NULL, ""SpecificationTaxYear"" INTEGER NOT NULL, ""SpecificationVersion"" TEXT NOT NULL, ""LayoutCompatibilityCode"" TEXT NOT NULL, ""OfficialSpecificationUrl"" TEXT NOT NULL, ""OfficialSpecificationSha256"" TEXT NOT NULL, ""SourceRetrievedOn"" TEXT NOT NULL, ""ReviewNotes"" TEXT NOT NULL, ""SubmitterEin"" TEXT NOT NULL, ""BsoUserId"" TEXT NOT NULL, ""SubmitterName"" TEXT NOT NULL, ""LocationAddress"" TEXT NOT NULL, ""DeliveryAddress"" TEXT NOT NULL, ""City"" TEXT NOT NULL, ""State"" TEXT NOT NULL, ""PostalCode"" TEXT NOT NULL, ""ContactName"" TEXT NOT NULL, ""ContactPhone"" TEXT NOT NULL, ""ContactEmail"" TEXT NOT NULL, ""PreparerCode"" TEXT NOT NULL, ""EmployerLocationAddress"" TEXT NOT NULL, ""EmployerDeliveryAddress"" TEXT NOT NULL, ""EmployerCity"" TEXT NOT NULL, ""EmployerState"" TEXT NOT NULL, ""EmployerPostalCode"" TEXT NOT NULL, ""EmployerContactName"" TEXT NOT NULL, ""EmployerContactPhone"" TEXT NOT NULL, ""EmployerContactEmail"" TEXT NOT NULL, ""KindOfEmployer"" TEXT NOT NULL, ""EmploymentCode"" TEXT NOT NULL, ""EmployerSignaturePin"" TEXT NOT NULL, ""IsApproved"" INTEGER NOT NULL, ""ApprovedByUserId"" TEXT NULL, ""ApprovedAtUtc"" TEXT NULL, ""IsActive"" INTEGER NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollSsaWageFileConfigurations", "FileKind", @"ALTER TABLE ""PayrollSsaWageFileConfigurations"" ADD COLUMN ""FileKind"" TEXT NOT NULL DEFAULT 'EFW2C';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollSsaWageFileConfigurations", "KindOfEmployer", @"ALTER TABLE ""PayrollSsaWageFileConfigurations"" ADD COLUMN ""KindOfEmployer"" TEXT NOT NULL DEFAULT 'N';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollSsaWageFileConfigurations", "EmploymentCode", @"ALTER TABLE ""PayrollSsaWageFileConfigurations"" ADD COLUMN ""EmploymentCode"" TEXT NOT NULL DEFAULT 'R';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "PayrollSsaWageFileConfigurations", "EmployerSignaturePin", @"ALTER TABLE ""PayrollSsaWageFileConfigurations"" ADD COLUMN ""EmployerSignaturePin"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_PayrollSsaWageFileConfigurations_CompanyId_SpecificationTaxYear"";", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollSsaWageFileConfigurations_CompanyId_SpecificationTaxYear_FileKind"" ON ""PayrollSsaWageFileConfigurations"" (""CompanyId"", ""SpecificationTaxYear"", ""FileKind"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollSsaWageFiles"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""PayrollFilingCorrectionId"" TEXT NOT NULL, ""PayrollSsaWageFileConfigurationId"" TEXT NOT NULL, ""TaxYear"" INTEGER NOT NULL, ""FileName"" TEXT NOT NULL, ""ContentBase64"" TEXT NOT NULL, ""ContentSha256"" TEXT NOT NULL, ""SourceDigestSha256"" TEXT NOT NULL, ""SpecificationVersion"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""RecordCount"" INTEGER NOT NULL, ""EmployeeRecordCount"" INTEGER NOT NULL, ""GeneratedByUserId"" TEXT NULL, ""GeneratedAtUtc"" TEXT NOT NULL, ""ValidatedByUserId"" TEXT NULL, ""ValidatedAtUtc"" TEXT NULL, ""AccuWageEvidenceReference"" TEXT NOT NULL, ""ValidationNotes"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL, FOREIGN KEY (""PayrollFilingCorrectionId"") REFERENCES ""PayrollFilingCorrections"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""PayrollSsaWageFileConfigurationId"") REFERENCES ""PayrollSsaWageFileConfigurations"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollSsaWageFiles_CompanyId_PayrollFilingCorrectionId"" ON ""PayrollSsaWageFiles"" (""CompanyId"", ""PayrollFilingCorrectionId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollSsaOriginalWageFiles"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""PayrollFilingId"" TEXT NOT NULL, ""PayrollSsaWageFileConfigurationId"" TEXT NOT NULL, ""TaxYear"" INTEGER NOT NULL, ""FileName"" TEXT NOT NULL, ""ContentBase64"" TEXT NOT NULL, ""ContentSha256"" TEXT NOT NULL, ""SourceDigestSha256"" TEXT NOT NULL, ""SpecificationVersion"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""RecordCount"" INTEGER NOT NULL, ""EmployeeRecordCount"" INTEGER NOT NULL, ""GeneratedByUserId"" TEXT NULL, ""GeneratedAtUtc"" TEXT NOT NULL, ""ValidatedByUserId"" TEXT NULL, ""ValidatedAtUtc"" TEXT NULL, ""AccuWageEvidenceReference"" TEXT NOT NULL, ""ValidationNotes"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL, FOREIGN KEY (""PayrollFilingId"") REFERENCES ""PayrollFilings"" (""Id"") ON DELETE RESTRICT, FOREIGN KEY (""PayrollSsaWageFileConfigurationId"") REFERENCES ""PayrollSsaWageFileConfigurations"" (""Id"") ON DELETE RESTRICT);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollSsaOriginalWageFiles_CompanyId_PayrollFilingId"" ON ""PayrollSsaOriginalWageFiles"" (""CompanyId"", ""PayrollFilingId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollClosePeriods"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""CompanyId"" TEXT NOT NULL, ""PeriodType"" TEXT NOT NULL, ""TaxYear"" INTEGER NOT NULL, ""Quarter"" INTEGER NULL, ""PeriodKey"" TEXT NOT NULL, ""PeriodStart"" TEXT NOT NULL, ""PeriodEnd"" TEXT NOT NULL, ""Status"" TEXT NOT NULL, ""ClosedByUserId"" TEXT NULL, ""ClosedAtUtc"" TEXT NOT NULL, ""ReopenedByUserId"" TEXT NULL, ""ReopenedAtUtc"" TEXT NULL, ""ReopenReason"" TEXT NOT NULL, ""ConcurrencyToken"" TEXT NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollClosePeriods_CompanyId_PeriodType_PeriodKey"" ON ""PayrollClosePeriods"" (""CompanyId"", ""PeriodType"", ""PeriodKey"");", cancellationToken);
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
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "AddressCity" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "AddressState" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "PostalCode" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "SocialSecurityNumber" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "BankRoutingNumber" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "BankAccountNumber" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "BankAccountType" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "DirectDepositEnabled" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "DirectDepositAuthorizationOn" date NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "DirectDepositAuthorizationReference" text NOT NULL DEFAULT '';""", cancellationToken);
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
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTimeEntries"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollTimecardId"" uuid NOT NULL, ""Sequence"" integer NOT NULL, ""WorkDate"" date NOT NULL, ""EarningCode"" text NOT NULL, ""EarningType"" text NOT NULL, ""Hours"" numeric(18,4) NOT NULL, ""Rate"" numeric(18,4) NOT NULL, ""Amount"" numeric(18,2) NOT NULL, ""IsTaxable"" boolean NOT NULL, ""WorkState"" text NOT NULL, ""WorkCounty"" text NOT NULL, ""WorkCity"" text NOT NULL, ""WorkSchoolDistrict"" text NOT NULL, ""ProjectJobId"" uuid NULL, ""Notes"" text NOT NULL, ""W2ReportingJson"" text NOT NULL DEFAULT '{{}}');", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollTimeEntries" ADD COLUMN IF NOT EXISTS "W2ReportingJson" text NOT NULL DEFAULT '{{}}';""", cancellationToken);
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
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "EmployerBenefitContributions" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRuns" ADD COLUMN IF NOT EXISTS "ConcurrencyToken" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollRuns_CompanyId_Reference"" ON ""PayrollRuns"" (""CompanyId"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollRunEmployeeLines"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollRunId"" uuid NOT NULL, ""EmployeeId"" uuid NOT NULL, ""WorkState"" text NOT NULL, ""FilingStatus"" text NOT NULL, ""GrossPay"" numeric(18,2) NOT NULL, ""PreTaxDeductions"" numeric(18,2) NOT NULL, ""EmployeeWithholdings"" numeric(18,2) NOT NULL, ""PostTaxDeductions"" numeric(18,2) NOT NULL, ""EmployerPayrollTaxes"" numeric(18,2) NOT NULL, ""NetPay"" numeric(18,2) NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "PayrollFrequency" text NOT NULL DEFAULT 'Biweekly';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollRunEmployeeLines_PayrollRunId_EmployeeId"" ON ""PayrollRunEmployeeLines"" (""PayrollRunId"", ""EmployeeId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollJurisdictionRules"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""ResidenceJurisdiction"" text NOT NULL, ""WorkJurisdiction"" text NOT NULL, ""ExemptWorkWithholding"" boolean NOT NULL, ""ResidentCreditRate"" numeric(9,5) NOT NULL, ""IsActive"" boolean NOT NULL, ""Notes"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollJurisdictionRules_CompanyId_ResidenceJurisdiction_WorkJurisdiction"" ON ""PayrollJurisdictionRules"" (""CompanyId"", ""ResidenceJurisdiction"", ""WorkJurisdiction"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDeductionPlans"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""Code"" text NOT NULL, ""Name"" text NOT NULL, ""Category"" text NOT NULL, ""CalculationMethod"" text NOT NULL, ""DefaultEmployeeValue"" numeric(18,6) NOT NULL, ""DefaultEmployerValue"" numeric(18,6) NOT NULL, ""IsPreTax"" boolean NOT NULL, ""ExemptFromFederalIncomeTax"" boolean NOT NULL, ""ExemptFromFica"" boolean NOT NULL, ""ExemptFromFuta"" boolean NOT NULL, ""ReducesDisposableEarnings"" boolean NOT NULL, ""LiabilityAccountNumber"" text NOT NULL, ""Priority"" integer NOT NULL, ""EmployeeLimitPerPay"" numeric(18,2) NULL, ""EmployeeAnnualLimit"" numeric(18,2) NULL, ""MinimumNetPay"" numeric(18,2) NOT NULL, ""LimitRuleCode"" text NOT NULL, ""LimitRuleJson"" text NOT NULL, ""OfficialSourceUrl"" text NOT NULL, ""SourceRetrievedOn"" date NULL, ""EffectiveOn"" date NOT NULL, ""ExpiresOn"" date NULL, ""IsActive"" boolean NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDeductionPlans_CompanyId_Code"" ON ""PayrollDeductionPlans"" (""CompanyId"", ""Code"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""EmployeePayrollDeductionElections"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""EmployeeId"" uuid NOT NULL, ""PayrollDeductionPlanId"" uuid NOT NULL, ""EmployeeValueOverride"" numeric(18,4) NULL, ""EmployerValueOverride"" numeric(18,4) NULL, ""EmployeeAnnualLimitOverride"" numeric(18,2) NULL, ""OrderDetailsJson"" text NOT NULL, ""EffectiveOn"" date NOT NULL, ""ExpiresOn"" date NULL, ""IsActive"" boolean NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_EmployeePayrollDeductionElections_CompanyId_EmployeeId_PayrollDeductionPlanId_EffectiveOn"" ON ""EmployeePayrollDeductionElections"" (""CompanyId"", ""EmployeeId"", ""PayrollDeductionPlanId"", ""EffectiveOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "WorkCity" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "ResidenceState" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "ResidenceCity" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "TaxableWages" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "YearToDateGrossBefore" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "YearToDateGrossAfter" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "CalculationTraceJson" text NOT NULL DEFAULT '[]';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollRunEmployeeLines" ADD COLUMN IF NOT EXISTS "EmployerBenefitContributions" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollEarningLines"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" uuid NOT NULL, ""PayrollTimeEntryId"" uuid NULL, ""Sequence"" integer NOT NULL, ""EarningCode"" text NOT NULL, ""EarningType"" text NOT NULL, ""Hours"" numeric(18,4) NOT NULL, ""Rate"" numeric(18,4) NOT NULL, ""Amount"" numeric(18,2) NOT NULL, ""IsTaxable"" boolean NOT NULL, ""WorkedOn"" date NULL, ""WorkState"" text NOT NULL, ""WorkCounty"" text NOT NULL, ""WorkCity"" text NOT NULL, ""WorkSchoolDistrict"" text NOT NULL, ""W2ReportingJson"" text NOT NULL DEFAULT '{{}}');", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollEarningLines" ADD COLUMN IF NOT EXISTS "PayrollTimeEntryId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollEarningLines" ADD COLUMN IF NOT EXISTS "W2ReportingJson" text NOT NULL DEFAULT '{{}}';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollEarningLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollEarningLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_PayrollEarningLines_PayrollTimeEntryId"";", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollEarningLines_PayrollTimeEntryId"" ON ""PayrollEarningLines"" (""PayrollTimeEntryId"") WHERE ""PayrollTimeEntryId"" IS NOT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDeductionLines"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" uuid NOT NULL, ""Sequence"" integer NOT NULL, ""DeductionCode"" text NOT NULL, ""DeductionType"" text NOT NULL, ""EmployeeAmount"" numeric(18,2) NOT NULL, ""EmployerAmount"" numeric(18,2) NOT NULL, ""IsPreTax"" boolean NOT NULL, ""LiabilityAccountNumber"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "ExemptFromFederalIncomeTax" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "ExemptFromFica" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "ExemptFromFuta" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "PayrollDeductionPlanId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "EmployeePayrollDeductionElectionId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "RequestedEmployeeAmount" numeric(18,2) NOT NULL DEFAULT 0;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "LimitApplied" boolean NOT NULL DEFAULT false;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "LimitRuleCode" text NOT NULL DEFAULT 'None';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDeductionLines" ADD COLUMN IF NOT EXISTS "CalculationTraceJson" text NOT NULL DEFAULT '{}';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDeductionLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollDeductionLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollTaxLines"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollRunEmployeeLineId"" uuid NOT NULL, ""Sequence"" integer NOT NULL, ""ObligationCode"" text NOT NULL, ""JurisdictionCode"" text NOT NULL, ""JurisdictionName"" text NOT NULL, ""TaxType"" text NOT NULL, ""TaxableWages"" numeric(18,2) NOT NULL, ""YearToDateTaxableWagesBefore"" numeric(18,2) NOT NULL, ""EmployeeAmount"" numeric(18,2) NOT NULL, ""EmployerAmount"" numeric(18,2) NOT NULL, ""TaxRuleSetId"" uuid NULL, ""TaxContentPackageId"" uuid NULL, ""ContentVersion"" text NOT NULL, ""Source"" text NOT NULL, ""CalculationTraceJson"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollTaxLines_PayrollRunEmployeeLineId_Sequence"" ON ""PayrollTaxLines"" (""PayrollRunEmployeeLineId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDepositScheduleConfigurations"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""JurisdictionCode"" text NOT NULL, ""ReturnFormCode"" text NOT NULL, ""TaxYear"" integer NOT NULL, ""ScheduleType"" text NOT NULL, ""LookbackLiability"" numeric(18,2) NOT NULL, ""LookbackPeriodStart"" date NOT NULL, ""LookbackPeriodEnd"" date NOT NULL, ""MonthlyThreshold"" numeric(18,2) NOT NULL, ""NextDayThreshold"" numeric(18,2) NOT NULL, ""SmallLiabilityThreshold"" numeric(18,2) NOT NULL, ""SmallLiabilityElectionQuartersJson"" text NOT NULL, ""LegalHolidaysJson"" text NOT NULL, ""OfficialRulesUrl"" text NOT NULL, ""OfficialCalendarUrl"" text NOT NULL, ""SourceRetrievedOn"" date NOT NULL, ""ReviewNotes"" text NOT NULL, ""IsApproved"" boolean NOT NULL, ""ApprovedByUserId"" uuid NULL, ""ApprovedAtUtc"" timestamptz NULL, ""IsActive"" boolean NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDepositScheduleConfigurations" ADD COLUMN IF NOT EXISTS "SmallLiabilityThreshold" numeric(18,2) NOT NULL DEFAULT 2500;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollDepositScheduleConfigurations" ADD COLUMN IF NOT EXISTS "SmallLiabilityElectionQuartersJson" text NOT NULL DEFAULT '[]';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDepositScheduleConfigurations_CompanyId_JurisdictionCode_ReturnFormCode_TaxYear"" ON ""PayrollDepositScheduleConfigurations"" (""CompanyId"", ""JurisdictionCode"", ""ReturnFormCode"", ""TaxYear"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollDisasterReliefConfigurations"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""AnnouncementCode"" text NOT NULL, ""DisasterName"" text NOT NULL, ""FemaDeclarationNumber"" text NOT NULL, ""CoveredAreasJson"" text NOT NULL, ""AffectedTaxpayerBasis"" text NOT NULL, ""EligibilityEvidenceReference"" text NOT NULL, ""ReliefActionsJson"" text NOT NULL, ""OfficialSourceUrl"" text NOT NULL, ""SourceRetrievedOn"" date NOT NULL, ""ReviewNotes"" text NOT NULL, ""IsApproved"" boolean NOT NULL, ""ApprovedByUserId"" uuid NULL, ""ApprovedAtUtc"" timestamptz NULL, ""IsActive"" boolean NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollDisasterReliefConfigurations_CompanyId_AnnouncementCode"" ON ""PayrollDisasterReliefConfigurations"" (""CompanyId"", ""AnnouncementCode"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollLiabilities"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""PayrollRunId"" uuid NOT NULL, ""PayrollRunEmployeeLineId"" uuid NOT NULL, ""SourceType"" text NOT NULL, ""SourceLineId"" uuid NOT NULL, ""ObligationCode"" text NOT NULL, ""JurisdictionCode"" text NOT NULL, ""JurisdictionName"" text NOT NULL, ""Description"" text NOT NULL, ""LiabilityAccountNumber"" text NOT NULL, ""OriginalAmount"" numeric(18,2) NOT NULL, ""OutstandingAmount"" numeric(18,2) NOT NULL, ""Status"" text NOT NULL, ""DueDate"" date NULL, ""DepositScheduleType"" text NOT NULL, ""DepositRuleCode"" text NOT NULL, ""DepositRuleSource"" text NOT NULL, ""DepositScheduleConfigurationId"" uuid NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollLiabilities" ADD COLUMN IF NOT EXISTS "DepositScheduleType" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollLiabilities" ADD COLUMN IF NOT EXISTS "DepositRuleCode" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollLiabilities" ADD COLUMN IF NOT EXISTS "DepositRuleSource" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollLiabilities" ADD COLUMN IF NOT EXISTS "DepositScheduleConfigurationId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollLiabilities_SourceType_SourceLineId"" ON ""PayrollLiabilities"" (""SourceType"", ""SourceLineId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollLiabilities_CompanyId_Status_DueDate"" ON ""PayrollLiabilities"" (""CompanyId"", ""Status"", ""DueDate"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollLiabilityPayments"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""BankAccountId"" uuid NOT NULL, ""PaymentDate"" date NOT NULL, ""Reference"" text NOT NULL, ""Payee"" text NOT NULL, ""Method"" text NOT NULL, ""Amount"" numeric(18,2) NOT NULL, ""Status"" text NOT NULL, ""JournalEntryId"" uuid NOT NULL, ""ReversalJournalEntryId"" uuid NULL, ""CreatedByUserId"" uuid NULL, ""CreatedAtUtc"" timestamptz NOT NULL, ""ReversedByUserId"" uuid NULL, ""ReversedAtUtc"" timestamptz NULL, ""ReversalDate"" date NULL, ""ReversalReason"" text NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollLiabilityPayments_CompanyId_Reference"" ON ""PayrollLiabilityPayments"" (""CompanyId"", ""Reference"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollLiabilityPaymentApplications"" (""Id"" uuid NOT NULL PRIMARY KEY, ""PayrollLiabilityPaymentId"" uuid NOT NULL, ""PayrollLiabilityId"" uuid NOT NULL, ""Amount"" numeric(18,2) NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollLiabilityPaymentApplications_PayrollLiabilityPaymentId_PayrollLiabilityId"" ON ""PayrollLiabilityPaymentApplications"" (""PayrollLiabilityPaymentId"", ""PayrollLiabilityId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollEmployeePayments"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""PayrollRunId"" uuid NOT NULL, ""PayrollRunEmployeeLineId"" uuid NOT NULL, ""EmployeeId"" uuid NOT NULL, ""EmployeeNumber"" text NOT NULL, ""EmployeeName"" text NOT NULL, ""Method"" text NOT NULL, ""Reference"" text NOT NULL, ""BankRoutingNumber"" text NOT NULL, ""BankAccountNumber"" text NOT NULL, ""BankAccountType"" text NOT NULL, ""DestinationLastFour"" text NOT NULL, ""Amount"" numeric(18,2) NOT NULL, ""YearToDateGross"" numeric(18,2) NOT NULL, ""YearToDateEmployeeTaxes"" numeric(18,2) NOT NULL, ""YearToDateEmployeeDeductions"" numeric(18,2) NOT NULL, ""YearToDateNetPay"" numeric(18,2) NOT NULL, ""Status"" text NOT NULL, ""IssuedAtUtc"" timestamptz NOT NULL, ""ReversedAtUtc"" timestamptz NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollEmployeePayments_PayrollRunId_EmployeeId"" ON ""PayrollEmployeePayments"" (""PayrollRunId"", ""EmployeeId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollEmployeePayments_CompanyId_Status_IssuedAtUtc"" ON ""PayrollEmployeePayments"" (""CompanyId"", ""Status"", ""IssuedAtUtc"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollBankOriginConfigurations"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""BankAccountId"" uuid NOT NULL, ""ImmediateDestinationRoutingNumber"" text NOT NULL, ""ImmediateOrigin"" text NOT NULL, ""DestinationBankName"" text NOT NULL, ""OriginName"" text NOT NULL, ""CompanyIdentification"" text NOT NULL, ""CompanyEntryDescription"" text NOT NULL, ""OriginatingDfiIdentification"" text NOT NULL, ""EffectiveOn"" date NOT NULL, ""ExpiresOn"" date NULL, ""IsActive"" boolean NOT NULL, ""IsBankValidated"" boolean NOT NULL, ""BankValidationNotes"" text NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollBankOriginConfigurations_CompanyId_BankAccountId_EffectiveOn"" ON ""PayrollBankOriginConfigurations"" (""CompanyId"", ""BankAccountId"", ""EffectiveOn"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollPaymentFiles"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""PayrollRunId"" uuid NOT NULL, ""PayrollBankOriginConfigurationId"" uuid NULL, ""Format"" text NOT NULL, ""FileName"" text NOT NULL, ""ContentType"" text NOT NULL, ""Content"" text NOT NULL, ""ContentSha256"" text NOT NULL, ""SourceDigestSha256"" text NOT NULL, ""EntryCount"" integer NOT NULL, ""CreditTotal"" numeric(18,2) NOT NULL, ""RoutingHash"" bigint NOT NULL, ""FileIdModifier"" text NOT NULL, ""Status"" text NOT NULL, ""SpecificationVersion"" text NOT NULL, ""GeneratedByUserId"" uuid NULL, ""GeneratedAtUtc"" timestamptz NOT NULL, ""VoidedAtUtc"" timestamptz NULL, ""VoidReason"" text NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollPaymentFiles_CompanyId_PayrollRunId_Format"" ON ""PayrollPaymentFiles"" (""CompanyId"", ""PayrollRunId"", ""Format"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PayrollPaymentFiles_CompanyId_GeneratedAtUtc"" ON ""PayrollPaymentFiles"" (""CompanyId"", ""GeneratedAtUtc"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollFilings"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""FormCode"" text NOT NULL, ""TaxYear"" integer NOT NULL, ""Quarter"" integer NULL, ""PeriodKey"" text NOT NULL, ""PeriodStart"" date NOT NULL, ""PeriodEnd"" date NOT NULL, ""Status"" text NOT NULL, ""DataJson"" text NOT NULL, ""SummaryJson"" text NOT NULL, ""SourcePayrollRunIdsJson"" text NOT NULL, ""SourceDigestSha256"" text NOT NULL, ""OfficialSourceUrl"" text NOT NULL, ""ContentVersion"" text NOT NULL, ""PreparedByUserId"" uuid NULL, ""PreparedAtUtc"" timestamptz NOT NULL, ""ApprovedByUserId"" uuid NULL, ""ApprovedAtUtc"" timestamptz NULL, ""ApprovedDataJson"" text NOT NULL, ""ApprovedSourceDigestSha256"" text NOT NULL, ""ApprovedBaselineAtUtc"" timestamptz NULL, ""ReopenedByUserId"" uuid NULL, ""ReopenedAtUtc"" timestamptz NULL, ""ReopenReason"" text NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollFilings" ADD COLUMN IF NOT EXISTS "ApprovedDataJson" text NOT NULL DEFAULT '{}';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollFilings" ADD COLUMN IF NOT EXISTS "ApprovedSourceDigestSha256" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollFilings" ADD COLUMN IF NOT EXISTS "ApprovedBaselineAtUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""UPDATE "PayrollFilings" SET "ApprovedDataJson" = "DataJson", "ApprovedSourceDigestSha256" = "SourceDigestSha256", "ApprovedBaselineAtUtc" = "ApprovedAtUtc" WHERE "Status" = 'Approved' AND "ApprovedSourceDigestSha256" = '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollFilings_CompanyId_FormCode_PeriodKey"" ON ""PayrollFilings"" (""CompanyId"", ""FormCode"", ""PeriodKey"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollFilingCorrections"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""OriginalPayrollFilingId"" uuid NOT NULL, ""Sequence"" integer NOT NULL, ""FormCode"" text NOT NULL, ""TaxYear"" integer NOT NULL, ""Quarter"" integer NOT NULL, ""Process"" text NOT NULL, ""DiscoveredOn"" date NOT NULL, ""Explanation"" text NOT NULL, ""FederalWithholdingCorrectionType"" text NOT NULL, ""EmployeeCertificationCode"" text NOT NULL, ""EmployeeCertificationEvidenceReference"" text NOT NULL, ""WageStatementsCorrected"" boolean NOT NULL, ""WageStatementEvidenceReference"" text NOT NULL, ""Status"" text NOT NULL, ""DataJson"" text NOT NULL, ""CorrectedSourceDigestSha256"" text NOT NULL, ""OfficialSourceUrl"" text NOT NULL, ""ContentVersion"" text NOT NULL, ""PreparedByUserId"" uuid NULL, ""PreparedAtUtc"" timestamptz NOT NULL, ""ApprovedByUserId"" uuid NULL, ""ApprovedAtUtc"" timestamptz NULL, ""VoidedByUserId"" uuid NULL, ""VoidedAtUtc"" timestamptz NULL, ""VoidReason"" text NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollFilingCorrections" ADD COLUMN IF NOT EXISTS "VoidedByUserId" uuid NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollFilingCorrections" ADD COLUMN IF NOT EXISTS "VoidedAtUtc" timestamptz NULL;""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollFilingCorrections" ADD COLUMN IF NOT EXISTS "VoidReason" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollFilingCorrections_CompanyId_OriginalPayrollFilingId_Sequence"" ON ""PayrollFilingCorrections"" (""CompanyId"", ""OriginalPayrollFilingId"", ""Sequence"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollSsaWageFileConfigurations"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""FileKind"" text NOT NULL, ""SpecificationTaxYear"" integer NOT NULL, ""SpecificationVersion"" text NOT NULL, ""LayoutCompatibilityCode"" text NOT NULL, ""OfficialSpecificationUrl"" text NOT NULL, ""OfficialSpecificationSha256"" text NOT NULL, ""SourceRetrievedOn"" date NOT NULL, ""ReviewNotes"" text NOT NULL, ""SubmitterEin"" text NOT NULL, ""BsoUserId"" text NOT NULL, ""SubmitterName"" text NOT NULL, ""LocationAddress"" text NOT NULL, ""DeliveryAddress"" text NOT NULL, ""City"" text NOT NULL, ""State"" text NOT NULL, ""PostalCode"" text NOT NULL, ""ContactName"" text NOT NULL, ""ContactPhone"" text NOT NULL, ""ContactEmail"" text NOT NULL, ""PreparerCode"" text NOT NULL, ""EmployerLocationAddress"" text NOT NULL, ""EmployerDeliveryAddress"" text NOT NULL, ""EmployerCity"" text NOT NULL, ""EmployerState"" text NOT NULL, ""EmployerPostalCode"" text NOT NULL, ""EmployerContactName"" text NOT NULL, ""EmployerContactPhone"" text NOT NULL, ""EmployerContactEmail"" text NOT NULL, ""KindOfEmployer"" text NOT NULL, ""EmploymentCode"" text NOT NULL, ""EmployerSignaturePin"" text NOT NULL, ""IsApproved"" boolean NOT NULL, ""ApprovedByUserId"" uuid NULL, ""ApprovedAtUtc"" timestamptz NULL, ""IsActive"" boolean NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollSsaWageFileConfigurations" ADD COLUMN IF NOT EXISTS "FileKind" text NOT NULL DEFAULT 'EFW2C';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollSsaWageFileConfigurations" ADD COLUMN IF NOT EXISTS "KindOfEmployer" text NOT NULL DEFAULT 'N';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollSsaWageFileConfigurations" ADD COLUMN IF NOT EXISTS "EmploymentCode" text NOT NULL DEFAULT 'R';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "PayrollSsaWageFileConfigurations" ADD COLUMN IF NOT EXISTS "EmployerSignaturePin" text NOT NULL DEFAULT '';""", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_PayrollSsaWageFileConfigurations_CompanyId_SpecificationTaxYear"";", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollSsaWageFileConfigurations_CompanyId_SpecificationTaxYear_FileKind"" ON ""PayrollSsaWageFileConfigurations"" (""CompanyId"", ""SpecificationTaxYear"", ""FileKind"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollSsaWageFiles"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""PayrollFilingCorrectionId"" uuid NOT NULL REFERENCES ""PayrollFilingCorrections""(""Id"") ON DELETE RESTRICT, ""PayrollSsaWageFileConfigurationId"" uuid NOT NULL REFERENCES ""PayrollSsaWageFileConfigurations""(""Id"") ON DELETE RESTRICT, ""TaxYear"" integer NOT NULL, ""FileName"" text NOT NULL, ""ContentBase64"" text NOT NULL, ""ContentSha256"" text NOT NULL, ""SourceDigestSha256"" text NOT NULL, ""SpecificationVersion"" text NOT NULL, ""Status"" text NOT NULL, ""RecordCount"" integer NOT NULL, ""EmployeeRecordCount"" integer NOT NULL, ""GeneratedByUserId"" uuid NULL, ""GeneratedAtUtc"" timestamptz NOT NULL, ""ValidatedByUserId"" uuid NULL, ""ValidatedAtUtc"" timestamptz NULL, ""AccuWageEvidenceReference"" text NOT NULL, ""ValidationNotes"" text NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollSsaWageFiles_CompanyId_PayrollFilingCorrectionId"" ON ""PayrollSsaWageFiles"" (""CompanyId"", ""PayrollFilingCorrectionId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollSsaOriginalWageFiles"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""PayrollFilingId"" uuid NOT NULL REFERENCES ""PayrollFilings""(""Id"") ON DELETE RESTRICT, ""PayrollSsaWageFileConfigurationId"" uuid NOT NULL REFERENCES ""PayrollSsaWageFileConfigurations""(""Id"") ON DELETE RESTRICT, ""TaxYear"" integer NOT NULL, ""FileName"" text NOT NULL, ""ContentBase64"" text NOT NULL, ""ContentSha256"" text NOT NULL, ""SourceDigestSha256"" text NOT NULL, ""SpecificationVersion"" text NOT NULL, ""Status"" text NOT NULL, ""RecordCount"" integer NOT NULL, ""EmployeeRecordCount"" integer NOT NULL, ""GeneratedByUserId"" uuid NULL, ""GeneratedAtUtc"" timestamptz NOT NULL, ""ValidatedByUserId"" uuid NULL, ""ValidatedAtUtc"" timestamptz NULL, ""AccuWageEvidenceReference"" text NOT NULL, ""ValidationNotes"" text NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollSsaOriginalWageFiles_CompanyId_PayrollFilingId"" ON ""PayrollSsaOriginalWageFiles"" (""CompanyId"", ""PayrollFilingId"");", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""PayrollClosePeriods"" (""Id"" uuid NOT NULL PRIMARY KEY, ""CompanyId"" uuid NOT NULL, ""PeriodType"" text NOT NULL, ""TaxYear"" integer NOT NULL, ""Quarter"" integer NULL, ""PeriodKey"" text NOT NULL, ""PeriodStart"" date NOT NULL, ""PeriodEnd"" date NOT NULL, ""Status"" text NOT NULL, ""ClosedByUserId"" uuid NULL, ""ClosedAtUtc"" timestamptz NOT NULL, ""ReopenedByUserId"" uuid NULL, ""ReopenedAtUtc"" timestamptz NULL, ""ReopenReason"" text NOT NULL, ""ConcurrencyToken"" text NOT NULL);", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PayrollClosePeriods_CompanyId_PeriodType_PeriodKey"" ON ""PayrollClosePeriods"" (""CompanyId"", ""PeriodType"", ""PeriodKey"");", cancellationToken);
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
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
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
            alterCommand.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
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
