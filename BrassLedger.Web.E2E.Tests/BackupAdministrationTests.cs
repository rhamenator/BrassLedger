using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class BackupAdministrationTests(PlaywrightWebAppFixture fixture)
{
    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task BackupAdministration_OwnerCreatesVerifiesAndRehearsesLocalCopy(BrowserKind browserKind)
    {
        await fixture.CreateQuickBooksAdministratorAsync();
        await fixture.SetIntegrationAdministratorOwnershipAsync(true);
        try
        {
            await using var session = await fixture.CreateSessionAsync(browserKind);
            await session.SignInAsync("integration-admin");
            await session.GotoAsync("/administration");
            await session.WaitForHeadingAsync("Define role templates, separate duties, and prepare replacement access before it becomes urgent.");

            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Create backup" }).ClickAsync();
            await Assertions.Expect(session.Page.GetByText("Latest backup:", new() { Exact = false })).ToBeVisibleAsync();
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Verify backup" }).ClickAsync();
            await Assertions.Expect(session.Page.GetByText("Verified at", new() { Exact = false })).ToBeVisibleAsync();
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Rehearse restore" }).ClickAsync();
            await Assertions.Expect(session.Page.GetByText("Local file restore check passed:", new() { Exact = false })).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByText("Off-machine key recovery is not verified.", new() { Exact = false })).ToBeVisibleAsync();
            await session.AssertNoUiFailuresAsync("backup administration owner workflow");
        }
        finally
        {
            await fixture.RemoveQuickBooksAdministratorAsync();
        }
    }
}
