using System.Text.Json;

namespace BrassLedger.Application.Accounting;

public sealed record SavePayrollFilingDraftRequest(Guid? FilingId, string FormCode, int TaxYear, int? Quarter = null, string ConcurrencyToken = "");
public sealed record ApprovePayrollFilingRequest(Guid FilingId, string ConcurrencyToken);
public sealed record ReopenPayrollFilingRequest(Guid FilingId, string Reason, string ConcurrencyToken);
public sealed record ClosePayrollPeriodRequest(string PeriodType, int TaxYear, int? Quarter = null);
public sealed record ReopenPayrollPeriodRequest(Guid PeriodId, string Reason, string ConcurrencyToken);
public sealed record SaveForm941CorrectionDraftRequest(
    Guid? CorrectionId, Guid OriginalPayrollFilingId, string Process, DateOnly DiscoveredOn,
    string Explanation, string FederalWithholdingCorrectionType, string EmployeeCertificationCode,
    string EmployeeCertificationEvidenceReference, bool WageStatementsCorrected,
    string WageStatementEvidenceReference, string ConcurrencyToken = "");
public sealed record ApproveForm941CorrectionRequest(Guid CorrectionId, string ConcurrencyToken);
public sealed record VoidForm941CorrectionRequest(Guid CorrectionId, string Reason, string ConcurrencyToken);
public sealed record SaveW2CorrectionDraftRequest(
    Guid? CorrectionId, Guid OriginalPayrollFilingId, DateOnly DiscoveredOn,
    string Explanation, bool EmployeeStatementsFurnished,
    string EmployeeStatementEvidenceReference, string ConcurrencyToken = "");
public sealed record ApproveW2CorrectionRequest(Guid CorrectionId, string ConcurrencyToken);
public sealed record VoidW2CorrectionRequest(Guid CorrectionId, string Reason, string ConcurrencyToken);

public sealed record PayrollFilingSnapshot(
    Guid Id, string FormCode, int TaxYear, int? Quarter, DateOnly PeriodStart, DateOnly PeriodEnd,
    string Status, JsonElement Data, JsonElement Summary, string SourceDigestSha256,
    string OfficialSourceUrl, string ContentVersion, DateTimeOffset PreparedAtUtc,
    DateTimeOffset? ApprovedAtUtc, bool HasApprovedBaseline, string ConcurrencyToken);

public sealed record PayrollClosePeriodSnapshot(
    Guid Id, string PeriodType, int TaxYear, int? Quarter, DateOnly PeriodStart, DateOnly PeriodEnd,
    string Status, DateTimeOffset ClosedAtUtc, DateTimeOffset? ReopenedAtUtc,
    string ReopenReason, string ConcurrencyToken);

public sealed record PayrollFilingCorrectionSnapshot(
    Guid Id, Guid OriginalPayrollFilingId, int Sequence, string FormCode, int TaxYear, int Quarter,
    string Process, DateOnly DiscoveredOn, string Explanation, string FederalWithholdingCorrectionType,
    string EmployeeCertificationCode, string EmployeeCertificationEvidenceReference,
    bool WageStatementsCorrected, string WageStatementEvidenceReference, string Status, JsonElement Data,
    string CorrectedSourceDigestSha256, string OfficialSourceUrl, string ContentVersion,
    DateTimeOffset PreparedAtUtc, DateTimeOffset? ApprovedAtUtc, DateTimeOffset? VoidedAtUtc,
    string VoidReason, string ConcurrencyToken);

public sealed record Form941CorrectionLine(string Code, string Label, decimal OriginallyReported, decimal CorrectedAmount, decimal Difference);
public sealed record Form941XData(
    string Form = "941-X", string Revision = "2026-04", int TaxYear = 0, int Quarter = 0,
    int CorrectionSequence = 0, string Process = "Adjustment", DateOnly DiscoveredOn = default,
    string EmployerLegalName = "", string EmployerEin = "", IReadOnlyList<Form941CorrectionLine>? Lines = null,
    decimal TotalTaxDifference = 0, decimal AmountOwed = 0, decimal CreditOrRefund = 0,
    string Explanation = "", string FederalWithholdingCorrectionType = "None",
    string EmployeeCertificationCode = "UnderreportedOnly", string EmployeeCertificationEvidenceReference = "",
    bool WageStatementsCorrected = false, string WageStatementEvidenceReference = "",
    bool IrsMefTransmissionImplemented = false, bool RequiresProfessionalReview = true);

public sealed record Form941LiabilityDay(DateOnly PayDate, decimal TaxLiability);
public sealed record Form941Data(
    string Form = "941", string Revision = "2026-03", int TaxYear = 0, int Quarter = 0,
    string EmployerLegalName = "", string EmployerEin = "", int EmployeeCount = 0,
    decimal WagesTipsAndOtherCompensation = 0, decimal FederalIncomeTaxWithheld = 0,
    decimal SocialSecurityWages = 0, decimal SocialSecurityTax = 0,
    decimal MedicareWagesAndTips = 0, decimal MedicareTax = 0,
    decimal AdditionalMedicareTaxableWages = 0, decimal AdditionalMedicareTax = 0,
    decimal TotalTaxesBeforeAdjustments = 0, decimal DepositsRecorded = 0,
    decimal BalanceDue = 0, IReadOnlyList<Form941LiabilityDay>? TaxLiabilityByPayDate = null,
    bool RequiresProfessionalReview = true);

