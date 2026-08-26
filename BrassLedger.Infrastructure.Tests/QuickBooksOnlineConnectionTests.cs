using System.Security.Claims;
using System.Net;
using System.Text;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BrassLedger.Infrastructure.Tests;

public sealed class QuickBooksOnlineConnectionTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine("/home/rich/temp", "BrassLedger.QuickBooks.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OAuthLifecycle_BindsOneUseStateEncryptsRotatingTokensAndRequiresConfirmedRevocation()
    {
        var provider = new FakeQuickBooksOnlineClient();
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IQuickBooksOnlineConnectionService>();

        var start = await service.BeginAuthorizationAsync(new(null, "Primary books", "Sandbox"));

        Assert.True(start.Succeeded, start.ErrorMessage);
        Assert.DoesNotContain("test-client-secret", start.AuthorizationUrl, StringComparison.Ordinal);
        var authorizationUri = new Uri(start.AuthorizationUrl!);
        Assert.Equal("appcenter.intuit.com", authorizationUri.Host);
        var query = QueryHelpers.ParseQuery(authorizationUri.Query);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("com.intuit.quickbooks.accounting", query["scope"]);
        Assert.Equal("http://127.0.0.1:5099/integrations/quickbooks-online/callback", query["redirect_uri"]);
        var state = query["state"].ToString();
        Assert.True(state.Length >= 40);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var attempt = await db.OAuthAuthorizationAttempts.SingleAsync();
            Assert.Equal(actor.UserId, attempt.UserId);
            Assert.Equal(actor.CompanyId, attempt.CompanyId);
            Assert.NotEqual(state, attempt.StateHash);
            Assert.Equal(64, attempt.StateHash.Length);
        }

        var otherCompany = Guid.NewGuid();
        SetContext(scope.ServiceProvider, actor.UserId, otherCompany);
        var wrongCompany = await service.CompleteAuthorizationAsync(new(state, "one-use-code", "123456789", null, null));
        Assert.False(wrongCompany.Succeeded);
        Assert.Equal(0, provider.ExchangeCodeCount);

        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var completion = await service.CompleteAuthorizationAsync(new(state, "one-use-code", "123456789", null, null));

        Assert.True(completion.Succeeded, completion.ErrorMessage);
        Assert.Equal("Acme QuickBooks", completion.CompanyName);
        Assert.Equal(1, provider.ExchangeCodeCount);
        var replay = await service.CompleteAuthorizationAsync(new(state, "one-use-code", "123456789", null, null));
        Assert.False(replay.Succeeded);
        Assert.Equal(1, provider.ExchangeCodeCount);

        var connectionId = completion.ConnectionId!.Value;
        var snapshots = await scope.ServiceProvider.GetRequiredService<IIntegrationService>().GetConnectionsAsync();
        var snapshot = Assert.Single(snapshots, connection => connection.Id == connectionId);
        Assert.Equal("Connected", snapshot.Status);
        Assert.Contains("Acme QuickBooks", snapshot.SettingsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("access-token-one", snapshot.SettingsJson, StringComparison.Ordinal);

        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            var rawCredentials = await ReadScalarAsync(connection, "SELECT CredentialsJson FROM IntegrationConnections WHERE Name = 'Primary books';");
            Assert.StartsWith("enc::", rawCredentials);
            Assert.DoesNotContain("access-token-one", rawCredentials, StringComparison.Ordinal);
            Assert.DoesNotContain("refresh-token-one", rawCredentials, StringComparison.Ordinal);
        }

        var refresh = await service.RefreshConnectionAsync(connectionId);
        Assert.True(refresh.Succeeded, refresh.ErrorMessage);
        Assert.Equal("refresh-token-one", provider.LastRefreshToken);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var stored = await db.IntegrationConnections.SingleAsync(connection => connection.Id == connectionId);
            Assert.Contains("access-token-two", stored.CredentialsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("access-token-one", stored.CredentialsJson, StringComparison.Ordinal);
            Assert.Equal(4, stored.CredentialVersion);
            Assert.Equal(string.Empty, stored.CredentialOperationLeaseId);
            Assert.Null(stored.CredentialOperationLeaseExpiresAtUtc);
        }

        provider.RevocationSucceeds = false;
        var failedDisconnect = await service.DisconnectAsync(connectionId);
        Assert.False(failedDisconnect.Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var retained = await db.IntegrationConnections.SingleAsync(connection => connection.Id == connectionId);
            Assert.Equal("DisconnectPending", retained.Status);
            Assert.Contains("refresh-token-two", retained.CredentialsJson, StringComparison.Ordinal);
            Assert.Equal(string.Empty, retained.CredentialOperationLeaseId);
            Assert.Null(retained.CredentialOperationLeaseExpiresAtUtc);
        }

        provider.RevocationSucceeds = true;
        var disconnected = await service.DisconnectAsync(connectionId);
        Assert.True(disconnected.Succeeded, disconnected.ErrorMessage);
        Assert.Equal("refresh-token-two", provider.LastRevokedToken);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var removed = await db.IntegrationConnections.SingleAsync(connection => connection.Id == connectionId);
            Assert.Equal("Disconnected", removed.Status);
            Assert.Equal("{}", removed.CredentialsJson);
            Assert.Equal(string.Empty, removed.CredentialOperationLeaseId);
            Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.connected" && audit.EntityId == connectionId);
            Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.disconnect_failed" && audit.EntityId == connectionId && !audit.DetailJson.Contains("refresh-token", StringComparison.Ordinal));
            Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.disconnected" && audit.EntityId == connectionId);
        }
    }

    [Fact]
    public async Task CredentialOperationLease_BlocksConcurrentProviderCallsRecoversExpiryAndReleasesAfterFailure()
    {
        var provider = new FakeQuickBooksOnlineClient();
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IQuickBooksOnlineConnectionService>();
        var connectionId = await ConnectAsync(scope.ServiceProvider);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var reconnectStart = await service.BeginAuthorizationAsync(new(connectionId, "ignored", "Sandbox"));
        var reconnectState = QueryHelpers.ParseQuery(new Uri(reconnectStart.AuthorizationUrl!).Query)["state"].ToString();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var connection = await db.IntegrationConnections.SingleAsync(candidate => candidate.Id == connectionId);
            connection.CredentialOperationLeaseId = "other-instance-lease";
            connection.CredentialOperation = "Refresh";
            connection.CredentialOperationLeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);
            connection.CredentialVersion++;
            await db.SaveChangesAsync();
        }

        var blockedReconnect = await service.CompleteAuthorizationAsync(new(reconnectState, "blocked-code", "123456789", null, null));
        Assert.False(blockedReconnect.Succeeded);
        Assert.Equal(1, provider.ExchangeCodeCount);

        var contended = await service.RefreshConnectionAsync(connectionId);
        Assert.False(contended.Succeeded);
        Assert.Contains("another application instance", contended.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.RefreshCount);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var connection = await db.IntegrationConnections.SingleAsync(candidate => candidate.Id == connectionId);
            connection.CredentialOperationLeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        provider.RefreshSucceeds = false;
        var providerFailure = await service.RefreshConnectionAsync(connectionId);
        Assert.False(providerFailure.Succeeded);
        Assert.Equal(1, provider.RefreshCount);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var connection = await db.IntegrationConnections.SingleAsync(candidate => candidate.Id == connectionId);
            Assert.Equal("Connected", connection.Status);
            Assert.Equal(string.Empty, connection.CredentialOperationLeaseId);
            Assert.Null(connection.CredentialOperationLeaseExpiresAtUtc);
            var audits = await db.BusinessAuditEntries.Where(entry => entry.EntityId == connectionId).ToArrayAsync();
            Assert.Contains(audits, audit => audit.Action == "integration.credential_operation_lease_recovered");
            Assert.Contains(audits, audit => audit.Action == "integration.token_refresh_failed");
        }

        provider.RefreshSucceeds = true;
        var retry = await service.RefreshConnectionAsync(connectionId);
        Assert.True(retry.Succeeded, retry.ErrorMessage);
        Assert.Equal(2, provider.RefreshCount);
    }

    [Fact]
    public async Task OAuthAttempt_RejectsExpiredStateProviderDenialAndUnauthorizedCallerWithoutSavingCredentials()
    {
        var provider = new FakeQuickBooksOnlineClient();
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IQuickBooksOnlineConnectionService>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, actor.UserId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, actor.CompanyId.ToString())
            ], "test"))
        };
        Assert.False((await service.BeginAuthorizationAsync(new(null, "Unauthorized", "Sandbox"))).Succeeded);

        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var deniedStart = await service.BeginAuthorizationAsync(new(null, "Denied", "Sandbox"));
        var deniedState = QueryHelpers.ParseQuery(new Uri(deniedStart.AuthorizationUrl!).Query)["state"].ToString();
        var denied = await service.CompleteAuthorizationAsync(new(deniedState, null, null, "access_denied", "operator declined"));
        Assert.False(denied.Succeeded);

        var expiredStart = await service.BeginAuthorizationAsync(new(null, "Expired", "Sandbox"));
        var expiredState = QueryHelpers.ParseQuery(new Uri(expiredStart.AuthorizationUrl!).Query)["state"].ToString();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var expiredAttempt = await db.OAuthAuthorizationAttempts.SingleAsync(attempt => attempt.ConnectionName == "Expired");
            expiredAttempt.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var expired = await service.CompleteAuthorizationAsync(new(expiredState, "code", "1234", null, null));
        Assert.False(expired.Succeeded);
        Assert.Equal(0, provider.ExchangeCodeCount);

        await using var verified = await factory.CreateDbContextAsync();
        var pendingConnections = await verified.IntegrationConnections.Where(connection => connection.ProviderCode == "quickbooks-online").ToArrayAsync();
        Assert.Equal(2, pendingConnections.Length);
        Assert.All(pendingConnections, connection =>
        {
            Assert.Equal("AuthorizationPending", connection.Status);
            Assert.Equal("{}", connection.CredentialsJson);
        });
        Assert.Contains(await verified.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.oauth_denied");
    }

    [Fact]
    public async Task OAuthAttempt_LatestStartSupersedesEarlierCallbackForTheSameConnectionName()
    {
        var provider = new FakeQuickBooksOnlineClient();
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IQuickBooksOnlineConnectionService>();

        var first = await service.BeginAuthorizationAsync(new(null, "Superseded books", "Sandbox"));
        var duplicate = await service.BeginAuthorizationAsync(new(null, "Superseded books", "Sandbox"));
        Assert.False(duplicate.Succeeded);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var connectionId = await db.IntegrationConnections.Where(connection => connection.Name == "Superseded books").Select(connection => connection.Id).SingleAsync();
        var second = await service.BeginAuthorizationAsync(new(connectionId, "Superseded books", "Sandbox"));
        var firstState = QueryHelpers.ParseQuery(new Uri(first.AuthorizationUrl!).Query)["state"].ToString();
        var secondState = QueryHelpers.ParseQuery(new Uri(second.AuthorizationUrl!).Query)["state"].ToString();

        var staleCallback = await service.CompleteAuthorizationAsync(new(firstState, "stale-code", "123456789", null, null));
        var currentCallback = await service.CompleteAuthorizationAsync(new(secondState, "current-code", "123456789", null, null));

        Assert.False(staleCallback.Succeeded);
        Assert.True(currentCallback.Succeeded, currentCallback.ErrorMessage);
        Assert.Equal(1, provider.ExchangeCodeCount);
    }

    [Fact]
    public async Task OAuthReconnect_UsesDistributedLeaseAndAtomicallyReplacesProtectedGrant()
    {
        var provider = new FakeQuickBooksOnlineClient();
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IQuickBooksOnlineConnectionService>();
        var connectionId = await ConnectAsync(scope.ServiceProvider);

        var reconnect = await service.BeginAuthorizationAsync(new(connectionId, "ignored", "Sandbox"));
        var state = QueryHelpers.ParseQuery(new Uri(reconnect.AuthorizationUrl!).Query)["state"].ToString();
        var completion = await service.CompleteAuthorizationAsync(new(state, "reconnect-code", "123456789", null, null));

        Assert.True(completion.Succeeded, completion.ErrorMessage);
        Assert.Equal(connectionId, completion.ConnectionId);
        Assert.Equal(2, provider.ExchangeCodeCount);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var connection = await db.IntegrationConnections.SingleAsync(candidate => candidate.Id == connectionId);
        Assert.Contains("access-token-reconnected", connection.CredentialsJson, StringComparison.Ordinal);
        Assert.Contains("refresh-token-reconnected", connection.CredentialsJson, StringComparison.Ordinal);
        Assert.Equal(string.Empty, connection.CredentialOperationLeaseId);
        Assert.Null(connection.CredentialOperationLeaseExpiresAtUtc);
        Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.EntityId == connectionId && audit.Action == "integration.credential_operation_lease_acquired" && audit.DetailJson.Contains("Reconnect", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OAuthCallback_RevokesIssuedGrantWhenCompanyValidationFails()
    {
        var provider = new FakeQuickBooksOnlineClient { CompanyValidationSucceeds = false };
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IQuickBooksOnlineConnectionService>();

        var start = await service.BeginAuthorizationAsync(new(null, "Invalid company", "Sandbox"));
        var state = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl!).Query)["state"].ToString();
        var completion = await service.CompleteAuthorizationAsync(new(state, "issued-code", "123456789", null, null));

        Assert.False(completion.Succeeded);
        Assert.Equal("refresh-token-one", provider.LastRevokedToken);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var pending = await db.IntegrationConnections.SingleAsync(connection => connection.ProviderCode == "quickbooks-online");
        Assert.Equal("AuthorizationPending", pending.Status);
        Assert.Equal("{}", pending.CredentialsJson);
        Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.oauth_unused_grant_revoked");
    }

    [Fact]
    public async Task GenericProfileEndpoint_RefusesQuickBooksCredentialJson()
    {
        var provider = new FakeQuickBooksOnlineClient();
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);

        var result = await scope.ServiceProvider.GetRequiredService<IIntegrationService>().SaveConnectionAsync(
            new(null, "quickbooks-online", "Unsafe", "{}", "{\"accessToken\":\"do-not-store-this-way\"}", true));

        Assert.False(result.Succeeded);
        Assert.Contains("protected QuickBooks connect", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configuration_RejectsInsecureProductionRedirectUri()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["QuickBooksOnline:Enabled"] = "true",
            ["QuickBooksOnline:Environment"] = "Production",
            ["QuickBooksOnline:ClientId"] = "test-client",
            ["QuickBooksOnline:ClientSecret"] = "test-client-secret",
            ["QuickBooksOnline:RedirectUri"] = "http://ledger.example.test/integrations/quickbooks-online/callback"
        }).Build();
        var collection = new ServiceCollection();
        collection.AddBrassLedgerInfrastructure(configuration, Path.Combine(_contentRootPath, "invalid-production-options"), seedSampleData: false);
        using var services = collection.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            services.GetRequiredService<IOptions<QuickBooksOnlineOptions>>().Value);

        Assert.Contains("absolute HTTPS URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderClient_UsesIntuitOAuthContractsWithoutLeakingClientSecretIntoUrls()
    {
        var requestNumber = 0;
        var handler = new RecordingHandler(async request =>
        {
            requestNumber++;
            if (requestNumber == 1)
            {
                Assert.Equal("https://oauth.test/token", request.RequestUri!.ToString());
                Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
                Assert.Equal("client-id:client-secret", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
                var form = QueryHelpers.ParseQuery("?" + await request.Content!.ReadAsStringAsync());
                Assert.Equal("authorization_code", form["grant_type"]);
                Assert.Equal("authorization-code", form["code"]);
                Assert.Equal("https://ledger.test/integrations/quickbooks-online/callback", form["redirect_uri"]);
                return JsonResponse("{\"access_token\":\"access-one\",\"refresh_token\":\"refresh-one\",\"token_type\":\"bearer\",\"scope\":\"com.intuit.quickbooks.accounting\",\"expires_in\":3600,\"x_refresh_token_expires_in\":8726400}");
            }
            if (requestNumber == 2)
            {
                Assert.Equal("https://oauth.test/token", request.RequestUri!.ToString());
                var form = QueryHelpers.ParseQuery("?" + await request.Content!.ReadAsStringAsync());
                Assert.Equal("refresh_token", form["grant_type"]);
                Assert.Equal("refresh-one", form["refresh_token"]);
                return JsonResponse("{\"access_token\":\"access-two\",\"refresh_token\":\"refresh-two\",\"token_type\":\"bearer\",\"expires_in\":3600,\"x_refresh_token_expires_in\":8726400}");
            }
            if (requestNumber == 3)
            {
                Assert.Equal("https://sandbox-api.test/v3/company/12345/companyinfo/12345", request.RequestUri!.ToString());
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("access-two", request.Headers.Authorization?.Parameter);
                return JsonResponse("{\"CompanyInfo\":{\"CompanyName\":\"Provider Company\",\"LegalName\":\"Provider Company LLC\",\"Country\":\"US\"}}");
            }
            if (requestNumber == 4)
            {
                Assert.Equal("https://sandbox-api.test/v3/company/12345/query", request.RequestUri!.GetLeftPart(UriPartial.Path));
                var queryText = QueryHelpers.ParseQuery(request.RequestUri.Query)["query"].ToString();
                Assert.Equal("select * from Account startposition 1 maxresults 1000", queryText);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                return JsonResponse("{\"QueryResponse\":{\"Account\":[{\"Id\":\"41\",\"SyncToken\":\"2\",\"Active\":true,\"Name\":\"Travel\",\"AcctNum\":\"7999\",\"AccountType\":\"Expense\",\"AccountSubType\":\"Travel\"}]}}");
            }
            Assert.Equal(5, requestNumber);
            Assert.Equal("https://oauth.test/revoke", request.RequestUri!.ToString());
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Contains("refresh-two", await request.Content!.ReadAsStringAsync(), StringComparison.Ordinal);
            return JsonResponse("{}");
        });
        var options = Options.Create(new QuickBooksOnlineOptions
        {
            Enabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://ledger.test/integrations/quickbooks-online/callback",
            AuthorizationEndpoint = "https://authorize.test/connect",
            TokenEndpoint = "https://oauth.test/token",
            RevocationEndpoint = "https://oauth.test/revoke",
            SandboxApiBaseUrl = "https://sandbox-api.test",
            ProductionApiBaseUrl = "https://api.test"
        });
        using var httpClient = new HttpClient(handler);
        var providerClient = new QuickBooksOnlineClient(new StaticHttpClientFactory(httpClient), options);

        var authorizationUrl = providerClient.BuildAuthorizationUrl("one-use-state");
        Assert.DoesNotContain("client-secret", authorizationUrl, StringComparison.Ordinal);
        Assert.Equal("one-use-state", QueryHelpers.ParseQuery(new Uri(authorizationUrl).Query)["state"]);
        var exchanged = await providerClient.ExchangeAuthorizationCodeAsync("authorization-code");
        Assert.True(exchanged.Succeeded);
        Assert.Equal("refresh-one", exchanged.RefreshToken);
        var refreshed = await providerClient.RefreshTokenAsync(exchanged.RefreshToken);
        Assert.True(refreshed.Succeeded);
        Assert.Equal("refresh-two", refreshed.RefreshToken);
        var company = await providerClient.GetCompanyInfoAsync("Sandbox", "12345", refreshed.AccessToken);
        Assert.True(company.Succeeded);
        Assert.Equal("Provider Company", company.CompanyName);
        var accounts = await providerClient.QueryEntitiesAsync("Sandbox", "12345", refreshed.AccessToken, "accounts");
        Assert.True(accounts.Succeeded);
        var account = Assert.Single(accounts.Entities);
        Assert.Equal("41", account.Id);
        Assert.Equal("7999", account.Number);
        var revoked = await providerClient.RevokeTokenAsync(refreshed.RefreshToken);
        Assert.True(revoked.Succeeded);
        Assert.Equal(5, requestNumber);
    }

    [Fact]
    public async Task ApiSync_PreviewsCommitsIdempotentlyUpdatesOnlyUnchangedLocalRecordsAndRetainsConflicts()
    {
        var provider = new FakeQuickBooksOnlineClient
        {
            Entities =
            {
                ["accounts"] =
                [
                    new("A-1", "0", true, "Travel expense", "7999", string.Empty, "Expense", "Travel"),
                    new("A-2", "0", true, "QuickBooks receivables", "QAR", string.Empty, "Accounts Receivable", "AccountsReceivable"),
                    new("A-3", "0", true, "Unsupported", "7998", string.Empty, "Cryptocurrency Reserve", "Unknown")
                ],
                ["customers"] = [new("C-1", "0", true, "API Customer", string.Empty, "customer@example.test", string.Empty, string.Empty)],
                ["vendors"] = [new("V-1", "0", true, "API Vendor", string.Empty, "vendor@example.test", string.Empty, string.Empty)]
            }
        };
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var connectionId = await ConnectAsync(scope.ServiceProvider);
        var sync = scope.ServiceProvider.GetRequiredService<IQuickBooksOnlineSyncService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();

        var unpreviewedCommit = await sync.ImportAsync(new(connectionId, "accounts", false));
        Assert.False(unpreviewedCommit.Succeeded);
        Assert.Contains(unpreviewedCommit.Issues, issue => issue.Code == "preview_required");

        var preview = await sync.ImportAsync(new(connectionId, "accounts", true));

        Assert.True(preview.Succeeded, preview.ErrorMessage);
        Assert.True(preview.DryRun);
        Assert.Equal(3, preview.FetchedCount);
        Assert.Equal(1, preview.CreatedCount);
        Assert.Equal(1, preview.ConflictCount);
        Assert.Equal(1, preview.RejectedCount);
        Assert.Contains(preview.Issues, issue => issue.ProviderEntityId == "A-2" && issue.Code == "control_account_mapping_required");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            Assert.False(await db.Accounts.AnyAsync(account => account.CompanyId == actor.CompanyId && account.Number == "7999"));
            Assert.Empty(await db.ExternalEntityLinks.Where(link => link.IntegrationConnectionId == connectionId).ToArrayAsync());
        }

        var committed = await sync.ImportAsync(new(connectionId, "accounts", false, preview.SnapshotSha256));
        Assert.True(committed.Succeeded, committed.ErrorMessage);
        Assert.False(committed.DryRun);
        Assert.Equal(1, committed.CreatedCount);
        Guid localAccountId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var account = await db.Accounts.SingleAsync(candidate => candidate.CompanyId == actor.CompanyId && candidate.Number == "7999");
            localAccountId = account.Id;
            var link = await db.ExternalEntityLinks.SingleAsync(candidate => candidate.IntegrationConnectionId == connectionId && candidate.ProviderEntityId == "A-1");
            Assert.Equal(account.Id, link.LocalEntityId);
        }

        var repeatPreview = await sync.ImportAsync(new(connectionId, "accounts", true));
        var repeated = await sync.ImportAsync(new(connectionId, "accounts", false, repeatPreview.SnapshotSha256));
        Assert.True(repeated.Succeeded, repeated.ErrorMessage);
        Assert.Equal(0, repeated.CreatedCount);
        Assert.Equal(1, repeated.UnchangedCount);
        await using (var db = await dbFactory.CreateDbContextAsync())
            Assert.Equal(1, await db.Accounts.CountAsync(candidate => candidate.CompanyId == actor.CompanyId && candidate.Number == "7999"));

        provider.Entities["accounts"][0] = provider.Entities["accounts"][0] with { SyncToken = "1" };
        var syncTokenOnlyPreview = await sync.ImportAsync(new(connectionId, "accounts", true));
        var syncTokenOnlyCommit = await sync.ImportAsync(new(connectionId, "accounts", false, syncTokenOnlyPreview.SnapshotSha256));
        Assert.True(syncTokenOnlyCommit.Succeeded, syncTokenOnlyCommit.ErrorMessage);
        Assert.Equal(0, syncTokenOnlyCommit.UpdatedCount);
        Assert.Equal(1, syncTokenOnlyCommit.UnchangedCount);

        provider.Entities["accounts"][0] = provider.Entities["accounts"][0] with { Name = "Travel and lodging", SyncToken = "2" };
        var updatePreview = await sync.ImportAsync(new(connectionId, "accounts", true));
        var updated = await sync.ImportAsync(new(connectionId, "accounts", false, updatePreview.SnapshotSha256));
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        Assert.Equal(1, updated.UpdatedCount);
        await using (var db = await dbFactory.CreateDbContextAsync())
            Assert.Equal("Travel and lodging", (await db.Accounts.SingleAsync(account => account.Id == localAccountId)).Name);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var locallyChanged = await db.Accounts.SingleAsync(account => account.Id == localAccountId);
            locallyChanged.Name = "Local approved description";
            await db.SaveChangesAsync();
        }
        provider.Entities["accounts"][0] = provider.Entities["accounts"][0] with { Name = "Remote changed too", SyncToken = "3" };
        var conflictPreview = await sync.ImportAsync(new(connectionId, "accounts", true));
        var conflict = await sync.ImportAsync(new(connectionId, "accounts", false, conflictPreview.SnapshotSha256));
        Assert.True(conflict.Succeeded, conflict.ErrorMessage);
        Assert.Contains(conflict.Issues, issue => issue.ProviderEntityId == "A-1" && issue.Code == "both_changed");
        await using (var db = await dbFactory.CreateDbContextAsync())
            Assert.Equal("Local approved description", (await db.Accounts.SingleAsync(account => account.Id == localAccountId)).Name);

        var customerPreview = await sync.ImportAsync(new(connectionId, "customers", true));
        provider.Entities["customers"][0] = provider.Entities["customers"][0] with { Name = "API Customer Updated", SyncToken = "1" };
        var staleCustomerCommit = await sync.ImportAsync(new(connectionId, "customers", false, customerPreview.SnapshotSha256));
        Assert.False(staleCustomerCommit.Succeeded);
        Assert.Contains(staleCustomerCommit.Issues, issue => issue.Code == "source_changed_after_preview");
        var currentCustomerPreview = await sync.ImportAsync(new(connectionId, "customers", true));
        Assert.True((await sync.ImportAsync(new(connectionId, "customers", false, currentCustomerPreview.SnapshotSha256))).Succeeded);
        var vendorPreview = await sync.ImportAsync(new(connectionId, "vendors", true));
        Assert.True((await sync.ImportAsync(new(connectionId, "vendors", false, vendorPreview.SnapshotSha256))).Succeeded);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            Assert.Contains(await db.Customers.ToArrayAsync(), customer => customer.CompanyId == actor.CompanyId && customer.CustomerNumber == "QBO-C-C-1" && customer.Name == "API Customer Updated");
            Assert.Contains(await db.Vendors.ToArrayAsync(), vendor => vendor.CompanyId == actor.CompanyId && vendor.VendorNumber == "QBO-V-V-1" && vendor.Name == "API Vendor");
            Assert.True(await db.IntegrationSyncRuns.CountAsync(run => run.IntegrationConnectionId == connectionId) >= 14);
            Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.quickbooks.sync_previewed");
            Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.quickbooks.sync_committed");
        }
        var history = await sync.GetRecentRunsAsync(connectionId);
        Assert.NotEmpty(history);
        Assert.All(history, run => Assert.Equal(connectionId, run.ConnectionId));

        SetContext(scope.ServiceProvider, actor.UserId, Guid.NewGuid());
        Assert.False((await sync.ImportAsync(new(connectionId, "accounts", true))).Succeeded);
    }

    [Fact]
    public async Task ExplicitMappings_AreSnapshotBoundClassificationSafeReplaceConfirmedAuditedAndNonDestructive()
    {
        var provider = new FakeQuickBooksOnlineClient
        {
            Entities =
            {
                ["accounts"] = [new("AR-REMOTE", "4", true, "QuickBooks receivables", "QBO-AR", string.Empty, "Accounts Receivable", "AccountsReceivable")],
                ["customers"] =
                [
                    new("C-ONE", "2", true, "Remote customer one", string.Empty, "one@example.test", string.Empty, string.Empty),
                    new("C-TWO", "1", true, "Remote customer two", string.Empty, "two@example.test", string.Empty, string.Empty)
                ]
            }
        };
        using var services = CreateServiceProvider(provider);
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var actor = await SetOwnerContextAsync(scope.ServiceProvider);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var connectionId = await ConnectAsync(scope.ServiceProvider);
        var sync = scope.ServiceProvider.GetRequiredService<IQuickBooksOnlineSyncService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();

        Guid receivablesControlId;
        Guid inventoryControlId;
        Guid incompatibleExpenseId;
        Guid customerOneId;
        Guid customerTwoId;
        Guid otherCompanyCustomerId;
        string originalControlName;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var receivablesControl = await db.Accounts.SingleAsync(account => account.CompanyId == actor.CompanyId && account.Number == "1100" && account.IsControlAccount);
            var inventoryControl = await db.Accounts.SingleAsync(account => account.CompanyId == actor.CompanyId && account.Number == "1200" && account.IsControlAccount);
            var incompatibleExpense = await db.Accounts.FirstAsync(account => account.CompanyId == actor.CompanyId && account.Type == AccountType.Expense && !account.IsControlAccount);
            receivablesControlId = receivablesControl.Id;
            inventoryControlId = inventoryControl.Id;
            incompatibleExpenseId = incompatibleExpense.Id;
            originalControlName = receivablesControl.Name;
            var first = new Customer { Id = Guid.NewGuid(), CompanyId = actor.CompanyId, CustomerNumber = "MAP-C-1", Name = "Local customer one", Email = "local-one@example.test" };
            var second = new Customer { Id = Guid.NewGuid(), CompanyId = actor.CompanyId, CustomerNumber = "MAP-C-2", Name = "Local customer two", Email = "local-two@example.test" };
            customerOneId = first.Id;
            customerTwoId = second.Id;
            var otherCompany = new Company { Id = Guid.NewGuid(), Name = $"Mapping isolation {Guid.NewGuid():N}", LegalName = "Mapping isolation", BaseCurrency = "USD", FiscalYearStartMonth = 1 };
            var otherCustomer = new Customer { Id = Guid.NewGuid(), CompanyId = otherCompany.Id, CustomerNumber = "OTHER-COMPANY", Name = "Other company customer", Email = "other@example.test" };
            otherCompanyCustomerId = otherCustomer.Id;
            db.Companies.Add(otherCompany);
            db.Customers.AddRange(first, second, otherCustomer);
            await db.SaveChangesAsync();
        }

        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, actor.UserId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, actor.CompanyId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage)
            ], "test"))
        };
        Assert.False((await sync.PreviewMappingsAsync(connectionId, "accounts")).Succeeded);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);

        var accountWorkspace = await sync.PreviewMappingsAsync(connectionId, "accounts");
        Assert.True(accountWorkspace.Succeeded, accountWorkspace.ErrorMessage);
        Assert.NotNull(accountWorkspace.PreviewRunId);
        var remoteAccount = Assert.Single(accountWorkspace.RemoteCandidates);
        Assert.True(remoteAccount.Eligible);
        Assert.Equal("Asset", remoteAccount.Classification);
        var badClassification = await sync.SaveMappingAsync(new(
            connectionId, "accounts", accountWorkspace.PreviewRunId!.Value, accountWorkspace.SnapshotSha256,
            remoteAccount.ProviderEntityId, incompatibleExpenseId));
        Assert.False(badClassification.Succeeded);
        Assert.Contains("classifications do not match", badClassification.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var wrongControlPurpose = await sync.SaveMappingAsync(new(
            connectionId, "accounts", accountWorkspace.PreviewRunId.Value, accountWorkspace.SnapshotSha256,
            remoteAccount.ProviderEntityId, inventoryControlId));
        Assert.False(wrongControlPurpose.Succeeded);
        Assert.Contains("corresponding control-account", wrongControlPurpose.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var controlTarget = Assert.Single(accountWorkspace.LocalCandidates, candidate => candidate.LocalEntityId == receivablesControlId);
        Assert.Equal("AccountsReceivable", controlTarget.ControlAccountPurpose);
        var accountMapped = await sync.SaveMappingAsync(new(
            connectionId, "accounts", accountWorkspace.PreviewRunId.Value, accountWorkspace.SnapshotSha256,
            remoteAccount.ProviderEntityId, controlTarget.LocalEntityId, null, controlTarget.MappedProviderEntityId ?? string.Empty));
        Assert.True(accountMapped.Succeeded, accountMapped.ErrorMessage);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var unchangedControl = await db.Accounts.SingleAsync(account => account.Id == receivablesControlId);
            Assert.Equal(originalControlName, unchangedControl.Name);
            Assert.Equal("AR-REMOTE", (await db.ExternalEntityLinks.SingleAsync(link => link.Id == accountMapped.Id)).ProviderEntityId);
        }
        var importPreview = await sync.ImportAsync(new(connectionId, "accounts", true));
        Assert.True(importPreview.Succeeded, importPreview.ErrorMessage);
        Assert.Equal(1, importPreview.UnchangedCount);
        Assert.DoesNotContain(importPreview.Issues, issue => issue.Code == "control_account_mapping_required");

        var customerWorkspace = await sync.PreviewMappingsAsync(connectionId, "customers");
        Assert.DoesNotContain(customerWorkspace.LocalCandidates, candidate => candidate.LocalEntityId == otherCompanyCustomerId);
        var remoteOne = Assert.Single(customerWorkspace.RemoteCandidates, candidate => candidate.ProviderEntityId == "C-ONE");
        var customerOneTarget = Assert.Single(customerWorkspace.LocalCandidates, candidate => candidate.LocalEntityId == customerOneId);
        var crossCompanyMapping = await sync.SaveMappingAsync(new(
            connectionId, "customers", customerWorkspace.PreviewRunId!.Value, customerWorkspace.SnapshotSha256,
            remoteOne.ProviderEntityId, otherCompanyCustomerId));
        Assert.False(crossCompanyMapping.Succeeded);
        Assert.Contains("active company", crossCompanyMapping.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        provider.Entities["customers"][0] = provider.Entities["customers"][0] with { Name = "Changed after mapping preview", SyncToken = "3" };
        var staleSnapshot = await sync.SaveMappingAsync(new(
            connectionId, "customers", customerWorkspace.PreviewRunId.Value, customerWorkspace.SnapshotSha256,
            remoteOne.ProviderEntityId, customerOneId, null, customerOneTarget.MappedProviderEntityId ?? string.Empty));
        Assert.False(staleSnapshot.Succeeded);
        Assert.Contains("changed after", staleSnapshot.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        provider.Entities["customers"][0] = provider.Entities["customers"][0] with { Name = "Remote customer one", SyncToken = "2" };
        customerWorkspace = await sync.PreviewMappingsAsync(connectionId, "customers");
        remoteOne = Assert.Single(customerWorkspace.RemoteCandidates, candidate => candidate.ProviderEntityId == "C-ONE");
        customerOneTarget = Assert.Single(customerWorkspace.LocalCandidates, candidate => candidate.LocalEntityId == customerOneId);
        var created = await sync.SaveMappingAsync(new(
            connectionId, "customers", customerWorkspace.PreviewRunId!.Value, customerWorkspace.SnapshotSha256,
            remoteOne.ProviderEntityId, customerOneId, null, customerOneTarget.MappedProviderEntityId ?? string.Empty));
        Assert.True(created.Succeeded, created.ErrorMessage);

        customerWorkspace = await sync.PreviewMappingsAsync(connectionId, "customers");
        remoteOne = Assert.Single(customerWorkspace.RemoteCandidates, candidate => candidate.ProviderEntityId == "C-ONE");
        var customerTwoTarget = Assert.Single(customerWorkspace.LocalCandidates, candidate => candidate.LocalEntityId == customerTwoId);
        var unconfirmedReplace = await sync.SaveMappingAsync(new(
            connectionId, "customers", customerWorkspace.PreviewRunId!.Value, customerWorkspace.SnapshotSha256,
            remoteOne.ProviderEntityId, customerTwoId, remoteOne.MappedLocalEntityId, customerTwoTarget.MappedProviderEntityId ?? string.Empty));
        Assert.False(unconfirmedReplace.Succeeded);
        Assert.Contains("confirmation", unconfirmedReplace.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var replaced = await sync.SaveMappingAsync(new(
            connectionId, "customers", customerWorkspace.PreviewRunId.Value, customerWorkspace.SnapshotSha256,
            remoteOne.ProviderEntityId, customerTwoId, remoteOne.MappedLocalEntityId, customerTwoTarget.MappedProviderEntityId ?? string.Empty, true));
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);

        customerWorkspace = await sync.PreviewMappingsAsync(connectionId, "customers");
        var remoteTwo = Assert.Single(customerWorkspace.RemoteCandidates, candidate => candidate.ProviderEntityId == "C-TWO");
        customerOneTarget = Assert.Single(customerWorkspace.LocalCandidates, candidate => candidate.LocalEntityId == customerOneId);
        Assert.True((await sync.SaveMappingAsync(new(
            connectionId, "customers", customerWorkspace.PreviewRunId!.Value, customerWorkspace.SnapshotSha256,
            remoteTwo.ProviderEntityId, customerOneId, remoteTwo.MappedLocalEntityId, customerOneTarget.MappedProviderEntityId ?? string.Empty))).Succeeded);

        customerWorkspace = await sync.PreviewMappingsAsync(connectionId, "customers");
        remoteOne = Assert.Single(customerWorkspace.RemoteCandidates, candidate => candidate.ProviderEntityId == "C-ONE");
        customerOneTarget = Assert.Single(customerWorkspace.LocalCandidates, candidate => candidate.LocalEntityId == customerOneId);
        var occupiedTarget = await sync.SaveMappingAsync(new(
            connectionId, "customers", customerWorkspace.PreviewRunId!.Value, customerWorkspace.SnapshotSha256,
            remoteOne.ProviderEntityId, customerOneId, remoteOne.MappedLocalEntityId, customerOneTarget.MappedProviderEntityId ?? string.Empty, true));
        Assert.False(occupiedTarget.Succeeded);
        Assert.Contains("already mapped", occupiedTarget.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var noConfirmation = await sync.RemoveMappingAsync(new(connectionId, "customers", "C-ONE", customerTwoId));
        Assert.False(noConfirmation.Succeeded);
        var removed = await sync.RemoveMappingAsync(new(connectionId, "customers", "C-ONE", customerTwoId, true));
        Assert.True(removed.Succeeded, removed.ErrorMessage);
        var staleRemoval = await sync.RemoveMappingAsync(new(connectionId, "customers", "C-ONE", customerTwoId, true));
        Assert.False(staleRemoval.Succeeded);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            Assert.False(await db.ExternalEntityLinks.AnyAsync(link => link.IntegrationConnectionId == connectionId && link.ProviderEntityId == "C-ONE"));
            var customers = await db.Customers.Where(customer => customer.Id == customerOneId || customer.Id == customerTwoId).OrderBy(customer => customer.CustomerNumber).ToArrayAsync();
            Assert.Equal(["Local customer one", "Local customer two"], customers.Select(customer => customer.Name));
            var audits = await db.BusinessAuditEntries.Where(entry => entry.CompanyId == actor.CompanyId).Select(entry => entry.Action).ToArrayAsync();
            Assert.Contains("integration.quickbooks.mapping_previewed", audits);
            Assert.Contains("integration.quickbooks.mapping_created", audits);
            Assert.Contains("integration.quickbooks.mapping_replaced", audits);
            Assert.Contains("integration.quickbooks.mapping_removed", audits);
        }
    }

    private ServiceProvider CreateServiceProvider(FakeQuickBooksOnlineClient provider)
    {
        Directory.CreateDirectory(_contentRootPath);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["QuickBooksOnline:Enabled"] = "true",
            ["QuickBooksOnline:Environment"] = "Sandbox",
            ["QuickBooksOnline:ClientId"] = "test-client",
            ["QuickBooksOnline:ClientSecret"] = "test-client-secret",
            ["QuickBooksOnline:RedirectUri"] = "http://127.0.0.1:5099/integrations/quickbooks-online/callback"
        }).Build();
        var collection = new ServiceCollection();
        collection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        collection.RemoveAll<IQuickBooksOnlineClient>();
        collection.AddSingleton<IQuickBooksOnlineClient>(provider);
        return collection.BuildServiceProvider();
    }

    private static async Task<(Guid UserId, Guid CompanyId)> SetOwnerContextAsync(IServiceProvider services)
    {
        var authentication = services.GetRequiredService<IUserAuthenticationService>();
        var signedIn = await authentication.AuthenticateAsync("controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "quickbooks-test");
        Assert.Equal(AuthenticationOutcome.Succeeded, signedIn.Outcome);
        Assert.NotNull(signedIn.User);
        return (signedIn.User.UserId, signedIn.User.CompanyId);
    }

    private static async Task<Guid> ConnectAsync(IServiceProvider services)
    {
        var connectionService = services.GetRequiredService<IQuickBooksOnlineConnectionService>();
        var start = await connectionService.BeginAuthorizationAsync(new(null, "Sync books", "Sandbox"));
        Assert.True(start.Succeeded, start.ErrorMessage);
        var state = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl!).Query)["state"].ToString();
        var completion = await connectionService.CompleteAuthorizationAsync(new(state, "sync-code", "123456789", null, null));
        Assert.True(completion.Succeeded, completion.ErrorMessage);
        return completion.ConnectionId!.Value;
    }

    private static void SetContext(IServiceProvider services, Guid userId, Guid companyId)
    {
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage),
                new Claim(ClaimTypes.Role, "Owner/CEO")
            ], "test"))
        };
    }

    private static async Task<string> ReadScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty;
    }

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    public void Dispose()
    {
        if (!Directory.Exists(_contentRootPath)) return;
        try { Directory.Delete(_contentRootPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class FakeQuickBooksOnlineClient : IQuickBooksOnlineClient
    {
        public int ExchangeCodeCount { get; private set; }
        public int RefreshCount { get; private set; }
        public string LastRefreshToken { get; private set; } = string.Empty;
        public string LastRevokedToken { get; private set; } = string.Empty;
        public bool RefreshSucceeds { get; set; } = true;
        public bool RevocationSucceeds { get; set; } = true;
        public bool CompanyValidationSucceeds { get; set; } = true;
        public Dictionary<string, List<QuickBooksRemoteEntity>> Entities { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string BuildAuthorizationUrl(string state) => QueryHelpers.AddQueryString("https://appcenter.intuit.com/connect/oauth2", new Dictionary<string, string?>
        {
            ["client_id"] = "test-client",
            ["response_type"] = "code",
            ["scope"] = "com.intuit.quickbooks.accounting",
            ["redirect_uri"] = "http://127.0.0.1:5099/integrations/quickbooks-online/callback",
            ["state"] = state
        });

        public Task<QuickBooksTokenResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            ExchangeCodeCount++;
            return Task.FromResult(ExchangeCodeCount == 1
                ? new QuickBooksTokenResponse(true, string.Empty, "access-token-one", "refresh-token-one", "bearer", "com.intuit.quickbooks.accounting", 3600, 8_726_400)
                : new QuickBooksTokenResponse(true, string.Empty, "access-token-reconnected", "refresh-token-reconnected", "bearer", "com.intuit.quickbooks.accounting", 3600, 8_726_400));
        }

        public Task<QuickBooksTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            LastRefreshToken = refreshToken;
            return Task.FromResult(RefreshSucceeds
                ? new QuickBooksTokenResponse(true, string.Empty, "access-token-two", "refresh-token-two", "bearer", "com.intuit.quickbooks.accounting", 3600, 8_726_400)
                : new QuickBooksTokenResponse(false, "temporarily_unavailable", string.Empty, string.Empty, string.Empty, string.Empty, 0, 0));
        }

        public Task<QuickBooksProviderResult> RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            LastRevokedToken = refreshToken;
            return Task.FromResult(new QuickBooksProviderResult(RevocationSucceeds, RevocationSucceeds ? string.Empty : "temporarily_unavailable"));
        }

        public Task<QuickBooksCompanyInfoResponse> GetCompanyInfoAsync(string environment, string realmId, string accessToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CompanyValidationSucceeds
                ? new QuickBooksCompanyInfoResponse(true, string.Empty, "Acme QuickBooks", "Acme QuickBooks LLC", "US")
                : new QuickBooksCompanyInfoResponse(false, "company_validation_failed", string.Empty, string.Empty, string.Empty));
        }

        public Task<QuickBooksEntityQueryResponse> QueryEntitiesAsync(string environment, string realmId, string accessToken, string entityType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickBooksEntityQueryResponse(true, string.Empty, Entities.GetValueOrDefault(entityType, [])));
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
