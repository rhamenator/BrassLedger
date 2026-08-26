# Production readiness

BrassLedger is under active production-readiness development. A successful build or the presence of a screen does not by itself establish that a workflow is suitable for a real business. This report separates verified behavior from work that remains incomplete or requires external review.

## Verified checkpoints

### Controlled inventory purchasing

The inventory purchasing workflow currently supports controlled purchase requisitions, purchase orders, partial receiving, multiple partial supplier invoices for one receipt, independent invoice review, posting, exact reversal, landed-cost allocation, and supplier returns.

Purchase-invoice matching records receipt accrual, actual invoice value, matched quantity, unmatched quantity variance, and price variance separately. Draft, submitted, approved, rejected, posted, cancelled, and reversed states retain actor, time, reason, source, and journal provenance. The preparer cannot decide a match, and the reviewer cannot post it. Supplier returns distinguish invoiced quantities from uninvoiced receipt accruals and can consume quantities across multiple partial invoices.

SQLite and PostgreSQL have separate versioned migrations for this schema. Existing receipt-linked bill and supplier-return data is backfilled. Databases that already contain the new schema but have lost migration history are adopted without replaying incompatible column additions. Downgrade across this migration is deliberately prohibited because the former one-bill-per-receipt schema cannot preserve the new provenance.

Verification completed on 2026-08-26:

- Release solution build: succeeded with zero warnings and zero errors.
- SQLite/default infrastructure suite: 187 tests passed; six PostgreSQL-only tests were skipped in that run.
- Disposable PostgreSQL 16 infrastructure suite: 193 of 193 tests passed with zero skips.
- API integration suite: 33 of 33 tests passed.
- Web component suite: 9 of 9 tests passed.
- Chromium end-to-end suite: 29 of 29 tests passed, including separated purchasing and AR/AP roles, rejected-draft correction and resubmission, and the visually inspected Ledger and Projects snapshots.
- SQLite and PostgreSQL EF model-drift checks: no pending model changes.
- NuGet direct and transitive vulnerability scan: no known vulnerable packages from the configured source.
- Cross-invoice supplier-return regression: passed for a return spanning two supplier invoices with different prices, followed by exact reversal.
- Vendor invoice-number scope regression: two vendors can use the same external number, while a duplicate for one vendor is rejected. Fresh, lost-history adoption, and destructive-downgrade checks pass on SQLite; the PostgreSQL fresh/upgrade/adoption migration test also passes.

The browser test host launches the already-built web assembly directly. This avoids nested MSBuild worker failures and ensures browser tests exercise the same Debug or Release output produced by their build.

### Controlled receivables and payables posting

Customer invoices and ordinary vendor bills enter through draft, approval, and posting workflows; neither the HTTP API nor the public accounting application-service contract exposes a direct ordinary posting operation that bypasses review. Integrations and future server-side components therefore have to create drafts. Built-in Receivables and Payables Preparer, Approver, and Poster roles permit least-privilege assignments. A preparer cannot approve or reject the same document as its reviewer, and its approver cannot post it.

Posting the journal, account and subledger balances, source invoice or bill, workflow state, and audit evidence is one database transaction. A forced source-document insertion failure proves that the earlier journal and balance changes roll back and the workflow remains approved for correction or retry. Repeating a completed request returns the original source-document ID without posting again; concurrent attempts produce exactly one source document and one journal entry. These controls are covered by infrastructure and API authorization regressions on 2026-08-26.

Saving and approving a draft now execute the retained posting payload inside a disposable database transaction and explicitly roll it back, so invalid references, distributions, dates, control-account rules, credit limits, and corrupted payloads are rejected before approval without changing the ledger. An authorized reviewer can reject a draft or approved document with a required reason and optimistic concurrency token. The reason, reviewer, time, and immutable audit event remain visible. A preparer corrects it by saving the same invoice number, or the same vendor and bill number; the workflow is revised in place, returns to Draft, retains before-and-after audit evidence, and must pass independent approval and posting again. SQLite and PostgreSQL migrations add the review fields, support lost-history adoption, and prohibit downgrade that could delete reviewer provenance.

Vendor-scoped external invoice numbers now apply consistently to drafts, recurring templates, generated drafts, posted bills, purchase-invoice matches, and landed-cost bills. Two vendors may both issue `1001`; a second `1001` for the same vendor is rejected before review. Versioned SQLite and PostgreSQL migrations backfill historical workflow identity from retained request data, support lost-history adoption, and block unsafe downgrade.

### Controlled general-journal posting

