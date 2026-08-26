namespace BrassLedger.Web.E2E.Tests.Pages;

using Microsoft.Playwright;

public sealed class ReceivablesPage
{
    private readonly UiSession _session;

    public ReceivablesPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/receivables");
        await _session.WaitForHeadingAsync("Customers, invoices, and open-balance follow-up.");
    }

    public async Task AssertCustomerAndInvoiceDataAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("C-1001 - Red Mesa Builders", content);
        Assert.Contains("INV-24015", content);
        Assert.Contains("Lakeview Retail Group", content);
        Assert.Contains("$12,720.00", content);
    }

    public async Task ApproveInvoiceAsync(string invoiceNumber)
    {
        var workflowRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = invoiceNumber });
        await Assertions.Expect(workflowRow).ToContainTextAsync("Draft");
        await workflowRow.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Invoice draft approved.");
    }

    public async Task RejectInvoiceAsync(string invoiceNumber, string reason)
    {
        await _session.Page.GetByLabel("Invoice rejection reason").FillAsync(reason);
        var workflowRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = invoiceNumber });
        await Assertions.Expect(workflowRow).ToContainTextAsync("Draft");
        await workflowRow.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Invoice draft rejected.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = invoiceNumber })).ToContainTextAsync(reason);
    }

    public async Task AssertRejectedInvoiceAsync(string invoiceNumber, string reason)
    {
        var workflowRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = invoiceNumber });
        await Assertions.Expect(workflowRow).ToContainTextAsync("Rejected");
        await Assertions.Expect(workflowRow).ToContainTextAsync(reason);
    }

    public async Task PostInvoiceAsync(string invoiceNumber, string expectedTotal)
    {
        var workflowRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = invoiceNumber });
        await Assertions.Expect(workflowRow).ToContainTextAsync("Approved");
        await workflowRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Approved invoice posted.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = invoiceNumber }).Filter(new() { HasText = expectedTotal })).ToContainTextAsync(expectedTotal);
    }

    public async Task CreateItemizedInvoiceDraftAsync(string invoiceNumber)
    {
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Add line" }).ClickAsync();
        await _session.AssertNoUiFailuresAsync("adding an invoice line");
        await Assertions.Expect(_session.Page.GetByLabel("Invoice line description")).ToHaveCountAsync(2);
        await _session.Page.GetByLabel("Invoice customer").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Invoice number").FillAsync(invoiceNumber);
        await _session.Page.GetByLabel("Invoice line description").First.FillAsync("Equipment");
        await _session.Page.GetByLabel("Invoice line quantity").First.FillAsync("2");
        await _session.Page.GetByLabel("Invoice line unit price").First.FillAsync("50");
        await _session.Page.GetByLabel("Invoice line discount").First.FillAsync("5");
        await _session.Page.GetByLabel("Invoice line tax").First.FillAsync("7");
        await _session.Page.GetByLabel("Invoice line description").Nth(1).FillAsync("Installation");
        await _session.Page.GetByLabel("Invoice line quantity").Nth(1).FillAsync("3");
        await _session.Page.GetByLabel("Invoice line unit price").Nth(1).FillAsync("20");
        await _session.Page.GetByLabel("Invoice line tax").Nth(1).FillAsync("3");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save invoice draft" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Invoice draft saved.");
    }

    public async Task RecordAndReturnCustomerPaymentAsync(string invoiceNumber, string paymentReference)
    {
        await _session.Page.GetByLabel("Payment customer").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Payment deposit account").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Payment total").FillAsync("180");
        await _session.Page.GetByLabel("Payment method").SelectOptionAsync("ACH");
        await _session.Page.GetByLabel("Payment reference").FillAsync(paymentReference);
        await _session.Page.GetByLabel($"Apply to {invoiceNumber}").CheckAsync();
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Record customer payment" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Customer payment recorded.");

        var historyRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = paymentReference });
        await Assertions.Expect(historyRow).ToContainTextAsync("$180.00");
        await Assertions.Expect(historyRow).ToContainTextAsync("$165.00");
        await Assertions.Expect(historyRow).ToContainTextAsync("$15.00");
        await Assertions.Expect(historyRow).ToContainTextAsync("Posted");

        await _session.Page.GetByLabel("Customer payment reversal reason").FillAsync("E2E returned deposit");
        await historyRow.GetByRole(AriaRole.Button, new() { Name = "Record returned" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Customer payment marked returned.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = paymentReference })).ToContainTextAsync("Returned");
    }

    public async Task RecordAndReverseCreditMemoAsync(string adjustmentReference)
    {
        await _session.Page.GetByLabel("Adjustment invoice").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Customer adjustment amount").FillAsync("5");
        await _session.Page.GetByLabel("Customer adjustment reference").FillAsync(adjustmentReference);
        await _session.Page.GetByLabel("Customer adjustment reason").FillAsync("E2E price allowance");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post customer adjustment" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Customer adjustment posted.");
        var row = _session.Page.Locator("tbody tr").Filter(new() { HasText = adjustmentReference });
        await Assertions.Expect(row).ToContainTextAsync("CreditMemo");
        await Assertions.Expect(row).ToContainTextAsync("$5.00");
        await _session.Page.GetByLabel("Customer adjustment reversal reason").FillAsync("E2E allowance withdrawn");
        await row.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Customer adjustment reversed.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = adjustmentReference })).ToContainTextAsync("Reversed");
    }
}
