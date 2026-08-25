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

    public async Task CreateItemizedInvoiceAsync(string invoiceNumber)
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
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post invoice" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Invoice posted.");
        var row = _session.Page.Locator("tbody tr").Filter(new() { HasText = invoiceNumber });
        await Assertions.Expect(row).ToContainTextAsync("$165.00");
        await Assertions.Expect(row).ToContainTextAsync("2");
    }
}
