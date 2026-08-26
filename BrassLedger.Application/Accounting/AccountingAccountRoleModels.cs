namespace BrassLedger.Application.Accounting;

public interface IAccountingAccountRoleService
{
    Task<AccountingAccountRoleWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);
    Task<TransactionResult> AssignAsync(AssignAccountingAccountRoleRequest request, CancellationToken cancellationToken = default);
}

public sealed record AccountingAccountRoleWorkspace(
    bool Authorized,
    IReadOnlyList<AccountingAccountRoleSnapshot> Roles,
    IReadOnlyList<AccountingAccountRoleCandidate> Accounts);

public sealed record AccountingAccountRoleSnapshot(
    string Code,
    string Name,
    string Description,
    string RequiredAccountType,
    bool RequiresControlAccount,
    bool RequiresZeroBalanceToReassign,
    Guid? AccountId,
    string AccountNumber,
    string AccountName,
    decimal AccountBalance);

public sealed record AccountingAccountRoleCandidate(
    Guid AccountId,
    string Number,
    string Name,
    string AccountType,
    bool IsControlAccount,
    decimal Balance,
    string AssignedRole);

public sealed record AssignAccountingAccountRoleRequest(
    string RoleCode,
    Guid AccountId,
    Guid? ExpectedCurrentAccountId,
    bool ConfirmAssignment = false);
