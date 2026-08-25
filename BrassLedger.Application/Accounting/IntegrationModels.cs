namespace BrassLedger.Application.Accounting;

public interface IIntegrationService
{
    Task<IReadOnlyList<IntegrationProviderSnapshot>> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IntegrationConnectionSnapshot>> GetConnectionsAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveConnectionAsync(SaveIntegrationConnectionRequest request, CancellationToken cancellationToken = default);
}

public sealed record IntegrationProviderSnapshot(string Code, string Name, string Category, string Description, bool SupportsSandbox, string ImplementationStatus = "Profile only", string SupportedCapabilities = "Connection profile storage only", bool LiveSynchronizationAvailable = false);
public sealed record IntegrationConnectionSnapshot(Guid Id, string ProviderCode, string Name, string Status, string SettingsJson, DateTimeOffset? LastValidatedAtUtc);
public sealed record SaveIntegrationConnectionRequest(Guid? Id, string ProviderCode, string Name, string SettingsJson, string CredentialsJson, bool Enable);
