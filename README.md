# BrassLedger

BrassLedger is an open-source cross-platform accounting and business management system for organizations that need general ledger, receivables, payables, payroll, projects, reporting, tax workflows, and printable business forms in one application.

The current public prerelease is `v0.1.0-pre.1`.

License: `GPL-3.0-only`. See [LICENSE](LICENSE).

## Why BrassLedger

BrassLedger is intended for teams that want a modern, open-source financial operations platform without locking their business into a proprietary desktop stack. The application is built to support daily accounting work, period close, payroll review, operational documents, tax-facing output, labels, checks, and other fixed-layout business forms.

The current end-user application is `BrassLedger.Web`, delivered as a cross-platform .NET application with installers for Windows, macOS, and Linux.

## What the application includes

- General ledger workspaces for journal activity, balances, and month-end review
- Receivables support for customer invoices, statements, balances, and cash application
- Payables support for vendor bills, approvals, due-date review, and disbursement preparation
- Operations workspaces for inventory, order flow, purchasing, fulfillment, and operational forms
- Payroll support for employee records, pay processing, liability review, and year-end forms
- Project and job tracking for cost visibility and work-in-progress review
- Reporting support for financial statements, checks, paychecks, labels, shipment forms, and management output
- Tax workspaces for federal, state, and employer-specific tax profile review

## Download and quick start

- Quick start and installation: [docs/quick-start.md](docs/quick-start.md)
- Current prerelease downloads: [GitHub Releases](https://github.com/rhamenator/BrassLedger/releases)

Current prerelease installers are available for:

- Windows `x64`
- macOS Intel
- macOS Apple silicon
- Linux `amd64`

On first launch of a brand-new installation, BrassLedger now opens a first-time setup flow so you can create the initial company and administrator account without editing configuration files by hand.

## How to use BrassLedger

Start with these guides:

- Main user guide: [docs/user-guide.md](docs/user-guide.md)
- Reporting, checks, labels, forms, and print output: [docs/reporting-guide.md](docs/reporting-guide.md)
- Administration, publishing, and support practices: [docs/administration-guide.md](docs/administration-guide.md)

Module-by-module usage links:

- Overview and daily review: [docs/user-guide.md#overview](docs/user-guide.md#overview)
- General ledger: [docs/user-guide.md#ledger](docs/user-guide.md#ledger)
- Receivables: [docs/user-guide.md#receivables](docs/user-guide.md#receivables)
- Payables: [docs/user-guide.md#payables](docs/user-guide.md#payables)
- Operations: [docs/user-guide.md#operations](docs/user-guide.md#operations)
- Payroll: [docs/user-guide.md#payroll](docs/user-guide.md#payroll)
- Projects: [docs/user-guide.md#projects](docs/user-guide.md#projects)
- Reporting and forms: [docs/user-guide.md#reporting-and-forms](docs/user-guide.md#reporting-and-forms)
- Taxes: [docs/user-guide.md#taxes](docs/user-guide.md#taxes)
- Publish workspace: [docs/user-guide.md#publish](docs/user-guide.md#publish)
- Month-end review: [docs/user-guide.md#month-end-review](docs/user-guide.md#month-end-review)
- Security and data handling: [docs/user-guide.md#security-and-data-handling](docs/user-guide.md#security-and-data-handling)

Specialized documentation:

- Financial statements: [docs/reporting-guide.md#financial-statements](docs/reporting-guide.md#financial-statements)
- Receivables output: [docs/reporting-guide.md#receivables-output](docs/reporting-guide.md#receivables-output)
- Payables output: [docs/reporting-guide.md#payables-output](docs/reporting-guide.md#payables-output)
- Payroll and year-end output: [docs/reporting-guide.md#payroll-and-year-end-output](docs/reporting-guide.md#payroll-and-year-end-output)
- Tax-facing output: [docs/reporting-guide.md#tax-facing-output](docs/reporting-guide.md#tax-facing-output)
- Operations documents and labels: [docs/reporting-guide.md#operations-documents-and-labels](docs/reporting-guide.md#operations-documents-and-labels)
- Administrative data handling: [docs/administration-guide.md#data-handling](docs/administration-guide.md#data-handling)
- Administrative publishing guidance: [docs/administration-guide.md#publishing](docs/administration-guide.md#publishing)

## Security baseline

The current application includes:

- authenticated access before accounting data loads in the web app or API
- password hashing through ASP.NET Core Identity primitives
- protection of sensitive fields at rest with ASP.NET Core Data Protection
- persisted application key material under `App_Data\keys`
- security headers in the web application and API
- bootstrap administrator requirements for first non-development startup

Before live production use, administrators should still review operational backup, recovery, secrets management, access control, and deployment procedures.

## Build, test, and publish

Helpful maintainer commands:

```powershell
dotnet build .\BrassLedger.slnx
```

```powershell
dotnet test .\BrassLedger.slnx -nr:false
```

```powershell
.\publish-brassledger.ps1 -Runtime win-x64
```

```powershell
.\publish-brassledger.ps1 -Runtime osx-x64
```

```powershell
.\publish-brassledger.ps1 -Runtime osx-arm64
```

```powershell
.\publish-brassledger.ps1 -Runtime linux-x64
```

```powershell
.\run-ui-tests.ps1 -InstallBrowsers
```

```powershell
.\run-ui-tests.ps1 -UpdateBaselines
```

Additional maintainer documentation:

- Platform-specific publish notes: [docs/publish-guide.md](docs/publish-guide.md)

Release automation is handled by `.github/workflows/release-installers.yml`, which builds installer assets for Windows, macOS Intel, macOS Apple silicon, and Linux and attaches them to tagged prereleases and releases.

## Repository layout

- `BrassLedger.Domain`: core business entities
- `BrassLedger.Application`: application-facing contracts and models
- `BrassLedger.Infrastructure`: authentication, persistence, security, and service implementations
- `BrassLedger.Api`: API endpoints
- `BrassLedger.Web`: end-user application
- `docs`: installation, usage, reporting, administration, and publishing guides

Source-controlled web assets belong under `BrassLedger.Web/wwwroot`. Generated publish output stays under `artifacts` and remains ignored by Git.
