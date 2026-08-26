# Project and job accounting

BrassLedger can assign accounting and operational lines to a company-scoped project or job. A project is a reporting dimension: the posted general ledger remains the financial source of truth, while project totals are derived from tagged journal lines rather than editable summary balances.

## Project setup and lifecycle

Open **Projects** to create or edit a job number, name, customer, start and expected-end dates, billing method, contract amount, cost budget, and retainage percentage. Job numbers are unique inside the active company. Supported billing-method labels are **Time and materials**, **Fixed price**, **Cost plus**, and **Internal**; the label describes the commercial arrangement but does not itself generate billing or revenue-recognition entries.

Only an active project can receive new activity. BrassLedger rejects missing, closed, or other-company project references in journals, invoices, bills, quotes, sales orders, purchase requisitions, purchase orders, timecards, and payroll runs. Optimistic concurrency prevents one operator from silently overwriting another operator's project changes.

Closing requires a close date, reason, and current concurrency token. A project cannot close while it has an open journal, quote, sales order, purchase order, purchase requisition, draft/submitted/approved payroll timecard, or unposted payroll run. Closing retains the actor, time, reason, and prior activity. Reopening also requires a reason and creates separate audit evidence. Corrective reversals and supplier returns can retain a historical closed-project dimension so closing a job does not make its posted accounting impossible to reverse.

## Assigning activity

The project selector is available on:

- ordinary general-journal lines;
- customer invoice and vendor-bill lines;
- sales quote and sales-order lines;
- purchase-requisition and purchase-order lines; and
- payroll time and earning lines.

The dimension follows the source line through approval, posting, fulfillment, invoice matching, customer and supplier returns, payroll posting, and reversal. Inventory shipments allocate COGS by project. Shipment invoices allocate revenue by project. Purchase invoice matching retains the purchase-order project on bill and variance lines. Payroll allocates gross pay and employer tax/benefit burden across each employee's project earnings; the final allocation absorbs rounding so the project lines reconcile exactly to the payroll posting.

Liability, cash, tax, and other balance-sheet lines may carry a project when they are source-line-specific, but the current project cost and revenue totals intentionally include only expense and revenue accounts.

## Portfolio and ledger reporting

The Projects page reports:

- contract amount and cost budget from approved project setup;
- actual cost from posted expense-account debits less credits;
- revenue from posted revenue-account credits less debits;
- commitments from the unreceived value of active purchase-order lines; and
- margin as revenue less actual cost.

Totals scan the complete posted project ledger using exact decimal values. The on-screen drill-down is deliberately limited to the 250 most recent tagged lines to keep the workspace response bounded. Select a project to filter that recent-line view. Use the journal reference and source module to trace a line back to its authorized transaction.

## QuickBooks CSV interchange

BrassLedger journal and zero-tax invoice CSV files include an optional `Project / Job` column containing the BrassLedger job number. Imports also accept `Project/Job`, `Project Job`, `Project`, or `Class` as header aliases. A populated value must resolve to one active project by job number or unique exact name; ambiguous, closed, foreign-company, and unknown values reject the batch instead of being discarded. Blank values remain unassigned.

QuickBooks products and subscriptions expose project and class tracking differently. Treat this extra column as a controlled BrassLedger round-trip field and explicitly map or preserve it during the QuickBooks review step. A successful CSV parse does not prove that a particular QuickBooks subscription imported the dimension.

## Current boundaries

The following project-accounting capabilities remain future work and must not be represented as complete:

- change-order documents and approval;
- automated progress, milestone, cost-plus, or time-and-materials billing;
- retainage invoicing, release, receivable classification, and aging;
- committed-cost forecasting beyond unreceived purchase-order value;
- WIP schedules and over/under-billing entries;
- percentage-of-completion or other automated revenue recognition;
- project-specific budgets by account, period, phase, task, department, or cost code;
- project invoicing from approved time and reimbursable expenses; and
- full historical project-ledger pagination/export beyond the recent workspace drill-down.

Until those workflows are implemented and tested, record their accounting through controlled general journals or ordinary subledger documents with project assignments, retain the external approval evidence, and reconcile the result manually.
