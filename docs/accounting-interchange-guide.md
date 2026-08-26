# Accounting Interchange Guide

## Supported QuickBooks workflows

BrassLedger supports two separate QuickBooks Online workflows:

- secure OAuth 2.0 connection to an Intuit company, followed by controlled inbound API synchronization of accounts, customers, and vendors; and
- operator-run CSV interchange for accounts, customers, vendors, non-control general journals, and zero-tax invoice drafts.

QuickBooks client credentials are installation configuration, not company data. Operators never paste a client secret, access token, or refresh token into the application. The Intuit callback is bound to the initiating BrassLedger operator and active company by a cryptographically random, hashed, one-use state that expires after 10 minutes by default. A durable `AuthorizationPending` connection reservation prevents two operators from exchanging authorization codes into the same connection name. BrassLedger validates the selected company with the Accounting API before it saves the connection. If validation or the database commit fails after Intuit issued a grant, BrassLedger attempts immediate revocation and records whether cleanup was confirmed.

Access and rotating refresh tokens are protected at rest and never returned in connection snapshots. Connect/reconnect completion, refresh, and disconnect use a database-backed, two-minute credential-operation lease in addition to per-process serialization and optimistic credential-version checks. Other application instances are refused before they call the provider; an expired lease can be recovered after a crashed instance and that recovery is audited. A canceled refresh is marked `ReauthorizationRequired` because provider-side rotation may have occurred; a canceled or unconfirmed disconnect is marked `DisconnectPending`. Disconnect retains the protected token until Intuit confirms revocation, allowing an administrator to retry instead of leaving an untracked remote authorization.

### Configure OAuth

Create an app in the Intuit Developer Portal and register the exact callback URL used by the running BrassLedger host. Configure secrets through the deployment secret store or environment, not a committed settings file:

```text
QuickBooksOnline__Enabled=true
QuickBooksOnline__Environment=Sandbox
QuickBooksOnline__ClientId=<Intuit client ID>
QuickBooksOnline__ClientSecret=<Intuit client secret>
QuickBooksOnline__RedirectUri=https://ledger.example.com/integrations/quickbooks-online/callback
```

Use `/api/integrations/quickbooks-online/callback` instead when the Intuit redirect targets `BrassLedger.Api`. Production callbacks must be HTTPS. Sandbox may use HTTP only on a loopback host, and Intuit still requires the registered redirect URI to match exactly. The desktop host normally selects a dynamic loopback port, so OAuth requires either a fixed registered sandbox port or a stable HTTPS deployment; CSV interchange does not have this requirement. Set `Environment=Production` only with production Intuit keys. BrassLedger refuses a connection request whose requested environment differs from the configured environment.

The authorization, token, revocation, sandbox API, and production API endpoints have secure Intuit defaults. Endpoint overrides must be absolute HTTPS URLs without embedded credentials. See Intuit's [OAuth 2.0 authorization documentation](https://developer.intuit.com/app/developer/qbo/docs/develop/authentication-and-authorization/oauth-2.0) and its maintained [.NET OAuth sample](https://github.com/IntuitDeveloper/OAuth2-Dotnet_UsingSDK).

### Controlled API import

In Administration, select accounts, customers, or vendors and choose **Preview import**. The preview fetches a bounded provider snapshot, compares it with company-scoped external-entity links, and records create, update, unchanged, conflict, and rejection counts without changing accounting records. **Import previewed snapshot** is enabled only for that entity and connection. A commit is rejected unless the same operator previewed the exact SHA-256 snapshot during the preceding 30 minutes and no later commit attempt superseded that preview.

The first committed import creates stable links from the Intuit entity ID to the BrassLedger entity ID. A repeated identical snapshot is unchanged rather than duplicated. A remote-only change can update a linked master record; any local change since the previous synchronization prevents overwrite and becomes a visible conflict. If both systems changed the record, it also remains a conflict. QuickBooks Accounts Receivable and Accounts Payable accounts require explicit control-account mapping and are never created automatically. Inactive, malformed, unsupported, missing, or natural-key-colliding records are retained as issues rather than silently deleted or coerced. Every preview, rejected commit, provider failure, and successful commit has a durable company-scoped run record and business-audit event.

### Explicit mappings

Choose **Review mappings** for the selected connection and data type to link a QuickBooks account, customer, or vendor to a record that already exists in the active BrassLedger company. The review is a durable, same-operator preview of the exact provider snapshot. Saving a mapping re-fetches QuickBooks, verifies the SHA-256 snapshot is no more than 30 minutes old, checks the expected current link at both ends, and rejects stale, duplicate, cross-company, or cross-type selections. The operator must have both external-connection administration permission and the applicable ledger, receivables, or payables permission.

Account targets must have the same BrassLedger account classification. An Intuit Accounts Receivable or Accounts Payable account can only map to the corresponding configured operational A/R or A/P control account; ordinary accounts cannot map to control accounts, and A/R cannot be mapped to another asset control such as inventory or vendor advances. Account numbers do not determine these purposes. Customer and vendor mappings cannot cross entity types. A local target already linked to a different Intuit record must be unlinked deliberately before it can be reused.

Creating, replacing, or removing a mapping never edits or deletes either business record. Replacement and removal use a separate confirmation step. Successful changes are protected by optimistic concurrency, appear in synchronization history as mapping changes rather than imports, and create immutable business-audit events containing company-safe identifiers and the reviewed snapshot. Removing a link does not disconnect OAuth and does not delete the remote or local record. Run a new import preview after mapping to verify that the intended conflict is resolved.

