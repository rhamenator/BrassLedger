using BrassLedger.Web.E2E.Tests.Pages;
using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class RecurringDocumentWorkflowTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public RecurringDocumentWorkflowTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task RecurringInvoiceTemplate_GeneratesDueOccurrenceThatApprovesAndPosts(BrowserKind browserKind)
    {
        await _fixture.CreateSubledgerWorkflowUsersAsync();
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var templateNumber = $"INV-RECUR-{browserKind}";
        var occurrenceNumber = $"{templateNumber}-{DateTime.Today:yyyyMMdd}";

        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var receivables = new ReceivablesPage(session);
        await receivables.OpenAsync();
        // Setting the template's own next occurrence and the generation cutoff to the same
        // day guarantees exactly one occurrence generates, deterministically, regardless of
        // the chosen recurrence frequency.
        await receivables.SaveRecurringInvoiceTemplateAsync(templateNumber, today);
        await receivables.GenerateDueRecurringInvoiceDraftsAsync(today);
        await session.AssertNoUiFailuresAsync("saving a recurring invoice template and generating its due occurrence");

        await using (var approverSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await approverSession.SignInAsync("e2e-ar-approver");
            var approver = new ReceivablesPage(approverSession);
            await approver.OpenAsync();
            await approver.ApproveInvoiceAsync(occurrenceNumber);
        }
        await using (var posterSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await posterSession.SignInAsync("e2e-ar-poster");
            var poster = new ReceivablesPage(posterSession);
            await poster.OpenAsync();
            await poster.PostInvoiceAsync(occurrenceNumber, "$100.00");
        }
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task RecurringVendorBillTemplate_GeneratesDueOccurrenceThatApprovesAndPosts(BrowserKind browserKind)
    {
        await _fixture.CreateSubledgerWorkflowUsersAsync();
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var templateNumber = $"RB-RECUR-{browserKind}";
        var occurrenceNumber = $"{templateNumber}-{DateTime.Today:yyyyMMdd}";

        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var payables = new PayablesPage(session);
        await payables.OpenAsync();
        await payables.SaveRecurringVendorBillTemplateAsync(templateNumber, today);
        await payables.GenerateDueRecurringBillDraftsAsync(today);
        await session.AssertNoUiFailuresAsync("saving a recurring vendor bill template and generating its due occurrence");

        await using (var approverSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await approverSession.SignInAsync("e2e-ap-approver");
            var approver = new PayablesPage(approverSession);
            await approver.OpenAsync();
            await approver.ApproveBillAsync(occurrenceNumber);
        }
        await using (var posterSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await posterSession.SignInAsync("e2e-ap-poster");
            var poster = new PayablesPage(posterSession);
            await poster.OpenAsync();
            await poster.PostBillAsync(occurrenceNumber, "$50.00");
        }
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ForeignRecurringInvoiceTemplate_RequiresRateAssignmentBeforeApproval(BrowserKind browserKind)
    {
        await _fixture.CreateSubledgerWorkflowUsersAsync();
        await _fixture.CreateForeignRefundRatesAsync();
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var templateNumber = $"INV-FXRECUR-{browserKind}";
        var occurrenceNumber = $"{templateNumber}-{DateTime.Today:yyyyMMdd}";

        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var receivables = new ReceivablesPage(session);
        await receivables.OpenAsync();
        // Saving a foreign recurring template never accepts a fixed rate (proven at the
        // service layer already); the generated occurrence must come back needing one.
        await receivables.SaveRecurringInvoiceTemplateAsync(templateNumber, today, transactionCurrency: "CAD");
        await receivables.GenerateDueRecurringInvoiceDraftsAsync(today);
        await session.AssertNoUiFailuresAsync("saving a foreign recurring invoice template and generating its due occurrence");

        // Approval must be blocked -- the Approve button itself is hidden -- until a rate is
        // assigned to this specific occurrence, proving the UI never lets a foreign occurrence
        // through without one.
        await using (var approverSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await approverSession.SignInAsync("e2e-ar-approver");
            var approver = new ReceivablesPage(approverSession);
            await approver.OpenAsync();
            var workflowRow = approverSession.Page.Locator("tbody tr").Filter(new() { HasText = occurrenceNumber });
            await Assertions.Expect(workflowRow).ToContainTextAsync("Draft");
            await Assertions.Expect(workflowRow.GetByRole(AriaRole.Button, new() { Name = "Approve" })).ToHaveCountAsync(0);
        }

        await receivables.OpenAsync();
        await receivables.AssignRecurringOccurrenceRateAsync(occurrenceNumber, "E2E document rate");
        await session.AssertNoUiFailuresAsync("assigning an exchange rate to the generated foreign occurrence");

        await using (var approverSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await approverSession.SignInAsync("e2e-ar-approver");
            var approver = new ReceivablesPage(approverSession);
            await approver.OpenAsync();
            await approver.ApproveInvoiceAsync(occurrenceNumber);
        }
        await using (var posterSession = await _fixture.CreateSessionAsync(browserKind))
        {
            await posterSession.SignInAsync("e2e-ar-poster");
            var poster = new ReceivablesPage(posterSession);
            await poster.OpenAsync();
            // A foreign invoice row never carries a "$" prefix (that formatting is
            // conditional on the transaction currency matching the base currency);
            // its own transaction-currency amount and the base-currency conversion at
            // the assigned 0.75 rate are asserted directly instead, proving the rate
            // assigned to this specific occurrence actually applied.
            await poster.PostInvoiceAsync(occurrenceNumber, "100.00 CAD");
            await Assertions.Expect(posterSession.Page.Locator("tbody tr").Filter(new() { HasText = occurrenceNumber }).Filter(new() { HasText = "75.00 USD" }))
                .ToBeVisibleAsync();
        }
    }
}
