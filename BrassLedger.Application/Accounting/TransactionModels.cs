namespace BrassLedger.Application.Accounting;

public sealed record JournalLineRequest(string AccountNumber, decimal Debit, decimal Credit, string Description);
public sealed record PostJournalEntryRequest(DateOnly PostedOn, string Reference, string Description, IReadOnlyList<JournalLineRequest> Lines);
public sealed record SaveJournalEntryDraftRequest(Guid? Id, DateOnly EntryDate, string Reference, string Description, IReadOnlyList<JournalLineRequest> Lines);
public sealed record ReverseJournalEntryRequest(Guid JournalEntryId, DateOnly ReversalDate, string Reason);
public sealed record SalesInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice, decimal DiscountAmount, decimal TaxAmount, string RevenueAccountNumber);
public sealed record CreateInvoiceRequest(Guid CustomerId, string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate, decimal Subtotal, decimal TaxAmount, string RevenueAccountNumber, string Description, IReadOnlyList<SalesInvoiceLineRequest>? Lines = null);
public sealed record VendorBillLineRequest(string Description, decimal Quantity, decimal UnitCost, decimal DiscountAmount, decimal TaxAmount, string ExpenseAccountNumber);
public sealed record CreateVendorBillRequest(Guid VendorId, string BillNumber, DateOnly BillDate, DateOnly DueDate, decimal TotalAmount, string ExpenseAccountNumber, string Description, IReadOnlyList<VendorBillLineRequest>? Lines = null);
public sealed record ApplyInvoicePaymentRequest(Guid InvoiceId, Guid BankAccountId, DateOnly PaymentDate, decimal Amount, string Reference);
public sealed record ApplyBillPaymentRequest(Guid VendorBillId, Guid BankAccountId, DateOnly PaymentDate, decimal Amount, string Reference);
public sealed record PaymentDocumentApplicationRequest(Guid DocumentId, decimal Amount);
public sealed record RecordCustomerPaymentRequest(Guid CustomerId, Guid BankAccountId, DateOnly PaymentDate, decimal Amount, string Reference, string Method, IReadOnlyList<PaymentDocumentApplicationRequest> Applications);
public sealed record RecordVendorPaymentRequest(Guid VendorId, Guid BankAccountId, DateOnly PaymentDate, decimal Amount, string Reference, string Method, IReadOnlyList<PaymentDocumentApplicationRequest> Applications);
public sealed record ReverseSubledgerPaymentRequest(Guid PaymentId, DateOnly ReversalDate, string Reason, string ReversalKind = "Reversed");
public sealed record RecordCustomerAdjustmentRequest(Guid InvoiceId, DateOnly AdjustmentDate, decimal Amount, string Reference, string OffsetAccountNumber, string Reason, string Kind = "CreditMemo");
public sealed record RecordVendorCreditRequest(Guid VendorBillId, DateOnly AdjustmentDate, decimal Amount, string Reference, string OffsetAccountNumber, string Reason);
public sealed record RefundUnappliedPaymentRequest(Guid PaymentId, Guid BankAccountId, DateOnly RefundDate, decimal Amount, string Reference, string Reason);
public sealed record VoidSubledgerDocumentRequest(Guid DocumentId, DateOnly VoidDate, string Reason);
public sealed record ReverseSubledgerAdjustmentRequest(Guid AdjustmentId, DateOnly ReversalDate, string Reason);
public sealed record SaveRecurringInvoiceTemplateRequest(CreateInvoiceRequest Invoice, string Frequency, int FrequencyInterval, DateOnly NextOccurrenceDate, DateOnly? EndDate = null);
public sealed record SaveRecurringVendorBillTemplateRequest(CreateVendorBillRequest Bill, string Frequency, int FrequencyInterval, DateOnly NextOccurrenceDate, DateOnly? EndDate = null);
public sealed record ReconcileBankAccountRequest(Guid BankAccountId, DateOnly StatementDate, decimal StatementClosingBalance, IReadOnlyList<Guid>? ClearedJournalEntryIds = null);
public sealed record UpdateBankLedgerMappingRequest(Guid BankAccountId, string LedgerAccountNumber);
public sealed record PostPayrollRunRequest(
    Guid BankAccountId,
    DateOnly PayDate,
    string Reference,
    decimal GrossPayroll,
    decimal? NetPay = null,
    decimal? EmployeeWithholdings = null,
    decimal? EmployerPayrollTaxes = null,
    string TaxJurisdiction = "Federal");
