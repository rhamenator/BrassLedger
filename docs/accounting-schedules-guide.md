# Accounting schedules

BrassLedger keeps fixed-asset depreciation, prepaid-expense amortization, and amortizing-loan payments in reviewed company-scoped schedules. A schedule calculates expected monthly installments but does not change the ledger by itself.

## Before creating a schedule

Record the originating transaction separately through the appropriate workflow:

- acquire a fixed asset through a bill, payment, or approved journal;
- record a prepaid purchase through a bill, payment, or approved journal; or
- record loan proceeds and the opening liability through an approved journal.

The schedule must not repeat that opening transaction. It creates only depreciation, expense recognition, or loan-payment entries.

New companies receive starter non-control accounts for prepaid expenses, fixed assets, accumulated depreciation, loans payable, depreciation expense, interest expense, and prepaid amortization expense. These are ordinary chart accounts, not reserved account numbers. An existing company retains its chart; starter accounts are added only when their proposed number is unused.

## Review and posting lifecycle

1. In **Ledger → Fixed assets, prepaids, and loans**, create a draft and review every calculated installment.
2. A user with journal-approval authority approves the schedule. Approved schedule assumptions cannot be edited silently.
3. Select a through date and prepare the installments that are due. Each becomes a separate draft in the normal journal review queue.
4. Approve and post those journal drafts. Closed-period and control-account protections are applied again at approval and posting.
5. If a posted installment is wrong, reverse it through the schedule. BrassLedger creates an equal-and-opposite journal and retains the original schedule, journal, and audit records. A completed bank reconciliation must be reopened before a payment included in it can be reversed.

For a fixed asset, **Dispose / retire** calculates book value from original cost less posted, unreversed schedule depreciation through the disposal date. The resulting draft removes asset cost and accumulated depreciation, records optional bank proceeds, and posts the difference to the selected gain or loss account. Prepared depreciation drafts must be resolved first, and depreciation posted after the disposal date must be reversed. A disposal in review or posted prevents additional depreciation drafts. A posted disposal can be reversed through the schedule; reconciled proceeds require reopening the reconciliation first.

Draft schedule edits use optimistic concurrency. Schedule numbers are unique within a company, and every read and mutation is company-scoped and permission-checked.

## Calculation and posting rules

Fixed assets use monthly straight-line book depreciation. The depreciable amount is original cost less residual value. Each installment debits depreciation expense and credits accumulated depreciation. The fixed-asset account is retained on the schedule as the related balance for review; the schedule does not credit the asset cost account.

Prepaids use monthly straight-line amortization with no residual value. Each installment debits the selected expense and credits the prepaid-asset account.

Loans use a monthly effective-interest amortization schedule. The annual percentage rate is divided by twelve; each payment debits loan principal and interest expense and credits the ledger account mapped to the selected bank account. The journal also carries that bank-account identity, so a posted loan payment appears in bank matching and reconciliation. The final installment absorbs currency rounding and reduces scheduled principal to zero.

The first posting date anchors every due date. For example, a January 31 monthly schedule uses February 28 or 29 and then March 31 rather than drifting permanently to the 28th.

## Current boundaries

The schedule workflow supports monthly straight-line depreciation/amortization, monthly fixed-payment loans, and fixed-asset disposal or retirement using posted book depreciation. Acquisition, impairment, accelerated or tax depreciation, like-kind exchanges, trade-ins, irregular loan payments, rate changes, early payoff, and partial-period conventions require their own authorized transaction or a replacement schedule and must not be represented as automated by this workflow. Add those calculation methods as explicit, tested methods rather than changing the meaning of an already approved schedule.

Before live use, reconcile the generated schedule to the executed loan agreement or accounting policy and obtain the organization's required accounting and tax review.
