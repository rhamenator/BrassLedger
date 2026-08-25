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

public sealed class QuickBooksOnlineSyncService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    IQuickBooksOnlineClient client,
    IQuickBooksOnlineConnectionService connectionService,
    TimeProvider timeProvider) : IQuickBooksOnlineSyncService
{
    private const string ProviderCode = "quickbooks-online";
    private static readonly string[] SupportedEntityTypes = ["accounts", "customers", "vendors"];

    public async Task<QuickBooksSyncResult> ImportAsync(QuickBooksSyncRequest request, CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null) return QuickBooksSyncResult.Failure("An authenticated company is required.", request.DryRun);
        var entityType = (request.EntityType ?? string.Empty).Trim().ToLowerInvariant();
        if (!SupportedEntityTypes.Contains(entityType, StringComparer.Ordinal))
            return QuickBooksSyncResult.Failure("Supported API imports are accounts, customers, and vendors.", request.DryRun);
        if (!CanSynchronize(actor.Principal, entityType))
            return QuickBooksSyncResult.Failure("You are not authorized to synchronize this QuickBooks data type.", request.DryRun);

        var loaded = await LoadAuthorizationAsync(actor.CompanyId, request.ConnectionId, cancellationToken);
        if (loaded.Connection is null || loaded.Credentials is null || loaded.Settings is null)
            return QuickBooksSyncResult.Failure(loaded.ErrorMessage, request.DryRun);
        if (loaded.Credentials.RefreshTokenExpiresAtUtc <= timeProvider.GetUtcNow())
            return QuickBooksSyncResult.Failure("The QuickBooks authorization has expired. Reconnect the company before synchronizing.", request.DryRun);
        if (loaded.Credentials.AccessTokenExpiresAtUtc <= timeProvider.GetUtcNow().AddMinutes(2))
        {
            var refreshed = await connectionService.RefreshConnectionAsync(request.ConnectionId, cancellationToken);
            if (!refreshed.Succeeded) return QuickBooksSyncResult.Failure(refreshed.ErrorMessage, request.DryRun);
            loaded = await LoadAuthorizationAsync(actor.CompanyId, request.ConnectionId, cancellationToken);
            if (loaded.Connection is null || loaded.Credentials is null || loaded.Settings is null)
                return QuickBooksSyncResult.Failure(loaded.ErrorMessage, request.DryRun);
        }

        var startedAt = timeProvider.GetUtcNow();
        QuickBooksEntityQueryResponse providerResponse;
        try
        {
            providerResponse = await client.QueryEntitiesAsync(
                loaded.Settings.Environment,
                loaded.Credentials.RealmId,
                loaded.Credentials.AccessToken,
                entityType,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return await RecordProviderFailureAsync(actor, request, entityType, startedAt, "provider_transport_failure", "QuickBooks could not be reached securely. No accounting records changed.", cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await RecordProviderFailureAsync(actor, request, entityType, startedAt, "provider_timeout", "QuickBooks did not respond before the synchronization timed out. No accounting records changed.", cancellationToken);
        }
        if (!providerResponse.Succeeded)
            return await RecordProviderFailureAsync(actor, request, entityType, startedAt, SafeCode(providerResponse.ErrorCode), $"QuickBooks could not provide {entityType} ({SafeCode(providerResponse.ErrorCode)}). No accounting records changed.", cancellationToken);

        var remoteEntities = providerResponse.Entities.OrderBy(entity => entity.Id, StringComparer.Ordinal).ToArray();
        var snapshotSha256 = Fingerprint(remoteEntities.Select(CanonicalRemoteSnapshot));
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connectionStillValid = await db.IntegrationConnections.AsNoTracking().AnyAsync(connection =>
            connection.Id == request.ConnectionId && connection.CompanyId == actor.CompanyId && connection.ProviderCode == ProviderCode && connection.Status != "Disconnected",
            cancellationToken);
        if (!connectionStillValid) return QuickBooksSyncResult.Failure("The QuickBooks connection changed while data was being fetched. No accounting records changed.", request.DryRun);
        if (!request.DryRun)
        {
            var expectedSnapshot = (request.ExpectedSnapshotSha256 ?? string.Empty).Trim().ToUpperInvariant();
            var priorRuns = await db.IntegrationSyncRuns.AsNoTracking().Where(run =>
                run.CompanyId == actor.CompanyId
                && run.IntegrationConnectionId == request.ConnectionId
                && run.EntityType == entityType
            ).ToListAsync(cancellationToken);
            var matchingPreview = priorRuns.Where(run => run.IsDryRun && run.SnapshotSha256 == expectedSnapshot && run.InitiatedByUserId == actor.UserId).OrderByDescending(run => run.CompletedAtUtc).FirstOrDefault();
            var lastCommitAttempt = priorRuns.Where(run => !run.IsDryRun).OrderByDescending(run => run.CompletedAtUtc).FirstOrDefault();
            var previewExists = expectedSnapshot.Length == 64
                && matchingPreview is not null
                && matchingPreview.CompletedAtUtc >= startedAt.AddMinutes(-30)
                && (lastCommitAttempt is null || matchingPreview.CompletedAtUtc > lastCommitAttempt.CompletedAtUtc);
            if (!previewExists || !string.Equals(expectedSnapshot, snapshotSha256, StringComparison.Ordinal))
            {
                var issue = new QuickBooksSyncIssue(string.Empty, previewExists ? "source_changed_after_preview" : "preview_required", previewExists ? "QuickBooks data changed after the selected preview. Preview again before importing." : "Run and review a preview of this exact QuickBooks snapshot before importing.");
                var rejectedAt = timeProvider.GetUtcNow();
                var rejectedRun = new IntegrationSyncRun { Id = Guid.NewGuid(), CompanyId = actor.CompanyId, IntegrationConnectionId = request.ConnectionId, ProviderCode = ProviderCode, EntityType = entityType, Direction = "Import", IsDryRun = false, Status = previewExists ? "PreviewStale" : "PreviewRequired", FetchedCount = remoteEntities.Length, ConflictCount = 1, SnapshotSha256 = snapshotSha256, DetailJson = JsonSerializer.Serialize(new[] { issue }), InitiatedByUserId = actor.UserId, StartedAtUtc = startedAt, CompletedAtUtc = rejectedAt };
                db.IntegrationSyncRuns.Add(rejectedRun);
                db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = actor.CompanyId, UserId = actor.UserId, Action = "integration.quickbooks.sync_commit_rejected", EntityType = nameof(IntegrationSyncRun), EntityId = rejectedRun.Id, DetailJson = JsonSerializer.Serialize(new { connectionId = request.ConnectionId, entityType, issue.Code, noRecordsChanged = true, currentSnapshotSha256 = snapshotSha256 }), OccurredAtUtc = rejectedAt });
                await db.SaveChangesAsync(cancellationToken);
                return new(false, issue.Message, rejectedRun.Id, false, remoteEntities.Length, 0, 0, 0, 1, 0, snapshotSha256, [issue]);
            }
        }

        var links = await db.ExternalEntityLinks.Where(link => link.CompanyId == actor.CompanyId && link.IntegrationConnectionId == request.ConnectionId && link.ProviderCode == ProviderCode && link.EntityType == entityType).ToDictionaryAsync(link => link.ProviderEntityId, StringComparer.Ordinal, cancellationToken);
        var analysis = entityType switch
        {
            "accounts" => await AnalyzeAccountsAsync(db, actor.CompanyId, request.ConnectionId, remoteEntities, links, request.DryRun, cancellationToken),
            "customers" => await AnalyzeCustomersAsync(db, actor.CompanyId, request.ConnectionId, remoteEntities, links, request.DryRun, cancellationToken),
            _ => await AnalyzeVendorsAsync(db, actor.CompanyId, request.ConnectionId, remoteEntities, links, request.DryRun, cancellationToken)
        };
        var completedAt = timeProvider.GetUtcNow();
        var status = request.DryRun
            ? analysis.ConflictCount + analysis.RejectedCount > 0 ? "PreviewWithIssues" : "PreviewReady"
            : analysis.ConflictCount + analysis.RejectedCount > 0 ? "CommittedWithIssues" : "Committed";
        var run = new IntegrationSyncRun
        {
            Id = Guid.NewGuid(),
            CompanyId = actor.CompanyId,
            IntegrationConnectionId = request.ConnectionId,
            ProviderCode = ProviderCode,
            EntityType = entityType,
            Direction = "Import",
            IsDryRun = request.DryRun,
            Status = status,
            FetchedCount = remoteEntities.Length,
            CreatedCount = analysis.CreatedCount,
            UpdatedCount = analysis.UpdatedCount,
            UnchangedCount = analysis.UnchangedCount,
            ConflictCount = analysis.ConflictCount,
            RejectedCount = analysis.RejectedCount,
            SnapshotSha256 = snapshotSha256,
            DetailJson = JsonSerializer.Serialize(analysis.Issues),
            InitiatedByUserId = actor.UserId,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt
        };
        db.IntegrationSyncRuns.Add(run);
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = actor.CompanyId,
            UserId = actor.UserId,
            Action = request.DryRun ? "integration.quickbooks.sync_previewed" : "integration.quickbooks.sync_committed",
            EntityType = nameof(IntegrationSyncRun),
            EntityId = run.Id,
            DetailJson = JsonSerializer.Serialize(new { connectionId = request.ConnectionId, entityType, run.IsDryRun, run.Status, run.FetchedCount, run.CreatedCount, run.UpdatedCount, run.UnchangedCount, run.ConflictCount, run.RejectedCount, run.SnapshotSha256 }),
            OccurredAtUtc = completedAt
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return QuickBooksSyncResult.Failure("The QuickBooks synchronization conflicted with another database change. No partial synchronization was committed; reload and preview again.", request.DryRun);
        }
        return new(true, string.Empty, run.Id, request.DryRun, run.FetchedCount, run.CreatedCount, run.UpdatedCount, run.UnchangedCount, run.ConflictCount, run.RejectedCount, snapshotSha256, analysis.Issues);
    }

    public async Task<IReadOnlyList<QuickBooksSyncRunSnapshot>> GetRecentRunsAsync(Guid? connectionId = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null || !CanViewRuns(actor.Principal)) return [];
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db.IntegrationSyncRuns.AsNoTracking().Where(run => run.CompanyId == actor.CompanyId && (!connectionId.HasValue || run.IntegrationConnectionId == connectionId)).ToListAsync(cancellationToken);
        var selected = runs.OrderByDescending(run => run.CompletedAtUtc).Take(Math.Clamp(limit, 1, 100)).ToArray();
        var userIds = selected.Where(run => run.InitiatedByUserId.HasValue).Select(run => run.InitiatedByUserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        return selected.Select(run => new QuickBooksSyncRunSnapshot(
            run.Id, run.IntegrationConnectionId, run.EntityType, run.Direction, run.IsDryRun, run.Status,
            run.FetchedCount, run.CreatedCount, run.UpdatedCount, run.UnchangedCount, run.ConflictCount, run.RejectedCount,
            run.SnapshotSha256, DeserializeIssues(run.DetailJson), run.InitiatedByUserId is { } userId ? users.GetValueOrDefault(userId) : null,
            run.StartedAtUtc, run.CompletedAtUtc)).ToArray();
    }

    private async Task<SyncAnalysis> AnalyzeAccountsAsync(BrassLedgerDbContext db, Guid companyId, Guid connectionId, IReadOnlyList<QuickBooksRemoteEntity> remotes, IReadOnlyDictionary<string, ExternalEntityLink> links, bool dryRun, CancellationToken cancellationToken)
    {
        var locals = await db.Accounts.Where(account => account.CompanyId == companyId).ToListAsync(cancellationToken);
        var byId = locals.ToDictionary(account => account.Id);
        var byNumber = locals.ToDictionary(account => account.Number, StringComparer.OrdinalIgnoreCase);
        var analysis = new SyncAnalysis { ConnectionId = connectionId };
        var synchronizedAt = timeProvider.GetUtcNow();
        foreach (var remote in remotes)
        {
            var number = string.IsNullOrWhiteSpace(remote.Number) ? $"QBO-A-{remote.Id}" : remote.Number.Trim();
            if (!ValidateRemote(remote, number, analysis)) continue;
            if (!TryMapAccountType(remote.AccountType, out var accountType, out var isControl))
            {
                analysis.Reject(remote.Id, "unsupported_account_type", "The QuickBooks account type is unsupported and was not imported.");
                continue;
            }
            if (!remote.Active)
            {
                analysis.Conflict(remote.Id, "inactive_remote_record", "QuickBooks marks this account inactive; BrassLedger does not silently deactivate a ledger account.");
                continue;
            }
            var remoteFingerprint = Fingerprint([CanonicalRemoteData(remote)]);
            if (!links.TryGetValue(remote.Id, out var link))
            {
                if (isControl)
                {
                    analysis.Conflict(remote.Id, "control_account_mapping_required", "Accounts Receivable and Accounts Payable control accounts require an explicit mapping; no control account was created.");
                    continue;
                }
                if (byNumber.ContainsKey(number))
                {
                    analysis.Conflict(remote.Id, "natural_key_collision", "An account already uses this account number; map it explicitly before importing.");
                    continue;
                }
                analysis.CreatedCount++;
                if (dryRun) continue;
                var account = new GeneralLedgerAccount { Id = Guid.NewGuid(), CompanyId = companyId, Number = number, Name = remote.Name.Trim(), Type = accountType, IsActive = remote.Active, IsControlAccount = false };
                db.Accounts.Add(account); byId[account.Id] = account; byNumber[number] = account;
                AddLink(db, companyId, analysis.ConnectionId, "accounts", remote, account.Id, remoteFingerprint, FingerprintAccount(account), synchronizedAt);
                continue;
            }
            analysis.ConnectionId = link.IntegrationConnectionId;
            if (!byId.TryGetValue(link.LocalEntityId, out var local))
            {
                analysis.Conflict(remote.Id, "local_record_missing", "The linked BrassLedger account no longer exists; repair the mapping before synchronizing.");
                continue;
            }
            var localFingerprint = FingerprintAccount(local);
            if (localFingerprint != link.LastLocalFingerprint)
            {
                analysis.Conflict(remote.Id, remoteFingerprint == link.LastRemoteFingerprint ? "local_changed" : "both_changed", "The BrassLedger account changed since the last synchronization; it was not overwritten.");
                continue;
            }
            if (remoteFingerprint == link.LastRemoteFingerprint)
            {
                analysis.UnchangedCount++;
                if (!dryRun) TouchLink(link, remote, remoteFingerprint, localFingerprint, synchronizedAt);
                continue;
            }
            if (local.Type != accountType || local.IsControlAccount != isControl)
            {
                analysis.Conflict(remote.Id, "account_classification_changed", "QuickBooks changed the account classification; review and map this account manually.");
                continue;
            }
            if (!string.Equals(local.Number, number, StringComparison.OrdinalIgnoreCase) && byNumber.TryGetValue(number, out var collision) && collision.Id != local.Id)
            {
                analysis.Conflict(remote.Id, "natural_key_collision", "Another BrassLedger account already uses the new QuickBooks account number.");
                continue;
            }
            analysis.UpdatedCount++;
            if (dryRun) continue;
            byNumber.Remove(local.Number); local.Number = number; local.Name = remote.Name.Trim(); local.IsActive = remote.Active; byNumber[number] = local;
            TouchLink(link, remote, remoteFingerprint, FingerprintAccount(local), synchronizedAt);
        }
        return analysis;
    }

    private async Task<SyncAnalysis> AnalyzeCustomersAsync(BrassLedgerDbContext db, Guid companyId, Guid connectionId, IReadOnlyList<QuickBooksRemoteEntity> remotes, IReadOnlyDictionary<string, ExternalEntityLink> links, bool dryRun, CancellationToken cancellationToken)
    {
        var locals = await db.Customers.Where(customer => customer.CompanyId == companyId).ToListAsync(cancellationToken);
        return AnalyzeParties(db, companyId, connectionId, "customers", remotes, links, dryRun, timeProvider.GetUtcNow(), locals,
            customer => customer.Id, customer => customer.CustomerNumber, customer => FingerprintParty(customer.CustomerNumber, customer.Name, customer.Email),
            (number, remote) => new Customer { Id = Guid.NewGuid(), CompanyId = companyId, CustomerNumber = number, Name = remote.Name.Trim(), Email = remote.Email.Trim() },
            (customer, number, remote) => { customer.CustomerNumber = number; customer.Name = remote.Name.Trim(); customer.Email = remote.Email.Trim(); });
    }

    private async Task<SyncAnalysis> AnalyzeVendorsAsync(BrassLedgerDbContext db, Guid companyId, Guid connectionId, IReadOnlyList<QuickBooksRemoteEntity> remotes, IReadOnlyDictionary<string, ExternalEntityLink> links, bool dryRun, CancellationToken cancellationToken)
    {
        var locals = await db.Vendors.Where(vendor => vendor.CompanyId == companyId).ToListAsync(cancellationToken);
        return AnalyzeParties(db, companyId, connectionId, "vendors", remotes, links, dryRun, timeProvider.GetUtcNow(), locals,
            vendor => vendor.Id, vendor => vendor.VendorNumber, vendor => FingerprintParty(vendor.VendorNumber, vendor.Name, vendor.Email),
            (number, remote) => new Vendor { Id = Guid.NewGuid(), CompanyId = companyId, VendorNumber = number, Name = remote.Name.Trim(), Email = remote.Email.Trim() },
            (vendor, number, remote) => { vendor.VendorNumber = number; vendor.Name = remote.Name.Trim(); vendor.Email = remote.Email.Trim(); });
    }

    private static SyncAnalysis AnalyzeParties<T>(BrassLedgerDbContext db, Guid companyId, Guid connectionId, string entityType, IReadOnlyList<QuickBooksRemoteEntity> remotes, IReadOnlyDictionary<string, ExternalEntityLink> links, bool dryRun, DateTimeOffset synchronizedAt, IReadOnlyList<T> locals, Func<T, Guid> id, Func<T, string> number, Func<T, string> fingerprint, Func<string, QuickBooksRemoteEntity, T> create, Action<T, string, QuickBooksRemoteEntity> update) where T : class
    {
        var byId = locals.ToDictionary(id);
        var byNumber = locals.ToDictionary(number, StringComparer.OrdinalIgnoreCase);
        var analysis = new SyncAnalysis { ConnectionId = connectionId };
        foreach (var remote in remotes)
        {
            var stableNumber = $"QBO-{(entityType == "customers" ? "C" : "V")}-{remote.Id}";
            if (!ValidateRemote(remote, stableNumber, analysis)) continue;
            if (!remote.Active)
            {
                analysis.Conflict(remote.Id, "inactive_remote_record", "QuickBooks marks this record inactive; BrassLedger does not silently delete or deactivate a party record.");
                continue;
            }
            var remoteFingerprint = Fingerprint([CanonicalRemoteData(remote)]);
            if (!links.TryGetValue(remote.Id, out var link))
            {
                if (byNumber.ContainsKey(stableNumber))
                {
                    analysis.Conflict(remote.Id, "natural_key_collision", "A BrassLedger record already uses the stable QuickBooks number; map it explicitly before importing.");
                    continue;
                }
                analysis.CreatedCount++;
                if (dryRun) continue;
                var local = create(stableNumber, remote); db.Add(local); byId[id(local)] = local; byNumber[stableNumber] = local;
                AddLink(db, companyId, analysis.ConnectionId, entityType, remote, id(local), remoteFingerprint, fingerprint(local), synchronizedAt);
                continue;
            }
            analysis.ConnectionId = link.IntegrationConnectionId;
            if (!byId.TryGetValue(link.LocalEntityId, out var existing))
            {
                analysis.Conflict(remote.Id, "local_record_missing", "The linked BrassLedger record no longer exists; repair the mapping before synchronizing.");
                continue;
            }
            var localFingerprint = fingerprint(existing);
            if (localFingerprint != link.LastLocalFingerprint)
            {
                analysis.Conflict(remote.Id, remoteFingerprint == link.LastRemoteFingerprint ? "local_changed" : "both_changed", "The BrassLedger record changed since the last synchronization; it was not overwritten.");
                continue;
            }
            if (remoteFingerprint == link.LastRemoteFingerprint)
            {
                analysis.UnchangedCount++;
                if (!dryRun) TouchLink(link, remote, remoteFingerprint, localFingerprint, synchronizedAt);
                continue;
            }
            analysis.UpdatedCount++;
            if (dryRun) continue;
            update(existing, stableNumber, remote);
            TouchLink(link, remote, remoteFingerprint, fingerprint(existing), synchronizedAt);
        }
        return analysis;
    }

    private async Task<AuthorizedConnection> LoadAuthorizationAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == connectionId && candidate.CompanyId == companyId && candidate.ProviderCode == ProviderCode, cancellationToken);
        if (connection is null) return new(null, null, null, "QuickBooks connection not found.");
        if (connection.Status != "Connected") return new(connection, null, null, "Validate or reconnect the QuickBooks company before synchronizing.");
        try
        {
            var credentials = JsonSerializer.Deserialize<TokenEnvelope>(connection.CredentialsJson);
            var settings = JsonSerializer.Deserialize<ConnectionSettings>(connection.SettingsJson);
            if (credentials is null || settings is null || credentials.Version != 1 || settings.Version != 1
                || string.IsNullOrWhiteSpace(credentials.AccessToken) || string.IsNullOrWhiteSpace(credentials.RefreshToken)
                || string.IsNullOrWhiteSpace(credentials.RealmId) || !string.Equals(credentials.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(credentials.RealmId, settings.RealmId, StringComparison.Ordinal)
                || (settings.Environment ?? string.Empty).Trim().ToUpperInvariant() is not ("SANDBOX" or "PRODUCTION"))
                return new(connection, null, settings, "The QuickBooks connection has no usable authorization. Reconnect the company.");
            return new(connection, credentials, settings, string.Empty);
        }
        catch (JsonException)
        {
            return new(connection, null, null, "The QuickBooks connection authorization cannot be read. Reconnect the company.");
        }
    }

    private async Task<QuickBooksSyncResult> RecordProviderFailureAsync(Actor actor, QuickBooksSyncRequest request, string entityType, DateTimeOffset startedAt, string errorCode, string message, CancellationToken cancellationToken)
    {
        var completedAt = timeProvider.GetUtcNow();
        var issue = new QuickBooksSyncIssue(string.Empty, SafeCode(errorCode), message);
        var run = new IntegrationSyncRun { Id = Guid.NewGuid(), CompanyId = actor.CompanyId, IntegrationConnectionId = request.ConnectionId, ProviderCode = ProviderCode, EntityType = entityType, Direction = "Import", IsDryRun = request.DryRun, Status = "ProviderFailed", RejectedCount = 1, DetailJson = JsonSerializer.Serialize(new[] { issue }), InitiatedByUserId = actor.UserId, StartedAtUtc = startedAt, CompletedAtUtc = completedAt };
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.IntegrationSyncRuns.Add(run);
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = actor.CompanyId, UserId = actor.UserId, Action = "integration.quickbooks.sync_failed", EntityType = nameof(IntegrationSyncRun), EntityId = run.Id, DetailJson = JsonSerializer.Serialize(new { connectionId = request.ConnectionId, entityType, errorCode = SafeCode(errorCode), noRecordsChanged = true }), OccurredAtUtc = completedAt });
        await db.SaveChangesAsync(cancellationToken);
        return new(false, message, run.Id, request.DryRun, 0, 0, 0, 0, 0, 1, string.Empty, [issue]);
    }

    private static bool ValidateRemote(QuickBooksRemoteEntity remote, string number, SyncAnalysis analysis)
    {
        if (string.IsNullOrWhiteSpace(remote.Id) || remote.Id.Length > 128 || remote.Id.Any(char.IsControl)) { analysis.Reject(string.Empty, "invalid_provider_id", "QuickBooks returned an invalid entity identifier."); return false; }
        if (string.IsNullOrWhiteSpace(remote.Name) || remote.Name.Length > 1024 || remote.Name.Any(char.IsControl)) { analysis.Reject(remote.Id, "invalid_name", "QuickBooks returned a missing or invalid name."); return false; }
        if (number.Length > 128 || number.Any(char.IsControl)) { analysis.Reject(remote.Id, "invalid_number", "QuickBooks returned an invalid record number."); return false; }
        if (remote.Email.Length > 320 || remote.Email.Any(char.IsControl)) { analysis.Reject(remote.Id, "invalid_email", "QuickBooks returned an invalid email value."); return false; }
        return true;
    }

    private static bool TryMapAccountType(string value, out AccountType accountType, out bool isControl)
    {
        isControl = false;
        switch (value.Trim().ToUpperInvariant())
        {
            case "BANK": case "OTHER CURRENT ASSET": case "FIXED ASSET": case "OTHER ASSET": accountType = AccountType.Asset; return true;
            case "ACCOUNTS RECEIVABLE": case "ACCOUNTS RECEIVABLE (A/R)": accountType = AccountType.Asset; isControl = true; return true;
            case "ACCOUNTS PAYABLE": case "ACCOUNTS PAYABLE (A/P)": accountType = AccountType.Liability; isControl = true; return true;
            case "CREDIT CARD": case "OTHER CURRENT LIABILITY": case "LONG TERM LIABILITY": accountType = AccountType.Liability; return true;
            case "EQUITY": accountType = AccountType.Equity; return true;
            case "INCOME": case "OTHER INCOME": accountType = AccountType.Revenue; return true;
            case "EXPENSE": case "OTHER EXPENSE": case "COST OF GOODS SOLD": accountType = AccountType.Expense; return true;
            default: accountType = default; return false;
        }
    }

    private static void AddLink(BrassLedgerDbContext db, Guid companyId, Guid connectionId, string entityType, QuickBooksRemoteEntity remote, Guid localId, string remoteFingerprint, string localFingerprint, DateTimeOffset synchronizedAt)
    {
        db.ExternalEntityLinks.Add(new ExternalEntityLink { Id = Guid.NewGuid(), CompanyId = companyId, IntegrationConnectionId = connectionId, ProviderCode = ProviderCode, EntityType = entityType, ProviderEntityId = remote.Id, LocalEntityId = localId, ProviderSyncToken = remote.SyncToken, LastRemoteFingerprint = remoteFingerprint, LastLocalFingerprint = localFingerprint, LastSynchronizedAtUtc = synchronizedAt });
    }

    private static void TouchLink(ExternalEntityLink link, QuickBooksRemoteEntity remote, string remoteFingerprint, string localFingerprint, DateTimeOffset synchronizedAt)
    {
        link.ProviderSyncToken = remote.SyncToken; link.LastRemoteFingerprint = remoteFingerprint; link.LastLocalFingerprint = localFingerprint; link.LastSynchronizedAtUtc = synchronizedAt;
    }

    private static string FingerprintAccount(GeneralLedgerAccount account) => Fingerprint([account.Number.Trim(), account.Name.Trim(), account.Type.ToString(), account.IsControlAccount.ToString(), account.IsActive.ToString()]);
    private static string FingerprintParty(string number, string name, string email) => Fingerprint([number.Trim(), name.Trim(), email.Trim().ToUpperInvariant()]);
    private static string CanonicalRemoteData(QuickBooksRemoteEntity remote) => string.Join('\u001f', remote.Id, remote.Active, remote.Name.Trim(), remote.Number.Trim(), remote.Email.Trim().ToUpperInvariant(), remote.AccountType.Trim(), remote.AccountSubType.Trim());
    private static string CanonicalRemoteSnapshot(QuickBooksRemoteEntity remote) => string.Join('\u001f', CanonicalRemoteData(remote), remote.SyncToken);
    private static string Fingerprint(IEnumerable<string> values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001e', values))));
    private static IReadOnlyList<QuickBooksSyncIssue> DeserializeIssues(string value) { try { return JsonSerializer.Deserialize<QuickBooksSyncIssue[]>(value) ?? []; } catch (JsonException) { return [new(string.Empty, "unreadable_history", "Stored synchronization details could not be read.")]; } }
    private static string SafeCode(string value) { var safe = new string((value ?? string.Empty).Where(character => char.IsLetterOrDigit(character) || character is '_' or '-').Take(64).ToArray()); return safe.Length == 0 ? "provider_request_failed" : safe; }

    private Actor? CurrentActor()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || !Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId)) return null;
        return new(userId, companyId, principal);
    }

    private static bool CanSynchronize(ClaimsPrincipal principal, string entityType)
    {
        if (principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")) return false;
        var privileged = principal.IsInRole("Administrator") || principal.IsInRole("Owner/CEO");
        var managesConnection = privileged || principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage);
        var dataPermission = entityType switch { "accounts" => BrassLedgerPermissions.LedgerManage, "customers" => BrassLedgerPermissions.ReceivablesManage, _ => BrassLedgerPermissions.PayablesManage };
        return managesConnection && (privileged || principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, dataPermission));
    }
    private static bool CanViewRuns(ClaimsPrincipal principal) => !principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true") && (principal.IsInRole("Administrator") || principal.IsInRole("Owner/CEO") || principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage));

    private sealed record Actor(Guid UserId, Guid CompanyId, ClaimsPrincipal Principal);
    private sealed record AuthorizedConnection(IntegrationConnection? Connection, TokenEnvelope? Credentials, ConnectionSettings? Settings, string ErrorMessage);
    private sealed record TokenEnvelope(int Version, string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc, DateTimeOffset RefreshTokenExpiresAtUtc, string TokenType, string Scope, string RealmId, DateTimeOffset IssuedAtUtc);
    private sealed record ConnectionSettings(int Version, string Environment, string RealmId, string CompanyName, string LegalName, string Country, DateTimeOffset LinkedAtUtc);
    private sealed class SyncAnalysis
    {
        public Guid ConnectionId { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int UnchangedCount { get; set; }
        public int ConflictCount { get; set; }
        public int RejectedCount { get; set; }
        public List<QuickBooksSyncIssue> Issues { get; } = [];
        public void Conflict(string id, string code, string message) { ConflictCount++; Issues.Add(new(id, code, message)); }
        public void Reject(string id, string code, string message) { RejectedCount++; Issues.Add(new(id, code, message)); }
    }
}
