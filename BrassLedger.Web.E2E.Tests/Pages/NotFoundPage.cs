namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class NotFoundPage
{
    private readonly UiSession _session;

    public NotFoundPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync(string path)
    {
        await _session.GotoAsync(path, allowHttpError: true);
        await _session.WaitForHeadingAsync("That page does not exist.");
    }

    public async Task AssertFallbackAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("Return to overview", content);
    }
}
