namespace BrassLedger.Application.Accounting;

public sealed record SavePayrollDepositScheduleRequest(
    Guid? Id, int TaxYear, string ScheduleType, decimal LookbackLiability,
    DateOnly LookbackPeriodStart, DateOnly LookbackPeriodEnd,
    decimal MonthlyThreshold, decimal NextDayThreshold, string LegalHolidaysJson,
    string OfficialRulesUrl, string OfficialCalendarUrl, DateOnly SourceRetrievedOn,
    string ReviewNotes, bool IsApproved, bool IsActive, string ConcurrencyToken = "");

public sealed record PayrollDepositScheduleSnapshot(
    Guid Id, int TaxYear, string ScheduleType, string RecommendedScheduleType,
    decimal LookbackLiability, DateOnly LookbackPeriodStart, DateOnly LookbackPeriodEnd,
    decimal MonthlyThreshold, decimal NextDayThreshold, IReadOnlyList<DateOnly> LegalHolidays,
    string OfficialRulesUrl, string OfficialCalendarUrl, DateOnly SourceRetrievedOn,
    string ReviewNotes, bool IsApproved, DateTimeOffset? ApprovedAtUtc, bool IsActive,
    string ConcurrencyToken);

public sealed record PayrollDepositDueSummary(
    int TaxYear, string ConfiguredScheduleType, string EffectiveScheduleType,
    decimal OpenAmount, decimal OverdueAmount, DateOnly? NextDueDate,
    int OpenLiabilityCount, int MissingScheduleCount, bool NextDayRuleTriggered);

public sealed record PayrollDepositScheduleWorkspace(
    IReadOnlyList<PayrollDepositScheduleSnapshot> Configurations,
    IReadOnlyList<PayrollDepositDueSummary> Summaries);

public interface IPayrollDepositScheduleService
{
    Task<PayrollDepositScheduleWorkspace> GetAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveAsync(SavePayrollDepositScheduleRequest request, CancellationToken cancellationToken = default);
}
