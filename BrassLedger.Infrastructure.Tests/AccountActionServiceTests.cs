using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BrassLedger.Application.Security;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Npgsql;

namespace BrassLedger.Infrastructure.Tests;

public sealed class AccountActionServiceTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.AccountAction.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExistingOperator_CanVerifyEmailWithoutChangingCredentialOrExposingToken()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
        var transport = new RecordingSecurityEmailTransport();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountEmail:Enabled"] = "true",
            ["AccountEmail:PublicBaseUrl"] = "https://ledger.example.test",
            ["AccountEmail:Host"] = "smtp.example.test",
            ["AccountEmail:FromAddress"] = "security@example.test",
            ["AccountEmail:EmailVerificationLifetimeHours"] = "24"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ISecurityEmailTransport>(transport);
        services.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        using var provider = services.BuildServiceProvider();
        await provider.InitializeBrassLedgerAsync();
        using var scope = provider.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<IAccountActionService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();

        Guid userId;
        string originalPasswordHash;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var user = await db.Users.SingleAsync(candidate => candidate.UserName == "controller");
            userId = user.Id;
            originalPasswordHash = user.PasswordHash;
            Assert.Null(user.EmailConfirmedAtUtc);
            Assert.Equal(64, user.EmailLookupHash?.Length);
        }

        var requested = await actions.RequestEmailVerificationAsync(userId, "127.0.0.1", "xunit");
        Assert.True(requested.Succeeded, requested.ErrorMessage);
        Assert.True(await dispatcher.DispatchNextAsync());
        var message = Assert.Single(transport.Messages);
        var token = ExtractToken(message.Body);
        Assert.Equal("EmailVerification", (await actions.GetActionAsync(token))!.Purpose);
        Assert.Equal(AccountActionCompletionOutcome.InvalidOrExpired, (await actions.CompleteAsync(
            token, "Must not become a reset 2026", "Must not become a reset 2026", "127.0.0.1", "xunit")).Outcome);
        Assert.Equal(AccountActionCompletionOutcome.Succeeded, (await actions.CompleteEmailVerificationAsync(token, "127.0.0.1", "xunit")).Outcome);
        Assert.Equal(AccountActionCompletionOutcome.InvalidOrExpired, (await actions.CompleteEmailVerificationAsync(token, "127.0.0.1", "xunit")).Outcome);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
            Assert.NotNull(user.EmailConfirmedAtUtc);
            Assert.Equal(originalPasswordHash, user.PasswordHash);
            Assert.Contains(await db.AuthenticationAuditEntries.Where(entry => entry.UserId == userId).ToListAsync(), entry => entry.EventType == "email_verified" && entry.Succeeded);
        }
        Assert.Equal(AuthenticationOutcome.Succeeded, (await authentication.AuthenticateAsync(
            "controller", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "xunit")).Outcome);

        var rejectedChange = await actions.ChangeEmailAsync(userId, "replacement@example.test", "wrong password", "127.0.0.1", "xunit");
        Assert.False(rejectedChange.Succeeded);
        var changed = await actions.ChangeEmailAsync(userId, "replacement@example.test", BrassLedgerAuthenticationDefaults.SeededPassword, "127.0.0.1", "xunit");
        Assert.True(changed.Succeeded, changed.ErrorMessage);
        while (await dispatcher.DispatchNextAsync()) { }
        var replacementMessage = Assert.Single(transport.Messages, candidate => candidate.Subject.Contains("new BrassLedger email", StringComparison.Ordinal));
        Assert.Single(transport.Messages, candidate => candidate.Subject.Contains("email address was changed", StringComparison.Ordinal));
        var replacementToken = ExtractToken(replacementMessage.Body);
        Assert.Equal(AccountActionCompletionOutcome.Succeeded, (await actions.CompleteEmailVerificationAsync(replacementToken, "127.0.0.1", "xunit")).Outcome);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
            Assert.Equal("replacement@example.test", user.Email);
            Assert.Equal(AccountEmailHash("replacement@example.test"), user.EmailLookupHash);
            Assert.NotNull(user.EmailConfirmedAtUtc);
        }
    }

    [Fact]
    public async Task InvitationAndPasswordReset_AreDeliveredProtectedSingleUseAndSessionInvalidating()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
        var transport = new RecordingSecurityEmailTransport();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountEmail:Enabled"] = "true",
            ["AccountEmail:PublicBaseUrl"] = "https://ledger.example.test",
            ["AccountEmail:Host"] = "smtp.example.test",
            ["AccountEmail:FromAddress"] = "security@example.test",
            ["AccountEmail:InvitationLifetimeHours"] = "24",
            ["AccountEmail:PasswordResetLifetimeMinutes"] = "30"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ISecurityEmailTransport>(transport);
        services.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        using var provider = services.BuildServiceProvider();
        await provider.InitializeBrassLedgerAsync();

        using var scope = provider.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<IAccountActionService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid companyId;
        await using (var db = await dbFactory.CreateDbContextAsync()) companyId = await db.Companies.Select(company => company.Id).FirstAsync();

        var invitation = await actions.IssueInvitationAsync(new AccountInvitationRequest(
            companyId, null, "invited-user", "Invited User", "invited@example.test", "Controller", "127.0.0.1", "xunit"));
        Assert.True(invitation.Succeeded, invitation.ErrorMessage);
        var unrelatedCompanyId = Guid.NewGuid();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var invitedUserId = await db.Users.Where(user => user.UserName == "invited-user").Select(user => user.Id).SingleAsync();
            db.Companies.Add(new Company
            {
                Id = unrelatedCompanyId,
                Name = "Unrelated company",
                LegalName = "Unrelated company",
                TaxId = "unrelated",
                BaseCurrency = "USD",
                FiscalYearStartMonth = 1
            });
            db.CompanyMemberships.Add(new CompanyMembership
            {
                Id = Guid.NewGuid(),
                UserId = invitedUserId,
                CompanyId = unrelatedCompanyId,
                Role = "Controller",
                IsActive = false,
                GrantedAtUtc = clock.GetUtcNow()
            });
            await db.SaveChangesAsync();
        }
        var duplicateEmail = await actions.IssueInvitationAsync(new AccountInvitationRequest(
            companyId, null, "different-user", "Different User", "INVITED@example.test", "Controller", "127.0.0.1", "xunit"));
        Assert.False(duplicateEmail.Succeeded);
        Assert.Contains("email address", duplicateEmail.ErrorMessage);
        Assert.Equal(AuthenticationOutcome.InvalidCredentials, (await authentication.AuthenticateAsync("invited-user", "not-active-yet", "127.0.0.1", "xunit")).Outcome);

        var databasePath = Path.Combine(_contentRootPath, "App_Data", "brassledger.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            Assert.Equal(64L, await ScalarLongAsync(connection, "SELECT length(TokenHash) FROM AccountActionTokens WHERE Purpose = 'Invitation';"));
            Assert.Equal(64L, await ScalarLongAsync(connection, "SELECT length(EmailLookupHash) FROM Users WHERE UserName = 'invited-user';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM Users WHERE UserName = 'invited-user' AND Email LIKE 'enc::%';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM SecurityEmailOutboxMessages WHERE RecipientEmail LIKE 'enc::%' AND Body LIKE 'enc::%';"));
            Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT IsActive FROM Users WHERE UserName = 'invited-user';"));
        }

        Assert.True(await dispatcher.DispatchNextAsync());
        var invitationMessage = Assert.Single(transport.Messages);
        Assert.Equal("invited@example.test", invitationMessage.Recipient);
        var invitationToken = ExtractToken(invitationMessage.Body);
        Assert.Equal("Invitation", (await actions.GetActionAsync(invitationToken))!.Purpose);
        Assert.Equal(AccountActionCompletionOutcome.InvalidPassword, (await actions.CompleteAsync(
            invitationToken, "short", "short", "127.0.0.1", "xunit")).Outcome);
        var accepted = await actions.CompleteAsync(
            invitationToken, "A new invited password 2026", "A new invited password 2026", "127.0.0.1", "xunit");
        Assert.Equal(AccountActionCompletionOutcome.Succeeded, accepted.Outcome);
        Assert.Equal("Invitation", accepted.Purpose);
        Assert.Equal(AccountActionCompletionOutcome.InvalidOrExpired, (await actions.CompleteAsync(
            invitationToken, "Another long password 2026", "Another long password 2026", "127.0.0.1", "xunit")).Outcome);
        var firstLogin = await authentication.AuthenticateAsync("invited-user", "A new invited password 2026", "127.0.0.1", "xunit");
        Assert.Equal(AuthenticationOutcome.Succeeded, firstLogin.Outcome);

        string stampBeforeReset;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var activated = await db.Users.SingleAsync(user => user.UserName == "invited-user");
            Assert.True(activated.IsActive);
            Assert.NotNull(activated.EmailConfirmedAtUtc);
            Assert.True((await db.CompanyMemberships.SingleAsync(membership => membership.UserId == activated.Id && membership.CompanyId == companyId)).IsActive);
            Assert.False((await db.CompanyMemberships.SingleAsync(membership => membership.UserId == activated.Id && membership.CompanyId == unrelatedCompanyId)).IsActive);
            stampBeforeReset = activated.SecurityStamp;
        }

        await actions.RequestPasswordResetAsync("missing@example.test", "127.0.0.1", "xunit");
        await actions.RequestPasswordResetAsync("invited@example.test", "127.0.0.1", "xunit");
        Assert.True(await dispatcher.DispatchNextAsync());
        var resetMessage = Assert.Single(transport.Messages, message => message.Subject.Contains("Reset", StringComparison.Ordinal));
        var resetToken = ExtractToken(resetMessage.Body);
        Assert.NotNull(await actions.GetActionAsync(resetToken));
        Assert.Equal(AccountActionCompletionOutcome.InvalidOrExpired, (await actions.CompleteEmailVerificationAsync(resetToken, "127.0.0.1", "xunit")).Outcome);
        var concurrentResetResults = await Task.WhenAll(
            actions.CompleteAsync(resetToken, "A reset password for 2026", "A reset password for 2026", "127.0.0.1", "xunit-a"),
            actions.CompleteAsync(resetToken, "A reset password for 2026", "A reset password for 2026", "127.0.0.1", "xunit-b"));
        var reset = Assert.Single(concurrentResetResults, result => result.Outcome == AccountActionCompletionOutcome.Succeeded);
        Assert.Single(concurrentResetResults, result => result.Outcome == AccountActionCompletionOutcome.InvalidOrExpired);
        Assert.Equal(AuthenticationOutcome.InvalidCredentials, (await authentication.AuthenticateAsync(
            "invited-user", "A new invited password 2026", "127.0.0.1", "xunit")).Outcome);
        Assert.Equal(AuthenticationOutcome.Succeeded, (await authentication.AuthenticateAsync(
            "invited-user", "A reset password for 2026", "127.0.0.1", "xunit")).Outcome);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var resetUser = await db.Users.SingleAsync(user => user.UserName == "invited-user");
            Assert.NotEqual(stampBeforeReset, resetUser.SecurityStamp);
            Assert.Contains(await db.AuthenticationAuditEntries.Where(entry => entry.UserId == resetUser.Id).ToListAsync(), entry => entry.EventType == "password_reset_completed" && entry.Succeeded);
        }
        Assert.Equal(AccountActionCompletionOutcome.InvalidOrExpired, (await actions.CompleteAsync(
            resetToken, "A third password for 2026", "A third password for 2026", "127.0.0.1", "xunit")).Outcome);
        Assert.True(await dispatcher.DispatchNextAsync());
        Assert.Single(transport.Messages, message => message.Subject.Contains("password was changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpiredActionLink_IsCancelledAndNeverDelivered()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
        var transport = new RecordingSecurityEmailTransport();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountEmail:Enabled"] = "true",
            ["AccountEmail:PublicBaseUrl"] = "https://ledger.example.test",
            ["AccountEmail:Host"] = "smtp.example.test",
            ["AccountEmail:FromAddress"] = "security@example.test",
            ["AccountEmail:InvitationLifetimeHours"] = "1"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ISecurityEmailTransport>(transport);
        services.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        using var provider = services.BuildServiceProvider();
        await provider.InitializeBrassLedgerAsync();
        using var scope = provider.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<IAccountActionService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(company => company.Id).FirstAsync();
        Assert.True((await actions.IssueInvitationAsync(new AccountInvitationRequest(
            companyId, null, "never-delivered", "Never Delivered", "never@example.test", "Controller", "127.0.0.1", "xunit"))).Succeeded);

        clock.Advance(TimeSpan.FromHours(2));
        Assert.False(await dispatcher.DispatchNextAsync());
        Assert.Empty(transport.Messages);
        db.ChangeTracker.Clear();
        var cancelled = await db.SecurityEmailOutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Empty(cancelled.Body);
    }

    [Fact]
    public async Task PermanentlyFailedActionEmail_IsPurgedWhenItsActionExpires()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
        var transport = new RecordingSecurityEmailTransport { FailuresRemaining = 1 };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountEmail:Enabled"] = "true",
            ["AccountEmail:PublicBaseUrl"] = "https://ledger.example.test",
            ["AccountEmail:Host"] = "smtp.example.test",
            ["AccountEmail:FromAddress"] = "security@example.test",
            ["AccountEmail:InvitationLifetimeHours"] = "1",
            ["AccountEmail:MaximumDeliveryAttempts"] = "1"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ISecurityEmailTransport>(transport);
        services.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        using var provider = services.BuildServiceProvider();
        await provider.InitializeBrassLedgerAsync();
        using var scope = provider.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<IAccountActionService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(company => company.Id).FirstAsync();
        Assert.True((await actions.IssueInvitationAsync(new AccountInvitationRequest(
            companyId, null, "failed-expiring", "Failed Expiring", "failed-expiring@example.test", "Controller", "127.0.0.1", "xunit"))).Succeeded);

        Assert.True(await dispatcher.DispatchNextAsync());
        db.ChangeTracker.Clear();
        var failed = await db.SecurityEmailOutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal("FailedPermanent", failed.Status);
        Assert.NotEmpty(failed.Body);

        clock.Advance(TimeSpan.FromHours(2));
        Assert.False(await dispatcher.DispatchNextAsync());
        db.ChangeTracker.Clear();
        var cancelled = await db.SecurityEmailOutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Empty(cancelled.Body);
    }

    [Fact]
    public void EnabledSecurityEmail_RejectsUnsafeStartupConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountEmail:Enabled"] = "true",
            ["AccountEmail:PublicBaseUrl"] = "http://ledger.example.test?token=unsafe",
            ["AccountEmail:Host"] = "smtp.example.test",
            ["AccountEmail:Security"] = "Auto",
            ["AccountEmail:FromAddress"] = "not a mailbox"
        }).Build();
        var services = new ServiceCollection();
        services.AddBrassLedgerInfrastructure(configuration, _contentRootPath);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AccountEmailOptions>>().Value);
        Assert.Contains(exception.Failures, failure => failure.Contains("AccountEmail:Security", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("AccountEmail:FromAddress", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("AccountEmail:PublicBaseUrl", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task PostgreSql_InvitationDeliveryAndConcurrentResetRedemption_AreAtomic()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_actions_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync();
            await using var create = administration.CreateCommand();
            create.CommandText = $"CREATE DATABASE {quotedDatabase}";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
            var transport = new RecordingSecurityEmailTransport();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = testBuilder.ConnectionString,
                ["AccountEmail:Enabled"] = "true",
                ["AccountEmail:PublicBaseUrl"] = "https://ledger.example.test",
                ["AccountEmail:Host"] = "smtp.example.test",
                ["AccountEmail:FromAddress"] = "security@example.test"
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<ISecurityEmailTransport>(transport);
            services.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
            using var provider = services.BuildServiceProvider();
            await provider.InitializeBrassLedgerAsync();
            using var scope = provider.CreateScope();
            var actions = scope.ServiceProvider.GetRequiredService<IAccountActionService>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            Guid companyId;
            await using (var db = await dbFactory.CreateDbContextAsync()) companyId = await db.Companies.Select(company => company.Id).FirstAsync();

            Assert.True((await actions.IssueInvitationAsync(new AccountInvitationRequest(
                companyId, null, "postgres-invited", "PostgreSQL Invited", "postgres-invited@example.test", "Controller", "127.0.0.1", "xunit"))).Succeeded);
            Assert.True(await dispatcher.DispatchNextAsync());
            var invitationToken = ExtractToken(Assert.Single(transport.Messages).Body);
            Assert.Equal(AccountActionCompletionOutcome.Succeeded, (await actions.CompleteAsync(
                invitationToken, "PostgreSQL invited password 2026", "PostgreSQL invited password 2026", "127.0.0.1", "xunit")).Outcome);
            await actions.RequestPasswordResetAsync("postgres-invited", "127.0.0.1", "xunit");
            Assert.True(await dispatcher.DispatchNextAsync());
            var resetToken = ExtractToken(Assert.Single(transport.Messages, message => message.Subject.Contains("Reset", StringComparison.Ordinal)).Body);
            var results = await Task.WhenAll(
                actions.CompleteAsync(resetToken, "PostgreSQL reset password 2026", "PostgreSQL reset password 2026", "127.0.0.1", "xunit-a"),
                actions.CompleteAsync(resetToken, "PostgreSQL reset password 2026", "PostgreSQL reset password 2026", "127.0.0.1", "xunit-b"));
            Assert.Single(results, result => result.Outcome == AccountActionCompletionOutcome.Succeeded);
            Assert.Single(results, result => result.Outcome == AccountActionCompletionOutcome.InvalidOrExpired);
            Assert.True(await dispatcher.DispatchNextAsync());
            Assert.Single(transport.Messages, message => message.Subject.Contains("password was changed", StringComparison.Ordinal));

            var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
            var controller = (await authentication.AuthenticateAsync(
                "controller", BrassLedgerAuthenticationDefaults.SeededPassword, "192.0.2.10", "postgres-session-test")).User!;
            var target = (await authentication.AuthenticateAsync(
                "operations", BrassLedgerAuthenticationDefaults.SeededPassword, "192.0.2.11", "postgres-session-test")).User!;
            var sessions = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
            var firstSession = await sessions.IssueAsync(target, "192.0.2.11", "Chrome/128 Linux");
            var secondSession = await sessions.IssueAsync(target, "198.51.100.12", "Firefox/129 Windows");
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, target.UserId.ToString()),
                    new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, target.CompanyId.ToString()),
                    new Claim(BrassLedgerAuthenticationDefaults.SessionIdClaimType, secondSession.SessionId!.Value.ToString())
                ], "test"))
            };
            Assert.Equal(2, (await sessions.GetActiveAsync(target.UserId, secondSession.SessionId.Value, accessor.HttpContext.User)).Count);
            Assert.True(await sessions.RevokeAsync(
                target.UserId, target.CompanyId, firstSession.SessionId!.Value, secondSession.SessionId.Value,
                accessor.HttpContext.User, "203.0.113.7", "postgres-session-test"));

            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                var controllerRecord = await db.Users.SingleAsync(user => user.Id == controller.UserId);
                controllerRecord.MfaEnabled = true;
                controllerRecord.MfaSecret = "POSTGRES-CONTROLLER-SECRET";
                var targetRecord = await db.Users.SingleAsync(user => user.Id == target.UserId);
                targetRecord.MfaEnabled = true;
                targetRecord.MfaSecret = "POSTGRES-TARGET-SECRET";
                targetRecord.MfaEnrolledAtUtc = clock.GetUtcNow();
                db.MfaRecoveryCodes.Add(new MfaRecoveryCode
                {
                    Id = Guid.NewGuid(), UserId = target.UserId, CodeHash = "postgres-recovery-hash", CreatedAtUtc = clock.GetUtcNow()
                });
                await db.SaveChangesAsync();
            }
            accessor.HttpContext = CreateAdministratorContext(controller, includeMfa: true);
            var administration = scope.ServiceProvider.GetRequiredService<ISecurityAdministrationService>();
            var recovered = await administration.ResetOperatorMfaAsync(new AdministratorMfaRecoveryRequest(
                target.UserId, target.UserName, BrassLedgerAuthenticationDefaults.SeededPassword,
                "Manager and HR attestation", "PG-CASE-2026-0042"));
            Assert.True(recovered.Succeeded, recovered.ErrorMessage);
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                Assert.False((await db.Users.SingleAsync(user => user.Id == target.UserId)).MfaEnabled);
                Assert.False(await db.MfaRecoveryCodes.AnyAsync(code => code.UserId == target.UserId));
                Assert.All(await db.UserSessions.Where(session => session.UserId == target.UserId).ToListAsync(), session => Assert.NotNull(session.RevokedAtUtc));
                Assert.True(await db.AccountActionTokens.AnyAsync(action => action.UserId == target.UserId && action.Purpose == "MfaAdministratorRecoveryNotice" && action.ConsumedAtUtc != null));
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString);
            await administration.OpenAsync();
            await using var drop = administration.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task AccountAction_ExpiresAndOutboxRetriesWithoutStoringPlaintextToken()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
        var transport = new RecordingSecurityEmailTransport { FailuresRemaining = 1 };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountEmail:Enabled"] = "true",
            ["AccountEmail:PublicBaseUrl"] = "https://ledger.example.test",
            ["AccountEmail:Host"] = "smtp.example.test",
            ["AccountEmail:FromAddress"] = "security@example.test",
            ["AccountEmail:InvitationLifetimeHours"] = "1"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ISecurityEmailTransport>(transport);
        services.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        using var provider = services.BuildServiceProvider();
        await provider.InitializeBrassLedgerAsync();
        using var scope = provider.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<IAccountActionService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var companyId = await db.Companies.Select(company => company.Id).FirstAsync();

        Assert.True((await actions.IssueInvitationAsync(new AccountInvitationRequest(
            companyId, null, "expiring-user", "Expiring User", "expires@example.test", "Controller", "127.0.0.1", "xunit"))).Succeeded);
        Assert.True(await dispatcher.DispatchNextAsync());
        Assert.Empty(transport.Messages);
        var failed = await db.SecurityEmailOutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal("Failed", failed.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.DoesNotContain("expires@example.test", failed.LastError, StringComparison.OrdinalIgnoreCase);

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var administratorId = await db.Users.Where(user => user.UserName == "controller").Select(user => user.Id).SingleAsync();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, administratorId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage)
            ], "test"))
        };
        var administration = scope.ServiceProvider.GetRequiredService<ISecurityAdministrationService>();
        var delivery = Assert.Single((await administration.GetSnapshotAsync()).SecurityEmailDeliveries);
        Assert.Equal("e***@example.test", delivery.MaskedRecipient);
        Assert.Equal("Failed", delivery.Status);
        Assert.True((await administration.RetrySecurityEmailAsync(delivery.MessageId)).Succeeded);
        db.ChangeTracker.Clear();
        var retried = await db.SecurityEmailOutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal("Pending", retried.Status);
        Assert.Equal(0, retried.AttemptCount);
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.True(await dispatcher.DispatchNextAsync());
        var token = ExtractToken(Assert.Single(transport.Messages).Body);
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_contentRootPath, "App_Data", "brassledger.db")}"))
        {
            await connection.OpenAsync();
            var storedHash = await ScalarStringAsync(connection, "SELECT TokenHash FROM AccountActionTokens LIMIT 1;");
            Assert.DoesNotContain(token, storedHash, StringComparison.Ordinal);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), storedHash);
        }
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Null(await actions.GetActionAsync(token));
    }

    [Fact]
    public async Task AdministratorMfaRecovery_RequiresMfaReauthenticationOwnerAuthorityAndQueuesNotice()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
        var transport = new RecordingSecurityEmailTransport();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountEmail:Enabled"] = "true",
            ["AccountEmail:PublicBaseUrl"] = "https://ledger.example.test",
            ["AccountEmail:Host"] = "smtp.example.test",
            ["AccountEmail:FromAddress"] = "security@example.test"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ISecurityEmailTransport>(transport);
        services.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        using var provider = services.BuildServiceProvider();
        await provider.InitializeBrassLedgerAsync();
        using var scope = provider.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var controllerSignIn = await authentication.AuthenticateAsync(
            "controller", BrassLedgerAuthenticationDefaults.SeededPassword, "192.0.2.10", "xunit");
        var targetSignIn = await authentication.AuthenticateAsync(
            "operations", BrassLedgerAuthenticationDefaults.SeededPassword, "192.0.2.11", "xunit");
        Assert.Equal(AuthenticationOutcome.Succeeded, controllerSignIn.Outcome);
        Assert.Equal(AuthenticationOutcome.Succeeded, targetSignIn.Outcome);
        var controller = controllerSignIn.User!;
        var target = targetSignIn.User!;
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        Guid priorSessionId;
        string priorStamp;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var controllerRecord = await db.Users.SingleAsync(user => user.Id == controller.UserId);
            controllerRecord.MfaEnabled = true;
            controllerRecord.MfaSecret = "CONTROLLER-SECRET";
            var targetRecord = await db.Users.SingleAsync(user => user.Id == target.UserId);
            targetRecord.MfaEnabled = true;
            targetRecord.MfaSecret = "TARGET-SECRET";
            targetRecord.MfaEnrolledAtUtc = clock.GetUtcNow().AddDays(-30);
            priorStamp = targetRecord.SecurityStamp;
            var controllerMembership = await db.CompanyMemberships.SingleAsync(membership => membership.UserId == controller.UserId && membership.CompanyId == controller.CompanyId);
            var targetMembership = await db.CompanyMemberships.SingleAsync(membership => membership.UserId == target.UserId && membership.CompanyId == controller.CompanyId);
            controllerMembership.IsOwner = false;
            targetMembership.IsOwner = true;
            db.MfaRecoveryCodes.Add(new MfaRecoveryCode
            {
                Id = Guid.NewGuid(), UserId = target.UserId, CodeHash = "recovery-hash", CreatedAtUtc = clock.GetUtcNow()
            });
            db.MfaSignInChallenges.Add(new MfaSignInChallenge
            {
                Id = Guid.NewGuid(), UserId = target.UserId, CompanyId = target.CompanyId,
                TokenHash = "challenge-hash", SecurityStamp = priorStamp,
                CreatedAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddMinutes(5),
                IpAddress = "192.0.2.11", UserAgent = "xunit"
            });
            db.AccountActionTokens.Add(new AccountActionToken
            {
                Id = Guid.NewGuid(), UserId = target.UserId, CompanyId = target.CompanyId,
                Purpose = "PasswordReset", TokenHash = "outstanding-action-hash", SecurityStamp = priorStamp,
                CreatedAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddHours(1),
                RequestedIpAddress = "192.0.2.11"
            });
            priorSessionId = Guid.NewGuid();
            db.UserSessions.Add(new UserSession
            {
                Id = priorSessionId, UserId = target.UserId, SecurityStamp = priorStamp,
                AuthenticationMethod = "Password + MFA", CreatedAtUtc = clock.GetUtcNow(),
                LastSeenAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddMinutes(20),
                IpAddress = "192.0.2.11", UserAgent = "xunit"
            });
            await db.SaveChangesAsync();
        }

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = CreateAdministratorContext(controller, includeMfa: false);
        var administration = scope.ServiceProvider.GetRequiredService<ISecurityAdministrationService>();
        var request = new AdministratorMfaRecoveryRequest(
            target.UserId, target.UserName, BrassLedgerAuthenticationDefaults.SeededPassword,
            "In-person identity verification", "CASE-2026-0042");
        var withoutMfa = await administration.ResetOperatorMfaAsync(request);
        Assert.False(withoutMfa.Succeeded);
        Assert.Contains("multi-factor", withoutMfa.ErrorMessage);

        accessor.HttpContext = CreateAdministratorContext(controller, includeMfa: true);
        var wrongPassword = await administration.ResetOperatorMfaAsync(request with { CurrentAdministratorPassword = "incorrect" });
        Assert.False(wrongPassword.Succeeded);
        Assert.Contains("password", wrongPassword.ErrorMessage);
        var unsupportedEvidence = await administration.ResetOperatorMfaAsync(request with { VerificationMethod = "Unreviewed custom method" });
        Assert.False(unsupportedEvidence.Succeeded);
        Assert.Contains("verification method", unsupportedEvidence.ErrorMessage);
        var wrongUserName = await administration.ResetOperatorMfaAsync(request with { ConfirmUserName = "Operations" });
        Assert.False(wrongUserName.Succeeded);
        Assert.Contains("username", wrongUserName.ErrorMessage);
        var ownerDenied = await administration.ResetOperatorMfaAsync(request);
        Assert.False(ownerDenied.Succeeded);
        Assert.Contains("owner", ownerDenied.ErrorMessage);

        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.CompanyMemberships.SingleAsync(membership => membership.UserId == controller.UserId && membership.CompanyId == controller.CompanyId)).IsOwner = true;
            await db.SaveChangesAsync();
        }
        var recovered = await administration.ResetOperatorMfaAsync(request);
        Assert.True(recovered.Succeeded, recovered.ErrorMessage);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var recoveredTarget = await db.Users.SingleAsync(user => user.Id == target.UserId);
            Assert.False(recoveredTarget.MfaEnabled);
            Assert.Empty(recoveredTarget.MfaSecret);
            Assert.Null(recoveredTarget.MfaEnrolledAtUtc);
            Assert.NotEqual(priorStamp, recoveredTarget.SecurityStamp);
            Assert.False(await db.MfaRecoveryCodes.AnyAsync(code => code.UserId == target.UserId));
            Assert.False(await db.MfaSignInChallenges.AnyAsync(challenge => challenge.UserId == target.UserId));
            Assert.True((await db.UserSessions.SingleAsync(session => session.Id == priorSessionId)).RevokedAtUtc.HasValue);
            Assert.All(await db.AccountActionTokens.Where(action => action.UserId == target.UserId).ToListAsync(), action => Assert.NotNull(action.ConsumedAtUtc));
            Assert.Contains(await db.AuthenticationAuditEntries.ToListAsync(), entry => entry.UserId == controller.UserId && entry.EventType == "mfa_administrator_recovery_reauthentication_failed" && !entry.Succeeded);
            Assert.Contains(await db.AuthenticationAuditEntries.ToListAsync(), entry => entry.UserId == target.UserId && entry.EventType == "mfa_administrator_recovery" && entry.Succeeded);
            var audit = Assert.Single(await db.BusinessAuditEntries.Where(entry => entry.Action == "security.operator.mfa-recovered" && entry.EntityId == target.UserId).ToListAsync());
            Assert.Contains("CASE-2026-0042", audit.DetailJson);
            Assert.Contains("SecurityNotificationQueued", audit.DetailJson);
            var notice = Assert.Single(await db.SecurityEmailOutboxMessages.ToListAsync());
            Assert.False(notice.RequiresUsableAction);
            Assert.Equal("Pending", notice.Status);
        }

        var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
        Assert.True(await dispatcher.DispatchNextAsync());
        var message = Assert.Single(transport.Messages);
        Assert.Contains("multi-factor authentication was reset", message.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE-2026-0042", message.Body);
        Assert.DoesNotContain("TARGET-SECRET", message.Body, StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateAdministratorContext(AuthenticatedUser administrator, bool includeMfa)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, administrator.UserId.ToString()),
            new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, administrator.CompanyId.ToString()),
            new(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage)
        };
        if (includeMfa) claims.Add(new Claim(BrassLedgerAuthenticationDefaults.AuthenticationMethodClaimType, "mfa"));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        context.Request.Headers.UserAgent = "xunit-administrator";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }

    private static string ExtractToken(string body)
    {
        var match = Regex.Match(body, @"https://\S+", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        var uri = new Uri(match.Value.Trim());
        return QueryHelpers.ParseQuery(uri.Query)["token"].ToString();
    }

    private static string AccountEmailHash(string email) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant())));

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty;
    }

    private sealed class RecordingSecurityEmailTransport : ISecurityEmailTransport
    {
        public bool IsConfigured => true;
        public int FailuresRemaining { get; set; }
        public List<RecordedMessage> Messages { get; } = [];

        public Task<string> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("Simulated transport rejection without sensitive content.");
            }
            Messages.Add(new RecordedMessage(recipient, subject, body));
            return Task.FromResult($"<{Guid.NewGuid():N}@example.test>");
        }
    }

    private sealed record RecordedMessage(string Recipient, string Subject, string Body);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_contentRootPath)) return;
        try { Directory.Delete(_contentRootPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
