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
        authorization.SetPolicies(
            BrassLedgerAuthorizationPolicies.PrepareJournals,
            BrassLedgerAuthorizationPolicies.ApproveJournals,
            BrassLedgerAuthorizationPolicies.PostJournals,
            BrassLedgerAuthorizationPolicies.ReverseJournals);
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
    }
}

internal sealed class StubAccountingTransactionService : IAccountingTransactionService
{
    public Task<TransactionResult> SaveJournalEntryDraftAsync(SaveJournalEntryDraftRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> ApproveJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(journalEntryId));
    public Task<TransactionResult> PostApprovedJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(journalEntryId));
    public Task<TransactionResult> ReverseJournalEntryAsync(ReverseJournalEntryRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> PostJournalEntryAsync(PostJournalEntryRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> PostJournalEntriesAsync(IReadOnlyList<PostJournalEntryRequest> requests, CancellationToken cancellationToken = default) => Task.FromResult(requests.Count == 0 ? TransactionResult.Failure("Provide at least one journal entry to import.") : TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> CreateVendorBillAsync(CreateVendorBillRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ApplyInvoicePaymentAsync(ApplyInvoicePaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.InvoiceId));
    public Task<TransactionResult> ApplyBillPaymentAsync(ApplyBillPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.VendorBillId));
    public Task<TransactionResult> RecordCustomerPaymentAsync(RecordCustomerPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> RecordVendorPaymentAsync(RecordVendorPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> ReverseSubledgerPaymentAsync(ReverseSubledgerPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.PaymentId));
    public Task<TransactionResult> ReconcileBankAccountAsync(ReconcileBankAccountRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.BankAccountId));
    public Task<TransactionResult> UpdateBankLedgerMappingAsync(UpdateBankLedgerMappingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.BankAccountId));
    public Task<TransactionResult> PostPayrollRunAsync(PostPayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<PayrollRunEstimate?> PreviewEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult<PayrollRunEstimate?>(null);
    public Task<TransactionResult> PostEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<TransactionResult> SaveEmployeePayrollSetupAsync(SaveEmployeePayrollSetupRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.EmployeeId));
    public Task<TransactionResult> SavePayrollJurisdictionRuleAsync(SavePayrollJurisdictionRuleRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> RecordInventoryAdjustmentAsync(RecordInventoryAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
}

internal sealed class StubAccountingInterchangeService : IAccountingInterchangeService
{
    public Task<AccountingInterchangeExport?> ExportQuickBooksOnlineCsvAsync(string entity, CancellationToken cancellationToken = default) => Task.FromResult<AccountingInterchangeExport?>(null);
    public Task<AccountingInterchangeImportResult> ImportQuickBooksOnlineCsvAsync(string entity, Stream content, CancellationToken cancellationToken = default) => Task.FromResult(AccountingInterchangeImportResult.Success(0));
}
