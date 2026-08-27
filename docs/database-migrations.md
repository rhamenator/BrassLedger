# Database migrations

BrassLedger supports SQLite and PostgreSQL through separate EF Core migration assemblies. This keeps each provider's generated column types, defaults, indexes, and annotations native and independently reviewable.

## Adding a model change

Install the repository-compatible EF tool in a disposable location if it is not already available:

```bash
dotnet tool install --tool-path /home/rich/temp/dotnet-tools dotnet-ef --version 8.0.30
```

After changing the domain model and `BrassLedgerDbContext`, generate the same semantic migration for both providers:

```bash
TMPDIR=/home/rich/temp DOTNET_ROOT=/home/rich/.dotnet-10.0.104 /home/rich/temp/dotnet-tools/dotnet-ef migrations add <MigrationName> \
  --project BrassLedger.Migrations.Sqlite/BrassLedger.Migrations.Sqlite.csproj \
  --startup-project BrassLedger.Migrations.Sqlite/BrassLedger.Migrations.Sqlite.csproj \
  --context BrassLedgerDbContext --output-dir Migrations

TMPDIR=/home/rich/temp DOTNET_ROOT=/home/rich/.dotnet-10.0.104 /home/rich/temp/dotnet-tools/dotnet-ef migrations add <MigrationName> \
  --project BrassLedger.Migrations.PostgreSql/BrassLedger.Migrations.PostgreSql.csproj \
  --startup-project BrassLedger.Migrations.PostgreSql/BrassLedger.Migrations.PostgreSql.csproj \
  --context BrassLedgerDbContext --output-dir Migrations
```

`DOTNET_ROOT` is installation-specific; use the root shown by `dotnet --info` on another development machine. The tool and its scratch database belong under `~/temp`, not in the repository.

## Review requirements

Review both generated migrations before committing. Confirm that they:

- express the same business schema and constraints for both providers;
- preserve existing rows or perform an explicit, tested transformation;
- use safe null/default staging when adding required columns;
- create company-scoped uniqueness and foreign keys where required;
- do not silently drop, truncate, rename, or reinterpret business data;
- have a workable rollback where rollback is safe, and fail explicitly where it is not; and
- leave both model snapshots with no pending model changes.

Never edit `__EFMigrationsHistory` or record a migration that has not actually been applied. The only automatic history adoption is the fixed initial baseline used for databases that already passed the legacy compatibility bridge.

`AddControlledPurchaseInvoiceMatching` is intentionally non-reversible. The older schema permits only one bill per receipt and cannot preserve partial-match, variance, or supplier-return provenance. Restore a verified backup taken before the upgrade instead of attempting to migrate a used database backward across that boundary.

`ScopeVendorBillNumbersByVendor` is also intentionally non-reversible after use. Its predecessor required bill numbers to be unique across an entire company; the corrected model allows different vendors to issue the same number. Restoring the former constraint could delete, misassociate, or reject valid bills, so a pre-upgrade backup is required instead.

`ScopeSubledgerVendorBillNumbersByVendor` carries that same identity rule into invoice and bill drafts, recurring templates, and generated drafts. The migration derives each historical vendor scope from its retained JSON request, keeps invoice numbering company-scoped, and isolates an unexpectedly malformed historical vendor payload under a legacy scope rather than guessing. It cannot safely downgrade after two vendors have used the same number through the approval workflow; restore a pre-upgrade backup instead.

`AddSubledgerRejectionWorkflow` adds the reviewer identity, rejection time, and reason retained by invoice and vendor-bill workflows. Existing rows receive an empty reason and remain otherwise unchanged. Lost-history adoption requires all three columns before recording the migration as present. Downgrade is prohibited because removing them could delete review decisions and their audit provenance; restore a verified pre-upgrade backup instead.

`AddControlledPayrollReview` adds payroll rejection evidence and an encrypted, numbered revision table that preserves every corrected calculation. Existing payroll runs receive an empty rejection reason and otherwise remain unchanged. Lost-history adoption requires all rejection columns, the revision table, its encrypted payload column, and its unique run/revision index. Downgrade is prohibited because it could delete reviewer decisions and historical employee calculations; restore a verified pre-upgrade backup instead.

`AddProjectLedgerDimensions` expands the project master record and adds optional project foreign keys to journal, sales, purchasing, receivables, payables, and payroll earning lines. It maps legacy `Open` and `Billing` projects to `Active`, assigns the time-and-materials billing label where none existed, and links a legacy project to a same-company customer only when its retained customer name identifies that customer. Lost-history adoption requires the project lifecycle columns, representative source and ledger dimensions, and the journal project index. Downgrade is prohibited because removing these columns could delete project attribution and lifecycle evidence; restore a verified pre-upgrade backup instead.

`AddControlledProjectBilling` adds effective-dated rates, source-derived proposals and lines, retainage-release provenance, prepared-project concurrency evidence, and reusable source reservations linked one-to-one with controlled receivables drafts. Lost-history adoption requires all four tables, the proposal fingerprint and prepared-project token, workflow link, and unique company/source reservation index. Downgrade is prohibited because it could delete billing derivation, retainage, rate, reservation, and invoice-workflow evidence; restore a verified pre-upgrade backup instead.

