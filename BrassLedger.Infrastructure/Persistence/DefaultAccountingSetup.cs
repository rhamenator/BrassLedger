using BrassLedger.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Persistence;

internal static class DefaultAccountingSetup
{
    public static IReadOnlyList<GeneralLedgerAccount> CreateAccounts(Guid companyId) =>
    [
        Account(companyId, "1000", "Operating Cash", AccountType.Asset),
        Account(companyId, "1010", "Payroll Clearing", AccountType.Asset),
        Account(companyId, "1050", "Bank Transfer Clearing", AccountType.Asset),
        Account(companyId, "1100", "Accounts Receivable", AccountType.Asset, true),
        Account(companyId, "1200", "Inventory Asset", AccountType.Asset, true),
        Account(companyId, "1300", "Vendor Advances", AccountType.Asset, true),
        Account(companyId, "2000", "Accounts Payable", AccountType.Liability, true),
        Account(companyId, "2100", "Sales Tax Payable", AccountType.Liability, true),
        Account(companyId, "2150", "Customer Deposits", AccountType.Liability, true),
        Account(companyId, "2200", "Payroll Liabilities", AccountType.Liability, true),
        Account(companyId, "3000", "Owner Equity", AccountType.Equity),
        Account(companyId, "4000", "Product Revenue", AccountType.Revenue),
        Account(companyId, "4300", "Foreign Exchange Gain", AccountType.Revenue),
        Account(companyId, "5100", "Cost of Goods Sold", AccountType.Expense),
        Account(companyId, "6100", "Payroll Expense", AccountType.Expense),
        Account(companyId, "6300", "Foreign Exchange Loss", AccountType.Expense)
    ];

    public static BankAccount CreateOperatingBankAccount(Guid companyId, Guid ledgerAccountId) => new()
    {
        Id = Guid.NewGuid(), CompanyId = companyId, Name = "Operating Account", AccountNumberMasked = "Not connected",
        LedgerAccountId = ledgerAccountId, CurrentBalance = 0m, UnreconciledAmount = 0m, LastReconciledOn = DateOnly.FromDateTime(DateTime.UtcNow), LastReconciledBalance = 0m
    };

    public static async Task EnsureMinimumSetupAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        foreach (var companyId in await dbContext.Companies.Select(company => company.Id).ToListAsync(cancellationToken))
        {
            if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId, cancellationToken))
            {
                var accounts = CreateAccounts(companyId);
                await dbContext.Accounts.AddRangeAsync(accounts, cancellationToken);
                await dbContext.BankAccounts.AddAsync(CreateOperatingBankAccount(companyId, accounts.Single(account => account.Number == "1000").Id), cancellationToken);
                continue;
            }
            else
            {
                if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && account.Number == "2100", cancellationToken))
                    await dbContext.Accounts.AddAsync(Account(companyId, "2100", "Sales Tax Payable", AccountType.Liability, true), cancellationToken);
                if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && account.Number == "2200", cancellationToken))
                    await dbContext.Accounts.AddAsync(Account(companyId, "2200", "Payroll Liabilities", AccountType.Liability, true), cancellationToken);
                if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && account.Number == "1300", cancellationToken))
                    await dbContext.Accounts.AddAsync(Account(companyId, "1300", "Vendor Advances", AccountType.Asset, true), cancellationToken);
                if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && account.Number == "1050", cancellationToken))
                    await dbContext.Accounts.AddAsync(Account(companyId, "1050", "Bank Transfer Clearing", AccountType.Asset), cancellationToken);
                if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && account.Number == "2150", cancellationToken))
                    await dbContext.Accounts.AddAsync(Account(companyId, "2150", "Customer Deposits", AccountType.Liability, true), cancellationToken);
                if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && account.Number == "4300", cancellationToken))
                    await dbContext.Accounts.AddAsync(Account(companyId, "4300", "Foreign Exchange Gain", AccountType.Revenue), cancellationToken);
                if (!await dbContext.Accounts.AnyAsync(account => account.CompanyId == companyId && account.Number == "6300", cancellationToken))
                    await dbContext.Accounts.AddAsync(Account(companyId, "6300", "Foreign Exchange Loss", AccountType.Expense), cancellationToken);
            }

            var operatingCash = await dbContext.Accounts.SingleAsync(account => account.CompanyId == companyId && account.Number == "1000", cancellationToken);
            var bankAccounts = await dbContext.BankAccounts.Where(account => account.CompanyId == companyId).ToListAsync(cancellationToken);
            if (bankAccounts.Count == 0)
                await dbContext.BankAccounts.AddAsync(CreateOperatingBankAccount(companyId, operatingCash.Id), cancellationToken);
            else
                foreach (var bankAccount in bankAccounts.Where(account => account.LedgerAccountId == Guid.Empty))
                    bankAccount.LedgerAccountId = operatingCash.Id;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static GeneralLedgerAccount Account(Guid companyId, string number, string name, AccountType type, bool isControlAccount = false) => new()
    {
        Id = Guid.NewGuid(), CompanyId = companyId, Number = number, Name = name, Type = type, IsControlAccount = isControlAccount, IsActive = true
    };
}
