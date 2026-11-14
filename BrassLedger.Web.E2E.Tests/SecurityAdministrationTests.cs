using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class SecurityAdministrationTests(PlaywrightWebAppFixture fixture)
{
    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task SecurityAdministration_ManagerCreatesRoleInInteractiveSession(BrowserKind browserKind)
    {
        await fixture.CreateQuickBooksAdministratorAsync();
        await fixture.GrantIntegrationAdministratorRoleManagementAsync();
        try
        {
            await using var session = await fixture.CreateSessionAsync(browserKind);
            await session.SignInAsync("integration-admin");
            await session.GotoAsync("/administration");
            await session.WaitForHeadingAsync("Define role templates, separate duties, and prepare replacement access before it becomes urgent.");

            var roleName = $"E2E Security Role {browserKind}";
            await session.Page.GetByLabel("Role name").FillAsync(roleName);
            await session.Page.GetByLabel("Description", new() { Exact = true }).FillAsync("Created during an authenticated interactive session.");
            await session.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Role administration" }).CheckAsync();
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save custom role" }).ClickAsync();

            await Assertions.Expect(session.Page.GetByText("Custom role saved.")).ToBeVisibleAsync();
            await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = roleName })).ToBeVisibleAsync();
            await session.AssertNoUiFailuresAsync("interactive security role administration");
        }
        finally
        {
            await fixture.RemoveQuickBooksAdministratorAsync();
        }
    }
}
