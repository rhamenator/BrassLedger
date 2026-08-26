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
    bool IsControlAccount,
    string OperationalRole = "");

public sealed record JournalEntrySnapshot(
    string EntryNumber,
    DateOnly PostedOn,
    string SourceModule,
    string Description,
    decimal TotalAmount,
    Guid Id = default,
    string Reference = "",
    string Status = "Posted",
    Guid? ReversalOfJournalEntryId = null,
    Guid? ReversedByJournalEntryId = null);

public sealed record ReceivablesWorkspace(
    decimal OpenBalance,
    int PastDueCount,
    IReadOnlyList<CustomerSnapshot> Customers,
    IReadOnlyList<InvoiceSnapshot> Invoices,
    IReadOnlyList<SubledgerPaymentSnapshot>? Payments = null,
    IReadOnlyList<SubledgerAdjustmentSnapshot>? Adjustments = null,
    IReadOnlyList<SubledgerDocumentWorkflowSnapshot>? Workflows = null);

public sealed record CustomerSnapshot(
    string CustomerNumber,
    string Name,
    string State,
    decimal CreditLimit,
    decimal OpenBalance,
    Guid Id = default);

public sealed record InvoiceSnapshot(
    string InvoiceNumber,
    string CustomerName,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string Status,
    decimal TotalAmount,
    decimal BalanceDue,
    Guid Id = default,
    IReadOnlyList<InvoiceLineSnapshot>? Lines = null,
    Guid CustomerId = default);

public sealed record InvoiceLineSnapshot(int Sequence, string Description, decimal Quantity, decimal UnitPrice, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal, string RevenueAccountNumber);

public sealed record PayablesWorkspace(
    decimal OpenBalance,
    int DueThisWeekCount,
    IReadOnlyList<VendorSnapshot> Vendors,
    IReadOnlyList<BillSnapshot> Bills,
    IReadOnlyList<SubledgerPaymentSnapshot>? Payments = null,
    IReadOnlyList<SubledgerAdjustmentSnapshot>? Adjustments = null,
    IReadOnlyList<SubledgerDocumentWorkflowSnapshot>? Workflows = null);

public sealed record VendorSnapshot(
    string VendorNumber,
    string Name,
    string State,
    string PaymentTerms,
    decimal OpenBalance,
    Guid Id = default);

public sealed record BillSnapshot(
    string BillNumber,
    string VendorName,
    DateOnly BillDate,
    DateOnly DueDate,
    string Status,
    decimal TotalAmount,
    decimal BalanceDue,
    Guid Id = default,
    IReadOnlyList<BillLineSnapshot>? Lines = null,
    Guid VendorId = default);

public sealed record BillLineSnapshot(int Sequence, string Description, decimal Quantity, decimal UnitCost, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal, string ExpenseAccountNumber);

public sealed record SubledgerPaymentSnapshot(Guid Id, string Direction, string CounterpartyName, DateOnly PaymentDate, decimal Amount, decimal AppliedAmount, decimal UnappliedAmount, string Reference, string Method, string Status, IReadOnlyList<PaymentApplicationSnapshot> Applications);
public sealed record PaymentApplicationSnapshot(Guid DocumentId, string DocumentNumber, decimal Amount);
public sealed record SubledgerAdjustmentSnapshot(Guid Id, string Subledger, string Kind, Guid CounterpartyId, string CounterpartyName, Guid? DocumentId, string DocumentNumber, Guid? PaymentId, DateOnly AdjustmentDate, decimal Amount, string Reference, string Reason, string OffsetAccountNumber, string Status, Guid JournalEntryId, Guid? ReversalJournalEntryId);
public sealed record SubledgerDocumentWorkflowSnapshot(Guid Id, string DocumentType, string DocumentNumber, string Status, bool IsRecurringTemplate, string Frequency, int FrequencyInterval, DateOnly? NextOccurrenceDate, DateOnly? EndDate, Guid? SourceTemplateId, Guid? PostedDocumentId, DateTimeOffset CreatedAtUtc, DateTimeOffset? ApprovedAtUtc, DateTimeOffset? PostedAtUtc);

