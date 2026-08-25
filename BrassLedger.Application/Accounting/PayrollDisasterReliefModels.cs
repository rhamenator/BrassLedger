namespace BrassLedger.Application.Accounting;

public sealed record PayrollDisasterReliefAction(
    string ActionType, DateOnly OriginalDueOnOrAfter, DateOnly OriginalDueBefore,
    DateOnly ReliefDeadline, string Notes = "");

public sealed record SavePayrollDisasterReliefRequest(
    Guid? Id, string AnnouncementCode, string DisasterName, string FemaDeclarationNumber,
    string CoveredAreasJson, string AffectedTaxpayerBasis, string EligibilityEvidenceReference,
    string ReliefActionsJson, string OfficialSourceUrl, DateOnly SourceRetrievedOn,
    string ReviewNotes, bool IsApproved, bool IsActive, string ConcurrencyToken = "");

public sealed record PayrollDisasterReliefSnapshot(
    Guid Id, string AnnouncementCode, string DisasterName, string FemaDeclarationNumber,
    IReadOnlyList<string> CoveredAreas, string AffectedTaxpayerBasis,
    string EligibilityEvidenceReference, IReadOnlyList<PayrollDisasterReliefAction> ReliefActions,
    string OfficialSourceUrl, DateOnly SourceRetrievedOn, string ReviewNotes,
    bool IsApproved, DateTimeOffset? ApprovedAtUtc, bool IsActive, string ConcurrencyToken);

public sealed record PayrollDisasterDepositImpactSnapshot(
    Guid ConfigurationId, string AnnouncementCode, DateOnly OriginalDueDate,
    DateOnly PenaltyReliefDeadline, decimal RequiredAmount, decimal PaidByOriginalDueDate,
    decimal PaidByReliefDeadline, string Status);

public sealed record PayrollDisasterReliefWorkspace(
    IReadOnlyList<PayrollDisasterReliefSnapshot> Configurations,
    IReadOnlyList<PayrollDisasterDepositImpactSnapshot> DepositImpacts);

public interface IPayrollDisasterReliefService
{
    Task<PayrollDisasterReliefWorkspace> GetAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveAsync(SavePayrollDisasterReliefRequest request, CancellationToken cancellationToken = default);
}
