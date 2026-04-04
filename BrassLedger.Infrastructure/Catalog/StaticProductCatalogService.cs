using BrassLedger.Application.Catalog;
using BrassLedger.Domain.Legacy;

namespace BrassLedger.Infrastructure.Catalog;

public sealed class StaticProductCatalogService : IProductCatalogService
{
    public ProductCatalog GetCatalog()
    {
        var inventory = new LegacyArtifactInventory(
            SourceStack: "Legacy desktop runtime",
            Projects: 12,
            Programs: 343,
            Forms: 561,
            Reports: 277,
            Tables: 531);

        var printableInventory = new LegacyPrintableInventory(
            Reports: 277,
            Labels: 9,
            ClassLibraries: 7,
            ActiveXControls: 2,
            CompiledArtifacts: 50);

        var modules = new[]
        {
            new LegacyModule("J", "General Ledger", "Core Accounting", "Referenced in Module.txt as G/L."),
            new LegacyModule("F", "Accounts Receivable", "Core Accounting", "Referenced in Module.txt as A/R."),
            new LegacyModule("E", "Accounts Payable", "Core Accounting", "Referenced in Module.txt as A/P."),
            new LegacyModule("Q", "Payroll", "Payroll", "Startup files and timecard notes reference payroll procedures."),
            new LegacyModule("K", "Inventory", "Operations", "Referenced in Module.txt as Inventory."),
            new LegacyModule("O", "Order Entry", "Operations", "Referenced in Module.txt as O/E."),
            new LegacyModule("S", "Purchase Order", "Operations", "Referenced in Module.txt as P/O."),
            new LegacyModule("P", "Point of Sale", "Operations", "Referenced in Module.txt as POS."),
            new LegacyModule("G", "Bank Manager", "Treasury", "Referenced in Module.txt as Bank."),
            new LegacyModule("U", "Zero Balance Accounting", "Treasury", "Referenced in Module.txt as ZBA."),
            new LegacyModule("L", "Job Tracking", "Projects", "Referenced in Module.txt as Job."),
            new LegacyModule("B", "Property Management", "Industry Module", "Property-management package appears in Module.txt."),
            new LegacyModule("T", "Timecard", "Workforce", "Referenced in TCLoGIN/CLCLOCK notes and Module.txt."),
            new LegacyModule("I", "CRM", "Customer Management", "Referenced in Module.txt as CRM.")
        };

        var taxSources = new[]
        {
            new OfficialTaxSource(
                "Federal",
                "IRS Publication 15-T",
                "https://www.irs.gov/publications/p15t",
                "Primary official source for federal income tax withholding methods and tables."),
            new OfficialTaxSource(
                "Federal",
                "IRS Publication 15",
                "https://www.irs.gov/publications/p15",
                "Primary official source for employer payroll tax rules including Social Security, Medicare, and FUTA."),
            new OfficialTaxSource(
                "States",
                "Federation of Tax Administrators directory",
                "https://taxadmin.org/state-tax-agencies/",
                "Useful index of official state tax agencies for per-state adapters and source discovery."),
            new OfficialTaxSource(
                "State UI example",
                "California DE 2088 explanation",
                "https://edd.ca.gov/siteassets/files/pdf_pub_ctr/de2088c.pdf",
                "Shows that some unemployment and payroll tax rates are employer-specific notices rather than a single public nationwide feed.")
        };

        return new ProductCatalog(
            RecommendedLanguage: "C#",
            RecommendedArchitecture: "ASP.NET Core backend with a separate TypeScript web frontend and dedicated payroll/reporting services",
            RecommendedDatabase: "PostgreSQL as the default operational database, with SQL Server as a viable secondary option",
            LegacyDataStrategy: "Treat DBF and DBC assets as legacy import sources only, then migrate the operational system to a relational database.",
            WhyCSharp: "C# is a strong migration target for a large legacy business application because it is well suited to Windows integration, reporting, long-lived enterprise maintenance, and staged replacement of DBF-era workflows.",
            Inventory: inventory,
            PrintableInventory: printableInventory,
            Risks: new[]
            {
                "The legacy forms and reports are stateful and event-driven, so a direct one-to-one screen port will create brittle code.",
                "DBF and DBC structures still need careful semantic mapping before data cutover, even if the new platform uses a relational database.",
                "Printing, check layouts, tax forms, and report fidelity are likely the highest regression-risk areas.",
                "Global object usage in startup code and class libraries implies hidden coupling across modules that should be untangled into services.",
                "ActiveX and legacy class libraries must be replaced with supported native components instead of binary compatibility shims."
            },
            Phases: new[]
            {
                "Document tables, forms, reports, and module boundaries from the legacy project.",
                "Design a canonical relational schema and import pipeline from DBF/CDX/FPT data.",
                "Rebuild shared business rules as testable C# domain and application services.",
                "Deliver module-by-module replacements starting with authentication, company setup, general ledger, and reporting.",
                "Run BrassLedger in parallel with the legacy product until data and printed outputs reconcile."
            },
            Modules: modules,
            ProductPrinciples: new[]
            {
                "Open source and free with every module available to every user.",
                "No premium subscription logic, no purchased-module gating, and no registration unlock tables.",
                "Accessible UI that is intentionally distinct from the legacy product's branding and trade dress.",
                "Tax logic and report rendering should be transparent, testable, and data-driven."
            },
            FeaturesToRemove: new[]
            {
                "Executable expiration logic in library.prg and ExpirationTest.",
                "Tax expiration reminders such as TaxExpirationReminder and related warning strings.",
                "Legacy registration and module-locking behavior tied to cobra.ovl and regisknt.",
                "Any forced-update or security-warning flow whose purpose was to drive paid updates rather than actual security remediation."
            },
            ConversionWorkstreams: new[]
            {
                "Convert FRX/FRT reports into current report definitions or server-rendered PDF templates.",
                "Convert LBX/LBT label layouts into a reusable label templating system.",
                "Extract business logic from VCX/VCT class libraries into C# domain and application services.",
                "Replace OCX and other binary dependencies with supported current libraries and web-native workflows.",
                "Reimplement payroll and tax engines from rules and test fixtures rather than copying legacy runtime behavior line-for-line."
            },
            VisualDifferentiators: new[]
            {
                "Use a fresh product name, iconography, and color system unrelated to the legacy application.",
                "Adopt current layout patterns, typography, spacing, and interaction design rather than reproducing the legacy windows and toolbars.",
                "Keep accounting workflows familiar while ensuring the UI is recognizably a new product."
            },
            TaxAutomationStrategy: new[]
            {
                "Do not assume a single free nationwide standardized feed exists for all payroll taxes and business rules.",
                "Build a pluggable tax-source pipeline with per-jurisdiction adapters and effective-date versioning.",
                "Automate federal imports from official IRS publications and machine-readable sources where available.",
                "Use official state sources per jurisdiction, with manual review paths for states or rules that remain PDF- or notice-driven.",
                "Treat employer-specific rates such as some UI/SUI values as account configuration or imported notices, not universal reference data."
            },
            TaxSources: taxSources);
    }
}