public sealed record OperationsWorkspace(
    int InventoryItemCount,
    int ReorderAlerts,
    int OpenSalesOrderCount,
    int OpenPurchaseOrderCount,
    IReadOnlyList<InventoryItemSnapshot> InventoryItems,
    IReadOnlyList<SalesOrderSnapshot> SalesOrders,
    IReadOnlyList<PurchaseOrderSnapshot> PurchaseOrders,
    IReadOnlyList<InventoryReceiptSnapshot>? InventoryReceipts = null,
    IReadOnlyList<InventoryShipmentSnapshot>? InventoryShipments = null,
    IReadOnlyList<SalesQuoteSnapshot>? SalesQuotes = null,
    IReadOnlyList<InventoryWarehouseSnapshot>? Warehouses = null,
    IReadOnlyList<InventoryTransferSnapshot>? InventoryTransfers = null,
    IReadOnlyList<InventoryPickSnapshot>? InventoryPicks = null,
    IReadOnlyList<InventoryPackingSlipSnapshot>? InventoryPackingSlips = null,
    IReadOnlyList<SalesOrderBackorderPromiseSnapshot>? BackorderPromises = null,
    IReadOnlyList<CustomerReturnAuthorizationSnapshot>? CustomerReturnAuthorizations = null,
    IReadOnlyList<CustomerReturnReceiptSnapshot>? CustomerReturnReceipts = null,
    IReadOnlyList<CustomerReturnCreditSnapshot>? CustomerReturnCredits = null);

public sealed record InventoryItemSnapshot(
    string Sku,
    string Description,
    decimal UnitPrice,
    decimal QuantityOnHand,
    decimal ReorderPoint,
    Guid Id = default,
    decimal UnitCost = 0m);

public sealed record InventoryWarehouseSnapshot(Guid Id, string Code, string Name, string AddressLine1, string AddressLine2, string City, string StateOrProvince, string PostalCode, string CountryCode, bool IsDefault, bool IsActive, string ConcurrencyToken, IReadOnlyList<InventoryBinSnapshot> Bins);
public sealed record InventoryBinSnapshot(Guid Id, Guid WarehouseId, string Code, string Name, bool IsDefault, bool IsActive, string ConcurrencyToken, IReadOnlyList<InventoryLocationBalanceSnapshot> Balances);
public sealed record InventoryLocationBalanceSnapshot(Guid Id, Guid InventoryItemId, string Sku, decimal QuantityOnHand, string ConcurrencyToken);
public sealed record InventoryTransferSnapshot(Guid Id, Guid InventoryItemId, string Sku, Guid SourceWarehouseId, Guid SourceBinId, string SourceLocation, Guid DestinationWarehouseId, Guid DestinationBinId, string DestinationLocation, decimal Quantity, decimal UnitCost, DateOnly TransferDate, string Reference, string Reason, string Status, string ConcurrencyToken);
public sealed record InventoryPickSnapshot(Guid Id, Guid SalesOrderId, string OrderNumber, Guid WarehouseId, Guid BinId, string Location, string PickNumber, DateOnly PickDate, string Status, string ConcurrencyToken, IReadOnlyList<InventoryPickLineSnapshot> Lines);
public sealed record InventoryPickLineSnapshot(Guid Id, Guid SalesOrderLineId, Guid InventoryItemId, string Sku, int Sequence, decimal RequestedQuantity, decimal PickedQuantity, decimal PackedQuantity);
public sealed record InventoryPackingSlipSnapshot(Guid Id, Guid SalesOrderId, string OrderNumber, Guid InventoryPickId, Guid WarehouseId, Guid BinId, string Location, string PackingSlipNumber, DateOnly PackedOn, string Status, Guid? InventoryShipmentId, string ConcurrencyToken, IReadOnlyList<InventoryPackingSlipLineSnapshot> Lines);
public sealed record InventoryPackingSlipLineSnapshot(Guid Id, Guid InventoryPickLineId, Guid SalesOrderLineId, Guid InventoryItemId, string Sku, int Sequence, decimal Quantity);
public sealed record SalesOrderBackorderPromiseSnapshot(Guid Id, Guid SalesOrderId, string OrderNumber, Guid SalesOrderLineId, Guid InventoryItemId, string Sku, decimal PromisedQuantity, decimal FulfilledQuantity, decimal OutstandingQuantity, DateOnly PromisedShipOn, string Reason, string Status, string ConcurrencyToken);

