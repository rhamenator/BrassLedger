using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class ConsolidationAdministrationTests(PlaywrightWebAppFixture fixture)
{
    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ConsolidationAdministration_SavesControlledAverageRateAndShowsTranslationPolicy(BrowserKind browserKind)
    {
        await fixture.CreateConsolidationAdministratorAsync();
        try
        {
            await using var session = await fixture.CreateSessionAsync(browserKind);
            await session.SignInAsync("integration-admin");
            await session.GotoAsync("/administration");
            await session.WaitForHeadingAsync("Define role templates, separate duties, and prepare replacement access before it becomes urgent.");

            await session.Page.GetByLabel("Exchange rate base currency").FillAsync("USD");
            await session.Page.GetByLabel("Exchange rate quote currency").FillAsync("CAD");
            await session.Page.GetByLabel("Exchange rate type").SelectOptionAsync("Average");
            await session.Page.GetByLabel("Average exchange rate period start").FillAsync("2026-01-01");
            await session.Page.GetByLabel("Exchange rate effective date").FillAsync("2026-12-31");
            await session.Page.GetByLabel("Exchange rate", new() { Exact = true }).FillAsync("1.25");
            await session.Page.GetByLabel("Exchange rate source", new() { Exact = true }).FillAsync("E2E independently reviewed rate");
            await session.Page.GetByLabel("Exchange rate source reference").FillAsync("https://example.test/e2e-rates");
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save exchange rate" }).ClickAsync();

            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Controlled exchange rates" })).ToContainTextAsync("USD/CAD");
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Controlled exchange rates" })).ToContainTextAsync("Average");
            await Assertions.Expect(session.Page.GetByLabel("CTA reporting account number")).ToBeVisibleAsync();
            await session.AssertNoUiFailuresAsync("controlled consolidation translation administration");
        }
        finally
        {
            await fixture.RemoveQuickBooksAdministratorAsync();
        }
    }
}
