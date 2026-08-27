# Production completion ledger

This is the canonical agent-readable queue for work still required before BrassLedger can be called production-ready. Read it with [production-readiness.md](production-readiness.md), which holds implemented evidence, known limitations, and external blockers. Update both documents whenever a checkpoint changes the truth of a capability.

## Maintenance contract

- Treat `Pending` and `In progress` as unfinished product work, not optional enhancements.
- Move a slice to `Verified` only after its complete end-to-end acceptance path and relevant negative, authorization, isolation, accounting-invariant, effective-date, rounding, concurrency, migration, reversal, and browser evidence pass.
- Record the proving commit and final gate totals when a slice becomes `Verified`.
- Use `External blocker` only when repository work cannot remove the dependency; retain the exact credential, publication, professional approval, or operational rehearsal required.
- Do not delete history to make the queue look shorter. Replace a completed row with a concise evidence link or move details to `production-readiness.md`.
- At the start of an agent session, inspect the worktree and latest commit before trusting the checkpoint note below. At the end of a safe checkpoint, update this file before committing.

## Current checkpoint

| Field | Current value |
| --- | --- |
| Updated | 2026-08-27 EDT |
| Branch | `codex/tax-content-intake-wip-20260824` |
| Last pushed commit | `7552e91 feat(reporting): add audited framework disclosures` |
| In progress | Consolidated acquisition/disposal, ownership-change, and profit-attribution accounting |
| Current evidence | The `7552e91` audited-disclosure checkpoint is pushed and gate-complete: clean out-of-tree Release build; SQLite/default 194 plus 10 expected skips; PostgreSQL 204/204; API 35/35; components 15/15; Chromium 36/36; both EF models clean; all 12 projects free of known vulnerable direct/transitive NuGet packages. Exact-period, independently reviewed versioned JSON packages now cover financing-liability reconciliation, supplier-finance arrangements, extensible categorized notes, SHA-256 identity, audited preparation/review, and period-specific JSON/CSV/Excel/PDF/browser output. |

## Immediate queue

1. **In progress — CONSOL-ACQUISITION:** implement reviewed acquisition/disposal accounting, purchase-price allocation, goodwill, ownership changes, income/NCI attribution, and loss-of-control workflows on the pushed statement/disclosure foundation.
2. **Pending — FX-TRANSACTIONS:** implement transaction-currency documents, rate provenance, settlement remeasurement, and realized/unrealized gains and losses.
3. **Pending — ACCEPTANCE-01:** automate the uninterrupted representative-business scenario from clean installation through encrypted restore and audit trace.

## Remaining capability slices

| ID | Status | Required end state / principal remaining evidence |
| --- | --- | --- |
| ARAP-COMPLETE | Pending | Complete invoice and bill taxes, terms, addresses, attachments, credits, refunds, write-offs, voids, recurring documents, payment batches, checks/ACH, remittances, returned payments, and full reconciliation acceptance. |
| BANK-FORMATS | Pending | Production-grade OFX, QFX, CAMT, and MT940 adapters with mapping, dry run, duplicate/idempotency controls, rejection correction, totals, provenance, and browser coverage. |
| INVENTORY-ADV | Pending | Lots, serial numbers, physical counts, reorder planning, FIFO and average-cost acceptance, and their posting/reversal/isolation matrices. |
| PAYROLL-FORMS | Pending | Filing/e-file-ready 941, 940, W-2/W-3, applicable 1099, new-hire, deposit, quarter-close, and year-close workflows with independently verified calculations. |
| TAX-ALL-JURISDICTIONS | Pending | Audit every state and DC runtime package plus state-required local rules; add official provenance, executable boundary/regression vectors, professional review, and explicit activation. |
| TAX-LOCAL-SCALE | Pending | State-by-state intake and effective-dated execution for counties, cities, school districts, and arbitrary user-maintained local jurisdictions. |
| PROJECT-ADV | Pending | Multiple performance obligations, variable consideration, expected-loss provisions, any additional required transaction dimensions, and complete historical export. |
| REPORTS-COMPLETE | Pending | All required financial, aging, bank, inventory, payroll, tax, project, audit, and consolidated reports with filters, drill-down, comparisons, saved layouts, CSV/Excel/PDF, print and email-ready output, and ledger reconciliation. |
| MULTICOMPANY-AUDIT | Pending | Complete owner/admin workflow and cross-module company-isolation matrix for every material operation and report. |
| CONSOL-ACQUISITION | Pending | Reviewed acquisition/disposal accounting, goodwill, purchase-price allocation, changes in ownership, income/NCI attribution, and loss-of-control workflows. |
| CONSOL-MATCHING | Pending | Reviewed partial, settlement-level, fuzzy, cross-currency matching and controlled elimination-line derivation without silent posting. |
| QUICKBOOKS-COMPLETE | Pending | Accounts, customers, vendors, items, classes, invoices, bills, payments, credit memos, journals, and opening balances through robust CSV/IIF and feasible bidirectional QBO synchronization; live OAuth rehearsal remains separately blocked. |
| OTHER-INTEGRATIONS | Pending | Reusable, fully controlled adapters for Xero, Sage, Wave, FreshBooks, and GnuCash. |
| SECURITY-OPERATIONS | Pending | Hosted security assessment, key-rotation rehearsal, operational alerting, retention/privacy controls, and complete authorization/leakage evidence. |
| BACKUP-RELEASE | Pending | Scheduled encrypted off-machine backups, verified clean-install restore with stated RPO/RTO, oldest-supported database upgrade, clean install/uninstall, checksums, SBOM, licenses, and release provenance gates. |
| FORMAT-BASELINE | Pending | Resolve repository-wide pre-existing formatting/whitespace debt and enforce the clean baseline without masking generated-file requirements. |
| ACCEPTANCE-01 | Pending | One automated, uninterrupted scenario proving multi-company setup, opening balances, procure-to-pay, inventory, quote-to-cash, multi-jurisdiction payroll, bank reconciliation, close, company and consolidated statements, QuickBooks exchange, backup, clean restore, and audit trace. |