public sealed record SalesQuoteSnapshot(
    Guid Id,
    Guid CustomerId,
    string QuoteNumber,
    string CustomerName,
    DateOnly QuotedOn,
    DateOnly ExpiresOn,
    string Status,
    bool IsExpired,
    decimal TotalAmount,
    string Notes,
    Guid? ConvertedSalesOrderId,
    string ConcurrencyToken,
    IReadOnlyList<SalesQuoteLineSnapshot> Lines);

public sealed record SalesQuoteLineSnapshot(Guid Id, int Sequence, Guid InventoryItemId, string Sku, string Description, decimal Quantity, decimal UnitPrice, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal, string RevenueAccountNumber);

public sealed record SalesOrderSnapshot(
    string OrderNumber,
    string CustomerName,
    DateOnly OrderedOn,
    string Status,
    decimal TotalAmount,
    Guid Id = default,
    Guid CustomerId = default,
    DateOnly? RequestedShipOn = null,
    string Notes = "",
    string ConcurrencyToken = "",
    IReadOnlyList<SalesOrderLineSnapshot>? Lines = null,
    Guid? SalesQuoteId = null);

public sealed record SalesOrderLineSnapshot(Guid Id, int Sequence, Guid InventoryItemId, string Sku, string Description, decimal OrderedQuantity, decimal AllocatedQuantity, decimal ShippedQuantity, decimal CancelledQuantity, decimal ReturnedQuantity, decimal InvoicedQuantity, decimal UnitPrice, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal, string RevenueAccountNumber, Guid? AllocationWarehouseId = null, Guid? AllocationBinId = null, string AllocationLocation = "");
public sealed record InventoryShipmentSnapshot(Guid Id, Guid SalesOrderId, string SalesOrderNumber, string ShipmentNumber, DateOnly ShippedOn, string Status, decimal TotalCost, Guid? SalesInvoiceId, string ConcurrencyToken, IReadOnlyList<InventoryShipmentLineSnapshot> Lines, Guid JournalEntryId, Guid? ReversalJournalEntryId, Guid? WarehouseId = null, Guid? BinId = null, string Location = "", Guid? InventoryPackingSlipId = null);
public sealed record InventoryShipmentLineSnapshot(Guid Id, Guid SalesOrderLineId, Guid InventoryItemId, string Sku, int Sequence, decimal Quantity, decimal UnitCost, decimal TotalCost);
public sealed record CustomerReturnAuthorizationSnapshot(Guid Id, Guid InventoryShipmentId, string ShipmentNumber, Guid SalesOrderId, string SalesOrderNumber, Guid CustomerId, string CustomerName, string ReturnNumber, DateOnly AuthorizedOn, string Reason, string Status, string ConcurrencyToken, IReadOnlyList<CustomerReturnAuthorizationLineSnapshot> Lines);
public sealed record CustomerReturnAuthorizationLineSnapshot(Guid Id, Guid InventoryShipmentLineId, Guid SalesOrderLineId, Guid InventoryItemId, string Sku, int Sequence, decimal AuthorizedQuantity, decimal ReceivedQuantity);
public sealed record CustomerReturnReceiptSnapshot(Guid Id, Guid CustomerReturnAuthorizationId, string ReturnNumber, string ReceiptNumber, DateOnly ReceivedOn, string Status, decimal TotalCost, Guid WarehouseId, Guid BinId, string Location, Guid JournalEntryId, Guid? ReversalJournalEntryId, string ConcurrencyToken, IReadOnlyList<CustomerReturnReceiptLineSnapshot> Lines);
public sealed record CustomerReturnReceiptLineSnapshot(Guid Id, Guid CustomerReturnAuthorizationLineId, Guid InventoryShipmentLineId, Guid SalesOrderLineId, Guid InventoryItemId, string Sku, int Sequence, decimal Quantity, decimal UnitCost, decimal TotalCost);
public sealed record CustomerReturnCreditSnapshot(Guid Id, Guid CustomerReturnReceiptId, string ReceiptNumber, Guid SalesInvoiceId, string InvoiceNumber, Guid CustomerId, string CustomerName, string CreditNumber, DateOnly CreditDate, string Reason, string Status, decimal Subtotal, decimal TaxAmount, decimal TotalAmount, decimal SourceAppliedAmount, decimal AppliedAmount, decimal RefundedAmount, decimal AvailableAmount, Guid JournalEntryId, Guid? ReversalJournalEntryId, string ConcurrencyToken, IReadOnlyList<CustomerReturnCreditApplicationSnapshot> Applications, IReadOnlyList<CustomerReturnCreditRefundSnapshot> Refunds);
public sealed record CustomerReturnCreditApplicationSnapshot(Guid Id, Guid SalesInvoiceId, string InvoiceNumber, DateOnly AppliedOn, decimal Amount, string Status, string ConcurrencyToken);
public sealed record CustomerReturnCreditRefundSnapshot(Guid Id, Guid BankAccountId, string BankAccountName, string Reference, DateOnly RefundDate, decimal Amount, string Status, Guid JournalEntryId, Guid? ReversalJournalEntryId, string ConcurrencyToken);

