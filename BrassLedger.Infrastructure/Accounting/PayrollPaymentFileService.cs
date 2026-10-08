using System.Globalization;
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

public sealed class PayrollPaymentFileService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : IPayrollPaymentFileService
{
    private const string NachaSource = "https://achdevguide.nacha.org/ach-file-overview";
    private const string NachaSpecificationVersion = "Nacha ACH Guide for Developers, retrieved 2026-08-25; bank acceptance required";

    public async Task<PayrollPaymentFileWorkspace> GetAsync(CancellationToken cancellationToken = default)
    {
        RequireSensitivePayroll();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var banks = await db.BankAccounts.AsNoTracking().Where(item => item.CompanyId == companyId).ToDictionaryAsync(item => item.Id, cancellationToken);
        var runs = await db.PayrollRuns.AsNoTracking().Where(item => item.CompanyId == companyId).ToDictionaryAsync(item => item.Id, cancellationToken);
        var origins = await db.PayrollBankOriginConfigurations.AsNoTracking().Where(item => item.CompanyId == companyId).OrderByDescending(item => item.EffectiveOn).ToListAsync(cancellationToken);
        var files = (await db.PayrollPaymentFiles.AsNoTracking().Where(item => item.CompanyId == companyId).ToListAsync(cancellationToken)).OrderByDescending(item => item.GeneratedAtUtc).ToList();
        return new PayrollPaymentFileWorkspace(
            origins.Select(item => new PayrollBankOriginConfigurationSnapshot(item.Id, item.BankAccountId, banks.GetValueOrDefault(item.BankAccountId)?.Name ?? "Bank unavailable", LastFour(item.ImmediateDestinationRoutingNumber), Mask(item.ImmediateOrigin), item.DestinationBankName, item.OriginName, Mask(item.CompanyIdentification), item.CompanyEntryDescription, item.OriginatingDfiIdentification, item.EffectiveOn, item.ExpiresOn, item.IsActive, item.IsBankValidated, item.BankValidationNotes, item.ConcurrencyToken)).ToArray(),
            files.Where(item => runs.ContainsKey(item.PayrollRunId)).Select(item => new PayrollPaymentFileSnapshot(item.Id, item.PayrollRunId, runs[item.PayrollRunId].Reference, item.Format, item.FileName, item.ContentType, item.ContentSha256, item.SourceDigestSha256, item.EntryCount, item.CreditTotal, item.RoutingHash, item.FileIdModifier, item.Status, item.SpecificationVersion, item.GeneratedAtUtc, item.VoidedAtUtc, item.VoidReason, item.ConcurrencyToken)).ToArray());
    }

