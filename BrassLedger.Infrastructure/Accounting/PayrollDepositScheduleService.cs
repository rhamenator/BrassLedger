using System.Security.Claims;
using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class PayrollDepositScheduleService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : IPayrollDepositScheduleService
{
    public async Task<PayrollDepositScheduleWorkspace> GetAsync(CancellationToken cancellationToken = default)
    {
        RequirePayrollManagement();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var configurations = await db.PayrollDepositScheduleConfigurations.AsNoTracking()
            .Where(item => item.CompanyId == companyId).OrderByDescending(item => item.TaxYear).ToListAsync(cancellationToken);
        var liabilities = await db.PayrollLiabilities.AsNoTracking().Where(item => item.CompanyId == companyId && item.SourceType == "Tax").ToListAsync(cancellationToken);
        var runs = await db.PayrollRuns.AsNoTracking().Where(item => item.CompanyId == companyId).ToDictionaryAsync(item => item.Id, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var summaries = configurations.Select(configuration =>
        {
            var relevant = liabilities.Where(item => PayrollDepositDueDateCalculator.IsForm941Obligation(item.ObligationCode) && runs.TryGetValue(item.PayrollRunId, out var run) && run.Status == "Posted" && run.PayDate.Year == configuration.TaxYear).ToArray();
            var priorConfiguration = configurations.SingleOrDefault(item => item.TaxYear == configuration.TaxYear - 1 && item.IsActive && item.IsApproved);
            var priorRelevant = priorConfiguration is null ? [] : liabilities.Where(item => PayrollDepositDueDateCalculator.IsForm941Obligation(item.ObligationCode) && runs.TryGetValue(item.PayrollRunId, out var run) && run.Status == "Posted" && run.PayDate.Year == configuration.TaxYear - 1).ToArray();
            var priorYearTrigger = priorConfiguration is not null && PayrollDepositDueDateCalculator.HasNextDayTrigger(priorConfiguration, priorRelevant, runs);
            var open = relevant.Where(item => item.Status is "Open" or "PartiallyPaid").ToArray();
            return new PayrollDepositDueSummary(configuration.TaxYear, configuration.ScheduleType,
                priorYearTrigger ? "Semiweekly (prior-year next-day rule)" : PayrollDepositDueDateCalculator.EffectiveSchedule(configuration, relevant, runs),
                open.Sum(item => item.OutstandingAmount), open.Where(item => item.DueDate < today).Sum(item => item.OutstandingAmount),
                open.Where(item => item.DueDate.HasValue).MinBy(item => item.DueDate)?.DueDate,
                open.Length, open.Count(item => item.DueDate is null),
                PayrollDepositDueDateCalculator.HasNextDayTrigger(configuration, relevant, runs));
        }).ToArray();
        return new PayrollDepositScheduleWorkspace(configurations.Select(ToSnapshot).ToArray(), summaries);
    }

    public async Task<TransactionResult> SaveAsync(SavePayrollDepositScheduleRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollManage) || !HasPermission(BrassLedgerPermissions.PayrollApprove)) return TransactionResult.Failure("You are not authorized to configure and approve payroll deposit schedules.");
        if (request.TaxYear is < 2000 or > 2200) return TransactionResult.Failure("Tax year must be between 2000 and 2200.");
        if (request.ScheduleType is not ("Monthly" or "Semiweekly")) return TransactionResult.Failure("Deposit schedule must be Monthly or Semiweekly.");
        if (request.LookbackLiability < 0 || request.MonthlyThreshold <= 0 || request.NextDayThreshold <= 0) return TransactionResult.Failure("Lookback liability and deposit thresholds must be valid nonnegative amounts.");
        if (request.LookbackPeriodEnd < request.LookbackPeriodStart) return TransactionResult.Failure("The lookback period end cannot precede its start.");
        if (request.LookbackPeriodStart != new DateOnly(request.TaxYear - 2, 7, 1) || request.LookbackPeriodEnd != new DateOnly(request.TaxYear - 1, 6, 30)) return TransactionResult.Failure("A Form 941 lookback period must run from July 1 two years earlier through June 30 of the prior year.");
        if (!TryHolidays(request.LegalHolidaysJson, request.TaxYear, out var holidays)) return TransactionResult.Failure("Legal holidays must be a JSON array of unique ISO dates in the configured tax year.");
        if (!ValidOfficialUrl(request.OfficialRulesUrl) || !ValidOfficialUrl(request.OfficialCalendarUrl)) return TransactionResult.Failure("Approved deposit schedules require HTTPS official rule and calendar sources.");
        var recommended = request.LookbackLiability <= request.MonthlyThreshold ? "Monthly" : "Semiweekly";
        if (request.IsApproved && request.ScheduleType != recommended && string.IsNullOrWhiteSpace(request.ReviewNotes)) return TransactionResult.Failure("Explain why the approved schedule differs from the lookback-based recommendation.");
        if (request.IsApproved && holidays.Count == 0) return TransactionResult.Failure("An approved schedule requires its official legal-holiday calendar.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        PayrollDepositScheduleConfiguration configuration;
        if (request.Id.HasValue)
        {
            configuration = await db.PayrollDepositScheduleConfigurations.SingleOrDefaultAsync(item => item.Id == request.Id && item.CompanyId == companyId, cancellationToken) ?? new();
            if (configuration.Id == Guid.Empty) return TransactionResult.Failure("Payroll deposit schedule not found.");
            if (!string.Equals(configuration.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The deposit schedule changed after it was opened. Refresh and try again.");
        }
        else
        {
            if (await db.PayrollDepositScheduleConfigurations.AnyAsync(item => item.CompanyId == companyId && item.JurisdictionCode == "US" && item.ReturnFormCode == "941" && item.TaxYear == request.TaxYear, cancellationToken)) return TransactionResult.Failure("A federal Form 941 deposit schedule already exists for that tax year.");
            configuration = new PayrollDepositScheduleConfiguration { Id = Guid.NewGuid(), CompanyId = companyId, JurisdictionCode = "US", ReturnFormCode = "941", TaxYear = request.TaxYear };
            db.PayrollDepositScheduleConfigurations.Add(configuration);
        }
        configuration.ScheduleType = request.ScheduleType; configuration.LookbackLiability = request.LookbackLiability;
        configuration.LookbackPeriodStart = request.LookbackPeriodStart; configuration.LookbackPeriodEnd = request.LookbackPeriodEnd;
        configuration.MonthlyThreshold = request.MonthlyThreshold; configuration.NextDayThreshold = request.NextDayThreshold;
        configuration.LegalHolidaysJson = JsonSerializer.Serialize(holidays); configuration.OfficialRulesUrl = request.OfficialRulesUrl.Trim(); configuration.OfficialCalendarUrl = request.OfficialCalendarUrl.Trim();
        configuration.SourceRetrievedOn = request.SourceRetrievedOn; configuration.ReviewNotes = request.ReviewNotes.Trim(); configuration.IsActive = request.IsActive;
        configuration.IsApproved = request.IsApproved; configuration.ApprovedByUserId = request.IsApproved ? ResolveUserId() : null; configuration.ApprovedAtUtc = request.IsApproved ? DateTimeOffset.UtcNow : null;
        configuration.ConcurrencyToken = Guid.NewGuid().ToString("N");
        await PayrollDepositDueDateCalculator.RecalculateYearAsync(db, companyId, configuration, cancellationToken);
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = request.Id.HasValue ? "payroll-deposit-schedule.updated" : "payroll-deposit-schedule.created", EntityType = nameof(PayrollDepositScheduleConfiguration), EntityId = configuration.Id, DetailJson = JsonSerializer.Serialize(new { configuration.TaxYear, configuration.ScheduleType, recommended, configuration.LookbackLiability, configuration.LookbackPeriodStart, configuration.LookbackPeriodEnd, configuration.MonthlyThreshold, configuration.NextDayThreshold, legalHolidays = holidays, configuration.OfficialRulesUrl, configuration.OfficialCalendarUrl, configuration.SourceRetrievedOn, configuration.ReviewNotes, configuration.IsApproved, configuration.ApprovedByUserId, configuration.ApprovedAtUtc, configuration.IsActive }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The deposit schedule changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The deposit schedule conflicts with an existing configuration for that tax year."); }
        return TransactionResult.Success(configuration.Id);
    }

    private static PayrollDepositScheduleSnapshot ToSnapshot(PayrollDepositScheduleConfiguration item) => new(item.Id, item.TaxYear, item.ScheduleType, item.LookbackLiability <= item.MonthlyThreshold ? "Monthly" : "Semiweekly", item.LookbackLiability, item.LookbackPeriodStart, item.LookbackPeriodEnd, item.MonthlyThreshold, item.NextDayThreshold, ParseHolidays(item.LegalHolidaysJson), item.OfficialRulesUrl, item.OfficialCalendarUrl, item.SourceRetrievedOn, item.ReviewNotes, item.IsApproved, item.ApprovedAtUtc, item.IsActive, item.ConcurrencyToken);
    private static IReadOnlyList<DateOnly> ParseHolidays(string json) => TryHolidays(json, null, out var dates) ? dates : [];
    private static bool TryHolidays(string json, int? requiredYear, out IReadOnlyList<DateOnly> dates)
    {
        try { var parsed = JsonSerializer.Deserialize<string[]>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? []; var result = parsed.Select(DateOnly.Parse).Distinct().Order().ToArray(); if (result.Length != parsed.Length || (requiredYear.HasValue && result.Any(date => date.Year != requiredYear))) { dates = []; return false; } dates = result; return true; }
        catch (Exception exception) when (exception is JsonException or FormatException) { dates = []; return false; }
    }
    private static bool ValidOfficialUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && (OfficialHost(uri.Host, "irs.gov") || OfficialHost(uri.Host, "opm.gov"));
    private static bool OfficialHost(string host, string authority) => host.Equals(authority, StringComparison.OrdinalIgnoreCase) || host.EndsWith($".{authority}", StringComparison.OrdinalIgnoreCase);
    private bool HasPermission(string permission) => httpContextAccessor.HttpContext is null || httpContextAccessor.HttpContext.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission);
    private void RequirePayrollManagement() { if (!HasPermission(BrassLedgerPermissions.PayrollManage)) throw new UnauthorizedAccessException("You are not authorized to view payroll deposit schedules."); }
    private Guid? ResolveUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken cancellationToken) { var context = httpContextAccessor.HttpContext; var claim = context?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType); if (context is not null && !Guid.TryParse(claim, out _)) throw new UnauthorizedAccessException("An authenticated company context is required."); if (Guid.TryParse(claim, out var id) && await db.Companies.AnyAsync(item => item.Id == id, cancellationToken)) return id; return await db.Companies.OrderBy(item => item.Name).Select(item => item.Id).FirstAsync(cancellationToken); }
}

