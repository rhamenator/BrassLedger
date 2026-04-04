namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class OperationsPage
{
    private readonly UiSession _session;

    public OperationsPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/operations");
        await _session.WaitForHeadingAsync("Operational flow from stock to shipment.");
    }

    public async Task AssertOperationsDataAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("FG-200", content);
        Assert.Contains("SO-3107", content);
        Assert.Contains("PO-4101", content);
        Assert.Contains("Compression Fitting Kit", content);
    }
}
