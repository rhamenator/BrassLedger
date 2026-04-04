using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class AccessibilityTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public AccessibilityTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task CorePages_HaveSingleHeading_AndNamedInteractiveElements(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();

        foreach (var route in new[] { "/", "/ledger", "/receivables", "/reporting", "/publish" })
        {
            await session.GotoAsync(route);
            await session.AssertSingleVisibleHeadingAsync();
            await session.AssertHeadingOrderAsync();
            await session.AssertInteractiveElementsHaveNamesAsync();
            await session.AssertNoUiFailuresAsync($"accessibility checks on {route}");
        }
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task KeyboardNavigation_CanReachAndActivateLedgerLink(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var overview = new OverviewPage(session);

        await overview.OpenAsync();
        await session.AssertKeyboardCanFocusAndActivateAsync("ledger", "Core accounting balances and posting history.");
        await session.AssertNoUiFailuresAsync("keyboard navigation to ledger");
    }
}
