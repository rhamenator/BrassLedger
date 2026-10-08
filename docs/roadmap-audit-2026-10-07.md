# Roadmap audit — 2026-10-07

Compares each "Where the project actually stands" claim in [roadmap-to-1.0.md](roadmap-to-1.0.md) with the merged code (main + `codex/tax-content-intake-wip-20260824`, 157 commits). Method: targeted source greps and the in-repo ledger [work-remaining.md](work-remaining.md). It is a reconnaissance, not a test run, so "Present" means the code exists, not that it is verified or complete. The ledger's own estimate is 68% complete.

## Claims that are stale

| Roadmap claim | Finding |
| --- | --- |
| Module pages are read-only; only Taxes and Administration save data | **Stale.** The API has 274 endpoints (65 GET, 177 POST, 32 PUT) covering invoicing, bills, payments, refunds, credits, returns, orders, purchasing, payroll, projects, consolidation, backups and integrations. Razor pages (Receivables, Payables, Operations, Payroll, Projects, Ledger) submit these workflows. |
| No line items on invoices, bills, or orders | **Stale.** 25 line entities, including `SalesInvoiceLine`, `VendorBillLine`, `SalesOrderLine`, `PurchaseOrderLine`, `JournalEntryLine`, `PayrollEarningLine`. |
| Schema from `EnsureCreated` plus hand-written ALTER TABLE; no EF migrations | **Stale.** 47 migrations per provider and tested legacy adoption (corrected in the roadmap). |
| The API exposes only GET snapshot endpoints plus auth | **Stale** (see above). |
| Nothing evaluates tax rule metadata | **Partly stale.** `TaxRuleEvaluator`, `FederalPayrollTaxCalculator` and payroll filing/reporting/payment-file services exist. Coverage of all jurisdictions is open (`TAX-ALL-JURISDICTIONS`, `TAX-LOCAL-SCALE`). |

## Claims that still hold

| Roadmap claim | Finding |
| --- | --- |
| Balances are stored values | **Still true.** `CurrentBalance`, `OpenBalance` and `BalanceDue` are persisted columns, updated by posting code in about 62 places (`+=`/`-=`). No projection-rebuild reconciliation test was found. Invariant 3 is open. |
| Statuses are free-form strings | **Still true.** Entities use `string Status` ("Draft", "Pending", "Closed", ...). No state-machine enums. |
| Automated subledger-to-GL tie-out | **Not found** by search. |
| Idempotency keys on API posts | **Not found.** Optimistic concurrency tokens are present (81 `ConcurrencyToken` fields). |
| Unsigned installers, no SBOM/checksums | Tracked as `SIGNING` (external blocker) and `BACKUP-RELEASE`. |
| OpenTelemetry/structured logging | Not present before this work; health checks, correlation IDs and problem details were added in Phase 0. |

## Present but not in the roadmap's list

Fiscal period service with open/closed status, audit entries (`BusinessAuditEntry`), MFA, account recovery, named sessions, QuickBooks Online OAuth/sync, consolidation (ownership, translation, NCI, acquisitions, statements), foreign-currency documents and remeasurement, project WIP and billing, backups with verification, and SSA W-2 file builders.

## Unfinished work already in progress (do these before new phases)

From [work-remaining.md](work-remaining.md):

1. **FX-TRANSACTIONS sub-slice 6, foreign project billing** — designed, not started. The foreign-currency settlement browser gap in sub-slice 4 is blocked on foreign-currency support in the sales/purchase order pipeline.
2. **ACCEPTANCE-01** — the uninterrupted end-to-end scenario.
3. Pending slices, in the ledger's order: `ARAP-COMPLETE`, `BANK-FORMATS`, `INVENTORY-ADV`, `PAYROLL-FORMS`, `TAX-ALL-JURISDICTIONS`, `TAX-LOCAL-SCALE`, `PROJECT-ADV`, `REPORTS-COMPLETE`, `MULTICOMPANY-AUDIT`, `CONSOL-MATCHING`, `QUICKBOOKS-COMPLETE`, `OTHER-INTEGRATIONS`, `SECURITY-OPERATIONS`, `BACKUP-RELEASE`, `FORMAT-BASELINE`.

External blockers (not repository work): independent tax, accounting, security and accessibility reviews; QuickBooks live credentials; signing certificates; backup custody.

## Gaps against the roadmap's invariants worth scheduling

Derived balances with a reconciliation test, enum status state machines, subledger tie-out, API idempotency keys, a cross-company read/write isolation matrix (`MULTICOMPANY-AUDIT`), and OpenTelemetry.
