# Foreign-currency adjustments and refunds

This is the implementation contract for the unfinished foreign refund, credit, write-off, void, and return portions of `FX-TRANSACTIONS`. It supplements the canonical queue in [work-remaining.md](work-remaining.md). Do not mark the slice verified until the relevant acceptance rows below pass on both providers and through the rendered workflow.

## Accounting invariants

- Never infer that a request amount is in company base currency. Every request and retained event identifies its transaction currency.
- Never recompute historical accounting from the mutable rate master. Copy the selected direct-or-inverse factor, effective date, source, source reference, and rate ID onto the event.
- Keep three concepts separate: transaction amount, released carrying amount, and translated cash or new-credit base amount. They can differ after settlement or remeasurement.
- A partial release uses the current remaining carrying ratio. A final release takes the exact remaining base balance so accumulated two-decimal rounding cannot leave a residual.
- Post the difference between carrying and settlement value explicitly to the configured realized FX gain or loss role. Do not hide it in revenue, expense, cash, AR, AP, deposits, or advances.
- Preserve `UnappliedAmount` with `TransactionUnappliedAmount`, and document `BalanceDue` with `TransactionBalanceDue`, in the same database transaction.
- Reverse the retained event exactly. Do not look up a new rate during reversal.
- Retain the accounting reversal date even when a zero-value path creates no journal.
- Block activity in closed periods, stale concurrency, wrong companies/counterparties/currencies, future or inactive rates, wrong currency pairs, unavailable operational roles, completed reconciliations, and unsafe out-of-order reversal.

## First implementation: unapplied refunds

Extend `RefundUnappliedPaymentRequest` with an optional `ExchangeRateId`, preserving existing callers. The request amount is in the selected payment's `TransactionCurrency`; callers do not submit a second currency value that could disagree with the payment.

For a remaining transaction balance `T`, remaining carrying balance `C`, requested refund `R`, and refund-date factor to base `F`:

```text
carryingReleased = R == T ? C : round(C * R / T, 2, away-from-zero)
cashBase         = round(R * F, 2, away-from-zero)

customer deposit gainLoss = carryingReleased - cashBase
vendor advance gainLoss   = cashBase - carryingReleased
```

A positive `gainLoss` is a credit to realized FX gain; a negative value is a debit to realized FX loss.

Example: a 100 CAD customer deposit carried at 75 USD is partially refunded by 40 CAD when the retained refund-date factor is 0.80. Release 30 USD of customer deposits, debit 2 USD of realized FX loss, and credit cash 32 USD. Remaining balances are 60 CAD and 45 USD. A vendor-advance refund with the same amounts debits cash 32 USD, credits vendor advances 30 USD, and credits realized FX gain 2 USD.

## Retained adjustment header

Add provider-native columns to `SubledgerAdjustments`; do not encode the accounting control amounts only in free-form JSON.

| Field | Meaning / backfill |
| --- | --- |
| `TransactionCurrency` | Three-letter event currency; backfill company base currency. |
| `TransactionAmount` | Refund, credit, write-off, or void amount in transaction currency; backfill `Amount`. |
| `CarryingAmount` | Base carrying value released/restored; backfill `Amount`. |
| `Amount` | Preserve as the event's translated/settled base amount for compatibility; equals cash base for a refund and translated credit base for a dated credit. |
| `RateBasis` | `BaseCurrency`, `OriginalDocumentRate`, `AdjustmentDateRate`, or `CarryingValue`; backfill `BaseCurrency`. |
| `ExchangeRateId` | Optional restrictive link to the selected retained rate. |
| `ExchangeRateToBase` | Frozen factor; backfill `1`. |
| `ExchangeRateEffectiveOn` | Frozen effective date; nullable for legacy/base events. |
| `ExchangeRateSource` / `ExchangeRateSourceReference` | Frozen provenance; backfill company-base label and empty reference. |
| `RealizedGainLoss` | Positive gain / negative loss; backfill `0`. |
| `ReversalDate` | Accounting date of exact reversal; nullable until reversed. |