internal static class PayrollDepositDueDateCalculator
{
    private static readonly HashSet<string> Form941Obligations = new(StringComparer.OrdinalIgnoreCase) { "US-FIT", "FEDERAL-ADDITIONAL-WITHHOLDING", "US-OASDI-EMPLOYEE", "US-OASDI-EMPLOYER", "US-MEDICARE-EMPLOYEE", "US-MEDICARE-EMPLOYER", "US-ADDITIONAL-MEDICARE" };
    internal static bool IsForm941Obligation(string code) => Form941Obligations.Contains(code);

    internal static async Task RecalculateYearAsync(BrassLedgerDbContext db, Guid companyId, PayrollDepositScheduleConfiguration configuration, CancellationToken cancellationToken)
    {
        var runs = await db.PayrollRuns.Where(item => item.CompanyId == companyId && item.Status == "Posted" && (item.PayDate.Year == configuration.TaxYear || item.PayDate.Year == configuration.TaxYear - 1)).ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var tracked in db.PayrollRuns.Local.Where(item => item.CompanyId == companyId && item.Status == "Posted" && (item.PayDate.Year == configuration.TaxYear || item.PayDate.Year == configuration.TaxYear - 1))) runs[tracked.Id] = tracked;
        var runIds = runs.Keys.ToArray();
        var liabilities = runIds.Length == 0 ? [] : await db.PayrollLiabilities.Where(item => item.CompanyId == companyId && item.SourceType == "Tax" && runIds.Contains(item.PayrollRunId)).ToListAsync(cancellationToken);
        foreach (var added in db.PayrollLiabilities.Local.Where(item => item.CompanyId == companyId && item.SourceType == "Tax" && runIds.Contains(item.PayrollRunId) && !liabilities.Contains(item))) liabilities.Add(added);
        var relevant = liabilities.Where(item => IsForm941Obligation(item.ObligationCode) && runs[item.PayrollRunId].PayDate.Year == configuration.TaxYear).ToArray();
        if (!configuration.IsActive || !configuration.IsApproved) { foreach (var liability in relevant.Where(item => item.Status is "Open" or "PartiallyPaid")) liability.DueDate = null; return; }
        var holidays = JsonSerializer.Deserialize<string[]>(configuration.LegalHolidaysJson)?.Select(DateOnly.Parse).ToHashSet() ?? [];
        var priorYearLiabilities = liabilities.Where(item => IsForm941Obligation(item.ObligationCode) && runs[item.PayrollRunId].PayDate.Year == configuration.TaxYear - 1).ToArray();
        var priorYearConfiguration = await db.PayrollDepositScheduleConfigurations.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == companyId && item.JurisdictionCode == "US" && item.ReturnFormCode == "941" && item.TaxYear == configuration.TaxYear - 1 && item.IsActive && item.IsApproved, cancellationToken);
        var priorYearTriggered = priorYearConfiguration is not null && HasNextDayTrigger(priorYearConfiguration, priorYearLiabilities, runs);
        var schedule = priorYearTriggered ? "Semiweekly" : configuration.ScheduleType;
        var dayGroups = relevant.GroupBy(item => runs[item.PayrollRunId].PayDate).OrderBy(group => group.Key).ToArray();
        var periodAmounts = new Dictionary<string, decimal>(); var daysInPeriod = new Dictionary<string, List<DateOnly>>(); var dueByDay = new Dictionary<DateOnly, DateOnly>();
        foreach (var day in dayGroups)
        {
            var periodKey = PeriodKey(day.Key, schedule); periodAmounts[periodKey] = periodAmounts.GetValueOrDefault(periodKey) + day.Sum(item => item.OriginalAmount);
            if (!daysInPeriod.TryGetValue(periodKey, out var periodDays)) { periodDays = []; daysInPeriod[periodKey] = periodDays; } periodDays.Add(day.Key);
            if (periodAmounts[periodKey] >= configuration.NextDayThreshold)
            {
                var due = AddBusinessDays(day.Key, 1, holidays); foreach (var payDate in periodDays) dueByDay[payDate] = due; schedule = "Semiweekly"; periodAmounts.Clear(); daysInPeriod.Clear();
            }
            else if (!dueByDay.ContainsKey(day.Key)) dueByDay[day.Key] = schedule == "Monthly" ? NextBusinessDay(new DateOnly(day.Key.AddMonths(1).Year, day.Key.AddMonths(1).Month, 15), holidays) : AddBusinessDays(SemiweeklyPeriodEnd(day.Key), 3, holidays);
        }
        foreach (var liability in relevant.Where(item => item.Status is "Open" or "PartiallyPaid")) liability.DueDate = dueByDay[runs[liability.PayrollRunId].PayDate];
    }

    internal static bool HasNextDayTrigger(PayrollDepositScheduleConfiguration configuration, IReadOnlyList<PayrollLiability> liabilities, IReadOnlyDictionary<Guid, PayrollRun> runs)
    {
        var periodAmounts = new Dictionary<string, decimal>();
        foreach (var day in liabilities.GroupBy(item => runs[item.PayrollRunId].PayDate).OrderBy(group => group.Key))
        {
            var periodKey = PeriodKey(day.Key, configuration.ScheduleType);
            periodAmounts[periodKey] = periodAmounts.GetValueOrDefault(periodKey) + day.Sum(item => item.OriginalAmount);
            if (periodAmounts[periodKey] >= configuration.NextDayThreshold) return true;
        }
        return false;
    }
    internal static string EffectiveSchedule(PayrollDepositScheduleConfiguration configuration, IReadOnlyList<PayrollLiability> liabilities, IReadOnlyDictionary<Guid, PayrollRun> runs) => HasNextDayTrigger(configuration, liabilities, runs) ? "Semiweekly (next-day rule triggered)" : configuration.ScheduleType;
    private static string PeriodKey(DateOnly date, string schedule) => schedule == "Monthly" ? $"M:{date:yyyyMM}" : $"S:{SemiweeklyPeriodEnd(date):yyyyMMdd}";
    private static DateOnly SemiweeklyPeriodEnd(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday or DayOfWeek.Monday or DayOfWeek.Tuesday ? date.AddDays(((int)DayOfWeek.Tuesday - (int)date.DayOfWeek + 7) % 7) : date.AddDays(((int)DayOfWeek.Friday - (int)date.DayOfWeek + 7) % 7);
    private static DateOnly AddBusinessDays(DateOnly date, int days, IReadOnlySet<DateOnly> holidays) { var result = date; while (days > 0) { result = result.AddDays(1); if (IsBusinessDay(result, holidays)) days--; } return result; }
    private static DateOnly NextBusinessDay(DateOnly date, IReadOnlySet<DateOnly> holidays) { while (!IsBusinessDay(date, holidays)) date = date.AddDays(1); return date; }
    private static bool IsBusinessDay(DateOnly date, IReadOnlySet<DateOnly> holidays) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date);
}
