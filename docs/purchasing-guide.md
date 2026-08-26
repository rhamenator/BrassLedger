# Purchasing and inventory receiving

BrassLedger provides a controlled requisition-to-purchase-order-to-pay workflow for stocked inventory. A requisitioning operator prepares and submits a multi-line request with a business purpose, optional suggested vendor, requested date, needed-by date, quantities, and estimated costs. A purchasing operator independently approves or rejects it, converts an approved request exactly once into a reviewable purchase-order draft, approves that order, records one or more partial receipts, and can match each receipt to one vendor bill when the invoice agrees with the accepted quantities and prices.

## Requisition controls

Requisitioning users cannot create purchase orders directly. They can edit drafts, submit them for purchasing review, and cancel drafts or submitted requests with a reason. Purchasing users can approve or reject submitted requests, cancel approved requests before conversion, and select the final vendor when creating the purchase-order draft. Conversion preserves the reviewed items, descriptions, quantities, estimated unit costs, total, purpose, and source requisition; the purchase order still requires its own approval before receiving.

Each company has its own unique requisition numbers. Status changes use optimistic concurrency, retain the responsible user and timestamp, and create business-audit events. Rejected and cancelled requests remain visible. A converted requisition cannot be converted again, and requisitions themselves do not post journals or change inventory.

## Accounting flow

A receipt posts its accepted value as a debit to the configured **Inventory asset** control account and a credit to **Goods received not invoiced (GRNI)**. The selected warehouse/bin balance, inventory item's aggregate quantity, and moving-average unit cost change in the same database transaction. The selling price is independent and is never overwritten by receiving or count adjustments.

An exact invoice match posts a debit to GRNI and a credit to the configured **Accounts payable** control account, creates an open vendor bill, and updates the vendor subledger. The resulting bill uses the normal payment, credit, and reporting workflows. BrassLedger rejects a receipt whose quantity exceeds the unreceived order quantity and rejects a receipt or match based on a stale concurrency token.

## Supplier returns and vendor credits

Use **Return to supplier** on a posted receipt to authorize exact quantities from its immutable receipt lines. Authorization reserves those quantities but does not move stock or post accounting. A return may then be shipped in one or more parts from the warehouse/bin that physically contains the goods. Shipment removes unreserved stock at the source receipt cost plus any posted landed cost allocated to that line and recalculates the remaining moving-average cost; BrassLedger refuses a return that would make quantity or inventory value negative. The goods vendor's credit is kept separate from that inventory value: it uses the original goods-receipt cost, while capitalized freight or duty that the goods vendor does not owe is posted to purchase-price variance. The shipment table shows both amounts.

The accounting depends on invoice timing. A shipment made before matching the receipt debits GRNI for the goods value; a shipment made after matching debits Accounts Payable for that value. Both credit Inventory for the complete capitalized value and post any nonrecoverable difference to purchase-price variance. A later match bills only the net units retained. The resulting vendor credit first reduces any balance still due on that source bill. Payables can deliberately apply the remaining credit to another open bill for the same vendor or record a cash refund to a mapped bank account. Credit allocation is a non-posting subledger action because the AP reduction was recorded by the physical return; a cash refund posts Cash against AP.

Return authorizations, shipments, allocations, refunds, and reversals retain source receipt, purchase-order line, vendor-bill, inventory-location, journal, actor, timestamp, and reason provenance. Quantities and amounts remain visible as gross history plus returned, credited, applied, refunded, and available values. Company-scoped numbers, permissions, optimistic concurrency, closed-period controls, and duplicate protection apply throughout.

The starter chart uses account 2050 for GRNI, but workflows resolve the company-scoped operational role rather than relying on that number. Existing companies receive a starter GRNI control account only when neither the role nor proposed number is already present. An administrator cannot reassign the role while unmatched posted receipts remain.

## Landed costs

Use **Allocate landed cost** on a posted inventory receipt for freight, customs duty, brokerage, insurance, handling, port fees, inspection, storage, demurrage, or another documented inbound charge. Payables enters one or more positive charge lines, the external vendor bill number and dates, and chooses allocation by retained receipt value, retained quantity, or exact manual line amounts. Manual allocations must include every retained receipt line and reconcile to the charge total; proportional methods assign any rounding remainder to the final line so the result always reconciles.

The saved document is a non-posting draft. Payables submits it, a different purchasing reviewer approves or rejects it with a reason, and a Payables operator other than that reviewer posts the approved allocation. Posting debits Inventory, credits Accounts Payable, creates the linked open vendor bill, adds a zero-quantity valuation transaction for each affected item, and incorporates the allocated amount into moving-average cost. The receipt, charge lines, allocation lines, item cost before and after posting, bill, journal, actors, decisions, and timestamps remain linked and auditable.

BrassLedger rejects duplicate allocation or bill numbers, stale receipt/item/allocation tokens, unsupported or unreconciled charges, cross-company sources, active supplier-return activity, and capitalization after affected stock has moved out. That last restriction is deliberate: the current weighted-average model cannot prove how a late charge should be split between remaining inventory and cost of goods sold after outbound movement. Enter and approve landed cost before selling or otherwise issuing the affected inventory; otherwise use a reviewed current-period inventory/COGS adjustment.

Do not void the generated bill through the generic Payables action. Use **Reverse** on the landed-cost allocation after reversing every payment, credit, or adjustment against its bill. Reversal requires both purchasing and payment-reversal authority, must remain the latest valuation event for every affected item, posts an inverse journal, restores prior item costs, voids the bill, and retains both sides of the history.

## Corrections

Do not edit generated journals. A fully open, unapplied matched bill can be voided from its receipt. This reverses AP to GRNI, restores the receipt's unmatched state, and preserves the voided bill and both journals. Reverse any bill payments or credits before unmatching it.

An unmatched receipt can then be reversed. Direct reversal is allowed only when it remains the latest valuation event for every affected item. BrassLedger records prior quantity and moving-average cost on each receipt line, reverses Inventory and GRNI, restores those values, and retains receipt and journal history. If later stock movement exists, enter a current compensating inventory adjustment instead of rewriting historical valuation.

Corrections follow dependency order. Reverse supplier-credit refunds and manually applied credits before reversing their physical return shipment. A pre-invoice return cannot be reversed after its receipt has subsequently been matched until that matched bill is voided. A physical return must still be the latest valuation event for every affected item. Cancel an unused authorization with a reason; an authorization with a posted shipment can be cancelled only after those shipments are reversed.

All postings enforce company isolation, active vendors and items, closed accounting periods, configured control accounts, balanced entries, unique order/receipt/bill numbers, optimistic concurrency, and business-audit events.

## Current boundary

This workflow supports separate purchase requisitions, exact approved conversion, exact receipt-level matching, warehouse/bin receiving, moving-average valuation, controlled landed-cost allocation, receipt-provenance supplier returns, vendor-credit settlement, and dependency-ordered corrections. Price/quantity variance approval, late landed-cost allocation after outbound movement, lots, serial numbers, and FIFO layers remain separate production-readiness work. Do not represent those capabilities as implemented.
