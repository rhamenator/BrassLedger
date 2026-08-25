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

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task AccountRecovery_UsesUniformResponseAndRejectsInvalidActionLink(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);

        var recoveryResponse = await session.Page.Context.APIRequest.GetAsync($"{session.BaseUrl}/forgot-password");
        Assert.Equal("no-store, no-cache", recoveryResponse.Headers["cache-control"]);
        await session.GotoAsync("/forgot-password");
        await session.WaitForHeadingAsync("Request a password reset.");
        await session.Page.GetByLabel("Username or verified email").FillAsync("definitely-missing@example.test");
        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Request reset link" }).ClickAsync();
        await session.Page.GetByText("If the account and verified email are eligible", new() { Exact = false }).WaitForAsync();
        Assert.DoesNotContain("not found", await session.Page.Locator("body").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        var actionResponse = await session.Page.Context.APIRequest.GetAsync($"{session.BaseUrl}/account/action/start?token=invalid-opaque-token", new() { MaxRedirects = 0 });
        Assert.Equal("no-store, no-cache", actionResponse.Headers["cache-control"]);
        await session.GotoAsync("/account/action/start?token=invalid-opaque-token");
        await session.WaitForHeadingAsync("This link cannot be used.");
        Assert.Contains("invalid, expired, already used", await session.Page.Locator("body").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        await session.AssertNoUiFailuresAsync("account recovery invalid-link handling");
    }
}
