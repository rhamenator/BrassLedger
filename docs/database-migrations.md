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

## Verification

At minimum, run:

```bash
TMPDIR=/home/rich/temp dotnet build BrassLedger.slnx -c Release
TMPDIR=/home/rich/temp dotnet test BrassLedger.Infrastructure.Tests/BrassLedger.Infrastructure.Tests.csproj -c Release
```

Also run the PostgreSQL infrastructure suite with `BRASSLEDGER_TEST_POSTGRES` pointed to a disposable database whose name contains `brassledger_test`. Before release, verify a copy of the oldest supported real database, reconcile record counts and control balances, and complete the documented backup/restore rehearsal.
