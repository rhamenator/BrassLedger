using Microsoft.Playwright;
using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class ForeignPurchaseOrderGuardTests(PlaywrightWebAppFixture fixture)
{
    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ForeignPurchaseOrder_DisplaysCurrencyAndCannotReceiveBeforeValuationSupport(BrowserKind browserKind)
    {
        var orderNumber = $"PO-FX-GUARD-{Guid.NewGuid().ToString("N")[..8]}";
        await using var session = await fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync("operations");
        var operations = new OperationsPage(session); await operations.OpenAsync();
        await session.Page.GetByLabel("Purchase order vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await session.Page.GetByLabel("Purchase order number").FillAsync(orderNumber);
        await session.Page.GetByLabel("Purchase order currency").FillAsync("CAD");
        await session.Page.GetByLabel("Purchase order line 1 item").SelectOptionAsync(new SelectOptionValue { Label = "RM-220 — Steel Fastener Pack" });
        await session.Page.GetByLabel("Purchase order line 1 description").FillAsync("Foreign fasteners");
        await session.Page.GetByLabel("Purchase order line 1 quantity").FillAsync("2");
        await session.Page.GetByLabel("Purchase order line 1 unit cost").FillAsync("50");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save purchase-order draft" }).ClickAsync();
        await session.Page.GetByText("Purchase-order draft saved.", new() { Exact = true }).WaitForAsync();
        var row = session.Page.Locator("tr").Filter(new() { HasTextString = orderNumber });
        await Assertions.Expect(row).ToContainTextAsync("CAD");
        await row.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await session.Page.GetByText($"Purchase order {orderNumber} approved.", new() { Exact = true }).WaitForAsync();
        row = session.Page.Locator("tr").Filter(new() { HasTextString = orderNumber });
        await Assertions.Expect(row).ToContainTextAsync("Foreign receiving pending");
        Assert.Equal(0, await row.GetByRole(AriaRole.Button, new() { Name = "Receive" }).CountAsync());
        await session.AssertNoUiFailuresAsync("foreign purchase-order valuation guard");
    }
}
