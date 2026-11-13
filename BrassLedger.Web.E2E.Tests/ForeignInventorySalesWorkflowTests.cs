using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class ForeignInventorySalesWorkflowTests(PlaywrightWebAppFixture fixture)
{
    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ForeignSalesOrder_ShipsInvoicesAndCreditsReturnAtSelectedRate(BrowserKind browserKind)
    {
        await fixture.CreateForeignRefundRatesAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var orderNumber = $"SO-FX-{suffix}"; var shipmentNumber = $"SHIP-FX-{suffix}"; var invoiceNumber = $"INV-FX-{suffix}";
        var returnNumber = $"RMA-FX-{suffix}"; var receiptNumber = $"CRCV-FX-{suffix}"; var creditNumber = $"CM-FX-{suffix}";
        await using (var salesSession = await fixture.CreateSessionAsync(browserKind))
        {
            await salesSession.SignInAsync("sales"); var sales = new OperationsPage(salesSession); await sales.OpenAsync();
            await sales.PrepareAndApproveSalesOrderAsync(orderNumber, "CAD");
            await salesSession.AssertNoUiFailuresAsync("foreign sales-order preparation");
        }
        await using (var warehouseSession = await fixture.CreateSessionAsync(browserKind))
        {
            await warehouseSession.SignInAsync("warehouse"); var warehouse = new OperationsPage(warehouseSession); await warehouse.OpenAsync();
            await warehouse.AllocateAndShipSalesOrderAsync(orderNumber, shipmentNumber);
            await warehouseSession.AssertNoUiFailuresAsync("foreign sales-order fulfillment");
        }
        await using (var receivablesSession = await fixture.CreateSessionAsync(browserKind))
        {
            await receivablesSession.SignInAsync("controller"); var receivables = new OperationsPage(receivablesSession); await receivables.OpenAsync();
            await receivables.InvoiceShipmentAsync(shipmentNumber, invoiceNumber, "E2E document rate");
            await receivablesSession.AssertNoUiFailuresAsync("foreign shipment invoicing");
        }
        await using (var salesReturnSession = await fixture.CreateSessionAsync(browserKind))
        {
            await salesReturnSession.SignInAsync("sales"); var sales = new OperationsPage(salesReturnSession); await sales.OpenAsync();
            await sales.AuthorizeCustomerReturnAsync(shipmentNumber, returnNumber);
        }
        await using (var warehouseReturnSession = await fixture.CreateSessionAsync(browserKind))
        {
            await warehouseReturnSession.SignInAsync("warehouse"); var warehouse = new OperationsPage(warehouseReturnSession); await warehouse.OpenAsync();
            await warehouse.ReceiveCustomerReturnAsync(returnNumber, receiptNumber);
        }
        await using (var creditSession = await fixture.CreateSessionAsync(browserKind))
        {
            await creditSession.SignInAsync("controller"); var receivables = new OperationsPage(creditSession); await receivables.OpenAsync();
            await receivables.CreditCustomerReturnAsync(receiptNumber, creditNumber);
            await creditSession.AssertNoUiFailuresAsync("foreign inventory return credit");
        }
    }
}
