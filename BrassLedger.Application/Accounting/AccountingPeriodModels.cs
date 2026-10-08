namespace BrassLedger.Application.Accounting;

public interface IAccountingPeriodService
{
    Task<AccountingControlsSnapshot> GetSnapshotAsync(int auditEntryLimit = 100, CancellationToken cancellationToken = default);
    Task<TransactionResult> SavePeriodAsync(SaveAccountingPeriodRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SetPeriodStatusAsync(Guid periodId, bool close, string notes, CancellationToken cancellationToken = default);
}

public sealed record SaveAccountingPeriodRequest(Guid? Id, DateOnly StartsOn, DateOnly EndsOn, string Notes);
public sealed record AccountingControlsSnapshot(IReadOnlyList<AccountingPeriodSnapshot> Periods, IReadOnlyList<BusinessAuditEntrySnapshot> AuditEntries);
public sealed record AccountingPeriodSnapshot(Guid Id, DateOnly StartsOn, DateOnly EndsOn, string Status, string Notes, string? ClosedBy, DateTimeOffset? ClosedAtUtc);
public sealed record BusinessAuditEntrySnapshot(Guid Id, DateTimeOffset OccurredAtUtc, string Action, string EntityType, Guid? EntityId, string? PerformedBy, string DetailJson);
