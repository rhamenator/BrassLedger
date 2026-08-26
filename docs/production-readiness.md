# Production readiness

BrassLedger is under active production-readiness development. A successful build or the presence of a screen does not by itself establish that a workflow is suitable for a real business. This report separates verified behavior from work that remains incomplete or requires external review.

## Verified checkpoints

### Controlled inventory purchasing

The inventory purchasing workflow currently supports controlled purchase requisitions, purchase orders, partial receiving, multiple partial supplier invoices for one receipt, independent invoice review, posting, exact reversal, landed-cost allocation, and supplier returns.

Purchase-invoice matching records receipt accrual, actual invoice value, matched quantity, unmatched quantity variance, and price variance separately. Draft, submitted, approved, rejected, posted, cancelled, and reversed states retain actor, time, reason, source, and journal provenance. The preparer cannot decide a match, and the reviewer cannot post it. Supplier returns distinguish invoiced quantities from uninvoiced receipt accruals and can consume quantities across multiple partial invoices.

SQLite and PostgreSQL have separate versioned migrations for this schema. Existing receipt-linked bill and supplier-return data is backfilled. Databases that already contain the new schema but have lost migration history are adopted without replaying incompatible column additions. Downgrade across this migration is deliberately prohibited because the former one-bill-per-receipt schema cannot preserve the new provenance.

Verification completed on 2026-08-26:

- Release solution build: succeeded with zero warnings and zero errors.
- SQLite/default infrastructure suite: 178 tests passed; five PostgreSQL-only tests were skipped in that run.
- Disposable PostgreSQL 16 infrastructure suite: 182 of 182 tests passed with zero skips.
- API integration suite: 32 of 32 tests passed.
- Web component suite: 7 of 7 tests passed.
- Chromium end-to-end suite: 27 of 27 tests passed, including separated purchasing roles and the visually approved snapshots.
- SQLite and PostgreSQL EF model-drift checks: no pending model changes.
- NuGet direct and transitive vulnerability scan: no known vulnerable packages from the configured source.
- Cross-invoice supplier-return regression: passed for a return spanning two supplier invoices with different prices, followed by exact reversal.
- Vendor invoice-number scope regression: two vendors can use the same external number, while a duplicate for one vendor is rejected. Fresh, lost-history adoption, and destructive-downgrade checks pass on SQLite; the PostgreSQL fresh/upgrade/adoption migration test also passes.

The browser test host launches the already-built web assembly directly. This avoids nested MSBuild worker failures and ensures browser tests exercise the same Debug or Release output produced by their build.

## Known limitations and unverified areas

The following areas are not yet proven complete against the project definition of done:

- The representative-business acceptance scenario has not passed as one uninterrupted install-to-restore workflow.
- Complete AR, AP, banking, payroll, inventory, projects, fixed assets, period-end, multi-currency, consolidation, and reporting acceptance matrices remain to be audited against the required positive, negative, authorization, isolation, rounding, concurrency, and reversal cases.
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
