using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests.Pages;

public sealed class OperationsPage
{
    private readonly UiSession _session;

    public OperationsPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/operations");
        await _session.WaitForHeadingAsync("Operational flow from stock to shipment.");
    }

    public async Task AssertOperationsDataAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("FG-200", content);
        Assert.Contains("SO-3107", content);
        Assert.Contains("PO-4101", content);
        Assert.Contains("Compression Fitting Kit", content);
    }

    public async Task PreparePurchaseOrderAsync(string orderNumber)
    {
        await _session.Page.GetByLabel("Purchase order vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Purchase order number").FillAsync(orderNumber);
        await _session.Page.GetByLabel("Purchase order line 1 item").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Purchase order line 1 description").FillAsync("Browser-tested inventory purchase");
        await _session.Page.GetByLabel("Purchase order line 1 quantity").FillAsync("2");
        await _session.Page.GetByLabel("Purchase order line 1 unit cost").FillAsync("17.50");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save purchase-order draft" }).ClickAsync();
        await _session.Page.GetByText("Purchase-order draft saved.", new() { Exact = true }).WaitForAsync();
    }

    public async Task ApproveReceiveAndMatchAsync(string orderNumber, string receiptNumber, string billNumber)
    {
        var orderRow = _session.Page.Locator("tr").Filter(new() { HasTextString = orderNumber });
        await orderRow.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await _session.Page.GetByText($"Purchase order {orderNumber} approved.", new() { Exact = true }).WaitForAsync();
        orderRow = _session.Page.Locator("tr").Filter(new() { HasTextString = orderNumber });
        await orderRow.GetByRole(AriaRole.Button, new() { Name = "Receive" }).ClickAsync();
        await _session.Page.GetByLabel("Inventory receipt number").FillAsync(receiptNumber);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post inventory receipt" }).ClickAsync();
        await _session.Page.GetByText("Inventory receipt posted.", new() { Exact = true }).WaitForAsync();
        var receiptRow = _session.Page.Locator("tr").Filter(new() { HasTextString = receiptNumber });
        await receiptRow.GetByRole(AriaRole.Button, new() { Name = "Create matched bill" }).ClickAsync();
        await _session.Page.GetByLabel("Matched vendor bill number").FillAsync(billNumber);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post matched vendor bill" }).ClickAsync();
        await _session.Page.GetByText("Vendor bill matched and posted.", new() { Exact = true }).WaitForAsync();
        receiptRow = _session.Page.Locator("tr").Filter(new() { HasTextString = receiptNumber });
        Assert.Contains("Matched", await receiptRow.InnerTextAsync());
    }
}
