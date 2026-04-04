namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class LedgerPage
{
    private readonly UiSession _session;

    public LedgerPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/ledger");
        await _session.WaitForHeadingAsync("Core accounting balances and posting history.");
    }

    public async Task AssertSeededDataAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("1000 - Operating Cash", content);
        Assert.Contains("JE-2401", content);
        Assert.Contains("Primary Operating", content);
        Assert.Contains("Payroll Clearing", content);
    }
}