    public async Task<TransactionResult> SaveBankOriginAsync(SavePayrollBankOriginConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollManage) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to maintain protected payroll bank-origin settings.");
        var destinationRouting = Digits(request.ImmediateDestinationRoutingNumber);
        var originatingDfi = Digits(request.OriginatingDfiIdentification);
        if (!IsValidAba(destinationRouting)) return TransactionResult.Failure("Immediate destination must be a valid nine-digit ABA routing number.");
        if (originatingDfi.Length != 8) return TransactionResult.Failure("Originating DFI identification must contain the first eight routing-number digits assigned by the bank.");
        if (string.IsNullOrWhiteSpace(request.ImmediateOrigin) || NormalizeAlpha(request.ImmediateOrigin, 10).Length == 0 || request.ImmediateOrigin.Trim().Length > 10) return TransactionResult.Failure("Immediate origin is required and cannot exceed ten characters.");
        if (NormalizeAlpha(request.OriginName, 23).Length == 0 || NormalizeAlpha(request.DestinationBankName, 23).Length == 0) return TransactionResult.Failure("Origin and destination bank names are required.");
        if (NormalizeAlpha(request.CompanyIdentification, 10).Length != 10) return TransactionResult.Failure("The ODFI-assigned company identification must contain exactly ten accepted characters.");
        if (NormalizeAlpha(request.CompanyEntryDescription, 10).Length == 0) return TransactionResult.Failure("A company entry description is required.");
        if (request.ExpiresOn < request.EffectiveOn) return TransactionResult.Failure("The bank-origin expiration date cannot precede its effective date.");
        if (request.IsBankValidated && string.IsNullOrWhiteSpace(request.BankValidationNotes)) return TransactionResult.Failure("Record how and when the originating bank validated these ACH settings.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.BankAccounts.AnyAsync(item => item.Id == request.BankAccountId && item.CompanyId == companyId, cancellationToken)) return TransactionResult.Failure("Payroll funding bank account not found in the active company.");
        PayrollBankOriginConfiguration configuration;
        if (request.Id.HasValue)
        {
            configuration = await db.PayrollBankOriginConfigurations.SingleOrDefaultAsync(item => item.Id == request.Id && item.CompanyId == companyId, cancellationToken) ?? new PayrollBankOriginConfiguration();
            if (configuration.Id == Guid.Empty) return TransactionResult.Failure("Payroll bank-origin configuration not found.");
            if (!string.Equals(configuration.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll bank-origin settings changed after they were opened. Refresh and try again.");
        }
        else
        {
            configuration = new PayrollBankOriginConfiguration { Id = Guid.NewGuid(), CompanyId = companyId };
            db.PayrollBankOriginConfigurations.Add(configuration);
        }
        var newEnd = request.ExpiresOn ?? DateOnly.MaxValue;
        if (await db.PayrollBankOriginConfigurations.AnyAsync(item => item.CompanyId == companyId && item.Id != configuration.Id && item.BankAccountId == request.BankAccountId && item.IsActive && request.IsActive && item.EffectiveOn <= newEnd && (item.ExpiresOn == null || item.ExpiresOn >= request.EffectiveOn), cancellationToken)) return TransactionResult.Failure("That funding account already has an overlapping active ACH origin configuration.");
        configuration.BankAccountId = request.BankAccountId; configuration.ImmediateDestinationRoutingNumber = destinationRouting; configuration.ImmediateOrigin = NormalizeAlpha(request.ImmediateOrigin, 10); configuration.DestinationBankName = NormalizeAlpha(request.DestinationBankName, 23); configuration.OriginName = NormalizeAlpha(request.OriginName, 23); configuration.CompanyIdentification = NormalizeAlpha(request.CompanyIdentification, 10); configuration.CompanyEntryDescription = NormalizeAlpha(request.CompanyEntryDescription, 10); configuration.OriginatingDfiIdentification = originatingDfi;
        configuration.EffectiveOn = request.EffectiveOn; configuration.ExpiresOn = request.ExpiresOn; configuration.IsActive = request.IsActive; configuration.IsBankValidated = request.IsBankValidated; configuration.BankValidationNotes = request.BankValidationNotes.Trim(); configuration.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAudit(db, companyId, request.Id.HasValue ? "payroll-bank-origin.updated" : "payroll-bank-origin.created", "PayrollBankOriginConfiguration", configuration.Id, new { configuration.BankAccountId, destinationRoutingLastFour = LastFour(destinationRouting), configuration.DestinationBankName, configuration.OriginName, configuration.CompanyEntryDescription, configuration.OriginatingDfiIdentification, configuration.EffectiveOn, configuration.ExpiresOn, configuration.IsActive, configuration.IsBankValidated, hasValidationNotes = configuration.BankValidationNotes.Length > 0 });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payroll bank-origin settings changed while they were being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The payroll bank-origin settings conflict with an existing effective-dated configuration."); }
        return TransactionResult.Success(configuration.Id);
    }

    public async Task<TransactionResult> GenerateAsync(GeneratePayrollPaymentFileRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPost) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to generate protected payroll payment files.");
        var format = request.Format.Trim();
        if (format is not ("AchInstructionsCsv" or "CheckRegisterCsv" or "NachaPpd")) return TransactionResult.Failure("Payment file format must be AchInstructionsCsv, CheckRegisterCsv, or NachaPpd.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var run = await db.PayrollRuns.SingleOrDefaultAsync(item => item.Id == request.PayrollRunId && item.CompanyId == companyId, cancellationToken);
        if (run is null) return TransactionResult.Failure("Payroll run not found.");
        if (run.Status != "Posted") return TransactionResult.Failure("Payment files can be generated only for posted, unreversed payroll.");
        if (await db.PayrollPaymentFiles.AnyAsync(item => item.CompanyId == companyId && item.PayrollRunId == run.Id && item.Format == format, cancellationToken)) return TransactionResult.Failure("That payment-file format was already generated for this payroll run. Use the immutable existing file.");
        var payments = await db.PayrollEmployeePayments.Where(item => item.CompanyId == companyId && item.PayrollRunId == run.Id && item.Status == "Issued").OrderBy(item => item.EmployeeNumber).ToListAsync(cancellationToken);
        if (payments.Count == 0 || payments.Sum(item => item.Amount) != run.NetPay) return TransactionResult.Failure("Issued employee payments do not reconcile to the posted payroll net pay.");
        PayrollBankOriginConfiguration? origin = null;
        var effectiveDate = request.EffectiveEntryDate ?? run.PayDate;
        if (format == "NachaPpd")
        {
            origin = await db.PayrollBankOriginConfigurations.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.BankAccountId == run.BankAccountId && item.IsActive && item.IsBankValidated && item.EffectiveOn <= effectiveDate && (item.ExpiresOn == null || item.ExpiresOn >= effectiveDate), cancellationToken);
            if (origin is null) return TransactionResult.Failure("A bank-validated, effective ACH origin configuration is required for this payroll funding account.");
        }
        var selected = format == "CheckRegisterCsv" ? payments.Where(item => item.Method == "Check").ToArray() : payments.Where(item => item.Method == "DirectDeposit").ToArray();
        if (selected.Length == 0) return TransactionResult.Failure(format == "CheckRegisterCsv" ? "This payroll run has no check payments." : "This payroll run has no direct-deposit payments.");
        if (format != "CheckRegisterCsv" && selected.Any(item => !IsValidAba(Digits(item.BankRoutingNumber)) || string.IsNullOrWhiteSpace(item.BankAccountNumber) || item.BankAccountNumber.Length > 17 || item.BankAccountType is not ("Checking" or "Savings"))) return TransactionResult.Failure("Every direct-deposit payment requires a valid ABA routing number, account number of at most 17 characters, and Checking or Savings account type.");
        var now = DateTimeOffset.UtcNow;
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero); var dayEnd = dayStart.AddDays(1);
        var sameDayFileCount = (await db.PayrollPaymentFiles.Where(item => item.CompanyId == companyId && item.Format == "NachaPpd").Select(item => item.GeneratedAtUtc).ToListAsync(cancellationToken)).Count(item => item >= dayStart && item < dayEnd);
        if (format == "NachaPpd" && sameDayFileCount >= 36) return TransactionResult.Failure("The available ACH file ID modifiers for this UTC creation date have been used. Contact the originating bank before creating another file.");
        var modifier = format == "NachaPpd" ? FileModifier(sameDayFileCount) : string.Empty;
        var content = format switch { "AchInstructionsCsv" => BuildAchCsv(run, selected), "CheckRegisterCsv" => BuildCheckCsv(run, selected), _ => BuildNacha(run, selected, origin!, effectiveDate, modifier, now) };
        var bytes = Encoding.UTF8.GetBytes(content);
        var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var sourceHash = BuildSourceDigest(run, payments, origin);
        var routingHash = format == "NachaPpd" ? selected.Sum(item => long.Parse(Digits(item.BankRoutingNumber)[..8], CultureInfo.InvariantCulture)) % 10_000_000_000L : 0;
        var file = new PayrollPaymentFile { Id = Guid.NewGuid(), CompanyId = companyId, PayrollRunId = run.Id, PayrollBankOriginConfigurationId = origin?.Id, Format = format, FileName = FileName(run, format), ContentType = format.EndsWith("Csv", StringComparison.Ordinal) ? "text/csv; charset=utf-8" : "text/plain; charset=us-ascii", Content = content, ContentSha256 = contentHash, SourceDigestSha256 = sourceHash, EntryCount = selected.Length, CreditTotal = selected.Sum(item => item.Amount), RoutingHash = routingHash, FileIdModifier = modifier, Status = format == "NachaPpd" ? "GeneratedForBankValidation" : "Generated", SpecificationVersion = format == "NachaPpd" ? NachaSpecificationVersion : "BrassLedger payroll payment export v1", GeneratedByUserId = ResolveUserId(), GeneratedAtUtc = now, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.PayrollPaymentFiles.Add(file);
        AddAudit(db, companyId, "payroll-payment-file.generated", "PayrollPaymentFile", file.Id, new { run.Id, run.Reference, file.Format, file.FileName, file.EntryCount, file.CreditTotal, file.RoutingHash, file.FileIdModifier, file.ContentSha256, file.SourceDigestSha256, file.Status, specificationSource = format == "NachaPpd" ? NachaSource : null });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return TransactionResult.Failure("That payment file was already generated or the payroll source changed concurrently."); }
        return TransactionResult.Success(file.Id);
    }

