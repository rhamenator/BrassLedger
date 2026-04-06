using BrassLedger.Domain.Accounting;

namespace BrassLedger.Infrastructure.Taxation;

internal static class TaxRuleCatalog
{
    public static IReadOnlyList<TaxCalculationMethodDefinition> Methods { get; } =
    [
        new("progressive-annualized", "Progressive annualized", "Annualize wages by pay frequency, apply brackets, then de-annualize withholding."),
        new("wage-bracket", "Wage bracket", "Use bracket thresholds and fixed amounts from the editable tax tables."),
        new("employer-rate-wage-base", "Employer rate with wage base", "Apply an employer rate until a wage-base ceiling is reached."),
        new("exemption-credit", "Exemption credit", "Apply a flat rate and reduce tax through per-exemption credits or allowances."),
        new("hourly-assessment", "Hourly assessment", "Calculate the assessment per hour worked instead of by wages."),
        new("local-code-a", "Local code A", "Local percentage with ceiling support, matching the archived local code A behavior."),
        new("local-code-e", "Local code E", "Standard allowance plus dependent allowance before local tax is applied."),
        new("custom-manual", "Custom/manual", "Keep the rule in the library and let administrators override fields without assuming a fixed formula.")
    ];

    public static IReadOnlyList<LegacyTaxArtifactDefinition> LegacyArtifacts { get; } =
    [
        new("Tax engine routines", "SourceCode/newproj/taxengin.prg", "Archived payroll tax routines, including Utah, New Jersey, Oregon, and local calculation branches."),
        new("Tax rule arrays", "SourceCode/newproj/taxrules.prg", "Archived withholding state used to carry forward deductions, annualization, and array-based tax state."),
        new("Primary tax table", "SourceCode/newproj/calc.ovr", "Archived editable table with FUTA, SUTA, SDIF, employer limits, and the B1-B12 bracket matrix."),
        new("Secondary tax table", "SourceCode/newproj/calc2.ovr", "Archived editable table with state-specific bracket rows and alternate code/sort structures."),
        new("Archived tax-table editor", "EditTaxTable_2006-05-01.zip", "Archived table editor that exposed FUTA, SUTA, SDIF, yearly limits, and multi-bracket rows directly to operators.")
    ];

