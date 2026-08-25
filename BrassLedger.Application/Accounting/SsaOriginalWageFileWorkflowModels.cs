namespace BrassLedger.Application.Accounting;

public sealed record SaveSsaOriginalWageFileConfigurationRequest(
    Guid? Id, int SpecificationTaxYear, string SpecificationVersion, string LayoutCompatibilityCode,
    string OfficialSpecificationUrl, string OfficialSpecificationSha256, DateOnly SourceRetrievedOn,
    string ReviewNotes, string SubmitterEin, string BsoUserId, string SubmitterName,
    string LocationAddress, string DeliveryAddress, string City, string State, string PostalCode,
    string ContactName, string ContactPhone, string ContactEmail, string PreparerCode,
    string EmployerLocationAddress, string EmployerDeliveryAddress, string EmployerCity,
    string EmployerState, string EmployerPostalCode, string EmployerContactName,
    string EmployerContactPhone, string EmployerContactEmail, string KindOfEmployer,
    string EmploymentCode, string EmployerSignaturePin, bool IsApproved, bool IsActive,
    string ConcurrencyToken = "");
public sealed record GenerateSsaOriginalWageFileRequest(Guid PayrollFilingId);
public sealed record SsaOriginalWageFileConfigurationSnapshot(
    Guid Id, SsaEfw2Submitter Submitter, string LayoutCompatibilityCode,
    string OfficialSpecificationSha256, DateOnly SourceRetrievedOn, string ReviewNotes,
    bool IsApproved, DateTimeOffset? ApprovedAtUtc, bool IsActive, string ConcurrencyToken);
public sealed record SsaOriginalWageFileSnapshot(
    Guid Id, Guid PayrollFilingId, int TaxYear, string FileName, string ContentSha256,
    string SourceDigestSha256, string SpecificationVersion, string Status, int RecordCount,
    int EmployeeRecordCount, DateTimeOffset GeneratedAtUtc, DateTimeOffset? ValidatedAtUtc,
    string AccuWageEvidenceReference, string ValidationNotes, string ConcurrencyToken);
public sealed record SsaOriginalWageFileWorkspace(
    IReadOnlyList<SsaOriginalWageFileConfigurationSnapshot> Configurations,
    IReadOnlyList<SsaOriginalWageFileSnapshot> Files);

public interface ISsaOriginalWageFileService
{
    Task<SsaOriginalWageFileWorkspace> GetAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveConfigurationAsync(SaveSsaOriginalWageFileConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> GenerateAsync(GenerateSsaOriginalWageFileRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> RecordValidationAsync(RecordSsaWageFileValidationRequest request, CancellationToken cancellationToken = default);
    Task<SsaWageFileDownload?> DownloadAsync(Guid fileId, CancellationToken cancellationToken = default);
}