## External blockers

| ID | Status | Exact dependency |
| --- | --- | --- |
| REVIEW-TAX | External blocker | Qualified payroll-tax review and current official publications before jurisdiction packages are activated. |
| REVIEW-ACCOUNTING | External blocker | Independent accounting review of statements, consolidation, acquisition, currency, and period-end behavior. |
| REVIEW-SECURITY-A11Y | External blocker | Independent hosted security and accessibility assessments. |
| QBO-LIVE | External blocker | Intuit production/sandbox credentials and provider approval for a live OAuth synchronization rehearsal. |
| SIGNING | External blocker | Windows signing certificate and Apple signing/notarization credentials. |
| BACKUP-CUSTODY | External blocker | Business-approved off-machine destination and encryption-key custody/recovery procedure. |

## Recently verified checkpoints

- `7552e91` — exact-period/group/framework versioned JSON disclosure packages; financing-liability and supplier-finance reconciliations; extensible narrative categories/fields; source evidence and SHA-256 identity; independent preparation/approval/rejection; provider migrations/adoption; current/comparative JSON, CSV, Excel, PDF, UI and complete build/test/browser/drift/vulnerability gates. This advances, but does not finish, `CONSOL-STATEMENTS`; acquisition/disposal, ownership-change and profit-attribution work remains under `CONSOL-ACQUISITION`.
- `7e1db24` — controlled current/comparative Excel and PDF workbooks/documents, explicit incomplete state, warnings and reconciliation, full source provenance, authenticated API/Web attachments, cross-platform embedded fonts, pagination/repeated headers, and full build/test/browser/drift/vulnerability gates. This advances, but does not finish, `CONSOL-STATEMENTS`.
- `f3cb8e3` — independently generated current/prior consolidated packages, four side-by-side statements, current-minus-prior variance, period-specific presentation/warnings, invalid-chronology rejection, controlled CSV/API/UI paths, and full build/test/browser/drift/vulnerability gates. This advances, but does not finish, `CONSOL-STATEMENTS`.
- `47e24b7` — separate effective-dated reviewed statement sections/captions/order, current/noncurrent support, explicit unconfigured-account handling, provider migrations/adoption, owner-only administration, API, concurrency, and full build/test/browser/drift/vulnerability gates. This advances, but does not finish, `CONSOL-STATEMENTS`.
- `076a0a5` — four consolidated statements, source drill-down, cross-statement controls, reviewed direct-cash-flow classifications, explicit incompleteness warnings, CSV output, provider migrations, and complete build/test/browser/drift/vulnerability gates. This proves the foundation, not the full `CONSOL-STATEMENTS` slice.
- `8e24b56` — explicit reporting-parent, controlled-subsidiary, combined-affiliate, and proportionate-interest bases; reviewed evidence; full controlled consolidation; dedicated, reversible NCI equity presentation; provider migrations and complete build/test/browser/drift/vulnerability gates.
- `496e04f` — reviewed intercompany matching; complete PostgreSQL suite passed 204/204, provider drift checks were clean, and all 12 projects had no known vulnerable direct or transitive NuGet packages.
- See [production-readiness.md](production-readiness.md) for earlier project, dimensions, consolidation mapping/translation/adjustment, accounting, payroll, tax-intake, security, and integration checkpoint evidence.
