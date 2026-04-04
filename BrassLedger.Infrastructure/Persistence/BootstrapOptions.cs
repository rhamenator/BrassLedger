namespace BrassLedger.Infrastructure.Persistence;

public sealed class BootstrapOptions
{
    public bool SeedSampleData { get; set; }
    public string CompanyName { get; set; } = "BrassLedger Company";
    public string LegalName { get; set; } = "BrassLedger Company";
    public string TaxId { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = "USD";
    public int FiscalYearStartMonth { get; set; } = 1;
    public string AdminUserName { get; set; } = "admin";
    public string AdminDisplayName { get; set; } = "System Administrator";
    public string AdminEmail { get; set; } = "admin@localhost.invalid";
    public string AdminPassword { get; set; } = string.Empty;
}
