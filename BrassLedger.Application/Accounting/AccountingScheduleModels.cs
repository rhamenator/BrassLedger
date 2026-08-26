namespace BrassLedger.Application.Accounting;

public sealed record SaveAccountingScheduleRequest(
    Guid? Id,
    string ScheduleNumber,
    string Name,
    string ScheduleType,
    DateOnly StartDate,
    int PeriodCount,
    decimal OriginalAmount,
    decimal ResidualAmount,
    decimal AnnualInterestRate,
    Guid? RelatedAssetAccountId,
    Guid BalanceAccountId,
    Guid ExpenseAccountId,
    Guid? PaymentBankAccountId,
    string Notes,
    string ConcurrencyToken = "");

public sealed record AccountingScheduleAccountSnapshot(Guid Id, string Number, string Name, string AccountType, bool IsControlAccount, bool IsActive);

public sealed record AccountingScheduleBankAccountSnapshot(Guid Id, string Name, string AccountNumberMasked, Guid LedgerAccountId, string LedgerAccountNumber);

public sealed record AccountingScheduleInstallmentSnapshot(
    Guid Id,
    int Sequence,
    DateOnly DueOn,
    decimal PrincipalAmount,
    decimal ExpenseAmount,
    decimal PaymentAmount,
    Guid? JournalEntryId,
    string JournalStatus,
    Guid? ReversalJournalEntryId);

public sealed record AccountingScheduleSnapshot(
    Guid Id,
    string ScheduleNumber,
    string Name,
    string ScheduleType,
    string CalculationMethod,
    string Status,
    DateOnly StartDate,
    int PeriodCount,
    decimal OriginalAmount,
    decimal ResidualAmount,
    decimal AnnualInterestRate,
    Guid? RelatedAssetAccountId,
    Guid BalanceAccountId,
    Guid ExpenseAccountId,
    Guid? PaymentBankAccountId,
    Guid? DisposalJournalEntryId,
    string DisposalJournalStatus,
    string Notes,
    string ConcurrencyToken,
    IReadOnlyList<AccountingScheduleInstallmentSnapshot> Installments);

public sealed record AccountingScheduleWorkspace(
    IReadOnlyList<AccountingScheduleSnapshot> Schedules,
    IReadOnlyList<AccountingScheduleAccountSnapshot> Accounts,
    IReadOnlyList<AccountingScheduleBankAccountSnapshot> BankAccounts);

public sealed record ApproveAccountingScheduleRequest(Guid ScheduleId, string ConcurrencyToken);
public sealed record PrepareAccountingScheduleInstallmentsRequest(Guid ScheduleId, DateOnly ThroughDate, string ConcurrencyToken);
public sealed record ReverseAccountingScheduleInstallmentRequest(Guid InstallmentId, DateOnly ReversalDate, string Reason);
public sealed record PrepareFixedAssetDisposalRequest(Guid ScheduleId, DateOnly DisposalDate, decimal ProceedsAmount, Guid? ProceedsBankAccountId, Guid? GainAccountId, Guid? LossAccountId, string Description, string ConcurrencyToken);
public sealed record ReverseFixedAssetDisposalRequest(Guid ScheduleId, DateOnly ReversalDate, string Reason, string ConcurrencyToken);
