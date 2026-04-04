namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class ReportingPage
{
    private readonly UiSession _session;

    public ReportingPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/reporting");
        await _session.WaitForHeadingAsync("Reports, labels, forms, and print fidelity stay in the product.");
    }

    public async Task AssertReportingCatalogAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("RDL-GL-TRIAL", content);
        Assert.Contains("Trial Balance", content);
        Assert.Contains("LBL-SHIP-4X6", content);
        Assert.Contains("Shipping Label 4x6", content);
    }
}
