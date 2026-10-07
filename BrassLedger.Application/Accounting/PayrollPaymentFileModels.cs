namespace BrassLedger.Application.Accounting;

public sealed record SavePayrollBankOriginConfigurationRequest(
    Guid? Id, Guid BankAccountId, string ImmediateDestinationRoutingNumber,
    string ImmediateOrigin, string DestinationBankName, string OriginName,
    string CompanyIdentification, string CompanyEntryDescription,
    string OriginatingDfiIdentification, DateOnly EffectiveOn, DateOnly? ExpiresOn,
    bool IsActive, bool IsBankValidated, string BankValidationNotes,
    string ConcurrencyToken = "");

public sealed record GeneratePayrollPaymentFileRequest(Guid PayrollRunId, string Format, DateOnly? EffectiveEntryDate = null);

public sealed record PayrollBankOriginConfigurationSnapshot(
    Guid Id, Guid BankAccountId, string BankAccountName, string DestinationRoutingLastFour,
    string ImmediateOriginMasked, string DestinationBankName, string OriginName,
    string CompanyIdentificationMasked, string CompanyEntryDescription,
    string OriginatingDfiIdentification, DateOnly EffectiveOn, DateOnly? ExpiresOn,
    bool IsActive, bool IsBankValidated, string BankValidationNotes, string ConcurrencyToken);

public sealed record PayrollPaymentFileSnapshot(
    Guid Id, Guid PayrollRunId, string PayrollReference, string Format, string FileName,
    string ContentType, string ContentSha256, string SourceDigestSha256, int EntryCount,
    decimal CreditTotal, long RoutingHash, string FileIdModifier, string Status,
    string SpecificationVersion, DateTimeOffset GeneratedAtUtc, DateTimeOffset? VoidedAtUtc,
    string VoidReason, string ConcurrencyToken);

public sealed record PayrollPaymentFileDownload(string FileName, string ContentType, byte[] Content, string ContentSha256);

public sealed record PayrollPaymentFileWorkspace(
    IReadOnlyList<PayrollBankOriginConfigurationSnapshot> BankOrigins,
    IReadOnlyList<PayrollPaymentFileSnapshot> Files);

public interface IPayrollPaymentFileService
{
    Task<PayrollPaymentFileWorkspace> GetAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveBankOriginAsync(SavePayrollBankOriginConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> GenerateAsync(GeneratePayrollPaymentFileRequest request, CancellationToken cancellationToken = default);
    Task<PayrollPaymentFileDownload?> DownloadAsync(Guid paymentFileId, CancellationToken cancellationToken = default);
}
