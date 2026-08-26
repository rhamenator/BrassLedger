# Updated WinBiz/newproj parity audit

The legacy WinBiz/`newproj` materials were reviewed read-only. Their durable
business areas include general ledger, receivables, payables, inventory,
purchasing, sales orders, payroll/timecards, banking, jobs, CRM, forms/labels,
and a property-management cluster.

BrassLedger already has modern, organization-neutral foundations for the core
ledger, customers/invoices, vendors/bills, inventory, purchase-order lines,
partial receiving, exact receipt-to-bill matching, line-based sales quotes with controlled conversion, line-based sales orders with controlled amendment and cancellation,
reservations, partial shipment, moving-average COGS, shipment invoicing, banking,
pick tickets, split packing slips, packing-backed shipments, dated backorder promises,
payroll/tax administration, jobs, and printable output. The
registration/module-purchase switches, global-state startup architecture,
registry coupling, compiled FoxPro artifacts, and bundled business data remain
explicit non-targets.

## Remaining parity work

- customer returns, lots/serials, landed costs, and FIFO layers (warehouses/bins, pick-pack documents, dated backorder promises, and location-aware receiving/reservation/shipping are implemented)
- purchase requisition documents, receipt/bill variance approval, supplier returns, and landed-cost allocation
- payment application and bank reconciliation workflows rather than summaries
- payroll earning/deduction lines, pay runs, liabilities, and posting
- project cost transactions, billing, and work-in-progress reconciliation
- repeatable import validation with provenance and rejection reports
- operator-maintained report/label templates and print queues

Property management is a product-boundary decision, not implicit accounting
parity. If pursued, it should be a separately named vertical product that posts
to BrassLedger through documented accounting contracts.

Manufacturing BOM/MRP behavior belongs in `prodflow-analyzer`; BrassLedger
should receive inventory and accounting transactions from it rather than grow a
second manufacturing engine.
