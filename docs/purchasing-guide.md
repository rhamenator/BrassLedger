# Purchasing and inventory receiving

BrassLedger provides a controlled purchase-order-to-pay workflow for stocked inventory. A requisitioning operator prepares a multi-line purchase-order draft. A purchasing operator independently approves it, records one or more partial receipts, and can match each receipt to one vendor bill when the invoice agrees with the accepted quantities and prices.

## Accounting flow

A receipt posts its accepted value as a debit to the configured **Inventory asset** control account and a credit to **Goods received not invoiced (GRNI)**. The inventory item's quantity and moving-average unit cost change in the same database transaction. The selling price is independent and is never overwritten by receiving or count adjustments.

An exact invoice match posts a debit to GRNI and a credit to the configured **Accounts payable** control account, creates an open vendor bill, and updates the vendor subledger. The resulting bill uses the normal payment, credit, and reporting workflows. BrassLedger rejects a receipt whose quantity exceeds the unreceived order quantity and rejects a receipt or match based on a stale concurrency token.

The starter chart uses account 2050 for GRNI, but workflows resolve the company-scoped operational role rather than relying on that number. Existing companies receive a starter GRNI control account only when neither the role nor proposed number is already present. An administrator cannot reassign the role while unmatched posted receipts remain.

## Corrections

Do not edit generated journals. A fully open, unapplied matched bill can be voided from its receipt. This reverses AP to GRNI, restores the receipt's unmatched state, and preserves the voided bill and both journals. Reverse any bill payments or credits before unmatching it.

An unmatched receipt can then be reversed. Direct reversal is allowed only when it remains the latest valuation event for every affected item. BrassLedger records prior quantity and moving-average cost on each receipt line, reverses Inventory and GRNI, restores those values, and retains receipt and journal history. If later stock movement exists, enter a current compensating inventory adjustment instead of rewriting historical valuation.

All postings enforce company isolation, active vendors and items, closed accounting periods, configured control accounts, balanced entries, unique order/receipt/bill numbers, optimistic concurrency, and business-audit events.

## Current boundary

This workflow intentionally supports exact receipt-level matching and moving-average valuation. Price/quantity variance approval, landed-cost allocation, warehouses, bins, lots, serial numbers, FIFO layers, purchase requisitions as separate documents, and supplier returns remain separate production-readiness work. Do not represent those capabilities as implemented.