    public static IReadOnlyList<TaxRuleTemplate> Templates { get; } =
    [
        new(
            Code: "FED-FIT",
            JurisdictionCode: "US",
            JurisdictionName: "Federal",
            JurisdictionType: "Federal",
            TaxType: "Employee withholding",
            CalculationMethod: "progressive-annualized",
            WithholdingFrequency: "Per payroll",
            EffectiveOn: new DateOnly(2026, 1, 1),
            Source: "IRS Publication 15-T",
            Notes: "Starter federal withholding template. Keep it editable and review current IRS annualized tables before using it for live payroll.",
            IsEmployerSpecific: false,
            SupportsBracketTable: true,
            SupportsParameterEditing: true,
            Parameters:
            [
                new("annualization-factor", "Annualization factor", "number", 26m, string.Empty, null, "Use the pay-frequency-specific factor that matches the payroll cycle.", 10),
                new("supplemental-rate", "Supplemental withholding rate", "number", 0.22m, string.Empty, null, "Flat supplemental rate for bonus-style wages when applicable.", 20),
                new("allowances-supported", "Allowances supported", "bool", null, string.Empty, false, "Modern federal withholding generally avoids old-style allowances, but the switch remains editable.", 30)
            ],
            Brackets:
            [
                new(1, 18000m, 0m, 0.10m, "Starter bracket row for editable federal withholding."),
                new(2, 72000m, 1800m, 0.12m, "Starter bracket row for editable federal withholding."),
                new(3, 9999999.99m, 8280m, 0.22m, "Starter top bracket row; replace with official annual values.")
            ],
            FormRequirements:
            [
                new("941", "Employer quarterly federal tax return", "Quarterly", "E-file or mail", "Due the last day of the month following quarter end.", "Operational reporting requirement for federal withholding deposits."),
                new("W-2", "Wage and tax statement", "Annual", "Electronic or print", "Provide to employees and agencies after year end.", "Printable employee year-end form."),
                new("W-3", "Transmittal of wage and tax statements", "Annual", "Electronic or mail", "Submit with annual wage reporting.", "Transmittal requirement paired with W-2 output.")
            ]),
        new(
            Code: "FED-FUTA",
            JurisdictionCode: "US",
            JurisdictionName: "Federal",
            JurisdictionType: "Federal",
            TaxType: "Employer unemployment",
            CalculationMethod: "employer-rate-wage-base",
            WithholdingFrequency: "Per payroll",
            EffectiveOn: new DateOnly(2026, 1, 1),
            Source: "IRS Publication 15",
            Notes: "Employer unemployment starter rule. Keep the wage base and reduced-credit rate editable in case law or credit assumptions move.",
            IsEmployerSpecific: false,
            SupportsBracketTable: false,
            SupportsParameterEditing: true,
            Parameters:
            [
                new("employer-rate", "Employer rate", "number", 0.006m, string.Empty, null, "Default reduced-credit FUTA rate.", 10),
                new("wage-base", "Wage base", "number", 7000m, string.Empty, null, "Editable wage base ceiling.", 20),
                new("no-suta-rate", "No SUTA credit rate", "number", 0.06m, string.Empty, null, "Fallback rate when state credit is not available.", 30)
            ],
            Brackets: [],
            FormRequirements:
            [
                new("940", "Employer annual federal unemployment return", "Annual", "E-file or mail", "Due after calendar year end.", "Annual unemployment reporting.")
            ]),
        new(
            Code: "AZ-SUI",
            JurisdictionCode: "AZ",
            JurisdictionName: "Arizona",
            JurisdictionType: "State",
            TaxType: "Employer unemployment",
            CalculationMethod: "employer-rate-wage-base",
            WithholdingFrequency: "Per payroll",
            EffectiveOn: new DateOnly(2026, 1, 1),
            Source: "Arizona DES employer notice",
            Notes: "Employer-specific unemployment starter rule. The rate and wage base should be updated from the employer notice each year.",
            IsEmployerSpecific: true,
            SupportsBracketTable: false,
            SupportsParameterEditing: true,
            Parameters:
            [
                new("employer-rate", "Employer rate", "number", 0.0245m, string.Empty, null, "Seeded from the sample company rate and meant to be edited.", 10),
                new("wage-base", "Wage base", "number", 8000m, string.Empty, null, "State unemployment wage base; confirm the current figure before use.", 20)
            ],
            Brackets: [],
            FormRequirements:
            [
                new("SUI-QTR", "State unemployment return", "Quarterly", "State portal", "Due after each calendar quarter.", "Generic quarterly unemployment filing.")
            ]),
        new(
            Code: "CA-ETT",
            JurisdictionCode: "CA",
            JurisdictionName: "California",
            JurisdictionType: "State",
            TaxType: "Employer training tax",
            CalculationMethod: "employer-rate-wage-base",
            WithholdingFrequency: "Per payroll",
            EffectiveOn: new DateOnly(2026, 1, 1),
            Source: "California employer reference",
            Notes: "Starter California employer tax rule. Use editable rate and limit values rather than hard-coded assumptions.",
            IsEmployerSpecific: false,
            SupportsBracketTable: false,
            SupportsParameterEditing: true,
            Parameters:
            [
                new("employer-rate", "Employer rate", "number", 0.001m, string.Empty, null, "Seeded starter percentage.", 10),
                new("wage-base", "Wage base", "number", 7000m, string.Empty, null, "Editable wage base ceiling.", 20)
            ],
            Brackets: [],
            FormRequirements:
            [
                new("DE9", "Quarterly contribution return and report of wages", "Quarterly", "State portal", "Due after each calendar quarter.", "Quarterly employer wage reporting."),
                new("DE9C", "Quarterly contribution return continuation", "Quarterly", "State portal", "Submit with quarterly wage detail.", "Employee wage detail for quarterly filing.")
            ]),
        new(
            Code: "NJ-WH",
            JurisdictionCode: "NJ",
            JurisdictionName: "New Jersey",
            JurisdictionType: "State",
            TaxType: "Employee withholding",
            CalculationMethod: "wage-bracket",
            WithholdingFrequency: "Per payroll",
            EffectiveOn: new DateOnly(2026, 1, 1),
            Source: "Archived NJ withholding branch",
            Notes: "New Jersey starter rule based on the archived bracket-driven behavior. Keep the bracket table and filing forms editable because New Jersey changes are often operationally specific.",
            IsEmployerSpecific: false,
            SupportsBracketTable: true,
            SupportsParameterEditing: true,
            Parameters:
            [
                new("allowance-credit", "Allowance credit", "number", 1000m, string.Empty, null, "Editable annualized allowance/credit placeholder.", 10),
                new("married-code-split", "Separate married filing codes", "bool", null, string.Empty, true, "Archived engine differentiated multiple filing-code paths; keep that choice visible.", 20)
            ],
            Brackets:
            [
                new(1, 20000m, 0m, 0.014m, "Starter bracket row."),
                new(2, 50000m, 280m, 0.0175m, "Starter bracket row."),
                new(3, 9999999.99m, 805m, 0.035m, "Starter bracket row.")
            ],
            FormRequirements:
            [
                new("NJ-927", "Employer withholding return", "Quarterly", "State portal", "Due after each calendar quarter.", "Quarterly withholding filing."),
                new("NJ-W-3", "Annual wage reconciliation", "Annual", "State portal", "Due after calendar year end.", "Annual wage reconciliation for withholding.")
            ]),
        new(
            Code: "UT-WH",
            JurisdictionCode: "UT",
            JurisdictionName: "Utah",
            JurisdictionType: "State",
            TaxType: "Employee withholding",
            CalculationMethod: "exemption-credit",
            WithholdingFrequency: "Per payroll",
            EffectiveOn: new DateOnly(2026, 1, 1),
            Source: "Archived Utah withholding branch",
            Notes: "Utah starter rule reflecting the archived flat-rate-plus-credit behavior. Keep the allowance credit and flat rate editable because that was the entire point of the old table editor.",
            IsEmployerSpecific: false,
            SupportsBracketTable: false,
            SupportsParameterEditing: true,
            Parameters:
            [
                new("flat-rate", "Flat withholding rate", "number", 0.0485m, string.Empty, null, "Starter Utah flat rate. Confirm current statute before live use.", 10),
                new("allowance-credit", "Allowance credit per exemption", "number", 38m, string.Empty, null, "Starter exemption credit; old routine treated this as the editable allowance amount.", 20),
                new("allowance-floor-zero", "Clamp below zero", "bool", null, string.Empty, true, "If allowance credits exceed gross calculated tax, the withheld amount becomes zero.", 30)
            ],
            Brackets: [],
            FormRequirements:
            [
                new("TC-941", "Employer quarterly return", "Quarterly", "State portal", "Due after each calendar quarter.", "Quarterly withholding filing."),
                new("TC-941R", "Annual reconciliation", "Annual", "State portal", "Due after calendar year end.", "Annual reconciliation and wage summary.")
            ]),
        new(
            Code: "LOCAL-E",
            JurisdictionCode: "LOCAL",
            JurisdictionName: "Local jurisdiction",
            JurisdictionType: "Local",
            TaxType: "Employee withholding",
            CalculationMethod: "local-code-e",
            WithholdingFrequency: "Per payroll",
            EffectiveOn: new DateOnly(2026, 1, 1),
            Source: "Archived local code E routine",
            Notes: "Starter local-tax template based on the archived local code E behavior: standard allowance percentage, minimum/maximum allowance bounds, and dependent allowances before tax is applied.",
            IsEmployerSpecific: false,
            SupportsBracketTable: false,
            SupportsParameterEditing: true,
            Parameters:
            [
                new("allowance-percent", "Standard allowance percent", "number", 0.10m, string.Empty, null, "Percentage of gross wages used to calculate the standard allowance.", 10),
                new("allowance-minimum", "Minimum allowance", "number", 0m, string.Empty, null, "Lower bound for the standard allowance.", 20),
                new("allowance-maximum", "Maximum allowance", "number", 250m, string.Empty, null, "Upper bound for the standard allowance.", 30),
                new("dependent-allowance", "Dependent allowance", "number", 25m, string.Empty, null, "Per-dependent allowance used before local tax is calculated.", 40),
                new("tax-rate", "Tax rate", "number", 0.01m, string.Empty, null, "Local withholding percentage after the allowance calculation.", 50)
            ],
            Brackets: [],
            FormRequirements:
            [
                new("LOCAL-WH", "Local withholding return", "Quarterly", "Agency-specific", "Due according to the jurisdiction calendar.", "Generic local filing placeholder that operators can rename.")
            ])
    ];

