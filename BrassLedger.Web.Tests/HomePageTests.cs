using Bunit;
using BrassLedger.Application.Accounting;
using BrassLedger.Application.Modernization;
using BrassLedger.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Web.Tests;

public sealed class HomePageTests : TestContext
{
    public HomePageTests()
    {
        Services.AddSingleton<IBusinessWorkspaceService>(new StubBusinessWorkspaceService(TestWorkspaceData.CreateWorkspace()));
        Services.AddSingleton<IModernizationAssessmentService>(new StubModernizationAssessmentService(TestWorkspaceData.CreateAssessment()));
    }

    [Fact]
    public void HomePage_RendersLiveWorkspaceSummary()
    {
        var cut = RenderComponent<Home>();

        Assert.Contains("Brass Ledger Manufacturing is live as a real multi-module workspace.", cut.Markup);
        Assert.Contains("Open ledger", cut.Markup);
        Assert.Contains("112,540", cut.Markup);
        Assert.Contains("Reports ready", cut.Markup);
    }
}
