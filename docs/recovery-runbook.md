# Backup and recovery runbook

Use the protected backup API to create a SQLite snapshot, then verify it and run the restore rehearsal. The rehearsal copies the snapshot and its data-protection keys into an isolated staging directory, opens that restored database, runs SQLite integrity checking, and reports core record counts. It never writes over the live database.

For an actual SQLite recovery, stop every BrassLedger application instance first. Preserve the failed database and current keys, replace the database and keys from a verified backup using an operating-system-level maintenance procedure, start one instance, and verify sign-in, company access, ledger balances, and the latest payroll records. Record the operator, backup ID, time, and verification evidence in the incident record.

PostgreSQL deployments must use the managed database platform's point-in-time recovery and backup service. BrassLedger intentionally does not fall back to copying PostgreSQL data files.
