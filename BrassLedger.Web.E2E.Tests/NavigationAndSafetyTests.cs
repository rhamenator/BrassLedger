using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class NavigationAndSafetyTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public NavigationAndSafetyTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task MajorRoutes_LoadWithoutClientOrServerFailures(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();

        var routes = new (string Path, string Heading)[]
        {
            ("/", "Brass Ledger Manufacturing coordinates finance, payroll, operations, reporting, and tax work from one workspace."),
            ("/modules", "Every legacy module is open to every user."),
            ("/ledger", "Core accounting balances and posting history."),
            ("/receivables", "Customers, invoices, and open-balance follow-up."),
            ("/payables", "Vendor management and outgoing cash commitments."),
            ("/projects", "Job tracking with room for industry-specific workflows."),
            ("/reporting", "Reports, labels, forms, and print fidelity stay in the product."),
            ("/taxes", "Keep withholdings, filing rules, and odd state behavior in editable tables instead of buried code."),
            ("/publish", "One .NET web application, packaged per platform."),
            ("/account/security", "Protect your operator account and review recent access.")
        };

        foreach (var route in routes)
        {
            await session.GotoAsync(route.Path);
            await session.WaitForHeadingAsync(route.Heading);
            await session.AssertNoUiFailuresAsync(route.Path);
        }

        await using var operationsSession = await _fixture.CreateSessionAsync(browserKind);
        await operationsSession.SignInAsync("operations");
        await operationsSession.GotoAsync("/operations");
        await operationsSession.WaitForHeadingAsync("Operational flow from stock to shipment.");
        await operationsSession.AssertNoUiFailuresAsync("/operations");

        await using var payrollSession = await _fixture.CreateSessionAsync(browserKind);
        await payrollSession.SignInAsync("payroll");
        await payrollSession.GotoAsync("/payroll");
        await payrollSession.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");
        await payrollSession.AssertNoUiFailuresAsync("/payroll");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task SidebarNavigation_RemainsResponsiveAcrossModules(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var shell = new AppShellPage(session);

        await shell.OpenAsync();
        await shell.NavigateMenuAsync("ledger", "Core accounting balances and posting history.");
        await shell.NavigateMenuAsync("receivables", "Customers, invoices, and open-balance follow-up.");
        await shell.NavigateMenuAsync("reporting", "Reports, labels, forms, and print fidelity stay in the product.");
        await shell.NavigateMenuAsync("publish", "One .NET web application, packaged per platform.");

        Assert.Equal(0, await session.Page.Locator("a.nav-link[href='operations']").CountAsync());
        Assert.Equal(0, await session.Page.Locator("a.nav-link[href='payroll']").CountAsync());

        await session.AssertNoUiFailuresAsync("sidebar navigation");

        await using var operationsSession = await _fixture.CreateSessionAsync(browserKind);
        await operationsSession.SignInAsync("operations");
        var operationsShell = new AppShellPage(operationsSession);
        await operationsShell.OpenAsync();
        await operationsShell.NavigateMenuAsync("operations", "Operational flow from stock to shipment.");
        Assert.Equal(0, await operationsSession.Page.Locator("a.nav-link[href='payroll']").CountAsync());
        await operationsSession.AssertNoUiFailuresAsync("operations sidebar navigation");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task UnknownRoute_ShowsSafeFallbackPage(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        var page = new NotFoundPage(session);

        await page.OpenAsync("/this-route-does-not-exist");
        await page.AssertFallbackAsync();
        await session.AssertNoUiFailuresAsync("unknown route");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ProtectedRoute_RedirectsAnonymousUserToLogin(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);

        await session.GotoAsync("/ledger");
        await session.WaitForHeadingAsync("Sign in to BrassLedger.");
        await session.AssertNoUiFailuresAsync("anonymous redirect to login");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task LoginPage_ShowsFriendlyErrorForInvalidCredentials(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);

        await session.GotoAsync("/login");
        await session.WaitForHeadingAsync("Sign in to BrassLedger.");
        await session.Page.Locator("input[name='userName']").FillAsync("controller");
        await session.Page.Locator("input[name='password']").FillAsync("not-the-password");
        await session.Page.Locator("button[type='submit']").ClickAsync();
        await session.WaitForHeadingAsync("Sign in to BrassLedger.");

        var content = await session.Page.ContentAsync();
        Assert.Contains("did not match an active operator", content);
        await session.AssertNoUiFailuresAsync("invalid login");
    }
}
