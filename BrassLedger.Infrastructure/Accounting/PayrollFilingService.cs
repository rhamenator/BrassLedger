using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class PayrollFilingService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : IPayrollFilingService
{
    private const string Form941Source = "https://www.irs.gov/instructions/i941";
    private const string Form941XSource = "https://www.irs.gov/instructions/i941x";
    private const string Form940Source = "https://www.irs.gov/forms-pubs/about-form-940";
    private const string W2Source = "https://www.irs.gov/instructions/iw2w3";
    private const string W2cSource = "https://www.irs.gov/instructions/iw2w3";

    public async Task<IReadOnlyList<PayrollFilingSnapshot>> GetFilingsAsync(CancellationToken cancellationToken = default)
    {
        RequirePermission(BrassLedgerPermissions.PayrollSensitiveData, "view protected payroll filing data");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var filings = await db.PayrollFilings.AsNoTracking().Where(item => item.CompanyId == companyId).OrderByDescending(item => item.TaxYear).ThenByDescending(item => item.Quarter).ThenBy(item => item.FormCode).ToListAsync(cancellationToken);
        return filings.Select(ToSnapshot).ToArray();
    }

    public async Task<PayrollFilingSnapshot?> GetFilingAsync(Guid filingId, CancellationToken cancellationToken = default)
    {
        RequirePermission(BrassLedgerPermissions.PayrollSensitiveData, "view protected payroll filing data");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var filing = await db.PayrollFilings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == filingId && item.CompanyId == companyId, cancellationToken);
        return filing is null ? null : ToSnapshot(filing);
    }

    public async Task<IReadOnlyList<PayrollClosePeriodSnapshot>> GetClosePeriodsAsync(CancellationToken cancellationToken = default)
    {
        RequirePayrollAccess();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        return (await db.PayrollClosePeriods.AsNoTracking().Where(item => item.CompanyId == companyId).OrderByDescending(item => item.TaxYear).ThenByDescending(item => item.Quarter).ToListAsync(cancellationToken))
            .Select(ToSnapshot).ToArray();
    }

    public async Task<IReadOnlyList<PayrollFilingCorrectionSnapshot>> GetCorrectionsAsync(CancellationToken cancellationToken = default)
    {
        RequirePermission(BrassLedgerPermissions.PayrollSensitiveData, "view protected payroll filing correction data");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        return (await db.PayrollFilingCorrections.AsNoTracking().Where(item => item.CompanyId == companyId).OrderByDescending(item => item.TaxYear).ThenByDescending(item => item.Quarter).ThenByDescending(item => item.Sequence).ToListAsync(cancellationToken)).Select(ToSnapshot).ToArray();
    }

    public async Task<PayrollFilingCorrectionSnapshot?> GetCorrectionAsync(Guid correctionId, CancellationToken cancellationToken = default)
    {
        RequirePermission(BrassLedgerPermissions.PayrollSensitiveData, "view protected payroll filing correction data");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var correction = await db.PayrollFilingCorrections.AsNoTracking().SingleOrDefaultAsync(item => item.Id == correctionId && item.CompanyId == companyId, cancellationToken);
        return correction is null ? null : ToSnapshot(correction);
    }

    public async Task<TransactionResult> SaveForm941CorrectionDraftAsync(SaveForm941CorrectionDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPrepare) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to prepare protected payroll filing corrections.");
        var process = request.Process?.Trim();
        var explanation = request.Explanation?.Trim() ?? string.Empty;
        var employeeEvidence = request.EmployeeCertificationEvidenceReference?.Trim() ?? string.Empty;
        var wageStatementEvidence = request.WageStatementEvidenceReference?.Trim() ?? string.Empty;
        if (process is not ("Adjustment" or "Claim")) return TransactionResult.Failure("Form 941-X process must be Adjustment or Claim.");
        if (explanation.Length < 20) return TransactionResult.Failure("Form 941-X requires a detailed correction explanation of at least 20 characters.");
        if (request.DiscoveredOn == default || request.DiscoveredOn > DateOnly.FromDateTime(DateTime.Today)) return TransactionResult.Failure("Enter the actual correction discovery date; it cannot be in the future.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var original = await db.PayrollFilings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.OriginalPayrollFilingId && item.CompanyId == companyId && item.FormCode == "941" && item.ApprovedSourceDigestSha256 != "", cancellationToken);
        if (original is null) return TransactionResult.Failure("Select a Form 941 filing with an approved immutable baseline from this company.");
        var source = await LoadSourceAsync(db, companyId, original.PeriodStart, original.PeriodEnd, cancellationToken);
        if (source.Runs.Count == 0) return TransactionResult.Failure("Corrected posted payroll does not contain any runs for this filing period.");
        var corrected = Build941(source, original.TaxYear, original.Quarter!.Value);
        var priorApproved = await db.PayrollFilingCorrections.AsNoTracking().Where(item => item.CompanyId == companyId && item.OriginalPayrollFilingId == original.Id && item.Status == "Approved").OrderByDescending(item => item.Sequence).FirstOrDefaultAsync(cancellationToken);
        var baseline = priorApproved is null ? Form941Values(JsonSerializer.Deserialize<Form941Data>(original.ApprovedDataJson) ?? new Form941Data()) : CorrectionValues(JsonSerializer.Deserialize<Form941XData>(priorApproved.DataJson) ?? new Form941XData());
        var correctedValues = Form941Values(corrected);
        var lines = Build941CorrectionLines(baseline, correctedValues);
        if (lines.All(item => item.Difference == 0m)) return TransactionResult.Failure("Corrected posted payroll matches the latest approved filing values; there is no Form 941-X difference to prepare.");
        var totalDifference = correctedValues["TotalTaxesBeforeAdjustments"] - baseline["TotalTaxesBeforeAdjustments"];
        var taxLineCodes = new[] { "FederalIncomeTaxWithheld", "SocialSecurityTax", "MedicareTax", "AdditionalMedicareTax" };
        if (process == "Claim" && (totalDifference >= 0m || lines.Any(item => taxLineCodes.Contains(item.Code) && item.Difference > 0m))) return TransactionResult.Failure("The claim process is limited to overreported tax only. Use the adjustment process for underreported or mixed corrections.");
        var fitDifference = lines.Single(item => item.Code == "FederalIncomeTaxWithheld").Difference;
        var fitType = request.FederalWithholdingCorrectionType?.Trim() ?? "None";
        if (!ValidFederalWithholdingCorrection(fitDifference, original.TaxYear, request.DiscoveredOn, fitType)) return TransactionResult.Failure("The federal-income-tax correction selection is inconsistent with the IRS same-year and prior-year correction restrictions.");
        var employeeTaxOverreport = lines.Any(item => (item.Code is "SocialSecurityTax" or "MedicareTax" or "AdditionalMedicareTax") && item.Difference < 0m);
        var certification = request.EmployeeCertificationCode?.Trim() ?? string.Empty;
        var allowedCertifications = new[] { "UnderreportedOnly", "RepaidOrReimbursed", "EmployeeConsent", "EmployerShareOnly", "NotWithheld" };
        if (!allowedCertifications.Contains(certification)) return TransactionResult.Failure("Select a supported Form 941-X employee-tax certification.");
        if (employeeTaxOverreport && (certification == "UnderreportedOnly" || string.IsNullOrWhiteSpace(employeeEvidence))) return TransactionResult.Failure("Overreported employee Social Security or Medicare tax requires the applicable employee protection certification and retained-evidence reference.");
        if (!employeeTaxOverreport && certification != "UnderreportedOnly" && string.IsNullOrWhiteSpace(employeeEvidence)) return TransactionResult.Failure("Enter the retained-evidence reference for the selected employee-tax certification.");
        if (!request.WageStatementsCorrected || string.IsNullOrWhiteSpace(wageStatementEvidence)) return TransactionResult.Failure("Certify that applicable Forms W-2/W-2c were or will be filed and enter the retained evidence reference.");
        PayrollFilingCorrection correction;
        if (request.CorrectionId.HasValue)
        {
            correction = await db.PayrollFilingCorrections.SingleOrDefaultAsync(item => item.Id == request.CorrectionId && item.CompanyId == companyId, cancellationToken) ?? new PayrollFilingCorrection();
            if (correction.Id == Guid.Empty) return TransactionResult.Failure("Form 941-X draft not found.");
            if (correction.Status != "Draft" || correction.OriginalPayrollFilingId != original.Id) return TransactionResult.Failure("Only the selected Form 941-X draft can be regenerated.");
            if (!string.Equals(correction.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The Form 941-X draft changed after it was opened. Refresh and try again.");
        }
        else
        {
            if (await db.PayrollFilingCorrections.AnyAsync(item => item.CompanyId == companyId && item.OriginalPayrollFilingId == original.Id && item.Status == "Draft", cancellationToken)) return TransactionResult.Failure("A Form 941-X draft already exists for this filing. Regenerate that draft instead.");
            var latestSequence = await db.PayrollFilingCorrections.Where(item => item.CompanyId == companyId && item.OriginalPayrollFilingId == original.Id).Select(item => (int?)item.Sequence).MaxAsync(cancellationToken) ?? 0;
            correction = new PayrollFilingCorrection { Id = Guid.NewGuid(), CompanyId = companyId, OriginalPayrollFilingId = original.Id, Sequence = latestSequence + 1, TaxYear = original.TaxYear, Quarter = original.Quarter.Value };
            db.PayrollFilingCorrections.Add(correction);
        }
        var data = new Form941XData(TaxYear: original.TaxYear, Quarter: original.Quarter.Value, CorrectionSequence: correction.Sequence, Process: process, DiscoveredOn: request.DiscoveredOn,
            EmployerLegalName: corrected.EmployerLegalName, EmployerEin: corrected.EmployerEin, Lines: lines, TotalTaxDifference: totalDifference, AmountOwed: Math.Max(0m, totalDifference), CreditOrRefund: Math.Max(0m, -totalDifference),
            Explanation: explanation, FederalWithholdingCorrectionType: fitType, EmployeeCertificationCode: certification, EmployeeCertificationEvidenceReference: employeeEvidence,
            WageStatementsCorrected: request.WageStatementsCorrected, WageStatementEvidenceReference: wageStatementEvidence);
        correction.Process = process; correction.DiscoveredOn = request.DiscoveredOn; correction.Explanation = explanation; correction.FederalWithholdingCorrectionType = fitType;
        correction.EmployeeCertificationCode = certification; correction.EmployeeCertificationEvidenceReference = employeeEvidence; correction.WageStatementsCorrected = request.WageStatementsCorrected; correction.WageStatementEvidenceReference = wageStatementEvidence;
        correction.Status = "Draft"; correction.DataJson = JsonSerializer.Serialize(data); correction.CorrectedSourceDigestSha256 = BuildSourceDigest(source); correction.OfficialSourceUrl = Form941XSource; correction.ContentVersion = "2026-941X-1";
        correction.PreparedByUserId = ResolveUserId(); correction.PreparedAtUtc = DateTimeOffset.UtcNow; correction.ApprovedByUserId = null; correction.ApprovedAtUtc = null; correction.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-filing-correction.draft.generated", nameof(PayrollFilingCorrection), correction.Id, new { correction.OriginalPayrollFilingId, correction.Sequence, correction.Process, correction.TaxYear, correction.Quarter, totalDifference, correction.CorrectedSourceDigestSha256 });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The Form 941-X draft changed while it was being regenerated. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("A conflicting Form 941-X draft or sequence already exists for this filing."); }
        return TransactionResult.Success(correction.Id);
    }

    public async Task<TransactionResult> VoidForm941CorrectionAsync(VoidForm941CorrectionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollReverse) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to void protected payroll filing corrections.");
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 10) return TransactionResult.Failure("A meaningful Form 941-X void reason of at least 10 characters is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var correction = await db.PayrollFilingCorrections.SingleOrDefaultAsync(item => item.Id == request.CorrectionId && item.CompanyId == companyId, cancellationToken);
        if (correction is null) return TransactionResult.Failure("Form 941-X correction not found.");
        if (correction.Status != "Draft") return TransactionResult.Failure("Only a draft Form 941-X correction can be voided.");
        if (!string.Equals(correction.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The Form 941-X correction changed after it was opened. Refresh and try again.");
        correction.Status = "Voided"; correction.VoidedByUserId = ResolveUserId(); correction.VoidedAtUtc = DateTimeOffset.UtcNow; correction.VoidReason = reason; correction.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-filing-correction.voided", nameof(PayrollFilingCorrection), correction.Id, new { correction.OriginalPayrollFilingId, correction.Sequence, correction.VoidReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The Form 941-X correction changed while it was being voided. Refresh and try again."); }
        return TransactionResult.Success(correction.Id);
    }

    public async Task<TransactionResult> ApproveForm941CorrectionAsync(ApproveForm941CorrectionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollApprove) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to approve protected payroll filing corrections.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var correction = await db.PayrollFilingCorrections.SingleOrDefaultAsync(item => item.Id == request.CorrectionId && item.CompanyId == companyId, cancellationToken);
        if (correction is null) return TransactionResult.Failure("Form 941-X correction not found.");
        if (correction.Status != "Draft") return TransactionResult.Failure("Only a draft Form 941-X correction can be approved.");
        if (!string.Equals(correction.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The Form 941-X correction changed after it was opened. Refresh and try again.");
        var original = await db.PayrollFilings.AsNoTracking().SingleAsync(item => item.Id == correction.OriginalPayrollFilingId && item.CompanyId == companyId, cancellationToken);
        var source = await LoadSourceAsync(db, companyId, original.PeriodStart, original.PeriodEnd, cancellationToken);
        if (!string.Equals(BuildSourceDigest(source), correction.CorrectedSourceDigestSha256, StringComparison.Ordinal)) return TransactionResult.Failure("Posted payroll changed after this Form 941-X draft was generated. Regenerate and review the correction before approval.");
        correction.Status = "Approved"; correction.ApprovedByUserId = ResolveUserId(); correction.ApprovedAtUtc = DateTimeOffset.UtcNow; correction.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-filing-correction.approved", nameof(PayrollFilingCorrection), correction.Id, new { correction.OriginalPayrollFilingId, correction.Sequence, correction.Process, correction.TaxYear, correction.Quarter, correction.CorrectedSourceDigestSha256 });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The Form 941-X correction changed while it was being approved. Refresh and try again."); }
        return TransactionResult.Success(correction.Id);
    }

    public async Task<TransactionResult> SaveW2CorrectionDraftAsync(SaveW2CorrectionDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPrepare) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to prepare protected payroll filing corrections.");
        var explanation = request.Explanation?.Trim() ?? string.Empty;
        var evidence = request.EmployeeStatementEvidenceReference?.Trim() ?? string.Empty;
        if (explanation.Length < 20) return TransactionResult.Failure("Forms W-2c/W-3c require a detailed correction explanation of at least 20 characters.");
        if (request.DiscoveredOn == default || request.DiscoveredOn > DateOnly.FromDateTime(DateTime.Today)) return TransactionResult.Failure("Enter the actual correction discovery date; it cannot be in the future.");
        if (!request.EmployeeStatementsFurnished || evidence.Length < 5) return TransactionResult.Failure("Certify that corrected employee statements were or will be furnished and enter the retained evidence reference.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var original = await db.PayrollFilings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.OriginalPayrollFilingId && item.CompanyId == companyId && item.FormCode == "W2" && item.ApprovedSourceDigestSha256 != "", cancellationToken);
        if (original is null) return TransactionResult.Failure("Select a Form W-2/W-3 filing with an approved immutable baseline from this company.");
        var source = await LoadSourceAsync(db, companyId, original.PeriodStart, original.PeriodEnd, cancellationToken);
        if (source.Runs.Count == 0) return TransactionResult.Failure("Corrected posted payroll does not contain any runs for this filing year.");
        var current = BuildW2(source, original.TaxYear);
        var priorApproved = await db.PayrollFilingCorrections.AsNoTracking().Where(item => item.CompanyId == companyId && item.OriginalPayrollFilingId == original.Id && item.FormCode == "W-2c/W-3c" && item.Status == "Approved").OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var originalPackage = JsonSerializer.Deserialize<W2PackageData>(original.ApprovedDataJson) ?? new W2PackageData();
        var baselineEmployees = (originalPackage.Employees ?? []).ToDictionary(item => item.EmployeeId);
        foreach (var prior in priorApproved)
            foreach (var employee in JsonSerializer.Deserialize<W2cPackageData>(prior.DataJson)?.Employees ?? [])
                baselineEmployees[employee.CorrectInformation.EmployeeId] = employee.CorrectInformation;
        var currentEmployees = (current.Employees ?? []).ToDictionary(item => item.EmployeeId);
        var changes = baselineEmployees.Keys.Union(currentEmployees.Keys).OrderBy(id => id).Select(id =>
        {
            var previous = baselineEmployees.GetValueOrDefault(id) ?? ZeroW2Amounts(currentEmployees[id]);
            var correct = currentEmployees.GetValueOrDefault(id) ?? ZeroW2Amounts(previous);
            var federalOrIdentityChanged = FederalOrIdentityChanged(previous, correct);
            var stateLocalChanged = CanonicalW2Jurisdictions(previous) != CanonicalW2Jurisdictions(correct);
            var addressChanged = previous.AddressLine1 != correct.AddressLine1 || previous.AddressLine2 != correct.AddressLine2 || previous.PostalCode != correct.PostalCode;
            var submit = federalOrIdentityChanged;
            var reason = submit ? "Federal wage/tax or employee identity correction" : stateLocalChanged ? "State/local-only correction; do not submit Copy A to SSA" : "Employee-address-only correction; do not submit Copy A to SSA";
            return new { Changed = federalOrIdentityChanged || stateLocalChanged || addressChanged, Item = new W2cEmployeeData(previous, correct, submit, reason) };
        }).Where(item => item.Changed).Select(item => item.Item).ToArray();
        if (changes.Length == 0) return TransactionResult.Failure("Corrected posted payroll matches the latest approved wage statement values; there is no W-2c/W-3c difference to prepare.");
        PayrollFilingCorrection correction;
        if (request.CorrectionId.HasValue)
        {
            correction = await db.PayrollFilingCorrections.SingleOrDefaultAsync(item => item.Id == request.CorrectionId && item.CompanyId == companyId && item.FormCode == "W-2c/W-3c", cancellationToken) ?? new PayrollFilingCorrection();
            if (correction.Id == Guid.Empty) return TransactionResult.Failure("W-2c/W-3c draft not found.");
            if (correction.Status != "Draft" || correction.OriginalPayrollFilingId != original.Id) return TransactionResult.Failure("Only the selected W-2c/W-3c draft can be regenerated.");
            if (!string.Equals(correction.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The W-2c/W-3c draft changed after it was opened. Refresh and try again.");
        }
        else
        {
            if (await db.PayrollFilingCorrections.AnyAsync(item => item.CompanyId == companyId && item.OriginalPayrollFilingId == original.Id && item.FormCode == "W-2c/W-3c" && item.Status == "Draft", cancellationToken)) return TransactionResult.Failure("A W-2c/W-3c draft already exists for this filing. Regenerate that draft instead.");
            var latestSequence = await db.PayrollFilingCorrections.Where(item => item.CompanyId == companyId && item.OriginalPayrollFilingId == original.Id && item.FormCode == "W-2c/W-3c").Select(item => (int?)item.Sequence).MaxAsync(cancellationToken) ?? 0;
            correction = new PayrollFilingCorrection { Id = Guid.NewGuid(), CompanyId = companyId, OriginalPayrollFilingId = original.Id, Sequence = latestSequence + 1, TaxYear = original.TaxYear, Quarter = 0, FormCode = "W-2c/W-3c" };
            db.PayrollFilingCorrections.Add(correction);
        }
        var previousTotals = SumW2(changes.Select(item => item.PreviouslyReported));
        var correctTotals = SumW2(changes.Select(item => item.CorrectInformation));
        var data = new W2cPackageData(TaxYear: original.TaxYear, CorrectionSequence: correction.Sequence, DiscoveredOn: request.DiscoveredOn,
            EmployerLegalName: current.EmployerLegalName, EmployerEin: current.EmployerEin, Employees: changes,
            W3cPreviousBox1Total: previousTotals[0], W3cCorrectBox1Total: correctTotals[0], W3cPreviousBox2Total: previousTotals[1], W3cCorrectBox2Total: correctTotals[1],
            W3cPreviousBox3Total: previousTotals[2], W3cCorrectBox3Total: correctTotals[2], W3cPreviousBox4Total: previousTotals[3], W3cCorrectBox4Total: correctTotals[3],
            W3cPreviousBox5Total: previousTotals[4], W3cCorrectBox5Total: correctTotals[4], W3cPreviousBox6Total: previousTotals[5], W3cCorrectBox6Total: correctTotals[5],
            Explanation: explanation, EmployeeStatementsFurnished: true, EmployeeStatementEvidenceReference: evidence);
        correction.Process = "Correction"; correction.DiscoveredOn = request.DiscoveredOn; correction.Explanation = explanation;
        correction.WageStatementsCorrected = true; correction.WageStatementEvidenceReference = evidence; correction.Status = "Draft";
        correction.DataJson = JsonSerializer.Serialize(data); correction.CorrectedSourceDigestSha256 = BuildSourceDigest(source); correction.OfficialSourceUrl = W2cSource; correction.ContentVersion = "2026-W2C-W3C-1";
        correction.PreparedByUserId = ResolveUserId(); correction.PreparedAtUtc = DateTimeOffset.UtcNow; correction.ApprovedByUserId = null; correction.ApprovedAtUtc = null; correction.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-w2c-correction.draft.generated", nameof(PayrollFilingCorrection), correction.Id, new { correction.OriginalPayrollFilingId, correction.Sequence, correction.TaxYear, employeeCount = changes.Length, ssaSubmissionCount = changes.Count(item => item.SubmitToSsa), correction.CorrectedSourceDigestSha256 });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The W-2c/W-3c draft changed while it was being regenerated. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("A conflicting W-2c/W-3c draft or sequence already exists for this filing."); }
        return TransactionResult.Success(correction.Id);
    }

    public Task<TransactionResult> ApproveW2CorrectionAsync(ApproveW2CorrectionRequest request, CancellationToken cancellationToken = default) => ChangeW2CorrectionStatusAsync(request.CorrectionId, request.ConcurrencyToken, true, "", cancellationToken);
    public Task<TransactionResult> VoidW2CorrectionAsync(VoidW2CorrectionRequest request, CancellationToken cancellationToken = default) => ChangeW2CorrectionStatusAsync(request.CorrectionId, request.ConcurrencyToken, false, request.Reason, cancellationToken);

    private async Task<TransactionResult> ChangeW2CorrectionStatusAsync(Guid id, string token, bool approve, string reason, CancellationToken cancellationToken)
    {
        var permission = approve ? BrassLedgerPermissions.PayrollApprove : BrassLedgerPermissions.PayrollReverse;
        if (!HasPermission(permission) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure($"You are not authorized to {(approve ? "approve" : "void")} protected wage statement corrections.");
        reason = reason?.Trim() ?? string.Empty;
        if (!approve && reason.Length < 10) return TransactionResult.Failure("A meaningful W-2c/W-3c void reason of at least 10 characters is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var correction = await db.PayrollFilingCorrections.SingleOrDefaultAsync(item => item.Id == id && item.CompanyId == companyId && item.FormCode == "W-2c/W-3c", cancellationToken);
        if (correction is null) return TransactionResult.Failure("W-2c/W-3c correction not found.");
        if (correction.Status != "Draft") return TransactionResult.Failure("Only a draft W-2c/W-3c correction can be changed.");
        if (!string.Equals(correction.ConcurrencyToken, token, StringComparison.Ordinal)) return TransactionResult.Failure("The W-2c/W-3c correction changed after it was opened. Refresh and try again.");
        if (approve)
        {
            var original = await db.PayrollFilings.AsNoTracking().SingleAsync(item => item.Id == correction.OriginalPayrollFilingId && item.CompanyId == companyId, cancellationToken);
            var source = await LoadSourceAsync(db, companyId, original.PeriodStart, original.PeriodEnd, cancellationToken);
            if (!string.Equals(BuildSourceDigest(source), correction.CorrectedSourceDigestSha256, StringComparison.Ordinal)) return TransactionResult.Failure("Posted payroll changed after this W-2c/W-3c draft was generated. Regenerate and review it before approval.");
            correction.Status = "Approved"; correction.ApprovedByUserId = ResolveUserId(); correction.ApprovedAtUtc = DateTimeOffset.UtcNow;
        }
        else { correction.Status = "Voided"; correction.VoidedByUserId = ResolveUserId(); correction.VoidedAtUtc = DateTimeOffset.UtcNow; correction.VoidReason = reason; }
        correction.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, approve ? "payroll-w2c-correction.approved" : "payroll-w2c-correction.voided", nameof(PayrollFilingCorrection), correction.Id, new { correction.OriginalPayrollFilingId, correction.Sequence, correction.CorrectedSourceDigestSha256, correction.VoidReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The W-2c/W-3c correction changed while it was being saved. Refresh and try again."); }
        return TransactionResult.Success(correction.Id);
    }

    public async Task<TransactionResult> SaveDraftAsync(SavePayrollFilingDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPrepare) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to prepare protected payroll filing data.");
        if (!TryResolvePeriod(request.FormCode, request.TaxYear, request.Quarter, out var formCode, out var periodStart, out var periodEnd, out var error)) return TransactionResult.Failure(error);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await IsPeriodClosedAsync(db, companyId, periodStart, periodEnd, cancellationToken)) return TransactionResult.Failure("Reopen the payroll period before regenerating filing data.");
        var source = await LoadSourceAsync(db, companyId, periodStart, periodEnd, cancellationToken);
        if (source.Runs.Count == 0) return TransactionResult.Failure("No posted, unreversed payroll runs exist in the requested filing period.");
        if (Digits(source.Company.TaxId).Length != 9) return TransactionResult.Failure("A valid nine-digit employer EIN is required before generating federal payroll filing data.");
        if (formCode == "W2" && source.Employees.Values.Any(employee => Digits(employee.SocialSecurityNumber).Length != 9 || string.IsNullOrWhiteSpace(employee.FirstName) || string.IsNullOrWhiteSpace(employee.LastName) || string.IsNullOrWhiteSpace(employee.AddressLine1) || string.IsNullOrWhiteSpace(employee.AddressCity) || employee.AddressState.Trim().Length != 2 || string.IsNullOrWhiteSpace(employee.PostalCode)))
            return TransactionResult.Failure("Every employee in W-2 source payroll requires a valid SSN, separate first and last name, street address, city, two-letter state, and postal code.");
        var payload = formCode switch
        {
            "941" => JsonSerializer.Serialize(Build941(source, request.TaxYear, request.Quarter!.Value)),
            "940" => JsonSerializer.Serialize(Build940(source, request.TaxYear)),
            "W2" => JsonSerializer.Serialize(BuildW2(source, request.TaxYear)),
            _ => throw new InvalidOperationException("Unsupported payroll form.")
        };
        var summary = BuildSummaryJson(formCode, source);
        var digest = BuildSourceDigest(source);
        PayrollFiling filing;
        if (request.FilingId.HasValue)
        {
            filing = await db.PayrollFilings.SingleOrDefaultAsync(item => item.Id == request.FilingId && item.CompanyId == companyId, cancellationToken) ?? new PayrollFiling();
            if (filing.Id == Guid.Empty) return TransactionResult.Failure("Payroll filing draft not found.");
            if (filing.Status != "Draft") return TransactionResult.Failure("Only a draft payroll filing can be regenerated.");
            if (!string.Equals(filing.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll filing changed after it was opened. Refresh and try again.");
            if (filing.FormCode != formCode || filing.TaxYear != request.TaxYear || filing.Quarter != request.Quarter) return TransactionResult.Failure("A filing draft cannot be changed to another form or period.");
        }
        else
        {
            if (await db.PayrollFilings.AnyAsync(item => item.CompanyId == companyId && item.FormCode == formCode && item.TaxYear == request.TaxYear && item.Quarter == request.Quarter, cancellationToken)) return TransactionResult.Failure("A payroll filing already exists for this form and period. Regenerate its draft instead.");
            filing = new PayrollFiling { Id = Guid.NewGuid(), CompanyId = companyId, FormCode = formCode, TaxYear = request.TaxYear, Quarter = request.Quarter, PeriodKey = BuildPeriodKey(request.TaxYear, request.Quarter), PeriodStart = periodStart, PeriodEnd = periodEnd };
            db.PayrollFilings.Add(filing);
        }
        filing.Status = "Draft"; filing.DataJson = payload; filing.SummaryJson = summary;
        filing.SourcePayrollRunIdsJson = JsonSerializer.Serialize(source.Runs.Select(run => run.Id).Order().ToArray());
        filing.SourceDigestSha256 = digest; filing.OfficialSourceUrl = SourceUrl(formCode); filing.ContentVersion = $"2026-{formCode}-1";
        filing.PreparedByUserId = ResolveUserId(); filing.PreparedAtUtc = DateTimeOffset.UtcNow; filing.ApprovedByUserId = null; filing.ApprovedAtUtc = null;
        filing.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-filing.draft.generated", "PayrollFiling", filing.Id, new { filing.FormCode, filing.TaxYear, filing.Quarter, sourceRunCount = source.Runs.Count, filing.SourceDigestSha256 });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payroll filing changed while it was being regenerated. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("A payroll filing already exists for this form and period."); }
        return TransactionResult.Success(filing.Id);
    }

    public async Task<TransactionResult> ApproveAsync(ApprovePayrollFilingRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollApprove) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to approve protected payroll filing data.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var filing = await db.PayrollFilings.SingleOrDefaultAsync(item => item.Id == request.FilingId && item.CompanyId == companyId, cancellationToken);
        if (filing is null) return TransactionResult.Failure("Payroll filing not found.");
        if (filing.Status != "Draft") return TransactionResult.Failure("Only a draft payroll filing can be approved.");
        if (!string.Equals(filing.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll filing changed after it was opened. Refresh and try again.");
        var source = await LoadSourceAsync(db, companyId, filing.PeriodStart, filing.PeriodEnd, cancellationToken);
        if (!string.Equals(BuildSourceDigest(source), filing.SourceDigestSha256, StringComparison.Ordinal)) return TransactionResult.Failure("Posted payroll changed after this filing draft was generated. Regenerate and review the filing before approval.");
        filing.Status = "Approved"; filing.ApprovedByUserId = ResolveUserId(); filing.ApprovedAtUtc = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(filing.ApprovedSourceDigestSha256))
        {
            filing.ApprovedDataJson = filing.DataJson; filing.ApprovedSourceDigestSha256 = filing.SourceDigestSha256; filing.ApprovedBaselineAtUtc = filing.ApprovedAtUtc;
        }
        filing.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-filing.approved", "PayrollFiling", filing.Id, new { filing.FormCode, filing.TaxYear, filing.Quarter, filing.SourceDigestSha256 });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payroll filing changed while it was being approved. Refresh and try again."); }
        return TransactionResult.Success(filing.Id);
    }

    public async Task<TransactionResult> ReopenFilingAsync(ReopenPayrollFilingRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollReverse)) return TransactionResult.Failure("You are not authorized to reopen payroll filings.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A payroll filing reopen reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var filing = await db.PayrollFilings.SingleOrDefaultAsync(item => item.Id == request.FilingId && item.CompanyId == companyId, cancellationToken);
        if (filing is null) return TransactionResult.Failure("Payroll filing not found.");
        if (filing.Status != "Approved") return TransactionResult.Failure("Only an approved payroll filing can be reopened.");
        if (!string.Equals(filing.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll filing changed after it was opened. Refresh and try again.");
        if (await IsPeriodClosedAsync(db, companyId, filing.PeriodStart, filing.PeriodEnd, cancellationToken)) return TransactionResult.Failure("Reopen the payroll close period before reopening its filing.");
        filing.Status = "Draft"; filing.ApprovedByUserId = null; filing.ApprovedAtUtc = null; filing.ReopenedByUserId = ResolveUserId(); filing.ReopenedAtUtc = DateTimeOffset.UtcNow; filing.ReopenReason = request.Reason.Trim(); filing.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-filing.reopened", "PayrollFiling", filing.Id, new { filing.FormCode, filing.TaxYear, filing.Quarter, filing.ReopenReason });
        await db.SaveChangesAsync(cancellationToken);
        return TransactionResult.Success(filing.Id);
    }

    public async Task<TransactionResult> ClosePeriodAsync(ClosePayrollPeriodRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPost)) return TransactionResult.Failure("You are not authorized to close payroll periods.");
        if (!TryResolveClosePeriod(request.PeriodType, request.TaxYear, request.Quarter, out var periodType, out var start, out var end, out var error)) return TransactionResult.Failure(error);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.PayrollClosePeriods.AnyAsync(item => item.CompanyId == companyId && item.PeriodType == periodType && item.TaxYear == request.TaxYear && item.Quarter == request.Quarter && item.Status == "Closed", cancellationToken)) return TransactionResult.Failure("This payroll period is already closed.");
        if (await db.PayrollRuns.AnyAsync(run => run.CompanyId == companyId && run.PayDate >= start && run.PayDate <= end && (run.Status == "Draft" || run.Status == "Approved"), cancellationToken)) return TransactionResult.Failure("Cancel or post every draft and approved payroll run in the period before closing it.");
        var requiredForms = periodType == "Quarter" ? new[] { "941" } : new[] { "940", "W2" };
        foreach (var form in requiredForms)
            if (!await db.PayrollFilings.AnyAsync(item => item.CompanyId == companyId && item.FormCode == form && item.TaxYear == request.TaxYear && item.Quarter == (periodType == "Quarter" ? request.Quarter : null) && item.Status == "Approved", cancellationToken)) return TransactionResult.Failure($"Approve Form {form} filing data for this period before closing it.");
        if (periodType == "Year")
        {
            var closedQuarters = await db.PayrollClosePeriods.CountAsync(item => item.CompanyId == companyId && item.PeriodType == "Quarter" && item.TaxYear == request.TaxYear && item.Status == "Closed", cancellationToken);
            if (closedQuarters != 4) return TransactionResult.Failure("Close all four payroll quarters before closing the payroll year.");
        }
        var existing = await db.PayrollClosePeriods.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.PeriodType == periodType && item.TaxYear == request.TaxYear && item.Quarter == request.Quarter, cancellationToken);
        var closePeriod = existing ?? new PayrollClosePeriod { Id = Guid.NewGuid(), CompanyId = companyId, PeriodType = periodType, TaxYear = request.TaxYear, Quarter = request.Quarter, PeriodKey = BuildPeriodKey(request.TaxYear, request.Quarter), PeriodStart = start, PeriodEnd = end };
        if (existing is null) db.PayrollClosePeriods.Add(closePeriod);
        closePeriod.Status = "Closed"; closePeriod.ClosedByUserId = ResolveUserId(); closePeriod.ClosedAtUtc = DateTimeOffset.UtcNow; closePeriod.ReopenedByUserId = null; closePeriod.ReopenedAtUtc = null; closePeriod.ReopenReason = string.Empty; closePeriod.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-period.closed", "PayrollClosePeriod", closePeriod.Id, new { closePeriod.PeriodType, closePeriod.TaxYear, closePeriod.Quarter, requiredForms });
        await db.SaveChangesAsync(cancellationToken);
        return TransactionResult.Success(closePeriod.Id);
    }

    public async Task<TransactionResult> ReopenPeriodAsync(ReopenPayrollPeriodRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollReverse)) return TransactionResult.Failure("You are not authorized to reopen payroll periods.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A payroll period reopen reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var period = await db.PayrollClosePeriods.SingleOrDefaultAsync(item => item.Id == request.PeriodId && item.CompanyId == companyId, cancellationToken);
        if (period is null) return TransactionResult.Failure("Payroll close period not found.");
        if (period.Status != "Closed") return TransactionResult.Failure("Only a closed payroll period can be reopened.");
        if (!string.Equals(period.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll close period changed after it was opened. Refresh and try again.");
        if (period.PeriodType == "Quarter" && await db.PayrollClosePeriods.AnyAsync(item => item.CompanyId == companyId && item.PeriodType == "Year" && item.TaxYear == period.TaxYear && item.Status == "Closed", cancellationToken)) return TransactionResult.Failure("Reopen the payroll year before reopening one of its quarters.");
        period.Status = "Reopened"; period.ReopenedByUserId = ResolveUserId(); period.ReopenedAtUtc = DateTimeOffset.UtcNow; period.ReopenReason = request.Reason.Trim(); period.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, "payroll-period.reopened", "PayrollClosePeriod", period.Id, new { period.PeriodType, period.TaxYear, period.Quarter, period.ReopenReason });
        await db.SaveChangesAsync(cancellationToken);
        return TransactionResult.Success(period.Id);
    }

    private static Form941Data Build941(FilingSource source, int year, int quarter)
    {
        var fit = source.TaxLines.Where(line => line.ObligationCode is "US-FIT" or "FEDERAL-ADDITIONAL-WITHHOLDING").Sum(line => line.EmployeeAmount);
        var socialWages = source.TaxLines.Where(line => line.ObligationCode == "US-OASDI-EMPLOYEE").Sum(line => line.TaxableWages);
        var socialTax = source.TaxLines.Where(line => line.ObligationCode.StartsWith("US-OASDI-", StringComparison.Ordinal)).Sum(line => line.EmployeeAmount + line.EmployerAmount);
        var medicareWages = source.TaxLines.Where(line => line.ObligationCode == "US-MEDICARE-EMPLOYEE").Sum(line => line.TaxableWages);
        var medicareTax = source.TaxLines.Where(line => line.ObligationCode.StartsWith("US-MEDICARE-", StringComparison.Ordinal)).Sum(line => line.EmployeeAmount + line.EmployerAmount);
        var additionalLines = source.TaxLines.Where(line => line.ObligationCode == "US-ADDITIONAL-MEDICARE").ToArray();
        var total = Round(fit + socialTax + medicareTax + additionalLines.Sum(line => line.EmployeeAmount));
        var deposits = source.Deposits.Where(item => Is941Obligation(item.ObligationCode)).Sum(item => item.Amount);
        var liabilities = source.TaxLines.Where(line => Is941Obligation(line.ObligationCode)).GroupBy(line => source.PayDateByEmployeeLine[line.PayrollRunEmployeeLineId]).OrderBy(group => group.Key).Select(group => new Form941LiabilityDay(group.Key, Round(group.Sum(line => line.EmployeeAmount + line.EmployerAmount)))).ToArray();
        var employeeCountDate = new DateOnly(year, quarter * 3, 12);
        var measurementRunIds = source.Runs.Where(run => run.PeriodStart <= employeeCountDate && run.PeriodEnd >= employeeCountDate).Select(run => run.Id).ToHashSet();
        return new Form941Data(TaxYear: year, Quarter: quarter, EmployerLegalName: source.Company.LegalName, EmployerEin: source.Company.TaxId,
            EmployeeCount: source.EmployeeLines.Where(line => measurementRunIds.Contains(line.PayrollRunId)).Select(line => line.EmployeeId).Distinct().Count(),
            WagesTipsAndOtherCompensation: Round(source.TaxLines.Where(line => line.ObligationCode == "US-FIT").Sum(line => line.TaxableWages)),
            FederalIncomeTaxWithheld: Round(fit), SocialSecurityWages: Round(socialWages), SocialSecurityTax: Round(socialTax),
            MedicareWagesAndTips: Round(medicareWages), MedicareTax: Round(medicareTax),
            AdditionalMedicareTaxableWages: Round(additionalLines.Sum(line => line.TaxableWages)), AdditionalMedicareTax: Round(additionalLines.Sum(line => line.EmployeeAmount)),
            TotalTaxesBeforeAdjustments: total, DepositsRecorded: Round(deposits), BalanceDue: Round(Math.Max(0, total - deposits)), TaxLiabilityByPayDate: liabilities);
    }

    private static Form940Data Build940(FilingSource source, int year)
    {
        var futa = source.TaxLines.Where(line => line.ObligationCode.EndsWith("FUTA", StringComparison.OrdinalIgnoreCase) || line.TaxType.Contains("FUTA", StringComparison.OrdinalIgnoreCase)).ToArray();
        var taxable = futa.Sum(line => line.TaxableWages);
        var tax = futa.Sum(line => line.EmployerAmount);
        var deposits = source.Deposits.Where(item => item.ObligationCode.EndsWith("FUTA", StringComparison.OrdinalIgnoreCase) || item.ObligationCode.Contains("FUTA", StringComparison.OrdinalIgnoreCase)).Sum(item => item.Amount);
        var payments = source.EmployeeLines.Sum(line => line.GrossPay);
        return new Form940Data(TaxYear: year, EmployerLegalName: source.Company.LegalName, EmployerEin: source.Company.TaxId,
            TotalPaymentsToEmployees: Round(payments), FutaTaxableWages: Round(taxable), PaymentsExemptOrAboveWageBase: Round(Math.Max(0, payments - taxable)),
            FutaTaxBeforeAdjustments: Round(tax), DepositsRecorded: Round(deposits), BalanceDue: Round(Math.Max(0, tax - deposits)));
    }

    private static W2PackageData BuildW2(FilingSource source, int year)
    {
        var taxByEmployeeLine = source.TaxLines.ToLookup(line => line.PayrollRunEmployeeLineId);
        var linesByEmployee = source.EmployeeLines.GroupBy(line => line.EmployeeId).OrderBy(group => source.Employees[group.Key].EmployeeNumber);
        var employees = new List<W2EmployeeData>();
        foreach (var group in linesByEmployee)
        {
            var employee = source.Employees[group.Key];
            var taxes = group.SelectMany(line => taxByEmployeeLine[line.Id]).ToArray();
            var jurisdictions = taxes.Where(line => line.JurisdictionCode is not ("US" or "FEDERAL") && !line.JurisdictionName.Equals("Federal", StringComparison.OrdinalIgnoreCase) && line.EmployeeAmount != 0 && (line.TaxType.Contains("withholding", StringComparison.OrdinalIgnoreCase) || line.TaxType.Contains("income tax", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(line => new { line.JurisdictionCode, line.JurisdictionName }).Select(item => new W2JurisdictionAmount(item.Key.JurisdictionCode, item.Key.JurisdictionName, Round(item.Sum(line => line.TaxableWages)), Round(item.Sum(line => line.EmployeeAmount)))).ToArray();
            employees.Add(new W2EmployeeData(employee.Id, employee.EmployeeNumber, $"{employee.FirstName} {employee.LastName}".Trim(), employee.SocialSecurityNumber,
                employee.AddressLine1, employee.AddressLine2, employee.PostalCode,
                Round(taxes.Where(line => line.ObligationCode == "US-FIT").Sum(line => line.TaxableWages)),
                Round(taxes.Where(line => line.ObligationCode is "US-FIT" or "FEDERAL-ADDITIONAL-WITHHOLDING").Sum(line => line.EmployeeAmount)),
                Round(taxes.Where(line => line.ObligationCode == "US-OASDI-EMPLOYEE").Sum(line => line.TaxableWages)),
                Round(taxes.Where(line => line.ObligationCode == "US-OASDI-EMPLOYEE").Sum(line => line.EmployeeAmount)),
                Round(taxes.Where(line => line.ObligationCode == "US-MEDICARE-EMPLOYEE").Sum(line => line.TaxableWages)),
                Round(taxes.Where(line => line.ObligationCode is "US-MEDICARE-EMPLOYEE" or "US-ADDITIONAL-MEDICARE").Sum(line => line.EmployeeAmount)), jurisdictions,
                employee.FirstName, "", employee.LastName, employee.AddressCity, employee.AddressState));
        }
        return new W2PackageData(TaxYear: year, EmployerLegalName: source.Company.LegalName, EmployerEin: source.Company.TaxId, Employees: employees,
            W3Box1Total: Round(employees.Sum(item => item.Box1WagesTipsOtherCompensation)), W3Box2Total: Round(employees.Sum(item => item.Box2FederalIncomeTaxWithheld)),
            W3Box3Total: Round(employees.Sum(item => item.Box3SocialSecurityWages)), W3Box4Total: Round(employees.Sum(item => item.Box4SocialSecurityTaxWithheld)),
            W3Box5Total: Round(employees.Sum(item => item.Box5MedicareWagesAndTips)), W3Box6Total: Round(employees.Sum(item => item.Box6MedicareTaxWithheld)));
    }

    private static W2EmployeeData ZeroW2Amounts(W2EmployeeData source) => source with
    {
        Box1WagesTipsOtherCompensation = 0, Box2FederalIncomeTaxWithheld = 0,
        Box3SocialSecurityWages = 0, Box4SocialSecurityTaxWithheld = 0,
        Box5MedicareWagesAndTips = 0, Box6MedicareTaxWithheld = 0,
        StateAndLocalAmounts = []
    };

    private static bool FederalOrIdentityChanged(W2EmployeeData previous, W2EmployeeData correct) =>
        previous.EmployeeNumber != correct.EmployeeNumber || previous.EmployeeName != correct.EmployeeName ||
        Digits(previous.SocialSecurityNumber) != Digits(correct.SocialSecurityNumber) ||
        previous.Box1WagesTipsOtherCompensation != correct.Box1WagesTipsOtherCompensation ||
        previous.Box2FederalIncomeTaxWithheld != correct.Box2FederalIncomeTaxWithheld ||
        previous.Box3SocialSecurityWages != correct.Box3SocialSecurityWages ||
        previous.Box4SocialSecurityTaxWithheld != correct.Box4SocialSecurityTaxWithheld ||
        previous.Box5MedicareWagesAndTips != correct.Box5MedicareWagesAndTips ||
        previous.Box6MedicareTaxWithheld != correct.Box6MedicareTaxWithheld;

    private static string CanonicalW2Jurisdictions(W2EmployeeData value) => JsonSerializer.Serialize(value.StateAndLocalAmounts.OrderBy(item => item.JurisdictionCode).ThenBy(item => item.JurisdictionName));
    private static decimal[] SumW2(IEnumerable<W2EmployeeData> employees)
    {
        var values = employees.ToArray();
        return [Round(values.Sum(item => item.Box1WagesTipsOtherCompensation)), Round(values.Sum(item => item.Box2FederalIncomeTaxWithheld)),
            Round(values.Sum(item => item.Box3SocialSecurityWages)), Round(values.Sum(item => item.Box4SocialSecurityTaxWithheld)),
            Round(values.Sum(item => item.Box5MedicareWagesAndTips)), Round(values.Sum(item => item.Box6MedicareTaxWithheld))];
    }

    private static bool Is941Obligation(string code) => code is "US-FIT" or "FEDERAL-ADDITIONAL-WITHHOLDING" or "US-OASDI-EMPLOYEE" or "US-OASDI-EMPLOYER" or "US-MEDICARE-EMPLOYEE" or "US-MEDICARE-EMPLOYER" or "US-ADDITIONAL-MEDICARE";
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Digits(string value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string BuildSummaryJson(string formCode, FilingSource source) => JsonSerializer.Serialize(new { formCode, payrollRunCount = source.Runs.Count, employeeCount = source.EmployeeLines.Select(line => line.EmployeeId).Distinct().Count(), grossPayroll = Round(source.EmployeeLines.Sum(line => line.GrossPay)), employeeTaxes = Round(source.TaxLines.Sum(line => line.EmployeeAmount)), employerTaxes = Round(source.TaxLines.Sum(line => line.EmployerAmount)), depositsRecorded = Round(source.Deposits.Sum(item => item.Amount)) });

    private static IReadOnlyDictionary<string, decimal> Form941Values(Form941Data data) => new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        ["EmployeeCount"] = data.EmployeeCount,
        ["WagesTipsAndOtherCompensation"] = data.WagesTipsAndOtherCompensation,
        ["FederalIncomeTaxWithheld"] = data.FederalIncomeTaxWithheld,
        ["SocialSecurityWages"] = data.SocialSecurityWages,
        ["SocialSecurityTax"] = data.SocialSecurityTax,
        ["MedicareWagesAndTips"] = data.MedicareWagesAndTips,
        ["MedicareTax"] = data.MedicareTax,
        ["AdditionalMedicareTaxableWages"] = data.AdditionalMedicareTaxableWages,
        ["AdditionalMedicareTax"] = data.AdditionalMedicareTax,
        ["TotalTaxesBeforeAdjustments"] = data.TotalTaxesBeforeAdjustments
    };

    private static IReadOnlyDictionary<string, decimal> CorrectionValues(Form941XData data)
    {
        var values = data.Lines?.ToDictionary(item => item.Code, item => item.CorrectedAmount, StringComparer.Ordinal) ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (!values.ContainsKey("TotalTaxesBeforeAdjustments")) values["TotalTaxesBeforeAdjustments"] = data.TotalTaxDifference;
        return values;
    }

    private static IReadOnlyList<Form941CorrectionLine> Build941CorrectionLines(IReadOnlyDictionary<string, decimal> baseline, IReadOnlyDictionary<string, decimal> corrected)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EmployeeCount"] = "Number of employees", ["WagesTipsAndOtherCompensation"] = "Wages, tips, and other compensation",
            ["FederalIncomeTaxWithheld"] = "Federal income tax withheld", ["SocialSecurityWages"] = "Taxable Social Security wages",
            ["SocialSecurityTax"] = "Social Security tax", ["MedicareWagesAndTips"] = "Taxable Medicare wages and tips",
            ["MedicareTax"] = "Medicare tax", ["AdditionalMedicareTaxableWages"] = "Wages subject to Additional Medicare Tax",
            ["AdditionalMedicareTax"] = "Additional Medicare Tax withholding", ["TotalTaxesBeforeAdjustments"] = "Total taxes before adjustments"
        };
        return labels.Select(item => new Form941CorrectionLine(item.Key, item.Value, Round(baseline.GetValueOrDefault(item.Key)), Round(corrected.GetValueOrDefault(item.Key)), Round(corrected.GetValueOrDefault(item.Key) - baseline.GetValueOrDefault(item.Key)))).ToArray();
    }

    private static bool ValidFederalWithholdingCorrection(decimal difference, int taxYear, DateOnly discoveredOn, string correctionType)
    {
        if (difference == 0m) return correctionType == "None";
        if (correctionType is not ("SameYearRepaid" or "AdministrativeError" or "Section3509")) return false;
        return discoveredOn.Year == taxYear ? correctionType is "SameYearRepaid" or "AdministrativeError" or "Section3509" : correctionType is "AdministrativeError" or "Section3509";
    }

    private static string BuildSourceDigest(FilingSource source)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            company = new { source.Company.Id, source.Company.LegalName, source.Company.TaxId },
            runs = source.Runs.OrderBy(run => run.Id).Select(run => new { run.Id, run.Status, run.PayDate, run.ConcurrencyToken }),
            employees = source.Employees.Values.OrderBy(employee => employee.Id).Select(employee => new { employee.Id, employee.EmployeeNumber, employee.FirstName, employee.LastName, employee.SocialSecurityNumber, employee.AddressLine1, employee.AddressLine2, employee.AddressCity, employee.AddressState, employee.PostalCode, employee.ConcurrencyToken }),
            employeeLines = source.EmployeeLines.OrderBy(line => line.Id).Select(line => new { line.Id, line.PayrollRunId, line.EmployeeId, line.GrossPay, line.TaxableWages, line.PreTaxDeductions, line.EmployeeWithholdings, line.PostTaxDeductions, line.EmployerPayrollTaxes, line.EmployerBenefitContributions, line.NetPay }),
            taxLines = source.TaxLines.OrderBy(line => line.Id).Select(line => new { line.Id, line.PayrollRunEmployeeLineId, line.ObligationCode, line.JurisdictionCode, line.TaxableWages, line.EmployeeAmount, line.EmployerAmount, line.ContentVersion }),
            deposits = source.Deposits.OrderBy(item => item.ApplicationId).Select(item => new { item.ApplicationId, item.ObligationCode, item.Amount })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static async Task<FilingSource> LoadSourceAsync(BrassLedgerDbContext db, Guid companyId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var company = await db.Companies.AsNoTracking().SingleAsync(item => item.Id == companyId, cancellationToken);
        var runs = await db.PayrollRuns.AsNoTracking().Where(run => run.CompanyId == companyId && run.Status == "Posted" && run.PayDate >= start && run.PayDate <= end).OrderBy(run => run.PayDate).ThenBy(run => run.Id).ToListAsync(cancellationToken);
        var runIds = runs.Select(run => run.Id).ToArray();
        var employeeLines = runIds.Length == 0 ? [] : await db.PayrollRunEmployeeLines.AsNoTracking().Where(line => runIds.Contains(line.PayrollRunId)).ToListAsync(cancellationToken);
        var employeeLineIds = employeeLines.Select(line => line.Id).ToArray();
        var taxLines = employeeLineIds.Length == 0 ? [] : await db.PayrollTaxLines.AsNoTracking().Where(line => employeeLineIds.Contains(line.PayrollRunEmployeeLineId)).ToListAsync(cancellationToken);
        var employeeIds = employeeLines.Select(line => line.EmployeeId).Distinct().ToArray();
        var employees = employeeIds.Length == 0 ? new Dictionary<Guid, Employee>() : await db.Employees.AsNoTracking().Where(employee => employee.CompanyId == companyId && employeeIds.Contains(employee.Id)).ToDictionaryAsync(employee => employee.Id, cancellationToken);
        var liabilities = runIds.Length == 0 ? [] : await db.PayrollLiabilities.AsNoTracking().Where(item => item.CompanyId == companyId && runIds.Contains(item.PayrollRunId) && item.SourceType == "Tax").ToListAsync(cancellationToken);
        var liabilityIds = liabilities.Select(item => item.Id).ToArray();
        var paymentIds = liabilityIds.Length == 0 ? [] : await db.PayrollLiabilityPaymentApplications.AsNoTracking().Where(item => liabilityIds.Contains(item.PayrollLiabilityId)).Select(item => item.PayrollLiabilityPaymentId).Distinct().ToArrayAsync(cancellationToken);
        var postedPaymentIds = paymentIds.Length == 0 ? [] : await db.PayrollLiabilityPayments.AsNoTracking().Where(item => item.CompanyId == companyId && paymentIds.Contains(item.Id) && item.Status == "Posted").Select(item => item.Id).ToArrayAsync(cancellationToken);
        var applications = postedPaymentIds.Length == 0 ? [] : await db.PayrollLiabilityPaymentApplications.AsNoTracking().Where(item => postedPaymentIds.Contains(item.PayrollLiabilityPaymentId) && liabilityIds.Contains(item.PayrollLiabilityId)).ToListAsync(cancellationToken);
        var liabilityById = liabilities.ToDictionary(item => item.Id);
        var deposits = applications.Select(item => new FilingDeposit(item.Id, liabilityById[item.PayrollLiabilityId].ObligationCode, item.Amount)).ToArray();
        var payDateByRun = runs.ToDictionary(run => run.Id, run => run.PayDate);
        return new FilingSource(company, runs, employeeLines, taxLines, employees, deposits, employeeLines.ToDictionary(line => line.Id, line => payDateByRun[line.PayrollRunId]));
    }

    private static bool TryResolvePeriod(string inputFormCode, int year, int? quarter, out string formCode, out DateOnly start, out DateOnly end, out string error)
    {
        formCode = inputFormCode?.Trim().ToUpperInvariant().Replace("-", string.Empty).Replace("/", string.Empty).Replace(" ", string.Empty) ?? string.Empty;
        if (formCode == "W2W3") formCode = "W2";
        start = default; end = default; error = string.Empty;
        if (year != 2026) { error = "Only the verified 2026 federal filing mappings are currently available."; return false; }
        if (formCode == "941")
        {
            if (!quarter.HasValue || quarter.Value is < 1 or > 4) { error = "Form 941 requires quarter 1 through 4."; return false; }
            start = new DateOnly(year, (quarter.Value - 1) * 3 + 1, 1); end = start.AddMonths(3).AddDays(-1); return true;
        }
        if (formCode is not ("940" or "W2")) { error = "Payroll filing form must be 941, 940, or W2/W3."; return false; }
        if (quarter.HasValue) { error = $"Form {formCode} is annual and cannot have a quarter."; return false; }
        start = new DateOnly(year, 1, 1); end = new DateOnly(year, 12, 31); return true;
    }

    private static bool TryResolveClosePeriod(string inputType, int year, int? quarter, out string periodType, out DateOnly start, out DateOnly end, out string error)
    {
        periodType = inputType?.Trim() ?? string.Empty; start = default; end = default; error = string.Empty;
        if (year != 2026) { error = "Only the verified 2026 federal filing mappings are currently available."; return false; }
        if (periodType.Equals("Quarter", StringComparison.OrdinalIgnoreCase))
        {
            periodType = "Quarter";
            if (!quarter.HasValue || quarter.Value is < 1 or > 4) { error = "A quarterly payroll close requires quarter 1 through 4."; return false; }
            start = new DateOnly(year, (quarter.Value - 1) * 3 + 1, 1); end = start.AddMonths(3).AddDays(-1); return true;
        }
        if (!periodType.Equals("Year", StringComparison.OrdinalIgnoreCase) || quarter.HasValue) { error = "Payroll close period must be a quarter with quarter 1 through 4, or a year without a quarter."; return false; }
        periodType = "Year"; start = new DateOnly(year, 1, 1); end = new DateOnly(year, 12, 31); return true;
    }

    private static string SourceUrl(string formCode) => formCode switch { "941" => Form941Source, "940" => Form940Source, _ => W2Source };
    private static string BuildPeriodKey(int year, int? quarter) => quarter.HasValue ? $"{year}-Q{quarter.Value}" : $"{year}-YEAR";
    private static PayrollFilingSnapshot ToSnapshot(PayrollFiling filing) => new(filing.Id, filing.FormCode, filing.TaxYear, filing.Quarter, filing.PeriodStart, filing.PeriodEnd, filing.Status, ParseElement(filing.DataJson), ParseElement(filing.SummaryJson), filing.SourceDigestSha256, filing.OfficialSourceUrl, filing.ContentVersion, filing.PreparedAtUtc, filing.ApprovedAtUtc, !string.IsNullOrWhiteSpace(filing.ApprovedSourceDigestSha256), filing.ConcurrencyToken);
    private static PayrollFilingCorrectionSnapshot ToSnapshot(PayrollFilingCorrection correction) => new(correction.Id, correction.OriginalPayrollFilingId, correction.Sequence, correction.FormCode, correction.TaxYear, correction.Quarter, correction.Process, correction.DiscoveredOn, correction.Explanation, correction.FederalWithholdingCorrectionType, correction.EmployeeCertificationCode, correction.EmployeeCertificationEvidenceReference, correction.WageStatementsCorrected, correction.WageStatementEvidenceReference, correction.Status, ParseElement(correction.DataJson), correction.CorrectedSourceDigestSha256, correction.OfficialSourceUrl, correction.ContentVersion, correction.PreparedAtUtc, correction.ApprovedAtUtc, correction.VoidedAtUtc, correction.VoidReason, correction.ConcurrencyToken);
    private static PayrollClosePeriodSnapshot ToSnapshot(PayrollClosePeriod item) => new(item.Id, item.PeriodType, item.TaxYear, item.Quarter, item.PeriodStart, item.PeriodEnd, item.Status, item.ClosedAtUtc, item.ReopenedAtUtc, item.ReopenReason, item.ConcurrencyToken);
    private static JsonElement ParseElement(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static async Task<bool> IsPeriodClosedAsync(BrassLedgerDbContext db, Guid companyId, DateOnly start, DateOnly end, CancellationToken cancellationToken) => await db.PayrollClosePeriods.AnyAsync(item => item.CompanyId == companyId && item.Status == "Closed" && item.PeriodStart <= end && item.PeriodEnd >= start, cancellationToken);

    private void RequirePayrollAccess() { if (httpContextAccessor.HttpContext is not null && !new[] { BrassLedgerPermissions.PayrollManage, BrassLedgerPermissions.PayrollPrepare, BrassLedgerPermissions.PayrollApprove, BrassLedgerPermissions.PayrollPost, BrassLedgerPermissions.PayrollReverse, BrassLedgerPermissions.PayrollSensitiveData }.Any(HasPermission)) throw new UnauthorizedAccessException("You are not authorized to access payroll filing periods."); }
    private void RequirePermission(string permission, string action) { if (!HasPermission(permission)) throw new UnauthorizedAccessException($"You are not authorized to {action}."); }
    private bool HasPermission(string permission) => httpContextAccessor.HttpContext is null || httpContextAccessor.HttpContext.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission);
    private Guid? ResolveUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken cancellationToken) { var context = httpContextAccessor.HttpContext; var claim = context?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType); if (context is not null && !Guid.TryParse(claim, out _)) throw new UnauthorizedAccessException("An authenticated company context is required."); if (Guid.TryParse(claim, out var id) && await db.Companies.AnyAsync(item => item.Id == id, cancellationToken)) return id; return await db.Companies.OrderBy(item => item.Name).Select(item => item.Id).FirstAsync(cancellationToken); }
    private void AddAudit(BrassLedgerDbContext db, Guid companyId, string action, string entityType, Guid entityId, object detail) => db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = entityType, EntityId = entityId, DetailJson = JsonSerializer.Serialize(detail), OccurredAtUtc = DateTimeOffset.UtcNow });

    private sealed record FilingDeposit(Guid ApplicationId, string ObligationCode, decimal Amount);
    private sealed record FilingSource(Company Company, IReadOnlyList<PayrollRun> Runs, IReadOnlyList<PayrollRunEmployeeLine> EmployeeLines, IReadOnlyList<PayrollTaxLine> TaxLines, IReadOnlyDictionary<Guid, Employee> Employees, IReadOnlyList<FilingDeposit> Deposits, IReadOnlyDictionary<Guid, DateOnly> PayDateByEmployeeLine);
}
