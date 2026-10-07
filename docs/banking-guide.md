# Banking and reconciliation

BrassLedger records bank work as auditable accounting transactions. Importing a statement does not create journal entries automatically: imported rows are evidence that can be matched to already-posted entries, while transfers and reconciliation adjustments use dedicated posting workflows.

## Import a statement

Open **Ledger**, choose the bank account and format under **Bank statement import and matching**, and select a file. Use **Validate statement** first. Validation parses the file without saving it and reports accepted transactions, duplicates, and rejected rows. **Import statement** saves the accepted rows and an import-batch audit record.

Supported formats are CSV, OFX, QFX, ISO 20022 CAMT.053, and MT940. Files are limited to 10 MB in the web interface.

The CSV header must include:

- `ExternalId`: a stable transaction identifier from the bank
- `Date`: an ISO date such as `2026-08-25`
- `Amount`: a signed, non-zero amount; deposits are positive and withdrawals are negative

Optional CSV columns are `PostedDate`, `Type`, `Payee`, `Memo`, and `Reference`. Fields containing commas may use standard CSV quotes.

The same exact file cannot be imported twice into one bank account. Transactions also have a per-account unique external identifier. To correct a rejected file, fix the rejected rows and import the corrected file; previously accepted external identifiers are reported as duplicates and are not saved again.

## Match statement transactions

An unmatched row offers posted journal entries for the same bank account and signed amount, with the closest posting dates listed first. Select the correct entry and choose **Match**. BrassLedger verifies the journal’s actual bank-account lines, not merely its displayed total, and prevents one journal from being matched more than once.

Use **Unmatch** to correct a match. A match included in a completed reconciliation cannot be changed until that reconciliation has been reopened. Matching never changes the ledger balance.

## Transfer funds between bank accounts

Use **Transfer funds** to choose different source and destination accounts, a date, positive amount, unique reference, and memo. BrassLedger posts one bank entry for each side through account `1050 Bank Transfer Clearing`; the clearing account must net to zero.

A posted transfer can be reversed with a date and reason. Reversal creates and links two new journal entries and restores both bank balances. If either side belongs to a completed reconciliation, reopen the affected reconciliation first. Posted and reversed transfers remain visible in history.

## Reconciliation adjustments

Use a signed amount: positive increases the selected bank balance and negative decreases it. Select a non-control offset account that is not a bank or transfer-clearing account. Every adjustment is a durable record linked to its journal entry and can be reversed with a date and reason. Reconciled adjustments require reopening before reversal.

## Complete and reopen a reconciliation

Choose the bank, statement date, statement closing balance, and the journal entries cleared by the statement. BrassLedger completes the reconciliation only when:

`opening reconciled balance + selected cleared activity = statement closing balance`

The retained report records opening balance, cleared activity, statement balance, book balance including outstanding posted activity, variance, item count, notes, operator, and timestamp.

Reopening retains the report and its item history, records the operator’s required reason, and restores the bank’s preceding reconciliation position. Later completed reconciliations must be reopened first. Once corrections are finished, the same statement date can be reconciled again with the corrected selection.

## Permissions and audit trail

Statement import, matching, transfer posting, adjustment posting, and reconciliation completion require ledger-management access. Reopening and reversing additionally require journal-reversal access. Each material action writes a company-scoped audit entry containing the responsible user and relevant identifiers.
