using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class QuickBooksAdministrationTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public QuickBooksAdministrationTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task QuickBooksAdministration_ShowsSafeConfigurationAndDryRunFirstControls(BrowserKind browserKind)
    {
        await _fixture.CreateQuickBooksAdministratorAsync();
        try
        {
            await using var session = await _fixture.CreateSessionAsync(browserKind);
            await session.SignInAsync("integration-admin");

            await session.GotoAsync("/administration");
            await session.WaitForHeadingAsync("Define role templates, separate duties, and prepare replacement access before it becomes urgent.");

            await Assertions.Expect(session.Page.GetByText("QuickBooks Online OAuth is not configured on this installation.")).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Button, new() { Name = "Connect through Intuit" })).ToBeDisabledAsync();
            await Assertions.Expect(session.Page.GetByLabel("QuickBooks API sync entity")).ToHaveValueAsync("accounts");
            await Assertions.Expect(session.Page.GetByLabel("QuickBooks connection name")).ToBeVisibleAsync();
            var providerOptions = await session.Page.GetByLabel("Integration provider").Locator("option").AllTextContentsAsync();
            Assert.DoesNotContain(providerOptions, option => option.Contains("QuickBooks", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("QuickBooks credentials JSON", await session.Page.Locator("body").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
            await session.AssertNoUiFailuresAsync("QuickBooks administration safety controls");
        }
        finally
        {
            await _fixture.RemoveQuickBooksAdministratorAsync();
        }
    }
}
