using BrassLedger.Application.Accounting;
using BrassLedger.Application.Taxation;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.SecurityAdministration;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf.IO;
using System.Text.Json;

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
        Assert.Equal("13", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM BrassLedgerSchemaVersions;"));
        Assert.Equal("13", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM BrassLedgerSchemaVersions WHERE Description LIKE 'Compatibility checkpoint recorded by EF migration baseline%';"));
        Assert.StartsWith("2026082513-", await ReadScalarAsync(connection, "SELECT VersionId FROM BrassLedgerSchemaVersions ORDER BY VersionId DESC LIMIT 1;"));
        Assert.Equal("40", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826014829_InitialCurrentSchema';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826025658_AddAccountingSchedules';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826033453_AddFixedAssetDisposals';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826052206_AddPurchaseReceiving';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826054520_AddSalesFulfillment';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826070149_AddSalesQuotes';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826073212_AddSalesOrderChangeControls';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826082201_AddInventoryLocations';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826090933_AddPickPackBackorders';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826102030_AddCustomerReturns';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826105518_AddPurchaseRequisitions';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826121304_AddSupplierReturns';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826124123_AddLandedCostAllocations';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826133347_SeparateSupplierReturnCreditValue';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826141924_AddControlledPurchaseInvoiceMatching';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826150956_ScopeVendorBillNumbersByVendor';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826160416_ScopeSubledgerVendorBillNumbersByVendor';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826164319_AddSubledgerRejectionWorkflow';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826172628_AddControlledJournalReview';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826181954_AddControlledPayrollReview';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826192156_AddProjectLedgerDimensions';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826210012_AddControlledProjectChangeOrders';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826225614_AddControlledProjectBilling';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827012549_AddProjectWipRevenueRecognition';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827035027_AddProjectPhaseCostCodeBudgets';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827042019_AddProjectPhaseCostCodeLineDimensions';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827042959_AddProjectBillingLineDimensions';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827055010_AddTrackingDimensions';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827062326_AddTrackingDimensionsToSourceLines';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827071655_AddEffectiveDatedConsolidationOwnership';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827080436_AddConsolidationAccountMappings';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827091057_AddControlledConsolidationTranslation';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827095525_AddControlledConsolidationAdjustments';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827120437_AddReviewedIntercompanyMatching';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827122336_ConstrainIntercompanyMatchMetadata';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827133355_AddExplicitConsolidationBasisAndNci';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827151409_AddConsolidatedCashFlowClassification';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827160327_AddConsolidatedStatementPresentation';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827185331_AddConsolidationDisclosurePackages';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827201050_AddConsolidationOwnershipEvents';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationTradingPartners';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationIntercompanyMatches';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationAdjustmentBatches';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationAdjustmentLines';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroupCompanies') WHERE name = 'ConsolidationBasis';"));
        Assert.Equal("3", await ReadScalarAsync(connection, "SELECT dflt_value FROM pragma_table_info('ConsolidationGroupCompanies') WHERE name = 'ConsolidationBasis';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroups') WHERE name = 'NciAccountNumber';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAdjustmentBatches') WHERE name = 'SubjectCompanyId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_ConsolidationAdjustmentBatches_ControlKey';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('CurrencyExchangeRates') WHERE name = 'RateType';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAccountMappings') WHERE name = 'TranslationMethod';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAccountMappings') WHERE name = 'CashFlowActivity';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAccountMappings') WHERE name = 'CashFlowRationale';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAccountMappings') WHERE name = 'CashFlowReviewedOn';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationStatementPresentations') WHERE name = 'SectionCode';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationStatementPresentations') WHERE name = 'ReviewedOn';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationDisclosurePackages') WHERE name = 'ContentJson';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_ConsolidationDisclosurePackages_ConsolidationGroupId_PeriodStart_AsOf_FrameworkCode';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationOwnershipEvents') WHERE name = 'ContentJson';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_ConsolidationOwnershipEvents_ConsolidationGroupId_Reference';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationAccountMappings';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroupCompanies') WHERE name = 'EffectiveFrom';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroupCompanies') WHERE name = 'EffectiveThrough';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProjectPhases';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProjectCostCodes';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProjectBudgetAllocations';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('JournalEntryLines') WHERE name = 'ProjectPhaseId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('PayrollEarningLines') WHERE name = 'ProjectCostCodeId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ProjectBillingLines') WHERE name = 'ProjectPhaseId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'TrackingDimensionValues';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('JournalEntryLines') WHERE name = 'DepartmentId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('JournalEntryLines') WHERE name = 'ClassId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('SalesInvoiceLines') WHERE name = 'DepartmentId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('VendorBillLines') WHERE name = 'ClassId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('PayrollTimeEntries') WHERE name = 'DepartmentId';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProjectWipSchedules';"));
        Assert.Equal("AsBilled", await ReadScalarAsync(connection, "SELECT RevenueRecognitionMethod FROM ProjectJobs ORDER BY JobNumber LIMIT 1;"));
        Assert.Equal("ContractAsset", await ReadScalarAsync(connection, "SELECT OperationalRole FROM Accounts WHERE Number = '1120';"));
        Assert.Equal("ContractLiability", await ReadScalarAsync(connection, "SELECT OperationalRole FROM Accounts WHERE Number = '2040';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProjectBillingProposals';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProjectChangeOrders';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AccountingSchedules';"));
        Assert.Equal("AccountsReceivable", await ReadScalarAsync(connection, "SELECT OperationalRole FROM Accounts WHERE Number = '1100';"));
        Assert.Equal("1", await ReadScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Accounts_CompanyId_OperationalRole';"));
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
                DROP TABLE "__EFMigrationsHistory";
                DROP TABLE "AccountingScheduleInstallments";
                DROP TABLE "AccountingSchedules";
                ALTER TABLE "PayrollTimeEntries" DROP COLUMN "W2ReportingJson";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await services.InitializeBrassLedgerAsync();

        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("13", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM BrassLedgerSchemaVersions;"));
        Assert.Equal("40", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826025658_AddAccountingSchedules';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826033453_AddFixedAssetDisposals';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826052206_AddPurchaseReceiving';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826054520_AddSalesFulfillment';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826070149_AddSalesQuotes';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826073212_AddSalesOrderChangeControls';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826082201_AddInventoryLocations';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826090933_AddPickPackBackorders';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826102030_AddCustomerReturns';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826105518_AddPurchaseRequisitions';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826121304_AddSupplierReturns';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826124123_AddLandedCostAllocations';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826133347_SeparateSupplierReturnCreditValue';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826141924_AddControlledPurchaseInvoiceMatching';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826150956_ScopeVendorBillNumbersByVendor';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826160416_ScopeSubledgerVendorBillNumbersByVendor';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826164319_AddSubledgerRejectionWorkflow';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826172628_AddControlledJournalReview';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826181954_AddControlledPayrollReview';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826192156_AddProjectLedgerDimensions';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826210012_AddControlledProjectChangeOrders';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826225614_AddControlledProjectBilling';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827012549_AddProjectWipRevenueRecognition';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827035027_AddProjectPhaseCostCodeBudgets';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827042019_AddProjectPhaseCostCodeLineDimensions';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827042959_AddProjectBillingLineDimensions';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827055010_AddTrackingDimensions';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827062326_AddTrackingDimensionsToSourceLines';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827071655_AddEffectiveDatedConsolidationOwnership';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827080436_AddConsolidationAccountMappings';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827091057_AddControlledConsolidationTranslation';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827095525_AddControlledConsolidationAdjustments';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827120437_AddReviewedIntercompanyMatching';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827122336_ConstrainIntercompanyMatchMetadata';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827133355_AddExplicitConsolidationBasisAndNci';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827151409_AddConsolidatedCashFlowClassification';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827160327_AddConsolidatedStatementPresentation';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827185331_AddConsolidationDisclosurePackages';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260827201050_AddConsolidationOwnershipEvents';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationAdjustmentBatches';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationTradingPartners';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationIntercompanyMatches';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroupCompanies') WHERE name = 'ConsolidationBasis';"));
        Assert.Equal("3", await ReadScalarAsync(verified, "SELECT dflt_value FROM pragma_table_info('ConsolidationGroupCompanies') WHERE name = 'ConsolidationBasis';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroups') WHERE name = 'NciAccountNumber';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAdjustmentBatches') WHERE name = 'ControlKey';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_ConsolidationAdjustmentBatches_ControlKey';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('CurrencyExchangeRates') WHERE name = 'RateType';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAccountMappings') WHERE name = 'CashFlowActivity';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAccountMappings') WHERE name = 'CashFlowRationale';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationAccountMappings') WHERE name = 'CashFlowReviewedOn';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationStatementPresentations') WHERE name = 'SectionCode';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationStatementPresentations') WHERE name = 'ReviewedOn';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationDisclosurePackages') WHERE name = 'ContentJson';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationOwnershipEvents') WHERE name = 'ContentJson';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroups') WHERE name = 'CtaAccountNumber';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConsolidationAccountMappings';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroupCompanies') WHERE name = 'EffectiveFrom';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ConsolidationGroups') WHERE name = 'ConcurrencyToken';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('SalesOrderLines') WHERE name = 'ClassId';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('ProjectBillingLines') WHERE name = 'DepartmentId';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProjectWipSchedules';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AccountingInterchangeBatches';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MfaSignInChallenges';"));
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
                DELETE FROM "BrassLedgerSchemaVersions" WHERE "VersionId" LIKE '2026082513-%' OR "VersionId" LIKE '2026082512-%' OR "VersionId" LIKE '2026082511-%' OR "VersionId" LIKE '2026082510-%' OR "VersionId" LIKE '2026082509-%' OR "VersionId" LIKE '2026082508-%' OR "VersionId" LIKE '2026082507-%' OR "VersionId" LIKE '2026082506-%' OR "VersionId" LIKE '2026082505-%' OR "VersionId" LIKE '2026082504-%' OR "VersionId" LIKE '2026082503-%' OR "VersionId" LIKE '2026082502-%';
                DROP INDEX "IX_Accounts_CompanyId_OperationalRole";
                ALTER TABLE "Accounts" DROP COLUMN "OperationalRole";
                DROP TABLE "UserSessions";
                DROP TABLE "OAuthAuthorizationAttempts";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialVersion";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialOperationLeaseId";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialOperation";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialOperationLeaseExpiresAtUtc";
                DROP TABLE "ExternalEntityLinks";
                DROP TABLE "IntegrationSyncRuns";
                ALTER TABLE "PayrollEarningLines" DROP COLUMN "W2ReportingJson";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await services.InitializeBrassLedgerAsync();

        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("13", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM BrassLedgerSchemaVersions;"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('PayrollEarningLines') WHERE name = 'W2ReportingJson';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AccountingInterchangeBatches';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MfaRecoveryCodes';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'UserSessions';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'OAuthAuthorizationAttempts';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('IntegrationConnections') WHERE name = 'CredentialVersion';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('IntegrationConnections') WHERE name = 'CredentialOperationLeaseId';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('IntegrationConnections') WHERE name = 'CredentialOperationLeaseExpiresAtUtc';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ExternalEntityLinks';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'IntegrationSyncRuns';"));
        Assert.Equal("AccountsReceivable", await ReadScalarAsync(verified, "SELECT OperationalRole FROM Accounts WHERE Number = '1100';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Accounts_CompanyId_OperationalRole';"));
        Assert.Equal("Brass Ledger Manufacturing", await ReadScalarAsync(verified, "SELECT Name FROM Companies WHERE Name = 'Brass Ledger Manufacturing';"));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_UpgradesPreMfaSchemaWithoutLosingOperators()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM "BrassLedgerSchemaVersions" WHERE "VersionId" LIKE '2026082513-%' OR "VersionId" LIKE '2026082512-%' OR "VersionId" LIKE '2026082511-%' OR "VersionId" LIKE '2026082510-%' OR "VersionId" LIKE '2026082509-%' OR "VersionId" LIKE '2026082508-%' OR "VersionId" LIKE '2026082507-%' OR "VersionId" LIKE '2026082506-%' OR "VersionId" LIKE '2026082505-%' OR "VersionId" LIKE '2026082504-%';
                DROP INDEX "IX_Accounts_CompanyId_OperationalRole";
                ALTER TABLE "Accounts" DROP COLUMN "OperationalRole";
                DROP TABLE "UserSessions";
                DROP TABLE "OAuthAuthorizationAttempts";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialVersion";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialOperationLeaseId";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialOperation";
                ALTER TABLE "IntegrationConnections" DROP COLUMN "CredentialOperationLeaseExpiresAtUtc";
                DROP TABLE "ExternalEntityLinks";
                DROP TABLE "IntegrationSyncRuns";
                DROP TABLE "SecurityEmailOutboxMessages";
                DROP TABLE "AccountActionTokens";
                DROP TABLE "MfaRecoveryCodes";
                DROP TABLE "MfaSignInChallenges";
                ALTER TABLE "Users" DROP COLUMN "MfaEnabled";
                ALTER TABLE "Users" DROP COLUMN "MfaSecret";
                ALTER TABLE "Users" DROP COLUMN "MfaEnrolledAtUtc";
                ALTER TABLE "Users" DROP COLUMN "MfaLastAcceptedTimeStep";
                ALTER TABLE "Users" DROP COLUMN "MfaFailedAttemptCount";
                ALTER TABLE "Users" DROP COLUMN "MfaLockoutEndUtc";
                ALTER TABLE "AccessRoles" DROP COLUMN "RequiresMfa";
                ALTER TABLE "Users" DROP COLUMN "EmailConfirmedAtUtc";
                DROP INDEX "IX_Users_EmailLookupHash";
                ALTER TABLE "Users" DROP COLUMN "EmailLookupHash";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await services.InitializeBrassLedgerAsync();

        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("13", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM BrassLedgerSchemaVersions;"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'MfaSecret';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MfaSignInChallenges';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('AccessRoles') WHERE name = 'RequiresMfa';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AccountActionTokens';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'UserSessions';"));
        Assert.Equal("64", await ReadScalarAsync(verified, "SELECT length(EmailLookupHash) FROM Users WHERE UserName = 'controller';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT RequiresMfa FROM AccessRoles WHERE Name = 'Administrator';"));
        Assert.Equal("controller", await ReadScalarAsync(verified, "SELECT UserName FROM Users WHERE UserName = 'controller';"));
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
    public async Task InitializeBrassLedgerAsync_RejectsUnknownEfMigrationWithoutChangingData()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('99999999999999_FutureMigration', '99.0.0');""";
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => services.InitializeBrassLedgerAsync());

        Assert.Contains("unsupported or newer EF migration", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automatic downgrade is prohibited", exception.Message, StringComparison.OrdinalIgnoreCase);
        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("Brass Ledger Manufacturing", await ReadScalarAsync(verified, "SELECT Name FROM Companies WHERE Name = 'Brass Ledger Manufacturing';"));
    }

    [Fact]
    public async Task InitializeBrassLedgerAsync_RefusesNonemptyUnknownDatabaseWithoutModifyingIt()
    {
        var dataDirectory = Path.Combine(_contentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE OtherApplicationData (Value TEXT NOT NULL); INSERT INTO OtherApplicationData (Value) VALUES ('preserve-me');";
            await command.ExecuteNonQueryAsync();
        }

        using var services = CreateServiceProvider();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => services.InitializeBrassLedgerAsync());

        Assert.Contains("not empty", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refused to modify", exception.Message, StringComparison.OrdinalIgnoreCase);
        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("preserve-me", await ReadScalarAsync(verified, "SELECT Value FROM OtherApplicationData;"));
        Assert.Equal("0", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('BrassLedgerSchemaVersions', '__EFMigrationsHistory');"));
    }

    [Fact]
    public async Task MigrationBaseline_RefusesDestructiveDowngradeAndRetainsBusinessData()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using (var scope = services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var migrator = db.Database.GetService<IMigrator>();
            var exception = await Assert.ThrowsAsync<NotSupportedException>(() => migrator.MigrateAsync("0"));
            Assert.Contains("could delete", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prohibited", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using var verified = new SqliteConnection($"Data Source={databasePath}");
        await verified.OpenAsync();
        Assert.Equal("Brass Ledger Manufacturing", await ReadScalarAsync(verified, "SELECT Name FROM Companies WHERE Name = 'Brass Ledger Manufacturing';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AccountingSchedules';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826014829_InitialCurrentSchema';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826025658_AddAccountingSchedules';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826033453_AddFixedAssetDisposals';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826052206_AddPurchaseReceiving';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826054520_AddSalesFulfillment';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826070149_AddSalesQuotes';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826073212_AddSalesOrderChangeControls';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826082201_AddInventoryLocations';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260826090933_AddPickPackBackorders';"));
        Assert.Equal("1", await ReadScalarAsync(verified, "SELECT COUNT(*) FROM pragma_table_info('AccountingSchedules') WHERE name = 'DisposalJournalEntryId';"));
    }

    [Fact]
    public async Task SubledgerScopeMigration_BackfillsHistoricalInvoiceAndVendorBillIdentity()
    {
        using var services = CreateServiceProvider();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260826150956_ScopeVendorBillNumbersByVendor");
        var companyId = Guid.NewGuid(); var vendorId = Guid.NewGuid(); var billWorkflowId = Guid.NewGuid(); var invoiceWorkflowId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var billPayload = System.Text.Json.JsonSerializer.Serialize(new CreateVendorBillRequest(vendorId, "LEGACY-1001", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 10m, "5100", "Legacy bill"));
        var invoicePayload = System.Text.Json.JsonSerializer.Serialize(new CreateInvoiceRequest(Guid.NewGuid(), "LEGACY-INV", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 10m, 0m, "4000", "Legacy invoice"));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubledgerDocumentWorkflows" ("Id", "CompanyId", "DocumentType", "DocumentNumber", "PayloadJson", "Status", "IsRecurringTemplate", "Frequency", "FrequencyInterval", "CreatedAtUtc", "ConcurrencyToken")
            VALUES ({billWorkflowId}, {companyId}, {"VendorBill"}, {"LEGACY-1001"}, {billPayload}, {"Draft"}, {false}, {string.Empty}, {1}, {createdAt}, {Guid.NewGuid().ToString("N")});
            INSERT INTO "SubledgerDocumentWorkflows" ("Id", "CompanyId", "DocumentType", "DocumentNumber", "PayloadJson", "Status", "IsRecurringTemplate", "Frequency", "FrequencyInterval", "CreatedAtUtc", "ConcurrencyToken")
            VALUES ({invoiceWorkflowId}, {companyId}, {"Invoice"}, {"LEGACY-INV"}, {invoicePayload}, {"Draft"}, {false}, {string.Empty}, {1}, {createdAt}, {Guid.NewGuid().ToString("N")});
            """);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(vendorId.ToString("N"), await db.SubledgerDocumentWorkflows.Where(item => item.Id == billWorkflowId).Select(item => item.DocumentScope).SingleAsync());
        Assert.Equal("company", await db.SubledgerDocumentWorkflows.Where(item => item.Id == invoiceWorkflowId).Select(item => item.DocumentScope).SingleAsync());
    }

    [Fact]
    public async Task SalesFulfillmentMigration_PreservesHeaderOnlyOrdersAsNonFulfillableReferences()
    {
        using var services = CreateServiceProvider();
        var companyId = Guid.NewGuid(); var customerId = Guid.NewGuid(); var orderId = Guid.NewGuid();
        using (var scope = services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.GetService<IMigrator>().MigrateAsync("20260826052206_AddPurchaseReceiving");
            await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "Companies" ("Id", "Name", "LegalName", "TaxId", "BaseCurrency", "FiscalYearStartMonth") VALUES ({companyId}, {"Migration Test Company"}, {"Migration Test Company LLC"}, {"00-0000000"}, {"USD"}, {1});""");
            await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "Customers" ("Id", "CompanyId", "CustomerNumber", "Name", "Email", "State", "CreditLimit", "OpenBalance") VALUES ({customerId}, {companyId}, {"C-LEGACY"}, {"Legacy Customer"}, {"legacy@example.test"}, {"MI"}, {1000m}, {0m});""");
            await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "SalesOrders" ("Id", "CompanyId", "CustomerId", "OrderNumber", "OrderedOn", "Status", "TotalAmount") VALUES ({orderId}, {companyId}, {customerId}, {"SO-LEGACY-1"}, {new DateOnly(2026, 7, 1)}, {"Open"}, {125m});""");
        }

        await services.InitializeBrassLedgerAsync();

        using var verifiedScope = services.CreateScope(); var verifiedFactory = verifiedScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var verified = await verifiedFactory.CreateDbContextAsync();
        var order = await verified.SalesOrders.SingleAsync(candidate => candidate.Id == orderId);
        Assert.Equal("LegacyReference", order.Status); Assert.Contains("header-only", order.Notes, StringComparison.OrdinalIgnoreCase); Assert.StartsWith("legacy-", order.ConcurrencyToken); Assert.False(await verified.SalesOrderLines.AnyAsync(line => line.SalesOrderId == order.Id));
        Assert.Contains("20260826054520_AddSalesFulfillment", await verified.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260826070149_AddSalesQuotes", await verified.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260826073212_AddSalesOrderChangeControls", await verified.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260826082201_AddInventoryLocations", await verified.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260826090933_AddPickPackBackorders", await verified.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await verified.SalesQuotes.ToListAsync());
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
    public async Task Consolidation_UsesEffectiveInverseRateAndPostedAsOfBalances()
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
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, signedInOwner.User!.UserId.ToString()), new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, signedInOwner.User.CompanyId.ToString()), new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ReportingManage)], "test"));
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
        var averageRate = await consolidation.SaveExchangeRateAsync(new SaveExchangeRateRequest("USD", "CAD", 1.10m, new DateOnly(2026, 12, 31), "Test average", RateType: "Average", PeriodStartOn: new DateOnly(2026, 1, 1)));
        Assert.True(averageRate.Succeeded, averageRate.ErrorMessage);
        var historicalRate = await consolidation.SaveExchangeRateAsync(new SaveExchangeRateRequest("USD", "CAD", 1m, DateOnly.MinValue, "Test historical", RateType: "Historical"));
        Assert.True(historicalRate.Succeeded, historicalRate.ErrorMessage);
        var overlappingAverage = await consolidation.SaveExchangeRateAsync(new SaveExchangeRateRequest("USD", "CAD", 1.11m, new DateOnly(2026, 6, 30), "Overlapping average", RateType: "Average", PeriodStartOn: new DateOnly(2026, 4, 1)));
        Assert.False(overlappingAverage.Succeeded);
        Assert.Contains("cannot overlap", overlappingAverage.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var basisReviewedOn = new DateOnly(2026, 1, 2);
        var group = await consolidation.SaveGroupAsync(new SaveConsolidationGroupRequest(null, "North America", "USD",
            [new ConsolidationMemberRequest(currentCompanyId, ConsolidationBasis: nameof(ConsolidationBasis.ReportingParent)), new ConsolidationMemberRequest(canadianCompanyId, .8m, ConsolidationBasis: nameof(ConsolidationBasis.ProportionateInterest), BasisRationale: "Reviewed management-reporting proportionate interest", BasisReviewedOn: basisReviewedOn)],
            CtaAccountNumber: "39999", CtaAccountName: "Cumulative translation adjustment", NciAccountNumber: "39997", NciAccountName: "Noncontrolling interests"));
        Assert.True(group.Succeeded, group.ErrorMessage);
        var configuredGroups = await consolidation.GetGroupsAsync();
        var configuredGroup = Assert.Single(configuredGroups, item => item.Id == group.Id);
        Assert.Equal(2, configuredGroup.Members.Count);
        var mappingWorkspace = await consolidation.GetAccountMappingWorkspaceAsync(group.Id!.Value);
        Assert.NotNull(mappingWorkspace);
        HashSet<Guid> cashLedgerAccountIds;
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
            cashLedgerAccountIds = (await db.BankAccounts.Where(bank => bank.CompanyId == currentCompanyId || bank.CompanyId == canadianCompanyId).Select(bank => bank.LedgerAccountId).ToListAsync()).ToHashSet();
        foreach (var sourceAccount in mappingWorkspace!.SourceAccounts)
        {
            var cashFlowActivity = cashLedgerAccountIds.Contains(sourceAccount.AccountId) ? nameof(ConsolidationCashFlowActivity.Unclassified) : sourceAccount.AccountType switch
            {
                nameof(AccountType.Asset) => nameof(ConsolidationCashFlowActivity.Investing),
                nameof(AccountType.Liability) or nameof(AccountType.Equity) => nameof(ConsolidationCashFlowActivity.Financing),
                _ => nameof(ConsolidationCashFlowActivity.Operating)
            };
            var mapping = await consolidation.SaveAccountMappingAsync(new SaveConsolidationAccountMappingRequest(null, group.Id.Value, sourceAccount.CompanyId, sourceAccount.AccountId, sourceAccount.AccountNumber, sourceAccount.AccountName, DateOnly.MinValue, null, CashFlowActivity: cashFlowActivity,
                CashFlowRationale: cashFlowActivity == nameof(ConsolidationCashFlowActivity.Unclassified) ? string.Empty : "Reviewed source-account cash-flow classification for the test business", CashFlowReviewedOn: cashFlowActivity == nameof(ConsolidationCashFlowActivity.Unclassified) ? null : basisReviewedOn));
            Assert.True(mapping.Succeeded, mapping.ErrorMessage);
        }
        var classifiedMappingWorkspace = await consolidation.GetAccountMappingWorkspaceAsync(group.Id.Value); Assert.NotNull(classifiedMappingWorkspace);
        Assert.Contains(classifiedMappingWorkspace!.Mappings, mapping => mapping.CashFlowActivity == nameof(ConsolidationCashFlowActivity.Operating));
        Assert.Contains(classifiedMappingWorkspace.Mappings, mapping => mapping.CashFlowActivity == nameof(ConsolidationCashFlowActivity.Investing));
        Assert.Contains(classifiedMappingWorkspace.Mappings, mapping => mapping.CashFlowActivity == nameof(ConsolidationCashFlowActivity.Financing));
        var cashMapping = classifiedMappingWorkspace.Mappings.First(mapping => cashLedgerAccountIds.Contains(mapping.AccountId));
        var invalidCashClassification = await consolidation.SaveAccountMappingAsync(new(cashMapping.Id, group.Id.Value, cashMapping.CompanyId, cashMapping.AccountId, cashMapping.ReportingAccountNumber, cashMapping.ReportingAccountName, cashMapping.EffectiveFrom, cashMapping.EffectiveThrough, cashMapping.IsActive, cashMapping.ConcurrencyToken, cashMapping.TranslationMethod, nameof(ConsolidationCashFlowActivity.Operating), "Incorrectly classify a bank account", basisReviewedOn));
        Assert.False(invalidCashClassification.Succeeded); Assert.Contains("noncash counterpart", invalidCashClassification.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var classifiedMapping = classifiedMappingWorkspace.Mappings.First(mapping => mapping.CashFlowActivity != nameof(ConsolidationCashFlowActivity.Unclassified));
        var missingClassificationEvidence = await consolidation.SaveAccountMappingAsync(new(classifiedMapping.Id, group.Id.Value, classifiedMapping.CompanyId, classifiedMapping.AccountId, classifiedMapping.ReportingAccountNumber, classifiedMapping.ReportingAccountName, classifiedMapping.EffectiveFrom, classifiedMapping.EffectiveThrough, classifiedMapping.IsActive, classifiedMapping.ConcurrencyToken, classifiedMapping.TranslationMethod, classifiedMapping.CashFlowActivity));
        Assert.False(missingClassificationEvidence.Succeeded); Assert.Contains("rationale", missingClassificationEvidence.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
        {
            var foreignAsset = await db.Accounts.FirstAsync(account => account.CompanyId == canadianCompanyId && account.Type == AccountType.Asset);
            var foreignEquity = await db.Accounts.FirstAsync(account => account.CompanyId == canadianCompanyId && account.Type == AccountType.Equity);
            var foreignRevenue = await db.Accounts.FirstAsync(account => account.CompanyId == canadianCompanyId && account.Type == AccountType.Revenue);
            var foreignJournal = new JournalEntry { Id = Guid.NewGuid(), CompanyId = canadianCompanyId, PostedOn = new DateOnly(2026, 2, 1), Reference = "CAD-CTA-TEST", Description = "Different closing, average, and historical rates create CTA", TotalAmount = 150m, Status = "Posted", IsPosted = true };
            db.JournalEntries.Add(foreignJournal);
            db.JournalEntryLines.AddRange(
                new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = foreignJournal.Id, AccountId = foreignAsset.Id, Debit = 150m, Description = foreignJournal.Description },
                new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = foreignJournal.Id, AccountId = foreignEquity.Id, Credit = 100m, Description = foreignJournal.Description },
                new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = foreignJournal.Id, AccountId = foreignRevenue.Id, Credit = 50m, Description = foreignJournal.Description });
            await db.SaveChangesAsync();
        }
        var firstSource = mappingWorkspace.SourceAccounts.First();
        var overlappingMapping = await consolidation.SaveAccountMappingAsync(new SaveConsolidationAccountMappingRequest(null, group.Id.Value, firstSource.CompanyId, firstSource.AccountId, firstSource.AccountNumber, firstSource.AccountName, new DateOnly(2026, 1, 1), null));
        Assert.False(overlappingMapping.Succeeded);
        Assert.Contains("cannot overlap", overlappingMapping.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var originalParentOwnership = configuredGroup.Members.Single(member => member.CompanyId == currentCompanyId);
        var overlapping = await consolidation.SaveOwnershipPeriodAsync(new SaveConsolidationOwnershipPeriodRequest(null, group.Id!.Value, currentCompanyId, .5m, new DateOnly(2026, 4, 1), null));
        Assert.False(overlapping.Succeeded);
        Assert.Contains("cannot overlap", overlapping.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var removeReportingParent = await consolidation.SaveOwnershipPeriodAsync(new SaveConsolidationOwnershipPeriodRequest(originalParentOwnership.Id, group.Id.Value, currentCompanyId, 1m, originalParentOwnership.EffectiveFrom, originalParentOwnership.EffectiveThrough, originalParentOwnership.ConcurrencyToken, nameof(ConsolidationBasis.CombinedAffiliate), "Invalid attempt to remove reporting parent", basisReviewedOn));
        Assert.False(removeReportingParent.Succeeded);
        Assert.Contains("reporting-parent", removeReportingParent.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var closeOriginalPeriod = await consolidation.SaveOwnershipPeriodAsync(new SaveConsolidationOwnershipPeriodRequest(originalParentOwnership.Id, group.Id.Value, currentCompanyId, 1m, originalParentOwnership.EffectiveFrom, new DateOnly(2026, 5, 31), originalParentOwnership.ConcurrencyToken, originalParentOwnership.ConsolidationBasis, originalParentOwnership.BasisRationale, originalParentOwnership.BasisReviewedOn));
        Assert.True(closeOriginalPeriod.Succeeded, closeOriginalPeriod.ErrorMessage);
        var staleOwnershipEdit = await consolidation.SaveOwnershipPeriodAsync(new SaveConsolidationOwnershipPeriodRequest(originalParentOwnership.Id, group.Id.Value, currentCompanyId, 1m, originalParentOwnership.EffectiveFrom, new DateOnly(2026, 4, 30), originalParentOwnership.ConcurrencyToken, originalParentOwnership.ConsolidationBasis, originalParentOwnership.BasisRationale, originalParentOwnership.BasisReviewedOn));
        Assert.False(staleOwnershipEdit.Succeeded);
        Assert.Contains("changed", staleOwnershipEdit.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var revisedParentOwnership = (await consolidation.GetGroupsAsync()).Single(item => item.Id == group.Id).Members.Single(member => member.Id == originalParentOwnership.Id);
        var movedOwnership = await consolidation.SaveOwnershipPeriodAsync(new SaveConsolidationOwnershipPeriodRequest(revisedParentOwnership.Id, group.Id.Value, canadianCompanyId, revisedParentOwnership.OwnershipPercentage, revisedParentOwnership.EffectiveFrom, revisedParentOwnership.EffectiveThrough, revisedParentOwnership.ConcurrencyToken, revisedParentOwnership.ConsolidationBasis, revisedParentOwnership.BasisRationale, revisedParentOwnership.BasisReviewedOn));
        Assert.False(movedOwnership.Succeeded);
        Assert.Contains("cannot be moved", movedOwnership.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var invalidSuccessorOwnership = await consolidation.SaveOwnershipPeriodAsync(new SaveConsolidationOwnershipPeriodRequest(null, group.Id.Value, currentCompanyId, .5m, new DateOnly(2026, 6, 1), null, ConsolidationBasis: nameof(ConsolidationBasis.ProportionateInterest), BasisRationale: "Invalid successor without reporting-parent coverage", BasisReviewedOn: basisReviewedOn));
        Assert.False(invalidSuccessorOwnership.Succeeded);
        Assert.Contains("reporting-parent", invalidSuccessorOwnership.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var successorOwnership = await consolidation.SaveOwnershipPeriodAsync(new SaveConsolidationOwnershipPeriodRequest(null, group.Id.Value, currentCompanyId, 1m, new DateOnly(2026, 6, 1), null, ConsolidationBasis: nameof(ConsolidationBasis.ReportingParent)));
        Assert.True(successorOwnership.Succeeded, successorOwnership.ErrorMessage);
        var report = await consolidation.GetBalanceReportAsync(group.Id!.Value, new DateOnly(2026, 5, 1));
        Assert.NotNull(report);
        Assert.Equal(new DateOnly(2026, 1, 1), report!.PeriodStart);
        Assert.Empty(report.Warnings);
        Assert.NotEmpty(report.Accounts);
        Assert.Equal(-20.36m, report.TranslationAdjustment);
        Assert.Equal(-20.36m, report.Accounts.Single(account => account.AccountNumber == "39999").ConvertedBalance);
        Assert.Contains(report.Accounts, account => account.AccountType == "Revenue" && account.TranslationMethod == "Average");
        Assert.Equal(0m, report.Accounts.Sum(account => account.AccountType is "Asset" or "Expense" ? account.ConvertedBalance : -account.ConvertedBalance));
        var averageSnapshot = (await consolidation.GetExchangeRatesAsync()).Single(item => item.Id == averageRate.Id);
        var retractAverage = await consolidation.SaveExchangeRateAsync(new SaveExchangeRateRequest(averageSnapshot.BaseCurrency, averageSnapshot.QuoteCurrency, averageSnapshot.Rate, averageSnapshot.EffectiveOn, averageSnapshot.Source, averageSnapshot.Id, averageSnapshot.RateType, averageSnapshot.PeriodStartOn, averageSnapshot.SourceReference, averageSnapshot.RetrievedOn, false, averageSnapshot.ConcurrencyToken));
        Assert.True(retractAverage.Succeeded, retractAverage.ErrorMessage);
        var missingAverageReport = await consolidation.GetBalanceReportAsync(group.Id.Value, new DateOnly(2026, 5, 1));
        Assert.Contains(missingAverageReport!.Warnings, warning => warning.Contains("average", StringComparison.OrdinalIgnoreCase) && warning.Contains("excluded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(missingAverageReport.Warnings, warning => warning.Contains("CTA was not calculated", StringComparison.OrdinalIgnoreCase));
        var retractedAverage = (await consolidation.GetExchangeRatesAsync()).Single(item => item.Id == averageRate.Id);
        var restoreAverage = await consolidation.SaveExchangeRateAsync(new SaveExchangeRateRequest(retractedAverage.BaseCurrency, retractedAverage.QuoteCurrency, retractedAverage.Rate, retractedAverage.EffectiveOn, retractedAverage.Source, retractedAverage.Id, retractedAverage.RateType, retractedAverage.PeriodStartOn, retractedAverage.SourceReference, retractedAverage.RetrievedOn, true, retractedAverage.ConcurrencyToken));
        Assert.True(restoreAverage.Succeeded, restoreAverage.ErrorMessage);

        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
        {
            var futureAsset = new GeneralLedgerAccount { Id = Guid.NewGuid(), CompanyId = currentCompanyId, Number = "19998", Name = "Future consolidation asset", Type = AccountType.Asset, IsActive = false, CurrentBalance = 125m };
            var futureEquity = new GeneralLedgerAccount { Id = Guid.NewGuid(), CompanyId = currentCompanyId, Number = "39998", Name = "Future consolidation equity", Type = AccountType.Equity, IsActive = false, CurrentBalance = 125m };
            var futureJournal = new JournalEntry { Id = Guid.NewGuid(), CompanyId = currentCompanyId, PostedOn = new DateOnly(2026, 6, 1), Reference = "FUTURE-CONSOLIDATION", Description = "Must not leak into an earlier as-of report", TotalAmount = 125m, Status = "Posted", IsPosted = true };
            db.Accounts.AddRange(futureAsset, futureEquity);
            db.JournalEntries.Add(futureJournal);
            db.JournalEntryLines.AddRange(
                new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = futureJournal.Id, AccountId = futureAsset.Id, Debit = 125m, Description = futureJournal.Description },
                new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = futureJournal.Id, AccountId = futureEquity.Id, Credit = 125m, Description = futureJournal.Description });
            await db.SaveChangesAsync();
        }
        var unmappedReport = await consolidation.GetBalanceReportAsync(group.Id.Value, new DateOnly(2026, 6, 1));
        Assert.NotNull(unmappedReport);
        Assert.DoesNotContain(unmappedReport!.Accounts, account => account.AccountNumber is "19998" or "39998");
        Assert.Contains(unmappedReport.Warnings, warning => warning.Contains("19998", StringComparison.Ordinal) && warning.Contains("excluded", StringComparison.OrdinalIgnoreCase));
        var futureMappingWorkspace = await consolidation.GetAccountMappingWorkspaceAsync(group.Id.Value);
        foreach (var sourceAccount in futureMappingWorkspace!.SourceAccounts.Where(account => account.AccountNumber is "19998" or "39998"))
        {
            var mapping = await consolidation.SaveAccountMappingAsync(new SaveConsolidationAccountMappingRequest(null, group.Id.Value, sourceAccount.CompanyId, sourceAccount.AccountId, sourceAccount.AccountNumber, sourceAccount.AccountName, new DateOnly(2026, 6, 1), null));
            Assert.True(mapping.Succeeded, mapping.ErrorMessage);
        }

        var historicalReport = await consolidation.GetBalanceReportAsync(group.Id!.Value, new DateOnly(2026, 5, 1));
        Assert.NotNull(historicalReport);
        Assert.DoesNotContain(historicalReport!.Accounts, account => account.AccountNumber is "19998" or "39998");
        var laterReport = await consolidation.GetBalanceReportAsync(group.Id!.Value, new DateOnly(2026, 6, 1));
        Assert.NotNull(laterReport);
        Assert.Equal(125m, laterReport!.Accounts.Single(account => account.AccountNumber == "19998").ConvertedBalance);
        Assert.Equal(125m, laterReport.Accounts.Single(account => account.AccountNumber == "39998").ConvertedBalance);
        var ownershipEventDate = new DateOnly(2026, 5, 1);
        var usdAffiliate = await companies.CreateCompanyAsync(new CreateCompanyRequest("US distribution affiliate", "US distribution affiliate LLC", "US-AFFILIATE-TEST", "USD", 1)); Assert.True(usdAffiliate.Succeeded, usdAffiliate.ErrorMessage); var usdAffiliateId = usdAffiliate.CompanyId!.Value;
        var affiliateOwnership = await consolidation.SaveOwnershipPeriodAsync(new SaveConsolidationOwnershipPeriodRequest(null, group.Id.Value, usdAffiliateId, .75m, DateOnly.MinValue, null, ConsolidationBasis: nameof(ConsolidationBasis.ControlledSubsidiary), BasisRationale: "Reviewed power, variable returns, and ability to affect returns", BasisReviewedOn: basisReviewedOn)); Assert.True(affiliateOwnership.Succeeded, affiliateOwnership.ErrorMessage);
        var acquisitionSubject = await companies.CreateCompanyAsync(new CreateCompanyRequest("Acquisition-date subsidiary", "Acquisition-date subsidiary LLC", "ACQ-SUBJECT-TEST", "USD", 1)); Assert.True(acquisitionSubject.Succeeded, acquisitionSubject.ErrorMessage); var acquisitionSubjectId = acquisitionSubject.CompanyId!.Value;
        var acquisitionOwnership = await consolidation.SaveOwnershipPeriodAsync(new(null, group.Id.Value, acquisitionSubjectId, 1m, ownershipEventDate, null, ConsolidationBasis: nameof(ConsolidationBasis.ControlledSubsidiary), BasisRationale: "Reviewed acquisition-date control conclusion", BasisReviewedOn: ownershipEventDate)); Assert.True(acquisitionOwnership.Succeeded, acquisitionOwnership.ErrorMessage);
        var transitionPriorStart = ownershipEventDate.AddDays(1); var transitionDate = new DateOnly(2026, 6, 1);
        var stepSubject = await companies.CreateCompanyAsync(new("Step-acquisition subsidiary", "Step-acquisition subsidiary LLC", "STEP-SUBJECT-TEST", "USD", 1)); Assert.True(stepSubject.Succeeded, stepSubject.ErrorMessage); var stepSubjectId = stepSubject.CompanyId!.Value;
        Assert.True((await consolidation.SaveOwnershipPeriodAsync(new(null, group.Id.Value, stepSubjectId, .50m, transitionPriorStart, transitionDate.AddDays(-1), ConsolidationBasis: nameof(ConsolidationBasis.ProportionateInterest), BasisRationale: "Reviewed noncontrolling interest before control", BasisReviewedOn: ownershipEventDate))).Succeeded);
        Assert.True((await consolidation.SaveOwnershipPeriodAsync(new(null, group.Id.Value, stepSubjectId, 1m, transitionDate, null, ConsolidationBasis: nameof(ConsolidationBasis.ControlledSubsidiary), BasisRationale: "Reviewed control obtained through a step acquisition", BasisReviewedOn: transitionDate))).Succeeded);
        var changeSubject = await companies.CreateCompanyAsync(new("Continuing-control subsidiary", "Continuing-control subsidiary LLC", "CHANGE-SUBJECT-TEST", "USD", 1)); Assert.True(changeSubject.Succeeded, changeSubject.ErrorMessage); var changeSubjectId = changeSubject.CompanyId!.Value;
        Assert.True((await consolidation.SaveOwnershipPeriodAsync(new(null, group.Id.Value, changeSubjectId, .80m, transitionPriorStart, transitionDate.AddDays(-1), ConsolidationBasis: nameof(ConsolidationBasis.ControlledSubsidiary), BasisRationale: "Reviewed continuing control before ownership change", BasisReviewedOn: ownershipEventDate))).Succeeded);
        Assert.True((await consolidation.SaveOwnershipPeriodAsync(new(null, group.Id.Value, changeSubjectId, .75m, transitionDate, null, ConsolidationBasis: nameof(ConsolidationBasis.ControlledSubsidiary), BasisRationale: "Reviewed continuing control after ownership change", BasisReviewedOn: transitionDate))).Succeeded);
        var lossSubject = await companies.CreateCompanyAsync(new("Disposed subsidiary", "Disposed subsidiary LLC", "LOSS-SUBJECT-TEST", "USD", 1)); Assert.True(lossSubject.Succeeded, lossSubject.ErrorMessage); var lossSubjectId = lossSubject.CompanyId!.Value;
        Assert.True((await consolidation.SaveOwnershipPeriodAsync(new(null, group.Id.Value, lossSubjectId, .75m, transitionPriorStart, transitionDate, ConsolidationBasis: nameof(ConsolidationBasis.ControlledSubsidiary), BasisRationale: "Reviewed control through the disposal date", BasisReviewedOn: ownershipEventDate))).Succeeded);
        Guid intercompanyCustomerId = Guid.NewGuid(); Guid intercompanyVendorId = Guid.NewGuid(); Guid intercompanyInvoiceId = Guid.NewGuid(); Guid intercompanyBillId = Guid.NewGuid();
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
        {
            db.Customers.Add(new Customer { Id = intercompanyCustomerId, CompanyId = currentCompanyId, CustomerNumber = "IC-US-DIST", Name = "US distribution affiliate", Email = "intercompany@example.invalid", State = "MI", CreditLimit = 10000m, OpenBalance = 125m });
            db.Vendors.Add(new Vendor { Id = intercompanyVendorId, CompanyId = usdAffiliateId, VendorNumber = "IC-PARENT", Name = "Brass Ledger Manufacturing", Email = "intercompany@example.invalid", State = "MI", PaymentTerms = "Net 30", OpenBalance = 125m });
            db.SalesInvoices.Add(new SalesInvoice { Id = intercompanyInvoiceId, CompanyId = currentCompanyId, CustomerId = intercompanyCustomerId, InvoiceNumber = "IC-INV-1001", InvoiceDate = new DateOnly(2026, 4, 15), DueDate = new DateOnly(2026, 5, 15), Status = "Open", Subtotal = 125m, TotalAmount = 125m, BalanceDue = 125m, ConcurrencyToken = Guid.NewGuid().ToString("N") });
            db.VendorBills.Add(new VendorBill { Id = intercompanyBillId, CompanyId = usdAffiliateId, VendorId = intercompanyVendorId, BillNumber = "ic-inv-1001", BillDate = new DateOnly(2026, 4, 16), DueDate = new DateOnly(2026, 5, 16), Status = "Open", TotalAmount = 125m, BalanceDue = 125m, ConcurrencyToken = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
        }
        var sellerLink = await consolidation.SaveTradingPartnerAsync(new(null, group.Id.Value, currentCompanyId, usdAffiliateId, intercompanyCustomerId, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30))); Assert.True(sellerLink.Succeeded, sellerLink.ErrorMessage);
        var buyerLink = await consolidation.SaveTradingPartnerAsync(new(null, group.Id.Value, usdAffiliateId, currentCompanyId, null, intercompanyVendorId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30))); Assert.True(buyerLink.Succeeded, buyerLink.ErrorMessage);
        var tradingPartnerWorkspace = await consolidation.GetTradingPartnerWorkspaceAsync(group.Id.Value); Assert.NotNull(tradingPartnerWorkspace); Assert.Equal(2, tradingPartnerWorkspace!.Links.Count);
        var preparerId = signedInOwner.User!.UserId; var reviewerId = Guid.NewGuid(); var posterId = Guid.NewGuid(); var reverserId = Guid.NewGuid();
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
        {
            foreach (var actorId in new[] { reviewerId, posterId, reverserId })
            foreach (var memberCompanyId in new[] { currentCompanyId, canadianCompanyId, usdAffiliateId, acquisitionSubjectId, stepSubjectId, changeSubjectId, lossSubjectId })
                db.CompanyMemberships.Add(new CompanyMembership { Id = Guid.NewGuid(), UserId = actorId, CompanyId = memberCompanyId, Role = "Accounting", IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        void SetConsolidationUser(Guid actorId, params string[] permissions)
        {
            var claims = permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)).ToList();
            claims.Add(new(System.Security.Claims.ClaimTypes.NameIdentifier, actorId.ToString())); claims.Add(new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, currentCompanyId.ToString()));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }
        var adjustmentPeriodStart = new DateOnly(2026, 1, 1); var adjustmentAsOf = ownershipEventDate;
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPrepare);
        var invalidDisclosureContent = new ConsolidationDisclosureDocument(1,
            [new("DEBT-1", "Term debt", "Long-term debt", 100m, -20m, 0m, 0m, 5m, 0m, 0m, 90m, string.Empty, "Debt rollforward WP-1")], [], []);
        var invalidDisclosure = await consolidation.SaveDisclosurePackageAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, "US-GAAP", "2026 annual", invalidDisclosureContent));
        Assert.False(invalidDisclosure.Succeeded); Assert.Contains("does not reconcile", invalidDisclosure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var disclosureContent = new ConsolidationDisclosureDocument(1,
            [new("DEBT-1", "Term debt", "Long-term debt", 100m, -20m, 0m, 0m, 5m, 0m, 0m, 85m, string.Empty, "Debt rollforward WP-1")],
            [new("SCF-1", "Primary supplier finance program", "Optional early payment; entity pays financier on extended terms.", "Accounts payable", 50m, 30m, 20m, 60m, 10m, 45, 90, 30, 45, "No security or guarantees", "Monitored within the weekly liquidity forecast.", "Treasury confirmation WP-2")],
            [new("AccountingPolicies", "CONSOL-BASIS", "Basis of consolidation", 100, "Controlled entities are consolidated from the date control begins.", "Consolidation policy WP-3", new() { ["futureStandardField"] = JsonDocument.Parse("{\"required\":true,\"label\":\"Extensible evidence\"}").RootElement.Clone() })]);
        var savedDisclosure = await consolidation.SaveDisclosurePackageAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, "us-gaap", "2026 annual", disclosureContent, "Prepared from controller-reviewed working papers."));
        Assert.True(savedDisclosure.Succeeded, savedDisclosure.ErrorMessage);
        var duplicateDisclosure = await consolidation.SaveDisclosurePackageAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, "US-GAAP", "2026 annual", disclosureContent));
        Assert.False(duplicateDisclosure.Succeeded); Assert.Contains("already retained", duplicateDisclosure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var disclosureWorkspace = await consolidation.GetDisclosureWorkspaceAsync(group.Id.Value); Assert.NotNull(disclosureWorkspace);
        var disclosureDraft = Assert.Single(disclosureWorkspace!.Packages); Assert.Equal("Draft", disclosureDraft.Status); Assert.Equal("US-GAAP", disclosureDraft.FrameworkCode); Assert.Equal(85m, Assert.Single(disclosureDraft.Content.FinancingLiabilities).ClosingBalance);
        Assert.True(Assert.Single(disclosureDraft.Content.NarrativeDisclosures).Extensions!["futureStandardField"].GetProperty("required").GetBoolean());
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalApprove);
        var selfDisclosureApproval = await consolidation.ApproveDisclosurePackageAsync(new(group.Id.Value, disclosureDraft.Id, disclosureDraft.ConcurrencyToken));
        Assert.False(selfDisclosureApproval.Succeeded); Assert.Contains("prepared", selfDisclosureApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetConsolidationUser(reviewerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalApprove);
        var approvedDisclosure = await consolidation.ApproveDisclosurePackageAsync(new(group.Id.Value, disclosureDraft.Id, disclosureDraft.ConcurrencyToken));
        Assert.True(approvedDisclosure.Succeeded, approvedDisclosure.ErrorMessage);
        var staleDisclosureReview = await consolidation.RejectDisclosurePackageAsync(new(group.Id.Value, disclosureDraft.Id, "Stale review", disclosureDraft.ConcurrencyToken));
        Assert.False(staleDisclosureReview.Succeeded);
        var reviewedDisclosure = Assert.Single((await consolidation.GetDisclosureWorkspaceAsync(group.Id.Value))!.Packages); Assert.Equal("Approved", reviewedDisclosure.Status); Assert.Equal("Unavailable user", reviewedDisclosure.ApprovedBy);
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPrepare);
        var adjustmentWorkspace = await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value); Assert.NotNull(adjustmentWorkspace);
        var adjustmentAsset = adjustmentWorkspace!.ReportingAccounts.First(account => account.AccountType == nameof(AccountType.Asset));
        var adjustmentEquity = adjustmentWorkspace.ReportingAccounts.First(account => account.AccountType == nameof(AccountType.Equity) && account.AccountNumber != "39997");
        var adjustmentRevenue = adjustmentWorkspace.ReportingAccounts.First(account => account.AccountType == nameof(AccountType.Revenue));
        var nciEquity = adjustmentWorkspace.ReportingAccounts.Single(account => account.AccountNumber == "39997" && account.AccountType == nameof(AccountType.Equity));
        var invalidAcquisitionContent = new ConsolidationOwnershipEventDocument(2, 0m, 1m, "NotApplicable", "Controller-reviewed purchase-price allocation", "Acquisition working paper PPA-1", string.Empty, string.Empty,
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, 100m, 0m), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 100m)],
            Acquisition: new(80m, 0m, 0m, 70m, 0m, 0m,
                [new("CASH", "Cash paid to sellers", "Cash", 80m, "Closing statement CS-1", new() { ["settlementChannel"] = JsonDocument.Parse("\"Wire\"").RootElement.Clone() })],
                [new("CUSTOMER-REL", "Customer relationships", "Asset", 100m, 0m, 10m, "Valuation report VR-1"), new("ASSUMED-DEBT", "Assumed term debt", "Liability", 20m, 0m, 0m, "Debt confirmation DC-1")],
                [new(new DateOnly(2026, 6, 1), "MPA-1", "Refine customer-relationship valuation", 0m, 0m, 0m, 2m, -2m, 0m, "Updated valuation report VR-2")], new DateOnly(2027, 5, 1),
                new() { ["valuationConvention"] = JsonDocument.Parse("\"Market participant\"").RootElement.Clone() }));
        var invalidAcquisition = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, acquisitionSubjectId, adjustmentAsOf, nameof(ConsolidationOwnershipEventType.AcquisitionOfControl), "ACQ-US-DIST-BAD", "US-GAAP", "ASC 805 current through 2026", invalidAcquisitionContent));
        Assert.False(invalidAcquisition.Succeeded); Assert.Contains("Goodwill", invalidAcquisition.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var componentMismatchContent = invalidAcquisitionContent with { Acquisition = invalidAcquisitionContent.Acquisition! with { Goodwill = 10m, ConsiderationComponents = [new("CASH", "Cash paid to sellers", "Cash", 79m, "Closing statement CS-1")] } };
        var componentMismatch = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, acquisitionSubjectId, adjustmentAsOf, nameof(ConsolidationOwnershipEventType.AcquisitionOfControl), "ACQ-US-DIST-COMPONENT-BAD", "US-GAAP", "ASC 805 current through 2026", componentMismatchContent));
        Assert.False(componentMismatch.Succeeded); Assert.Contains("component", componentMismatch.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var adjustmentMismatchContent = invalidAcquisitionContent with { Acquisition = invalidAcquisitionContent.Acquisition! with { Goodwill = 10m, MeasurementPeriodAdjustments = [new(new DateOnly(2026, 6, 1), "MPA-1", "Refine customer-relationship valuation", 0m, 0m, 0m, 2m, -1m, 0m, "Updated valuation report VR-2")] } };
        var adjustmentMismatch = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, acquisitionSubjectId, adjustmentAsOf, nameof(ConsolidationOwnershipEventType.AcquisitionOfControl), "ACQ-US-DIST-MPA-BAD", "US-GAAP", "ASC 805 current through 2026", adjustmentMismatchContent));
        Assert.False(adjustmentMismatch.Succeeded); Assert.Contains("adjustment", adjustmentMismatch.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var acquisitionContent = invalidAcquisitionContent with
        {
            Acquisition = invalidAcquisitionContent.Acquisition! with { Goodwill = 10m },
            Extensions = new() { ["valuationSpecialist"] = JsonDocument.Parse("\"Independent Valuation LLC\"").RootElement.Clone() }
        };
        var savedAcquisition = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, acquisitionSubjectId, adjustmentAsOf, nameof(ConsolidationOwnershipEventType.AcquisitionOfControl), "ACQ-US-DIST-1", "US-GAAP", "ASC 805 current through 2026", acquisitionContent));
        Assert.True(savedAcquisition.Succeeded, savedAcquisition.ErrorMessage);
        var acquisitionDraft = Assert.Single((await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value))!.Events); Assert.Equal("Draft", acquisitionDraft.Status); Assert.Equal(10m, acquisitionDraft.Content.Acquisition!.Goodwill); Assert.False(string.IsNullOrWhiteSpace(acquisitionDraft.ContentSha256));
        Assert.Equal(2, acquisitionDraft.SchemaVersion); Assert.Equal("Closing statement CS-1", Assert.Single(acquisitionDraft.Content.Acquisition.ConsiderationComponents!).SourceReference);
        Assert.Equal("Wire", Assert.Single(acquisitionDraft.Content.Acquisition.ConsiderationComponents!).Extensions!["settlementChannel"].GetString());
        Assert.Equal("Market participant", acquisitionDraft.Content.Acquisition.Extensions!["valuationConvention"].GetString());
        Assert.Equal(10m, acquisitionDraft.Content.Acquisition.IdentifiableItems!.Single(item => item.Code == "CUSTOMER-REL").DeferredTaxLiability);
        Assert.Equal(-2m, Assert.Single(acquisitionDraft.Content.Acquisition.MeasurementPeriodAdjustments!).GoodwillChange);
        SetConsolidationUser(reviewerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalApprove);
        var approvedAcquisition = await consolidation.ApproveOwnershipEventAsync(new(group.Id.Value, acquisitionDraft.Id, acquisitionDraft.ConcurrencyToken)); Assert.True(approvedAcquisition.Succeeded, approvedAcquisition.ErrorMessage);
        SetConsolidationUser(posterId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPost);
        var approvedAcquisitionSnapshot = Assert.Single((await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value))!.Events);
        var postedAcquisition = await consolidation.PostOwnershipEventAsync(new(group.Id.Value, approvedAcquisitionSnapshot.Id, approvedAcquisitionSnapshot.ConcurrencyToken)); Assert.True(postedAcquisition.Succeeded, postedAcquisition.ErrorMessage);
        var acquisitionReport = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(acquisitionReport);
        Assert.Contains(acquisitionReport!.Accounts.SelectMany(account => account.Contributions ?? []), contribution => contribution.SourceKind == nameof(ConsolidationOwnershipEventType.AcquisitionOfControl) && contribution.Reference == "ACQ-US-DIST-1");
        var postedAcquisitionSnapshot = Assert.Single((await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value))!.Events, item => item.ReversalOfEventId is null);
        SetConsolidationUser(reverserId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalReverse);
        var reversedAcquisition = await consolidation.ReverseOwnershipEventAsync(new(group.Id.Value, postedAcquisitionSnapshot.Id, adjustmentAsOf.AddDays(1), "Acquisition schedule superseded by corrected valuation", postedAcquisitionSnapshot.ConcurrencyToken)); Assert.True(reversedAcquisition.Succeeded, reversedAcquisition.ErrorMessage);
        var reversedWorkspace = await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value); Assert.NotNull(reversedWorkspace); Assert.Equal(2, reversedWorkspace!.Events.Count); Assert.Contains(reversedWorkspace.Events, item => item.ReversalOfEventId == postedAcquisitionSnapshot.Id && item.Status == "Posted");
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPrepare);
        var attributionContent = new ConsolidationOwnershipEventDocument(1, .75m, .75m, "NotApplicable", "Reviewed period-end allocation to parent and noncontrolling interests", "Income attribution working paper NCI-1", adjustmentEquity.AccountNumber, adjustmentEquity.AccountName,
            [new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 10m, 0m, "Allocate consolidated earnings"), new(adjustmentRevenue.AccountNumber, adjustmentRevenue.AccountName, adjustmentRevenue.AccountType, 0m, 10m, "Attributed subsidiary earnings")],
            ProfitAttribution: new(10m, 7.50m, 2.50m, 0m, 0m, 0m));
        var savedAttribution = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, usdAffiliateId, adjustmentAsOf, nameof(ConsolidationOwnershipEventType.ProfitAttribution), "ATTR-US-DIST-1", "US-GAAP", "ASC 810 current through 2026", attributionContent)); Assert.True(savedAttribution.Succeeded, savedAttribution.ErrorMessage);
        var attributionDraft = Assert.Single((await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value))!.Events, item => item.Id == savedAttribution.Id);
        SetConsolidationUser(reviewerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalApprove);
        Assert.True((await consolidation.ApproveOwnershipEventAsync(new(group.Id.Value, attributionDraft.Id, attributionDraft.ConcurrencyToken))).Succeeded);
        var attributionApproved = Assert.Single((await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value))!.Events, item => item.Id == attributionDraft.Id);
        SetConsolidationUser(posterId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPost);
        Assert.True((await consolidation.PostOwnershipEventAsync(new(group.Id.Value, attributionApproved.Id, attributionApproved.ConcurrencyToken))).Succeeded);
        var attributionCurrentReport = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.Contains(attributionCurrentReport!.Accounts.SelectMany(account => account.Contributions ?? []), contribution => contribution.Reference == "ATTR-US-DIST-1" && contribution.SourceKind == nameof(ConsolidationOwnershipEventType.ProfitAttribution));
        var attributionCarryforwardReport = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentAsOf.AddDays(1), adjustmentAsOf.AddMonths(1));
        Assert.Contains(attributionCarryforwardReport!.Accounts.SelectMany(account => account.Contributions ?? []), contribution => contribution.Reference == "ATTR-US-DIST-1" && contribution.SourceKind == "OwnershipEventCarryforward");
        var historicalOwnershipPackage = await consolidation.GetStatementPackageAsync(group.Id.Value, adjustmentAsOf.AddDays(1), adjustmentAsOf.AddMonths(1)); Assert.NotNull(historicalOwnershipPackage);
        Assert.Contains(historicalOwnershipPackage!.OwnershipEvents!, item => item.Reference == "ACQ-US-DIST-1"); Assert.Contains(historicalOwnershipPackage.OwnershipEvents!, item => item.ReversalOfEventId == postedAcquisitionSnapshot.Id); Assert.Contains(historicalOwnershipPackage.OwnershipEvents!, item => item.Reference == "ATTR-US-DIST-1");
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPrepare);
        var ownershipValidationDate = transitionDate;
        var stepContent = acquisitionContent with { OwnershipBefore = .50m, Acquisition = new(30m, 40m, 0m, 60m, 10m, 0m,
            [new("STEP-CASH", "Cash paid for additional interest", "Cash", 30m, "Step closing statement SCS-1")],
            [new("STEP-ASSETS", "Identifiable assets", "Asset", 80m, 0m, 0m, "Step valuation report SVR-1"), new("STEP-LIABILITIES", "Identifiable liabilities", "Liability", 20m, 0m, 0m, "Step debt schedule SDS-1")],
            [], new DateOnly(2027, 6, 1)), Extensions = null };
        var savedStep = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, stepSubjectId, ownershipValidationDate, nameof(ConsolidationOwnershipEventType.StepAcquisition), "STEP-US-DIST-1", "US-GAAP", "ASC 805 current through 2026", stepContent)); Assert.True(savedStep.Succeeded, savedStep.ErrorMessage);
        var invalidChangeContent = new ConsolidationOwnershipEventDocument(1, .80m, .75m, "NotApplicable", "Reviewed continuing-control ownership transaction", "Ownership schedule EQ-1", string.Empty, string.Empty,
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, 5m, 0m), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 5m)],
            OwnershipChange: new(5m, 0m, 0m, 0m, 0m, 0m));
        var invalidChange = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, usdAffiliateId, ownershipValidationDate, nameof(ConsolidationOwnershipEventType.OwnershipChangeWithoutLossOfControl), "CHANGE-US-DIST-BAD", "US-GAAP", "ASC 810 current through 2026", invalidChangeContent)); Assert.False(invalidChange.Succeeded); Assert.Contains("reconcile", invalidChange.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var changeContent = invalidChangeContent with { OwnershipChange = new(5m, 0m, 0m, 5m, 0m, 0m) };
        var savedChange = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, changeSubjectId, ownershipValidationDate, nameof(ConsolidationOwnershipEventType.OwnershipChangeWithoutLossOfControl), "CHANGE-US-DIST-1", "US-GAAP", "ASC 810 current through 2026", changeContent)); Assert.True(savedChange.Succeeded, savedChange.ErrorMessage);
        var invalidLossContent = new ConsolidationOwnershipEventDocument(1, .75m, 0m, "NotApplicable", "Reviewed loss-of-control derecognition", "Disposal schedule DISP-1", string.Empty, string.Empty,
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, 120m, 0m), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 120m)],
            LossOfControl: new(100m, 0m, 20m, 110m, 10m, 0m, 1m));
        var invalidLoss = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, usdAffiliateId, ownershipValidationDate, nameof(ConsolidationOwnershipEventType.LossOfControl), "LOSS-US-DIST-BAD", "IFRS", "IFRS 10 current through 2026", invalidLossContent)); Assert.False(invalidLoss.Succeeded); Assert.Contains("does not reconcile", invalidLoss.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var lossContent = invalidLossContent with { LossOfControl = new(100m, 0m, 20m, 110m, 10m, 0m, 0m) };
        var savedLoss = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, lossSubjectId, ownershipValidationDate, nameof(ConsolidationOwnershipEventType.LossOfControl), "LOSS-US-DIST-1", "IFRS", "IFRS 10 current through 2026", lossContent)); Assert.True(savedLoss.Succeeded, savedLoss.ErrorMessage);
        var invalidAttribution = attributionContent with { ProfitAttribution = new(10m, 8m, 1m, 0m, 0m, 0m) };
        var rejectedAttributionCalculation = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, usdAffiliateId, ownershipValidationDate, nameof(ConsolidationOwnershipEventType.ProfitAttribution), "ATTR-US-DIST-BAD", "US-GAAP", "ASC 810 current through 2026", invalidAttribution)); Assert.False(rejectedAttributionCalculation.Succeeded); Assert.Contains("reconcile", rejectedAttributionCalculation.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var rejectedAttributionDraftResult = await consolidation.SaveOwnershipEventAsync(new(null, group.Id.Value, usdAffiliateId, ownershipValidationDate, nameof(ConsolidationOwnershipEventType.ProfitAttribution), "ATTR-US-DIST-REJECT", "US-GAAP", "ASC 810 current through 2026", attributionContent)); Assert.True(rejectedAttributionDraftResult.Succeeded, rejectedAttributionDraftResult.ErrorMessage);
        var rejectedAttributionDraft = Assert.Single((await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value))!.Events, item => item.Id == rejectedAttributionDraftResult.Id);
        var transitionDrafts = (await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value))!.Events.Where(item => item.Id == savedStep.Id!.Value || item.Id == savedChange.Id!.Value || item.Id == savedLoss.Id!.Value).ToArray(); Assert.Equal(3, transitionDrafts.Length);
        SetConsolidationUser(reviewerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalApprove);
        Assert.True((await consolidation.RejectOwnershipEventAsync(new(group.Id.Value, rejectedAttributionDraft.Id, "Fixture-only schedule validation", rejectedAttributionDraft.ConcurrencyToken))).Succeeded);
        foreach (var transitionDraft in transitionDrafts) Assert.True((await consolidation.ApproveOwnershipEventAsync(new(group.Id.Value, transitionDraft.Id, transitionDraft.ConcurrencyToken))).Succeeded);
        var approvedTransitionEvents = (await consolidation.GetOwnershipEventWorkspaceAsync(group.Id.Value))!.Events.Where(item => transitionDrafts.Any(draft => draft.Id == item.Id)).ToArray(); Assert.All(approvedTransitionEvents, item => Assert.Equal("Approved", item.Status));
        SetConsolidationUser(posterId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPost);
        foreach (var approvedTransition in approvedTransitionEvents) Assert.True((await consolidation.PostOwnershipEventAsync(new(group.Id.Value, approvedTransition.Id, approvedTransition.ConcurrencyToken))).Succeeded);
        var transitionReport = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentPeriodStart, ownershipValidationDate); Assert.DoesNotContain(transitionReport!.Warnings, warning => warning.Contains("ATTR-US-DIST-REJECT", StringComparison.Ordinal));
        foreach (var reference in new[] { "STEP-US-DIST-1", "CHANGE-US-DIST-1", "LOSS-US-DIST-1" }) Assert.Contains(transitionReport.Accounts.SelectMany(account => account.Contributions ?? []), contribution => contribution.Reference == reference && contribution.TranslationMethod == "OwnershipEvent");
        var transitionPackage = await consolidation.GetStatementPackageAsync(group.Id.Value, adjustmentPeriodStart, ownershipValidationDate); Assert.NotNull(transitionPackage);
        foreach (var reference in new[] { "STEP-US-DIST-1", "CHANGE-US-DIST-1", "LOSS-US-DIST-1" }) Assert.Contains(transitionPackage!.OwnershipEvents!, item => item.Reference == reference && item.Status == "Posted");
        var transitionCsv = await consolidation.ExportStatementPackageCsvAsync(group.Id.Value, adjustmentPeriodStart, ownershipValidationDate); Assert.NotNull(transitionCsv); Assert.Contains("STEP-US-DIST-1", transitionCsv, StringComparison.Ordinal); Assert.Contains("CHANGE-US-DIST-1", transitionCsv, StringComparison.Ordinal); Assert.Contains("LOSS-US-DIST-1", transitionCsv, StringComparison.Ordinal);
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPrepare);
        var missingNciReport = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(missingNciReport);
        Assert.Contains(missingNciReport!.Warnings, warning => warning.Contains("no posted NCI reclassification", StringComparison.OrdinalIgnoreCase));
        var savedNci = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, nameof(ConsolidationAdjustmentKind.NoncontrollingInterest), "NCI-US-DIST-1", "Reviewed NCI equity attribution", string.Empty,
            [new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 10m, 0m, "Reclassify parent-attributable equity", usdAffiliateId), new(nciEquity.AccountNumber, nciEquity.AccountName, nciEquity.AccountType, 0m, 10m, "Present NCI within equity", usdAffiliateId)], SubjectCompanyId: usdAffiliateId));
        Assert.True(savedNci.Succeeded, savedNci.ErrorMessage);
        var duplicateNci = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, nameof(ConsolidationAdjustmentKind.NoncontrollingInterest), "NCI-US-DIST-DUP", "Concurrent-safe duplicate must fail", string.Empty,
            [new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 10m, 0m, "Duplicate offset", usdAffiliateId), new(nciEquity.AccountNumber, nciEquity.AccountName, nciEquity.AccountType, 0m, 10m, "Duplicate NCI", usdAffiliateId)], SubjectCompanyId: usdAffiliateId));
        Assert.False(duplicateNci.Succeeded); Assert.Contains("already retained", duplicateNci.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var discovery = await consolidation.DiscoverIntercompanyMatchesAsync(new(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf)); Assert.True(discovery.Succeeded, discovery.ErrorMessage); Assert.Equal(1, discovery.CreatedCount);
        var discoveredMatch = Assert.Single((await consolidation.GetIntercompanyMatchWorkspaceAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf))!.Matches); Assert.Equal("Suggested", discoveredMatch.Status); Assert.Equal("USD", discoveredMatch.Currency); Assert.Equal(125m, discoveredMatch.Amount); Assert.Equal($"IC-{intercompanyInvoiceId:N}-{intercompanyBillId:N}", discoveredMatch.MatchReference);
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
        {
            var affiliatePeriod = await db.ConsolidationGroupCompanies.SingleAsync(period => period.ConsolidationGroupId == group.Id && period.MemberCompanyId == usdAffiliateId);
            var affiliateMembership = await db.CompanyMemberships.SingleAsync(membership => membership.UserId == preparerId && membership.CompanyId == usdAffiliateId);
            affiliatePeriod.EffectiveThrough = adjustmentAsOf.AddDays(-1); affiliateMembership.IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Null(await consolidation.GetIntercompanyMatchWorkspaceAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf));
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
        {
            var affiliatePeriod = await db.ConsolidationGroupCompanies.SingleAsync(period => period.ConsolidationGroupId == group.Id && period.MemberCompanyId == usdAffiliateId);
            var affiliateMembership = await db.CompanyMemberships.SingleAsync(membership => membership.UserId == preparerId && membership.CompanyId == usdAffiliateId);
            affiliatePeriod.EffectiveThrough = null; affiliateMembership.IsActive = true; await db.SaveChangesAsync();
        }
        var missingExclusionReason = await consolidation.SetIntercompanyMatchDecisionAsync(new(group.Id.Value, discoveredMatch.Id, "Exclude", string.Empty, discoveredMatch.ConcurrencyToken)); Assert.False(missingExclusionReason.Succeeded);
        var excludedMatch = await consolidation.SetIntercompanyMatchDecisionAsync(new(group.Id.Value, discoveredMatch.Id, "Exclude", "Documents require controller review", discoveredMatch.ConcurrencyToken)); Assert.True(excludedMatch.Succeeded, excludedMatch.ErrorMessage);
        var excludedSnapshot = Assert.Single((await consolidation.GetIntercompanyMatchWorkspaceAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf))!.Matches); Assert.Equal("Excluded", excludedSnapshot.Status);
        var restoredMatch = await consolidation.SetIntercompanyMatchDecisionAsync(new(group.Id.Value, discoveredMatch.Id, "Restore", string.Empty, excludedSnapshot.ConcurrencyToken)); Assert.True(restoredMatch.Succeeded, restoredMatch.ErrorMessage);
        discoveredMatch = Assert.Single((await consolidation.GetIntercompanyMatchWorkspaceAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf))!.Matches); Assert.Equal("Suggested", discoveredMatch.Status);
        var invalidNumericKind = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, "999", "INVALID-KIND", "Invalid numeric enum", string.Empty,
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, 1m, 0m), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 1m)]));
        Assert.False(invalidNumericKind.Succeeded); Assert.Contains("kind", invalidNumericKind.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var invalidNumericAccountType = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, nameof(ConsolidationAdjustmentKind.ManualAdjustment), "INVALID-ACCOUNT-TYPE", "Invalid numeric enum", string.Empty,
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, "999", 1m, 0m), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 1m)]));
        Assert.False(invalidNumericAccountType.Succeeded); Assert.Contains("valid reporting account", invalidNumericAccountType.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var outOfRangeAmount = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, nameof(ConsolidationAdjustmentKind.ManualAdjustment), "INVALID-AMOUNT", "Out-of-range money", string.Empty,
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, 10000000000000000m, 0m), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 10000000000000000m)]));
        Assert.False(outOfRangeAmount.Succeeded); Assert.Contains("currency range", outOfRangeAmount.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var invalidElimination = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, nameof(ConsolidationAdjustmentKind.IntercompanyElimination), "ELIM-MISSING-PROVENANCE", "Invalid elimination", "MATCH-1",
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, 25m, 0m), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 25m)]));
        Assert.False(invalidElimination.Succeeded); Assert.Contains("different companies", invalidElimination.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var savedAdjustment = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, nameof(ConsolidationAdjustmentKind.IntercompanyElimination), "CONSOL-ADJ-1", "Controlled reporting-only adjustment", discoveredMatch.MatchReference,
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, 25m, 0m, "Reporting asset true-up", currentCompanyId, usdAffiliateId), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 25m, "Reporting equity offset", usdAffiliateId, currentCompanyId)]));
        Assert.True(savedAdjustment.Succeeded, savedAdjustment.ErrorMessage);
        var controlledMatch = Assert.Single((await consolidation.GetIntercompanyMatchWorkspaceAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf))!.Matches); Assert.Equal("Controlled", controlledMatch.Status); Assert.Equal(savedAdjustment.Id, controlledMatch.ConsolidationAdjustmentBatchId);
        var rejectionCandidate = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, nameof(ConsolidationAdjustmentKind.ManualAdjustment), "CONSOL-REJECT-1", "Rejected correction example", string.Empty,
            [new(adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, 5m, 0m), new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 0m, 5m)]));
        Assert.True(rejectionCandidate.Succeeded, rejectionCandidate.ErrorMessage);
        var draft = (await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value))!.Adjustments.Single(item => item.Id == savedAdjustment.Id);
        var nciDraft = (await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value))!.Adjustments.Single(item => item.Id == savedNci.Id);
        var rejectionDraft = (await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value))!.Adjustments.Single(item => item.Id == rejectionCandidate.Id);
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalApprove);
        var selfApproval = await consolidation.ApproveAdjustmentAsync(new(group.Id.Value, draft.Id, draft.ConcurrencyToken)); Assert.False(selfApproval.Succeeded); Assert.Contains("prepared", selfApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetConsolidationUser(reviewerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalApprove);
        var rejected = await consolidation.RejectAdjustmentAsync(new(group.Id.Value, rejectionDraft.Id, "Provide supporting consolidation schedule", rejectionDraft.ConcurrencyToken)); Assert.True(rejected.Succeeded, rejected.ErrorMessage);
        var rejectedSnapshot = (await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value))!.Adjustments.Single(item => item.Id == rejectionDraft.Id); Assert.Equal("Rejected", rejectedSnapshot.Status); Assert.NotNull(rejectedSnapshot.RejectedBy); Assert.Equal("Provide supporting consolidation schedule", rejectedSnapshot.DecisionReason);
        var approvedNci = await consolidation.ApproveAdjustmentAsync(new(group.Id.Value, nciDraft.Id, nciDraft.ConcurrencyToken)); Assert.True(approvedNci.Succeeded, approvedNci.ErrorMessage);
        var approved = await consolidation.ApproveAdjustmentAsync(new(group.Id.Value, draft.Id, draft.ConcurrencyToken)); Assert.True(approved.Succeeded, approved.ErrorMessage);
        SetConsolidationUser(posterId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPost);
        var approvedNciSnapshot = (await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value))!.Adjustments.Single(item => item.Id == nciDraft.Id);
        var postedNci = await consolidation.PostAdjustmentAsync(new(group.Id.Value, nciDraft.Id, approvedNciSnapshot.ConcurrencyToken)); Assert.True(postedNci.Succeeded, postedNci.ErrorMessage);
        var beforeAdjustment = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(beforeAdjustment);
        Assert.DoesNotContain(beforeAdjustment!.Warnings, warning => warning.Contains("no posted NCI reclassification", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(10m, beforeAdjustment.Accounts.Single(account => account.AccountNumber == nciEquity.AccountNumber).ConvertedBalance);
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage);
        var presentationWorkspace = await consolidation.GetStatementPresentationWorkspaceAsync(group.Id.Value); Assert.NotNull(presentationWorkspace);
        Assert.Contains(presentationWorkspace!.Candidates, candidate => candidate.StatementCode == "BALANCE-SHEET" && candidate.ReportingAccountNumber == adjustmentAsset.AccountNumber);
        var missingPresentationEvidence = await consolidation.SaveStatementPresentationAsync(new(null, group.Id.Value, "BALANCE-SHEET", adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, "CURRENT-ASSETS", "Current assets", 100, "Cash and other current assets", 100, string.Empty, basisReviewedOn, DateOnly.MinValue, null));
        Assert.False(missingPresentationEvidence.Succeeded); Assert.Contains("rationale", missingPresentationEvidence.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var savedPresentation = await consolidation.SaveStatementPresentationAsync(new(null, group.Id.Value, "BALANCE-SHEET", adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, "CURRENT-ASSETS", "Current assets", 100, "Cash and other current assets", 100, "Reviewed current classification for consolidated presentation", basisReviewedOn, DateOnly.MinValue, null));
        Assert.True(savedPresentation.Succeeded, savedPresentation.ErrorMessage);
        var overlappingPresentation = await consolidation.SaveStatementPresentationAsync(new(null, group.Id.Value, "BALANCE-SHEET", adjustmentAsset.AccountNumber, adjustmentAsset.AccountName, adjustmentAsset.AccountType, "NONCURRENT-ASSETS", "Noncurrent assets", 200, "Other assets", 100, "Conflicting presentation period", basisReviewedOn, adjustmentPeriodStart, null));
        Assert.False(overlappingPresentation.Succeeded); Assert.Contains("overlap", overlappingPresentation.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        presentationWorkspace = await consolidation.GetStatementPresentationWorkspaceAsync(group.Id.Value); var retainedPresentation = Assert.Single(presentationWorkspace!.Presentations);
        Assert.Equal("CURRENT-ASSETS", retainedPresentation.SectionCode); Assert.Equal("Reviewed current classification for consolidated presentation", retainedPresentation.Rationale);
        SetConsolidationUser(posterId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPost);
        var statementPackage = await consolidation.GetStatementPackageAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(statementPackage);
        Assert.False(statementPackage!.IsComplete);
        var statementDisclosure = Assert.Single(statementPackage.DisclosurePackages!); Assert.Equal("US-GAAP", statementDisclosure.FrameworkCode); Assert.Equal("Approved", statementDisclosure.Status);
        Assert.Equal(2, statementPackage.OwnershipEvents!.Count); Assert.Contains(statementPackage.OwnershipEvents, item => item.Reference == "ACQ-US-DIST-1" && item.Content.Acquisition!.Goodwill == 10m); Assert.Contains(statementPackage.OwnershipEvents, item => item.Reference == "ATTR-US-DIST-1" && item.Content.ProfitAttribution!.NoncontrollingInterestProfitOrLoss == 2.50m);
        Assert.DoesNotContain(statementPackage.Warnings, warning => warning.Contains("has no effective operating, investing, or financing classification", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, statementPackage.Reconciliation.BalanceSheetDifference);
        Assert.Equal(0m, statementPackage.Reconciliation.EquityStatementDifference);
        Assert.Equal(0m, statementPackage.Reconciliation.CashFlowDifference);
        Assert.Equal(statementPackage.Reconciliation.Assets, statementPackage.Reconciliation.LiabilitiesAndEquity);
        Assert.Equal(statementPackage.Reconciliation.EndingCash - statementPackage.Reconciliation.OpeningCash, statementPackage.Reconciliation.NetCashChange);
        Assert.Contains(statementPackage.BalanceSheet.Sections.SelectMany(section => section.Accounts).SelectMany(account => account.Contributions), contribution => contribution.SourceKind == "MemberLedger");
        var currentAssetsSection = statementPackage.BalanceSheet.Sections.Single(section => section.Code == "CURRENT-ASSETS");
        Assert.Contains(currentAssetsSection.Accounts, account => account.AccountNumber == adjustmentAsset.AccountNumber && account.AccountName == "Cash and other current assets");
        Assert.Contains(statementPackage.Warnings, warning => warning.Contains("has no effective reviewed presentation policy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(statementPackage.CashFlowStatement.Sections, section => section.Code == "OPERATING");
        Assert.Contains(statementPackage.CashFlowStatement.Sections, section => section.Code == "INVESTING");
        Assert.Contains(statementPackage.CashFlowStatement.Sections, section => section.Code == "FINANCING");
        Assert.Contains(statementPackage.CashFlowStatement.Sections.SelectMany(section => section.Accounts).SelectMany(account => account.Contributions), contribution => contribution.SourceKind == "CashFlow" && !string.IsNullOrWhiteSpace(contribution.Reference));
        var statementCsv = await consolidation.ExportStatementPackageCsvAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(statementCsv);
        Assert.Contains("\"Contribution\"", statementCsv, StringComparison.Ordinal);
        Assert.Contains("\"Reconciliation\"", statementCsv, StringComparison.Ordinal);
        Assert.Contains("NCI-US-DIST-1", statementCsv, StringComparison.Ordinal);
        Assert.Contains("\"Disclosure\",\"US-GAAP\",\"Financing liabilities\"", statementCsv, StringComparison.Ordinal);
        Assert.Contains("Treasury confirmation WP-2", statementCsv, StringComparison.Ordinal);
        Assert.Contains("\"Ownership measurement\",\"US-GAAP\",\"AcquisitionOfControl\",\"ACQ-US-DIST-1\",\"Goodwill\"", statementCsv, StringComparison.Ordinal);
        Assert.Contains("Acquisition working paper PPA-1", statementCsv, StringComparison.Ordinal);
        Assert.Contains("\"PPA consideration\"", statementCsv, StringComparison.Ordinal);
        Assert.Contains("Valuation report VR-1", statementCsv, StringComparison.Ordinal);
        Assert.Contains("Updated valuation report VR-2", statementCsv, StringComparison.Ordinal);
        var statementExcel = await consolidation.ExportStatementPackageExcelAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(statementExcel);
        using (var workbook = new XLWorkbook(new MemoryStream(statementExcel!)))
        {
            Assert.Equal(8, workbook.Worksheets.Count);
            Assert.Contains("INCOMPLETE", workbook.Worksheet("Summary").Cell("A3").GetString(), StringComparison.Ordinal);
            Assert.Equal("Section code", workbook.Worksheet("Balance sheet").Cell("A6").GetString());
            Assert.Contains("NCI-US-DIST-1", workbook.Worksheet("Source detail").CellsUsed().Select(cell => cell.GetString()));
            Assert.Equal("Financing-liability reconciliation", workbook.Worksheet("Current US-GAAP notes").Cell("A8").GetString());
            Assert.Contains("Treasury confirmation WP-2", workbook.Worksheet("Current US-GAAP notes").CellsUsed().Select(cell => cell.GetString()));
            Assert.Contains(workbook.Worksheet("Ownership schedules").CellsUsed().Select(cell => cell.GetString()), value => value.Contains("ACQ-US-DIST-1", StringComparison.Ordinal));
            Assert.Contains("Goodwill", workbook.Worksheet("Ownership schedules").CellsUsed().Select(cell => cell.GetString()));
            Assert.Contains("Cash paid to sellers", workbook.Worksheet("Ownership schedules").CellsUsed().Select(cell => cell.GetString()));
            Assert.Contains("Valuation report VR-1", workbook.Worksheet("Ownership schedules").CellsUsed().Select(cell => cell.GetString()));
            Assert.Contains("MPA-1", workbook.Worksheet("Ownership schedules").CellsUsed().Select(cell => cell.GetString()));
        }
        var statementPdf = await consolidation.ExportStatementPackagePdfAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(statementPdf);
        Assert.True(statementPdf!.AsSpan().StartsWith("%PDF"u8));
        using (var document = PdfReader.Open(new MemoryStream(statementPdf), PdfDocumentOpenMode.Import))
        {
            Assert.True(document.PageCount >= 9);
            Assert.Contains("consolidated statements", document.Info.Title, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Incomplete", document.Info.Subject, StringComparison.OrdinalIgnoreCase);
        }
        var comparisonPeriodStart = adjustmentPeriodStart.AddYears(-1);
        var comparisonAsOf = adjustmentAsOf.AddYears(-1);
        var comparativeStatements = await consolidation.GetComparativeStatementPackageAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, comparisonPeriodStart, comparisonAsOf); Assert.NotNull(comparativeStatements);
        Assert.Equal(4, comparativeStatements!.Statements.Count);
        Assert.Equal(adjustmentAsOf, comparativeStatements.Current.AsOf);
        Assert.Equal(comparisonAsOf, comparativeStatements.Comparison.AsOf);
        Assert.All(comparativeStatements.Statements.SelectMany(statement => statement.Lines), line => Assert.Equal(line.CurrentAmount - line.ComparisonAmount, line.Variance));
        Assert.All(comparativeStatements.Statements, statement => Assert.Equal(statement.CurrentTotal - statement.ComparisonTotal, statement.Variance));
        var comparativeAsset = comparativeStatements.Statements.Single(statement => statement.Code == "BALANCE-SHEET").Lines.Single(line => line.AccountNumber == adjustmentAsset.AccountNumber);
        Assert.Equal("CURRENT-ASSETS", comparativeAsset.CurrentSectionCode);
        Assert.Equal("Cash and other current assets", comparativeAsset.CurrentLineCaption);
        var comparativeCsv = await consolidation.ExportComparativeStatementPackageCsvAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, comparisonPeriodStart, comparisonAsOf); Assert.NotNull(comparativeCsv);
        Assert.Contains("Current Amount,Comparison Section Code,Comparison Section,Comparison Caption,Comparison Amount,Variance", comparativeCsv, StringComparison.Ordinal);
        Assert.Contains("\"Reconciliation\"", comparativeCsv, StringComparison.Ordinal);
        Assert.Contains(comparisonAsOf.ToString("yyyy-MM-dd"), comparativeCsv, StringComparison.Ordinal);
        Assert.Contains("\"Current ownership\",\"AcquisitionOfControl\",\"ACQ-US-DIST-1\",\"Measurement\"", comparativeCsv, StringComparison.Ordinal);
        Assert.Contains("\"PPA detail\"", comparativeCsv, StringComparison.Ordinal);
        Assert.Contains("Updated valuation report VR-2", comparativeCsv, StringComparison.Ordinal);
        var comparativeExcel = await consolidation.ExportComparativeStatementPackageExcelAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, comparisonPeriodStart, comparisonAsOf); Assert.NotNull(comparativeExcel);
        using (var workbook = new XLWorkbook(new MemoryStream(comparativeExcel!)))
        {
            Assert.Equal(9, workbook.Worksheets.Count);
            Assert.Equal("Current minus comparison", workbook.Worksheet("Summary").Cell("B7").GetString());
            Assert.Equal("Variance", workbook.Worksheet("Balance sheet").Cell("I7").GetString());
            Assert.Equal(comparativeAsset.Variance, workbook.Worksheet("Balance sheet").RowsUsed().Single(row => row.Cell(1).GetString() == comparativeAsset.AccountNumber).Cell(9).GetValue<decimal>());
            Assert.True(workbook.TryGetWorksheet("Current US-GAAP notes", out _));
            Assert.True(workbook.TryGetWorksheet("Current ownership", out _));
            Assert.Contains("Cash paid to sellers", workbook.Worksheet("Current ownership").CellsUsed().Select(cell => cell.GetString()));
        }
        var comparativePdf = await consolidation.ExportComparativeStatementPackagePdfAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, comparisonPeriodStart, comparisonAsOf); Assert.NotNull(comparativePdf);
        Assert.True(comparativePdf!.AsSpan().StartsWith("%PDF"u8));
        using (var document = PdfReader.Open(new MemoryStream(comparativePdf), PdfDocumentOpenMode.Import))
        {
            Assert.True(document.PageCount >= 8);
            Assert.Contains("comparative consolidated statements", document.Info.Title, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Null(await consolidation.GetComparativeStatementPackageAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, adjustmentPeriodStart, adjustmentAsOf));
        var stalePost = await consolidation.PostAdjustmentAsync(new(group.Id.Value, draft.Id, draft.ConcurrencyToken)); Assert.False(stalePost.Succeeded); Assert.Contains("changed", stalePost.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var approvedSnapshot = (await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value))!.Adjustments.Single(item => item.Id == draft.Id);
        SetConsolidationUser(reviewerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPost);
        var reviewerPost = await consolidation.PostAdjustmentAsync(new(group.Id.Value, draft.Id, approvedSnapshot.ConcurrencyToken)); Assert.False(reviewerPost.Succeeded); Assert.Contains("approved", reviewerPost.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetConsolidationUser(posterId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPost);
        Guid closedPostingPeriodId = Guid.NewGuid();
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync()) { db.AccountingPeriods.Add(new AccountingPeriod { Id = closedPostingPeriodId, CompanyId = currentCompanyId, StartsOn = adjustmentPeriodStart, EndsOn = adjustmentPeriodStart, Status = "Closed" }); await db.SaveChangesAsync(); }
        var closedPeriodPosting = await consolidation.PostAdjustmentAsync(new(group.Id.Value, draft.Id, approvedSnapshot.ConcurrencyToken)); Assert.False(closedPeriodPosting.Succeeded); Assert.Contains("closed", closedPeriodPosting.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync()) { (await db.AccountingPeriods.SingleAsync(period => period.Id == closedPostingPeriodId)).Status = "Open"; await db.SaveChangesAsync(); }
        var postedAdjustment = await consolidation.PostAdjustmentAsync(new(group.Id.Value, draft.Id, approvedSnapshot.ConcurrencyToken)); Assert.True(postedAdjustment.Succeeded, postedAdjustment.ErrorMessage);
        var adjustedReport = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(adjustedReport);
        Assert.Equal(beforeAdjustment!.Accounts.Single(account => account.AccountNumber == adjustmentAsset.AccountNumber).ConvertedBalance + 25m, adjustedReport!.Accounts.Single(account => account.AccountNumber == adjustmentAsset.AccountNumber).ConvertedBalance);
        Assert.Equal(beforeAdjustment.Accounts.Single(account => account.AccountNumber == adjustmentEquity.AccountNumber).ConvertedBalance + 25m, adjustedReport.Accounts.Single(account => account.AccountNumber == adjustmentEquity.AccountNumber).ConvertedBalance);
        Assert.Equal(0m, adjustedReport.Accounts.Sum(account => account.AccountType is "Asset" or "Expense" ? account.ConvertedBalance : -account.ConvertedBalance));
        var postedSnapshot = (await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value))!.Adjustments.Single(item => item.Id == draft.Id);
        SetConsolidationUser(reverserId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalReverse);
        Guid closedPeriodId = Guid.NewGuid();
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync()) { db.AccountingPeriods.Add(new AccountingPeriod { Id = closedPeriodId, CompanyId = currentCompanyId, StartsOn = adjustmentPeriodStart.AddDays(1), EndsOn = adjustmentPeriodStart.AddDays(1), Status = "Closed" }); await db.SaveChangesAsync(); }
        var closedPeriodReversal = await consolidation.ReverseAdjustmentAsync(new(group.Id.Value, draft.Id, "Closed-period reversal must fail", postedSnapshot.ConcurrencyToken)); Assert.False(closedPeriodReversal.Succeeded); Assert.Contains("closed", closedPeriodReversal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync()) { (await db.AccountingPeriods.SingleAsync(period => period.Id == closedPeriodId)).Status = "Open"; await db.SaveChangesAsync(); }
        var reversedAdjustment = await consolidation.ReverseAdjustmentAsync(new(group.Id.Value, draft.Id, "Remove test consolidation true-up", postedSnapshot.ConcurrencyToken)); Assert.True(reversedAdjustment.Succeeded, reversedAdjustment.ErrorMessage);
        var restoredReport = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.NotNull(restoredReport);
        Assert.Equal(beforeAdjustment.Accounts.Single(account => account.AccountNumber == adjustmentAsset.AccountNumber).ConvertedBalance, restoredReport!.Accounts.Single(account => account.AccountNumber == adjustmentAsset.AccountNumber).ConvertedBalance);
        Assert.Equal(beforeAdjustment.Accounts.Single(account => account.AccountNumber == adjustmentEquity.AccountNumber).ConvertedBalance, restoredReport.Accounts.Single(account => account.AccountNumber == adjustmentEquity.AccountNumber).ConvertedBalance);
        var postedNciSnapshot = (await consolidation.GetAdjustmentWorkspaceAsync(group.Id.Value))!.Adjustments.Single(item => item.Id == savedNci.Id);
        var reversedNci = await consolidation.ReverseAdjustmentAsync(new(group.Id.Value, savedNci.Id!.Value, "Replace reviewed NCI reclassification", postedNciSnapshot.ConcurrencyToken)); Assert.True(reversedNci.Succeeded, reversedNci.ErrorMessage);
        var afterNciReversal = await consolidation.GetBalanceReportAsync(group.Id.Value, adjustmentPeriodStart, adjustmentAsOf); Assert.Contains(afterNciReversal!.Warnings, warning => warning.Contains("no posted NCI reclassification", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, afterNciReversal.Accounts.Single(account => account.AccountNumber == nciEquity.AccountNumber).ConvertedBalance);
        SetConsolidationUser(preparerId, BrassLedgerPermissions.ReportingManage, BrassLedgerPermissions.JournalPrepare);
        var replacementNci = await consolidation.SaveAdjustmentAsync(new(null, group.Id.Value, adjustmentPeriodStart, adjustmentAsOf, nameof(ConsolidationAdjustmentKind.NoncontrollingInterest), "NCI-US-DIST-2", "Replacement reviewed NCI equity attribution", string.Empty,
            [new(adjustmentEquity.AccountNumber, adjustmentEquity.AccountName, adjustmentEquity.AccountType, 12m, 0m, "Replacement offset", usdAffiliateId), new(nciEquity.AccountNumber, nciEquity.AccountName, nciEquity.AccountType, 0m, 12m, "Replacement NCI", usdAffiliateId)], SubjectCompanyId: usdAffiliateId));
        Assert.True(replacementNci.Succeeded, replacementNci.ErrorMessage);
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
        {
            Assert.Equal(9, await db.BusinessAuditEntries.CountAsync(entry => entry.CompanyId == currentCompanyId && entry.EntityType == nameof(ConsolidationGroupCompany)));
            Assert.Equal(2, await db.BusinessAuditEntries.CountAsync(entry => entry.CompanyId == currentCompanyId && entry.EntityType == nameof(ConsolidationTradingPartner)));
            Assert.Equal(3, await db.BusinessAuditEntries.CountAsync(entry => entry.CompanyId == currentCompanyId && entry.EntityType == nameof(ConsolidationIntercompanyMatch)));
            Assert.Equal(11, await db.BusinessAuditEntries.CountAsync(entry => entry.CompanyId == currentCompanyId && entry.EntityType == nameof(ConsolidationAdjustmentBatch)));
            Assert.Equal(6, await db.ConsolidationAdjustmentBatches.CountAsync(batch => batch.ConsolidationGroupId == group.Id));
            Assert.Equal(2, await db.BusinessAuditEntries.CountAsync(entry => entry.CompanyId == currentCompanyId && entry.EntityType == nameof(ConsolidationDisclosurePackage)));
            Assert.Equal(18, await db.BusinessAuditEntries.CountAsync(entry => entry.CompanyId == currentCompanyId && entry.EntityType == nameof(ConsolidationOwnershipEvent)));
            Assert.Equal(7, await db.ConsolidationOwnershipEvents.CountAsync(item => item.ConsolidationGroupId == group.Id));
        }
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
        Assert.Equal("Controlled master-data API import", quickBooks.ImplementationStatus); Assert.True(quickBooks.LiveSynchronizationAvailable);
        Assert.True(quickBooks.SupportsSandbox);
        Assert.Contains("Protected OAuth lifecycle", quickBooks.SupportedCapabilities, StringComparison.OrdinalIgnoreCase);
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
        Assert.True(authenticationResult.User.MfaEnrollmentRequired);
        Assert.Empty(authenticationResult.User.Permissions);

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
        Assert.Contains(roles, role => role.Name == "Project Change Order Preparer" && role.Permissions.Contains(BrassLedgerPermissions.ProjectChangeOrderPrepare, StringComparison.Ordinal));
        Assert.Contains(roles, role => role.Name == "Project Change Order Approver" && role.Permissions.Contains(BrassLedgerPermissions.ProjectChangeOrderApprove, StringComparison.Ordinal));
        Assert.Contains(roles, role => role.Name == "Controller" && role.Permissions.Contains(BrassLedgerPermissions.ProjectChangeOrderPrepare, StringComparison.Ordinal) && role.Permissions.Contains(BrassLedgerPermissions.ProjectChangeOrderApprove, StringComparison.Ordinal));
        Assert.Contains(roles, role => role.Name == "Project WIP Preparer" && role.Permissions.Contains(BrassLedgerPermissions.ProjectWipPrepare, StringComparison.Ordinal));
        Assert.Contains(roles, role => role.Name == "Project WIP Approver" && role.Permissions.Contains(BrassLedgerPermissions.ProjectWipApprove, StringComparison.Ordinal));
        Assert.Contains(roles, role => role.Name == "Project WIP Poster" && role.Permissions.Contains(BrassLedgerPermissions.ProjectWipPost, StringComparison.Ordinal) && role.Permissions.Contains(BrassLedgerPermissions.ProjectWipReverse, StringComparison.Ordinal));
        Assert.Contains(roles, role => role.Name == "Project Billing Preparer" && role.Permissions.Contains(BrassLedgerPermissions.ProjectBillingPrepare, StringComparison.Ordinal) && role.Permissions.Contains(BrassLedgerPermissions.SubledgerPrepare, StringComparison.Ordinal));
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
    public async Task MultiFactorAuthentication_EnrollsChallengesUsesRecoveryOnceAndCanBeDisabled()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var configuration = new ConfigurationBuilder().Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<TimeProvider>(clock);
        serviceCollection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        using var services = serviceCollection.BuildServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var initial = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        Assert.Equal(AuthenticationOutcome.Succeeded, initial.Outcome);

        var rejectedEnrollment = await authentication.BeginMfaEnrollmentAsync(
            initial.User!.UserId, initial.User.CompanyId, "wrong-password", "127.0.0.1", "mfa-test");
        Assert.Equal(MfaOperationOutcome.InvalidPassword, rejectedEnrollment.Outcome);
        var enrollment = await authentication.BeginMfaEnrollmentAsync(
            initial.User.UserId, initial.User.CompanyId, BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        Assert.Equal(MfaOperationOutcome.Succeeded, enrollment.Outcome);
        Assert.Equal(32, enrollment.Secret.Length);
        Assert.Equal(BrassLedgerAuthenticationDefaults.RecoveryCodeCount, enrollment.RecoveryCodes!.Count);
        Assert.All(enrollment.RecoveryCodes, code => Assert.Matches("^[0-9A-F]{8}(-[0-9A-F]{8}){3}$", code));

        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            Assert.StartsWith("enc::", await ReadScalarAsync(connection, "SELECT MfaSecret FROM Users WHERE UserName = 'controller';"));
            Assert.DoesNotContain(enrollment.Secret, await ReadScalarAsync(connection, "SELECT MfaSecret FROM Users WHERE UserName = 'controller';"), StringComparison.Ordinal);
            var storedRecoveryHash = await ReadScalarAsync(connection, "SELECT CodeHash FROM MfaRecoveryCodes LIMIT 1;");
            Assert.Equal(64, storedRecoveryHash.Length);
            Assert.DoesNotContain(enrollment.RecoveryCodes[0].Replace("-", string.Empty, StringComparison.Ordinal), storedRecoveryHash, StringComparison.Ordinal);
        }

        var enrollmentStep = clock.GetUtcNow().ToUnixTimeSeconds() / TotpService.TimeStepSeconds;
        var enrollmentCode = TotpService.ComputeCode(TotpService.DecodeBase32(enrollment.Secret), enrollmentStep);
        var enabled = await authentication.EnableMfaAsync(
            initial.User.UserId, initial.User.CompanyId, enrollmentCode, "127.0.0.1", "mfa-test");
        Assert.Equal(MfaOperationOutcome.Succeeded, enabled.Outcome);

        var expiredPasswordStage = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        clock.Advance(TimeSpan.FromMinutes(BrassLedgerAuthenticationDefaults.MfaChallengeMinutes + 1));
        Assert.Equal(MfaOperationOutcome.Expired, (await authentication.CompleteMfaChallengeAsync(
            expiredPasswordStage.MfaChallengeToken, enrollment.RecoveryCodes[0], "127.0.0.1", "mfa-test")).Outcome);

        var passwordStage = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        Assert.Equal(AuthenticationOutcome.MfaRequired, passwordStage.Outcome);
        Assert.Null(passwordStage.User);
        Assert.NotEmpty(passwordStage.MfaChallengeToken);
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            var storedChallengeHash = await ReadScalarAsync(connection, "SELECT TokenHash FROM MfaSignInChallenges ORDER BY CreatedAtUtc DESC LIMIT 1;");
            Assert.Equal(64, storedChallengeHash.Length);
            Assert.NotEqual(passwordStage.MfaChallengeToken, storedChallengeHash);
        }
        var completed = await authentication.CompleteMfaChallengeAsync(
            passwordStage.MfaChallengeToken, enrollment.RecoveryCodes[0], "127.0.0.1", "mfa-test");
        Assert.Equal(MfaOperationOutcome.Succeeded, completed.Outcome);
        Assert.True(completed.UsedRecoveryCode);
        Assert.True(completed.User!.MfaAuthenticated);
        Assert.Equal(MfaOperationOutcome.InvalidCode, (await authentication.CompleteMfaChallengeAsync(
            passwordStage.MfaChallengeToken, enrollment.RecoveryCodes[0], "127.0.0.1", "mfa-test")).Outcome);

        var secondPasswordStage = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        Assert.Equal(AuthenticationOutcome.MfaRequired, secondPasswordStage.Outcome);
        Assert.Equal(MfaOperationOutcome.InvalidCode, (await authentication.CompleteMfaChallengeAsync(
            secondPasswordStage.MfaChallengeToken, enrollment.RecoveryCodes[0], "127.0.0.1", "mfa-test")).Outcome);
        clock.Advance(TimeSpan.FromSeconds(TotpService.TimeStepSeconds));
        var freshStep = clock.GetUtcNow().ToUnixTimeSeconds() / TotpService.TimeStepSeconds;
        var freshCode = TotpService.ComputeCode(TotpService.DecodeBase32(enrollment.Secret), freshStep);
        var secondCompleted = await authentication.CompleteMfaChallengeAsync(
            secondPasswordStage.MfaChallengeToken, freshCode, "127.0.0.1", "mfa-test");
        Assert.Equal(MfaOperationOutcome.Succeeded, secondCompleted.Outcome);
        Assert.False(secondCompleted.UsedRecoveryCode);

        var snapshot = await authentication.GetAccountSecurityAsync(initial.User.UserId);
        Assert.True(snapshot!.MfaEnabled);
        Assert.Equal(BrassLedgerAuthenticationDefaults.RecoveryCodeCount - 1, snapshot.RecoveryCodesRemaining);

        var replacement = await authentication.RegenerateMfaRecoveryCodesAsync(
            initial.User.UserId,
            initial.User.CompanyId,
            BrassLedgerAuthenticationDefaults.SeededPassword,
            enrollment.RecoveryCodes[2],
            "127.0.0.1",
            "mfa-test");
        Assert.Equal(MfaOperationOutcome.Succeeded, replacement.Outcome);
        Assert.Equal(BrassLedgerAuthenticationDefaults.RecoveryCodeCount, replacement.RecoveryCodes!.Count);
        var replacementChallenge = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        Assert.Equal(MfaOperationOutcome.InvalidCode, (await authentication.CompleteMfaChallengeAsync(
            replacementChallenge.MfaChallengeToken, enrollment.RecoveryCodes[3], "127.0.0.1", "mfa-test")).Outcome);
        var concurrentChallenge = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        var concurrentCompletions = await Task.WhenAll(
            authentication.CompleteMfaChallengeAsync(replacementChallenge.MfaChallengeToken, replacement.RecoveryCodes[0], "127.0.0.1", "mfa-test"),
            authentication.CompleteMfaChallengeAsync(concurrentChallenge.MfaChallengeToken, replacement.RecoveryCodes[0], "127.0.0.1", "mfa-test"));
        Assert.Single(concurrentCompletions, result => result.Outcome == MfaOperationOutcome.Succeeded);
        Assert.Single(concurrentCompletions, result => result.Outcome == MfaOperationOutcome.InvalidCode);

        var staleChallenge = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        Assert.Equal(AuthenticationOutcome.MfaRequired, staleChallenge.Outcome);
        Assert.Equal(AccountSecurityOutcome.Succeeded, (await authentication.RevokeOtherSessionsAsync(
            initial.User.UserId, initial.User.CompanyId, "127.0.0.1", "mfa-test")).Outcome);
        Assert.Equal(MfaOperationOutcome.Unauthorized, (await authentication.CompleteMfaChallengeAsync(
            staleChallenge.MfaChallengeToken, replacement.RecoveryCodes[2], "127.0.0.1", "mfa-test")).Outcome);

        var lockoutChallenge = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test");
        Assert.Equal(AuthenticationOutcome.MfaRequired, lockoutChallenge.Outcome);
        MfaChallengeResult? lockoutResult = null;
        for (var attempt = 0; attempt < BrassLedgerAuthenticationDefaults.MaxMfaAttempts; attempt++)
        {
            lockoutResult = await authentication.CompleteMfaChallengeAsync(
                lockoutChallenge.MfaChallengeToken, "invalid-code", "127.0.0.1", "mfa-test");
        }
        Assert.Equal(MfaOperationOutcome.LockedOut, lockoutResult!.Outcome);
        Assert.Equal(AuthenticationOutcome.LockedOut, (await authentication.AuthenticateAsync(
            "controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test")).Outcome);

        var replacementWhileLocked = await authentication.RegenerateMfaRecoveryCodesAsync(
            initial.User.UserId,
            initial.User.CompanyId,
            BrassLedgerAuthenticationDefaults.SeededPassword,
            replacement.RecoveryCodes[1],
            "127.0.0.1",
            "mfa-test");
        Assert.Equal(MfaOperationOutcome.LockedOut, replacementWhileLocked.Outcome);
        Assert.Equal(AccountSecurityOutcome.InvalidRequest, (await authentication.DisableMfaAsync(
            initial.User.UserId,
            initial.User.CompanyId,
            BrassLedgerAuthenticationDefaults.SeededPassword,
            replacement.RecoveryCodes[1],
            "127.0.0.1",
            "mfa-test")).Outcome);

        clock.Advance(TimeSpan.FromMinutes(BrassLedgerAuthenticationDefaults.LockoutMinutes + 1));

        var disabled = await authentication.DisableMfaAsync(
            initial.User.UserId,
            initial.User.CompanyId,
            BrassLedgerAuthenticationDefaults.SeededPassword,
            replacement.RecoveryCodes[1],
            "127.0.0.1",
            "mfa-test");
        Assert.Equal(AccountSecurityOutcome.Succeeded, disabled.Outcome);
        Assert.False(disabled.User!.MfaAuthenticated);
        Assert.Equal(AuthenticationOutcome.Succeeded, (await authentication.AuthenticateAsync(
            "controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "mfa-test")).Outcome);
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

        var invoiceResult = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(
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
        var first = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(customer.Id, "INV-PAY-MULTI-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m, 0m, "4000", "First payment invoice"));
        var second = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(customer.Id, "INV-PAY-MULTI-2", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 50m, 0m, "4000", "Second payment invoice"));
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
        var first = await PostVendorBillThroughWorkflowAsync(transactions, new CreateVendorBillRequest(vendor.Id, "B-PAY-MULTI-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 40m, "5100", "First payment bill"));
        var second = await PostVendorBillThroughWorkflowAsync(transactions, new CreateVendorBillRequest(vendor.Id, "B-PAY-MULTI-2", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 60m, "5100", "Second payment bill"));
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

        ActAs(BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare, BrassLedgerPermissions.SubledgerApprove, BrassLedgerPermissions.SubledgerPost);
        var invoice = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(customerId, "INV-PAY-SOD-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 25m, 0m, "4000", "Payment authority test"));
        Assert.True(invoice.Succeeded, invoice.ErrorMessage);
        ActAs(BrassLedgerPermissions.ReceivablesManage);
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
        var invoice = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(customer.Id, "INV-ADJ-1", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), 100m, 0m, "4000", "Adjustment test"));
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
        var invoice = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(customer.Id, "INV-VOID-1", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 80m, 4m, "4000", "Void invoice"));
        var bill = await PostVendorBillThroughWorkflowAsync(transactions, new CreateVendorBillRequest(vendor.Id, "B-VOID-1", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 60m, "5100", "Void bill"));
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
        var retry = await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value);
        Assert.True(retry.Succeeded, retry.ErrorMessage);
        Assert.Equal(posted.Id, retry.Id);
        await using (var verification = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync())
        {
            Assert.Equal(1, await verification.SalesInvoices.CountAsync(item => item.Id == posted.Id));
            Assert.Equal(1, await verification.JournalEntries.CountAsync(item => item.SourceDocumentType == "SalesInvoice" && item.SourceDocumentId == posted.Id));
        }
        Assert.Equal(afterPost.Receivables.OpenBalance, (await workspaceService.GetWorkspaceAsync()).Receivables.OpenBalance);

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
    public async Task SubledgerWorkflow_RollsBackJournalBalancesAndWorkflowWhenSourceDocumentInsertFails()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var before = await workspaceService.GetWorkspaceAsync();
        var request = new CreateInvoiceRequest(before.Receivables.Customers.First().Id, "INV-WF-ROLLBACK-1", new DateOnly(2026, 8, 3), new DateOnly(2026, 9, 2), 73m, 0m, "4000", "Atomic workflow rollback");
        var draft = await transactions.SaveInvoiceDraftAsync(request);
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(draft.Id!.Value)).Succeeded);

        await using (var triggerDb = await factory.CreateDbContextAsync())
        {
            await triggerDb.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER SimulateSalesInvoiceInsertFailure
                BEFORE INSERT ON SalesInvoices
                BEGIN
                    SELECT RAISE(ABORT, 'simulated source insert failure');
                END;
                """);
        }
        var failed = await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value);
        Assert.False(failed.Succeeded);
        await using (var triggerDb = await factory.CreateDbContextAsync())
        {
            await triggerDb.Database.ExecuteSqlRawAsync("DROP TRIGGER SimulateSalesInvoiceInsertFailure;");
        }

        var afterFailure = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.Receivables.OpenBalance, afterFailure.Receivables.OpenBalance);
        Assert.DoesNotContain(afterFailure.Receivables.Invoices, item => item.InvoiceNumber == request.InvoiceNumber);
        await using (var verification = await factory.CreateDbContextAsync())
        {
            Assert.Equal("Approved", await verification.SubledgerDocumentWorkflows.Where(item => item.Id == draft.Id).Select(item => item.Status).SingleAsync());
            Assert.False(await verification.JournalEntries.AnyAsync(item => item.Reference == request.InvoiceNumber));
            Assert.False(await verification.BusinessAuditEntries.AnyAsync(item => item.Action == "journal.posted" && item.DetailJson.Contains(request.InvoiceNumber)));
        }

        var retry = await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value);
        Assert.True(retry.Succeeded, retry.ErrorMessage);
        Assert.Equal(before.Receivables.OpenBalance + 73m, (await workspaceService.GetWorkspaceAsync()).Receivables.OpenBalance);
    }

    [Fact]
    public async Task SubledgerWorkflow_ConcurrentPostingCreatesExactlyOneDocumentAndIsRetryable()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var setupScope = services.CreateScope();
        var setupWorkspace = await setupScope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var setupTransactions = setupScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var request = new CreateInvoiceRequest(setupWorkspace.Receivables.Customers.First().Id, "INV-WF-CONCURRENT-1", new DateOnly(2026, 8, 4), new DateOnly(2026, 9, 3), 41m, 0m, "4000", "Concurrent workflow posting");
        var draft = await setupTransactions.SaveInvoiceDraftAsync(request);
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        Assert.True((await setupTransactions.ApproveSubledgerDocumentAsync(draft.Id!.Value)).Succeeded);

        using var firstScope = services.CreateScope();
        using var secondScope = services.CreateScope();
        var firstTransactions = firstScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var secondTransactions = secondScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var attempts = await Task.WhenAll(
            firstTransactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value),
            secondTransactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value));
        Assert.Contains(attempts, attempt => attempt.Succeeded);

        var firstRetry = await firstTransactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value);
        var secondRetry = await secondTransactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value);
        Assert.True(firstRetry.Succeeded, firstRetry.ErrorMessage);
        Assert.True(secondRetry.Succeeded, secondRetry.ErrorMessage);
        Assert.Equal(firstRetry.Id, secondRetry.Id);
        await using var verification = await setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync();
        var invoice = await verification.SalesInvoices.SingleAsync(item => item.InvoiceNumber == request.InvoiceNumber);
        Assert.Equal(invoice.Id, firstRetry.Id);
        Assert.Equal(1, await verification.JournalEntries.CountAsync(item => item.SourceDocumentType == "SalesInvoice" && item.SourceDocumentId == invoice.Id));
        Assert.Equal("Posted", await verification.SubledgerDocumentWorkflows.Where(item => item.Id == draft.Id).Select(item => item.Status).SingleAsync());
    }

    [Fact]
    public async Task SubledgerWorkflow_EnforcesPreparationApprovalAndPostingPermissionsSeparately()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(item => item.Id).FirstAsync(); var customerId = await db.Customers.Where(item => item.CompanyId == companyId).Select(item => item.Id).FirstAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(); var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        void ActAs(Guid userId, params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()), new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        var preparerId = Guid.NewGuid(); var approverId = Guid.NewGuid(); var posterId = Guid.NewGuid();
        var request = new CreateInvoiceRequest(customerId, "INV-WF-SOD-1", new DateOnly(2026, 8, 2), new DateOnly(2026, 9, 1), 10m, 0m, "4000", "Workflow permissions");
        ActAs(preparerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var draft = await transactions.SaveInvoiceDraftAsync(request); Assert.True(draft.Succeeded, draft.ErrorMessage); Assert.False((await transactions.ApproveSubledgerDocumentAsync(draft.Id!.Value)).Succeeded);
        ActAs(preparerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        var selfApproval = await transactions.ApproveSubledgerDocumentAsync(draft.Id.Value); Assert.False(selfApproval.Succeeded); Assert.Contains("prepared", selfApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(approverId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(draft.Id.Value)).Succeeded); Assert.False((await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value)).Succeeded);
        ActAs(approverId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost);
        var selfPosting = await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value); Assert.False(selfPosting.Succeeded); Assert.Contains("approved", selfPosting.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost);
        Assert.True((await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value)).Succeeded);
    }

    [Fact]
    public async Task SubledgerWorkflow_RejectsWithConcurrencyAndAuditThenRevisesAndResubmits()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var setupDb = await factory.CreateDbContextAsync();
        var companyId = await setupDb.Companies.Select(item => item.Id).FirstAsync();
        var customerId = await setupDb.Customers.Where(item => item.CompanyId == companyId).Select(item => item.Id).FirstAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        void ActAs(Guid userId, params string[] permissions)
        {
            var claims = new List<System.Security.Claims.Claim>
            {
                new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())
            };
            claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }
        async Task<SubledgerDocumentWorkflow> LoadWorkflowAsync(Guid id)
        {
            await using var verification = await factory.CreateDbContextAsync();
            return await verification.SubledgerDocumentWorkflows.AsNoTracking().SingleAsync(item => item.Id == id);
        }

        var preparerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var posterId = Guid.NewGuid();
        var request = new CreateInvoiceRequest(customerId, "INV-WF-REJECT-1", new DateOnly(2026, 8, 5), new DateOnly(2026, 9, 4), 33m, 0m, "4000", "Original draft");
        ActAs(preparerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var saved = await transactions.SaveInvoiceDraftAsync(request);
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var draft = await LoadWorkflowAsync(saved.Id!.Value);

        ActAs(preparerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        var selfRejection = await transactions.RejectSubledgerDocumentAsync(new RejectSubledgerDocumentRequest(draft.Id, "I prepared this.", draft.ConcurrencyToken));
        Assert.False(selfRejection.Succeeded);
        Assert.Contains("prepared", selfRejection.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        ActAs(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        var staleRejection = await transactions.RejectSubledgerDocumentAsync(new RejectSubledgerDocumentRequest(draft.Id, "Correct the description.", "stale-token"));
        Assert.False(staleRejection.Succeeded);
        Assert.Contains("changed", staleRejection.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var rejection = await transactions.RejectSubledgerDocumentAsync(new RejectSubledgerDocumentRequest(draft.Id, "Correct the description.", draft.ConcurrencyToken));
        Assert.True(rejection.Succeeded, rejection.ErrorMessage);

        var rejected = await LoadWorkflowAsync(draft.Id);
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal(reviewerId, rejected.RejectedByUserId);
        Assert.NotNull(rejected.RejectedAtUtc);
        Assert.Equal("Correct the description.", rejected.DecisionReason);
        Assert.NotEqual(draft.ConcurrencyToken, rejected.ConcurrencyToken);
        ActAs(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost);
        Assert.False((await transactions.PostApprovedSubledgerDocumentAsync(draft.Id)).Succeeded);

        ActAs(preparerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var revised = await transactions.SaveInvoiceDraftAsync(request with { Description = "Corrected draft" });
        Assert.True(revised.Succeeded, revised.ErrorMessage);
        Assert.Equal(draft.Id, revised.Id);
        var corrected = await LoadWorkflowAsync(draft.Id);
        Assert.Equal("Draft", corrected.Status);
        Assert.Equal(preparerId, corrected.CreatedByUserId);
        Assert.Null(corrected.RejectedByUserId);
        Assert.Null(corrected.RejectedAtUtc);
        Assert.Empty(corrected.DecisionReason);
        Assert.NotEqual(rejected.ConcurrencyToken, corrected.ConcurrencyToken);

        ActAs(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(draft.Id)).Succeeded);
        ActAs(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost);
        Assert.True((await transactions.PostApprovedSubledgerDocumentAsync(draft.Id)).Succeeded);

        await using var auditDb = await factory.CreateDbContextAsync();
        var audits = (await auditDb.BusinessAuditEntries.Where(item => item.EntityId == draft.Id).ToListAsync()).OrderBy(item => item.OccurredAtUtc).ToList();
        Assert.Contains(audits, item => item.Action == "subledger-document.rejected" && item.UserId == reviewerId && item.DetailJson.Contains("Correct the description.", StringComparison.Ordinal));
        Assert.Contains(audits, item => item.Action == "subledger-document.revised" && item.UserId == preparerId && item.DetailJson.Contains("previousReason", StringComparison.Ordinal));
        Assert.Contains(audits, item => item.Action == "subledger-document.posted" && item.UserId == posterId);
    }

    [Fact]
    public async Task SubledgerWorkflow_ApprovalPreflightRejectsCorruptedPayloadWithoutPosting()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var request = new CreateInvoiceRequest(workspace.Receivables.Customers.First().Id, "INV-WF-PREFLIGHT-1", new DateOnly(2026, 8, 6), new DateOnly(2026, 9, 5), 19m, 0m, "4000", "Approval preflight");
        var saved = await transactions.SaveInvoiceDraftAsync(request);
        Assert.True(saved.Succeeded, saved.ErrorMessage);

        await using (var corruptingDb = await factory.CreateDbContextAsync())
        {
            var workflow = await corruptingDb.SubledgerDocumentWorkflows.SingleAsync(item => item.Id == saved.Id);
            workflow.PayloadJson = System.Text.Json.JsonSerializer.Serialize(request with { CustomerId = Guid.NewGuid() });
            workflow.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await corruptingDb.SaveChangesAsync();
        }

        var approval = await transactions.ApproveSubledgerDocumentAsync(saved.Id!.Value);
        Assert.False(approval.Succeeded);
        Assert.Contains("not postable", approval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Customer not found", approval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using var verification = await factory.CreateDbContextAsync();
        Assert.Equal("Draft", await verification.SubledgerDocumentWorkflows.Where(item => item.Id == saved.Id).Select(item => item.Status).SingleAsync());
        Assert.False(await verification.SalesInvoices.AnyAsync(item => item.InvoiceNumber == request.InvoiceNumber));
        Assert.False(await verification.JournalEntries.AnyAsync(item => item.Reference == request.InvoiceNumber));
    }

    [Fact]
    public void AccountingTransactionContract_DoesNotExposeDirectInvoiceOrVendorBillPosting()
    {
        var methodNames = typeof(IAccountingTransactionService).GetMethods().Select(method => method.Name).ToArray();
        Assert.DoesNotContain("CreateInvoiceAsync", methodNames);
        Assert.DoesNotContain("CreateVendorBillAsync", methodNames);
        Assert.Contains("SaveInvoiceDraftAsync", methodNames);
        Assert.Contains("SaveVendorBillDraftAsync", methodNames);
        Assert.Contains("ApproveSubledgerDocumentAsync", methodNames);
        Assert.Contains("RejectSubledgerDocumentAsync", methodNames);
        Assert.Contains("PostApprovedSubledgerDocumentAsync", methodNames);
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

        var result = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(
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
        var result = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(
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
            setupDb.Accounts.Add(new GeneralLedgerAccount { Id = Guid.NewGuid(), CompanyId = companyId, Number = "6210", Name = "Office Expense", Type = AccountType.Expense, IsActive = true });
            await setupDb.SaveChangesAsync();
        }

        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var vendor = (await workspaceService.GetWorkspaceAsync()).Payables.Vendors.First();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var result = await PostVendorBillThroughWorkflowAsync(transactions, new CreateVendorBillRequest(
            vendor.Id, "B-LINES-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 9999m, "invalid-summary-account", "Itemized bill",
            [
                new VendorBillLineRequest("Materials", 2m, 25m, 5m, 3m, "5100"),
                new VendorBillLineRequest("Supplies", 1m, 40m, 0m, 2m, "6210")
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
        Assert.Contains(postings, line => line.Number == "6210" && line.Debit == 42m);
        Assert.Contains(postings, line => line.Number == "2000" && line.Credit == 90m);
        Assert.Equal(postings.Sum(line => line.Debit), postings.Sum(line => line.Credit));
        var snapshot = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(2, snapshot.Payables.Bills.Single(item => item.Id == bill.Id).Lines?.Count);
    }

    [Fact]
    public async Task VendorBills_ScopeSupplierInvoiceNumbersByVendor()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var vendors = workspace.Payables.Vendors.Take(2).ToArray();
        Assert.Equal(2, vendors.Length);
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var billDate = new DateOnly(2026, 5, 1);
        var dueDate = new DateOnly(2026, 5, 31);

        var firstRequest = new CreateVendorBillRequest(
            vendors[0].Id,
            "1001",
            billDate,
            dueDate,
            25m,
            "5100",
            "First vendor's invoice 1001");
        var secondRequest = new CreateVendorBillRequest(
            vendors[1].Id,
            "1001",
            billDate,
            dueDate,
            40m,
            "5100",
            "Second vendor's invoice 1001");
        var duplicateRequest = new CreateVendorBillRequest(
            vendors[0].Id,
            "1001",
            billDate,
            dueDate,
            10m,
            "5100",
            "Duplicate from first vendor");

        async Task<TransactionResult> PostThroughWorkflowAsync(CreateVendorBillRequest request)
        {
            var draft = await transactions.SaveVendorBillDraftAsync(request);
            if (!draft.Succeeded) return draft;
            var approval = await transactions.ApproveSubledgerDocumentAsync(draft.Id!.Value);
            return approval.Succeeded ? await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value) : approval;
        }
        var first = await PostThroughWorkflowAsync(firstRequest);
        var second = await PostThroughWorkflowAsync(secondRequest);
        var duplicate = await transactions.SaveVendorBillDraftAsync(duplicateRequest);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.False(duplicate.Succeeded);
        Assert.Contains("customer or vendor", duplicate.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.VendorBills.CountAsync(bill => bill.BillNumber == "1001"));
        Assert.Equal(2, await db.VendorBills.Where(bill => bill.BillNumber == "1001").Select(bill => bill.VendorId).Distinct().CountAsync());
    }

    [Fact]
    public async Task TransactionService_RejectsInvalidDocumentLinesWithoutPosting()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        var result = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(
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

        var result = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(customer.Id, "INV-CREDIT-TEST-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), customer.CreditLimit, 1m, "4000", "Credit limit test"));

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
        var interchange = scope.ServiceProvider.GetRequiredService<IAccountingInterchangeService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var before = await workspaceService.GetWorkspaceAsync();

        var posted = await PostJournalThroughWorkflowAsync(transactions, new SaveJournalEntryDraftRequest(null, new DateOnly(2026, 5, 1), "JE-TEST-1", "Journal test",
            [new JournalLineRequest("1000", 50m, 0m, "Cash adjustment"), new JournalLineRequest("4000", 0m, 50m, "Revenue adjustment")]));
        Assert.True(posted.Succeeded, posted.ErrorMessage);
        var invalidDraft = await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(null, new DateOnly(2026, 5, 1), "JE-TEST-2", "Invalid journal test",
            [new JournalLineRequest("1000", 50m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 40m, "Credit")]));
        Assert.True(invalidDraft.Succeeded, invalidDraft.ErrorMessage);
        Assert.False((await transactions.ApproveJournalEntryAsync(invalidDraft.Id!.Value)).Succeeded);
        var controlAccountJournal = await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(null, new DateOnly(2026, 5, 1), "JE-TEST-3", "Control account journal",
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

        var draftSnapshot = (await workspaceService.GetWorkspaceAsync()).GeneralLedger.RecentEntries.Single(entry => entry.Id == draft.Id);
        var balancedDraft = await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(draft.Id, date, "JE-LIFECYCLE-1", "Lifecycle test",
            [new JournalLineRequest("1000", 75m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 75m, "Balanced credit")], draftSnapshot.ConcurrencyToken));
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

        void ActAs(Guid userId, params string[] permissions)
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())
            };
            claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test"));
            accessor.HttpContext = context;
        }

        var preparerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var posterId = Guid.NewGuid();
        var reverserId = Guid.NewGuid();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();

        ActAs(preparerId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove);
        var draft = await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(null, new DateOnly(2026, 5, 6), "JE-SOD-1", "Separation of duties",
            [new JournalLineRequest("1000", 20m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 20m, "Credit")]));
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        var selfApproval = await transactions.ApproveJournalEntryAsync(draft.Id!.Value);
        Assert.False(selfApproval.Succeeded);
        Assert.Contains("prepared", selfApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var current = (await workspaceService.GetWorkspaceAsync()).GeneralLedger.RecentEntries.Single(entry => entry.Id == draft.Id);
        var selfRejection = await transactions.RejectJournalEntryAsync(new(draft.Id.Value, "Prepared amount needs support.", current.ConcurrencyToken));
        Assert.False(selfRejection.Succeeded);
        Assert.Contains("prepared", selfRejection.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        ActAs(reviewerId, BrassLedgerPermissions.JournalApprove);
        var staleRejection = await transactions.RejectJournalEntryAsync(new(draft.Id.Value, "Stale review.", "stale-token"));
        Assert.False(staleRejection.Succeeded);
        Assert.Contains("changed", staleRejection.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var rejection = await transactions.RejectJournalEntryAsync(new(draft.Id.Value, "Attach the supporting calculation.", current.ConcurrencyToken));
        Assert.True(rejection.Succeeded, rejection.ErrorMessage);
        current = (await workspaceService.GetWorkspaceAsync()).GeneralLedger.RecentEntries.Single(entry => entry.Id == draft.Id);
        Assert.Equal("Rejected", current.Status);
        Assert.Equal("Attach the supporting calculation.", current.DecisionReason);

        ActAs(preparerId, BrassLedgerPermissions.JournalPrepare);
        var correction = await transactions.SaveJournalEntryDraftAsync(new(draft.Id, new DateOnly(2026, 5, 6), "JE-SOD-1", "Separation of duties — support attached",
            [new JournalLineRequest("1000", 20m, 0m, "Corrected debit"), new JournalLineRequest("4000", 0m, 20m, "Corrected credit")], current.ConcurrencyToken));
        Assert.True(correction.Succeeded, correction.ErrorMessage);
        current = (await workspaceService.GetWorkspaceAsync()).GeneralLedger.RecentEntries.Single(entry => entry.Id == draft.Id);
        Assert.Equal("Draft", current.Status);
        Assert.Equal(string.Empty, current.DecisionReason);

        ActAs(reviewerId, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost);
        Assert.True((await transactions.ApproveJournalEntryAsync(draft.Id.Value)).Succeeded);
        var selfPosting = await transactions.PostApprovedJournalEntryAsync(draft.Id.Value);
        Assert.False(selfPosting.Succeeded);
        Assert.Contains("approved", selfPosting.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        ActAs(posterId, BrassLedgerPermissions.JournalPost);
        Assert.True((await transactions.PostApprovedJournalEntryAsync(draft.Id.Value)).Succeeded);
        Assert.False((await transactions.ReverseJournalEntryAsync(new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 7), "Not authorized"))).Succeeded);

        ActAs(reverserId, BrassLedgerPermissions.JournalReverse);
        Assert.True((await transactions.ReverseJournalEntryAsync(new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 7), "Authorized reversal"))).Succeeded);

        var auditActions = await db.BusinessAuditEntries.Where(entry => entry.EntityId == draft.Id).Select(entry => entry.Action).ToListAsync();
        Assert.Contains("journal.rejected", auditActions);
        Assert.Contains("journal.draft.revised", auditActions);
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
        var invoice = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(customer.Id, "INV-BANK-MAP-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m, 0m, "4000", "Bank mapping"));
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
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var before = await workspaceService.GetWorkspaceAsync();
        var bank = before.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010");
        var employee = before.Payroll.Employees.First();
        var request = new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 5, 15), "PAY-CALCULATED", [new EmployeePayrollInput(employee.Id, 1_000m)]);
        var preview = await transactions.PreviewEmployeePayrollRunAsync(request);
        Assert.NotNull(preview);

        var result = await PostEmployeePayrollThroughWorkflowAsync(transactions, factory, request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var after = await workspaceService.GetWorkspaceAsync();
        var liabilityTotal = preview!.PreTaxDeductions + preview.EmployeeWithholdings + preview.PostTaxDeductions + preview.EmployerPayrollTaxes + preview.EmployerBenefitContributions;
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "2200").Balance + liabilityTotal, after.GeneralLedger.Accounts.Single(account => account.Number == "2200").Balance);
        Assert.Equal(bank.CurrentBalance - preview.NetPay, after.Treasury.BankAccounts.Single(account => account.Id == bank.Id).CurrentBalance);
        Assert.Contains(after.GeneralLedger.RecentEntries, entry => entry.SourceModule == "Payroll" && entry.TotalAmount == preview.GrossPayroll + preview.EmployerPayrollTaxes + preview.EmployerBenefitContributions);
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
        var originalLocation = await before.InventoryLocationBalances.SingleAsync(balance => balance.InventoryItemId == item.Id);
        var result = await transactions.RecordInventoryAdjustmentAsync(new RecordInventoryAdjustmentRequest(item.Id, new DateOnly(2026, 5, 16), 3m, 12m, "INV-ADJ-1", "Cycle count increase"));
        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(originalQuantity + 3m, (await after.InventoryItems.SingleAsync(candidate => candidate.Id == item.Id)).QuantityOnHand);
        var movement = await after.InventoryTransactions.SingleAsync(transaction => transaction.JournalEntryId == result.Id);
        Assert.Equal(36m, movement.TotalCost); Assert.Equal(originalLocation.WarehouseId, movement.WarehouseId); Assert.Equal(originalLocation.BinId, movement.BinId);
        Assert.Equal(originalLocation.QuantityOnHand + 3m, await after.InventoryLocationBalances.Where(balance => balance.Id == originalLocation.Id).Select(balance => balance.QuantityOnHand).SingleAsync());
        var lines = await after.JournalEntryLines.Where(line => line.JournalEntryId == result.Id).ToListAsync();
        Assert.Equal(lines.Sum(line => line.Debit), lines.Sum(line => line.Credit));
    }

    [Fact]
    public async Task InventoryLocations_AdoptBalancesTransferWithoutPostingAndEnforceReservedStock()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid customerId; Guid itemId; Guid mainWarehouseId; Guid mainBinId; Guid foreignWarehouseId; Guid foreignBinId; decimal companyQuantity; int journalCount;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync(); customerId = await db.Customers.Select(customer => customer.Id).FirstAsync(); var item = await db.InventoryItems.SingleAsync(candidate => candidate.Sku == "RM-220"); itemId = item.Id; companyQuantity = item.QuantityOnHand; journalCount = await db.JournalEntries.CountAsync();
            var main = await db.InventoryWarehouses.SingleAsync(warehouse => warehouse.CompanyId == companyId && warehouse.IsDefault); mainWarehouseId = main.Id; mainBinId = await db.InventoryBins.Where(bin => bin.WarehouseId == main.Id && bin.IsDefault).Select(bin => bin.Id).SingleAsync();
            Assert.Equal(item.QuantityOnHand, (await db.InventoryLocationBalances.Where(balance => balance.InventoryItemId == item.Id).Select(balance => balance.QuantityOnHand).ToListAsync()).Sum());
            Assert.All(await db.InventoryTransactions.Where(movement => movement.CompanyId == companyId).ToListAsync(), movement => { Assert.NotNull(movement.WarehouseId); Assert.NotNull(movement.BinId); });
            var foreignCompanyId = Guid.NewGuid(); foreignWarehouseId = Guid.NewGuid(); foreignBinId = Guid.NewGuid();
            db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Foreign inventory company", LegalName = "Foreign inventory company", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
            db.InventoryWarehouses.Add(new InventoryWarehouse { Id = foreignWarehouseId, CompanyId = foreignCompanyId, Code = "FOREIGN", Name = "Foreign warehouse", IsDefault = true, DefaultMarker = "DEFAULT", IsActive = true });
            db.InventoryBins.Add(new InventoryBin { Id = foreignBinId, CompanyId = foreignCompanyId, WarehouseId = foreignWarehouseId, Code = "STOCK", Name = "Foreign stock", IsDefault = true, DefaultMarker = "DEFAULT", IsActive = true });
            await db.SaveChangesAsync();
        }
        void ActAs(params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }

        ActAs(BrassLedgerPermissions.FulfillmentManage); Assert.False((await transactions.SaveInventoryWarehouseAsync(new(null, "EAST", "East warehouse", "", "", "", "", "", "US", false, true))).Succeeded);
        ActAs(BrassLedgerPermissions.PurchasingManage); var warehouseResult = await transactions.SaveInventoryWarehouseAsync(new(null, "EAST", "East warehouse", "", "", "Detroit", "MI", "48201", "US", false, true)); Assert.True(warehouseResult.Succeeded, warehouseResult.ErrorMessage);
        Assert.False((await transactions.SaveInventoryWarehouseAsync(new(foreignWarehouseId, "STOLEN", "Cross-company edit", "", "", "", "", "", "US", false, true, ""))).Succeeded);
        Guid eastBinId; string eastWarehouseToken; await using (var db = await factory.CreateDbContextAsync()) { eastBinId = await db.InventoryBins.Where(bin => bin.WarehouseId == warehouseResult.Id && bin.IsDefault).Select(bin => bin.Id).SingleAsync(); eastWarehouseToken = await db.InventoryWarehouses.Where(warehouse => warehouse.Id == warehouseResult.Id).Select(warehouse => warehouse.ConcurrencyToken).SingleAsync(); }
        Assert.False((await transactions.SaveInventoryWarehouseAsync(new(warehouseResult.Id, "EAST", "Stale edit", "", "", "Detroit", "MI", "48201", "US", false, true, "stale-token"))).Succeeded);
        Assert.True((await transactions.SaveInventoryWarehouseAsync(new(warehouseResult.Id, "EAST", "East distribution", "1 East Road", "", "Detroit", "MI", "48201", "US", false, true, eastWarehouseToken))).Succeeded);
        Assert.False((await transactions.TransferInventoryAsync(new(itemId, mainWarehouseId, mainBinId, warehouseResult.Id!.Value, eastBinId, 5m, new DateOnly(2026, 8, 25), "XFER-LOC-1", "Stage eastern orders"))).Succeeded);
        ActAs(BrassLedgerPermissions.FulfillmentManage); Assert.False((await transactions.TransferInventoryAsync(new(itemId, mainWarehouseId, mainBinId, foreignWarehouseId, foreignBinId, 1m, new DateOnly(2026, 8, 25), "XFER-FOREIGN", "Cross-company destination"))).Succeeded);
        var transfer = await transactions.TransferInventoryAsync(new(itemId, mainWarehouseId, mainBinId, warehouseResult.Id.Value, eastBinId, 5m, new DateOnly(2026, 8, 25), "XFER-LOC-1", "Stage eastern orders")); Assert.True(transfer.Succeeded, transfer.ErrorMessage);
        string transferToken; await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(companyQuantity, await db.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync()); Assert.Equal(journalCount, await db.JournalEntries.CountAsync());
            Assert.Equal(5m, await db.InventoryLocationBalances.Where(balance => balance.InventoryItemId == itemId && balance.BinId == eastBinId).Select(balance => balance.QuantityOnHand).SingleAsync()); Assert.Equal(2, await db.InventoryTransactions.CountAsync(movement => movement.InventoryTransferId == transfer.Id && movement.JournalEntryId == null));
            transferToken = await db.InventoryTransfers.Where(candidate => candidate.Id == transfer.Id).Select(candidate => candidate.ConcurrencyToken).SingleAsync();
        }
        ActAs(BrassLedgerPermissions.PurchasingManage); string currentEastToken; await using (var db = await factory.CreateDbContextAsync()) currentEastToken = await db.InventoryWarehouses.Where(warehouse => warehouse.Id == warehouseResult.Id).Select(warehouse => warehouse.ConcurrencyToken).SingleAsync();
        var deactivateStockedWarehouse = await transactions.SaveInventoryWarehouseAsync(new(warehouseResult.Id, "EAST", "East distribution", "1 East Road", "", "Detroit", "MI", "48201", "US", false, false, currentEastToken)); Assert.False(deactivateStockedWarehouse.Succeeded); Assert.Contains("stock", deactivateStockedWarehouse.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(BrassLedgerPermissions.FulfillmentManage);
        Assert.False((await transactions.ReverseInventoryTransferAsync(new(transfer.Id!.Value, new DateOnly(2026, 8, 25), "Stale", "stale-token"))).Succeeded);
        Assert.True((await transactions.ReverseInventoryTransferAsync(new(transfer.Id.Value, new DateOnly(2026, 8, 25), "Return staging stock", transferToken))).Succeeded);
        var staged = await transactions.TransferInventoryAsync(new(itemId, mainWarehouseId, mainBinId, warehouseResult.Id.Value, eastBinId, 4m, new DateOnly(2026, 8, 26), "XFER-LOC-2", "Stage allocated order")); Assert.True(staged.Succeeded, staged.ErrorMessage);

        ActAs(BrassLedgerPermissions.SalesManage); var orderResult = await transactions.SaveSalesOrderAsync(new(null, customerId, "SO-LOC-1", new DateOnly(2026, 8, 26), null, "East fulfillment", [new SalesOrderLineRequest(itemId, "Located fasteners", 4m, 20m, 0m, 0m, "4000")])); Assert.True(orderResult.Succeeded, orderResult.ErrorMessage);
        Guid orderLineId; string orderToken; await using (var db = await factory.CreateDbContextAsync()) { var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == orderResult.Id); orderLineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Id).SingleAsync(); Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); orderToken = await db.SalesOrders.Where(candidate => candidate.Id == order.Id).Select(candidate => candidate.ConcurrencyToken).SingleAsync(); }
        ActAs(BrassLedgerPermissions.FulfillmentManage); var allocation = await transactions.AllocateSalesOrderAsync(new(orderResult.Id!.Value, [new AllocateSalesOrderLineRequest(orderLineId, 4m)], orderToken, warehouseResult.Id.Value, eastBinId)); Assert.True(allocation.Succeeded, allocation.ErrorMessage);
        var reservedTransfer = await transactions.TransferInventoryAsync(new(itemId, warehouseResult.Id.Value, eastBinId, mainWarehouseId, mainBinId, 1m, new DateOnly(2026, 8, 26), "XFER-LOC-RESERVED", "Would consume reserved stock")); Assert.False(reservedTransfer.Succeeded); Assert.Contains("unreserved", reservedTransfer.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(BrassLedgerPermissions.PurchasingManage); var reservedAdjustment = await transactions.RecordInventoryAdjustmentAsync(new(itemId, new DateOnly(2026, 8, 26), -1m, 1m, "ADJ-LOC-RESERVED", "Would consume reserved stock", warehouseResult.Id.Value, eastBinId)); Assert.False(reservedAdjustment.Succeeded); Assert.Contains("reserved", reservedAdjustment.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(BrassLedgerPermissions.FulfillmentManage);
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == orderResult.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var shipment = await transactions.ShipSalesOrderAsync(new(orderResult.Id.Value, "SHIP-LOC-1", new DateOnly(2026, 8, 27), [new ShipSalesOrderLineRequest(orderLineId, 2m)], orderToken)); Assert.True(shipment.Succeeded, shipment.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync(); var postedShipment = await after.InventoryShipments.SingleAsync(candidate => candidate.Id == shipment.Id); Assert.Equal(warehouseResult.Id, postedShipment.WarehouseId); Assert.Equal(eastBinId, postedShipment.BinId); Assert.Equal(2m, await after.InventoryLocationBalances.Where(balance => balance.InventoryItemId == itemId && balance.BinId == eastBinId).Select(balance => balance.QuantityOnHand).SingleAsync()); Assert.Equal(companyQuantity - 2m, await after.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync());
    }

    [Fact]
    public async Task PickPackAndBackorders_PreserveCommitmentsProvenanceAndReversibleShipmentState()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope(); var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid customerId; Guid itemId; await using (var db = await factory.CreateDbContextAsync()) { companyId = await db.Companies.Select(company => company.Id).SingleAsync(); customerId = await db.Customers.Where(customer => customer.CompanyId == companyId).Select(customer => customer.Id).FirstAsync(); itemId = await db.InventoryItems.Where(item => item.CompanyId == companyId && item.Sku == "RM-220").Select(item => item.Id).SingleAsync(); }
        void ActAsCompany(Guid activeCompanyId, params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, activeCompanyId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        void ActAs(params string[] permissions) => ActAsCompany(companyId, permissions);
        var today = DateOnly.FromDateTime(DateTime.Today); ActAs(BrassLedgerPermissions.SalesManage); var saved = await transactions.SaveSalesOrderAsync(new(null, customerId, "SO-PICK-1", today, today.AddDays(1), "Pick and pack", [new SalesOrderLineRequest(itemId, "Picked fasteners", 5m, 20m, 0m, 0m, "4000")])); Assert.True(saved.Succeeded, saved.ErrorMessage);
        Guid lineId; string orderToken; await using (var db = await factory.CreateDbContextAsync()) { var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); lineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Id).SingleAsync(); orderToken = await db.SalesOrders.Where(candidate => candidate.Id == order.Id).Select(candidate => candidate.ConcurrencyToken).SingleAsync(); }
        ActAs(BrassLedgerPermissions.FulfillmentManage); Assert.False((await transactions.PromiseSalesOrderBackorderAsync(new(saved.Id!.Value, lineId, 4m, today.AddDays(4), "Await replenishment", orderToken))).Succeeded);
        ActAs(BrassLedgerPermissions.SalesManage); var backorderResult = await transactions.PromiseSalesOrderBackorderAsync(new(saved.Id!.Value, lineId, 4m, today.AddDays(4), "Await replenishment", orderToken)); Assert.True(backorderResult.Succeeded, backorderResult.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var secondBackorderResult = await transactions.PromiseSalesOrderBackorderAsync(new(saved.Id.Value, lineId, 1m, today.AddDays(5), "Second replenishment promise", orderToken)); Assert.True(secondBackorderResult.Succeeded, secondBackorderResult.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        ActAs(BrassLedgerPermissions.FulfillmentManage); var allocation = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new AllocateSalesOrderLineRequest(lineId, 3m)], orderToken)); Assert.True(allocation.Succeeded, allocation.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { var promise = await db.SalesOrderBackorderPromises.SingleAsync(candidate => candidate.Id == backorderResult.Id); Assert.Equal("PartiallyFulfilled", promise.Status); Assert.Equal(3m, promise.FulfilledQuantity); orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); }
        var releasedAllocation = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new AllocateSalesOrderLineRequest(lineId, 2m)], orderToken)); Assert.True(releasedAllocation.Succeeded, releasedAllocation.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { var promise = await db.SalesOrderBackorderPromises.SingleAsync(candidate => candidate.Id == backorderResult.Id); Assert.Equal("PartiallyFulfilled", promise.Status); Assert.Equal(2m, promise.FulfilledQuantity); orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); }
        var restoredAllocation = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new AllocateSalesOrderLineRequest(lineId, 3m)], orderToken)); Assert.True(restoredAllocation.Succeeded, restoredAllocation.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { Assert.Equal(3m, await db.SalesOrderBackorderPromises.Where(candidate => candidate.Id == backorderResult.Id).Select(candidate => candidate.FulfilledQuantity).SingleAsync()); orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); }
        var fullyAllocated = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new AllocateSalesOrderLineRequest(lineId, 5m)], orderToken)); Assert.True(fullyAllocated.Succeeded, fullyAllocated.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { Assert.Equal(4m, await db.SalesOrderBackorderPromises.Where(candidate => candidate.Id == backorderResult.Id).Select(candidate => candidate.FulfilledQuantity).SingleAsync()); Assert.Equal(1m, await db.SalesOrderBackorderPromises.Where(candidate => candidate.Id == secondBackorderResult.Id).Select(candidate => candidate.FulfilledQuantity).SingleAsync()); orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); }
        var partiallyReleased = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new AllocateSalesOrderLineRequest(lineId, 3m)], orderToken)); Assert.True(partiallyReleased.Succeeded, partiallyReleased.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { Assert.Equal(3m, await db.SalesOrderBackorderPromises.Where(candidate => candidate.Id == backorderResult.Id).Select(candidate => candidate.FulfilledQuantity).SingleAsync()); Assert.Equal(0m, await db.SalesOrderBackorderPromises.Where(candidate => candidate.Id == secondBackorderResult.Id).Select(candidate => candidate.FulfilledQuantity).SingleAsync()); Assert.Equal("Open", await db.SalesOrderBackorderPromises.Where(candidate => candidate.Id == secondBackorderResult.Id).Select(candidate => candidate.Status).SingleAsync()); orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); }
        var pickResult = await transactions.CreateInventoryPickAsync(new(saved.Id.Value, "PICK-1", today, [new CreateInventoryPickLineRequest(lineId, 2m)], orderToken)); Assert.True(pickResult.Succeeded, pickResult.ErrorMessage);
        Guid pickLineId; string pickToken; await using (var db = await factory.CreateDbContextAsync()) { var pick = await db.InventoryPicks.SingleAsync(candidate => candidate.Id == pickResult.Id); pickToken = pick.ConcurrencyToken; pickLineId = await db.InventoryPickLines.Where(line => line.InventoryPickId == pick.Id).Select(line => line.Id).SingleAsync(); orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); }
        var belowPick = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new AllocateSalesOrderLineRequest(lineId, 1m)], orderToken)); Assert.False(belowPick.Succeeded); Assert.Contains("commitment", belowPick.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False((await transactions.CompleteInventoryPickAsync(new(pickResult.Id!.Value, [new CompleteInventoryPickLineRequest(pickLineId, 2m)], "stale-token"))).Succeeded); var completed = await transactions.CompleteInventoryPickAsync(new(pickResult.Id.Value, [new CompleteInventoryPickLineRequest(pickLineId, 2m)], pickToken)); Assert.True(completed.Succeeded, completed.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) pickToken = await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.ConcurrencyToken).SingleAsync(); var firstPackResult = await transactions.PackInventoryPickAsync(new(pickResult.Id.Value, "PACK-1", today, [new PackInventoryPickLineRequest(pickLineId, 1m)], pickToken)); Assert.True(firstPackResult.Succeeded, firstPackResult.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) pickToken = await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.ConcurrencyToken).SingleAsync(); var secondPackResult = await transactions.PackInventoryPickAsync(new(pickResult.Id.Value, "PACK-2", today, [new PackInventoryPickLineRequest(pickLineId, 1m)], pickToken)); Assert.True(secondPackResult.Succeeded, secondPackResult.ErrorMessage);
        string firstPackToken; string secondPackToken; await using (var db = await factory.CreateDbContextAsync()) { firstPackToken = await db.InventoryPackingSlips.Where(pack => pack.Id == firstPackResult.Id).Select(pack => pack.ConcurrencyToken).SingleAsync(); secondPackToken = await db.InventoryPackingSlips.Where(pack => pack.Id == secondPackResult.Id).Select(pack => pack.ConcurrencyToken).SingleAsync(); orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); }
        Assert.False((await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-DIRECT-BLOCKED", today.AddDays(1), [new ShipSalesOrderLineRequest(lineId, 1m)], orderToken))).Succeeded); var mismatch = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-PACK-MISMATCH", today.AddDays(1), [new ShipSalesOrderLineRequest(lineId, 2m)], orderToken, firstPackResult.Id, firstPackToken)); Assert.False(mismatch.Succeeded); Assert.Contains("exactly", mismatch.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var firstShipment = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-PACK-1", today.AddDays(1), [new ShipSalesOrderLineRequest(lineId, 1m)], orderToken, firstPackResult.Id, firstPackToken)); Assert.True(firstShipment.Succeeded, firstShipment.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); Assert.Equal("Packed", await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.Status).SingleAsync()); }
        var secondShipment = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-PACK-2", today.AddDays(2), [new ShipSalesOrderLineRequest(lineId, 1m)], orderToken, secondPackResult.Id, secondPackToken)); Assert.True(secondShipment.Succeeded, secondShipment.ErrorMessage);
        string secondShipmentToken; await using (var db = await factory.CreateDbContextAsync()) { var shipment = await db.InventoryShipments.SingleAsync(candidate => candidate.Id == secondShipment.Id); Assert.Equal(secondPackResult.Id, shipment.InventoryPackingSlipId); Assert.Equal("Shipped", await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.Status).SingleAsync()); secondShipmentToken = shipment.ConcurrencyToken; }
        var reversed = await transactions.ReverseInventoryShipmentAsync(new(secondShipment.Id!.Value, today.AddDays(2), "Packing correction", secondShipmentToken)); Assert.True(reversed.Succeeded, reversed.ErrorMessage);
        string restoredPackTokenForReship; await using (var db = await factory.CreateDbContextAsync()) { restoredPackTokenForReship = await db.InventoryPackingSlips.Where(pack => pack.Id == secondPackResult.Id).Select(pack => pack.ConcurrencyToken).SingleAsync(); orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); }
        var reshipped = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-PACK-2-REPOST", today.AddDays(3), [new ShipSalesOrderLineRequest(lineId, 1m)], orderToken, secondPackResult.Id, restoredPackTokenForReship)); Assert.True(reshipped.Succeeded, reshipped.ErrorMessage);
        var workspaceAfterReship = (await workspaceService.GetWorkspaceAsync()).Operations; var reshippedPack = Assert.Single(workspaceAfterReship.InventoryPackingSlips ?? [], pack => pack.Id == secondPackResult.Id); Assert.Equal(reshipped.Id, reshippedPack.InventoryShipmentId); Assert.Equal(2, (workspaceAfterReship.InventoryShipments ?? []).Count(shipment => shipment.InventoryPackingSlipId == secondPackResult.Id));
        Guid foreignCompanyId; string restoredPackToken; string promiseToken;
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal("Shipped", await db.InventoryPackingSlips.Where(pack => pack.Id == secondPackResult.Id).Select(pack => pack.Status).SingleAsync()); Assert.Equal("Shipped", await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.Status).SingleAsync());
            restoredPackToken = await db.InventoryPackingSlips.Where(pack => pack.Id == secondPackResult.Id).Select(pack => pack.ConcurrencyToken).SingleAsync(); promiseToken = await db.SalesOrderBackorderPromises.Where(candidate => candidate.Id == backorderResult.Id).Select(candidate => candidate.ConcurrencyToken).SingleAsync();
            foreignCompanyId = Guid.NewGuid(); db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Foreign pick company", LegalName = "Foreign pick company", BaseCurrency = "USD", FiscalYearStartMonth = 1 }); await db.SaveChangesAsync();
        }
        ActAsCompany(foreignCompanyId, BrassLedgerPermissions.FulfillmentManage, BrassLedgerPermissions.SalesManage);
        var foreignPick = await transactions.CompleteInventoryPickAsync(new(pickResult.Id!.Value, [new CompleteInventoryPickLineRequest(pickLineId, 1m)], pickToken)); Assert.False(foreignPick.Succeeded); Assert.Contains("not found", foreignPick.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var foreignPack = await transactions.CancelInventoryPackingSlipAsync(new(secondPackResult.Id!.Value, "Cross-company attempt", restoredPackToken)); Assert.False(foreignPack.Succeeded); Assert.Contains("not found", foreignPack.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var foreignBackorder = await transactions.CancelSalesOrderBackorderAsync(new(backorderResult.Id!.Value, "Cross-company attempt", promiseToken)); Assert.False(foreignBackorder.Succeeded); Assert.Contains("not found", foreignBackorder.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var foreignWorkspace = (await workspaceService.GetWorkspaceAsync()).Operations; Assert.Empty(foreignWorkspace.InventoryPicks ?? []); Assert.Empty(foreignWorkspace.InventoryPackingSlips ?? []); Assert.Empty(foreignWorkspace.BackorderPromises ?? []);
        ActAs(BrassLedgerPermissions.FulfillmentManage); Assert.False((await transactions.CancelSalesOrderBackorderAsync(new(backorderResult.Id.Value, "Unauthorized", promiseToken))).Succeeded); ActAs(BrassLedgerPermissions.SalesManage); Assert.True((await transactions.CancelSalesOrderBackorderAsync(new(backorderResult.Id.Value, "Customer accepted partial supply", promiseToken))).Succeeded); await using (var db = await factory.CreateDbContextAsync()) { var secondPromise = await db.SalesOrderBackorderPromises.SingleAsync(candidate => candidate.Id == secondBackorderResult.Id); Assert.True((await transactions.CancelSalesOrderBackorderAsync(new(secondPromise.Id, "Remaining promise cancelled", secondPromise.ConcurrencyToken))).Succeeded); }
    }

    [Fact]
    public async Task PickPackCancellation_ReleasesAllocationCommitmentsOnlyAfterAuditedDocumentCancellation()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid customerId; Guid itemId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            customerId = await db.Customers.Where(customer => customer.CompanyId == companyId).Select(customer => customer.Id).FirstAsync();
            itemId = await db.InventoryItems.Where(item => item.CompanyId == companyId && item.Sku == "RM-220").Select(item => item.Id).SingleAsync();
        }
        void ActAs(string permission) => accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)
            ], "test"))
        };

        var today = new DateOnly(2026, 8, 26);
        ActAs(BrassLedgerPermissions.SalesManage);
        var saved = await transactions.SaveSalesOrderAsync(new(null, customerId, "SO-PICK-CANCEL", today, null, "Cancellation controls", [new SalesOrderLineRequest(itemId, "Cancelled pick", 2m, 20m, 0m, 0m, "4000")]));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        Guid lineId; string orderToken;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); lineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Id).SingleAsync();
            Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded);
            orderToken = await db.SalesOrders.Where(candidate => candidate.Id == order.Id).Select(candidate => candidate.ConcurrencyToken).SingleAsync();
        }
        ActAs(BrassLedgerPermissions.FulfillmentManage);
        Assert.True((await transactions.AllocateSalesOrderAsync(new(saved.Id!.Value, [new AllocateSalesOrderLineRequest(lineId, 2m)], orderToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var pickResult = await transactions.CreateInventoryPickAsync(new(saved.Id.Value, "PICK-CANCEL", today, [new CreateInventoryPickLineRequest(lineId, 2m)], orderToken));
        Assert.True(pickResult.Succeeded, pickResult.ErrorMessage);
        Guid pickLineId; string pickToken;
        await using (var db = await factory.CreateDbContextAsync())
        {
            pickLineId = await db.InventoryPickLines.Where(line => line.InventoryPickId == pickResult.Id).Select(line => line.Id).SingleAsync(); pickToken = await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.ConcurrencyToken).SingleAsync();
        }
        Assert.True((await transactions.CompleteInventoryPickAsync(new(pickResult.Id!.Value, [new CompleteInventoryPickLineRequest(pickLineId, 2m)], pickToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) pickToken = await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.ConcurrencyToken).SingleAsync();
        var packResult = await transactions.PackInventoryPickAsync(new(pickResult.Id.Value, "PACK-CANCEL", today, [new PackInventoryPickLineRequest(pickLineId, 2m)], pickToken));
        Assert.True(packResult.Succeeded, packResult.ErrorMessage);
        string packToken;
        await using (var db = await factory.CreateDbContextAsync()) { packToken = await db.InventoryPackingSlips.Where(pack => pack.Id == packResult.Id).Select(pack => pack.ConcurrencyToken).SingleAsync(); pickToken = await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.ConcurrencyToken).SingleAsync(); }
        var blockedPickCancellation = await transactions.CancelInventoryPickAsync(new(pickResult.Id.Value, "Cannot skip packing cancellation", pickToken)); Assert.False(blockedPickCancellation.Succeeded); Assert.Contains("unpacked", blockedPickCancellation.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False((await transactions.CancelInventoryPackingSlipAsync(new(packResult.Id!.Value, "Stale cancellation", "stale-token"))).Succeeded);
        Assert.True((await transactions.CancelInventoryPackingSlipAsync(new(packResult.Id.Value, "Customer cancelled before shipment", packToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) pickToken = await db.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.ConcurrencyToken).SingleAsync();
        Assert.True((await transactions.CancelInventoryPickAsync(new(pickResult.Id.Value, "Release warehouse commitment", pickToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var released = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new AllocateSalesOrderLineRequest(lineId, 0m)], orderToken)); Assert.True(released.Succeeded, released.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync(); Assert.Equal("Cancelled", await after.InventoryPicks.Where(pick => pick.Id == pickResult.Id).Select(pick => pick.Status).SingleAsync()); Assert.Equal("Cancelled", await after.InventoryPackingSlips.Where(pack => pack.Id == packResult.Id).Select(pack => pack.Status).SingleAsync()); Assert.Equal(0m, await after.SalesOrderLines.Where(line => line.Id == lineId).Select(line => line.AllocatedQuantity).SingleAsync());
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "inventory-pick.cancelled" && audit.EntityId == pickResult.Id); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "inventory-packing-slip.cancelled" && audit.EntityId == packResult.Id);
    }

    [Fact]
    public async Task PurchaseRequisitions_EnforceSeparationCompanyScopeConcurrencyAndOneTimeConversion()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(); var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        Guid companyId; Guid foreignCompanyId; Guid vendorId; Guid itemId; int journalCount;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync(); vendorId = await db.Vendors.Select(vendor => vendor.Id).FirstAsync(); itemId = await db.InventoryItems.Where(item => item.Sku == "RM-220").Select(item => item.Id).SingleAsync(); journalCount = await db.JournalEntries.CountAsync();
            foreignCompanyId = Guid.NewGuid(); db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Other requisition company", LegalName = "Other requisition company", BaseCurrency = "USD", FiscalYearStartMonth = 1 }); await db.SaveChangesAsync();
        }
        void ActAsCompany(Guid activeCompanyId, params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, activeCompanyId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        void ActAs(params string[] permissions) => ActAsCompany(companyId, permissions);
        var request = new SavePurchaseRequisitionRequest(null, vendorId, "REQ-CONTROL-1", new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27), "Controlled replenishment", [new(itemId, "Fastener replenishment", 3m, 12.50m)]);

        ActAs(BrassLedgerPermissions.RequisitionManage);
        Assert.False((await transactions.SavePurchaseOrderAsync(new(null, vendorId, "PO-BYPASS-1", request.RequestedOn, request.NeededBy, request.Purpose, [new(itemId, "Bypass", 3m, 12.50m)]))).Succeeded);
        var saved = await transactions.SavePurchaseRequisitionAsync(request); Assert.True(saved.Succeeded, saved.ErrorMessage);
        PurchaseRequisition requisition; await using (var db = await factory.CreateDbContextAsync()) requisition = await db.PurchaseRequisitions.SingleAsync(candidate => candidate.Id == saved.Id);
        Assert.False((await transactions.SubmitPurchaseRequisitionAsync(new(requisition.Id, "stale-token"))).Succeeded);
        Assert.True((await transactions.SubmitPurchaseRequisitionAsync(new(requisition.Id, requisition.ConcurrencyToken))).Succeeded);

        await using (var db = await factory.CreateDbContextAsync()) requisition = await db.PurchaseRequisitions.SingleAsync(candidate => candidate.Id == saved.Id);
        Assert.False((await transactions.DecidePurchaseRequisitionAsync(new(requisition.Id, true, "Unauthorized", requisition.ConcurrencyToken))).Succeeded);
        ActAsCompany(foreignCompanyId, BrassLedgerPermissions.PurchasingManage);
        var foreignDecision = await transactions.DecidePurchaseRequisitionAsync(new(requisition.Id, true, "Cross-company", requisition.ConcurrencyToken)); Assert.False(foreignDecision.Succeeded); Assert.Contains("not found", foreignDecision.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(BrassLedgerPermissions.PurchasingManage);
        Assert.True((await transactions.DecidePurchaseRequisitionAsync(new(requisition.Id, true, "Budget and need reviewed", requisition.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) requisition = await db.PurchaseRequisitions.SingleAsync(candidate => candidate.Id == saved.Id);
        var converted = await transactions.ConvertPurchaseRequisitionAsync(new(requisition.Id, vendorId, "PO-CONTROL-1", request.RequestedOn, request.NeededBy, request.Purpose, requisition.ConcurrencyToken)); Assert.True(converted.Succeeded, converted.ErrorMessage);
        Assert.False((await transactions.ConvertPurchaseRequisitionAsync(new(requisition.Id, vendorId, "PO-CONTROL-2", request.RequestedOn, request.NeededBy, request.Purpose, requisition.ConcurrencyToken))).Succeeded);

        await using var after = await factory.CreateDbContextAsync();
        var finalRequisition = await after.PurchaseRequisitions.SingleAsync(candidate => candidate.Id == saved.Id); var order = await after.PurchaseOrders.SingleAsync(candidate => candidate.Id == converted.Id); var line = await after.PurchaseOrderLines.SingleAsync(candidate => candidate.PurchaseOrderId == order.Id);
        Assert.Equal("Converted", finalRequisition.Status); Assert.Equal(finalRequisition.Id, order.PurchaseRequisitionId); Assert.Equal("Draft", order.Status); Assert.Equal(37.50m, order.TotalAmount); Assert.Equal(3m, line.OrderedQuantity); Assert.Equal(12.50m, line.UnitCost); Assert.Equal(journalCount, await after.JournalEntries.CountAsync());
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "purchase-requisition.submitted" && audit.EntityId == saved.Id); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "purchase-requisition.approved" && audit.EntityId == saved.Id); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "purchase-requisition.converted" && audit.EntityId == saved.Id);
    }

    [Fact]
    public async Task PurchasingWorkflow_PartiallyReceivesInventory_AndThreeWayMatchesAccrual()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var before = await factory.CreateDbContextAsync();
        var vendor = await before.Vendors.FirstAsync();
        var item = await before.InventoryItems.FirstAsync();
        var priorLocation = await before.InventoryLocationBalances.SingleAsync(balance => balance.InventoryItemId == item.Id);
        var priorQuantity = item.QuantityOnHand;
        var priorCost = item.UnitCost;
        var inventoryBalance = await before.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync();
        var grniBalance = await before.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced).Select(account => account.CurrentBalance).SingleAsync();
        var payableBalance = await before.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync();

        var saved = await transactions.SavePurchaseOrderAsync(new SavePurchaseOrderRequest(null, vendor.Id, "PO-RECEIVE-1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 8), "Test inventory purchase", [new PurchaseOrderLineRequest(item.Id, "Purchased inventory", 5m, 20m)]));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        await using (var approveDb = await factory.CreateDbContextAsync())
        {
            var draft = await approveDb.PurchaseOrders.SingleAsync(order => order.Id == saved.Id);
            var approved = await transactions.ApprovePurchaseOrderAsync(new ApprovePurchaseOrderRequest(draft.Id, draft.ConcurrencyToken));
            Assert.True(approved.Succeeded, approved.ErrorMessage);
        }
        Guid poLineId; string approvedToken;
        await using (var receiveDb = await factory.CreateDbContextAsync())
        {
            var order = await receiveDb.PurchaseOrders.SingleAsync(candidate => candidate.Id == saved.Id);
            approvedToken = order.ConcurrencyToken;
            poLineId = await receiveDb.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).Select(line => line.Id).SingleAsync();
        }
        var staleReceipt = await transactions.ReceivePurchaseOrderAsync(new ReceivePurchaseOrderRequest(saved.Id!.Value, "RCV-STALE-1", new DateOnly(2026, 8, 3), [new ReceivePurchaseOrderLineRequest(poLineId, 1m)], "stale-token"));
        Assert.False(staleReceipt.Succeeded); Assert.Contains("changed", staleReceipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var overReceipt = await transactions.ReceivePurchaseOrderAsync(new ReceivePurchaseOrderRequest(saved.Id.Value, "RCV-OVER-1", new DateOnly(2026, 8, 3), [new ReceivePurchaseOrderLineRequest(poLineId, 6m)], approvedToken));
        Assert.False(overReceipt.Succeeded); Assert.Contains("exceeds", overReceipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var received = await transactions.ReceivePurchaseOrderAsync(new ReceivePurchaseOrderRequest(saved.Id!.Value, "RCV-RECEIVE-1", new DateOnly(2026, 8, 3), [new ReceivePurchaseOrderLineRequest(poLineId, 2m)], approvedToken));
        Assert.True(received.Succeeded, received.ErrorMessage);

        await using (var receiptDb = await factory.CreateDbContextAsync())
        {
            var order = await receiptDb.PurchaseOrders.SingleAsync(candidate => candidate.Id == saved.Id);
            var line = await receiptDb.PurchaseOrderLines.SingleAsync(candidate => candidate.Id == poLineId);
            var receivedItem = await receiptDb.InventoryItems.SingleAsync(candidate => candidate.Id == item.Id);
            var receipt = await receiptDb.InventoryReceipts.SingleAsync(candidate => candidate.Id == received.Id);
            Assert.Equal("PartiallyReceived", order.Status);
            Assert.Equal(2m, line.ReceivedQuantity);
            Assert.Equal(0m, line.InvoicedQuantity);
            Assert.Equal(priorQuantity + 2m, receivedItem.QuantityOnHand);
            Assert.Equal(decimal.Round(((priorQuantity * priorCost) + 40m) / (priorQuantity + 2m), 2, MidpointRounding.AwayFromZero), receivedItem.UnitCost);
            Assert.Equal(40m, receipt.TotalAmount);
            Assert.Equal(priorLocation.WarehouseId, receipt.WarehouseId); Assert.Equal(priorLocation.BinId, receipt.BinId);
            Assert.Equal(priorLocation.QuantityOnHand + 2m, await receiptDb.InventoryLocationBalances.Where(balance => balance.Id == priorLocation.Id).Select(balance => balance.QuantityOnHand).SingleAsync());
            Assert.Equal(inventoryBalance + 40m, await receiptDb.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync());
            Assert.Equal(grniBalance + 40m, await receiptDb.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced).Select(account => account.CurrentBalance).SingleAsync());
        }

        string receiptToken;
        await using (var matchDb = await factory.CreateDbContextAsync()) receiptToken = await matchDb.InventoryReceipts.Where(receipt => receipt.Id == received.Id).Select(receipt => receipt.ConcurrencyToken).SingleAsync();
        var matched = await PostControlledPurchaseInvoiceAsync(transactions, factory, received.Id!.Value, "BILL-RECEIVE-1", new DateOnly(2026, 8, 4), new DateOnly(2026, 9, 3), "Matched inventory invoice");
        var unsafeGenericVoid = await transactions.VoidVendorBillAsync(new VoidSubledgerDocumentRequest(matched.VendorBillId, new DateOnly(2026, 8, 4), "Would desynchronize receiving"));
        Assert.False(unsafeGenericVoid.Succeeded); Assert.Contains("invoice match", unsafeGenericVoid.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using var after = await factory.CreateDbContextAsync();
        var bill = await after.VendorBills.SingleAsync(candidate => candidate.Id == matched.VendorBillId);
        var matchedReceipt = await after.InventoryReceipts.SingleAsync(candidate => candidate.Id == received.Id);
        Assert.Equal(received.Id, bill.InventoryReceiptId);
        Assert.Equal(matched.VendorBillId, await after.VendorBills.Where(candidate => candidate.InventoryReceiptId == matchedReceipt.Id).Select(candidate => candidate.Id).SingleAsync());
        Assert.Equal(40m, bill.BalanceDue);
        Assert.Equal(grniBalance, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced).Select(account => account.CurrentBalance).SingleAsync());
        Assert.Equal(payableBalance + 40m, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync());
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "purchase-invoice-match.posted" && audit.EntityId == matched.MatchId);
    }

    [Fact]
    public async Task PurchaseInvoiceMatching_PostsPartialPriceAndQuantityVariances_AndReversesExactMatch()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid vendorId; Guid itemId; decimal grniBefore; decimal payableBefore; decimal varianceBefore;
        await using (var db = await factory.CreateDbContextAsync())
        {
            vendorId = await db.Vendors.Select(item => item.Id).FirstAsync(); itemId = await db.InventoryItems.Select(item => item.Id).FirstAsync();
            grniBefore = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced).Select(account => account.CurrentBalance).SingleAsync();
            payableBefore = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync();
            varianceBefore = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.PurchasePriceVariance).Select(account => account.CurrentBalance).SingleAsync();
        }
        var orderResult = await transactions.SavePurchaseOrderAsync(new(null, vendorId, "PO-MATCH-PARTIAL-1", new DateOnly(2026, 8, 1), null, "Partial invoice matching", [new(itemId, "Variance-tested goods", 10m, 10m)])); Assert.True(orderResult.Succeeded, orderResult.ErrorMessage);
        Guid orderLineId; string orderToken;
        await using (var db = await factory.CreateDbContextAsync()) { var order = await db.PurchaseOrders.SingleAsync(item => item.Id == orderResult.Id); Assert.True((await transactions.ApprovePurchaseOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); orderLineId = await db.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).Select(line => line.Id).SingleAsync(); orderToken = await db.PurchaseOrders.Where(item => item.Id == order.Id).Select(item => item.ConcurrencyToken).SingleAsync(); }
        var receiptResult = await transactions.ReceivePurchaseOrderAsync(new(orderResult.Id!.Value, "RCV-MATCH-PARTIAL-1", new DateOnly(2026, 8, 2), [new(orderLineId, 10m)], orderToken)); Assert.True(receiptResult.Succeeded, receiptResult.ErrorMessage);
        Guid receiptLineId; await using (var db = await factory.CreateDbContextAsync()) receiptLineId = await db.InventoryReceiptLines.Where(line => line.InventoryReceiptId == receiptResult.Id).Select(line => line.Id).SingleAsync();

        async Task<Guid> PostMatchAsync(string billNumber, decimal quantity, decimal unitCost)
        {
            string receiptToken; await using (var db = await factory.CreateDbContextAsync()) receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync();
            var saved = await transactions.SavePurchaseInvoiceMatchAsync(new(null, receiptResult.Id!.Value, billNumber, new DateOnly(2026, 8, 3), new DateOnly(2026, 9, 2), "Partial supplier invoice", [new(receiptLineId, quantity, unitCost)], receiptToken)); Assert.True(saved.Succeeded, saved.ErrorMessage);
            PurchaseInvoiceMatch match; await using (var db = await factory.CreateDbContextAsync()) match = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == saved.Id); Assert.True((await transactions.SubmitPurchaseInvoiceMatchAsync(new(match.Id, match.ConcurrencyToken))).Succeeded);
            await using (var db = await factory.CreateDbContextAsync()) match = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == saved.Id); Assert.True((await transactions.DecidePurchaseInvoiceMatchAsync(new(match.Id, true, "Quantity and price variance accepted", match.ConcurrencyToken))).Succeeded);
            await using (var db = await factory.CreateDbContextAsync()) match = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == saved.Id); var posted = await transactions.PostPurchaseInvoiceMatchAsync(new(match.Id, match.ConcurrencyToken)); Assert.True(posted.Succeeded, posted.ErrorMessage); return match.Id;
        }

        var firstMatchId = await PostMatchAsync("BILL-MATCH-PARTIAL-1", 4m, 12m); var secondMatchId = await PostMatchAsync("BILL-MATCH-PARTIAL-2", 7m, 9m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var first = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == firstMatchId); var second = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == secondMatchId); var secondLine = await db.PurchaseInvoiceMatchLines.SingleAsync(item => item.PurchaseInvoiceMatchId == secondMatchId);
            Assert.Equal((48m, 40m, 8m, 0m), (first.InvoiceAmount, first.AccrualAmount, first.PriceVarianceAmount, first.QuantityVarianceAmount));
            Assert.Equal((63m, 60m, -6m, 1m, 9m), (second.InvoiceAmount, second.AccrualAmount, second.PriceVarianceAmount, second.QuantityVarianceQuantity, second.QuantityVarianceAmount)); Assert.Equal(6m, secondLine.MatchedQuantity);
            Assert.Equal(grniBefore, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(payableBefore + 111m, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(varianceBefore + 11m, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.PurchasePriceVariance).Select(account => account.CurrentBalance).SingleAsync());
        }

        string receiptToken;
        await using (var db = await factory.CreateDbContextAsync())
            receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync();
        var returnAuthorization = await transactions.AuthorizeSupplierReturnAsync(new(
            receiptResult.Id!.Value,
            "SRA-MATCH-PARTIAL-1",
            new DateOnly(2026, 8, 4),
            "Return crosses two partial supplier invoices",
            [new(receiptLineId, 5m)],
            receiptToken));
        Assert.True(returnAuthorization.Succeeded, returnAuthorization.ErrorMessage);
        Guid returnLineId;
        string returnToken;
        await using (var db = await factory.CreateDbContextAsync())
        {
            returnLineId = await db.SupplierReturnAuthorizationLines.Where(line => line.SupplierReturnAuthorizationId == returnAuthorization.Id).Select(line => line.Id).SingleAsync();
            returnToken = await db.SupplierReturnAuthorizations.Where(item => item.Id == returnAuthorization.Id).Select(item => item.ConcurrencyToken).SingleAsync();
        }

        var returnShipment = await transactions.ShipSupplierReturnAsync(new(
            returnAuthorization.Id!.Value,
            "SRS-MATCH-PARTIAL-1",
            new DateOnly(2026, 8, 4),
            null,
            null,
            [new(returnLineId, 5m)],
            returnToken));
        Assert.True(returnShipment.Succeeded, returnShipment.ErrorMessage);
        string returnShipmentToken;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var shipment = await db.SupplierReturnShipments.SingleAsync(item => item.Id == returnShipment.Id);
            var shipmentLine = await db.SupplierReturnShipmentLines.SingleAsync(line => line.SupplierReturnShipmentId == shipment.Id);
            returnShipmentToken = shipment.ConcurrencyToken;
            Assert.Equal((50m, 57m, 48m), (shipment.TotalAmount, shipment.VendorCreditAmount, shipment.SourceAppliedAmount));
            Assert.Equal((5m, 0m, 57m), (shipmentLine.InvoicedQuantity, shipmentLine.GrniReductionAmount, shipmentLine.VendorCreditAmount));
            Assert.Equal(payableBefore + 54m, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync());
            Assert.Equal(varianceBefore + 4m, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.PurchasePriceVariance).Select(account => account.CurrentBalance).SingleAsync());
        }
        var reversedReturn = await transactions.ReverseSupplierReturnShipmentAsync(new(
            returnShipment.Id!.Value,
            new DateOnly(2026, 8, 5),
            "Goods retained after invoice reconciliation",
            returnShipmentToken));
        Assert.True(reversedReturn.Succeeded, reversedReturn.ErrorMessage);

        PurchaseInvoiceMatch secondMatch; await using (var db = await factory.CreateDbContextAsync()) secondMatch = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == secondMatchId); var reversed = await transactions.ReversePurchaseInvoiceMatchAsync(new(secondMatch.Id, new DateOnly(2026, 8, 6), "Second invoice entered against wrong packing slip", secondMatch.ConcurrencyToken)); Assert.True(reversed.Succeeded, reversed.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync(); Assert.Equal("Posted", await after.PurchaseInvoiceMatches.Where(item => item.Id == firstMatchId).Select(item => item.Status).SingleAsync()); Assert.Equal("Reversed", await after.PurchaseInvoiceMatches.Where(item => item.Id == secondMatchId).Select(item => item.Status).SingleAsync()); Assert.Equal(grniBefore + 60m, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(payableBefore + 48m, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(varianceBefore + 8m, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.PurchasePriceVariance).Select(account => account.CurrentBalance).SingleAsync());
    }

    [Fact]
    public async Task SupplierReturns_SeparatePreInvoiceGrniFromPostInvoiceCredits_AndReverseInOrder()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid foreignCompanyId; Guid vendorId; Guid itemId; Guid bankId; Guid projectId; decimal inventoryBefore; decimal grniBefore; decimal payableBefore; decimal cashBefore; decimal itemQuantityBefore;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(item => item.Id).SingleAsync(); vendorId = await db.Vendors.Select(item => item.Id).FirstAsync(); itemId = await db.InventoryItems.Where(item => item.Sku == "RM-220").Select(item => item.Id).SingleAsync(); bankId = await db.BankAccounts.Select(item => item.Id).FirstAsync(); projectId = await db.ProjectJobs.Where(project => project.Status == "Active").Select(project => project.Id).FirstAsync(); itemQuantityBefore = await db.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync();
            inventoryBefore = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync(); grniBefore = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced).Select(account => account.CurrentBalance).SingleAsync(); payableBefore = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync(); cashBefore = await db.BankAccounts.Where(bank => bank.Id == bankId).Select(bank => bank.CurrentBalance).SingleAsync();
            foreignCompanyId = Guid.NewGuid(); db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Other supplier-return company", LegalName = "Other supplier-return company", BaseCurrency = "USD", FiscalYearStartMonth = 1 }); await db.SaveChangesAsync();
        }
        void ActAsCompany(Guid activeCompanyId, params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, activeCompanyId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        void ActAs(params string[] permissions) => ActAsCompany(companyId, permissions);
        ActAs(BrassLedgerPermissions.PurchasingManage, BrassLedgerPermissions.PayablesManage, BrassLedgerPermissions.CheckDisbursementManage, BrassLedgerPermissions.PaymentReverse);
        var orderResult = await transactions.SavePurchaseOrderAsync(new(null, vendorId, "PO-SUPRET-1", new DateOnly(2026, 8, 1), null, "Supplier-return controls", [new(itemId, "Returnable fasteners", 5m, 20m, projectId)])); Assert.True(orderResult.Succeeded, orderResult.ErrorMessage);
        Guid orderLineId; string token; await using (var db = await factory.CreateDbContextAsync()) { var order = await db.PurchaseOrders.SingleAsync(item => item.Id == orderResult.Id); Assert.True((await transactions.ApprovePurchaseOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); token = await db.PurchaseOrders.Where(item => item.Id == order.Id).Select(item => item.ConcurrencyToken).SingleAsync(); orderLineId = await db.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).Select(line => line.Id).SingleAsync(); }
        var receiptResult = await transactions.ReceivePurchaseOrderAsync(new(orderResult.Id!.Value, "RCV-SUPRET-1", new DateOnly(2026, 8, 2), [new(orderLineId, 5m)], token)); Assert.True(receiptResult.Succeeded, receiptResult.ErrorMessage);
        Guid receiptLineId; string receiptToken; await using (var db = await factory.CreateDbContextAsync()) { receiptLineId = await db.InventoryReceiptLines.Where(line => line.InventoryReceiptId == receiptResult.Id).Select(line => line.Id).SingleAsync(); receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync(); }

        ActAs(BrassLedgerPermissions.PayablesManage); Assert.False((await transactions.AuthorizeSupplierReturnAsync(new(receiptResult.Id!.Value, "SRA-UNAUTHORIZED", new DateOnly(2026, 8, 3), "Unauthorized attempt", [new(receiptLineId, 2m)], receiptToken))).Succeeded);
        ActAs(BrassLedgerPermissions.PurchasingManage, BrassLedgerPermissions.PayablesManage, BrassLedgerPermissions.CheckDisbursementManage, BrassLedgerPermissions.PaymentReverse); Assert.False((await transactions.AuthorizeSupplierReturnAsync(new(receiptResult.Id.Value, "SRA-STALE", new DateOnly(2026, 8, 3), "Stale attempt", [new(receiptLineId, 2m)], "stale-token"))).Succeeded);
        var preInvoiceAuthorization = await transactions.AuthorizeSupplierReturnAsync(new(receiptResult.Id.Value, "SRA-PRE-1", new DateOnly(2026, 8, 3), "Damaged before invoice", [new(receiptLineId, 2m)], receiptToken)); Assert.True(preInvoiceAuthorization.Succeeded, preInvoiceAuthorization.ErrorMessage);
        Guid authorizationLineId; string authorizationToken; await using (var db = await factory.CreateDbContextAsync()) { authorizationLineId = await db.SupplierReturnAuthorizationLines.Where(line => line.SupplierReturnAuthorizationId == preInvoiceAuthorization.Id).Select(line => line.Id).SingleAsync(); authorizationToken = await db.SupplierReturnAuthorizations.Where(item => item.Id == preInvoiceAuthorization.Id).Select(item => item.ConcurrencyToken).SingleAsync(); }
        ActAsCompany(foreignCompanyId, BrassLedgerPermissions.PurchasingManage); var foreignCancellation = await transactions.CancelSupplierReturnAsync(new(preInvoiceAuthorization.Id!.Value, "Cross-company attempt", authorizationToken)); Assert.False(foreignCancellation.Succeeded); Assert.Contains("not found", foreignCancellation.ErrorMessage, StringComparison.OrdinalIgnoreCase); ActAs(BrassLedgerPermissions.PurchasingManage, BrassLedgerPermissions.PayablesManage, BrassLedgerPermissions.CheckDisbursementManage, BrassLedgerPermissions.PaymentReverse);
        var preInvoiceShipment = await transactions.ShipSupplierReturnAsync(new(preInvoiceAuthorization.Id!.Value, "SRS-PRE-1", new DateOnly(2026, 8, 4), null, null, [new(authorizationLineId, 2m)], authorizationToken)); Assert.True(preInvoiceShipment.Succeeded, preInvoiceShipment.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var journalEntryId = await db.SupplierReturnShipments.Where(shipment => shipment.Id == preInvoiceShipment.Id).Select(shipment => shipment.JournalEntryId).SingleAsync();
            Assert.All(await db.JournalEntryLines.Where(line => line.JournalEntryId == journalEntryId).ToListAsync(), line => Assert.Equal(projectId, line.ProjectJobId));
        }
        await using (var db = await factory.CreateDbContextAsync()) receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync();
        var billResult = await PostControlledPurchaseInvoiceAsync(transactions, factory, receiptResult.Id.Value, "BILL-SUPRET-1", new DateOnly(2026, 8, 5), new DateOnly(2026, 9, 4), "Net received quantity");
        await using (var db = await factory.CreateDbContextAsync())
        {
            var bill = await db.VendorBills.SingleAsync(item => item.Id == billResult.VendorBillId); Assert.Equal(60m, bill.TotalAmount); Assert.Equal(3m, await db.VendorBillLines.Where(line => line.VendorBillId == bill.Id).Select(line => line.Quantity).SingleAsync()); Assert.NotNull(await db.VendorBillLines.Where(line => line.VendorBillId == bill.Id).Select(line => line.InventoryReceiptLineId).SingleAsync());
        }
        var payment = await transactions.RecordVendorPaymentAsync(new(vendorId, bankId, new DateOnly(2026, 8, 6), 50m, "CHK-SUPRET-1", "Check", [new(billResult.VendorBillId, 50m)])); Assert.True(payment.Succeeded, payment.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync();
        var postInvoiceAuthorization = await transactions.AuthorizeSupplierReturnAsync(new(receiptResult.Id.Value, "SRA-POST-1", new DateOnly(2026, 8, 7), "Defect found after invoice", [new(receiptLineId, 1m)], receiptToken)); Assert.True(postInvoiceAuthorization.Succeeded, postInvoiceAuthorization.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { authorizationLineId = await db.SupplierReturnAuthorizationLines.Where(line => line.SupplierReturnAuthorizationId == postInvoiceAuthorization.Id).Select(line => line.Id).SingleAsync(); authorizationToken = await db.SupplierReturnAuthorizations.Where(item => item.Id == postInvoiceAuthorization.Id).Select(item => item.ConcurrencyToken).SingleAsync(); }
        var postInvoiceShipment = await transactions.ShipSupplierReturnAsync(new(postInvoiceAuthorization.Id!.Value, "SRS-POST-1", new DateOnly(2026, 8, 8), null, null, [new(authorizationLineId, 1m)], authorizationToken)); Assert.True(postInvoiceShipment.Succeeded, postInvoiceShipment.ErrorMessage);
        string shipmentToken; await using (var db = await factory.CreateDbContextAsync()) { var shipment = await db.SupplierReturnShipments.SingleAsync(item => item.Id == postInvoiceShipment.Id); Assert.True(shipment.CreatesVendorCredit); Assert.Equal(10m, shipment.SourceAppliedAmount); Assert.Equal(10m, shipment.TotalAmount - shipment.AppliedAmount); shipmentToken = shipment.ConcurrencyToken; Assert.Equal(0m, await db.VendorBills.Where(item => item.Id == billResult.VendorBillId).Select(item => item.BalanceDue).SingleAsync()); }
        var refund = await transactions.RefundSupplierReturnCreditAsync(new(postInvoiceShipment.Id!.Value, bankId, "REF-SUPRET-1", new DateOnly(2026, 8, 9), 10m, shipmentToken)); Assert.True(refund.Succeeded, refund.ErrorMessage);
        string refundToken; await using (var db = await factory.CreateDbContextAsync()) refundToken = await db.SupplierReturnCreditRefunds.Where(item => item.Id == refund.Id).Select(item => item.ConcurrencyToken).SingleAsync(); Assert.True((await transactions.ReverseSupplierReturnCreditRefundAsync(new(refund.Id!.Value, new DateOnly(2026, 8, 10), "Refund entered prematurely", refundToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) shipmentToken = await db.SupplierReturnShipments.Where(item => item.Id == postInvoiceShipment.Id).Select(item => item.ConcurrencyToken).SingleAsync(); var reversedShipment = await transactions.ReverseSupplierReturnShipmentAsync(new(postInvoiceShipment.Id.Value, new DateOnly(2026, 8, 11), "Goods retained after vendor concession", shipmentToken)); Assert.True(reversedShipment.Succeeded, reversedShipment.ErrorMessage);

        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(itemQuantityBefore + 3m, await after.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync()); Assert.Equal(2m, await after.InventoryReceiptLines.Where(line => line.Id == receiptLineId).Select(line => line.ReturnedQuantity).SingleAsync()); Assert.Equal(2m, await after.PurchaseOrderLines.Where(line => line.Id == orderLineId).Select(line => line.ReturnedQuantity).SingleAsync()); Assert.Equal(0m, await after.PurchaseOrderLines.Where(line => line.Id == orderLineId).Select(line => line.CreditedQuantity).SingleAsync()); Assert.Equal(10m, await after.VendorBills.Where(item => item.Id == billResult.VendorBillId).Select(item => item.BalanceDue).SingleAsync());
        Assert.Equal(inventoryBefore + 60m, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(grniBefore, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(payableBefore + 10m, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(cashBefore - 50m, await after.BankAccounts.Where(bank => bank.Id == bankId).Select(bank => bank.CurrentBalance).SingleAsync());
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "supplier-return.shipped" && audit.EntityId == preInvoiceShipment.Id); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "supplier-return.credit.refunded" && audit.EntityId == refund.Id); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "supplier-return.shipment.reversed" && audit.EntityId == postInvoiceShipment.Id);
    }

    [Fact]
    public async Task LandedCosts_AllocateApprovePostFlowIntoInventoryAndReverseWithSourceProvenance()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid foreignCompanyId; Guid freightVendorId; Guid purchaseVendorId; Guid firstItemId; Guid secondItemId; decimal inventoryBefore; decimal payableBefore; decimal vendorOpenBefore;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(item => item.Id).SingleAsync(); purchaseVendorId = await db.Vendors.Select(item => item.Id).FirstAsync(); freightVendorId = await db.Vendors.Select(item => item.Id).Skip(1).FirstAsync(); var items = await db.InventoryItems.OrderBy(item => item.Sku).Take(2).ToListAsync(); firstItemId = items[0].Id; secondItemId = items[1].Id; inventoryBefore = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync(); payableBefore = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync(); vendorOpenBefore = await db.Vendors.Where(item => item.Id == freightVendorId).Select(item => item.OpenBalance).SingleAsync(); foreignCompanyId = Guid.NewGuid(); db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Other landed-cost company", LegalName = "Other landed-cost company", BaseCurrency = "USD", FiscalYearStartMonth = 1 }); await db.SaveChangesAsync();
        }
        void ActAsCompany(Guid activeCompanyId, params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, activeCompanyId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        void ActAs(params string[] permissions) => ActAsCompany(companyId, permissions);
        ActAs(BrassLedgerPermissions.PurchasingManage, BrassLedgerPermissions.PayablesManage, BrassLedgerPermissions.PaymentReverse);
        var orderResult = await transactions.SavePurchaseOrderAsync(new(null, purchaseVendorId, "PO-LC-1", new DateOnly(2026, 8, 1), null, "Landed-cost source", [new(firstItemId, "Imported component A", 5m, 20m), new(secondItemId, "Imported component B", 10m, 10m)])); Assert.True(orderResult.Succeeded, orderResult.ErrorMessage);
        Guid[] orderLineIds; string orderToken; await using (var db = await factory.CreateDbContextAsync()) { var order = await db.PurchaseOrders.SingleAsync(item => item.Id == orderResult.Id); Assert.True((await transactions.ApprovePurchaseOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); orderToken = await db.PurchaseOrders.Where(item => item.Id == order.Id).Select(item => item.ConcurrencyToken).SingleAsync(); orderLineIds = await db.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).OrderBy(line => line.Sequence).Select(line => line.Id).ToArrayAsync(); }
        var receiptResult = await transactions.ReceivePurchaseOrderAsync(new(orderResult.Id!.Value, "RCV-LC-1", new DateOnly(2026, 8, 2), [new(orderLineIds[0], 5m), new(orderLineIds[1], 10m)], orderToken)); Assert.True(receiptResult.Succeeded, receiptResult.ErrorMessage);
        Guid[] receiptLineIds; string receiptToken; Dictionary<Guid, decimal> postReceiptCost; Dictionary<Guid, decimal> postReceiptQuantity;
        await using (var db = await factory.CreateDbContextAsync()) { receiptLineIds = await db.InventoryReceiptLines.Where(line => line.InventoryReceiptId == receiptResult.Id).OrderBy(line => line.Sequence).Select(line => line.Id).ToArrayAsync(); receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync(); postReceiptCost = await db.InventoryItems.Where(item => item.Id == firstItemId || item.Id == secondItemId).ToDictionaryAsync(item => item.Id, item => item.UnitCost); postReceiptQuantity = await db.InventoryItems.Where(item => item.Id == firstItemId || item.Id == secondItemId).ToDictionaryAsync(item => item.Id, item => item.QuantityOnHand); }
        var incompleteManual = await transactions.SaveLandedCostAllocationAsync(new(null, receiptResult.Id!.Value, freightVendorId, "LC-BAD", "LCB-BAD", new DateOnly(2026, 8, 3), new DateOnly(2026, 9, 2), "Manual", "Incomplete manual allocation", [new("Freight", "Ocean freight", 50m)], [new(receiptLineIds[0], 50m)], receiptToken)); Assert.False(incompleteManual.Succeeded); Assert.Contains("each line", incompleteManual.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var saved = await transactions.SaveLandedCostAllocationAsync(new(null, receiptResult.Id!.Value, freightVendorId, "LC-1", "LCB-1", new DateOnly(2026, 8, 3), new DateOnly(2026, 9, 2), "ReceiptValue", "Inbound freight and customs", [new("Freight", "Ocean freight", 40m), new("CustomsDuty", "Import duty", 10m)], null, receiptToken)); Assert.True(saved.Succeeded, saved.ErrorMessage);
        LandedCostAllocation allocation; await using (var db = await factory.CreateDbContextAsync()) { allocation = await db.LandedCostAllocations.SingleAsync(item => item.Id == saved.Id); Assert.Equal([25m, 25m], await db.LandedCostAllocationLines.Where(line => line.LandedCostAllocationId == allocation.Id).OrderBy(line => line.Sequence).Select(line => line.AllocatedAmount).ToArrayAsync()); }
        ActAs(BrassLedgerPermissions.PayablesManage); Assert.False((await transactions.DecideLandedCostAllocationAsync(new(allocation.Id, true, "Unauthorized purchasing decision", allocation.ConcurrencyToken))).Succeeded); Assert.False((await transactions.SubmitLandedCostAllocationAsync(new(allocation.Id, "stale-token"))).Succeeded);
        ActAsCompany(foreignCompanyId, BrassLedgerPermissions.PayablesManage); var foreignSubmit = await transactions.SubmitLandedCostAllocationAsync(new(allocation.Id, allocation.ConcurrencyToken)); Assert.False(foreignSubmit.Succeeded); Assert.Contains("not found", foreignSubmit.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(BrassLedgerPermissions.PayablesManage); Assert.True((await transactions.SubmitLandedCostAllocationAsync(new(allocation.Id, allocation.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) allocation = await db.LandedCostAllocations.SingleAsync(item => item.Id == saved.Id);
        ActAs(BrassLedgerPermissions.PurchasingManage); Assert.True((await transactions.DecideLandedCostAllocationAsync(new(allocation.Id, true, "Freight documents and allocation reviewed", allocation.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) allocation = await db.LandedCostAllocations.SingleAsync(item => item.Id == saved.Id);
        ActAs(BrassLedgerPermissions.PayablesManage); Assert.False((await transactions.PostLandedCostAllocationAsync(new(allocation.Id, "stale-token"))).Succeeded); var posted = await transactions.PostLandedCostAllocationAsync(new(allocation.Id, allocation.ConcurrencyToken)); Assert.True(posted.Succeeded, posted.ErrorMessage);
        Guid landedBillId; await using (var db = await factory.CreateDbContextAsync())
        {
            allocation = await db.LandedCostAllocations.SingleAsync(item => item.Id == saved.Id); landedBillId = allocation.VendorBillId!.Value; var bill = await db.VendorBills.SingleAsync(item => item.Id == landedBillId); var journalLines = await db.JournalEntryLines.Where(line => line.JournalEntryId == allocation.JournalEntryId).ToListAsync(); Assert.Equal("Posted", allocation.Status); Assert.Equal(50m, bill.TotalAmount); Assert.Equal(50m, bill.BalanceDue); Assert.Equal(50m, journalLines.Sum(line => line.Debit)); Assert.Equal(50m, journalLines.Sum(line => line.Credit)); Assert.Equal(2, await db.InventoryTransactions.CountAsync(item => item.Reference == "LC-1" && item.TransactionType == "Landed cost")); Assert.Equal(inventoryBefore + 250m, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(payableBefore + 50m, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(vendorOpenBefore + 50m, await db.Vendors.Where(item => item.Id == freightVendorId).Select(item => item.OpenBalance).SingleAsync());
            var updated = await db.InventoryItems.Where(item => item.Id == firstItemId || item.Id == secondItemId).ToDictionaryAsync(item => item.Id); Assert.Equal(Math.Round(((postReceiptQuantity[firstItemId] * postReceiptCost[firstItemId]) + 25m) / postReceiptQuantity[firstItemId], 2, MidpointRounding.AwayFromZero), updated[firstItemId].UnitCost); Assert.Equal(Math.Round(((postReceiptQuantity[secondItemId] * postReceiptCost[secondItemId]) + 25m) / postReceiptQuantity[secondItemId], 2, MidpointRounding.AwayFromZero), updated[secondItemId].UnitCost); receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync();
        }
        ActAs(BrassLedgerPermissions.PayablesManage, BrassLedgerPermissions.PaymentReverse); var unsafeVoid = await transactions.VoidVendorBillAsync(new(landedBillId, new DateOnly(2026, 8, 4), "Would bypass inventory valuation")); Assert.False(unsafeVoid.Succeeded); Assert.Contains("landed-cost", unsafeVoid.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(BrassLedgerPermissions.PurchasingManage, BrassLedgerPermissions.PayablesManage);
        var goodsBill = await PostControlledPurchaseInvoiceAsync(transactions, factory, receiptResult.Id.Value, "BILL-LC-GOODS-1", new DateOnly(2026, 8, 4), new DateOnly(2026, 9, 3), "Goods vendor invoice");
        await using (var db = await factory.CreateDbContextAsync()) receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync();
        ActAs(BrassLedgerPermissions.PurchasingManage); var supplierReturn = await transactions.AuthorizeSupplierReturnAsync(new(receiptResult.Id.Value, "SRA-LC-1", new DateOnly(2026, 8, 4), "Verify landed-cost return valuation", [new(receiptLineIds[0], 1m), new(receiptLineIds[1], 1m)], receiptToken)); Assert.True(supplierReturn.Succeeded, supplierReturn.ErrorMessage);
        Guid[] supplierReturnLineIds; string returnToken; await using (var db = await factory.CreateDbContextAsync()) { var returnLines = await db.SupplierReturnAuthorizationLines.Where(line => line.SupplierReturnAuthorizationId == supplierReturn.Id).OrderBy(line => line.Sequence).ToArrayAsync(); Assert.Equal([25m, 12.5m], returnLines.Select(line => line.UnitCost)); Assert.Equal([20m, 10m], returnLines.Select(line => line.ReceiptUnitCost)); supplierReturnLineIds = returnLines.Select(line => line.Id).ToArray(); returnToken = await db.SupplierReturnAuthorizations.Where(item => item.Id == supplierReturn.Id).Select(item => item.ConcurrencyToken).SingleAsync(); }
        var supplierReturnShipment = await transactions.ShipSupplierReturnAsync(new(supplierReturn.Id!.Value, "SRS-LC-1", new DateOnly(2026, 8, 5), null, null, [new(supplierReturnLineIds[0], 1m), new(supplierReturnLineIds[1], 1m)], returnToken)); Assert.True(supplierReturnShipment.Succeeded, supplierReturnShipment.ErrorMessage);
        string supplierReturnShipmentToken; await using (var db = await factory.CreateDbContextAsync()) { var shipment = await db.SupplierReturnShipments.SingleAsync(item => item.Id == supplierReturnShipment.Id); supplierReturnShipmentToken = shipment.ConcurrencyToken; Assert.Equal(37.5m, shipment.TotalAmount); Assert.Equal(30m, shipment.VendorCreditAmount); Assert.Equal(170m, await db.VendorBills.Where(item => item.Id == goodsBill.VendorBillId).Select(item => item.BalanceDue).SingleAsync()); Assert.Equal(50m, await db.VendorBills.Where(item => item.Id == landedBillId).Select(item => item.BalanceDue).SingleAsync()); Assert.Equal(7.5m, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.PurchasePriceVariance).Select(account => account.CurrentBalance).SingleAsync()); }
        ActAs(BrassLedgerPermissions.PurchasingManage, BrassLedgerPermissions.PaymentReverse); Assert.True((await transactions.ReverseSupplierReturnShipmentAsync(new(supplierReturnShipment.Id!.Value, new DateOnly(2026, 8, 6), "Return shipment test complete", supplierReturnShipmentToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) receiptToken = await db.InventoryReceipts.Where(item => item.Id == receiptResult.Id).Select(item => item.ConcurrencyToken).SingleAsync();
        PurchaseInvoiceMatch goodsMatch; await using (var db = await factory.CreateDbContextAsync()) goodsMatch = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == goodsBill.MatchId);
        ActAs(BrassLedgerPermissions.PurchasingManage, BrassLedgerPermissions.PaymentReverse); Assert.True((await transactions.ReversePurchaseInvoiceMatchAsync(new(goodsMatch.Id, new DateOnly(2026, 8, 6), "Remove goods bill after return test", goodsMatch.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) allocation = await db.LandedCostAllocations.SingleAsync(item => item.Id == saved.Id);
        ActAs(BrassLedgerPermissions.PurchasingManage, BrassLedgerPermissions.PaymentReverse); var reversed = await transactions.ReverseLandedCostAllocationAsync(new(allocation.Id, new DateOnly(2026, 8, 7), "Carrier invoice was cancelled", allocation.ConcurrencyToken)); Assert.True(reversed.Succeeded, reversed.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync(); allocation = await after.LandedCostAllocations.SingleAsync(item => item.Id == saved.Id); Assert.Equal("Reversed", allocation.Status); Assert.NotNull(allocation.ReversalJournalEntryId); Assert.Equal("Voided", await after.VendorBills.Where(item => item.Id == landedBillId).Select(item => item.Status).SingleAsync()); Assert.Equal(inventoryBefore + 200m, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(payableBefore, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsPayable).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(0m, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.PurchasePriceVariance).Select(account => account.CurrentBalance).SingleAsync()); Assert.Equal(vendorOpenBefore, await after.Vendors.Where(item => item.Id == freightVendorId).Select(item => item.OpenBalance).SingleAsync()); Assert.Equal(postReceiptCost[firstItemId], await after.InventoryItems.Where(item => item.Id == firstItemId).Select(item => item.UnitCost).SingleAsync()); Assert.Equal(postReceiptCost[secondItemId], await after.InventoryItems.Where(item => item.Id == secondItemId).Select(item => item.UnitCost).SingleAsync()); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "landed-cost.posted" && audit.EntityId == allocation.Id); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "landed-cost.reversed" && audit.EntityId == allocation.Id);
    }

    [Fact]
    public async Task PurchasingWorkflow_UnmatchesBillThenReversesLatestReceipt_WithoutLosingHistory()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid vendorId; Guid itemId; decimal priorQuantity; decimal priorCost;
        await using (var db = await factory.CreateDbContextAsync()) { var vendor = await db.Vendors.FirstAsync(); var seedItem = await db.InventoryItems.FirstAsync(); vendorId = vendor.Id; itemId = seedItem.Id; priorQuantity = seedItem.QuantityOnHand; priorCost = seedItem.UnitCost; }
        var saved = await transactions.SavePurchaseOrderAsync(new SavePurchaseOrderRequest(null, vendorId, "PO-REVERSE-1", new DateOnly(2026, 8, 10), null, "Reversal test", [new PurchaseOrderLineRequest(itemId, "Reversible receipt", 1m, 25m)])); Assert.True(saved.Succeeded, saved.ErrorMessage);
        Guid lineId; string token;
        await using (var db = await factory.CreateDbContextAsync()) { var order = await db.PurchaseOrders.SingleAsync(candidate => candidate.Id == saved.Id); Assert.True((await transactions.ApprovePurchaseOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); }
        await using (var db = await factory.CreateDbContextAsync()) { var order = await db.PurchaseOrders.SingleAsync(candidate => candidate.Id == saved.Id); token = order.ConcurrencyToken; lineId = await db.PurchaseOrderLines.Where(line => line.PurchaseOrderId == order.Id).Select(line => line.Id).SingleAsync(); }
        var received = await transactions.ReceivePurchaseOrderAsync(new(saved.Id!.Value, "RCV-REVERSE-1", new DateOnly(2026, 8, 11), [new(lineId, 1m)], token)); Assert.True(received.Succeeded, received.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.InventoryReceipts.Where(receipt => receipt.Id == received.Id).Select(receipt => receipt.ConcurrencyToken).SingleAsync();
        var matched = await PostControlledPurchaseInvoiceAsync(transactions, factory, received.Id!.Value, "BILL-REVERSE-1", new DateOnly(2026, 8, 12), new DateOnly(2026, 9, 11), "Invoice to correct");
        PurchaseInvoiceMatch invoiceMatch; await using (var db = await factory.CreateDbContextAsync()) invoiceMatch = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == matched.MatchId);
        var unmatched = await transactions.ReversePurchaseInvoiceMatchAsync(new(invoiceMatch.Id, new DateOnly(2026, 8, 12), "Incorrect vendor invoice number", invoiceMatch.ConcurrencyToken)); Assert.True(unmatched.Succeeded, unmatched.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.InventoryReceipts.Where(receipt => receipt.Id == received.Id).Select(receipt => receipt.ConcurrencyToken).SingleAsync();
        var reversed = await transactions.ReverseInventoryReceiptAsync(new(received.Id!.Value, new DateOnly(2026, 8, 13), "Goods were not accepted", token)); Assert.True(reversed.Succeeded, reversed.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync();
        var item = await after.InventoryItems.SingleAsync(candidate => candidate.Id == itemId);
        var receipt = await after.InventoryReceipts.SingleAsync(candidate => candidate.Id == received.Id);
        var bill = await after.VendorBills.SingleAsync(candidate => candidate.Id == matched.VendorBillId);
        Assert.Equal(priorQuantity, item.QuantityOnHand); Assert.Equal(priorCost, item.UnitCost);
        Assert.Equal("Reversed", receipt.Status); Assert.NotNull(receipt.ReversalJournalEntryId); Assert.Equal(matched.VendorBillId, await after.VendorBills.Where(candidate => candidate.InventoryReceiptId == receipt.Id).Select(candidate => candidate.Id).SingleAsync());
        Assert.Equal("Voided", bill.Status); Assert.Equal(0m, bill.BalanceDue);
        Assert.Equal("Approved", await after.PurchaseOrders.Where(order => order.Id == saved.Id).Select(order => order.Status).SingleAsync());
        Assert.Equal(2, await after.InventoryTransactions.CountAsync(movement => movement.Reference == "RCV-REVERSE-1" || movement.Reference == "REV-RCV-REVERSE-1"));
    }

    [Fact]
    public async Task SalesFulfillment_PartiallyShipsInvoicesVoidsAndReverses_WithBalancedTraceablePostings()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid customerId; Guid itemId; decimal priorQuantity; decimal unitCost; decimal inventoryBalance; decimal cogsBalance; decimal receivableBalance;
        await using (var db = await factory.CreateDbContextAsync())
        {
            customerId = await db.Customers.Select(customer => customer.Id).FirstAsync();
            var item = await db.InventoryItems.SingleAsync(candidate => candidate.Sku == "FG-200"); itemId = item.Id; priorQuantity = item.QuantityOnHand; unitCost = item.UnitCost;
            inventoryBalance = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync();
            cogsBalance = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.CostOfGoodsSold).Select(account => account.CurrentBalance).SingleAsync();
            receivableBalance = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(account => account.CurrentBalance).SingleAsync();
        }

        var roundedToZero = await transactions.SaveSalesOrderAsync(new SaveSalesOrderRequest(null, customerId, "SO-FULFILL-TINY", new DateOnly(2026, 8, 1), null, "Sub-precision quantity", [new SalesOrderLineRequest(itemId, "Compression kits", 0.00001m, 100m, 0m, 0m, "4000")]));
        Assert.False(roundedToZero.Succeeded);
        Assert.Contains("positive quantity", roundedToZero.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var saved = await transactions.SaveSalesOrderAsync(new SaveSalesOrderRequest(null, customerId, "SO-FULFILL-1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5), "Controlled fulfillment test", [new SalesOrderLineRequest(itemId, "Compression kits", 4m, 100m, 10m, 8m, "4000")]));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        Guid orderLineId; string token;
        await using (var db = await factory.CreateDbContextAsync()) { var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); orderLineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Id).SingleAsync(); Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); }
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var staleAllocation = await transactions.AllocateSalesOrderAsync(new(saved.Id!.Value, [new(orderLineId, 4m)], "stale-token")); Assert.False(staleAllocation.Succeeded); Assert.Contains("changed", staleAllocation.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var overAllocation = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new(orderLineId, priorQuantity + 1m)], token)); Assert.False(overAllocation.Succeeded);
        var allocated = await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new(orderLineId, 4m)], token)); Assert.True(allocated.Succeeded, allocated.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var overShipment = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-OVER-1", new DateOnly(2026, 8, 5), [new(orderLineId, 5m)], token)); Assert.False(overShipment.Succeeded); Assert.Contains("allocated", overShipment.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var shipped = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-FULFILL-1", new DateOnly(2026, 8, 5), [new(orderLineId, 2m)], token)); Assert.True(shipped.Succeeded, shipped.ErrorMessage);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); var line = await db.SalesOrderLines.SingleAsync(candidate => candidate.Id == orderLineId); var item = await db.InventoryItems.SingleAsync(candidate => candidate.Id == itemId); var shipment = await db.InventoryShipments.SingleAsync(candidate => candidate.Id == shipped.Id);
            Assert.Equal("PartiallyShipped", order.Status); Assert.Equal(2m, line.AllocatedQuantity); Assert.Equal(2m, line.ShippedQuantity); Assert.Equal(priorQuantity - 2m, item.QuantityOnHand); Assert.Equal(2m * unitCost, shipment.TotalCost);
            Assert.Equal(inventoryBalance - shipment.TotalCost, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync());
            Assert.Equal(cogsBalance + shipment.TotalCost, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.CostOfGoodsSold).Select(account => account.CurrentBalance).SingleAsync());
            token = shipment.ConcurrencyToken;
        }
        Guid ambiguousMovementId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            ambiguousMovementId = Guid.NewGuid();
            var otherJournalId = await db.JournalEntries.Where(entry => entry.Id != db.InventoryShipments.Where(shipment => shipment.Id == shipped.Id).Select(shipment => shipment.JournalEntryId).Single()).Select(entry => entry.Id).FirstAsync();
            db.InventoryTransactions.Add(new InventoryTransaction { Id = ambiguousMovementId, CompanyId = await db.Companies.Select(company => company.Id).SingleAsync(), InventoryItemId = itemId, OccurredOn = new DateOnly(2026, 8, 5), TransactionType = "Same-day valuation test", QuantityChange = 0m, UnitCost = unitCost, TotalCost = 0m, Reference = "SAME-DAY-VALUATION", JournalEntryId = otherJournalId });
            await db.SaveChangesAsync();
        }
        var ambiguousReversal = await transactions.ReverseInventoryShipmentAsync(new(shipped.Id!.Value, new DateOnly(2026, 8, 5), "Ambiguous same-day valuation", token));
        Assert.False(ambiguousReversal.Succeeded);
        Assert.Contains("same-day", ambiguousReversal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.InventoryTransactions.Remove(await db.InventoryTransactions.SingleAsync(entry => entry.Id == ambiguousMovementId));
            await db.SaveChangesAsync();
        }
        var invoiced = await transactions.InvoiceInventoryShipmentAsync(new(shipped.Id!.Value, "INV-FULFILL-1", new DateOnly(2026, 8, 6), new DateOnly(2026, 9, 5), "Invoice exact shipped quantities", token)); Assert.True(invoiced.Succeeded, invoiced.ErrorMessage);
        var duplicateInvoice = await transactions.InvoiceInventoryShipmentAsync(new(shipped.Id.Value, "INV-FULFILL-2", new DateOnly(2026, 8, 6), new DateOnly(2026, 9, 5), "Duplicate attempt", token)); Assert.False(duplicateInvoice.Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var invoice = await db.SalesInvoices.SingleAsync(candidate => candidate.Id == invoiced.Id); var line = await db.SalesInvoiceLines.SingleAsync(candidate => candidate.SalesInvoiceId == invoice.Id); var shipment = await db.InventoryShipments.SingleAsync(candidate => candidate.Id == shipped.Id);
            Assert.Equal(195m, invoice.Subtotal); Assert.Equal(4m, invoice.TaxAmount); Assert.Equal(199m, invoice.TotalAmount); Assert.Equal(orderLineId, line.SalesOrderLineId); Assert.Equal(shipped.Id, invoice.InventoryShipmentId); Assert.Equal(invoice.Id, shipment.SalesInvoiceId);
            Assert.Equal(receivableBalance + 199m, await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(account => account.CurrentBalance).SingleAsync());
            token = shipment.ConcurrencyToken;
        }
        var unsafeReversal = await transactions.ReverseInventoryShipmentAsync(new(shipped.Id.Value, new DateOnly(2026, 8, 7), "Cannot reverse billed goods", token)); Assert.False(unsafeReversal.Succeeded); Assert.Contains("invoice", unsafeReversal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var voided = await transactions.VoidInvoiceAsync(new(invoiced.Id!.Value, new DateOnly(2026, 8, 7), "Customer shipment invoice entered prematurely")); Assert.True(voided.Succeeded, voided.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.InventoryShipments.Where(shipment => shipment.Id == shipped.Id).Select(shipment => shipment.ConcurrencyToken).SingleAsync();
        var reversed = await transactions.ReverseInventoryShipmentAsync(new(shipped.Id.Value, new DateOnly(2026, 8, 8), "Carrier returned goods before delivery", token)); Assert.True(reversed.Succeeded, reversed.ErrorMessage);

        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(priorQuantity, await after.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync());
        Assert.Equal("Reversed", await after.InventoryShipments.Where(shipment => shipment.Id == shipped.Id).Select(shipment => shipment.Status).SingleAsync());
        Assert.Equal("Voided", await after.SalesInvoices.Where(invoice => invoice.Id == invoiced.Id).Select(invoice => invoice.Status).SingleAsync());
        var finalOrderLine = await after.SalesOrderLines.SingleAsync(line => line.Id == orderLineId); Assert.Equal(4m, finalOrderLine.AllocatedQuantity); Assert.Equal(0m, finalOrderLine.ShippedQuantity); Assert.Equal(0m, finalOrderLine.InvoicedQuantity);
        Assert.Equal(inventoryBalance, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(account => account.CurrentBalance).SingleAsync());
        Assert.Equal(cogsBalance, await after.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.CostOfGoodsSold).Select(account => account.CurrentBalance).SingleAsync());
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "inventory-shipment.posted" && audit.EntityId == shipped.Id);
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "inventory-shipment.invoiced" && audit.EntityId == shipped.Id);
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "inventory-shipment.reversed" && audit.EntityId == shipped.Id);
    }

    [Fact]
    public async Task SalesFulfillment_EnforcesSalesWarehouseAndReceivablesSeparation()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(); var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        Guid companyId; Guid foreignCompanyId; Guid customerId; Guid itemId; Guid foreignCustomerId; Guid foreignItemId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync(); customerId = await db.Customers.Select(customer => customer.Id).FirstAsync(); itemId = await db.InventoryItems.Where(item => item.Sku == "RM-220").Select(item => item.Id).SingleAsync();
            foreignCompanyId = Guid.NewGuid(); foreignCustomerId = Guid.NewGuid(); foreignItemId = Guid.NewGuid();
            db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Foreign company", LegalName = "Foreign company", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
            db.Customers.Add(new Customer { Id = foreignCustomerId, CompanyId = foreignCompanyId, CustomerNumber = "FOREIGN-C", Name = "Foreign customer" });
            db.InventoryItems.Add(new InventoryItem { Id = foreignItemId, CompanyId = foreignCompanyId, Sku = "FOREIGN-I", Description = "Foreign item", UnitPrice = 1m, UnitCost = 1m, QuantityOnHand = 10m, IsActive = true });
            await db.SaveChangesAsync();
        }
        void ActAsCompany(Guid activeCompanyId, params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, activeCompanyId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        void ActAs(params string[] permissions) => ActAsCompany(companyId, permissions);
        var request = new SaveSalesOrderRequest(null, customerId, "SO-SOD-1", new DateOnly(2026, 8, 10), null, "Separation test", [new SalesOrderLineRequest(itemId, "Fasteners", 2m, 20m, 0m, 0m, "4000")]);
        ActAs(BrassLedgerPermissions.FulfillmentManage); Assert.False((await transactions.SaveSalesOrderAsync(request)).Succeeded);
        ActAs(BrassLedgerPermissions.SalesManage);
        Assert.False((await transactions.SaveSalesOrderAsync(request with { CustomerId = foreignCustomerId, OrderNumber = "SO-FOREIGN-C" })).Succeeded);
        Assert.False((await transactions.SaveSalesOrderAsync(request with { OrderNumber = "SO-FOREIGN-I", Lines = [new SalesOrderLineRequest(foreignItemId, "Foreign", 1m, 1m, 0m, 0m, "4000")] })).Succeeded);
        var saved = await transactions.SaveSalesOrderAsync(request); Assert.True(saved.Succeeded, saved.ErrorMessage);
        Guid lineId; string token; await using (var db = await factory.CreateDbContextAsync()) { var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); lineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Id).SingleAsync(); Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); }
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        ActAsCompany(foreignCompanyId, BrassLedgerPermissions.SalesManage);
        var foreignCancellation = await transactions.CancelSalesOrderAsync(new(saved.Id!.Value, "Cross-company attempt", token)); Assert.False(foreignCancellation.Succeeded); Assert.Contains("not found", foreignCancellation.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var foreignAmendment = await transactions.AmendSalesOrderAsync(new(saved.Id.Value, request.OrderedOn, null, request.Notes, "Cross-company attempt", request.Lines, token)); Assert.False(foreignAmendment.Succeeded); Assert.Contains("not found", foreignAmendment.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(BrassLedgerPermissions.SalesManage);
        Assert.False((await transactions.AllocateSalesOrderAsync(new(saved.Id!.Value, [new(lineId, 2m)], token))).Succeeded);
        ActAs(BrassLedgerPermissions.FulfillmentManage); Assert.False((await transactions.CancelSalesOrderAsync(new(saved.Id.Value, "Warehouse cannot cancel commercial demand", token))).Succeeded); Assert.True((await transactions.AllocateSalesOrderAsync(new(saved.Id.Value, [new(lineId, 2m)], token))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var shipped = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-SOD-1", new DateOnly(2026, 8, 11), [new(lineId, 2m)], token)); Assert.True(shipped.Succeeded, shipped.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.InventoryShipments.Where(shipment => shipment.Id == shipped.Id).Select(shipment => shipment.ConcurrencyToken).SingleAsync();
        Assert.False((await transactions.InvoiceInventoryShipmentAsync(new(shipped.Id!.Value, "INV-SOD-1", new DateOnly(2026, 8, 11), new DateOnly(2026, 9, 10), "Unauthorized invoice", token))).Succeeded);
        ActAs(BrassLedgerPermissions.ReceivablesManage); Assert.True((await transactions.InvoiceInventoryShipmentAsync(new(shipped.Id.Value, "INV-SOD-1", new DateOnly(2026, 8, 11), new DateOnly(2026, 9, 10), "Authorized invoice", token))).Succeeded);
        Guid shipmentLineId; await using (var db = await factory.CreateDbContextAsync()) { token = await db.InventoryShipments.Where(shipment => shipment.Id == shipped.Id).Select(shipment => shipment.ConcurrencyToken).SingleAsync(); shipmentLineId = await db.InventoryShipmentLines.Where(line => line.InventoryShipmentId == shipped.Id).Select(line => line.Id).SingleAsync(); }
        ActAs(BrassLedgerPermissions.ReceivablesManage); Assert.False((await transactions.AuthorizeCustomerReturnAsync(new(shipped.Id.Value, "RMA-SOD-1", new DateOnly(2026, 8, 12), "Unauthorized return", [new(shipmentLineId, 1m)], token))).Succeeded);
        ActAs(BrassLedgerPermissions.SalesManage); var returnAuthorization = await transactions.AuthorizeCustomerReturnAsync(new(shipped.Id.Value, "RMA-SOD-1", new DateOnly(2026, 8, 12), "Authorized return", [new(shipmentLineId, 1m)], token)); Assert.True(returnAuthorization.Succeeded, returnAuthorization.ErrorMessage);
        string returnToken; await using (var db = await factory.CreateDbContextAsync()) returnToken = await db.CustomerReturnAuthorizations.Where(authorization => authorization.Id == returnAuthorization.Id).Select(authorization => authorization.ConcurrencyToken).SingleAsync();
        ActAsCompany(foreignCompanyId, BrassLedgerPermissions.SalesManage); var foreignReturnCancellation = await transactions.CancelCustomerReturnAsync(new(returnAuthorization.Id!.Value, "Cross-company attempt", returnToken)); Assert.False(foreignReturnCancellation.Succeeded); Assert.Contains("not found", foreignReturnCancellation.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomerReturns_PreserveShipmentCostAndInvoiceAmounts_ThroughCreditApplyRefundAndReversals()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid customerId; Guid itemId; Guid bankId; decimal startingQuantity; decimal startingInventory; decimal startingCogs; decimal startingReceivables; decimal startingCustomerBalance;
        await using (var db = await factory.CreateDbContextAsync())
        {
            customerId = await db.Customers.Select(x => x.Id).FirstAsync(); startingCustomerBalance = await db.Customers.Where(x => x.Id == customerId).Select(x => x.OpenBalance).SingleAsync(); var item = await db.InventoryItems.SingleAsync(x => x.Sku == "FG-200"); itemId = item.Id; startingQuantity = item.QuantityOnHand; bankId = await db.BankAccounts.Select(x => x.Id).FirstAsync();
            startingInventory = await db.Accounts.Where(x => x.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(x => x.CurrentBalance).SingleAsync(); startingCogs = await db.Accounts.Where(x => x.OperationalRole == AccountingAccountRoles.CostOfGoodsSold).Select(x => x.CurrentBalance).SingleAsync(); startingReceivables = await db.Accounts.Where(x => x.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(x => x.CurrentBalance).SingleAsync();
        }
        var orderResult = await transactions.SaveSalesOrderAsync(new(null, customerId, "SO-RETURN-1", new DateOnly(2026, 8, 1), null, "Return provenance test", [new SalesOrderLineRequest(itemId, "Compression kits", 2m, 100m, 10m, 8m, "4000")])); Assert.True(orderResult.Succeeded, orderResult.ErrorMessage);
        Guid orderLineId; string orderToken; await using (var db = await factory.CreateDbContextAsync()) { var order = await db.SalesOrders.SingleAsync(x => x.Id == orderResult.Id); orderLineId = await db.SalesOrderLines.Where(x => x.SalesOrderId == order.Id).Select(x => x.Id).SingleAsync(); Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); }
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(x => x.Id == orderResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); Assert.True((await transactions.AllocateSalesOrderAsync(new(orderResult.Id!.Value, [new(orderLineId, 2m)], orderToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(x => x.Id == orderResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); var shipmentResult = await transactions.ShipSalesOrderAsync(new(orderResult.Id.Value, "SHIP-RETURN-1", new DateOnly(2026, 8, 2), [new(orderLineId, 2m)], orderToken)); Assert.True(shipmentResult.Succeeded, shipmentResult.ErrorMessage);
        string shipmentToken; Guid shipmentLineId; await using (var db = await factory.CreateDbContextAsync()) { var shipment = await db.InventoryShipments.SingleAsync(x => x.Id == shipmentResult.Id); shipmentToken = shipment.ConcurrencyToken; shipmentLineId = await db.InventoryShipmentLines.Where(x => x.InventoryShipmentId == shipment.Id).Select(x => x.Id).SingleAsync(); }
        var invoiceResult = await transactions.InvoiceInventoryShipmentAsync(new(shipmentResult.Id!.Value, "INV-RETURN-1", new DateOnly(2026, 8, 3), new DateOnly(2026, 9, 2), "Original sale", shipmentToken)); Assert.True(invoiceResult.Succeeded, invoiceResult.ErrorMessage);
        Assert.True((await transactions.ApplyInvoicePaymentAsync(new(invoiceResult.Id!.Value, bankId, new DateOnly(2026, 8, 4), 198m, "PAY-RETURN-1"))).Succeeded);
        var otherInvoice = await PostInvoiceThroughWorkflowAsync(transactions, new(customerId, "INV-RETURN-OTHER", new DateOnly(2026, 8, 5), new DateOnly(2026, 9, 4), 50m, 0m, "4000", "Later sale")); Assert.True(otherInvoice.Succeeded, otherInvoice.ErrorMessage);

        await using (var db = await factory.CreateDbContextAsync()) shipmentToken = await db.InventoryShipments.Where(x => x.Id == shipmentResult.Id).Select(x => x.ConcurrencyToken).SingleAsync();
        var authorizationResult = await transactions.AuthorizeCustomerReturnAsync(new(shipmentResult.Id.Value, "RMA-RETURN-1", new DateOnly(2026, 8, 6), "Customer changed requirements", [new(shipmentLineId, 1m)], shipmentToken)); Assert.True(authorizationResult.Succeeded, authorizationResult.ErrorMessage);
        string authorizationToken; Guid authorizationLineId; await using (var db = await factory.CreateDbContextAsync()) { var authorization = await db.CustomerReturnAuthorizations.SingleAsync(x => x.Id == authorizationResult.Id); authorizationToken = authorization.ConcurrencyToken; authorizationLineId = await db.CustomerReturnAuthorizationLines.Where(x => x.CustomerReturnAuthorizationId == authorization.Id).Select(x => x.Id).SingleAsync(); }
        var staleAuthorization = await transactions.AuthorizeCustomerReturnAsync(new(shipmentResult.Id.Value, "RMA-RETURN-STALE", new DateOnly(2026, 8, 6), "Stale competing request", [new(shipmentLineId, 1m)], shipmentToken)); Assert.False(staleAuthorization.Succeeded); Assert.Contains("changed", staleAuthorization.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync()) shipmentToken = await db.InventoryShipments.Where(x => x.Id == shipmentResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); var excessiveReturn = await transactions.AuthorizeCustomerReturnAsync(new(shipmentResult.Id.Value, "RMA-RETURN-OVER", new DateOnly(2026, 8, 6), "Excess attempt", [new(shipmentLineId, 2m)], shipmentToken)); Assert.False(excessiveReturn.Succeeded); Assert.Contains("unreserved", excessiveReturn.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var receiptResult = await transactions.ReceiveCustomerReturnAsync(new(authorizationResult.Id!.Value, "CRCV-RETURN-1", new DateOnly(2026, 8, 7), null, null, [new(authorizationLineId, 1m)], authorizationToken)); Assert.True(receiptResult.Succeeded, receiptResult.ErrorMessage);
        string receiptToken; await using (var db = await factory.CreateDbContextAsync()) { var receipt = await db.CustomerReturnReceipts.SingleAsync(x => x.Id == receiptResult.Id); receiptToken = receipt.ConcurrencyToken; Assert.Equal(startingQuantity - 1m, await db.InventoryItems.Where(x => x.Id == itemId).Select(x => x.QuantityOnHand).SingleAsync()); Assert.Equal(startingInventory - receipt.TotalCost, await db.Accounts.Where(x => x.OperationalRole == AccountingAccountRoles.InventoryAsset).Select(x => x.CurrentBalance).SingleAsync()); Assert.Equal(startingCogs + receipt.TotalCost, await db.Accounts.Where(x => x.OperationalRole == AccountingAccountRoles.CostOfGoodsSold).Select(x => x.CurrentBalance).SingleAsync()); }
        var creditResult = await transactions.CreditCustomerReturnAsync(new(receiptResult.Id!.Value, "CM-RETURN-1", new DateOnly(2026, 8, 8), "Credit accepted return", receiptToken)); Assert.True(creditResult.Succeeded, creditResult.ErrorMessage);
        string creditToken; await using (var db = await factory.CreateDbContextAsync()) { var credit = await db.CustomerReturnCredits.SingleAsync(x => x.Id == creditResult.Id); creditToken = credit.ConcurrencyToken; Assert.Equal(95m, credit.Subtotal); Assert.Equal(4m, credit.TaxAmount); Assert.Equal(99m, credit.TotalAmount); Assert.Equal(0m, credit.SourceAppliedAmount); Assert.Equal(startingReceivables + 50m - 99m, await db.Accounts.Where(x => x.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(x => x.CurrentBalance).SingleAsync()); Assert.Equal(startingCustomerBalance - 49m, await db.Customers.Where(x => x.Id == customerId).Select(x => x.OpenBalance).SingleAsync()); }
        Guid closedPeriodId; await using (var db = await factory.CreateDbContextAsync()) { closedPeriodId = Guid.NewGuid(); db.AccountingPeriods.Add(new AccountingPeriod { Id = closedPeriodId, CompanyId = await db.Companies.Select(x => x.Id).SingleAsync(), StartsOn = new DateOnly(2026, 8, 9), EndsOn = new DateOnly(2026, 8, 9), Status = "Closed", ClosedAtUtc = DateTimeOffset.UtcNow, Notes = "Return credit application control test" }); await db.SaveChangesAsync(); }
        var closedApplication = await transactions.ApplyCustomerReturnCreditAsync(new(creditResult.Id!.Value, otherInvoice.Id!.Value, new DateOnly(2026, 8, 9), 50m, creditToken)); Assert.False(closedApplication.Succeeded); Assert.Contains("closed", closedApplication.ErrorMessage, StringComparison.OrdinalIgnoreCase); await using (var db = await factory.CreateDbContextAsync()) { db.AccountingPeriods.Remove(await db.AccountingPeriods.SingleAsync(x => x.Id == closedPeriodId)); await db.SaveChangesAsync(); }
        var applicationResult = await transactions.ApplyCustomerReturnCreditAsync(new(creditResult.Id!.Value, otherInvoice.Id!.Value, new DateOnly(2026, 8, 9), 50m, creditToken)); Assert.True(applicationResult.Succeeded, applicationResult.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) creditToken = await db.CustomerReturnCredits.Where(x => x.Id == creditResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); var refundResult = await transactions.RefundCustomerReturnCreditAsync(new(creditResult.Id.Value, bankId, "REFUND-RETURN-1", new DateOnly(2026, 8, 10), 49m, creditToken)); Assert.True(refundResult.Succeeded, refundResult.ErrorMessage);
        string refundToken; string applicationToken; await using (var db = await factory.CreateDbContextAsync()) { var credit = await db.CustomerReturnCredits.SingleAsync(x => x.Id == creditResult.Id); Assert.Equal(99m, credit.AppliedAmount + credit.RefundedAmount); Assert.Equal(startingCustomerBalance, await db.Customers.Where(x => x.Id == customerId).Select(x => x.OpenBalance).SingleAsync()); refundToken = await db.CustomerReturnCreditRefunds.Where(x => x.Id == refundResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); applicationToken = await db.CustomerReturnCreditApplications.Where(x => x.Id == applicationResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); }
        Assert.True((await transactions.ReverseCustomerReturnCreditRefundAsync(new(refundResult.Id!.Value, new DateOnly(2026, 8, 11), "Refund payment stopped", refundToken))).Succeeded); Assert.True((await transactions.ReverseCustomerReturnCreditApplicationAsync(new(applicationResult.Id!.Value, new DateOnly(2026, 8, 11), "Apply to different document", applicationToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) creditToken = await db.CustomerReturnCredits.Where(x => x.Id == creditResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); Assert.True((await transactions.ReverseCustomerReturnCreditAsync(new(creditResult.Id.Value, new DateOnly(2026, 8, 12), "Return rejected after inspection", creditToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) receiptToken = await db.CustomerReturnReceipts.Where(x => x.Id == receiptResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); Assert.True((await transactions.ReverseCustomerReturnReceiptAsync(new(receiptResult.Id!.Value, new DateOnly(2026, 8, 12), "Goods sent back to customer", receiptToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) authorizationToken = await db.CustomerReturnAuthorizations.Where(x => x.Id == authorizationResult.Id).Select(x => x.ConcurrencyToken).SingleAsync(); Assert.True((await transactions.CancelCustomerReturnAsync(new(authorizationResult.Id!.Value, "Return rejected", authorizationToken))).Succeeded);
        await using var after = await factory.CreateDbContextAsync(); Assert.Equal(startingQuantity - 2m, await after.InventoryItems.Where(x => x.Id == itemId).Select(x => x.QuantityOnHand).SingleAsync()); Assert.Equal(startingCustomerBalance + 50m, await after.Customers.Where(x => x.Id == customerId).Select(x => x.OpenBalance).SingleAsync()); Assert.Equal(50m, await after.SalesInvoices.Where(x => x.Id == otherInvoice.Id).Select(x => x.BalanceDue).SingleAsync()); Assert.Equal("Cancelled", await after.CustomerReturnAuthorizations.Where(x => x.Id == authorizationResult.Id).Select(x => x.Status).SingleAsync()); Assert.Equal("Reversed", await after.CustomerReturnReceipts.Where(x => x.Id == receiptResult.Id).Select(x => x.Status).SingleAsync()); Assert.Equal("Reversed", await after.CustomerReturnCredits.Where(x => x.Id == creditResult.Id).Select(x => x.Status).SingleAsync()); Assert.Equal(10, await after.BusinessAuditEntries.CountAsync(x => x.EntityId == authorizationResult.Id || x.EntityId == receiptResult.Id || x.EntityId == creditResult.Id || x.EntityId == applicationResult.Id || x.EntityId == refundResult.Id));
    }

    [Fact]
    public async Task SalesQuote_ApprovesAndConvertsExactTermsOnce_WithoutPostingOrMovingInventory()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid customerId; Guid itemId; decimal quantityOnHand; int journalCount;
        await using (var db = await factory.CreateDbContextAsync())
        {
            customerId = await db.Customers.Select(customer => customer.Id).FirstAsync();
            var item = await db.InventoryItems.Where(candidate => candidate.Sku == "FG-200").SingleAsync(); itemId = item.Id; quantityOnHand = item.QuantityOnHand;
            journalCount = await db.JournalEntries.CountAsync();
        }
        var saved = await transactions.SaveSalesQuoteAsync(new SaveSalesQuoteRequest(null, customerId, "QUO-LIFECYCLE-1", today.AddDays(-1), today.AddDays(30), "Customer-approved scope", [new SalesOrderLineRequest(itemId, "Quoted kits", 2m, 125m, 10m, 12m, "4000")]));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        string token;
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesQuotes.Where(quote => quote.Id == saved.Id).Select(quote => quote.ConcurrencyToken).SingleAsync();
        var originalToken = token;
        var revised = await transactions.SaveSalesQuoteAsync(new SaveSalesQuoteRequest(saved.Id, customerId, "QUO-LIFECYCLE-1", today.AddDays(-1), today.AddDays(30), "Revised customer-approved scope", [new SalesOrderLineRequest(itemId, "Quoted kits", 2m, 125m, 10m, 12m, "4000")], token)); Assert.True(revised.Succeeded, revised.ErrorMessage);
        var staleApproval = await transactions.ApproveSalesQuoteAsync(new(saved.Id!.Value, originalToken)); Assert.False(staleApproval.Succeeded); Assert.Contains("changed", staleApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesQuotes.Where(quote => quote.Id == saved.Id).Select(quote => quote.ConcurrencyToken).SingleAsync();
        var approved = await transactions.ApproveSalesQuoteAsync(new(saved.Id.Value, token)); Assert.True(approved.Succeeded, approved.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesQuotes.Where(quote => quote.Id == saved.Id).Select(quote => quote.ConcurrencyToken).SingleAsync();
        var expiredConversion = await transactions.ConvertSalesQuoteAsync(new(saved.Id.Value, "SO-QUOTE-EXPIRED", today.AddDays(31), null, "Expired attempt", token)); Assert.False(expiredConversion.Succeeded); Assert.Contains("expired", expiredConversion.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var converted = await transactions.ConvertSalesQuoteAsync(new(saved.Id.Value, "SO-FROM-QUOTE-1", today.AddDays(1), today.AddDays(4), "Accepted quote", token)); Assert.True(converted.Succeeded, converted.ErrorMessage);
        var duplicate = await transactions.ConvertSalesQuoteAsync(new(saved.Id.Value, "SO-FROM-QUOTE-2", today.AddDays(1), null, "Duplicate conversion", token)); Assert.False(duplicate.Succeeded);
        string orderToken; await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == converted.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var changedTerms = await transactions.SaveSalesOrderAsync(new SaveSalesOrderRequest(converted.Id, customerId, "SO-FROM-QUOTE-1", today.AddDays(1), today.AddDays(4), "Changed after acceptance", [new SalesOrderLineRequest(itemId, "Changed terms", 3m, 1m, 0m, 0m, "4000")], orderToken)); Assert.False(changedTerms.Succeeded); Assert.Contains("quote-derived", changedTerms.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True((await transactions.ApproveSalesOrderAsync(new(converted.Id!.Value, orderToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == converted.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var quoteAmendment = await transactions.AmendSalesOrderAsync(new(converted.Id.Value, today.AddDays(1), today.AddDays(4), "Changed after acceptance", "Attempt to bypass quote", [new SalesOrderLineRequest(itemId, "Changed terms", 3m, 1m, 0m, 0m, "4000")], orderToken)); Assert.False(quoteAmendment.Succeeded); Assert.Contains("Quote-derived", quoteAmendment.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        await using var after = await factory.CreateDbContextAsync();
        var quote = await after.SalesQuotes.SingleAsync(candidate => candidate.Id == saved.Id); var quoteLine = await after.SalesQuoteLines.SingleAsync(line => line.SalesQuoteId == quote.Id);
        var order = await after.SalesOrders.SingleAsync(candidate => candidate.Id == converted.Id); var orderLine = await after.SalesOrderLines.SingleAsync(line => line.SalesOrderId == order.Id);
        Assert.Equal("Converted", quote.Status); Assert.Equal(quote.Id, order.SalesQuoteId); Assert.Equal("Approved", order.Status); Assert.Equal(252m, order.TotalAmount);
        Assert.Equal(quoteLine.InventoryItemId, orderLine.InventoryItemId); Assert.Equal(quoteLine.RevenueAccountId, orderLine.RevenueAccountId); Assert.Equal(quoteLine.Quantity, orderLine.OrderedQuantity); Assert.Equal(quoteLine.UnitPrice, orderLine.UnitPrice); Assert.Equal(quoteLine.DiscountAmount, orderLine.DiscountAmount); Assert.Equal(quoteLine.TaxAmount, orderLine.TaxAmount); Assert.Equal(quoteLine.LineTotal, orderLine.LineTotal);
        Assert.Equal(quantityOnHand, await after.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync()); Assert.Equal(journalCount, await after.JournalEntries.CountAsync());
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "sales-quote.approved" && audit.EntityId == quote.Id);
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "sales-quote.converted" && audit.EntityId == quote.Id);
        Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "sales-order.created-from-quote" && audit.EntityId == order.Id);
    }

    [Fact]
    public async Task SalesOrderAmendment_ReleasesReservationsPreservesRevisionAndRequiresReapproval()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid customerId; Guid itemId; int journalCount; decimal quantityOnHand;
        await using (var db = await factory.CreateDbContextAsync()) { customerId = await db.Customers.Select(customer => customer.Id).FirstAsync(); var item = await db.InventoryItems.Where(candidate => candidate.Sku == "RM-220").SingleAsync(); itemId = item.Id; quantityOnHand = item.QuantityOnHand; journalCount = await db.JournalEntries.CountAsync(); }
        var saved = await transactions.SaveSalesOrderAsync(new SaveSalesOrderRequest(null, customerId, "SO-AMEND-1", new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 25), "Original terms", [new SalesOrderLineRequest(itemId, "Original fasteners", 2m, 20m, 0m, 2m, "4000")])); Assert.True(saved.Succeeded, saved.ErrorMessage);
        Guid originalLineId; string token;
        await using (var db = await factory.CreateDbContextAsync()) { var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); originalLineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Id).SingleAsync(); Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); }
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        Assert.True((await transactions.AllocateSalesOrderAsync(new(saved.Id!.Value, [new AllocateSalesOrderLineRequest(originalLineId, 2m)], token))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var request = new AmendSalesOrderRequest(saved.Id.Value, new DateOnly(2026, 8, 21), new DateOnly(2026, 8, 28), "Revised terms", "Customer increased quantity", [new SalesOrderLineRequest(itemId, "Revised fasteners", 3m, 21m, 3m, 3m, "4000")], token);
        Assert.False((await transactions.AmendSalesOrderAsync(request with { ConcurrencyToken = "stale" })).Succeeded);
        var amended = await transactions.AmendSalesOrderAsync(request); Assert.True(amended.Succeeded, amended.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); var line = await db.SalesOrderLines.SingleAsync(candidate => candidate.SalesOrderId == order.Id); var revision = await db.SalesOrderAmendments.SingleAsync(candidate => candidate.SalesOrderId == order.Id);
            Assert.Equal("Draft", order.Status); Assert.Null(order.ApprovedAtUtc); Assert.Equal(63m, order.TotalAmount); Assert.Equal(0m, line.AllocatedQuantity); Assert.Equal(3m, line.OrderedQuantity); Assert.NotEqual(originalLineId, line.Id);
            Assert.Equal(1, revision.RevisionNumber); Assert.Equal("Customer increased quantity", revision.Reason); Assert.Contains("Original terms", revision.BeforeJson); Assert.Contains("Revised terms", revision.AfterJson); token = order.ConcurrencyToken;
        }
        Assert.True((await transactions.ApproveSalesOrderAsync(new(saved.Id.Value, token))).Succeeded);
        await using var after = await factory.CreateDbContextAsync(); Assert.Equal(quantityOnHand, await after.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync()); Assert.Equal(journalCount, await after.JournalEntries.CountAsync()); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "sales-order.amended" && audit.EntityId == saved.Id);
    }

    [Fact]
    public async Task SalesOrderCancellation_ClosesUnshippedDemandAndProratesFinalShipmentInvoice()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid customerId; Guid itemId; decimal originalQuantity; await using (var db = await factory.CreateDbContextAsync()) { customerId = await db.Customers.Select(customer => customer.Id).FirstAsync(); var item = await db.InventoryItems.Where(candidate => candidate.Sku == "FG-200").SingleAsync(); itemId = item.Id; originalQuantity = item.QuantityOnHand; }
        var saved = await transactions.SaveSalesOrderAsync(new SaveSalesOrderRequest(null, customerId, "SO-CANCEL-1", new DateOnly(2026, 8, 20), null, "Partial cancellation", [new SalesOrderLineRequest(itemId, "Cancellation kits", 4m, 100m, 10m, 8m, "4000")])); Assert.True(saved.Succeeded, saved.ErrorMessage);
        Guid lineId; string token; await using (var db = await factory.CreateDbContextAsync()) { var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); lineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Id).SingleAsync(); Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); }
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); Assert.True((await transactions.AllocateSalesOrderAsync(new(saved.Id!.Value, [new AllocateSalesOrderLineRequest(lineId, 4m)], token))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); var shipped = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, "SHIP-CANCEL-1", new DateOnly(2026, 8, 21), [new ShipSalesOrderLineRequest(lineId, 2m)], token)); Assert.True(shipped.Succeeded, shipped.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        Assert.False((await transactions.CancelSalesOrderAsync(new(saved.Id.Value, "", token))).Succeeded);
        var cancelled = await transactions.CancelSalesOrderAsync(new(saved.Id.Value, "Customer cancelled the undelivered balance", token)); Assert.True(cancelled.Succeeded, cancelled.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); var line = await db.SalesOrderLines.SingleAsync(candidate => candidate.Id == lineId); Assert.Equal("ClosedPendingInvoice", order.Status); Assert.Equal(199m, order.TotalAmount); Assert.Equal(2m, line.CancelledQuantity); Assert.Equal(0m, line.AllocatedQuantity); Assert.NotNull(order.CancelledAtUtc); token = await db.InventoryShipments.Where(shipment => shipment.Id == shipped.Id).Select(shipment => shipment.ConcurrencyToken).SingleAsync();
        }
        var invoice = await transactions.InvoiceInventoryShipmentAsync(new(shipped.Id!.Value, "INV-CANCEL-1", new DateOnly(2026, 8, 22), new DateOnly(2026, 9, 21), "Invoice retained shipment", token)); Assert.True(invoice.Succeeded, invoice.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { var postedInvoice = await db.SalesInvoices.SingleAsync(candidate => candidate.Id == invoice.Id); Assert.Equal(195m, postedInvoice.Subtotal); Assert.Equal(4m, postedInvoice.TaxAmount); Assert.Equal(199m, postedInvoice.TotalAmount); Assert.Equal("Closed", await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.Status).SingleAsync()); }
        Assert.True((await transactions.VoidInvoiceAsync(new(invoice.Id!.Value, new DateOnly(2026, 8, 23), "Shipment never reached customer"))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.InventoryShipments.Where(shipment => shipment.Id == shipped.Id).Select(shipment => shipment.ConcurrencyToken).SingleAsync();
        Assert.True((await transactions.ReverseInventoryShipmentAsync(new(shipped.Id.Value, new DateOnly(2026, 8, 24), "Carrier returned undelivered goods", token))).Succeeded);
        await using var after = await factory.CreateDbContextAsync(); var finalOrder = await after.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); var finalLine = await after.SalesOrderLines.SingleAsync(candidate => candidate.Id == lineId); Assert.Equal("Cancelled", finalOrder.Status); Assert.Equal(0m, finalOrder.TotalAmount); Assert.Equal(4m, finalLine.CancelledQuantity); Assert.Equal(0m, finalLine.ShippedQuantity); Assert.Equal(originalQuantity, await after.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync()); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "sales-order.cancelled" && audit.EntityId == saved.Id);
    }

    [Fact]
    public async Task SalesOrderCancellation_CancelsDraftWithoutPostingAndRejectsReplay()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid customerId; Guid itemId; decimal quantityOnHand; int journalCount;
        await using (var db = await factory.CreateDbContextAsync()) { customerId = await db.Customers.Select(customer => customer.Id).FirstAsync(); var item = await db.InventoryItems.Where(candidate => candidate.Sku == "RM-220").SingleAsync(); itemId = item.Id; quantityOnHand = item.QuantityOnHand; journalCount = await db.JournalEntries.CountAsync(); }
        var saved = await transactions.SaveSalesOrderAsync(new SaveSalesOrderRequest(null, customerId, "SO-CANCEL-DRAFT-1", new DateOnly(2026, 8, 25), null, "Cancelled before approval", [new SalesOrderLineRequest(itemId, "Unneeded fasteners", 3m, 20m, 3m, 3m, "4000")])); Assert.True(saved.Succeeded, saved.ErrorMessage);
        string token; await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        Assert.False((await transactions.CancelSalesOrderAsync(new(saved.Id!.Value, "Stale cancellation", "stale-token"))).Succeeded);
        var cancelled = await transactions.CancelSalesOrderAsync(new(saved.Id.Value, "Customer withdrew before approval", token)); Assert.True(cancelled.Succeeded, cancelled.ErrorMessage);
        Assert.False((await transactions.CancelSalesOrderAsync(new(saved.Id.Value, "Replay", token))).Succeeded);
        await using var after = await factory.CreateDbContextAsync(); var order = await after.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); var line = await after.SalesOrderLines.SingleAsync(candidate => candidate.SalesOrderId == order.Id);
        Assert.Equal("Cancelled", order.Status); Assert.Equal(0m, order.TotalAmount); Assert.Equal(3m, line.CancelledQuantity); Assert.Equal(0m, line.AllocatedQuantity); Assert.Equal(quantityOnHand, await after.InventoryItems.Where(item => item.Id == itemId).Select(item => item.QuantityOnHand).SingleAsync()); Assert.Equal(journalCount, await after.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task SalesOrderCancellation_ReconcilesRetainedTotalToPreviouslyRoundedInvoices()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid customerId; Guid itemId; await using (var db = await factory.CreateDbContextAsync()) { customerId = await db.Customers.Select(customer => customer.Id).FirstAsync(); itemId = await db.InventoryItems.Where(item => item.Sku == "RM-220").Select(item => item.Id).SingleAsync(); }
        var saved = await transactions.SaveSalesOrderAsync(new SaveSalesOrderRequest(null, customerId, "SO-CANCEL-ROUND-1", new DateOnly(2026, 8, 20), null, "Rounding reconciliation", [new SalesOrderLineRequest(itemId, "Individually invoiced fasteners", 3m, 10m, 1m, 0m, "4000")])); Assert.True(saved.Succeeded, saved.ErrorMessage);
        Guid lineId; string orderToken; await using (var db = await factory.CreateDbContextAsync()) { var order = await db.SalesOrders.SingleAsync(candidate => candidate.Id == saved.Id); lineId = await db.SalesOrderLines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Id).SingleAsync(); Assert.True((await transactions.ApproveSalesOrderAsync(new(order.Id, order.ConcurrencyToken))).Succeeded); }
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync(); Assert.True((await transactions.AllocateSalesOrderAsync(new(saved.Id!.Value, [new AllocateSalesOrderLineRequest(lineId, 3m)], orderToken))).Succeeded);
        for (var shipmentSequence = 1; shipmentSequence <= 2; shipmentSequence++)
        {
            await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
            var shipped = await transactions.ShipSalesOrderAsync(new(saved.Id.Value, $"SHIP-CANCEL-ROUND-{shipmentSequence}", new DateOnly(2026, 8, 20 + shipmentSequence), [new ShipSalesOrderLineRequest(lineId, 1m)], orderToken)); Assert.True(shipped.Succeeded, shipped.ErrorMessage);
            string shipmentToken; await using (var db = await factory.CreateDbContextAsync()) shipmentToken = await db.InventoryShipments.Where(shipment => shipment.Id == shipped.Id).Select(shipment => shipment.ConcurrencyToken).SingleAsync();
            var invoiced = await transactions.InvoiceInventoryShipmentAsync(new(shipped.Id!.Value, $"INV-CANCEL-ROUND-{shipmentSequence}", new DateOnly(2026, 8, 20 + shipmentSequence), new DateOnly(2026, 9, 20 + shipmentSequence), "Individually rounded shipment", shipmentToken)); Assert.True(invoiced.Succeeded, invoiced.ErrorMessage);
        }
        await using (var db = await factory.CreateDbContextAsync()) orderToken = await db.SalesOrders.Where(order => order.Id == saved.Id).Select(order => order.ConcurrencyToken).SingleAsync();
        var cancelled = await transactions.CancelSalesOrderAsync(new(saved.Id.Value, "Customer cancelled the last unit", orderToken)); Assert.True(cancelled.Succeeded, cancelled.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync(); var orderAfterCancellation = await after.SalesOrders.SingleAsync(order => order.Id == saved.Id); var invoiceTotal = (await after.SalesInvoices.Where(invoice => invoice.SalesOrderId == saved.Id && invoice.Status != "Voided").Select(invoice => invoice.TotalAmount).ToListAsync()).Sum();
        Assert.Equal("Closed", orderAfterCancellation.Status); Assert.Equal(19.34m, invoiceTotal); Assert.Equal(invoiceTotal, orderAfterCancellation.TotalAmount);
    }

    [Fact]
    public async Task SalesQuote_EnforcesAuthorityCompanyIsolationExpiryAndWithdrawal()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(); var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid companyId; Guid customerId; Guid itemId; Guid foreignCustomerId; Guid foreignItemId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync(); customerId = await db.Customers.Select(customer => customer.Id).FirstAsync(); itemId = await db.InventoryItems.Where(item => item.Sku == "RM-220").Select(item => item.Id).SingleAsync();
            var foreignCompanyId = Guid.NewGuid(); foreignCustomerId = Guid.NewGuid(); foreignItemId = Guid.NewGuid();
            db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Quote foreign company", LegalName = "Quote foreign company", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
            db.Customers.Add(new Customer { Id = foreignCustomerId, CompanyId = foreignCompanyId, CustomerNumber = "Q-FOREIGN-C", Name = "Foreign quote customer" });
            db.InventoryItems.Add(new InventoryItem { Id = foreignItemId, CompanyId = foreignCompanyId, Sku = "Q-FOREIGN-I", Description = "Foreign quote item", UnitPrice = 1m, UnitCost = 1m, QuantityOnHand = 10m, IsActive = true });
            await db.SaveChangesAsync();
        }
        void ActAs(params string[] permissions) { var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()) }; claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission))); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        var request = new SaveSalesQuoteRequest(null, customerId, "QUO-CONTROL-1", today, today.AddDays(30), "Control test", [new SalesOrderLineRequest(itemId, "Fasteners", 2m, 20m, 0m, 0m, "4000")]);
        ActAs(BrassLedgerPermissions.FulfillmentManage); Assert.False((await transactions.SaveSalesQuoteAsync(request)).Succeeded);
        ActAs(BrassLedgerPermissions.SalesManage);
        Assert.False((await transactions.SaveSalesQuoteAsync(request with { CustomerId = foreignCustomerId, QuoteNumber = "QUO-FOREIGN-C" })).Succeeded);
        Assert.False((await transactions.SaveSalesQuoteAsync(request with { QuoteNumber = "QUO-FOREIGN-I", Lines = [new SalesOrderLineRequest(foreignItemId, "Foreign", 1m, 1m, 0m, 0m, "4000")] })).Succeeded);
        var expired = await transactions.SaveSalesQuoteAsync(request with { QuoteNumber = "QUO-EXPIRED", QuotedOn = new DateOnly(2020, 1, 1), ExpiresOn = new DateOnly(2020, 1, 31) }); Assert.True(expired.Succeeded, expired.ErrorMessage);
        string token; await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesQuotes.Where(quote => quote.Id == expired.Id).Select(quote => quote.ConcurrencyToken).SingleAsync();
        Assert.False((await transactions.ApproveSalesQuoteAsync(new(expired.Id!.Value, token))).Succeeded);
        var saved = await transactions.SaveSalesQuoteAsync(request); Assert.True(saved.Succeeded, saved.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesQuotes.Where(quote => quote.Id == saved.Id).Select(quote => quote.ConcurrencyToken).SingleAsync();
        Assert.True((await transactions.ApproveSalesQuoteAsync(new(saved.Id!.Value, token))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) token = await db.SalesQuotes.Where(quote => quote.Id == saved.Id).Select(quote => quote.ConcurrencyToken).SingleAsync();
        Assert.False((await transactions.WithdrawSalesQuoteAsync(new(saved.Id.Value, "", token))).Succeeded);
        var withdrawn = await transactions.WithdrawSalesQuoteAsync(new(saved.Id.Value, "Customer selected another proposal", token)); Assert.True(withdrawn.Succeeded, withdrawn.ErrorMessage);
        await using var after = await factory.CreateDbContextAsync(); var quote = await after.SalesQuotes.SingleAsync(candidate => candidate.Id == saved.Id); Assert.Equal("Withdrawn", quote.Status); Assert.Equal("Customer selected another proposal", quote.WithdrawalReason); Assert.Contains(await after.BusinessAuditEntries.ToListAsync(), audit => audit.Action == "sales-quote.withdrawn" && audit.EntityId == quote.Id);
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

        var result = await PostEmployeePayrollThroughWorkflowAsync(transactions, factory, request);
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
    public async Task PayrollWorkflow_EnforcesSeparationRejectsAndPreservesEncryptedCorrections()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var setupDb = await factory.CreateDbContextAsync();
        var companyId = await setupDb.Companies.Select(company => company.Id).FirstAsync();
        var employeeId = await setupDb.Employees.Where(employee => employee.CompanyId == companyId && employee.IsActive).Select(employee => employee.Id).FirstAsync();
        var bankId = await setupDb.BankAccounts.Where(bank => bank.CompanyId == companyId).Select(bank => bank.Id).FirstAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        void ActAs(Guid userId, params string[] permissions)
        {
            var claims = new List<System.Security.Claims.Claim>
            {
                new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())
            };
            claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }
        async Task<PayrollRun> LoadRunAsync(Guid id)
        {
            await using var db = await factory.CreateDbContextAsync();
            return await db.PayrollRuns.AsNoTracking().SingleAsync(run => run.Id == id);
        }

        var preparerId = Guid.NewGuid(); var reviewerId = Guid.NewGuid(); var posterId = Guid.NewGuid();
        ActAs(preparerId, BrassLedgerPermissions.PayrollPrepare);
        var saved = await transactions.SaveEmployeePayrollRunDraftAsync(new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 19), "PR-SOD-CORRECT-1", [new EmployeePayrollInput(employeeId, 1_000m)]));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var run = await LoadRunAsync(saved.Id!.Value);

        ActAs(preparerId, BrassLedgerPermissions.PayrollApprove);
        var selfApproval = await transactions.ApprovePayrollRunAsync(new(run.Id, run.ConcurrencyToken));
        Assert.False(selfApproval.Succeeded); Assert.Contains("prepared", selfApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var selfRejection = await transactions.RejectPayrollRunAsync(new(run.Id, "I prepared this run.", run.ConcurrencyToken));
        Assert.False(selfRejection.Succeeded); Assert.Contains("prepared", selfRejection.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        ActAs(reviewerId, BrassLedgerPermissions.PayrollApprove);
        var staleRejection = await transactions.RejectPayrollRunAsync(new(run.Id, "Correct gross pay.", "stale-token"));
        Assert.False(staleRejection.Succeeded); Assert.Contains("changed", staleRejection.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var rejected = await transactions.RejectPayrollRunAsync(new(run.Id, "Correct gross pay and resubmit.", run.ConcurrencyToken));
        Assert.True(rejected.Succeeded, rejected.ErrorMessage);

        ActAs(preparerId, BrassLedgerPermissions.PayrollPrepare);
        var correction = await transactions.GetEmployeePayrollRunDraftAsync(run.Id);
        Assert.NotNull(correction); Assert.Equal("Correct gross pay and resubmit.", (await LoadRunAsync(run.Id)).RejectionReason);
        var correctedEmployee = correction!.Employees.Single();
        var revised = await transactions.SaveEmployeePayrollRunDraftAsync(correction with { Employees = [correctedEmployee with { GrossPay = 1_200m, Earnings = correctedEmployee.Earnings?.Select(earning => earning with { Amount = 1_200m }).ToArray() }] });
        Assert.True(revised.Succeeded, revised.ErrorMessage); Assert.Equal(run.Id, revised.Id);
        run = await LoadRunAsync(run.Id);
        Assert.Equal(("Draft", 1_200m), (run.Status, run.GrossPayroll));

        await using (var verification = await factory.CreateDbContextAsync())
        {
            var revision = await verification.PayrollRunRevisions.SingleAsync(item => item.PayrollRunId == run.Id);
            Assert.Equal((1, "Rejected", "Correct gross pay and resubmit."), (revision.RevisionNumber, revision.StatusBeforeRevision, revision.Reason));
            Assert.Contains("\"GrossPayroll\":1000", revision.PayloadJson, StringComparison.Ordinal);
            await verification.Database.OpenConnectionAsync();
            await using var command = verification.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT PayloadJson FROM PayrollRunRevisions WHERE Id = $id";
            var parameter = command.CreateParameter(); parameter.ParameterName = "$id"; parameter.Value = revision.Id; command.Parameters.Add(parameter);
            Assert.StartsWith("enc::", (await command.ExecuteScalarAsync())?.ToString());
            var actions = await verification.BusinessAuditEntries.Where(entry => entry.EntityId == run.Id).Select(entry => entry.Action).ToListAsync();
            Assert.Contains("payroll-run.rejected", actions); Assert.Contains("payroll-run.revised", actions);
        }

        ActAs(reviewerId, BrassLedgerPermissions.PayrollApprove);
        Assert.True((await transactions.ApprovePayrollRunAsync(new(run.Id, run.ConcurrencyToken))).Succeeded);
        run = await LoadRunAsync(run.Id);
        ActAs(reviewerId, BrassLedgerPermissions.PayrollPost);
        var selfPosting = await transactions.PostApprovedPayrollRunAsync(new(run.Id, run.ConcurrencyToken));
        Assert.False(selfPosting.Succeeded); Assert.Contains("approved", selfPosting.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ActAs(posterId, BrassLedgerPermissions.PayrollPost);
        var posted = await transactions.PostApprovedPayrollRunAsync(new(run.Id, run.ConcurrencyToken));
        Assert.True(posted.Succeeded, posted.ErrorMessage);
    }

    [Fact]
    public async Task PayrollFilings_ReconcileProtectDataDetectSourceChangesAndLockClosedPeriods()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var filings = scope.ServiceProvider.GetRequiredService<IPayrollFilingService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.First();
        var bankId = workspace.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010").Id;
        var protectedDetails = await transactions.SaveEmployeeEmploymentDetailsAsync(new SaveEmployeeEmploymentDetailsRequest(employee.Id, "1 Main St", "", "85001", "Maricopa", "", "Maricopa", "", new DateOnly(2024, 1, 1), null, 25m, 37.5m, false, "", "123-45-6789", "", "", ConcurrencyToken: employee.ConcurrencyToken, AddressCity: "Phoenix", AddressState: "AZ"));
        Assert.True(protectedDetails.Succeeded, protectedDetails.ErrorMessage);

        var firstRun = await PostEmployeePayrollThroughWorkflowAsync(transactions, factory, new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 4, 10), "FILING-Q2-1", [new EmployeePayrollInput(employee.Id, 1_000m,
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

        var changedSource = await PostEmployeePayrollThroughWorkflowAsync(transactions, factory, new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 5, 8), "FILING-Q2-2", [new EmployeePayrollInput(employee.Id, 750m)], new DateOnly(2026, 4, 26), new DateOnly(2026, 5, 2)));
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
        var additionalCorrectionRun = await PostEmployeePayrollThroughWorkflowAsync(transactions, factory, new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 6, 12), "FILING-Q2-CORRECTION-2", [new EmployeePayrollInput(employee.Id, 2_000m,
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
        var posting = await PostEmployeePayrollThroughWorkflowAsync(transactions, factory, new PostEmployeePayrollRunRequest(workspace.Treasury.BankAccounts.First().Id, new DateOnly(2026, 5, 15), "PACKAGE-POST", [new EmployeePayrollInput(employee.Id, 1_000m)]));
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
        var packagePath = TestRepositoryPaths.TaxContent("us/ny/2026-runtime-package.json");
        var import = await taxAdministration.ImportTaxContentDocumentAsync(await File.ReadAllTextAsync(packagePath));
        Assert.True(import.Succeeded, import.ErrorMessage);
        var activation = await taxAdministration.ActivateContentPackageAsync(import.SavedId!.Value);
        Assert.True(activation.Succeeded, activation.ErrorMessage);

        var workspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.First();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
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
        var operatorAttempt = await administration.InviteOperatorAsync(new BrassLedger.Application.Security.CreateOperatorInvitationRequest("role-only", "Role Only", "role@example.test", "Controller"));
        Assert.False(operatorAttempt.Succeeded);
        Assert.Contains("not authorized", operatorAttempt.ErrorMessage);

        var restrictedAdministrator = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        restrictedAdministrator.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Administrator"),
            new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, "0e561f1b-47b0-4c33-bd9f-1a3298ed29c6"),
            new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")
        ], "test"));
        accessor.HttpContext = restrictedAdministrator;
        var restrictedAttempt = await administration.CreateRoleAsync(new BrassLedger.Application.Security.CreateAccessRoleRequest(
            "Restricted escalation", "Must not bypass MFA", [BrassLedgerPermissions.LedgerManage]));
        Assert.False(restrictedAttempt.Succeeded);
        Assert.Contains("not authorized", restrictedAttempt.ErrorMessage);
    }

    [Fact]
    public async Task SecurityAdministration_RequiresConfiguredSecureDeliveryForNewOperators()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<BrassLedger.Application.Security.ISecurityAdministrationService>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.UserManage);

        var result = await administration.InviteOperatorAsync(new BrassLedger.Application.Security.CreateOperatorInvitationRequest(
            "new-operator",
            "New Operator",
            "new-operator@example.test",
            "Controller"));

        Assert.False(result.Succeeded);
        Assert.Contains("security-email delivery", result.ErrorMessage);
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        Assert.False(await dbContext.Users.AnyAsync(candidate => candidate.UserName == "new-operator"));
    }

    [Fact]
    public async Task SecurityAdministration_EnforcesAndAuditsConfigurableRoleMfa()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<BrassLedger.Application.Security.ISecurityAdministrationService>();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.RoleManage);

        var initialSnapshot = await administration.GetSnapshotAsync();
        Assert.True(initialSnapshot.Roles.Single(role => role.Name == "Administrator").RequiresMfa);
        Assert.False(initialSnapshot.Roles.Single(role => role.Name == "Controller").RequiresMfa);
        var created = await administration.CreateRoleAsync(new BrassLedger.Application.Security.CreateAccessRoleRequest(
            "Protected reviewer", "Requires a second factor", [BrassLedgerPermissions.ReportingManage], true));
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.True((await administration.GetSnapshotAsync()).Roles.Single(role => role.Name == "Protected reviewer").RequiresMfa);

        var enabled = await administration.SetRoleMfaRequirementAsync("Controller", true);
        Assert.True(enabled.Succeeded, enabled.ErrorMessage);
        var restricted = await authentication.AuthenticateAsync(
            "controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "role-mfa-test");
        Assert.Equal(AuthenticationOutcome.Succeeded, restricted.Outcome);
        Assert.True(restricted.User!.MfaEnrollmentRequired);
        Assert.Empty(restricted.User.Permissions);

        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        Assert.Contains(await dbContext.BusinessAuditEntries.ToListAsync(), entry => entry.Action == "security.role.mfa-requirement-changed" && entry.EntityType == "AccessRole");
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

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(
            null, new DateOnly(2026, 5, 1), "NO-COMPANY", "Must fail closed", [new JournalLineRequest("1000", 1m, 0m, "Debit"), new JournalLineRequest("4000", 0m, 1m, "Credit")])));
    }

    [Fact]
    public async Task TransactionService_PostsBillPaymentPayrollAndReconciliation()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var before = await workspaceService.GetWorkspaceAsync();
        var vendor = before.Payables.Vendors.First();
        var operatingBank = before.Treasury.BankAccounts.First();
        var payrollBank = before.Treasury.BankAccounts.Last();

        var billResult = await PostVendorBillThroughWorkflowAsync(transactions, new CreateVendorBillRequest(
            vendor.Id, "B-TEST-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m, "5100", "Workflow bill"));
        Assert.True(billResult.Succeeded, billResult.ErrorMessage);
        var paymentResult = await transactions.ApplyBillPaymentAsync(new ApplyBillPaymentRequest(
            billResult.Id!.Value, operatingBank.Id, new DateOnly(2026, 5, 2), 100m, "CHK-TEST-1"));
        Assert.True(paymentResult.Succeeded, paymentResult.ErrorMessage);

        var payrollEmployee = before.Payroll.Employees.First();
        var payrollRequest = new PostEmployeePayrollRunRequest(payrollBank.Id, new DateOnly(2026, 5, 3), "PAY-TEST-1", [new EmployeePayrollInput(payrollEmployee.Id, 250m)]);
        var payrollPreview = await transactions.PreviewEmployeePayrollRunAsync(payrollRequest);
        Assert.NotNull(payrollPreview);
        var payrollResult = await PostEmployeePayrollThroughWorkflowAsync(transactions, dbContextFactory, payrollRequest);
        Assert.True(payrollResult.Succeeded, payrollResult.ErrorMessage);

        var beforeReconciliation = await workspaceService.GetWorkspaceAsync();
        var reconcileBank = beforeReconciliation.Treasury.BankAccounts.First();
        var reconciliationResult = await transactions.ReconcileBankAccountAsync(new ReconcileBankAccountRequest(
            reconcileBank.Id, new DateOnly(2026, 5, 31), reconcileBank.CurrentBalance));
        Assert.True(reconciliationResult.Succeeded, reconciliationResult.ErrorMessage);

        var after = await workspaceService.GetWorkspaceAsync();
        Assert.Equal(before.Payables.OpenBalance, after.Payables.OpenBalance);
        Assert.Equal(0m, after.Treasury.BankAccounts.Single(bank => bank.Id == reconcileBank.Id).UnreconciledAmount);
        var payrollLiabilityTotal = payrollPreview!.PreTaxDeductions + payrollPreview.EmployeeWithholdings + payrollPreview.PostTaxDeductions + payrollPreview.EmployerPayrollTaxes + payrollPreview.EmployerBenefitContributions;
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "2200").Balance + payrollLiabilityTotal, after.GeneralLedger.Accounts.Single(account => account.Number == "2200").Balance);
        Assert.Contains(after.GeneralLedger.RecentEntries, entry => entry.Description == "Vendor payment");
        Assert.Contains(after.GeneralLedger.RecentEntries, entry => entry.SourceModule == "Payroll" && entry.TotalAmount == payrollPreview.GrossPayroll + payrollPreview.EmployerPayrollTaxes + payrollPreview.EmployerBenefitContributions);
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
        var invoice = await PostInvoiceThroughWorkflowAsync(transactions, new CreateInvoiceRequest(customer.Id, "INV-BANK-WF-1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 100m, 0m, "4000", "Bank match"));
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
        var bill = await PostVendorBillThroughWorkflowAsync(transactions, new CreateVendorBillRequest(initial.Payables.Vendors.First().Id, "B-REC-1", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), 40m, "5100", "Reconciliation test bill"));
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
        var payroll = await PostEmployeePayrollThroughWorkflowAsync(transactions, factory, new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 8, 28), "PR-ACH-1", [new EmployeePayrollInput(employee.Id, 1000m)], new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 28)));
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

    [Fact]
    public async Task PayrollPosting_AllocatesGrossAndEmployerBurdenAcrossProjectsAndReversesDimensions()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var workspace = await workspaceService.GetWorkspaceAsync();
        var employee = workspace.Payroll.Employees.Single(candidate => candidate.State == "AZ");
        var bankId = workspace.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010").Id;
        var projectIds = workspace.Projects.Jobs.Where(project => project.Status == "Active").Take(2).Select(project => project.Id).ToArray();
        Assert.Equal(2, projectIds.Length);
        var request = new PostEmployeePayrollRunRequest(bankId, new DateOnly(2026, 8, 28), "PR-PROJECT-ALLOC-1",
        [
            new EmployeePayrollInput(employee.Id, 0m,
            [
                new PayrollEarningInput("REG-A", "Regular", 20m, 25m, 500m, ProjectJobId: projectIds[0]),
                new PayrollEarningInput("REG-B", "Regular", 20m, 25m, 500m, ProjectJobId: projectIds[1])
            ])
        ]);
        var posted = await PostEmployeePayrollThroughWorkflowAsync(transactions, factory, request);
        Assert.True(posted.Succeeded, posted.ErrorMessage);
        PayrollRun run;
        await using (var db = await factory.CreateDbContextAsync())
        {
            run = await db.PayrollRuns.SingleAsync(candidate => candidate.Id == posted.Id);
            var expenseAccountId = await db.Accounts.Where(account => account.OperationalRole == AccountingAccountRoles.PayrollExpense).Select(account => account.Id).SingleAsync();
            var expenseLines = await db.JournalEntryLines.Where(line => line.JournalEntryId == run.JournalEntryId && line.AccountId == expenseAccountId).OrderBy(line => line.ProjectJobId).ToListAsync();
            Assert.Equal(2, expenseLines.Count);
            Assert.Equal(projectIds.Order(), expenseLines.Select(line => line.ProjectJobId!.Value).Order());
            Assert.Equal(run.GrossPayroll + run.EmployerPayrollTaxes + run.EmployerBenefitContributions, expenseLines.Sum(line => line.Debit));
            Assert.InRange(decimal.Abs(expenseLines[0].Debit - expenseLines[1].Debit), 0m, 0.01m);
        }
        var afterPosting = await workspaceService.GetWorkspaceAsync();
        Assert.All(projectIds, projectId => Assert.True(afterPosting.Projects.Jobs.Single(project => project.Id == projectId).ActualCost > 500m));
        var reversed = await transactions.ReversePayrollRunAsync(new(run.Id, new DateOnly(2026, 8, 29), "Project payroll correction", run.ConcurrencyToken));
        Assert.True(reversed.Succeeded, reversed.ErrorMessage);
        var afterReversal = await workspaceService.GetWorkspaceAsync();
        Assert.All(projectIds, projectId => Assert.Equal(0m, afterReversal.Projects.Jobs.Single(project => project.Id == projectId).ActualCost));
    }

    [Fact]
    public async Task ProjectAccounting_EnforcesCompanyConcurrencyLifecycleAndDerivesLedgerAmounts()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid customerId; Guid foreignCustomerId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            customerId = await db.Customers.OrderBy(customer => customer.CustomerNumber).Select(customer => customer.Id).FirstAsync();
            var foreignCompanyId = Guid.NewGuid(); foreignCustomerId = Guid.NewGuid();
            db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Foreign project company", LegalName = "Foreign project company", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
            db.Customers.Add(new Customer { Id = foreignCustomerId, CompanyId = foreignCompanyId, CustomerNumber = "FOREIGN-PROJECT", Name = "Foreign project customer" });
            await db.SaveChangesAsync();
        }
        var claims = new List<System.Security.Claims.Claim> { new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()) };
        claims.AddRange(new[] { BrassLedgerPermissions.ProjectsManage, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost }.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
        accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };

        var foreign = await transactions.SaveProjectJobAsync(new(null, "JOB-FOREIGN", "Must remain isolated", foreignCustomerId, new DateOnly(2026, 8, 1), null, "TimeAndMaterials", 1_000m, 800m, 0m));
        Assert.False(foreign.Succeeded);
        Assert.Contains("customer", foreign.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var created = await transactions.SaveProjectJobAsync(new(null, "JOB-LEDGER-1", "Ledger-derived project", customerId, new DateOnly(2026, 8, 1), new DateOnly(2027, 1, 31), "FixedPrice", 5_000m, 3_000m, 0.1m));
        Assert.True(created.Succeeded, created.ErrorMessage);
        ProjectJob project;
        await using (var db = await factory.CreateDbContextAsync()) project = await db.ProjectJobs.SingleAsync(item => item.Id == created.Id);
        var originalToken = project.ConcurrencyToken;
        var updated = await transactions.SaveProjectJobAsync(new(project.Id, project.JobNumber, "Ledger-derived project updated", customerId, project.StartDate!.Value, project.ExpectedEndDate, project.BillingMethod, project.ContractAmount, 3_200m, project.RetainagePercent, originalToken));
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        var stale = await transactions.SaveProjectJobAsync(new(project.Id, project.JobNumber, "Stale update", customerId, project.StartDate.Value, project.ExpectedEndDate, project.BillingMethod, project.ContractAmount, 9_999m, project.RetainagePercent, originalToken));
        Assert.False(stale.Succeeded);
        Assert.Contains("changed", stale.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var draft = await transactions.SaveJournalEntryDraftAsync(new SaveJournalEntryDraftRequest(null, new DateOnly(2026, 8, 15), "PROJECT-LEDGER-1", "Project cost and earned revenue", [new JournalLineRequest("5100", 125m, 0m, "Project materials", project.Id), new JournalLineRequest("4000", 0m, 125m, "Project revenue", project.Id)]));
        Assert.True(draft.Succeeded, draft.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) project = await db.ProjectJobs.SingleAsync(item => item.Id == project.Id);
        var blockedClose = await transactions.CloseProjectJobAsync(new(project.Id, new DateOnly(2026, 8, 31), "Draft journal still needs review", project.ConcurrencyToken));
        Assert.False(blockedClose.Succeeded);
        Assert.Contains("open journals", blockedClose.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var approved = await transactions.ApproveJournalEntryAsync(draft.Id!.Value);
        Assert.True(approved.Succeeded, approved.ErrorMessage);
        var posted = await transactions.PostApprovedJournalEntryAsync(draft.Id.Value);
        Assert.True(posted.Succeeded, posted.ErrorMessage);
        var workspace = await workspaceService.GetWorkspaceAsync();
        var snapshot = workspace.Projects.Jobs.Single(item => item.Id == project.Id);
        Assert.Equal(125m, snapshot.ActualCost);
        Assert.Equal(125m, snapshot.Revenue);
        Assert.Equal(3_200m, snapshot.BudgetAmount);
        Assert.Equal(2, (workspace.Projects.LedgerLines ?? []).Count(line => line.ProjectJobId == project.Id));

        await using (var db = await factory.CreateDbContextAsync()) project = await db.ProjectJobs.SingleAsync(item => item.Id == project.Id);
        var closed = await transactions.CloseProjectJobAsync(new(project.Id, new DateOnly(2026, 8, 31), "Work completed and commitments cleared", project.ConcurrencyToken));
        Assert.True(closed.Succeeded, closed.ErrorMessage);
        var closedPosting = await transactions.SaveJournalEntryDraftAsync(new(null, new DateOnly(2026, 9, 1), "PROJECT-CLOSED", "Must reject closed project", [new JournalLineRequest("5100", 1m, 0m, "Closed cost", project.Id), new JournalLineRequest("4000", 0m, 1m, "Offset", project.Id)]));
        Assert.False(closedPosting.Succeeded);
        Assert.Contains("closed", closedPosting.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync()) project = await db.ProjectJobs.SingleAsync(item => item.Id == project.Id);
        var reopened = await transactions.ReopenProjectJobAsync(new(project.Id, "Approved follow-up work", project.ConcurrencyToken));
        Assert.True(reopened.Succeeded, reopened.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal("Active", (await db.ProjectJobs.SingleAsync(item => item.Id == project.Id)).Status);
            Assert.Contains(await db.BusinessAuditEntries.Where(entry => entry.EntityId == project.Id).ToListAsync(), entry => entry.Action == "project.created");
            Assert.Contains(await db.BusinessAuditEntries.Where(entry => entry.EntityId == project.Id).ToListAsync(), entry => entry.Action == "project.closed");
            Assert.Contains(await db.BusinessAuditEntries.Where(entry => entry.EntityId == project.Id).ToListAsync(), entry => entry.Action == "project.reopened");
        }
    }

    [Fact]
    public async Task ProjectDimensions_EnforceHierarchyCompanyBudgetAndConcurrencyControls()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var interchange = scope.ServiceProvider.GetRequiredService<IAccountingInterchangeService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; ProjectJob project; Guid foreignProjectId; Guid inventoryItemId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(x => x.Id).SingleAsync();
            project = await db.ProjectJobs.Where(x => x.Status == "Active" && x.BudgetAmount > 0m).OrderBy(x => x.JobNumber).FirstAsync();
            inventoryItemId = await db.InventoryItems.Where(x => x.IsActive).OrderBy(x => x.Sku).Select(x => x.Id).FirstAsync();
            var foreignCompanyId = Guid.NewGuid(); foreignProjectId = Guid.NewGuid();
            db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Foreign dimensions", LegalName = "Foreign dimensions", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
            db.ProjectJobs.Add(new ProjectJob { Id = foreignProjectId, CompanyId = foreignCompanyId, JobNumber = "FOREIGN-DIM", Name = "Invisible project", Status = "Active", BillingMethod = "Internal", RevenueRecognitionMethod = "AsBilled", BudgetAmount = 1_000m, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var userId = Guid.NewGuid();
        accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
                new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectsManage),
                new(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.JournalPrepare),
                new(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.RequisitionManage)
            ], "test"))
        };

        var foreign = await transactions.SaveProjectPhaseAsync(new(null, foreignProjectId, null, "X", "Cross-company phase", "Phase", "", null, null));
        Assert.False(foreign.Succeeded);
        Assert.Contains("not found", foreign.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var parentResult = await transactions.SaveProjectPhaseAsync(new(null, project.Id, null, " 01 ", "Foundation", "Phase", "Primary scope", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));
        Assert.True(parentResult.Succeeded, parentResult.ErrorMessage);
        var childResult = await transactions.SaveProjectPhaseAsync(new(null, project.Id, parentResult.Id, "01.10", "Excavation", "Task", "", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        Assert.True(childResult.Succeeded, childResult.ErrorMessage);
        var childOutsideParent = await transactions.SaveProjectPhaseAsync(new(null, project.Id, parentResult.Id, "01.20", "Out-of-range task", "Task", "", new DateOnly(2025, 12, 31), new DateOnly(2026, 4, 1)));
        Assert.False(childOutsideParent.Succeeded);
        Assert.Contains("parent", childOutsideParent.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var duplicatePhase = await transactions.SaveProjectPhaseAsync(new(null, project.Id, null, "01", "Duplicate", "Phase", "", null, null));
        Assert.False(duplicatePhase.Succeeded);

        ProjectPhase parent; ProjectPhase child;
        await using (var db = await factory.CreateDbContextAsync())
        {
            parent = await db.ProjectPhases.SingleAsync(x => x.Id == parentResult.Id);
            child = await db.ProjectPhases.SingleAsync(x => x.Id == childResult.Id);
        }
        var cycle = await transactions.SaveProjectPhaseAsync(new(parent.Id, project.Id, child.Id, parent.Code, parent.Name, parent.Kind, parent.Description, parent.StartsOn, parent.EndsOn, true, parent.ConcurrencyToken));
        Assert.False(cycle.Succeeded);
        Assert.Contains("cycle", cycle.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var parentInsideChild = await transactions.SaveProjectPhaseAsync(new(parent.Id, project.Id, null, parent.Code, parent.Name, parent.Kind, parent.Description, new DateOnly(2026, 1, 15), parent.EndsOn, true, parent.ConcurrencyToken));
        Assert.False(parentInsideChild.Succeeded);
        Assert.Contains("child", parentInsideChild.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var activeChildBlock = await transactions.SaveProjectPhaseAsync(new(parent.Id, project.Id, null, parent.Code, parent.Name, parent.Kind, parent.Description, parent.StartsOn, parent.EndsOn, false, parent.ConcurrencyToken));
        Assert.False(activeChildBlock.Succeeded);
        Assert.Contains("child", activeChildBlock.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var costCodeResult = await transactions.SaveProjectCostCodeAsync(new(null, " lab ", "Labor", "Direct cost", "Reusable labor code"));
        Assert.True(costCodeResult.Succeeded, costCodeResult.ErrorMessage);
        var duplicateCode = await transactions.SaveProjectCostCodeAsync(new(null, "LAB", "Duplicate", "", ""));
        Assert.False(duplicateCode.Succeeded);

        var amount = Math.Min(project.BudgetAmount, 100m);
        var allocationResult = await transactions.SaveProjectBudgetAllocationAsync(new(null, project.Id, child.Id, costCodeResult.Id, "5100", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), amount, amount + 25m, "Forecast preserves a visible overrun"));
        Assert.True(allocationResult.Succeeded, allocationResult.ErrorMessage);
        var duplicateAllocation = await transactions.SaveProjectBudgetAllocationAsync(new(null, project.Id, child.Id, costCodeResult.Id, "5100", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), amount, amount, "Duplicate key"));
        Assert.False(duplicateAllocation.Succeeded);
        var revenueAccount = await transactions.SaveProjectBudgetAllocationAsync(new(null, project.Id, null, null, "4000", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), 1m, 1m, "Wrong account type"));
        Assert.False(revenueAccount.Succeeded);
        Assert.Contains("expense", revenueAccount.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var overBudget = await transactions.SaveProjectBudgetAllocationAsync(new(null, project.Id, null, null, "5100", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), project.BudgetAmount, project.BudgetAmount, "Must exceed total"));
        Assert.False(overBudget.Succeeded);
        Assert.Contains("exceed", overBudget.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        ProjectBudgetAllocation allocation;
        await using (var db = await factory.CreateDbContextAsync()) allocation = await db.ProjectBudgetAllocations.SingleAsync(x => x.Id == allocationResult.Id);
        var updated = await transactions.SaveProjectBudgetAllocationAsync(new(allocation.Id, project.Id, child.Id, costCodeResult.Id, "5100", allocation.PeriodStart, allocation.PeriodEnd, allocation.BudgetAmount, allocation.ForecastAmount + 10m, "Updated forecast", allocation.ConcurrencyToken));
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        var stale = await transactions.SaveProjectBudgetAllocationAsync(new(allocation.Id, project.Id, child.Id, costCodeResult.Id, "5100", allocation.PeriodStart, allocation.PeriodEnd, allocation.BudgetAmount, allocation.ForecastAmount, "Stale update", allocation.ConcurrencyToken));
        Assert.False(stale.Succeeded);
        Assert.Contains("changed", stale.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var remainingBudget = project.BudgetAmount - amount;
        Assert.True(remainingBudget > 0m);
        var concurrentAmount = Math.Round(remainingBudget * 0.75m, 2, MidpointRounding.AwayFromZero);
        using var firstAllocationScope = services.CreateScope();
        using var secondAllocationScope = services.CreateScope();
        var concurrentAllocations = await Task.WhenAll(
            firstAllocationScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().SaveProjectBudgetAllocationAsync(new(null, project.Id, null, costCodeResult.Id, "5100", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), concurrentAmount, concurrentAmount, "Concurrent allocation A")),
            secondAllocationScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>().SaveProjectBudgetAllocationAsync(new(null, project.Id, null, costCodeResult.Id, "5100", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), concurrentAmount, concurrentAmount, "Concurrent allocation B")));
        Assert.Single(concurrentAllocations, result => result.Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
            Assert.True((await db.ProjectBudgetAllocations.Where(x => x.ProjectJobId == project.Id).Select(x => x.BudgetAmount).ToListAsync()).Sum() <= project.BudgetAmount);

        var workspace = await workspaceService.GetWorkspaceAsync();
        Assert.Contains(workspace.Projects.Phases ?? [], x => x.Id == child.Id && x.ParentProjectPhaseId == parent.Id && x.Kind == "Task");
        Assert.Contains(workspace.Projects.CostCodes ?? [], x => x.Id == costCodeResult.Id && x.Code == "LAB");
        Assert.Contains(workspace.Projects.BudgetAllocations ?? [], x => x.Id == allocation.Id && x.ProjectPhaseCode == "01.10" && x.ProjectCostCode == "LAB" && x.ForecastAmount == amount + 35m);

        var dimensionlessProject = await transactions.SaveJournalEntryDraftAsync(new(null, new DateOnly(2026, 1, 15), "DIM-NO-PROJECT", "Dimensions require their project", [new JournalLineRequest("5100", 1m, 0m, "Invalid", null, child.Id, costCodeResult.Id), new JournalLineRequest("4000", 0m, 1m, "Offset")]));
        Assert.False(dimensionlessProject.Succeeded);
        Assert.Contains("project", dimensionlessProject.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var requisitionResult = await transactions.SavePurchaseRequisitionAsync(new(
            null,
            null,
            "REQ-PROJECT-DIMENSIONS",
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 1, 31),
            "Retain project dimensions from requisition entry",
            [new(inventoryItemId, "Project material", 2m, 25m, project.Id, child.Id, costCodeResult.Id)]));
        Assert.True(requisitionResult.Succeeded, requisitionResult.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var requisitionLine = await db.PurchaseRequisitionLines.SingleAsync(x => x.PurchaseRequisitionId == requisitionResult.Id);
            Assert.Equal(project.Id, requisitionLine.ProjectJobId);
            Assert.Equal(child.Id, requisitionLine.ProjectPhaseId);
            Assert.Equal(costCodeResult.Id, requisitionLine.ProjectCostCodeId);
        }
        var requisitionWorkspace = await workspaceService.GetWorkspaceAsync();
        var requisitionSnapshot = Assert.Single(requisitionWorkspace.Operations.PurchaseRequisitions!, x => x.Id == requisitionResult.Id);
        var requisitionLineSnapshot = Assert.Single(requisitionSnapshot.Lines);
        Assert.Equal(project.Id, requisitionLineSnapshot.ProjectJobId);
        Assert.Equal(child.Id, requisitionLineSnapshot.ProjectPhaseId);
        Assert.Equal(costCodeResult.Id, requisitionLineSnapshot.ProjectCostCodeId);

        const string importHeader = "Journal No.,Journal Date,Reference,Journal/Description,Account Name,Debits,Credits,Line Description,Project / Job,Project Phase,Cost Code\r\n";
        var importCsv = importHeader
            + $"DIM-IMPORT,2026-01-15,DIM-REF,Dimension import,5100,10,0,Imported project cost,{project.JobNumber},{child.Code},LAB\r\n"
            + $"DIM-IMPORT,2026-01-15,DIM-REF,Dimension import,4000,0,10,Imported project revenue,{project.JobNumber},{child.Code},LAB\r\n";
        await using (var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(importCsv)))
        {
            var imported = await interchange.ImportQuickBooksOnlineCsvAsync("journal-entries", content, new(false, "project-dimensions.csv"));
            Assert.True(imported.Succeeded, string.Join("; ", imported.Errors));
            Assert.Equal(1, imported.ImportedCount);
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var importedEntry = await db.JournalEntries.SingleAsync(x => x.CompanyId == companyId && x.Description.Contains("QuickBooks journal DIM-IMPORT"));
            var importedLines = await db.JournalEntryLines.Where(x => x.JournalEntryId == importedEntry.Id).ToListAsync();
            Assert.All(importedLines, line => { Assert.Equal(project.Id, line.ProjectJobId); Assert.Equal(child.Id, line.ProjectPhaseId); Assert.Equal(costCodeResult.Id, line.ProjectCostCodeId); });
            importedEntry.Status = "Posted";
            importedEntry.IsPosted = true;
            await db.SaveChangesAsync();
        }
        var exported = await interchange.ExportQuickBooksOnlineCsvAsync("journal-entries");
        Assert.NotNull(exported);
        var exportedCsv = System.Text.Encoding.UTF8.GetString(exported.Content);
        Assert.Contains("\"Project Phase\",\"Cost Code\"", exportedCsv, StringComparison.Ordinal);
        Assert.Contains($"\"{child.Code}\",\"LAB\"", exportedCsv, StringComparison.Ordinal);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actions = await db.BusinessAuditEntries.Where(x => x.EntityId == parent.Id || x.EntityId == child.Id || x.EntityId == costCodeResult.Id || x.EntityId == allocation.Id).Select(x => x.Action).ToListAsync();
            Assert.Contains("project-phase.created", actions);
            Assert.Contains("project-cost-code.created", actions);
            Assert.Contains("project-budget-allocation.created", actions);
            Assert.Contains("project-budget-allocation.updated", actions);
        }
    }

    [Fact]
    public async Task TrackingDimensions_EnforceCompanyTypeHierarchyEffectiveDatesPostingAndReversal()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var interchange = scope.ServiceProvider.GetRequiredService<IAccountingInterchangeService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid foreignDepartmentId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            var foreignCompanyId = Guid.NewGuid(); foreignDepartmentId = Guid.NewGuid();
            db.Companies.Add(new Company { Id = foreignCompanyId, Name = "Foreign dimension company", LegalName = "Foreign dimension company", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
            db.TrackingDimensionValues.Add(new TrackingDimensionValue { Id = foreignDepartmentId, CompanyId = foreignCompanyId, DimensionType = "Department", Code = "FOREIGN", Name = "Foreign department", CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        void SetUser(Guid userId, params string[] permissions)
        {
            var claims = permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)).ToList();
            claims.Add(new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()));
            claims.Add(new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }

        SetUser(Guid.NewGuid(), BrassLedgerPermissions.WorkspaceView);
        var denied = await transactions.SaveTrackingDimensionValueAsync(new(null, "Department", null, "OPS", "Operations", "", null, null));
        Assert.False(denied.Succeeded);
        Assert.Contains("authorized", denied.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var dimensionManagerId = Guid.NewGuid();
        SetUser(dimensionManagerId, BrassLedgerPermissions.AccountingDimensionsManage);
        var parentResult = await transactions.SaveTrackingDimensionValueAsync(new(null, " department ", null, " ops ", "Operations", "Operating departments", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        Assert.True(parentResult.Succeeded, parentResult.ErrorMessage);
        var childResult = await transactions.SaveTrackingDimensionValueAsync(new(null, "Department", parentResult.Id, "field", "Field service", "Mobile technicians", new DateOnly(2026, 2, 1), new DateOnly(2026, 11, 30)));
        Assert.True(childResult.Succeeded, childResult.ErrorMessage);
        var accountingClassResult = await transactions.SaveTrackingDimensionValueAsync(new(null, "Class", null, "commercial", "Commercial", "Commercial customer activity", new DateOnly(2026, 1, 1), null));
        Assert.True(accountingClassResult.Succeeded, accountingClassResult.ErrorMessage);
        var futureDepartmentResult = await transactions.SaveTrackingDimensionValueAsync(new(null, "Department", null, "future", "Future organization", "", new DateOnly(2027, 1, 1), null));
        Assert.True(futureDepartmentResult.Succeeded, futureDepartmentResult.ErrorMessage);
        var wrongParentType = await transactions.SaveTrackingDimensionValueAsync(new(null, "Class", parentResult.Id, "WRONG", "Wrong parent type", "", null, null));
        Assert.False(wrongParentType.Succeeded);
        Assert.Contains("parent", wrongParentType.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var outsideParent = await transactions.SaveTrackingDimensionValueAsync(new(null, "Department", parentResult.Id, "LATE", "Outside parent dates", "", new DateOnly(2025, 12, 31), new DateOnly(2027, 1, 1)));
        Assert.False(outsideParent.Succeeded);
        Assert.Contains("parent", outsideParent.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        TrackingDimensionValue parent; TrackingDimensionValue child; TrackingDimensionValue accountingClass;
        await using (var db = await factory.CreateDbContextAsync())
        {
            parent = await db.TrackingDimensionValues.SingleAsync(value => value.Id == parentResult.Id);
            child = await db.TrackingDimensionValues.SingleAsync(value => value.Id == childResult.Id);
            accountingClass = await db.TrackingDimensionValues.SingleAsync(value => value.Id == accountingClassResult.Id);
        }
        var cycle = await transactions.SaveTrackingDimensionValueAsync(new(parent.Id, parent.DimensionType, child.Id, parent.Code, parent.Name, parent.Description, parent.EffectiveFrom, parent.EffectiveThrough, parent.IsActive, parent.ConcurrencyToken));
        Assert.False(cycle.Succeeded);
        Assert.Contains("cycle", cycle.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var renamed = await transactions.SaveTrackingDimensionValueAsync(new(parent.Id, parent.DimensionType, null, parent.Code, "Operations and service", parent.Description, parent.EffectiveFrom, parent.EffectiveThrough, parent.IsActive, parent.ConcurrencyToken));
        Assert.True(renamed.Succeeded, renamed.ErrorMessage);
        var stale = await transactions.SaveTrackingDimensionValueAsync(new(parent.Id, parent.DimensionType, null, parent.Code, parent.Name, parent.Description, parent.EffectiveFrom, parent.EffectiveThrough, parent.IsActive, parent.ConcurrencyToken));
        Assert.False(stale.Succeeded);
        Assert.Contains("changed", stale.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var preparerId = Guid.NewGuid();
        SetUser(preparerId, BrassLedgerPermissions.JournalPrepare);
        var entryDate = new DateOnly(2026, 6, 15);
        var wrongType = await transactions.SaveJournalEntryDraftAsync(new(null, entryDate, "DIM-WRONG-TYPE", "Reject class used as department", [new("5100", 1m, 0m, "Wrong", DepartmentId: accountingClass.Id), new("4000", 0m, 1m, "Offset", DepartmentId: accountingClass.Id)]));
        Assert.False(wrongType.Succeeded);
        Assert.Contains("correct type", wrongType.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var foreign = await transactions.SaveJournalEntryDraftAsync(new(null, entryDate, "DIM-FOREIGN", "Reject another company dimension", [new("5100", 1m, 0m, "Foreign", DepartmentId: foreignDepartmentId), new("4000", 0m, 1m, "Offset", DepartmentId: foreignDepartmentId)]));
        Assert.False(foreign.Succeeded);
        Assert.Contains("current company", foreign.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var future = await transactions.SaveJournalEntryDraftAsync(new(null, entryDate, "DIM-FUTURE", "Reject future dimension", [new("5100", 1m, 0m, "Future", DepartmentId: futureDepartmentResult.Id), new("4000", 0m, 1m, "Offset", DepartmentId: futureDepartmentResult.Id)]));
        Assert.False(future.Succeeded);
        Assert.Contains("entry date", future.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var draft = await transactions.SaveJournalEntryDraftAsync(new(null, entryDate, "DIM-CONTROLLED", "Controlled department and class", [new("5100", 25m, 0m, "Tracked cost", DepartmentId: child.Id, ClassId: accountingClass.Id), new("4000", 0m, 25m, "Tracked offset", DepartmentId: child.Id, ClassId: accountingClass.Id)]));
        Assert.True(draft.Succeeded, draft.ErrorMessage);

        SetUser(Guid.NewGuid(), BrassLedgerPermissions.JournalApprove);
        Assert.True((await transactions.ApproveJournalEntryAsync(draft.Id!.Value)).Succeeded);
        SetUser(dimensionManagerId, BrassLedgerPermissions.AccountingDimensionsManage);
        var deactivateClass = await transactions.SaveTrackingDimensionValueAsync(new(accountingClass.Id, accountingClass.DimensionType, null, accountingClass.Code, accountingClass.Name, accountingClass.Description, accountingClass.EffectiveFrom, accountingClass.EffectiveThrough, false, accountingClass.ConcurrencyToken));
        Assert.True(deactivateClass.Succeeded, deactivateClass.ErrorMessage);
        SetUser(Guid.NewGuid(), BrassLedgerPermissions.JournalPost);
        var inactivePost = await transactions.PostApprovedJournalEntryAsync(draft.Id.Value);
        Assert.False(inactivePost.Succeeded);
        Assert.Contains("inactive", inactivePost.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        SetUser(dimensionManagerId, BrassLedgerPermissions.AccountingDimensionsManage);
        await using (var db = await factory.CreateDbContextAsync()) accountingClass = await db.TrackingDimensionValues.SingleAsync(value => value.Id == accountingClass.Id);
        Assert.True((await transactions.SaveTrackingDimensionValueAsync(new(accountingClass.Id, accountingClass.DimensionType, null, accountingClass.Code, accountingClass.Name, accountingClass.Description, accountingClass.EffectiveFrom, accountingClass.EffectiveThrough, true, accountingClass.ConcurrencyToken))).Succeeded);
        SetUser(Guid.NewGuid(), BrassLedgerPermissions.JournalPost);
        Assert.True((await transactions.PostApprovedJournalEntryAsync(draft.Id.Value)).Succeeded);

        Guid customerId; Guid vendorId; Guid itemId; Guid employeeId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            customerId = await db.Customers.Select(customer => customer.Id).FirstAsync();
            vendorId = await db.Vendors.Select(vendor => vendor.Id).FirstAsync();
            itemId = await db.InventoryItems.Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
            employeeId = await db.Employees.Where(employee => employee.IsActive).Select(employee => employee.Id).FirstAsync();
        }

        SetUser(Guid.NewGuid(), BrassLedgerPermissions.SubledgerPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.PayablesManage);
        var invoiceDraft = await transactions.SaveInvoiceDraftAsync(new(customerId, "DIM-AR-1", entryDate, entryDate.AddDays(30), 0m, 0m, "4000", "Tracked invoice", [new("Tracked revenue", 1m, 10m, 0m, 0m, "4000", DepartmentId: child.Id, ClassId: accountingClass.Id)]));
        Assert.True(invoiceDraft.Succeeded, invoiceDraft.ErrorMessage);
        var billDraft = await transactions.SaveVendorBillDraftAsync(new(vendorId, "DIM-AP-1", entryDate, entryDate.AddDays(30), 0m, "5100", "Tracked bill", [new("Tracked expense", 1m, 8m, 0m, 0m, "5100", DepartmentId: child.Id, ClassId: accountingClass.Id)]));
        Assert.True(billDraft.Succeeded, billDraft.ErrorMessage);
        SetUser(Guid.NewGuid(), BrassLedgerPermissions.SubledgerApprove, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.PayablesManage);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(invoiceDraft.Id!.Value)).Succeeded);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(billDraft.Id!.Value)).Succeeded);
        SetUser(Guid.NewGuid(), BrassLedgerPermissions.SubledgerPost, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.PayablesManage);
        var postedInvoice = await transactions.PostApprovedSubledgerDocumentAsync(invoiceDraft.Id.Value);
        var postedBill = await transactions.PostApprovedSubledgerDocumentAsync(billDraft.Id.Value);
        Assert.True(postedInvoice.Succeeded, postedInvoice.ErrorMessage);
        Assert.True(postedBill.Succeeded, postedBill.ErrorMessage);

        SetUser(Guid.NewGuid(), BrassLedgerPermissions.SalesManage);
        var quote = await transactions.SaveSalesQuoteAsync(new(null, customerId, "DIM-QUOTE-1", entryDate, entryDate.AddDays(15), "Tracked quote", [new(itemId, "Tracked quote line", 1m, 12m, 0m, 0m, "4000", DepartmentId: child.Id, ClassId: accountingClass.Id)]));
        Assert.True(quote.Succeeded, quote.ErrorMessage);
        SetUser(Guid.NewGuid(), BrassLedgerPermissions.RequisitionManage);
        var requisition = await transactions.SavePurchaseRequisitionAsync(new(null, vendorId, "DIM-REQ-1", entryDate, entryDate.AddDays(10), "Tracked requisition", [new(itemId, "Tracked requisition line", 1m, 7m, DepartmentId: child.Id, ClassId: accountingClass.Id)]));
        Assert.True(requisition.Succeeded, requisition.ErrorMessage);
        SetUser(Guid.NewGuid(), BrassLedgerPermissions.PayrollPrepare);
        var timecard = await transactions.SavePayrollTimecardDraftAsync(new(null, employeeId, entryDate, entryDate, [new(entryDate, "REG", "Regular", 1m, 20m, 20m, DepartmentId: child.Id, ClassId: accountingClass.Id)], "Tracked timecard"));
        Assert.True(timecard.Succeeded, timecard.ErrorMessage);

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.All(await db.SalesInvoiceLines.Where(line => line.SalesInvoiceId == postedInvoice.Id).ToListAsync(), line => { Assert.Equal(child.Id, line.DepartmentId); Assert.Equal(accountingClass.Id, line.ClassId); });
            Assert.All(await db.VendorBillLines.Where(line => line.VendorBillId == postedBill.Id).ToListAsync(), line => { Assert.Equal(child.Id, line.DepartmentId); Assert.Equal(accountingClass.Id, line.ClassId); });
            Assert.All(await db.SalesQuoteLines.Where(line => line.SalesQuoteId == quote.Id).ToListAsync(), line => { Assert.Equal(child.Id, line.DepartmentId); Assert.Equal(accountingClass.Id, line.ClassId); });
            Assert.All(await db.PurchaseRequisitionLines.Where(line => line.PurchaseRequisitionId == requisition.Id).ToListAsync(), line => { Assert.Equal(child.Id, line.DepartmentId); Assert.Equal(accountingClass.Id, line.ClassId); });
            Assert.All(await db.PayrollTimeEntries.Where(line => line.PayrollTimecardId == timecard.Id).ToListAsync(), line => { Assert.Equal(child.Id, line.DepartmentId); Assert.Equal(accountingClass.Id, line.ClassId); });
        }

        var invoiceExport = await interchange.ExportQuickBooksOnlineCsvAsync("invoices");
        Assert.NotNull(invoiceExport);
        var invoiceCsv = System.Text.Encoding.UTF8.GetString(invoiceExport.Content);
        Assert.Contains("\"Department\",\"Class\"", invoiceCsv, StringComparison.Ordinal);
        Assert.Contains("\"FIELD\",\"COMMERCIAL\"", invoiceCsv, StringComparison.Ordinal);
        string customerReference;
        await using (var db = await factory.CreateDbContextAsync()) customerReference = await db.Customers.Where(customer => customer.Id == customerId).Select(customer => customer.CustomerNumber).SingleAsync();
        var invoiceImportCsv = "Invoice No.,Customer,Invoice Date,Due Date,Item Amount,Item Description,Quantity,Rate,Project / Job,Project Phase,Cost Code,Department,Class\r\n"
            + $"DIM-QB-AR-1,{customerReference},2026-06-20,2026-07-20,9.00,Imported tracked invoice,1,9.00,,,,FIELD,COMMERCIAL\r\n";
        SetUser(Guid.NewGuid(), BrassLedgerPermissions.SubledgerPrepare, BrassLedgerPermissions.ReceivablesManage);
        await using (var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invoiceImportCsv)))
        {
            var imported = await interchange.ImportQuickBooksOnlineCsvAsync("invoices", content, new(false, "tracked-invoices.csv"));
            Assert.True(imported.Succeeded, string.Join("; ", imported.Errors));
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var workflow = await db.SubledgerDocumentWorkflows.SingleAsync(item => item.DocumentNumber == "DIM-QB-AR-1");
            var payload = System.Text.Json.JsonSerializer.Deserialize<CreateInvoiceRequest>(workflow.PayloadJson)!;
            Assert.All(payload.Lines ?? [], line => { Assert.Equal(child.Id, line.DepartmentId); Assert.Equal(accountingClass.Id, line.ClassId); });
        }

        var exported = await interchange.ExportQuickBooksOnlineCsvAsync("journal-entries");
        Assert.NotNull(exported);
        var exportedCsv = System.Text.Encoding.UTF8.GetString(exported.Content);
        Assert.Contains("\"Department\",\"Class\"", exportedCsv, StringComparison.Ordinal);
        Assert.Contains("\"FIELD\",\"COMMERCIAL\"", exportedCsv, StringComparison.Ordinal);
        const string dimensionImport = "Journal No.,Journal Date,Reference,Journal/Description,Account Name,Debits,Credits,Line Description,Project / Job,Project Phase,Cost Code,Department,Class\r\n"
            + "DIM-IMPORT-TRACK,2026-06-20,DIM-IMPORT,Tracking import,5100,12,0,Imported cost,,,,FIELD,COMMERCIAL\r\n"
            + "DIM-IMPORT-TRACK,2026-06-20,DIM-IMPORT,Tracking import,4000,0,12,Imported offset,,,,FIELD,COMMERCIAL\r\n";
        SetUser(preparerId, BrassLedgerPermissions.JournalPrepare);
        await using (var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(dimensionImport)))
        {
            var imported = await interchange.ImportQuickBooksOnlineCsvAsync("journal-entries", content, new(false, "tracking-dimensions.csv"));
            Assert.True(imported.Succeeded, string.Join("; ", imported.Errors));
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var importedEntry = await db.JournalEntries.SingleAsync(entry => entry.Description.Contains("DIM-IMPORT-TRACK"));
            Assert.All(await db.JournalEntryLines.Where(line => line.JournalEntryId == importedEntry.Id).ToListAsync(), line => { Assert.Equal(child.Id, line.DepartmentId); Assert.Equal(accountingClass.Id, line.ClassId); });
        }

        SetUser(dimensionManagerId, BrassLedgerPermissions.AccountingDimensionsManage);
        await using (var db = await factory.CreateDbContextAsync()) accountingClass = await db.TrackingDimensionValues.SingleAsync(value => value.Id == accountingClass.Id);
        Assert.True((await transactions.SaveTrackingDimensionValueAsync(new(accountingClass.Id, accountingClass.DimensionType, null, accountingClass.Code, accountingClass.Name, accountingClass.Description, accountingClass.EffectiveFrom, accountingClass.EffectiveThrough, false, accountingClass.ConcurrencyToken))).Succeeded);
        SetUser(Guid.NewGuid(), BrassLedgerPermissions.JournalReverse);
        var reversal = await transactions.ReverseJournalEntryAsync(new(draft.Id.Value, new DateOnly(2026, 6, 30), "Correct tracked posting"));
        Assert.True(reversal.Succeeded, reversal.ErrorMessage);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var originalLines = await db.JournalEntryLines.Where(line => line.JournalEntryId == draft.Id).ToListAsync();
            var reversalLines = await db.JournalEntryLines.Where(line => line.JournalEntryId == reversal.Id).ToListAsync();
            Assert.All(originalLines.Concat(reversalLines), line => { Assert.Equal(child.Id, line.DepartmentId); Assert.Equal(accountingClass.Id, line.ClassId); });
            var auditActions = await db.BusinessAuditEntries.Where(entry => entry.EntityId == parent.Id || entry.EntityId == child.Id || entry.EntityId == accountingClass.Id).Select(entry => entry.Action).ToListAsync();
            Assert.Contains("tracking-dimension.created", auditActions);
            Assert.Contains("tracking-dimension.updated", auditActions);
        }
        var workspace = await workspaceService.GetWorkspaceAsync();
        Assert.Contains(workspace.GeneralLedger.TrackingDimensions ?? [], value => value.Id == child.Id && value.Code == "FIELD" && value.ParentTrackingDimensionValueId == parent.Id);
        var reversalSnapshot = Assert.Single(workspace.GeneralLedger.RecentEntries, entry => entry.Id == reversal.Id);
        Assert.All(reversalSnapshot.Lines ?? [], line => { Assert.Equal("FIELD", line.DepartmentCode); Assert.Equal("COMMERCIAL", line.ClassCode); });
    }

    [Fact]
    public async Task ProjectChangeOrders_RequireIndependentApprovalAndAtomicallyReviseAuthorizedTotals()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid projectId; Guid customerId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            projectId = await db.ProjectJobs.Where(project => project.Status == "Active").Select(project => project.Id).FirstAsync();
            customerId = await db.ProjectJobs.Where(project => project.Id == projectId).Select(project => project.CustomerId!.Value).SingleAsync();
        }
        var preparerId = Guid.NewGuid(); var approverId = Guid.NewGuid();
        void SetUser(Guid userId, Guid selectedCompanyId, params string[] permissions)
        {
            var claims = new List<System.Security.Claims.Claim>
            {
                new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
                new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, selectedCompanyId.ToString())
            };
            claims.AddRange(permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }

        ProjectJob project;
        await using (var db = await factory.CreateDbContextAsync()) project = await db.ProjectJobs.SingleAsync(candidate => candidate.Id == projectId);
        var startingContract = project.ContractAmount; var startingBudget = project.BudgetAmount;
        SetUser(preparerId, companyId, BrassLedgerPermissions.ProjectChangeOrderPrepare, BrassLedgerPermissions.ProjectChangeOrderApprove, BrassLedgerPermissions.ProjectsManage);
        var saved = await transactions.SaveProjectChangeOrderDraftAsync(new(null, projectId, "CO-TEST-001", "Expanded customer scope", "Customer authorized added work", new DateOnly(2026, 8, 26), new DateOnly(2026, 9, 1), 1_250.125m, 700.125m));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        ProjectChangeOrder changeOrder;
        await using (var db = await factory.CreateDbContextAsync()) changeOrder = await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == saved.Id);
        Assert.Equal(1_250.13m, changeOrder.ContractAmountChange);
        var submitted = await transactions.SubmitProjectChangeOrderAsync(new(changeOrder.Id, changeOrder.ConcurrencyToken));
        Assert.True(submitted.Succeeded, submitted.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) changeOrder = await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == saved.Id);

        var selfApproval = await transactions.DecideProjectChangeOrderAsync(new(changeOrder.Id, true, "Cannot approve my own work", changeOrder.ConcurrencyToken));
        Assert.False(selfApproval.Succeeded);
        Assert.Contains("preparer", selfApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var directRevision = await transactions.SaveProjectJobAsync(new(project.Id, project.JobNumber, project.Name, customerId, project.StartDate!.Value, project.ExpectedEndDate, project.BillingMethod, project.ContractAmount + 1m, project.BudgetAmount, project.RetainagePercent, project.ConcurrencyToken));
        Assert.False(directRevision.Succeeded);
        Assert.Contains("change order", directRevision.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        SetUser(approverId, Guid.NewGuid(), BrassLedgerPermissions.ProjectChangeOrderApprove);
        var foreignDecision = await transactions.DecideProjectChangeOrderAsync(new(changeOrder.Id, true, "Wrong company must not see it", changeOrder.ConcurrencyToken));
        Assert.False(foreignDecision.Succeeded);
        Assert.Contains("not found", foreignDecision.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetUser(approverId, companyId, BrassLedgerPermissions.ProjectChangeOrderApprove);
        var approved = await transactions.DecideProjectChangeOrderAsync(new(changeOrder.Id, true, "Customer authorization independently verified", changeOrder.ConcurrencyToken));
        Assert.True(approved.Succeeded, approved.ErrorMessage);

        var workspace = await workspaceService.GetWorkspaceAsync();
        var projectSnapshot = workspace.Projects.Jobs.Single(candidate => candidate.Id == projectId);
        var changeSnapshot = Assert.Single(workspace.Projects.ChangeOrders!, candidate => candidate.Id == changeOrder.Id);
        Assert.Equal(startingContract + 1_250.13m, projectSnapshot.ContractAmount);
        Assert.Equal(startingBudget + 700.13m, projectSnapshot.BudgetAmount);
        Assert.Equal("Approved", changeSnapshot.Status);
        Assert.Equal(startingContract, changeSnapshot.ContractAmountBefore);
        Assert.Equal(startingContract + 1_250.13m, changeSnapshot.ContractAmountAfter);

        SetUser(preparerId, companyId, BrassLedgerPermissions.ProjectChangeOrderPrepare);
        var cancelApproved = await transactions.CancelProjectChangeOrderAsync(new(changeOrder.Id, "Approved history must remain immutable", changeSnapshot.ConcurrencyToken));
        Assert.False(cancelApproved.Succeeded);
        var reduction = await transactions.SaveProjectChangeOrderDraftAsync(new(null, projectId, "CO-TEST-002", "Reduce approved scope", "Customer removed part of the added work", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 3), -250.13m, -100.13m));
        Assert.True(reduction.Succeeded, reduction.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) changeOrder = await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == reduction.Id);
        Assert.True((await transactions.SubmitProjectChangeOrderAsync(new(changeOrder.Id, changeOrder.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) changeOrder = await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == reduction.Id);
        SetUser(approverId, companyId, BrassLedgerPermissions.ProjectChangeOrderApprove);
        Assert.True((await transactions.DecideProjectChangeOrderAsync(new(changeOrder.Id, true, "Reduction independently verified", changeOrder.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var reducedProject = await db.ProjectJobs.SingleAsync(candidate => candidate.Id == projectId);
            Assert.Equal(startingContract + 1_000m, reducedProject.ContractAmount);
            Assert.Equal(startingBudget + 600m, reducedProject.BudgetAmount);
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal("Approved", (await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == saved.Id)).Status);
            Assert.Contains(await db.BusinessAuditEntries.Where(entry => entry.EntityId == saved.Id).ToListAsync(), entry => entry.Action == "project-change-order.created");
            Assert.Contains(await db.BusinessAuditEntries.Where(entry => entry.EntityId == saved.Id).ToListAsync(), entry => entry.Action == "project-change-order.submitted");
            Assert.Contains(await db.BusinessAuditEntries.Where(entry => entry.EntityId == saved.Id).ToListAsync(), entry => entry.Action == "project-change-order.approved");
        }
    }

    [Fact]
    public async Task ProjectChangeOrders_BlockCloseSupportRejectedCorrectionAndDetectStaleProjectReview()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid customerId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(company => company.Id).SingleAsync();
            customerId = await db.Customers.Select(customer => customer.Id).FirstAsync();
        }
        var preparerId = Guid.NewGuid(); var approverId = Guid.NewGuid();
        void SetUser(Guid userId, params string[] permissions)
        {
            var claims = permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)).ToList();
            claims.Add(new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()));
            claims.Add(new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }

        SetUser(preparerId, BrassLedgerPermissions.ProjectsManage);
        var unauthorized = await transactions.SaveProjectChangeOrderDraftAsync(new(null, Guid.NewGuid(), "CO-NO", "Unauthorized", "Must fail before lookup", new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 26), 1m, 0m));
        Assert.False(unauthorized.Succeeded);
        Assert.Contains("not authorized", unauthorized.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var projectResult = await transactions.SaveProjectJobAsync(new(null, "JOB-CO-LIFECYCLE", "Change-order lifecycle", customerId, new DateOnly(2026, 8, 1), null, "FixedPrice", 10_000m, 6_000m, 0m));
        Assert.True(projectResult.Succeeded, projectResult.ErrorMessage);
        SetUser(preparerId, BrassLedgerPermissions.ProjectsManage, BrassLedgerPermissions.ProjectChangeOrderPrepare);
        var oversized = await transactions.SaveProjectChangeOrderDraftAsync(new(null, projectResult.Id!.Value, "CO-TOO-LARGE", "Out-of-range amount", "Must fail before persistence", new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 26), 10_000_000_000_000_000m, 0m));
        Assert.False(oversized.Succeeded);
        Assert.Contains("18-digit", oversized.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var saved = await transactions.SaveProjectChangeOrderDraftAsync(new(null, projectResult.Id!.Value, "CO-002", "Lifecycle test", "Document revised scope", new DateOnly(2026, 8, 26), new DateOnly(2026, 9, 1), 500m, 250m));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        ProjectJob project; ProjectChangeOrder changeOrder;
        await using (var db = await factory.CreateDbContextAsync()) { project = await db.ProjectJobs.SingleAsync(candidate => candidate.Id == projectResult.Id); changeOrder = await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == saved.Id); }
        var closeBlocked = await transactions.CloseProjectJobAsync(new(project.Id, new DateOnly(2026, 9, 30), "Cannot close with unresolved scope", project.ConcurrencyToken));
        Assert.False(closeBlocked.Succeeded);
        Assert.Contains("open", closeBlocked.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True((await transactions.SubmitProjectChangeOrderAsync(new(changeOrder.Id, changeOrder.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) { project = await db.ProjectJobs.SingleAsync(candidate => candidate.Id == project.Id); changeOrder = await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == changeOrder.Id); }
        var renamed = await transactions.SaveProjectJobAsync(new(project.Id, project.JobNumber, "Change-order lifecycle renamed", customerId, project.StartDate!.Value, project.ExpectedEndDate, project.BillingMethod, project.ContractAmount, project.BudgetAmount, project.RetainagePercent, project.ConcurrencyToken));
        Assert.True(renamed.Succeeded, renamed.ErrorMessage);
        SetUser(approverId, BrassLedgerPermissions.ProjectChangeOrderApprove);
        var staleApproval = await transactions.DecideProjectChangeOrderAsync(new(changeOrder.Id, true, "Project changed after submission", changeOrder.ConcurrencyToken));
        Assert.False(staleApproval.Succeeded);
        Assert.Contains("project changed", staleApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var rejected = await transactions.DecideProjectChangeOrderAsync(new(changeOrder.Id, false, "Return the stale proposal for correction", changeOrder.ConcurrencyToken));
        Assert.True(rejected.Succeeded, rejected.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) changeOrder = await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == changeOrder.Id);
        SetUser(preparerId, BrassLedgerPermissions.ProjectChangeOrderPrepare, BrassLedgerPermissions.ProjectsManage);
        var staleCorrection = await transactions.SaveProjectChangeOrderDraftAsync(new(changeOrder.Id, changeOrder.ProjectJobId, changeOrder.ChangeOrderNumber, changeOrder.Description, changeOrder.Reason, changeOrder.RequestedOn, changeOrder.EffectiveOn, 450m, 225m, "stale"));
        Assert.False(staleCorrection.Succeeded);
        var corrected = await transactions.SaveProjectChangeOrderDraftAsync(new(changeOrder.Id, changeOrder.ProjectJobId, changeOrder.ChangeOrderNumber, "Lifecycle test corrected", "Updated after independent review", changeOrder.RequestedOn, changeOrder.EffectiveOn, 450m, 225m, changeOrder.ConcurrencyToken));
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) changeOrder = await db.ProjectChangeOrders.SingleAsync(candidate => candidate.Id == changeOrder.Id);
        var cancelled = await transactions.CancelProjectChangeOrderAsync(new(changeOrder.Id, "Customer withdrew the added scope", changeOrder.ConcurrencyToken));
        Assert.True(cancelled.Succeeded, cancelled.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) project = await db.ProjectJobs.SingleAsync(candidate => candidate.Id == project.Id);
        var closed = await transactions.CloseProjectJobAsync(new(project.Id, new DateOnly(2026, 9, 30), "No remaining activity", project.ConcurrencyToken));
        Assert.True(closed.Succeeded, closed.ErrorMessage);
    }

    [Fact]
    public async Task ProjectBilling_DerivesApprovedTimeControlsReviewPostingCorrectionAndSourceReuse()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid customerId; Guid employeeId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(x => x.Id).SingleAsync();
            customerId = await db.Customers.OrderBy(x => x.CustomerNumber).Select(x => x.Id).FirstAsync();
            employeeId = await db.Employees.OrderBy(x => x.EmployeeNumber).Select(x => x.Id).FirstAsync();
        }
        var preparerId = Guid.NewGuid(); var reviewerId = Guid.NewGuid(); var posterId = Guid.NewGuid();
        void SetUser(Guid userId, params string[] permissions)
        {
            var claims = permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)).ToList();
            claims.Add(new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()));
            claims.Add(new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }

        SetUser(preparerId, BrassLedgerPermissions.ProjectsManage, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var projectResult = await transactions.SaveProjectJobAsync(new(null, "JOB-BILLING-1", "Controlled project billing", customerId, new DateOnly(2026, 8, 1), null, "TimeAndMaterials", 1_000m, 700m, 0.10m));
        Assert.True(projectResult.Succeeded, projectResult.ErrorMessage);
        var projectId = projectResult.Id!.Value;
        var phaseResult = await transactions.SaveProjectPhaseAsync(new(null, projectId, null, "LABOR", "Labor phase", "Phase", "Billable labor", new DateOnly(2026, 8, 1), null));
        Assert.True(phaseResult.Succeeded, phaseResult.ErrorMessage);
        var costCodeResult = await transactions.SaveProjectCostCodeAsync(new(null, "LAB-BILL", "Billable labor", "Labor", "Project labor billed to customers"));
        Assert.True(costCodeResult.Succeeded, costCodeResult.ErrorMessage);
        var phaseId = phaseResult.Id!.Value;
        var costCodeId = costCodeResult.Id!.Value;
        var rateResult = await transactions.SaveProjectBillingRateAsync(new(null, projectId, "REGULAR", 100m, new DateOnly(2026, 8, 1), null));
        Assert.True(rateResult.Succeeded, rateResult.ErrorMessage);
        var overlappingRate = await transactions.SaveProjectBillingRateAsync(new(null, projectId, "REGULAR", 110m, new DateOnly(2026, 8, 15), null));
        Assert.False(overlappingRate.Succeeded);
        Assert.Contains("overlapping", overlappingRate.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        Guid firstTimeEntryId; Guid secondTimeEntryId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var card = new PayrollTimecard { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = employeeId, PeriodStart = new DateOnly(2026, 8, 17), PeriodEnd = new DateOnly(2026, 8, 23), Status = "Approved", PreparedByUserId = preparerId, PreparedAtUtc = DateTimeOffset.UtcNow, ApprovedByUserId = reviewerId, ApprovedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
            firstTimeEntryId = Guid.NewGuid(); secondTimeEntryId = Guid.NewGuid();
            db.PayrollTimecards.Add(card);
            db.PayrollTimeEntries.AddRange(
                new PayrollTimeEntry { Id = firstTimeEntryId, PayrollTimecardId = card.Id, Sequence = 1, WorkDate = new DateOnly(2026, 8, 18), EarningCode = "REGULAR", EarningType = "Regular", Hours = 2m, Rate = 30m, Amount = 60m, ProjectJobId = projectId, ProjectPhaseId = phaseId, ProjectCostCodeId = costCodeId },
                new PayrollTimeEntry { Id = secondTimeEntryId, PayrollTimecardId = card.Id, Sequence = 2, WorkDate = new DateOnly(2026, 8, 19), EarningCode = "REGULAR", EarningType = "Regular", Hours = 1m, Rate = 30m, Amount = 30m, ProjectJobId = projectId, ProjectPhaseId = phaseId, ProjectCostCodeId = costCodeId });
            await db.SaveChangesAsync();
        }

        var previewRequest = new ProjectBillingPreviewRequest(projectId, "PB-1001", new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 24), "4000", "August approved project time", IncludeCosts: false);
        var preview = await transactions.PreviewProjectBillingAsync(previewRequest);
        Assert.True(preview.Succeeded, preview.ErrorMessage);
        var previewLine = Assert.Single(preview.Lines);
        Assert.Equal(firstTimeEntryId, previewLine.SourceId);
        Assert.Equal(phaseId, previewLine.ProjectPhaseId);
        Assert.Equal(costCodeId, previewLine.ProjectCostCodeId);
        Assert.Equal(200m, preview.GrossAmount);
        Assert.Equal(20m, preview.RetainageAmount);
        Assert.Equal(180m, preview.InvoiceAmount);
        var staleSave = await transactions.SaveProjectBillingProposalAsync(new(null, previewRequest, preview.Fingerprint, "stale"));
        Assert.False(staleSave.Succeeded);
        var saved = await transactions.SaveProjectBillingProposalAsync(new(null, previewRequest, preview.Fingerprint, preview.ProjectConcurrencyToken));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        ProjectBillingProposal proposal; SubledgerDocumentWorkflow workflow; ProjectJob project;
        await using (var db = await factory.CreateDbContextAsync())
        {
            proposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == saved.Id);
            workflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == proposal.SubledgerDocumentWorkflowId);
            project = await db.ProjectJobs.SingleAsync(x => x.Id == projectId);
            var billingLine = await db.ProjectBillingLines.SingleAsync(x => x.ProjectBillingProposalId == proposal.Id);
            Assert.Equal(phaseId, billingLine.ProjectPhaseId);
            Assert.Equal(costCodeId, billingLine.ProjectCostCodeId);
            Assert.Equal("Reserved", (await db.ProjectBillingSourceReservations.SingleAsync(x => x.SourceKey == $"TIME:{firstTimeEntryId:N}")).Status);
        }
        var closeBlocked = await transactions.CloseProjectJobAsync(new(projectId, new DateOnly(2026, 8, 31), "Billing remains unresolved", project.ConcurrencyToken));
        Assert.False(closeBlocked.Succeeded);

        SetUser(preparerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        var selfReview = await transactions.ApproveSubledgerDocumentAsync(workflow.Id);
        Assert.False(selfReview.Succeeded);
        Assert.Contains("prepared", selfReview.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        var rejected = await transactions.RejectSubledgerDocumentAsync(new(workflow.Id, "Clarify the time description", workflow.ConcurrencyToken));
        Assert.True(rejected.Succeeded, rejected.ErrorMessage);

        SetUser(preparerId, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var bypass = await transactions.SaveInvoiceDraftAsync(new(customerId, "PB-1001", new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 24), 180m, 0m, "4000", "Bypass project derivation"));
        Assert.False(bypass.Succeeded);
        Assert.Contains("project billing", bypass.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync()) proposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == proposal.Id);
        var correctionRequest = previewRequest with { Description = "August approved project time — reviewed", ExistingProposalId = proposal.Id };
        var correctedPreview = await transactions.PreviewProjectBillingAsync(correctionRequest);
        Assert.True(correctedPreview.Succeeded, correctedPreview.ErrorMessage);
        var corrected = await transactions.SaveProjectBillingProposalAsync(new(proposal.Id, correctionRequest, correctedPreview.Fingerprint, correctedPreview.ProjectConcurrencyToken, proposal.ConcurrencyToken));
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) { proposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == proposal.Id); workflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == proposal.SubledgerDocumentWorkflowId); }
        Assert.Equal("Draft", proposal.Status);
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(workflow.Id)).Succeeded);
        SetUser(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost);
        var posted = await transactions.PostApprovedSubledgerDocumentAsync(workflow.Id);
        Assert.True(posted.Succeeded, posted.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            proposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == proposal.Id);
            Assert.Equal("Posted", proposal.Status);
            Assert.Equal("Billed", (await db.ProjectBillingSourceReservations.SingleAsync(x => x.SourceKey == $"TIME:{firstTimeEntryId:N}")).Status);
            var invoice = await db.SalesInvoices.SingleAsync(x => x.Id == posted.Id);
            Assert.Equal(180m, invoice.TotalAmount);
            var invoiceLine = await db.SalesInvoiceLines.SingleAsync(x => x.SalesInvoiceId == invoice.Id);
            Assert.Equal(20m, invoiceLine.DiscountAmount);
            Assert.Equal(projectId, invoiceLine.ProjectJobId);
            Assert.Equal(phaseId, invoiceLine.ProjectPhaseId);
            Assert.Equal(costCodeId, invoiceLine.ProjectCostCodeId);
        }
        SetUser(posterId, BrassLedgerPermissions.PayrollReverse);
        PayrollTimecard billedTimecard;
        await using (var db = await factory.CreateDbContextAsync()) billedTimecard = await db.PayrollTimecards.SingleAsync(x => x.EmployeeId == employeeId && x.PeriodStart == new DateOnly(2026, 8, 17));
        var billedTimecardVoid = await transactions.VoidPayrollTimecardAsync(new(billedTimecard.Id, "Cannot invalidate billed time", billedTimecard.ConcurrencyToken));
        Assert.False(billedTimecardVoid.Succeeded); Assert.Contains("project billing", billedTimecardVoid.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetUser(preparerId, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var duplicateSource = await transactions.PreviewProjectBillingAsync(previewRequest with { InvoiceNumber = "PB-1001-DUP" });
        Assert.False(duplicateSource.Succeeded);
        Assert.Contains("No eligible", duplicateSource.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var secondRequest = previewRequest with { InvoiceNumber = "PB-1002", BillingThrough = new DateOnly(2026, 8, 19), SelectedTimeEntryIds = [secondTimeEntryId] };
        var secondPreview = await transactions.PreviewProjectBillingAsync(secondRequest);
        Assert.True(secondPreview.Succeeded, secondPreview.ErrorMessage);
        var secondSaved = await transactions.SaveProjectBillingProposalAsync(new(null, secondRequest, secondPreview.Fingerprint, secondPreview.ProjectConcurrencyToken));
        Assert.True(secondSaved.Succeeded, secondSaved.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) proposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == secondSaved.Id);
        var cancelled = await transactions.CancelProjectBillingProposalAsync(new(proposal.Id, "Customer deferred this billing", proposal.ConcurrencyToken));
        Assert.True(cancelled.Succeeded, cancelled.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) Assert.Equal("Released", (await db.ProjectBillingSourceReservations.SingleAsync(x => x.SourceKey == $"TIME:{secondTimeEntryId:N}")).Status);
        var reusable = await transactions.PreviewProjectBillingAsync(secondRequest with { InvoiceNumber = "PB-1003" });
        Assert.True(reusable.Succeeded, reusable.ErrorMessage);
        Assert.Equal(secondTimeEntryId, Assert.Single(reusable.Lines).SourceId);

        var workspace = await workspaceService.GetWorkspaceAsync();
        Assert.Contains(workspace.Projects.BillingRates!, x => x.ProjectJobId == projectId && x.EarningCode == "REGULAR");
        Assert.Contains(workspace.Projects.BillingProposals!, x => x.Id == saved.Id && x.Status == "Posted" && x.Lines.Count == 1 && x.Lines[0].ProjectPhaseCode == "LABOR" && x.Lines[0].ProjectCostCode == "LAB-BILL");
        var postedInvoiceSnapshot = Assert.Single(workspace.Receivables.Invoices, x => x.Id == posted.Id);
        var postedInvoiceLineSnapshot = Assert.Single(postedInvoiceSnapshot.Lines!);
        Assert.Equal(projectId, postedInvoiceLineSnapshot.ProjectJobId);
        Assert.Equal("JOB-BILLING-1", postedInvoiceLineSnapshot.ProjectJobNumber);
        Assert.Equal(phaseId, postedInvoiceLineSnapshot.ProjectPhaseId);
        Assert.Equal("LABOR", postedInvoiceLineSnapshot.ProjectPhaseCode);
        Assert.Equal(costCodeId, postedInvoiceLineSnapshot.ProjectCostCodeId);
        Assert.Equal("LAB-BILL", postedInvoiceLineSnapshot.ProjectCostCode);

        SetUser(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.PaymentReverse);
        var voided = await transactions.VoidInvoiceAsync(new(posted.Id!.Value, new DateOnly(2026, 8, 26), "Customer cancelled the billed work"));
        Assert.True(voided.Succeeded, voided.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal("Voided", (await db.ProjectBillingProposals.SingleAsync(x => x.Id == saved.Id)).Status);
            Assert.Equal("Released", (await db.ProjectBillingSourceReservations.SingleAsync(x => x.SourceKey == $"TIME:{firstTimeEntryId:N}")).Status);
        }
        SetUser(preparerId, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var afterVoid = await transactions.PreviewProjectBillingAsync(previewRequest with { InvoiceNumber = "PB-1004" });
        Assert.True(afterVoid.Succeeded, afterVoid.ErrorMessage);
        Assert.Equal(firstTimeEntryId, Assert.Single(afterVoid.Lines).SourceId);
        SetUser(posterId, BrassLedgerPermissions.PayrollReverse);
        await using (var db = await factory.CreateDbContextAsync()) billedTimecard = await db.PayrollTimecards.SingleAsync(x => x.Id == billedTimecard.Id);
        Assert.True((await transactions.VoidPayrollTimecardAsync(new(billedTimecard.Id, "Customer cancelled all billed work", billedTimecard.ConcurrencyToken))).Succeeded);

        Guid consumedTimeEntryId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var consumedCard = new PayrollTimecard { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = employeeId, PeriodStart = new DateOnly(2026, 8, 24), PeriodEnd = new DateOnly(2026, 8, 30), Status = "Consumed", PreparedByUserId = preparerId, PreparedAtUtc = DateTimeOffset.UtcNow, ApprovedByUserId = reviewerId, ApprovedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
            consumedTimeEntryId = Guid.NewGuid();
            db.PayrollTimecards.Add(consumedCard);
            db.PayrollTimeEntries.Add(new PayrollTimeEntry { Id = consumedTimeEntryId, PayrollTimecardId = consumedCard.Id, Sequence = 1, WorkDate = new DateOnly(2026, 8, 25), EarningCode = "REGULAR", EarningType = "Regular", Hours = 1.5m, Rate = 30m, Amount = 45m, ProjectJobId = projectId });
            await db.SaveChangesAsync();
        }
        SetUser(preparerId, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var consumedRequest = previewRequest with { InvoiceNumber = "PB-1005", BillingThrough = new DateOnly(2026, 8, 25), Description = "Payroll-consumed approved time", SelectedTimeEntryIds = [consumedTimeEntryId] };
        var consumedPreview = await transactions.PreviewProjectBillingAsync(consumedRequest); Assert.True(consumedPreview.Succeeded, consumedPreview.ErrorMessage); Assert.Equal(consumedTimeEntryId, Assert.Single(consumedPreview.Lines).SourceId);
        var consumedSaved = await transactions.SaveProjectBillingProposalAsync(new(null, consumedRequest, consumedPreview.Fingerprint, consumedPreview.ProjectConcurrencyToken)); Assert.True(consumedSaved.Succeeded, consumedSaved.ErrorMessage);
        ProjectBillingProposal consumedProposal; SubledgerDocumentWorkflow consumedWorkflow;
        await using (var db = await factory.CreateDbContextAsync()) { consumedProposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == consumedSaved.Id); consumedWorkflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == consumedProposal.SubledgerDocumentWorkflowId); }
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(consumedWorkflow.Id)).Succeeded);
    }

    [Fact]
    public async Task ProjectBilling_EnforcesFixedPriceProgressAndDerivesCostPlusPostedCosts()
    {
        using var services = CreateServiceProvider(); await services.InitializeBrassLedgerAsync(); using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid customerId; decimal receivablesBefore; decimal retainageBefore; decimal revenueBefore;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(x => x.Id).SingleAsync();
            customerId = await db.Customers.OrderBy(x => x.CustomerNumber).Select(x => x.Id).FirstAsync();
            receivablesBefore = await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(x => x.CurrentBalance).SingleAsync();
            retainageBefore = await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.RetainageReceivable).Select(x => x.CurrentBalance).SingleAsync();
            revenueBefore = await db.Accounts.Where(x => x.CompanyId == companyId && x.Number == "4000").Select(x => x.CurrentBalance).SingleAsync();
        }
        var userId = Guid.NewGuid(); var reviewerId = Guid.NewGuid(); var posterId = Guid.NewGuid();
        void SetUser(Guid id, params string[] permissions) { var claims = permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)).ToList(); claims.Add(new(System.Security.Claims.ClaimTypes.NameIdentifier, id.ToString())); claims.Add(new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString())); accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) }; }
        SetUser(userId, BrassLedgerPermissions.ProjectsManage, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);

        var fixedResult = await transactions.SaveProjectJobAsync(new(null, "JOB-FIXED-BILL", "Progress billing", customerId, new DateOnly(2026, 8, 1), null, "FixedPrice", 10_000m, 7_000m, 0.05m));
        Assert.True(fixedResult.Succeeded, fixedResult.ErrorMessage);
        var fixedRequest = new ProjectBillingPreviewRequest(fixedResult.Id!.Value, "PB-FIXED-1", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30), "4000", "August progress billing", ProgressPercentToDate: 0.25m, IncludeLabor: false, IncludeCosts: false);
        var fixedPreview = await transactions.PreviewProjectBillingAsync(fixedRequest);
        Assert.True(fixedPreview.Succeeded, fixedPreview.ErrorMessage);
        Assert.Equal("FixedPriceProgress", fixedPreview.BillingBasis);
        Assert.Equal(2_500m, fixedPreview.GrossAmount); Assert.Equal(125m, fixedPreview.RetainageAmount); Assert.Equal(2_375m, fixedPreview.InvoiceAmount);
        var fixedSaved = await transactions.SaveProjectBillingProposalAsync(new(null, fixedRequest, fixedPreview.Fingerprint, fixedPreview.ProjectConcurrencyToken));
        Assert.True(fixedSaved.Succeeded, fixedSaved.ErrorMessage);
        var regressive = await transactions.PreviewProjectBillingAsync(fixedRequest with { InvoiceNumber = "PB-FIXED-2", ProgressPercentToDate = 0.20m });
        Assert.False(regressive.Succeeded); Assert.Contains("does not exceed", regressive.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var overContract = await transactions.PreviewProjectBillingAsync(fixedRequest with { InvoiceNumber = "PB-FIXED-3", ProgressPercentToDate = 0m, MilestoneAmount = 8_000m });
        Assert.False(overContract.Succeeded); Assert.Contains("authorized", overContract.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        ProjectBillingProposal fixedProposal; SubledgerDocumentWorkflow fixedWorkflow; ProjectJob fixedProject;
        await using (var db = await factory.CreateDbContextAsync()) { fixedProposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == fixedSaved.Id); fixedWorkflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == fixedProposal.SubledgerDocumentWorkflowId); fixedProject = await db.ProjectJobs.SingleAsync(x => x.Id == fixedResult.Id); }
        var changedTerms = await transactions.SaveProjectJobAsync(new(fixedProject.Id, fixedProject.JobNumber, fixedProject.Name, customerId, fixedProject.StartDate!.Value, fixedProject.ExpectedEndDate, fixedProject.BillingMethod, fixedProject.ContractAmount, fixedProject.BudgetAmount, 0.06m, fixedProject.ConcurrencyToken));
        Assert.False(changedTerms.Succeeded); Assert.Contains("billing history", changedTerms.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var renamed = await transactions.SaveProjectJobAsync(new(fixedProject.Id, fixedProject.JobNumber, "Progress billing renamed", customerId, fixedProject.StartDate.Value, fixedProject.ExpectedEndDate, fixedProject.BillingMethod, fixedProject.ContractAmount, fixedProject.BudgetAmount, fixedProject.RetainagePercent, fixedProject.ConcurrencyToken));
        Assert.True(renamed.Succeeded, renamed.ErrorMessage);
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        var staleApproval = await transactions.ApproveSubledgerDocumentAsync(fixedWorkflow.Id);
        Assert.False(staleApproval.Succeeded); Assert.Contains("project", staleApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase); Assert.Contains("fresh preview", staleApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True((await transactions.RejectSubledgerDocumentAsync(new(fixedWorkflow.Id, "Project changed after billing preparation", fixedWorkflow.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) fixedProposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == fixedProposal.Id);
        SetUser(userId, BrassLedgerPermissions.ProjectsManage, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var correctedFixedRequest = fixedRequest with { ExistingProposalId = fixedProposal.Id, Description = "August progress billing after project review" };
        var correctedFixedPreview = await transactions.PreviewProjectBillingAsync(correctedFixedRequest); Assert.True(correctedFixedPreview.Succeeded, correctedFixedPreview.ErrorMessage);
        Assert.True((await transactions.SaveProjectBillingProposalAsync(new(fixedProposal.Id, correctedFixedRequest, correctedFixedPreview.Fingerprint, correctedFixedPreview.ProjectConcurrencyToken, fixedProposal.ConcurrencyToken))).Succeeded);
        Guid retainageAccountId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            fixedProposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == fixedProposal.Id);
            fixedWorkflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == fixedProposal.SubledgerDocumentWorkflowId);
            var retainageAccount = await db.Accounts.SingleAsync(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.RetainageReceivable);
            retainageAccountId = retainageAccount.Id; retainageAccount.OperationalRole = null; await db.SaveChangesAsync();
        }
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        var missingRetainageAccount = await transactions.ApproveSubledgerDocumentAsync(fixedWorkflow.Id); Assert.False(missingRetainageAccount.Succeeded); Assert.Contains("retainage-receivable control account", missingRetainageAccount.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync()) { (await db.Accounts.SingleAsync(x => x.Id == retainageAccountId)).OperationalRole = AccountingAccountRoles.RetainageReceivable; await db.SaveChangesAsync(); }
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove); Assert.True((await transactions.ApproveSubledgerDocumentAsync(fixedWorkflow.Id)).Succeeded);
        SetUser(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost); var fixedPosted = await transactions.PostApprovedSubledgerDocumentAsync(fixedWorkflow.Id); Assert.True(fixedPosted.Succeeded, fixedPosted.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(receivablesBefore + 2_375m, await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(x => x.CurrentBalance).SingleAsync());
            Assert.Equal(retainageBefore + 125m, await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.RetainageReceivable).Select(x => x.CurrentBalance).SingleAsync());
            Assert.Equal(revenueBefore + 2_500m, await db.Accounts.Where(x => x.CompanyId == companyId && x.Number == "4000").Select(x => x.CurrentBalance).SingleAsync());
        }
        var retainedWorkspace = await workspaceService.GetWorkspaceAsync();
        var retainedAging = Assert.Single(retainedWorkspace.Projects.RetainageAging!, item => item.ProposalId == fixedProposal.Id);
        Assert.Equal(125m, retainedWorkspace.Projects.RetainageReceivable); Assert.Equal(retainageBefore + 125m, retainedWorkspace.Projects.RetainageControlBalance); Assert.Equal(retainageBefore, retainedWorkspace.Projects.RetainageReconciliationDifference); Assert.Equal(125m, retainedAging.OutstandingAmount); Assert.Equal(125m, retainedAging.Days0To30 + retainedAging.Days31To60 + retainedAging.Days61To90 + retainedAging.DaysOver90);
        SetUser(userId, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var customer = await db.Customers.SingleAsync(x => x.Id == customerId);
            customer.CreditLimit = customer.OpenBalance + 125m;
            await db.SaveChangesAsync();
        }
        var retainedCreditBlocked = await transactions.SaveInvoiceDraftAsync(new(customerId, "PB-RETAINED-CREDIT", new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), 1m, 0m, "4000", "Retainage must consume customer credit"));
        Assert.False(retainedCreditBlocked.Succeeded); Assert.Contains("outstanding retainage", retainedCreditBlocked.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var releaseRequest = fixedRequest with { InvoiceNumber = "PB-FIXED-RET-1", Description = "Partial retainage release", ProgressPercentToDate = 0m, RetainageReleaseOfProposalId = fixedProposal.Id, RetainageReleaseAmount = 60m };
        var releasePreview = await transactions.PreviewProjectBillingAsync(releaseRequest); Assert.True(releasePreview.Succeeded, releasePreview.ErrorMessage); Assert.Equal("RetainageRelease", releasePreview.BillingBasis); Assert.Equal(60m, releasePreview.InvoiceAmount); Assert.Equal(0m, releasePreview.RetainageAmount);
        var releaseSaved = await transactions.SaveProjectBillingProposalAsync(new(null, releaseRequest, releasePreview.Fingerprint, releasePreview.ProjectConcurrencyToken)); Assert.True(releaseSaved.Succeeded, releaseSaved.ErrorMessage);
        var excessiveRelease = await transactions.PreviewProjectBillingAsync(releaseRequest with { InvoiceNumber = "PB-FIXED-RET-2", RetainageReleaseAmount = 70m }); Assert.False(excessiveRelease.Succeeded); Assert.Contains("65.00", excessiveRelease.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ProjectBillingProposal releaseProposal; SubledgerDocumentWorkflow releaseWorkflow;
        await using (var db = await factory.CreateDbContextAsync()) { releaseProposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == releaseSaved.Id); releaseWorkflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == releaseProposal.SubledgerDocumentWorkflowId); }
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove); Assert.True((await transactions.ApproveSubledgerDocumentAsync(releaseWorkflow.Id)).Succeeded);
        SetUser(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost); var releasePosted = await transactions.PostApprovedSubledgerDocumentAsync(releaseWorkflow.Id); Assert.True(releasePosted.Succeeded, releasePosted.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(receivablesBefore + 2_435m, await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(x => x.CurrentBalance).SingleAsync());
            Assert.Equal(retainageBefore + 65m, await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.RetainageReceivable).Select(x => x.CurrentBalance).SingleAsync());
            Assert.Equal(revenueBefore + 2_500m, await db.Accounts.Where(x => x.CompanyId == companyId && x.Number == "4000").Select(x => x.CurrentBalance).SingleAsync());
        }
        var releasedWorkspace = await workspaceService.GetWorkspaceAsync();
        var releasedAging = Assert.Single(releasedWorkspace.Projects.RetainageAging!, item => item.ProposalId == fixedProposal.Id);
        Assert.Equal(65m, releasedWorkspace.Projects.RetainageReceivable); Assert.Equal(retainageBefore + 65m, releasedWorkspace.Projects.RetainageControlBalance); Assert.Equal(retainageBefore, releasedWorkspace.Projects.RetainageReconciliationDifference); Assert.Equal(60m, releasedAging.ReleasedAmount); Assert.Equal(65m, releasedAging.OutstandingAmount);
        SetUser(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.PaymentReverse);
        var blockedOriginalVoid = await transactions.VoidInvoiceAsync(new(fixedPosted.Id!.Value, new DateOnly(2026, 9, 1), "Original cannot bypass its retainage release")); Assert.False(blockedOriginalVoid.Succeeded); Assert.Contains("retainage release", blockedOriginalVoid.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var voidedRelease = await transactions.VoidInvoiceAsync(new(releasePosted.Id!.Value, new DateOnly(2026, 9, 2), "Release was entered before the holdback was approved")); Assert.True(voidedRelease.Succeeded, voidedRelease.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(receivablesBefore + 2_375m, await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(x => x.CurrentBalance).SingleAsync());
            Assert.Equal(retainageBefore + 125m, await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.RetainageReceivable).Select(x => x.CurrentBalance).SingleAsync());
            Assert.Equal(revenueBefore + 2_500m, await db.Accounts.Where(x => x.CompanyId == companyId && x.Number == "4000").Select(x => x.CurrentBalance).SingleAsync());
        }
        Assert.Equal(125m, (await workspaceService.GetWorkspaceAsync()).Projects.RetainageReceivable);
        var voidedOriginal = await transactions.VoidInvoiceAsync(new(fixedPosted.Id.Value, new DateOnly(2026, 9, 3), "The progress billing was cancelled")); Assert.True(voidedOriginal.Succeeded, voidedOriginal.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(receivablesBefore, await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.AccountsReceivable).Select(x => x.CurrentBalance).SingleAsync());
            Assert.Equal(retainageBefore, await db.Accounts.Where(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.RetainageReceivable).Select(x => x.CurrentBalance).SingleAsync());
            Assert.Equal(revenueBefore, await db.Accounts.Where(x => x.CompanyId == companyId && x.Number == "4000").Select(x => x.CurrentBalance).SingleAsync());
        }
        var voidedWorkspace = await workspaceService.GetWorkspaceAsync(); Assert.Equal(0m, voidedWorkspace.Projects.RetainageReceivable); Assert.DoesNotContain(voidedWorkspace.Projects.RetainageAging!, item => item.ProposalId == fixedProposal.Id);
        SetUser(userId, BrassLedgerPermissions.ProjectsManage, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);

        var costPlusResult = await transactions.SaveProjectJobAsync(new(null, "JOB-COSTPLUS-BILL", "Cost-plus billing", customerId, new DateOnly(2026, 8, 1), null, "CostPlus", 5_000m, 3_000m, 0m));
        Assert.True(costPlusResult.Succeeded, costPlusResult.ErrorMessage);
        Guid costLineId; Guid costJournalId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var expense = await db.Accounts.SingleAsync(x => x.CompanyId == companyId && x.Number == "5100");
            var offset = await db.Accounts.SingleAsync(x => x.CompanyId == companyId && x.Number == "4000");
            var entry = new JournalEntry { Id = Guid.NewGuid(), CompanyId = companyId, EntryNumber = "CP-COST-1", PostedOn = new DateOnly(2026, 8, 20), SourceModule = "General Ledger", Reference = "CP-COST-1", Description = "Eligible subcontractor cost", TotalAmount = 100m, Status = "Posted", IsPosted = true, CreatedAtUtc = DateTimeOffset.UtcNow, PostedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
            costJournalId = entry.Id;
            costLineId = Guid.NewGuid(); db.JournalEntries.Add(entry);
            db.JournalEntryLines.AddRange(new JournalEntryLine { Id = costLineId, JournalEntryId = entry.Id, AccountId = expense.Id, ProjectJobId = costPlusResult.Id, Description = "Subcontractor cost", Debit = 100m }, new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = entry.Id, AccountId = offset.Id, ProjectJobId = costPlusResult.Id, Description = "Test offset", Credit = 100m });
            await db.SaveChangesAsync();
        }
        var costRequest = new ProjectBillingPreviewRequest(costPlusResult.Id!.Value, "PB-CP-1", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30), "4000", "August cost-plus billing", CostMarkupPercent: 0.20m, IncludeLabor: false, IncludeCosts: true, SelectedJournalEntryLineIds: [costLineId]);
        var costPreview = await transactions.PreviewProjectBillingAsync(costRequest);
        Assert.True(costPreview.Succeeded, costPreview.ErrorMessage);
        var costLine = Assert.Single(costPreview.Lines);
        Assert.Equal("PostedCost", costLine.SourceType); Assert.Equal(100m, costLine.SourceCost); Assert.Equal(20m, costLine.MarkupAmount); Assert.Equal(120m, costLine.GrossAmount);
        var costSaved = await transactions.SaveProjectBillingProposalAsync(new(null, costRequest, costPreview.Fingerprint, costPreview.ProjectConcurrencyToken));
        Assert.True(costSaved.Succeeded, costSaved.ErrorMessage);
        SetUser(userId, BrassLedgerPermissions.JournalReverse);
        var reservedCostReversal = await transactions.ReverseJournalEntryAsync(new(costJournalId, new DateOnly(2026, 9, 1), "Cannot reverse a reserved billed cost"));
        Assert.False(reservedCostReversal.Succeeded); Assert.Contains("project billing", reservedCostReversal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        ProjectBillingProposal costProposal; SubledgerDocumentWorkflow costWorkflow;
        await using (var db = await factory.CreateDbContextAsync())
        {
            costProposal = await db.ProjectBillingProposals.SingleAsync(x => x.Id == costSaved.Id);
            costWorkflow = await db.SubledgerDocumentWorkflows.SingleAsync(x => x.Id == costProposal.SubledgerDocumentWorkflowId);
            var sourceJournal = await db.JournalEntries.SingleAsync(x => x.Id == costJournalId);
            sourceJournal.Status = "Reversed";
            sourceJournal.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        var reversedSourceApproval = await transactions.ApproveSubledgerDocumentAsync(costWorkflow.Id);
        Assert.False(reversedSourceApproval.Succeeded); Assert.Contains("reversed", reversedSourceApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetUser(userId, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var reversedPreview = await transactions.PreviewProjectBillingAsync(costRequest with { InvoiceNumber = "PB-CP-2" });
        Assert.False(reversedPreview.Succeeded); Assert.Contains("No eligible", reversedPreview.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TransactionResult> PostInvoiceThroughWorkflowAsync(IAccountingTransactionService transactions, CreateInvoiceRequest request)
    {
        var draft = await transactions.SaveInvoiceDraftAsync(request);
        if (!draft.Succeeded) return draft;
        var approval = await transactions.ApproveSubledgerDocumentAsync(draft.Id!.Value);
        return approval.Succeeded ? await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value) : approval;
    }

    private static async Task<TransactionResult> PostJournalThroughWorkflowAsync(IAccountingTransactionService transactions, SaveJournalEntryDraftRequest request)
    {
        var draft = await transactions.SaveJournalEntryDraftAsync(request);
        if (!draft.Succeeded) return draft;
        var approval = await transactions.ApproveJournalEntryAsync(draft.Id!.Value);
        return approval.Succeeded ? await transactions.PostApprovedJournalEntryAsync(draft.Id.Value) : approval;
    }

    private static async Task<TransactionResult> PostVendorBillThroughWorkflowAsync(IAccountingTransactionService transactions, CreateVendorBillRequest request)
    {
        var draft = await transactions.SaveVendorBillDraftAsync(request);
        if (!draft.Succeeded) return draft;
        var approval = await transactions.ApproveSubledgerDocumentAsync(draft.Id!.Value);
        return approval.Succeeded ? await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value) : approval;
    }

    private static async Task<TransactionResult> PostEmployeePayrollThroughWorkflowAsync(
        IAccountingTransactionService transactions,
        IDbContextFactory<BrassLedgerDbContext> factory,
        PostEmployeePayrollRunRequest request)
    {
        var draft = await transactions.SaveEmployeePayrollRunDraftAsync(request);
        if (!draft.Succeeded) return draft;
        string token;
        await using (var db = await factory.CreateDbContextAsync())
            token = await db.PayrollRuns.Where(run => run.Id == draft.Id).Select(run => run.ConcurrencyToken).SingleAsync();
        var approval = await transactions.ApprovePayrollRunAsync(new ApprovePayrollRunRequest(draft.Id!.Value, token));
        if (!approval.Succeeded) return approval;
        await using (var db = await factory.CreateDbContextAsync())
            token = await db.PayrollRuns.Where(run => run.Id == draft.Id).Select(run => run.ConcurrencyToken).SingleAsync();
        return await transactions.PostApprovedPayrollRunAsync(new PostApprovedPayrollRunRequest(draft.Id.Value, token));
    }

    private static void AddFederalLiability(BrassLedgerDbContext db, Employee employee, DateOnly payDate, string reference, decimal amount)
    {
        var runId = Guid.NewGuid(); var lineId = Guid.NewGuid();
        db.PayrollRuns.Add(new PayrollRun { Id = runId, CompanyId = employee.CompanyId, BankAccountId = db.BankAccounts.First(item => item.CompanyId == employee.CompanyId).Id, PayDate = payDate, PeriodStart = payDate, PeriodEnd = payDate, RunType = "Regular", Status = "Posted", Reference = reference, GrossPayroll = amount, EmployeeWithholdings = amount, NetPay = 0, PreparedAtUtc = DateTimeOffset.UtcNow, PostedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") });
        db.PayrollRunEmployeeLines.Add(new PayrollRunEmployeeLine { Id = lineId, PayrollRunId = runId, EmployeeId = employee.Id, GrossPay = amount, TaxableWages = amount, EmployeeWithholdings = amount, NetPay = 0 });
        db.PayrollLiabilities.Add(new PayrollLiability { Id = Guid.NewGuid(), CompanyId = employee.CompanyId, PayrollRunId = runId, PayrollRunEmployeeLineId = lineId, SourceType = "Tax", SourceLineId = Guid.NewGuid(), ObligationCode = "US-FIT", JurisdictionCode = "US", JurisdictionName = "Federal", Description = "Federal income tax withholding", LiabilityAccountNumber = db.Accounts.First(account => account.CompanyId == employee.CompanyId && account.OperationalRole == AccountingAccountRoles.PayrollLiabilities).Number, OriginalAmount = amount, OutstandingAmount = amount, Status = "Open", ConcurrencyToken = Guid.NewGuid().ToString("N") });
    }

    private static async Task<(Guid MatchId, Guid VendorBillId)> PostControlledPurchaseInvoiceAsync(
        IAccountingTransactionService transactions,
        IDbContextFactory<BrassLedgerDbContext> factory,
        Guid inventoryReceiptId,
        string billNumber,
        DateOnly billDate,
        DateOnly dueDate,
        string description)
    {
        string receiptToken;
        PurchaseInvoiceMatchLineRequest[] requestedLines;
        await using (var db = await factory.CreateDbContextAsync())
        {
            receiptToken = await db.InventoryReceipts.Where(receipt => receipt.Id == inventoryReceiptId).Select(receipt => receipt.ConcurrencyToken).SingleAsync();
            requestedLines = await db.InventoryReceiptLines.Where(line => line.InventoryReceiptId == inventoryReceiptId && line.Quantity > line.ReturnedQuantity).OrderBy(line => line.Sequence).Select(line => new PurchaseInvoiceMatchLineRequest(line.Id, line.Quantity - line.ReturnedQuantity, line.UnitCost)).ToArrayAsync();
        }

        var saved = await transactions.SavePurchaseInvoiceMatchAsync(new(null, inventoryReceiptId, billNumber, billDate, dueDate, description, requestedLines, receiptToken));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        PurchaseInvoiceMatch match;
        await using (var db = await factory.CreateDbContextAsync()) match = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == saved.Id);
        var submitted = await transactions.SubmitPurchaseInvoiceMatchAsync(new(match.Id, match.ConcurrencyToken));
        Assert.True(submitted.Succeeded, submitted.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) match = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == saved.Id);
        var approved = await transactions.DecidePurchaseInvoiceMatchAsync(new(match.Id, true, "Receipt quantities and supplier invoice reviewed", match.ConcurrencyToken));
        Assert.True(approved.Succeeded, approved.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) match = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == saved.Id);
        var posted = await transactions.PostPurchaseInvoiceMatchAsync(new(match.Id, match.ConcurrencyToken));
        Assert.True(posted.Succeeded, posted.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync()) match = await db.PurchaseInvoiceMatches.SingleAsync(item => item.Id == saved.Id);
        return (match.Id, match.VendorBillId!.Value);
    }

    [Fact]
    public async Task ProjectWip_ControlsCumulativeRevenueContractPositionAndExactReversal()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        Guid companyId; Guid customerId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            companyId = await db.Companies.Select(x => x.Id).SingleAsync();
            customerId = await db.Customers.OrderBy(x => x.CustomerNumber).Select(x => x.Id).FirstAsync();
        }
        void SetUser(Guid userId, params string[] permissions)
        {
            var claims = permissions.Select(permission => new System.Security.Claims.Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)).ToList();
            claims.Add(new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()));
            claims.Add(new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()));
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")) };
        }
        var preparerId = Guid.NewGuid(); var reviewerId = Guid.NewGuid(); var posterId = Guid.NewGuid();
        SetUser(preparerId, BrassLedgerPermissions.ProjectsManage);
        var projectResult = await transactions.SaveProjectJobAsync(new(null, "JOB-WIP-1", "Controlled WIP", customerId, new DateOnly(2026, 8, 1), null, "FixedPrice", 10_000m, 5_000m, 0m, RevenueRecognitionMethod: "CostToCost"));
        Assert.True(projectResult.Succeeded, projectResult.ErrorMessage);
        var projectId = projectResult.Id!.Value;

        SetUser(preparerId, BrassLedgerPermissions.JournalPrepare);
        var costDraft = await transactions.SaveJournalEntryDraftAsync(new(null, new DateOnly(2026, 8, 20), "WIP-COST-1", "Project costs through August", [new("5100", 1_000m, 0m, "Project costs", projectId), new("1000", 0m, 1_000m, "Cash cost", projectId)]));
        Assert.True(costDraft.Succeeded, costDraft.ErrorMessage);
        SetUser(reviewerId, BrassLedgerPermissions.JournalApprove); Assert.True((await transactions.ApproveJournalEntryAsync(costDraft.Id!.Value)).Succeeded);
        SetUser(posterId, BrassLedgerPermissions.JournalPost); Assert.True((await transactions.PostApprovedJournalEntryAsync(costDraft.Id.Value)).Succeeded);

        var firstRequest = new ProjectWipPreviewRequest(projectId, new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), "4000", "August WIP true-up");
        SetUser(preparerId, BrassLedgerPermissions.ProjectWipPrepare);
        var firstPreview = await transactions.PreviewProjectWipScheduleAsync(firstRequest);
        Assert.True(firstPreview.Succeeded, firstPreview.ErrorMessage);
        Assert.Equal(1_000m, firstPreview.ActualCostToDate); Assert.Equal(0.2m, firstPreview.CompletionPercent); Assert.Equal(2_000m, firstPreview.EarnedRevenueToDate); Assert.Equal(2_000m, firstPreview.DesiredContractAsset); Assert.Equal(2_000m, firstPreview.RevenueAdjustment);
        var firstSaved = await transactions.SaveProjectWipScheduleAsync(new(null, firstRequest, firstPreview.Fingerprint, firstPreview.ProjectConcurrencyToken));
        Assert.True(firstSaved.Succeeded, firstSaved.ErrorMessage);
        ProjectWipSchedule first;
        await using (var db = await factory.CreateDbContextAsync()) first = await db.ProjectWipSchedules.SingleAsync(x => x.Id == firstSaved.Id);
        Assert.True((await transactions.SubmitProjectWipScheduleAsync(new(first.Id, first.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) first = await db.ProjectWipSchedules.SingleAsync(x => x.Id == first.Id);
        SetUser(preparerId, BrassLedgerPermissions.ProjectWipApprove);
        var selfApproval = await transactions.DecideProjectWipScheduleAsync(new(first.Id, true, "Self review must fail", first.ConcurrencyToken)); Assert.False(selfApproval.Succeeded);
        SetUser(reviewerId, BrassLedgerPermissions.ProjectWipApprove); Assert.True((await transactions.DecideProjectWipScheduleAsync(new(first.Id, true, "Cost and contract reviewed", first.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) first = await db.ProjectWipSchedules.SingleAsync(x => x.Id == first.Id);
        SetUser(reviewerId, BrassLedgerPermissions.ProjectWipPost); Assert.False((await transactions.PostProjectWipScheduleAsync(new(first.Id, first.ConcurrencyToken))).Succeeded);
        SetUser(posterId, BrassLedgerPermissions.ProjectWipPost); Assert.True((await transactions.PostProjectWipScheduleAsync(new(first.Id, first.ConcurrencyToken))).Succeeded);

        await using (var db = await factory.CreateDbContextAsync())
        {
            first = await db.ProjectWipSchedules.SingleAsync(x => x.Id == first.Id);
            var asset = await db.Accounts.SingleAsync(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.ContractAsset);
            Assert.Equal(2_000m, asset.CurrentBalance);
            var journalLines = await db.JournalEntryLines.Where(x => x.JournalEntryId == first.JournalEntryId).ToListAsync();
            Assert.Equal(2, journalLines.Count); Assert.All(journalLines, line => Assert.Equal(projectId, line.ProjectJobId));
        }

        SetUser(preparerId, BrassLedgerPermissions.ProjectBillingPrepare, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var billingRequest = new ProjectBillingPreviewRequest(projectId, "WIP-BILL-1", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 30), "4000", "September progress billing", ProgressPercentToDate: 0.3m);
        var billingPreview = await transactions.PreviewProjectBillingAsync(billingRequest); Assert.True(billingPreview.Succeeded, billingPreview.ErrorMessage);
        var billingSaved = await transactions.SaveProjectBillingProposalAsync(new(null, billingRequest, billingPreview.Fingerprint, billingPreview.ProjectConcurrencyToken)); Assert.True(billingSaved.Succeeded, billingSaved.ErrorMessage);
        Guid billingWorkflowId;
        await using (var db = await factory.CreateDbContextAsync()) billingWorkflowId = await db.ProjectBillingProposals.Where(x => x.Id == billingSaved.Id).Select(x => x.SubledgerDocumentWorkflowId).SingleAsync();
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove); Assert.True((await transactions.ApproveSubledgerDocumentAsync(billingWorkflowId)).Succeeded);
        SetUser(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost); Assert.True((await transactions.PostApprovedSubledgerDocumentAsync(billingWorkflowId)).Succeeded);

        var secondRequest = firstRequest with { ThroughDate = new DateOnly(2026, 9, 30), PostingDate = new DateOnly(2026, 9, 30), Description = "September WIP true-up" };
        SetUser(preparerId, BrassLedgerPermissions.ProjectWipPrepare);
        var secondPreview = await transactions.PreviewProjectWipScheduleAsync(secondRequest); Assert.True(secondPreview.Succeeded, secondPreview.ErrorMessage);
        Assert.Equal(3_000m, secondPreview.BilledRevenueToDate); Assert.Equal(2_000m, secondPreview.PriorContractAsset); Assert.Equal(1_000m, secondPreview.DesiredContractLiability); Assert.Equal(-3_000m, secondPreview.RevenueAdjustment);
        var secondSaved = await transactions.SaveProjectWipScheduleAsync(new(null, secondRequest, secondPreview.Fingerprint, secondPreview.ProjectConcurrencyToken)); Assert.True(secondSaved.Succeeded, secondSaved.ErrorMessage);
        ProjectWipSchedule second;
        await using (var db = await factory.CreateDbContextAsync()) second = await db.ProjectWipSchedules.SingleAsync(x => x.Id == secondSaved.Id);
        Assert.True((await transactions.SubmitProjectWipScheduleAsync(new(second.Id, second.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) second = await db.ProjectWipSchedules.SingleAsync(x => x.Id == second.Id);
        SetUser(reviewerId, BrassLedgerPermissions.ProjectWipApprove); Assert.True((await transactions.DecideProjectWipScheduleAsync(new(second.Id, true, "Cumulative position reviewed", second.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) second = await db.ProjectWipSchedules.SingleAsync(x => x.Id == second.Id);
        SetUser(posterId, BrassLedgerPermissions.ProjectWipPost); Assert.True((await transactions.PostProjectWipScheduleAsync(new(second.Id, second.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            second = await db.ProjectWipSchedules.SingleAsync(x => x.Id == second.Id);
            Assert.Equal(0m, (await db.Accounts.SingleAsync(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.ContractAsset)).CurrentBalance);
            Assert.Equal(1_000m, (await db.Accounts.SingleAsync(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.ContractLiability)).CurrentBalance);
            var projectRevenue = await (from line in db.JournalEntryLines join entry in db.JournalEntries on line.JournalEntryId equals entry.Id join account in db.Accounts on line.AccountId equals account.Id where entry.CompanyId == companyId && entry.IsPosted && line.ProjectJobId == projectId && account.Type == AccountType.Revenue select line.Credit - line.Debit).ToListAsync();
            Assert.Equal(2_000m, projectRevenue.Sum());
        }
        var reconciledWorkspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        Assert.Equal(0m, reconciledWorkspace.Projects.ContractAssetReconciliationDifference); Assert.Equal(0m, reconciledWorkspace.Projects.ContractLiabilityReconciliationDifference);
        Assert.Equal(1_000m, reconciledWorkspace.Projects.ContractLiabilitySubledger);
        accessor.HttpContext = CreatePermissionContext(BrassLedgerPermissions.ProjectWipReverse);
        var unidentifiedReversal = await transactions.ReverseProjectWipScheduleAsync(new(second.Id, new DateOnly(2026, 10, 1), "Missing actor must fail", second.ConcurrencyToken));
        Assert.False(unidentifiedReversal.Succeeded); Assert.Contains("identity", unidentifiedReversal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetUser(posterId, BrassLedgerPermissions.ProjectWipReverse);
        var reversed = await transactions.ReverseProjectWipScheduleAsync(new(second.Id, new DateOnly(2026, 10, 1), "Reverse September WIP for correction", second.ConcurrencyToken)); Assert.True(reversed.Succeeded, reversed.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
        {
            second = await db.ProjectWipSchedules.SingleAsync(x => x.Id == second.Id); Assert.Equal("Reversed", second.Status); Assert.NotNull(second.ReversalJournalEntryId);
            Assert.Equal(2_000m, (await db.Accounts.SingleAsync(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.ContractAsset)).CurrentBalance);
            Assert.Equal(0m, (await db.Accounts.SingleAsync(x => x.CompanyId == companyId && x.OperationalRole == AccountingAccountRoles.ContractLiability)).CurrentBalance);
            Assert.Equal(1, await db.BusinessAuditEntries.CountAsync(x => x.EntityId == second.Id && x.Action == "project-wip.reversed"));
        }
        var restoredWorkspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        Assert.Equal(2_000m, restoredWorkspace.Projects.ContractAssetSubledger); Assert.Equal(0m, restoredWorkspace.Projects.ContractAssetReconciliationDifference); Assert.Equal(0m, restoredWorkspace.Projects.ContractLiabilityReconciliationDifference);

        var ordinaryInvoiceRequest = new CreateInvoiceRequest(customerId, "WIP-ORDINARY-1", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 0m, 0m, "4000", "Ordinary project-tagged invoice",
            [new SalesInvoiceLineRequest("Additional project billing", 1m, 500m, 0m, 0m, "4000", projectId)]);
        SetUser(preparerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPrepare);
        var ordinaryDraft = await transactions.SaveInvoiceDraftAsync(ordinaryInvoiceRequest); Assert.True(ordinaryDraft.Succeeded, ordinaryDraft.ErrorMessage);
        SetUser(reviewerId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerApprove);
        Assert.True((await transactions.ApproveSubledgerDocumentAsync(ordinaryDraft.Id!.Value)).Succeeded);
        SetUser(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.SubledgerPost);
        var ordinaryPosted = await transactions.PostApprovedSubledgerDocumentAsync(ordinaryDraft.Id.Value); Assert.True(ordinaryPosted.Succeeded, ordinaryPosted.ErrorMessage);

        var ordinaryRequest = secondRequest with { ThroughDate = new DateOnly(2026, 10, 31), PostingDate = new DateOnly(2026, 10, 31), Description = "October WIP with ordinary project billing" };
        SetUser(preparerId, BrassLedgerPermissions.ProjectWipPrepare);
        var ordinaryPreview = await transactions.PreviewProjectWipScheduleAsync(ordinaryRequest); Assert.True(ordinaryPreview.Succeeded, ordinaryPreview.ErrorMessage);
        Assert.Equal(3_500m, ordinaryPreview.BilledRevenueToDate);

        SetUser(posterId, BrassLedgerPermissions.ReceivablesManage, BrassLedgerPermissions.PaymentReverse);
        var ordinaryVoid = await transactions.VoidInvoiceAsync(new(ordinaryPosted.Id!.Value, new DateOnly(2026, 10, 2), "Remove ordinary project billing")); Assert.True(ordinaryVoid.Succeeded, ordinaryVoid.ErrorMessage);
        SetUser(preparerId, BrassLedgerPermissions.ProjectWipPrepare);
        var afterVoidPreview = await transactions.PreviewProjectWipScheduleAsync(ordinaryRequest); Assert.True(afterVoidPreview.Succeeded, afterVoidPreview.ErrorMessage);
        Assert.Equal(3_000m, afterVoidPreview.BilledRevenueToDate);

        SetUser(preparerId, BrassLedgerPermissions.ProjectsManage);
        var completedResult = await transactions.SaveProjectJobAsync(new(null, "JOB-WIP-CC", "Completed-contract WIP", customerId, new DateOnly(2026, 10, 1), null, "FixedPrice", 1_000m, 500m, 0m, RevenueRecognitionMethod: "CompletedContract"));
        Assert.True(completedResult.Succeeded, completedResult.ErrorMessage);
        ProjectJob completedProject;
        await using (var db = await factory.CreateDbContextAsync()) completedProject = await db.ProjectJobs.SingleAsync(x => x.Id == completedResult.Id);
        var completedClose = await transactions.CloseProjectJobAsync(new(completedProject.Id, new DateOnly(2026, 10, 31), "Contract performance completed", completedProject.ConcurrencyToken)); Assert.True(completedClose.Succeeded, completedClose.ErrorMessage);

        var completedRequest = new ProjectWipPreviewRequest(completedProject.Id, new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 31), "4000", "Recognize completed contract");
        SetUser(preparerId, BrassLedgerPermissions.ProjectWipPrepare);
        var completedPreview = await transactions.PreviewProjectWipScheduleAsync(completedRequest); Assert.True(completedPreview.Succeeded, completedPreview.ErrorMessage); Assert.Equal(1_000m, completedPreview.EarnedRevenueToDate);
        var completedSaved = await transactions.SaveProjectWipScheduleAsync(new(null, completedRequest, completedPreview.Fingerprint, completedPreview.ProjectConcurrencyToken)); Assert.True(completedSaved.Succeeded, completedSaved.ErrorMessage);
        ProjectWipSchedule completedSchedule;
        await using (var db = await factory.CreateDbContextAsync()) completedSchedule = await db.ProjectWipSchedules.SingleAsync(x => x.Id == completedSaved.Id);
        Assert.True((await transactions.SubmitProjectWipScheduleAsync(new(completedSchedule.Id, completedSchedule.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) completedSchedule = await db.ProjectWipSchedules.SingleAsync(x => x.Id == completedSchedule.Id);
        SetUser(reviewerId, BrassLedgerPermissions.ProjectWipApprove); Assert.True((await transactions.DecideProjectWipScheduleAsync(new(completedSchedule.Id, true, "Completion evidence reviewed", completedSchedule.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) completedSchedule = await db.ProjectWipSchedules.SingleAsync(x => x.Id == completedSchedule.Id);
        SetUser(posterId, BrassLedgerPermissions.ProjectWipPost); Assert.True((await transactions.PostProjectWipScheduleAsync(new(completedSchedule.Id, completedSchedule.ConcurrencyToken))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) { completedProject = await db.ProjectJobs.SingleAsync(x => x.Id == completedProject.Id); completedSchedule = await db.ProjectWipSchedules.SingleAsync(x => x.Id == completedSchedule.Id); }
        SetUser(preparerId, BrassLedgerPermissions.ProjectsManage);
        var unsafeReopen = await transactions.ReopenProjectJobAsync(new(completedProject.Id, "Additional work discovered", completedProject.ConcurrencyToken)); Assert.False(unsafeReopen.Succeeded); Assert.Contains("Reverse", unsafeReopen.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        SetUser(posterId, BrassLedgerPermissions.ProjectWipReverse); Assert.True((await transactions.ReverseProjectWipScheduleAsync(new(completedSchedule.Id, new DateOnly(2026, 11, 1), "Reopen contract for additional work", completedSchedule.ConcurrencyToken))).Succeeded);
        SetUser(preparerId, BrassLedgerPermissions.ProjectsManage);
        var safeReopen = await transactions.ReopenProjectJobAsync(new(completedProject.Id, "Additional work discovered", completedProject.ConcurrencyToken)); Assert.True(safeReopen.Succeeded, safeReopen.ErrorMessage);
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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
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