public sealed record PurchaseOrderSnapshot(
    string OrderNumber,
    string VendorName,
    DateOnly OrderedOn,
    string Status,
    decimal TotalAmount,
    Guid Id = default,
    Guid VendorId = default,
    DateOnly? ExpectedOn = null,
    string Notes = "",
    string ConcurrencyToken = "",
    IReadOnlyList<PurchaseOrderLineSnapshot>? Lines = null);

public sealed record PurchaseOrderLineSnapshot(Guid Id, int Sequence, Guid InventoryItemId, string Sku, string Description, decimal OrderedQuantity, decimal UnitCost, decimal ReceivedQuantity, decimal InvoicedQuantity, decimal LineTotal);
public sealed record InventoryReceiptSnapshot(Guid Id, Guid PurchaseOrderId, string PurchaseOrderNumber, string ReceiptNumber, DateOnly ReceivedOn, string Status, decimal TotalAmount, Guid? VendorBillId, string ConcurrencyToken, IReadOnlyList<InventoryReceiptLineSnapshot> Lines, Guid JournalEntryId, Guid? ReversalJournalEntryId, Guid? WarehouseId = null, Guid? BinId = null, string Location = "");
public sealed record InventoryReceiptLineSnapshot(Guid Id, Guid PurchaseOrderLineId, Guid InventoryItemId, string Sku, int Sequence, decimal Quantity, decimal UnitCost, decimal LineTotal);

public sealed record TreasuryWorkspace(
    decimal CashOnHand,
    decimal UnreconciledBalance,
    IReadOnlyList<BankAccountSnapshot> BankAccounts,
    IReadOnlyList<BankReconciliationCandidateSnapshot>? ReconciliationCandidates = null,
    IReadOnlyList<BankStatementTransactionSnapshot>? StatementTransactions = null,
    IReadOnlyList<BankStatementImportBatchSnapshot>? ImportBatches = null,
    IReadOnlyList<BankReconciliationSnapshot>? Reconciliations = null,
    IReadOnlyList<BankTransferSnapshot>? Transfers = null,
    IReadOnlyList<BankAdjustmentSnapshot>? Adjustments = null);

