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
public sealed record PayrollW2ReportingInput(
    decimal SocialSecurityTips = 0,
    decimal CashTipsReported = 0,
    decimal QualifiedOvertimeCompensation = 0,
    IReadOnlyList<string>? TreasuryTippedOccupationCodes = null);
public sealed record PayrollEarningInput(string EarningCode, string EarningType, decimal Hours, decimal Rate, decimal Amount, bool IsTaxable = true, DateOnly? WorkedOn = null, string WorkState = "", string WorkCounty = "", string WorkCity = "", string WorkSchoolDistrict = "", Guid? SourceTimeEntryId = null, PayrollW2ReportingInput? W2Reporting = null);
public sealed record PayrollDeductionInput(string DeductionCode, string DeductionType, decimal EmployeeAmount, decimal EmployerAmount = 0, bool IsPreTax = false, string LiabilityAccountNumber = "", bool ExemptFromFederalIncomeTax = false, bool ExemptFromFica = false, bool ExemptFromFuta = false, Guid? PayrollDeductionPlanId = null, Guid? EmployeePayrollDeductionElectionId = null, decimal? RequestedEmployeeAmount = null, bool LimitApplied = false, string LimitRuleCode = "None", string CalculationTraceJson = "{}");
public sealed record PayrollTimeEntryInput(DateOnly WorkDate, string EarningCode, string EarningType, decimal Hours, decimal Rate, decimal Amount, bool IsTaxable = true, string WorkState = "", string WorkCounty = "", string WorkCity = "", string WorkSchoolDistrict = "", Guid? ProjectJobId = null, string Notes = "", PayrollW2ReportingInput? W2Reporting = null);
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
public sealed record RecordInventoryAdjustmentRequest(Guid InventoryItemId, DateOnly OccurredOn, decimal QuantityChange, decimal UnitCost, string Reference, string Description, Guid? WarehouseId = null, Guid? BinId = null);
public sealed record SaveInventoryWarehouseRequest(Guid? Id, string Code, string Name, string AddressLine1, string AddressLine2, string City, string StateOrProvince, string PostalCode, string CountryCode, bool IsDefault, bool IsActive, string ConcurrencyToken = "");
public sealed record SaveInventoryBinRequest(Guid? Id, Guid WarehouseId, string Code, string Name, bool IsDefault, bool IsActive, string ConcurrencyToken = "");
public sealed record TransferInventoryRequest(Guid InventoryItemId, Guid SourceWarehouseId, Guid SourceBinId, Guid DestinationWarehouseId, Guid DestinationBinId, decimal Quantity, DateOnly TransferDate, string Reference, string Reason);
public sealed record ReverseInventoryTransferRequest(Guid InventoryTransferId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record SalesOrderLineRequest(Guid InventoryItemId, string Description, decimal Quantity, decimal UnitPrice, decimal DiscountAmount, decimal TaxAmount, string RevenueAccountNumber);
public sealed record SaveSalesQuoteRequest(Guid? Id, Guid CustomerId, string QuoteNumber, DateOnly QuotedOn, DateOnly ExpiresOn, string Notes, IReadOnlyList<SalesOrderLineRequest> Lines, string ConcurrencyToken = "");
public sealed record ApproveSalesQuoteRequest(Guid SalesQuoteId, string ConcurrencyToken);
public sealed record WithdrawSalesQuoteRequest(Guid SalesQuoteId, string Reason, string ConcurrencyToken);
public sealed record ConvertSalesQuoteRequest(Guid SalesQuoteId, string OrderNumber, DateOnly OrderedOn, DateOnly? RequestedShipOn, string Notes, string ConcurrencyToken);
public sealed record SaveSalesOrderRequest(Guid? Id, Guid CustomerId, string OrderNumber, DateOnly OrderedOn, DateOnly? RequestedShipOn, string Notes, IReadOnlyList<SalesOrderLineRequest> Lines, string ConcurrencyToken = "");
public sealed record ApproveSalesOrderRequest(Guid SalesOrderId, string ConcurrencyToken);
public sealed record AmendSalesOrderRequest(Guid SalesOrderId, DateOnly OrderedOn, DateOnly? RequestedShipOn, string Notes, string Reason, IReadOnlyList<SalesOrderLineRequest> Lines, string ConcurrencyToken);
public sealed record CancelSalesOrderRequest(Guid SalesOrderId, string Reason, string ConcurrencyToken);
public sealed record AllocateSalesOrderLineRequest(Guid SalesOrderLineId, decimal Quantity);
public sealed record AllocateSalesOrderRequest(Guid SalesOrderId, IReadOnlyList<AllocateSalesOrderLineRequest> Lines, string ConcurrencyToken, Guid? WarehouseId = null, Guid? BinId = null);
public sealed record CreateInventoryPickLineRequest(Guid SalesOrderLineId, decimal Quantity);
public sealed record CreateInventoryPickRequest(Guid SalesOrderId, string PickNumber, DateOnly PickDate, IReadOnlyList<CreateInventoryPickLineRequest> Lines, string SalesOrderConcurrencyToken);
public sealed record CompleteInventoryPickLineRequest(Guid InventoryPickLineId, decimal PickedQuantity);
public sealed record CompleteInventoryPickRequest(Guid InventoryPickId, IReadOnlyList<CompleteInventoryPickLineRequest> Lines, string ConcurrencyToken);
public sealed record CancelInventoryPickRequest(Guid InventoryPickId, string Reason, string ConcurrencyToken);
public sealed record PackInventoryPickLineRequest(Guid InventoryPickLineId, decimal Quantity);
public sealed record PackInventoryPickRequest(Guid InventoryPickId, string PackingSlipNumber, DateOnly PackedOn, IReadOnlyList<PackInventoryPickLineRequest> Lines, string ConcurrencyToken);
public sealed record CancelInventoryPackingSlipRequest(Guid InventoryPackingSlipId, string Reason, string ConcurrencyToken);
public sealed record PromiseSalesOrderBackorderRequest(Guid SalesOrderId, Guid SalesOrderLineId, decimal Quantity, DateOnly PromisedShipOn, string Reason, string SalesOrderConcurrencyToken);
public sealed record CancelSalesOrderBackorderRequest(Guid SalesOrderBackorderPromiseId, string Reason, string ConcurrencyToken);
public sealed record ShipSalesOrderLineRequest(Guid SalesOrderLineId, decimal Quantity);
public sealed record ShipSalesOrderRequest(Guid SalesOrderId, string ShipmentNumber, DateOnly ShippedOn, IReadOnlyList<ShipSalesOrderLineRequest> Lines, string ConcurrencyToken, Guid? InventoryPackingSlipId = null, string PackingSlipConcurrencyToken = "");
public sealed record InvoiceInventoryShipmentRequest(Guid InventoryShipmentId, string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate, string Description, string ConcurrencyToken);
public sealed record ReverseInventoryShipmentRequest(Guid InventoryShipmentId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record AuthorizeCustomerReturnLineRequest(Guid InventoryShipmentLineId, decimal Quantity);
public sealed record AuthorizeCustomerReturnRequest(Guid InventoryShipmentId, string ReturnNumber, DateOnly AuthorizedOn, string Reason, IReadOnlyList<AuthorizeCustomerReturnLineRequest> Lines, string ShipmentConcurrencyToken);
public sealed record CancelCustomerReturnRequest(Guid CustomerReturnAuthorizationId, string Reason, string ConcurrencyToken);
public sealed record ReceiveCustomerReturnLineRequest(Guid CustomerReturnAuthorizationLineId, decimal Quantity);
public sealed record ReceiveCustomerReturnRequest(Guid CustomerReturnAuthorizationId, string ReceiptNumber, DateOnly ReceivedOn, Guid? WarehouseId, Guid? BinId, IReadOnlyList<ReceiveCustomerReturnLineRequest> Lines, string ConcurrencyToken);
public sealed record ReverseCustomerReturnReceiptRequest(Guid CustomerReturnReceiptId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record CreditCustomerReturnRequest(Guid CustomerReturnReceiptId, string CreditNumber, DateOnly CreditDate, string Reason, string ConcurrencyToken);
public sealed record ReverseCustomerReturnCreditRequest(Guid CustomerReturnCreditId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record ApplyCustomerReturnCreditRequest(Guid CustomerReturnCreditId, Guid SalesInvoiceId, DateOnly AppliedOn, decimal Amount, string ConcurrencyToken);
public sealed record ReverseCustomerReturnCreditApplicationRequest(Guid CustomerReturnCreditApplicationId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record RefundCustomerReturnCreditRequest(Guid CustomerReturnCreditId, Guid BankAccountId, string Reference, DateOnly RefundDate, decimal Amount, string ConcurrencyToken);
public sealed record ReverseCustomerReturnCreditRefundRequest(Guid CustomerReturnCreditRefundId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record PurchaseRequisitionLineRequest(Guid InventoryItemId, string Description, decimal Quantity, decimal EstimatedUnitCost);
public sealed record SavePurchaseRequisitionRequest(Guid? Id, Guid? RequestedVendorId, string RequisitionNumber, DateOnly RequestedOn, DateOnly? NeededBy, string Purpose, IReadOnlyList<PurchaseRequisitionLineRequest> Lines, string ConcurrencyToken = "");
public sealed record SubmitPurchaseRequisitionRequest(Guid PurchaseRequisitionId, string ConcurrencyToken);
public sealed record DecidePurchaseRequisitionRequest(Guid PurchaseRequisitionId, bool Approve, string Reason, string ConcurrencyToken);
public sealed record CancelPurchaseRequisitionRequest(Guid PurchaseRequisitionId, string Reason, string ConcurrencyToken);
public sealed record ConvertPurchaseRequisitionRequest(Guid PurchaseRequisitionId, Guid VendorId, string OrderNumber, DateOnly OrderedOn, DateOnly? ExpectedOn, string Notes, string ConcurrencyToken);
public sealed record PurchaseOrderLineRequest(Guid InventoryItemId, string Description, decimal Quantity, decimal UnitCost);
public sealed record SavePurchaseOrderRequest(Guid? Id, Guid VendorId, string OrderNumber, DateOnly OrderedOn, DateOnly? ExpectedOn, string Notes, IReadOnlyList<PurchaseOrderLineRequest> Lines, string ConcurrencyToken = "");
public sealed record ApprovePurchaseOrderRequest(Guid PurchaseOrderId, string ConcurrencyToken);
public sealed record ReceivePurchaseOrderLineRequest(Guid PurchaseOrderLineId, decimal Quantity);
public sealed record ReceivePurchaseOrderRequest(Guid PurchaseOrderId, string ReceiptNumber, DateOnly ReceivedOn, IReadOnlyList<ReceivePurchaseOrderLineRequest> Lines, string ConcurrencyToken, Guid? WarehouseId = null, Guid? BinId = null);
public sealed record MatchPurchaseOrderReceiptBillRequest(Guid InventoryReceiptId, string BillNumber, DateOnly BillDate, DateOnly DueDate, string Description, string ConcurrencyToken);
public sealed record ReverseInventoryReceiptRequest(Guid InventoryReceiptId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
public sealed record UnmatchPurchaseOrderReceiptBillRequest(Guid InventoryReceiptId, DateOnly VoidDate, string Reason, string ConcurrencyToken);
public sealed record SavePayrollJurisdictionRuleRequest(Guid? Id, string ResidenceJurisdiction, string WorkJurisdiction, bool ExemptWorkWithholding, decimal ResidentCreditRate, bool IsActive, string Notes);
public sealed record TransactionResult(bool Succeeded, string ErrorMessage, Guid? Id = null)
{
    public static TransactionResult Success(Guid id) => new(true, string.Empty, id);
    public static TransactionResult Failure(string error) => new(false, error);
}

public sealed record AccountingInterchangeExport(string FileName, string ContentType, byte[] Content);
public sealed record AccountingInterchangeImportOptions(bool DryRun = false, string FileName = "");
public sealed record AccountingInterchangeImportResult(bool Succeeded, int ImportedCount, IReadOnlyList<string> Errors, bool DryRun = false, int RowCount = 0, string ContentSha256 = "", Guid? BatchId = null, int DuplicateCount = 0, int RejectedCount = 0)
{
    public static AccountingInterchangeImportResult Success(int importedCount, bool dryRun = false, int rowCount = 0, string contentSha256 = "") => new(true, importedCount, [], dryRun, rowCount, contentSha256);
    public static AccountingInterchangeImportResult Failure(params string[] errors) => new(false, 0, errors);
}
public sealed record AccountingInterchangeBatchSnapshot(Guid Id, string ProviderCode, string EntityType, string FileName, string ContentSha256, string Status, bool IsDryRun, int RowCount, int ImportedCount, int DuplicateCount, int RejectedCount, IReadOnlyList<string> Rejections, string? ProcessedBy, DateTimeOffset ProcessedAtUtc);

public interface IAccountingTransactionService
{
    Task<TransactionResult> SaveJournalEntryDraftAsync(SaveJournalEntryDraftRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostApprovedJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseJournalEntryAsync(ReverseJournalEntryRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostJournalEntryAsync(PostJournalEntryRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PostJournalEntriesAsync(IReadOnlyList<PostJournalEntryRequest> requests, CancellationToken cancellationToken = default);
    Task<AccountingScheduleWorkspace> GetAccountingScheduleWorkspaceAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveAccountingScheduleAsync(SaveAccountingScheduleRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveAccountingScheduleAsync(ApproveAccountingScheduleRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PrepareAccountingScheduleInstallmentsAsync(PrepareAccountingScheduleInstallmentsRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseAccountingScheduleInstallmentAsync(ReverseAccountingScheduleInstallmentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PrepareFixedAssetDisposalAsync(PrepareFixedAssetDisposalRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseFixedAssetDisposalAsync(ReverseFixedAssetDisposalRequest request, CancellationToken cancellationToken = default);
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
    Task<TransactionResult> SaveInventoryWarehouseAsync(SaveInventoryWarehouseRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveInventoryBinAsync(SaveInventoryBinRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> TransferInventoryAsync(TransferInventoryRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseInventoryTransferAsync(ReverseInventoryTransferRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveSalesQuoteAsync(SaveSalesQuoteRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveSalesQuoteAsync(ApproveSalesQuoteRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> WithdrawSalesQuoteAsync(WithdrawSalesQuoteRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ConvertSalesQuoteAsync(ConvertSalesQuoteRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveSalesOrderAsync(SaveSalesOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveSalesOrderAsync(ApproveSalesOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> AmendSalesOrderAsync(AmendSalesOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CancelSalesOrderAsync(CancelSalesOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> AllocateSalesOrderAsync(AllocateSalesOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CreateInventoryPickAsync(CreateInventoryPickRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CompleteInventoryPickAsync(CompleteInventoryPickRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CancelInventoryPickAsync(CancelInventoryPickRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PackInventoryPickAsync(PackInventoryPickRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CancelInventoryPackingSlipAsync(CancelInventoryPackingSlipRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> PromiseSalesOrderBackorderAsync(PromiseSalesOrderBackorderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CancelSalesOrderBackorderAsync(CancelSalesOrderBackorderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ShipSalesOrderAsync(ShipSalesOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> InvoiceInventoryShipmentAsync(InvoiceInventoryShipmentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseInventoryShipmentAsync(ReverseInventoryShipmentRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> AuthorizeCustomerReturnAsync(AuthorizeCustomerReturnRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CancelCustomerReturnAsync(CancelCustomerReturnRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReceiveCustomerReturnAsync(ReceiveCustomerReturnRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseCustomerReturnReceiptAsync(ReverseCustomerReturnReceiptRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CreditCustomerReturnAsync(CreditCustomerReturnRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseCustomerReturnCreditAsync(ReverseCustomerReturnCreditRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApplyCustomerReturnCreditAsync(ApplyCustomerReturnCreditRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseCustomerReturnCreditApplicationAsync(ReverseCustomerReturnCreditApplicationRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RefundCustomerReturnCreditAsync(RefundCustomerReturnCreditRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseCustomerReturnCreditRefundAsync(ReverseCustomerReturnCreditRefundRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SavePurchaseRequisitionAsync(SavePurchaseRequisitionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SubmitPurchaseRequisitionAsync(SubmitPurchaseRequisitionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> DecidePurchaseRequisitionAsync(DecidePurchaseRequisitionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> CancelPurchaseRequisitionAsync(CancelPurchaseRequisitionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ConvertPurchaseRequisitionAsync(ConvertPurchaseRequisitionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SavePurchaseOrderAsync(SavePurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApprovePurchaseOrderAsync(ApprovePurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReceivePurchaseOrderAsync(ReceivePurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> MatchPurchaseOrderReceiptBillAsync(MatchPurchaseOrderReceiptBillRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> UnmatchPurchaseOrderReceiptBillAsync(UnmatchPurchaseOrderReceiptBillRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReverseInventoryReceiptAsync(ReverseInventoryReceiptRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Exports and imports the core list CSV shapes used by QuickBooks Online.</summary>
public interface IAccountingInterchangeService
{
    Task<AccountingInterchangeExport?> ExportQuickBooksOnlineCsvAsync(string entity, CancellationToken cancellationToken = default);
    Task<AccountingInterchangeImportResult> ImportQuickBooksOnlineCsvAsync(string entity, Stream content, AccountingInterchangeImportOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountingInterchangeBatchSnapshot>> GetRecentBatchesAsync(int limit = 20, CancellationToken cancellationToken = default);
}
