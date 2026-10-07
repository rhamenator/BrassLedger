using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class ProjectWipWorkflowTests(PlaywrightWebAppFixture fixture)
{
    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ProjectWip_UsesSeparatedPreparationApprovalPostingAndExactReversal(BrowserKind browserKind)
    {
        await fixture.CreateProjectWipUsersAsync();
        var projectNumber = $"WIP-{Guid.NewGuid():N}"[..12];

        await using (var setup = await fixture.CreateSessionAsync(browserKind))
        {
            await setup.SignInAsync(returnPath: "/projects");
            await setup.GotoAsync("/projects");
            await setup.Page.GetByLabel("Project number").FillAsync(projectNumber);
            await setup.Page.GetByLabel("Project name").FillAsync("Controlled WIP acceptance project");
            await setup.Page.GetByLabel("Project customer").SelectOptionAsync(new SelectOptionValue { Index = 1 });
            await setup.Page.GetByLabel("Project start date").FillAsync("2026-08-01");
            await setup.Page.GetByLabel("Project expected end date").FillAsync("2026-12-31");
            await setup.Page.GetByLabel("Project billing method").SelectOptionAsync("FixedPrice");
            await setup.Page.GetByLabel("Project revenue recognition method").SelectOptionAsync("ManualPercent");
            await setup.Page.GetByLabel("Project contract amount").FillAsync("10000");
            await setup.Page.GetByLabel("Project budget").FillAsync("5000");
            await setup.Page.GetByRole(AriaRole.Button, new() { Name = "Create project", Exact = true }).ClickAsync();
            await setup.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = $"Project {projectNumber} saved." }).WaitForAsync();
            await setup.AssertNoUiFailuresAsync("manual-percent project setup for WIP");
        }

        await using (var preparation = await fixture.CreateSessionAsync(browserKind))
        {
            await preparation.SignInAsync("e2e-project-wip-preparer", returnPath: "/projects");
            await preparation.GotoAsync("/projects");
            Assert.Empty(await preparation.Page.Locator("input[aria-label='Project number']").AllAsync());
            await preparation.Page.GetByLabel("WIP project").SelectOptionAsync(new SelectOptionValue { Label = $"{projectNumber} — Controlled WIP acceptance project (Manual percentage)" });
            await preparation.Page.GetByLabel("WIP through date").FillAsync("2026-08-26");
            await preparation.Page.GetByLabel("WIP posting date").FillAsync("2026-08-26");
            await preparation.Page.GetByLabel("WIP manual completion percent").FillAsync("25");
            await preparation.Page.GetByLabel("WIP revenue account").SelectOptionAsync("4000");
            await preparation.Page.GetByLabel("WIP description").FillAsync("Recognize reviewed project progress");
            await preparation.Page.GetByRole(AriaRole.Button, new() { Name = "Preview WIP", Exact = true }).ClickAsync();
            var preview = preparation.Page.Locator("table[aria-label='WIP preview'] tbody tr");
            await Assertions.Expect(preview).ToContainTextAsync("25.00%");
            await Assertions.Expect(preview).ToContainTextAsync("$2,500.00");
            await preparation.Page.GetByRole(AriaRole.Button, new() { Name = "Save controlled draft", Exact = true }).ClickAsync();
            await preparation.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Project WIP schedule saved as a controlled draft." }).WaitForAsync();
            var row = preparation.Page.Locator("table[aria-label='Project WIP schedules'] tbody tr").Filter(new() { HasTextString = projectNumber });
            await row.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
            await preparation.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Project WIP submitted for independent review." }).WaitForAsync();
            await preparation.AssertNoUiFailuresAsync("project WIP preparation and submission");
        }

        await using (var approval = await fixture.CreateSessionAsync(browserKind))
        {
            await approval.SignInAsync("e2e-project-wip-approver", returnPath: "/projects");
            await approval.GotoAsync("/projects");
            Assert.Empty(await approval.Page.Locator("select[aria-label='WIP project']").AllAsync());
            var row = approval.Page.Locator("table[aria-label='Project WIP schedules'] tbody tr").Filter(new() { HasTextString = projectNumber });
            await row.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
            await approval.Page.GetByLabel("WIP action reason").FillAsync("Reviewed contract and completion evidence");
            await approval.Page.GetByRole(AriaRole.Button, new() { Name = "Confirm approve", Exact = true }).ClickAsync();
            await approval.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Project WIP approve completed." }).WaitForAsync();
            row = approval.Page.Locator("table[aria-label='Project WIP schedules'] tbody tr").Filter(new() { HasTextString = projectNumber });
            await Assertions.Expect(row).ToContainTextAsync("Approved");
            await approval.AssertNoUiFailuresAsync("independent project WIP approval");
        }

        await using (var posting = await fixture.CreateSessionAsync(browserKind))
        {
            await posting.SignInAsync("e2e-project-wip-poster", returnPath: "/projects");
            await posting.GotoAsync("/projects");
            Assert.Empty(await posting.Page.Locator("select[aria-label='WIP project']").AllAsync());
            var row = posting.Page.Locator("table[aria-label='Project WIP schedules'] tbody tr").Filter(new() { HasTextString = projectNumber });
            await row.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
            await posting.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Approved project WIP posted to the ledger." }).WaitForAsync();
            row = posting.Page.Locator("table[aria-label='Project WIP schedules'] tbody tr").Filter(new() { HasTextString = projectNumber });
            await Assertions.Expect(row).ToContainTextAsync("Posted");
            await Assertions.Expect(row).ToContainTextAsync("Asset $2,500.00; liability $0.00");
            await row.GetByRole(AriaRole.Button, new() { Name = "Reverse", Exact = true }).ClickAsync();
            await posting.Page.GetByLabel("WIP reversal date").FillAsync("2026-08-26");
            await posting.Page.GetByLabel("WIP action reason").FillAsync("Acceptance-test exact reversal");
            await posting.Page.GetByRole(AriaRole.Button, new() { Name = "Confirm reverse", Exact = true }).ClickAsync();
            await posting.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = "Project WIP reverse completed." }).WaitForAsync();
            row = posting.Page.Locator("table[aria-label='Project WIP schedules'] tbody tr").Filter(new() { HasTextString = projectNumber });
            await Assertions.Expect(row).ToContainTextAsync("Reversed");
            await posting.AssertNoUiFailuresAsync("project WIP posting and exact reversal");
        }
    }
}
