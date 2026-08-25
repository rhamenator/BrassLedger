using System.Security.Claims;
using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class PayrollDeductionConfigurationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : IPayrollDeductionConfigurationService
{
    public static readonly string[] Categories =
    [
        "Medical", "Dental", "Vision", "HSA", "HealthFSA", "DependentCareFSA",
        "Retirement401k", "Retirement403b", "Retirement457", "RetirementSimpleIra",
        "RetirementSepIra", "RetirementRoth", "LifeInsurance", "DisabilityInsurance",
        "Transit", "Parking", "UnionDues", "CharitableContribution", "EmployeeLoan",
        "ChildSupport", "Alimony", "OrdinaryGarnishment", "TaxLevy", "BankruptcyOrder",
        "FederalAgencyDebt", "PtoPurchase", "Other"
    ];

    public async Task<PayrollDeductionConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        RequireProtectedPayrollMaintenance();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var plans = await db.PayrollDeductionPlans.AsNoTracking().Where(item => item.CompanyId == companyId).OrderBy(item => item.Priority).ThenBy(item => item.Code).ToListAsync(cancellationToken);
        var planById = plans.ToDictionary(item => item.Id);
        var elections = await db.EmployeePayrollDeductionElections.AsNoTracking().Where(item => item.CompanyId == companyId).OrderBy(item => item.EmployeeId).ThenBy(item => item.EffectiveOn).ToListAsync(cancellationToken);
        return new PayrollDeductionConfiguration(
            plans.Select(item => new PayrollDeductionPlanSnapshot(item.Id, item.Code, item.Name, item.Category, item.CalculationMethod, item.DefaultEmployeeValue, item.DefaultEmployerValue, item.IsPreTax, item.ExemptFromFederalIncomeTax, item.ExemptFromFica, item.ExemptFromFuta, item.ReducesDisposableEarnings, item.LiabilityAccountNumber, item.Priority, item.EmployeeLimitPerPay, item.EmployeeAnnualLimit, item.MinimumNetPay, item.LimitRuleCode, item.LimitRuleJson, item.OfficialSourceUrl, item.SourceRetrievedOn, item.EffectiveOn, item.ExpiresOn, item.IsActive, item.ConcurrencyToken)).ToArray(),
            elections.Where(item => planById.ContainsKey(item.PayrollDeductionPlanId)).Select(item =>
            {
                var plan = planById[item.PayrollDeductionPlanId];
                return new EmployeePayrollDeductionElectionSnapshot(item.Id, item.EmployeeId, item.PayrollDeductionPlanId, plan.Code, plan.Name, item.EmployeeValueOverride, item.EmployerValueOverride, item.EmployeeAnnualLimitOverride, item.OrderDetailsJson, item.EffectiveOn, item.ExpiresOn, item.IsActive, item.ConcurrencyToken);
            }).ToArray());
    }

    public async Task<TransactionResult> SavePlanAsync(SavePayrollDeductionPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollManage)) return TransactionResult.Failure("You are not authorized to maintain payroll deduction plans.");
        var validation = ValidatePlan(request);
        if (validation.Length > 0) return TransactionResult.Failure(validation);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var liabilityAccount = request.LiabilityAccountNumber.Trim();
        if (!await db.Accounts.AnyAsync(item => item.CompanyId == companyId && item.Number == liabilityAccount && item.Type == AccountType.Liability && item.IsActive, cancellationToken))
            return TransactionResult.Failure("The deduction plan requires an active liability account in this company.");
        PayrollDeductionPlan plan;
        if (request.Id.HasValue)
        {
            plan = await db.PayrollDeductionPlans.SingleOrDefaultAsync(item => item.Id == request.Id && item.CompanyId == companyId, cancellationToken) ?? new PayrollDeductionPlan();
            if (plan.Id == Guid.Empty) return TransactionResult.Failure("Payroll deduction plan not found.");
            if (!string.Equals(plan.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The deduction plan changed after it was opened. Refresh and try again.");
        }
        else
        {
            plan = new PayrollDeductionPlan { Id = Guid.NewGuid(), CompanyId = companyId };
            db.PayrollDeductionPlans.Add(plan);
        }
        if (await db.PayrollDeductionPlans.AnyAsync(item => item.CompanyId == companyId && item.Id != plan.Id && item.Code == request.Code.Trim().ToUpperInvariant(), cancellationToken)) return TransactionResult.Failure("A payroll deduction plan with that code already exists.");
        plan.Code = request.Code.Trim().ToUpperInvariant(); plan.Name = request.Name.Trim(); plan.Category = request.Category.Trim(); plan.CalculationMethod = request.CalculationMethod.Trim();
        plan.DefaultEmployeeValue = request.DefaultEmployeeValue; plan.DefaultEmployerValue = request.DefaultEmployerValue; plan.IsPreTax = request.IsPreTax;
        plan.ExemptFromFederalIncomeTax = request.ExemptFromFederalIncomeTax; plan.ExemptFromFica = request.ExemptFromFica; plan.ExemptFromFuta = request.ExemptFromFuta; plan.ReducesDisposableEarnings = request.ReducesDisposableEarnings;
        plan.LiabilityAccountNumber = liabilityAccount; plan.Priority = request.Priority; plan.EmployeeLimitPerPay = request.EmployeeLimitPerPay; plan.EmployeeAnnualLimit = request.EmployeeAnnualLimit; plan.MinimumNetPay = request.MinimumNetPay;
        plan.LimitRuleCode = request.LimitRuleCode.Trim(); plan.LimitRuleJson = NormalizeJson(request.LimitRuleJson); plan.OfficialSourceUrl = request.OfficialSourceUrl.Trim(); plan.SourceRetrievedOn = request.SourceRetrievedOn;
        plan.EffectiveOn = request.EffectiveOn; plan.ExpiresOn = request.ExpiresOn; plan.IsActive = request.IsActive; plan.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, request.Id.HasValue ? "payroll-deduction-plan.updated" : "payroll-deduction-plan.created", "PayrollDeductionPlan", plan.Id, new { plan.Code, plan.Name, plan.Category, plan.CalculationMethod, plan.IsPreTax, plan.LimitRuleCode, plan.EffectiveOn, plan.ExpiresOn, plan.IsActive });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The deduction plan changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The deduction plan could not be saved because its code or configuration conflicts with another record."); }
        return TransactionResult.Success(plan.Id);
    }

    public async Task<TransactionResult> SaveElectionAsync(SaveEmployeePayrollDeductionElectionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollManage) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to maintain protected employee deduction elections.");
        if (request.EmployeeId == Guid.Empty || request.PayrollDeductionPlanId == Guid.Empty) return TransactionResult.Failure("Select an employee and deduction plan.");
        if (request.EmployeeValueOverride is < 0 || request.EmployerValueOverride is < 0 || request.EmployeeAnnualLimitOverride is < 0) return TransactionResult.Failure("Deduction election values and limits cannot be negative.");
        if (request.ExpiresOn < request.EffectiveOn) return TransactionResult.Failure("The election expiration date cannot precede its effective date.");
        if (!TryNormalizeJson(request.OrderDetailsJson, out var orderDetails)) return TransactionResult.Failure("Order details must be a valid JSON object.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.Employees.AnyAsync(item => item.Id == request.EmployeeId && item.CompanyId == companyId, cancellationToken)) return TransactionResult.Failure("Employee not found in the active company.");
        var plan = await db.PayrollDeductionPlans.SingleOrDefaultAsync(item => item.Id == request.PayrollDeductionPlanId && item.CompanyId == companyId, cancellationToken);
        if (plan is null) return TransactionResult.Failure("Deduction plan not found in the active company.");
        if (plan.CalculationMethod != "Fixed" && (request.EmployeeValueOverride > 1 || request.EmployerValueOverride > 1)) return TransactionResult.Failure("Percentage election overrides must be decimal rates between zero and one.");
        EmployeePayrollDeductionElection election;
        if (request.Id.HasValue)
        {
            election = await db.EmployeePayrollDeductionElections.SingleOrDefaultAsync(item => item.Id == request.Id && item.CompanyId == companyId, cancellationToken) ?? new EmployeePayrollDeductionElection();
            if (election.Id == Guid.Empty) return TransactionResult.Failure("Employee deduction election not found.");
            if (!string.Equals(election.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The employee deduction election changed after it was opened. Refresh and try again.");
        }
        else
        {
            election = new EmployeePayrollDeductionElection { Id = Guid.NewGuid(), CompanyId = companyId };
            db.EmployeePayrollDeductionElections.Add(election);
        }
        var newEnd = request.ExpiresOn ?? DateOnly.MaxValue;
        if (await db.EmployeePayrollDeductionElections.AnyAsync(item => item.CompanyId == companyId && item.Id != election.Id && item.EmployeeId == request.EmployeeId && item.PayrollDeductionPlanId == request.PayrollDeductionPlanId && item.IsActive && request.IsActive && item.EffectiveOn <= newEnd && (item.ExpiresOn == null || item.ExpiresOn >= request.EffectiveOn), cancellationToken))
            return TransactionResult.Failure("This employee already has an active, overlapping election for the selected plan.");
        election.EmployeeId = request.EmployeeId; election.PayrollDeductionPlanId = request.PayrollDeductionPlanId; election.EmployeeValueOverride = request.EmployeeValueOverride; election.EmployerValueOverride = request.EmployerValueOverride; election.EmployeeAnnualLimitOverride = request.EmployeeAnnualLimitOverride;
        election.OrderDetailsJson = orderDetails; election.EffectiveOn = request.EffectiveOn; election.ExpiresOn = request.ExpiresOn; election.IsActive = request.IsActive; election.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, request.Id.HasValue ? "employee-payroll-deduction-election.updated" : "employee-payroll-deduction-election.created", "EmployeePayrollDeductionElection", election.Id, new { election.EmployeeId, plan.Code, election.EffectiveOn, election.ExpiresOn, election.IsActive, hasOrderDetails = election.OrderDetailsJson != "{}" });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The employee deduction election changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The employee deduction election conflicts with an existing effective-dated record."); }
        return TransactionResult.Success(election.Id);
    }

    private static string ValidatePlan(SavePayrollDeductionPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return "A deduction plan code and name are required.";
        if (!Categories.Contains(request.Category, StringComparer.Ordinal)) return "Select a supported deduction plan category.";
        if (request.CalculationMethod is not ("Fixed" or "PercentGross" or "PercentDisposable")) return "Calculation method must be Fixed, PercentGross, or PercentDisposable.";
        if (request.IsPreTax && request.CalculationMethod == "PercentDisposable") return "A pretax plan cannot use disposable earnings because taxes have not yet been calculated.";
        if (!request.IsPreTax && (request.ExemptFromFederalIncomeTax || request.ExemptFromFica || request.ExemptFromFuta)) return "Tax exemptions apply only to pretax deductions calculated before withholding.";
        if (request.DefaultEmployeeValue < 0 || request.DefaultEmployerValue < 0 || request.EmployeeLimitPerPay is < 0 || request.EmployeeAnnualLimit is < 0 || request.MinimumNetPay < 0) return "Plan values and limits cannot be negative.";
        if (request.CalculationMethod != "Fixed" && (request.DefaultEmployeeValue > 1 || request.DefaultEmployerValue > 1)) return "Percentage plan values must be decimal rates between zero and one.";
        if (request.ExpiresOn < request.EffectiveOn) return "The plan expiration date cannot precede its effective date.";
        if (request.Priority < 0 || request.Priority > 100000) return "Plan priority must be between 0 and 100000.";
        if (request.LimitRuleCode is not ("None" or "OrdinaryGarnishmentFederal" or "ChildSupportFederal" or "ConfiguredDisposablePercent" or "NoCcpaLimit")) return "Select a supported legal limit rule.";
        if (request.IsPreTax && request.LimitRuleCode != "None") return "Court-order and disposable-earnings legal limits must be applied after legally required withholding, not as pretax deductions.";
        if (!TryNormalizeJson(request.LimitRuleJson, out var normalizedRule)) return "Legal limit configuration must be a valid JSON object.";
        if (request.LimitRuleCode == "ConfiguredDisposablePercent" && (!TryJsonDecimal(normalizedRule, "maxDisposablePercent", out var configuredPercent) || configuredPercent is <= 0 or > 1)) return "ConfiguredDisposablePercent requires maxDisposablePercent greater than zero and no more than one.";
        if (TryJsonDecimal(normalizedRule, "maxDisposablePercent", out var maxPercent) && maxPercent is < 0 or > 1) return "maxDisposablePercent must be between zero and one.";
        if (TryJsonDecimal(normalizedRule, "protectedMinimumHourlyRate", out var hourlyRate) && hourlyRate < 0) return "protectedMinimumHourlyRate cannot be negative.";
        if (TryJsonDecimal(normalizedRule, "protectedHoursPerWeek", out var protectedHours) && protectedHours < 0) return "protectedHoursPerWeek cannot be negative.";
        if ((request.LimitRuleCode != "None" || request.ReducesDisposableEarnings) && (!Uri.TryCreate(request.OfficialSourceUrl, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps || request.SourceRetrievedOn is null)) return "A dated HTTPS official source is required for legal-limit or disposable-earnings rules.";
        return string.Empty;
    }

    private static string NormalizeJson(string json) => TryNormalizeJson(json, out var normalized) ? normalized : "{}";
    private static bool TryNormalizeJson(string json, out string normalized)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) { normalized = string.Empty; return false; }
            normalized = JsonSerializer.Serialize(document.RootElement); return true;
        }
        catch (JsonException) { normalized = string.Empty; return false; }
    }

    private static bool TryJsonDecimal(string json, string propertyName, out decimal value)
    {
        value = 0;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var property) && property.TryGetDecimal(out value);
    }

    private bool HasPermission(string permission) => httpContextAccessor.HttpContext is null || httpContextAccessor.HttpContext.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission);
    private void RequireProtectedPayrollMaintenance() { if (!HasPermission(BrassLedgerPermissions.PayrollManage) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) throw new UnauthorizedAccessException("You are not authorized to access protected payroll deduction elections."); }
    private Guid? ResolveUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext; var claim = context?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (context is not null && !Guid.TryParse(claim, out _)) throw new UnauthorizedAccessException("An authenticated company context is required.");
        if (Guid.TryParse(claim, out var id) && await db.Companies.AnyAsync(item => item.Id == id, cancellationToken)) return id;
        return await db.Companies.OrderBy(item => item.Name).Select(item => item.Id).FirstAsync(cancellationToken);
    }
    private void AddAudit(BrassLedgerDbContext db, Guid companyId, string action, string entityType, Guid entityId, object detail) => db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = entityType, EntityId = entityId, DetailJson = JsonSerializer.Serialize(detail), OccurredAtUtc = DateTimeOffset.UtcNow });
}
