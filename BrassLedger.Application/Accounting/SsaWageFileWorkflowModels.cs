namespace BrassLedger.Application.Accounting;

public sealed record SaveSsaWageFileConfigurationRequest(
    Guid? Id, int SpecificationTaxYear, string SpecificationVersion, string LayoutCompatibilityCode,
    string OfficialSpecificationUrl, string OfficialSpecificationSha256, DateOnly SourceRetrievedOn,
    string ReviewNotes, string SubmitterEin, string BsoUserId, string SubmitterName,
    string LocationAddress, string DeliveryAddress, string City, string State, string PostalCode,
    string ContactName, string ContactPhone, string ContactEmail, string PreparerCode,
    string EmployerLocationAddress, string EmployerDeliveryAddress, string EmployerCity,
    string EmployerState, string EmployerPostalCode, string EmployerContactName,
    string EmployerContactPhone, string EmployerContactEmail, bool IsApproved, bool IsActive,
    string ConcurrencyToken = "");
public sealed record GenerateSsaWageFileRequest(Guid PayrollFilingCorrectionId);
public sealed record RecordSsaWageFileValidationRequest(Guid FileId, bool Passed, string EvidenceReference, string Notes, string ConcurrencyToken);
public sealed record SsaWageFileConfigurationSnapshot(Guid Id, SsaEfw2cSubmitter Submitter, string LayoutCompatibilityCode, string OfficialSpecificationSha256, DateOnly SourceRetrievedOn, string ReviewNotes, bool IsApproved, DateTimeOffset? ApprovedAtUtc, bool IsActive, string ConcurrencyToken);
public sealed record SsaWageFileSnapshot(Guid Id, Guid PayrollFilingCorrectionId, int TaxYear, string FileName, string ContentSha256, string SourceDigestSha256, string SpecificationVersion, string Status, int RecordCount, int EmployeeRecordCount, DateTimeOffset GeneratedAtUtc, DateTimeOffset? ValidatedAtUtc, string AccuWageEvidenceReference, string ValidationNotes, string ConcurrencyToken);
public sealed record SsaWageFileWorkspace(IReadOnlyList<SsaWageFileConfigurationSnapshot> Configurations, IReadOnlyList<SsaWageFileSnapshot> Files);
public sealed record SsaWageFileDownload(string FileName, byte[] Content, string ContentSha256, string Status);
public interface ISsaWageFileService
{
    Task<SsaWageFileWorkspace> GetAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveConfigurationAsync(SaveSsaWageFileConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> GenerateAsync(GenerateSsaWageFileRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordValidationAsync(RecordSsaWageFileValidationRequest request, CancellationToken cancellationToken = default);
    Task<SsaWageFileDownload?> DownloadAsync(Guid fileId, CancellationToken cancellationToken = default);
}
