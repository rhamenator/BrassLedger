# BrassLedger Reporting Guide

This guide covers the report, form, check, paycheck, tax-form, and label side of BrassLedger: what kinds of output the application should own, how to review that output, and which assets belong in source control versus publish output.

## Reporting principles

A report definition is part of the product when it expresses business meaning, layout intent, or a repeatable review standard. A copied file under a publish directory is not the source of truth.

Preferred principles:

- keep report datasets stable and explicit
- keep formatting and pagination reviewable
- keep business rules in the application or report dataset, not hidden inside one vendor tool
- keep authored layouts and static assets in source control
- keep generated publish output out of Git

## Output families

### Financial statements

Typical members:

- trial balance
- balance sheet
- income statement
- account detail
- journal detail
- period comparison statements

Review points:

- totals reconcile to current posted balances
- sign conventions are correct
- period labels are correct
- page breaks do not hide subtotal context

### Receivables output

Typical members:

- customer aging
- statements
- unapplied cash review
- customer activity detail
- collection follow-up lists

### Payables output

Typical members:

- vendor aging
- payment proposal
- payment register
- printed checks
- remittance detail

### Payroll and year-end output

Typical members:

- paychecks
- payroll register
- gross-to-net summary
- liability summary
- deduction summary
- year-end employee forms such as W-2 style output

### Tax-facing output

Typical members:

- federal and state payroll tax forms
- withholding summaries
- deposit support schedules
- reconciliation reports used before filing

### Operations documents and labels

Typical members:

- pick tickets
- packing slips
- order status reports
- shipment labels
- mailing labels
- internal routing labels
- item or bin labels

## Label and form design guidance

General rules:

- design for the real stock or device target
- respect printer margin behavior and trim risk
- keep barcodes and postal blocks within safe zones
- preview with realistic data, including long names and addresses
- test on the intended printer path when stock or fonts change

## Release checklist for reports and forms

1. Confirm the dataset matches the intended filters and period.
2. Reconcile totals to the owning module or ledger account.
3. Review page breaks, repeating headers, decimal precision, and date formats.
4. Validate long names, addresses, and reference fields.
5. Test sample print or PDF output.
6. Commit the authored sources and supporting static assets.
7. Publish generated files only as release artifacts, not as tracked source files.

## Source control guidance

Commit these items:

- authored report definitions
- source-side CSS, images, and static assets under `BrassLedger.Web/wwwroot`
- report tests, snapshots, and documentation

Do not commit these items:

- `artifacts`
- `bin`
- `obj`
- copied publish output such as generated `web.config`, bundled CSS maps, or runtime files

A generated file may appear under `artifacts/win-x64` or `artifacts/win-x64/bootstrap`, but if it can be recreated by publishing the application, it should not become a tracked source artifact.
