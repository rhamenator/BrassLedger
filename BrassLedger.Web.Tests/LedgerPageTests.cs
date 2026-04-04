using Bunit;
using BrassLedger.Application.Accounting;
using BrassLedger.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Web.Tests;

public sealed class LedgerPageTests : TestContext
{
    public LedgerPageTests()
    {
        Services.AddSingleton<IBusinessWorkspaceService>(new StubBusinessWorkspaceService(TestWorkspaceData.CreateWorkspace()));
    }

    [Fact]
    public void LedgerPage_RendersAccountAndJournalData()
    {
        var cut = RenderComponent<Ledger>();

        Assert.Contains("Operating Cash", cut.Markup);
        Assert.Contains("JE-2401", cut.Markup);
        Assert.Contains("Primary Operating", cut.Markup);
    }
}
