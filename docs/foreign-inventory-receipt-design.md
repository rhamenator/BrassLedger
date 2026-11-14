# Foreign-currency inventory receipt and matched-bill design

This is the implementation contract for the unfinished purchase side of `FX-TRANSACTIONS` sub-slice 7. A foreign purchase order retains its commercial currency, but receiving remains blocked until the entire receipt-to-bill accounting path below is implemented and verified. Do not enable receiving by simply translating the order's unit costs into the two-decimal inventory cost field.

## Required retained facts

- The order and each line retain the transaction currency and agreed transaction unit price. Neither an edit nor a later rate update may relabel an approved amount.
- The receipt stores the selected closing rate ID, effective date, source, direction, frozen rate-to-base, transaction amount, and base inventory/GRNI amount. Each receipt line retains its transaction amount and its allocated base amount, including any cent residual. The receipt's line totals must sum exactly to both header totals and the posted journal by dimension.
- The matched supplier invoice stores the invoice-date selected closing rate and the same frozen provenance. Its lines retain invoice currency amounts, base payable, cleared receipt GRNI, price/quantity variance, and rate movement separately. A partial match must release the correct share of the receipt's retained GRNI; the final match takes the residual cents.
- A vendor bill produced by matching must retain the original transaction currency and its own bill-date base carrying amount. Payment and credit paths must consume these retained balances without converting them a second time.

## Accounting invariants

1. Receipt: debit inventory and credit GRNI for identical base amounts. Inventory cost is a base-currency carrying value, not a renamed foreign price.
2. Matched invoice: debit GRNI for the retained receipt carrying amount; credit AP for the invoice amount translated at the frozen bill rate. Explicit price/quantity variance and exchange-rate movement bridge the difference. Do not hide exchange movement in purchase-price variance or inventory cost.
3. A shipment, supplier return, landed-cost allocation, invoice-match reversal, receipt reversal, and report must use the retained currency and carrying values of their sources. Each reversal must undo exactly the original base journal, regardless of the current exchange rate.
4. Within each document and accounting dimension, sum rounded line postings to the posted header. Allocate rounding residual deterministically once; do not independently round a unit cost and then assume quantity times that rounded unit cost equals the translated document amount. The existing two-decimal `InventoryItem.UnitCost` and `InventoryReceiptLine.UnitCost` require an explicit policy for residual carrying value before receiving can be enabled.

## Acceptance cases

- Direct and inverse rate pairs; wrong company, inactive, future, wrong pair or wrong type; a rate changed after posting; closed period and stale token.
- One receipt split across dates/rates and several partial supplier invoices, including a final one-cent residual; one invoice covering several receipt lines with distinct dimensions; quantity and price variance plus a rate change on the same bill.
- Supplier return before billing and after partial billing, full/partial credit or refund, then exact reversal; landed cost and inventory issue after a foreign receipt; cross-company isolation.
- SQLite, PostgreSQL, migration/backfill and downgrade guards, API authorization, browser rate selection, and a rendered report that distinguishes transaction and base amounts.

The current order/receipt input path also rejects quantities that round to zero at the database's four-decimal quantity precision. This prevents a positive-looking quantity from becoming a persisted zero line.
