# Inventory warehouses, bins, and transfers

BrassLedger maintains an immutable, company-scoped physical location for every inventory movement. Each company has one active default warehouse and each warehouse has one active default bin. Existing installations are adopted into `MAIN/STOCK`; startup refuses to continue if pre-existing bin balances do not equal the item-level on-hand total, rather than silently changing inventory.

## Configuration

A user with **Purchasing** permission can create and edit warehouses and bins in **Operations → Warehouses and bins**. Warehouse codes are unique within a company and bin codes are unique within a warehouse. Addresses include country, state or province, city, postal code, and two street lines. One warehouse is the company default, and one bin in each warehouse is its default.

Default or stock-bearing locations cannot be deactivated. Locations with active sales-order reservations also cannot be deactivated. A bin cannot be moved to another warehouse; create the destination bin and transfer its stock instead. Edits use concurrency tokens, so a stale screen cannot overwrite a newer change.

## Location-aware stock activity

Adjustments and purchase receipts accept an explicit warehouse/bin. When omitted, the active default is used for backward-compatible requests. Sales allocation reserves a precise bin, and shipment posting can consume only the bin recorded on its order lines. A shipment cannot combine lines allocated in different locations. Receipt and shipment reversals return stock to their original location.

The item-level `QuantityOnHand` remains the accounting aggregate and must equal the sum of every bin balance for that item. Mutations update both levels atomically and use optimistic concurrency on the item and location balances.

## Transfers and reversals

A user with **Order fulfillment** permission can transfer a positive quantity between two distinct active bins. The source must contain enough unreserved stock. A transfer creates paired immutable movements at the item's current moving-average unit cost:

- a negative movement at the source;
- an equal positive movement at the destination.

The pair nets to zero quantity and value, does not change the company item total, and does not create a general-ledger journal. References are unique within a company. Reversal requires a date, reason, current concurrency token, and sufficient unreserved stock at the destination; it adds inverse movements and retains the original transfer.

## Operational checks

Before live use, confirm the default warehouse/bin and addresses, transfer staged quantities into each location, and reconcile each item total to its displayed bin balances. Review `inventory-warehouse.saved`, `inventory-bin.saved`, `inventory-transfer.posted`, and `inventory-transfer.reversed` business-audit events. Locations currently model ordinary fungible stock; pick/pack documents, backorder promises, lots, serial numbers, and FIFO layers are separate capabilities.
