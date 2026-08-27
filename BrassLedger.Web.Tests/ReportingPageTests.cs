using Bunit;
using Bunit.TestDoubles;
using BrassLedger.Application.Accounting;
using BrassLedger.Application.Catalog;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Web.Tests;

public sealed class ReportingPageTests : TestContext
{
    private readonly StubConsolidationService _consolidation = new();

    public ReportingPageTests()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("controller");
        authorization.SetPolicies(BrassLedgerAuthorizationPolicies.ManageReporting, BrassLedgerAuthorizationPolicies.PrepareJournals, BrassLedgerAuthorizationPolicies.ApproveJournals, BrassLedgerAuthorizationPolicies.PostJournals, BrassLedgerAuthorizationPolicies.ReverseJournals);
        Services.AddSingleton<IBusinessWorkspaceService>(new StubBusinessWorkspaceService(TestWorkspaceData.CreateWorkspace()));
        Services.AddSingleton<IProductCatalogService>(new StubProductCatalogService(TestWorkspaceData.CreateAssessment()));
        Services.AddSingleton<IConsolidationService>(_consolidation);
    }

    [Fact]
    public void ReportingPage_ExposesControlledConsolidationEntryAndPreviewWorkflow()
    {
        var cut = RenderComponent<Reporting>();

        Assert.Contains("reporting-only entries", cut.Markup);
        Assert.NotNull(cut.Find("table[aria-label='Consolidation adjustment lines']"));
        Assert.NotNull(cut.Find("table[aria-label='Retained consolidation adjustments']"));
        Assert.Contains("CONSOL-TEST-1", cut.Markup);
        Assert.Contains("Prepared by Preparer", cut.Markup);
        Assert.Equal(2, cut.FindAll("input[aria-label='Adjustment debit']").Count);
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Add line").Click();
        Assert.Equal(3, cut.FindAll("input[aria-label='Adjustment debit']").Count);
        cut.Find("select#adjustmentKind").Change("IntercompanyElimination");
        Assert.NotNull(cut.Find("input#adjustmentMatchReference"));
        Assert.Equal(3, cut.FindAll("select[aria-label='Elimination source company']").Count);
        Assert.Contains("Reviewed intercompany matches", cut.Markup);
        Assert.Contains("IC-INV-1001", cut.Markup);
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Discover exact matches").Click();
        Assert.Equal(1, _consolidation.DiscoveryCount);
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Prepare elimination").Click();
        Assert.Equal("IC-reviewed-reference", cut.Find("input#adjustmentMatchReference").GetAttribute("value"));
        Assert.Contains("No accounting entry has been inferred or posted", cut.Markup);
    }

    [Fact]
    public void ReportingPage_PreparesNciWithControlledSubjectAndExplicitProvenance()
    {
        var cut = RenderComponent<Reporting>();

        cut.Find("select#adjustmentKind").Change("NoncontrollingInterest");
        var subject = cut.Find("select#adjustmentSubjectCompany");
        Assert.Contains("Subsidiary", subject.TextContent);
        Assert.DoesNotContain("Parent company", subject.TextContent);
        Assert.Contains("does not infer acquisition accounting, goodwill, or the NCI amount", cut.Markup);
        subject.Change(StubConsolidationService.SubsidiaryCompanyId.ToString());
        cut.Find("input#adjustmentReference").Change("NCI-COMPONENT-1");
        cut.Find("input#adjustmentDescription").Change("Reviewed component NCI attribution");
        cut.FindAll("select[aria-label='Reporting account']").ToArray()[0].Change("Equity|3000|Retained earnings");
        cut.FindAll("select[aria-label='Reporting account']").ToArray()[1].Change("Equity|39998|Noncontrolling interests");
        cut.FindAll("input[aria-label='Adjustment debit']").ToArray()[0].Change("5.00");
        cut.FindAll("input[aria-label='Adjustment credit']").ToArray()[1].Change("5.00");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Prepare draft").Click();

        var request = Assert.IsType<SaveConsolidationAdjustmentRequest>(_consolidation.LastAdjustmentRequest);
        Assert.Equal("NoncontrollingInterest", request.Kind);
        Assert.Equal(StubConsolidationService.SubsidiaryCompanyId, request.SubjectCompanyId);
        Assert.All(request.Lines, line => Assert.Equal(StubConsolidationService.SubsidiaryCompanyId, line.SourceCompanyId));
        Assert.Contains(request.Lines, line => line.ReportingAccountNumber == "39998" && line.Credit == 5m);
    }

    [Fact]
    public void ReportingPage_RendersStatementPackageWarningsReconciliationAndDrilldown()
    {
        var cut = RenderComponent<Reporting>();

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Run statement package").Click();

        Assert.Contains("North America statement package", cut.Markup);
        Assert.Contains("Incomplete — resolve every warning before external use", cut.Markup);
        Assert.Contains("Cash flow pending classification", cut.Markup);
        Assert.Contains("Consolidated balance sheet", cut.Markup);
        Assert.Contains("Consolidated income statement", cut.Markup);
        Assert.Contains("Consolidated statement of changes in equity", cut.Markup);
        Assert.Contains("Consolidated statement of cash flows", cut.Markup);
        Assert.NotNull(cut.Find("table[aria-label='Consolidated statement reconciliation']"));
        Assert.Contains("1 source contribution(s)", cut.Markup);
        var exportLink = cut.FindAll("a").Single(link => link.TextContent.Trim() == "Download statement package CSV");
        Assert.Contains("/consolidation-groups/70000000-0000-0000-0000-000000000001/statements.csv?periodStart=", exportLink.OuterHtml);
        var excelLink = cut.FindAll("a").Single(link => link.TextContent.Trim() == "Download statement package Excel");
        Assert.Contains("/statements.xlsx?periodStart=", excelLink.OuterHtml);
        Assert.Equal("false", excelLink.GetAttribute("data-enhance-nav"));
        Assert.Contains("/statements.pdf?periodStart=", cut.FindAll("a").Single(link => link.TextContent.Trim() == "Download statement package PDF").OuterHtml);
    }

    [Fact]
    public void ReportingPage_RendersComparativeStatementsWithPeriodSpecificPresentationAndExport()
    {
        var cut = RenderComponent<Reporting>();

        cut.Find("input#comparisonPeriodStart").Change("2025-01-01");
        cut.Find("input#comparisonAsOf").Change("2025-08-31");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Compare statement periods").Click();

        Assert.Contains("North America comparative statements", cut.Markup);
        Assert.Contains("Incomplete — resolve warnings in both periods before external use", cut.Markup);
        Assert.Equal(4, cut.FindAll("table[aria-label$=' comparison']").Count);
        Assert.Contains("Current assets · Cash", cut.Markup);
        Assert.Contains("Prior assets · Prior-period cash", cut.Markup);
        Assert.Contains("Variance", cut.Markup);
        Assert.Contains("current minus comparison", cut.Markup);
        var exportLink = cut.FindAll("a").Single(link => link.TextContent.Trim() == "Download comparative statement CSV");
        Assert.Contains("/statements/comparative.csv?currentPeriodStart=", exportLink.OuterHtml);
        Assert.Contains("comparisonPeriodStart=2025-01-01&amp;comparisonAsOf=2025-08-31", exportLink.OuterHtml);
        Assert.Contains("/statements/comparative.xlsx?currentPeriodStart=", cut.FindAll("a").Single(link => link.TextContent.Trim() == "Download comparative statement Excel").OuterHtml);
        var pdfLink = cut.FindAll("a").Single(link => link.TextContent.Trim() == "Download comparative statement PDF");
        Assert.Contains("/statements/comparative.pdf?currentPeriodStart=", pdfLink.OuterHtml);
        Assert.Equal("false", pdfLink.GetAttribute("data-enhance-nav"));
    }

    [Fact]
    public void ReportingPage_PreparesExtensibleFrameworkDisclosurePackage()
    {
        var cut = RenderComponent<Reporting>();

        Assert.Contains("Framework disclosures", cut.Markup);
        Assert.Contains("versioned JSON document", cut.Markup);
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Add narrative disclosure").Click();
        cut.Find("input[aria-label='Disclosure category']").Change("GoingConcern");
        cut.Find("input[aria-label='Disclosure code']").Change("GC-1");
        cut.Find("input[aria-label='Disclosure title']").Change("Going concern assessment");
        cut.Find("textarea[aria-label='Disclosure narrative']").Change("Management reviewed twelve months of liquidity forecasts.");
        cut.Find("input[aria-label='Disclosure source reference']").Change("Board package WP-9");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Prepare disclosure package").Click();

        var request = Assert.IsType<SaveConsolidationDisclosurePackageRequest>(_consolidation.LastDisclosureRequest);
        Assert.Equal("US-GAAP", request.FrameworkCode);
        var narrative = Assert.Single(request.Content.NarrativeDisclosures);
        Assert.Equal("GoingConcern", narrative.Category);
        Assert.Equal("Board package WP-9", narrative.SourceReference);
    }
}

