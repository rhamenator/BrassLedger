namespace BrassLedger.Domain.Accounting;

public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense
}

public sealed class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = "USD";
    public int FiscalYearStartMonth { get; set; }
}

public sealed class AppUser
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? EmailLookupHash { get; set; }
    public DateTimeOffset? EmailConfirmedAtUtc { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int FailedSignInCount { get; set; }
    public DateTimeOffset? LastFailedSignInUtc { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public DateTimeOffset? LastSuccessfulSignInUtc { get; set; }
    public DateTimeOffset? LastPasswordChangedUtc { get; set; }
    public bool MfaEnabled { get; set; }
    public string MfaSecret { get; set; } = string.Empty;
    public DateTimeOffset? MfaEnrolledAtUtc { get; set; }
    public long? MfaLastAcceptedTimeStep { get; set; }
    public int MfaFailedAttemptCount { get; set; }
    public DateTimeOffset? MfaLockoutEndUtc { get; set; }
}

public sealed class MfaRecoveryCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
}

public sealed class MfaSignInChallenge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public int FailedAttemptCount { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}

public sealed class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public string AuthenticationMethod { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}

public sealed class AccountActionToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CompanyId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string RequestedIpAddress { get; set; } = string.Empty;
}

public sealed class SecurityEmailOutboxMessage
{
    public Guid Id { get; set; }
    public Guid AccountActionTokenId { get; set; }
    public bool RequiresUsableAction { get; set; } = true;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
}

public sealed class CompanyMembership
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset GrantedAtUtc { get; set; }
}

public sealed class CurrencyExchangeRate
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class ConsolidationGroup
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ReportingCurrency { get; set; } = "USD";
    public bool IsActive { get; set; } = true;
}

public sealed class ConsolidationGroupCompany
{
    public Guid Id { get; set; }
    public Guid ConsolidationGroupId { get; set; }
    public Guid MemberCompanyId { get; set; }
    public decimal OwnershipPercentage { get; set; } = 1m;
}

public sealed class AccountingPeriod
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string Status { get; set; } = "Open";
    public Guid? ClosedByUserId { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class BusinessAuditEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string DetailJson { get; set; } = "{}";
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class IntegrationConnection
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Disabled";
    public string SettingsJson { get; set; } = "{}";
    public string CredentialsJson { get; set; } = "{}";
    public DateTimeOffset? LastValidatedAtUtc { get; set; }
    public int CredentialVersion { get; set; }
    public string CredentialOperationLeaseId { get; set; } = string.Empty;
    public string CredentialOperation { get; set; } = string.Empty;
    public DateTimeOffset? CredentialOperationLeaseExpiresAtUtc { get; set; }
}

public sealed class OAuthAuthorizationAttempt
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string StateHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}

public sealed class ExternalEntityLink
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string ProviderEntityId { get; set; } = string.Empty;
    public Guid LocalEntityId { get; set; }
    public string ProviderSyncToken { get; set; } = string.Empty;
    public string LastRemoteFingerprint { get; set; } = string.Empty;
    public string LastLocalFingerprint { get; set; } = string.Empty;
    public DateTimeOffset LastSynchronizedAtUtc { get; set; }
}

public sealed class IntegrationSyncRun
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Direction { get; set; } = "Import";
    public bool IsDryRun { get; set; }
    public string Status { get; set; } = string.Empty;
    public int FetchedCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int ConflictCount { get; set; }
    public int RejectedCount { get; set; }
    public string SnapshotSha256 { get; set; } = string.Empty;
    public string DetailJson { get; set; } = "[]";
    public Guid? InitiatedByUserId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

public sealed class InventoryTransaction
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InventoryItemId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? BinId { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal QuantityChange { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid? JournalEntryId { get; set; }
    public Guid? InventoryTransferId { get; set; }
}

public sealed class AccessRole
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string Permissions { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; }
    public bool RequiresMfa { get; set; }
}

public sealed class AuthenticationAuditEntry
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CompanyId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class GeneralLedgerAccount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public decimal CurrentBalance { get; set; }
    public bool IsControlAccount { get; set; }
    public bool IsActive { get; set; }
    public string? OperationalRole { get; set; }
}