public sealed record BankAccountSnapshot(
    string Name,
    string AccountNumberMasked,
    decimal CurrentBalance,
    decimal UnreconciledAmount,
    DateOnly LastReconciledOn,
    Guid Id = default,
    string LedgerAccountNumber = "",
    decimal LastReconciledBalance = 0m);

public sealed record BankReconciliationCandidateSnapshot(
    Guid BankAccountId,
    Guid JournalEntryId,
    DateOnly PostedOn,
    string Reference,
    string Description,
    string SourceModule,
    decimal SignedAmount);
public sealed record BankStatementTransactionSnapshot(Guid Id, Guid BankAccountId, Guid ImportBatchId, string ExternalId, DateOnly TransactionDate, decimal Amount, string TransactionType, string Payee, string Memo, string Reference, string Status, Guid? MatchedJournalEntryId, string MatchNote);
public sealed record BankStatementImportBatchSnapshot(Guid Id, Guid BankAccountId, string FileName, string Format, string Status, int ImportedCount, int DuplicateCount, int RejectedCount, decimal DebitTotal, decimal CreditTotal, DateTimeOffset ImportedAtUtc);
public sealed record BankReconciliationSnapshot(Guid Id, Guid BankAccountId, DateOnly StatementDate, decimal OpeningBalance, decimal ClearedAmount, decimal StatementClosingBalance, decimal BookBalance, decimal Variance, string Status, string Notes, DateTimeOffset ReconciledAtUtc, DateTimeOffset? ReopenedAtUtc, string ReopenReason, int ItemCount);
public sealed record BankTransferSnapshot(Guid Id, Guid FromBankAccountId, Guid ToBankAccountId, DateOnly TransferDate, decimal Amount, string Reference, string Memo, string Status, Guid JournalEntryId, Guid InboundJournalEntryId, Guid? ReversalJournalEntryId, Guid? InboundReversalJournalEntryId, DateOnly? ReversalDate, string ReversalReason);
public sealed record BankAdjustmentSnapshot(Guid Id, Guid BankAccountId, DateOnly AdjustmentDate, decimal Amount, string Reference, string Description, string OffsetAccountNumber, string Status, Guid JournalEntryId, Guid? ReversalJournalEntryId);

public sealed record PayrollWorkspace(
    int ActiveEmployees,
    decimal MonthlyGross,
    IReadOnlyList<EmployeeSnapshot> Employees,
    IReadOnlyList<PayrollJurisdictionRuleSnapshot>? JurisdictionRules = null,
    IReadOnlyList<PayrollRunSnapshot>? Runs = null,
    IReadOnlyList<PayrollTimecardSnapshot>? Timecards = null,
    IReadOnlyList<PayrollLiabilitySnapshot>? Liabilities = null,
    IReadOnlyList<PayrollLiabilityPaymentSnapshot>? LiabilityPayments = null);