internal sealed class StubConsolidationService : IConsolidationService
{
    private static readonly Guid GroupId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid ParentId = Guid.Parse("70000000-0000-0000-0000-000000000002");
    internal static readonly Guid SubsidiaryCompanyId = Guid.Parse("70000000-0000-0000-0000-000000000003");
    private static readonly ConsolidationGroupMemberSnapshot[] Members =
    [
        new(Guid.NewGuid(), ParentId, "Parent company", "USD", 1m, DateOnly.MinValue, null, "parent-token", "ReportingParent"),
        new(Guid.NewGuid(), SubsidiaryCompanyId, "Subsidiary", "CAD", .8m, DateOnly.MinValue, null, "subsidiary-token", "ControlledSubsidiary", "Reviewed control evidence", new DateOnly(2026, 1, 1))
    ];
    private static readonly ConsolidationReportingAccountSnapshot[] ReportingAccounts =
    [
        new("1000", "Cash", "Asset"),
        new("3000", "Retained earnings", "Equity"),
        new("39998", "Noncontrolling interests", "Equity")
    ];
    private static readonly ConsolidationAdjustmentSnapshot Draft = new(Guid.Parse("70000000-0000-0000-0000-000000000004"), new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 31), "ManualAdjustment", "CONSOL-TEST-1", "Reporting true-up", string.Empty, "Draft", "Preparer", DateTimeOffset.Parse("2026-08-31T12:00:00Z"), null, null, null, null, null, null, string.Empty, null, null, string.Empty, "draft-token",
    [
        new(Guid.NewGuid(), 1, "1000", "Cash", "Asset", 10m, 0m, "Debit", null, null, null, null),
        new(Guid.NewGuid(), 2, "3000", "Retained earnings", "Equity", 0m, 10m, "Credit", null, null, null, null)
    ]);
    private static readonly ConsolidationIntercompanyMatchSnapshot Match = new(
        Guid.Parse("70000000-0000-0000-0000-000000000005"), ParentId, "Parent company", SubsidiaryCompanyId, "Subsidiary",
        Guid.Parse("70000000-0000-0000-0000-000000000006"), "IC-INV-1001", new DateOnly(2026, 8, 15),
        Guid.Parse("70000000-0000-0000-0000-000000000007"), "ic-inv-1001", new DateOnly(2026, 8, 16),
        "IC-reviewed-reference", "USD", 125m, 125m, 125m, "Suggested", string.Empty, null, null, null, "match-token");

    public int DiscoveryCount { get; private set; }
    public SaveConsolidationAdjustmentRequest? LastAdjustmentRequest { get; private set; }
    public SaveConsolidationDisclosurePackageRequest? LastDisclosureRequest { get; private set; }

    public Task<TransactionResult> SaveExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<IReadOnlyList<ExchangeRateSnapshot>> GetExchangeRatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExchangeRateSnapshot>>([]);
    public Task<TransactionResult> SaveGroupAsync(SaveConsolidationGroupRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? GroupId));
    public Task<TransactionResult> SaveOwnershipPeriodAsync(SaveConsolidationOwnershipPeriodRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SaveAccountMappingAsync(SaveConsolidationAccountMappingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SaveStatementPresentationAsync(SaveConsolidationStatementPresentationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SaveTradingPartnerAsync(SaveConsolidationTradingPartnerRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<ConsolidationTradingPartnerWorkspace?> GetTradingPartnerWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidationTradingPartnerWorkspace?>(null);
    public Task<ConsolidationIntercompanyDiscoveryResult> DiscoverIntercompanyMatchesAsync(DiscoverConsolidationIntercompanyMatchesRequest request, CancellationToken cancellationToken = default) { DiscoveryCount++; return Task.FromResult(new ConsolidationIntercompanyDiscoveryResult(true, string.Empty, 1, 0, [])); }
    public Task<TransactionResult> SetIntercompanyMatchDecisionAsync(SetConsolidationIntercompanyMatchDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.MatchId));
    public Task<ConsolidationIntercompanyMatchWorkspace?> GetIntercompanyMatchWorkspaceAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidationIntercompanyMatchWorkspace?>(new(GroupId, "North America", periodStart, asOf, [Match]));
    public Task<IReadOnlyList<ConsolidationGroupSnapshot>> GetGroupsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConsolidationGroupSnapshot>>([new(GroupId, "North America", "USD", true, "group-token", Members, "39999", "CTA", "39998", "Noncontrolling interests")]);
    public Task<ConsolidationAccountMappingWorkspace?> GetAccountMappingWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidationAccountMappingWorkspace?>(null);
    public Task<ConsolidationStatementPresentationWorkspace?> GetStatementPresentationWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidationStatementPresentationWorkspace?>(null);
    public Task<TransactionResult> SaveDisclosurePackageAsync(SaveConsolidationDisclosurePackageRequest request, CancellationToken cancellationToken = default) { LastDisclosureRequest = request; return Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid())); }
    public Task<TransactionResult> ApproveDisclosurePackageAsync(ConsolidationDisclosureActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.DisclosurePackageId));
    public Task<TransactionResult> RejectDisclosurePackageAsync(ConsolidationDisclosureDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.DisclosurePackageId));
    public Task<ConsolidationDisclosureWorkspace?> GetDisclosureWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidationDisclosureWorkspace?>(new(GroupId, "North America", "USD", []));
    public Task<TransactionResult> SaveAdjustmentAsync(SaveConsolidationAdjustmentRequest request, CancellationToken cancellationToken = default) { LastAdjustmentRequest = request; return Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid())); }
    public Task<TransactionResult> ApproveAdjustmentAsync(ConsolidationAdjustmentActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.AdjustmentBatchId));
    public Task<TransactionResult> RejectAdjustmentAsync(ConsolidationAdjustmentDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.AdjustmentBatchId));
    public Task<TransactionResult> PostAdjustmentAsync(ConsolidationAdjustmentActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.AdjustmentBatchId));
    public Task<TransactionResult> ReverseAdjustmentAsync(ReverseConsolidationAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<ConsolidationAdjustmentWorkspace?> GetAdjustmentWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidationAdjustmentWorkspace?>(new(GroupId, "North America", "USD", ReportingAccounts, Members, [Draft]));
    public Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly asOf, CancellationToken cancellationToken = default) => GetBalanceReportAsync(groupId, new DateOnly(asOf.Year, 1, 1), asOf, cancellationToken);
    public Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidatedBalanceReport?>(new(GroupId, "North America", "USD", periodStart, asOf, [new("1000", "Cash", "Asset", 10m, "Closing"), new("3000", "Retained earnings", "Equity", 10m, "Historical")], [], 0m));
    public Task<ConsolidatedStatementPackage?> GetStatementPackageAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var source = new ConsolidatedAccountContribution(ParentId, "Parent", "1000", "Cash", "MemberLedger", string.Empty, 10m, "Closing");
        var asset = new ConsolidatedStatementAccount("1000", "Cash", "Asset", 10m, [source]);
        var equity = new ConsolidatedStatementAccount("3000", "Retained earnings", "Equity", 10m, []);
        var balance = new ConsolidatedFinancialStatement("BALANCE-SHEET", "Consolidated balance sheet", [new("ASSETS", "Assets", [asset], 10m), new("EQUITY", "Equity", [equity], 10m)], 10m, 0m);
        var income = new ConsolidatedFinancialStatement("INCOME-STATEMENT", "Consolidated income statement", [], 0m, 0m);
        var changes = new ConsolidatedFinancialStatement("EQUITY-STATEMENT", "Consolidated statement of changes in equity", [], 10m, 0m);
        var cash = new ConsolidatedFinancialStatement("CASH-FLOW", "Consolidated statement of cash flows", [], 0m, 0m);
        return Task.FromResult<ConsolidatedStatementPackage?>(new(GroupId, "North America", "USD", periodStart, asOf, balance, income, changes, cash, new(10m, 0m, 10m, 0m, 10m, 0m, 10m, 0m, 10m, 0m, 10m, 10m, 0m, 0m, 0m), ["Cash flow pending classification"], false));
    }
    public Task<string?> ExportStatementPackageCsvAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Record Type,Statement\n");
    public Task<byte[]?> ExportStatementPackageExcelAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>([0x50, 0x4b]);
    public Task<byte[]?> ExportStatementPackagePdfAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>("%PDF"u8.ToArray());
    public async Task<ConsolidatedComparativeStatementPackage?> GetComparativeStatementPackageAsync(Guid groupId, DateOnly currentPeriodStart, DateOnly currentAsOf, DateOnly comparisonPeriodStart, DateOnly comparisonAsOf, CancellationToken cancellationToken = default)
    {
        var current = await GetStatementPackageAsync(groupId, currentPeriodStart, currentAsOf, cancellationToken);
        var comparisonSource = await GetStatementPackageAsync(groupId, comparisonPeriodStart, comparisonAsOf, cancellationToken);
        if (current is null || comparisonSource is null) return null;
        var priorAccount = new ConsolidatedStatementAccount("1000", "Prior-period cash", "Asset", 7m, []);
        var comparison = comparisonSource with { BalanceSheet = new("BALANCE-SHEET", "Consolidated balance sheet", [new("PRIOR-ASSETS", "Prior assets", [priorAccount], 7m)], 7m, 0m) };
        var line = new ConsolidatedComparativeStatementLine("1000", "Asset", "ASSETS", "Current assets", "Cash", 10m, "PRIOR-ASSETS", "Prior assets", "Prior-period cash", 7m, 3m);
        var statements = new[]
        {
            new ConsolidatedComparativeFinancialStatement("BALANCE-SHEET", "Consolidated balance sheet", 10m, 7m, 3m, [line]),
            new ConsolidatedComparativeFinancialStatement("INCOME-STATEMENT", "Consolidated income statement", 0m, 0m, 0m, []),
            new ConsolidatedComparativeFinancialStatement("EQUITY-STATEMENT", "Consolidated statement of changes in equity", 10m, 10m, 0m, []),
            new ConsolidatedComparativeFinancialStatement("CASH-FLOW", "Consolidated statement of cash flows", 0m, 0m, 0m, [])
        };
        return new(GroupId, "North America", "USD", current, comparison, statements, ["Current period: Cash flow pending classification"], false);
    }
    public Task<string?> ExportComparativeStatementPackageCsvAsync(Guid groupId, DateOnly currentPeriodStart, DateOnly currentAsOf, DateOnly comparisonPeriodStart, DateOnly comparisonAsOf, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Record Type,Statement,Current Amount,Comparison Amount,Variance\n");
    public Task<byte[]?> ExportComparativeStatementPackageExcelAsync(Guid groupId, DateOnly currentPeriodStart, DateOnly currentAsOf, DateOnly comparisonPeriodStart, DateOnly comparisonAsOf, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>([0x50, 0x4b]);
    public Task<byte[]?> ExportComparativeStatementPackagePdfAsync(Guid groupId, DateOnly currentPeriodStart, DateOnly currentAsOf, DateOnly comparisonPeriodStart, DateOnly comparisonAsOf, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>("%PDF"u8.ToArray());
}
