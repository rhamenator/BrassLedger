# Roadmap to 1.0

This document is a working prompt for an AI coding agent (or a contributor brief) that
describes the remaining work to take BrassLedger from prerelease to a professional-grade
1.0. Feed it to the agent one phase at a time, and keep the "Where the project actually
stands" section current as phases land.

---

# Mission: Take BrassLedger from prerelease to a professional-grade 1.0

You are working in the BrassLedger repository (.NET, Blazor Server web app + minimal API,
EF Core with SQLite default / PostgreSQL optional, xUnit + bUnit + Playwright tests,
cross-platform installers). Read README.md, docs/architecture-plan.md,
docs/legacy-parity-audit.md, docs/DO-178C-ASSURANCE-POLICY.md, and
docs/RELEASE-READINESS-REVIEW.md before changing anything. These define scope, non-goals,
and the evidence every change must carry.

## Where the project actually stands (verify, don't assume)

- The statements below were audited on 2026-10-07; see [roadmap-audit-2026-10-07.md](roadmap-audit-2026-10-07.md)
  for evidence. The ledger in [work-remaining.md](work-remaining.md) is the authoritative queue
  of unfinished work and takes priority over the phases below.
- Module workflows (invoices, bills, payments, orders, payroll, projects, consolidation) are
  implemented behind roughly 270 API endpoints and the Razor pages. Build on them; do not rebuild.
- Balances (`GeneralLedgerAccount.CurrentBalance`, `Customer/Vendor.OpenBalance`,
  `BankAccount.CurrentBalance`, invoice/bill `BalanceDue`) are still stored values updated at
  posting time, not derived projections, and statuses are still free-form strings.
- No automated subledger tie-out or API idempotency keys were found.
- Schema is managed by provider-specific EF Core migrations (`BrassLedger.Migrations.Sqlite`
  and `BrassLedger.Migrations.PostgreSql`, see docs/database-migrations.md). Legacy
  pre-ledger databases are adopted through `EnsureLegacySchemaCompatibilityAsync`, which is
  covered by upgrade tests in `WorkspaceInitializationTests`.
- All projects target net10.0 (global.json pins the SDK).
- Release installers are unsigned. There are no SBOMs or checksums.

## Non-negotiable accounting invariants (enforce in the domain layer and test them)

