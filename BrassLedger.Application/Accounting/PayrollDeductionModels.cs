namespace BrassLedger.Application.Accounting;

public sealed record SavePayrollDeductionPlanRequest(
    Guid? Id,
    string Code,
    string Name,
    string Category,
    string CalculationMethod,
    decimal DefaultEmployeeValue,
    decimal DefaultEmployerValue,
    bool IsPreTax,
    bool ExemptFromFederalIncomeTax,
    bool ExemptFromFica,
    bool ExemptFromFuta,
    bool ReducesDisposableEarnings,
    string LiabilityAccountNumber,
    int Priority,
    decimal? EmployeeLimitPerPay,
    decimal? EmployeeAnnualLimit,
    decimal MinimumNetPay,
    string LimitRuleCode,
    string LimitRuleJson,
    string OfficialSourceUrl,
    DateOnly? SourceRetrievedOn,
    DateOnly EffectiveOn,
    DateOnly? ExpiresOn,
    bool IsActive,
    string ConcurrencyToken = "");

public sealed record SaveEmployeePayrollDeductionElectionRequest(
    Guid? Id,
    Guid EmployeeId,
    Guid PayrollDeductionPlanId,
    decimal? EmployeeValueOverride,
    decimal? EmployerValueOverride,
    decimal? EmployeeAnnualLimitOverride,
    string OrderDetailsJson,
    DateOnly EffectiveOn,
    DateOnly? ExpiresOn,
    bool IsActive,
    string ConcurrencyToken = "");

public sealed record PayrollDeductionPlanSnapshot(
    Guid Id, string Code, string Name, string Category, string CalculationMethod,
    decimal DefaultEmployeeValue, decimal DefaultEmployerValue, bool IsPreTax,
    bool ExemptFromFederalIncomeTax, bool ExemptFromFica, bool ExemptFromFuta,
    bool ReducesDisposableEarnings, string LiabilityAccountNumber, int Priority,
    decimal? EmployeeLimitPerPay, decimal? EmployeeAnnualLimit, decimal MinimumNetPay,
    string LimitRuleCode, string LimitRuleJson, string OfficialSourceUrl,
    DateOnly? SourceRetrievedOn, DateOnly EffectiveOn, DateOnly? ExpiresOn,
    bool IsActive, string ConcurrencyToken);

public sealed record EmployeePayrollDeductionElectionSnapshot(
    Guid Id, Guid EmployeeId, Guid PayrollDeductionPlanId, string PlanCode, string PlanName,
    decimal? EmployeeValueOverride, decimal? EmployerValueOverride,
    decimal? EmployeeAnnualLimitOverride, string OrderDetailsJson,
    DateOnly EffectiveOn, DateOnly? ExpiresOn, bool IsActive, string ConcurrencyToken);

public sealed record PayrollDeductionConfiguration(
    IReadOnlyList<PayrollDeductionPlanSnapshot> Plans,
    IReadOnlyList<EmployeePayrollDeductionElectionSnapshot> Elections);

public interface IPayrollDeductionConfigurationService
{
    Task<PayrollDeductionConfiguration> GetAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SavePlanAsync(SavePayrollDeductionPlanRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveElectionAsync(SaveEmployeePayrollDeductionElectionRequest request, CancellationToken cancellationToken = default);
}
