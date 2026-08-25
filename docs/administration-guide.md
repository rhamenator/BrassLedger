# BrassLedger Administration Guide

This guide covers the administrative side of the application: account hygiene, data location, publish practices, and source-control expectations.

## Security baseline

The current application baseline already assumes:

- authenticated access before accounting data is loaded
- hashed passwords for operator credentials
- a 12-character minimum for administrator-created and self-service password changes
- temporary account lockout after repeated invalid credentials
- per-network login throttling in both the web application and API
- self-service password changes that invalidate previously issued sessions
- a self-service control to sign out every other browser session
- a recent account-activity view for successful, rejected, and revoked access events
- protected sensitive fields at rest
- data-protection key storage for application cryptography
- security headers in the web application and API

Every operator can open **Account security** from the signed-in header. Changing a password requires the current password, matching new-password confirmation, and at least 12 characters. A successful change rotates the account security stamp, signs out other browsers, records an audit event, and reissues the current browser's short-lived cookie. **Sign out other sessions** performs the same rotation without changing the password. Use it after a lost device or suspicious activity.

Company access is validated against the operator's active, company-specific membership on every cookie validation. A role in one company does not grant that role in another company, and disabling a membership invalidates a cookie issued for that company.

Before using live confidential books in production, the remaining security work includes:

- authenticator-based MFA or passkeys and recovery codes
- verified password-reset and account-invitation delivery
- named device/session inventory instead of stamp-based all-other-session revocation
- externalized key management where appropriate
- operational backup and restore procedures
- release approval discipline

## Data handling

Use these rules consistently:

- the database is the source of truth for accounting data
- local SQLite data and key material belong in a writable application data directory and should stay out of Git
- `Storage:DataRoot` can be set explicitly if you need to control where local application data is stored
- `BrassLedger.Web/wwwroot` is the source location for committed static assets
- `artifacts` is a publish output folder and should be disposable

## Database upgrades

BrassLedger records every applied schema step in `BrassLedgerSchemaVersions`. A brand-new empty database is created from the current model and immediately receives the ordered baseline and subsequent version records. A database from a prerelease that predates the ledger traverses the legacy compatibility bridge exactly once, inside the baseline transaction, and is then governed by the same ordered migrations. Normal subsequent startup applies only missing versions; it does not replay the legacy compatibility script.

Each migration and its ledger record commit in one database transaction. Startup stops before seeding or normal application access if a step fails, if a later version appears without its prerequisite, or if the database contains a version unknown to the running application. BrassLedger never attempts an automatic downgrade. Restore a verified backup or install a compatible newer application rather than deleting or editing version records manually.

Before upgrading a production installation:

1. Stop all but one application instance.
2. Create and verify a backup that includes the database and data-protection keys.
3. Record the current application release and latest schema version.
4. Start the new release and retain its startup/migration log.
5. Confirm sign-in, company counts, ledger balances, open subledgers, and the latest payroll after startup.
6. Keep the pre-upgrade backup until business-owner reconciliation is complete.

The automated infrastructure suite exercises fresh creation, pre-ledger adoption, an independently missing ordered migration, refusal of a future version, and business-data retention on SQLite. CI also provisions an isolated PostgreSQL database and runs the creation and incremental-migration scenario there. Maintainers can run the PostgreSQL test locally by setting `BRASSLEDGER_TEST_POSTGRES` to an isolated database whose name contains `brassledger_test`; the test deliberately recreates that database's `public` schema.

## Publishing

A clean publish process looks like this:

1. Build from source.
2. Run the relevant tests.
3. Publish for the target runtime identifier.
4. Smoke-test the published build.
5. Package the publish output for distribution.
6. Store the package in a release channel, not as ordinary source control content.

## Git guidance

Track:

- application source
- tests
- documentation
- authored report assets
- static web assets in source form

Ignore:

- `artifacts`
- `bin`
- `obj`
- local application data directories such as `App_Data`
- local IDE state

## Support checklist

When a user reports a problem:

1. identify the module and workflow affected
2. confirm whether the issue is data, configuration, or application behavior
3. run the related report before attempting a balancing correction
4. capture the environment and release details
5. update documentation if the fix changes operator behavior
