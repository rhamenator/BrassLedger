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

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Reporting_PreparesApprovesAndPublishesVersionedFrameworkDisclosures(BrowserKind browserKind)
    {
        await fixture.CreateConsolidationWorkflowAsync();
        try
        {
            await using (var preparer = await fixture.CreateSessionAsync(browserKind))
            {
                await preparer.SignInAsync("integration-admin"); await preparer.GotoAsync("/reporting");
                await preparer.Page.Locator("#disclosurePeriodStart").FillAsync("2026-01-01");
                await preparer.Page.Locator("#disclosureAsOf").FillAsync("2026-08-31");
                await preparer.Page.GetByRole(AriaRole.Button, new() { Name = "Add narrative disclosure" }).ClickAsync();
                await preparer.Page.GetByLabel("Disclosure category").FillAsync("GoingConcern");
                await preparer.Page.GetByLabel("Disclosure code").FillAsync("E2E-GC-1");
                await preparer.Page.GetByLabel("Disclosure title").FillAsync("E2E going concern assessment");
                await preparer.Page.GetByLabel("Disclosure narrative").FillAsync("Management reviewed twelve months of liquidity forecasts and covenant headroom.");
                await preparer.Page.GetByLabel("Disclosure source reference").FillAsync("E2E board package WP-9");
                await preparer.Page.GetByRole(AriaRole.Button, new() { Name = "Prepare disclosure package" }).ClickAsync();
                await Assertions.Expect(preparer.Page.GetByText("The disclosure package was retained for independent review.")).ToBeVisibleAsync();
                await Assertions.Expect(preparer.Page.GetByRole(AriaRole.Table, new() { Name = "Retained consolidation disclosure packages" })).ToContainTextAsync("1 narrative disclosure");
                await preparer.AssertNoUiFailuresAsync("framework disclosure preparation");
            }
            await using (var reviewer = await fixture.CreateSessionAsync(browserKind))
            {
                await reviewer.SignInAsync("e2e-consolidation-reviewer"); await reviewer.GotoAsync("/reporting");
                var disclosureRow = reviewer.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = "2026 annual" });
                await disclosureRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
                await Assertions.Expect(reviewer.Page.GetByText("The disclosure package was independently approved.")).ToBeVisibleAsync();
                await reviewer.Page.Locator("#adjustmentPeriodStart").FillAsync("2026-01-01"); await reviewer.Page.Locator("#adjustmentAsOf").FillAsync("2026-08-31");
                await reviewer.Page.GetByRole(AriaRole.Button, new() { Name = "Run statement package" }).ClickAsync();
                await Assertions.Expect(reviewer.Page.GetByRole(AriaRole.Heading, new() { Name = "US-GAAP disclosures · 2026 annual" })).ToBeVisibleAsync();
                await Assertions.Expect(reviewer.Page.GetByText("Management reviewed twelve months of liquidity forecasts", new() { Exact = false })).ToBeVisibleAsync();
                await reviewer.AssertNoUiFailuresAsync("approved framework disclosure statement output");
            }
        }
        finally
        {
            await fixture.RemoveIntercompanyMatchingWorkflowAsync();
        }
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Reporting_PreparesApprovesPostsAndReversesAcquisitionSchedule(BrowserKind browserKind)
    {
        await fixture.CreateConsolidationWorkflowAsync();
        var reference = $"E2E-ACQ-{browserKind}";
        try
        {
            await using (var preparer = await fixture.CreateSessionAsync(browserKind))
            {
                await preparer.SignInAsync("integration-admin"); await preparer.GotoAsync("/reporting");
                await preparer.Page.Locator("#ownershipSubject").SelectOptionAsync("71000000-0000-0000-0000-000000000010");
                await preparer.Page.Locator("#ownershipEventDate").FillAsync("2026-08-31"); await preparer.Page.Locator("#ownershipReference").FillAsync(reference); await preparer.Page.Locator("#ownershipAfter").FillAsync("0.75");
                await preparer.Page.Locator("#ownershipRationale").FillAsync("E2E controller-reviewed purchase-price allocation"); await preparer.Page.Locator("#ownershipSource").FillAsync("E2E acquisition working paper PPA-1");
                await preparer.Page.GetByLabel("Consideration transferred").FillAsync("80"); await preparer.Page.GetByLabel("NCI recognized").FillAsync("20"); await preparer.Page.GetByLabel("Identifiable net assets at fair value").FillAsync("90"); await preparer.Page.GetByLabel("Goodwill", new() { Exact = true }).FillAsync("10");
                var accounts = preparer.Page.GetByLabel("Ownership-event reporting account"); await accounts.Nth(0).SelectOptionAsync("1000"); await accounts.Nth(1).SelectOptionAsync("3000");
                await preparer.Page.GetByLabel("Debit", new() { Exact = true }).Nth(0).FillAsync("100"); await preparer.Page.GetByLabel("Credit", new() { Exact = true }).Nth(1).FillAsync("100");
                await preparer.Page.GetByRole(AriaRole.Button, new() { Name = "Prepare ownership event" }).ClickAsync();
                await Assertions.Expect(preparer.Page.GetByText("The ownership event was retained for independent review.")).ToBeVisibleAsync();
                await Assertions.Expect(preparer.Page.GetByRole(AriaRole.Table, new() { Name = "Retained consolidation ownership events" })).ToContainTextAsync(reference);
                await preparer.AssertNoUiFailuresAsync("acquisition schedule preparation");
            }
            await using (var reviewer = await fixture.CreateSessionAsync(browserKind))
            {
                await reviewer.SignInAsync("e2e-consolidation-reviewer"); await reviewer.GotoAsync("/reporting");
                var row = reviewer.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = reference }); await row.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
                await Assertions.Expect(reviewer.Page.GetByText("The ownership event was independently approved.")).ToBeVisibleAsync(); await reviewer.AssertNoUiFailuresAsync("acquisition schedule approval");
            }
            await using (var poster = await fixture.CreateSessionAsync(browserKind))
            {
                await poster.SignInAsync("e2e-consolidation-poster"); await poster.GotoAsync("/reporting");
                var row = poster.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = reference }); await row.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
                await Assertions.Expect(poster.Page.GetByText("The ownership event was posted to consolidated reporting.")).ToBeVisibleAsync();
                await poster.Page.Locator("#ownershipDecisionReason").FillAsync("E2E corrected valuation schedule"); await poster.Page.Locator("#ownershipReversalDate").FillAsync("2026-09-01");
                row = poster.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = reference }); await row.GetByRole(AriaRole.Button, new() { Name = "Reverse", Exact = true }).ClickAsync();
                await Assertions.Expect(poster.Page.GetByText("A dated, traceable reversing ownership event was posted.")).ToBeVisibleAsync();
                await Assertions.Expect(poster.Page.GetByRole(AriaRole.Table, new() { Name = "Retained consolidation ownership events" })).ToContainTextAsync("Reversed");
                await poster.AssertNoUiFailuresAsync("acquisition schedule posting and reversal");
            }
        }
        finally { await fixture.RemoveIntercompanyMatchingWorkflowAsync(); }
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Reporting_PreparesReviewedNciForPartiallyOwnedControlledSubsidiary(BrowserKind browserKind)
    {
        await fixture.CreateConsolidationWorkflowAsync();
        try
        {
            await using var session = await fixture.CreateSessionAsync(browserKind);
            await session.SignInAsync("integration-admin");
            await session.GotoAsync("/administration");
            await session.WaitForHeadingAsync("Define role templates, separate duties, and prepare replacement access before it becomes urgent.");
            var groupRow = session.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = "E2E controlled consolidation" });
            await Assertions.Expect(groupRow).ToContainTextAsync("Controlled subsidiary");
            await Assertions.Expect(groupRow).ToContainTextAsync("E2E reviewed control conclusion");
            await Assertions.Expect(groupRow).ToContainTextAsync("NCI: 39998 · Noncontrolling interests");
            await groupRow.GetByRole(AriaRole.Button, new() { Name = "Map accounts" }).ClickAsync();
            var mappings = session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidation account mappings" });
            await Assertions.Expect(mappings).ToContainTextAsync("Financing");
            await Assertions.Expect(mappings).ToContainTextAsync("E2E reviewed financing counterpart classification");
            var equityMapping = mappings.GetByRole(AriaRole.Row).Filter(new() { HasTextString = "3000" });
            await equityMapping.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
            await Assertions.Expect(session.Page.GetByLabel("Consolidation cash-flow activity")).ToHaveValueAsync("Financing");
            await Assertions.Expect(session.Page.GetByLabel("Consolidation cash-flow rationale")).ToHaveValueAsync("E2E reviewed financing counterpart classification");
            await groupRow.GetByRole(AriaRole.Button, new() { Name = "Present statements" }).ClickAsync();
            await session.Page.GetByLabel("Statement presentation reporting account").SelectOptionAsync(new SelectOptionValue { Index = 5 });
            await session.Page.GetByLabel("Statement presentation section code").FillAsync("EQUITY");
            await session.Page.GetByLabel("Statement presentation section caption").FillAsync("Equity");
            await session.Page.GetByLabel("Statement presentation section order").FillAsync("100");
            await session.Page.GetByLabel("Statement presentation line caption").FillAsync("Current earnings attributable to owners");
            await session.Page.GetByLabel("Statement presentation line order").FillAsync("100");
            await session.Page.GetByLabel("Statement presentation rationale").FillAsync("E2E reviewed current classification and liquidity presentation");
            await session.Page.GetByLabel("Statement presentation reviewed on").FillAsync("2026-01-01");
            await session.Page.GetByLabel("Statement presentation effective from").FillAsync("2026-01-01");
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save presentation policy" }).ClickAsync();
            var presentations = session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidation statement presentation policies" });
            await Assertions.Expect(presentations).ToContainTextAsync("CURRENT-EARNINGS");
            await Assertions.Expect(presentations).ToContainTextAsync("E2E reviewed current classification and liquidity presentation");
            await session.AssertNoUiFailuresAsync("statement presentation administration save");
            await session.GotoAsync("/reporting");
            await session.WaitForHeadingAsync("Reports, labels, forms, and print fidelity stay in the product.");
            await session.Page.Locator("#adjustmentPeriodStart").FillAsync("2026-01-01");
            await session.Page.Locator("#adjustmentAsOf").FillAsync("2026-08-31");
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Run statement package" }).ClickAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Consolidated balance sheet" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Consolidated income statement" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Consolidated statement of changes in equity" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Consolidated statement of cash flows" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidated balance sheet — Equity" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByText("Current earnings attributable to owners", new() { Exact = false }).First).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByText("Incomplete — resolve every warning before external use", new() { Exact = false })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidated statement reconciliation" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByText("source contribution(s)", new() { Exact = false }).First).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Download statement package CSV" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("/consolidation-groups/.+/statements\\.csv\\?periodStart=2026-01-01&asOf=2026-08-31"));
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Download statement package Excel" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("/consolidation-groups/.+/statements\\.xlsx\\?periodStart=2026-01-01&asOf=2026-08-31"));
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Download statement package PDF" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("/consolidation-groups/.+/statements\\.pdf\\?periodStart=2026-01-01&asOf=2026-08-31"));
            var statementExcelHref = await session.Page.GetByRole(AriaRole.Link, new() { Name = "Download statement package Excel" }).GetAttributeAsync("href");
            var statementExcelResponse = await session.Page.Context.APIRequest.GetAsync($"{session.BaseUrl}{statementExcelHref}", new() { Timeout = 120000 });
            Assert.True(statementExcelResponse.Ok, $"Statement Excel endpoint returned HTTP {statementExcelResponse.Status}.");
            Assert.Contains(".xlsx", statementExcelResponse.Headers["content-disposition"], StringComparison.OrdinalIgnoreCase);
            Assert.True((await statementExcelResponse.BodyAsync()).AsSpan().StartsWith("PK"u8));
            await session.Page.Locator("#comparisonPeriodStart").FillAsync("2025-01-01");
            await session.Page.Locator("#comparisonAsOf").FillAsync("2025-08-31");
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Compare statement periods" }).ClickAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "E2E controlled consolidation comparative statements" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidated balance sheet comparison" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidated income statement comparison" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidated statement of changes in equity comparison" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidated statement of cash flows comparison" })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Consolidated balance sheet comparison" })).ToContainTextAsync("Variance");
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Download comparative statement CSV" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("/consolidation-groups/.+/statements/comparative\\.csv\\?currentPeriodStart=2026-01-01&currentAsOf=2026-08-31&comparisonPeriodStart=2025-01-01&comparisonAsOf=2025-08-31"));
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Download comparative statement Excel" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("/consolidation-groups/.+/statements/comparative\\.xlsx\\?currentPeriodStart=2026-01-01&currentAsOf=2026-08-31&comparisonPeriodStart=2025-01-01&comparisonAsOf=2025-08-31"));
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Download comparative statement PDF" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("/consolidation-groups/.+/statements/comparative\\.pdf\\?currentPeriodStart=2026-01-01&currentAsOf=2026-08-31&comparisonPeriodStart=2025-01-01&comparisonAsOf=2025-08-31"));
            var comparativePdfHref = await session.Page.GetByRole(AriaRole.Link, new() { Name = "Download comparative statement PDF" }).GetAttributeAsync("href");
            var comparativePdfResponse = await session.Page.Context.APIRequest.GetAsync($"{session.BaseUrl}{comparativePdfHref}", new() { Timeout = 120000 });
            Assert.True(comparativePdfResponse.Ok, $"Comparative PDF endpoint returned HTTP {comparativePdfResponse.Status}.");
            Assert.Contains(".pdf", comparativePdfResponse.Headers["content-disposition"], StringComparison.OrdinalIgnoreCase);
            Assert.True((await comparativePdfResponse.BodyAsync()).AsSpan().StartsWith("%PDF"u8));
            await session.Page.Locator("#adjustmentKind").SelectOptionAsync("NoncontrollingInterest");
            await Assertions.Expect(session.Page.GetByText("does not infer acquisition accounting, goodwill, or the NCI amount", new() { Exact = false })).ToBeVisibleAsync();
            await session.Page.Locator("#adjustmentSubjectCompany").SelectOptionAsync("71000000-0000-0000-0000-000000000010");
            await session.Page.Locator("#adjustmentReference").FillAsync("NCI-E2E-CONTROLLED");
            await session.Page.Locator("#adjustmentDescription").FillAsync("E2E reviewed NCI equity attribution");
            var accounts = session.Page.GetByLabel("Reporting account");
            await accounts.Nth(0).SelectOptionAsync(new SelectOptionValue { Index = 2 });
            await accounts.Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 3 });
            await session.Page.GetByLabel("Adjustment debit").Nth(0).FillAsync("10.00");
            await session.Page.GetByLabel("Adjustment credit").Nth(1).FillAsync("10.00");
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Prepare draft" }).ClickAsync();

            await Assertions.Expect(session.Page.GetByText("The consolidation draft was retained for independent review.")).ToBeVisibleAsync();
            var retained = session.Page.GetByRole(AriaRole.Table, new() { Name = "Retained consolidation adjustments" });
            await Assertions.Expect(retained).ToContainTextAsync("NCI-E2E-CONTROLLED");
            await Assertions.Expect(retained).ToContainTextAsync("Noncontrolling-interest reclassification");
            await Assertions.Expect(retained).ToContainTextAsync("E2E intercompany affiliate");
            await session.AssertNoUiFailuresAsync("reviewed noncontrolling-interest preparation");
        }
        finally
        {
            await fixture.RemoveIntercompanyMatchingWorkflowAsync();
        }
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task IntercompanyMatching_ConfiguresReviewsAndPreparesControlledElimination(BrowserKind browserKind)
    {
        await fixture.CreateConsolidationWorkflowAsync();
        try
        {
        await using var session = await fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync("integration-admin");
        await session.GotoAsync("/administration");
        await session.WaitForHeadingAsync("Define role templates, separate duties, and prepare replacement access before it becomes urgent.");
        var groupRow = session.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = "E2E controlled consolidation" });
        await groupRow.GetByRole(AriaRole.Button, new() { Name = "Trading partners" }).ClickAsync();

        var memberRecord = session.Page.GetByLabel("Intercompany member customer or vendor");
        var counterparty = session.Page.GetByLabel("Intercompany counterparty company");
        await memberRecord.SelectOptionAsync(new SelectOptionValue { Label = "Brass Ledger Manufacturing · Customer · E2E-IC-CUST · E2E intercompany affiliate" });
        await counterparty.SelectOptionAsync(new SelectOptionValue { Label = "E2E intercompany affiliate" });
        await session.Page.GetByLabel("Trading partner effective from").FillAsync("2026-01-01");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save trading-partner link" }).ClickAsync();
        await Assertions.Expect(session.Page.GetByText("Trading-partner link saved with retained effective-date history.")).ToBeVisibleAsync();

        await memberRecord.SelectOptionAsync(new SelectOptionValue { Label = "E2E intercompany affiliate · Vendor · E2E-IC-VEND · Brass Ledger Manufacturing" });
        await counterparty.SelectOptionAsync(new SelectOptionValue { Label = "Brass Ledger Manufacturing" });
        await session.Page.GetByLabel("Trading partner effective from").FillAsync("2026-01-01");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save trading-partner link" }).ClickAsync();
        var links = session.Page.GetByRole(AriaRole.Table, new() { Name = "Intercompany trading partner links" });
        await Assertions.Expect(links).ToContainTextAsync("E2E-IC-CUST");
        await Assertions.Expect(links).ToContainTextAsync("E2E-IC-VEND");

        await session.GotoAsync("/reporting");
        await session.WaitForHeadingAsync("Reports, labels, forms, and print fidelity stay in the product.");
        await session.Page.Locator("#adjustmentPeriodStart").FillAsync("2026-01-01");
        await session.Page.Locator("#adjustmentAsOf").FillAsync("2026-08-31");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Discover exact matches" }).ClickAsync();
        var matches = session.Page.GetByRole(AriaRole.Table, new() { Name = "Reviewed intercompany matches" });
        await Assertions.Expect(matches).ToContainTextAsync("E2E-IC-INV-1001");
        await Assertions.Expect(matches).ToContainTextAsync("125.00 USD");

        await session.Page.Locator("#adjustmentDecisionReason").FillAsync("E2E supporting documents require review");
        await matches.GetByRole(AriaRole.Button, new() { Name = "Exclude" }).ClickAsync();
        await Assertions.Expect(matches).ToContainTextAsync("Excluded");
        await Assertions.Expect(matches).ToContainTextAsync("E2E supporting documents require review");
        await matches.GetByRole(AriaRole.Button, new() { Name = "Restore" }).ClickAsync();
        await matches.GetByRole(AriaRole.Button, new() { Name = "Prepare elimination" }).ClickAsync();
        await Assertions.Expect(session.Page.Locator("#adjustmentMatchReference")).ToHaveValueAsync("IC-71000000000000000000000000000022-71000000000000000000000000000023");
        await Assertions.Expect(session.Page.GetByText("No accounting entry has been inferred or posted.", new() { Exact = false })).ToBeVisibleAsync();

        var accountSelectors = session.Page.GetByLabel("Reporting account");
        await accountSelectors.Nth(0).SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await accountSelectors.Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 2 });
        await session.Page.GetByLabel("Adjustment debit").Nth(0).FillAsync("125.00");
        await session.Page.GetByLabel("Adjustment credit").Nth(1).FillAsync("125.00");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Prepare draft" }).ClickAsync();
        await Assertions.Expect(session.Page.GetByRole(AriaRole.Table, new() { Name = "Retained consolidation adjustments" })).ToContainTextAsync("ELIM-E2E-IC-INV-1001");
        await Assertions.Expect(matches).ToContainTextAsync("Controlled");
        await session.AssertNoUiFailuresAsync("reviewed intercompany matching and controlled elimination preparation");
        }
        finally
        {
            await fixture.RemoveIntercompanyMatchingWorkflowAsync();
        }
    }
}
