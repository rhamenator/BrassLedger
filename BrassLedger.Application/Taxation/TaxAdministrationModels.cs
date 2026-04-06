namespace BrassLedger.Application.Taxation;

public interface ITaxAdministrationService
{
    Task<TaxAdministrationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveRuleSetAsync(SaveTaxRuleSetRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveParameterAsync(SaveTaxRuleParameterRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveBracketAsync(SaveTaxRuleBracketRequest request, CancellationToken cancellationToken = default);
    Task<TaxAdministrationResult> SaveFormRequirementAsync(SaveTaxFormRequirementRequest request, CancellationToken cancellationToken = default);
}

public sealed record TaxAdministrationSnapshot(
    IReadOnlyList<TaxCalculationMethodSnapshot> Methods,
    IReadOnlyList<LegacyTaxArtifactSnapshot> LegacyArtifacts,
    IReadOnlyList<TaxRuleSetSnapshot> RuleSets);

public sealed record TaxCalculationMethodSnapshot(
    string Code,
    string Name,
    string Description);

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
    IReadOnlyList<TaxRuleParameterSnapshot> Parameters,
    IReadOnlyList<TaxRuleBracketSnapshot> Brackets,
    IReadOnlyList<TaxFormRequirementSnapshot> FormRequirements);

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
    bool IsActive);

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
