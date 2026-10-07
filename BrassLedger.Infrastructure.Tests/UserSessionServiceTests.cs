using System.Security.Claims;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Infrastructure.Tests;

public sealed class UserSessionServiceTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.UserSession.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task NamedSessions_AreCallerScopedProtectedMaskedAndIndividuallyRevocable()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-25T12:00:00Z"));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddBrassLedgerInfrastructure(new ConfigurationBuilder().Build(), _contentRootPath, seedSampleData: true);
        using var provider = services.BuildServiceProvider();
        await provider.InitializeBrassLedgerAsync();
        using var scope = provider.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var sessionService = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var signedIn = await authentication.AuthenticateAsync(
            "controller", BrassLedgerAuthenticationDefaults.SeededPassword, "192.0.2.23", "xunit");
        Assert.Equal(AuthenticationOutcome.Succeeded, signedIn.Outcome);

        accessor.HttpContext = CreateOperatorContext(signedIn.User!);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var expiredHistoryId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserSessions.Add(new UserSession
            {
                Id = expiredHistoryId,
                UserId = signedIn.User!.UserId,
                SecurityStamp = signedIn.User.SecurityStamp,
                AuthenticationMethod = "Password",
                CreatedAtUtc = clock.GetUtcNow().AddDays(-101),
                LastSeenAtUtc = clock.GetUtcNow().AddDays(-101),
                ExpiresAtUtc = clock.GetUtcNow().AddDays(-100),
                RevokedAtUtc = clock.GetUtcNow().AddDays(-100),
                IpAddress = "192.0.2.99",
                UserAgent = "retained-history"
            });
            await db.SaveChangesAsync();
        }
        var first = await sessionService.IssueAsync(
            signedIn.User!, "192.0.2.23",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/128.0 Safari/537.36");
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await sessionService.IssueAsync(
            signedIn.User!, "198.51.100.44",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:129.0) Gecko/20100101 Firefox/129.0");
        accessor.HttpContext = CreateOperatorContext(second);

        var sessions = await sessionService.GetActiveAsync(signedIn.User!.UserId, second.SessionId!.Value, accessor.HttpContext.User);
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, session => session.Id == first.SessionId && session.Device == "Chrome on Linux" && session.MaskedNetwork == "192.0.2.xxx" && !session.IsCurrent);
        Assert.Contains(sessions, session => session.Id == second.SessionId && session.Device == "Firefox on Windows" && session.MaskedNetwork == "198.51.100.xxx" && session.IsCurrent);
        Assert.False(await sessionService.RevokeAsync(
            signedIn.User.UserId, signedIn.User.CompanyId, second.SessionId.Value, second.SessionId.Value, accessor.HttpContext.User, "203.0.113.1", "xunit"));
        Assert.True(await sessionService.RevokeAsync(
            signedIn.User.UserId, signedIn.User.CompanyId, first.SessionId!.Value, second.SessionId.Value, accessor.HttpContext.User, "203.0.113.1", "xunit"));
        Assert.Single(await sessionService.GetActiveAsync(signedIn.User.UserId, second.SessionId.Value, accessor.HttpContext.User));

        accessor.HttpContext = new DefaultHttpContext();
        Assert.Empty(await sessionService.GetActiveAsync(signedIn.User.UserId, second.SessionId.Value, accessor.HttpContext.User));
        Assert.False(await sessionService.RevokeAsync(
            signedIn.User.UserId, signedIn.User.CompanyId, first.SessionId.Value, second.SessionId.Value, accessor.HttpContext.User, "203.0.113.1", "xunit"));

        accessor.HttpContext = CreateOperatorContext(signedIn.User);
        await sessionService.RevokeCurrentAsync(
            signedIn.User.UserId, signedIn.User.CompanyId, second.SessionId.Value, "203.0.113.1", "xunit");
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.False(await db.UserSessions.AnyAsync(session => session.Id == expiredHistoryId));
            Assert.Equal(2, await db.UserSessions.CountAsync(session => session.UserId == signedIn.User.UserId && session.RevokedAtUtc != null));
            Assert.Contains(await db.AuthenticationAuditEntries.Where(entry => entry.UserId == signedIn.User.UserId).ToListAsync(),
                entry => entry.EventType == "session_revoked" && entry.CompanyId == signedIn.User.CompanyId);
            Assert.Contains(await db.AuthenticationAuditEntries.Where(entry => entry.UserId == signedIn.User.UserId).ToListAsync(),
                entry => entry.EventType == "logout" && entry.CompanyId == signedIn.User.CompanyId);
        }

        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_contentRootPath, "App_Data", "brassledger.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IpAddress, UserAgent FROM UserSessions LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var protectedIp = reader.GetString(0);
        var protectedUserAgent = reader.GetString(1);
        Assert.StartsWith("enc::", protectedIp);
        Assert.StartsWith("enc::", protectedUserAgent);
        Assert.DoesNotContain("192.0.2.23", protectedIp, StringComparison.Ordinal);
        Assert.DoesNotContain("Chrome", protectedUserAgent, StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateOperatorContext(AuthenticatedUser user)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, user.CompanyId.ToString()),
            new Claim(BrassLedgerAuthenticationDefaults.SessionIdClaimType, user.SessionId?.ToString() ?? string.Empty)
        ], "test"));
        return context;
    }

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
