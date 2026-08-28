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
| Latest implementation checkpoint | `d962b7b fix(accounting): synchronize unapplied refund balances` |
| In progress | Native foreign credit/refund/return handling |
| Current evidence | `6dfa814` completes controlled period-end remeasurement with clean Release/provider/API/component/Chromium/drift/vulnerability gates. `d962b7b` begins the next slice by keeping base-currency `UnappliedAmount` and `TransactionUnappliedAmount` synchronized through refund and exact reversal; clean Release build, focused lifecycle, final SQLite/default 198 plus 10 expected skips, PostgreSQL 208/208, API 37/37, and whitespace gates pass. The exact foreign-refund/credit/return implementation order and retained accounting data are specified below. |

## Immediate queue

1. **Verified — CONSOL-ACQUISITION:** `712f49f` completes the controlled acquisition/disposal, continuing-control change, profit/NCI attribution, schema-v2 purchase-price-allocation, browser and export slice; independent professional review remains separately blocked.
2. **In progress — FX-TRANSACTIONS:** ordinary transaction-currency documents, realized settlement results, and controlled period-end unrealized remeasurement are checkpointed; implement native foreign credit/refund/return handling next.
3. **Pending — ACCEPTANCE-01:** automate the uninterrupted representative-business scenario from clean installation through encrypted restore and audit trace.

## FX transaction implementation order

Use these as independently checkpointed sub-slices; do not treat a partial row as completing `FX-TRANSACTIONS`.

1. **Foreign unapplied-payment refunds:** extend `SubledgerAdjustment` to retain transaction amount/currency, deposit-or-advance carrying amount, cash base amount, selected direct/inverse settlement-rate identity/factor/effective date/source/reference, realized gain/loss, reversal accounting date, and concurrency evidence. A refund request amount is in the payment currency. Reduce both unapplied balances; translate cash at the refund-date rate; release the proportional retained carrying balance; post the difference to the configured realized FX gain/loss role; and reverse every amount exactly. Preserve base-currency request compatibility. The UI must filter compatible rates and label both currencies; API, provider migration/adoption/downgrade, partial/final rounding, insufficient cash, wrong pair/date/company, reconciliation, concurrency, and browser paths must pass.
2. **Foreign invoice credit memos and write-offs:** retain a typed rate-basis policy per adjustment instead of assuming every credit is economically identical. `OriginalDocumentRate` corrects the original transaction, `AdjustmentDateRate` records a new dated concession/return and separately recognizes the carrying-value difference, and `CarryingValue` derecognizes a write-off without inventing a new exchange conversion. Retain transaction, carrying, translated, tax, policy, rate, realized-result, source, review, and reversal evidence; require an explicit supported policy and reject silent base-amount fallback.
3. **Foreign vendor credits:** apply the same retained policy model and exact carrying-value/FX controls to payables, including asset/expense routing, partial credits, later settlement, reversal ordering, and company isolation.
4. **Foreign customer and supplier inventory returns:** first enable transaction currency through fulfillment/receiving cost and revenue provenance; then derive return quantities and transaction credits from the retained source lines. Credit application and cash refund are separate dated events with their own carrying and rate evidence. Prove inventory quantity/cost, sales/expense/tax, AR/AP, cash, FX, project/dimension, and reversal reconciliation together.
5. **Foreign recurring documents and project billing:** select a rate per generated occurrence or posting date—never freeze a template's rate. Retain the generated occurrence's rate evidence and subject it to the normal controlled document and remeasurement lifecycle.

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
| CONSOL-ACQUISITION | Verified | `5f78f2c` proves the typed reviewed event ledger, posting/reversal lifecycle, statement integration and controlled exports; `76fdd16` binds event dates and ownership to actual transitions; `712f49f` adds schema-v2 consideration, identifiable asset/liability, deferred-tax and measurement-period detail with extensible retained JSON, guided browser entry, controlled current/comparative exports, hostile-input rejection and complete gates. Transaction-specific professional conclusions remain under `REVIEW-ACCOUNTING`. |
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

