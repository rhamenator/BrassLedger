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
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int FailedSignInCount { get; set; }
    public DateTimeOffset? LastFailedSignInUtc { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public DateTimeOffset? LastSuccessfulSignInUtc { get; set; }
    public DateTimeOffset? LastPasswordChangedUtc { get; set; }
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
}

public sealed class InventoryTransaction
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InventoryItemId { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal QuantityChange { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid JournalEntryId { get; set; }
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
    public bool IsPosted { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset PostedAtUtc { get; set; }
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
    public Guid? ReconciledByUserId { get; set; }
    public DateTimeOffset ReconciledAtUtc { get; set; }
}

public sealed class BankReconciliationItem
{
    public Guid Id { get; set; }
    public Guid BankReconciliationId { get; set; }
    public Guid JournalEntryId { get; set; }
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
    public string ConcurrencyToken { get; set; } = string.Empty;
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
    public string ConcurrencyToken { get; set; } = string.Empty;
}

public sealed class InventoryItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal ReorderPoint { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SalesOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateOnly OrderedOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

public sealed class PurchaseOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid VendorId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateOnly OrderedOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
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
    public string PayType { get; set; } = string.Empty;
    public decimal MonthlyBasePay { get; set; }
    public string FilingStatus { get; set; } = "Single";
    public string PayrollFrequency { get; set; } = "Biweekly";
    public int Allowances { get; set; }
    public decimal AdditionalWithholding { get; set; }
    public decimal PreTaxBenefitDeductions { get; set; }
    public decimal PostTaxBenefitDeductions { get; set; }
    public bool IsActive { get; set; }
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
}

public sealed class PayrollRun
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public DateOnly PayDate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public decimal GrossPayroll { get; set; }
    public decimal PreTaxDeductions { get; set; }
    public decimal EmployeeWithholdings { get; set; }
    public decimal PostTaxDeductions { get; set; }
    public decimal EmployerPayrollTaxes { get; set; }
    public decimal NetPay { get; set; }
    public DateTimeOffset PostedAtUtc { get; set; }
    public string TaxContentSnapshotJson { get; set; } = "[]";
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
    public decimal PreTaxDeductions { get; set; }
    public decimal EmployeeWithholdings { get; set; }
    public decimal PostTaxDeductions { get; set; }
    public decimal EmployerPayrollTaxes { get; set; }
    public decimal NetPay { get; set; }
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