`AddProjectWipRevenueRecognition` adds the effective project recognition method and controlled cumulative WIP schedules with retained cost, contract, completion, earned-revenue, billing, contract-position, fingerprint, actor, decision, posting, and reversal evidence. Existing projects are backfilled to `AsBilled`. New standard charts add separate contract-asset and contract-liability controls during minimum setup. Downgrade is prohibited after use because it could delete period-end accounting conclusions and journal provenance; restore a verified pre-upgrade backup instead.

`AddProjectPhaseCostCodeBudgets` adds project phase/task hierarchies, reusable company cost codes, and effective-period budget and forecast allocations. Lost-history adoption requires all three tables, concurrency fields, hierarchy and company foreign keys, and the uniqueness indexes that protect codes and allocation identity. Downgrade is prohibited because it could delete retained planning, hierarchy, and audit relationships.

`AddProjectPhaseCostCodeLineDimensions` extends journal, invoice, bill, quote, sales-order, requisition, purchase-order, payroll-time, and payroll-earning lines with optional phase and cost-code attribution. Both providers add restrictive foreign keys and lookup indexes without rewriting existing project history. Lost-history adoption verifies representative columns and indexes before recording the migration. Downgrade is prohibited because it could delete retained accounting attribution.

`AddProjectBillingLineDimensions` retains the source phase and cost code on controlled project-billing derivation lines so preview, approval revalidation, invoice creation, corrections, and historical display use the same attribution. Downgrade is prohibited because it could delete retained billing attribution.

`AddTrackingDimensions` adds the company-scoped, hierarchical, effective-dated tracking-value master and optional Department and Class references on journal lines. Codes are unique by company and dimension type; parent relationships remain within the company and type. Lost-history adoption requires the master table, lifecycle and concurrency columns, journal references, and protective indexes before recording the migration. Downgrade is prohibited because it could delete the controlled classifications and retained journal attribution.

`AddTrackingDimensionsToSourceLines` extends invoice, bill, quote, sales-order, requisition, purchase-order, payroll-time, payroll-earning, and project-billing lines with optional Department and Class references. Both providers add restrictive foreign keys and lookup indexes without rewriting existing records. Lost-history adoption verifies both columns and both indexes on every affected source table. Downgrade is prohibited because removing these columns could delete accounting classifications needed to reproduce source-to-ledger posting and historical reversals.

`AddEffectiveDatedConsolidationOwnership` converts consolidation membership from one replaceable company percentage into retained ownership periods. Existing memberships begin at the minimum supported date, both group and period rows receive nonblank concurrency tokens, and the uniqueness constraint moves to group/company/effective-from. Lost-history adoption requires both effective-date columns, both concurrency columns, and the provider-specific ownership index. Downgrade is prohibited because restoring one membership row per company could delete later ownership periods and concurrency evidence.

`AddConsolidationAccountMappings` adds retained effective-dated mappings from each member-company account to an explicit reporting number, name, and type. Restrictive foreign keys preserve the group, company, and source-account provenance. Lost-history adoption requires the mapping table, reporting identity, lifecycle and concurrency columns, and provider-specific source mapping index. Downgrade is prohibited because it would delete the classification evidence used to reproduce consolidated reports.

`AddControlledConsolidationTranslation` separates closing, period-average, and historical exchange-rate evidence; adds source reference, retrieval, approval, and concurrency controls; retains each account mapping's translation policy; and adds the consolidation group's dedicated CTA reporting identity. Existing rates are preserved as active closing rates with nonblank concurrency tokens. Existing asset and liability mappings remain closing, revenue and expense mappings become average, and equity mappings become historical. Lost-history adoption requires the rate-policy and provenance columns, CTA identity, mapping method, and provider-specific typed-rate index. Downgrade is prohibited because it could delete the policy evidence needed to reproduce translated balances.

`AddControlledConsolidationAdjustments` adds the separate reporting ledger for exact-period manual adjustments and explicit intercompany eliminations. Batches retain preparation, independent decision, posting, rejection, concurrency, match, and reversal evidence; lines retain reporting identity and optional source/counterparty member provenance. Restrictive group/company relationships and unique batch/sequence indexes protect attribution. Lost-history adoption requires both tables, lifecycle and reversal columns, company-pair provenance, and both provider-specific control indexes. Downgrade is prohibited because it could delete posted consolidation history and review evidence.

## Verification

At minimum, run:

```bash
TMPDIR=/home/rich/temp dotnet build BrassLedger.slnx -c Release
TMPDIR=/home/rich/temp dotnet test BrassLedger.Infrastructure.Tests/BrassLedger.Infrastructure.Tests.csproj -c Release
```

Also run the PostgreSQL infrastructure suite with `BRASSLEDGER_TEST_POSTGRES` pointed to a disposable database whose name contains `brassledger_test`. Before release, verify a copy of the oldest supported real database, reconcile record counts and control balances, and complete the documented backup/restore rehearsal.
