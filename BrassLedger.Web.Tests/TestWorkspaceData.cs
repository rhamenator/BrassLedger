using BrassLedger.Application.Accounting;
using BrassLedger.Application.Catalog;

namespace BrassLedger.Web.Tests;

internal static class TestWorkspaceData
{
    public static BusinessWorkspaceSnapshot CreateWorkspace()
    {
        return new BusinessWorkspaceSnapshot(
            GeneratedAtUtc: new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            Company: new CompanySnapshot("Brass Ledger Manufacturing", "Brass Ledger Manufacturing, LLC", "84-9923145", "USD", 1, 4),
            Dashboard: new DashboardSnapshot(112540.32m, 34715.75m, 31844.77m, 24367m, 5, 3, 3, 14, 9),
            Modules: new[]
            {
                new ModuleWorkspaceSnapshot("J", "General Ledger", "Core Accounting", "Live foundation", "Chart of accounts is live.", 12),
                new ModuleWorkspaceSnapshot("F", "Accounts Receivable", "Core Accounting", "Live foundation", "Receivables are live.", 8)
            },
            GeneralLedger: new GeneralLedgerWorkspace(
                188436.37m,
                31844.77m,
                95000m,
                640225.18m,
                511130.09m,
                new[] { new AccountSnapshot("1000", "Operating Cash", "Asset", 112540.32m, false) },
                new[] { new JournalEntrySnapshot("JE-2401", new DateOnly(2026, 3, 30), "Accounts Receivable", "March billing batch", 12720m) }),
            Receivables: new ReceivablesWorkspace(
                34715.75m,
                0,
                new[] { new CustomerSnapshot("C-1001", "Red Mesa Builders", "AZ", 45000m, 18220.15m) },
                new[] { new InvoiceSnapshot("INV-24015", "Red Mesa Builders", new DateOnly(2026, 3, 8), new DateOnly(2026, 4, 7), "Open", 12720m, 12720m) }),
            Payables: new PayablesWorkspace(
                31844.77m,
                2,
                new[] { new VendorSnapshot("V-2001", "Ironwood Steel Supply", "TX", "Net 30", 13210.50m) },
                new[] { new BillSnapshot("B-8810", "Ironwood Steel Supply", new DateOnly(2026, 3, 16), new DateOnly(2026, 4, 15), "Open", 13210.50m, 13210.50m) }),
            Operations: new OperationsWorkspace(
                5,
                1,
                3,
                2,
                new[] { new InventoryItemSnapshot("FG-100", "Machined Brass Valve", 185m, 84m, 40m) },
                new[] { new SalesOrderSnapshot("SO-3107", "Red Mesa Builders", new DateOnly(2026, 3, 27), "Picking", 12840m) },
                new[] { new PurchaseOrderSnapshot("PO-4101", "Ironwood Steel Supply", new DateOnly(2026, 3, 29), "Issued", 22110m) }),
            Treasury: new TreasuryWorkspace(
                112540.32m,
                1840.60m,
                new[] { new BankAccountSnapshot("Primary Operating", "****1044", 96580.11m, 1840.60m, new DateOnly(2026, 3, 31)) }),
            Payroll: new PayrollWorkspace(
                4,
                24367m,
                new[] { new EmployeeSnapshot("E-100", "Rosa Mendoza", "Production", "AZ", "Hourly", 4912m, true) }),
            Projects: new ProjectsWorkspace(
                3,
                135500m,
                103630m,
                new[] { new ProjectJobSnapshot("JOB-5007", "Red Mesa Expansion", "Red Mesa Builders", "Open", 45000m, 27600m) }),
            Reporting: new ReportingWorkspace(
                6,
                3,
                "Visual Studio RDL/RDLC",
                "RDLC plus PDF templates",
                new[] { new ReportCatalogSnapshot("RDL-GL-TRIAL", "Trial Balance", "General Ledger", "RDLC", "Month-end trial balance.", true) },
                new[] { new LabelTemplateSnapshot("LBL-SHIP-4X6", "Shipping Label 4x6", "Thermal 4x6", "Carrier-ready shipment label.") }),
            Taxes: new TaxWorkspace(
                4,
                1,
                new[] { new TaxProfileSnapshot("Federal", "FUTA", 0.00600m, new DateOnly(2026, 1, 1), "IRS Publication 15", false) }));
    }

    public static ProductCatalog CreateAssessment()
    {
        return new ProductCatalog(
            "C#",
            "ASP.NET Core backend with a separate TypeScript web frontend and dedicated payroll/reporting services",
            "PostgreSQL as the default operational database, with SQL Server as a viable secondary option",
            "Treat DBF and DBC assets as legacy import sources only, then migrate the operational system to a relational database.",
            "C# is a natural migration target for a large legacy business application.",
            new( "Legacy desktop runtime", 12, 343, 561, 277, 531 ),
            new(277, 9, 7, 2, 50),
            new[] { "Printing fidelity is a regression risk." },
            new[] { "Design schema", "Migrate modules" },
            new[] { new BrassLedger.Domain.Legacy.LegacyModule("J", "General Ledger", "Core Accounting", "Seeded") },
            new[] { "Open source and free with every module available to every user." },
            new[] { "Executable expiration logic." },
            new[] { "Convert reports." },
            new[] { "Fresh branding." },
            new[] { "Build per-jurisdiction tax adapters." },
            new[] { new OfficialTaxSource("Federal", "IRS Publication 15", "https://www.irs.gov/publications/p15", "Payroll tax rules.") });
    }
}

internal sealed class StubBusinessWorkspaceService : IBusinessWorkspaceService
{
    private readonly BusinessWorkspaceSnapshot _snapshot;

    public StubBusinessWorkspaceService(BusinessWorkspaceSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public Task<BusinessWorkspaceSnapshot> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_snapshot);
    }
}

internal sealed class StubProductCatalogService : IProductCatalogService
{
    private readonly ProductCatalog _assessment;

    public StubProductCatalogService(ProductCatalog assessment)
    {
        _assessment = assessment;
    }

    public ProductCatalog GetCatalog()
    {
        return _assessment;
    }
}

