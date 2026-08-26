using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E Mutable")]
public sealed class ProjectChangeOrderWorkflowTests(PlaywrightWebAppFixture fixture)
{
    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task ProjectChangeOrder_UsesSeparatedPreparationAndApprovalRoles(BrowserKind browserKind)
    {
        await fixture.CreateProjectChangeOrderUsersAsync();
        var number = $"CO-E2E-{Guid.NewGuid():N}"[..15];

        await using (var preparation = await fixture.CreateSessionAsync(browserKind))
        {
            await preparation.SignInAsync("e2e-project-preparer", returnPath: "/projects");
            await preparation.GotoAsync("/projects");
            await preparation.WaitForHeadingAsync("Track project setup, commitments, costs, and revenue.");
            Assert.Empty(await preparation.Page.Locator("input[aria-label='Project number']").AllAsync());
            await preparation.Page.Locator("select[aria-label='Change order project']").SelectOptionAsync(new SelectOptionValue { Index = 1 });
            await preparation.Page.Locator("input[aria-label='Change order number']").FillAsync(number);
            await preparation.Page.Locator("input[aria-label='Change order description']").FillAsync("Additional field installation");
            await preparation.Page.Locator("input[aria-label='Change order reason']").FillAsync("Customer approved expanded field scope");
            await preparation.Page.Locator("input[aria-label='Change order requested date']").FillAsync("2026-08-26");
            await preparation.Page.Locator("input[aria-label='Change order effective date']").FillAsync("2026-09-01");
            await preparation.Page.Locator("input[aria-label='Change order contract amount']").FillAsync("500");
            await preparation.Page.Locator("input[aria-label='Change order budget amount']").FillAsync("275");
            await preparation.Page.GetByRole(AriaRole.Button, new() { Name = "Save change-order draft", Exact = true }).ClickAsync();
            await preparation.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = $"Change order {number} saved as a draft." }).WaitForAsync();
            var row = preparation.Page.Locator("table[aria-label='Project change orders'] tbody tr").Filter(new() { HasTextString = number });
            await row.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
            await preparation.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = $"Change order {number} submitted for independent review." }).WaitForAsync();
            await preparation.AssertNoUiFailuresAsync("project change-order preparation and submission");
        }

        await using (var approval = await fixture.CreateSessionAsync(browserKind))
        {
            await approval.SignInAsync("e2e-project-approver", returnPath: "/projects");
            await approval.GotoAsync("/projects");
            Assert.Empty(await approval.Page.Locator("input[aria-label='Change order number']").AllAsync());
            var row = approval.Page.Locator("table[aria-label='Project change orders'] tbody tr").Filter(new() { HasTextString = number });
            await row.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
            await approval.Page.Locator("input[aria-label='Change order decision reason']").FillAsync("Customer authorization independently verified");
            await approval.Page.GetByRole(AriaRole.Button, new() { Name = "Approve change order", Exact = true }).ClickAsync();
            await approval.Page.GetByRole(AriaRole.Status).Filter(new() { HasTextString = $"Change order {number} approved." }).WaitForAsync();
            row = approval.Page.Locator("table[aria-label='Project change orders'] tbody tr").Filter(new() { HasTextString = number });
            await Assertions.Expect(row).ToContainTextAsync("Approved");
            await Assertions.Expect(row).ToContainTextAsync("+$500.00");
            await Assertions.Expect(row).ToContainTextAsync("+$275.00");
            await approval.AssertNoUiFailuresAsync("independent project change-order approval");
        }
    }
}
