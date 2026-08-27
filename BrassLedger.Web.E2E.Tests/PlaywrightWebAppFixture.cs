using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

public sealed class PlaywrightWebAppFixture : IAsyncLifetime
{
    private static readonly Regex ListeningUrlRegex = new(@"Now listening on:\s+(https?://\S+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly string _solutionRoot;
    private readonly string _projectRoot;
    private readonly string _applicationPath;
    private readonly string _buildConfiguration;
    private readonly string _dataRootPath;
    private readonly string _sqliteConnectionString;
    private readonly List<Task> _logPumpTasks = new();
    private readonly TaskCompletionSource<string> _listeningUrlSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _appProcess;
    private string? _baseUrl;

    public PlaywrightWebAppFixture()
    {
        _solutionRoot = ResolveSolutionRoot();
        _projectRoot = Path.Combine(_solutionRoot, "BrassLedger.Web");
        _buildConfiguration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the E2E test build configuration.");
        _applicationPath = ResolveApplicationPath();
        _dataRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.Web.E2E.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRootPath);
        _sqliteConnectionString = $"Data Source={Path.Combine(_dataRootPath, "brassledger.e2e.db")}";
    }

    public string BaseUrl => _baseUrl ?? throw new InvalidOperationException("The web app has not finished starting yet.");
    public IPlaywright Playwright { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        StartApplication();
        await WaitForServerAsync();

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        Playwright?.Dispose();

        if (_appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            await _appProcess.WaitForExitAsync();
        }

        await Task.WhenAll(_logPumpTasks);

        if (Directory.Exists(_dataRootPath))
        {
            try
            {
                Directory.Delete(_dataRootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public string GetLogs()
    {
        return string.Join(Environment.NewLine, _logs);
    }

    public async Task<UiSession> CreateSessionAsync(BrowserKind browserKind, int width = 1440, int height = 1600)
    {
        var browser = await LaunchBrowserAsync(browserKind);
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height
            }
        });

        return new UiSession(this, browserKind, browser, page);
    }

    public async Task CreateQuickBooksAdministratorAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO "AccessRoles" ("Id", "CompanyId", "Name", "Description", "TemplateCode", "Permissions", "IsSystemRole", "IsActive", "RequiresMfa")
            SELECT $roleId, "CompanyId", 'Integration Test Administrator',
                   (SELECT "Description" FROM "AccessRoles" WHERE "CompanyId" = "Users"."CompanyId" AND "Name" = 'Controller'),
                   'e2e-integration-admin',
                   (SELECT "Permissions" FROM "AccessRoles" WHERE "CompanyId" = "Users"."CompanyId" AND "Name" = 'Controller') || '|security.users.manage',
                   0, 1, 0
            FROM "Users" WHERE "UserName" = 'controller';
            INSERT OR IGNORE INTO "Users" (
                "Id", "CompanyId", "UserName", "DisplayName", "Email", "EmailLookupHash", "EmailConfirmedAtUtc",
                "PasswordHash", "SecurityStamp", "Role", "IsActive", "FailedSignInCount", "LastFailedSignInUtc",
                "LockoutEndUtc", "LastSuccessfulSignInUtc", "LastPasswordChangedUtc", "MfaEnabled", "MfaSecret",
                "MfaEnrolledAtUtc", "MfaLastAcceptedTimeStep", "MfaFailedAttemptCount", "MfaLockoutEndUtc")
            SELECT $userId, "CompanyId", 'integration-admin', "DisplayName", "Email", NULL, "EmailConfirmedAtUtc",
                   "PasswordHash", $securityStamp, 'Integration Test Administrator', 1, 0, NULL,
                   NULL, NULL, "LastPasswordChangedUtc", 0, "MfaSecret", NULL, NULL, 0, NULL
            FROM "Users" WHERE "UserName" = 'controller';
            INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
            SELECT $membershipId, "Id", "CompanyId", 'Integration Test Administrator', 0, 1, $grantedAtUtc
            FROM "Users" WHERE "UserName" = 'integration-admin';
            """;
        command.Parameters.AddWithValue("$roleId", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$securityStamp", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$membershipId", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$grantedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task CreateConsolidationAdministratorAsync()
    {
        await CreateQuickBooksAdministratorAsync();
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE "CompanyMemberships"
            SET "IsOwner" = 1
            WHERE "UserId" = (SELECT "Id" FROM "Users" WHERE "UserName" = 'integration-admin');
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task CreateConsolidationWorkflowAsync()
    {
        await CreateConsolidationAdministratorAsync();
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        foreach (var userName in new[] { "e2e-consolidation-reviewer", "e2e-consolidation-poster" })
        {
            await using var userCommand = connection.CreateCommand();
            userCommand.CommandText = """
                INSERT OR IGNORE INTO "Users" (
                    "Id", "CompanyId", "UserName", "DisplayName", "Email", "EmailLookupHash", "EmailConfirmedAtUtc",
                    "PasswordHash", "SecurityStamp", "Role", "IsActive", "FailedSignInCount", "LastFailedSignInUtc",
                    "LockoutEndUtc", "LastSuccessfulSignInUtc", "LastPasswordChangedUtc", "MfaEnabled", "MfaSecret",
                    "MfaEnrolledAtUtc", "MfaLastAcceptedTimeStep", "MfaFailedAttemptCount", "MfaLockoutEndUtc")
                SELECT $userId, "CompanyId", $userName, $userName, $email, NULL, "EmailConfirmedAtUtc",
                       "PasswordHash", $securityStamp, 'Controller', 1, 0, NULL,
                       NULL, NULL, "LastPasswordChangedUtc", 0, "MfaSecret", NULL, NULL, 0, NULL
                FROM "Users" WHERE "UserName" = 'controller';
                INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
                SELECT $membershipId, "Id", "CompanyId", 'Controller', 0, 1, $grantedAtUtc FROM "Users" WHERE "UserName" = $userName;
                """;
            userCommand.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString().ToUpperInvariant()); userCommand.Parameters.AddWithValue("$userName", userName);
            userCommand.Parameters.AddWithValue("$email", $"{userName}@example.test"); userCommand.Parameters.AddWithValue("$securityStamp", Guid.NewGuid().ToString("N"));
            userCommand.Parameters.AddWithValue("$membershipId", Guid.NewGuid().ToString().ToUpperInvariant()); userCommand.Parameters.AddWithValue("$grantedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await userCommand.ExecuteNonQueryAsync();
        }
        await using var configuration = connection.CreateCommand();
        configuration.CommandText = """
            DELETE FROM "ConsolidationIntercompanyMatches" WHERE "SalesInvoiceId" = '71000000-0000-0000-0000-000000000022';
            DELETE FROM "ConsolidationAdjustmentLines" WHERE "ConsolidationAdjustmentBatchId" IN (SELECT "Id" FROM "ConsolidationAdjustmentBatches" WHERE "Reference" = 'ELIM-E2E-IC-INV-1001');
            DELETE FROM "ConsolidationAdjustmentBatches" WHERE "Reference" = 'ELIM-E2E-IC-INV-1001';
            DELETE FROM "ConsolidationTradingPartners" WHERE "CustomerId" = '71000000-0000-0000-0000-000000000020' OR "VendorId" = '71000000-0000-0000-0000-000000000021';
            DELETE FROM "ConsolidationStatementPresentations" WHERE "ConsolidationGroupId" = '71000000-0000-0000-0000-000000000001';
            DELETE FROM "ConsolidationDisclosurePackages" WHERE "ConsolidationGroupId" = '71000000-0000-0000-0000-000000000001';
            DELETE FROM "ConsolidationOwnershipEvents" WHERE "ConsolidationGroupId" = '71000000-0000-0000-0000-000000000001' AND "ReversalOfEventId" IS NOT NULL;
            DELETE FROM "ConsolidationOwnershipEvents" WHERE "ConsolidationGroupId" = '71000000-0000-0000-0000-000000000001';
            INSERT OR IGNORE INTO "Companies" ("Id", "Name", "LegalName", "TaxId", "BaseCurrency", "FiscalYearStartMonth")
            VALUES ('71000000-0000-0000-0000-000000000010', 'E2E intercompany affiliate', 'E2E intercompany affiliate LLC', 'E2E-IC-AFFILIATE', 'USD', 1);
            INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
            SELECT '71000000-0000-0000-0000-000000000011', "Id", '71000000-0000-0000-0000-000000000010', 'Integration Test Administrator', 1, 1, $grantedAtUtc
            FROM "Users" WHERE "UserName" = 'integration-admin';
            INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
            SELECT '71000000-0000-0000-0000-000000000012', "Id", '71000000-0000-0000-0000-000000000010', 'Controller', 0, 1, $grantedAtUtc
            FROM "Users" WHERE "UserName" = 'e2e-consolidation-reviewer';
            INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
            SELECT '71000000-0000-0000-0000-000000000013', "Id", '71000000-0000-0000-0000-000000000010', 'Controller', 0, 1, $grantedAtUtc
            FROM "Users" WHERE "UserName" = 'e2e-consolidation-poster';
            INSERT OR IGNORE INTO "ConsolidationGroups" ("Id", "CompanyId", "Name", "ReportingCurrency", "CtaAccountNumber", "CtaAccountName", "NciAccountNumber", "NciAccountName", "IsActive", "ConcurrencyToken")
            SELECT '71000000-0000-0000-0000-000000000001', "Id", 'E2E controlled consolidation', 'USD', '39999', 'Cumulative translation adjustment', '39998', 'Noncontrolling interests', 1, 'e2e-group-token'
            FROM "Companies" WHERE "Name" = 'Brass Ledger Manufacturing';
            INSERT OR IGNORE INTO "ConsolidationGroupCompanies" ("Id", "ConsolidationGroupId", "MemberCompanyId", "OwnershipPercentage", "ConsolidationBasis", "BasisRationale", "BasisReviewedOn", "EffectiveFrom", "EffectiveThrough", "ConcurrencyToken")
            SELECT '71000000-0000-0000-0000-000000000002', '71000000-0000-0000-0000-000000000001', "Id", '1.0', 0, '', NULL, '0001-01-01', NULL, 'e2e-membership-token'
            FROM "Companies" WHERE "Name" = 'Brass Ledger Manufacturing';
            INSERT OR IGNORE INTO "ConsolidationGroupCompanies" ("Id", "ConsolidationGroupId", "MemberCompanyId", "OwnershipPercentage", "ConsolidationBasis", "BasisRationale", "BasisReviewedOn", "EffectiveFrom", "EffectiveThrough", "ConcurrencyToken")
            VALUES ('71000000-0000-0000-0000-000000000014', '71000000-0000-0000-0000-000000000001', '71000000-0000-0000-0000-000000000010', '0.75', 1, 'E2E reviewed control conclusion', '2026-01-01', '0001-01-01', NULL, 'e2e-affiliate-membership-token');
            INSERT OR IGNORE INTO "ConsolidationAccountMappings" ("Id", "ConsolidationGroupId", "MemberCompanyId", "MemberAccountId", "ReportingAccountNumber", "ReportingAccountName", "ReportingAccountType", "TranslationMethod", "CashFlowActivity", "CashFlowRationale", "CashFlowReviewedOn", "EffectiveFrom", "EffectiveThrough", "IsActive", "ConcurrencyToken")
            SELECT '71000000-0000-0000-0000-000000000003', '71000000-0000-0000-0000-000000000001', "CompanyId", "Id", "Number", "Name", "Type", 0, 0, '', NULL, '0001-01-01', NULL, 1, 'e2e-cash-mapping-token'
            FROM "Accounts" WHERE "Number" = '1000' LIMIT 1;
            INSERT OR IGNORE INTO "ConsolidationAccountMappings" ("Id", "ConsolidationGroupId", "MemberCompanyId", "MemberAccountId", "ReportingAccountNumber", "ReportingAccountName", "ReportingAccountType", "TranslationMethod", "CashFlowActivity", "CashFlowRationale", "CashFlowReviewedOn", "EffectiveFrom", "EffectiveThrough", "IsActive", "ConcurrencyToken")
            SELECT '71000000-0000-0000-0000-000000000004', '71000000-0000-0000-0000-000000000001', "CompanyId", "Id", "Number", "Name", 2, 2, 3, 'E2E reviewed financing counterpart classification', '2026-01-01', '0001-01-01', NULL, 1, 'e2e-equity-mapping-token'
            FROM "Accounts" WHERE "Number" = '3000' LIMIT 1;
            INSERT OR IGNORE INTO "Customers" ("Id", "CompanyId", "CustomerNumber", "Name", "Email", "State", "CreditLimit", "OpenBalance")
            SELECT '71000000-0000-0000-0000-000000000020', "Id", 'E2E-IC-CUST', 'E2E intercompany affiliate', 'affiliate@example.invalid', 'MI', '10000.0', '125.0'
            FROM "Companies" WHERE "Name" = 'Brass Ledger Manufacturing';
            INSERT OR IGNORE INTO "Vendors" ("Id", "CompanyId", "VendorNumber", "Name", "Email", "State", "PaymentTerms", "OpenBalance")
            VALUES ('71000000-0000-0000-0000-000000000021', '71000000-0000-0000-0000-000000000010', 'E2E-IC-VEND', 'Brass Ledger Manufacturing', 'parent@example.invalid', 'MI', 'Net 30', '125.0');
            INSERT OR IGNORE INTO "SalesInvoices" ("Id", "CompanyId", "CustomerId", "InvoiceNumber", "InvoiceDate", "DueDate", "Status", "Subtotal", "TaxAmount", "TotalAmount", "BalanceDue", "SalesOrderId", "InventoryShipmentId", "ConcurrencyToken")
            SELECT '71000000-0000-0000-0000-000000000022', "Id", '71000000-0000-0000-0000-000000000020', 'E2E-IC-INV-1001', '2026-08-15', '2026-09-14', 'Open', '125.0', '0.0', '125.0', '125.0', NULL, NULL, 'e2e-ic-invoice-token'
            FROM "Companies" WHERE "Name" = 'Brass Ledger Manufacturing';
            INSERT OR IGNORE INTO "VendorBills" ("Id", "CompanyId", "VendorId", "BillNumber", "BillDate", "DueDate", "Status", "TotalAmount", "BalanceDue", "PurchaseOrderId", "InventoryReceiptId", "ConcurrencyToken")
            VALUES ('71000000-0000-0000-0000-000000000023', '71000000-0000-0000-0000-000000000010', '71000000-0000-0000-0000-000000000021', 'e2e-ic-inv-1001', '2026-08-16', '2026-09-15', 'Open', '125.0', '125.0', NULL, NULL, 'e2e-ic-bill-token');
            """;
        configuration.Parameters.AddWithValue("$grantedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await configuration.ExecuteNonQueryAsync();
    }

    public async Task CreateSubledgerWorkflowUsersAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        foreach (var (userName, roleName) in new[]
        {
            ("e2e-ar-approver", "Receivables Approver"),
            ("e2e-ar-poster", "Receivables Poster"),
            ("e2e-ap-approver", "Payables Approver"),
            ("e2e-ap-poster", "Payables Poster"),
            ("e2e-journal-reviewer", "Controller"),
            ("e2e-journal-poster", "Controller"),
            ("e2e-payroll-reviewer", "Payroll Approver"),
            ("e2e-payroll-poster", "Payroll Poster")
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO "Users" (
                    "Id", "CompanyId", "UserName", "DisplayName", "Email", "EmailLookupHash", "EmailConfirmedAtUtc",
                    "PasswordHash", "SecurityStamp", "Role", "IsActive", "FailedSignInCount", "LastFailedSignInUtc",
                    "LockoutEndUtc", "LastSuccessfulSignInUtc", "LastPasswordChangedUtc", "MfaEnabled", "MfaSecret",
                    "MfaEnrolledAtUtc", "MfaLastAcceptedTimeStep", "MfaFailedAttemptCount", "MfaLockoutEndUtc")
                SELECT $userId, "CompanyId", $userName, $userName, "Email", NULL, "EmailConfirmedAtUtc",
                       "PasswordHash", $securityStamp, $roleName, 1, 0, NULL,
                       NULL, NULL, "LastPasswordChangedUtc", 0, "MfaSecret", NULL, NULL, 0, NULL
                FROM "Users" WHERE "UserName" = 'controller';
                INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
                SELECT $membershipId, "Id", "CompanyId", $roleName, 0, 1, $grantedAtUtc
                FROM "Users" WHERE "UserName" = $userName;
                """;
            command.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$userName", userName);
            command.Parameters.AddWithValue("$roleName", roleName);
            command.Parameters.AddWithValue("$securityStamp", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$membershipId", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$grantedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task CreateProjectChangeOrderUsersAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        foreach (var (userName, roleName) in new[]
        {
            ("e2e-project-preparer", "Project Change Order Preparer"),
            ("e2e-project-approver", "Project Change Order Approver")
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO "Users" (
                    "Id", "CompanyId", "UserName", "DisplayName", "Email", "EmailLookupHash", "EmailConfirmedAtUtc",
                    "PasswordHash", "SecurityStamp", "Role", "IsActive", "FailedSignInCount", "LastFailedSignInUtc",
                    "LockoutEndUtc", "LastSuccessfulSignInUtc", "LastPasswordChangedUtc", "MfaEnabled", "MfaSecret",
                    "MfaEnrolledAtUtc", "MfaLastAcceptedTimeStep", "MfaFailedAttemptCount", "MfaLockoutEndUtc")
                SELECT $userId, "CompanyId", $userName, $userName, "Email", NULL, "EmailConfirmedAtUtc",
                       "PasswordHash", $securityStamp, $roleName, 1, 0, NULL,
                       NULL, NULL, "LastPasswordChangedUtc", 0, "MfaSecret", NULL, NULL, 0, NULL
                FROM "Users" WHERE "UserName" = 'controller';
                INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
                SELECT $membershipId, "Id", "CompanyId", $roleName, 0, 1, $grantedAtUtc
                FROM "Users" WHERE "UserName" = $userName;
                """;
            command.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$userName", userName);
            command.Parameters.AddWithValue("$roleName", roleName);
            command.Parameters.AddWithValue("$securityStamp", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$membershipId", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$grantedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task CreateProjectWipUsersAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        foreach (var (userName, roleName) in new[]
        {
            ("e2e-project-wip-preparer", "Project WIP Preparer"),
            ("e2e-project-wip-approver", "Project WIP Approver"),
            ("e2e-project-wip-poster", "Project WIP Poster")
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO "Users" (
                    "Id", "CompanyId", "UserName", "DisplayName", "Email", "EmailLookupHash", "EmailConfirmedAtUtc",
                    "PasswordHash", "SecurityStamp", "Role", "IsActive", "FailedSignInCount", "LastFailedSignInUtc",
                    "LockoutEndUtc", "LastSuccessfulSignInUtc", "LastPasswordChangedUtc", "MfaEnabled", "MfaSecret",
                    "MfaEnrolledAtUtc", "MfaLastAcceptedTimeStep", "MfaFailedAttemptCount", "MfaLockoutEndUtc")
                SELECT $userId, "CompanyId", $userName, $userName, "Email", NULL, "EmailConfirmedAtUtc",
                       "PasswordHash", $securityStamp, $roleName, 1, 0, NULL,
                       NULL, NULL, "LastPasswordChangedUtc", 0, "MfaSecret", NULL, NULL, 0, NULL
                FROM "Users" WHERE "UserName" = 'controller';
                INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
                SELECT $membershipId, "Id", "CompanyId", $roleName, 0, 1, $grantedAtUtc
                FROM "Users" WHERE "UserName" = $userName;
                """;
            command.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$userName", userName);
            command.Parameters.AddWithValue("$roleName", roleName);
            command.Parameters.AddWithValue("$securityStamp", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$membershipId", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$grantedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task RemoveQuickBooksAdministratorAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM "CompanyMemberships"
            WHERE "UserId" = (SELECT "Id" FROM "Users" WHERE "UserName" = 'integration-admin');
            DELETE FROM "Users" WHERE "UserName" = 'integration-admin';
            DELETE FROM "AccessRoles" WHERE "TemplateCode" = 'e2e-integration-admin';
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task RemoveIntercompanyMatchingWorkflowAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM "BusinessAuditEntries" WHERE "Action" LIKE 'consolidation-intercompany-match.%' OR "Action" LIKE 'consolidation-trading-partner.%' OR "Action" LIKE 'consolidation-statement-presentation.%' OR "Action" LIKE 'consolidation-disclosure.%' OR "Action" LIKE 'consolidation-ownership-event.%';
            DELETE FROM "ConsolidationIntercompanyMatches" WHERE "SalesInvoiceId" = '71000000-0000-0000-0000-000000000022';
            DELETE FROM "BusinessAuditEntries" WHERE "EntityId" IN (SELECT "Id" FROM "ConsolidationAdjustmentBatches" WHERE "Reference" IN ('ELIM-E2E-IC-INV-1001', 'NCI-E2E-CONTROLLED'));
            DELETE FROM "ConsolidationAdjustmentLines" WHERE "ConsolidationAdjustmentBatchId" IN (SELECT "Id" FROM "ConsolidationAdjustmentBatches" WHERE "Reference" IN ('ELIM-E2E-IC-INV-1001', 'NCI-E2E-CONTROLLED'));
            DELETE FROM "ConsolidationAdjustmentBatches" WHERE "Reference" IN ('ELIM-E2E-IC-INV-1001', 'NCI-E2E-CONTROLLED');
            DELETE FROM "ConsolidationTradingPartners" WHERE "CustomerId" = '71000000-0000-0000-0000-000000000020' OR "VendorId" = '71000000-0000-0000-0000-000000000021';
            DELETE FROM "ConsolidationStatementPresentations" WHERE "ConsolidationGroupId" = '71000000-0000-0000-0000-000000000001';
            DELETE FROM "ConsolidationDisclosurePackages" WHERE "ConsolidationGroupId" = '71000000-0000-0000-0000-000000000001';
            DELETE FROM "ConsolidationOwnershipEvents" WHERE "ConsolidationGroupId" = '71000000-0000-0000-0000-000000000001' AND "ReversalOfEventId" IS NOT NULL;
            DELETE FROM "ConsolidationOwnershipEvents" WHERE "ConsolidationGroupId" = '71000000-0000-0000-0000-000000000001';
            DELETE FROM "SalesInvoices" WHERE "Id" = '71000000-0000-0000-0000-000000000022';
            DELETE FROM "VendorBills" WHERE "Id" = '71000000-0000-0000-0000-000000000023';
            DELETE FROM "Customers" WHERE "Id" = '71000000-0000-0000-0000-000000000020';
            DELETE FROM "Vendors" WHERE "Id" = '71000000-0000-0000-0000-000000000021';
            DELETE FROM "ConsolidationGroupCompanies" WHERE "Id" = '71000000-0000-0000-0000-000000000014';
            DELETE FROM "CompanyMemberships" WHERE "CompanyId" = '71000000-0000-0000-0000-000000000010';
            DELETE FROM "Companies" WHERE "Id" = '71000000-0000-0000-0000-000000000010';
            """;
        await command.ExecuteNonQueryAsync();
    }

    private void StartApplication()
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(_applicationPath);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("http://127.0.0.1:0");

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__BrassLedgerSqlite"] = _sqliteConnectionString;

        _appProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start BrassLedger.Web for Playwright tests.");
        PumpLogs(_appProcess.StandardOutput, "stdout");
        PumpLogs(_appProcess.StandardError, "stderr");
    }

    private async Task WaitForServerAsync()
    {
        using var httpClient = new HttpClient();
        var timeoutAt = DateTime.UtcNow.AddSeconds(45);

        while (DateTime.UtcNow < timeoutAt)
        {
            if (_appProcess is { HasExited: true })
            {
                throw new InvalidOperationException($"The web app exited before it started listening.{Environment.NewLine}{GetLogs()}");
            }

            if (_baseUrl is null && _listeningUrlSource.Task.IsCompletedSuccessfully)
            {
                _baseUrl = _listeningUrlSource.Task.Result.TrimEnd('/');
            }

            if (_baseUrl is null)
            {
                await Task.Delay(250);
                continue;
            }

            try
            {
                using var response = await httpClient.GetAsync(_baseUrl);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Timed out waiting for BrassLedger.Web at {_baseUrl}.{Environment.NewLine}{GetLogs()}");
    }

    private void PumpLogs(StreamReader reader, string source)
    {
        _logPumpTasks.Add(Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                _logs.Enqueue($"[{source}] {line}");

                var match = ListeningUrlRegex.Match(line);
                if (match.Success)
                {
                    _listeningUrlSource.TrySetResult(match.Groups[1].Value);
                }
            }
        }));
    }

    private string ResolveApplicationPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("BRASSLEDGER_E2E_APP_PATH");
        var applicationPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_projectRoot, "bin", _buildConfiguration, "net8.0", "BrassLedger.Web.dll")
            : Path.GetFullPath(configuredPath);
        return File.Exists(applicationPath)
            ? applicationPath
            : throw new FileNotFoundException("Could not locate the BrassLedger web application for E2E testing. Set BRASSLEDGER_E2E_APP_PATH when using an out-of-tree artifacts directory.", applicationPath);
    }

    private static string ResolveSolutionRoot([CallerFilePath] string callerFilePath = "")
    {
        var configuredRoot = Environment.GetEnvironmentVariable("BRASSLEDGER_REPOSITORY_ROOT");
        var startingPaths = new[]
        {
            configuredRoot,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(callerFilePath),
            AppContext.BaseDirectory
        };

        foreach (var startingPath in startingPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directory = new DirectoryInfo(startingPath!);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BrassLedger.slnx"))) return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate BrassLedger.slnx. Set BRASSLEDGER_REPOSITORY_ROOT when E2E tests run outside the repository checkout.");
    }

    public static IReadOnlyList<BrowserKind> GetInstalledBrowsers()
    {
        var browserRoot = ResolveBrowserRoot();

        if (!Directory.Exists(browserRoot))
        {
            return Array.Empty<BrowserKind>();
        }

        var installedBrowsers = new List<BrowserKind>();

        if (TryResolveChromiumExecutablePath(out _))
        {
            installedBrowsers.Add(BrowserKind.Chromium);
        }

        if (TryResolveEdgeExecutablePath(out _))
        {
            installedBrowsers.Add(BrowserKind.Edge);
        }

        if (Directory.EnumerateDirectories(browserRoot, "firefox-*", SearchOption.TopDirectoryOnly).Any())
        {
            installedBrowsers.Add(BrowserKind.Firefox);
        }

        if (Directory.EnumerateDirectories(browserRoot, "webkit-*", SearchOption.TopDirectoryOnly).Any())
        {
            installedBrowsers.Add(BrowserKind.WebKit);
        }

        return installedBrowsers;
    }

    private async Task<IBrowser> LaunchBrowserAsync(BrowserKind browserKind)
    {
        return browserKind switch
        {
            BrowserKind.Chromium => await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = ResolveChromiumExecutablePath(),
                Args = ["--disable-gpu", "--font-render-hinting=none"]
            }),
            BrowserKind.Edge => await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = ResolveEdgeExecutablePath(),
                Args = ["--disable-gpu", "--font-render-hinting=none"]
            }),
            BrowserKind.Firefox => await Playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            }),
            BrowserKind.WebKit => await Playwright.Webkit.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(browserKind), browserKind, "Unsupported browser kind.")
        };
    }

    private static string ResolveBrowserRoot()
    {
        var configuredPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && configuredPath != "0")
        {
            return Path.GetFullPath(configuredPath);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ms-playwright");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Caches", "ms-playwright")
            : Path.Combine(home, ".cache", "ms-playwright");
    }

    private static string ResolveChromiumExecutablePath()
    {
        return TryResolveChromiumExecutablePath(out var executable)
            ? executable
            : throw new FileNotFoundException("Chromium was not found in the Playwright cache. Run playwright.ps1 install chromium.");
    }

    private static bool TryResolveChromiumExecutablePath(out string executablePath)
    {
        var relativeExecutablePaths = OperatingSystem.IsWindows()
            ? new[] { Path.Combine("chrome-win", "chrome.exe") }
            : OperatingSystem.IsMacOS()
                ? new[] { Path.Combine("chrome-mac", "Chromium.app", "Contents", "MacOS", "Chromium") }
                : new[]
                {
                    Path.Combine("chrome-linux", "chrome"),
                    Path.Combine("chrome-linux64", "chrome")
                };

        executablePath = Directory.Exists(ResolveBrowserRoot())
            ? Directory
                .EnumerateDirectories(ResolveBrowserRoot(), "chromium-*", SearchOption.TopDirectoryOnly)
                .SelectMany(path => relativeExecutablePaths.Select(relativePath => Path.Combine(path, relativePath)))
                .FirstOrDefault(File.Exists) ?? string.Empty
            : string.Empty;
        return !string.IsNullOrWhiteSpace(executablePath);
    }

    private static string ResolveEdgeExecutablePath()
    {
        return TryResolveEdgeExecutablePath(out var executable)
            ? executable
            : throw new FileNotFoundException("Microsoft Edge was not found on this machine.");
    }

    private static bool TryResolveEdgeExecutablePath(out string executablePath)
    {
        var candidatePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };

        executablePath = candidatePaths.FirstOrDefault(File.Exists) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(executablePath);
    }
}
