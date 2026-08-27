namespace BrassLedger.Application.Accounting;

public interface IConsolidationService
{
    Task<TransactionResult> SaveExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExchangeRateSnapshot>> GetExchangeRatesAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveGroupAsync(SaveConsolidationGroupRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveOwnershipPeriodAsync(SaveConsolidationOwnershipPeriodRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveAccountMappingAsync(SaveConsolidationAccountMappingRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConsolidationGroupSnapshot>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<ConsolidationAccountMappingWorkspace?> GetAccountMappingWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly asOf, CancellationToken cancellationToken = default);
    Task<ConsolidatedBalanceReport?> GetBalanceReportAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default);
}

public sealed record SaveExchangeRateRequest(string BaseCurrency, string QuoteCurrency, decimal Rate, DateOnly EffectiveOn, string Source, Guid? Id = null, string RateType = "Closing", DateOnly? PeriodStartOn = null, string SourceReference = "", DateOnly? RetrievedOn = null, bool IsActive = true, string ConcurrencyToken = "");
public sealed record ExchangeRateSnapshot(Guid Id, string BaseCurrency, string QuoteCurrency, decimal Rate, string RateType, DateOnly? PeriodStartOn, DateOnly EffectiveOn, string Source, string SourceReference, DateOnly? RetrievedOn, bool IsActive, string ConcurrencyToken);
public sealed record ConsolidationMemberRequest(Guid CompanyId, decimal OwnershipPercentage = 1m, DateOnly? EffectiveFrom = null, DateOnly? EffectiveThrough = null);
public sealed record SaveConsolidationGroupRequest(Guid? Id, string Name, string ReportingCurrency, IReadOnlyList<ConsolidationMemberRequest> Members, bool IsActive = true, string ConcurrencyToken = "", string CtaAccountNumber = "", string CtaAccountName = "");
public sealed record SaveConsolidationOwnershipPeriodRequest(Guid? Id, Guid ConsolidationGroupId, Guid MemberCompanyId, decimal OwnershipPercentage, DateOnly EffectiveFrom, DateOnly? EffectiveThrough, string ConcurrencyToken = "");
public sealed record ConsolidationGroupSnapshot(Guid Id, string Name, string ReportingCurrency, bool IsActive, string ConcurrencyToken, IReadOnlyList<ConsolidationGroupMemberSnapshot> Members, string CtaAccountNumber, string CtaAccountName);
public sealed record ConsolidationGroupMemberSnapshot(Guid Id, Guid CompanyId, string CompanyName, string BaseCurrency, decimal OwnershipPercentage, DateOnly EffectiveFrom, DateOnly? EffectiveThrough, string ConcurrencyToken);
public sealed record SaveConsolidationAccountMappingRequest(Guid? Id, Guid ConsolidationGroupId, Guid MemberCompanyId, Guid MemberAccountId, string ReportingAccountNumber, string ReportingAccountName, DateOnly EffectiveFrom, DateOnly? EffectiveThrough, bool IsActive = true, string ConcurrencyToken = "", string? TranslationMethod = null);
public sealed record ConsolidationSourceAccountSnapshot(Guid CompanyId, string CompanyName, Guid AccountId, string AccountNumber, string AccountName, string AccountType);
public sealed record ConsolidationAccountMappingSnapshot(Guid Id, Guid CompanyId, string CompanyName, Guid AccountId, string AccountNumber, string AccountName, string AccountType, string ReportingAccountNumber, string ReportingAccountName, string ReportingAccountType, string TranslationMethod, DateOnly EffectiveFrom, DateOnly? EffectiveThrough, bool IsActive, string ConcurrencyToken);
public sealed record ConsolidationAccountMappingWorkspace(Guid GroupId, string GroupName, IReadOnlyList<ConsolidationSourceAccountSnapshot> SourceAccounts, IReadOnlyList<ConsolidationAccountMappingSnapshot> Mappings);
public sealed record ConsolidatedBalanceReport(Guid GroupId, string GroupName, string ReportingCurrency, DateOnly PeriodStart, DateOnly AsOf, IReadOnlyList<ConsolidatedAccountBalance> Accounts, IReadOnlyList<string> Warnings, decimal TranslationAdjustment);
public sealed record ConsolidatedAccountBalance(string AccountNumber, string AccountName, string AccountType, decimal ConvertedBalance, string TranslationMethod = "");
