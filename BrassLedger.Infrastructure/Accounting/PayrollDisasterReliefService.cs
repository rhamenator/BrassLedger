using System.Security.Claims;
using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class PayrollDisasterReliefService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : IPayrollDisasterReliefService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] KnownActionTypes = ["ReturnFilingPostponement", "TaxPaymentPostponement", "DepositPenaltyAbatement"];
    private static readonly string[] EligibilityBases = ["PrincipalPlaceOfBusiness", "RecordsInCoveredArea", "IrsIndividualDetermination"];

    public async Task<PayrollDisasterReliefWorkspace> GetAsync(CancellationToken cancellationToken = default)
    {
        RequirePayrollAccess();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var configurations = await db.PayrollDisasterReliefConfigurations.AsNoTracking().Where(item => item.CompanyId == companyId).OrderByDescending(item => item.SourceRetrievedOn).ThenBy(item => item.AnnouncementCode).ToListAsync(cancellationToken);
        var liabilities = await db.PayrollLiabilities.AsNoTracking().Where(item => item.CompanyId == companyId && item.SourceType == "Tax" && item.DueDate.HasValue).ToListAsync(cancellationToken);
        var runs = await db.PayrollRuns.AsNoTracking().Where(item => item.CompanyId == companyId && item.Status == "Posted").ToDictionaryAsync(item => item.Id, cancellationToken);
        var applications = await db.PayrollLiabilityPaymentApplications.AsNoTracking()
            .Join(db.PayrollLiabilityPayments.AsNoTracking().Where(payment => payment.CompanyId == companyId && payment.Status == "Posted"), application => application.PayrollLiabilityPaymentId, payment => payment.Id, (application, payment) => new { application.PayrollLiabilityId, application.Amount, payment.PaymentDate })
            .ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var impacts = configurations.Where(item => item.IsActive && item.IsApproved).SelectMany(configuration => ParseActions(configuration.ReliefActionsJson)
            .Where(action => action.ActionType == "DepositPenaltyAbatement").SelectMany(action =>
            {
                var affected = liabilities.Where(item => PayrollDepositDueDateCalculator.IsForm941Obligation(item.ObligationCode) && runs.ContainsKey(item.PayrollRunId) && item.DueDate >= action.OriginalDueOnOrAfter && item.DueDate < action.OriginalDueBefore).ToArray();
                return affected.GroupBy(item => item.DueDate!.Value).Select(group =>
                {
                    var ids = group.Select(item => item.Id).ToHashSet(); var required = group.Sum(item => item.OriginalAmount);
                    var paidByDue = applications.Where(item => ids.Contains(item.PayrollLiabilityId) && item.PaymentDate <= group.Key).Sum(item => item.Amount);
                    var paidByRelief = applications.Where(item => ids.Contains(item.PayrollLiabilityId) && item.PaymentDate <= action.ReliefDeadline).Sum(item => item.Amount);
                    var status = paidByDue >= required ? "DepositedOnTime" : paidByRelief >= required ? "PenaltyReliefConditionsMet" : action.ReliefDeadline >= today ? "ReliefPending" : "ReliefDeadlineMissed";
                    return new PayrollDisasterDepositImpactSnapshot(configuration.Id, configuration.AnnouncementCode, group.Key, action.ReliefDeadline, required, paidByDue, paidByRelief, status);
                });
            })).OrderBy(item => item.OriginalDueDate).ThenBy(item => item.AnnouncementCode).ToArray();
        return new PayrollDisasterReliefWorkspace(configurations.Select(ToSnapshot).ToArray(), impacts);
    }

    public async Task<TransactionResult> SaveAsync(SavePayrollDisasterReliefRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollManage)) return TransactionResult.Failure("You are not authorized to configure payroll disaster relief.");
        if (request.IsApproved && !HasPermission(BrassLedgerPermissions.PayrollApprove)) return TransactionResult.Failure("Payroll approval permission is required to approve disaster relief.");
        var announcementCode = request.AnnouncementCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var disasterName = request.DisasterName?.Trim() ?? string.Empty;
        var evidence = request.EligibilityEvidenceReference?.Trim() ?? string.Empty;
        var notes = request.ReviewNotes?.Trim() ?? string.Empty;
        if (announcementCode.Length < 4 || disasterName.Length < 5) return TransactionResult.Failure("Enter the IRS announcement code and disaster name.");
        if (!TryCoveredAreas(request.CoveredAreasJson, out var coveredAreas)) return TransactionResult.Failure("Covered areas must be a JSON array of unique, nonblank jurisdiction names.");
        if (!TryActions(request.ReliefActionsJson, out var actions)) return TransactionResult.Failure("Relief actions must be valid JSON with due-window start, exclusive end, relief deadline, and action type.");
        if (!EligibilityBases.Contains(request.AffectedTaxpayerBasis)) return TransactionResult.Failure("Select a supported affected-taxpayer eligibility basis.");
        if (!ValidOfficialUrl(request.OfficialSourceUrl)) return TransactionResult.Failure("The relief source must be an official HTTPS IRS URL.");
        if (request.SourceRetrievedOn == default || request.SourceRetrievedOn > DateOnly.FromDateTime(DateTime.Today)) return TransactionResult.Failure("Enter the actual source retrieval date; it cannot be in the future.");
        if (request.IsActive && !request.IsApproved) return TransactionResult.Failure("Only approved disaster relief can be active.");
        if (request.IsApproved && (coveredAreas.Count == 0 || actions.Count == 0 || evidence.Length < 5 || notes.Length < 20)) return TransactionResult.Failure("Approval requires covered areas, relief actions, eligibility evidence, and substantive review notes.");
        if (request.IsApproved && actions.Any(action => !KnownActionTypes.Contains(action.ActionType))) return TransactionResult.Failure("An approved relief configuration contains an unsupported action type; retain it as an inactive draft until the runtime supports that action.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        PayrollDisasterReliefConfiguration configuration;
        if (request.Id.HasValue)
        {
            configuration = await db.PayrollDisasterReliefConfigurations.SingleOrDefaultAsync(item => item.Id == request.Id && item.CompanyId == companyId, cancellationToken) ?? new PayrollDisasterReliefConfiguration();
            if (configuration.Id == Guid.Empty) return TransactionResult.Failure("Payroll disaster relief configuration not found.");
            if (!string.Equals(configuration.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The disaster relief configuration changed after it was opened. Refresh and try again.");
        }
        else
        {
            if (await db.PayrollDisasterReliefConfigurations.AnyAsync(item => item.CompanyId == companyId && item.AnnouncementCode == announcementCode, cancellationToken)) return TransactionResult.Failure("This IRS disaster announcement is already configured for the company.");
            configuration = new PayrollDisasterReliefConfiguration { Id = Guid.NewGuid(), CompanyId = companyId }; db.PayrollDisasterReliefConfigurations.Add(configuration);
        }
        configuration.AnnouncementCode = announcementCode; configuration.DisasterName = disasterName; configuration.FemaDeclarationNumber = request.FemaDeclarationNumber?.Trim().ToUpperInvariant() ?? string.Empty;
        configuration.CoveredAreasJson = JsonSerializer.Serialize(coveredAreas); configuration.AffectedTaxpayerBasis = request.AffectedTaxpayerBasis; configuration.EligibilityEvidenceReference = evidence;
        configuration.ReliefActionsJson = JsonSerializer.Serialize(actions); configuration.OfficialSourceUrl = request.OfficialSourceUrl.Trim(); configuration.SourceRetrievedOn = request.SourceRetrievedOn; configuration.ReviewNotes = notes;
        configuration.IsApproved = request.IsApproved; configuration.ApprovedByUserId = request.IsApproved ? ResolveUserId() : null; configuration.ApprovedAtUtc = request.IsApproved ? DateTimeOffset.UtcNow : null; configuration.IsActive = request.IsActive; configuration.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = request.Id.HasValue ? "payroll-disaster-relief.updated" : "payroll-disaster-relief.created", EntityType = nameof(PayrollDisasterReliefConfiguration), EntityId = configuration.Id, DetailJson = JsonSerializer.Serialize(new { configuration.AnnouncementCode, configuration.DisasterName, configuration.FemaDeclarationNumber, coveredAreas, configuration.AffectedTaxpayerBasis, configuration.EligibilityEvidenceReference, actions, configuration.OfficialSourceUrl, configuration.SourceRetrievedOn, configuration.ReviewNotes, configuration.IsApproved, configuration.ApprovedByUserId, configuration.ApprovedAtUtc, configuration.IsActive }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The disaster relief configuration changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("This IRS disaster announcement conflicts with an existing company configuration."); }
        return TransactionResult.Success(configuration.Id);
    }

    private static PayrollDisasterReliefSnapshot ToSnapshot(PayrollDisasterReliefConfiguration item) => new(item.Id, item.AnnouncementCode, item.DisasterName, item.FemaDeclarationNumber, ParseCoveredAreas(item.CoveredAreasJson), item.AffectedTaxpayerBasis, item.EligibilityEvidenceReference, ParseActions(item.ReliefActionsJson), item.OfficialSourceUrl, item.SourceRetrievedOn, item.ReviewNotes, item.IsApproved, item.ApprovedAtUtc, item.IsActive, item.ConcurrencyToken);
    private static IReadOnlyList<string> ParseCoveredAreas(string json) => TryCoveredAreas(json, out var values) ? values : [];
    private static bool TryCoveredAreas(string json, out IReadOnlyList<string> values) { try { var parsed = JsonSerializer.Deserialize<string[]>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? []; var normalized = parsed.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(); values = normalized; return normalized.Length == parsed.Length; } catch (JsonException) { values = []; return false; } }
    private static IReadOnlyList<PayrollDisasterReliefAction> ParseActions(string json) => TryActions(json, out var values) ? values : [];
    private static bool TryActions(string json, out IReadOnlyList<PayrollDisasterReliefAction> values)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<PayrollDisasterReliefAction[]>(string.IsNullOrWhiteSpace(json) ? "[]" : json, JsonOptions) ?? [];
            if (parsed.Any(item => string.IsNullOrWhiteSpace(item.ActionType) || item.OriginalDueOnOrAfter == default || item.OriginalDueBefore <= item.OriginalDueOnOrAfter || item.ReliefDeadline < item.OriginalDueOnOrAfter)) { values = []; return false; }
            values = parsed.Select(item => item with { ActionType = item.ActionType.Trim(), Notes = item.Notes?.Trim() ?? string.Empty }).ToArray(); return true;
        }
        catch (JsonException) { values = []; return false; }
    }
    private static bool ValidOfficialUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && (uri.Host.Equals("irs.gov", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".irs.gov", StringComparison.OrdinalIgnoreCase));
    private void RequirePayrollAccess() { if (!HasPermission(BrassLedgerPermissions.PayrollManage)) throw new UnauthorizedAccessException("You are not authorized to view payroll disaster relief configurations."); }
    private bool HasPermission(string permission) => httpContextAccessor.HttpContext is null || httpContextAccessor.HttpContext.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission);
    private Guid? ResolveUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken cancellationToken) { var context = httpContextAccessor.HttpContext; var claim = context?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType); if (context is not null && !Guid.TryParse(claim, out _)) throw new UnauthorizedAccessException("An authenticated company context is required."); if (Guid.TryParse(claim, out var id) && await db.Companies.AnyAsync(item => item.Id == id, cancellationToken)) return id; return await db.Companies.OrderBy(item => item.Name).Select(item => item.Id).FirstAsync(cancellationToken); }
}