Ordinary general journals now enter only through draft, approval, and posting; neither the HTTP API nor the public accounting application-service contract exposes the former direct-post operation. The preparer cannot approve or reject the same journal, and its approver cannot post it. Reviewers can reject a draft or approved-but-unposted ordinary journal with a required reason and current concurrency token. Correction revises the same journal identity, resets it to Draft, and retains its previous header, lines, status, and decision in audit evidence. Generated source-workflow journals cannot be edited or rejected through the general-journal screen.

QuickBooks CSV journal imports create controlled drafts atomically with their import batch and provenance instead of changing balances. Invalid line polarity, unavailable or control accounts, imbalance, and duplicates are rejected. Only posted general journals are exported. SQLite and PostgreSQL migrations retain journal review decisions and prohibit destructive downgrade. Service, API, component, and browser regressions cover stale review, self-approval, self-rejection, approver self-posting, correction, posting, reversal, import draft behavior, and the three-user rendered workflow on 2026-08-26.

### Project ledger dimensions

Projects now have company-scoped customer, schedule, billing-method, contract, budget, retainage, lifecycle, concurrency, and audit controls. Only active same-company projects accept new source activity. Dimensions propagate through journals, invoice and bill lines, sales and purchasing documents, shipment cost and revenue, invoice matching, customer and supplier returns, timecards, payroll expense allocation, and reversals. Closing is blocked by open orders, requisitions, or timecards; reopening requires a reason. Posted expense and revenue lines, rather than editable project summary fields, drive project cost, revenue, and margin. Unreceived purchase-order value drives commitments. Exact totals use the full posted project ledger while the workspace drill-down is bounded to 250 recent lines.

QuickBooks journal and zero-tax invoice CSV paths preserve an optional project/job number. Imports reject unknown, ambiguous, inactive, or other-company project references instead of silently dropping them. Versioned SQLite and PostgreSQL migrations preserve source and ledger attribution and prohibit destructive downgrade. Service, API, component, migration, and cross-provider verification cover project isolation, concurrency, lifecycle, accounting derivation, payroll allocation, return and reversal propagation, and CSV round trips on 2026-08-26.

Controlled project change orders now provide draft, submission, independent decision, rejection correction, cancellation, stale-project detection, immutable approved history, and atomic contract/budget revision. Unresolved proposals block close, approved reductions use a new negative proposal, and dedicated preparer and approver roles enforce least privilege. SQLite and PostgreSQL have versioned migrations with lost-history adoption and destructive downgrade protection. Automated billing, retainage accounting, WIP, revenue recognition, phases/cost codes, and full historical export remain incomplete; see the [project accounting guide](project-accounting-guide.md).

Verification completed on 2026-08-26:

- Release solution build: succeeded with zero warnings and zero errors.
- SQLite/default infrastructure suite: 189 tests passed; seven PostgreSQL-only tests were skipped in that run (196 total).
- Disposable PostgreSQL 16 infrastructure suite: 196 of 196 tests passed with zero skips, including concurrent change-order approval applying authorized totals exactly once.
- API integration suite: 33 of 33 tests passed, including mismatched routes, antiforgery enforcement, self-approval rejection, independent approval, and revised totals.
- Web component suite: 9 of 9 tests passed.
- Chromium end-to-end suite: 30 of 30 tests passed, including distinct least-privilege preparer and approver users and the visually inspected Projects baseline.
- SQLite and PostgreSQL EF model-drift checks: no pending model changes.
- All 12 solution projects reported no known vulnerable direct or transitive NuGet packages from the configured source.

## Capability matrix

