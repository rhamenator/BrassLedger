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
public sealed record ReconcileBankAccountRequest(Guid BankAccountId, DateOnly StatementDate, decimal StatementClosingBalance, IReadOnlyList<Guid>? ClearedJournalEntryIds = null, string Notes = "");
public sealed record UpdateBankLedgerMappingRequest(Guid BankAccountId, string LedgerAccountNumber);
public sealed record ImportBankStatementRequest(Guid BankAccountId, string FileName, string Format, string Content, bool DryRun = false);
public sealed record BankStatementImportResult(bool Succeeded, Guid? BatchId, int ImportedCount, int DuplicateCount, int RejectedCount, decimal DebitTotal, decimal CreditTotal, IReadOnlyList<string> Rejections, string ErrorMessage)
{
    public static BankStatementImportResult Failure(string error) => new(false, null, 0, 0, 0, 0, 0, [], error);
}
public sealed record MatchBankTransactionRequest(Guid BankStatementTransactionId, Guid JournalEntryId, string Note = "");
public sealed record CreateBankTransferRequest(Guid FromBankAccountId, Guid ToBankAccountId, DateOnly TransferDate, decimal Amount, string Reference, string Memo);
public sealed record ReverseBankTransferRequest(Guid BankTransferId, DateOnly ReversalDate, string Reason);
public sealed record CreateReconciliationAdjustmentRequest(Guid BankAccountId, DateOnly AdjustmentDate, decimal Amount, string OffsetAccountNumber, string Reference, string Description);
public sealed record ReverseReconciliationAdjustmentRequest(Guid AdjustmentId, DateOnly ReversalDate, string Reason);
public sealed record ReopenBankReconciliationRequest(Guid ReconciliationId, string Reason);
public sealed record PostPayrollRunRequest(
    Guid BankAccountId,
    DateOnly PayDate,
    string Reference,
    decimal GrossPayroll,
    decimal? NetPay = null,
    decimal? EmployeeWithholdings = null,
    decimal? EmployerPayrollTaxes = null,
    string TaxJurisdiction = "Federal");
