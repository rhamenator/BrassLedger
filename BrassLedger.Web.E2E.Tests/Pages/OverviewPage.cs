namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class OverviewPage
{
    private readonly UiSession _session;

    public OverviewPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/");
        await _session.WaitForHeadingAsync("Brass Ledger Manufacturing coordinates finance, payroll, operations, reporting, and tax work from one workspace.");
    }

    public async Task OpenLedgerQuickActionAsync()
    {
        await _session.Page.Locator("a.action-primary[href='/ledger']").ClickAsync();
        await _session.WaitForHeadingAsync("Core accounting balances and posting history.");
    }

    public async Task AssertKeyMetricsAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("$112,540", content);
        Assert.Contains("$34,716", content);
        Assert.Contains("$31,845", content);
        Assert.Contains("$24,367", content);
    }
}