    public async Task<PayrollPaymentFileDownload?> DownloadAsync(Guid paymentFileId, CancellationToken cancellationToken = default)
    {
        RequireSensitivePayroll();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var file = await db.PayrollPaymentFiles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == paymentFileId && item.CompanyId == companyId, cancellationToken);
        if (file is null) return null;
        var bytes = Encoding.UTF8.GetBytes(file.Content);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actualHash), Encoding.ASCII.GetBytes(file.ContentSha256))) throw new InvalidOperationException("The protected payroll payment file failed its stored integrity check.");
        var downloadName = file.Status == "Voided" ? $"VOID-DO-NOT-PROCESS-{file.FileName}" : file.FileName;
        return new PayrollPaymentFileDownload(downloadName, file.ContentType, bytes, file.ContentSha256);
    }

    private static string BuildAchCsv(PayrollRun run, IReadOnlyList<PayrollEmployeePayment> payments)
    {
        var rows = new List<string> { "PayrollReference,PayDate,EmployeeNumber,EmployeeName,RoutingNumber,AccountNumber,AccountType,Amount,PaymentReference" };
        rows.AddRange(payments.Select(item => string.Join(',', Csv(run.Reference), run.PayDate.ToString("yyyy-MM-dd"), Csv(item.EmployeeNumber), Csv(item.EmployeeName), Digits(item.BankRoutingNumber), Csv(item.BankAccountNumber), item.BankAccountType, item.Amount.ToString("0.00", CultureInfo.InvariantCulture), Csv(item.Reference))));
        return string.Join("\r\n", rows) + "\r\n";
    }

    private static string BuildCheckCsv(PayrollRun run, IReadOnlyList<PayrollEmployeePayment> payments)
    {
        var rows = new List<string> { "PayrollReference,PayDate,CheckReference,EmployeeNumber,Payee,Amount" };
        rows.AddRange(payments.Select(item => string.Join(',', Csv(run.Reference), run.PayDate.ToString("yyyy-MM-dd"), Csv(item.Reference), Csv(item.EmployeeNumber), Csv(item.EmployeeName), item.Amount.ToString("0.00", CultureInfo.InvariantCulture))));
        return string.Join("\r\n", rows) + "\r\n";
    }

    private static string BuildNacha(PayrollRun run, IReadOnlyList<PayrollEmployeePayment> payments, PayrollBankOriginConfiguration origin, DateOnly effectiveDate, string modifier, DateTimeOffset generatedAt)
    {
        var local = generatedAt.ToLocalTime();
        var records = new List<string>
        {
            "1" + "01" + Fixed(" " + origin.ImmediateDestinationRoutingNumber, 10) + Fixed(origin.ImmediateOrigin.Length == 9 ? " " + origin.ImmediateOrigin : origin.ImmediateOrigin, 10) + local.ToString("yyMMdd") + local.ToString("HHmm") + modifier + "094" + "10" + "1" + Alpha(origin.DestinationBankName, 23) + Alpha(origin.OriginName, 23) + Alpha(run.Reference, 8),
            "5" + "220" + Alpha(origin.OriginName, 16) + new string(' ', 20) + Alpha(origin.CompanyIdentification, 10) + "PPD" + Alpha(origin.CompanyEntryDescription, 10) + effectiveDate.ToString("yyMMdd") + effectiveDate.ToString("yyMMdd") + "   " + "1" + origin.OriginatingDfiIdentification + Numeric(1, 7)
        };
        long entryHash = 0; long creditCents = 0;
        for (var index = 0; index < payments.Count; index++)
        {
            var payment = payments[index]; var routing = Digits(payment.BankRoutingNumber); var cents = ToCents(payment.Amount); creditCents += cents; entryHash += long.Parse(routing[..8], CultureInfo.InvariantCulture);
            var transactionCode = payment.BankAccountType == "Savings" ? "32" : "22";
            records.Add("6" + transactionCode + routing[..8] + routing[8] + Alpha(payment.BankAccountNumber, 17) + Numeric(cents, 10) + Alpha(payment.EmployeeNumber, 15) + Alpha(payment.EmployeeName, 22) + "  " + "0" + origin.OriginatingDfiIdentification + Numeric(index + 1, 7));
        }
        entryHash %= 10_000_000_000L;
        records.Add("8" + "220" + Numeric(payments.Count, 6) + Numeric(entryHash, 10) + Numeric(0, 12) + Numeric(creditCents, 12) + Alpha(origin.CompanyIdentification, 10) + new string(' ', 19) + new string(' ', 6) + origin.OriginatingDfiIdentification + Numeric(1, 7));
        var logicalRecordCount = records.Count + 1; var blockCount = (logicalRecordCount + 9) / 10;
        records.Add("9" + Numeric(1, 6) + Numeric(blockCount, 6) + Numeric(payments.Count, 8) + Numeric(entryHash, 10) + Numeric(0, 12) + Numeric(creditCents, 12) + new string(' ', 39));
        while (records.Count % 10 != 0) records.Add(new string('9', 94));
        if (records.Any(record => Encoding.ASCII.GetByteCount(record) != 94)) throw new InvalidOperationException("Generated ACH record does not contain exactly 94 ASCII bytes.");
        return string.Join("\r\n", records) + "\r\n";
    }

    private static string BuildSourceDigest(PayrollRun run, IReadOnlyList<PayrollEmployeePayment> payments, PayrollBankOriginConfiguration? origin)
    {
        var source = JsonSerializer.Serialize(new { run.Id, run.Reference, run.PayDate, run.Status, run.NetPay, run.ConcurrencyToken, payments = payments.OrderBy(item => item.Id).Select(item => new { item.Id, item.EmployeeId, item.EmployeeNumber, item.EmployeeName, item.Method, item.Reference, item.BankRoutingNumber, item.BankAccountNumber, item.BankAccountType, item.Amount, item.Status, item.ConcurrencyToken }), origin = origin is null ? null : new { origin.Id, origin.BankAccountId, origin.ImmediateDestinationRoutingNumber, origin.ImmediateOrigin, origin.DestinationBankName, origin.OriginName, origin.CompanyIdentification, origin.CompanyEntryDescription, origin.OriginatingDfiIdentification, origin.EffectiveOn, origin.ExpiresOn, origin.IsBankValidated, origin.ConcurrencyToken } });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private static string FileName(PayrollRun run, string format) => $"{SafeFile(run.Reference)}-{run.PayDate:yyyyMMdd}-{format switch { "AchInstructionsCsv" => "ach-instructions.csv", "CheckRegisterCsv" => "check-register.csv", _ => "nacha-ppd.ach" }}";
    private static string SafeFile(string value) { var normalized = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray()); return string.IsNullOrWhiteSpace(normalized) ? "payroll" : normalized; }
    private static string FileModifier(int index) => index < 26 ? ((char)('A' + index)).ToString() : ((char)('0' + index - 26)).ToString();
    private static long ToCents(decimal amount) { var cents = checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero)); if (cents < 0 || cents > 9_999_999_999L) throw new InvalidOperationException("An ACH entry amount exceeds the ten-digit record limit."); return cents; }
    private static string Numeric(long value, int length) { var text = value.ToString(CultureInfo.InvariantCulture); if (text.Length > length) throw new InvalidOperationException("A numeric ACH field exceeds its fixed width."); return text.PadLeft(length, '0'); }
    private static string Fixed(string value, int length) { var accepted = NormalizeAlpha(value, length); return accepted.PadRight(length); }
    private static string Alpha(string value, int length) => Fixed(value, length);
    private static string NormalizeAlpha(string value, int length) => new((value ?? string.Empty).ToUpperInvariant().Where(character => character is >= ' ' and <= '~').Take(length).ToArray());
    private static string Digits(string value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    private static bool IsValidAba(string value) { if (value.Length != 9) return false; var d = value.Select(c => c - '0').ToArray(); return (3 * (d[0] + d[3] + d[6]) + 7 * (d[1] + d[4] + d[7]) + d[2] + d[5] + d[8]) % 10 == 0; }
    private static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static string LastFour(string value) => string.IsNullOrEmpty(value) ? string.Empty : value[^Math.Min(4, value.Length)..];
    private static string Mask(string value) => string.IsNullOrEmpty(value) ? string.Empty : new string('•', Math.Max(0, value.Length - 4)) + LastFour(value);
    private bool HasPermission(string permission) => httpContextAccessor.HttpContext is null || httpContextAccessor.HttpContext.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission);
    private void RequireSensitivePayroll() { if (!HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) throw new UnauthorizedAccessException("You are not authorized to access protected payroll payment files."); }
    private Guid? ResolveUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken cancellationToken) { var context = httpContextAccessor.HttpContext; var claim = context?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType); if (context is not null && !Guid.TryParse(claim, out _)) throw new UnauthorizedAccessException("An authenticated company context is required."); if (Guid.TryParse(claim, out var id) && await db.Companies.AnyAsync(item => item.Id == id, cancellationToken)) return id; return await db.Companies.OrderBy(item => item.Name).Select(item => item.Id).FirstAsync(cancellationToken); }
    private void AddAudit(BrassLedgerDbContext db, Guid companyId, string action, string entityType, Guid entityId, object detail) => db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = entityType, EntityId = entityId, DetailJson = JsonSerializer.Serialize(detail), OccurredAtUtc = DateTimeOffset.UtcNow });
}
