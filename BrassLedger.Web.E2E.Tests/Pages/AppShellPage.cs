using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class AppShellPage
{
    private readonly UiSession _session;

    public AppShellPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/");
    }

    public async Task NavigateMenuAsync(string href, string expectedHeading)
    {
        await _session.Page.Locator($"a.nav-link[href='{href}']").ClickAsync();
        await _session.WaitForHeadingAsync(expectedHeading);
    }
}
