namespace BrassLedger.Web.E2E.Tests.Pages;

using System.Text;
using Microsoft.Playwright;

public sealed class LedgerPage
{
    private readonly UiSession _session;

    public LedgerPage(UiSession session)
    {
        _session = session;
    }

    public async Task OpenAsync()
    {
        await _session.GotoAsync("/ledger");
        await _session.WaitForHeadingAsync("Core accounting balances and posting history.");
    }

    public async Task AssertSeededDataAsync()
    {
        var content = await _session.Page.ContentAsync();
        Assert.Contains("1000 - Operating Cash", content);
        Assert.Contains("JE-2401", content);
        Assert.Contains("Primary Operating", content);
        Assert.Contains("Payroll Clearing", content);
    }

    public async Task ImportStatementAndReverseBankingEntriesAsync(string suffix)
    {
        var externalId = $"BANK-E2E-{suffix}";
        await _session.Page.GetByLabel("Statement import bank account").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Bank statement file").SetInputFilesAsync(new FilePayload
        {
            Name = $"statement-{suffix}.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes($"ExternalId,Date,Amount,Payee,Memo\n{externalId},2026-08-01,9.75,Test customer,E2E statement")
        });
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Validate statement" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Validated 1 transaction(s)");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Import statement" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Imported 1 transaction(s)");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = externalId })).ToContainTextAsync("$9.75");

        var transferReference = $"TR-E2E-{suffix}";
        await _session.Page.GetByLabel("Transfer from bank").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Transfer to bank").SelectOptionAsync(new SelectOptionValue { Index = 2 });
        await _session.Page.GetByLabel("Transfer amount").FillAsync("25");
        await _session.Page.GetByLabel("Transfer reference").FillAsync(transferReference);
        await _session.Page.GetByLabel("Transfer memo").FillAsync("E2E cash movement");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post bank transfer" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Bank transfer posted.");
        var transferRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = transferReference });
        await Assertions.Expect(transferRow).ToContainTextAsync("Posted");
        await _session.Page.GetByLabel("Transfer reversal reason").FillAsync("E2E transfer correction");
        await transferRow.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Bank transfer reversed.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = transferReference })).ToContainTextAsync("Reversed");

        var adjustmentReference = $"ADJ-E2E-{suffix}";
        await _session.Page.GetByLabel("Adjustment bank account").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Bank adjustment amount").FillAsync("5");
        await _session.Page.GetByLabel("Bank adjustment offset account").SelectOptionAsync("5100");
        await _session.Page.GetByLabel("Bank adjustment reference").FillAsync(adjustmentReference);
        await _session.Page.GetByLabel("Bank adjustment description").FillAsync("E2E statement correction");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post reconciliation adjustment" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Reconciliation adjustment posted.");
        var adjustmentRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = adjustmentReference });
        await Assertions.Expect(adjustmentRow).ToContainTextAsync("Posted");
        await _session.Page.GetByLabel("Adjustment reversal reason").FillAsync("E2E adjustment correction");
        await adjustmentRow.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Reconciliation adjustment reversed.");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = adjustmentReference })).ToContainTextAsync("Reversed");
    }
}
