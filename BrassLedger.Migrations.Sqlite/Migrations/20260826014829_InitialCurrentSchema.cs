using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCurrentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    TemplateCode = table.Column<string>(type: "TEXT", nullable: false),
                    Permissions = table.Column<string>(type: "TEXT", nullable: false),
                    IsSystemRole = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresMfa = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountActionTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedIpAddress = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountActionTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountingInterchangeBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderCode = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    CommittedImportKey = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IsDryRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    RowCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RejectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RejectionJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingInterchangeBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsControlAccount = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    OperationalRole = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthenticationAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticationAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AccountNumberMasked = table.Column<string>(type: "TEXT", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    UnreconciledAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LastReconciledOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LastReconciledBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankReconciliationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StatementClosingBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    BookBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ClearedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Variance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ReconciledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReconciledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReopenedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReopenedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReopenReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DetailJson = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", nullable: false),
                    TaxId = table.Column<string>(type: "TEXT", nullable: false),
                    BaseCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    FiscalYearStartMonth = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    IsOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMemberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidationGroupCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberCompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnershipPercentage = table.Column<decimal>(type: "TEXT", precision: 9, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationGroupCompanies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidationGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ReportingCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurrencyExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    QuoteCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OpenBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    LastName = table.Column<string>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    ResidenceState = table.Column<string>(type: "TEXT", nullable: false),
                    ResidenceCity = table.Column<string>(type: "TEXT", nullable: false),
                    WorkCity = table.Column<string>(type: "TEXT", nullable: false),
                    ResidenceCounty = table.Column<string>(type: "TEXT", nullable: false),
                    ResidenceSchoolDistrict = table.Column<string>(type: "TEXT", nullable: false),
                    WorkCounty = table.Column<string>(type: "TEXT", nullable: false),
                    WorkSchoolDistrict = table.Column<string>(type: "TEXT", nullable: false),
                    AddressLine1 = table.Column<string>(type: "TEXT", nullable: false),
                    AddressLine2 = table.Column<string>(type: "TEXT", nullable: false),
                    AddressCity = table.Column<string>(type: "TEXT", nullable: false),
                    AddressState = table.Column<string>(type: "TEXT", nullable: false),
                    PostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    SocialSecurityNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BankRoutingNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BankAccountNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BankAccountType = table.Column<string>(type: "TEXT", nullable: false),
                    DirectDepositEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DirectDepositAuthorizationOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DirectDepositAuthorizationReference = table.Column<string>(type: "TEXT", nullable: false),
                    EmploymentStartedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EmploymentEndedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PayType = table.Column<string>(type: "TEXT", nullable: false),
                    MonthlyBasePay = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    HourlyRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    OvertimeRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FilingStatus = table.Column<string>(type: "TEXT", nullable: false),
                    PayrollFrequency = table.Column<string>(type: "TEXT", nullable: false),
                    Allowances = table.Column<int>(type: "INTEGER", nullable: false),
                    FederalFormW4Year = table.Column<int>(type: "INTEGER", nullable: false),
                    FederalStep2MultipleJobs = table.Column<bool>(type: "INTEGER", nullable: false),
                    FederalStep3Credits = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FederalStep4OtherIncome = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FederalStep4Deductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FederalWithholdingExempt = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdditionalWithholding = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PreTaxBenefitDeductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PostTaxBenefitDeductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalEntityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderCode = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderEntityId = table.Column<string>(type: "TEXT", nullable: false),
                    LocalEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderSyncToken = table.Column<string>(type: "TEXT", nullable: false),
                    LastRemoteFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    LastLocalFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    LastSynchronizedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalEntityLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderCode = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialsJson = table.Column<string>(type: "TEXT", nullable: false),
                    LastValidatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CredentialVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CredentialOperationLeaseId = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialOperation = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialOperationLeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderCode = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: false),
                    IsDryRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnchangedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConflictCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RejectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    DetailJson = table.Column<string>(type: "TEXT", nullable: false),
                    InitiatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ReorderPoint = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TransactionType = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceDocumentType = table.Column<string>(type: "TEXT", nullable: false),
                    EntryNumber = table.Column<string>(type: "TEXT", nullable: false),
                    PostedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SourceModule = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IsPosted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReversalOfJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedByJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Debit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabelTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    StockType = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MfaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRecoveryCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MfaSignInChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaSignInChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuthAuthorizationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProviderCode = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionName = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    StateHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAuthorizationAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollClosePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodType = table.Column<string>(type: "TEXT", nullable: false),
                    TaxYear = table.Column<int>(type: "INTEGER", nullable: false),
                    Quarter = table.Column<int>(type: "INTEGER", nullable: true),
                    PeriodKey = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReopenedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReopenedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReopenReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollClosePeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollDeductionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    CalculationMethod = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultEmployeeValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    DefaultEmployerValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    IsPreTax = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptFromFederalIncomeTax = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptFromFica = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptFromFuta = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReducesDisposableEarnings = table.Column<bool>(type: "INTEGER", nullable: false),
                    LiabilityAccountNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeLimitPerPay = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    EmployeeAnnualLimit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    MinimumNetPay = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LimitRuleCode = table.Column<string>(type: "TEXT", nullable: false),
                    LimitRuleJson = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialSourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRetrievedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollDeductionPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollDepositScheduleConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", nullable: false),
                    ReturnFormCode = table.Column<string>(type: "TEXT", nullable: false),
                    TaxYear = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduleType = table.Column<string>(type: "TEXT", nullable: false),
                    LookbackLiability = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LookbackPeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LookbackPeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    MonthlyThreshold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    NextDayThreshold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SmallLiabilityThreshold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SmallLiabilityElectionQuartersJson = table.Column<string>(type: "TEXT", nullable: false),
                    LegalHolidaysJson = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialRulesUrl = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialCalendarUrl = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRetrievedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReviewNotes = table.Column<string>(type: "TEXT", nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollDepositScheduleConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollDisasterReliefConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnnouncementCode = table.Column<string>(type: "TEXT", nullable: false),
                    DisasterName = table.Column<string>(type: "TEXT", nullable: false),
                    FemaDeclarationNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CoveredAreasJson = table.Column<string>(type: "TEXT", nullable: false),
                    AffectedTaxpayerBasis = table.Column<string>(type: "TEXT", nullable: false),
                    EligibilityEvidenceReference = table.Column<string>(type: "TEXT", nullable: false),
                    ReliefActionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialSourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRetrievedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReviewNotes = table.Column<string>(type: "TEXT", nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollDisasterReliefConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollFilings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormCode = table.Column<string>(type: "TEXT", nullable: false),
                    TaxYear = table.Column<int>(type: "INTEGER", nullable: false),
                    Quarter = table.Column<int>(type: "INTEGER", nullable: true),
                    PeriodKey = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourcePayrollRunIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDigestSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialSourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ContentVersion = table.Column<string>(type: "TEXT", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedSourceDigestSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedBaselineAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReopenedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReopenReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollFilings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollJurisdictionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResidenceJurisdiction = table.Column<string>(type: "TEXT", nullable: false),
                    WorkJurisdiction = table.Column<string>(type: "TEXT", nullable: false),
                    ExemptWorkWithholding = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResidentCreditRate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 5, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollJurisdictionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollSsaWageFileConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileKind = table.Column<string>(type: "TEXT", nullable: false),
                    SpecificationTaxYear = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecificationVersion = table.Column<string>(type: "TEXT", nullable: false),
                    LayoutCompatibilityCode = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialSpecificationUrl = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialSpecificationSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRetrievedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReviewNotes = table.Column<string>(type: "TEXT", nullable: false),
                    SubmitterEin = table.Column<string>(type: "TEXT", nullable: false),
                    BsoUserId = table.Column<string>(type: "TEXT", nullable: false),
                    SubmitterName = table.Column<string>(type: "TEXT", nullable: false),
                    LocationAddress = table.Column<string>(type: "TEXT", nullable: false),
                    DeliveryAddress = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    PostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    ContactName = table.Column<string>(type: "TEXT", nullable: false),
                    ContactPhone = table.Column<string>(type: "TEXT", nullable: false),
                    ContactEmail = table.Column<string>(type: "TEXT", nullable: false),
                    PreparerCode = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerLocationAddress = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerDeliveryAddress = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerCity = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerState = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerPostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerContactName = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerContactPhone = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerContactEmail = table.Column<string>(type: "TEXT", nullable: false),
                    KindOfEmployer = table.Column<string>(type: "TEXT", nullable: false),
                    EmploymentCode = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerSignaturePin = table.Column<string>(type: "TEXT", nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSsaWageFileConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ActualCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    OrderedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    LayoutType = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    SupportsVisualStudioDesign = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportCatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BalanceDue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    OrderedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityEmailOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountActionTokenId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequiresUsableAction = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecipientEmail = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEmailOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubledgerDocumentWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentNumber = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IsRecurringTemplate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", nullable: false),
                    FrequencyInterval = table.Column<int>(type: "INTEGER", nullable: false),
                    NextOccurrenceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SourceTemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubledgerDocumentWorkflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxContentPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageCode = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumEngineVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeSummary = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxContentPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxFormRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormCode = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    FilingFrequency = table.Column<string>(type: "TEXT", nullable: false),
                    DeliveryChannel = table.Column<string>(type: "TEXT", nullable: false),
                    DueRule = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxFormRequirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", nullable: false),
                    TaxType = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 5, nullable: false),
                    AnnualWageBase = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    IsEmployerSpecific = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    VerificationNotes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRuleBrackets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    UpperBoundAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FixedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 5, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRuleBrackets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRuleFieldDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldCode = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    DataType = table.Column<string>(type: "TEXT", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultValueJson = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationJson = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    HelpText = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRuleFieldDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRuleParameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParameterCode = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    ValueType = table.Column<string>(type: "TEXT", nullable: false),
                    NumericValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TextValue = table.Column<string>(type: "TEXT", nullable: false),
                    BooleanValue = table.Column<bool>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRuleParameters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRuleSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionName = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionType = table.Column<string>(type: "TEXT", nullable: false),
                    TaxType = table.Column<string>(type: "TEXT", nullable: false),
                    CalculationMethod = table.Column<string>(type: "TEXT", nullable: false),
                    WithholdingFrequency = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    IsEmployerSpecific = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsBracketTable = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsParameterEditing = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TaxContentPackageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContentVersion = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumEngineVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ParentJurisdictionCode = table.Column<string>(type: "TEXT", nullable: false),
                    ObligationCode = table.Column<string>(type: "TEXT", nullable: false),
                    CalculationVariant = table.Column<string>(type: "TEXT", nullable: false),
                    ExclusiveGroup = table.Column<string>(type: "TEXT", nullable: false),
                    VariantPriority = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicabilityJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRuleTestCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    InputJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedOutputJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsRequiredForActivation = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRuleTestCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxSourceCaptures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxContentPackageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    RawContent = table.Column<string>(type: "TEXT", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxSourceCaptures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    EmailLookupHash = table.Column<string>(type: "TEXT", nullable: true),
                    EmailConfirmedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedSignInCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFailedSignInUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSuccessfulSignInUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastPasswordChangedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MfaEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MfaSecret = table.Column<string>(type: "TEXT", nullable: false),
                    MfaEnrolledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MfaLastAcceptedTimeStep = table.Column<long>(type: "INTEGER", nullable: true),
                    MfaFailedAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MfaLockoutEndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VendorBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BillNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BillDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BalanceDue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorBills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentTerms = table.Column<string>(type: "TEXT", nullable: false),
                    OpenBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Format = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RejectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DebitTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreditTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RejectionJson = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementImportBatches_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollBankOriginConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImmediateDestinationRoutingNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ImmediateOrigin = table.Column<string>(type: "TEXT", nullable: false),
                    DestinationBankName = table.Column<string>(type: "TEXT", nullable: false),
                    OriginName = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyIdentification = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyEntryDescription = table.Column<string>(type: "TEXT", nullable: false),
                    OriginatingDfiIdentification = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBankValidated = table.Column<bool>(type: "INTEGER", nullable: false),
                    BankValidationNotes = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBankOriginConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBankOriginConfigurations_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromBankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToBankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransferDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InboundJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InboundReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransfers_BankAccounts_FromBankAccountId",
                        column: x => x.FromBankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_BankAccounts_ToBankAccountId",
                        column: x => x.ToBankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_JournalEntries_InboundJournalEntryId",
                        column: x => x.InboundJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_JournalEntries_InboundReversalJournalEntryId",
                        column: x => x.InboundReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollLiabilityPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Payee = table.Column<string>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLiabilityPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollLiabilityPayments_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollLiabilityPayments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollLiabilityPayments_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RunType = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    GrossPayroll = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PreTaxDeductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployeeWithholdings = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PostTaxDeductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployerPayrollTaxes = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployerBenefitContributions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    NetPay = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    CalculationWarningsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TaxContentSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubledgerPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    UnappliedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubledgerPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubledgerPayments_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubledgerPayments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubledgerPayments_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeePayrollDeductionElections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollDeductionPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeValueOverride = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    EmployerValueOverride = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    EmployeeAnnualLimitOverride = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    OrderDetailsJson = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayrollDeductionElections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeePayrollDeductionElections_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeePayrollDeductionElections_PayrollDeductionPlans_PayrollDeductionPlanId",
                        column: x => x.PayrollDeductionPlanId,
                        principalTable: "PayrollDeductionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollFilingCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalPayrollFilingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    FormCode = table.Column<string>(type: "TEXT", nullable: false),
                    TaxYear = table.Column<int>(type: "INTEGER", nullable: false),
                    Quarter = table.Column<int>(type: "INTEGER", nullable: false),
                    Process = table.Column<string>(type: "TEXT", nullable: false),
                    DiscoveredOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", nullable: false),
                    FederalWithholdingCorrectionType = table.Column<string>(type: "TEXT", nullable: false),
                    EmployeeCertificationCode = table.Column<string>(type: "TEXT", nullable: false),
                    EmployeeCertificationEvidenceReference = table.Column<string>(type: "TEXT", nullable: false),
                    WageStatementsCorrected = table.Column<bool>(type: "INTEGER", nullable: false),
                    WageStatementEvidenceReference = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false),
                    CorrectedSourceDigestSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialSourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ContentVersion = table.Column<string>(type: "TEXT", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollFilingCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollFilingCorrections_PayrollFilings_OriginalPayrollFilingId",
                        column: x => x.OriginalPayrollFilingId,
                        principalTable: "PayrollFilings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollSsaOriginalWageFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollFilingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollSsaWageFileConfigurationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxYear = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentBase64 = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDigestSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    SpecificationVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeRecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ValidatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AccuWageEvidenceReference = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationNotes = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSsaOriginalWageFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollSsaOriginalWageFiles_PayrollFilings_PayrollFilingId",
                        column: x => x.PayrollFilingId,
                        principalTable: "PayrollFilings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSsaOriginalWageFiles_PayrollSsaWageFileConfigurations_PayrollSsaWageFileConfigurationId",
                        column: x => x.PayrollSsaWageFileConfigurationId,
                        principalTable: "PayrollSsaWageFileConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_Accounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: false),
                    AuthenticationMethod = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorBillLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorBillLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorBillLines_Accounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorBillLines_VendorBills_VendorBillId",
                        column: x => x.VendorBillId,
                        principalTable: "VendorBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PostedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TransactionType = table.Column<string>(type: "TEXT", nullable: false),
                    Payee = table.Column<string>(type: "TEXT", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MatchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MatchedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MatchNote = table.Column<string>(type: "TEXT", nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementTransactions_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementTransactions_BankStatementImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "BankStatementImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementTransactions_JournalEntries_MatchedJournalEntryId",
                        column: x => x.MatchedJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollPaymentFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollBankOriginConfigurationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Format = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDigestSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    EntryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreditTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RoutingHash = table.Column<long>(type: "INTEGER", nullable: false),
                    FileIdModifier = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SpecificationVersion = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPaymentFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollPaymentFiles_PayrollBankOriginConfigurations_PayrollBankOriginConfigurationId",
                        column: x => x.PayrollBankOriginConfigurationId,
                        principalTable: "PayrollBankOriginConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollPaymentFiles_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunEmployeeLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkState = table.Column<string>(type: "TEXT", nullable: false),
                    WorkCity = table.Column<string>(type: "TEXT", nullable: false),
                    ResidenceState = table.Column<string>(type: "TEXT", nullable: false),
                    ResidenceCity = table.Column<string>(type: "TEXT", nullable: false),
                    FilingStatus = table.Column<string>(type: "TEXT", nullable: false),
                    PayrollFrequency = table.Column<string>(type: "TEXT", nullable: false),
                    GrossPay = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxableWages = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    YearToDateGrossBefore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    YearToDateGrossAfter = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PreTaxDeductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployeeWithholdings = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PostTaxDeductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployerPayrollTaxes = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployerBenefitContributions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    NetPay = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CalculationTraceJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunEmployeeLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployeeLines_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployeeLines_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollTimecards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollTimecards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollTimecards_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollTimecards_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubledgerAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Subledger = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PaymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AdjustmentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    OffsetAccountNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubledgerAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubledgerAdjustments_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubledgerAdjustments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubledgerAdjustments_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubledgerAdjustments_SubledgerPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "SubledgerPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubledgerPaymentApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubledgerPaymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubledgerPaymentApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubledgerPaymentApplications_SubledgerPayments_SubledgerPaymentId",
                        column: x => x.SubledgerPaymentId,
                        principalTable: "SubledgerPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollSsaWageFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollFilingCorrectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollSsaWageFileConfigurationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxYear = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentBase64 = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDigestSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    SpecificationVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeRecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ValidatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AccuWageEvidenceReference = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationNotes = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSsaWageFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollSsaWageFiles_PayrollFilingCorrections_PayrollFilingCorrectionId",
                        column: x => x.PayrollFilingCorrectionId,
                        principalTable: "PayrollFilingCorrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSsaWageFiles_PayrollSsaWageFileConfigurations_PayrollSsaWageFileConfigurationId",
                        column: x => x.PayrollSsaWageFileConfigurationId,
                        principalTable: "PayrollSsaWageFileConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollDeductionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PayrollDeductionPlanId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EmployeePayrollDeductionElectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeductionCode = table.Column<string>(type: "TEXT", nullable: false),
                    DeductionType = table.Column<string>(type: "TEXT", nullable: false),
                    EmployeeAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RequestedEmployeeAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployerAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsPreTax = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptFromFederalIncomeTax = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptFromFica = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptFromFuta = table.Column<bool>(type: "INTEGER", nullable: false),
                    LiabilityAccountNumber = table.Column<string>(type: "TEXT", nullable: false),
                    LimitApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    LimitRuleCode = table.Column<string>(type: "TEXT", nullable: false),
                    CalculationTraceJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollDeductionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollDeductionLines_EmployeePayrollDeductionElections_EmployeePayrollDeductionElectionId",
                        column: x => x.EmployeePayrollDeductionElectionId,
                        principalTable: "EmployeePayrollDeductionElections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollDeductionLines_PayrollDeductionPlans_PayrollDeductionPlanId",
                        column: x => x.PayrollDeductionPlanId,
                        principalTable: "PayrollDeductionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollDeductionLines_PayrollRunEmployeeLines_PayrollRunEmployeeLineId",
                        column: x => x.PayrollRunEmployeeLineId,
                        principalTable: "PayrollRunEmployeeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollEmployeePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "TEXT", nullable: false),
                    EmployeeName = table.Column<string>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    BankRoutingNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BankAccountNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BankAccountType = table.Column<string>(type: "TEXT", nullable: false),
                    DestinationLastFour = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    YearToDateGross = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    YearToDateEmployeeTaxes = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    YearToDateEmployeeDeductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    YearToDateNetPay = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEmployeePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollEmployeePayments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollEmployeePayments_PayrollRunEmployeeLines_PayrollRunEmployeeLineId",
                        column: x => x.PayrollRunEmployeeLineId,
                        principalTable: "PayrollRunEmployeeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollEmployeePayments_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollLiabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    SourceLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObligationCode = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    LiabilityAccountNumber = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DepositScheduleType = table.Column<string>(type: "TEXT", nullable: false),
                    DepositRuleCode = table.Column<string>(type: "TEXT", nullable: false),
                    DepositRuleSource = table.Column<string>(type: "TEXT", nullable: false),
                    DepositScheduleConfigurationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLiabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollLiabilities_PayrollDepositScheduleConfigurations_DepositScheduleConfigurationId",
                        column: x => x.DepositScheduleConfigurationId,
                        principalTable: "PayrollDepositScheduleConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollLiabilities_PayrollRunEmployeeLines_PayrollRunEmployeeLineId",
                        column: x => x.PayrollRunEmployeeLineId,
                        principalTable: "PayrollRunEmployeeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollLiabilities_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollTaxLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ObligationCode = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionName = table.Column<string>(type: "TEXT", nullable: false),
                    TaxType = table.Column<string>(type: "TEXT", nullable: false),
                    TaxableWages = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    YearToDateTaxableWagesBefore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployeeAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployerAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaxContentPackageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContentVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    CalculationTraceJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollTaxLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollTaxLines_PayrollRunEmployeeLines_PayrollRunEmployeeLineId",
                        column: x => x.PayrollRunEmployeeLineId,
                        principalTable: "PayrollRunEmployeeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollTimeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollTimecardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EarningCode = table.Column<string>(type: "TEXT", nullable: false),
                    EarningType = table.Column<string>(type: "TEXT", nullable: false),
                    Hours = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsTaxable = table.Column<bool>(type: "INTEGER", nullable: false),
                    WorkState = table.Column<string>(type: "TEXT", nullable: false),
                    WorkCounty = table.Column<string>(type: "TEXT", nullable: false),
                    WorkCity = table.Column<string>(type: "TEXT", nullable: false),
                    WorkSchoolDistrict = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    W2ReportingJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollTimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollTimeEntries_PayrollTimecards_PayrollTimecardId",
                        column: x => x.PayrollTimecardId,
                        principalTable: "PayrollTimecards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollTimeEntries_ProjectJobs_ProjectJobId",
                        column: x => x.ProjectJobId,
                        principalTable: "ProjectJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollLiabilityPaymentApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollLiabilityPaymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollLiabilityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLiabilityPaymentApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollLiabilityPaymentApplications_PayrollLiabilities_PayrollLiabilityId",
                        column: x => x.PayrollLiabilityId,
                        principalTable: "PayrollLiabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollLiabilityPaymentApplications_PayrollLiabilityPayments_PayrollLiabilityPaymentId",
                        column: x => x.PayrollLiabilityPaymentId,
                        principalTable: "PayrollLiabilityPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollEarningLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunEmployeeLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollTimeEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    EarningCode = table.Column<string>(type: "TEXT", nullable: false),
                    EarningType = table.Column<string>(type: "TEXT", nullable: false),
                    Hours = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsTaxable = table.Column<bool>(type: "INTEGER", nullable: false),
                    WorkedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    WorkState = table.Column<string>(type: "TEXT", nullable: false),
                    WorkCounty = table.Column<string>(type: "TEXT", nullable: false),
                    WorkCity = table.Column<string>(type: "TEXT", nullable: false),
                    WorkSchoolDistrict = table.Column<string>(type: "TEXT", nullable: false),
                    W2ReportingJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEarningLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollEarningLines_PayrollRunEmployeeLines_PayrollRunEmployeeLineId",
                        column: x => x.PayrollRunEmployeeLineId,
                        principalTable: "PayrollRunEmployeeLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollEarningLines_PayrollTimeEntries_PayrollTimeEntryId",
                        column: x => x.PayrollTimeEntryId,
                        principalTable: "PayrollTimeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRoles_CompanyId_Name",
                table: "AccessRoles",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountActionTokens_TokenHash",
                table: "AccountActionTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountActionTokens_UserId_Purpose_ExpiresAtUtc",
                table: "AccountActionTokens",
                columns: new[] { "UserId", "Purpose", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingInterchangeBatches_CompanyId_CommittedImportKey",
                table: "AccountingInterchangeBatches",
                columns: new[] { "CompanyId", "CommittedImportKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingInterchangeBatches_CompanyId_ProviderCode_EntityType_ProcessedAtUtc",
                table: "AccountingInterchangeBatches",
                columns: new[] { "CompanyId", "ProviderCode", "EntityType", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_CompanyId_StartsOn_EndsOn",
                table: "AccountingPeriods",
                columns: new[] { "CompanyId", "StartsOn", "EndsOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyId_Number",
                table: "Accounts",
                columns: new[] { "CompanyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyId_OperationalRole",
                table: "Accounts",
                columns: new[] { "CompanyId", "OperationalRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationAuditEntries_UserName_OccurredUtc",
                table: "AuthenticationAuditEntries",
                columns: new[] { "UserName", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_CompanyId_LedgerAccountId",
                table: "BankAccounts",
                columns: new[] { "CompanyId", "LedgerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationItems_BankReconciliationId_JournalEntryId",
                table: "BankReconciliationItems",
                columns: new[] { "BankReconciliationId", "JournalEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliations_BankAccountId_StatementDate",
                table: "BankReconciliations",
                columns: new[] { "BankAccountId", "StatementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImportBatches_BankAccountId",
                table: "BankStatementImportBatches",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImportBatches_CompanyId_BankAccountId_ContentSha256",
                table: "BankStatementImportBatches",
                columns: new[] { "CompanyId", "BankAccountId", "ContentSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementTransactions_BankAccountId",
                table: "BankStatementTransactions",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementTransactions_CompanyId_BankAccountId_ExternalId",
                table: "BankStatementTransactions",
                columns: new[] { "CompanyId", "BankAccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementTransactions_CompanyId_BankAccountId_Status_TransactionDate",
                table: "BankStatementTransactions",
                columns: new[] { "CompanyId", "BankAccountId", "Status", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementTransactions_ImportBatchId",
                table: "BankStatementTransactions",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementTransactions_MatchedJournalEntryId",
                table: "BankStatementTransactions",
                column: "MatchedJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_CompanyId_Reference",
                table: "BankTransfers",
                columns: new[] { "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_FromBankAccountId",
                table: "BankTransfers",
                column: "FromBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_InboundJournalEntryId",
                table: "BankTransfers",
                column: "InboundJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_InboundReversalJournalEntryId",
                table: "BankTransfers",
                column: "InboundReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_JournalEntryId",
                table: "BankTransfers",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_ReversalJournalEntryId",
                table: "BankTransfers",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_ToBankAccountId",
                table: "BankTransfers",
                column: "ToBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessAuditEntries_CompanyId_OccurredAtUtc",
                table: "BusinessAuditEntries",
                columns: new[] { "CompanyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name",
                table: "Companies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMemberships_UserId_CompanyId",
                table: "CompanyMemberships",
                columns: new[] { "UserId", "CompanyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationGroupCompanies_ConsolidationGroupId_MemberCompanyId",
                table: "ConsolidationGroupCompanies",
                columns: new[] { "ConsolidationGroupId", "MemberCompanyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationGroups_CompanyId_Name",
                table: "ConsolidationGroups",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyExchangeRates_CompanyId_BaseCurrency_QuoteCurrency_EffectiveOn",
                table: "CurrencyExchangeRates",
                columns: new[] { "CompanyId", "BaseCurrency", "QuoteCurrency", "EffectiveOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_CustomerNumber",
                table: "Customers",
                columns: new[] { "CompanyId", "CustomerNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrollDeductionElections_CompanyId_EmployeeId_PayrollDeductionPlanId_EffectiveOn",
                table: "EmployeePayrollDeductionElections",
                columns: new[] { "CompanyId", "EmployeeId", "PayrollDeductionPlanId", "EffectiveOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrollDeductionElections_EmployeeId",
                table: "EmployeePayrollDeductionElections",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrollDeductionElections_PayrollDeductionPlanId",
                table: "EmployeePayrollDeductionElections",
                column: "PayrollDeductionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId_EmployeeNumber",
                table: "Employees",
                columns: new[] { "CompanyId", "EmployeeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEntityLinks_IntegrationConnectionId_EntityType_LocalEntityId",
                table: "ExternalEntityLinks",
                columns: new[] { "IntegrationConnectionId", "EntityType", "LocalEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEntityLinks_IntegrationConnectionId_EntityType_ProviderEntityId",
                table: "ExternalEntityLinks",
                columns: new[] { "IntegrationConnectionId", "EntityType", "ProviderEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnections_CompanyId_ProviderCode_Name",
                table: "IntegrationConnections",
                columns: new[] { "CompanyId", "ProviderCode", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSyncRuns_CompanyId_IntegrationConnectionId_CompletedAtUtc",
                table: "IntegrationSyncRuns",
                columns: new[] { "CompanyId", "IntegrationConnectionId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_CompanyId_InventoryItemId_OccurredOn",
                table: "InventoryTransactions",
                columns: new[] { "CompanyId", "InventoryItemId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId_BankAccountId_PostedOn",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "BankAccountId", "PostedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId_SourceDocumentType_SourceDocumentId",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "SourceDocumentType", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId_Status_PostedOn",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "Status", "PostedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_CodeHash",
                table: "MfaRecoveryCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_UserId_UsedAtUtc",
                table: "MfaRecoveryCodes",
                columns: new[] { "UserId", "UsedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MfaSignInChallenges_TokenHash",
                table: "MfaSignInChallenges",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfaSignInChallenges_UserId_ExpiresAtUtc",
                table: "MfaSignInChallenges",
                columns: new[] { "UserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationAttempts_CompanyId_UserId_ExpiresAtUtc",
                table: "OAuthAuthorizationAttempts",
                columns: new[] { "CompanyId", "UserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationAttempts_StateHash",
                table: "OAuthAuthorizationAttempts",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBankOriginConfigurations_BankAccountId",
                table: "PayrollBankOriginConfigurations",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBankOriginConfigurations_CompanyId_BankAccountId_EffectiveOn",
                table: "PayrollBankOriginConfigurations",
                columns: new[] { "CompanyId", "BankAccountId", "EffectiveOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollClosePeriods_CompanyId_PeriodType_PeriodKey",
                table: "PayrollClosePeriods",
                columns: new[] { "CompanyId", "PeriodType", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDeductionLines_EmployeePayrollDeductionElectionId",
                table: "PayrollDeductionLines",
                column: "EmployeePayrollDeductionElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDeductionLines_PayrollDeductionPlanId",
                table: "PayrollDeductionLines",
                column: "PayrollDeductionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDeductionLines_PayrollRunEmployeeLineId_Sequence",
                table: "PayrollDeductionLines",
                columns: new[] { "PayrollRunEmployeeLineId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDeductionPlans_CompanyId_Code",
                table: "PayrollDeductionPlans",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDepositScheduleConfigurations_CompanyId_JurisdictionCode_ReturnFormCode_TaxYear",
                table: "PayrollDepositScheduleConfigurations",
                columns: new[] { "CompanyId", "JurisdictionCode", "ReturnFormCode", "TaxYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollDisasterReliefConfigurations_CompanyId_AnnouncementCode",
                table: "PayrollDisasterReliefConfigurations",
                columns: new[] { "CompanyId", "AnnouncementCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEarningLines_PayrollRunEmployeeLineId_Sequence",
                table: "PayrollEarningLines",
                columns: new[] { "PayrollRunEmployeeLineId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEarningLines_PayrollTimeEntryId",
                table: "PayrollEarningLines",
                column: "PayrollTimeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEmployeePayments_CompanyId_Status_IssuedAtUtc",
                table: "PayrollEmployeePayments",
                columns: new[] { "CompanyId", "Status", "IssuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEmployeePayments_EmployeeId",
                table: "PayrollEmployeePayments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEmployeePayments_PayrollRunEmployeeLineId",
                table: "PayrollEmployeePayments",
                column: "PayrollRunEmployeeLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEmployeePayments_PayrollRunId_EmployeeId",
                table: "PayrollEmployeePayments",
                columns: new[] { "PayrollRunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollFilingCorrections_CompanyId_OriginalPayrollFilingId_Sequence",
                table: "PayrollFilingCorrections",
                columns: new[] { "CompanyId", "OriginalPayrollFilingId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollFilingCorrections_OriginalPayrollFilingId",
                table: "PayrollFilingCorrections",
                column: "OriginalPayrollFilingId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollFilings_CompanyId_FormCode_PeriodKey",
                table: "PayrollFilings",
                columns: new[] { "CompanyId", "FormCode", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollJurisdictionRules_CompanyId_ResidenceJurisdiction_WorkJurisdiction",
                table: "PayrollJurisdictionRules",
                columns: new[] { "CompanyId", "ResidenceJurisdiction", "WorkJurisdiction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilities_CompanyId_Status_DueDate",
                table: "PayrollLiabilities",
                columns: new[] { "CompanyId", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilities_DepositScheduleConfigurationId",
                table: "PayrollLiabilities",
                column: "DepositScheduleConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilities_PayrollRunEmployeeLineId",
                table: "PayrollLiabilities",
                column: "PayrollRunEmployeeLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilities_PayrollRunId",
                table: "PayrollLiabilities",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilities_SourceType_SourceLineId",
                table: "PayrollLiabilities",
                columns: new[] { "SourceType", "SourceLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilityPaymentApplications_PayrollLiabilityId",
                table: "PayrollLiabilityPaymentApplications",
                column: "PayrollLiabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilityPaymentApplications_PayrollLiabilityPaymentId_PayrollLiabilityId",
                table: "PayrollLiabilityPaymentApplications",
                columns: new[] { "PayrollLiabilityPaymentId", "PayrollLiabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilityPayments_BankAccountId",
                table: "PayrollLiabilityPayments",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilityPayments_CompanyId_Reference",
                table: "PayrollLiabilityPayments",
                columns: new[] { "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilityPayments_JournalEntryId",
                table: "PayrollLiabilityPayments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLiabilityPayments_ReversalJournalEntryId",
                table: "PayrollLiabilityPayments",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPaymentFiles_CompanyId_GeneratedAtUtc",
                table: "PayrollPaymentFiles",
                columns: new[] { "CompanyId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPaymentFiles_CompanyId_PayrollRunId_Format",
                table: "PayrollPaymentFiles",
                columns: new[] { "CompanyId", "PayrollRunId", "Format" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPaymentFiles_PayrollBankOriginConfigurationId",
                table: "PayrollPaymentFiles",
                column: "PayrollBankOriginConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPaymentFiles_PayrollRunId",
                table: "PayrollPaymentFiles",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployeeLines_EmployeeId",
                table: "PayrollRunEmployeeLines",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployeeLines_PayrollRunId_EmployeeId",
                table: "PayrollRunEmployeeLines",
                columns: new[] { "PayrollRunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_BankAccountId",
                table: "PayrollRuns",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_CompanyId_Reference",
                table: "PayrollRuns",
                columns: new[] { "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_JournalEntryId",
                table: "PayrollRuns",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_ReversalJournalEntryId",
                table: "PayrollRuns",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSsaOriginalWageFiles_CompanyId_PayrollFilingId",
                table: "PayrollSsaOriginalWageFiles",
                columns: new[] { "CompanyId", "PayrollFilingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSsaOriginalWageFiles_PayrollFilingId",
                table: "PayrollSsaOriginalWageFiles",
                column: "PayrollFilingId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSsaOriginalWageFiles_PayrollSsaWageFileConfigurationId",
                table: "PayrollSsaOriginalWageFiles",
                column: "PayrollSsaWageFileConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSsaWageFileConfigurations_CompanyId_SpecificationTaxYear_FileKind",
                table: "PayrollSsaWageFileConfigurations",
                columns: new[] { "CompanyId", "SpecificationTaxYear", "FileKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSsaWageFiles_CompanyId_PayrollFilingCorrectionId",
                table: "PayrollSsaWageFiles",
                columns: new[] { "CompanyId", "PayrollFilingCorrectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSsaWageFiles_PayrollFilingCorrectionId",
                table: "PayrollSsaWageFiles",
                column: "PayrollFilingCorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSsaWageFiles_PayrollSsaWageFileConfigurationId",
                table: "PayrollSsaWageFiles",
                column: "PayrollSsaWageFileConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTaxLines_PayrollRunEmployeeLineId_ObligationCode_JurisdictionCode",
                table: "PayrollTaxLines",
                columns: new[] { "PayrollRunEmployeeLineId", "ObligationCode", "JurisdictionCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTaxLines_PayrollRunEmployeeLineId_Sequence",
                table: "PayrollTaxLines",
                columns: new[] { "PayrollRunEmployeeLineId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimecards_CompanyId_EmployeeId_PeriodStart_PeriodEnd_Status",
                table: "PayrollTimecards",
                columns: new[] { "CompanyId", "EmployeeId", "PeriodStart", "PeriodEnd", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimecards_EmployeeId",
                table: "PayrollTimecards",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimecards_PayrollRunId",
                table: "PayrollTimecards",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimeEntries_PayrollTimecardId_Sequence",
                table: "PayrollTimeEntries",
                columns: new[] { "PayrollTimecardId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimeEntries_ProjectJobId",
                table: "PayrollTimeEntries",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectJobs_CompanyId_JobNumber",
                table: "ProjectJobs",
                columns: new[] { "CompanyId", "JobNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CompanyId_OrderNumber",
                table: "PurchaseOrders",
                columns: new[] { "CompanyId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_RevenueAccountId",
                table: "SalesInvoiceLines",
                column: "RevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_SalesInvoiceId_Sequence",
                table: "SalesInvoiceLines",
                columns: new[] { "SalesInvoiceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CompanyId_InvoiceNumber",
                table: "SalesInvoices",
                columns: new[] { "CompanyId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_OrderNumber",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEmailOutboxMessages_Status_NextAttemptAtUtc_LeaseExpiresAtUtc",
                table: "SecurityEmailOutboxMessages",
                columns: new[] { "Status", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerAdjustments_BankAccountId",
                table: "SubledgerAdjustments",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerAdjustments_CompanyId_DocumentId",
                table: "SubledgerAdjustments",
                columns: new[] { "CompanyId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerAdjustments_CompanyId_Subledger_Reference",
                table: "SubledgerAdjustments",
                columns: new[] { "CompanyId", "Subledger", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerAdjustments_JournalEntryId",
                table: "SubledgerAdjustments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerAdjustments_PaymentId",
                table: "SubledgerAdjustments",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerAdjustments_ReversalJournalEntryId",
                table: "SubledgerAdjustments",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentNumber_IsRecurringTemplate",
                table: "SubledgerDocumentWorkflows",
                columns: new[] { "CompanyId", "DocumentType", "DocumentNumber", "IsRecurringTemplate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerDocumentWorkflows_CompanyId_Status_NextOccurrenceDate",
                table: "SubledgerDocumentWorkflows",
                columns: new[] { "CompanyId", "Status", "NextOccurrenceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerPaymentApplications_SubledgerPaymentId_DocumentId",
                table: "SubledgerPaymentApplications",
                columns: new[] { "SubledgerPaymentId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerPayments_BankAccountId",
                table: "SubledgerPayments",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerPayments_CompanyId_CounterpartyId_PaymentDate",
                table: "SubledgerPayments",
                columns: new[] { "CompanyId", "CounterpartyId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerPayments_CompanyId_Direction_Reference",
                table: "SubledgerPayments",
                columns: new[] { "CompanyId", "Direction", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerPayments_JournalEntryId",
                table: "SubledgerPayments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerPayments_ReversalJournalEntryId",
                table: "SubledgerPayments",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxContentPackages_CompanyId_PackageCode_Version",
                table: "TaxContentPackages",
                columns: new[] { "CompanyId", "PackageCode", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxFormRequirements_TaxRuleSetId_FormCode",
                table: "TaxFormRequirements",
                columns: new[] { "TaxRuleSetId", "FormCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRuleBrackets_TaxRuleSetId_Sequence",
                table: "TaxRuleBrackets",
                columns: new[] { "TaxRuleSetId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRuleFieldDefinitions_TaxRuleSetId_FieldCode",
                table: "TaxRuleFieldDefinitions",
                columns: new[] { "TaxRuleSetId", "FieldCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRuleParameters_TaxRuleSetId_ParameterCode",
                table: "TaxRuleParameters",
                columns: new[] { "TaxRuleSetId", "ParameterCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRuleSets_CompanyId_Code",
                table: "TaxRuleSets",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRuleTestCases_TaxRuleSetId_Name",
                table: "TaxRuleTestCases",
                columns: new[] { "TaxRuleSetId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxSourceCaptures_CompanyId_CapturedAtUtc",
                table: "TaxSourceCaptures",
                columns: new[] { "CompanyId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailLookupHash",
                table: "Users",
                column: "EmailLookupHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId_RevokedAtUtc_ExpiresAtUtc",
                table: "UserSessions",
                columns: new[] { "UserId", "RevokedAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ExpenseAccountId",
                table: "VendorBillLines",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_VendorBillId_Sequence",
                table: "VendorBillLines",
                columns: new[] { "VendorBillId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_CompanyId_BillNumber",
                table: "VendorBills",
                columns: new[] { "CompanyId", "BillNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_CompanyId_VendorNumber",
                table: "Vendors",
                columns: new[] { "CompanyId", "VendorNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!string.IsNullOrWhiteSpace(migrationBuilder.ActiveProvider))
                throw new NotSupportedException("Downgrading below the BrassLedger SQLite migration baseline would delete business data and is prohibited. Restore a verified pre-upgrade backup instead.");

            migrationBuilder.DropTable(
                name: "AccessRoles");

            migrationBuilder.DropTable(
                name: "AccountActionTokens");

            migrationBuilder.DropTable(
                name: "AccountingInterchangeBatches");

            migrationBuilder.DropTable(
                name: "AccountingPeriods");

            migrationBuilder.DropTable(
                name: "AuthenticationAuditEntries");

            migrationBuilder.DropTable(
                name: "BankReconciliationItems");

            migrationBuilder.DropTable(
                name: "BankReconciliations");

            migrationBuilder.DropTable(
                name: "BankStatementTransactions");

            migrationBuilder.DropTable(
                name: "BankTransfers");

            migrationBuilder.DropTable(
                name: "BusinessAuditEntries");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "CompanyMemberships");

            migrationBuilder.DropTable(
                name: "ConsolidationGroupCompanies");

            migrationBuilder.DropTable(
                name: "ConsolidationGroups");

            migrationBuilder.DropTable(
                name: "CurrencyExchangeRates");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "ExternalEntityLinks");

            migrationBuilder.DropTable(
                name: "IntegrationConnections");

            migrationBuilder.DropTable(
                name: "IntegrationSyncRuns");

            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "InventoryTransactions");

            migrationBuilder.DropTable(
                name: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "LabelTemplates");

            migrationBuilder.DropTable(
                name: "MfaRecoveryCodes");

            migrationBuilder.DropTable(
                name: "MfaSignInChallenges");

            migrationBuilder.DropTable(
                name: "OAuthAuthorizationAttempts");

            migrationBuilder.DropTable(
                name: "PayrollClosePeriods");

            migrationBuilder.DropTable(
                name: "PayrollDeductionLines");

            migrationBuilder.DropTable(
                name: "PayrollDisasterReliefConfigurations");

            migrationBuilder.DropTable(
                name: "PayrollEarningLines");

            migrationBuilder.DropTable(
                name: "PayrollEmployeePayments");

            migrationBuilder.DropTable(
                name: "PayrollJurisdictionRules");

            migrationBuilder.DropTable(
                name: "PayrollLiabilityPaymentApplications");

            migrationBuilder.DropTable(
                name: "PayrollPaymentFiles");

            migrationBuilder.DropTable(
                name: "PayrollSsaOriginalWageFiles");

            migrationBuilder.DropTable(
                name: "PayrollSsaWageFiles");

            migrationBuilder.DropTable(
                name: "PayrollTaxLines");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "ReportCatalogItems");

            migrationBuilder.DropTable(
                name: "SalesInvoiceLines");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropTable(
                name: "SecurityEmailOutboxMessages");

            migrationBuilder.DropTable(
                name: "SubledgerAdjustments");

            migrationBuilder.DropTable(
                name: "SubledgerDocumentWorkflows");

            migrationBuilder.DropTable(
                name: "SubledgerPaymentApplications");

            migrationBuilder.DropTable(
                name: "TaxContentPackages");

            migrationBuilder.DropTable(
                name: "TaxFormRequirements");

            migrationBuilder.DropTable(
                name: "TaxProfiles");

            migrationBuilder.DropTable(
                name: "TaxRuleBrackets");

            migrationBuilder.DropTable(
                name: "TaxRuleFieldDefinitions");

            migrationBuilder.DropTable(
                name: "TaxRuleParameters");

            migrationBuilder.DropTable(
                name: "TaxRuleSets");

            migrationBuilder.DropTable(
                name: "TaxRuleTestCases");

            migrationBuilder.DropTable(
                name: "TaxSourceCaptures");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "VendorBillLines");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropTable(
                name: "BankStatementImportBatches");

            migrationBuilder.DropTable(
                name: "EmployeePayrollDeductionElections");

            migrationBuilder.DropTable(
                name: "PayrollTimeEntries");

            migrationBuilder.DropTable(
                name: "PayrollLiabilities");

            migrationBuilder.DropTable(
                name: "PayrollLiabilityPayments");

            migrationBuilder.DropTable(
                name: "PayrollBankOriginConfigurations");

            migrationBuilder.DropTable(
                name: "PayrollFilingCorrections");

            migrationBuilder.DropTable(
                name: "PayrollSsaWageFileConfigurations");

            migrationBuilder.DropTable(
                name: "SalesInvoices");

            migrationBuilder.DropTable(
                name: "SubledgerPayments");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "VendorBills");

            migrationBuilder.DropTable(
                name: "PayrollDeductionPlans");

            migrationBuilder.DropTable(
                name: "PayrollTimecards");

            migrationBuilder.DropTable(
                name: "ProjectJobs");

            migrationBuilder.DropTable(
                name: "PayrollDepositScheduleConfigurations");

            migrationBuilder.DropTable(
                name: "PayrollRunEmployeeLines");

            migrationBuilder.DropTable(
                name: "PayrollFilings");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropTable(
                name: "JournalEntries");
        }
    }
}
