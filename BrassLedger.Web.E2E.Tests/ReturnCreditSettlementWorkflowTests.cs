using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class ReturnCreditSettlementWorkflowTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public ReturnCreditSettlementWorkflowTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task CustomerReturnCredit_LeftoverAfterPaidInvoiceCanBeAppliedToAnotherInvoice(BrowserKind browserKind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var orderNumberA = $"SO-SETA-{suffix}"; var shipmentNumberA = $"SHIP-SETA-{suffix}"; var invoiceNumberA = $"INV-SETA-{suffix}"; var paymentReferenceA = $"PMT-SETA-{suffix}";
        var orderNumberB = $"SO-SETB-{suffix}"; var shipmentNumberB = $"SHIP-SETB-{suffix}"; var invoiceNumberB = $"INV-SETB-{suffix}";
        var returnNumber = $"RMA-SET-{suffix}"; var receiptNumber = $"CRCV-SET-{suffix}"; var creditNumber = $"CM-SET-{suffix}";

        await using (var salesSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await salesSession.SignInAsync("sales");
            var sales = new OperationsPage(salesSession);
            await sales.OpenAsync();
            await sales.PrepareAndApproveSalesOrderAsync(orderNumberA);
            await sales.PrepareAndApproveSalesOrderAsync(orderNumberB);
            await salesSession.AssertNoUiFailuresAsync("preparing two sales orders for return-credit settlement");
        }
        await using (var warehouseSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await warehouseSession.SignInAsync("warehouse");
            var warehouse = new OperationsPage(warehouseSession);
            await warehouse.OpenAsync();
            await warehouse.AllocateAndShipSalesOrderAsync(orderNumberA, shipmentNumberA);
            await warehouse.AllocateAndShipSalesOrderAsync(orderNumberB, shipmentNumberB);
            await warehouseSession.AssertNoUiFailuresAsync("shipping both sales orders for return-credit settlement");
        }
        await using (var receivablesSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await receivablesSession.SignInAsync("controller");
            var receivables = new OperationsPage(receivablesSession);
            await receivables.OpenAsync();
            await receivables.InvoiceShipmentAsync(shipmentNumberA, invoiceNumberA);
            await receivables.OpenAsync();
            await receivables.InvoiceShipmentAsync(shipmentNumberB, invoiceNumberB);
            await receivablesSession.AssertNoUiFailuresAsync("invoicing both shipments for return-credit settlement");
        }
        // Pay invoice A in full before the return posts. CreditCustomerReturnAsync's service layer
        // auto-nets the return credit against its own source invoice's own remaining balance
        // (SourceAppliedAmount) at creation time -- with the invoice already at a zero balance, none
        // of the credit is consumed there, leaving the entire credit available for settlement. This is
        // the real finding recorded in docs/work-remaining.md that blocked simply grafting a settlement
        // step onto the existing role-separated sales-order fixture.
        await using (var paymentSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await paymentSession.SignInAsync("controller");
            var receivablesForPayment = new ReceivablesPage(paymentSession);
            await receivablesForPayment.OpenAsync();
            await receivablesForPayment.RecordCustomerPaymentAsync(invoiceNumberA, paymentReferenceA, "100");
            await paymentSession.AssertNoUiFailuresAsync("paying off invoice A before the customer return");
        }
        await using (var salesReturnSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await salesReturnSession.SignInAsync("sales");
            var sales = new OperationsPage(salesReturnSession);
            await sales.OpenAsync();
            await sales.AuthorizeCustomerReturnAsync(shipmentNumberA, returnNumber);
            await salesReturnSession.AssertNoUiFailuresAsync("customer return authorization for return-credit settlement");
        }
        await using (var warehouseReturnSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await warehouseReturnSession.SignInAsync("warehouse");
            var warehouse = new OperationsPage(warehouseReturnSession);
            await warehouse.OpenAsync();
            await warehouse.ReceiveCustomerReturnAsync(returnNumber, receiptNumber);
            await warehouseReturnSession.AssertNoUiFailuresAsync("physical customer return receipt for return-credit settlement");
        }
        await using var settlementSession = await _fixture.CreateSessionAsync(browserKind);
        await settlementSession.SignInAsync("controller");
        var settlement = new OperationsPage(settlementSession);
        await settlement.OpenAsync();
        await settlement.CreditCustomerReturnAsync(receiptNumber, creditNumber);
        await settlement.ApplyCustomerReturnCreditAsync(creditNumber, invoiceNumberB);
        await settlementSession.AssertNoUiFailuresAsync("applying the leftover customer return credit to a second invoice");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task SupplierReturnCredit_LeftoverAfterPaidBillCanBeRefunded(BrowserKind browserKind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var requisitionNumber = $"REQ-SET-{suffix}"; var orderNumber = $"PO-SET-{suffix}"; var receiptNumber = $"RCV-SET-{suffix}";
        var billNumber = $"BILL-SET-{suffix}"; var paymentReference = $"PMT-SET-{suffix}";
        var returnNumber = $"SRA-SET-{suffix}"; var returnShipmentNumber = $"SRS-SET-{suffix}";

        await using (var preparerSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await preparerSession.SignInAsync("requisition");
            var preparation = new OperationsPage(preparerSession);
            await preparation.OpenAsync();
            await preparation.PrepareAndSubmitPurchaseRequisitionAsync(requisitionNumber);
            await preparerSession.AssertNoUiFailuresAsync("purchase-requisition preparation for return-credit settlement");
        }
        await using var purchasingSession = await _fixture.CreateSessionAsync(browserKind);
        await purchasingSession.SignInAsync("operations");
        var purchasing = new OperationsPage(purchasingSession);
        await purchasing.OpenAsync();
        await purchasing.ApproveAndConvertPurchaseRequisitionAsync(requisitionNumber, orderNumber);
        await purchasing.ApproveAndReceiveAsync(orderNumber, receiptNumber);
        await using (var payablesSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await payablesSession.SignInAsync("controller");
            var payablesPrep = new OperationsPage(payablesSession);
            await payablesPrep.OpenAsync();
            await payablesPrep.PreparePurchaseInvoiceAsync(receiptNumber, billNumber);
            await payablesSession.AssertNoUiFailuresAsync("supplier-invoice preparation for return-credit settlement");
        }
        await purchasing.OpenAsync();
        await purchasing.ApprovePurchaseInvoiceAsync(billNumber);
        await using (var postingSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await postingSession.SignInAsync("controller");
            var payablesPosting = new OperationsPage(postingSession);
            await payablesPosting.OpenAsync();
            await payablesPosting.PostPurchaseInvoiceAsync(billNumber);
            await postingSession.AssertNoUiFailuresAsync("supplier-invoice posting for return-credit settlement");
        }
        // Pay the bill in full before the return posts, for the same reason as the customer-side
        // sibling test above: ShipSupplierReturnAsync's service layer auto-nets the vendor credit
        // against its own source bill's own remaining balance at creation time.
        await using (var paymentSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await paymentSession.SignInAsync("controller");
            var payablesForPayment = new PayablesPage(paymentSession);
            await payablesForPayment.OpenAsync();
            await payablesForPayment.RecordVendorPaymentAsync(billNumber, paymentReference, "1000");
            await paymentSession.AssertNoUiFailuresAsync("paying off the vendor bill before the supplier return");
        }
        await purchasing.OpenAsync();
        await purchasing.AuthorizeAndShipSupplierReturnAsync(receiptNumber, returnNumber, returnShipmentNumber);
        await using var settlementSession = await _fixture.CreateSessionAsync(browserKind);
        await settlementSession.SignInAsync("controller");
        var settlement = new OperationsPage(settlementSession);
        await settlement.OpenAsync();
        await settlement.RefundSupplierReturnCreditAsync(returnShipmentNumber);
        await settlementSession.AssertNoUiFailuresAsync("refunding the leftover supplier return credit");
    }
}
