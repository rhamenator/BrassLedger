using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class ItemizedDocumentWorkflowTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public ItemizedDocumentWorkflowTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ReceivablesAndPayables_PostItemizedDocuments(BrowserKind browserKind)
    {
        await _fixture.CreateSubledgerWorkflowUsersAsync();
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var receivables = new ReceivablesPage(session);
        var payables = new PayablesPage(session);

        await receivables.OpenAsync();
        var invoiceNumber = $"INV-E2E-{browserKind}";
        await receivables.CreateItemizedInvoiceDraftAsync(invoiceNumber);
        await using (var approverSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await approverSession.SignInAsync("e2e-ar-approver"); var approver = new ReceivablesPage(approverSession); await approver.OpenAsync(); await approver.ApproveInvoiceAsync(invoiceNumber);
        }
        await using (var posterSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await posterSession.SignInAsync("e2e-ar-poster"); var poster = new ReceivablesPage(posterSession); await poster.OpenAsync(); await poster.PostInvoiceAsync(invoiceNumber, "$165.00");
        }
        await receivables.OpenAsync();
        await receivables.RecordAndReturnCustomerPaymentAsync(invoiceNumber, $"DEP-E2E-{browserKind}");
        await receivables.RecordAndReverseCreditMemoAsync($"CM-E2E-{browserKind}");
        await session.AssertNoUiFailuresAsync("itemized invoice workflow");

        await payables.OpenAsync();
        var billNumber = $"B-E2E-{browserKind}";
        await payables.CreateItemizedBillDraftAsync(billNumber);
        await using (var approverSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await approverSession.SignInAsync("e2e-ap-approver"); var approver = new PayablesPage(approverSession); await approver.OpenAsync(); await approver.ApproveBillAsync(billNumber);
        }
        await using (var posterSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await posterSession.SignInAsync("e2e-ap-poster"); var poster = new PayablesPage(posterSession); await poster.OpenAsync(); await poster.PostBillAsync(billNumber, "$90.00");
        }
        await payables.OpenAsync();
        await payables.RecordAndVoidVendorPaymentAsync(billNumber, $"CHK-E2E-{browserKind}");
        await payables.RecordAndReverseVendorCreditAsync($"VC-E2E-{browserKind}");
        await session.AssertNoUiFailuresAsync("itemized bill workflow");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task InvoiceReviewer_CanRejectAndPreparerCanCorrectAndResubmit(BrowserKind browserKind)
    {
        await _fixture.CreateSubledgerWorkflowUsersAsync();
        var invoiceNumber = $"INV-REVISE-{Guid.NewGuid():N}"[..24];
        const string rejectionReason = "Clarify the customer-facing work description.";

        await using var preparerSession = await _fixture.CreateSessionAsync(browserKind);
        await preparerSession.SignInAsync();
        var preparer = new ReceivablesPage(preparerSession);
        await preparer.OpenAsync();
        await preparer.CreateItemizedInvoiceDraftAsync(invoiceNumber);

        await using (var reviewerSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await reviewerSession.SignInAsync("e2e-ar-approver");
            var reviewer = new ReceivablesPage(reviewerSession);
            await reviewer.OpenAsync();
            await reviewer.RejectInvoiceAsync(invoiceNumber, rejectionReason);
            await reviewerSession.AssertNoUiFailuresAsync("invoice rejection workflow");
        }

        await preparer.OpenAsync();
        await preparer.AssertRejectedInvoiceAsync(invoiceNumber, rejectionReason);
        await preparer.CreateItemizedInvoiceDraftAsync(invoiceNumber);

        await using (var reviewerSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await reviewerSession.SignInAsync("e2e-ar-approver");
            var reviewer = new ReceivablesPage(reviewerSession);
            await reviewer.OpenAsync();
            await reviewer.ApproveInvoiceAsync(invoiceNumber);
        }
        await using (var posterSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await posterSession.SignInAsync("e2e-ar-poster");
            var poster = new ReceivablesPage(posterSession);
            await poster.OpenAsync();
            await poster.PostInvoiceAsync(invoiceNumber, "$165.00");
            await posterSession.AssertNoUiFailuresAsync("corrected invoice posting workflow");
        }
    }
}
