using Bunit;
using Bunit.TestDoubles;
using BrassLedger.Application.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Web.Tests;

public sealed class LedgerPageTests : TestContext
{
    public LedgerPageTests()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("controller");
        authorization.SetRoles("Administrator");
        authorization.SetPolicies(
            BrassLedgerAuthorizationPolicies.PrepareJournals,
            BrassLedgerAuthorizationPolicies.ApproveJournals,
            BrassLedgerAuthorizationPolicies.PostJournals,
            BrassLedgerAuthorizationPolicies.ReverseJournals,
            BrassLedgerAuthorizationPolicies.PrepareSubledgerDocuments,
            BrassLedgerAuthorizationPolicies.ApproveSubledgerDocuments,
            BrassLedgerAuthorizationPolicies.PostSubledgerDocuments,
            BrassLedgerAuthorizationPolicies.ManageOperations,
            BrassLedgerAuthorizationPolicies.ManageProjects,
            BrassLedgerAuthorizationPolicies.AccessProjects,
            BrassLedgerAuthorizationPolicies.PrepareProjectChangeOrders,
            BrassLedgerAuthorizationPolicies.ApproveProjectChangeOrders);
        Services.AddSingleton<IBusinessWorkspaceService>(new StubBusinessWorkspaceService(TestWorkspaceData.CreateWorkspace()));
        Services.AddSingleton<IAccountingTransactionService>(new StubAccountingTransactionService());
        Services.AddSingleton<IAccountingInterchangeService>(new StubAccountingInterchangeService());
    }

    [Fact]
    public void LedgerPage_RendersAccountAndJournalData()
    {
        var cut = RenderComponent<Ledger>();

        Assert.Contains("Operating Cash", cut.Markup);
        Assert.Contains("JE-2401", cut.Markup);
        Assert.Contains("Primary Operating", cut.Markup);
        Assert.Contains("Recent interchange batches", cut.Markup);
        Assert.Contains("malformed-customers.csv", cut.Markup);
        Assert.Contains("Fix row 2", cut.Markup);
        Assert.Contains("Fixed assets, prepaids, and loans", cut.Markup);
        Assert.NotNull(cut.Find("input[aria-label='Journal rejection reason']"));
        Assert.Contains("Attach the supporting bank statement.", cut.Markup);
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Correct").Click();
        Assert.Contains("Correct journal entry", cut.Markup);
        Assert.Equal("JE-CORRECT-1", cut.Find("input[aria-label='Journal entry reference']").GetAttribute("value"));
        Assert.Equal("1000", cut.Find("select[aria-label='Journal entry account']").GetAttribute("value"));
        cut.Find("select[aria-label='Accounting schedule type']").Change("Loan");
        Assert.Contains("Loan payment bank account", cut.Markup);
    }

    [Fact]
    public void OperationsPage_ExposesPurchaseOrderReceivingAndMatchingWorkflow()
    {
        var cut = RenderComponent<Operations>();

        Assert.Contains("Prepare purchase requisition", cut.Markup);
        Assert.Contains("Purchase requisitions", cut.Markup);
        Assert.NotNull(cut.Find("select[aria-label='Purchase order vendor']"));
        Assert.NotNull(cut.Find("select[aria-label='Purchase order line 1 item']"));
        Assert.NotNull(cut.Find("select[aria-label='Purchase order line 1 project']"));
        cut.FindAll("button").Last(button => button.TextContent.Trim() == "Add line").Click();
        Assert.NotNull(cut.Find("select[aria-label='Purchase order line 2 item']"));
        Assert.Contains("Inventory receipts and invoice matching", cut.Markup);
        Assert.Contains("Average cost", cut.Markup);
        Assert.Contains("Prepare sales quote", cut.Markup);
        Assert.NotNull(cut.Find("select[aria-label='Sales quote customer']"));
        Assert.NotNull(cut.Find("select[aria-label='Sales quote line 1 item']"));
        Assert.NotNull(cut.Find("select[aria-label='Sales quote line 1 project']"));
        Assert.Contains("Prepare sales order", cut.Markup);
        Assert.NotNull(cut.Find("select[aria-label='Sales order customer']"));
        Assert.NotNull(cut.Find("select[aria-label='Sales order line 1 item']"));
        Assert.NotNull(cut.Find("select[aria-label='Sales order line 1 project']"));
        Assert.NotNull(cut.Find("table[aria-label='Inventory picks']"));
        Assert.NotNull(cut.Find("table[aria-label='Inventory packing slips']"));
        Assert.NotNull(cut.Find("table[aria-label='Sales backorder promises']"));
        Assert.Contains("Pick tickets commit an exact bin", cut.Markup);
        Assert.Contains("Customer shipments and invoicing", cut.Markup);
        Assert.NotNull(cut.Find("table[aria-label='Customer return authorizations']"));
        Assert.NotNull(cut.Find("table[aria-label='Customer return receipts']"));
        Assert.NotNull(cut.Find("table[aria-label='Customer return credits']"));
        Assert.Contains("Sales authorizes; fulfillment receives; receivables credits", cut.Markup);
        Assert.NotNull(cut.Find("table[aria-label='Supplier return authorizations']"));
        Assert.NotNull(cut.Find("table[aria-label='Supplier return shipments']"));
        Assert.Contains("Returns remain linked to the exact receipt and purchase-order lines", cut.Markup);
        Assert.Contains("Inventory removed", cut.Markup);
        Assert.Contains("Vendor credit", cut.Markup);
        Assert.Contains("purchase-price variance", cut.Markup);
        Assert.NotNull(cut.Find("table[aria-label='Landed cost allocations']"));
        Assert.Contains("Freight and import charges remain tied to their source receipt", cut.Markup);
    }

    [Fact]
    public void ProjectsPage_ExposesMaintenanceLifecycleMetricsAndLedgerDrillDown()
    {
        var cut = RenderComponent<Projects>();

        Assert.Contains("Project accounting", cut.Markup);
        Assert.Contains("Open commitments", cut.Markup);
        Assert.NotNull(cut.Find("input[aria-label='Project number']"));
        Assert.NotNull(cut.Find("select[aria-label='Project customer']"));
        Assert.NotNull(cut.Find("select[aria-label='Project billing method']"));
        Assert.NotNull(cut.Find("input[aria-label='Project retainage percent']"));
        Assert.NotNull(cut.Find("table[aria-label='Project portfolio']"));
        Assert.NotNull(cut.Find("select[aria-label='Change order project']"));
        Assert.NotNull(cut.Find("input[aria-label='Change order number']"));
        Assert.Contains("Project change orders", cut.Markup);
        Assert.Contains("Gross margin", cut.Markup);
        cut.FindAll("button").First(button => button.TextContent.Contains("JOB-5007", StringComparison.Ordinal)).Click();
        Assert.Contains("JOB-5007 ledger", cut.Markup);
        Assert.Contains("Budget remaining after posted cost and open commitments", cut.Markup);
    }

    [Fact]
    public void ReceivablesAndPayables_ExposeAuditableRejectionAndCorrectionGuidance()
    {
        var receivables = RenderComponent<Receivables>();
        Assert.NotNull(receivables.Find("input[aria-label='Invoice rejection reason']"));
        Assert.Contains("correct and resubmit it using the same invoice number", receivables.Markup);
        Assert.Contains("Review note", receivables.Markup);

        var payables = RenderComponent<Payables>();
        Assert.NotNull(payables.Find("input[aria-label='Vendor bill rejection reason']"));
        Assert.Contains("correct and resubmit it using the same vendor and bill number", payables.Markup);
        Assert.Contains("Review note", payables.Markup);
    }
}