public sealed record PayrollJurisdictionRuleSnapshot(Guid Id, string ResidenceJurisdiction, string WorkJurisdiction, bool ExemptWorkWithholding, decimal ResidentCreditRate, bool IsActive, string Notes);
public sealed record PayrollRunSnapshot(Guid Id, string Reference, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly PayDate, string RunType, string Status, decimal GrossPayroll, decimal EmployeeWithholdings, decimal EmployerPayrollTaxes, decimal NetPay, string ConcurrencyToken, Guid? JournalEntryId, Guid? ReversalJournalEntryId, DateTimeOffset PreparedAtUtc, DateTimeOffset? ApprovedAtUtc, DateTimeOffset? PostedAtUtc, DateTimeOffset? ReversedAtUtc, string ReversalReason, DateTimeOffset? CancelledAtUtc = null, string CancellationReason = "", decimal EmployerBenefitContributions = 0);
public sealed record PayrollLiabilitySnapshot(Guid Id, Guid PayrollRunId, Guid EmployeeId, string EmployeeName, string SourceType, string ObligationCode, string JurisdictionCode, string JurisdictionName, string Description, string LiabilityAccountNumber, decimal OriginalAmount, decimal OutstandingAmount, string Status, DateOnly? DueDate, string DepositScheduleType, string DepositRuleCode, string DepositRuleSource, Guid? DepositScheduleConfigurationId, string ConcurrencyToken);
public sealed record PayrollLiabilityPaymentSnapshot(Guid Id, Guid BankAccountId, DateOnly PaymentDate, string Reference, string Payee, string Method, decimal Amount, string Status, Guid JournalEntryId, Guid? ReversalJournalEntryId, string ConcurrencyToken, IReadOnlyList<PayrollLiabilityPaymentApplicationSnapshot> Applications);
public sealed record PayrollLiabilityPaymentApplicationSnapshot(Guid PayrollLiabilityId, string ObligationCode, decimal Amount);
public sealed record PayrollTimecardSnapshot(Guid Id, Guid EmployeeId, string EmployeeNumber, string EmployeeName, DateOnly PeriodStart, DateOnly PeriodEnd, string Status, decimal TotalHours, decimal TotalAmount, string Notes, string ConcurrencyToken, Guid? PayrollRunId, DateTimeOffset PreparedAtUtc, DateTimeOffset? SubmittedAtUtc, DateTimeOffset? ApprovedAtUtc, DateTimeOffset? VoidedAtUtc, string VoidReason, IReadOnlyList<PayrollTimeEntrySnapshot> Entries);
public sealed record PayrollTimeEntrySnapshot(Guid Id, int Sequence, DateOnly WorkDate, string EarningCode, string EarningType, decimal Hours, decimal Rate, decimal Amount, bool IsTaxable, string WorkState, string WorkCounty, string WorkCity, string WorkSchoolDistrict, Guid? ProjectJobId, string Notes, PayrollW2ReportingInput W2Reporting);

public sealed record EmployeeSnapshot(
    string EmployeeNumber,
    string FullName,
    string Department,
    string State,
    string PayType,
    decimal MonthlyBasePay,
    bool IsActive,
    Guid Id = default,
    string FilingStatus = "Single",
    int Allowances = 0,
    decimal AdditionalWithholding = 0m,
    decimal PreTaxBenefitDeductions = 0m,
    decimal PostTaxBenefitDeductions = 0m,
    string ResidenceState = "",
    string ResidenceCity = "",
    string WorkCity = "",
    string PayrollFrequency = "Biweekly",
    string ResidenceCounty = "",
    string ResidenceSchoolDistrict = "",
    string WorkCounty = "",
    string WorkSchoolDistrict = "",
    DateOnly? EmploymentStartedOn = null,
    DateOnly? EmploymentEndedOn = null,
    decimal HourlyRate = 0m,
    decimal OvertimeRate = 0m,
    bool DirectDepositEnabled = false,
    bool HasSocialSecurityNumber = false,
    bool HasBankAccount = false,
    string ConcurrencyToken = "",
    string AddressLine1 = "",
    string AddressLine2 = "",
    string PostalCode = "",
    int FederalFormW4Year = 0,
    bool FederalStep2MultipleJobs = false,
    decimal FederalStep3Credits = 0m,
    decimal FederalStep4OtherIncome = 0m,
    decimal FederalStep4Deductions = 0m,
    bool FederalWithholdingExempt = false,
    DateOnly? DirectDepositAuthorizationOn = null,
    bool HasDirectDepositAuthorization = false,
    string AddressCity = "",
    string AddressState = "");

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
    decimal ActualCost,
    Guid Id = default);

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
    bool IsEmployerSpecific,
    bool IsActive = false,
    bool IsVerified = false,
    string VerificationNotes = "");
