using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class AccountingPeriodService(IDbContextFactory<BrassLedgerDbContext> dbContextFactory, IHttpContextAccessor httpContextAccessor) : IAccountingPeriodService
{
    public async Task<AccountingControlsSnapshot> GetSnapshotAsync(int auditEntryLimit = 100, CancellationToken cancellationToken = default)
    {
        var companyId = CompanyId();
        if (companyId is null) return new AccountingControlsSnapshot([], []);
        var limit = Math.Clamp(auditEntryLimit, 1, 500);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(period => period.CompanyId == companyId).OrderByDescending(period => period.StartsOn).ToListAsync(cancellationToken);
        var audits = await db.BusinessAuditEntries.AsNoTracking().Where(entry => entry.CompanyId == companyId).OrderByDescending(entry => entry.OccurredAtUtc).Take(limit).ToListAsync(cancellationToken);
        var userIds = periods.Where(period => period.ClosedByUserId.HasValue).Select(period => period.ClosedByUserId!.Value).Concat(audits.Where(entry => entry.UserId.HasValue).Select(entry => entry.UserId!.Value)).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        return new AccountingControlsSnapshot(
            periods.Select(period => new AccountingPeriodSnapshot(period.Id, period.StartsOn, period.EndsOn, period.Status, period.Notes, period.ClosedByUserId is { } userId ? users.GetValueOrDefault(userId) : null, period.ClosedAtUtc)).ToArray(),
            audits.Select(entry => new BusinessAuditEntrySnapshot(entry.Id, entry.OccurredAtUtc, entry.Action, entry.EntityType, entry.EntityId, entry.UserId is { } userId ? users.GetValueOrDefault(userId) : null, entry.DetailJson)).ToArray());
    }

    public async Task<TransactionResult> SavePeriodAsync(SaveAccountingPeriodRequest request, CancellationToken cancellationToken = default)
    {
        if (request.EndsOn < request.StartsOn) return TransactionResult.Failure("An accounting period cannot end before it starts.");
        var companyId = CompanyId(); if (companyId is null) return TransactionResult.Failure("An active company is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = request.Id is { } id ? await db.AccountingPeriods.SingleOrDefaultAsync(period => period.CompanyId == companyId && period.Id == id, cancellationToken) : null;
        if (request.Id.HasValue && entity is null) return TransactionResult.Failure("Accounting period not found.");
        if (entity?.Status == "Closed") return TransactionResult.Failure("Reopen a closed period before changing its dates.");
        if (await db.AccountingPeriods.AnyAsync(period => period.CompanyId == companyId && period.Id != request.Id && period.StartsOn <= request.EndsOn && period.EndsOn >= request.StartsOn, cancellationToken)) return TransactionResult.Failure("Accounting periods cannot overlap.");
        entity ??= new AccountingPeriod { Id = Guid.NewGuid(), CompanyId = companyId.Value }; entity.StartsOn = request.StartsOn; entity.EndsOn = request.EndsOn; entity.Notes = request.Notes.Trim();
        if (db.Entry(entity).State == EntityState.Detached) db.AccountingPeriods.Add(entity); await db.SaveChangesAsync(cancellationToken); return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> SetPeriodStatusAsync(Guid periodId, bool close, string notes, CancellationToken cancellationToken = default)
    {
        var companyId = CompanyId(); var userId = Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null; if (companyId is null || userId is null) return TransactionResult.Failure("An authenticated company operator is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var period = await db.AccountingPeriods.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == periodId, cancellationToken); if (period is null) return TransactionResult.Failure("Accounting period not found.");
        period.Status = close ? "Closed" : "Open"; period.ClosedByUserId = close ? userId : null; period.ClosedAtUtc = close ? DateTimeOffset.UtcNow : null; period.Notes = notes.Trim();
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = close ? "accounting-period.closed" : "accounting-period.reopened", EntityType = "AccountingPeriod", EntityId = period.Id, DetailJson = System.Text.Json.JsonSerializer.Serialize(new { period.StartsOn, period.EndsOn, period.Notes }), OccurredAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken); return TransactionResult.Success(period.Id);
    }

    private Guid? CompanyId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId) ? companyId : null;
}