| Area | Status | Current evidence or principal gap |
| --- | --- | --- |
| Controlled general journals | Implemented and tested | Draft, independent approval, separate posting, rejection, correction, reversal, closed-period controls, audit, and QuickBooks draft import are covered. |
| Ordinary receivables and payables | Partially implemented | Controlled invoice/bill posting and corrections are covered; the complete credits, refunds, recurring, payment-batch, ACH, and document-output acceptance matrix is not. |
| Purchasing and inventory | Partially implemented | Requisitions, orders, partial receiving, matching, landed cost, returns, locations, and core reversals are covered; lots, serials, counts, reorder planning, and all costing variants remain incomplete. |
| Banking and reconciliation | Partially implemented | Imports, matching, transfers, adjustments, reconciliation, and reopening exist; OFX/QFX/CAMT/MT940 breadth, payment-originating integrations, and full operational rehearsal remain incomplete. |
| Payroll | Partially implemented | Controlled run review/posting/reversal, jurisdiction allocation, project burden, liabilities, and several corrections are covered; filing/e-file outputs and every jurisdiction-specific rule remain incomplete. |
| Tax content | Not production-approved | Versioned schema, intake, validation, provenance, and selected runtime packages exist; all 50 states plus DC and required local content still need source completion, test vectors, approval, and activation. |
| Project/job dimensioning | Implemented and tested | Company isolation, lifecycle, source propagation, ledger actuals, commitments, payroll, returns, reversals, CSV interchange, and recent drill-down are covered. |
| Advanced project accounting | Partially implemented | Controlled change orders are implemented; automated billing, retainage accounting, WIP, revenue recognition, phases/cost codes, and full historical export remain open. |
| Financial and operational reporting | Partially implemented | Catalogs and selected reports exist; every required statement, reconciliation, drill-down, export, print, and cross-report reconciliation has not passed acceptance. |
| Multi-company | Partially implemented | Memberships, secure company context, switching, and isolation tests exist; the complete owner/admin workflow and cross-module isolation matrix remain incomplete. |
| Multi-currency and consolidation | Partially implemented | Exchange-rate and consolidation foundations exist; transaction remeasurement, gains/losses, ownership periods, intercompany matching/eliminations, and consolidated statements are incomplete. |
| QuickBooks interoperability | Partially implemented | Controlled CSV and bounded inbound API master synchronization exist; broader transaction types, outbound API synchronization, live OAuth rehearsal, and Desktop IIF are incomplete. |
| Other accounting products | Not implemented | Xero, Sage, Wave, FreshBooks, and GnuCash adapters remain open. |
| Security and administration | Partially implemented | Authentication, permissions, MFA, invitations, recovery, sessions, audit, sensitive-field protection, and company context exist; complete hosted security assessment, key-rotation rehearsal, alerts, and privacy controls remain open. |
| Database lifecycle and recovery | Partially implemented | Versioned SQLite/PostgreSQL migrations and several backup/restore controls are tested; oldest-supported production upgrade and encrypted off-machine clean-install restore remain release blockers. |
| Packaging and release provenance | Partially implemented | Publishing and quality workflows exist; clean installer lifecycle, signing/notarization, SBOM, checksums, and complete provenance gate remain incomplete. |
| Independent professional review | Required before production | Human accounting, payroll-tax, security, accessibility, and release approval has not been completed. |

## Known limitations and unverified areas

The following areas are not yet proven complete against the project definition of done:

- The representative-business acceptance scenario has not passed as one uninterrupted install-to-restore workflow.
- The ordinary AR/AP draft-to-post path is controlled, rejectable, correctable, and retry-safe, but complete AR, AP, banking, payroll, inventory, fixed assets, period-end, multi-currency, consolidation, and reporting acceptance matrices remain to be audited against the required positive, negative, authorization, isolation, rounding, concurrency, and reversal cases.
- Project dimensioning, lifecycle, derived actuals, purchase commitments, recent-ledger drill-down, and controlled change orders are implemented, but automated project billing, retainage accounting, WIP, revenue recognition, phased budgets, and full historical ledger export are not yet complete.
- Tax runtime packages for every state and the District of Columbia require a source-by-source completion audit, executable boundary tests, review, and explicit activation. Captured or LLM-assisted interpretations are not regulatory approval.
- Local payroll-tax coverage, reciprocity, convenience-of-employer rules, filing outputs, and multi-work-location allocation require jurisdiction-specific verification by qualified payroll/tax reviewers.
- QuickBooks and other interchange paths require a capability-by-capability audit of mapping, dry runs, idempotency, duplicate handling, rejection correction, reconciliation totals, and round trips. QuickBooks Online production synchronization additionally depends on provider credentials and approval.
- Clean install, supported historical upgrades, encrypted off-machine backup, restore to a clean installation, uninstall, installer signing/notarization, SBOM, checksums, and release provenance have not all been rehearsed as a release gate.
- Repository-wide formatting verification currently reports pre-existing whitespace debt in files outside the controlled invoice-matching checkpoint. Changed-file whitespace and `git diff --check` pass, but the broader formatting baseline still needs remediation.
- Independent human accounting, payroll-tax, security, accessibility, and release review remains required before production approval.

## External release blockers

- Official tax publications and professional review are required before activating jurisdiction packages for production payroll.
- QuickBooks Online OAuth credentials and provider approval are required for a live synchronization rehearsal.
- Platform signing/notarization credentials are required for distributable Windows and macOS packages.
- A business-approved backup destination and encryption-key custody procedure are required for an off-machine restore rehearsal.

This document must be updated whenever a checkpoint is added, invalidated, or independently verified. Claims move out of the limitations section only when current authoritative evidence covers the full stated workflow.
