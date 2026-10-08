using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class QuickBooksOnlineConnectionService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    IQuickBooksOnlineClient client,
    IOptions<QuickBooksOnlineOptions> optionsAccessor,
    TimeProvider timeProvider) : IQuickBooksOnlineConnectionService
{
    private const string ProviderCode = "quickbooks-online";
    private const int MaximumConnectionNameLength = 100;
    private static readonly TimeSpan CredentialOperationLeaseLifetime = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> CredentialOperationLocks = new();
    private readonly QuickBooksOnlineOptions _options = optionsAccessor.Value;

    public Task<QuickBooksOnlineAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var configured = _options.Enabled
            && !string.IsNullOrWhiteSpace(_options.ClientId)
            && !string.IsNullOrWhiteSpace(_options.ClientSecret)
            && !string.IsNullOrWhiteSpace(_options.RedirectUri);
        var message = configured
            ? $"QuickBooks Online OAuth is configured for the {_options.Environment} environment."
            : "QuickBooks Online OAuth is not configured on this installation. File interchange remains available.";
        return Task.FromResult(new QuickBooksOnlineAvailability(configured, NormalizeEnvironment(_options.Environment), message));
    }

    public async Task<QuickBooksAuthorizationStartResult> BeginAuthorizationAsync(BeginQuickBooksAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null || !CanManageConnections(actor.Principal))
            return QuickBooksAuthorizationStartResult.Failure("You are not authorized to manage accounting connections.");
        if (!_options.Enabled)
            return QuickBooksAuthorizationStartResult.Failure("QuickBooks Online OAuth is not configured on this installation.");

        var connectionName = (request.ConnectionName ?? string.Empty).Trim();
        if (connectionName.Length is < 1 or > MaximumConnectionNameLength || connectionName.Any(char.IsControl))
            return QuickBooksAuthorizationStartResult.Failure($"Connection name must contain 1 to {MaximumConnectionNameLength} printable characters.");
        var environment = NormalizeEnvironment(request.Environment);
        if (!string.Equals(environment, NormalizeEnvironment(_options.Environment), StringComparison.Ordinal))
            return QuickBooksAuthorizationStartResult.Failure($"This installation is configured for QuickBooks {_options.Environment}; cross-environment authorization is prohibited.");

        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        IntegrationConnection? existing = null;
        if (request.ConnectionId is { } connectionId)
        {
            existing = await db.IntegrationConnections.SingleOrDefaultAsync(
                connection => connection.Id == connectionId && connection.CompanyId == actor.CompanyId && connection.ProviderCode == ProviderCode,
                cancellationToken);
            if (existing is null) return QuickBooksAuthorizationStartResult.Failure("QuickBooks connection not found.");
            if (HasActiveCredentialOperationLease(existing, now))
                return QuickBooksAuthorizationStartResult.Failure("Another application instance is changing this QuickBooks authorization. Wait for it to finish before reconnecting.");
            connectionName = existing.Name;
        }
        else if (await db.IntegrationConnections.AnyAsync(
                     connection => connection.CompanyId == actor.CompanyId && connection.ProviderCode == ProviderCode && connection.Name == connectionName,
                     cancellationToken))
        {
            return QuickBooksAuthorizationStartResult.Failure("A QuickBooks connection with this name already exists. Use reconnect on the existing connection.");
        }

        if (existing is null)
        {
            existing = new IntegrationConnection
            {
                Id = Guid.NewGuid(),
                CompanyId = actor.CompanyId,
                ProviderCode = ProviderCode,
                Name = connectionName,
                Status = "AuthorizationPending",
                SettingsJson = JsonSerializer.Serialize(new { Version = 1, Environment = environment, AuthorizationPending = true }),
                CredentialsJson = "{}"
            };
            db.IntegrationConnections.Add(existing);
        }

        var actorAttempts = await db.OAuthAuthorizationAttempts
            .Where(attempt => attempt.CompanyId == actor.CompanyId && attempt.UserId == actor.UserId)
            .ToListAsync(cancellationToken);
        db.OAuthAuthorizationAttempts.RemoveRange(actorAttempts.Where(attempt => attempt.ExpiresAtUtc < now.AddDays(-1)));
        foreach (var pendingAttempt in actorAttempts.Where(attempt =>
                     attempt.ProviderCode == ProviderCode
                     && attempt.ConsumedAtUtc is null
                     && (existing is not null
                         ? attempt.ConnectionId == existing.Id
                         : attempt.ConnectionId is null && attempt.ConnectionName == connectionName)))
        {
            pendingAttempt.ConsumedAtUtc = now;
        }

        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var attempt = new OAuthAuthorizationAttempt
        {
            Id = Guid.NewGuid(),
            CompanyId = actor.CompanyId,
            UserId = actor.UserId,
            ConnectionId = existing.Id,
            ProviderCode = ProviderCode,
            ConnectionName = connectionName,
            Environment = environment,
            StateHash = HashState(state),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.AuthorizationStateLifetimeMinutes)
        };
        db.OAuthAuthorizationAttempts.Add(attempt);
        AddAudit(db, actor, "integration.oauth_started", attempt.Id, new { provider = ProviderCode, connectionName, environment, expiresAtUtc = attempt.ExpiresAtUtc });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return QuickBooksAuthorizationStartResult.Failure("Another operator created or changed this QuickBooks connection. Reload and use reconnect on the stored connection.");
        }
        return QuickBooksAuthorizationStartResult.Success(client.BuildAuthorizationUrl(state));
    }

    public async Task<QuickBooksAuthorizationCompletionResult> CompleteAuthorizationAsync(CompleteQuickBooksAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null || !CanManageConnections(actor.Principal))
            return QuickBooksAuthorizationCompletionResult.Failure("Sign in as the same authorized operator who started the QuickBooks connection.");
        if (!_options.Enabled)
            return QuickBooksAuthorizationCompletionResult.Failure("QuickBooks Online OAuth is not configured on this installation.");
        if (string.IsNullOrWhiteSpace(request.State) || request.State.Length > 1024)
            return QuickBooksAuthorizationCompletionResult.Failure("The QuickBooks authorization state is missing or invalid. Start the connection again.");

        var now = timeProvider.GetUtcNow();
        var stateHash = HashState(request.State);
        OAuthAuthorizationAttempt attempt;
        await using (var stateDb = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            attempt = await stateDb.OAuthAuthorizationAttempts.AsNoTracking().SingleOrDefaultAsync(candidate =>
                    candidate.StateHash == stateHash
                    && candidate.CompanyId == actor.CompanyId
                    && candidate.UserId == actor.UserId
                    && candidate.ProviderCode == ProviderCode,
                cancellationToken) ?? new OAuthAuthorizationAttempt();
            if (attempt.Id == Guid.Empty || attempt.ConsumedAtUtc is not null || attempt.ExpiresAtUtc <= now)
                return QuickBooksAuthorizationCompletionResult.Failure("The QuickBooks authorization request is invalid, expired, already used, or belongs to another session. Start the connection again.");

            var consumed = await stateDb.OAuthAuthorizationAttempts
                .Where(candidate => candidate.Id == attempt.Id && candidate.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ConsumedAtUtc, now), cancellationToken);
            if (consumed != 1)
                return QuickBooksAuthorizationCompletionResult.Failure("The QuickBooks authorization request is invalid, expired, already used, or belongs to another session. Start the connection again.");

            AddAudit(stateDb, actor, "integration.oauth_callback_received", attempt.Id, new { provider = ProviderCode, attempt.Environment });
            await stateDb.SaveChangesAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.ProviderError))
        {
            await RecordFailureAsync(actor, attempt.Id, "integration.oauth_denied", "The provider did not grant authorization.", cancellationToken);
            return QuickBooksAuthorizationCompletionResult.Failure("QuickBooks did not grant access. No credentials were saved; start the connection again when ready.");
        }
        if (!IsAuthorizationCodeValid(request.Code) || string.IsNullOrWhiteSpace(request.RealmId) || !RealmIdPattern().IsMatch(request.RealmId))
        {
            await RecordFailureAsync(actor, attempt.Id, "integration.oauth_invalid_callback", "The callback omitted required provider values.", cancellationToken);
            return QuickBooksAuthorizationCompletionResult.Failure("QuickBooks returned an incomplete authorization response. No credentials were saved; start the connection again.");
        }

        await using (var preflightDb = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            if (attempt.ConnectionId is { } reconnectId)
            {
                var reconnect = await preflightDb.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == reconnectId && candidate.CompanyId == actor.CompanyId && candidate.ProviderCode == ProviderCode, cancellationToken);
                if (reconnect is null)
                    return QuickBooksAuthorizationCompletionResult.Failure("The original QuickBooks connection no longer exists. No credentials were requested or saved.");
                if (HasActiveCredentialOperationLease(reconnect, now))
                    return QuickBooksAuthorizationCompletionResult.Failure("Another application instance is changing this QuickBooks authorization. Wait for that operation to finish, then start reconnect again.");
            }
            else if (await preflightDb.IntegrationConnections.AnyAsync(candidate => candidate.CompanyId == actor.CompanyId && candidate.ProviderCode == ProviderCode && candidate.Name == attempt.ConnectionName, cancellationToken))
            {
                return QuickBooksAuthorizationCompletionResult.Failure("A QuickBooks connection with this name already exists. No credentials were requested or saved; reconnect the existing connection instead.");
            }
        }

        string? reconnectLeaseId = null;
        if (attempt.ConnectionId is { } leasedConnectionId)
        {
            var reconnectLease = await TryAcquireCredentialOperationLeaseAsync(
                actor,
                leasedConnectionId,
                "Reconnect",
                ["AuthorizationPending", "Connected", "ValidationFailed", "ReauthorizationRequired", "DisconnectPending", "Disconnected"],
                cancellationToken);
            if (!reconnectLease.Succeeded)
                return QuickBooksAuthorizationCompletionResult.Failure(reconnectLease.ErrorMessage);
            reconnectLeaseId = reconnectLease.LeaseId;
        }

        async Task ReleaseReconnectLeaseAsync(string reason)
        {
            if (attempt.ConnectionId is not { } leasedId || reconnectLeaseId is null) return;
            await CompleteCredentialOperationLeaseAsync(
                actor,
                leasedId,
                reconnectLeaseId,
                null,
                "integration.oauth_reconnect_lease_released",
                new { provider = ProviderCode, reason = SafeCode(reason), credentialsChanged = false },
                CancellationToken.None);
            reconnectLeaseId = null;
        }

        QuickBooksTokenResponse token;
        QuickBooksTokenResponse? issuedToken = null;
        QuickBooksCompanyInfoResponse companyInfo;
        try
        {
            token = await client.ExchangeAuthorizationCodeAsync(request.Code!, cancellationToken);
            if (!token.Succeeded)
            {
                await RecordFailureAsync(actor, attempt.Id, "integration.oauth_token_failed", token.ErrorCode, cancellationToken);
                await ReleaseReconnectLeaseAsync(token.ErrorCode);
                return QuickBooksAuthorizationCompletionResult.Failure(ProviderFailure("exchange the authorization code", token.ErrorCode));
            }
            issuedToken = token;
            if (!string.Equals(token.TokenType, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                await RecordFailureAsync(actor, attempt.Id, "integration.oauth_token_failed", "unexpected_token_type", cancellationToken);
                var cleaned = await CleanupUnusedAuthorizationAsync(actor, attempt.Id, token, "unexpected_token_type");
                await ReleaseReconnectLeaseAsync("unexpected_token_type");
                return QuickBooksAuthorizationCompletionResult.Failure("QuickBooks returned an unsupported token type. No credentials were saved." + UnusedGrantCleanupMessage(cleaned));
            }
            companyInfo = await client.GetCompanyInfoAsync(attempt.Environment, request.RealmId, token.AccessToken, cancellationToken);
            if (!companyInfo.Succeeded)
            {
                await RecordFailureAsync(actor, attempt.Id, "integration.oauth_validation_failed", companyInfo.ErrorCode, cancellationToken);
                var cleaned = await CleanupUnusedAuthorizationAsync(actor, attempt.Id, token, companyInfo.ErrorCode);
                await ReleaseReconnectLeaseAsync(companyInfo.ErrorCode);
                return QuickBooksAuthorizationCompletionResult.Failure(ProviderFailure("validate the selected company", companyInfo.ErrorCode) + UnusedGrantCleanupMessage(cleaned));
            }
        }
        catch (HttpRequestException)
        {
            await RecordFailureAsync(actor, attempt.Id, "integration.oauth_provider_unavailable", "provider_transport_failure", cancellationToken);
            var cleanupMessage = issuedToken is null ? string.Empty : UnusedGrantCleanupMessage(await CleanupUnusedAuthorizationAsync(actor, attempt.Id, issuedToken, "provider_transport_failure"));
            await ReleaseReconnectLeaseAsync("provider_transport_failure");
            return QuickBooksAuthorizationCompletionResult.Failure("QuickBooks could not be reached securely. No credentials were saved; start the connection again after provider connectivity is restored." + cleanupMessage);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordFailureAsync(actor, attempt.Id, "integration.oauth_provider_unavailable", "provider_timeout", cancellationToken);
            var cleanupMessage = issuedToken is null ? string.Empty : UnusedGrantCleanupMessage(await CleanupUnusedAuthorizationAsync(actor, attempt.Id, issuedToken, "provider_timeout"));
            await ReleaseReconnectLeaseAsync("provider_timeout");
            return QuickBooksAuthorizationCompletionResult.Failure("QuickBooks did not respond before the secure connection timed out. No credentials were saved." + cleanupMessage);
        }

        var accessExpiresAt = now.AddSeconds(token.AccessTokenExpiresInSeconds);
        var refreshExpiresAt = now.AddSeconds(token.RefreshTokenExpiresInSeconds);
        var credentials = new QuickBooksTokenEnvelope(
            1,
            token.AccessToken,
            token.RefreshToken,
            accessExpiresAt,
            refreshExpiresAt,
            "Bearer",
            token.Scope,
            request.RealmId,
            now);
        var settings = new QuickBooksConnectionSettings(
            1,
            attempt.Environment,
            request.RealmId,
            DisplayCompanyName(companyInfo, attempt.ConnectionName),
            companyInfo.LegalName,
            companyInfo.Country,
            now);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        IntegrationConnection? connection = null;
        if (attempt.ConnectionId is { } connectionId)
            connection = await db.IntegrationConnections.SingleOrDefaultAsync(candidate => candidate.Id == connectionId && candidate.CompanyId == actor.CompanyId && candidate.ProviderCode == ProviderCode, cancellationToken);
        if (attempt.ConnectionId.HasValue && connection is null)
        {
            var cleaned = await CleanupUnusedAuthorizationAsync(actor, attempt.Id, token, "connection_removed_during_callback");
            await ReleaseReconnectLeaseAsync("connection_removed_during_callback");
            return QuickBooksAuthorizationCompletionResult.Failure("The original QuickBooks connection no longer exists. No credentials were saved." + UnusedGrantCleanupMessage(cleaned));
        }
        if (connection is null && await db.IntegrationConnections.AnyAsync(candidate => candidate.CompanyId == actor.CompanyId && candidate.ProviderCode == ProviderCode && candidate.Name == attempt.ConnectionName, cancellationToken))
        {
            var cleaned = await CleanupUnusedAuthorizationAsync(actor, attempt.Id, token, "connection_name_claimed_during_callback");
            await ReleaseReconnectLeaseAsync("connection_name_claimed_during_callback");
            return QuickBooksAuthorizationCompletionResult.Failure("A QuickBooks connection with this name was created while authorization was in progress. No credentials were saved." + UnusedGrantCleanupMessage(cleaned));
        }
        if (connection is not null
            && (reconnectLeaseId is not null
                ? !string.Equals(connection.CredentialOperationLeaseId, reconnectLeaseId, StringComparison.Ordinal)
                : HasActiveCredentialOperationLease(connection, timeProvider.GetUtcNow())))
        {
            var cleaned = await CleanupUnusedAuthorizationAsync(actor, attempt.Id, token, "credential_operation_started_during_callback");
            await ReleaseReconnectLeaseAsync("credential_operation_started_during_callback");
            return QuickBooksAuthorizationCompletionResult.Failure("Another application instance began changing this QuickBooks authorization. No credentials were saved; start reconnect again after it finishes." + UnusedGrantCleanupMessage(cleaned));
        }

        connection ??= new IntegrationConnection
        {
            Id = Guid.NewGuid(),
            CompanyId = actor.CompanyId,
            ProviderCode = ProviderCode,
            Name = attempt.ConnectionName
        };
        connection.Status = "Connected";
        connection.SettingsJson = JsonSerializer.Serialize(settings);
        connection.CredentialsJson = JsonSerializer.Serialize(credentials);
        connection.LastValidatedAtUtc = now;
        connection.CredentialVersion++;
        ClearCredentialOperationLease(connection);
        if (db.Entry(connection).State == EntityState.Detached) db.IntegrationConnections.Add(connection);
        AddAudit(db, actor, "integration.connected", connection.Id, new { provider = ProviderCode, connection.Name, attempt.Environment, realmId = request.RealmId, companyName = settings.CompanyName });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var cleaned = await CleanupUnusedAuthorizationAsync(actor, attempt.Id, token, "connection_changed_during_callback");
            await ReleaseReconnectLeaseAsync("connection_changed_during_callback");
            return QuickBooksAuthorizationCompletionResult.Failure("The QuickBooks connection changed while authorization was completing. No credentials were saved; reload and start reconnect again." + UnusedGrantCleanupMessage(cleaned));
        }
        reconnectLeaseId = null;
        return QuickBooksAuthorizationCompletionResult.Success(connection.Id, settings.CompanyName);
    }

    public async Task<TransactionResult> ValidateConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null || !CanManageConnections(actor.Principal)) return TransactionResult.Failure("You are not authorized to validate accounting connections.");
        var loaded = await LoadConnectionAsync(actor.CompanyId, connectionId, cancellationToken);
        if (loaded.Connection is null || loaded.Credentials is null) return TransactionResult.Failure(loaded.ErrorMessage);
        if (HasActiveCredentialOperationLease(loaded.Connection, timeProvider.GetUtcNow()))
            return TransactionResult.Failure("Another application instance is changing this QuickBooks authorization. Wait for it to finish before validating.");
        if (loaded.Connection.Status is "Disconnected" or "DisconnectPending" or "ReauthorizationRequired")
            return TransactionResult.Failure("Reconnect the QuickBooks company before validating it.");
        if (loaded.Credentials.RefreshTokenExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            await SetReauthorizationRequiredAsync(actor, connectionId, "refresh_token_expired", cancellationToken);
            return TransactionResult.Failure("The QuickBooks authorization has expired. Reconnect the company.");
        }
        if (loaded.Credentials.AccessTokenExpiresAtUtc <= timeProvider.GetUtcNow().AddMinutes(2))
        {
            var refresh = await RefreshConnectionAsync(connectionId, cancellationToken);
            if (!refresh.Succeeded) return refresh;
            loaded = await LoadConnectionAsync(actor.CompanyId, connectionId, cancellationToken);
            if (loaded.Connection is null || loaded.Credentials is null) return TransactionResult.Failure(loaded.ErrorMessage);
        }

        QuickBooksCompanyInfoResponse companyInfo;
        try
        {
            companyInfo = await client.GetCompanyInfoAsync(loaded.Settings!.Environment, loaded.Credentials.RealmId, loaded.Credentials.AccessToken, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return TransactionResult.Failure("QuickBooks could not be reached securely. The saved authorization was not changed.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TransactionResult.Failure("QuickBooks validation timed out. The saved authorization was not changed.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.IntegrationConnections.SingleAsync(candidate => candidate.Id == connectionId && candidate.CompanyId == actor.CompanyId && candidate.ProviderCode == ProviderCode, cancellationToken);
        if (connection.CredentialVersion != loaded.Connection.CredentialVersion)
            return TransactionResult.Failure("The QuickBooks authorization changed while validation was in progress. Reload before retrying.");
        if (!companyInfo.Succeeded)
        {
            connection.Status = companyInfo.ErrorCode is "AuthenticationFailed" or "3200" or "invalid_grant" ? "ReauthorizationRequired" : "ValidationFailed";
            connection.CredentialVersion++;
            AddAudit(db, actor, "integration.validation_failed", connection.Id, new { provider = ProviderCode, errorCode = companyInfo.ErrorCode });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return TransactionResult.Failure("The QuickBooks authorization changed while validation was in progress. Reload before retrying.");
            }
            return TransactionResult.Failure(ProviderFailure("validate the selected company", companyInfo.ErrorCode));
        }

        var now = timeProvider.GetUtcNow();
        connection.Status = "Connected";
        connection.LastValidatedAtUtc = now;
        connection.CredentialVersion++;
        connection.SettingsJson = JsonSerializer.Serialize(loaded.Settings! with
        {
            CompanyName = DisplayCompanyName(companyInfo, connection.Name),
            LegalName = companyInfo.LegalName,
            Country = companyInfo.Country,
            LinkedAtUtc = loaded.Settings.LinkedAtUtc
        });
        AddAudit(db, actor, "integration.validated", connection.Id, new { provider = ProviderCode, connection.Name });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The QuickBooks authorization changed while validation was in progress. Reload before retrying.");
        }
        return TransactionResult.Success(connection.Id);
    }

    public async Task<TransactionResult> RefreshConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null || !CanManageConnections(actor.Principal)) return TransactionResult.Failure("You are not authorized to refresh accounting connections.");
        var operationLock = CredentialOperationLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await operationLock.WaitAsync(cancellationToken);
        string? leaseId = null;
        var leaseCompleted = false;
        try
        {
            var lease = await TryAcquireCredentialOperationLeaseAsync(
                actor,
                connectionId,
                "Refresh",
                ["Connected", "ValidationFailed"],
                cancellationToken);
            if (!lease.Succeeded) return TransactionResult.Failure(lease.ErrorMessage);
            leaseId = lease.LeaseId;

            var loaded = await LoadConnectionAsync(actor.CompanyId, connectionId, cancellationToken);
            if (loaded.Connection is null || loaded.Credentials is null) return TransactionResult.Failure(loaded.ErrorMessage);
            if (!string.Equals(loaded.Connection.CredentialOperationLeaseId, leaseId, StringComparison.Ordinal))
                return TransactionResult.Failure("Another application instance took control of the QuickBooks credential operation. Reload before retrying.");
            if (loaded.Credentials.RefreshTokenExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, "ReauthorizationRequired", "integration.reauthorization_required", new { provider = ProviderCode, reason = "refresh_token_expired" }, cancellationToken);
                return TransactionResult.Failure("The QuickBooks authorization has expired. Reconnect the company.");
            }

            QuickBooksTokenResponse token;
            try
            {
                token = await client.RefreshTokenAsync(loaded.Credentials.RefreshToken, cancellationToken);
            }
            catch (HttpRequestException)
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, null, "integration.token_refresh_failed", new { provider = ProviderCode, reason = "provider_transport_failure", credentialsRetained = true }, CancellationToken.None);
                return TransactionResult.Failure("QuickBooks could not be reached securely. The existing encrypted tokens were retained.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, null, "integration.token_refresh_failed", new { provider = ProviderCode, reason = "provider_timeout", credentialsRetained = true }, CancellationToken.None);
                return TransactionResult.Failure("QuickBooks token refresh timed out. The existing encrypted tokens were retained.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, "ReauthorizationRequired", "integration.token_refresh_outcome_unknown", new { provider = ProviderCode, reason = "request_cancelled", reconnectRequired = true }, CancellationToken.None);
                throw;
            }
            if (!token.Succeeded)
            {
                var requiresAuthorization = token.ErrorCode is "invalid_grant" or "invalid_token";
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(
                    actor,
                    connectionId,
                    leaseId,
                    requiresAuthorization ? "ReauthorizationRequired" : null,
                    requiresAuthorization ? "integration.reauthorization_required" : "integration.token_refresh_failed",
                    new { provider = ProviderCode, reason = SafeCode(token.ErrorCode), credentialsRetained = true },
                    cancellationToken);
                return TransactionResult.Failure(ProviderFailure("refresh authorization", token.ErrorCode));
            }
            if (!string.Equals(token.TokenType, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, null, "integration.token_refresh_failed", new { provider = ProviderCode, reason = "unexpected_token_type", credentialsRetained = true }, cancellationToken);
                return TransactionResult.Failure("QuickBooks returned an unsupported token type. Existing encrypted tokens were retained.");
            }

            var now = timeProvider.GetUtcNow();
            var replacement = loaded.Credentials with
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                AccessTokenExpiresAtUtc = now.AddSeconds(token.AccessTokenExpiresInSeconds),
                RefreshTokenExpiresAtUtc = now.AddSeconds(token.RefreshTokenExpiresInSeconds),
                Scope = token.Scope,
                IssuedAtUtc = now
            };
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var connection = await db.IntegrationConnections.SingleAsync(candidate => candidate.Id == connectionId && candidate.CompanyId == actor.CompanyId && candidate.ProviderCode == ProviderCode, cancellationToken);
            if (!string.Equals(connection.CredentialOperationLeaseId, leaseId, StringComparison.Ordinal))
                return TransactionResult.Failure("Another application instance took control of the QuickBooks credential operation. Reload before retrying.");
            connection.CredentialsJson = JsonSerializer.Serialize(replacement);
            connection.CredentialVersion++;
            connection.Status = "Connected";
            ClearCredentialOperationLease(connection);
            AddAudit(db, actor, "integration.token_refreshed", connection.Id, new { provider = ProviderCode, connection.Name, accessExpiresAtUtc = replacement.AccessTokenExpiresAtUtc });
            await db.SaveChangesAsync(cancellationToken);
            leaseCompleted = true;
            return TransactionResult.Success(connection.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The QuickBooks authorization changed concurrently. Reload before retrying.");
        }
        finally
        {
            if (leaseId is not null && !leaseCompleted)
                await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, null, "integration.credential_operation_released", new { provider = ProviderCode, operation = "Refresh", completed = false }, CancellationToken.None);
            operationLock.Release();
        }
    }

    public async Task<TransactionResult> DisconnectAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null || !CanManageConnections(actor.Principal)) return TransactionResult.Failure("You are not authorized to disconnect accounting connections.");
        var operationLock = CredentialOperationLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await operationLock.WaitAsync(cancellationToken);
        string? leaseId = null;
        var leaseCompleted = false;
        try
        {
            var lease = await TryAcquireCredentialOperationLeaseAsync(
                actor,
                connectionId,
                "Disconnect",
                ["Connected", "ValidationFailed", "ReauthorizationRequired", "DisconnectPending"],
                cancellationToken);
            if (!lease.Succeeded) return TransactionResult.Failure(lease.ErrorMessage);
            leaseId = lease.LeaseId;

            var loaded = await LoadConnectionAsync(actor.CompanyId, connectionId, cancellationToken);
            if (loaded.Connection is null || loaded.Credentials is null) return TransactionResult.Failure(loaded.ErrorMessage);
            if (!string.Equals(loaded.Connection.CredentialOperationLeaseId, leaseId, StringComparison.Ordinal))
                return TransactionResult.Failure("Another application instance took control of the QuickBooks credential operation. Reload before retrying.");

            QuickBooksProviderResult revoked;
            try
            {
                revoked = await client.RevokeTokenAsync(loaded.Credentials.RefreshToken, cancellationToken);
            }
            catch (HttpRequestException)
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, "DisconnectPending", "integration.disconnect_failed", new { provider = ProviderCode, reason = "provider_transport_failure", credentialsRetained = true }, CancellationToken.None);
                return TransactionResult.Failure("QuickBooks could not be reached, so the remote authorization may still be active. Encrypted tokens were retained for a safe retry.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, "DisconnectPending", "integration.disconnect_failed", new { provider = ProviderCode, reason = "provider_timeout", credentialsRetained = true }, CancellationToken.None);
                return TransactionResult.Failure("QuickBooks revocation timed out, so the remote authorization may still be active. Encrypted tokens were retained for a safe retry.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, "DisconnectPending", "integration.disconnect_outcome_unknown", new { provider = ProviderCode, reason = "request_cancelled", credentialsRetained = true }, CancellationToken.None);
                throw;
            }
            if (!revoked.Succeeded)
            {
                leaseCompleted = await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, "DisconnectPending", "integration.disconnect_failed", new { provider = ProviderCode, reason = SafeCode(revoked.ErrorCode), credentialsRetained = true }, cancellationToken);
                return TransactionResult.Failure(ProviderFailure("revoke authorization", revoked.ErrorCode) + " Encrypted tokens were retained for a safe retry.");
            }

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var connection = await db.IntegrationConnections.SingleAsync(candidate => candidate.Id == connectionId && candidate.CompanyId == actor.CompanyId && candidate.ProviderCode == ProviderCode, cancellationToken);
            if (!string.Equals(connection.CredentialOperationLeaseId, leaseId, StringComparison.Ordinal))
                return TransactionResult.Failure("Another application instance took control of the QuickBooks credential operation. The provider grant may have been revoked; reconnect before using this connection.");
            connection.CredentialsJson = "{}";
            connection.Status = "Disconnected";
            connection.CredentialVersion++;
            ClearCredentialOperationLease(connection);
            AddAudit(db, actor, "integration.disconnected", connection.Id, new { provider = ProviderCode, connection.Name, remoteAuthorizationRevoked = true });
            await db.SaveChangesAsync(cancellationToken);
            leaseCompleted = true;
            return TransactionResult.Success(connection.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The QuickBooks authorization changed concurrently. Reload before retrying.");
        }
        finally
        {
            if (leaseId is not null && !leaseCompleted)
                await CompleteCredentialOperationLeaseAsync(actor, connectionId, leaseId, null, "integration.credential_operation_released", new { provider = ProviderCode, operation = "Disconnect", completed = false }, CancellationToken.None);
            operationLock.Release();
        }
    }

    private async Task<LoadedConnection> LoadConnectionAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == connectionId && candidate.CompanyId == companyId && candidate.ProviderCode == ProviderCode, cancellationToken);
        if (connection is null) return new(null, null, null, "QuickBooks connection not found.");
        try
        {
            var credentials = JsonSerializer.Deserialize<QuickBooksTokenEnvelope>(connection.CredentialsJson);
            var settings = JsonSerializer.Deserialize<QuickBooksConnectionSettings>(connection.SettingsJson);
            if (credentials is null || settings is null || credentials.Version != 1 || settings.Version != 1
                || string.IsNullOrWhiteSpace(credentials.AccessToken) || string.IsNullOrWhiteSpace(credentials.RefreshToken)
                || string.IsNullOrWhiteSpace(credentials.RealmId) || !RealmIdPattern().IsMatch(credentials.RealmId)
                || !string.Equals(credentials.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(credentials.RealmId, settings.RealmId, StringComparison.Ordinal)
                || !IsKnownEnvironment(settings.Environment)
                || !string.Equals(NormalizeEnvironment(settings.Environment), NormalizeEnvironment(_options.Environment), StringComparison.Ordinal))
                return new(connection, null, settings, "The QuickBooks connection has no usable authorization. Reconnect the company.");
            return new(connection, credentials, settings, string.Empty);
        }
        catch (JsonException)
        {
            return new(connection, null, null, "The QuickBooks connection authorization cannot be read. Reconnect the company.");
        }
    }

    private async Task<CredentialOperationLeaseResult> TryAcquireCredentialOperationLeaseAsync(
        Actor actor,
        Guid connectionId,
        string operation,
        IReadOnlyCollection<string> allowedStatuses,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.IntegrationConnections.SingleOrDefaultAsync(candidate =>
            candidate.Id == connectionId
            && candidate.CompanyId == actor.CompanyId
            && candidate.ProviderCode == ProviderCode,
            cancellationToken);
        if (connection is null) return CredentialOperationLeaseResult.Failure("QuickBooks connection not found.");
        if (!allowedStatuses.Contains(connection.Status, StringComparer.Ordinal))
            return CredentialOperationLeaseResult.Failure("The QuickBooks connection is not in a state that permits this credential operation. Validate or reconnect it first.");

        var hasLease = !string.IsNullOrWhiteSpace(connection.CredentialOperationLeaseId);
        if (hasLease && (!connection.CredentialOperationLeaseExpiresAtUtc.HasValue || connection.CredentialOperationLeaseExpiresAtUtc > now))
            return CredentialOperationLeaseResult.Failure($"Another application instance is already performing QuickBooks {SafeCode(connection.CredentialOperation).ToLowerInvariant()}. Wait for it to finish or for its lease to expire, then retry.");

        var recoveredExpiredLease = hasLease;
        var priorOperation = connection.CredentialOperation;
        var leaseId = Guid.NewGuid().ToString("N");
        connection.CredentialOperationLeaseId = leaseId;
        connection.CredentialOperation = operation;
        connection.CredentialOperationLeaseExpiresAtUtc = now.Add(CredentialOperationLeaseLifetime);
        connection.CredentialVersion++;
        if (recoveredExpiredLease)
            AddAudit(db, actor, "integration.credential_operation_lease_recovered", connection.Id, new { provider = ProviderCode, priorOperation = SafeCode(priorOperation), priorLeaseExpired = true });
        AddAudit(db, actor, "integration.credential_operation_lease_acquired", connection.Id, new { provider = ProviderCode, operation, leaseExpiresAtUtc = connection.CredentialOperationLeaseExpiresAtUtc });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return CredentialOperationLeaseResult.Success(leaseId);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CredentialOperationLeaseResult.Failure("Another application instance began changing this QuickBooks authorization. Reload before retrying.");
        }
        catch (DbUpdateException)
        {
            return CredentialOperationLeaseResult.Failure("The QuickBooks credential operation could not obtain a durable database lease. Reload before retrying.");
        }
    }

    private async Task<bool> CompleteCredentialOperationLeaseAsync(
        Actor actor,
        Guid connectionId,
        string leaseId,
        string? replacementStatus,
        string auditAction,
        object auditDetail,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.IntegrationConnections.SingleOrDefaultAsync(candidate =>
            candidate.Id == connectionId
            && candidate.CompanyId == actor.CompanyId
            && candidate.ProviderCode == ProviderCode,
            cancellationToken);
        if (connection is null || !string.Equals(connection.CredentialOperationLeaseId, leaseId, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(replacementStatus)) connection.Status = replacementStatus;
        ClearCredentialOperationLease(connection);
        connection.CredentialVersion++;
        AddAudit(db, actor, auditAction, connection.Id, auditDetail);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private static void ClearCredentialOperationLease(IntegrationConnection connection)
    {
        connection.CredentialOperationLeaseId = string.Empty;
        connection.CredentialOperation = string.Empty;
        connection.CredentialOperationLeaseExpiresAtUtc = null;
    }

    private async Task<bool> CleanupUnusedAuthorizationAsync(Actor actor, Guid attemptId, QuickBooksTokenResponse token, string reason)
    {
        var revoked = false;
        try
        {
            var result = await client.RevokeTokenAsync(token.RefreshToken, CancellationToken.None);
            revoked = result.Succeeded;
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }

        try
        {
            await RecordFailureAsync(
                actor,
                attemptId,
                revoked ? "integration.oauth_unused_grant_revoked" : "integration.oauth_unused_grant_revocation_failed",
                reason,
                CancellationToken.None);
        }
        catch (DbUpdateException)
        {
        }
        return revoked;
    }

    private static bool HasActiveCredentialOperationLease(IntegrationConnection connection, DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(connection.CredentialOperationLeaseId)
        && (!connection.CredentialOperationLeaseExpiresAtUtc.HasValue || connection.CredentialOperationLeaseExpiresAtUtc > now);

    private static string UnusedGrantCleanupMessage(bool revoked) => revoked
        ? " The unused Intuit grant was revoked."
        : " BrassLedger could not confirm cleanup of the unused Intuit grant; remove its access from the Intuit account before retrying.";

    private async Task SetReauthorizationRequiredAsync(Actor actor, Guid connectionId, string reason, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.IntegrationConnections.SingleOrDefaultAsync(candidate => candidate.Id == connectionId && candidate.CompanyId == actor.CompanyId && candidate.ProviderCode == ProviderCode, cancellationToken);
        if (connection is null) return;
        connection.Status = "ReauthorizationRequired";
        connection.CredentialVersion++;
        AddAudit(db, actor, "integration.reauthorization_required", connection.Id, new { provider = ProviderCode, reason = SafeCode(reason) });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
        }
    }

    private async Task RecordFailureAsync(Actor actor, Guid attemptId, string action, string reason, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        AddAudit(db, actor, action, attemptId, new { provider = ProviderCode, reason = SafeCode(reason) });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddAudit(BrassLedgerDbContext db, Actor actor, string action, Guid entityId, object detail)
    {
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = actor.CompanyId,
            UserId = actor.UserId,
            Action = action,
            EntityType = action.Contains("callback", StringComparison.Ordinal) || action.Contains("oauth_", StringComparison.Ordinal) ? "OAuthAuthorizationAttempt" : "IntegrationConnection",
            EntityId = entityId,
            DetailJson = JsonSerializer.Serialize(detail),
            OccurredAtUtc = actor.Now
        });
    }

    private Actor? CurrentActor()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId))
            return null;
        return new Actor(userId, companyId, principal, timeProvider.GetUtcNow());
    }

    private static bool CanManageConnections(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")) return false;
        return principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage)
            || principal.IsInRole("Administrator")
            || principal.IsInRole("Owner/CEO");
    }

    private static string NormalizeEnvironment(string value) => string.Equals(value?.Trim(), "Production", StringComparison.OrdinalIgnoreCase) ? "Production" : "Sandbox";
    private static bool IsKnownEnvironment(string value) => value?.Trim().ToUpperInvariant() is "SANDBOX" or "PRODUCTION";
    private static string HashState(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
    private static bool IsAuthorizationCodeValid(string? code) => !string.IsNullOrWhiteSpace(code) && code.Length <= 4096 && !code.Any(char.IsControl);
    private static string DisplayCompanyName(QuickBooksCompanyInfoResponse companyInfo, string fallback) => string.IsNullOrWhiteSpace(companyInfo.CompanyName) ? fallback : companyInfo.CompanyName.Trim();
    private static string ProviderFailure(string operation, string errorCode) => $"QuickBooks could not {operation} ({SafeCode(errorCode)}).";
    private static string SafeCode(string value)
    {
        var sanitized = new string((value ?? string.Empty).Where(character => char.IsLetterOrDigit(character) || character is '_' or '-').Take(64).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "provider_request_failed" : sanitized;
    }

    [GeneratedRegex("^[0-9]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RealmIdPattern();

    private sealed record Actor(Guid UserId, Guid CompanyId, ClaimsPrincipal Principal, DateTimeOffset Now);
    private sealed record LoadedConnection(IntegrationConnection? Connection, QuickBooksTokenEnvelope? Credentials, QuickBooksConnectionSettings? Settings, string ErrorMessage);
    private sealed record CredentialOperationLeaseResult(bool Succeeded, string LeaseId, string ErrorMessage)
    {
        public static CredentialOperationLeaseResult Success(string leaseId) => new(true, leaseId, string.Empty);
        public static CredentialOperationLeaseResult Failure(string errorMessage) => new(false, string.Empty, errorMessage);
    }
    private sealed record QuickBooksTokenEnvelope(int Version, string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc, DateTimeOffset RefreshTokenExpiresAtUtc, string TokenType, string Scope, string RealmId, DateTimeOffset IssuedAtUtc);
    private sealed record QuickBooksConnectionSettings(int Version, string Environment, string RealmId, string CompanyName, string LegalName, string Country, DateTimeOffset LinkedAtUtc);
}
