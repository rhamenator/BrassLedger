namespace BrassLedger.Application.Taxation;

public interface ITaxAdministrationService
{
    Task<TaxAdministrationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveRuleSetAsync(SaveTaxRuleSetRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveParameterAsync(SaveTaxRuleParameterRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveBracketAsync(SaveTaxRuleBracketRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveFormRequirementAsync(SaveTaxFormRequirementRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveContentPackageAsync(SaveTaxContentPackageRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveFieldDefinitionAsync(SaveTaxRuleFieldDefinitionRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveTestCaseAsync(SaveTaxRuleTestCaseRequest request, CancellationToken cancellationToken = default);
    Task<TaxContentValidationResult> ValidateContentPackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> ActivateContentPackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> ImportTaxLocusAsync(CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> CaptureStateSourceAsync(CaptureTaxSourceRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> ImportTaxContentDocumentAsync(string documentJson, CancellationToken cancellationToken = default);
}

public sealed record TaxAdministrationSnapshot(
    IReadOnlyList<TaxCalculationMethodSnapshot> Methods,
    IReadOnlyList<TaxJurisdictionSnapshot> StateJurisdictions,
    IReadOnlyList<LegacyTaxArtifactSnapshot> LegacyArtifacts,
    IReadOnlyList<TaxRuleSetSnapshot> RuleSets,
    IReadOnlyList<TaxContentPackageSnapshot> Packages,
    IReadOnlyList<TaxSourceCaptureSnapshot> SourceCaptures);

public sealed record TaxContentPackageSnapshot(Guid Id, string PackageCode, string Version, DateOnly EffectiveOn, string Status, string MinimumEngineVersion, string ManifestJson, string Source, string ChangeSummary, DateTimeOffset CreatedAtUtc, DateTimeOffset? ApprovedAtUtc);
public sealed record TaxSourceCaptureSnapshot(Guid Id, Guid? TaxContentPackageId, string SourceKind, string JurisdictionCode, string SourceUrl, string ContentType, string ContentSha256, int ContentLength, DateTimeOffset CapturedAtUtc, string Notes);
public sealed record CaptureTaxSourceRequest(string JurisdictionCode, string SourceUrl, string Notes);
public sealed record TaxContentImportDocument(string PackageCode, string Version, DateOnly EffectiveOn, string Source, string ChangeSummary, IReadOnlyList<TaxContentImportRule> Rules);
public sealed record TaxContentImportRule(string Code, string JurisdictionCode, string JurisdictionName, string JurisdictionType, string TaxType, string CalculationMethod, string WithholdingFrequency, bool IsEmployerSpecific, IReadOnlyList<TaxContentImportParameter> Parameters, IReadOnlyList<TaxContentImportTest> Tests, IReadOnlyList<TaxContentImportField>? Fields = null, IReadOnlyList<TaxContentImportBracket>? Brackets = null, IReadOnlyList<TaxContentImportForm>? Forms = null);
public sealed record TaxContentImportParameter(string Code, string Label, decimal? Number, string? Text, bool? Boolean, string Notes);
public sealed record TaxContentImportTest(string Name, string InputJson, string ExpectedOutputJson);
public sealed record TaxContentImportField(string Code, string Label, string DataType, bool Required, System.Text.Json.JsonElement? Default, System.Text.Json.JsonElement? Validation, string? HelpText = null);
public sealed record TaxContentImportBracket(int Sequence, decimal UpperBoundAmount, decimal FixedAmount, decimal Rate, string? Notes = null);
public sealed record TaxContentImportForm(string Code, string Name, string? FilingFrequency, string? DeliveryChannel, string? DueRule, string? Notes = null);

public sealed record TaxCalculationMethodSnapshot(
    string Code,
    string Name,
    string Description);

public sealed record TaxJurisdictionSnapshot(string Code, string Name);

public sealed record LegacyTaxArtifactSnapshot(
    string Name,
    string SourcePath,
    string Notes);

public sealed record TaxRuleSetSnapshot(
    Guid Id,
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
    bool IsActive,
    Guid? TaxContentPackageId,
    string ContentVersion,
    string MinimumEngineVersion,
    IReadOnlyList<TaxRuleParameterSnapshot> Parameters,
    IReadOnlyList<TaxRuleBracketSnapshot> Brackets,
    IReadOnlyList<TaxFormRequirementSnapshot> FormRequirements,
    IReadOnlyList<TaxRuleFieldDefinitionSnapshot> FieldDefinitions,
    IReadOnlyList<TaxRuleTestCaseSnapshot> TestCases);

public sealed record TaxRuleFieldDefinitionSnapshot(Guid Id, string FieldCode, string Label, string DataType, bool IsRequired, string DefaultValueJson, string ValidationJson, int DisplayOrder, string HelpText);
public sealed record TaxRuleTestCaseSnapshot(Guid Id, string Name, string InputJson, string ExpectedOutputJson, bool IsRequiredForActivation);

public sealed record TaxRuleParameterSnapshot(
    Guid Id,
    string ParameterCode,
    string Label,
    string ValueType,
    decimal? NumericValue,
    string TextValue,
    bool? BooleanValue,
    string Notes,
    int DisplayOrder);

public sealed record TaxRuleBracketSnapshot(
    Guid Id,
    int Sequence,
    decimal UpperBoundAmount,
    decimal FixedAmount,
    decimal Rate,
    string Notes);

public sealed record TaxFormRequirementSnapshot(
    Guid Id,
    string FormCode,
    string Name,
    string FilingFrequency,
    string DeliveryChannel,
    string DueRule,
    string Notes);

public sealed record SaveTaxRuleSetRequest(
    Guid? Id,
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
    bool IsActive,
    Guid? TaxContentPackageId = null,
    string ContentVersion = "1.0",
    string MinimumEngineVersion = "1.0");

public sealed record SaveTaxContentPackageRequest(Guid? Id, string PackageCode, string Version, DateOnly EffectiveOn, string Status, string MinimumEngineVersion, string ManifestJson, string Source, string ChangeSummary);
public sealed record SaveTaxRuleFieldDefinitionRequest(Guid RuleSetId, Guid? Id, string FieldCode, string Label, string DataType, bool IsRequired, string DefaultValueJson, string ValidationJson, int DisplayOrder, string HelpText);
public sealed record SaveTaxRuleTestCaseRequest(Guid RuleSetId, Guid? Id, string Name, string InputJson, string ExpectedOutputJson, bool IsRequiredForActivation);

public sealed record SaveTaxRuleParameterRequest(
    Guid RuleSetId,
    Guid? Id,
    string ParameterCode,
    string Label,
    string ValueType,
    decimal? NumericValue,
    string TextValue,
    bool? BooleanValue,
    string Notes,
    int DisplayOrder);

public sealed record SaveTaxRuleBracketRequest(
    Guid RuleSetId,
    Guid? Id,
    int Sequence,
    decimal UpperBoundAmount,
    decimal FixedAmount,
    decimal Rate,
    string Notes);

public sealed record SaveTaxFormRequirementRequest(
    Guid RuleSetId,
    Guid? Id,
    string FormCode,
    string Name,
    string FilingFrequency,
    string DeliveryChannel,
    string DueRule,
    string Notes);

public sealed record TaxAdministrationResult(
    bool Succeeded,
    string ErrorMessage,
    Guid? SavedId)
{
    public static TaxAdministrationResult Success(Guid? savedId = null) => new(true, string.Empty, savedId);
    public static TaxAdministrationResult Failure(string errorMessage) => new(false, errorMessage, null);
}

public sealed record TaxContentValidationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static TaxContentValidationResult Success() => new(true, []);
    public static TaxContentValidationResult Failure(params string[] errors) => new(false, errors);
}
