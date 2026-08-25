using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class BankingWorkflowTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public BankingWorkflowTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Ledger_ImportsStatementAndReversesTransferAndAdjustment(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var ledger = new LedgerPage(session);
        await ledger.OpenAsync();
        await ledger.ImportStatementAndReverseBankingEntriesAsync(browserKind.ToString());
        await session.AssertNoUiFailuresAsync("banking workflow");
    }
}
