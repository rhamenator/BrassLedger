using BrassLedger.Application.Accounting;
using BrassLedger.Application.Catalog;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class BusinessWorkspaceService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IProductCatalogService assessmentService,
    IHttpContextAccessor httpContextAccessor) : IBusinessWorkspaceService
{
    public async Task<BusinessWorkspaceSnapshot> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var claimValue = httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        var companies = dbContext.Companies.AsNoTracking();
        var company = Guid.TryParse(claimValue, out var companyId)
            ? await companies.SingleAsync(x => x.Id == companyId, cancellationToken)
            : await companies.OrderBy(x => x.Name).FirstAsync(cancellationToken);
        var users = await dbContext.Users.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).ToListAsync(cancellationToken);
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).OrderBy(x => x.Number).ToListAsync(cancellationToken);
        var journalEntries = await dbContext.JournalEntries.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsPosted).OrderByDescending(x => x.PostedOn).Take(8).ToListAsync(cancellationToken);
        var customers = await dbContext.Customers.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.CustomerNumber).ToListAsync(cancellationToken);
        var invoices = await dbContext.SalesInvoices.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.InvoiceDate).ToListAsync(cancellationToken);
        var vendors = await dbContext.Vendors.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.VendorNumber).ToListAsync(cancellationToken);
        var vendorBills = await dbContext.VendorBills.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.DueDate).ToListAsync(cancellationToken);
        var inventoryItems = await dbContext.InventoryItems.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).OrderBy(x => x.Sku).ToListAsync(cancellationToken);
        var salesOrders = await dbContext.SalesOrders.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.OrderedOn).ToListAsync(cancellationToken);
        var purchaseOrders = await dbContext.PurchaseOrders.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.OrderedOn).ToListAsync(cancellationToken);
        var bankAccounts = await dbContext.BankAccounts.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var employees = await dbContext.Employees.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.EmployeeNumber).ToListAsync(cancellationToken);
        var projectJobs = await dbContext.ProjectJobs.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.JobNumber).ToListAsync(cancellationToken);
        var taxProfiles = await dbContext.TaxProfiles.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Jurisdiction).ThenBy(x => x.TaxType).ToListAsync(cancellationToken);
        var reports = await dbContext.ReportCatalogItems.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        var labels = await dbContext.LabelTemplates.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Name).ToListAsync(cancellationToken);

        var customerNames = customers.ToDictionary(x => x.Id, x => x.Name);
        var vendorNames = vendors.ToDictionary(x => x.Id, x => x.Name);

        var moduleCounts = BuildModuleCounts(
            accounts.Count + journalEntries.Count,
            customers.Count + invoices.Count,
            vendors.Count + vendorBills.Count,
            employees.Count + taxProfiles.Count,
            inventoryItems.Count,
            salesOrders.Count,
            purchaseOrders.Count,
            bankAccounts.Count,
            projectJobs.Count);

        var assessment = assessmentService.GetCatalog();

        return new BusinessWorkspaceSnapshot(
            GeneratedAtUtc: DateTime.UtcNow,
            Company: new CompanySnapshot(
                Name: company.Name,
                LegalName: company.LegalName,
                TaxId: MaskTaxId(company.TaxId),
                BaseCurrency: company.BaseCurrency,
                FiscalYearStartMonth: company.FiscalYearStartMonth,
                ActiveUsers: users.Count),
            Dashboard: new DashboardSnapshot(
                CashOnHand: bankAccounts.Sum(x => x.CurrentBalance),
                ReceivablesOpen: invoices.Sum(x => x.BalanceDue),
                PayablesOpen: vendorBills.Sum(x => x.BalanceDue),
                MonthlyPayroll: employees.Where(x => x.IsActive).Sum(x => x.MonthlyBasePay),
                InventoryItems: inventoryItems.Count,
                OpenSalesOrders: salesOrders.Count(x => x.Status is "Open" or "Picking" or "Allocated"),
                OpenProjects: projectJobs.Count(x => x.Status is "Open" or "Billing"),
                EnabledModules: assessment.Modules.Count,
                ReportsReady: reports.Count + labels.Count),
            Modules: assessment.Modules
                .Select(module =>
                {
                    var details = moduleCounts.GetValueOrDefault(module.Code);
                    return new ModuleWorkspaceSnapshot(
                        module.Code,
                        module.Name,
                        module.Area,
                        string.IsNullOrWhiteSpace(details.Status) ? "Planned" : details.Status,
                        string.IsNullOrWhiteSpace(details.Summary) ? "Modeled in the open-source roadmap." : details.Summary,
                        details.RecordCount);
                })
                .ToArray(),
            GeneralLedger: new GeneralLedgerWorkspace(
                Assets: SumByType(accounts, AccountType.Asset),
                Liabilities: SumByType(accounts, AccountType.Liability),
                Equity: SumByType(accounts, AccountType.Equity),
                Revenue: SumByType(accounts, AccountType.Revenue),
                Expenses: SumByType(accounts, AccountType.Expense),
                Accounts: accounts.Select(x => new AccountSnapshot(x.Number, x.Name, x.Type.ToString(), x.CurrentBalance, x.IsControlAccount)).ToArray(),
                RecentEntries: journalEntries.Select(x => new JournalEntrySnapshot(x.EntryNumber, x.PostedOn, x.SourceModule, x.Description, x.TotalAmount)).ToArray()),
            Receivables: new ReceivablesWorkspace(
                OpenBalance: invoices.Sum(x => x.BalanceDue),
                PastDueCount: invoices.Count(x => x.DueDate < DateOnly.FromDateTime(DateTime.Today) && x.BalanceDue > 0m),
                Customers: customers.Select(x => new CustomerSnapshot(x.CustomerNumber, x.Name, x.State, x.CreditLimit, x.OpenBalance)).ToArray(),
                Invoices: invoices.Select(x => new InvoiceSnapshot(
                    x.InvoiceNumber,
                    customerNames.GetValueOrDefault(x.CustomerId, "Unknown customer"),
                    x.InvoiceDate,
                    x.DueDate,
                    x.Status,
                    x.TotalAmount,
                    x.BalanceDue)).ToArray()),
            Payables: new PayablesWorkspace(
                OpenBalance: vendorBills.Sum(x => x.BalanceDue),
                DueThisWeekCount: vendorBills.Count(x => x.DueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(7)) && x.BalanceDue > 0m),
                Vendors: vendors.Select(x => new VendorSnapshot(x.VendorNumber, x.Name, x.State, x.PaymentTerms, x.OpenBalance)).ToArray(),
                Bills: vendorBills.Select(x => new BillSnapshot(
                    x.BillNumber,
                    vendorNames.GetValueOrDefault(x.VendorId, "Unknown vendor"),
                    x.BillDate,
                    x.DueDate,
                    x.Status,
                    x.TotalAmount,
                    x.BalanceDue)).ToArray()),
            Operations: new OperationsWorkspace(
                InventoryItemCount: inventoryItems.Count,
                ReorderAlerts: inventoryItems.Count(x => x.QuantityOnHand <= x.ReorderPoint),
                OpenSalesOrderCount: salesOrders.Count(x => x.Status is "Open" or "Picking" or "Allocated"),
                OpenPurchaseOrderCount: purchaseOrders.Count(x => x.Status is "Issued" or "Approved"),
                InventoryItems: inventoryItems.Select(x => new InventoryItemSnapshot(x.Sku, x.Description, x.UnitPrice, x.QuantityOnHand, x.ReorderPoint)).ToArray(),
                SalesOrders: salesOrders.Select(x => new SalesOrderSnapshot(
                    x.OrderNumber,
                    customerNames.GetValueOrDefault(x.CustomerId, "Unknown customer"),
                    x.OrderedOn,
                    x.Status,
                    x.TotalAmount)).ToArray(),
                PurchaseOrders: purchaseOrders.Select(x => new PurchaseOrderSnapshot(
                    x.OrderNumber,
                    vendorNames.GetValueOrDefault(x.VendorId, "Unknown vendor"),
                    x.OrderedOn,
                    x.Status,
                    x.TotalAmount)).ToArray()),
            Treasury: new TreasuryWorkspace(
                CashOnHand: bankAccounts.Sum(x => x.CurrentBalance),
                UnreconciledBalance: bankAccounts.Sum(x => x.UnreconciledAmount),
                BankAccounts: bankAccounts.Select(x => new BankAccountSnapshot(x.Name, x.AccountNumberMasked, x.CurrentBalance, x.UnreconciledAmount, x.LastReconciledOn)).ToArray()),
            Payroll: new PayrollWorkspace(
                ActiveEmployees: employees.Count(x => x.IsActive),
                MonthlyGross: employees.Where(x => x.IsActive).Sum(x => x.MonthlyBasePay),
                Employees: employees.Select(x => new EmployeeSnapshot(
                    x.EmployeeNumber,
                    $"{x.FirstName} {x.LastName}",
                    x.Department,
                    x.State,
                    x.PayType,
                    x.MonthlyBasePay,
                    x.IsActive)).ToArray()),
            Projects: new ProjectsWorkspace(
                OpenJobs: projectJobs.Count(x => x.Status is "Open" or "Billing"),
                BudgetAmount: projectJobs.Sum(x => x.BudgetAmount),
                ActualCost: projectJobs.Sum(x => x.ActualCost),
                Jobs: projectJobs.Select(x => new ProjectJobSnapshot(x.JobNumber, x.Name, x.CustomerName, x.Status, x.BudgetAmount, x.ActualCost)).ToArray()),
            Reporting: new ReportingWorkspace(
                ReportCount: reports.Count,
                LabelCount: labels.Count,
                PreferredDesigner: "Visual Studio RDL/RDLC",
                RenderingStrategy: "Use RDLC-authored operational reports plus server-side PDF exports for dashboards and special forms.",
                Reports: reports.Select(x => new ReportCatalogSnapshot(x.Code, x.Name, x.Category, x.LayoutType, x.Description, x.SupportsVisualStudioDesign)).ToArray(),
                Labels: labels.Select(x => new LabelTemplateSnapshot(x.Code, x.Name, x.StockType, x.Description)).ToArray()),
            Taxes: new TaxWorkspace(
                ProfileCount: taxProfiles.Count,
                EmployerSpecificCount: taxProfiles.Count(x => x.IsEmployerSpecific),
                Profiles: taxProfiles.Select(x => new TaxProfileSnapshot(x.Jurisdiction, x.TaxType, x.Rate, x.EffectiveOn, x.Source, x.IsEmployerSpecific)).ToArray()));
    }

    private static Dictionary<string, (string Status, string Summary, int RecordCount)> BuildModuleCounts(
        int ledgerCount,
        int receivablesCount,
        int payablesCount,
        int payrollCount,
        int inventoryCount,
        int orderCount,
        int purchaseCount,
        int bankCount,
        int projectCount)
    {
        return new Dictionary<string, (string Status, string Summary, int RecordCount)>
        {
            ["J"] = ("Live foundation", "Chart of accounts, journal history, and balances are seeded in the shared workspace.", ledgerCount),
            ["F"] = ("Live foundation", "Customers and invoices are active with aging-ready open balances.", receivablesCount),
            ["E"] = ("Live foundation", "Vendor master data and open bills are wired for payables workflows.", payablesCount),
            ["Q"] = ("Live foundation", "Employees, payroll cost, and tax profiles are in the operational data model.", payrollCount),
            ["K"] = ("Live foundation", "Item master, reorder thresholds, and stock counts are available now.", inventoryCount),
            ["O"] = ("Live foundation", "Sales orders feed the operational pipeline and reporting surface.", orderCount),
            ["S"] = ("Live foundation", "Purchase orders are present and aligned to vendor workflows.", purchaseCount),
            ["P"] = ("Modeled foundation", "Point-of-sale can ride the order pipeline while dedicated ticket workflows are added.", orderCount),
            ["G"] = ("Live foundation", "Bank balances and reconciliation deltas are available in treasury.", bankCount),
            ["U"] = ("Modeled foundation", "Zero-balance behavior will build on the banking model rather than legacy switches.", bankCount),
            ["L"] = ("Live foundation", "Jobs carry budget, cost, and customer linkage for project accounting.", projectCount),
            ["B"] = ("Modeled foundation", "Property workflows can extend the job ledger without module gating.", projectCount),
            ["T"] = ("Modeled foundation", "Time capture will attach to employees and jobs in the same payroll model.", payrollCount),
            ["I"] = ("Live foundation", "CRM data is represented through the live customer workspace.", receivablesCount)
        };
    }

    private static decimal SumByType(IEnumerable<GeneralLedgerAccount> accounts, AccountType accountType)
    {
        return accounts.Where(x => x.Type == accountType).Sum(x => x.CurrentBalance);
    }

    private static string MaskTaxId(string taxId)
    {
        var digits = new string(taxId.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4)
        {
            return $"***-**-{digits[^4..]}";
        }

        return "***";
    }
}

