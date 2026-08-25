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

public sealed class SsaWageFileService(IDbContextFactory<BrassLedgerDbContext> dbContextFactory, IHttpContextAccessor httpContextAccessor) : ISsaWageFileService
{
    public const string SupportedLayoutCode = "EFW2C-1024-RCA-RCE-RCW-RCT-RCF";
    private static readonly IReadOnlyDictionary<int, DateOnly> SupportedSpecificationPublicationDates = new Dictionary<int, DateOnly>
    {
        [2025] = new(2026, 1, 20),
        [2026] = new(2026, 7, 10)
    };

    public async Task<SsaWageFileWorkspace> GetAsync(CancellationToken cancellationToken = default)
    {
        RequireSensitive(); await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var configurations = await db.PayrollSsaWageFileConfigurations.AsNoTracking().Where(item => item.CompanyId == companyId).OrderByDescending(item => item.SpecificationTaxYear).ToListAsync(cancellationToken);
        var files = await db.PayrollSsaWageFiles.AsNoTracking().Where(item => item.CompanyId == companyId).ToListAsync(cancellationToken);
        return new(configurations.Select(ToSnapshot).ToArray(), files.OrderByDescending(item => item.GeneratedAtUtc).Select(ToSnapshot).ToArray());
    }

    public async Task<TransactionResult> SaveConfigurationAsync(SaveSsaWageFileConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        if (!Has(BrassLedgerPermissions.PayrollPrepare) || !Has(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to prepare protected SSA wage-file settings.");
        if (request.IsApproved && !Has(BrassLedgerPermissions.PayrollApprove)) return TransactionResult.Failure("Payroll approval permission is required to approve an SSA specification.");
        var sourceUrl = request.OfficialSpecificationUrl?.Trim() ?? string.Empty; var digest = request.OfficialSpecificationSha256?.Trim().ToLowerInvariant() ?? string.Empty; var notes = request.ReviewNotes?.Trim() ?? string.Empty;
        if (request.SpecificationTaxYear is < 1994 or > 2100) return TransactionResult.Failure("Enter a valid SSA specification tax year.");
        var expectedFile = $"{request.SpecificationTaxYear % 100:00}efw2c.pdf";
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host.Equals("ssa.gov", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".ssa.gov", StringComparison.OrdinalIgnoreCase)) || !uri.AbsolutePath.EndsWith(expectedFile, StringComparison.OrdinalIgnoreCase)) return TransactionResult.Failure($"Use the exact official SSA {request.SpecificationTaxYear} EFW2C PDF URL ending in {expectedFile}.");
        if (request.SourceRetrievedOn == default || request.SourceRetrievedOn > DateOnly.FromDateTime(DateTime.Today)) return TransactionResult.Failure("Enter the actual specification retrieval date; it cannot be in the future.");
        if (request.IsActive && !request.IsApproved) return TransactionResult.Failure("Only an approved SSA specification can be active.");
        if (request.IsApproved)
        {
            if (!SupportedSpecificationPublicationDates.TryGetValue(request.SpecificationTaxYear, out var publicationDate)) return TransactionResult.Failure($"The encoder has no reviewed layout for SSA tax year {request.SpecificationTaxYear}; retain that specification as an inactive draft until its layout is implemented and verified.");
            if (request.SourceRetrievedOn < publicationDate) return TransactionResult.Failure($"The reviewed SSA tax year {request.SpecificationTaxYear} publication was issued on {publicationDate:yyyy-MM-dd}; the retrieval date cannot precede it.");
        }
        if (request.IsApproved && (request.LayoutCompatibilityCode != SupportedLayoutCode || digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character)) || notes.Length < 30)) return TransactionResult.Failure("Approval requires the supported reviewed layout code, the official PDF SHA-256, and substantive review notes.");
        var submitter = ToSubmitter(request);
        var validation = SsaEfw2cFileBuilder.Build(new W2cPackageData(TaxYear: request.SpecificationTaxYear, EmployerLegalName: "VALIDATION EMPLOYER", EmployerEin: "123456789", Employees: [ValidationEmployee()]), submitter);
        if (!validation.Succeeded) return TransactionResult.Failure(string.Join(" ", validation.Errors));
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken); PayrollSsaWageFileConfiguration entity;
        if (request.Id.HasValue) { entity = await db.PayrollSsaWageFileConfigurations.SingleOrDefaultAsync(item => item.Id == request.Id && item.CompanyId == companyId, cancellationToken) ?? new(); if (entity.Id == Guid.Empty) return TransactionResult.Failure("SSA wage-file configuration not found."); if (entity.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The SSA configuration changed after it was opened. Refresh and try again."); }
        else { if (await db.PayrollSsaWageFileConfigurations.AnyAsync(item => item.CompanyId == companyId && item.SpecificationTaxYear == request.SpecificationTaxYear, cancellationToken)) return TransactionResult.Failure("This company already has an SSA configuration for that tax year."); entity = new() { Id = Guid.NewGuid(), CompanyId = companyId }; db.Add(entity); }
        Apply(entity, request, sourceUrl, digest, notes); entity.ApprovedByUserId = request.IsApproved ? ResolveUserId() : null; entity.ApprovedAtUtc = request.IsApproved ? DateTimeOffset.UtcNow : null; entity.ConcurrencyToken = Guid.NewGuid().ToString("N");
        Audit(db, companyId, request.Id.HasValue ? "ssa-wage-file.configuration.updated" : "ssa-wage-file.configuration.created", entity.Id, new { entity.SpecificationTaxYear, entity.SpecificationVersion, entity.LayoutCompatibilityCode, entity.OfficialSpecificationUrl, entity.OfficialSpecificationSha256, entity.SourceRetrievedOn, entity.ReviewNotes, entity.IsApproved, entity.IsActive });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The SSA configuration changed while saving. Refresh and try again."); } catch (DbUpdateException) { return TransactionResult.Failure("The SSA specification tax year conflicts with an existing company configuration."); }
        return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> GenerateAsync(GenerateSsaWageFileRequest request, CancellationToken cancellationToken = default)
    {
        if (!Has(BrassLedgerPermissions.PayrollPrepare) || !Has(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to generate protected SSA wage files.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var correction = await db.PayrollFilingCorrections.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.PayrollFilingCorrectionId && item.CompanyId == companyId && item.FormCode == "W-2c/W-3c" && item.Status == "Approved", cancellationToken);
        if (correction is null) return TransactionResult.Failure("Select an approved source-locked W-2c/W-3c correction from this company.");
        if (await db.PayrollSsaWageFiles.AnyAsync(item => item.CompanyId == companyId && item.PayrollFilingCorrectionId == correction.Id, cancellationToken)) return TransactionResult.Failure("An immutable SSA wage file already exists for this correction.");
        var configuration = await db.PayrollSsaWageFileConfigurations.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == companyId && item.SpecificationTaxYear == correction.TaxYear && item.IsApproved && item.IsActive, cancellationToken);
        if (configuration is null) return TransactionResult.Failure($"No approved active SSA EFW2C specification exists for tax year {correction.TaxYear}; do not reuse another year's layout.");
        var package = JsonSerializer.Deserialize<W2cPackageData>(correction.DataJson) ?? new(); var built = SsaEfw2cFileBuilder.Build(package, ToSubmitter(configuration)); if (!built.Succeeded) return TransactionResult.Failure(string.Join(" ", built.Errors));
        var contentHash = Hash(built.Content); var sourceHash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { correctionId = correction.Id, correction.CorrectedSourceDigestSha256, correction.DataJson, configurationId = configuration.Id, configuration.ConcurrencyToken, configuration.OfficialSpecificationSha256 })));
        var file = new PayrollSsaWageFile { Id = Guid.NewGuid(), CompanyId = companyId, PayrollFilingCorrectionId = correction.Id, PayrollSsaWageFileConfigurationId = configuration.Id, TaxYear = correction.TaxYear, FileName = $"EFW2C-{correction.TaxYear}-{correction.Sequence}.txt", ContentBase64 = Convert.ToBase64String(built.Content), ContentSha256 = contentHash, SourceDigestSha256 = sourceHash, SpecificationVersion = configuration.SpecificationVersion, Status = "GeneratedForAccuWage", RecordCount = built.RecordCount, EmployeeRecordCount = built.EmployeeRecordCount, GeneratedByUserId = ResolveUserId(), GeneratedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") }; db.Add(file);
        Audit(db, companyId, "ssa-wage-file.generated", file.Id, new { correction.Id, file.TaxYear, file.FileName, file.ContentSha256, file.SourceDigestSha256, file.SpecificationVersion, file.RecordCount, file.EmployeeRecordCount, file.Status });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return TransactionResult.Failure("An SSA wage file was already generated for this correction."); } return TransactionResult.Success(file.Id);
    }

    public async Task<TransactionResult> RecordValidationAsync(RecordSsaWageFileValidationRequest request, CancellationToken cancellationToken = default)
    {
        if (!Has(BrassLedgerPermissions.PayrollApprove) || !Has(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to record AccuWage review of protected wage files.");
        var evidence = request.EvidenceReference?.Trim() ?? string.Empty; var notes = request.Notes?.Trim() ?? string.Empty; if (evidence.Length < 5 || notes.Length < 20) return TransactionResult.Failure("Retain the AccuWage evidence reference and substantive validation notes.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken); var file = await db.PayrollSsaWageFiles.SingleOrDefaultAsync(item => item.Id == request.FileId && item.CompanyId == companyId, cancellationToken);
        if (file is null) return TransactionResult.Failure("SSA wage file not found."); if (file.Status != "GeneratedForAccuWage") return TransactionResult.Failure("AccuWage outcome has already been recorded for this immutable file."); if (file.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The wage file changed after it was opened. Refresh and try again.");
        file.Status = request.Passed ? "AccuWageValidated" : "AccuWageRejected"; file.AccuWageEvidenceReference = evidence; file.ValidationNotes = notes; file.ValidatedByUserId = ResolveUserId(); file.ValidatedAtUtc = DateTimeOffset.UtcNow; file.ConcurrencyToken = Guid.NewGuid().ToString("N"); Audit(db, companyId, "ssa-wage-file.accuwage-recorded", file.Id, new { file.Status, file.AccuWageEvidenceReference, file.ValidationNotes, file.ContentSha256 });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The wage file changed while validation was recorded. Refresh and try again."); } return TransactionResult.Success(file.Id);
    }

    public async Task<SsaWageFileDownload?> DownloadAsync(Guid fileId, CancellationToken cancellationToken = default) { RequireSensitive(); await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken); var file = await db.PayrollSsaWageFiles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == fileId && item.CompanyId == companyId, cancellationToken); if (file is null) return null; var bytes = Convert.FromBase64String(file.ContentBase64); if (Hash(bytes) != file.ContentSha256) throw new InvalidOperationException("Stored SSA wage-file integrity validation failed."); return new(file.FileName, bytes, file.ContentSha256, file.Status); }

    private static W2cEmployeeData ValidationEmployee() { var item = new W2EmployeeData(Guid.Empty, "TEST", "TEST EMPLOYEE", "123456789", "1 MAIN ST", "", "12345", 1, 1, 1, .06m, 1, .01m, [], "TEST", "", "EMPLOYEE", "ANYTOWN", "MI"); return new(item, item with { Box1WagesTipsOtherCompensation = 2 }, true, "Validation"); }
    private static SsaEfw2cSubmitter ToSubmitter(SaveSsaWageFileConfigurationRequest x) => new(x.SpecificationTaxYear, x.SpecificationVersion, x.OfficialSpecificationUrl, x.SubmitterEin, x.BsoUserId, x.SubmitterName, x.LocationAddress, x.DeliveryAddress, x.City, x.State, x.PostalCode, x.ContactName, x.ContactPhone, x.ContactEmail, x.PreparerCode, x.EmployerLocationAddress, x.EmployerDeliveryAddress, x.EmployerCity, x.EmployerState, x.EmployerPostalCode, x.EmployerContactName, x.EmployerContactPhone, x.EmployerContactEmail);
    private static SsaEfw2cSubmitter ToSubmitter(PayrollSsaWageFileConfiguration x) => new(x.SpecificationTaxYear, x.SpecificationVersion, x.OfficialSpecificationUrl, x.SubmitterEin, x.BsoUserId, x.SubmitterName, x.LocationAddress, x.DeliveryAddress, x.City, x.State, x.PostalCode, x.ContactName, x.ContactPhone, x.ContactEmail, x.PreparerCode, x.EmployerLocationAddress, x.EmployerDeliveryAddress, x.EmployerCity, x.EmployerState, x.EmployerPostalCode, x.EmployerContactName, x.EmployerContactPhone, x.EmployerContactEmail);
    private static SsaWageFileConfigurationSnapshot ToSnapshot(PayrollSsaWageFileConfiguration x) => new(x.Id, ToSubmitter(x), x.LayoutCompatibilityCode, x.OfficialSpecificationSha256, x.SourceRetrievedOn, x.ReviewNotes, x.IsApproved, x.ApprovedAtUtc, x.IsActive, x.ConcurrencyToken);
    private static SsaWageFileSnapshot ToSnapshot(PayrollSsaWageFile x) => new(x.Id, x.PayrollFilingCorrectionId, x.TaxYear, x.FileName, x.ContentSha256, x.SourceDigestSha256, x.SpecificationVersion, x.Status, x.RecordCount, x.EmployeeRecordCount, x.GeneratedAtUtc, x.ValidatedAtUtc, x.AccuWageEvidenceReference, x.ValidationNotes, x.ConcurrencyToken);
    private static void Apply(PayrollSsaWageFileConfiguration x, SaveSsaWageFileConfigurationRequest r, string url, string digest, string notes) { x.SpecificationTaxYear=r.SpecificationTaxYear; x.SpecificationVersion=r.SpecificationVersion.Trim(); x.LayoutCompatibilityCode=r.LayoutCompatibilityCode.Trim(); x.OfficialSpecificationUrl=url; x.OfficialSpecificationSha256=digest; x.SourceRetrievedOn=r.SourceRetrievedOn; x.ReviewNotes=notes; x.SubmitterEin=r.SubmitterEin.Trim(); x.BsoUserId=r.BsoUserId.Trim(); x.SubmitterName=r.SubmitterName.Trim(); x.LocationAddress=r.LocationAddress.Trim(); x.DeliveryAddress=r.DeliveryAddress.Trim(); x.City=r.City.Trim(); x.State=r.State.Trim().ToUpperInvariant(); x.PostalCode=r.PostalCode.Trim(); x.ContactName=r.ContactName.Trim(); x.ContactPhone=r.ContactPhone.Trim(); x.ContactEmail=r.ContactEmail.Trim(); x.PreparerCode=r.PreparerCode.Trim().ToUpperInvariant(); x.EmployerLocationAddress=r.EmployerLocationAddress.Trim(); x.EmployerDeliveryAddress=r.EmployerDeliveryAddress.Trim(); x.EmployerCity=r.EmployerCity.Trim(); x.EmployerState=r.EmployerState.Trim().ToUpperInvariant(); x.EmployerPostalCode=r.EmployerPostalCode.Trim(); x.EmployerContactName=r.EmployerContactName.Trim(); x.EmployerContactPhone=r.EmployerContactPhone.Trim(); x.EmployerContactEmail=r.EmployerContactEmail.Trim(); x.IsApproved=r.IsApproved; x.IsActive=r.IsActive; }
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private bool Has(string permission) => httpContextAccessor.HttpContext is null || httpContextAccessor.HttpContext.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission);
    private void RequireSensitive() { if (!Has(BrassLedgerPermissions.PayrollSensitiveData)) throw new UnauthorizedAccessException("You are not authorized to view protected SSA wage files."); }
    private Guid? ResolveUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken token) { var context=httpContextAccessor.HttpContext; var claim=context?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType); if (context is not null && !Guid.TryParse(claim, out _)) throw new UnauthorizedAccessException("An authenticated company context is required."); if (Guid.TryParse(claim, out var id) && await db.Companies.AnyAsync(x=>x.Id==id,token)) return id; return await db.Companies.OrderBy(x=>x.Name).Select(x=>x.Id).FirstAsync(token); }
    private void Audit(BrassLedgerDbContext db, Guid companyId, string action, Guid id, object detail) => db.BusinessAuditEntries.Add(new() { Id=Guid.NewGuid(), CompanyId=companyId, UserId=ResolveUserId(), Action=action, EntityType=nameof(PayrollSsaWageFile), EntityId=id, DetailJson=JsonSerializer.Serialize(detail), OccurredAtUtc=DateTimeOffset.UtcNow });
}
