namespace BrassLedger.Domain.Legacy;

public sealed record LegacyPrintableInventory(
    int Reports,
    int Labels,
    int ClassLibraries,
    int ActiveXControls,
    int CompiledArtifacts);
