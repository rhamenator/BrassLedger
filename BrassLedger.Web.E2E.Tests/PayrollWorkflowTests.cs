using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class PayrollWorkflowTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public PayrollWorkflowTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Payroll_PostingProducesReconciledRegisterAndPayStatement(BrowserKind browserKind)
    {
        await _fixture.CreateSubledgerWorkflowUsersAsync();
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync("payroll");
        await session.GotoAsync("/payroll");
        await session.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");
        await Assertions.Expect(session.Page.GetByLabel("Time entry Social Security tips").First).ToBeVisibleAsync();
        await Assertions.Expect(session.Page.GetByLabel("Time entry cash tips reported").First).ToBeVisibleAsync();
        await Assertions.Expect(session.Page.GetByLabel("Time entry qualified overtime premium").First).ToBeVisibleAsync();
        await Assertions.Expect(session.Page.GetByText("TT is only the premium above regular pay", new() { Exact = false })).ToBeVisibleAsync();

        var ssaOriginalWorkflow = session.Page.GetByText("SSA original EFW2 specification and AccuWage workflow", new() { Exact = true });
        await ssaOriginalWorkflow.ClickAsync();
        await session.Page.GetByText("SSA published tax-year 2026 EFW2 on July 7, 2026.", new() { Exact = false }).WaitForAsync();
        await Assertions.Expect(session.Page.GetByLabel("SSA original specification tax year")).ToHaveValueAsync("2026");
        await Assertions.Expect(session.Page.GetByLabel("SSA original layout compatibility code")).ToHaveValueAsync("EFW2-2026-512-RA-RE-RW-RO-RT-RU-RF");
        await Assertions.Expect(session.Page.GetByLabel("SSA kind of employer")).ToHaveValueAsync("N");
        await Assertions.Expect(session.Page.GetByLabel("SSA employment code")).ToHaveValueAsync("R");
        await Assertions.Expect(session.Page.GetByRole(AriaRole.Button, new() { Name = "Generate immutable original file for AccuWage", Exact = true })).ToBeDisabledAsync();
        await ssaOriginalWorkflow.ClickAsync();

        var ssaWorkflow = session.Page.GetByText("SSA EFW2C specification and AccuWage workflow", new() { Exact = true });
        await ssaWorkflow.ClickAsync();
        await session.Page.GetByText("SSA published tax-year 2026 EFW2C on July 10, 2026.", new() { Exact = false }).WaitForAsync();
        await Assertions.Expect(session.Page.GetByLabel("SSA specification tax year")).ToHaveValueAsync("2026");
        await Assertions.Expect(session.Page.GetByLabel("SSA layout compatibility code")).ToHaveValueAsync("EFW2C-2026-1024-RCA-RCE-RCW-RCO-RCT-RCU-RCF");
        await Assertions.Expect(session.Page.GetByRole(AriaRole.Button, new() { Name = "Generate immutable file for AccuWage", Exact = true })).ToBeDisabledAsync();
        await ssaWorkflow.ClickAsync();

        await session.Page.GetByText("Configure a Form 941 deposit schedule", new() { Exact = true }).ClickAsync();
        var depositScheduleSection = session.Page.GetByRole(AriaRole.Heading, new() { Name = "Federal payroll deposit schedule", Exact = true }).Locator("..");
        await depositScheduleSection.GetByRole(AriaRole.Button, new() { Name = "Load official 2026 defaults", Exact = true }).ClickAsync();
        await depositScheduleSection.GetByLabel("Approved against the official sources").CheckAsync();
        await depositScheduleSection.GetByLabel("Federal deposit schedule review notes").FillAsync("E2E verified lookback and official 2026 sources.");
        await depositScheduleSection.GetByRole(AriaRole.Button, new() { Name = "Save deposit schedule", Exact = true }).ClickAsync();
        await session.Page.GetByText("Federal Form 941 deposit schedule saved; open liabilities were recalculated from their pay dates.", new() { Exact = true }).WaitForAsync();

        var reference = $"PR-E2E-OUTPUT-{browserKind}";
        await session.Page.GetByLabel("Run reference").FillAsync(reference);
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Preview payroll", Exact = true }).ClickAsync();
        await session.Page.GetByText("Review the calculated employee payroll before posting.", new() { Exact = true }).WaitForAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save reviewed draft", Exact = true }).ClickAsync();
        Assert.Equal("Payroll draft saved. It has not changed the ledger or funding account.", await WaitForStatusChangeAsync(session.Page, "Review the calculated employee payroll before posting."));

        var runRow = session.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
        await runRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await session.Page.GetByText("The person who prepared a payroll run cannot approve it.", new() { Exact = true }).WaitForAsync();
        await using (var reviewerSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await reviewerSession.SignInAsync("e2e-payroll-reviewer"); await reviewerSession.GotoAsync("/payroll"); await reviewerSession.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");
            var reviewerRow = reviewerSession.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
            await reviewerSession.Page.GetByLabel("Payroll rejection reason").FillAsync("Confirm the employee earnings before posting.");
            await reviewerRow.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
            await reviewerSession.Page.GetByText("Payroll rejected and returned to its preparer for correction.", new() { Exact = true }).WaitForAsync();
        }
        await session.GotoAsync("/payroll"); await session.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");
        runRow = session.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
        await Assertions.Expect(runRow).ToContainTextAsync("Confirm the employee earnings before posting.");
        await runRow.GetByRole(AriaRole.Button, new() { Name = "Correct", Exact = true }).ClickAsync();
        await session.Page.GetByText("Loaded the rejected payroll and its source timecards for correction.", new() { Exact = true }).WaitForAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Preview payroll", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save corrected draft", Exact = true }).ClickAsync();
        await session.Page.GetByText("Payroll draft saved. It has not changed the ledger or funding account.", new() { Exact = true }).WaitForAsync();
        await using (var reviewerSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await reviewerSession.SignInAsync("e2e-payroll-reviewer"); await reviewerSession.GotoAsync("/payroll"); await reviewerSession.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");
            var reviewerRow = reviewerSession.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
            await reviewerRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
            await reviewerSession.Page.GetByText("Payroll run approved and ready for posting.", new() { Exact = true }).WaitForAsync();
        }
        await using (var posterSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await posterSession.SignInAsync("e2e-payroll-poster"); await posterSession.GotoAsync("/payroll"); await posterSession.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");
            var posterRow = posterSession.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
            await posterRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
            Assert.Equal("Approved payroll posted to the ledger.", await WaitForStatusChangeAsync(posterSession.Page, string.Empty));
        }
        await session.GotoAsync("/payroll"); await session.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");
        var liabilitySection = session.Page.GetByRole(AriaRole.Heading, new() { Name = "Payroll liabilities", Exact = true }).Locator("..");
        var federalLiabilityRows = liabilitySection.Locator("table.data-table tbody tr").Filter(new() { HasTextString = "Federal" });
        Assert.True(await federalLiabilityRows.CountAsync() > 0);
        foreach (var federalLiabilityRow in await federalLiabilityRows.AllAsync()) Assert.DoesNotContain("Schedule required", await federalLiabilityRow.InnerTextAsync());

        var paymentFileSection = session.Page.GetByRole(AriaRole.Heading, new() { Name = "Employee payment files", Exact = true }).Locator("..");
        var paymentRunSelect = paymentFileSection.GetByLabel("Payment file payroll run");
        await Assertions.Expect(paymentRunSelect.Locator("option:checked")).ToContainTextAsync(reference);
        await paymentFileSection.GetByLabel("Payroll payment file format").SelectOptionAsync("CheckRegisterCsv");
        await paymentFileSection.GetByRole(AriaRole.Button, new() { Name = "Generate immutable payment file", Exact = true }).ClickAsync();
        await session.Page.GetByText("Immutable payroll payment file generated and reconciled. Download it below; NACHA output still requires bank acceptance.", new() { Exact = true }).WaitForAsync();
        var paymentFileRow = paymentFileSection.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
        await Assertions.Expect(paymentFileRow).ToContainTextAsync("CheckRegisterCsv");
        Assert.Contains("/payroll/payment-files/", await paymentFileRow.GetByRole(AriaRole.Link, new() { Name = "Download", Exact = true }).GetAttributeAsync("href"));

        runRow = session.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
        await runRow.GetByRole(AriaRole.Button, new() { Name = "View register", Exact = true }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = $"Register — {reference}", Exact = true }).WaitForAsync();
        var register = session.Page.GetByLabel("Payroll register preview");
        await register.GetByText("Totals", new() { Exact = true }).WaitForAsync();
        var download = register.GetByRole(AriaRole.Link, new() { Name = "Download CSV", Exact = true });
        Assert.Contains("/payroll/reports/", await download.GetAttributeAsync("href"));

        await register.GetByRole(AriaRole.Button, new() { Name = "Pay statement", Exact = true }).First.ClickAsync();
        var payStatement = session.Page.GetByLabel("Pay statement preview");
        await payStatement.WaitForAsync();
        Assert.Contains("YTD:", await payStatement.InnerTextAsync());
        Assert.Contains("Earnings", await payStatement.InnerTextAsync());
        Assert.Contains("Taxes and deductions", await payStatement.InnerTextAsync());
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Generate filing draft", Exact = true }).ClickAsync();
        await session.Page.GetByText("Payroll filing draft generated from posted payroll; professional review is still required.", new() { Exact = true }).WaitForAsync();
        var filingSection = session.Page.GetByRole(AriaRole.Heading, new() { Name = "Federal filing data and payroll close", Exact = true }).Locator("..");
        await filingSection.GetByText("Draft", new() { Exact = true }).WaitForAsync();
        Assert.Contains("/payroll/filings/", await filingSection.GetByRole(AriaRole.Link, new() { Name = "Download JSON", Exact = true }).GetAttributeAsync("href"));
        await session.AssertNoUiFailuresAsync("payroll register and pay statement workflow");
    }

    private static async Task<string> WaitForStatusChangeAsync(IPage page, string previous)
    {
        var status = page.Locator("[role=status]");
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var text = (await status.InnerTextAsync()).Trim();
            if (!string.Equals(text, previous, StringComparison.Ordinal)) return text;
            await Task.Delay(100);
        }
        return (await status.InnerTextAsync()).Trim();
    }
}