public sealed record EmployeePayrollInput(Guid EmployeeId, decimal GrossPay);
public sealed record PostEmployeePayrollRunRequest(Guid BankAccountId, DateOnly PayDate, string Reference, IReadOnlyList<EmployeePayrollInput> Employees);
public sealed record EmployeePayrollEstimate(Guid EmployeeId, string EmployeeName, string WorkState, string FilingStatus, decimal GrossPay, decimal PreTaxDeductions, decimal EmployeeWithholdings, decimal PostTaxDeductions, decimal EmployerPayrollTaxes, decimal NetPay);
public sealed record PayrollRunEstimate(decimal GrossPayroll, decimal PreTaxDeductions, decimal EmployeeWithholdings, decimal PostTaxDeductions, decimal EmployerPayrollTaxes, decimal NetPay, IReadOnlyList<EmployeePayrollEstimate> Employees);
public sealed record SaveEmployeePayrollSetupRequest(Guid EmployeeId, string FilingStatus, int Allowances, decimal AdditionalWithholding, decimal PreTaxBenefitDeductions, decimal PostTaxBenefitDeductions, string ResidenceState = "", string ResidenceCity = "", string WorkState = "", string WorkCity = "", string PayrollFrequency = "Biweekly");
public sealed record RecordInventoryAdjustmentRequest(Guid InventoryItemId, DateOnly OccurredOn, decimal QuantityChange, decimal UnitCost, string Reference, string Description);
public sealed record SavePayrollJurisdictionRuleRequest(Guid? Id, string ResidenceJurisdiction, string WorkJurisdiction, bool ExemptWorkWithholding, decimal ResidentCreditRate, bool IsActive, string Notes);
public sealed record TransactionResult(bool Succeeded, string ErrorMessage, Guid? Id = null)
{
    public static TransactionResult Success(Guid id) => new(true, string.Empty, id);
    public static TransactionResult Failure(string error) => new(false, error);
}

public sealed record AccountingInterchangeExport(string FileName, string ContentType, byte[] Content);
public sealed record AccountingInterchangeImportResult(bool Succeeded, int ImportedCount, IReadOnlyList<string> Errors)
{
    public static AccountingInterchangeImportResult Success(int importedCount) => new(true, importedCount, []);
    public static AccountingInterchangeImportResult Failure(params string[] errors) => new(false, 0, errors);
}

public interface IAccountingTransactionService
{
    Task<TransactionResult> SaveJournalEntryDraftAsync(SaveJournalEntryDraftRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostApprovedJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseJournalEntryAsync(ReverseJournalEntryRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostJournalEntryAsync(PostJournalEntryRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostJournalEntriesAsync(IReadOnlyList<PostJournalEntryRequest> requests, CancellationToken cancellationToken = default);
    Task<TransactionResult> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CreateVendorBillAsync(CreateVendorBillRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApplyInvoicePaymentAsync(ApplyInvoicePaymentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApplyBillPaymentAsync(ApplyBillPaymentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordCustomerPaymentAsync(RecordCustomerPaymentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordVendorPaymentAsync(RecordVendorPaymentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseSubledgerPaymentAsync(ReverseSubledgerPaymentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordCustomerAdjustmentAsync(RecordCustomerAdjustmentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordVendorCreditAsync(RecordVendorCreditRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RefundUnappliedPaymentAsync(RefundUnappliedPaymentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> VoidInvoiceAsync(VoidSubledgerDocumentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> VoidVendorBillAsync(VoidSubledgerDocumentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseSubledgerAdjustmentAsync(ReverseSubledgerAdjustmentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveInvoiceDraftAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveVendorBillDraftAsync(CreateVendorBillRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveSubledgerDocumentAsync(Guid workflowId, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostApprovedSubledgerDocumentAsync(Guid workflowId, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveRecurringInvoiceTemplateAsync(SaveRecurringInvoiceTemplateRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveRecurringVendorBillTemplateAsync(SaveRecurringVendorBillTemplateRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> GenerateDueRecurringDocumentsAsync(DateOnly throughDate, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReconcileBankAccountAsync(ReconcileBankAccountRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> UpdateBankLedgerMappingAsync(UpdateBankLedgerMappingRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostPayrollRunAsync(PostPayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<PayrollRunEstimate?> PreviewEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveEmployeePayrollSetupAsync(SaveEmployeePayrollSetupRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SavePayrollJurisdictionRuleAsync(SavePayrollJurisdictionRuleRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordInventoryAdjustmentAsync(RecordInventoryAdjustmentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Exports and imports the core list CSV shapes used by QuickBooks Online.</summary>
public interface IAccountingInterchangeService
{
    Task<AccountingInterchangeExport?> ExportQuickBooksOnlineCsvAsync(string entity, CancellationToken cancellationToken = default);
    Task<AccountingInterchangeImportResult> ImportQuickBooksOnlineCsvAsync(string entity, Stream content, CancellationToken cancellationToken = default);
}
