# Project and job accounting

BrassLedger can assign accounting and operational lines to a company-scoped project or job. A project is a reporting dimension: the posted general ledger remains the financial source of truth, while project totals are derived from tagged journal lines rather than editable summary balances.

## Project setup and lifecycle

Open **Projects** to create or edit a job number, name, customer, start and expected-end dates, billing method, revenue-recognition method, contract amount, cost budget, and retainage percentage. Job numbers are unique inside the active company. Supported billing methods are **Time and materials**, **Fixed price**, **Cost plus**, and **Internal**. The first three drive the controlled billing preview described below; Internal projects cannot create customer billing. Revenue recognition defaults to **As billed** for existing and new projects unless the operator deliberately selects cost-to-cost, reviewed manual percentage, or completed-contract recognition.

Only an active project can receive new activity. BrassLedger rejects missing, closed, or other-company project references in journals, invoices, bills, quotes, sales orders, purchase requisitions, purchase orders, timecards, and payroll runs. Optimistic concurrency prevents one operator from silently overwriting another operator's project changes.

Each project can have an arbitrarily deep hierarchy of phases, tasks, or work packages. Codes are unique within the project, a child must belong to the same project as its parent, date ranges must remain inside the parent range when both are specified, and cycle creation is rejected. Reusable company-scoped cost codes classify work independently of the project hierarchy. Inactive phases and cost codes remain visible on historical records but cannot receive new activity.

Detailed budget allocations may combine a project, optional phase/task, optional cost code, optional active expense account, and an accounting period. The same dimension/account/period identity cannot be entered twice. Their total authorized budget cannot exceed the project's current budget, while forecast amounts may exceed it so an emerging overrun remains visible. Use a controlled change order before increasing the project budget itself. Allocation edits use concurrency tokens and retain audit evidence.

Closing requires a close date, reason, and current concurrency token. A project cannot close while it has an open journal, quote, sales order, purchase order, purchase requisition, draft/submitted/approved payroll timecard, or unposted payroll run. Closing retains the actor, time, reason, and prior activity. Reopening also requires a reason and creates separate audit evidence. Corrective reversals and supplier returns can retain a historical closed-project dimension so closing a job does not make its posted accounting impossible to reverse.

## Controlled change orders

After project activity or change-order history exists, revise the authorized contract or budget through a project change order instead of editing those totals directly. A preparer records a project-specific number, description, business reason, request and effective dates, and signed contract and budget changes. Numbers are normalized to uppercase and are unique within a project. At least one amount must be nonzero, the effective date cannot precede the request or project start date, and a change cannot make the resulting contract or budget negative.

Save the change as a draft and submit it for independent review. The submitted record captures the project's concurrency token. A later project setup change makes approval stale, so the reviewer must reject the proposal and the preparer must correct and resubmit it. The preparer or submitter cannot approve or reject that same change order. Approval atomically records the before-and-after contract and budget amounts, changes both authorized project totals, and retains the decision actor, time, reason, and audit event. Rejection preserves its reason and permits correction in place with the same number. Draft, submitted, or rejected work can be cancelled with a reason; unresolved change orders block project closing.

Approved change orders are immutable. Record a reduction or reversal as a new negative change order so the original authority and the subsequent correction both remain visible. The built-in **Project Change Order Preparer** and **Project Change Order Approver** roles provide least-privilege access; the Controller has both permissions for staffing flexibility, but same-document self-decision remains prohibited.

## Controlled project billing

The built-in **Project Billing Preparer** role maintains effective-dated billing rates and creates source-derived customer invoice drafts. It includes receivables and subledger preparation but not invoice approval or posting. The ordinary **Receivables Approver** and **Receivables Poster** roles provide the independent second and third stages.

For time-and-materials work, maintain an earning-code-specific hourly rate or a `*` fallback. Rate periods for the same project and code cannot overlap. The preview uses only positive project time through the selected cutoff whose timecard is approved or has subsequently been consumed by payroll; a missing effective rate fails the whole preview rather than silently omitting time. Optional reimbursable cost lines come from positive, posted, unreversed project expense lines and exclude payroll expense when labor is billed from time. Cost-plus previews use positive posted, unreversed project expense lines, including payroll cost, and apply the entered markup. Source selection is available to API clients; the Projects screen selects all currently eligible sources through the cutoff.

Fixed-price work supports either cumulative progress-to-date or a milestone amount. Progress billing calculates the incremental amount after every noncancelled prior billing. A proposal cannot regress cumulative progress or cause active and posted gross billing to exceed the current authorized contract. All external billing methods enforce that contract cap; approve a change order before billing additional scope.

