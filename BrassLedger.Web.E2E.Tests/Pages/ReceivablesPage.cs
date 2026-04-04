namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class ReceivablesPage
{
    private readonly UiSession _session;

    public ReceivablesPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/receivables");
        await _session.WaitForHeadingAsync("Customers, invoices, and open-balance follow-up.");
    }

    public async Task AssertCustomerAndInvoiceDataAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("C-1001 - Red Mesa Builders", content);
        Assert.Contains("INV-24015", content);
        Assert.Contains("Lakeview Retail Group", content);
        Assert.Contains("$12,720.00", content);
    }
}