public sealed record PayrollEarningInput(string EarningCode, string EarningType, decimal Hours, decimal Rate, decimal Amount, bool IsTaxable = true, DateOnly? WorkedOn = null, string WorkState = "", string WorkCounty = "", string WorkCity = "", string WorkSchoolDistrict = "", Guid? SourceTimeEntryId = null);
public sealed record PayrollDeductionInput(string DeductionCode, string DeductionType, decimal EmployeeAmount, decimal EmployerAmount = 0, bool IsPreTax = false, string LiabilityAccountNumber = "2200", bool ExemptFromFederalIncomeTax = false, bool ExemptFromFica = false, bool ExemptFromFuta = false, Guid? PayrollDeductionPlanId = null, Guid? EmployeePayrollDeductionElectionId = null, decimal? RequestedEmployeeAmount = null, bool LimitApplied = false, string LimitRuleCode = "None", string CalculationTraceJson = "{}");
public sealed record PayrollTimeEntryInput(DateOnly WorkDate, string EarningCode, string EarningType, decimal Hours, decimal Rate, decimal Amount, bool IsTaxable = true, string WorkState = "", string WorkCounty = "", string WorkCity = "", string WorkSchoolDistrict = "", Guid? ProjectJobId = null, string Notes = "");
public sealed record SavePayrollTimecardDraftRequest(Guid? TimecardId, Guid EmployeeId, DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<PayrollTimeEntryInput> Entries, string Notes = "", string ConcurrencyToken = "");
public sealed record SubmitPayrollTimecardRequest(Guid TimecardId, string ConcurrencyToken);
public sealed record ApprovePayrollTimecardRequest(Guid TimecardId, string ConcurrencyToken);
public sealed record VoidPayrollTimecardRequest(Guid TimecardId, string Reason, string ConcurrencyToken);
public sealed record PayrollLiabilityPaymentApplicationInput(Guid PayrollLiabilityId, decimal Amount);
public sealed record RecordPayrollLiabilityPaymentRequest(Guid BankAccountId, DateOnly PaymentDate, string Reference, string Payee, string Method, IReadOnlyList<PayrollLiabilityPaymentApplicationInput> Applications);
public sealed record ReversePayrollLiabilityPaymentRequest(Guid PaymentId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record EmployeePayrollInput(Guid EmployeeId, decimal GrossPay, IReadOnlyList<PayrollEarningInput>? Earnings = null, IReadOnlyList<PayrollDeductionInput>? Deductions = null);
public sealed record PostEmployeePayrollRunRequest(Guid BankAccountId, DateOnly PayDate, string Reference, IReadOnlyList<EmployeePayrollInput> Employees, DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null, string RunType = "Regular", IReadOnlyList<Guid>? ApprovedTimecardIds = null);
public sealed record PayrollTaxEstimate(string ObligationCode, string JurisdictionCode, string JurisdictionName, string TaxType, decimal TaxableWages, decimal YearToDateTaxableWagesBefore, decimal EmployeeAmount, decimal EmployerAmount, Guid? TaxRuleSetId, Guid? TaxContentPackageId, string ContentVersion, string Source, string CalculationTraceJson);
public sealed record EmployeePayrollEstimate(Guid EmployeeId, string EmployeeName, string WorkState, string FilingStatus, decimal GrossPay, decimal PreTaxDeductions, decimal EmployeeWithholdings, decimal PostTaxDeductions, decimal EmployerPayrollTaxes, decimal NetPay, decimal YearToDateGrossBefore = 0, IReadOnlyList<PayrollTaxEstimate>? Taxes = null, decimal EmployerBenefitContributions = 0, IReadOnlyList<PayrollDeductionInput>? Deductions = null);
public sealed record PayrollRunEstimate(decimal GrossPayroll, decimal PreTaxDeductions, decimal EmployeeWithholdings, decimal PostTaxDeductions, decimal EmployerPayrollTaxes, decimal NetPay, IReadOnlyList<EmployeePayrollEstimate> Employees, decimal EmployerBenefitContributions = 0);
public sealed record ApprovePayrollRunRequest(Guid PayrollRunId, string ConcurrencyToken);
public sealed record PostApprovedPayrollRunRequest(Guid PayrollRunId, string ConcurrencyToken);
public sealed record CancelPayrollRunRequest(Guid PayrollRunId, string Reason, string ConcurrencyToken);
public sealed record ReversePayrollRunRequest(Guid PayrollRunId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record SaveEmployeePayrollSetupRequest(Guid EmployeeId, string FilingStatus, int Allowances, decimal AdditionalWithholding, decimal PreTaxBenefitDeductions, decimal PostTaxBenefitDeductions, string ResidenceState = "", string ResidenceCity = "", string WorkState = "", string WorkCity = "", string PayrollFrequency = "Biweekly", int FederalFormW4Year = 2026, bool FederalStep2MultipleJobs = false, decimal FederalStep3Credits = 0, decimal FederalStep4OtherIncome = 0, decimal FederalStep4Deductions = 0, bool FederalWithholdingExempt = false);
public sealed record SaveEmployeeEmploymentDetailsRequest(Guid EmployeeId, string AddressLine1, string AddressLine2, string PostalCode, string ResidenceCounty, string ResidenceSchoolDistrict, string WorkCounty, string WorkSchoolDistrict, DateOnly? EmploymentStartedOn, DateOnly? EmploymentEndedOn, decimal HourlyRate, decimal OvertimeRate, bool DirectDepositEnabled, string BankAccountType, string SocialSecurityNumber = "", string BankRoutingNumber = "", string BankAccountNumber = "", bool ClearSocialSecurityNumber = false, bool ClearBankDetails = false, string ConcurrencyToken = "", DateOnly? DirectDepositAuthorizationOn = null, string DirectDepositAuthorizationReference = "", bool ClearDirectDepositAuthorization = false, string AddressCity = "", string AddressState = "");
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
    Task<BankStatementImportResult> ImportBankStatementAsync(ImportBankStatementRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> MatchBankTransactionAsync(MatchBankTransactionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> UnmatchBankTransactionAsync(Guid bankStatementTransactionId, string reason, CancellationToken cancellationToken = default);
    Task<TransactionResult> CreateBankTransferAsync(CreateBankTransferRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseBankTransferAsync(ReverseBankTransferRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CreateReconciliationAdjustmentAsync(CreateReconciliationAdjustmentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseReconciliationAdjustmentAsync(ReverseReconciliationAdjustmentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReopenBankReconciliationAsync(ReopenBankReconciliationRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostPayrollRunAsync(PostPayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<PayrollRunEstimate?> PreviewEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveEmployeePayrollRunDraftAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApprovePayrollRunAsync(ApprovePayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostApprovedPayrollRunAsync(PostApprovedPayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CancelPayrollRunAsync(CancelPayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReversePayrollRunAsync(ReversePayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveEmployeePayrollSetupAsync(SaveEmployeePayrollSetupRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveEmployeeEmploymentDetailsAsync(SaveEmployeeEmploymentDetailsRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SavePayrollTimecardDraftAsync(SavePayrollTimecardDraftRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SubmitPayrollTimecardAsync(SubmitPayrollTimecardRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApprovePayrollTimecardAsync(ApprovePayrollTimecardRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> VoidPayrollTimecardAsync(VoidPayrollTimecardRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordPayrollLiabilityPaymentAsync(RecordPayrollLiabilityPaymentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReversePayrollLiabilityPaymentAsync(ReversePayrollLiabilityPaymentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SavePayrollJurisdictionRuleAsync(SavePayrollJurisdictionRuleRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordInventoryAdjustmentAsync(RecordInventoryAdjustmentRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Exports and imports the core list CSV shapes used by QuickBooks Online.</summary>
public interface IAccountingInterchangeService
{
    Task<AccountingInterchangeExport?> ExportQuickBooksOnlineCsvAsync(string entity, CancellationToken cancellationToken = default);
    Task<AccountingInterchangeImportResult> ImportQuickBooksOnlineCsvAsync(string entity, Stream content, CancellationToken cancellationToken = default);
}