The equivalent API operations are antiforgery-protected `POST` requests under `/api/integrations/quickbooks-online`: `/{connectionId}/mappings/{entityType}/preview`, `/mappings`, and `/mappings/remove`. API clients must carry the preview run ID, exact snapshot hash, expected link endpoints, and explicit confirmation flags where applicable.

The API import is intentionally operator-initiated; it is not an unattended background or two-way sync. It currently imports master data only. Imported transactional data continues to use the reviewed CSV/draft workflows described below.

The Ledger page can export and import:

- chart of accounts;
- customers;
- vendors; and
- non-control general journal entries.

It can also export posted, non-voided invoices that have no sales-tax amount, with one CSV row per invoice line. It imports the same zero-tax shape as an atomic batch of invoice drafts; an authorized operator must review, approve, and post each draft through the normal AR workflow before balances change. Intuit currently says its invoice CSV importer cannot be used when sales tax is configured, so BrassLedger rejects nonzero tax and excludes taxable invoices rather than silently dropping their tax. Because Intuit does not accept negative discount lines in this importer, BrassLedger exports a discounted line at its net item amount and leaves quantity/rate blank; reconcile the resulting line detail before import.

The chart export uses `Account Name`, `Type`, `Detail Type`, and `Account Number`, matching Intuit's current chart-import fields. The journal export uses `Journal No.`, `Journal Date`, `Account Name`, `Journal/Description`, `Debits`, and `Credits`, with extra reference and line-description columns available for mapping. QuickBooks Online performs an explicit mapping and review step during upload, so review the mapped fields in QuickBooks rather than assuming header recognition alone.

Invoice CSV requires `Invoice No.`, `Customer`, `Invoice Date`, `Due Date`, and a positive `Item Amount` on every line. `Customer` may be an existing BrassLedger customer number or an unambiguous exact name. Optional `Quantity` and `Rate` must either both be blank or multiply to `Item Amount` to the cent. Optional `Income Account` may be an active revenue-account number or unambiguous exact name; blank values map to the active company's configured **Default revenue** operational account. Optional `Tax Amount` must be blank or zero. All lines sharing an invoice number must use the same customer and dates.

Authoritative Intuit references:

- [Import a chart of accounts](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/import-chart-accounts-quickbooks-online/L9Res1eb1_US_en_US)
- [Import journal entries](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/import-journal-entries-quickbooks-online/L4tQBwbs7_US_en_US)
- [Import customers or vendors](https://quickbooks.intuit.com/learn-support/en-us/help-article/customer-list/import-customers-vendors-email-contacts-quickbooks/L12erg8Db_US_en_US)
- [QuickBooks Online import types and ordering](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/common-questions-importing-data-quickbooks-online/L4OYJRFdj_US_en_US)
- [Import multiple invoices](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/import-multiple-invoices/L7E9Xrd8l_US_en_US)
- [QuickBooks Online Accounting API](https://developer.intuit.com/app/developer/qbo/docs/develop)

Intuit's supported data types and subscription restrictions can change. Check the current in-product sample file and mapping screen before a live transfer. BrassLedger's reviewed shapes are not a substitute for Intuit's current sample file.

## Safe import procedure

1. Back up both systems before a live conversion.
2. Import lists in dependency order: accounts, customers, vendors, then transaction data.
3. Select the data type and CSV in BrassLedger.
4. Choose **Validate only**. BrassLedger parses every row, resolves required accounts, verifies dates and money, checks journal balance and control-account restrictions, and reports the number that would import without changing accounting records.
5. Correct every reported error and validate again.
6. Choose **Import CSV** only after the preview is clean.
7. For invoices and general journals, review the generated drafts and use their ordinary approval and posting workflows.
8. Reconcile record counts and control totals in both products.

BrassLedger limits a file to 2 MiB and 1,000 rows and hashes the exact uploaded bytes with SHA-256. Every validation, committed import, rejected batch, and rejected duplicate is retained in the Ledger page's recent batch history with the provider, data type, safe file name, hash, row/result counts, rejection details, operator, and time. The immutable business audit points to the durable batch record. Imports and batch history are company-scoped. Master lists are saved atomically with their batch and audit event. Journal imports must contain at least two balanced lines, use exactly one positive debit or credit per line, and resolve to one active non-control account per line. A committed import creates non-posting general-journal drafts atomically with the batch and audit evidence. Each draft then requires an independent approver and a different poster; exports include only posted journals. A committed file fingerprint and stable source-journal identities prevent retries from creating a second draft or posting the same data twice.

Rejected batches are all-or-none: correct the source file, preserve it as conversion evidence, and validate the corrected file as a new batch. The batch history deliberately retains the rejected filename, hash, counts, and messages so the correction can be explained later.

## Not yet supported

The following are not implemented and must not be represented as available:

- QuickBooks Desktop IIF import or export;
- products/services, classes, locations, taxable invoices, bills, payments, credit memos, journal entries, and opening balances through the API adapter;
- outbound API synchronization, background synchronization, automatic conflict resolution, or two-way synchronization;
- file adapters for Xero, Sage, Wave, FreshBooks, or GnuCash.

QuickBooks Desktop IIF is a tab-separated format with product/version-specific headers and important limitations. BrassLedger will not emit an IIF file until its supported record types have fixtures from Intuit's current import kit and independent import verification. See Intuit's [IIF overview](https://quickbooks.intuit.com/learn-support/en-us/help-article/list-management/iif-overview-import-kit-sample-files-headers/L5CZIpJne_US_en_US) and [IIF import/export guidance](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/export-import-edit-iif-files/L56LT9Z0Q_US_en_US).
