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

## Verification

At minimum, run:

```bash
TMPDIR=/home/rich/temp dotnet build BrassLedger.slnx -c Release
TMPDIR=/home/rich/temp dotnet test BrassLedger.Infrastructure.Tests/BrassLedger.Infrastructure.Tests.csproj -c Release
```

Also run the PostgreSQL infrastructure suite with `BRASSLEDGER_TEST_POSTGRES` pointed to a disposable database whose name contains `brassledger_test`. Before release, verify a copy of the oldest supported real database, reconcile record counts and control balances, and complete the documented backup/restore rehearsal.