- `d962b7b` — synchronizes retained base and transaction unapplied balances through base-currency customer/vendor refund and exact reversal; focused lifecycle, clean Release, SQLite/default 198 plus 10 expected skips, PostgreSQL 208/208, API 37/37, and whitespace gates pass. This is a prerequisite, not foreign-refund completion.
- `6dfa814` — controlled exact-date open-document remeasurement; frozen closing-rate calculations; independent review and separate posting; unrealized gain/loss; period-close gating; newest-to-oldest exact reversal including zero-adjustment accounting dates; safe historical-activity rejection; persisted payment reversal dates; provider migrations/adoption; guided UI/API; and complete Release/provider/API/component/Chromium/drift/vulnerability gates. This advances but does not finish `FX-TRANSACTIONS`; native foreign credits/refunds/returns remain open.
- `b9e23c5` — ordinary transaction-currency invoices, bills, receipts and disbursements; frozen direct/inverse closing-rate provenance; transaction/base balances; realized settlement gain/loss and exact reversal; safe legacy backfill/adoption/downgrade protection; guided UI/API paths; and complete Release/provider/API/component/Chromium/drift/vulnerability gates. This advances but does not finish `FX-TRANSACTIONS`; unrealized remeasurement and native foreign credits/refunds/returns remain open.
- `712f49f` — schema-v2 purchase-price allocation with consideration components, identifiable assets/liabilities, deferred tax, measurement-period limits and reconciled adjustment history; extension preservation; legacy compatibility; controlled JSON/CSV/Excel/PDF output; browser workflow; and complete Release/provider/API/component/Chromium/drift/vulnerability gates. This verifies `CONSOL-ACQUISITION`; independent professional review remains external.
- `76fdd16` — exact ownership-history coupling for acquisitions, step acquisitions, continuing-control changes and loss of control; prior-interest/NCI consistency; departure/reentry warnings; and focused valid/invalid transition, posting, statement, CSV, API and Chromium evidence. This advances the still-open schema-v2 PPA portion of `CONSOL-ACQUISITION`.
- `5f78f2c` — versioned acquisition, step-acquisition, continuing-control ownership-change, loss-of-control and profit/OCI-attribution schedules; typed reconciliation formulas; effective reporting-account posting; independent preparation/approval/posting; immutable dated reversal; audit/concurrency/closed-period controls; historical nominal carryforward; supporting current/comparative JSON, CSV, Excel, PDF, browser and source-detail output; provider migrations/adoption; and complete build/test/browser/drift/vulnerability gates. This materially advances but does not finish `CONSOL-ACQUISITION`; the narrower remaining controls are recorded above.
- `7552e91` — exact-period/group/framework versioned JSON disclosure packages; financing-liability and supplier-finance reconciliations; extensible narrative categories/fields; source evidence and SHA-256 identity; independent preparation/approval/rejection; provider migrations/adoption; current/comparative JSON, CSV, Excel, PDF, UI and complete build/test/browser/drift/vulnerability gates. This advances, but does not finish, `CONSOL-STATEMENTS`; acquisition/disposal, ownership-change and profit-attribution work remains under `CONSOL-ACQUISITION`.
- `7e1db24` — controlled current/comparative Excel and PDF workbooks/documents, explicit incomplete state, warnings and reconciliation, full source provenance, authenticated API/Web attachments, cross-platform embedded fonts, pagination/repeated headers, and full build/test/browser/drift/vulnerability gates. This advances, but does not finish, `CONSOL-STATEMENTS`.
- `f3cb8e3` — independently generated current/prior consolidated packages, four side-by-side statements, current-minus-prior variance, period-specific presentation/warnings, invalid-chronology rejection, controlled CSV/API/UI paths, and full build/test/browser/drift/vulnerability gates. This advances, but does not finish, `CONSOL-STATEMENTS`.
- `47e24b7` — separate effective-dated reviewed statement sections/captions/order, current/noncurrent support, explicit unconfigured-account handling, provider migrations/adoption, owner-only administration, API, concurrency, and full build/test/browser/drift/vulnerability gates. This advances, but does not finish, `CONSOL-STATEMENTS`.
- `076a0a5` — four consolidated statements, source drill-down, cross-statement controls, reviewed direct-cash-flow classifications, explicit incompleteness warnings, CSV output, provider migrations, and complete build/test/browser/drift/vulnerability gates. This proves the foundation, not the full `CONSOL-STATEMENTS` slice.
- `8e24b56` — explicit reporting-parent, controlled-subsidiary, combined-affiliate, and proportionate-interest bases; reviewed evidence; full controlled consolidation; dedicated, reversible NCI equity presentation; provider migrations and complete build/test/browser/drift/vulnerability gates.
- `496e04f` — reviewed intercompany matching; complete PostgreSQL suite passed 204/204, provider drift checks were clean, and all 12 projects had no known vulnerable direct or transitive NuGet packages.
- See [production-readiness.md](production-readiness.md) for earlier project, dimensions, consolidation mapping/translation/adjustment, accounting, payroll, tax-intake, security, and integration checkpoint evidence.
