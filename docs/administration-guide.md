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
- durable named browser-session inventory with individual and all-other-session revocation
- RFC 6238 authenticator MFA with a mandatory password-plus-code enrollment ceremony and configurable role enforcement
- ten high-entropy, hashed, one-use recovery codes with controlled replacement and disablement
- five-minute, hashed MFA login challenges with strict attempt limits, TOTP replay prevention, and security-stamp invalidation
- a recent account-activity view for successful, rejected, and revoked access events
- protected sensitive fields at rest
- data-protection key storage for application cryptography
- security headers in the web application and API
- expiring, hashed, one-use invitations, email verification, and password-reset actions delivered through a protected SMTP outbox
- controlled administrator-assisted MFA recovery after password reauthentication and documented identity verification

Every operator can open **Account security** from the signed-in header. The **Signed-in browsers** table identifies the inferred browser and platform, masked network, authentication strength, last activity, and current browser. Network and user-agent values are protected at rest. An operator can revoke any other row individually; the current row cannot be revoked from that table. Normal sign-out revokes the current durable session. Revoked named-session metadata is retained for 90 days and then removed during a later successful sign-in; immutable authentication audit events remain subject to the deployment's separate audit-retention policy.

Changing a password requires the current password, matching new-password confirmation, and at least 12 characters. A successful change rotates the account security stamp, signs out other browsers, records an audit event, and reissues the current browser's short-lived cookie. **Sign out other sessions** performs the same rotation without changing the password. Use it after a lost device or suspicious activity. Session validation also rejects an inactive account, expired or revoked session, changed security stamp, inactive company membership, changed company role or permissions, or unsatisfied MFA requirement.

### Authenticator MFA

Each operator can enroll a standards-compatible authenticator from **Account security**:

1. Re-enter the current password.
2. Open the `otpauth://` setup link on the authenticator device or enter the Base32 key manually.
3. Save all ten recovery codes in an offline password manager or similarly controlled location. BrassLedger displays the plaintext codes only during enrollment or replacement; the database retains only SHA-256 hashes of 128-bit random values.
4. Enter a current six-digit authenticator code and affirm that the recovery codes were saved.
5. Sign in again. Enabling MFA rotates the security stamp, so every prior browser session is rejected.

An MFA-enabled password login creates a random five-minute challenge whose bearer token is returned only to the browser or API client and whose SHA-256 hash is stored. Password acceptance does not create an authenticated accounting session. The challenge must be completed with either a six-digit TOTP or an unused recovery code. TOTP accepts only the current 30-second step and one adjacent step for bounded clock skew; an accepted time step cannot be replayed. Challenge, time-step, and recovery-code claims are conditional database updates, so concurrent requests cannot redeem the same factor twice. Five failed second-factor attempts lock the account for 15 minutes. Changing the password or rotating the security stamp invalidates every pending MFA challenge.

Password and MFA endpoints share a network-address ceiling of 60 requests per minute. This complements, rather than replaces, the stricter five-failure per-account lockout and avoids treating a modest office behind one NAT address as a single operator. A rejected API request receives HTTP 429, a one-minute `Retry-After`, and a machine-readable error; the browser receives the same wait instruction on the sign-in page.

Recovery codes are one use. **Replace recovery codes** requires the current password plus a valid authenticator or remaining recovery code, deletes every prior code, creates a new set, audits the action, and invalidates other sessions. **Disable authenticator MFA** has the same two-factor reauthentication requirement and deletes all remaining codes.

If an operator loses both the authenticator and every recovery code, an authorized user manager may use **Administrator MFA recovery** only from an MFA-authenticated session. The administrator must select an active MFA-enabled operator in the current company, type the exact username, re-enter the administrator's own password, select the documented identity-verification procedure, and enter a non-sensitive internal case reference. Self-recovery is prohibited, and only another company owner may recover an owner account. Rejected administrator passwords count toward normal lockout and create a failed authentication audit event.

A successful administrator recovery atomically clears the authenticator secret, recovery codes, pending MFA challenges, and unused account-action links; revokes every target session; rotates the target security stamp; and writes target and acting-administrator audit records. If security email is configured, BrassLedger queues a notice to the registered address. Otherwise the administrator must notify the operator through the company's verified out-of-band procedure. The operator signs in again and must enroll a new factor before receiving business permissions when the role requires MFA. Never put identity documents, answers to verification questions, or other sensitive evidence in the case-reference field.

