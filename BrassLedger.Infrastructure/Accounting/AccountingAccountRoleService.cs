using System.Security.Claims;
using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class AccountingAccountRoleService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider) : IAccountingAccountRoleService
{
    public async Task<AccountingAccountRoleWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null || !CanManage(actor.Principal)) return new(false, [], []);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = await db.Accounts.AsNoTracking()
            .Where(account => account.CompanyId == actor.CompanyId && account.IsActive)
            .OrderBy(account => account.Number)
            .ToListAsync(cancellationToken);
        var byRole = accounts.Where(account => !string.IsNullOrWhiteSpace(account.OperationalRole)).ToDictionary(account => account.OperationalRole!, StringComparer.Ordinal);
        var roles = AccountingAccountRoles.Definitions.Select(definition =>
        {
            var account = byRole.GetValueOrDefault(definition.Code);
            return new AccountingAccountRoleSnapshot(
                definition.Code,
                definition.Name,
                definition.Description,
                definition.RequiredAccountType.ToString(),
                definition.RequiresControlAccount,
                definition.RequiresZeroBalanceToReassign,
                account?.Id,
                account?.Number ?? string.Empty,
                account?.Name ?? string.Empty,
                account?.CurrentBalance ?? 0m);
        }).ToArray();
        var candidates = accounts.Select(account => new AccountingAccountRoleCandidate(
            account.Id,
            account.Number,
            account.Name,
            account.Type.ToString(),
            account.IsControlAccount,
            account.CurrentBalance,
            account.OperationalRole ?? string.Empty)).ToArray();
        return new(true, roles, candidates);
    }

    public async Task<TransactionResult> AssignAsync(AssignAccountingAccountRoleRequest request, CancellationToken cancellationToken = default)
    {
        var actor = CurrentActor();
        if (actor is null) return TransactionResult.Failure("An authenticated company is required.");
        if (!CanManage(actor.Principal)) return TransactionResult.Failure("You are not authorized to change operational account routing.");
        var definition = AccountingAccountRoles.Find(request.RoleCode);
        if (definition is null || request.AccountId == Guid.Empty) return TransactionResult.Failure("Select a known operational role and a valid account.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = await db.Accounts.Where(account => account.CompanyId == actor.CompanyId).ToListAsync(cancellationToken);
        var current = accounts.SingleOrDefault(account => string.Equals(account.OperationalRole, definition.Code, StringComparison.Ordinal));
        if (current?.Id != request.ExpectedCurrentAccountId)
            return TransactionResult.Failure("The operational account assignment changed after it was displayed. Reload the configuration before saving.");
        var target = accounts.SingleOrDefault(account => account.Id == request.AccountId);
        if (target is null || !target.IsActive) return TransactionResult.Failure("The selected account is unavailable in the active company.");
        if (target.Type != definition.RequiredAccountType || target.IsControlAccount != definition.RequiresControlAccount)
            return TransactionResult.Failure($"{definition.Name} requires an active {definition.RequiredAccountType} account whose control-account setting is {(definition.RequiresControlAccount ? "enabled" : "disabled")}.");
        if (!string.IsNullOrWhiteSpace(target.OperationalRole) && !string.Equals(target.OperationalRole, definition.Code, StringComparison.Ordinal))
            return TransactionResult.Failure("The selected account already serves another operational role. Assign a different eligible account.");
        if (current?.Id == target.Id) return TransactionResult.Success(target.Id);
        if (!request.ConfirmAssignment) return TransactionResult.Failure("Changing operational account routing requires explicit confirmation.");

        if (definition.RequiresZeroBalanceToReassign)
        {
            if ((current?.CurrentBalance ?? 0m) != 0m || target.CurrentBalance != 0m)
                return TransactionResult.Failure("This role cannot be reassigned while either the current or replacement account has a nonzero balance. Reconcile and transfer the balance through an authorized accounting workflow first.");
            var openDependency = await HasOpenDependencyAsync(db, actor.CompanyId, definition.Code, cancellationToken);
            if (openDependency)
                return TransactionResult.Failure("This role cannot be reassigned while related subledger items, inventory, deposits, advances, or payroll liabilities remain open.");
        }

        var priorAccountId = current?.Id;
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Clear and persist the old assignment first so providers cannot choose an
            // update order that momentarily violates the unique company/role index.
            // The surrounding transaction makes the two saves atomic.
            if (current is not null)
            {
                current.OperationalRole = null;
                await db.SaveChangesAsync(cancellationToken);
            }

            target.OperationalRole = definition.Code;
            db.BusinessAuditEntries.Add(new BusinessAuditEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = actor.CompanyId,
                UserId = actor.UserId,
                Action = "accounting.operational_account_role_assigned",
                EntityType = nameof(GeneralLedgerAccount),
                EntityId = target.Id,
                DetailJson = JsonSerializer.Serialize(new { roleCode = definition.Code, previousAccountId = priorAccountId, accountId = target.Id, target.Number, target.Type, target.IsControlAccount }),
                OccurredAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The operational account configuration changed concurrently. Reload it before trying again.");
        }
        catch (DbUpdateException)
        {
            return TransactionResult.Failure("The selected account or role was assigned concurrently. No partial configuration was saved; reload before trying again.");
        }
        return TransactionResult.Success(target.Id);
    }

    private static async Task<bool> HasOpenDependencyAsync(BrassLedgerDbContext db, Guid companyId, string roleCode, CancellationToken cancellationToken)
    {
        return roleCode switch
        {
            AccountingAccountRoles.AccountsReceivable => await db.SalesInvoices.AnyAsync(invoice => invoice.CompanyId == companyId && invoice.BalanceDue != 0m && invoice.Status != "Voided", cancellationToken),
            AccountingAccountRoles.AccountsPayable => await db.VendorBills.AnyAsync(bill => bill.CompanyId == companyId && bill.BalanceDue != 0m && bill.Status != "Voided", cancellationToken),
            AccountingAccountRoles.GoodsReceivedNotInvoiced => await db.InventoryReceipts.AnyAsync(receipt => receipt.CompanyId == companyId && receipt.Status == "Posted" && !db.VendorBills.Any(bill => bill.InventoryReceiptId == receipt.Id), cancellationToken),
            AccountingAccountRoles.InventoryAsset => await db.InventoryItems.AnyAsync(item => item.CompanyId == companyId && item.QuantityOnHand != 0m, cancellationToken),
            AccountingAccountRoles.VendorAdvances => await db.SubledgerPayments.AnyAsync(payment => payment.CompanyId == companyId && payment.Direction == "VendorDisbursement" && payment.UnappliedAmount != 0m && payment.Status == "Posted", cancellationToken),
            AccountingAccountRoles.CustomerDeposits => await db.SubledgerPayments.AnyAsync(payment => payment.CompanyId == companyId && payment.Direction == "CustomerReceipt" && payment.UnappliedAmount != 0m && payment.Status == "Posted", cancellationToken),
            AccountingAccountRoles.PayrollLiabilities => await db.PayrollLiabilities.AnyAsync(liability => liability.CompanyId == companyId && liability.OutstandingAmount != 0m, cancellationToken),
            _ => false
        };
    }

    private Actor? CurrentActor()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId)) return null;
        return new(userId, companyId, principal);
    }

    private static bool CanManage(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")) return false;
        if (principal.IsInRole("Administrator") || principal.IsInRole("Owner/CEO")) return true;
        return principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage)
            && principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.LedgerManage);
    }

    private sealed record Actor(Guid UserId, Guid CompanyId, ClaimsPrincipal Principal);
}
