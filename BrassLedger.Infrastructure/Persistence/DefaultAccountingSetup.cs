using BrassLedger.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Persistence;

internal static class DefaultAccountingSetup
{
    public static IReadOnlyList<GeneralLedgerAccount> CreateAccounts(Guid companyId) =>
    [
        Account(companyId, "1000", "Operating Cash", AccountType.Asset, role: AccountingAccountRoles.OperatingCash),
        Account(companyId, "1010", "Payroll Clearing", AccountType.Asset, role: AccountingAccountRoles.PayrollClearing),
        Account(companyId, "1050", "Bank Transfer Clearing", AccountType.Asset, role: AccountingAccountRoles.BankTransferClearing),
        Account(companyId, "1100", "Accounts Receivable", AccountType.Asset, true, AccountingAccountRoles.AccountsReceivable),
        Account(companyId, "1110", "Retainage Receivable", AccountType.Asset, true, AccountingAccountRoles.RetainageReceivable),
        Account(companyId, "1200", "Inventory Asset", AccountType.Asset, true, AccountingAccountRoles.InventoryAsset),
        Account(companyId, "1300", "Vendor Advances", AccountType.Asset, true, AccountingAccountRoles.VendorAdvances),
        Account(companyId, "1400", "Prepaid Expenses", AccountType.Asset),
        Account(companyId, "1500", "Fixed Assets", AccountType.Asset),
        Account(companyId, "1590", "Accumulated Depreciation", AccountType.Asset),
        Account(companyId, "2000", "Accounts Payable", AccountType.Liability, true, AccountingAccountRoles.AccountsPayable),
        Account(companyId, "2050", "Goods Received Not Invoiced", AccountType.Liability, true, AccountingAccountRoles.GoodsReceivedNotInvoiced),
        Account(companyId, "2100", "Sales Tax Payable", AccountType.Liability, true, AccountingAccountRoles.SalesTaxPayable),
        Account(companyId, "2150", "Customer Deposits", AccountType.Liability, true, AccountingAccountRoles.CustomerDeposits),
        Account(companyId, "2200", "Payroll Liabilities", AccountType.Liability, true, AccountingAccountRoles.PayrollLiabilities),
        Account(companyId, "2500", "Loans Payable", AccountType.Liability),
        Account(companyId, "3000", "Owner Equity", AccountType.Equity, role: AccountingAccountRoles.OwnerEquity),
        Account(companyId, "4000", "Product Revenue", AccountType.Revenue, role: AccountingAccountRoles.DefaultRevenue),
        Account(companyId, "4300", "Foreign Exchange Gain", AccountType.Revenue, role: AccountingAccountRoles.ForeignExchangeGain),
        Account(companyId, "4400", "Gain on Asset Disposal", AccountType.Revenue),
        Account(companyId, "5100", "Cost of Goods Sold", AccountType.Expense, role: AccountingAccountRoles.CostOfGoodsSold),
        Account(companyId, "5200", "Purchase Price Variance", AccountType.Expense, role: AccountingAccountRoles.PurchasePriceVariance),
        Account(companyId, "6100", "Payroll Expense", AccountType.Expense, role: AccountingAccountRoles.PayrollExpense),
        Account(companyId, "6200", "Depreciation Expense", AccountType.Expense),
        Account(companyId, "6250", "Interest Expense", AccountType.Expense),
        Account(companyId, "6400", "Prepaid Amortization Expense", AccountType.Expense),
        Account(companyId, "6500", "Loss on Asset Disposal", AccountType.Expense),
        Account(companyId, "6300", "Foreign Exchange Loss", AccountType.Expense, role: AccountingAccountRoles.ForeignExchangeLoss)
    ];

