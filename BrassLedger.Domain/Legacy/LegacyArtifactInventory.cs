namespace BrassLedger.Domain.Legacy;

public sealed record LegacyArtifactInventory(
    string SourceStack,
    int Projects,
    int Programs,
    int Forms,
    int Reports,
    int Tables);
