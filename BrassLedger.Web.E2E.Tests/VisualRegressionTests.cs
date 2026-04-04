using BrassLedger.Web.E2E.Tests.Pages;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class VisualRegressionTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public VisualRegressionTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.SnapshotBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task OverviewSnapshot_MatchesBaseline(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var overview = new OverviewPage(session);

        await overview.OpenAsync();
        await session.AssertSnapshotAsync("overview");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.SnapshotBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task LedgerSnapshot_MatchesBaseline(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var ledger = new LedgerPage(session);

        await ledger.OpenAsync();
        await session.AssertSnapshotAsync("ledger");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.SnapshotBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ReportingSnapshot_MatchesBaseline(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync();
        var reporting = new ReportingPage(session);

        await reporting.OpenAsync();
        await session.AssertSnapshotAsync("reporting");
    }
}
