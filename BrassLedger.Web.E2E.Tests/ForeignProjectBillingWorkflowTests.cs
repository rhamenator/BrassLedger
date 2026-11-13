using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class ForeignProjectBillingWorkflowTests(PlaywrightWebAppFixture fixture)
{
    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ForeignFixedPriceBilling_RendersRatePickerCorrectsAndReleasesRetainage(BrowserKind browserKind)
    {
        await fixture.CreateForeignRefundRatesAsync();
        await fixture.CreateSubledgerWorkflowUsersAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectNumber = $"FX-PROJ-{suffix}";
        var invoiceNumber = $"FX-PB-{suffix}";
        await using var session = await fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync("controller", returnPath: "/projects");
        await session.GotoAsync("/projects");
        await session.WaitForHeadingAsync("Control project scope, cost, billing, and revenue.");

        await session.Page.GetByLabel("Project number", new() { Exact = true }).FillAsync(projectNumber);
        await session.Page.GetByLabel("Project name", new() { Exact = true }).FillAsync("Foreign fixed-price billing");
        await session.Page.GetByLabel("Project customer", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await session.Page.GetByLabel("Project billing method", new() { Exact = true }).SelectOptionAsync("FixedPrice");
        await session.Page.GetByLabel("Project contract amount", new() { Exact = true }).FillAsync("1000");
        await session.Page.GetByLabel("Project retainage percent", new() { Exact = true }).FillAsync("10");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Create project", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = $"Project {projectNumber} saved." }).WaitForAsync();

        await session.Page.GetByLabel("Project billing project", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = $"{projectNumber} — Foreign fixed-price billing (Fixed price)" });
        await session.Page.GetByLabel("Project billing invoice number", new() { Exact = true }).FillAsync(invoiceNumber);
        await session.Page.GetByLabel("Project billing milestone amount", new() { Exact = true }).FillAsync("100");
        await session.Page.GetByLabel("Project billing currency", new() { Exact = true }).FillAsync("CAD");
        await session.Page.GetByLabel("Project billing currency", new() { Exact = true }).PressAsync("Tab");
        var ratePicker = session.Page.GetByLabel("Project billing exchange rate", new() { Exact = true });
        await ratePicker.WaitForAsync();
        await ratePicker.SelectOptionAsync(new SelectOptionValue { Index = 2 });
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Preview billing", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Review the derived billing sources and totals" }).WaitForAsync();
        await Assertions.Expect(session.Page.Locator("table[aria-label='Project billing preview']")).ToContainTextAsync("FixedPriceMilestone");
        await Assertions.Expect(session.Page.Locator(".panel-subsection").Filter(new() { HasTextString = "Billing preview" })).ToContainTextAsync("Foreign invoice (CAD)");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Create controlled invoice draft", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = $"Project billing {invoiceNumber} saved as a controlled receivables draft." }).WaitForAsync();
        var proposal = session.Page.Locator("table[aria-label='Project billing proposals'] tbody tr").Filter(new() { HasTextString = invoiceNumber });
        await Assertions.Expect(proposal).ToContainTextAsync("Draft");
        using (var invoiceJson = System.Text.Json.JsonDocument.Parse(await fixture.GetInvoiceWorkflowPayloadAsync(invoiceNumber)))
        {
            Assert.Equal("CAD", invoiceJson.RootElement.GetProperty("Currency").GetString());
        }
        await using (var reviewer = await fixture.CreateSessionAsync(browserKind))
        {
            await reviewer.SignInAsync("e2e-ar-approver", returnPath: "/receivables");
            await reviewer.GotoAsync("/receivables");
            await reviewer.Page.GetByLabel("Invoice rejection reason", new() { Exact = true }).FillAsync("Clarify the project milestone");
            await reviewer.Page.GetByLabel("Invoice rejection reason", new() { Exact = true }).PressAsync("Tab");
            var reviewRow = reviewer.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = invoiceNumber });
            await reviewRow.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
            await reviewer.Page.GetByRole(AriaRole.Status).WaitForAsync();
            Assert.Contains("Invoice draft rejected", await reviewer.Page.GetByRole(AriaRole.Status).InnerTextAsync());
            await reviewer.AssertNoUiFailuresAsync("foreign project invoice rejection");
        }
        await session.GotoAsync("/projects");
        proposal = session.Page.Locator("table[aria-label='Project billing proposals'] tbody tr").Filter(new() { HasTextString = invoiceNumber });
        await proposal.GetByRole(AriaRole.Button, new() { Name = "Correct", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Preview the corrected billing before saving it." }).WaitForAsync();
        Assert.Equal("CAD", await session.Page.GetByLabel("Project billing currency", new() { Exact = true }).InputValueAsync());
        Assert.NotEqual(string.Empty, await session.Page.GetByLabel("Project billing exchange rate", new() { Exact = true }).InputValueAsync());
        await session.Page.GetByLabel("Project billing description", new() { Exact = true }).FillAsync("Clarified foreign milestone");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Preview billing", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Review the derived billing sources and totals" }).WaitForAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Create controlled invoice draft", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = $"Project billing {invoiceNumber} saved as a controlled receivables draft." }).WaitForAsync();
        await session.AssertNoUiFailuresAsync("foreign project rate picker rendering");

        await using (var reviewer = await fixture.CreateSessionAsync(browserKind))
        {
            await reviewer.SignInAsync("e2e-ar-approver", returnPath: "/receivables");
            await reviewer.GotoAsync("/receivables");
            var reviewRow = reviewer.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = invoiceNumber });
            await reviewRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
            await reviewer.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Invoice draft approved." }).WaitForAsync();
        }
        await using (var poster = await fixture.CreateSessionAsync(browserKind))
        {
            await poster.SignInAsync("e2e-ar-poster", returnPath: "/receivables");
            await poster.GotoAsync("/receivables");
            var postingRow = poster.Page.Locator("table.data-table tbody tr").Filter(new() { HasTextString = invoiceNumber });
            await postingRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
            await poster.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Approved invoice posted." }).WaitForAsync();
        }
        await session.GotoAsync("/projects");
        proposal = session.Page.Locator("table[aria-label='Project billing proposals'] tbody tr").Filter(new() { HasTextString = invoiceNumber });
        await proposal.GetByRole(AriaRole.Button, new() { Name = "Release retainage", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Enter a release invoice number" }).WaitForAsync();
        Assert.Equal("CAD", await session.Page.GetByLabel("Project billing currency", new() { Exact = true }).InputValueAsync());
        await session.Page.GetByLabel("Project billing invoice number", new() { Exact = true }).FillAsync($"{invoiceNumber}-RET");
        await session.Page.GetByLabel("Project billing exchange rate", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Preview billing", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Review the derived billing sources and totals" }).WaitForAsync();
        await Assertions.Expect(session.Page.Locator(".panel-subsection").Filter(new() { HasTextString = "Billing preview" })).ToContainTextAsync("realized foreign exchange");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Create controlled invoice draft", Exact = true }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = $"Project billing {invoiceNumber}-RET saved as a controlled receivables draft." }).WaitForAsync();
        await session.AssertNoUiFailuresAsync("foreign retainage release preparation");
    }
}
