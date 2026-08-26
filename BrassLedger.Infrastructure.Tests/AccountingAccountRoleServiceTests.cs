using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Infrastructure.Tests;

public sealed class AccountingAccountRoleServiceTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine("/home/rich/temp", "BrassLedger.AccountRoles.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Assignment_RequiresAuthorityConfirmationAndSafeControlAccountState()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var actor = await GetActorAsync(factory);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, "Administrator");
        Guid replacementRevenueId;
        Guid replacementReceivablesId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            replacementRevenueId = Guid.NewGuid();
            replacementReceivablesId = Guid.NewGuid();
            db.Accounts.AddRange(
                new GeneralLedgerAccount { Id = replacementRevenueId, CompanyId = actor.CompanyId, Number = "4999", Name = "Configured sales revenue", Type = AccountType.Revenue, IsActive = true },
                new GeneralLedgerAccount { Id = replacementReceivablesId, CompanyId = actor.CompanyId, Number = "1199", Name = "Replacement receivables", Type = AccountType.Asset, IsControlAccount = true, IsActive = true });
            await db.SaveChangesAsync();
        }

        var service = scope.ServiceProvider.GetRequiredService<IAccountingAccountRoleService>();
        var workspace = await service.GetWorkspaceAsync();
        Assert.True(workspace.Authorized);
        Assert.Equal(AccountingAccountRoles.Definitions.Count, workspace.Roles.Count);
        var revenue = Assert.Single(workspace.Roles, role => role.Code == AccountingAccountRoles.DefaultRevenue);

        var unconfirmed = await service.AssignAsync(new(revenue.Code, replacementRevenueId, revenue.AccountId));
        Assert.False(unconfirmed.Succeeded);
        Assert.Contains("confirmation", unconfirmed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var changed = await service.AssignAsync(new(revenue.Code, replacementRevenueId, revenue.AccountId, true));
        Assert.True(changed.Succeeded, changed.ErrorMessage);

        var refreshed = await service.GetWorkspaceAsync();
        var receivables = Assert.Single(refreshed.Roles, role => role.Code == AccountingAccountRoles.AccountsReceivable);
        var unsafeControlChange = await service.AssignAsync(new(receivables.Code, replacementReceivablesId, receivables.AccountId, true));
        Assert.False(unsafeControlChange.Succeeded);
        Assert.Contains("nonzero balance", unsafeControlChange.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        await using var verified = await factory.CreateDbContextAsync();
        Assert.Equal(AccountingAccountRoles.DefaultRevenue, (await verified.Accounts.SingleAsync(account => account.Id == replacementRevenueId)).OperationalRole);
        Assert.Null((await verified.Accounts.SingleAsync(account => account.Id == revenue.AccountId)).OperationalRole);
        Assert.Contains(await verified.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "accounting.operational_account_role_assigned" && audit.EntityId == replacementRevenueId && audit.UserId == actor.UserId);

        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, null, BrassLedgerPermissions.LedgerManage);
        Assert.False((await service.GetWorkspaceAsync()).Authorized);
        var unauthorized = await service.AssignAsync(new(revenue.Code, replacementRevenueId, replacementRevenueId, true));
        Assert.False(unauthorized.Succeeded);
        Assert.Contains("not authorized", unauthorized.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BankTransfer_UsesConfiguredClearingRole_AndGeneralJournalCannotUseInternalRoleReference()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var actor = await GetActorAsync(factory);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, "Administrator");
        Guid replacementClearingId;
        Guid destinationBankId;
        Guid sourceBankId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            replacementClearingId = Guid.NewGuid();
            var destinationCashId = Guid.NewGuid();
            destinationBankId = Guid.NewGuid();
            sourceBankId = await db.BankAccounts.Where(bank => bank.CompanyId == actor.CompanyId).Select(bank => bank.Id).FirstAsync();
            db.Accounts.AddRange(
                new GeneralLedgerAccount { Id = replacementClearingId, CompanyId = actor.CompanyId, Number = "1059", Name = "Configured transfer clearing", Type = AccountType.Asset, IsActive = true },
                new GeneralLedgerAccount { Id = destinationCashId, CompanyId = actor.CompanyId, Number = "1009", Name = "Secondary cash", Type = AccountType.Asset, IsActive = true });
            db.BankAccounts.Add(new BankAccount { Id = destinationBankId, CompanyId = actor.CompanyId, LedgerAccountId = destinationCashId, Name = "Secondary bank", AccountNumberMasked = "••1009", CurrentBalance = 0m, LastReconciledOn = DateOnly.MinValue });
            await db.SaveChangesAsync();
        }

        var roles = scope.ServiceProvider.GetRequiredService<IAccountingAccountRoleService>();
        var clearing = Assert.Single((await roles.GetWorkspaceAsync()).Roles, role => role.Code == AccountingAccountRoles.BankTransferClearing);
        var assigned = await roles.AssignAsync(new(clearing.Code, replacementClearingId, clearing.AccountId, true));
        Assert.True(assigned.Succeeded, assigned.ErrorMessage);

        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var transfer = await transactions.CreateBankTransferAsync(new(sourceBankId, destinationBankId, new DateOnly(2026, 8, 25), 25m, "ROLE-XFER-1", "Role-routed transfer"));
        Assert.True(transfer.Succeeded, transfer.ErrorMessage);
        await using (var verified = await factory.CreateDbContextAsync())
        {
            var entryId = await verified.JournalEntries.Where(entry => entry.CompanyId == actor.CompanyId && entry.SourceDocumentId == transfer.Id && entry.SourceDocumentType == "BankTransferOutbound").Select(entry => entry.Id).SingleAsync();
            Assert.Contains(await verified.JournalEntryLines.Where(line => line.JournalEntryId == entryId).ToArrayAsync(), line => line.AccountId == replacementClearingId && line.Debit == 25m);
        }

        var attemptedBypass = await transactions.SaveJournalEntryDraftAsync(new(null, new DateOnly(2026, 8, 25), "ROLE-BYPASS", "Attempt internal role reference",
        [
            new("\u001foperational-role:AccountsReceivable", 1m, 0m, "Unauthorized control route"),
            new("4999", 0m, 1m, "Offset")
        ]));
        Assert.False(attemptedBypass.Succeeded);
        Assert.Contains("reserved", attemptedBypass.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddBrassLedgerInfrastructure(new ConfigurationBuilder().Build(), _contentRootPath, seedSampleData: true);
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid UserId, Guid CompanyId)> GetActorAsync(IDbContextFactory<BrassLedgerDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(candidate => candidate.UserName == "controller");
        return (user.Id, user.CompanyId);
    }

    private static void SetContext(IServiceProvider services, Guid userId, Guid companyId, string? role, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString())
        };
        if (!string.IsNullOrWhiteSpace(role)) claims.Add(new(ClaimTypes.Role, role));
        claims.AddRange(permissions.Select(permission => new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
    }

    public void Dispose()
    {
        if (!Directory.Exists(_contentRootPath)) return;
        try { Directory.Delete(_contentRootPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
