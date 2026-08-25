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

        foreach (var route in new[] { "/", "/ledger", "/receivables", "/payables", "/reporting", "/publish" })
        {
            await session.GotoAsync(route);
            await session.AssertSingleVisibleHeadingAsync();
            await session.AssertHeadingOrderAsync();
            await session.AssertInteractiveElementsHaveNamesAsync();
            await session.AssertNoUiFailuresAsync($"accessibility checks on {route}");
        }

        await using var payrollSession = await _fixture.CreateSessionAsync(browserKind);
        await payrollSession.SignInAsync("payroll");
        await payrollSession.GotoAsync("/payroll");
        await payrollSession.WaitForHeadingAsync("Employees, labor cost, and tax-ready setup.");
        await payrollSession.AssertSingleVisibleHeadingAsync();
        await payrollSession.AssertHeadingOrderAsync();
        await payrollSession.AssertInteractiveElementsHaveNamesAsync();
        await payrollSession.AssertNoUiFailuresAsync("accessibility checks on /payroll");

        await using var operationsSession = await _fixture.CreateSessionAsync(browserKind);
        await operationsSession.SignInAsync("operations");
        await operationsSession.GotoAsync("/operations");
        await operationsSession.WaitForHeadingAsync("Operational flow from stock to shipment.");
        await operationsSession.AssertSingleVisibleHeadingAsync();
        await operationsSession.AssertHeadingOrderAsync();
        await operationsSession.AssertInteractiveElementsHaveNamesAsync();
        await operationsSession.AssertNoUiFailuresAsync("accessibility checks on /operations");
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
