# Legacy FoxPro Modernization Plan

## Product direction

This remake should be treated as a new open-source accounting platform, not as a branded continuation of the old commercial product.

Core product principles:

- every module is included for every user
- no premium tiers, purchased-module gating, or registration unlock paths
- no executable expiration, tax-expiration reminders, or forced-update warnings
- modern UI and branding that are clearly distinct from the legacy product

## What the current application contains

The codebase under `SourceCode/newproj` is a large Visual FoxPro business application. Current inventory found in this repository:

- 12 FoxPro project files (`.PJX`/`.PJT`)
- 343 program modules (`.PRG`)
- 561 forms (`.SCX`/`.SCT`)
- 277 reports (`.FRX`/`.FRT`)
- 9 label layouts (`.LBX`/`.LBT`)
- 531 DBF tables
- 7 class libraries in `newproj` plus a much larger library surface under `SourceCode/classes`
- 2 ActiveX controls (`.OCX`)
- 50 compiled FoxPro artifacts (`.FXP`)

The startup code in `account.prg` and `clstart.prg` shows a classic FoxPro desktop architecture with globals, menu composition, registry usage, report dependencies, and module toggles.

## Legacy behaviors to delete

The new system should not carry these behaviors forward:

- executable expiration logic in `library.prg` and `ExpirationTest`
- tax-expiration reminder forms and warning strings such as `TaxExpirationReminder`
- registration and license state tied to `cobra.ovl`
- module segmentation based on purchase status or subscription level

Legacy evidence already found:

- `library.prg` calls `TaxExpirationReminder` and contains expiration-date logic
- `importtaxfiles.prg`, `taxengin.prg`, and `taxrules.prg` contain the legacy payroll-tax subsystem
- `editcobra.scx` and related files appear to support old registration or entitlement behavior

## Recommended target stack

Primary target:

- backend: ASP.NET Core
- domain logic: C# class libraries
- default database: PostgreSQL
- frontend: TypeScript web app

Secondary option:

- WPF desktop tooling only for transitional admin or printing workflows if the web UI is not ready yet

Why this fits:

- C# is strong for payroll, accounting, reporting, and long-lived enterprise maintenance
- PostgreSQL is a strong open-source operational store and a much better future target than DBF/DBC/CDX/FPT
- a web frontend makes it much easier to modernize the look and differentiate from the original product

## Data strategy

Do not carry DBF, DBC, CDX, or FPT forward as the operational store.

Use them only as legacy inputs for:

- schema discovery
- data import
- reconciliation
- historical reference during migration

For indexes specifically:

- do not start by reproducing every CDX index
- derive the new indexing strategy from business workflows, queries, reports, and relational integrity needs
- use legacy index files only as supporting evidence when uniqueness, lookup keys, or sort behavior are unclear

## Reports, labels, and object conversion

This rewrite must include the printable surface area, not just the data-entry screens.

Workstreams:

1. Convert FRX/FRT reports into modern report definitions.
2. Convert LBX/LBT label layouts into a reusable label engine.
3. Replace VCX/VCT class-library behavior with C# services and UI components.
4. Replace OCX dependencies with supported libraries or browser-native capabilities.
5. Rebuild checks, tax forms, invoices, statements, and labels with automated regression fixtures.

Recommended report direction:

- define printable documents as versioned templates
- render to PDF in the backend
- support printer-friendly HTML for routine operational documents
- keep field mapping, layout metadata, and effective dates explicit in source control

## Tax update strategy

There does not appear to be a single free nationwide standardized feed that fully covers federal payroll tax rules plus every state and local business rule in one format.

The better design is:

- one payroll-tax domain model
- one rules repository with effective-date versioning
- multiple official-source adapters
- a reviewed import pipeline
- employer-specific override storage for account-specific rates

Practical source strategy:

- federal withholding and employer tax rules from IRS publications such as Publication 15 and Publication 15-T
- state-by-state adapters from official tax agency sources
- employer-specific unemployment and similar rates stored per employer account when a state issues them as notices instead of public universal tables

This is important because some state rates are not universal reference data. They are assigned to a particular employer and must be treated as account-level configuration.

## UI and differentiation

To avoid reproducing the old product too closely, the new application should deliberately diverge in:

- product name
- logo and icons
- typography
- layout and navigation
- color system
- screen composition
- help and onboarding content

The goal is to preserve functional workflows while clearly presenting a new interface and new product identity.

## Suggested migration order

1. Authentication, company selection, user rights, and configuration
2. Chart of accounts and general ledger
3. Customers, vendors, A/R, and A/P
4. Inventory, purchasing, and order workflows
5. Payroll core, tax rules, and timecard
6. Reports, labels, checks, statements, and tax forms
7. Remaining modules such as POS, CRM, ZBA, and property management

## Immediate next implementation step

The starter .NET solution under `E:\BrassLedger` should next grow in three directions:

1. a PostgreSQL-first schema for companies, users, chart of accounts, customers, and vendors
2. a report-and-label inventory extractor so each FRX/LBX asset is cataloged for migration
3. a payroll-tax import subsystem with official-source adapters and versioned rule storage