Every preview shows source, phase/task, cost code, quantity, billing rate or price, source cost, markup, gross amount, retainage, and invoice amount. Saving is permitted only if the project token and a SHA-256 fingerprint of the commercial inputs, source dimensions, and calculations still match the preview. Saving atomically creates a project proposal, immutable line derivation, source reservation, audit event, and ordinary receivables draft. The phase and cost code follow the retained source into that invoice draft. Database uniqueness and project concurrency prevent two operators from billing the same time or cost concurrently. A reserved or posted source cannot appear in another proposal. Customer, billing method, and retainage terms cannot be edited after billing history exists; this prevents later setup changes from contradicting retained invoice derivation.

The linked receivables draft uses the normal independent approval and posting workflow. Approval rechecks the retained project concurrency token, every source reservation, approved or payroll-consumed time quantity and cost, and every posted-cost amount and unreversed journal state. If the project, billing history, or source changed after preparation, the reviewer must reject the draft and the preparer must correct it from a fresh preview. The project proposal follows Draft, Approved, Rejected, and Posted state changes atomically. A generic invoice correction cannot edit a project-derived draft. Instead, correct a rejected proposal from Projects; that re-derives eligible sources, preserves the prior payload and line summary in audit evidence, and returns the same invoice identity to Draft. Draft or rejected proposals can be cancelled with a reason, which cancels the linked invoice draft and releases their source reservations. An unresolved billing proposal blocks project close.

Retainage is calculated line by line with the final line absorbing rounding so proposal totals reconcile exactly. Posting the initial invoice debits ordinary accounts receivable for the currently collectible net invoice, debits the configured retainage-receivable control account for the held amount, and credits the project's revenue account for the gross billing. The customer's ordinary open balance excludes unreleased holdback, while every invoice credit-limit check includes all of that customer's outstanding retainage as exposure. A retainage release does not increase exposure because it transfers the same balance to ordinary receivables. Approval requires an active Asset control account assigned to the **Retainage receivable** operational role; a new standard chart uses account `1110`, but an upgraded or customized chart may require an administrator to assign another eligible account.

After the source proposal posts, use **Release retainage** to create one or more controlled release invoice drafts. Posting a release debits ordinary accounts receivable and credits retainage receivable; it does not recognize the same revenue a second time. Cumulative active releases cannot exceed the source proposal's retained amount. The Projects page ages each posted source invoice's outstanding holdback in 0–30, 31–60, 61–90, and over-90-day buckets, subtracting only posted releases. It also compares the aging total with the configured control-account balance and displays a prominent investigation warning when they differ. Do not use an unreconciled aging report for financial reporting.

An original invoice cannot be voided while a retainage release remains active. Voiding a fully open release restores its amount to retainage receivable; voiding the original invoice then reverses ordinary receivables, retainage receivable, and gross revenue and marks its proposal Voided. The original source reservations are released so corrected billing can be prepared without erasing history. Timecards containing reserved or billed project time cannot be voided, and source journals containing reserved or billed project cost cannot be reversed, until the related billing is cancelled or voided. Reservation creation rotates the source parent concurrency token so a simultaneous source reversal and billing save cannot both commit.

## Controlled WIP and earned revenue

Projects configured for **Cost-to-cost** recognize cumulative earned revenue as authorized contract value multiplied by posted project expense through the cutoff divided by the current cost budget, capped at 100%. A zero estimate or abnormal negative project cost fails validation. **Manual percentage** uses a reviewed cumulative percentage from 0% through 100%. **Completed contract** recognizes the authorized contract only after the project is closed. **As billed** does not create WIP schedules.

The preview independently derives cumulative cost, completion, earned revenue, and posted project billings. Billings include gross controlled project proposals, including retained amounts but excluding retainage releases, plus net pretax lines from other posted itemized invoices carrying the project dimension. Voided invoices are excluded. Earned revenue less billings becomes a contract asset when positive or a contract liability when negative. It compares that desired cumulative position with all posted and reversed WIP control lines through the cutoff, then proposes only the incremental true-up. A move from asset to liability therefore credits the prior contract asset, credits the new contract liability, and debits revenue in one balanced posting; it does not recognize or defer the same amount twice.

