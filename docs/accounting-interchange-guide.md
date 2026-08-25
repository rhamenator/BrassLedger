# Accounting Interchange Guide

## Supported QuickBooks workflow

BrassLedger currently provides operator-run CSV interchange with QuickBooks Online. It does not connect to an Intuit company, request OAuth tokens, call the QuickBooks Accounting API, or synchronize changes in the background. A saved QuickBooks connection profile is therefore only protected configuration for future adapter work; it is not evidence of a working connection.

The Ledger page can export and import:

- chart of accounts;
- customers;
- vendors; and
- non-control general journal entries.

The chart export uses `Account Name`, `Type`, `Detail Type`, and `Account Number`, matching Intuit's current chart-import fields. The journal export uses `Journal No.`, `Journal Date`, `Account Name`, `Journal/Description`, `Debits`, and `Credits`, with extra reference and line-description columns available for mapping. QuickBooks Online performs an explicit mapping and review step during upload, so review the mapped fields in QuickBooks rather than assuming header recognition alone.

Authoritative Intuit references:

- [Import a chart of accounts](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/import-chart-accounts-quickbooks-online/L9Res1eb1_US_en_US)
- [Import journal entries](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/import-journal-entries-quickbooks-online/L4tQBwbs7_US_en_US)
- [Import customers or vendors](https://quickbooks.intuit.com/learn-support/en-us/help-article/customer-list/import-customers-vendors-email-contacts-quickbooks/L12erg8Db_US_en_US)
- [QuickBooks Online import types and ordering](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/common-questions-importing-data-quickbooks-online/L4OYJRFdj_US_en_US)

Intuit's supported data types and subscription restrictions can change. Check the current in-product sample file and mapping screen before a live transfer. BrassLedger's reviewed shapes are not a substitute for Intuit's current sample file.

## Safe import procedure

1. Back up both systems before a live conversion.
2. Import lists in dependency order: accounts, customers, vendors, then transaction data.
3. Select the data type and CSV in BrassLedger.
4. Choose **Validate only**. BrassLedger parses every row, resolves required accounts, verifies dates and money, checks journal balance and control-account restrictions, and reports the number that would import without changing accounting records.
5. Correct every reported error and validate again.
6. Choose **Import CSV** only after the preview is clean.
7. Reconcile record counts and control totals in both products.

BrassLedger limits a file to 2 MiB and 1,000 rows and hashes the exact uploaded bytes with SHA-256. Every validation, committed import, rejected batch, and rejected duplicate is retained in the Ledger page's recent batch history with the provider, data type, safe file name, hash, row/result counts, rejection details, operator, and time. The immutable business audit points to the durable batch record. Imports and batch history are company-scoped. Master lists are saved atomically with their batch and audit event. Journal imports must contain at least two balanced lines, resolve to one active non-control account per line, and are posted through the normal ledger controls. A committed file fingerprint and stable source-journal identities prevent retries from double-posting the same data.

Rejected batches are all-or-none: correct the source file, preserve it as conversion evidence, and validate the corrected file as a new batch. The batch history deliberately retains the rejected filename, hash, counts, and messages so the correction can be explained later.

## Not yet supported

The following are not implemented and must not be represented as available:

- QuickBooks Online OAuth/API synchronization;
- QuickBooks Desktop IIF import or export;
- products/services, classes, locations, invoices, bills, payments, credit memos, and opening balances through this adapter;
- automatic conflict resolution or two-way synchronization; and
- file adapters for Xero, Sage, Wave, FreshBooks, or GnuCash.

QuickBooks Desktop IIF is a tab-separated format with product/version-specific headers and important limitations. BrassLedger will not emit an IIF file until its supported record types have fixtures from Intuit's current import kit and independent import verification. See Intuit's [IIF overview](https://quickbooks.intuit.com/learn-support/en-us/help-article/list-management/iif-overview-import-kit-sample-files-headers/L5CZIpJne_US_en_US) and [IIF import/export guidance](https://quickbooks.intuit.com/learn-support/en-us/help-article/import-export-data-files/export-import-edit-iif-files/L56LT9Z0Q_US_en_US).