internal sealed class StubAccountingTransactionService : IAccountingTransactionService
{
    public Task<TransactionResult> SaveJournalEntryDraftAsync(SaveJournalEntryDraftRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> ApproveJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(journalEntryId));
    public Task<TransactionResult> RejectJournalEntryAsync(RejectJournalEntryRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.JournalEntryId));
    public Task<TransactionResult> PostApprovedJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(journalEntryId));
    public Task<TransactionResult> ReverseJournalEntryAsync(ReverseJournalEntryRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<AccountingScheduleWorkspace> GetAccountingScheduleWorkspaceAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AccountingScheduleWorkspace([], [], []));
    public Task<TransactionResult> SaveAccountingScheduleAsync(SaveAccountingScheduleRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> ApproveAccountingScheduleAsync(ApproveAccountingScheduleRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ScheduleId));
    public Task<TransactionResult> PrepareAccountingScheduleInstallmentsAsync(PrepareAccountingScheduleInstallmentsRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ScheduleId));
    public Task<TransactionResult> ReverseAccountingScheduleInstallmentAsync(ReverseAccountingScheduleInstallmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InstallmentId));
    public Task<TransactionResult> PrepareFixedAssetDisposalAsync(PrepareFixedAssetDisposalRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseFixedAssetDisposalAsync(ReverseFixedAssetDisposalRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ScheduleId));
    public Task<TransactionResult> ApplyInvoicePaymentAsync(ApplyInvoicePaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InvoiceId));
    public Task<TransactionResult> ApplyBillPaymentAsync(ApplyBillPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.VendorBillId));
    public Task<TransactionResult> RecordCustomerPaymentAsync(RecordCustomerPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> RecordVendorPaymentAsync(RecordVendorPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseSubledgerPaymentAsync(ReverseSubledgerPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PaymentId));
    public Task<TransactionResult> RecordCustomerAdjustmentAsync(RecordCustomerAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> RecordVendorCreditAsync(RecordVendorCreditRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> RefundUnappliedPaymentAsync(RefundUnappliedPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> VoidInvoiceAsync(VoidSubledgerDocumentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.DocumentId));
    public Task<TransactionResult> VoidVendorBillAsync(VoidSubledgerDocumentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.DocumentId));
    public Task<TransactionResult> ReverseSubledgerAdjustmentAsync(ReverseSubledgerAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.AdjustmentId));
    public Task<TransactionResult> SaveInvoiceDraftAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> SaveVendorBillDraftAsync(CreateVendorBillRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ApproveSubledgerDocumentAsync(Guid workflowId, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(workflowId));
    public Task<TransactionResult> RejectSubledgerDocumentAsync(RejectSubledgerDocumentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.WorkflowId));
    public Task<TransactionResult> PostApprovedSubledgerDocumentAsync(Guid workflowId, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(workflowId));
    public Task<TransactionResult> SaveRecurringInvoiceTemplateAsync(SaveRecurringInvoiceTemplateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> SaveRecurringVendorBillTemplateAsync(SaveRecurringVendorBillTemplateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> GenerateDueRecurringDocumentsAsync(DateOnly throughDate, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReconcileBankAccountAsync(ReconcileBankAccountRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.BankAccountId));
    public Task<TransactionResult> UpdateBankLedgerMappingAsync(UpdateBankLedgerMappingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.BankAccountId));
    public Task<BankStatementImportResult> ImportBankStatementAsync(ImportBankStatementRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new BankStatementImportResult(true, Guid.NewGuid(), 0, 0, 0, 0, 0, [], string.Empty));
    public Task<TransactionResult> MatchBankTransactionAsync(MatchBankTransactionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.BankStatementTransactionId));
    public Task<TransactionResult> UnmatchBankTransactionAsync(Guid bankStatementTransactionId, string reason, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(bankStatementTransactionId));
    public Task<TransactionResult> CreateBankTransferAsync(CreateBankTransferRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseBankTransferAsync(ReverseBankTransferRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.BankTransferId));
    public Task<TransactionResult> CreateReconciliationAdjustmentAsync(CreateReconciliationAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseReconciliationAdjustmentAsync(ReverseReconciliationAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.AdjustmentId));
    public Task<TransactionResult> ReopenBankReconciliationAsync(ReopenBankReconciliationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ReconciliationId));
    public Task<PayrollRunEstimate?> PreviewEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult<PayrollRunEstimate?>(null);
    public Task<TransactionResult> SaveEmployeePayrollRunDraftAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ApprovePayrollRunAsync(ApprovePayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PayrollRunId));
    public Task<TransactionResult> RejectPayrollRunAsync(RejectPayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PayrollRunId));
    public Task<PostEmployeePayrollRunRequest?> GetEmployeePayrollRunDraftAsync(Guid payrollRunId, CancellationToken cancellationToken = default) => Task.FromResult<PostEmployeePayrollRunRequest?>(null);
    public Task<TransactionResult> PostApprovedPayrollRunAsync(PostApprovedPayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PayrollRunId));
    public Task<TransactionResult> CancelPayrollRunAsync(CancelPayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PayrollRunId));
    public Task<TransactionResult> ReversePayrollRunAsync(ReversePayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PayrollRunId));
    public Task<TransactionResult> SaveEmployeePayrollSetupAsync(SaveEmployeePayrollSetupRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.EmployeeId));
    public Task<TransactionResult> SaveEmployeeEmploymentDetailsAsync(SaveEmployeeEmploymentDetailsRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.EmployeeId));
    public Task<TransactionResult> SavePayrollTimecardDraftAsync(SavePayrollTimecardDraftRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.TimecardId ?? Guid.NewGuid()));
    public Task<TransactionResult> SubmitPayrollTimecardAsync(SubmitPayrollTimecardRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.TimecardId));
    public Task<TransactionResult> ApprovePayrollTimecardAsync(ApprovePayrollTimecardRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.TimecardId));
    public Task<TransactionResult> VoidPayrollTimecardAsync(VoidPayrollTimecardRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.TimecardId));
    public Task<TransactionResult> RecordPayrollLiabilityPaymentAsync(RecordPayrollLiabilityPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReversePayrollLiabilityPaymentAsync(ReversePayrollLiabilityPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PaymentId));
    public Task<TransactionResult> SaveProjectJobAsync(SaveProjectJobRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> CloseProjectJobAsync(CloseProjectJobRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ProjectJobId));
    public Task<TransactionResult> ReopenProjectJobAsync(ReopenProjectJobRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ProjectJobId));
    public Task<TransactionResult> SaveProjectChangeOrderDraftAsync(SaveProjectChangeOrderDraftRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SubmitProjectChangeOrderAsync(SubmitProjectChangeOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ProjectChangeOrderId));
    public Task<TransactionResult> DecideProjectChangeOrderAsync(DecideProjectChangeOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ProjectChangeOrderId));
    public Task<TransactionResult> CancelProjectChangeOrderAsync(CancelProjectChangeOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.ProjectChangeOrderId));
    public Task<TransactionResult> SavePayrollJurisdictionRuleAsync(SavePayrollJurisdictionRuleRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> RecordInventoryAdjustmentAsync(RecordInventoryAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> SaveInventoryWarehouseAsync(SaveInventoryWarehouseRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SaveInventoryBinAsync(SaveInventoryBinRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> TransferInventoryAsync(TransferInventoryRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseInventoryTransferAsync(ReverseInventoryTransferRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InventoryTransferId));
    public Task<TransactionResult> SaveSalesQuoteAsync(SaveSalesQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> ApproveSalesQuoteAsync(ApproveSalesQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SalesQuoteId));
    public Task<TransactionResult> WithdrawSalesQuoteAsync(WithdrawSalesQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SalesQuoteId));
    public Task<TransactionResult> ConvertSalesQuoteAsync(ConvertSalesQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> SaveSalesOrderAsync(SaveSalesOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> ApproveSalesOrderAsync(ApproveSalesOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SalesOrderId));
    public Task<TransactionResult> AmendSalesOrderAsync(AmendSalesOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SalesOrderId));
    public Task<TransactionResult> CancelSalesOrderAsync(CancelSalesOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SalesOrderId));
    public Task<TransactionResult> AllocateSalesOrderAsync(AllocateSalesOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SalesOrderId));
    public Task<TransactionResult> CreateInventoryPickAsync(CreateInventoryPickRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> CompleteInventoryPickAsync(CompleteInventoryPickRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InventoryPickId));
    public Task<TransactionResult> CancelInventoryPickAsync(CancelInventoryPickRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InventoryPickId));
    public Task<TransactionResult> PackInventoryPickAsync(PackInventoryPickRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> CancelInventoryPackingSlipAsync(CancelInventoryPackingSlipRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InventoryPackingSlipId));
    public Task<TransactionResult> PromiseSalesOrderBackorderAsync(PromiseSalesOrderBackorderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> CancelSalesOrderBackorderAsync(CancelSalesOrderBackorderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SalesOrderBackorderPromiseId));
    public Task<TransactionResult> ShipSalesOrderAsync(ShipSalesOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> InvoiceInventoryShipmentAsync(InvoiceInventoryShipmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseInventoryShipmentAsync(ReverseInventoryShipmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InventoryShipmentId));
    public Task<TransactionResult> AuthorizeCustomerReturnAsync(AuthorizeCustomerReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> CancelCustomerReturnAsync(CancelCustomerReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.CustomerReturnAuthorizationId));
    public Task<TransactionResult> ReceiveCustomerReturnAsync(ReceiveCustomerReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseCustomerReturnReceiptAsync(ReverseCustomerReturnReceiptRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.CustomerReturnReceiptId));
    public Task<TransactionResult> CreditCustomerReturnAsync(CreditCustomerReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseCustomerReturnCreditAsync(ReverseCustomerReturnCreditRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.CustomerReturnCreditId));
    public Task<TransactionResult> ApplyCustomerReturnCreditAsync(ApplyCustomerReturnCreditRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseCustomerReturnCreditApplicationAsync(ReverseCustomerReturnCreditApplicationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.CustomerReturnCreditApplicationId));
    public Task<TransactionResult> RefundCustomerReturnCreditAsync(RefundCustomerReturnCreditRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseCustomerReturnCreditRefundAsync(ReverseCustomerReturnCreditRefundRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.CustomerReturnCreditRefundId));
    public Task<TransactionResult> SavePurchaseRequisitionAsync(SavePurchaseRequisitionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SubmitPurchaseRequisitionAsync(SubmitPurchaseRequisitionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseRequisitionId));
    public Task<TransactionResult> DecidePurchaseRequisitionAsync(DecidePurchaseRequisitionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseRequisitionId));
    public Task<TransactionResult> CancelPurchaseRequisitionAsync(CancelPurchaseRequisitionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseRequisitionId));
    public Task<TransactionResult> ConvertPurchaseRequisitionAsync(ConvertPurchaseRequisitionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> SavePurchaseOrderAsync(SavePurchaseOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> ApprovePurchaseOrderAsync(ApprovePurchaseOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseOrderId));
    public Task<TransactionResult> ReceivePurchaseOrderAsync(ReceivePurchaseOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> SavePurchaseInvoiceMatchAsync(SavePurchaseInvoiceMatchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SubmitPurchaseInvoiceMatchAsync(SubmitPurchaseInvoiceMatchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseInvoiceMatchId));
    public Task<TransactionResult> DecidePurchaseInvoiceMatchAsync(DecidePurchaseInvoiceMatchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseInvoiceMatchId));
    public Task<TransactionResult> CancelPurchaseInvoiceMatchAsync(CancelPurchaseInvoiceMatchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseInvoiceMatchId));
    public Task<TransactionResult> PostPurchaseInvoiceMatchAsync(PostPurchaseInvoiceMatchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseInvoiceMatchId));
    public Task<TransactionResult> ReversePurchaseInvoiceMatchAsync(ReversePurchaseInvoiceMatchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PurchaseInvoiceMatchId));
    public Task<TransactionResult> UnmatchPurchaseOrderReceiptBillAsync(UnmatchPurchaseOrderReceiptBillRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InventoryReceiptId));
    public Task<TransactionResult> ReverseInventoryReceiptAsync(ReverseInventoryReceiptRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InventoryReceiptId));
    public Task<TransactionResult> AuthorizeSupplierReturnAsync(AuthorizeSupplierReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> CancelSupplierReturnAsync(CancelSupplierReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SupplierReturnAuthorizationId));
    public Task<TransactionResult> ShipSupplierReturnAsync(ShipSupplierReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ApplySupplierReturnCreditAsync(ApplySupplierReturnCreditRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> RefundSupplierReturnCreditAsync(RefundSupplierReturnCreditRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseSupplierReturnShipmentAsync(ReverseSupplierReturnShipmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SupplierReturnShipmentId));
    public Task<TransactionResult> ReverseSupplierReturnCreditApplicationAsync(ReverseSupplierReturnCreditApplicationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SupplierReturnCreditApplicationId));
    public Task<TransactionResult> ReverseSupplierReturnCreditRefundAsync(ReverseSupplierReturnCreditRefundRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.SupplierReturnCreditRefundId));
    public Task<TransactionResult> SaveLandedCostAllocationAsync(SaveLandedCostAllocationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SubmitLandedCostAllocationAsync(SubmitLandedCostAllocationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.LandedCostAllocationId));
    public Task<TransactionResult> DecideLandedCostAllocationAsync(DecideLandedCostAllocationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.LandedCostAllocationId));
    public Task<TransactionResult> CancelLandedCostAllocationAsync(CancelLandedCostAllocationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.LandedCostAllocationId));
    public Task<TransactionResult> PostLandedCostAllocationAsync(PostLandedCostAllocationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.LandedCostAllocationId));
    public Task<TransactionResult> ReverseLandedCostAllocationAsync(ReverseLandedCostAllocationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.LandedCostAllocationId));
}

internal sealed class StubAccountingInterchangeService : IAccountingInterchangeService
{
    public Task<AccountingInterchangeExport?> ExportQuickBooksOnlineCsvAsync(string entity, CancellationToken cancellationToken = default) => Task.FromResult<AccountingInterchangeExport?>(null);
    public Task<AccountingInterchangeImportResult> ImportQuickBooksOnlineCsvAsync(string entity, Stream content, AccountingInterchangeImportOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(AccountingInterchangeImportResult.Success(0, options?.DryRun ?? false));
    public Task<IReadOnlyList<AccountingInterchangeBatchSnapshot>> GetRecentBatchesAsync(int limit = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AccountingInterchangeBatchSnapshot>>(
        [new(Guid.NewGuid(), "quickbooks-online", "customers", "malformed-customers.csv", new string('a', 64), "Rejected", true, 1, 0, 0, 1, ["Fix row 2."], "Controller", DateTimeOffset.UtcNow)]);
}
