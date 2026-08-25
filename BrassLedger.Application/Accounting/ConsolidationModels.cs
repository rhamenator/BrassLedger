namespace BrassLedger.Application.Accounting;

public interface IConsolidationService
{
    Task<TransactionResult> SaveExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveGroupAsync(SaveConsolidationGroupRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConsolidationGroupSnapshot>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly asOf, CancellationToken cancellationToken = default);
}

public sealed record SaveExchangeRateRequest(string BaseCurrency, string QuoteCurrency, decimal Rate, DateOnly EffectiveOn, string Source);
public sealed record ConsolidationMemberRequest(Guid CompanyId, decimal OwnershipPercentage = 1m);
public sealed record SaveConsolidationGroupRequest(Guid? Id, string Name, string ReportingCurrency, IReadOnlyList<ConsolidationMemberRequest> Members, bool IsActive = true);
public sealed record ConsolidationGroupSnapshot(Guid Id, string Name, string ReportingCurrency, bool IsActive, IReadOnlyList<ConsolidationGroupMemberSnapshot> Members);
public sealed record ConsolidationGroupMemberSnapshot(Guid CompanyId, string CompanyName, string BaseCurrency, decimal OwnershipPercentage);
public sealed record ConsolidatedBalanceReport(Guid GroupId, string GroupName, string ReportingCurrency, DateOnly AsOf, IReadOnlyList<ConsolidatedAccountBalance> Accounts, IReadOnlyList<string> Warnings);
public sealed record ConsolidatedAccountBalance(string AccountNumber, string AccountName, string AccountType, decimal ConvertedBalance);
