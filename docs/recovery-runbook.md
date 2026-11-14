# Backup and recovery runbook

Use the protected backup API to create a SQLite snapshot, then verify it and run the restore rehearsal. The rehearsal copies the snapshot and its data-protection keys into an isolated staging directory, opens that restored database, runs SQLite integrity checking, and reports core record counts. It never writes over the live database.

The local v2 manifest checks the database and every copied Data Protection key or revocation file. Verification rejects missing, altered, unexpected, or symlinked contents. On Unix it also rejects a backup if the backups root, backup directory, keys directory, database, manifest, or copied key files have group or other access. A mount that cannot reliably report Unix modes fails this check. The service does not change permissions on older backups during verification.

If an older local backup fails only the Unix permission check, first confirm its owner, storage location, ACLs, and custody. In an operating-system maintenance window, set the backups root, backup directory, and keys directory to mode `0700`, and the database, manifest, and copied XML files to mode `0600`; then verify and rehearse it again. Inspect ownership and ACL configuration separately; the mode check does not verify owner identity or parent default ACL policy. On Windows, inspect the inherited file and directory ACLs as part of clean-install acceptance.

The request event for backup creation is included in its SQLite snapshot. The completion event is written to the live database after the file is verified, so a restored snapshot will not contain that later event. Match incident evidence by backup ID and the shared audit event ID across company audit views. A local rehearsal does not prove that machine-bound Windows DPAPI keys can decrypt records on another machine. Encrypted off-machine custody and a clean-install restore remain required before relying on the backup for disaster recovery.

For an actual SQLite recovery, stop every BrassLedger application instance first. Preserve the failed database and current keys, replace the database and keys from a verified backup using an operating-system-level maintenance procedure, start one instance, and verify sign-in, company access, ledger balances, and the latest payroll records. Record the operator, backup ID, time, and verification evidence in the incident record.

PostgreSQL deployments must use the managed database platform's point-in-time recovery and backup service. BrassLedger intentionally does not fall back to copying PostgreSQL data files.
