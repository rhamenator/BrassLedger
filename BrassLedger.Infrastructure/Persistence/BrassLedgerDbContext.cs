using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BrassLedger.Infrastructure.Persistence;

public sealed class BrassLedgerDbContext(
    DbContextOptions<BrassLedgerDbContext> options,
    ISensitiveDataProtector sensitiveDataProtector) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AccessRole> AccessRoles => Set<AccessRole>();
    public DbSet<AuthenticationAuditEntry> AuthenticationAuditEntries => Set<AuthenticationAuditEntry>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<GeneralLedgerAccount> Accounts => Set<GeneralLedgerAccount>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<LabelTemplate> LabelTemplates => Set<LabelTemplate>();
    public DbSet<ProjectJob> ProjectJobs => Set<ProjectJob>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<ReportCatalogItem> ReportCatalogItems => Set<ReportCatalogItem>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<TaxProfile> TaxProfiles => Set<TaxProfile>();
    public DbSet<TaxRuleSet> TaxRuleSets => Set<TaxRuleSet>();
    public DbSet<TaxRuleParameter> TaxRuleParameters => Set<TaxRuleParameter>();
    public DbSet<TaxRuleBracket> TaxRuleBrackets => Set<TaxRuleBracket>();
    public DbSet<TaxFormRequirement> TaxFormRequirements => Set<TaxFormRequirement>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<VendorBill> VendorBills => Set<VendorBill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var encryptedStringConverter = new ValueConverter<string, string>(
            value => sensitiveDataProtector.Protect(value),
            value => sensitiveDataProtector.Unprotect(value));

        modelBuilder.Entity<Company>().HasKey(x => x.Id);
        modelBuilder.Entity<AppUser>().HasKey(x => x.Id);
        modelBuilder.Entity<AccessRole>().HasKey(x => x.Id);
        modelBuilder.Entity<AuthenticationAuditEntry>().HasKey(x => x.Id);
        modelBuilder.Entity<GeneralLedgerAccount>().HasKey(x => x.Id);
        modelBuilder.Entity<JournalEntry>().HasKey(x => x.Id);
        modelBuilder.Entity<JournalEntryLine>().HasKey(x => x.Id);
        modelBuilder.Entity<Customer>().HasKey(x => x.Id);
        modelBuilder.Entity<SalesInvoice>().HasKey(x => x.Id);
        modelBuilder.Entity<Vendor>().HasKey(x => x.Id);
        modelBuilder.Entity<VendorBill>().HasKey(x => x.Id);
        modelBuilder.Entity<InventoryItem>().HasKey(x => x.Id);
        modelBuilder.Entity<SalesOrder>().HasKey(x => x.Id);
        modelBuilder.Entity<PurchaseOrder>().HasKey(x => x.Id);
        modelBuilder.Entity<BankAccount>().HasKey(x => x.Id);
        modelBuilder.Entity<Employee>().HasKey(x => x.Id);
        modelBuilder.Entity<ProjectJob>().HasKey(x => x.Id);
        modelBuilder.Entity<TaxProfile>().HasKey(x => x.Id);
        modelBuilder.Entity<TaxRuleSet>().HasKey(x => x.Id);
        modelBuilder.Entity<TaxRuleParameter>().HasKey(x => x.Id);
        modelBuilder.Entity<TaxRuleBracket>().HasKey(x => x.Id);
        modelBuilder.Entity<TaxFormRequirement>().HasKey(x => x.Id);
        modelBuilder.Entity<ReportCatalogItem>().HasKey(x => x.Id);
        modelBuilder.Entity<LabelTemplate>().HasKey(x => x.Id);

        modelBuilder.Entity<GeneralLedgerAccount>()
            .Property(x => x.Type)
            .HasConversion<string>();

        modelBuilder.Entity<Company>().Property(x => x.TaxId).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<AppUser>().Property(x => x.DisplayName).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<AppUser>().Property(x => x.Email).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<AccessRole>().Property(x => x.Description).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<Customer>().Property(x => x.Name).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<Customer>().Property(x => x.Email).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<Vendor>().Property(x => x.Name).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<Vendor>().Property(x => x.Email).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<BankAccount>().Property(x => x.AccountNumberMasked).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<Employee>().Property(x => x.FirstName).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<Employee>().Property(x => x.LastName).HasConversion(encryptedStringConverter);
        modelBuilder.Entity<ProjectJob>().Property(x => x.CustomerName).HasConversion(encryptedStringConverter);

        ConfigureMoney(modelBuilder.Entity<GeneralLedgerAccount>().Property(x => x.CurrentBalance));
        ConfigureMoney(modelBuilder.Entity<JournalEntry>().Property(x => x.TotalAmount));
        ConfigureMoney(modelBuilder.Entity<JournalEntryLine>().Property(x => x.Debit));
        ConfigureMoney(modelBuilder.Entity<JournalEntryLine>().Property(x => x.Credit));
        ConfigureMoney(modelBuilder.Entity<Customer>().Property(x => x.CreditLimit));
        ConfigureMoney(modelBuilder.Entity<Customer>().Property(x => x.OpenBalance));
        ConfigureMoney(modelBuilder.Entity<SalesInvoice>().Property(x => x.Subtotal));
        ConfigureMoney(modelBuilder.Entity<SalesInvoice>().Property(x => x.TaxAmount));
        ConfigureMoney(modelBuilder.Entity<SalesInvoice>().Property(x => x.TotalAmount));
        ConfigureMoney(modelBuilder.Entity<SalesInvoice>().Property(x => x.BalanceDue));
        ConfigureMoney(modelBuilder.Entity<Vendor>().Property(x => x.OpenBalance));
        ConfigureMoney(modelBuilder.Entity<VendorBill>().Property(x => x.TotalAmount));
        ConfigureMoney(modelBuilder.Entity<VendorBill>().Property(x => x.BalanceDue));
        ConfigureMoney(modelBuilder.Entity<InventoryItem>().Property(x => x.UnitPrice));
        ConfigureMoney(modelBuilder.Entity<InventoryItem>().Property(x => x.QuantityOnHand));
        ConfigureMoney(modelBuilder.Entity<InventoryItem>().Property(x => x.ReorderPoint));
        ConfigureMoney(modelBuilder.Entity<SalesOrder>().Property(x => x.TotalAmount));
        ConfigureMoney(modelBuilder.Entity<PurchaseOrder>().Property(x => x.TotalAmount));
        ConfigureMoney(modelBuilder.Entity<BankAccount>().Property(x => x.CurrentBalance));
        ConfigureMoney(modelBuilder.Entity<BankAccount>().Property(x => x.UnreconciledAmount));
        ConfigureMoney(modelBuilder.Entity<Employee>().Property(x => x.MonthlyBasePay));
        ConfigureMoney(modelBuilder.Entity<ProjectJob>().Property(x => x.BudgetAmount));
        ConfigureMoney(modelBuilder.Entity<ProjectJob>().Property(x => x.ActualCost));
        ConfigureMoney(modelBuilder.Entity<TaxProfile>().Property(x => x.Rate), 9, 5);
        modelBuilder.Entity<TaxRuleParameter>().Property(x => x.NumericValue).HasPrecision(18, 4);
        ConfigureMoney(modelBuilder.Entity<TaxRuleBracket>().Property(x => x.UpperBoundAmount), 18, 2);
        ConfigureMoney(modelBuilder.Entity<TaxRuleBracket>().Property(x => x.FixedAmount), 18, 2);
        ConfigureMoney(modelBuilder.Entity<TaxRuleBracket>().Property(x => x.Rate), 9, 5);

        modelBuilder.Entity<Company>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<AppUser>().HasIndex(x => x.UserName).IsUnique();
        modelBuilder.Entity<AccessRole>().HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        modelBuilder.Entity<AuthenticationAuditEntry>().HasIndex(x => new { x.UserName, x.OccurredUtc });
        modelBuilder.Entity<GeneralLedgerAccount>().HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(x => new { x.CompanyId, x.CustomerNumber }).IsUnique();
        modelBuilder.Entity<Vendor>().HasIndex(x => new { x.CompanyId, x.VendorNumber }).IsUnique();
        modelBuilder.Entity<SalesInvoice>().HasIndex(x => new { x.CompanyId, x.InvoiceNumber }).IsUnique();
        modelBuilder.Entity<VendorBill>().HasIndex(x => new { x.CompanyId, x.BillNumber }).IsUnique();
        modelBuilder.Entity<SalesOrder>().HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
        modelBuilder.Entity<PurchaseOrder>().HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
        modelBuilder.Entity<Employee>().HasIndex(x => new { x.CompanyId, x.EmployeeNumber }).IsUnique();
        modelBuilder.Entity<ProjectJob>().HasIndex(x => new { x.CompanyId, x.JobNumber }).IsUnique();
        modelBuilder.Entity<TaxRuleSet>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        modelBuilder.Entity<TaxRuleParameter>().HasIndex(x => new { x.TaxRuleSetId, x.ParameterCode }).IsUnique();
        modelBuilder.Entity<TaxRuleBracket>().HasIndex(x => new { x.TaxRuleSetId, x.Sequence }).IsUnique();
        modelBuilder.Entity<TaxFormRequirement>().HasIndex(x => new { x.TaxRuleSetId, x.FormCode }).IsUnique();
    }

    private static void ConfigureMoney(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<decimal> propertyBuilder, int precision = 18, int scale = 2)
    {
        propertyBuilder.HasPrecision(precision, scale);
    }
}