public sealed class JournalEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BankAccountId { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public string EntryNumber { get; set; } = string.Empty;
    public DateOnly PostedOn { get; set; }
    public string SourceModule { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Posted";
    public bool IsPosted { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTimeOffset? RejectedAtUtc { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset PostedAtUtc { get; set; }
    public Guid? ReversalOfJournalEntryId { get; set; }
    public Guid? ReversedByJournalEntryId { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class JournalEntryLine
{
    public Guid Id { get; set; }
    public Guid JournalEntryId { get; set; }
    public Guid AccountId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public sealed class BankReconciliation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public DateOnly StatementDate { get; set; }
    public decimal StatementClosingBalance { get; set; }
    public decimal BookBalance { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClearedAmount { get; set; }
    public decimal Variance { get; set; }
    public string Status { get; set; } = "Completed";
    public string Notes { get; set; } = string.Empty;
    public Guid? ReconciledByUserId { get; set; }
    public DateTimeOffset ReconciledAtUtc { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public DateTimeOffset? ReopenedAtUtc { get; set; }
    public string ReopenReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class BankReconciliationItem
{
    public Guid Id { get; set; }
    public Guid BankReconciliationId { get; set; }
    public Guid JournalEntryId { get; set; }
}

public sealed class BankStatementImportBatch
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public string Status { get; set; } = "Imported";
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int RejectedCount { get; set; }
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public string RejectionJson { get; set; } = "[]";
    public Guid? ImportedByUserId { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
}

public sealed class AccountingInterchangeBatch
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public string? CommittedImportKey { get; set; }
    public string Status { get; set; } = "Validated";
    public bool IsDryRun { get; set; }
    public int RowCount { get; set; }
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int RejectedCount { get; set; }
    public string RejectionJson { get; set; } = "[]";
    public Guid? ProcessedByUserId { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}

public sealed class BankStatementTransaction
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public Guid ImportBatchId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public DateOnly? PostedDate { get; set; }
    public decimal Amount { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string Payee { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = "Unmatched";
    public Guid? MatchedJournalEntryId { get; set; }
    public DateTimeOffset? MatchedAtUtc { get; set; }
    public Guid? MatchedByUserId { get; set; }
    public string MatchNote { get; set; } = string.Empty;
    public string RawJson { get; set; } = "{}";
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class BankTransfer
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FromBankAccountId { get; set; }
    public Guid ToBankAccountId { get; set; }
    public DateOnly TransferDate { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;
    public string Status { get; set; } = "Posted";
    public Guid JournalEntryId { get; set; }
    public Guid InboundJournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? InboundReversalJournalEntryId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class Customer
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CustomerNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal CreditLimit { get; set; }
    public decimal OpenBalance { get; set; }
}

public sealed class SalesInvoice
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal BalanceDue { get; set; }
    public Guid? SalesOrderId { get; set; }
    public Guid? InventoryShipmentId { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class SalesInvoiceLine
{
    public Guid Id { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public int Sequence { get; set; }
    public Guid RevenueAccountId { get; set; }
    public Guid? SalesOrderLineId { get; set; }
    public Guid? InventoryShipmentLineId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Vendor
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string VendorNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public decimal OpenBalance { get; set; }
}

public sealed class VendorBill
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid VendorId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal BalanceDue { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? InventoryReceiptId { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class VendorBillLine
{
    public Guid Id { get; set; }
    public Guid VendorBillId { get; set; }
    public Guid? InventoryReceiptLineId { get; set; }
    public int Sequence { get; set; }
    public Guid ExpenseAccountId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal MatchedQuantity { get; set; }
    public decimal QuantityVarianceQuantity { get; set; }
    public decimal ReceiptUnitCost { get; set; }
    public decimal AccrualAmount { get; set; }
    public decimal PriceVarianceAmount { get; set; }
    public decimal QuantityVarianceAmount { get; set; }
}

public sealed class SubledgerPayment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public Guid CounterpartyId { get; set; }
    public Guid BankAccountId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public decimal AppliedAmount { get; set; }
    public decimal UnappliedAmount { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = "Posted";
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SubledgerPaymentApplication
{
    public Guid Id { get; set; }
    public Guid SubledgerPaymentId { get; set; }
    public Guid DocumentId { get; set; }
    public decimal Amount { get; set; }
}

public sealed class SubledgerAdjustment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Subledger { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public Guid CounterpartyId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? BankAccountId { get; set; }
    public DateOnly AdjustmentDate { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string OffsetAccountNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Posted";
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SubledgerDocumentWorkflow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentScope { get; set; } = "company";
    public string DocumentNumber { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Draft";
    public bool IsRecurringTemplate { get; set; }
    public string Frequency { get; set; } = string.Empty;
    public int FrequencyInterval { get; set; } = 1;
    public DateOnly? NextOccurrenceDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Guid? SourceTemplateId { get; set; }
    public Guid? PostedDocumentId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTimeOffset? RejectedAtUtc { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal ReorderPoint { get; set; }
    public bool IsActive { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryWarehouse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string StateOrProvince { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
    public bool IsDefault { get; set; }
    public string? DefaultMarker { get; set; }
    public bool IsActive { get; set; } = true;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryBin
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string? DefaultMarker { get; set; }
    public bool IsActive { get; set; } = true;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryLocationBalance
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InventoryItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid BinId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryTransfer
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InventoryItemId { get; set; }
    public Guid SourceWarehouseId { get; set; }
    public Guid SourceBinId { get; set; }
    public Guid DestinationWarehouseId { get; set; }
    public Guid DestinationBinId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateOnly TransferDate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Posted";
    public Guid? TransferredByUserId { get; set; }
    public DateTimeOffset TransferredAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SalesQuote
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public DateOnly QuotedOn { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public string Status { get; set; } = "Draft";
    public decimal TotalAmount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? WithdrawnByUserId { get; set; }
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public string WithdrawalReason { get; set; } = string.Empty;
    public Guid? ConvertedByUserId { get; set; }
    public DateTimeOffset? ConvertedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SalesQuoteLine
{
    public Guid Id { get; set; }
    public Guid SalesQuoteId { get; set; }
    public int Sequence { get; set; }
    public Guid InventoryItemId { get; set; }
    public Guid RevenueAccountId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class SalesOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? SalesQuoteId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateOnly OrderedOn { get; set; }
    public DateOnly? RequestedShipOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SalesOrderLine
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public int Sequence { get; set; }
    public Guid InventoryItemId { get; set; }
    public Guid RevenueAccountId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public Guid? AllocationWarehouseId { get; set; }
    public Guid? AllocationBinId { get; set; }
    public decimal ShippedQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal InvoicedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class SalesOrderAmendment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderId { get; set; }
    public int RevisionNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public Guid? AmendedByUserId { get; set; }
    public DateTimeOffset AmendedAtUtc { get; set; }
}

public sealed class InventoryShipment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid? InventoryPackingSlipId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? BinId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public DateOnly ShippedOn { get; set; }
    public string Status { get; set; } = "Posted";
    public decimal TotalCost { get; set; }
    public Guid JournalEntryId { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? ShippedByUserId { get; set; }
    public DateTimeOffset ShippedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryPick
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid BinId { get; set; }
    public string PickNumber { get; set; } = string.Empty;
    public DateOnly PickDate { get; set; }
    public string Status { get; set; } = "Draft";
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryPickLine
{
    public Guid Id { get; set; }
    public Guid InventoryPickId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
}

public sealed class InventoryPackingSlip
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid InventoryPickId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid BinId { get; set; }
    public string PackingSlipNumber { get; set; } = string.Empty;
    public DateOnly PackedOn { get; set; }
    public string Status { get; set; } = "Packed";
    public Guid? PackedByUserId { get; set; }
    public DateTimeOffset PackedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryPackingSlipLine
{
    public Guid Id { get; set; }
    public Guid InventoryPackingSlipId { get; set; }
    public Guid InventoryPickLineId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class SalesOrderBackorderPromise
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public decimal PromisedQuantity { get; set; }
    public decimal FulfilledQuantity { get; set; }
    public DateOnly PromisedShipOn { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryShipmentLine
{
    public Guid Id { get; set; }
    public Guid InventoryShipmentId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

public sealed class CustomerReturnAuthorization
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InventoryShipmentId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateOnly AuthorizedOn { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public Guid? AuthorizedByUserId { get; set; }
    public DateTimeOffset AuthorizedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class CustomerReturnAuthorizationLine
{
    public Guid Id { get; set; }
    public Guid CustomerReturnAuthorizationId { get; set; }
    public Guid InventoryShipmentLineId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal AuthorizedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
}

public sealed class CustomerReturnReceipt
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerReturnAuthorizationId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid BinId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateOnly ReceivedOn { get; set; }
    public string Status { get; set; } = "Posted";
    public decimal TotalCost { get; set; }
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class CustomerReturnReceiptLine
{
    public Guid Id { get; set; }
    public Guid CustomerReturnReceiptId { get; set; }
    public Guid CustomerReturnAuthorizationLineId { get; set; }
    public Guid InventoryShipmentLineId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

public sealed class CustomerReturnCredit
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerReturnReceiptId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public Guid CustomerId { get; set; }
    public string CreditNumber { get; set; } = string.Empty;
    public DateOnly CreditDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Posted";
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SourceAppliedAmount { get; set; }
    public decimal AppliedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class CustomerReturnCreditLine
{
    public Guid Id { get; set; }
    public Guid CustomerReturnCreditId { get; set; }
    public Guid CustomerReturnReceiptLineId { get; set; }
    public Guid SalesInvoiceLineId { get; set; }
    public Guid RevenueAccountId { get; set; }
    public int Sequence { get; set; }
    public decimal Quantity { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class CustomerReturnCreditApplication
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerReturnCreditId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public DateOnly AppliedOn { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Posted";
    public Guid? AppliedByUserId { get; set; }
    public DateTimeOffset AppliedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class CustomerReturnCreditRefund
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerReturnCreditId { get; set; }
    public Guid BankAccountId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly RefundDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Posted";
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? RefundedByUserId { get; set; }
    public DateTimeOffset RefundedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class PurchaseOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? PurchaseRequisitionId { get; set; }
    public Guid VendorId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateOnly OrderedOn { get; set; }
    public DateOnly? ExpectedOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class PurchaseRequisition
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? RequestedVendorId { get; set; }
    public string RequisitionNumber { get; set; } = string.Empty;
    public DateOnly RequestedOn { get; set; }
    public DateOnly? NeededBy { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public decimal TotalEstimatedAmount { get; set; }
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTimeOffset? RejectedAtUtc { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public Guid? ConvertedByUserId { get; set; }
    public DateTimeOffset? ConvertedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class PurchaseRequisitionLine
{
    public Guid Id { get; set; }
    public Guid PurchaseRequisitionId { get; set; }
    public int Sequence { get; set; }
    public Guid InventoryItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public decimal EstimatedUnitCost { get; set; }
    public decimal EstimatedLineTotal { get; set; }
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public int Sequence { get; set; }
    public Guid InventoryItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal InvoicedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal CreditedQuantity { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class InventoryReceipt
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? BinId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateOnly ReceivedOn { get; set; }
    public string Status { get; set; } = "Posted";
    public decimal TotalAmount { get; set; }
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class InventoryReceiptLine
{
    public Guid Id { get; set; }
    public Guid InventoryReceiptId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
    public decimal PriorQuantityOnHand { get; set; }
    public decimal PriorUnitCost { get; set; }
    public decimal ResultingUnitCost { get; set; }
}

public sealed class PurchaseInvoiceMatch
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InventoryReceiptId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid VendorId { get; set; }
    public Guid? VendorBillId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public decimal InvoiceAmount { get; set; }
    public decimal AccrualAmount { get; set; }
    public decimal PriceVarianceAmount { get; set; }
    public decimal QuantityVarianceQuantity { get; set; }
    public decimal QuantityVarianceAmount { get; set; }
    public string SourceReceiptConcurrencyToken { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class PurchaseInvoiceMatchLine
{
    public Guid Id { get; set; }
    public Guid PurchaseInvoiceMatchId { get; set; }
    public Guid InventoryReceiptLineId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal InvoiceQuantity { get; set; }
    public decimal MatchedQuantity { get; set; }
    public decimal QuantityVarianceQuantity { get; set; }
    public decimal ReceiptUnitCost { get; set; }
    public decimal InvoiceUnitCost { get; set; }
    public decimal AccrualAmount { get; set; }
    public decimal InvoiceAmount { get; set; }
    public decimal PriceVarianceAmount { get; set; }
    public decimal QuantityVarianceAmount { get; set; }
}

public sealed class SupplierReturnAuthorization
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InventoryReceiptId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid VendorId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateOnly AuthorizedOn { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public Guid? AuthorizedByUserId { get; set; }
    public DateTimeOffset AuthorizedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SupplierReturnAuthorizationLine
{
    public Guid Id { get; set; }
    public Guid SupplierReturnAuthorizationId { get; set; }
    public Guid InventoryReceiptLineId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal AuthorizedQuantity { get; set; }
    public decimal ShippedQuantity { get; set; }
    public decimal ReceiptUnitCost { get; set; }
    public decimal UnitCost { get; set; }
}

public sealed class SupplierReturnShipment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SupplierReturnAuthorizationId { get; set; }
    public Guid? SourceVendorBillId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid BinId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public DateOnly ShippedOn { get; set; }
    public string Status { get; set; } = "Posted";
    public decimal TotalAmount { get; set; }
    public decimal VendorCreditAmount { get; set; }
    public bool CreatesVendorCredit { get; set; }
    public decimal SourceAppliedAmount { get; set; }
    public decimal AppliedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? ShippedByUserId { get; set; }
    public DateTimeOffset ShippedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SupplierReturnShipmentLine
{
    public Guid Id { get; set; }
    public Guid SupplierReturnShipmentId { get; set; }
    public Guid SupplierReturnAuthorizationLineId { get; set; }
    public Guid InventoryReceiptLineId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal InvoicedQuantity { get; set; }
    public decimal GrniReductionAmount { get; set; }
    public decimal VendorCreditUnitCost { get; set; }
    public decimal VendorCreditAmount { get; set; }
    public decimal PriorQuantityOnHand { get; set; }
    public decimal PriorUnitCost { get; set; }
    public decimal ResultingUnitCost { get; set; }
}

public sealed class SupplierReturnCreditApplication
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SupplierReturnShipmentId { get; set; }
    public Guid VendorBillId { get; set; }
    public DateOnly AppliedOn { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Posted";
    public Guid? AppliedByUserId { get; set; }
    public DateTimeOffset AppliedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SupplierReturnCreditRefund
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SupplierReturnShipmentId { get; set; }
    public Guid BankAccountId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly RefundDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Posted";
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? RefundedByUserId { get; set; }
    public DateTimeOffset RefundedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class LandedCostAllocation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InventoryReceiptId { get; set; }
    public Guid VendorId { get; set; }
    public Guid? VendorBillId { get; set; }
    public string AllocationNumber { get; set; } = string.Empty;
    public string BillNumber { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string AllocationMethod { get; set; } = "ReceiptValue";
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public decimal TotalAmount { get; set; }
    public string SourceReceiptConcurrencyToken { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class LandedCostCharge
{
    public Guid Id { get; set; }
    public Guid LandedCostAllocationId { get; set; }
    public int Sequence { get; set; }
    public string ChargeType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class LandedCostAllocationLine
{
    public Guid Id { get; set; }
    public Guid LandedCostAllocationId { get; set; }
    public Guid InventoryReceiptLineId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid InventoryItemId { get; set; }
    public int Sequence { get; set; }
    public decimal BasisQuantity { get; set; }
    public decimal BasisAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public string PreparedItemConcurrencyToken { get; set; } = string.Empty;
    public decimal PriorQuantityOnHand { get; set; }
    public decimal PriorUnitCost { get; set; }
    public decimal ResultingUnitCost { get; set; }
}

public sealed class AccountingSchedule
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ScheduleNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = string.Empty;
    public string CalculationMethod { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public DateOnly StartDate { get; set; }
    public int PeriodCount { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal ResidualAmount { get; set; }
    public decimal AnnualInterestRate { get; set; }
    public Guid? RelatedAssetAccountId { get; set; }
    public Guid BalanceAccountId { get; set; }
    public Guid ExpenseAccountId { get; set; }
    public Guid? PaymentBankAccountId { get; set; }
    public Guid? DisposalJournalEntryId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class AccountingScheduleInstallment
{
    public Guid Id { get; set; }
    public Guid AccountingScheduleId { get; set; }
    public int Sequence { get; set; }
    public DateOnly DueOn { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal ExpenseAmount { get; set; }
    public decimal PaymentAmount { get; set; }
    public Guid? JournalEntryId { get; set; }
}

public sealed class BankAccount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AccountNumberMasked { get; set; } = string.Empty;
    public Guid LedgerAccountId { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal UnreconciledAmount { get; set; }
    public DateOnly LastReconciledOn { get; set; }
    public decimal LastReconciledBalance { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class Employee
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ResidenceState { get; set; } = string.Empty;
    public string ResidenceCity { get; set; } = string.Empty;
    public string WorkCity { get; set; } = string.Empty;
    public string ResidenceCounty { get; set; } = string.Empty;
    public string ResidenceSchoolDistrict { get; set; } = string.Empty;
    public string WorkCounty { get; set; } = string.Empty;
    public string WorkSchoolDistrict { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string AddressCity { get; set; } = string.Empty;
    public string AddressState { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string SocialSecurityNumber { get; set; } = string.Empty;
    public string BankRoutingNumber { get; set; } = string.Empty;
    public string BankAccountNumber { get; set; } = string.Empty;
    public string BankAccountType { get; set; } = string.Empty;
    public bool DirectDepositEnabled { get; set; }
    public DateOnly? DirectDepositAuthorizationOn { get; set; }
    public string DirectDepositAuthorizationReference { get; set; } = string.Empty;
    public DateOnly? EmploymentStartedOn { get; set; }
    public DateOnly? EmploymentEndedOn { get; set; }
    public string PayType { get; set; } = string.Empty;
    public decimal MonthlyBasePay { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal OvertimeRate { get; set; }
    public string FilingStatus { get; set; } = "Single";
    public string PayrollFrequency { get; set; } = "Biweekly";
    public int Allowances { get; set; }
    public int FederalFormW4Year { get; set; }
    public bool FederalStep2MultipleJobs { get; set; }
    public decimal FederalStep3Credits { get; set; }
    public decimal FederalStep4OtherIncome { get; set; }
    public decimal FederalStep4Deductions { get; set; }
    public bool FederalWithholdingExempt { get; set; }
    public decimal AdditionalWithholding { get; set; }
    public decimal PreTaxBenefitDeductions { get; set; }
    public decimal PostTaxBenefitDeductions { get; set; }
    public bool IsActive { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollTimecard
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Status { get; set; } = "Draft";
    public string Notes { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public DateTimeOffset? VoidedAtUtc { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public Guid? PayrollRunId { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollTimeEntry
{
    public Guid Id { get; set; }
    public Guid PayrollTimecardId { get; set; }
    public int Sequence { get; set; }
    public DateOnly WorkDate { get; set; }
    public string EarningCode { get; set; } = "REGULAR";
    public string EarningType { get; set; } = "Regular";
    public decimal Hours { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public bool IsTaxable { get; set; } = true;
    public string WorkState { get; set; } = string.Empty;
    public string WorkCounty { get; set; } = string.Empty;
    public string WorkCity { get; set; } = string.Empty;
    public string WorkSchoolDistrict { get; set; } = string.Empty;
    public Guid? ProjectJobId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string W2ReportingJson { get; set; } = "{}";
}

public sealed class ProjectJob
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal BudgetAmount { get; set; }
    public decimal ActualCost { get; set; }
}

public sealed class TaxProfile
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Jurisdiction { get; set; } = string.Empty;
    public string TaxType { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal? AnnualWageBase { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsEmployerSpecific { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public string VerificationNotes { get; set; } = string.Empty;
}

public sealed class PayrollRun
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public DateOnly PayDate { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string RunType { get; set; } = "Regular";
    public string Status { get; set; } = "Draft";
    public string Reference { get; set; } = string.Empty;
    public decimal GrossPayroll { get; set; }
    public decimal PreTaxDeductions { get; set; }
    public decimal EmployeeWithholdings { get; set; }
    public decimal PostTaxDeductions { get; set; }
    public decimal EmployerPayrollTaxes { get; set; }
    public decimal EmployerBenefitContributions { get; set; }
    public decimal NetPay { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTimeOffset? RejectedAtUtc { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string CancellationReason { get; set; } = string.Empty;
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string CalculationWarningsJson { get; set; } = "[]";
    public string TaxContentSnapshotJson { get; set; } = "[]";
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollRunRevision
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayrollRunId { get; set; }
    public int RevisionNumber { get; set; }
    public string StatusBeforeRevision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public Guid? SavedByUserId { get; set; }
    public DateTimeOffset SavedAtUtc { get; set; }
}

public sealed class PayrollJurisdictionRule
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ResidenceJurisdiction { get; set; } = string.Empty;
    public string WorkJurisdiction { get; set; } = string.Empty;
    public bool ExemptWorkWithholding { get; set; }
    public decimal ResidentCreditRate { get; set; }
    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}

public sealed class PayrollDepositScheduleConfiguration
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string JurisdictionCode { get; set; } = "US";
    public string ReturnFormCode { get; set; } = "941";
    public int TaxYear { get; set; }
    public string ScheduleType { get; set; } = "Monthly";
    public decimal LookbackLiability { get; set; }
    public DateOnly LookbackPeriodStart { get; set; }
    public DateOnly LookbackPeriodEnd { get; set; }
    public decimal MonthlyThreshold { get; set; } = 50000m;
    public decimal NextDayThreshold { get; set; } = 100000m;
    public decimal SmallLiabilityThreshold { get; set; } = 2500m;
    public string SmallLiabilityElectionQuartersJson { get; set; } = "[]";
    public string LegalHolidaysJson { get; set; } = "[]";
    public string OfficialRulesUrl { get; set; } = string.Empty;
    public string OfficialCalendarUrl { get; set; } = string.Empty;
    public DateOnly SourceRetrievedOn { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollDisasterReliefConfiguration
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string AnnouncementCode { get; set; } = string.Empty;
    public string DisasterName { get; set; } = string.Empty;
    public string FemaDeclarationNumber { get; set; } = string.Empty;
    public string CoveredAreasJson { get; set; } = "[]";
    public string AffectedTaxpayerBasis { get; set; } = string.Empty;
    public string EligibilityEvidenceReference { get; set; } = string.Empty;
    public string ReliefActionsJson { get; set; } = "[]";
    public string OfficialSourceUrl { get; set; } = string.Empty;
    public DateOnly SourceRetrievedOn { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollDeductionPlan
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string CalculationMethod { get; set; } = "Fixed";
    public decimal DefaultEmployeeValue { get; set; }
    public decimal DefaultEmployerValue { get; set; }
    public bool IsPreTax { get; set; }
    public bool ExemptFromFederalIncomeTax { get; set; }
    public bool ExemptFromFica { get; set; }
    public bool ExemptFromFuta { get; set; }
    public bool ReducesDisposableEarnings { get; set; }
    public string LiabilityAccountNumber { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
    public decimal? EmployeeLimitPerPay { get; set; }
    public decimal? EmployeeAnnualLimit { get; set; }
    public decimal MinimumNetPay { get; set; }
    public string LimitRuleCode { get; set; } = "None";
    public string LimitRuleJson { get; set; } = "{}";
    public string OfficialSourceUrl { get; set; } = string.Empty;
    public DateOnly? SourceRetrievedOn { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public bool IsActive { get; set; } = true;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class EmployeePayrollDeductionElection
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid PayrollDeductionPlanId { get; set; }
    public decimal? EmployeeValueOverride { get; set; }
    public decimal? EmployerValueOverride { get; set; }
    public decimal? EmployeeAnnualLimitOverride { get; set; }
    public string OrderDetailsJson { get; set; } = "{}";
    public DateOnly EffectiveOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public bool IsActive { get; set; } = true;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollRunEmployeeLine
{
    public Guid Id { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public string WorkState { get; set; } = string.Empty;
    public string WorkCity { get; set; } = string.Empty;
    public string ResidenceState { get; set; } = string.Empty;
    public string ResidenceCity { get; set; } = string.Empty;
    public string FilingStatus { get; set; } = string.Empty;
    public string PayrollFrequency { get; set; } = string.Empty;
    public decimal GrossPay { get; set; }
    public decimal TaxableWages { get; set; }
    public decimal YearToDateGrossBefore { get; set; }
    public decimal YearToDateGrossAfter { get; set; }
    public decimal PreTaxDeductions { get; set; }
    public decimal EmployeeWithholdings { get; set; }
    public decimal PostTaxDeductions { get; set; }
    public decimal EmployerPayrollTaxes { get; set; }
    public decimal EmployerBenefitContributions { get; set; }
    public decimal NetPay { get; set; }
    public string CalculationTraceJson { get; set; } = "[]";
}

public sealed class PayrollEarningLine
{
    public Guid Id { get; set; }
    public Guid PayrollRunEmployeeLineId { get; set; }
    public Guid? PayrollTimeEntryId { get; set; }
    public int Sequence { get; set; }
    public string EarningCode { get; set; } = "REGULAR";
    public string EarningType { get; set; } = "Regular";
    public decimal Hours { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public bool IsTaxable { get; set; } = true;
    public DateOnly? WorkedOn { get; set; }
    public string WorkState { get; set; } = string.Empty;
    public string WorkCounty { get; set; } = string.Empty;
    public string WorkCity { get; set; } = string.Empty;
    public string WorkSchoolDistrict { get; set; } = string.Empty;
    public string W2ReportingJson { get; set; } = "{}";
}

public sealed class PayrollDeductionLine
{
    public Guid Id { get; set; }
    public Guid PayrollRunEmployeeLineId { get; set; }
    public int Sequence { get; set; }
    public Guid? PayrollDeductionPlanId { get; set; }
    public Guid? EmployeePayrollDeductionElectionId { get; set; }
    public string DeductionCode { get; set; } = string.Empty;
    public string DeductionType { get; set; } = string.Empty;
    public decimal EmployeeAmount { get; set; }
    public decimal RequestedEmployeeAmount { get; set; }
    public decimal EmployerAmount { get; set; }
    public bool IsPreTax { get; set; }
    public bool ExemptFromFederalIncomeTax { get; set; }
    public bool ExemptFromFica { get; set; }
    public bool ExemptFromFuta { get; set; }
    public string LiabilityAccountNumber { get; set; } = string.Empty;
    public bool LimitApplied { get; set; }
    public string LimitRuleCode { get; set; } = "None";
    public string CalculationTraceJson { get; set; } = "{}";
}

public sealed class PayrollTaxLine
{
    public Guid Id { get; set; }
    public Guid PayrollRunEmployeeLineId { get; set; }
    public int Sequence { get; set; }
    public string ObligationCode { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty;
    public string JurisdictionName { get; set; } = string.Empty;
    public string TaxType { get; set; } = string.Empty;
    public decimal TaxableWages { get; set; }
    public decimal YearToDateTaxableWagesBefore { get; set; }
    public decimal EmployeeAmount { get; set; }
    public decimal EmployerAmount { get; set; }
    public Guid? TaxRuleSetId { get; set; }
    public Guid? TaxContentPackageId { get; set; }
    public string ContentVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string CalculationTraceJson { get; set; } = "{}";
}

public sealed class PayrollLiability
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid PayrollRunEmployeeLineId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceLineId { get; set; }
    public string ObligationCode { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty;
    public string JurisdictionName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LiabilityAccountNumber { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string Status { get; set; } = "Open";
    public DateOnly? DueDate { get; set; }
    public string DepositScheduleType { get; set; } = string.Empty;
    public string DepositRuleCode { get; set; } = string.Empty;
    public string DepositRuleSource { get; set; } = string.Empty;
    public Guid? DepositScheduleConfigurationId { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollLiabilityPayment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Payee { get; set; } = string.Empty;
    public string Method { get; set; } = "EFT";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Posted";
    public Guid JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateOnly? ReversalDate { get; set; }
    public string ReversalReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollLiabilityPaymentApplication
{
    public Guid Id { get; set; }
    public Guid PayrollLiabilityPaymentId { get; set; }
    public Guid PayrollLiabilityId { get; set; }
    public decimal Amount { get; set; }
}

public sealed class PayrollEmployeePayment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid PayrollRunEmployeeLineId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Method { get; set; } = "Check";
    public string Reference { get; set; } = string.Empty;
    public string BankRoutingNumber { get; set; } = string.Empty;
    public string BankAccountNumber { get; set; } = string.Empty;
    public string BankAccountType { get; set; } = string.Empty;
    public string DestinationLastFour { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal YearToDateGross { get; set; }
    public decimal YearToDateEmployeeTaxes { get; set; }
    public decimal YearToDateEmployeeDeductions { get; set; }
    public decimal YearToDateNetPay { get; set; }
    public string Status { get; set; } = "Issued";
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollBankOriginConfiguration
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public string ImmediateDestinationRoutingNumber { get; set; } = string.Empty;
    public string ImmediateOrigin { get; set; } = string.Empty;
    public string DestinationBankName { get; set; } = string.Empty;
    public string OriginName { get; set; } = string.Empty;
    public string CompanyIdentification { get; set; } = string.Empty;
    public string CompanyEntryDescription { get; set; } = "PAYROLL";
    public string OriginatingDfiIdentification { get; set; } = string.Empty;
    public DateOnly EffectiveOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBankValidated { get; set; }
    public string BankValidationNotes { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollPaymentFile
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid? PayrollBankOriginConfigurationId { get; set; }
    public string Format { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/plain";
    public string Content { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public string SourceDigestSha256 { get; set; } = string.Empty;
    public int EntryCount { get; set; }
    public decimal CreditTotal { get; set; }
    public long RoutingHash { get; set; }
    public string FileIdModifier { get; set; } = string.Empty;
    public string Status { get; set; } = "Generated";
    public string SpecificationVersion { get; set; } = string.Empty;
    public Guid? GeneratedByUserId { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public DateTimeOffset? VoidedAtUtc { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollFiling
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string FormCode { get; set; } = string.Empty;
    public int TaxYear { get; set; }
    public int? Quarter { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Status { get; set; } = "Draft";
    public string DataJson { get; set; } = "{}";
    public string SummaryJson { get; set; } = "{}";
    public string SourcePayrollRunIdsJson { get; set; } = "[]";
    public string SourceDigestSha256 { get; set; } = string.Empty;
    public string OfficialSourceUrl { get; set; } = string.Empty;
    public string ContentVersion { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string ApprovedDataJson { get; set; } = "{}";
    public string ApprovedSourceDigestSha256 { get; set; } = string.Empty;
    public DateTimeOffset? ApprovedBaselineAtUtc { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public DateTimeOffset? ReopenedAtUtc { get; set; }
    public string ReopenReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollFilingCorrection
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OriginalPayrollFilingId { get; set; }
    public int Sequence { get; set; }
    public string FormCode { get; set; } = "941-X";
    public int TaxYear { get; set; }
    public int Quarter { get; set; }
    public string Process { get; set; } = "Adjustment";
    public DateOnly DiscoveredOn { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string FederalWithholdingCorrectionType { get; set; } = "None";
    public string EmployeeCertificationCode { get; set; } = "UnderreportedOnly";
    public string EmployeeCertificationEvidenceReference { get; set; } = string.Empty;
    public bool WageStatementsCorrected { get; set; }
    public string WageStatementEvidenceReference { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string DataJson { get; set; } = "{}";
    public string CorrectedSourceDigestSha256 { get; set; } = string.Empty;
    public string OfficialSourceUrl { get; set; } = string.Empty;
    public string ContentVersion { get; set; } = string.Empty;
    public Guid? PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public DateTimeOffset? VoidedAtUtc { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollSsaWageFileConfiguration
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string FileKind { get; set; } = "EFW2C";
    public int SpecificationTaxYear { get; set; }
    public string SpecificationVersion { get; set; } = string.Empty;
    public string LayoutCompatibilityCode { get; set; } = string.Empty;
    public string OfficialSpecificationUrl { get; set; } = string.Empty;
    public string OfficialSpecificationSha256 { get; set; } = string.Empty;
    public DateOnly SourceRetrievedOn { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public string SubmitterEin { get; set; } = string.Empty;
    public string BsoUserId { get; set; } = string.Empty;
    public string SubmitterName { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string PreparerCode { get; set; } = "L";
    public string EmployerLocationAddress { get; set; } = string.Empty;
    public string EmployerDeliveryAddress { get; set; } = string.Empty;
    public string EmployerCity { get; set; } = string.Empty;
    public string EmployerState { get; set; } = string.Empty;
    public string EmployerPostalCode { get; set; } = string.Empty;
    public string EmployerContactName { get; set; } = string.Empty;
    public string EmployerContactPhone { get; set; } = string.Empty;
    public string EmployerContactEmail { get; set; } = string.Empty;
    public string KindOfEmployer { get; set; } = "N";
    public string EmploymentCode { get; set; } = "R";
    public string EmployerSignaturePin { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollSsaOriginalWageFile
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayrollFilingId { get; set; }
    public Guid PayrollSsaWageFileConfigurationId { get; set; }
    public int TaxYear { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public string SourceDigestSha256 { get; set; } = string.Empty;
    public string SpecificationVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "GeneratedForAccuWage";
    public int RecordCount { get; set; }
    public int EmployeeRecordCount { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public Guid? ValidatedByUserId { get; set; }
    public DateTimeOffset? ValidatedAtUtc { get; set; }
    public string AccuWageEvidenceReference { get; set; } = string.Empty;
    public string ValidationNotes { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollSsaWageFile
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayrollFilingCorrectionId { get; set; }
    public Guid PayrollSsaWageFileConfigurationId { get; set; }
    public int TaxYear { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public string SourceDigestSha256 { get; set; } = string.Empty;
    public string SpecificationVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "GeneratedForAccuWage";
    public int RecordCount { get; set; }
    public int EmployeeRecordCount { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public Guid? ValidatedByUserId { get; set; }
    public DateTimeOffset? ValidatedAtUtc { get; set; }
    public string AccuWageEvidenceReference { get; set; } = string.Empty;
    public string ValidationNotes { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class PayrollClosePeriod
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string PeriodType { get; set; } = "Quarter";
    public int TaxYear { get; set; }
    public int? Quarter { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Status { get; set; } = "Closed";
    public Guid? ClosedByUserId { get; set; }
    public DateTimeOffset ClosedAtUtc { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public DateTimeOffset? ReopenedAtUtc { get; set; }
    public string ReopenReason { get; set; } = string.Empty;
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class TaxRuleSet
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty;
    public string JurisdictionName { get; set; } = string.Empty;
    public string JurisdictionType { get; set; } = string.Empty;
    public string TaxType { get; set; } = string.Empty;
    public string CalculationMethod { get; set; } = string.Empty;
    public string WithholdingFrequency { get; set; } = string.Empty;
    public DateOnly EffectiveOn { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsEmployerSpecific { get; set; }
    public bool SupportsBracketTable { get; set; }
    public bool SupportsParameterEditing { get; set; }
    public bool IsActive { get; set; }
    public Guid? TaxContentPackageId { get; set; }
    public string ContentVersion { get; set; } = "1.0";
    public string MinimumEngineVersion { get; set; } = "1.0";
    public string ParentJurisdictionCode { get; set; } = string.Empty;
    public string ObligationCode { get; set; } = string.Empty;
    public string CalculationVariant { get; set; } = string.Empty;
    public string ExclusiveGroup { get; set; } = string.Empty;
    public int VariantPriority { get; set; }
    public string ApplicabilityJson { get; set; } = "{}";
}

public sealed class TaxContentPackage
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string PackageCode { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateOnly EffectiveOn { get; set; }
    public string Status { get; set; } = "Draft";
    public string MinimumEngineVersion { get; set; } = "1.0";
    public string ManifestJson { get; set; } = "{}";
    public string Source { get; set; } = string.Empty;
    public string ChangeSummary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
}

public sealed class TaxSourceCapture
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? TaxContentPackageId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public string RawContent { get; set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class TaxRuleFieldDefinition
{
    public Guid Id { get; set; }
    public Guid TaxRuleSetId { get; set; }
    public string FieldCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DataType { get; set; } = "text";
    public bool IsRequired { get; set; }
    public string DefaultValueJson { get; set; } = "null";
    public string ValidationJson { get; set; } = "{}";
    public int DisplayOrder { get; set; }
    public string HelpText { get; set; } = string.Empty;
}

public sealed class TaxRuleTestCase
{
    public Guid Id { get; set; }
    public Guid TaxRuleSetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
    public string ExpectedOutputJson { get; set; } = "{}";
    public bool IsRequiredForActivation { get; set; } = true;
}

public sealed class TaxRuleParameter
{
    public Guid Id { get; set; }
    public Guid TaxRuleSetId { get; set; }
    public string ParameterCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ValueType { get; set; } = string.Empty;
    public decimal? NumericValue { get; set; }
    public string TextValue { get; set; } = string.Empty;
    public bool? BooleanValue { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public sealed class TaxRuleBracket
{
    public Guid Id { get; set; }
    public Guid TaxRuleSetId { get; set; }
    public int Sequence { get; set; }
    public decimal UpperBoundAmount { get; set; }
    public decimal FixedAmount { get; set; }
    public decimal Rate { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class TaxFormRequirement
{
    public Guid Id { get; set; }
    public Guid TaxRuleSetId { get; set; }
    public string FormCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FilingFrequency { get; set; } = string.Empty;
    public string DeliveryChannel { get; set; } = string.Empty;
    public string DueRule { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class ReportCatalogItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string LayoutType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SupportsVisualStudioDesign { get; set; }
}

public sealed class LabelTemplate
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StockType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