    public static BankAccount CreateOperatingBankAccount(Guid companyId, Guid ledgerAccountId) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        Name = "Operating Account",
        AccountNumberMasked = "Not connected",
        LedgerAccountId = ledgerAccountId,
        CurrentBalance = 0m,
        UnreconciledAmount = 0m,
        LastReconciledOn = DateOnly.FromDateTime(DateTime.UtcNow),
        LastReconciledBalance = 0m
    };

    public static async Task EnsureMinimumSetupAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        foreach (var companyId in await dbContext.Companies.Select(company => company.Id).ToListAsync(cancellationToken))
        {
            if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId, cancellationToken))
            {
                var accounts = CreateAccounts(companyId);
                await dbContext.Accounts.AddRangeAsync(accounts, cancellationToken);
                await dbContext.BankAccounts.AddAsync(CreateOperatingBankAccount(companyId, accounts.Single(account => account.OperationalRole == AccountingAccountRoles.OperatingCash).Id), cancellationToken);
                continue;
            }
            else
            {
                await EnsureRoleAccountAsync(dbContext, companyId, "2100", "Sales Tax Payable", AccountType.Liability, true, AccountingAccountRoles.SalesTaxPayable, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "2200", "Payroll Liabilities", AccountType.Liability, true, AccountingAccountRoles.PayrollLiabilities, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "2050", "Goods Received Not Invoiced", AccountType.Liability, true, AccountingAccountRoles.GoodsReceivedNotInvoiced, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "1300", "Vendor Advances", AccountType.Asset, true, AccountingAccountRoles.VendorAdvances, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "1110", "Retainage Receivable", AccountType.Asset, true, AccountingAccountRoles.RetainageReceivable, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "1050", "Bank Transfer Clearing", AccountType.Asset, false, AccountingAccountRoles.BankTransferClearing, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "2150", "Customer Deposits", AccountType.Liability, true, AccountingAccountRoles.CustomerDeposits, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "4300", "Foreign Exchange Gain", AccountType.Revenue, false, AccountingAccountRoles.ForeignExchangeGain, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "5200", "Purchase Price Variance", AccountType.Expense, false, AccountingAccountRoles.PurchasePriceVariance, cancellationToken);
                await EnsureRoleAccountAsync(dbContext, companyId, "6300", "Foreign Exchange Loss", AccountType.Expense, false, AccountingAccountRoles.ForeignExchangeLoss, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "1400", "Prepaid Expenses", AccountType.Asset, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "1500", "Fixed Assets", AccountType.Asset, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "1590", "Accumulated Depreciation", AccountType.Asset, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "2500", "Loans Payable", AccountType.Liability, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "6200", "Depreciation Expense", AccountType.Expense, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "6250", "Interest Expense", AccountType.Expense, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "6400", "Prepaid Amortization Expense", AccountType.Expense, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "4400", "Gain on Asset Disposal", AccountType.Revenue, cancellationToken);
                await EnsureAccountAsync(dbContext, companyId, "6500", "Loss on Asset Disposal", AccountType.Expense, cancellationToken);
            }

            var operatingCash = await EnsureOperationalRolesAsync(dbContext, companyId, cancellationToken);
            var bankAccounts = await dbContext.BankAccounts.Where(account => account.CompanyId == companyId).ToListAsync(cancellationToken);
            if (bankAccounts.Count == 0 && operatingCash is not null)
                await dbContext.BankAccounts.AddAsync(CreateOperatingBankAccount(companyId, operatingCash.Id), cancellationToken);
            else if (operatingCash is not null)
                foreach (var bankAccount in bankAccounts.Where(account => account.LedgerAccountId == Guid.Empty))
                    bankAccount.LedgerAccountId = operatingCash.Id;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<GeneralLedgerAccount?> EnsureOperationalRolesAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        var defaults = CreateAccounts(companyId).ToDictionary(account => account.Number, account => account.OperationalRole, StringComparer.OrdinalIgnoreCase);
        var accounts = await dbContext.Accounts.Where(account => account.CompanyId == companyId).ToListAsync(cancellationToken);
        foreach (var account in accounts.Where(account => string.IsNullOrWhiteSpace(account.OperationalRole) && defaults.ContainsKey(account.Number)))
        {
            var role = defaults[account.Number];
            var definition = AccountingAccountRoles.Find(role);
            if (definition is not null
                && account.Type == definition.RequiredAccountType
                && account.IsControlAccount == definition.RequiresControlAccount
                && !accounts.Any(candidate => string.Equals(candidate.OperationalRole, role, StringComparison.Ordinal))) account.OperationalRole = role;
        }

        return accounts.SingleOrDefault(account => account.IsActive && account.OperationalRole == AccountingAccountRoles.OperatingCash);
    }

    private static async Task EnsureRoleAccountAsync(BrassLedgerDbContext dbContext, Guid companyId, string number, string name, AccountType type, bool isControlAccount, string role, CancellationToken cancellationToken)
    {
        if (await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && (account.OperationalRole == role || account.Number == number), cancellationToken)) return;
        await dbContext.Accounts.AddAsync(Account(companyId, number, name, type, isControlAccount, role), cancellationToken);
    }

    private static async Task EnsureAccountAsync(BrassLedgerDbContext dbContext, Guid companyId, string number, string name, AccountType type, CancellationToken cancellationToken)
    {
        if (await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && account.Number == number, cancellationToken)) return;
        await dbContext.Accounts.AddAsync(Account(companyId, number, name, type), cancellationToken);
    }

    private static GeneralLedgerAccount Account(Guid companyId, string number, string name, AccountType type, bool isControlAccount = false, string? role = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        Number = number,
        Name = name,
        Type = type,
        IsControlAccount = isControlAccount,
        IsActive = true,
        OperationalRole = role
    };
}
