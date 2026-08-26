namespace BrassLedger.Domain.Accounting;

public static class AccountingAccountRoles
{
    public const string OperatingCash = "OperatingCash";
    public const string PayrollClearing = "PayrollClearing";
    public const string BankTransferClearing = "BankTransferClearing";
    public const string AccountsReceivable = "AccountsReceivable";
    public const string InventoryAsset = "InventoryAsset";
    public const string VendorAdvances = "VendorAdvances";
    public const string AccountsPayable = "AccountsPayable";
    public const string GoodsReceivedNotInvoiced = "GoodsReceivedNotInvoiced";
    public const string SalesTaxPayable = "SalesTaxPayable";
    public const string CustomerDeposits = "CustomerDeposits";
    public const string PayrollLiabilities = "PayrollLiabilities";
    public const string OwnerEquity = "OwnerEquity";
    public const string DefaultRevenue = "DefaultRevenue";
    public const string ForeignExchangeGain = "ForeignExchangeGain";
    public const string CostOfGoodsSold = "CostOfGoodsSold";
    public const string PayrollExpense = "PayrollExpense";
    public const string ForeignExchangeLoss = "ForeignExchangeLoss";

    public static IReadOnlyList<AccountingAccountRoleDefinition> Definitions { get; } =
    [
        new(OperatingCash, "Operating cash", "Default cash account used when a dedicated bank mapping is unavailable.", AccountType.Asset, false),
        new(PayrollClearing, "Payroll clearing", "Clearing account available to payroll funding workflows.", AccountType.Asset, false),
        new(BankTransferClearing, "Bank-transfer clearing", "Clearing account used for the two sides of an internal bank transfer.", AccountType.Asset, false, true),
        new(AccountsReceivable, "Accounts receivable", "Control account used by customer invoices, credits, and payment applications.", AccountType.Asset, true, true),
        new(InventoryAsset, "Inventory asset", "Control account used for inventory value and quantity adjustments.", AccountType.Asset, true, true),
        new(VendorAdvances, "Vendor advances", "Control account used for unapplied vendor payments and refunds.", AccountType.Asset, true, true),
        new(AccountsPayable, "Accounts payable", "Control account used by vendor bills, credits, and payment applications.", AccountType.Liability, true, true),
        new(GoodsReceivedNotInvoiced, "Goods received not invoiced", "Control account used for received inventory awaiting a matched vendor invoice.", AccountType.Liability, true, true),
        new(SalesTaxPayable, "Sales-tax payable", "Control account used for sales-tax liabilities.", AccountType.Liability, true, true),
        new(CustomerDeposits, "Customer deposits", "Control account used for unapplied customer receipts and refunds.", AccountType.Liability, true, true),
        new(PayrollLiabilities, "Payroll liabilities", "Control account used for employee deductions and employer payroll obligations.", AccountType.Liability, true, true),
        new(OwnerEquity, "Owner equity", "Default equity account for opening-balance and ownership workflows.", AccountType.Equity, false),
        new(DefaultRevenue, "Default revenue", "Default revenue account used by reviewed invoice imports when a line has no explicit account.", AccountType.Revenue, false),
        new(ForeignExchangeGain, "Foreign-exchange gain", "Revenue account used for realized and unrealized currency gains.", AccountType.Revenue, false),
        new(CostOfGoodsSold, "Cost of goods sold", "Expense account used for inventory cost and adjustment offsets.", AccountType.Expense, false),
        new(PayrollExpense, "Payroll expense", "Expense account used for gross payroll, employer taxes, and employer contributions.", AccountType.Expense, false),
        new(ForeignExchangeLoss, "Foreign-exchange loss", "Expense account used for realized and unrealized currency losses.", AccountType.Expense, false)
    ];

    public static AccountingAccountRoleDefinition? Find(string? code) =>
        Definitions.FirstOrDefault(definition => string.Equals(definition.Code, code?.Trim(), StringComparison.Ordinal));
}

public sealed record AccountingAccountRoleDefinition(
    string Code,
    string Name,
    string Description,
    AccountType RequiredAccountType,
    bool RequiresControlAccount,
    bool RequiresZeroBalanceToReassign = false);