Each company role has a **Require MFA** control in **Administration**. Administrator and Owner/CEO roles require MFA by default; custom roles can require it at creation, and an authorized role manager can change any active role later. Changing the requirement is audited and rotates every assigned operator's security stamp. A password-only operator assigned to a required role receives an authenticated but deliberately restricted session with no business permissions and is directed to **Account security** for enrollment. Company switching applies the destination company's role requirement. Once an account is assigned any active MFA-required role, self-service MFA disablement is unavailable until an authorized role manager removes every such requirement. This prevents a role assignment in a second company from being silently weakened.

The implementation follows [RFC 6238](https://datatracker.ietf.org/doc/html/rfc6238.html), [Microsoft's ASP.NET Core MFA guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/mfa?view=aspnetcore-8.0), and the [OWASP MFA Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multifactor_Authentication_Cheat_Sheet.html). TOTP materially improves password security but is not phishing-resistant; passkeys remain a future stronger factor.

Company access is validated against the operator's active, company-specific membership on every cookie validation. A role in one company does not grant that role in another company, and disabling a membership invalidates a cookie issued for that company.

Every authenticated state-changing API request (`POST`, `PUT`, `PATCH`, or `DELETE`) also requires an antiforgery request token from `GET /api/antiforgery/token`, obtained through the same authenticated cookie session and sent in the `X-CSRF-TOKEN` header. This applies uniformly to accounting, payroll, administration, integrations, file imports, sign-out, company switching, and authenticated email-security actions. Anonymous sign-in, MFA-challenge, recovery, and one-time action endpoints instead use credential validation, short-lived challenge or action tokens where applicable, and dedicated rate limits because no authenticated browser authority is being exercised.

### Invitations, verified email, and password recovery

Authorized user managers invite operators from **Administration**. An invitation creates an inactive account and inactive membership, then queues a one-use link through configured security email. The recipient verifies the address and chooses the password; BrassLedger no longer exposes an administrator-selected-password account-creation path. Existing operators use **Account security** to verify their current address. Replacing an address requires password reauthentication, invalidates existing sessions, and requires verification of the new address. Only an active account with a verified address is eligible for password recovery.

Recovery responses are intentionally identical for eligible and unknown identifiers. Tokens are random, stored only as hashes, bound to the account security stamp, rate-limited, expiring, and claimed transactionally so concurrent submissions cannot both succeed. A successful reset rotates the security stamp and invalidates prior sessions. See the [security email guide](security-email-guide.md) for secure SMTP configuration, lifetimes, delivery monitoring, retry behavior, and deployment verification.

Before using live confidential books in production, the remaining security work includes:

- phishing-resistant passkeys where deployment requirements justify them
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

## Operational account routing

Accounting workflows use company-scoped operational roles instead of reserved account numbers. Open **Administration → Operational account routing** to review the active account used for cash defaults, transfers, ordinary receivables, retainage receivables, project contract assets and liabilities, payables, inventory, goods received not invoiced (GRNI), deposits and advances, sales tax, payroll, equity, revenue, foreign-exchange gains or losses, and cost or payroll expense. An eligible account must be active, have the required account type, have the required control-account setting, and cannot serve a second operational role. Retained project billing requires a dedicated **Retainage receivable** Asset control; WIP requires separate **Contract asset** and **Contract liability** controls.

Every change has a separate confirmation step, optimistic concurrency protection, and an immutable business-audit event. Existing journal entries are never rewritten. Ordinary receivables, retainage receivables, contract assets and liabilities, payables, inventory, GRNI, deposit, advance, transfer-clearing, sales-tax, and payroll-liability roles cannot be reassigned while the current or replacement account has a nonzero balance; subledger roles also check their open dependent records. Contract controls remain locked while any posted WIP schedule exists, the retainage role remains locked while a posted source billing has unreleased holdback, and GRNI remains locked while an unmatched posted inventory receipt exists. Use an authorized transfer or adjustment workflow to clear and reconcile the old account first. Never change roles by editing the database directly.

Sales fulfillment uses distinct **Sales orders**, **Order fulfillment**, and **Receivables** permissions. The Sales Clerk template prepares, approves, withdraws, and converts quotes and prepares, approves, amends, and cancels commercial order terms without inventory authority. Amendments release reservations and require reapproval; cancellation preserves posted fulfillment. The Warehouse Operator can reserve and ship stock without changing prices or cancelling demand, and a receivables operator can invoice posted shipments. Assign combined authority only when the company's staffing model requires it, and review `sales-quote.*`, `sales-order.*`, and `inventory-shipment.*` events plus `SalesOrderAmendments` before/after evidence in the business audit trail. See the [sales fulfillment guide](sales-fulfillment-guide.md).

Ordinary customer invoices and vendor bills have separate **Preparer**, **Approver**, and **Poster** role templates for Receivables and Payables. The preparer cannot approve the same draft, and its approver cannot post it. Assign those roles to different people when staffing permits. Controllers retain combined authority for small-company administration, but the workflow still refuses those two same-document combinations. Posting is atomic and safe to retry: a completed retry returns the existing document, while any source-document failure rolls back the journal, balances, workflow state, and audit changes together. Direct invoice and vendor-bill posting endpoints and public service operations are intentionally unavailable; integrations create drafts for normal review.

Project scope authority has separate **Project Change Order Preparer** and **Project Change Order Approver** templates. A preparer can draft, correct, submit, or cancel a proposal but cannot decide it; an approver can review it but cannot prepare it. The Controller includes both permissions for small-company staffing, while the service still refuses approval or rejection by that change order's preparer or submitter. Review `project-change-order.*` audit events and the retained before-and-after authorized totals.

Project billing has a separate **Project Billing Preparer** template. It can maintain effective-dated rates and preview, save, correct, or cancel source-derived billing, but cannot approve or post the linked customer invoice. Assign independent Receivables Approver and Receivables Poster operators for those stages. Approval rejects a proposal whose project token, reserved time, posted cost, or retainage source changed after preparation, and it rejects retained billing when no eligible retainage control account is configured. Reject and correct stale billing from a fresh preview rather than bypassing that control. Review `project-billing-*` and linked `subledger-document.project-billing-*` audit events, source reservations, fingerprints, retainage releases, cancellations, voids, and the Projects-page aging-to-control reconciliation.

Project WIP has separate **Project WIP Preparer**, **Project WIP Approver**, and **Project WIP Poster** templates. The poster template also holds reversal authority. A preparer cannot decide their schedule, and an approver cannot post it. Review `project-wip.*` events, preview fingerprints, journal source-document links, reversals, and both Projects-page control reconciliations. Controller and owner roles contain all four permissions for staffing flexibility, but the same-document separation rules still apply.

Ordinary general journals use the same three-stage control: a preparer saves or corrects a draft, a different reviewer approves or rejects it, and someone other than that approver posts it. Rejection requires a reason and current concurrency token. Correction revises the same journal identity, returns it to Draft, clears the current decision, and preserves the prior values and lines in immutable audit evidence. Source-workflow journals remain editable only through their originating receivables, payables, inventory, payroll, banking, or schedule workflow. The direct general-journal posting API and public application-service operations are intentionally unavailable. QuickBooks CSV journal imports create drafts, and journal export excludes drafts, approvals awaiting posting, rejections, and reversals.

Payroll uses the same controlled actor separation. A preparer cannot approve or reject their own run, and an approver cannot post it. Reviewers may reject a draft or approved-but-unposted run with a required reason. Preparers correct and resubmit the same run identity; every prior employee, earning, deduction, tax, source-timecard, and calculation snapshot is retained in an encrypted numbered revision. The legacy aggregate posting API and single-call prepare/approve/post operation are intentionally unavailable.

The equivalent API is `GET` and antiforgery-protected `PUT /api/accounting/operational-account-roles`. The caller needs both user-administration and ledger-management authority and must submit the displayed current account ID plus explicit confirmation. A stale request is rejected rather than overwriting a later administrator's choice.

## Accounting schedules

Fixed assets, prepaids, and loans use reviewed schedules and the normal journal approval queue. Loan payments and asset-disposal proceeds select configured bank accounts so their posted journals remain available to bank matching and reconciliation. Fixed-asset disposals calculate book value from posted depreciation and recognize the resulting gain or loss through selected non-control accounts. The opening acquisition, prepaid purchase, or loan proceeds are separate transactions and must not be duplicated by the schedule. See the [accounting schedules guide](accounting-schedules-guide.md) for calculation, posting, disposal, reversal, account, and current-scope details.

Consolidation groups retain non-overlapping effective-dated basis and ownership periods and require the operator to own every included company. Classify exactly one 100% **Reporting parent**; record a rationale and review date for each controlled subsidiary, combined affiliate, or proportionate interest. Controlled and combined members are included at 100%; only a proportionate interest uses its ownership percentage. Use **Map accounts** to assign each source account to an explicit effective-dated reporting identity and closing, average, or historical translation method; do not assume matching local account numbers have the same meaning. On each noncash counterpart mapping, retain an Operating, Investing, or Financing cash-flow category with its rationale and review date; leave bank ledger mappings Unclassified because they identify cash rather than transaction nature. Use **Trading partners** to map each member's customer or vendor record to the represented member company for a retained effective period. Maintain separately sourced rate types and configure separate CTA and NCI equity reporting accounts in **Policy**. Historical reports use retained basis, ownership, mapping, typed rates, posted ledger activity, and exact-period posted reporting adjustments. Missing rates, an unbalanced source-period selection, an unreviewed legacy basis, an unclassified cash counterpart, or a missing required NCI reclassification leaves the statement package visibly incomplete. Under **Reporting**, strict same-currency exact-match discovery can prepare—but never infer or post—an operator-reviewed elimination; a separate reviewed NCI entry identifies one partially owned controlled subsidiary and never infers acquisition accounting or goodwill. See [Consolidation and currency reporting](consolidation-guide.md) for current calculations, duties, reversals, and limitations.

## Database upgrades

BrassLedger now uses provider-specific EF Core migration assemblies as its primary schema-upgrade mechanism: `BrassLedger.Migrations.Sqlite` and `BrassLedger.Migrations.PostgreSql`. A new empty database is created by the applicable initial migration rather than `EnsureCreated`. Every later model change must have a reviewed migration in both assemblies and is recorded in `__EFMigrationsHistory`.

Prerelease databases created before this transition retain the older ordered `BrassLedgerSchemaVersions` ledger. On their first upgraded startup, BrassLedger validates and completes that ledger, verifies the resulting current schema, and records the applicable EF baseline without replaying the initial migration's table-creation operations. The compatibility path is therefore adoption-only; subsequent changes run through EF migrations. Do not delete either history table.

Each compatibility step and EF migration is transactional. Startup stops before seeding or normal application access if a step fails, an ordered compatibility prerequisite is missing, the required EF baseline is absent, or either history contains a version unknown to the running application. BrassLedger never attempts an automatic downgrade. Restore a verified backup or install a compatible newer application rather than deleting or editing history records manually.

Before upgrading a production installation:

1. Stop all but one application instance.
2. Create and verify a backup that includes the database and data-protection keys.
3. Record the current application release and latest schema version.
4. Start the new release and retain its startup/migration log.
5. Confirm sign-in, company counts, ledger balances, open subledgers, and the latest payroll after startup.
6. Keep the pre-upgrade backup until business-owner reconciliation is complete.

Schema version `2026082509-named-user-sessions` introduces mandatory durable session identifiers. Cookies issued by an older application version do not contain those identifiers and are intentionally rejected after the upgraded application starts; plan for every operator to sign in again.

Schema version `2026082513-operational-account-roles` adds the nullable role column and a company-scoped unique index, then backfills the original starter chart by account number once. After that migration, account numbers are labels only: workflow routing follows the configured role.

The automated infrastructure suite exercises migration-created fresh databases, pre-EF baseline adoption, pre-ledger adoption, independently missing compatibility steps, refusal of future compatibility and EF versions, and business-data retention on SQLite. CI also provisions an isolated PostgreSQL database and runs fresh creation, pre-EF adoption, ordered compatibility upgrades, downgrade refusal, and data-retention checks there. Maintainers can run the PostgreSQL test locally by setting `BRASSLEDGER_TEST_POSTGRES` to an isolated database whose name contains `brassledger_test`; the test deliberately recreates that database's `public` schema.

Maintainer commands and the required two-provider migration review procedure are in [database-migrations.md](database-migrations.md).

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
