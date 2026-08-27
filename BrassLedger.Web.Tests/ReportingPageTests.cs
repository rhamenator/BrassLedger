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
    public ReportingPageTests()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("controller");
        authorization.SetPolicies(BrassLedgerAuthorizationPolicies.ManageReporting, BrassLedgerAuthorizationPolicies.PrepareJournals, BrassLedgerAuthorizationPolicies.ApproveJournals, BrassLedgerAuthorizationPolicies.PostJournals, BrassLedgerAuthorizationPolicies.ReverseJournals);
        Services.AddSingleton<IBusinessWorkspaceService>(new StubBusinessWorkspaceService(TestWorkspaceData.CreateWorkspace()));
        Services.AddSingleton<IProductCatalogService>(new StubProductCatalogService(TestWorkspaceData.CreateAssessment()));
        Services.AddSingleton<IConsolidationService>(new StubConsolidationService());
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
    }
}

internal sealed class StubConsolidationService : IConsolidationService
{
    private static readonly Guid GroupId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid ParentId = Guid.Parse("70000000-0000-0000-0000-000000000002");
    private static readonly Guid SubsidiaryId = Guid.Parse("70000000-0000-0000-0000-000000000003");
    private static readonly ConsolidationGroupMemberSnapshot[] Members =
    [
        new(Guid.NewGuid(), ParentId, "Parent company", "USD", 1m, DateOnly.MinValue, null, "parent-token"),
        new(Guid.NewGuid(), SubsidiaryId, "Subsidiary", "CAD", .8m, DateOnly.MinValue, null, "subsidiary-token")
    ];
    private static readonly ConsolidationReportingAccountSnapshot[] ReportingAccounts =
    [
        new("1000", "Cash", "Asset"),
        new("3000", "Retained earnings", "Equity")
    ];
    private static readonly ConsolidationAdjustmentSnapshot Draft = new(Guid.Parse("70000000-0000-0000-0000-000000000004"), new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 31), "ManualAdjustment", "CONSOL-TEST-1", "Reporting true-up", string.Empty, "Draft", "Preparer", DateTimeOffset.Parse("2026-08-31T12:00:00Z"), null, null, null, null, null, null, string.Empty, null, null, string.Empty, "draft-token",
    [
        new(Guid.NewGuid(), 1, "1000", "Cash", "Asset", 10m, 0m, "Debit", null, null, null, null),
        new(Guid.NewGuid(), 2, "3000", "Retained earnings", "Equity", 0m, 10m, "Credit", null, null, null, null)
    ]);

    public Task<TransactionResult> SaveExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<IReadOnlyList<ExchangeRateSnapshot>> GetExchangeRatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExchangeRateSnapshot>>([]);
    public Task<TransactionResult> SaveGroupAsync(SaveConsolidationGroupRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? GroupId));
    public Task<TransactionResult> SaveOwnershipPeriodAsync(SaveConsolidationOwnershipPeriodRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> SaveAccountMappingAsync(SaveConsolidationAccountMappingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<IReadOnlyList<ConsolidationGroupSnapshot>> GetGroupsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConsolidationGroupSnapshot>>([new(GroupId, "North America", "USD", true, "group-token", Members, "39999", "CTA")]);
    public Task<ConsolidationAccountMappingWorkspace?> GetAccountMappingWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidationAccountMappingWorkspace?>(null);
    public Task<TransactionResult> SaveAdjustmentAsync(SaveConsolidationAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.Id ?? Guid.NewGuid()));
    public Task<TransactionResult> ApproveAdjustmentAsync(ConsolidationAdjustmentActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.AdjustmentBatchId));
    public Task<TransactionResult> RejectAdjustmentAsync(ConsolidationAdjustmentDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.AdjustmentBatchId));
    public Task<TransactionResult> PostAdjustmentAsync(ConsolidationAdjustmentActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(request.AdjustmentBatchId));
    public Task<TransactionResult> ReverseAdjustmentAsync(ReverseConsolidationAdjustmentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(TransactionResult.Success(Guid.NewGuid()));
    public Task<ConsolidationAdjustmentWorkspace?> GetAdjustmentWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidationAdjustmentWorkspace?>(new(GroupId, "North America", "USD", ReportingAccounts, Members, [Draft]));
    public Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly asOf, CancellationToken cancellationToken = default) => GetBalanceReportAsync(groupId, new DateOnly(asOf.Year, 1, 1), asOf, cancellationToken);
    public Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default) => Task.FromResult<ConsolidatedBalanceReport?>(new(GroupId, "North America", "USD", periodStart, asOf, [new("1000", "Cash", "Asset", 10m, "Closing"), new("3000", "Retained earnings", "Equity", 10m, "Historical")], [], 0m));
}
