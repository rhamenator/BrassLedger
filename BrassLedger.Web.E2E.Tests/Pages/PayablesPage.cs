namespace BrassLedger.Web.E2E.Tests.Pages;

using Microsoft.Playwright;

public sealed class PayablesPage
{
    private readonly UiSession _session;

    public PayablesPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/payables");
        await _session.WaitForHeadingAsync("Vendor management and outgoing cash commitments.");
    }

    public async Task AssertVendorAndBillDataAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("V-2001 - Ironwood Steel Supply", content);
        Assert.Contains("B-8810", content);
        Assert.Contains("Apex Staffing", content);
        Assert.Contains("$13,210.50", content);
    }

    public async Task CreateItemizedBillDraftAsync(string billNumber)
    {
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Add line" }).ClickAsync();
        await _session.AssertNoUiFailuresAsync("adding a bill line");
        await Assertions.Expect(_session.Page.GetByLabel("Bill line description")).ToHaveCountAsync(2);
        await _session.Page.GetByLabel("Bill vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Bill number").FillAsync(billNumber);
        await _session.Page.GetByLabel("Bill line description").First.FillAsync("Materials");
        await _session.Page.GetByLabel("Bill line quantity").First.FillAsync("2");
        await _session.Page.GetByLabel("Bill line unit cost").First.FillAsync("25");
        await _session.Page.GetByLabel("Bill line discount").First.FillAsync("5");
        await _session.Page.GetByLabel("Bill line tax").First.FillAsync("3");
        await _session.Page.GetByLabel("Bill line description").Nth(1).FillAsync("Supplies");
        await _session.Page.GetByLabel("Bill line unit cost").Nth(1).FillAsync("40");
        await _session.Page.GetByLabel("Bill line tax").Nth(1).FillAsync("2");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save bill draft" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor bill draft saved.");
    }

    public async Task ApproveBillAsync(string billNumber)
    {
        var workflowRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = billNumber });
        await Assertions.Expect(workflowRow).ToContainTextAsync("Draft");
        await workflowRow.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor bill draft approved.");
    }

    public async Task PostBillAsync(string billNumber, string expectedTotal)
    {
        var workflowRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = billNumber });
        await Assertions.Expect(workflowRow).ToContainTextAsync("Approved");
        await workflowRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Approved vendor bill posted.");
        var row = _session.Page.Locator("tbody tr").Filter(new() { HasText = billNumber }).Filter(new() { HasText = expectedTotal });
        await Assertions.Expect(row).ToContainTextAsync(expectedTotal);
        await Assertions.Expect(row).ToContainTextAsync("2");
    }

    public async Task RecordAndVoidVendorPaymentAsync(string billNumber, string paymentReference)
    {
        await _session.Page.GetByLabel("Payment vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Payment account").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Vendor payment total").FillAsync("100");
        await _session.Page.GetByLabel("Vendor payment method").SelectOptionAsync("Check");
        await _session.Page.GetByLabel("Payment reference").FillAsync(paymentReference);
        await _session.Page.GetByLabel($"Apply to {billNumber}").CheckAsync();
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Record vendor payment" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor payment recorded.");

        var historyRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = paymentReference });
        await Assertions.Expect(historyRow).ToContainTextAsync("$100.00");
        await Assertions.Expect(historyRow).ToContainTextAsync("$90.00");
        await Assertions.Expect(historyRow).ToContainTextAsync("$10.00");
        await Assertions.Expect(historyRow).ToContainTextAsync("Posted");

        await _session.Page.GetByLabel("Vendor payment reversal reason").FillAsync("E2E voided check");
        await historyRow.GetByRole(AriaRole.Button, new() { Name = "Record voided" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor payment marked voided.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = paymentReference })).ToContainTextAsync("Voided");
    }

    public async Task RecordAndReverseVendorCreditAsync(string adjustmentReference)
    {
        await _session.Page.GetByLabel("Vendor credit bill").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Vendor credit amount").FillAsync("5");
        await _session.Page.GetByLabel("Vendor credit reference").FillAsync(adjustmentReference);
        await _session.Page.GetByLabel("Vendor credit reason").FillAsync("E2E vendor allowance");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post vendor credit" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor credit posted.");
        var row = _session.Page.Locator("tbody tr").Filter(new() { HasText = adjustmentReference });
        await Assertions.Expect(row).ToContainTextAsync("VendorCredit");
        await Assertions.Expect(row).ToContainTextAsync("$5.00");
        await _session.Page.GetByLabel("Vendor adjustment reversal reason").FillAsync("E2E allowance withdrawn");
        await row.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor adjustment reversed.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = adjustmentReference })).ToContainTextAsync("Reversed");
    }

    public async Task RecordAndRefundForeignAdvanceAsync(string paymentReference, string refundReference)
    {
        var paymentDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-1).ToString("yyyy-MM-dd");
        var refundDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        await _session.Page.GetByLabel("Payment vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Payment account").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Vendor payment date").FillAsync(paymentDate);
        await _session.Page.GetByLabel("Vendor payment total").FillAsync("100");
        await _session.Page.GetByLabel("Vendor payment method").SelectOptionAsync("Wire");
        await _session.Page.GetByLabel("Payment reference").FillAsync(paymentReference);
        await _session.Page.GetByLabel("Vendor payment transaction currency").FillAsync("CAD");
        await _session.Page.GetByLabel("Vendor payment transaction currency").PressAsync("Tab");
        await SelectOptionContainingAsync("Vendor payment exchange rate", "E2E document rate");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Record vendor payment" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor payment recorded.");
        var paymentRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = paymentReference });
        await Assertions.Expect(paymentRow).ToContainTextAsync("100.00 CAD");
        await Assertions.Expect(paymentRow).ToContainTextAsync("$75.00 USD");

        await SelectOptionContainingAsync("Refund vendor advance", paymentReference);
        await _session.Page.GetByLabel("Vendor refund bank").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Vendor refund date").FillAsync(refundDate);
        await SelectOptionContainingAsync("Vendor refund exchange rate", "E2E refund rate");
        await _session.Page.GetByLabel("Vendor refund amount").FillAsync("40");
        await _session.Page.GetByLabel("Vendor refund reference").FillAsync(refundReference);
        await _session.Page.GetByLabel("Vendor refund reason").FillAsync("E2E CAD advance refund");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Record vendor refund" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor refund recorded.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = refundReference })).ToContainTextAsync("$32.00");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = paymentReference })).ToContainTextAsync("60.00 CAD");
    }

    public async Task CreateForeignItemizedBillDraftAsync(string billNumber)
    {
        await _session.Page.GetByLabel("Bill vendor").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Bill number").FillAsync(billNumber);
        await _session.Page.GetByLabel("Bill transaction currency").FillAsync("CAD");
        await _session.Page.GetByLabel("Bill transaction currency").PressAsync("Tab");
        await SelectOptionContainingAsync("Bill exchange rate", "E2E document rate");
        await Assertions.Expect(_session.Page.GetByLabel("Bill line description")).ToHaveCountAsync(1);
        await _session.Page.GetByLabel("Bill line description").First.FillAsync("Foreign materials");
        await _session.Page.GetByLabel("Bill line quantity").First.FillAsync("2");
        await _session.Page.GetByLabel("Bill line unit cost").First.FillAsync("50");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save bill draft" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor bill draft saved.");
    }

    public async Task PostForeignBillAsync(string billNumber)
    {
        var workflowRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = billNumber }).Filter(new() { HasText = "Approved" });
        await Assertions.Expect(workflowRow).ToContainTextAsync("Approved");
        await workflowRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Approved vendor bill posted.");
    }

    public async Task RecordAndReverseForeignVendorCreditAsync(string billNumber, string reference)
    {
        await RecordForeignVendorCreditAsync(billNumber, reference, "40", "OriginalDocumentRate");
        var row = _session.Page.Locator("tbody tr").Filter(new() { HasText = reference });
        await Assertions.Expect(row).ToContainTextAsync("VendorCredit");
        await Assertions.Expect(row).ToContainTextAsync("$30.00");
        await _session.Page.GetByLabel("Vendor adjustment reversal reason").FillAsync("E2E foreign allowance withdrawn");
        await row.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor adjustment reversed.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = reference })).ToContainTextAsync("Reversed");
    }

    public async Task RecordAndReverseForeignAdjustmentDateVendorCreditAsync(string billNumber, string reference)
    {
        await RecordForeignVendorCreditAsync(billNumber, reference, "30", "AdjustmentDateRate");
        var row = _session.Page.Locator("tbody tr").Filter(new() { HasText = reference });
        await Assertions.Expect(row).ToContainTextAsync("VendorCredit");
        await Assertions.Expect(row).ToContainTextAsync("$24.00");
        await _session.Page.GetByLabel("Vendor adjustment reversal reason").FillAsync("E2E foreign dated concession withdrawn");
        await row.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor adjustment reversed.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = reference })).ToContainTextAsync("Reversed");
    }

    private async Task RecordForeignVendorCreditAsync(string billNumber, string reference, string amount, string rateBasis)
    {
        await SelectOptionContainingAsync("Vendor credit bill", billNumber);
        await Assertions.Expect(_session.Page.GetByLabel("Vendor credit rate basis")).ToBeVisibleAsync();
        await _session.Page.GetByLabel("Vendor credit rate basis").SelectOptionAsync(rateBasis);
        if (rateBasis == "AdjustmentDateRate")
        {
            await SelectOptionContainingAsync("Vendor credit exchange rate", "E2E refund rate");
        }
        await _session.Page.GetByLabel("Vendor credit amount").FillAsync(amount);
        await _session.Page.GetByLabel("Vendor credit reference").FillAsync(reference);
        await _session.Page.GetByLabel("Vendor credit reason").FillAsync("E2E foreign vendor adjustment");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post vendor credit" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor credit posted.");
    }

    private async Task SelectOptionContainingAsync(string label, string text)
    {
        var select = _session.Page.GetByLabel(label);
        await Assertions.Expect(select).ToBeEnabledAsync();
        var option = select.Locator("option").Filter(new() { HasText = text });
        await Assertions.Expect(option).ToHaveCountAsync(1);
        await select.SelectOptionAsync(await option.GetAttributeAsync("value") ?? throw new InvalidOperationException($"The {label} option has no value."));
    }
}
