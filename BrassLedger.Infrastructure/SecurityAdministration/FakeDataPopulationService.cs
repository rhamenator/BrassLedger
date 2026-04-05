using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.SecurityAdministration;

public interface IFakeDataPopulationService
{
    Task<FakeDataPopulationResult> PopulateAsync(CancellationToken cancellationToken = default);
}

public sealed record FakeDataPopulationResult(
    int CustomersAdded,
    int InvoicesAdded,
    int VendorsAdded,
    int BillsAdded,
    int ItemsAdded,
    int EmployeesAdded,
    int OrdersAdded,
    int PurchaseOrdersAdded,
    int ProjectsAdded,
    int JournalEntriesAdded,
    int UsersAdded);

public sealed class FakeDataPopulationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IPasswordHasher<AppUser> passwordHasher) : IFakeDataPopulationService
{
    public async Task<FakeDataPopulationResult> PopulateAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var company = await dbContext.Companies.OrderBy(candidate => candidate.Name).FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            throw new InvalidOperationException("Run first-time setup before loading fake data.");
        }

        await SecurityAdministrationService.EnsureBuiltInRolesAsync(dbContext, company.Id, cancellationToken);

        var customersAdded = await AddCustomersAsync(dbContext, company.Id, cancellationToken);
        var invoicesAdded = await AddInvoicesAsync(dbContext, company.Id, cancellationToken);
        var vendorsAdded = await AddVendorsAsync(dbContext, company.Id, cancellationToken);
        var billsAdded = await AddBillsAsync(dbContext, company.Id, cancellationToken);
        var itemsAdded = await AddInventoryAsync(dbContext, company.Id, cancellationToken);
        var ordersAdded = await AddSalesOrdersAsync(dbContext, company.Id, cancellationToken);
        var purchaseOrdersAdded = await AddPurchaseOrdersAsync(dbContext, company.Id, cancellationToken);
        var employeesAdded = await AddEmployeesAsync(dbContext, company.Id, cancellationToken);
        var projectsAdded = await AddProjectsAsync(dbContext, company.Id, cancellationToken);
        var journalEntriesAdded = await AddJournalEntriesAsync(dbContext, company.Id, cancellationToken);
        var usersAdded = await AddOperatorsAsync(dbContext, company.Id, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new FakeDataPopulationResult(
            customersAdded,
            invoicesAdded,
            vendorsAdded,
            billsAdded,
            itemsAdded,
            employeesAdded,
            ordersAdded,
            purchaseOrdersAdded,
            projectsAdded,
            journalEntriesAdded,
            usersAdded);
    }

    private async Task<int> AddCustomersAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.Customers.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var customers = new[]
        {
            new Customer { Id = Guid.NewGuid(), CompanyId = companyId, CustomerNumber = "C-2101", Name = "Harbor View Contractors", Email = "ap@harborview.example", State = "WA", CreditLimit = 85000m, OpenBalance = 21440.50m },
            new Customer { Id = Guid.NewGuid(), CompanyId = companyId, CustomerNumber = "C-2102", Name = "Summit Medical Supply", Email = "finance@summitmedical.example", State = "CO", CreditLimit = 120000m, OpenBalance = 11875m },
            new Customer { Id = Guid.NewGuid(), CompanyId = companyId, CustomerNumber = "C-2103", Name = "Blue Canyon Transit", Email = "billing@bluecanyon.example", State = "NM", CreditLimit = 64000m, OpenBalance = 9325.15m }
        };

        await dbContext.Customers.AddRangeAsync(customers, cancellationToken);
        return customers.Length;
    }

    private async Task<int> AddInvoicesAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.SalesInvoices.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var customers = await dbContext.Customers.Where(candidate => candidate.CompanyId == companyId).OrderBy(candidate => candidate.CustomerNumber).ToListAsync(cancellationToken);
        if (customers.Count == 0)
        {
            return 0;
        }

        var invoices = new[]
        {
            new SalesInvoice { Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = customers[0].Id, InvoiceNumber = "INV-30101", InvoiceDate = new DateOnly(2026, 4, 1), DueDate = new DateOnly(2026, 5, 1), Status = "Open", Subtotal = 18950m, TaxAmount = 1137m, TotalAmount = 20087m, BalanceDue = 20087m },
            new SalesInvoice { Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = customers[Math.Min(1, customers.Count - 1)].Id, InvoiceNumber = "INV-30104", InvoiceDate = new DateOnly(2026, 4, 3), DueDate = new DateOnly(2026, 5, 3), Status = "Partial", Subtotal = 11620m, TaxAmount = 0m, TotalAmount = 11620m, BalanceDue = 5820m },
            new SalesInvoice { Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = customers[Math.Min(2, customers.Count - 1)].Id, InvoiceNumber = "INV-30106", InvoiceDate = new DateOnly(2026, 4, 5), DueDate = new DateOnly(2026, 5, 5), Status = "Open", Subtotal = 8420.15m, TaxAmount = 505.21m, TotalAmount = 8925.36m, BalanceDue = 8925.36m }
        };

        await dbContext.SalesInvoices.AddRangeAsync(invoices, cancellationToken);
        return invoices.Length;
    }

    private async Task<int> AddVendorsAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.Vendors.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var vendors = new[]
        {
            new Vendor { Id = Guid.NewGuid(), CompanyId = companyId, VendorNumber = "V-3101", Name = "Granite Industrial Supply", Email = "billing@graniteindustrial.example", State = "UT", PaymentTerms = "Net 30", OpenBalance = 12880m },
            new Vendor { Id = Guid.NewGuid(), CompanyId = companyId, VendorNumber = "V-3102", Name = "Signal Freight Systems", Email = "ap@signalfreight.example", State = "KS", PaymentTerms = "Net 15", OpenBalance = 4630.75m },
            new Vendor { Id = Guid.NewGuid(), CompanyId = companyId, VendorNumber = "V-3103", Name = "Riverbend Office Interiors", Email = "ar@riverbendoffice.example", State = "TN", PaymentTerms = "Net 10", OpenBalance = 2190m }
        };

        await dbContext.Vendors.AddRangeAsync(vendors, cancellationToken);
        return vendors.Length;
    }

    private async Task<int> AddBillsAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.VendorBills.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var vendors = await dbContext.Vendors.Where(candidate => candidate.CompanyId == companyId).OrderBy(candidate => candidate.VendorNumber).ToListAsync(cancellationToken);
        if (vendors.Count == 0)
        {
            return 0;
        }

        var bills = new[]
        {
            new VendorBill { Id = Guid.NewGuid(), CompanyId = companyId, VendorId = vendors[0].Id, BillNumber = "B-9301", BillDate = new DateOnly(2026, 4, 2), DueDate = new DateOnly(2026, 5, 2), Status = "Approved", TotalAmount = 12880m, BalanceDue = 12880m },
            new VendorBill { Id = Guid.NewGuid(), CompanyId = companyId, VendorId = vendors[Math.Min(1, vendors.Count - 1)].Id, BillNumber = "B-9304", BillDate = new DateOnly(2026, 4, 4), DueDate = new DateOnly(2026, 4, 19), Status = "Open", TotalAmount = 4630.75m, BalanceDue = 4630.75m },
            new VendorBill { Id = Guid.NewGuid(), CompanyId = companyId, VendorId = vendors[Math.Min(2, vendors.Count - 1)].Id, BillNumber = "B-9307", BillDate = new DateOnly(2026, 4, 6), DueDate = new DateOnly(2026, 4, 16), Status = "Open", TotalAmount = 2190m, BalanceDue = 2190m }
        };

        await dbContext.VendorBills.AddRangeAsync(bills, cancellationToken);
        return bills.Length;
    }

    private async Task<int> AddInventoryAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.InventoryItems.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var items = new[]
        {
            new InventoryItem { Id = Guid.NewGuid(), CompanyId = companyId, Sku = "FG-810", Description = "Stainless Pump Housing", UnitPrice = 264m, QuantityOnHand = 48m, ReorderPoint = 20m, IsActive = true },
            new InventoryItem { Id = Guid.NewGuid(), CompanyId = companyId, Sku = "RM-155", Description = "Copper Coil Set", UnitPrice = 88.50m, QuantityOnHand = 125m, ReorderPoint = 60m, IsActive = true },
            new InventoryItem { Id = Guid.NewGuid(), CompanyId = companyId, Sku = "KIT-420", Description = "Service Retrofit Kit", UnitPrice = 315m, QuantityOnHand = 19m, ReorderPoint = 15m, IsActive = true }
        };

        await dbContext.InventoryItems.AddRangeAsync(items, cancellationToken);
        return items.Length;
    }

    private async Task<int> AddSalesOrdersAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.SalesOrders.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var customers = await dbContext.Customers.Where(candidate => candidate.CompanyId == companyId).OrderBy(candidate => candidate.CustomerNumber).ToListAsync(cancellationToken);
        if (customers.Count == 0)
        {
            return 0;
        }

        var orders = new[]
        {
            new SalesOrder { Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = customers[0].Id, OrderNumber = "SO-8801", OrderedOn = new DateOnly(2026, 4, 7), Status = "Open", TotalAmount = 15440m },
            new SalesOrder { Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = customers[Math.Min(1, customers.Count - 1)].Id, OrderNumber = "SO-8802", OrderedOn = new DateOnly(2026, 4, 7), Status = "Allocated", TotalAmount = 9320m }
        };

        await dbContext.SalesOrders.AddRangeAsync(orders, cancellationToken);
        return orders.Length;
    }

    private async Task<int> AddPurchaseOrdersAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.PurchaseOrders.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var vendors = await dbContext.Vendors.Where(candidate => candidate.CompanyId == companyId).OrderBy(candidate => candidate.VendorNumber).ToListAsync(cancellationToken);
        if (vendors.Count == 0)
        {
            return 0;
        }

        var orders = new[]
        {
            new PurchaseOrder { Id = Guid.NewGuid(), CompanyId = companyId, VendorId = vendors[0].Id, OrderNumber = "PO-7701", OrderedOn = new DateOnly(2026, 4, 7), Status = "Approved", TotalAmount = 6420m },
            new PurchaseOrder { Id = Guid.NewGuid(), CompanyId = companyId, VendorId = vendors[Math.Min(1, vendors.Count - 1)].Id, OrderNumber = "PO-7704", OrderedOn = new DateOnly(2026, 4, 8), Status = "Issued", TotalAmount = 3880m }
        };

        await dbContext.PurchaseOrders.AddRangeAsync(orders, cancellationToken);
        return orders.Length;
    }

    private async Task<int> AddEmployeesAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.Employees.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var employees = new[]
        {
            new Employee { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeNumber = "E-201", FirstName = "Mara", LastName = "Nguyen", Department = "Finance", State = "WA", PayType = "Salary", MonthlyBasePay = 8250m, IsActive = true },
            new Employee { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeNumber = "E-205", FirstName = "Elias", LastName = "Turner", Department = "Purchasing", State = "UT", PayType = "Salary", MonthlyBasePay = 6120m, IsActive = true },
            new Employee { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeNumber = "E-212", FirstName = "Renee", LastName = "Lopez", Department = "Warehouse", State = "CO", PayType = "Hourly", MonthlyBasePay = 4280m, IsActive = true }
        };

        await dbContext.Employees.AddRangeAsync(employees, cancellationToken);
        return employees.Length;
    }

    private async Task<int> AddProjectsAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.ProjectJobs.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var jobs = new[]
        {
            new ProjectJob { Id = Guid.NewGuid(), CompanyId = companyId, JobNumber = "JOB-8801", Name = "Harbor Retrofit Phase 1", CustomerName = "Harbor View Contractors", Status = "Open", BudgetAmount = 58000m, ActualCost = 17120m },
            new ProjectJob { Id = Guid.NewGuid(), CompanyId = companyId, JobNumber = "JOB-8802", Name = "Transit Depot Refit", CustomerName = "Blue Canyon Transit", Status = "Billing", BudgetAmount = 41300m, ActualCost = 40110m }
        };

        await dbContext.ProjectJobs.AddRangeAsync(jobs, cancellationToken);
        return jobs.Length;
    }

    private async Task<int> AddJournalEntriesAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.JournalEntries.AnyAsync(candidate => candidate.CompanyId == companyId, cancellationToken))
        {
            return 0;
        }

        var accounts = await dbContext.Accounts.Where(candidate => candidate.CompanyId == companyId).OrderBy(candidate => candidate.Number).ToListAsync(cancellationToken);
        if (accounts.Count < 2)
        {
            return 0;
        }

        var entries = new[]
        {
            new JournalEntry { Id = Guid.NewGuid(), CompanyId = companyId, EntryNumber = "JE-9001", PostedOn = new DateOnly(2026, 4, 9), SourceModule = "Receivables", Reference = "INV-30101", Description = "Customer billing batch", TotalAmount = 20087m, IsPosted = true },
            new JournalEntry { Id = Guid.NewGuid(), CompanyId = companyId, EntryNumber = "JE-9002", PostedOn = new DateOnly(2026, 4, 9), SourceModule = "Payables", Reference = "B-9301", Description = "Vendor receipt accrual", TotalAmount = 12880m, IsPosted = true }
        };
        var lines = new[]
        {
            new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = entries[0].Id, AccountId = accounts[0].Id, Description = "Debit", Debit = 20087m, Credit = 0m },
            new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = entries[0].Id, AccountId = accounts[1].Id, Description = "Credit", Debit = 0m, Credit = 20087m },
            new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = entries[1].Id, AccountId = accounts[0].Id, Description = "Debit", Debit = 12880m, Credit = 0m },
            new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = entries[1].Id, AccountId = accounts[1].Id, Description = "Credit", Debit = 0m, Credit = 12880m }
        };

        await dbContext.JournalEntries.AddRangeAsync(entries, cancellationToken);
        await dbContext.JournalEntryLines.AddRangeAsync(lines, cancellationToken);
        return entries.Length;
    }

    private async Task<int> AddOperatorsAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken)
    {
        var existingUsers = await dbContext.Users.Where(candidate => candidate.CompanyId == companyId).ToListAsync(cancellationToken);
        if (existingUsers.Count > 1)
        {
            return 0;
        }

        var operators = new[]
        {
            CreateUser(companyId, "owner", "Avery Stone", "owner@company.example", "Owner/CEO"),
            CreateUser(companyId, "requisition", "Riley Foster", "requisition@company.example", "Requisitioning Clerk"),
            CreateUser(companyId, "purchasing", "Casey Morgan", "purchasing@company.example", "Purchasing Manager"),
            CreateUser(companyId, "checks", "Taylor Brooks", "checks@company.example", "Cash Disbursements")
        };

        foreach (var user in operators)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, BrassLedgerAuthenticationDefaults.SeededPassword);
        }

        await dbContext.Users.AddRangeAsync(operators, cancellationToken);
        return operators.Length;
    }

    private static AppUser CreateUser(Guid companyId, string userName, string displayName, string email, string role)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserName = userName,
            DisplayName = displayName,
            Email = email,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = role,
            IsActive = true,
            LastPasswordChangedUtc = DateTimeOffset.UtcNow
        };
    }
}
