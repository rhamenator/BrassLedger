using System.Text.Json;
using BrassLedger.Domain.Accounting;

namespace BrassLedger.Infrastructure.Taxation;

internal static class FederalPayrollTaxCalculator
{
    internal const string Publication15TSource = "https://www.irs.gov/publications/p15t";
    internal const string Publication15Source = "https://www.irs.gov/publications/p15";
    internal const string SocialSecuritySource = "https://www.ssa.gov/oact/COLA/cbb.html";
    private static readonly Lazy<FederalPackageData> Package = new(LoadPackage, LazyThreadSafetyMode.ExecutionAndPublication);
    internal static string ContentVersion => Package.Value.ContentVersion;

    public static IReadOnlyList<FederalTaxCalculation> Calculate2026(Employee employee, decimal federalIncomeTaxableWages, decimal ficaTaxableWages, decimal priorSocialSecurityWages, decimal priorMedicareWages)
    {
        if (employee.FederalFormW4Year <= 0) throw new InvalidOperationException("A valid Form W-4 year is required before federal withholding can be calculated.");
        var data = Package.Value;
        if (!data.Worksheet1A.PeriodsPerYear.TryGetValue(employee.PayrollFrequency, out var periods)) throw new InvalidOperationException("The employee payroll frequency is not supported for federal withholding.");
        var schedules = employee.FederalFormW4Year >= 2020 && employee.FederalStep2MultipleJobs
            ? data.Worksheet1A.Step2CheckboxSchedules
            : data.Worksheet1A.StandardSchedules;
        if (!schedules.TryGetValue(employee.FilingStatus, out var scheduleRows)) throw new InvalidOperationException("The employee filing status is not supported for federal withholding.");
        var schedule = scheduleRows.Select(row => new Bracket(row[0]!.Value, row[1], row[2]!.Value, row[3]!.Value)).ToArray();

        var annualizedWages = federalIncomeTaxableWages * periods;
        decimal adjustedAnnualWages;
        if (employee.FederalFormW4Year >= 2020)
        {
            var worksheetAdjustment = employee.FederalStep2MultipleJobs ? data.Worksheet1A.StandardAdjustment.Step2Checkbox : employee.FilingStatus.Equals("Married filing jointly", StringComparison.OrdinalIgnoreCase) ? data.Worksheet1A.StandardAdjustment.MarriedFilingJointly : data.Worksheet1A.StandardAdjustment.AllOtherStatuses;
            adjustedAnnualWages = Math.Max(0, annualizedWages + employee.FederalStep4OtherIncome - employee.FederalStep4Deductions - worksheetAdjustment);
        }
        else
        {
            adjustedAnnualWages = Math.Max(0, annualizedWages - employee.Allowances * data.Worksheet1A.LegacyAllowanceAmount);
        }

        var bracket = schedule.Last(candidate => adjustedAnnualWages >= candidate.LowerBound && (!candidate.UpperBound.HasValue || adjustedAnnualWages < candidate.UpperBound));
        var tentativeAnnual = bracket.FixedAmount + (adjustedAnnualWages - bracket.LowerBound) * bracket.Rate;
        var fit = employee.FederalWithholdingExempt ? 0 : Round(Math.Max(0, tentativeAnnual / periods - employee.FederalStep3Credits / periods));

        var socialSecurityTaxable = Math.Max(0, Math.Min(ficaTaxableWages, data.Fica.SocialSecurity.AnnualWageBase!.Value - priorSocialSecurityWages));
        var socialSecurity = Round(socialSecurityTaxable * data.Fica.SocialSecurity.EmployeeRate);
        var medicare = Round(ficaTaxableWages * data.Fica.Medicare.EmployeeRate);
        var additionalMedicareTaxable = Math.Max(0, priorMedicareWages + ficaTaxableWages - data.Fica.AdditionalMedicare.WithholdingThreshold) - Math.Max(0, priorMedicareWages - data.Fica.AdditionalMedicare.WithholdingThreshold);
        var additionalMedicare = Round(additionalMedicareTaxable * data.Fica.AdditionalMedicare.EmployeeRate);

        return
        [
            new("US-FIT", "Federal income tax withholding", federalIncomeTaxableWages, 0, fit, 0, Publication15TSource,
                JsonSerializer.Serialize(new { method = employee.FederalStep2MultipleJobs && employee.FederalFormW4Year >= 2020 ? "2026 Publication 15-T Worksheet 1A Step 2 checkbox schedule" : "2026 Publication 15-T Worksheet 1A standard schedule", employee.FederalFormW4Year, employee.PayrollFrequency, periods, annualizedWages, adjustedAnnualWages, bracket.LowerBound, bracket.FixedAmount, bracket.Rate, employee.FederalStep3Credits, employee.FederalWithholdingExempt, amount = fit })),
            new("US-OASDI-EMPLOYEE", "Social Security employee", socialSecurityTaxable, priorSocialSecurityWages, socialSecurity, 0, SocialSecuritySource,
                JsonSerializer.Serialize(new { rate = data.Fica.SocialSecurity.EmployeeRate, wageBase = data.Fica.SocialSecurity.AnnualWageBase, priorSocialSecurityWages, taxableWages = socialSecurityTaxable, amount = socialSecurity })),
            new("US-OASDI-EMPLOYER", "Social Security employer", socialSecurityTaxable, priorSocialSecurityWages, 0, socialSecurity, SocialSecuritySource,
                JsonSerializer.Serialize(new { rate = data.Fica.SocialSecurity.EmployerRate, wageBase = data.Fica.SocialSecurity.AnnualWageBase, priorSocialSecurityWages, taxableWages = socialSecurityTaxable, amount = socialSecurity })),
            new("US-MEDICARE-EMPLOYEE", "Medicare employee", ficaTaxableWages, priorMedicareWages, medicare, 0, Publication15Source,
                JsonSerializer.Serialize(new { rate = data.Fica.Medicare.EmployeeRate, priorMedicareWages, taxableWages = ficaTaxableWages, amount = medicare })),
            new("US-MEDICARE-EMPLOYER", "Medicare employer", ficaTaxableWages, priorMedicareWages, 0, medicare, Publication15Source,
                JsonSerializer.Serialize(new { rate = data.Fica.Medicare.EmployerRate, priorMedicareWages, taxableWages = ficaTaxableWages, amount = medicare })),
            new("US-ADDITIONAL-MEDICARE", "Additional Medicare employee", additionalMedicareTaxable, priorMedicareWages, additionalMedicare, 0, Publication15Source,
                JsonSerializer.Serialize(new { rate = data.Fica.AdditionalMedicare.EmployeeRate, threshold = data.Fica.AdditionalMedicare.WithholdingThreshold, priorMedicareWages, taxableWages = additionalMedicareTaxable, amount = additionalMedicare }))
        ];
    }

