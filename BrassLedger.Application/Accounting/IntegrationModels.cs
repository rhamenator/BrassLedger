namespace BrassLedger.Application.Accounting;

public interface IIntegrationService
{
    Task<IReadOnlyList<IntegrationProviderSnapshot>> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IntegrationConnectionSnapshot>> GetConnectionsAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveConnectionAsync(SaveIntegrationConnectionRequest request, CancellationToken cancellationToken = default);
}

public interface IQuickBooksOnlineConnectionService
{
    Task<QuickBooksOnlineAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<QuickBooksAuthorizationStartResult> BeginAuthorizationAsync(BeginQuickBooksAuthorizationRequest request, CancellationToken cancellationToken = default);
    Task<QuickBooksAuthorizationCompletionResult> CompleteAuthorizationAsync(CompleteQuickBooksAuthorizationRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ValidateConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);
    Task<TransactionResult> RefreshConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);
    Task<TransactionResult> DisconnectAsync(Guid connectionId, CancellationToken cancellationToken = default);
}

public interface IQuickBooksOnlineSyncService
{
    Task<QuickBooksSyncResult> ImportAsync(QuickBooksSyncRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuickBooksSyncRunSnapshot>> GetRecentRunsAsync(Guid? connectionId = null, int limit = 20, CancellationToken cancellationToken = default);
}

public sealed record IntegrationProviderSnapshot(string Code, string Name, string Category, string Description, bool SupportsSandbox, string ImplementationStatus = "Profile only", string SupportedCapabilities = "Connection profile storage only", bool LiveSynchronizationAvailable = false);
public sealed record IntegrationConnectionSnapshot(Guid Id, string ProviderCode, string Name, string Status, string SettingsJson, DateTimeOffset? LastValidatedAtUtc);
public sealed record SaveIntegrationConnectionRequest(Guid? Id, string ProviderCode, string Name, string SettingsJson, string CredentialsJson, bool Enable);
public sealed record QuickBooksOnlineAvailability(bool Configured, string Environment, string Message);
public sealed record BeginQuickBooksAuthorizationRequest(Guid? ConnectionId, string ConnectionName, string Environment);
public sealed record QuickBooksAuthorizationStartResult(bool Succeeded, string ErrorMessage, string? AuthorizationUrl = null)
{
    public static QuickBooksAuthorizationStartResult Success(string authorizationUrl) => new(true, string.Empty, authorizationUrl);
    public static QuickBooksAuthorizationStartResult Failure(string errorMessage) => new(false, errorMessage);
}
public sealed record CompleteQuickBooksAuthorizationRequest(string State, string? Code, string? RealmId, string? ProviderError, string? ProviderErrorDescription);
public sealed record QuickBooksAuthorizationCompletionResult(bool Succeeded, string ErrorMessage, Guid? ConnectionId = null, string? CompanyName = null)
{
    public static QuickBooksAuthorizationCompletionResult Success(Guid connectionId, string companyName) => new(true, string.Empty, connectionId, companyName);
    public static QuickBooksAuthorizationCompletionResult Failure(string errorMessage) => new(false, errorMessage);
}
public sealed record QuickBooksSyncRequest(Guid ConnectionId, string EntityType, bool DryRun = true, string ExpectedSnapshotSha256 = "");
public sealed record QuickBooksSyncIssue(string ProviderEntityId, string Code, string Message);
public sealed record QuickBooksSyncResult(bool Succeeded, string ErrorMessage, Guid? RunId, bool DryRun, int FetchedCount, int CreatedCount, int UpdatedCount, int UnchangedCount, int ConflictCount, int RejectedCount, string SnapshotSha256, IReadOnlyList<QuickBooksSyncIssue> Issues)
{
    public static QuickBooksSyncResult Failure(string errorMessage, bool dryRun = true) => new(false, errorMessage, null, dryRun, 0, 0, 0, 0, 0, 0, string.Empty, []);
}
public sealed record QuickBooksSyncRunSnapshot(Guid Id, Guid ConnectionId, string EntityType, string Direction, bool IsDryRun, string Status, int FetchedCount, int CreatedCount, int UpdatedCount, int UnchangedCount, int ConflictCount, int RejectedCount, string SnapshotSha256, IReadOnlyList<QuickBooksSyncIssue> Issues, string? InitiatedBy, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc);
