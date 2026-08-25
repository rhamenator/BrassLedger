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
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var receivables = new ReceivablesPage(session);
        var payables = new PayablesPage(session);

        await receivables.OpenAsync();
        var invoiceNumber = $"INV-E2E-{browserKind}";
        await receivables.CreateItemizedInvoiceAsync(invoiceNumber);
        await receivables.RecordAndReturnCustomerPaymentAsync(invoiceNumber, $"DEP-E2E-{browserKind}");
        await receivables.RecordAndReverseCreditMemoAsync($"CM-E2E-{browserKind}");
        await session.AssertNoUiFailuresAsync("itemized invoice workflow");

        await payables.OpenAsync();
        var billNumber = $"B-E2E-{browserKind}";
        await payables.CreateItemizedBillAsync(billNumber);
        await payables.RecordAndVoidVendorPaymentAsync(billNumber, $"CHK-E2E-{browserKind}");
        await payables.RecordAndReverseVendorCreditAsync($"VC-E2E-{browserKind}");
        await session.AssertNoUiFailuresAsync("itemized bill workflow");
    }
}
