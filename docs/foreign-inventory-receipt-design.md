# Foreign-currency inventory receipt and matched-bill design

This is the implementation contract for the unfinished purchase side of `FX-TRANSACTIONS` sub-slice 7. A foreign purchase order retains its commercial currency, but receiving remains blocked until the entire receipt-to-bill accounting path below is implemented and verified. Do not enable receiving by simply translating the order's unit costs into the two-decimal inventory cost field.

The base-currency invoice-match path also checks the linked order's retained currency when a draft is prepared and again immediately before posting. This protects imported or historically inconsistent receipts from being silently relabeled as base-currency vendor bills.

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

For example, ten items received at CAD 10 each with a receipt rate of USD 0.75 per CAD create USD 75 of inventory and GRNI. If the vendor invoices all ten at CAD 11 each when the bill rate is USD 0.80 per CAD, the payable is USD 88: debit GRNI USD 75, debit purchase-price variance USD 8, debit exchange loss USD 5, and credit AP USD 88. A four-item first invoice would clear USD 30 of GRNI, recognize USD 3.20 of price variance and USD 2 of exchange loss, and create USD 35.20 of AP. The remaining six-item invoice must clear exactly the retained USD 45, including any residual cents from more difficult rates and quantities. These examples define expected journal balancing; they do not authorize receipt posting before the full lifecycle is implemented.

## Acceptance cases

- Direct and inverse rate pairs; wrong company, inactive, future, wrong pair or wrong type; a rate changed after posting; closed period and stale token.
- One receipt split across dates/rates and several partial supplier invoices, including a final one-cent residual; one invoice covering several receipt lines with distinct dimensions; quantity and price variance plus a rate change on the same bill.
- Supplier return before billing and after partial billing, full/partial credit or refund, then exact reversal; landed cost and inventory issue after a foreign receipt; cross-company isolation.
- SQLite, PostgreSQL, migration/backfill and downgrade guards, API authorization, browser rate selection, and a rendered report that distinguishes transaction and base amounts.

The current order/receipt input path also rejects quantities that round to zero at the database's four-decimal quantity precision. This prevents a positive-looking quantity from becoming a persisted zero line.
