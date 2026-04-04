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
    public async Task OperationsAndReportingPages_ShowExpectedOperationalArtifacts(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var operations = new OperationsPage(session);
        var reporting = new ReportingPage(session);

        await operations.OpenAsync();
        await operations.AssertOperationsDataAsync();
        await session.AssertNoUiFailuresAsync("operations workflow");

        await reporting.OpenAsync();
        await reporting.AssertReportingCatalogAsync();
        await session.AssertNoUiFailuresAsync("reporting workflow");
    }
}
