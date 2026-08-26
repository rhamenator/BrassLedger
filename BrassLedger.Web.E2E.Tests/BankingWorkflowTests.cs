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

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Ledger_ReviewsPostsDisposesAndReversesFixedAsset(BrowserKind browserKind)
    {
        await _fixture.CreateSubledgerWorkflowUsersAsync();
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await using var reviewerSession = await _fixture.CreateSessionAsync(browserKind);
        await using var posterSession = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        await reviewerSession.SignInAsync("e2e-journal-reviewer");
        await posterSession.SignInAsync("e2e-journal-poster");
        var ledger = new LedgerPage(session);
        var reviewerLedger = new LedgerPage(reviewerSession);
        var posterLedger = new LedgerPage(posterSession);
        await ledger.OpenAsync();
        await ledger.CreateDepreciateDisposeAndReverseAssetAsync(Guid.NewGuid().ToString("N")[..8], reviewerLedger, posterLedger);
        await session.AssertNoUiFailuresAsync("fixed-asset schedule workflow");
        await reviewerSession.AssertNoUiFailuresAsync("fixed-asset journal review workflow");
        await posterSession.AssertNoUiFailuresAsync("fixed-asset journal posting workflow");
    }
}
