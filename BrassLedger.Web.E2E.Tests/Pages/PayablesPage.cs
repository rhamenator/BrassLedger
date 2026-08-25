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

    public async Task CreateItemizedBillAsync(string billNumber)
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
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post bill" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Vendor bill posted.");
        var row = _session.Page.Locator("tbody tr").Filter(new() { HasText = billNumber });
        await Assertions.Expect(row).ToContainTextAsync("$90.00");
        await Assertions.Expect(row).ToContainTextAsync("2");
    }
}
