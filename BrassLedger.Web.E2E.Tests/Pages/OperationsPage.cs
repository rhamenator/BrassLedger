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

    public async Task ConfigureEditTransferAndReverseInventoryAsync(string warehouseCode, string transferReference)
    {
        await _session.Page.GetByText("Add warehouse", new() { Exact = true }).ClickAsync();
        await _session.Page.GetByLabel("Warehouse code").FillAsync(warehouseCode);
        await _session.Page.GetByLabel("Warehouse name").FillAsync("Browser distribution center");
        await _session.Page.GetByLabel("Warehouse address line 1").FillAsync("100 Browser Way");
        await _session.Page.GetByLabel("Warehouse city").FillAsync("Detroit");
        await _session.Page.GetByLabel("Warehouse state or province").FillAsync("MI");
        await _session.Page.GetByLabel("Warehouse postal code").FillAsync("48201");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save warehouse" }).ClickAsync();
        await _session.Page.GetByText("Warehouse and default stock bin created.", new() { Exact = true }).WaitForAsync();
        var locationRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory locations" }).Locator("tr").Filter(new() { HasTextString = warehouseCode });
        Assert.Contains("100 Browser Way", await locationRow.InnerTextAsync());

        await locationRow.GetByRole(AriaRole.Button, new() { Name = "Edit warehouse" }).ClickAsync();
        var warehouseName = _session.Page.GetByLabel("Warehouse name");
        await Assertions.Expect(warehouseName).ToHaveValueAsync("Browser distribution center");
        await warehouseName.FillAsync("Browser fulfillment center");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save warehouse" }).ClickAsync();
        await _session.Page.GetByText("Warehouse updated.", new() { Exact = true }).WaitForAsync();
        locationRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory locations" }).Locator("tr").Filter(new() { HasTextString = warehouseCode });
        await Assertions.Expect(locationRow).ToContainTextAsync("Browser fulfillment center");

        await _session.Page.GetByText("Add bin", new() { Exact = true }).ClickAsync();
        await _session.Page.GetByLabel("Bin warehouse").SelectOptionAsync(new SelectOptionValue { Label = $"{warehouseCode} — Browser fulfillment center" });
        await _session.Page.GetByLabel("Bin code").FillAsync("PICK");
        await _session.Page.GetByLabel("Bin name").FillAsync("Browser picking bin");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save bin" }).ClickAsync();
        await _session.Page.GetByText("Inventory bin created.", new() { Exact = true }).WaitForAsync();
        var binRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory locations" }).Locator("tr").Filter(new() { HasTextString = "PICK — Browser picking bin" });
        await binRow.GetByRole(AriaRole.Button, new() { Name = "Edit bin" }).ClickAsync();
        var binName = _session.Page.GetByLabel("Bin name"); await Assertions.Expect(binName).ToHaveValueAsync("Browser picking bin"); await binName.FillAsync("Primary browser picking");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save bin" }).ClickAsync();
        await _session.Page.GetByText("Inventory bin updated.", new() { Exact = true }).WaitForAsync();
        binRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory locations" }).Locator("tr").Filter(new() { HasTextString = "PICK" });
        await Assertions.Expect(binRow).ToContainTextAsync("Primary browser picking");

        await _session.Page.GetByText("Transfer stock", new() { Exact = true }).ClickAsync();
        await _session.Page.GetByLabel("Transfer inventory item").SelectOptionAsync(new SelectOptionValue { Label = "RM-220 — Steel Fastener Pack" });
        await _session.Page.GetByLabel("Transfer source bin").SelectOptionAsync(new SelectOptionValue { Label = "MAIN/STOCK" });
        await _session.Page.GetByLabel("Transfer destination bin").SelectOptionAsync(new SelectOptionValue { Label = $"{warehouseCode}/PICK" });
        await _session.Page.GetByLabel("Transfer quantity").FillAsync("1");
        await _session.Page.GetByLabel("Transfer reference").FillAsync(transferReference);
        await _session.Page.GetByLabel("Transfer reason").FillAsync("Browser-tested replenishment");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post transfer" }).ClickAsync();
        await _session.Page.GetByText("Inventory transfer posted.", new() { Exact = true }).WaitForAsync();
        var transferRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory transfers" }).Locator("tr").Filter(new() { HasTextString = transferReference });
        Assert.Contains($"MAIN/STOCK → {warehouseCode}/PICK", await transferRow.InnerTextAsync());
        await transferRow.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await _session.Page.GetByLabel("Transfer reversal reason").FillAsync("Browser-tested return to source");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Confirm transfer reversal" }).ClickAsync();
        await _session.Page.GetByText("Inventory transfer reversed.", new() { Exact = true }).WaitForAsync();
        transferRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory transfers" }).Locator("tr").Filter(new() { HasTextString = transferReference });
        Assert.Contains("Reversed", await transferRow.InnerTextAsync());
    }

    public async Task PrepareAndSubmitPurchaseRequisitionAsync(string requisitionNumber)
    {
        await _session.Page.GetByLabel("Purchase requisition suggested vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Purchase requisition number").FillAsync(requisitionNumber);
        await _session.Page.GetByLabel("Purchase requisition business purpose").FillAsync("Browser-tested inventory purchase");
        await _session.Page.GetByLabel("Purchase requisition line 1 item").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Purchase requisition line 1 description").FillAsync("Browser-tested inventory purchase");
        await _session.Page.GetByLabel("Purchase requisition line 1 quantity").FillAsync("2");
        await _session.Page.GetByLabel("Purchase requisition line 1 estimated unit cost").FillAsync("17.50");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save requisition draft" }).ClickAsync();
        await _session.Page.GetByText("Purchase-requisition draft saved.", new() { Exact = true }).WaitForAsync();
        var row = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Purchase requisitions" }).Locator("tr").Filter(new() { HasTextString = requisitionNumber });
        await row.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await _session.Page.GetByText($"Purchase requisition {requisitionNumber} submitted.", new() { Exact = true }).WaitForAsync();
    }

    public async Task ApproveAndConvertPurchaseRequisitionAsync(string requisitionNumber, string orderNumber)
    {
        var row = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Purchase requisitions" }).Locator("tr").Filter(new() { HasTextString = requisitionNumber });
        await row.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await _session.Page.GetByText($"Purchase requisition {requisitionNumber} approved.", new() { Exact = true }).WaitForAsync();
        row = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Purchase requisitions" }).Locator("tr").Filter(new() { HasTextString = requisitionNumber });
        await row.GetByRole(AriaRole.Button, new() { Name = "Create purchase order" }).ClickAsync();
        await _session.Page.GetByLabel("Converted purchase order number").FillAsync(orderNumber);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Create reviewable purchase-order draft" }).ClickAsync();
        await _session.Page.GetByText($"Purchase-order draft {orderNumber} created from {requisitionNumber}.", new() { Exact = true }).WaitForAsync();
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
        var row = SalesOrderRow(orderNumber);
        await row.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await _session.Page.GetByText($"Sales order {orderNumber} approved.", new() { Exact = true }).WaitForAsync();
    }

    public async Task AmendAndReapproveSalesOrderAsync(string orderNumber)
    {
        var row = SalesOrderRow(orderNumber);
        await row.GetByRole(AriaRole.Button, new() { Name = "Amend" }).ClickAsync();
        await _session.Page.GetByLabel("Sales order amendment reason").FillAsync("Browser-tested customer change");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save amendment for reapproval" }).ClickAsync();
        await _session.Page.GetByText($"Sales order {orderNumber} amended and returned to draft for approval.", new() { Exact = true }).WaitForAsync();
        row = SalesOrderRow(orderNumber); await row.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await _session.Page.GetByText($"Sales order {orderNumber} approved.", new() { Exact = true }).WaitForAsync();
    }

    public async Task CancelOpenSalesOrderQuantityAsync(string orderNumber)
    {
        var row = SalesOrderRow(orderNumber); await row.GetByRole(AriaRole.Button, new() { Name = "Cancel open quantity" }).ClickAsync();
        await _session.Page.GetByLabel("Sales order cancellation reason").FillAsync("Browser-tested customer cancellation");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Confirm cancellation" }).ClickAsync();
        await _session.Page.GetByText($"Open quantity on sales order {orderNumber} cancelled.", new() { Exact = true }).WaitForAsync();
        row = SalesOrderRow(orderNumber);
        var rowText = await row.InnerTextAsync();
        Assert.Contains("Closed", rowText);
        Assert.Matches(@"1(?:\.0+)? cancelled", rowText);
    }

    public async Task PrepareApproveAndConvertSalesQuoteAsync(string quoteNumber, string orderNumber)
    {
        await _session.Page.GetByText("Create quote draft", new() { Exact = true }).ClickAsync();
        await _session.Page.GetByLabel("Sales quote customer").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Sales quote number").FillAsync(quoteNumber);
        await _session.Page.GetByLabel("Sales quote line 1 item").SelectOptionAsync(new SelectOptionValue { Label = "RM-220 — Steel Fastener Pack" });
        await _session.Page.GetByLabel("Sales quote line 1 description").FillAsync("Browser-tested quoted fasteners");
        await _session.Page.GetByLabel("Sales quote line 1 quantity").FillAsync("2");
        await _session.Page.GetByLabel("Sales quote line 1 unit price").FillAsync("20");
        await _session.Page.GetByLabel("Sales quote line 1 discount").FillAsync("1");
        await _session.Page.GetByLabel("Sales quote line 1 tax").FillAsync("2");
        await _session.Page.GetByLabel("Sales quote line 1 revenue account").SelectOptionAsync(new SelectOptionValue { Label = "4000 — Product Revenue" });
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save quote draft" }).ClickAsync();
        await _session.Page.GetByText("Sales-quote draft saved.", new() { Exact = true }).WaitForAsync();
        var quoteRow = _session.Page.Locator("tr").Filter(new() { HasTextString = quoteNumber });
        await quoteRow.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await _session.Page.GetByText($"Sales quote {quoteNumber} approved.", new() { Exact = true }).WaitForAsync();
        quoteRow = _session.Page.Locator("tr").Filter(new() { HasTextString = quoteNumber });
        await quoteRow.GetByRole(AriaRole.Button, new() { Name = "Convert to order" }).ClickAsync();
        await _session.Page.GetByLabel("Converted sales order number").FillAsync(orderNumber);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Create draft order" }).ClickAsync();
        await _session.Page.GetByText($"Quote {quoteNumber} converted to draft sales order {orderNumber}.", new() { Exact = true }).WaitForAsync();
        quoteRow = _session.Page.Locator("tr").Filter(new() { HasTextString = quoteNumber }); Assert.Contains("Converted", await quoteRow.InnerTextAsync());
        var orderRow = _session.Page.Locator("tr").Filter(new() { HasTextString = orderNumber }); Assert.Contains("Draft", await orderRow.InnerTextAsync()); Assert.Contains("$41.00", await orderRow.InnerTextAsync());
    }

    public async Task AllocateAndShipSalesOrderAsync(string orderNumber, string shipmentNumber, decimal shipmentQuantity = 2m)
    {
        var quantityText = shipmentQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var pickNumber = $"PICK-{shipmentNumber}";
        var packingSlipNumber = $"PACK-{shipmentNumber}";
        var row = SalesOrderRow(orderNumber);
        await row.GetByRole(AriaRole.Button, new() { Name = "Allocate" }).ClickAsync();
        await _session.Page.GetByLabel("Allocate RM-220 quantity").FillAsync("2");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save allocation" }).ClickAsync();
        await _session.Page.GetByText("Inventory allocation saved.", new() { Exact = true }).WaitForAsync();

        row = SalesOrderRow(orderNumber);
        await row.GetByRole(AriaRole.Button, new() { Name = "Create pick" }).ClickAsync();
        await _session.Page.GetByLabel("Inventory pick number").FillAsync(pickNumber);
        await _session.Page.GetByLabel("Pick RM-220 quantity").FillAsync(quantityText);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Create pick ticket" }).ClickAsync();
        await _session.Page.GetByText($"Pick ticket {pickNumber} created.", new() { Exact = true }).WaitForAsync();

        var pickRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory picks" }).Locator("tr").Filter(new() { HasTextString = pickNumber });
        await pickRow.GetByRole(AriaRole.Button, new() { Name = "Complete pick" }).ClickAsync();
        await _session.Page.GetByLabel("Picked RM-220 quantity").FillAsync(quantityText);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Complete pick ticket" }).ClickAsync();
        await _session.Page.GetByText($"Pick ticket {pickNumber} completed.", new() { Exact = true }).WaitForAsync();

        pickRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory picks" }).Locator("tr").Filter(new() { HasTextString = pickNumber });
        await pickRow.GetByRole(AriaRole.Button, new() { Name = "Pack" }).ClickAsync();
        await _session.Page.GetByLabel("Inventory packing slip number").FillAsync(packingSlipNumber);
        await _session.Page.GetByLabel("Pack RM-220 quantity").FillAsync(quantityText);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Create packing slip" }).ClickAsync();
        await _session.Page.GetByText($"Packing slip {packingSlipNumber} created.", new() { Exact = true }).WaitForAsync();

        var packingRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory packing slips" }).Locator("tr").Filter(new() { HasTextString = packingSlipNumber });
        await packingRow.GetByRole(AriaRole.Button, new() { Name = "Ship packing slip" }).ClickAsync();
        await _session.Page.GetByLabel("Inventory shipment number").FillAsync(shipmentNumber);
        var packedShipmentQuantity = decimal.Parse(await _session.Page.GetByLabel("Ship RM-220 quantity").InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(shipmentQuantity, packedShipmentQuantity);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post shipment" }).ClickAsync();
        await _session.Page.GetByText("Customer shipment posted; inventory and COGS were updated.", new() { Exact = true }).WaitForAsync();
        packingRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory packing slips" }).Locator("tr").Filter(new() { HasTextString = packingSlipNumber });
        await Assertions.Expect(packingRow).ToContainTextAsync("Shipped");
    }

    public async Task InvoiceShipmentAsync(string shipmentNumber, string invoiceNumber)
    {
        var row = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Customer shipments" }).Locator("tr").Filter(new() { HasTextString = shipmentNumber });
        await row.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await _session.Page.GetByLabel("Shipment invoice number").FillAsync(invoiceNumber);
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post shipment invoice" }).ClickAsync();
        await _session.Page.GetByText("Shipment invoice posted to receivables.", new() { Exact = true }).WaitForAsync();
        row = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Customer shipments" }).Locator("tr").Filter(new() { HasTextString = shipmentNumber });
        Assert.Contains("Invoiced", await row.InnerTextAsync());
    }

    public async Task AuthorizeCustomerReturnAsync(string shipmentNumber, string returnNumber)
    {
        var shipmentRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Customer shipments" }).Locator("tr").Filter(new() { HasTextString = shipmentNumber });
        await shipmentRow.GetByRole(AriaRole.Button, new() { Name = "Authorize return" }).ClickAsync();
        await _session.Page.GetByLabel("Customer return authorization number").FillAsync(returnNumber);
        await _session.Page.GetByLabel("Customer return authorization reason").FillAsync("Browser-tested customer return");
        await _session.Page.GetByLabel("Return RM-220 quantity").FillAsync("1");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Authorize customer return" }).ClickAsync();
        await _session.Page.GetByText($"Customer return {returnNumber} authorized.", new() { Exact = true }).WaitForAsync();
        var row = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Customer return authorizations" }).Locator("tr").Filter(new() { HasTextString = returnNumber }); await Assertions.Expect(row).ToContainTextAsync("Open");
    }

    public async Task ReceiveCustomerReturnAsync(string returnNumber, string receiptNumber)
    {
        var authorizationRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Customer return authorizations" }).Locator("tr").Filter(new() { HasTextString = returnNumber }); await authorizationRow.GetByRole(AriaRole.Button, new() { Name = "Receive" }).ClickAsync();
        await _session.Page.GetByLabel("Customer return receipt number").FillAsync(receiptNumber); await _session.Page.GetByLabel("Receive returned RM-220 quantity").FillAsync("1"); await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post physical return receipt" }).ClickAsync();
        await _session.Page.GetByText($"Physical return receipt {receiptNumber} posted.", new() { Exact = true }).WaitForAsync(); var row = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Customer return receipts" }).Locator("tr").Filter(new() { HasTextString = receiptNumber }); await Assertions.Expect(row).ToContainTextAsync("Posted");
    }

    public async Task CreditCustomerReturnAsync(string receiptNumber, string creditNumber)
    {
        var receiptRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Customer return receipts" }).Locator("tr").Filter(new() { HasTextString = receiptNumber }); await receiptRow.GetByRole(AriaRole.Button, new() { Name = "Create credit" }).ClickAsync();
        await _session.Page.GetByLabel("Customer return credit number").FillAsync(creditNumber); await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post return credit" }).ClickAsync(); await _session.Page.GetByText($"Customer return credit {creditNumber} posted.", new() { Exact = true }).WaitForAsync(); var row = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Customer return credits" }).Locator("tr").Filter(new() { HasTextString = creditNumber }); await Assertions.Expect(row).ToContainTextAsync("Posted"); await Assertions.Expect(row).ToContainTextAsync("$0.00");
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

    public async Task AuthorizeAndShipSupplierReturnAsync(string receiptNumber, string returnNumber, string shipmentNumber)
    {
        var receiptRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory receipts" }).Locator("tr").Filter(new() { HasTextString = receiptNumber });
        await receiptRow.GetByRole(AriaRole.Button, new() { Name = "Return to supplier" }).ClickAsync();
        await _session.Page.GetByLabel("Supplier return authorization number").FillAsync(returnNumber);
        await _session.Page.GetByLabel("Supplier return authorization reason").FillAsync("Browser-tested supplier return");
        await _session.Page.Locator("input[aria-label^='Return '][aria-label$=' quantity']").FillAsync("1");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Authorize supplier return" }).ClickAsync();
        await _session.Page.GetByText($"Supplier return {returnNumber} authorized.", new() { Exact = true }).WaitForAsync();
        var authorizationRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Supplier return authorizations" }).Locator("tr").Filter(new() { HasTextString = returnNumber });
        await authorizationRow.GetByRole(AriaRole.Button, new() { Name = "Ship return" }).ClickAsync();
        await _session.Page.GetByLabel("Supplier return shipment number").FillAsync(shipmentNumber);
        await _session.Page.Locator("input[aria-label^='Ship returned '][aria-label$=' quantity']").FillAsync("1");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post supplier-return shipment" }).ClickAsync();
        await _session.Page.GetByText($"Supplier-return shipment {shipmentNumber} posted.", new() { Exact = true }).WaitForAsync();
        var shipmentRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Supplier return shipments" }).Locator("tr").Filter(new() { HasTextString = shipmentNumber }); await Assertions.Expect(shipmentRow).ToContainTextAsync("Vendor credit"); await Assertions.Expect(shipmentRow).ToContainTextAsync("Posted");
    }

    public async Task PrepareLandedCostAsync(string receiptNumber, string allocationNumber, string billNumber)
    {
        var receiptRow = _session.Page.GetByRole(AriaRole.Table, new() { Name = "Inventory receipts" }).Locator("tr").Filter(new() { HasTextString = receiptNumber });
        await receiptRow.GetByRole(AriaRole.Button, new() { Name = "Allocate landed cost" }).ClickAsync();
        await _session.Page.GetByLabel("Landed cost vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Landed cost allocation number").FillAsync(allocationNumber);
        await _session.Page.GetByLabel("Landed cost bill number").FillAsync(billNumber);
        await _session.Page.GetByLabel("Landed cost charge 1 amount").FillAsync("12.50");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save landed-cost draft" }).ClickAsync();
        await _session.Page.GetByText($"Landed-cost allocation {allocationNumber} saved as a draft.", new() { Exact = true }).WaitForAsync();
        var row = LandedCostRow(allocationNumber); await row.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await _session.Page.GetByText($"Landed-cost allocation {allocationNumber} submitted to Purchasing.", new() { Exact = true }).WaitForAsync();
    }

    public async Task ApproveLandedCostAsync(string allocationNumber)
    {
        var row = LandedCostRow(allocationNumber); await row.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await _session.Page.GetByLabel("Landed cost action reason").FillAsync("Browser review of freight invoice and allocation");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Confirm" }).ClickAsync();
        await _session.Page.GetByText($"Landed-cost allocation {allocationNumber} approved.", new() { Exact = true }).WaitForAsync();
    }

    public async Task PostLandedCostAsync(string allocationNumber)
    {
        var row = LandedCostRow(allocationNumber); await row.GetByRole(AriaRole.Button, new() { Name = "Post allocation" }).ClickAsync();
        await _session.Page.GetByText($"Landed-cost allocation {allocationNumber} posted to inventory and accounts payable.", new() { Exact = true }).WaitForAsync();
        row = LandedCostRow(allocationNumber); await Assertions.Expect(row).ToContainTextAsync("Posted"); await Assertions.Expect(row).ToContainTextAsync("$12.50");
    }

    private ILocator LandedCostRow(string allocationNumber) => _session.Page.GetByRole(AriaRole.Table, new() { Name = "Landed cost allocations" }).Locator("tr").Filter(new() { HasTextString = allocationNumber });

    private ILocator SalesOrderRow(string orderNumber) =>
        _session.Page.GetByRole(AriaRole.Table, new() { Name = "Sales orders" }).Locator("tr").Filter(new()
        {
            Has = _session.Page.GetByText(orderNumber, new() { Exact = true })
        });
}