Saving retains a SHA-256 fingerprint covering project terms and concurrency, every cost source, every posted billing source, every prior control line, calculation inputs, and results. It also advances the project token atomically so two preparers cannot reserve the same cumulative starting point. Submit the draft for independent review. Its preparer or submitter cannot decide it, and its approver cannot post it. Approval and posting both recompute the preview and reject changed costs, billings, project terms, control activity, source identity, or retained calculation values. The posting date must be on or after the cutoff and outside closed periods.

Standard charts route underbillings to the `1120` **Contract asset** control account and overbillings to the `2040` **Contract liability** control account. Administrators may assign other eligible company accounts, but neither role can move while it has a balance or any posted WIP remains. Once a project has posted WIP, subsequent schedules retain the same revenue account unless all WIP is reversed. The Projects page compares the latest posted cumulative schedule for each project with both general-ledger controls and displays an alert for either difference.

Only the latest posted schedule for a project may be reversed. Reversal creates the exact inverse journal on an open date and restores the preceding cumulative schedule as the active subledger position. A zero-dollar schedule still preserves its reviewed period-end conclusion and can be reversed without inventing a journal. Revenue-recognition method cannot change after WIP history exists. A completed-contract project cannot be reopened while completed-contract WIP remains posted; reverse that recognition first so its accounting remains consistent with the reopened lifecycle.

These methods provide controlled billing-independent recognition and WIP accounting; they do not by themselves establish that a contract satisfies a particular financial-reporting framework. Variable consideration, multiple performance obligations, expected-loss provisions, and method selection still require documented accounting policy and qualified review until their dedicated workflows are implemented.

## Assigning activity

The project selector is available on:

- ordinary general-journal lines;
- customer invoice and vendor-bill lines;
- sales quote and sales-order lines;
- purchase-requisition and purchase-order lines; and
- payroll time and earning lines.

Project, phase/task, and cost code follow the source line through approval, posting, quote or requisition conversion, fulfillment, invoice matching, customer and supplier returns, payroll posting, project billing, and reversal. A phase or cost code cannot be supplied without its project. Inventory shipments allocate COGS by the retained source dimensions. Shipment invoices retain those dimensions on revenue. Purchase invoice matching retains them from the purchase-order line on bill and variance lines. Payroll allocates gross pay and employer tax/benefit burden across each employee's dimensioned earnings; the final allocation absorbs rounding so the project lines reconcile exactly to the payroll posting.

Liability, cash, tax, and other balance-sheet lines may carry a project when they are source-line-specific, but the current project cost and revenue totals intentionally include only expense and revenue accounts.

## Portfolio and ledger reporting

The Projects page reports:

- contract amount and cost budget from approved project setup;
- actual cost from posted expense-account debits less credits;
- revenue from posted revenue-account credits less debits;
- commitments from the unreceived value of active purchase-order lines; and
- margin as revenue less actual cost.

Totals scan the complete posted project ledger using exact decimal values. The on-screen drill-down is deliberately limited to the 250 most recent tagged lines to keep the workspace response bounded. Select a project to filter that recent-line view. Use the displayed phase/task and cost code together with the journal reference and source module to trace a line back to its authorized transaction.

## QuickBooks CSV interchange

BrassLedger journal and zero-tax invoice CSV files include optional `Project / Job`, `Project Phase`, and `Cost Code` columns. Imports accept project aliases (`Project/Job`, `Project Job`, `Project`, or `Class`), phase aliases (`Phase`, `Project Task`, `Task`, or `Work Package`), and common cost-code spellings. A populated project must resolve by job number or unique exact name to one active same-company project. A phase must resolve inside that project, and a cost code must be active in the same company. A phase or cost code without a project, or an ambiguous, closed, foreign-company, or unknown value, rejects the batch instead of discarding attribution. Blank values remain unassigned.

QuickBooks products and subscriptions expose project and class tracking differently. Treat this extra column as a controlled BrassLedger round-trip field and explicitly map or preserve it during the QuickBooks review step. A successful CSV parse does not prove that a particular QuickBooks subscription imported the dimension.

## Current boundaries

The following project-accounting capabilities remain future work and must not be represented as complete:

- committed-cost forecasting beyond unreceived purchase-order value;
- variable consideration, multiple performance obligations, and expected-loss provisions;
- department/class dimensions and cross-project resource planning beyond the implemented phase/task, cost-code, account, period, budget, and forecast allocations;
- full historical project-ledger pagination/export beyond the recent workspace drill-down.

Until those workflows are implemented and tested, record their accounting through controlled general journals or ordinary subledger documents with project assignments, retain the external approval evidence, and reconcile the result manually.
