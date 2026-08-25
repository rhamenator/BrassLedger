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
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync("payroll");
        await session.GotoAsync("/payroll");
        await session.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");

        var reference = $"PR-E2E-OUTPUT-{browserKind}";
        await session.Page.GetByLabel("Run reference").FillAsync(reference);
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Preview payroll", Exact = true }).ClickAsync();
        await session.Page.GetByText("Review the calculated employee payroll before posting.", new() { Exact = true }).WaitForAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save reviewed draft", Exact = true }).ClickAsync();
        Assert.Equal("Payroll draft saved. It has not changed the ledger or funding account.", await WaitForStatusChangeAsync(session.Page, "Review the calculated employee payroll before posting."));

        var runRow = session.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
        await runRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await session.Page.GetByText("Payroll run approved and ready for posting.", new() { Exact = true }).WaitForAsync();
        runRow = session.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = reference });
        await runRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
        await session.Page.GetByText("Approved payroll posted to the ledger.", new() { Exact = true }).WaitForAsync();
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
