# Project and job accounting

BrassLedger can assign accounting and operational lines to a company-scoped project or job. A project is a reporting dimension: the posted general ledger remains the financial source of truth, while project totals are derived from tagged journal lines rather than editable summary balances.

## Project setup and lifecycle

Open **Projects** to create or edit a job number, name, customer, start and expected-end dates, billing method, contract amount, cost budget, and retainage percentage. Job numbers are unique inside the active company. Supported billing methods are **Time and materials**, **Fixed price**, **Cost plus**, and **Internal**. The first three drive the controlled billing preview described below; Internal projects cannot create customer billing.

Only an active project can receive new activity. BrassLedger rejects missing, closed, or other-company project references in journals, invoices, bills, quotes, sales orders, purchase requisitions, purchase orders, timecards, and payroll runs. Optimistic concurrency prevents one operator from silently overwriting another operator's project changes.

Closing requires a close date, reason, and current concurrency token. A project cannot close while it has an open journal, quote, sales order, purchase order, purchase requisition, draft/submitted/approved payroll timecard, or unposted payroll run. Closing retains the actor, time, reason, and prior activity. Reopening also requires a reason and creates separate audit evidence. Corrective reversals and supplier returns can retain a historical closed-project dimension so closing a job does not make its posted accounting impossible to reverse.

## Controlled change orders

After project activity or change-order history exists, revise the authorized contract or budget through a project change order instead of editing those totals directly. A preparer records a project-specific number, description, business reason, request and effective dates, and signed contract and budget changes. Numbers are normalized to uppercase and are unique within a project. At least one amount must be nonzero, the effective date cannot precede the request or project start date, and a change cannot make the resulting contract or budget negative.

Save the change as a draft and submit it for independent review. The submitted record captures the project's concurrency token. A later project setup change makes approval stale, so the reviewer must reject the proposal and the preparer must correct and resubmit it. The preparer or submitter cannot approve or reject that same change order. Approval atomically records the before-and-after contract and budget amounts, changes both authorized project totals, and retains the decision actor, time, reason, and audit event. Rejection preserves its reason and permits correction in place with the same number. Draft, submitted, or rejected work can be cancelled with a reason; unresolved change orders block project closing.

Approved change orders are immutable. Record a reduction or reversal as a new negative change order so the original authority and the subsequent correction both remain visible. The built-in **Project Change Order Preparer** and **Project Change Order Approver** roles provide least-privilege access; the Controller has both permissions for staffing flexibility, but same-document self-decision remains prohibited.

## Controlled project billing

The built-in **Project Billing Preparer** role maintains effective-dated billing rates and creates source-derived customer invoice drafts. It includes receivables and subledger preparation but not invoice approval or posting. The ordinary **Receivables Approver** and **Receivables Poster** roles provide the independent second and third stages.

For time-and-materials work, maintain an earning-code-specific hourly rate or a `*` fallback. Rate periods for the same project and code cannot overlap. The preview uses only positive project time through the selected cutoff whose timecard is approved or has subsequently been consumed by payroll; a missing effective rate fails the whole preview rather than silently omitting time. Optional reimbursable cost lines come from positive, posted, unreversed project expense lines and exclude payroll expense when labor is billed from time. Cost-plus previews use positive posted, unreversed project expense lines, including payroll cost, and apply the entered markup. Source selection is available to API clients; the Projects screen selects all currently eligible sources through the cutoff.

Fixed-price work supports either cumulative progress-to-date or a milestone amount. Progress billing calculates the incremental amount after every noncancelled prior billing. A proposal cannot regress cumulative progress or cause active and posted gross billing to exceed the current authorized contract. All external billing methods enforce that contract cap; approve a change order before billing additional scope.

Every preview shows source, quantity, billing rate or price, source cost, markup, gross amount, retainage, and invoice amount. Saving is permitted only if the project token and a SHA-256 fingerprint of the commercial inputs and source calculations still match the preview. Saving atomically creates a project proposal, immutable line derivation, source reservation, audit event, and ordinary receivables draft. Database uniqueness and project concurrency prevent two operators from billing the same time or cost concurrently. A reserved or posted source cannot appear in another proposal. Customer, billing method, and retainage terms cannot be edited after billing history exists; this prevents later setup changes from contradicting retained invoice derivation.

The linked receivables draft uses the normal independent approval and posting workflow. Approval rechecks the retained project concurrency token, every source reservation, approved or payroll-consumed time quantity and cost, and every posted-cost amount and unreversed journal state. If the project, billing history, or source changed after preparation, the reviewer must reject the draft and the preparer must correct it from a fresh preview. The project proposal follows Draft, Approved, Rejected, and Posted state changes atomically. A generic invoice correction cannot edit a project-derived draft. Instead, correct a rejected proposal from Projects; that re-derives eligible sources, preserves the prior payload and line summary in audit evidence, and returns the same invoice identity to Draft. Draft or rejected proposals can be cancelled with a reason, which cancels the linked invoice draft and releases their source reservations. An unresolved billing proposal blocks project close.

Retainage is calculated line by line with the final line absorbing rounding so proposal totals reconcile exactly. The initial invoice discounts the retained amount and records gross, held, and invoiced values in project billing history. After the source proposal posts, use **Release retainage** to create one or more controlled release invoice drafts. Cumulative active releases cannot exceed the source proposal's retained amount. An original invoice cannot be voided while a retainage release remains active. Voiding a fully open project invoice marks its proposal Voided and releases its sources so corrected billing can be prepared without erasing the original history. Timecards containing reserved or billed project time cannot be voided, and source journals containing reserved or billed project cost cannot be reversed, until the related billing is cancelled or voided. Reservation creation rotates the source parent concurrency token so a simultaneous source reversal and billing save cannot both commit.

This retainage implementation recognizes the held portion as revenue when its release invoice posts; it does not yet classify held retainage in a separate retainage-receivable control account or provide a dedicated retainage aging report. Businesses requiring gross revenue recognition before release must use a reviewed WIP/revenue-recognition policy and controlled journal until that later workflow is implemented.

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

- separate retainage-receivable classification and aging;
- committed-cost forecasting beyond unreceived purchase-order value;
- WIP schedules and over/under-billing entries;
- percentage-of-completion or other automated revenue recognition;
- project-specific budgets by account, period, phase, task, department, or cost code;
- full historical project-ledger pagination/export beyond the recent workspace drill-down.

Until those workflows are implemented and tested, record their accounting through controlled general journals or ordinary subledger documents with project assignments, retain the external approval evidence, and reconcile the result manually.
