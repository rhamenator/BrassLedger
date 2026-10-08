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

    public async Task ImportQuickBooksInvoiceDraftAsync(string invoiceNumber)
    {
        await _session.Page.GetByLabel("QuickBooks data to import").SelectOptionAsync("invoices");
        await _session.Page.GetByLabel("QuickBooks CSV file").SetInputFilesAsync(new FilePayload
        {
            Name = $"{invoiceNumber}.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes($"Invoice No.,Customer,Invoice Date,Due Date,Item Amount,Item Description,Quantity,Rate,Income Account\n{invoiceNumber},C-1003,2026-08-10,2026-09-09,75.00,E2E imported service,3,25.00,4000")
        });
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Validate only" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Validation passed: 1 QuickBooks invoices record(s)");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = $"{invoiceNumber}.csv" })).ToContainTextAsync("Validated");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Import CSV" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Created 1 QuickBooks invoice draft(s)");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = $"{invoiceNumber}.csv" }).First).ToContainTextAsync("DraftsCreated");
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
        var treasuryActivity = _session.Page.Locator("section.panel").Filter(new() { HasText = "Transfers and reconciliation adjustments" }).First;
        var transferRow = treasuryActivity.Locator("tbody tr").Filter(new() { HasText = transferReference });
        await Assertions.Expect(transferRow).ToContainTextAsync("Posted");
        await _session.Page.GetByLabel("Transfer reversal reason").FillAsync("E2E transfer correction");
        await transferRow.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Bank transfer reversed.");
        await Assertions.Expect(treasuryActivity.Locator("tbody tr").Filter(new() { HasText = transferReference })).ToContainTextAsync("Reversed");

        var adjustmentReference = $"ADJ-E2E-{suffix}";
        await _session.Page.GetByLabel("Adjustment bank account").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Bank adjustment amount").FillAsync("5");
        await _session.Page.GetByLabel("Bank adjustment offset account").SelectOptionAsync("5100");
        await _session.Page.GetByLabel("Bank adjustment reference").FillAsync(adjustmentReference);
        await _session.Page.GetByLabel("Bank adjustment description").FillAsync("E2E statement correction");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Post reconciliation adjustment" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Reconciliation adjustment posted.");
        var adjustmentRow = treasuryActivity.Locator("tbody tr").Filter(new() { HasText = adjustmentReference });
        await Assertions.Expect(adjustmentRow).ToContainTextAsync("Posted");
        await _session.Page.GetByLabel("Adjustment reversal reason").FillAsync("E2E adjustment correction");
        await adjustmentRow.GetByRole(AriaRole.Button, new() { Name = "Reverse" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Reconciliation adjustment reversed.");
        await Assertions.Expect(treasuryActivity.Locator("tbody tr").Filter(new() { HasText = adjustmentReference })).ToContainTextAsync("Reversed");
    }

    public async Task CreateDepreciateDisposeAndReverseAssetAsync(string suffix, LedgerPage reviewer, LedgerPage poster)
    {
        var scheduleNumber = $"FA-E2E-{suffix}";
        var acquisitionReference = $"ACQ-{suffix}";
        await _session.Page.GetByLabel("Journal entry date").FillAsync("2026-08-01");
        await _session.Page.GetByLabel("Journal entry reference").FillAsync(acquisitionReference);
        await _session.Page.GetByLabel("Journal entry description").FillAsync("Record E2E asset acquisition");
        await _session.Page.GetByLabel("Journal entry account").Nth(0).SelectOptionAsync("1500");
        await _session.Page.GetByLabel("Journal entry debit").Nth(0).FillAsync("1200");
        await _session.Page.GetByLabel("Journal entry line description").Nth(0).FillAsync("Asset cost");
        await _session.Page.GetByLabel("Journal entry account").Nth(1).SelectOptionAsync("3000");
        await _session.Page.GetByLabel("Journal entry credit").Nth(1).FillAsync("1200");
        await _session.Page.GetByLabel("Journal entry line description").Nth(1).FillAsync("Opening financing");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save draft", Exact = true }).ClickAsync();
        var recentEntries = _session.Page.Locator("article.panel").Filter(new() { HasText = "Recent journal entries" });
        var acquisitionRow = recentEntries.Locator("tbody tr").Filter(new() { HasText = acquisitionReference }).First;
        await Assertions.Expect(acquisitionRow).ToContainTextAsync("Draft");

        await reviewer.OpenAsync();
        await reviewer._session.Page.GetByLabel("Journal rejection reason").FillAsync("Attach the acquisition support.");
        var reviewerEntries = reviewer._session.Page.Locator("article.panel").Filter(new() { HasText = "Recent journal entries" });
        var reviewerAcquisitionRow = reviewerEntries.Locator("tbody tr").Filter(new() { HasText = acquisitionReference }).First;
        await reviewerAcquisitionRow.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
        await Assertions.Expect(reviewer._session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Journal draft rejected");
        await Assertions.Expect(reviewerEntries.Locator("tbody tr").Filter(new() { HasText = acquisitionReference }).First).ToContainTextAsync("Attach the acquisition support.");

        await OpenAsync();
        acquisitionRow = recentEntries.Locator("tbody tr").Filter(new() { HasText = acquisitionReference }).First;
        await acquisitionRow.GetByRole(AriaRole.Button, new() { Name = "Correct", Exact = true }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Heading, new() { Name = "Correct journal entry" })).ToBeVisibleAsync();
        await _session.Page.GetByLabel("Journal entry description").FillAsync("Record E2E asset acquisition — support attached");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save corrected draft", Exact = true }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Corrected journal draft saved for review");

        await reviewer.OpenAsync();
        reviewerAcquisitionRow = reviewerEntries.Locator("tbody tr").Filter(new() { HasText = acquisitionReference }).First;
        await reviewerAcquisitionRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await reviewer._session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Journal entry approved and ready to post" }).WaitForAsync();
        await poster.OpenAsync();
        var posterEntries = poster._session.Page.Locator("article.panel").Filter(new() { HasText = "Recent journal entries" });
        var posterAcquisitionRow = posterEntries.Locator("tbody tr").Filter(new() { HasText = acquisitionReference }).First;
        await posterAcquisitionRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
        await Assertions.Expect(poster._session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Approved journal entry posted");
        await OpenAsync();

        await _session.Page.GetByLabel("Accounting schedule number").FillAsync(scheduleNumber);
        await _session.Page.GetByLabel("Accounting schedule name").FillAsync("E2E test asset");
        await _session.Page.GetByLabel("Accounting schedule first posting date").FillAsync("2026-08-31");
        await _session.Page.GetByLabel("Accounting schedule period count").FillAsync("12");
        await _session.Page.GetByLabel("Accounting schedule original amount").FillAsync("1200");
        await _session.Page.GetByLabel("Fixed asset residual value").FillAsync("0");
        await _session.Page.GetByLabel("Fixed asset account").SelectOptionAsync(new SelectOptionValue { Label = "1500 — Fixed Assets" });
        await _session.Page.GetByLabel("Accounting schedule balance account").SelectOptionAsync(new SelectOptionValue { Label = "1590 — Accumulated Depreciation" });
        await _session.Page.GetByLabel("Accounting schedule expense account").SelectOptionAsync(new SelectOptionValue { Label = "6200 — Depreciation Expense" });
        await _session.Page.GetByLabel("Accounting schedule notes").FillAsync("Browser lifecycle test");
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Save schedule draft" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Accounting schedule draft saved");

        var scheduleRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = scheduleNumber }).First;
        await scheduleRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Accounting schedule approved");
        await _session.Page.GetByLabel("Prepare schedule installments through").FillAsync("2026-08-31");
        scheduleRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = scheduleNumber }).First;
        await scheduleRow.GetByRole(AriaRole.Button, new() { Name = "Prepare due drafts" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("journal review queue");

        var depreciationRow = recentEntries.Locator("tbody tr").Filter(new() { HasText = "E2E test asset installment 1" }).First;
        await Assertions.Expect(depreciationRow).ToContainTextAsync("Draft");
        await reviewer.OpenAsync();
        var reviewerDepreciationRow = reviewer._session.Page.Locator("article.panel").Filter(new() { HasText = "Recent journal entries" }).Locator("tbody tr").Filter(new() { HasText = "E2E test asset installment 1" }).First;
        await reviewerDepreciationRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await reviewer._session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Journal entry approved and ready to post" }).WaitForAsync();
        await poster.OpenAsync();
        var posterDepreciationRow = poster._session.Page.Locator("article.panel").Filter(new() { HasText = "Recent journal entries" }).Locator("tbody tr").Filter(new() { HasText = "E2E test asset installment 1" }).First;
        await posterDepreciationRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
        await Assertions.Expect(poster._session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Approved journal entry posted");
        await OpenAsync();

        scheduleRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = scheduleNumber }).First;
        await scheduleRow.GetByRole(AriaRole.Button, new() { Name = "Dispose / retire" }).ClickAsync();
        await _session.Page.GetByLabel("Fixed asset disposal date").FillAsync("2026-09-15");
        await _session.Page.GetByLabel("Fixed asset disposal proceeds").FillAsync("1300");
        await _session.Page.GetByLabel("Fixed asset disposal bank account").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await _session.Page.GetByLabel("Fixed asset disposal gain account").SelectOptionAsync(new SelectOptionValue { Label = "4400 — Gain on Asset Disposal" });
        await _session.Page.GetByLabel("Fixed asset disposal loss account").SelectOptionAsync(new SelectOptionValue { Label = "6500 — Loss on Asset Disposal" });
        await _session.Page.GetByRole(AriaRole.Button, new() { Name = "Prepare disposal draft" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("disposal draft was added");

        var disposalRow = recentEntries.Locator("tbody tr").Filter(new() { HasText = "Dispose or retire E2E test asset" }).First;
        await Assertions.Expect(disposalRow).ToContainTextAsync("Draft");
        await reviewer.OpenAsync();
        var reviewerDisposalRow = reviewer._session.Page.Locator("article.panel").Filter(new() { HasText = "Recent journal entries" }).Locator("tbody tr").Filter(new() { HasText = "Dispose or retire E2E test asset" }).First;
        await reviewerDisposalRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await reviewer._session.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Journal entry approved and ready to post" }).WaitForAsync();
        await poster.OpenAsync();
        var posterDisposalRow = poster._session.Page.Locator("article.panel").Filter(new() { HasText = "Recent journal entries" }).Locator("tbody tr").Filter(new() { HasText = "Dispose or retire E2E test asset" }).First;
        await posterDisposalRow.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
        await OpenAsync();
        scheduleRow = _session.Page.Locator("tbody tr").Filter(new() { HasText = scheduleNumber }).First;
        await Assertions.Expect(scheduleRow).ToContainTextAsync("Disposed");

        await _session.Page.GetByLabel("Schedule reversal date").FillAsync("2026-09-16");
        await _session.Page.GetByLabel("Schedule reversal reason").FillAsync("E2E disposal correction");
        await scheduleRow.GetByRole(AriaRole.Button, new() { Name = "Reverse disposal" }).ClickAsync();
        await Assertions.Expect(_session.Page.GetByRole(AriaRole.Status)).ToContainTextAsync("asset disposal was reversed");
        await Assertions.Expect(_session.Page.Locator("tbody tr").Filter(new() { HasText = scheduleNumber }).First).ToContainTextAsync("Disposal reversed");
    }
}
