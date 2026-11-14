using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

public sealed class MultiCompanyContextGuardTests
{
    [Fact]
    public async Task OriginalCompanyUsersCanReadAndPrepareAfterSecondCompanyAppears()
    {
        var fixture = new PlaywrightWebAppFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var session = await fixture.CreateSessionAsync(BrowserKind.Chromium);
            await session.SignInAsync(returnPath: "/projects");
            await fixture.AddUnrelatedCompanyForIsolationAsync();

            await session.GotoAsync("/projects");
            await session.WaitForHeadingAsync("Control project scope, cost, billing, and revenue.");
            await session.AssertNoUiFailuresAsync("multi-company project workspace read");

            await using var operationsSession = await fixture.CreateSessionAsync(BrowserKind.Chromium);
            await operationsSession.SignInAsync("operations", returnPath: "/operations");
            await operationsSession.GotoAsync("/operations");
            await operationsSession.WaitForHeadingAsync("Operational flow from stock to shipment.");
            var orderNumber = $"PO-CONTEXT-{Guid.NewGuid().ToString("N")[..8]}";
            await operationsSession.Page.GetByLabel("Purchase order vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
            await operationsSession.Page.GetByLabel("Purchase order number").FillAsync(orderNumber);
            await operationsSession.Page.GetByLabel("Purchase order line 1 item").SelectOptionAsync(new SelectOptionValue { Label = "RM-220 — Steel Fastener Pack" });
            await operationsSession.Page.GetByLabel("Purchase order line 1 description").FillAsync("Context-isolated fasteners");
            await operationsSession.Page.GetByLabel("Purchase order line 1 quantity").FillAsync("1");
            await operationsSession.Page.GetByLabel("Purchase order line 1 unit cost").FillAsync("5");
            await operationsSession.Page.GetByRole(AriaRole.Button, new() { Name = "Save purchase-order draft" }).ClickAsync();
            await operationsSession.Page.GetByText("Purchase-order draft saved.", new() { Exact = true }).WaitForAsync();
            await Assertions.Expect(operationsSession.Page.Locator("tr").Filter(new() { HasTextString = orderNumber })).ToBeVisibleAsync();
            await operationsSession.AssertNoUiFailuresAsync("multi-company purchase-order draft");

            await using var payrollSession = await fixture.CreateSessionAsync(BrowserKind.Chromium);
            await payrollSession.SignInAsync("payroll", returnPath: "/payroll");
            await payrollSession.GotoAsync("/payroll");
            await payrollSession.WaitForHeadingAsync("Prepare, approve, post, and audit payroll.");
            await Assertions.Expect(payrollSession.Page.GetByRole(AriaRole.Heading, new() { Name = "Employee payment files" })).ToBeVisibleAsync();
            await payrollSession.Page.GetByText("Configure a Form 941 deposit schedule", new() { Exact = true }).ClickAsync();
            var depositSchedule = payrollSession.Page.GetByRole(AriaRole.Heading, new() { Name = "Federal payroll deposit schedule", Exact = true }).Locator("..");
            await depositSchedule.GetByRole(AriaRole.Button, new() { Name = "Load official 2026 defaults", Exact = true }).ClickAsync();
            await depositSchedule.GetByLabel("Approved against the official sources").CheckAsync();
            await depositSchedule.GetByLabel("Federal deposit schedule review notes").FillAsync("Multi-company browser isolation review.");
            await depositSchedule.GetByRole(AriaRole.Button, new() { Name = "Save deposit schedule", Exact = true }).ClickAsync();
            await payrollSession.Page.GetByText("Federal Form 941 deposit schedule saved; open liabilities were recalculated from their pay dates.", new() { Exact = true }).WaitForAsync();
            await payrollSession.AssertNoUiFailuresAsync("multi-company payroll workspace read");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
