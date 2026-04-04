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
    public string EntryNumber { get; set; } = string.Empty;
    public DateOnly PostedOn { get; set; }
    public string SourceModule { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public bool IsPosted { get; set; }
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
    public decimal CurrentBalance { get; set; }
    public decimal UnreconciledAmount { get; set; }
    public DateOnly LastReconciledOn { get; set; }
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
    public string PayType { get; set; } = string.Empty;
    public decimal MonthlyBasePay { get; set; }
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
    public DateOnly EffectiveOn { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsEmployerSpecific { get; set; }
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
