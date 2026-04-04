namespace BrassLedger.Application.Accounting;

public sealed record BusinessWorkspaceSnapshot(
    DateTime GeneratedAtUtc,
    CompanySnapshot Company,
    DashboardSnapshot Dashboard,
    IReadOnlyList<ModuleWorkspaceSnapshot> Modules,
    GeneralLedgerWorkspace GeneralLedger,
    ReceivablesWorkspace Receivables,
    PayablesWorkspace Payables,
    OperationsWorkspace Operations,
    TreasuryWorkspace Treasury,
    PayrollWorkspace Payroll,
    ProjectsWorkspace Projects,
    ReportingWorkspace Reporting,
    TaxWorkspace Taxes);

public sealed record CompanySnapshot(
    string Name,
    string LegalName,
    string TaxId,
    string BaseCurrency,
    int FiscalYearStartMonth,
    int ActiveUsers);

public sealed record DashboardSnapshot(
    decimal CashOnHand,
    decimal ReceivablesOpen,
    decimal PayablesOpen,
    decimal MonthlyPayroll,
    int InventoryItems,
    int OpenSalesOrders,
    int OpenProjects,
    int EnabledModules,
    int ReportsReady);

public sealed record ModuleWorkspaceSnapshot(
    string Code,
    string Name,
    string Area,
    string Status,
    string Summary,
    int RecordCount);

public sealed record GeneralLedgerWorkspace(
    decimal Assets,
    decimal Liabilities,
    decimal Equity,
    decimal Revenue,
    decimal Expenses,
    IReadOnlyList<AccountSnapshot> Accounts,
    IReadOnlyList<JournalEntrySnapshot> RecentEntries);

public sealed record AccountSnapshot(
    string Number,
    string Name,
    string Type,
    decimal Balance,
    bool IsControlAccount);

public sealed record JournalEntrySnapshot(
    string EntryNumber,
    DateOnly PostedOn,
    string SourceModule,
    string Description,
    decimal TotalAmount);

public sealed record ReceivablesWorkspace(
    decimal OpenBalance,
    int PastDueCount,
    IReadOnlyList<CustomerSnapshot> Customers,
    IReadOnlyList<InvoiceSnapshot> Invoices);

public sealed record CustomerSnapshot(
    string CustomerNumber,
    string Name,
    string State,
    decimal CreditLimit,
    decimal OpenBalance);

public sealed record InvoiceSnapshot(
    string InvoiceNumber,
    string CustomerName,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string Status,
    decimal TotalAmount,
    decimal BalanceDue);

public sealed record PayablesWorkspace(
    decimal OpenBalance,
    int DueThisWeekCount,
    IReadOnlyList<VendorSnapshot> Vendors,
    IReadOnlyList<BillSnapshot> Bills);

public sealed record VendorSnapshot(
    string VendorNumber,
    string Name,
    string State,
    string PaymentTerms,
    decimal OpenBalance);

public sealed record BillSnapshot(
    string BillNumber,
    string VendorName,
    DateOnly BillDate,
    DateOnly DueDate,
    string Status,
    decimal TotalAmount,
    decimal BalanceDue);

public sealed record OperationsWorkspace(
    int InventoryItemCount,
    int ReorderAlerts,
    int OpenSalesOrderCount,
    int OpenPurchaseOrderCount,
    IReadOnlyList<InventoryItemSnapshot> InventoryItems,
    IReadOnlyList<SalesOrderSnapshot> SalesOrders,
    IReadOnlyList<PurchaseOrderSnapshot> PurchaseOrders);

public sealed record InventoryItemSnapshot(
    string Sku,
    string Description,
    decimal UnitPrice,
    decimal QuantityOnHand,
    decimal ReorderPoint);

public sealed record SalesOrderSnapshot(
    string OrderNumber,
    string CustomerName,
    DateOnly OrderedOn,
    string Status,
    decimal TotalAmount);

public sealed record PurchaseOrderSnapshot(
    string OrderNumber,
    string VendorName,
    DateOnly OrderedOn,
    string Status,
    decimal TotalAmount);

public sealed record TreasuryWorkspace(
    decimal CashOnHand,
    decimal UnreconciledBalance,
    IReadOnlyList<BankAccountSnapshot> BankAccounts);

public sealed record BankAccountSnapshot(
    string Name,
    string AccountNumberMasked,
    decimal CurrentBalance,
    decimal UnreconciledAmount,
    DateOnly LastReconciledOn);

public sealed record PayrollWorkspace(
    int ActiveEmployees,
    decimal MonthlyGross,
    IReadOnlyList<EmployeeSnapshot> Employees);

public sealed record EmployeeSnapshot(
    string EmployeeNumber,
    string FullName,
    string Department,
    string State,
    string PayType,
    decimal MonthlyBasePay,
    bool IsActive);

public sealed record ProjectsWorkspace(
    int OpenJobs,
    decimal BudgetAmount,
    decimal ActualCost,
    IReadOnlyList<ProjectJobSnapshot> Jobs);

public sealed record ProjectJobSnapshot(
    string JobNumber,
    string Name,
    string CustomerName,
    string Status,
    decimal BudgetAmount,
    decimal ActualCost);

public sealed record ReportingWorkspace(
    int ReportCount,
    int LabelCount,
    string PreferredDesigner,
    string RenderingStrategy,
    IReadOnlyList<ReportCatalogSnapshot> Reports,
    IReadOnlyList<LabelTemplateSnapshot> Labels);

public sealed record ReportCatalogSnapshot(
    string Code,
    string Name,
    string Category,
    string LayoutType,
    string Description,
    bool SupportsVisualStudioDesign);

public sealed record LabelTemplateSnapshot(
    string Code,
    string Name,
    string StockType,
    string Description);

public sealed record TaxWorkspace(
    int ProfileCount,
    int EmployerSpecificCount,
    IReadOnlyList<TaxProfileSnapshot> Profiles);

public sealed record TaxProfileSnapshot(
    string Jurisdiction,
    string TaxType,
    decimal Rate,
    DateOnly EffectiveOn,
    string Source,
    bool IsEmployerSpecific);
