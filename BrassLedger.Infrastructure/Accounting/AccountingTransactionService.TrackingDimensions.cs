using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    private static readonly string[] SupportedTrackingDimensionTypes = ["Department", "Class"];

    public async Task<TransactionResult> SaveTrackingDimensionValueAsync(SaveTrackingDimensionValueRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.AccountingDimensionsManage)) return TransactionResult.Failure("You are not authorized to manage accounting dimensions.");
        var dimensionType = SupportedTrackingDimensionTypes.SingleOrDefault(value => value.Equals(request.DimensionType.Trim(), StringComparison.OrdinalIgnoreCase));
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var description = request.Description.Trim();
        if (dimensionType is null) return TransactionResult.Failure("Dimension type must be Department or Class.");
        if (code.Length is < 1 or > 50 || name.Length is < 1 or > 200) return TransactionResult.Failure("A dimension code of at most 50 characters and a name of at most 200 characters are required.");
        if (description.Length > 1000) return TransactionResult.Failure("The dimension description cannot exceed 1,000 characters.");
        if (request.EffectiveFrom.HasValue && request.EffectiveThrough.HasValue && request.EffectiveThrough < request.EffectiveFrom) return TransactionResult.Failure("The dimension effective-through date cannot precede its effective-from date.");
        if (request.Id.HasValue && request.ParentTrackingDimensionValueId == request.Id) return TransactionResult.Failure("A dimension value cannot be its own parent.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.TrackingDimensionValues.AnyAsync(value => value.CompanyId == companyId && value.DimensionType == dimensionType && value.Code == code && value.Id != request.Id, cancellationToken))
            return TransactionResult.Failure($"{dimensionType} code already exists in this company.");

        TrackingDimensionValue? parent = null;
        if (request.ParentTrackingDimensionValueId.HasValue)
        {
            parent = await db.TrackingDimensionValues.SingleOrDefaultAsync(value => value.Id == request.ParentTrackingDimensionValueId && value.CompanyId == companyId && value.DimensionType == dimensionType, cancellationToken);
            if (parent is null) return TransactionResult.Failure($"The parent must be a {dimensionType.ToLowerInvariant()} in this company.");
            if (request.IsActive && !parent.IsActive) return TransactionResult.Failure("An active dimension value cannot be placed under an inactive parent.");
            var ancestorId = parent.ParentTrackingDimensionValueId;
            while (ancestorId.HasValue)
            {
                if (ancestorId == request.Id) return TransactionResult.Failure("The selected parent would create a dimension hierarchy cycle.");
                ancestorId = await db.TrackingDimensionValues.Where(value => value.Id == ancestorId.Value && value.CompanyId == companyId && value.DimensionType == dimensionType).Select(value => value.ParentTrackingDimensionValueId).SingleOrDefaultAsync(cancellationToken);
            }
            if (parent.EffectiveFrom.HasValue && request.EffectiveFrom.HasValue && request.EffectiveFrom < parent.EffectiveFrom) return TransactionResult.Failure("A child dimension cannot become effective before its parent.");
            if (parent.EffectiveThrough.HasValue && request.EffectiveThrough.HasValue && request.EffectiveThrough > parent.EffectiveThrough) return TransactionResult.Failure("A child dimension cannot remain effective after its parent.");
        }

        var now = DateTimeOffset.UtcNow;
        TrackingDimensionValue value;
        object? prior = null;
        if (request.Id.HasValue)
        {
            value = await db.TrackingDimensionValues.SingleOrDefaultAsync(candidate => candidate.Id == request.Id && candidate.CompanyId == companyId, cancellationToken) ?? new TrackingDimensionValue();
            if (value.Id == Guid.Empty) return TransactionResult.Failure("Accounting dimension value not found.");
            if (!string.Equals(value.DimensionType, dimensionType, StringComparison.Ordinal)) return TransactionResult.Failure("The dimension type cannot be changed after creation.");
            if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || !string.Equals(value.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The accounting dimension changed after it was displayed. Refresh before saving it.");
            if (!request.IsActive && await db.TrackingDimensionValues.AnyAsync(child => child.ParentTrackingDimensionValueId == value.Id && child.IsActive, cancellationToken)) return TransactionResult.Failure("Deactivate active child dimensions before deactivating their parent.");
            if (request.EffectiveFrom.HasValue && await db.TrackingDimensionValues.AnyAsync(child => child.ParentTrackingDimensionValueId == value.Id && child.EffectiveFrom.HasValue && child.EffectiveFrom < request.EffectiveFrom, cancellationToken)) return TransactionResult.Failure("The dimension cannot become effective after one of its children.");
            if (request.EffectiveThrough.HasValue && await db.TrackingDimensionValues.AnyAsync(child => child.ParentTrackingDimensionValueId == value.Id && child.EffectiveThrough.HasValue && child.EffectiveThrough > request.EffectiveThrough, cancellationToken)) return TransactionResult.Failure("The dimension cannot expire before one of its children.");
            prior = TrackingDimensionAuditState(value);
            value.UpdatedByUserId = ResolveUserId();
            value.UpdatedAtUtc = now;
        }
        else
        {
            value = new TrackingDimensionValue { Id = Guid.NewGuid(), CompanyId = companyId, DimensionType = dimensionType, CreatedByUserId = ResolveUserId(), CreatedAtUtc = now };
            db.TrackingDimensionValues.Add(value);
        }

        value.ParentTrackingDimensionValueId = parent?.Id;
        value.Code = code;
        value.Name = name;
        value.Description = description;
        value.EffectiveFrom = request.EffectiveFrom;
        value.EffectiveThrough = request.EffectiveThrough;
        value.IsActive = request.IsActive;
        value.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserId = ResolveUserId(),
            Action = prior is null ? "tracking-dimension.created" : "tracking-dimension.updated",
            EntityType = nameof(TrackingDimensionValue),
            EntityId = value.Id,
            DetailJson = JsonSerializer.Serialize(new { prior, current = TrackingDimensionAuditState(value) }),
            OccurredAtUtc = now
        });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The accounting dimension changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The dimension code is already in use or related company data changed. Refresh and try again."); }
        return TransactionResult.Success(value.Id);
    }

    private static async Task<bool> AreActiveTrackingDimensionsAsync(
        BrassLedgerDbContext db,
        Guid companyId,
        DateOnly effectiveOn,
        IEnumerable<(Guid? DepartmentId, Guid? ClassId)> requestedDimensions,
        CancellationToken cancellationToken,
        bool allowHistorical = false)
    {
        var dimensions = requestedDimensions.Distinct().ToArray();
        var departmentIds = dimensions.Where(value => value.DepartmentId.HasValue).Select(value => value.DepartmentId!.Value).Distinct().ToArray();
        if (departmentIds.Length > 0 && await db.TrackingDimensionValues.CountAsync(value => value.CompanyId == companyId
                && value.DimensionType == "Department"
                && departmentIds.Contains(value.Id)
                && (allowHistorical || value.IsActive && (!value.EffectiveFrom.HasValue || value.EffectiveFrom <= effectiveOn) && (!value.EffectiveThrough.HasValue || value.EffectiveThrough >= effectiveOn)), cancellationToken) != departmentIds.Length) return false;
        var classIds = dimensions.Where(value => value.ClassId.HasValue).Select(value => value.ClassId!.Value).Distinct().ToArray();
        return classIds.Length == 0 || await db.TrackingDimensionValues.CountAsync(value => value.CompanyId == companyId
            && value.DimensionType == "Class"
            && classIds.Contains(value.Id)
            && (allowHistorical || value.IsActive && (!value.EffectiveFrom.HasValue || value.EffectiveFrom <= effectiveOn) && (!value.EffectiveThrough.HasValue || value.EffectiveThrough >= effectiveOn)), cancellationToken) == classIds.Length;
    }

    private static object TrackingDimensionAuditState(TrackingDimensionValue value) => new { value.DimensionType, value.ParentTrackingDimensionValueId, value.Code, value.Name, value.Description, value.EffectiveFrom, value.EffectiveThrough, value.IsActive };
}
