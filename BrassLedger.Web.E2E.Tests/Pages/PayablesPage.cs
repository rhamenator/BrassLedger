namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class PayablesPage
{
    private readonly UiSession _session;

    public PayablesPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/payables");
        await _session.WaitForHeadingAsync("Vendor management and outgoing cash commitments.");
    }

    public async Task AssertVendorAndBillDataAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("V-2001 - Ironwood Steel Supply", content);
        Assert.Contains("B-8810", content);
        Assert.Contains("Apex Staffing", content);
        Assert.Contains("$13,210.50", content);
    }
}
