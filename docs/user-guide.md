# BrassLedger User Guide

This guide is the main operator reference for the current BrassLedger workspace. It explains how the major modules fit together, what to review before posting activity, and how to keep the books coherent across daily work, payroll, tax processing, and month-end review.

## Workspace overview

BrassLedger is organized as one authenticated workspace with multiple business modules. The modules are separate so users can stay focused, but the balances and supporting activity are connected.

Normal navigation:

- Overview
- Modules
- Ledger
- Receivables
- Payables
- Operations
- Payroll
- Projects
- Reporting
- Taxes
- Publish
- Help

A normal day should begin on Overview. Review cash, receivables, payables, payroll, inventory, and open work before new batches are entered.

## Getting started

### First sign-in

Confirm these items before processing live work:

- the correct company is loaded
- the masked tax ID is recognizable
- the fiscal-year start month matches expectations
- the base currency is correct
- the dashboard totals look plausible for the current period

If any of those items are clearly wrong, stop and investigate before entering new activity.

### Daily opening routine

1. Open Overview and check summary totals.
2. Review receivables for urgent cash and customer issues.
3. Review payables for due items and scheduled disbursements.
4. Review operations for open orders, inventory pressure, and fulfillment blockers.
5. Review payroll if a payroll date or tax deposit date is approaching.
6. Review reporting before distributing statements, checks, paychecks, or tax-facing output.

## Module guidance

### Overview

Overview is the control tower for the day. Use it to answer these questions quickly:

- Is cash materially different from expectation?
- Are receivables or payables growing in a way that needs attention?
- Is payroll exposure consistent with the current cycle?
- Are inventory, order, or project counts out of line?
- Are enough reports prepared for review and release?

### Ledger

Ledger is the home for journal entries, accruals, reclasses, and period adjustments that genuinely belong in the general ledger.

Best practices:

- include reference text and effective dates on every batch
- distinguish recurring entries from one-time adjustments
- reconcile control accounts after major posting sessions
- avoid using ledger entries to hide unresolved subledger errors

### Receivables

Receivables manages invoices, customer balances, cash application, and collections support.

Best practices:

- issue invoices with complete customer and document detail
- apply cash promptly and explicitly
- separate disputes from write-offs
- review aging and unapplied cash before period close

### Payables

Payables handles vendor invoices, due dates, credits, approvals, and payment release.

Best practices:

- capture due dates, references, and approval state
- keep vendor credits visible until intentionally applied
- review payment proposals before releasing checks or payments
- reconcile payable aging to the control account

### Operations

Operations covers inventory, order flow, fulfillment, and the documents that accompany physical work.

The current purchasing workflow separates preparation from approval. Create a purchase-order draft with one or more inventory lines, have a purchasing operator approve it, and receive only the quantities physically accepted. Partial receipts remain open. Each receipt updates on-hand quantity and moving-average cost and posts Inventory against GRNI. When the corresponding vendor invoice agrees with that receipt, use **Create matched bill** to clear GRNI into Accounts Payable. See [Purchasing and inventory receiving](purchasing-guide.md) for accounting, correction, and current-boundary details.

Warehouses and bins retain the exact physical location of adjustments, receipts, reservations, shipments, and reversals. Configure addresses and defaults in Operations, then use a reasoned stock transfer to move unreserved quantity between bins without changing company-wide on-hand quantity or posting a journal. See [Inventory warehouses, bins, and transfers](inventory-locations-guide.md).

Sales can first prepare an expiring, priced, line-based quote. Approval locks the offer for conversion; an approved quote can create one draft sales order using the exact customer, items, quantities, prices, discounts, tax, and revenue distributions. Expired quotes cannot be converted, and withdrawn quotes retain their required reason and audit history. A quote never reserves inventory or posts accounting.

The downstream sales workflow separates duties. Sales prepares and approves priced line-based orders; the warehouse reserves available quantities and posts partial or complete shipments; receivables creates an invoice from each exact shipment. Before shipment, Sales can make a reasoned amendment that releases reservations, preserves before-and-after evidence, and requires approval again. Sales can also cancel all open quantity without changing shipment or invoice history; partially fulfilled orders remain pending until their retained shipments are invoiced. Shipment posting relieves inventory to COGS at moving-average cost, while invoice posting records AR, revenue, and sales tax with line-level source provenance. Void a fully open shipment invoice before correcting its physical shipment. See [Sales orders and inventory fulfillment](sales-fulfillment-guide.md).

Best practices:

- resolve stock exceptions before committing shipment promises
- keep quantity movement aligned with financial posting timing
- review open orders and backorders daily in active environments
- treat printed operational documents as controlled output
- reverse or compensate incorrect receipts through the displayed workflow; never edit their generated journals

### Payroll

Payroll covers employee setup, earnings, deductions, liabilities, and tax-facing output.

Best practices:

- verify employee setup before every run cycle
- review gross-to-net and liability reports before finalizing paychecks
- store employer-specific rates and notices separately from general tax content
- treat payroll changes as financially sensitive changes, not casual edits

### Projects

Projects organize activity by job, engagement, or cost-tracking unit.

Best practices:

- align time, material, and billing timing
- review work in progress before month-end
- close completed projects intentionally
- keep margin analysis tied to posted costs and billings

### Reporting and forms

Reporting is where fixed-layout output lives. That includes:

- financial statements
- customer statements
- vendor checks and remittance output
- paychecks and payroll registers
- tax forms and year-end employee forms
- operational forms such as pick tickets and packing slips
- labels for mailing, shipment, routing, or inventory use

Before releasing output:

1. verify the company, period, and filters
2. compare totals to the owning module
3. check page breaks and long-name behavior
4. verify decimal precision and date formatting
5. archive or tag the authored version if it must be reproduced later

### Taxes

Tax handling should separate generally published tax data from employer-specific rates or notices.

Guidance:

- track the source of each update
- keep jurisdiction-specific rules explicit
- distinguish federal, state, and employer-level settings
- review tax-facing reports after updates rather than assuming the import was sufficient

### Publish

Publish is for packaging the application, not for accounting entry. Use it when preparing a Windows, Linux, or macOS release.

Do not use publish output under `artifacts` as the source of truth for source control. Generated builds should be recreated from source and distributed through releases, not committed as day-to-day code.

## Month-end review

A disciplined close usually follows this order:

1. finish operational posting or document intentional deferrals
2. reconcile receivables and payables to their control accounts
3. verify payroll liabilities and related expenses
4. post approved accruals, recurring entries, and reclasses
5. review trial balance, income statement, and balance sheet
6. release final management, tax, and operational output

## Security and data handling

Current expectations:

- authenticated access is required before users can load accounting data
- invitations require a one-use email link; operators choose their own password and activate their company membership
- verify the account email from **Account security** before relying on self-service password recovery
- password-reset requests use the same response for eligible and unknown identifiers and invalidate prior sessions after a successful reset
- review **Signed-in browsers** under **Account security** and individually revoke a browser you no longer control; network values are masked and browser names are approximate
- use **Sign out other sessions** after suspected compromise; this rotates account security and preserves only a newly issued session for the current browser
- if every MFA factor is lost, follow the company's documented identity-verification process and ask an authorized MFA-authenticated administrator to perform controlled recovery
- confidential data should live in the database, not copied publish folders
- local fallback data directories should not be committed to Git
- static site assets should come from `BrassLedger.Web/wwwroot`
- published output under `artifacts` should be treated as disposable packaging
