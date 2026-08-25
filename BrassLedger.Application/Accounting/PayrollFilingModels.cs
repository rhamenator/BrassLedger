using System.Text.Json;

namespace BrassLedger.Application.Accounting;

public sealed record SavePayrollFilingDraftRequest(Guid? FilingId, string FormCode, int TaxYear, int? Quarter = null, string ConcurrencyToken = "");
public sealed record ApprovePayrollFilingRequest(Guid FilingId, string ConcurrencyToken);
public sealed record ReopenPayrollFilingRequest(Guid FilingId, string Reason, string ConcurrencyToken);
public sealed record ClosePayrollPeriodRequest(string PeriodType, int TaxYear, int? Quarter = null);
public sealed record ReopenPayrollPeriodRequest(Guid PeriodId, string Reason, string ConcurrencyToken);

public sealed record PayrollFilingSnapshot(
    Guid Id, string FormCode, int TaxYear, int? Quarter, DateOnly PeriodStart, DateOnly PeriodEnd,
    string Status, JsonElement Data, JsonElement Summary, string SourceDigestSha256,
    string OfficialSourceUrl, string ContentVersion, DateTimeOffset PreparedAtUtc,
    DateTimeOffset? ApprovedAtUtc, string ConcurrencyToken);

public sealed record PayrollClosePeriodSnapshot(
    Guid Id, string PeriodType, int TaxYear, int? Quarter, DateOnly PeriodStart, DateOnly PeriodEnd,
    string Status, DateTimeOffset ClosedAtUtc, DateTimeOffset? ReopenedAtUtc,
    string ReopenReason, string ConcurrencyToken);

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
}
