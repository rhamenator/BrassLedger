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

Best practices:

- resolve stock exceptions before committing shipment promises
- keep quantity movement aligned with financial posting timing
- review open orders and backorders daily in active environments
- treat printed operational documents as controlled output

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
- confidential data should live in the database, not copied publish folders
- local fallback data under `App_Data` should not be committed to Git
- static site assets should come from `BrassLedger.Web/wwwroot`
- published output under `artifacts` should be treated as disposable packaging

