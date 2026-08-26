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

    public async Task PrepareAndApproveSalesOrderAsync(string orderNumber)
    {
        await _session.Page.GetByLabel("Sales order customer").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Sales order number").FillAsync(orderNumber);
        await _session.Page.GetByLabel("Sales order line 1 item").SelectOptionAsync(new SelectOptionValue { Label = "RM-220 — Steel Fastener Pack" });
        await _session.Page.GetByLabel("Sales order line 1 description").FillAsync("Browser-tested customer shipment");
        await _session.Page.GetByLabel("Sales order line 1 quantity").FillAsync("2");
        await _session.Page.GetByLabel("Sales order line 1 unit price").FillAsync("20");
        await _session.Page.GetByLabel("Sales order line 1 tax").FillAsync("2");
        await _session.Page.GetByLabel("Sales order line 1 revenue account").SelectOptionAsync(new SelectOptionValue { Label = "4000 — Product Revenue" });
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save sales-order draft" }).ClickAsync();
        await _session.Page.GetByText("Sales-order draft saved.", new() { Exact = true }).WaitForAsync();
        var row = _session.Page.Locator("tr").Filter(new() { HasTextString = orderNumber });
        await row.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await _session.Page.GetByText($"Sales order {orderNumber} approved.", new() { Exact = true }).WaitForAsync();
    }

    public async Task AllocateAndShipSalesOrderAsync(string orderNumber, string shipmentNumber)
    {
        var row = _session.Page.Locator("tr").Filter(new() { HasTextString = orderNumber });
        await row.GetByRole(AriaRole.Button, new() { Name = "Allocate" }).ClickAsync();
        await _session.Page.GetByLabel("Allocate RM-220 quantity").FillAsync("2");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save allocation" }).ClickAsync();
        await _session.Page.GetByText("Inventory allocation saved.", new() { Exact = true }).WaitForAsync();
        row = _session.Page.Locator("tr").Filter(new() { HasTextString = orderNumber });
        await row.GetByRole(AriaRole.Button, new() { Name = "Ship" }).ClickAsync();
        await _session.Page.GetByLabel("Inventory shipment number").FillAsync(shipmentNumber);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post shipment" }).ClickAsync();
        await _session.Page.GetByText("Customer shipment posted; inventory and COGS were updated.", new() { Exact = true }).WaitForAsync();
    }

    public async Task InvoiceShipmentAsync(string shipmentNumber, string invoiceNumber)
    {
        var row = _session.Page.Locator("tr").Filter(new() { HasTextString = shipmentNumber });
        await row.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await _session.Page.GetByLabel("Shipment invoice number").FillAsync(invoiceNumber);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post shipment invoice" }).ClickAsync();
        await _session.Page.GetByText("Shipment invoice posted to receivables.", new() { Exact = true }).WaitForAsync();
        row = _session.Page.Locator("tr").Filter(new() { HasTextString = shipmentNumber });
        Assert.Contains("Invoiced", await row.InnerTextAsync());
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
