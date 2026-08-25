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
- optional RFC 6238 authenticator MFA with a mandatory password-plus-code enrollment ceremony
- ten high-entropy, hashed, one-use recovery codes with controlled replacement and disablement
- five-minute, hashed MFA login challenges with strict attempt limits, TOTP replay prevention, and security-stamp invalidation
- a recent account-activity view for successful, rejected, and revoked access events
- protected sensitive fields at rest
- data-protection key storage for application cryptography
- security headers in the web application and API

Every operator can open **Account security** from the signed-in header. Changing a password requires the current password, matching new-password confirmation, and at least 12 characters. A successful change rotates the account security stamp, signs out other browsers, records an audit event, and reissues the current browser's short-lived cookie. **Sign out other sessions** performs the same rotation without changing the password. Use it after a lost device or suspicious activity.

### Authenticator MFA

Each operator can enroll a standards-compatible authenticator from **Account security**:

1. Re-enter the current password.
2. Open the `otpauth://` setup link on the authenticator device or enter the Base32 key manually.
3. Save all ten recovery codes in an offline password manager or similarly controlled location. BrassLedger displays the plaintext codes only during enrollment or replacement; the database retains only SHA-256 hashes of 128-bit random values.
4. Enter a current six-digit authenticator code and affirm that the recovery codes were saved.
5. Sign in again. Enabling MFA rotates the security stamp, so every prior browser session is rejected.

An MFA-enabled password login creates a random five-minute challenge whose bearer token is returned only to the browser or API client and whose SHA-256 hash is stored. Password acceptance does not create an authenticated accounting session. The challenge must be completed with either a six-digit TOTP or an unused recovery code. TOTP accepts only the current 30-second step and one adjacent step for bounded clock skew; an accepted time step cannot be replayed. Challenge, time-step, and recovery-code claims are conditional database updates, so concurrent requests cannot redeem the same factor twice. Five failed second-factor attempts lock the account for 15 minutes. Changing the password or rotating the security stamp invalidates every pending MFA challenge.

Password and MFA endpoints share a network-address ceiling of 60 requests per minute. This complements, rather than replaces, the stricter five-failure per-account lockout and avoids treating a modest office behind one NAT address as a single operator. A rejected API request receives HTTP 429, a one-minute `Retry-After`, and a machine-readable error; the browser receives the same wait instruction on the sign-in page.

Recovery codes are one use. **Replace recovery codes** requires the current password plus a valid authenticator or remaining recovery code, deletes every prior code, creates a new set, audits the action, and invalidates other sessions. **Disable authenticator MFA** has the same two-factor reauthentication requirement and deletes all remaining codes. If an operator loses both the authenticator and every recovery code, do not bypass MFA informally; use a documented administrator identity-verification and recovery process. That administrator recovery workflow is still pending implementation.

The implementation follows [RFC 6238](https://datatracker.ietf.org/doc/html/rfc6238.html), [Microsoft's ASP.NET Core MFA guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/mfa?view=aspnetcore-8.0), and the [OWASP MFA Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multifactor_Authentication_Cheat_Sheet.html). TOTP materially improves password security but is not phishing-resistant; passkeys remain a future stronger factor.

Company access is validated against the operator's active, company-specific membership on every cookie validation. A role in one company does not grant that role in another company, and disabling a membership invalidates a cookie issued for that company.

Before using live confidential books in production, the remaining security work includes:

- verified password-reset and account-invitation delivery
- a controlled administrator MFA reset after documented identity verification
- configurable enforcement of MFA for privileged and sensitive-data roles
- phishing-resistant passkeys where deployment requirements justify them
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
