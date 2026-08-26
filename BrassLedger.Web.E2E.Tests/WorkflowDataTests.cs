using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class WorkflowDataTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public WorkflowDataTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task OverviewQuickAction_ReachesLedgerAndShowsSeededAccounts(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var overview = new OverviewPage(session);
        var ledger = new LedgerPage(session);

        await overview.OpenAsync();
        await overview.AssertKeyMetricsAsync();
        await overview.OpenLedgerQuickActionAsync();
        await ledger.AssertSeededDataAsync();
        await session.AssertNoUiFailuresAsync("overview to ledger workflow");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ReceivablesAndPayablesPages_ShowExpectedFinancialQueues(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var receivables = new ReceivablesPage(session);
        var payables = new PayablesPage(session);

        await receivables.OpenAsync();
        await receivables.AssertCustomerAndInvoiceDataAsync();
        await session.AssertNoUiFailuresAsync("receivables workflow");

        await payables.OpenAsync();
        await payables.AssertVendorAndBillDataAsync();
        await session.AssertNoUiFailuresAsync("payables workflow");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task QuickBooksInvoiceImport_ValidatesCreatesDraftAndUsesNormalPostingWorkflow(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var invoiceNumber = $"QBO-E2E-{Guid.NewGuid():N}"[..20];
        var ledger = new LedgerPage(session);
        await ledger.OpenAsync();
        await ledger.ImportQuickBooksInvoiceDraftAsync(invoiceNumber);
        var receivables = new ReceivablesPage(session);
        await receivables.OpenAsync();
        await receivables.ApproveAndPostImportedInvoiceAsync(invoiceNumber);
        await session.AssertNoUiFailuresAsync("QuickBooks invoice import workflow");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task OperationsAndReportingPages_ShowExpectedOperationalArtifacts(BrowserKind browserKind)
    {
        await using var operationsSession = await _fixture.CreateSessionAsync(browserKind);
        await operationsSession.SignInAsync("operations");
        var operations = new OperationsPage(operationsSession);

        await operations.OpenAsync();
        await operations.AssertOperationsDataAsync();
        await operationsSession.AssertNoUiFailuresAsync("operations workflow");

        await using var reportingSession = await _fixture.CreateSessionAsync(browserKind);
        await reportingSession.SignInAsync();
        var reporting = new ReportingPage(reportingSession);

        await reporting.OpenAsync();
        await reporting.AssertReportingCatalogAsync();
        await reportingSession.AssertNoUiFailuresAsync("reporting workflow");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task InventoryLocations_CanBeConfiguredEditedTransferredAndReversed(BrowserKind browserKind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync("warehouse");
        var operations = new OperationsPage(session);
        await operations.OpenAsync();
        await operations.ConfigureEditTransferAndReverseInventoryAsync($"E{suffix}", $"XFER-E2E-{suffix}");
        await session.AssertNoUiFailuresAsync("inventory-location configuration, transfer, and reversal");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task PurchaseOrder_PreparationApprovalReceiptAndBillMatch_WorkAcrossSeparatedRoles(BrowserKind browserKind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var requisitionNumber = $"REQ-E2E-{suffix}";
        var orderNumber = $"PO-E2E-{suffix}";
        var receiptNumber = $"RCV-E2E-{suffix}";
        var billNumber = $"BILL-E2E-{suffix}";
        var returnNumber = $"SRA-E2E-{suffix}";
        var returnShipmentNumber = $"SRS-E2E-{suffix}";
        var landedCostNumber = $"LC-E2E-{suffix}";
        var landedCostBillNumber = $"LCB-E2E-{suffix}";
        await using (var preparerSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await preparerSession.SignInAsync("requisition");
            var preparation = new OperationsPage(preparerSession);
            await preparation.OpenAsync();
            await preparation.PrepareAndSubmitPurchaseRequisitionAsync(requisitionNumber);
            await preparerSession.AssertNoUiFailuresAsync("purchase-requisition preparation and submission");
        }
        await using var purchasingSession = await _fixture.CreateSessionAsync(browserKind);
        await purchasingSession.SignInAsync("operations");
        var purchasing = new OperationsPage(purchasingSession);
        await purchasing.OpenAsync();
        await purchasing.ApproveAndConvertPurchaseRequisitionAsync(requisitionNumber, orderNumber);
        await purchasing.ApproveAndReceiveAsync(orderNumber, receiptNumber);
        await using (var payablesPreparationSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await payablesPreparationSession.SignInAsync("controller"); var payablesPreparation = new OperationsPage(payablesPreparationSession); await payablesPreparation.OpenAsync(); await payablesPreparation.PreparePurchaseInvoiceAsync(receiptNumber, billNumber); await payablesPreparation.PrepareLandedCostAsync(receiptNumber, landedCostNumber, landedCostBillNumber); await payablesPreparationSession.AssertNoUiFailuresAsync("supplier-invoice and landed-cost preparation and submission");
        }
        await purchasing.OpenAsync(); await purchasing.ApprovePurchaseInvoiceAsync(billNumber); await purchasing.ApproveLandedCostAsync(landedCostNumber);
        await using (var payablesPostingSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await payablesPostingSession.SignInAsync("controller"); var payablesPosting = new OperationsPage(payablesPostingSession); await payablesPosting.OpenAsync(); await payablesPosting.PostPurchaseInvoiceAsync(billNumber); await payablesPosting.PostLandedCostAsync(landedCostNumber); await payablesPostingSession.AssertNoUiFailuresAsync("supplier-invoice and landed-cost posting");
        }
        await purchasing.OpenAsync();
        await purchasing.AuthorizeAndShipSupplierReturnAsync(receiptNumber, returnNumber, returnShipmentNumber);
        await purchasingSession.AssertNoUiFailuresAsync("purchase-order approval, receipt, invoice match, landed-cost review, and supplier return");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task SalesOrder_AllocationShipmentAndInvoice_WorkAcrossSeparatedRoles(BrowserKind browserKind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8]; var orderNumber = $"SO-E2E-{suffix}"; var shipmentNumber = $"SHIP-E2E-{suffix}"; var invoiceNumber = $"INV-E2E-{suffix}"; var returnNumber = $"RMA-E2E-{suffix}"; var receiptNumber = $"CRCV-E2E-{suffix}"; var creditNumber = $"CM-E2E-{suffix}";
        await using (var salesSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await salesSession.SignInAsync("sales"); var sales = new OperationsPage(salesSession); await sales.OpenAsync(); await sales.PrepareAndApproveSalesOrderAsync(orderNumber); await sales.AmendAndReapproveSalesOrderAsync(orderNumber); await salesSession.AssertNoUiFailuresAsync("sales-order preparation, amendment, and reapproval");
        }
        await using (var warehouseSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await warehouseSession.SignInAsync("warehouse"); var warehouse = new OperationsPage(warehouseSession); await warehouse.OpenAsync(); await warehouse.AllocateAndShipSalesOrderAsync(orderNumber, shipmentNumber, 1m); await warehouseSession.AssertNoUiFailuresAsync("sales-order allocation and partial shipment");
        }
        await using (var receivablesSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await receivablesSession.SignInAsync("controller"); var receivables = new OperationsPage(receivablesSession); await receivables.OpenAsync(); await receivables.InvoiceShipmentAsync(shipmentNumber, invoiceNumber); await receivablesSession.AssertNoUiFailuresAsync("shipment invoicing");
        }
        await using (var salesReturnSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await salesReturnSession.SignInAsync("sales"); var sales = new OperationsPage(salesReturnSession); await sales.OpenAsync(); await sales.AuthorizeCustomerReturnAsync(shipmentNumber, returnNumber); await salesReturnSession.AssertNoUiFailuresAsync("customer return authorization");
        }
        await using (var warehouseReturnSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await warehouseReturnSession.SignInAsync("warehouse"); var warehouse = new OperationsPage(warehouseReturnSession); await warehouse.OpenAsync(); await warehouse.ReceiveCustomerReturnAsync(returnNumber, receiptNumber); await warehouseReturnSession.AssertNoUiFailuresAsync("physical customer return receipt");
        }
        await using (var creditSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await creditSession.SignInAsync("controller"); var receivables = new OperationsPage(creditSession); await receivables.OpenAsync(); await receivables.CreditCustomerReturnAsync(receiptNumber, creditNumber); await creditSession.AssertNoUiFailuresAsync("customer return credit");
        }
        await using var cancellationSession = await _fixture.CreateSessionAsync(browserKind); await cancellationSession.SignInAsync("sales"); var cancellation = new OperationsPage(cancellationSession); await cancellation.OpenAsync(); await cancellation.CancelOpenSalesOrderQuantityAsync(orderNumber); await cancellationSession.AssertNoUiFailuresAsync("sales-order remaining-quantity cancellation");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task SalesQuote_ApprovalAndConversion_CreateExactDraftOrder(BrowserKind browserKind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8]; var quoteNumber = $"QUO-E2E-{suffix}"; var orderNumber = $"SO-QUO-{suffix}";
        await using var salesSession = await _fixture.CreateSessionAsync(browserKind);
        await salesSession.SignInAsync("sales"); var sales = new OperationsPage(salesSession); await sales.OpenAsync(); await sales.PrepareApproveAndConvertSalesQuoteAsync(quoteNumber, orderNumber); await salesSession.AssertNoUiFailuresAsync("sales-quote approval and conversion");
    }
}
