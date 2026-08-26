# Sales orders and inventory fulfillment

BrassLedger provides a controlled quote-to-cash path for stocked inventory. A sales operator can prepare and approve an expiring, priced quote and convert it once into a draft sales order. Sales then approves the order, a warehouse operator separately reserves available stock and posts partial or complete shipments, and a receivables operator creates the customer invoice from an exact posted shipment rather than re-keying quantities.

## Quotes

A quote contains company-scoped customer, item, price, discount, tax, and revenue-account snapshots. It has a quote date, expiration date, notes, preparer, approver, lifecycle timestamps, and optimistic-concurrency token. Drafts can be edited. Approval locks the commercial terms. A draft or approved quote can be withdrawn only with a reason; a withdrawn or converted quote remains immutable and visible.

An approved quote can be converted exactly once and only on or before its expiration date. Conversion creates a draft sales order with copied line terms and a unique database-enforced quote link. It does not approve the order, reserve inventory, move stock, or post a journal. If an item or revenue account became inactive after approval, conversion stops for review instead of silently substituting another record. `sales-quote.draft.saved`, `sales-quote.approved`, `sales-quote.withdrawn`, `sales-quote.converted`, and `sales-order.created-from-quote` audit events preserve the lifecycle.

## Workflow and authority

1. A user with **Sales orders** permission saves a draft containing the customer, dates, items, quantities, prices, line discounts, line tax, and revenue distributions.
2. Sales approval authorizes fulfillment but does not reserve or move inventory.
3. A user with **Order fulfillment** permission sets the total reservation on each unshipped line. Availability subtracts reservations held by other orders. Setting zero releases a reservation.
4. The warehouse ships only reserved quantities. Partial shipments leave the remaining reservation and order balance open.
5. A user with **Receivables** permission creates one invoice from each uninvoiced posted shipment. The invoice retains its order, shipment, shipment-line, order-line, item, and revenue-account provenance.

Sales, fulfillment, and receivables permissions are intentionally separate. Administrators can combine them when staffing requires it, but a warehouse operator cannot approve prices or post accounts receivable merely because that person can move stock.

## Amendments and cancellation

An approved or allocated order can be amended only before any shipment, invoice, return, or cancellation history exists. Quote-derived commercial terms cannot be amended. An amendment requires a reason, stores immutable before-and-after JSON with a sequential revision number, releases all reservations, replaces the reviewed lines atomically, and returns the order to `Draft`. Sales must approve it again before the warehouse can reserve or ship anything.

Sales may cancel every still-open quantity on a draft, approved, allocated, or partially shipped order. Cancellation requires a reason, releases reservations, records cancelled quantity per line, and never changes posted shipments or invoices. A completely unshipped order becomes `Cancelled`. A partially fulfilled order becomes `ClosedPendingInvoice` until every retained shipment is invoiced, then `Closed`. The retained order total, discounts, and tax are prorated from the approved line terms; final shipment invoicing receives the rounding remainder. If all retained shipments were already invoiced separately, the retained total uses their actual active invoice amounts so independently rounded documents still reconcile. Reversing an uninvoiced shipment after cancellation converts its formerly shipped quantity to cancelled demand rather than reopening it for allocation.

## Accounting

Shipment posting credits the configured **Inventory asset** control account and debits **Cost of goods sold** using the item's current moving-average cost. On-hand quantity, reserved quantity, shipped quantity, the inventory movement, COGS journal, shipment, and audit event are saved atomically.

Shipment invoicing debits **Accounts receivable**, credits each line's reviewed revenue account, and credits **Sales-tax payable** for line tax. Discounts and tax are prorated across partial shipments; the final shipment for a line receives any rounding remainder so the invoices reconcile to the approved order amounts. The customer open balance and order invoiced quantities change in the same transaction.

The selling price and moving-average cost remain independent. A shipment never changes the item's average cost.

## Corrections

Do not edit shipment or invoice journals. An invoiced shipment cannot be physically reversed. First void its fully open, unapplied invoice through the normal controlled invoice-void workflow. That clears the shipment's invoice link and restores its uninvoiced quantities while retaining the invoice, its provenance, and all journals. Reversing that invoice void restores the exact link and quantities.

An uninvoiced shipment can be reversed only when it is still the latest valuation event for every affected item. BrassLedger posts an inverse COGS/inventory journal, restores on-hand and reserved quantities, and retains the original shipment. If a later valuation event exists, use a dated customer-return or compensating inventory workflow instead of rewriting history.

All operations enforce company isolation, closed periods, active customers/items/accounts, customer credit limits, unique document numbers, optimistic concurrency, balanced entries, and immutable audit events.

## Migrated header-only orders

Older prerelease databases may contain sales-order headers with no authoritative lines. The migration preserves those records as `LegacyReference`; it does not invent item, price, tax, revenue-distribution, or fulfillment detail. Create a new line-based order before fulfillment.

## Current boundary

This workflow covers line-based quotes, approval, withdrawal, expiration-aware one-time conversion, line-based sales orders, approval, auditable amendment/reapproval, open-quantity cancellation, reservations, partial shipments, moving-average COGS, shipment-derived invoices, and controlled shipment correction. Pick/pack documents, explicit backorder scheduling, customer return authorization and credit/refund coordination, warehouses/bins/lots/serials, and FIFO valuation remain separate production-readiness work. Do not advertise those capabilities as implemented.