    public sealed record TaxCalculationMethodDefinition(
        string Code,
        string Name,
        string Description);

    public sealed record LegacyTaxArtifactDefinition(
        string Name,
        string SourcePath,
        string Notes);

    public sealed record TaxRuleTemplate(
        string Code,
        string JurisdictionCode,
        string JurisdictionName,
        string JurisdictionType,
        string TaxType,
        string CalculationMethod,
        string WithholdingFrequency,
        DateOnly EffectiveOn,
        string Source,
        string Notes,
        bool IsEmployerSpecific,
        bool SupportsBracketTable,
        bool SupportsParameterEditing,
        IReadOnlyList<TaxRuleTemplateParameter> Parameters,
        IReadOnlyList<TaxRuleTemplateBracket> Brackets,
        IReadOnlyList<TaxRuleTemplateFormRequirement> FormRequirements);

    public sealed record TaxRuleTemplateParameter(
        string ParameterCode,
        string Label,
        string ValueType,
        decimal? NumericValue,
        string TextValue,
        bool? BooleanValue,
        string Notes,
        int DisplayOrder);

    public sealed record TaxRuleTemplateBracket(
        int Sequence,
        decimal UpperBoundAmount,
        decimal FixedAmount,
        decimal Rate,
        string Notes);

    public sealed record TaxRuleTemplateFormRequirement(
        string FormCode,
        string Name,
        string FilingFrequency,
        string DeliveryChannel,
        string DueRule,
        string Notes);
}