    private static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static FederalPackageData LoadPackage()
    {
        var assembly = typeof(FederalPayrollTaxCalculator).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith("2026-payroll-tax-data.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException("The embedded 2026 federal payroll tax package is missing.");
        return JsonSerializer.Deserialize<FederalPackageData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("The embedded 2026 federal payroll tax package is invalid.");
    }
    private sealed record Bracket(decimal LowerBound, decimal? UpperBound, decimal FixedAmount, decimal Rate);
    private sealed class FederalPackageData { public string ContentVersion { get; set; } = string.Empty; public WorksheetData Worksheet1A { get; set; } = new(); public FicaData Fica { get; set; } = new(); }
    private sealed class WorksheetData { public Dictionary<string, int> PeriodsPerYear { get; set; } = []; public StandardAdjustmentData StandardAdjustment { get; set; } = new(); public decimal LegacyAllowanceAmount { get; set; } public Dictionary<string, decimal?[][]> StandardSchedules { get; set; } = []; public Dictionary<string, decimal?[][]> Step2CheckboxSchedules { get; set; } = []; }
    private sealed class StandardAdjustmentData { public decimal MarriedFilingJointly { get; set; } public decimal AllOtherStatuses { get; set; } public decimal Step2Checkbox { get; set; } }
    private sealed class FicaData { public FicaObligationData SocialSecurity { get; set; } = new(); public FicaObligationData Medicare { get; set; } = new(); public AdditionalMedicareData AdditionalMedicare { get; set; } = new(); }
    private sealed class FicaObligationData { public decimal EmployeeRate { get; set; } public decimal EmployerRate { get; set; } public decimal? AnnualWageBase { get; set; } }
    private sealed class AdditionalMedicareData { public decimal EmployeeRate { get; set; } public decimal EmployerRate { get; set; } public decimal WithholdingThreshold { get; set; } }
}

internal sealed record FederalTaxCalculation(string ObligationCode, string TaxType, decimal TaxableWages, decimal YearToDateTaxableWagesBefore, decimal EmployeeAmount, decimal EmployerAmount, string Source, string CalculationTraceJson);
