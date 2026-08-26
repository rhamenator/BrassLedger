# Purchasing and inventory receiving

BrassLedger provides a controlled requisition-to-purchase-order-to-pay workflow for stocked inventory. A requisitioning operator prepares and submits a multi-line request with a business purpose, optional suggested vendor, requested date, needed-by date, quantities, and estimated costs. A purchasing operator independently approves or rejects it, converts an approved request exactly once into a reviewable purchase-order draft, approves that order, records one or more partial receipts, and can match each receipt to one vendor bill when the invoice agrees with the accepted quantities and prices.

## Requisition controls

Requisitioning users cannot create purchase orders directly. They can edit drafts, submit them for purchasing review, and cancel drafts or submitted requests with a reason. Purchasing users can approve or reject submitted requests, cancel approved requests before conversion, and select the final vendor when creating the purchase-order draft. Conversion preserves the reviewed items, descriptions, quantities, estimated unit costs, total, purpose, and source requisition; the purchase order still requires its own approval before receiving.

Each company has its own unique requisition numbers. Status changes use optimistic concurrency, retain the responsible user and timestamp, and create business-audit events. Rejected and cancelled requests remain visible. A converted requisition cannot be converted again, and requisitions themselves do not post journals or change inventory.

## Accounting flow

A receipt posts its accepted value as a debit to the configured **Inventory asset** control account and a credit to **Goods received not invoiced (GRNI)**. The selected warehouse/bin balance, inventory item's aggregate quantity, and moving-average unit cost change in the same database transaction. The selling price is independent and is never overwritten by receiving or count adjustments.

An exact invoice match posts a debit to GRNI and a credit to the configured **Accounts payable** control account, creates an open vendor bill, and updates the vendor subledger. The resulting bill uses the normal payment, credit, and reporting workflows. BrassLedger rejects a receipt whose quantity exceeds the unreceived order quantity and rejects a receipt or match based on a stale concurrency token.

The starter chart uses account 2050 for GRNI, but workflows resolve the company-scoped operational role rather than relying on that number. Existing companies receive a starter GRNI control account only when neither the role nor proposed number is already present. An administrator cannot reassign the role while unmatched posted receipts remain.

## Corrections

Do not edit generated journals. A fully open, unapplied matched bill can be voided from its receipt. This reverses AP to GRNI, restores the receipt's unmatched state, and preserves the voided bill and both journals. Reverse any bill payments or credits before unmatching it.

An unmatched receipt can then be reversed. Direct reversal is allowed only when it remains the latest valuation event for every affected item. BrassLedger records prior quantity and moving-average cost on each receipt line, reverses Inventory and GRNI, restores those values, and retains receipt and journal history. If later stock movement exists, enter a current compensating inventory adjustment instead of rewriting historical valuation.

All postings enforce company isolation, active vendors and items, closed accounting periods, configured control accounts, balanced entries, unique order/receipt/bill numbers, optimistic concurrency, and business-audit events.

## Current boundary

This workflow intentionally supports separate purchase requisitions, exact approved conversion, exact receipt-level matching, warehouse/bin receiving, and moving-average valuation. Price/quantity variance approval, landed-cost allocation, lots, serial numbers, FIFO layers, and supplier returns remain separate production-readiness work. Do not represent those capabilities as implemented.