1. Every posted journal entry balances (sum of debits = sum of credits, per currency) and
   has at least two lines. Use `decimal` everywhere, with explicit, documented rounding
   (banker's vs. away-from-zero per use case) and currency precision.
2. Posted entries are immutable. Corrections are reversing or adjusting entries that link
   to the original. There is no hard delete of financial records.
3. Every balance a user sees (account, customer, vendor, bank, document open amount) is
   derived from posted entries and applications, or is a projection rebuilt from them and
   verified by a reconciliation test. Remove or demote the stored balance fields.
4. Fiscal periods can be open, soft-closed, or closed. Posting into a closed period is
   rejected. Reopening requires a permission and is audited.
5. Subledgers (AR, AP, inventory, payroll, bank) must tie to their GL control accounts.
   Provide an automated tie-out check and surface any differences in the UI.
6. Every create, update, post, void, and permission change writes an append-only audit
   record (who, when, what, before/after) scoped to the company.
7. All data access is scoped to a company (tenant). Add a test proving that cross-company
   reads and writes fail.
8. Writes are idempotent where retries are plausible (idempotency keys on API posts) and
   use optimistic concurrency tokens on editable aggregates.

## Phased plan

Work the phases in order. Split each phase into as many focused PRs as it needs (for
example, Phase 0 might be: framework upgrade; migrations; observability; CI matrix). Each
PR must pass CI on its own, and a phase is finished only when all its PRs are merged.

### Phase 0: Platform foundation

- Retarget all projects to net10.0 (LTS). Update EF Core, Npgsql, ASP.NET packages and
  the test stack to matching supported versions. Pin SDK via global.json. Enable
  `<Nullable>enable`, `<TreatWarningsAsErrors>`, .NET analyzers at a sensible level, and
  `Directory.Build.props` / central package management (`Directory.Packages.props`).
- (Done: migrations exist; keep the remaining legacy bridge covered by tests.) Replace `EnsureCreated` and the hand-written ALTER TABLE logic with EF Core migrations,
  maintained separately for SQLite and PostgreSQL. Provide a one-time upgrade path for
  existing prerelease databases (detect the legacy schema, then baseline the migration
  history), with a test that upgrades a pre.6-shaped database.
- Add structured logging (Serilog or the built-in logging with JSON output),
  OpenTelemetry traces and metrics, health checks (`/health/live`, `/health/ready`), and a
  global ProblemDetails error handler with correlation IDs.
- (Windows and macOS build-and-test jobs added in dotnet-quality.yml.) Run CI on Ubuntu, Windows, and macOS, plus a PostgreSQL service container job so both
  providers run the infrastructure test suite.

### Phase 1: Core general ledger

- Chart-of-accounts CRUD (hierarchy, account types and subtypes, active/inactive, control
  account flags, no deletes once used).
- Fiscal years and periods, period close and reopen.
- Manual journal entry: draft, validate, post, reverse. Recurring entries. Attachments.
- Trial balance, GL detail, and account inquiry with drill-down, all derived from posted
  lines.
- Opening-balance import.

### Phase 2: Receivables and payables

- Customers and vendors with full CRUD, terms, tax settings, and contacts.
- Invoices and bills with line items (item or GL account, quantity, price, tax, discount).
  Status is an enum state machine (Draft, Approved/Open, PartiallyPaid, Paid, Void).
  Posting generates GL entries.
- Cash receipts and payments with application to one or many documents, unapplied credit,
  credit memos, refunds, and write-offs.
- AR and AP aging, customer statements, 1099 vendor tracking.
- Payment runs that prepare check and ACH batches (NACHA file export). Check printing
  is deferred to Phase 7, which builds the rendering pipeline. Until then, a payment run
  records check numbers and exports a check register.

### Phase 3: Banking

- Bank transaction import (OFX/QFX/CSV with mapping profiles), matching rules,
  reconciliation workflow with a statement-balance proof, and a reconciliation report.
  Imports are idempotent (duplicate detection).

### Phase 4: Inventory, orders, purchasing

- Sales order and purchase order lines, allocation, partial shipment and receipt,
  back-orders.
- A perpetual inventory ledger (receipts, issues, adjustments, transfers) using a
  documented costing method (start with moving average; design for FIFO). COGS posting.
- Inventory valuation report that ties to the GL.
- Manufacturing/BOM stays out of scope (it belongs in prodflow-analyzer). Expose a
  documented inbound transaction contract instead.

### Phase 5: Payroll

- Employees with earnings, deductions, benefits, and direct deposit (sensitive fields
  protected with Data Protection).
- Pay runs: draft, calculate, review, approve, post, and void/reissue.
- Build the withholding calculation engine. The repo currently only stores tax rule
  metadata (`TaxRuleCatalog`, `TaxAdministrationService`); nothing evaluates it. The
  engine must evaluate the versioned TaxRuleSet data (annualization by pay frequency,
  brackets, standard deductions and allowances, wage bases and caps, flat and local
  formulas) with effective-date selection.
- Employer liabilities, a liability payment workflow, and GL posting.
- Pay stubs, W-2/W-3, 941/940 worksheet output, and state equivalents where rules exist.
- Tax tables must come only from cited official sources (IRS Pub 15-T, state agencies),
  with an effective date and provenance on every value. Never invent rates. Add golden
  tests for withholding calculated against published worked examples.

### Phase 6: Projects/jobs

- Cost transactions from AP, payroll, and inventory. Budgets vs. actuals. Time-and-
  materials and fixed-fee billing into AR. WIP schedule that reconciles to the GL.

### Phase 7: Reporting and printable output

- Financial statements (balance sheet, income statement, cash flow, comparative and
  budget variants) generated from the ledger. Every figure drills down to source entries.
- Server-side PDF rendering (e.g. QuestPDF, after checking the license against GPL-3.0)
  for invoices, statements, checks (MICR-ready layout with alignment calibration), pay
  stubs, labels, and tax forms. Templates are versioned. Add check printing to the
  Phase 2 payment runs on top of this pipeline.
- CSV and XLSX export for every grid and report.
- Printable-output regression tests (rendered text plus image or PDF snapshot
  comparisons).

### Phase 8: API, import/export, and integrations

- A versioned REST API (`/api/v1`) with full CRUD and posting commands for every module,
  OpenAPI docs, API tokens or PATs with scoped permissions, rate limiting, pagination,
  filtering, and idempotency keys.
- A data import pipeline (CSV and legacy DBF via a reader in BrassLedger.Tools) with
  validation, a dry-run, rejection reports, and provenance records, as the parity audit
  requires.
- Full company backup/export and restore, plus a verified restore test.

### Phase 9: Security and operations hardening

- Authorization: enforce the permission model per endpoint and per UI action (not just
  per page). Segregation-of-duties options (preparer ≠ approver for payments and pay
  runs).
- MFA (TOTP) and a password policy. Session timeout and anti-forgery protection
  throughout. Optional OIDC sign-in for hosted deployments.
- Secrets via configuration providers (never in repo). Data Protection key protection on
  Linux and macOS (not only DPAPI on Windows).
- Automated scheduled backups for SQLite (online backup API) with retention, and
  documented pg_dump guidance for PostgreSQL.
- A threat model document (STRIDE) covering the desktop-loopback and hosted modes.
- Fix all CodeQL and dependency-review findings. Add a SECURITY.md disclosure timeline.

### Phase 10: UX quality

- Consistent design system: data grids with sort, filter, paging, column chooser and
  saved views. Keyboard-first data entry for journal and invoice lines. Inline validation
  with accessible error summaries. Unsaved-changes guards. Toasts with undo where safe.
  Empty states and loading skeletons.
- Global search (customers, vendors, documents, accounts) and recent items.
- WCAG 2.2 AA. Extend the existing Playwright axe tests to every page and form.
- Localization-ready strings (resx) and culture-aware number and date formatting. Keep
  USD as the default, but make currency precision data-driven.
- Rewrite placeholder copy (e.g. "This is the first real accounting slice...") into
  product-quality text.

### Phase 11: Release engineering

- Code-sign Windows installers and MSIX. Sign and notarize macOS pkgs. Sign the Linux
  .deb or its repo metadata. Make signing conditional on secrets being present so forks
  still build.
- Generate an SBOM (CycloneDX) and SHA-256 checksums for every release asset. Add
  provenance attestation (actions/attest-build-provenance).
- Semantic versioning from tags, an auto-generated CHANGELOG, an in-app About page with
  version and commit, and an in-app update check that notifies only (no forced updates,
  per the architecture plan).
- Assemble the release-readiness review packet described in
  RELEASE-READINESS-REVIEW.md. Do not claim independent review has happened.

## Quality bar for every PR

- Follow .github/pull_request_template.md: requirement link, impact, verification
  commands and results, risk. Keep each PR focused.
- Tests at the lowest effective level: domain unit tests for every invariant (property-
  based tests with FsCheck for balancing and rounding are encouraged), infrastructure
  tests on both SQLite and PostgreSQL, API integration tests, bUnit for components, and
  Playwright for the core journeys: enter invoice → receive payment → reconcile bank →
  close period → print statements.
- Report line and branch coverage with coverlet, naming the tool and scope. Target ≥80%
  on Domain and Application, with no decrease allowed on any PR.
- `dotnet build -warnaserror` and `dotnet test` must pass on all three OSes. Visual
  baselines are updated only intentionally.
- Update the user, admin, and reporting guides in docs/ in the same PR as the feature.
  Keep README accurate. Never describe unbuilt functionality as available.
- Never commit secrets, real personal data, or generated output. Sample data stays
  clearly fictional.

## Scope boundaries

- In scope: everything above.
- Out of scope unless explicitly approved: property management (a separate vertical
  product), manufacturing/MRP (prodflow-analyzer), license or registration gating,
  expiration logic, forced updates.
- When a requirement is ambiguous (e.g. costing method, state tax coverage, multi-
  currency depth), stop and ask, or record the decision as a documented derived
  requirement in the PR. Do not guess silently.

## Definition of done (1.0)

A small business can install BrassLedger on Windows, macOS, or Linux; complete first-run
setup; import opening balances; run a full month of AR, AP, banking, inventory, and
payroll activity; reconcile; close the period; and produce accurate financial statements
and printed documents. Every balance ties to the ledger, every change is audited, the
database upgrades safely between versions, backups restore successfully, CI is green on
all platforms and both databases, release artifacts are signed with an SBOM and
checksums, and the release-readiness review packet is complete.

Start with Phase 0. Before writing code, post a short plan for that phase (files touched,
migration strategy, risks) and wait for confirmation if anything is ambiguous.
