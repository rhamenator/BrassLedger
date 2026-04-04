using BrassLedger.Domain.Legacy;

namespace BrassLedger.Application.Catalog;

public sealed record ProductCatalog(
    string RecommendedLanguage,
    string RecommendedArchitecture,
    string RecommendedDatabase,
    string LegacyDataStrategy,
    string WhyCSharp,
    LegacyArtifactInventory Inventory,
    LegacyPrintableInventory PrintableInventory,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Phases,
    IReadOnlyList<LegacyModule> Modules,
    IReadOnlyList<string> ProductPrinciples,
    IReadOnlyList<string> FeaturesToRemove,
    IReadOnlyList<string> ConversionWorkstreams,
    IReadOnlyList<string> VisualDifferentiators,
    IReadOnlyList<string> TaxAutomationStrategy,
    IReadOnlyList<OfficialTaxSource> TaxSources);

