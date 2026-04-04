using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Persistence;

internal static class BrassLedgerSeedData
{
    private static readonly Guid CompanyId = Guid.Parse("0e561f1b-47b0-4c33-bd9f-1a3298ed29c6");

    public static async Task SeedAsync(
        BrassLedgerDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        CancellationToken cancellationToken = default)
    {
        if (await dbContext.Companies.AnyAsync(cancellationToken))
        {
            await EnsureSeedUserCredentialsAsync(dbContext, passwordHasher, cancellationToken);
            return;
        }

        var company = new Company
        {
            Id = CompanyId,
            Name = "Brass Ledger Manufacturing",
            LegalName = "Brass Ledger Manufacturing, LLC",
            TaxId = "84-9923145",
            BaseCurrency = "USD",
            FiscalYearStartMonth = 1
        };

        var users = new[]
        {
            CreateSeedUser(passwordHasher, Guid.Parse("535e60cf-7572-4dca-8328-fda4a470cdb9"), "controller", "Erin Dorsey", "erin@brassledger.local", "Controller"),
            CreateSeedUser(passwordHasher, Guid.Parse("a1d1c1de-af10-49f5-a2c4-e40bc060f9f6"), "operations", "Marco Patel", "marco@brassledger.local", "Operations"),
            CreateSeedUser(passwordHasher, Guid.Parse("9fc1ab25-5f42-4230-b8a5-c4df814dcb7d"), "payroll", "June Ellis", "june@brassledger.local", "Payroll"),
            CreateSeedUser(passwordHasher, Guid.Parse("c77634df-9bc0-4cac-b57d-bf7b8df7f604"), "sales", "Noah Bennett", "noah@brassledger.local", "Sales")
        };

        var accounts = new[]
        {
            new GeneralLedgerAccount { Id = Guid.Parse("945d6fcb-28fb-427c-9910-1d2ae0c90f0b"), CompanyId = CompanyId, Number = "1000", Name = "Operating Cash", Type = AccountType.Asset, CurrentBalance = 112540.32m, IsControlAccount = false, IsActive = true },
            new GeneralLedgerAccount { Id = Guid.Parse("bdb4983d-0b35-44ab-a9f0-723b9184d288"), CompanyId = CompanyId, Number = "1100", Name = "Accounts Receivable", Type = AccountType.Asset, CurrentBalance = 48215.90m, IsControlAccount = true, IsActive = true },
            new GeneralLedgerAccount { Id = Guid.Parse("7d7fd728-81de-4ad4-b417-77ad7193882f"), CompanyId = CompanyId, Number = "1200", Name = "Inventory Asset", Type = AccountType.Asset, CurrentBalance = 27680.15m, IsControlAccount = true, IsActive = true },
            new GeneralLedgerAccount { Id = Guid.Parse("7b2df519-fd50-4ca5-8704-6bfbe83cf322"), CompanyId = CompanyId, Number = "2000", Name = "Accounts Payable", Type = AccountType.Liability, CurrentBalance = 31844.77m, IsControlAccount = true, IsActive = true },
            new GeneralLedgerAccount { Id = Guid.Parse("01d8733f-fac4-47c7-b31c-c6585e97ff40"), CompanyId = CompanyId, Number = "3000", Name = "Owner Equity", Type = AccountType.Equity, CurrentBalance = 95000.00m, IsControlAccount = false, IsActive = true },
            new GeneralLedgerAccount { Id = Guid.Parse("8218f913-99cc-41f1-9d13-952de1911091"), CompanyId = CompanyId, Number = "4000", Name = "Product Revenue", Type = AccountType.Revenue, CurrentBalance = 640225.18m, IsControlAccount = false, IsActive = true },
            new GeneralLedgerAccount { Id = Guid.Parse("2dc7fd59-72b4-447a-8718-a08fef58b2db"), CompanyId = CompanyId, Number = "5100", Name = "Cost of Goods Sold", Type = AccountType.Expense, CurrentBalance = 354901.45m, IsControlAccount = false, IsActive = true },
            new GeneralLedgerAccount { Id = Guid.Parse("b38e5fb7-ed79-40dc-b64d-f9ad877c0875"), CompanyId = CompanyId, Number = "6100", Name = "Payroll Expense", Type = AccountType.Expense, CurrentBalance = 156228.64m, IsControlAccount = false, IsActive = true }
        };

        var customers = new[]
        {
            new Customer { Id = Guid.Parse("4aa05559-64da-4b96-bb4a-a0e3d91d0c80"), CompanyId = CompanyId, CustomerNumber = "C-1001", Name = "Red Mesa Builders", Email = "ap@redmesa.example", State = "AZ", CreditLimit = 45000m, OpenBalance = 18220.15m },
            new Customer { Id = Guid.Parse("25d684d6-87d0-4698-a9f7-567dd13cb1e1"), CompanyId = CompanyId, CustomerNumber = "C-1002", Name = "Lakeview Retail Group", Email = "accounting@lakeview.example", State = "IL", CreditLimit = 60000m, OpenBalance = 15995.00m },
            new Customer { Id = Guid.Parse("52b3204f-1ea8-4f70-93f3-b099f37d2df9"), CompanyId = CompanyId, CustomerNumber = "C-1003", Name = "North Coast Health", Email = "finance@northcoast.example", State = "CA", CreditLimit = 90000m, OpenBalance = 9010.75m },
            new Customer { Id = Guid.Parse("eb981a2f-ca8e-4ff7-9ca0-a1b95f9528cd"), CompanyId = CompanyId, CustomerNumber = "C-1004", Name = "Hilltop Civic Works", Email = "projects@hilltop.example", State = "OH", CreditLimit = 55000m, OpenBalance = 4990.00m }
        };

        var invoices = new[]
        {
            new SalesInvoice { Id = Guid.Parse("3b405d22-e29d-4fd3-b579-b5c19f8787f7"), CompanyId = CompanyId, CustomerId = customers[0].Id, InvoiceNumber = "INV-24015", InvoiceDate = new DateOnly(2026, 3, 8), DueDate = new DateOnly(2026, 4, 7), Status = "Open", Subtotal = 12000m, TaxAmount = 720m, TotalAmount = 12720m, BalanceDue = 12720m },
            new SalesInvoice { Id = Guid.Parse("13d8e6dc-2f7f-42c4-978b-eb0b6377d8fa"), CompanyId = CompanyId, CustomerId = customers[1].Id, InvoiceNumber = "INV-24018", InvoiceDate = new DateOnly(2026, 3, 19), DueDate = new DateOnly(2026, 4, 18), Status = "Partial", Subtotal = 14500m, TaxAmount = 870m, TotalAmount = 15370m, BalanceDue = 7995m },
            new SalesInvoice { Id = Guid.Parse("8e3716d5-b2a9-4d6c-a742-c7196b950df3"), CompanyId = CompanyId, CustomerId = customers[2].Id, InvoiceNumber = "INV-24021", InvoiceDate = new DateOnly(2026, 3, 24), DueDate = new DateOnly(2026, 4, 23), Status = "Open", Subtotal = 9010.75m, TaxAmount = 0m, TotalAmount = 9010.75m, BalanceDue = 9010.75m },
            new SalesInvoice { Id = Guid.Parse("2e0c71ba-b85f-4df3-8639-dd95e57ff365"), CompanyId = CompanyId, CustomerId = customers[3].Id, InvoiceNumber = "INV-24024", InvoiceDate = new DateOnly(2026, 3, 30), DueDate = new DateOnly(2026, 4, 29), Status = "Open", Subtotal = 4990m, TaxAmount = 0m, TotalAmount = 4990m, BalanceDue = 4990m }
        };

        var vendors = new[]
        {
            new Vendor { Id = Guid.Parse("8df6ab2a-b6ca-4c88-966f-2e2320b72f09"), CompanyId = CompanyId, VendorNumber = "V-2001", Name = "Ironwood Steel Supply", Email = "billing@ironwood.example", State = "TX", PaymentTerms = "Net 30", OpenBalance = 13210.50m },
            new Vendor { Id = Guid.Parse("c0c12fc1-80f6-4557-a1aa-2fc31e0dd804"), CompanyId = CompanyId, VendorNumber = "V-2002", Name = "Summit Packaging", Email = "ar@summitpack.example", State = "NV", PaymentTerms = "Net 15", OpenBalance = 6488.40m },
            new Vendor { Id = Guid.Parse("4e9bd12b-e6dd-410b-aefb-b6f886b66f01"), CompanyId = CompanyId, VendorNumber = "V-2003", Name = "Continental Freight", Email = "payables@continental.example", State = "MO", PaymentTerms = "Net 10", OpenBalance = 4330.87m },
            new Vendor { Id = Guid.Parse("5a6d7ee5-f1a5-4cf6-bd9e-7bfe4ac5f784"), CompanyId = CompanyId, VendorNumber = "V-2004", Name = "Apex Staffing", Email = "invoice@apexstaff.example", State = "GA", PaymentTerms = "Net 7", OpenBalance = 7815.00m }
        };

        var vendorBills = new[]
        {
            new VendorBill { Id = Guid.Parse("ce0eca9a-3777-46ce-8c51-77dd71d76cce"), CompanyId = CompanyId, VendorId = vendors[0].Id, BillNumber = "B-8810", BillDate = new DateOnly(2026, 3, 16), DueDate = new DateOnly(2026, 4, 15), Status = "Open", TotalAmount = 13210.50m, BalanceDue = 13210.50m },
            new VendorBill { Id = Guid.Parse("1312c8e2-da89-41d8-b701-0ca521c7a1ff"), CompanyId = CompanyId, VendorId = vendors[1].Id, BillNumber = "B-8819", BillDate = new DateOnly(2026, 3, 25), DueDate = new DateOnly(2026, 4, 9), Status = "Open", TotalAmount = 6488.40m, BalanceDue = 6488.40m },
            new VendorBill { Id = Guid.Parse("e8cb96d5-bd1a-485f-bf6e-f7459d816d49"), CompanyId = CompanyId, VendorId = vendors[2].Id, BillNumber = "B-8822", BillDate = new DateOnly(2026, 3, 28), DueDate = new DateOnly(2026, 4, 7), Status = "Approved", TotalAmount = 4330.87m, BalanceDue = 4330.87m },
            new VendorBill { Id = Guid.Parse("46c42d81-bf15-49c7-a8a2-e4b1daebc9cc"), CompanyId = CompanyId, VendorId = vendors[3].Id, BillNumber = "B-8824", BillDate = new DateOnly(2026, 4, 1), DueDate = new DateOnly(2026, 4, 8), Status = "Open", TotalAmount = 7815m, BalanceDue = 7815m }
        };

        var inventoryItems = new[]
        {
            new InventoryItem { Id = Guid.Parse("a59d6164-b872-471f-a3d5-ed3ea80be4b6"), CompanyId = CompanyId, Sku = "FG-100", Description = "Machined Brass Valve", UnitPrice = 185m, QuantityOnHand = 84m, ReorderPoint = 40m, IsActive = true },
            new InventoryItem { Id = Guid.Parse("855c69d5-8f1b-469f-a269-99d2d914cf39"), CompanyId = CompanyId, Sku = "FG-200", Description = "Compression Fitting Kit", UnitPrice = 92m, QuantityOnHand = 33m, ReorderPoint = 36m, IsActive = true },
            new InventoryItem { Id = Guid.Parse("5691f062-f8a8-47e6-a620-b49c463b75a8"), CompanyId = CompanyId, Sku = "RM-110", Description = "Brass Sheet 4x8", UnitPrice = 430m, QuantityOnHand = 18m, ReorderPoint = 12m, IsActive = true },
            new InventoryItem { Id = Guid.Parse("1bdf9fc0-c040-40bc-9f26-5495751321b5"), CompanyId = CompanyId, Sku = "RM-220", Description = "Steel Fastener Pack", UnitPrice = 16m, QuantityOnHand = 220m, ReorderPoint = 80m, IsActive = true },
            new InventoryItem { Id = Guid.Parse("b2547f8c-46c7-44bc-a9ff-13210ef32d60"), CompanyId = CompanyId, Sku = "SRV-900", Description = "Field Install Service", UnitPrice = 1250m, QuantityOnHand = 12m, ReorderPoint = 4m, IsActive = true }
        };

        var salesOrders = new[]
        {
            new SalesOrder { Id = Guid.Parse("4af34d71-1f1c-4ad7-b21d-c7490dc9c4e7"), CompanyId = CompanyId, CustomerId = customers[0].Id, OrderNumber = "SO-3107", OrderedOn = new DateOnly(2026, 3, 27), Status = "Picking", TotalAmount = 12840m },
            new SalesOrder { Id = Guid.Parse("d73d33bf-adb4-44aa-b3bf-bd7052db5389"), CompanyId = CompanyId, CustomerId = customers[1].Id, OrderNumber = "SO-3112", OrderedOn = new DateOnly(2026, 4, 1), Status = "Open", TotalAmount = 9425m },
            new SalesOrder { Id = Guid.Parse("8d6466fa-7d2d-49fb-b97b-e10e2d5ed9bd"), CompanyId = CompanyId, CustomerId = customers[2].Id, OrderNumber = "SO-3114", OrderedOn = new DateOnly(2026, 4, 2), Status = "Allocated", TotalAmount = 18200m }
        };

        var purchaseOrders = new[]
        {
            new PurchaseOrder { Id = Guid.Parse("6537cf79-a778-4fe1-aaf6-dd8d40ad0e0b"), CompanyId = CompanyId, VendorId = vendors[0].Id, OrderNumber = "PO-4101", OrderedOn = new DateOnly(2026, 3, 29), Status = "Issued", TotalAmount = 22110m },
            new PurchaseOrder { Id = Guid.Parse("b044fe73-c916-4df0-87fb-d1f0fc81c3f0"), CompanyId = CompanyId, VendorId = vendors[1].Id, OrderNumber = "PO-4104", OrderedOn = new DateOnly(2026, 4, 2), Status = "Approved", TotalAmount = 4890m }
        };

        var bankAccounts = new[]
        {
            new BankAccount { Id = Guid.Parse("3f98e4ef-6591-433f-8d5f-0c065c42fa3f"), CompanyId = CompanyId, Name = "Primary Operating", AccountNumberMasked = "****1044", CurrentBalance = 96580.11m, UnreconciledAmount = 1840.60m, LastReconciledOn = new DateOnly(2026, 3, 31) },
            new BankAccount { Id = Guid.Parse("03dac36a-b7ba-4361-b277-1f25517e53c8"), CompanyId = CompanyId, Name = "Payroll Clearing", AccountNumberMasked = "****2281", CurrentBalance = 15960.21m, UnreconciledAmount = 0m, LastReconciledOn = new DateOnly(2026, 3, 31) }
        };

        var employees = new[]
        {
            new Employee { Id = Guid.Parse("d6fc3d7c-c72f-4dac-a816-6a6935f33c37"), CompanyId = CompanyId, EmployeeNumber = "E-100", FirstName = "Rosa", LastName = "Mendoza", Department = "Production", State = "AZ", PayType = "Hourly", MonthlyBasePay = 4912m, IsActive = true },
            new Employee { Id = Guid.Parse("1d1e5ca2-aa46-4eaf-8c02-12222c00cb6a"), CompanyId = CompanyId, EmployeeNumber = "E-104", FirstName = "Milo", LastName = "Hart", Department = "Warehouse", State = "NV", PayType = "Hourly", MonthlyBasePay = 4380m, IsActive = true },
            new Employee { Id = Guid.Parse("c958dd4f-3678-4165-9c80-dcd8f01a2ae8"), CompanyId = CompanyId, EmployeeNumber = "E-109", FirstName = "Priya", LastName = "Shaw", Department = "Finance", State = "CA", PayType = "Salary", MonthlyBasePay = 7950m, IsActive = true },
            new Employee { Id = Guid.Parse("5042fc71-3749-42e8-b594-a9f069eb3bb4"), CompanyId = CompanyId, EmployeeNumber = "E-113", FirstName = "Gavin", LastName = "Cole", Department = "Field Service", State = "OH", PayType = "Salary", MonthlyBasePay = 7125m, IsActive = true }
        };

        var projectJobs = new[]
        {
            new ProjectJob { Id = Guid.Parse("bcc9c7cc-51ff-4574-9b06-73d3d1004a7c"), CompanyId = CompanyId, JobNumber = "JOB-5007", Name = "Red Mesa Expansion", CustomerName = "Red Mesa Builders", Status = "Open", BudgetAmount = 45000m, ActualCost = 27600m },
            new ProjectJob { Id = Guid.Parse("ca58f9c0-f6fd-4a05-8d7a-c9df54b7468c"), CompanyId = CompanyId, JobNumber = "JOB-5012", Name = "Hilltop Retrofit", CustomerName = "Hilltop Civic Works", Status = "Open", BudgetAmount = 28500m, ActualCost = 14320m },
            new ProjectJob { Id = Guid.Parse("d6579a0b-9767-47eb-bdab-b649fbebaf9e"), CompanyId = CompanyId, JobNumber = "JOB-5002", Name = "North Coast Lab Fitout", CustomerName = "North Coast Health", Status = "Billing", BudgetAmount = 62000m, ActualCost = 61710m }
        };

        var taxProfiles = new[]
        {
            new TaxProfile { Id = Guid.Parse("afef0ef3-e39b-4b6d-b8c5-3cab981d2b81"), CompanyId = CompanyId, Jurisdiction = "Federal", TaxType = "FIT Withholding", Rate = 0.22000m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "IRS Publication 15-T", IsEmployerSpecific = false },
            new TaxProfile { Id = Guid.Parse("ba8e3a79-f0fe-4983-a417-5d908b6860ca"), CompanyId = CompanyId, Jurisdiction = "Federal", TaxType = "FUTA", Rate = 0.00600m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "IRS Publication 15", IsEmployerSpecific = false },
            new TaxProfile { Id = Guid.Parse("1bb2c570-e04b-4f5f-bf86-a4edbeea8820"), CompanyId = CompanyId, Jurisdiction = "Arizona", TaxType = "SUI", Rate = 0.02450m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "Arizona DES notice", IsEmployerSpecific = true },
            new TaxProfile { Id = Guid.Parse("9a29289b-8b48-40c0-9ae7-06d6e0b71e47"), CompanyId = CompanyId, Jurisdiction = "California", TaxType = "ETT", Rate = 0.00100m, EffectiveOn = new DateOnly(2026, 1, 1), Source = "EDD employer reference", IsEmployerSpecific = false }
        };

        var journalEntries = new[]
        {
            new JournalEntry { Id = Guid.Parse("f1a46371-5db4-41b4-86b4-7b93e0ae620f"), CompanyId = CompanyId, EntryNumber = "JE-2401", PostedOn = new DateOnly(2026, 3, 30), SourceModule = "Accounts Receivable", Reference = invoices[0].InvoiceNumber, Description = "March billing batch", TotalAmount = 12720m, IsPosted = true },
            new JournalEntry { Id = Guid.Parse("8ad765c1-86e8-4308-a339-26c4071a8774"), CompanyId = CompanyId, EntryNumber = "JE-2402", PostedOn = new DateOnly(2026, 3, 31), SourceModule = "Accounts Payable", Reference = vendorBills[0].BillNumber, Description = "Steel receipt accrual", TotalAmount = 13210.50m, IsPosted = true },
            new JournalEntry { Id = Guid.Parse("6c5af614-9cf8-4ca0-b505-12e50e987b53"), CompanyId = CompanyId, EntryNumber = "JE-2403", PostedOn = new DateOnly(2026, 3, 31), SourceModule = "Payroll", Reference = "PR-031", Description = "Semi-monthly payroll", TotalAmount = 24367m, IsPosted = true },
            new JournalEntry { Id = Guid.Parse("d649c6a0-9807-4e09-a720-5eb83f13a982"), CompanyId = CompanyId, EntryNumber = "JE-2404", PostedOn = new DateOnly(2026, 4, 1), SourceModule = "Inventory", Reference = purchaseOrders[0].OrderNumber, Description = "Receipt into inventory", TotalAmount = 22110m, IsPosted = true }
        };

        var journalLines = new[]
        {
            new JournalEntryLine { Id = Guid.Parse("bd13ab5f-a291-4b27-a1e6-4027867ecae6"), JournalEntryId = journalEntries[0].Id, AccountId = accounts[1].Id, Description = "Invoice receivable", Debit = 12720m, Credit = 0m },
            new JournalEntryLine { Id = Guid.Parse("76586e01-b349-4319-b69f-f7bf7ec794bf"), JournalEntryId = journalEntries[0].Id, AccountId = accounts[5].Id, Description = "Invoice revenue", Debit = 0m, Credit = 12720m },
            new JournalEntryLine { Id = Guid.Parse("6a16aafb-3f8c-40b4-89c9-8bfed82f9b44"), JournalEntryId = journalEntries[1].Id, AccountId = accounts[6].Id, Description = "Material expense", Debit = 13210.50m, Credit = 0m },
            new JournalEntryLine { Id = Guid.Parse("d6cf1726-e382-47f3-8a77-2bdcc28ef7e3"), JournalEntryId = journalEntries[1].Id, AccountId = accounts[3].Id, Description = "Accounts payable", Debit = 0m, Credit = 13210.50m },
            new JournalEntryLine { Id = Guid.Parse("e30f3cad-0683-4b8f-a32e-a12957d0950f"), JournalEntryId = journalEntries[2].Id, AccountId = accounts[7].Id, Description = "Payroll expense", Debit = 24367m, Credit = 0m },
            new JournalEntryLine { Id = Guid.Parse("6d7209d8-4bfa-4c54-a908-8d72c1e7ca1c"), JournalEntryId = journalEntries[2].Id, AccountId = accounts[0].Id, Description = "Payroll cash", Debit = 0m, Credit = 24367m },
            new JournalEntryLine { Id = Guid.Parse("53b1bd9f-6f88-4678-ac8d-b0f80fca6b0a"), JournalEntryId = journalEntries[3].Id, AccountId = accounts[2].Id, Description = "Inventory receipt", Debit = 22110m, Credit = 0m },
            new JournalEntryLine { Id = Guid.Parse("09af8522-8846-487e-ba32-b1d90443759b"), JournalEntryId = journalEntries[3].Id, AccountId = accounts[3].Id, Description = "Received not invoiced", Debit = 0m, Credit = 22110m }
        };

        var reportCatalog = new[]
        {
            new ReportCatalogItem { Id = Guid.Parse("1e55d64a-8712-4458-823d-422ff0586500"), CompanyId = CompanyId, Code = "RDL-GL-TRIAL", Name = "Trial Balance", Category = "General Ledger", LayoutType = "RDLC", Description = "Month-end trial balance with account grouping.", SupportsVisualStudioDesign = true },
            new ReportCatalogItem { Id = Guid.Parse("a159f74f-ff53-48ec-bb32-4a54deef00fd"), CompanyId = CompanyId, Code = "RDL-AR-AGING", Name = "A/R Aging", Category = "Accounts Receivable", LayoutType = "RDLC", Description = "Customer aging by current, 30, 60, 90+.", SupportsVisualStudioDesign = true },
            new ReportCatalogItem { Id = Guid.Parse("e72a1384-f923-4e7b-8021-6451f6dadd29"), CompanyId = CompanyId, Code = "RDL-AP-AGING", Name = "A/P Aging", Category = "Accounts Payable", LayoutType = "RDLC", Description = "Open vendor balances with due buckets.", SupportsVisualStudioDesign = true },
            new ReportCatalogItem { Id = Guid.Parse("a0d17be0-af4e-4f0f-a777-ec6744bf4b56"), CompanyId = CompanyId, Code = "RDL-PR-CHECK", Name = "Payroll Register", Category = "Payroll", LayoutType = "RDLC", Description = "Gross-to-net register by payroll run.", SupportsVisualStudioDesign = true },
            new ReportCatalogItem { Id = Guid.Parse("5d33a739-44fb-4370-aec4-75ae9d5581ef"), CompanyId = CompanyId, Code = "RDL-INV-AVAIL", Name = "Inventory Availability", Category = "Operations", LayoutType = "RDLC", Description = "On-hand and reorder analysis.", SupportsVisualStudioDesign = true },
            new ReportCatalogItem { Id = Guid.Parse("a498ba3b-77e7-43cc-861a-767e5e45e26b"), CompanyId = CompanyId, Code = "PDF-EXEC-DASH", Name = "Executive Flash Report", Category = "Management", LayoutType = "PDF Template", Description = "Daily operating scorecard rendered server-side.", SupportsVisualStudioDesign = false }
        };

        var labelTemplates = new[]
        {
            new LabelTemplate { Id = Guid.Parse("b1f58391-f21e-4fd6-bd9c-dceea9186db4"), CompanyId = CompanyId, Code = "LBL-SHIP-4X6", Name = "Shipping Label 4x6", StockType = "Thermal 4x6", Description = "Carrier-ready shipment label." },
            new LabelTemplate { Id = Guid.Parse("fb8e2b4d-2e89-4f60-8354-a568723c852f"), CompanyId = CompanyId, Code = "LBL-BIN-2X1", Name = "Warehouse Bin Label", StockType = "Thermal 2x1", Description = "Shelf and bin location label." },
            new LabelTemplate { Id = Guid.Parse("a4d45fd4-ad2d-4970-ab2f-12380135ff79"), CompanyId = CompanyId, Code = "LBL-ADDR-5160", Name = "Customer Address Sheet", StockType = "Avery 5160", Description = "Mail-merge style customer address labels." }
        };

        await dbContext.Companies.AddAsync(company, cancellationToken);
        await dbContext.Users.AddRangeAsync(users, cancellationToken);
        await dbContext.Accounts.AddRangeAsync(accounts, cancellationToken);
        await dbContext.Customers.AddRangeAsync(customers, cancellationToken);
        await dbContext.SalesInvoices.AddRangeAsync(invoices, cancellationToken);
        await dbContext.Vendors.AddRangeAsync(vendors, cancellationToken);
        await dbContext.VendorBills.AddRangeAsync(vendorBills, cancellationToken);
        await dbContext.InventoryItems.AddRangeAsync(inventoryItems, cancellationToken);
        await dbContext.SalesOrders.AddRangeAsync(salesOrders, cancellationToken);
        await dbContext.PurchaseOrders.AddRangeAsync(purchaseOrders, cancellationToken);
        await dbContext.BankAccounts.AddRangeAsync(bankAccounts, cancellationToken);
        await dbContext.Employees.AddRangeAsync(employees, cancellationToken);
        await dbContext.ProjectJobs.AddRangeAsync(projectJobs, cancellationToken);
        await dbContext.TaxProfiles.AddRangeAsync(taxProfiles, cancellationToken);
        await dbContext.JournalEntries.AddRangeAsync(journalEntries, cancellationToken);
        await dbContext.JournalEntryLines.AddRangeAsync(journalLines, cancellationToken);
        await dbContext.ReportCatalogItems.AddRangeAsync(reportCatalog, cancellationToken);
        await dbContext.LabelTemplates.AddRangeAsync(labelTemplates, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AppUser CreateSeedUser(
        IPasswordHasher<AppUser> passwordHasher,
        Guid userId,
        string userName,
        string displayName,
        string email,
        string role)
    {
        var user = new AppUser
        {
            Id = userId,
            CompanyId = CompanyId,
            UserName = userName,
            DisplayName = displayName,
            Email = email,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = role,
            IsActive = true,
            LastPasswordChangedUtc = DateTimeOffset.UtcNow
        };

        user.PasswordHash = passwordHasher.HashPassword(user, BrassLedgerAuthenticationDefaults.SeededPassword);
        return user;
    }

    private static async Task EnsureSeedUserCredentialsAsync(
        BrassLedgerDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        CancellationToken cancellationToken)
    {
        var knownUsers = new Dictionary<string, (string UserName, string Role)>(StringComparer.OrdinalIgnoreCase)
        {
            ["erin@brassledger.local"] = ("controller", "Controller"),
            ["marco@brassledger.local"] = ("operations", "Operations"),
            ["june@brassledger.local"] = ("payroll", "Payroll"),
            ["noah@brassledger.local"] = ("sales", "Sales")
        };

        var users = await dbContext.Users.ToListAsync(cancellationToken);
        var hasChanges = false;

        foreach (var user in users)
        {
            if (!knownUsers.TryGetValue(user.Email, out var definition))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(user.UserName))
            {
                user.UserName = definition.UserName;
                hasChanges = true;
            }

            if (string.IsNullOrWhiteSpace(user.Role))
            {
                user.Role = definition.Role;
                hasChanges = true;
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                user.PasswordHash = passwordHasher.HashPassword(user, BrassLedgerAuthenticationDefaults.SeededPassword);
                user.LastPasswordChangedUtc ??= DateTimeOffset.UtcNow;
                hasChanges = true;
            }

            if (string.IsNullOrWhiteSpace(user.SecurityStamp))
            {
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