public sealed record Form940Data(
    string Form = "940", string Revision = "2026", int TaxYear = 0,
    string EmployerLegalName = "", string EmployerEin = "", decimal TotalPaymentsToEmployees = 0,
    decimal FutaTaxableWages = 0, decimal PaymentsExemptOrAboveWageBase = 0,
    decimal FutaTaxBeforeAdjustments = 0, decimal DepositsRecorded = 0, decimal BalanceDue = 0,
    bool CreditReductionAndStateAdjustmentsRequired = true, bool RequiresProfessionalReview = true);

public sealed record W2JurisdictionAmount(string JurisdictionCode, string JurisdictionName, decimal Wages, decimal IncomeTax);
public sealed record W2EmployeeData(
    Guid EmployeeId, string EmployeeNumber, string EmployeeName, string SocialSecurityNumber,
    string AddressLine1, string AddressLine2, string PostalCode, decimal Box1WagesTipsOtherCompensation,
    decimal Box2FederalIncomeTaxWithheld, decimal Box3SocialSecurityWages,
    decimal Box4SocialSecurityTaxWithheld, decimal Box5MedicareWagesAndTips,
    decimal Box6MedicareTaxWithheld, IReadOnlyList<W2JurisdictionAmount> StateAndLocalAmounts);
public sealed record W2PackageData(
    string Form = "W-2/W-3", string Revision = "2026", int TaxYear = 0,
    string EmployerLegalName = "", string EmployerEin = "", IReadOnlyList<W2EmployeeData>? Employees = null,
    decimal W3Box1Total = 0, decimal W3Box2Total = 0, decimal W3Box3Total = 0,
    decimal W3Box4Total = 0, decimal W3Box5Total = 0, decimal W3Box6Total = 0,
    bool SsaEFileTransmissionImplemented = false, bool RequiresProfessionalReview = true);

public sealed record W2cEmployeeData(
    W2EmployeeData PreviouslyReported, W2EmployeeData CorrectInformation,
    bool SubmitToSsa, string SubmissionReason);
public sealed record W2cPackageData(
    string Form = "W-2c/W-3c", string Revision = "2026-01", int TaxYear = 0,
    int CorrectionSequence = 0, DateOnly DiscoveredOn = default,
    string EmployerLegalName = "", string EmployerEin = "",
    IReadOnlyList<W2cEmployeeData>? Employees = null,
    decimal W3cPreviousBox1Total = 0, decimal W3cCorrectBox1Total = 0,
    decimal W3cPreviousBox2Total = 0, decimal W3cCorrectBox2Total = 0,
    decimal W3cPreviousBox3Total = 0, decimal W3cCorrectBox3Total = 0,
    decimal W3cPreviousBox4Total = 0, decimal W3cCorrectBox4Total = 0,
    decimal W3cPreviousBox5Total = 0, decimal W3cCorrectBox5Total = 0,
    decimal W3cPreviousBox6Total = 0, decimal W3cCorrectBox6Total = 0,
    string Explanation = "", bool EmployeeStatementsFurnished = false,
    string EmployeeStatementEvidenceReference = "",
    bool SsaEfw2cTransmissionImplemented = false, bool RequiresProfessionalReview = true);

public interface IPayrollFilingService
{
    Task<IReadOnlyList<PayrollFilingSnapshot>> GetFilingsAsync(CancellationToken cancellationToken = default);
    Task<PayrollFilingSnapshot?> GetFilingAsync(Guid filingId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollClosePeriodSnapshot>> GetClosePeriodsAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveDraftAsync(SavePayrollFilingDraftRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveAsync(ApprovePayrollFilingRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReopenFilingAsync(ReopenPayrollFilingRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ClosePeriodAsync(ClosePayrollPeriodRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ReopenPeriodAsync(ReopenPayrollPeriodRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollFilingCorrectionSnapshot>> GetCorrectionsAsync(CancellationToken cancellationToken = default);
    Task<PayrollFilingCorrectionSnapshot?> GetCorrectionAsync(Guid correctionId, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveForm941CorrectionDraftAsync(SaveForm941CorrectionDraftRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveForm941CorrectionAsync(ApproveForm941CorrectionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> VoidForm941CorrectionAsync(VoidForm941CorrectionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> SaveW2CorrectionDraftAsync(SaveW2CorrectionDraftRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> ApproveW2CorrectionAsync(ApproveW2CorrectionRequest request, CancellationToken cancellationToken = default);
    Task<TransactionResult> VoidW2CorrectionAsync(VoidW2CorrectionRequest request, CancellationToken cancellationToken = default);
}
