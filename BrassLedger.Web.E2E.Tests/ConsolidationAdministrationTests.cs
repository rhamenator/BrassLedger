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

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Reporting_PreparesApprovesPostsAndReversesConsolidationAdjustment(BrowserKind browserKind)
    {
        await fixture.CreateConsolidationWorkflowAsync();
        var reference = $"E2E-CONSOL-{browserKind}";
        await using (var preparer = await fixture.CreateSessionAsync(browserKind))
        {
            await preparer.SignInAsync("integration-admin"); await preparer.GotoAsync("/reporting"); await preparer.WaitForHeadingAsync("Reports, labels, forms, and print fidelity stay in the product.");
            await preparer.Page.Locator("#adjustmentPeriodStart").FillAsync("2026-01-01"); await preparer.Page.Locator("#adjustmentAsOf").FillAsync("2026-08-31");
            await preparer.Page.Locator("#adjustmentReference").FillAsync(reference); await preparer.Page.Locator("#adjustmentDescription").FillAsync("E2E reporting-only true-up");
            var accountSelectors = preparer.Page.GetByLabel("Reporting account");
            await accountSelectors.Nth(0).SelectOptionAsync(new SelectOptionValue { Index = 1 }); await accountSelectors.Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 2 });
            await preparer.Page.GetByLabel("Adjustment debit").Nth(0).FillAsync("25.00"); await preparer.Page.GetByLabel("Adjustment credit").Nth(1).FillAsync("25.00");
            await preparer.Page.GetByRole(AriaRole.Button, new() { Name = "Prepare draft" }).ClickAsync();
            await Assertions.Expect(preparer.Page.GetByRole(AriaRole.Table, new() { Name = "Retained consolidation adjustments" })).ToContainTextAsync(reference);
            await Assertions.Expect(preparer.Page.GetByText("The consolidation draft was retained for independent review.")).ToBeVisibleAsync();
            await preparer.AssertNoUiFailuresAsync("consolidation adjustment preparation");
        }
        await using (var reviewer = await fixture.CreateSessionAsync(browserKind))
        {
            await reviewer.SignInAsync("e2e-consolidation-reviewer"); await reviewer.GotoAsync("/reporting");
            var row = reviewer.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = reference }); await row.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
            await Assertions.Expect(reviewer.Page.GetByText("The consolidation adjustment was approved.")).ToBeVisibleAsync(); await reviewer.AssertNoUiFailuresAsync("consolidation adjustment approval");
        }
        await using (var poster = await fixture.CreateSessionAsync(browserKind))
        {
            await poster.SignInAsync("e2e-consolidation-poster"); await poster.GotoAsync("/reporting");
            var row = poster.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = reference }); await row.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
            await Assertions.Expect(poster.Page.GetByText("The consolidation adjustment was posted to the reporting ledger.")).ToBeVisibleAsync();
            await poster.Page.Locator("#adjustmentDecisionReason").FillAsync("E2E non-destructive correction");
            row = poster.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = reference }); await row.GetByRole(AriaRole.Button, new() { Name = "Reverse", Exact = true }).ClickAsync();
            await Assertions.Expect(poster.Page.GetByText("A non-destructive reversal was posted to the reporting ledger.")).ToBeVisibleAsync();
            await Assertions.Expect(poster.Page.GetByRole(AriaRole.Table, new() { Name = "Retained consolidation adjustments" })).ToContainTextAsync("Reversed");
            await poster.AssertNoUiFailuresAsync("consolidation adjustment posting and reversal");
        }
    }
}