Add length/precision constraints, the rate foreign key/index, lost-history adoption checks for every material field/index, and explicit downgrade prohibition. Existing adjustment references and journal links remain stable.

`SubledgerAdjustmentSnapshot`, AR/AP adjustment tables, API JSON, and audit details must expose transaction amount/currency, carrying amount, settled base amount, FX result, rate basis/provenance, and reversal date. Actor/time evidence remains separate from accounting dates.

## Credit and write-off policies

Do not use one implicit conversion rule for all credits.

- `OriginalDocumentRate`: a correction to the original transaction. Translate the new credit at the retained original factor, release the proportional current carrying amount, and state the resulting realized difference explicitly.
- `AdjustmentDateRate`: a new dated concession or return. Require a retained active closing rate effective on or before the credit date, translate at that frozen factor, release proportional carrying value, and recognize the difference explicitly.
- `CarryingValue`: required for write-offs. Debit the configured bad-debt/expense amount and credit AR at proportional carrying value; do not invent a new exchange conversion or realized FX result.

The request must explicitly choose a supported policy. The service must reject a policy that is inconsistent with the adjustment kind. A correction/reversal of a posted credit reuses its retained amounts and rate evidence.

Full credit memos need typed lines for revenue/expense, quantity, tax, project, phase/task, cost code, department, class, and source-return provenance. Add a separate `SubledgerAdjustmentLine` table (with an extension JSON object for unforeseen provider/jurisdiction fields) rather than continuing to expand one header amount. Header/line transaction totals, base totals, tax, control-account movement, and journal totals must reconcile before posting.

## Existing integration points

- Requests and snapshots: `BrassLedger.Application/Accounting/TransactionModels.cs` and `BusinessWorkspaceSnapshot.cs`.
- Entity/model/migration adoption: `BusinessEntities.cs`, `BrassLedgerDbContext.cs`, both provider migration projects, and `ServiceCollectionExtensions.cs`.
- Calculation/posting/reversal: `RefundUnappliedPaymentAsync`, `RecordCustomerAdjustmentAsync`, `RecordVendorCreditAsync`, `ReverseSubledgerAdjustmentAsync`, and `CreateAdjustment` in `AccountingTransactionService.cs`; reuse `ResolveTransactionRateAsync` and operational FX roles.
- Workspace projection: both `SubledgerAdjustmentSnapshot` projections in `BusinessWorkspaceService.cs`.
- HTTP: existing `/api/subledger-payments/refund-unapplied` and `/api/subledger-adjustments/reverse` routes; extend bodies without adding a second competing endpoint.
- UI: Receivables and Payables already load retained exchange rates and have `ApplicableRates`, `RateLabel`, currency formatting, and refund editors. Add payment-derived currency display, a compatible-rate selector for foreign payments, and carrying/base preview; preserve the current base-currency path.
- Tests: extend the existing foreign document, customer adjustment/refund, API customer payment, AR/AP component, migration fresh/adoption/downgrade, and Chromium workflows instead of creating disconnected happy-path-only fixtures.

## Acceptance matrix

For customer deposits and vendor advances, prove base currency plus direct and inverse foreign quotes, partial and final refund, gain and loss, final rounding, exact reversal, and subsequent original-payment reversal. Assert cash, deposit/advance control, payment transaction/base balances, bank balances, realized FX roles, journals, audit, snapshots, and rendered provenance.

Negative coverage must include missing/wrong/inactive/future rate; rate from another company; wrong bank/counterparty/currency; amount above transaction balance; insufficient customer-refund cash; duplicate reference; closed refund/reversal period; completed reconciliation; stale/concurrent request; reversal before refund; and original-payment reversal while an active refund exists.

For credits/write-offs and later inventory returns, add policy-specific vectors, tax and dimension reconciliation, remeasurement-before/after interactions, later settlement, chronological reversal, and full source-to-ledger-to-report trace. Independent accounting review remains required before production activation.
