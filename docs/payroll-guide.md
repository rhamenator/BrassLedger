# Payroll operator and tax-calculation guide

## Controlled payroll workflow

BrassLedger payroll uses a durable subledger workflow. A calculation preview is read-only. **Save reviewed draft** preserves the pay period, employees, earnings, deductions, tax lines, year-to-date inputs, rule versions, and source trace without changing cash or the general ledger.

A draft must move through these states:

1. `Draft` — prepared details can be reviewed; no financial posting exists.
2. `Approved` — the reviewed calculation is locked for posting.
3. `Posted` — a balanced payroll journal and funding-account movement exist.
4. `Reversed` — the original remains in history and a linked equal-and-opposite journal records the correction.

Preparation, approval, posting, reversal, general payroll maintenance, and access to protected employee fields are separate permissions. A stale browser or API request is rejected by the payroll run's concurrency token. Payroll cannot post into a closed accounting period, and a reconciled payroll journal cannot be reversed until its bank reconciliation is reopened.

## Employee and earning setup

Maintain the employee's residence and primary work state/city in **Employee tax elections and benefits**. County and school-district identifiers, employment dates, hourly/overtime rates, SSN, and direct-deposit fields are maintained in the permission-protected employee panel.

SSNs, routing numbers, and bank account numbers are encrypted before database persistence. The application reports only whether a value is on file and does not return the stored value to the payroll workspace. Leaving a protected input blank keeps its current value; use the explicit removal controls to clear it.

The payroll contract supports multiple earning lines for one employee in one run. Each line preserves its earning code/type, hours, rate, amount, work date, and state/county/city/school-district allocation. A non-taxable earning is excluded from taxable earnings. Deduction lines separately record employee/employer amounts and whether the deduction is exempt from federal income tax, FICA, or FUTA; do not assume every “pre-tax” benefit has the same tax treatment.

The current operator screen provides gross-pay entry while the API/domain support detailed earning and deduction lines. Timecard entry and a full line editor remain required before live hourly payroll use.

## 2026 federal calculation

The active 2026 federal calculation reads its constants and percentage schedules from `tax-content/us/federal/2026-payroll-tax-data.json`. The file records official-source URLs, IRS PDF SHA-256 hashes, effective/expiration dates, and review state.

Federal income-tax withholding implements the standard schedules and Worksheet 1A flow from [IRS Publication 15-T (2026)](https://www.irs.gov/publications/p15t): pay-period annualization, current/legacy W-4 handling, Step 2 multiple-jobs selection, Step 3 credits, Step 4 other income/deductions, additional withholding, and the employee's exemption election.

FICA implements the 2026 values published in [IRS Publication 15](https://www.irs.gov/publications/p15) and the [SSA contribution and benefit base](https://www.ssa.gov/oact/COLA/cbb.html):

- Social Security: 6.2% employee and 6.2% employer, limited to $184,500 of 2026 wages.
- Medicare: 1.45% employee and 1.45% employer with no wage base.
- Additional Medicare withholding: 0.9% employee-only after the employer pays the employee more than $200,000 during the calendar year.

Every federal obligation is stored as a separate payroll tax line with taxable wages, prior year-to-date wages, amount, content version, source, and calculation trace. State/local content is composed by obligation, so activating a state package cannot suppress FIT or FICA.

The 2026 calculation intentionally refuses other payroll years instead of applying 2026 rules to the wrong year. Add and verify the effective-dated package for a new year before processing that year's payroll.

## FUTA and employer-specific profiles

Publication 15 states a 6.0% FUTA rate on the first $7,000 but explains that the commonly used 0.6% net rate depends on the maximum state credit, timely and complete state unemployment payments, covered-wage alignment, and credit-reduction-state treatment. Consequently, BrassLedger does not activate its seeded 0.6% example. An employer must verify and activate the applicable employer configuration from its official notices and Form 940 facts.

All seeded rate profiles are visibly inactive and unverified. Only profiles marked both active and verified enter a calculation. Inactive starter data must not be promoted merely to make a preview produce a desired amount.

## Review checklist

Before approving a run:

- confirm pay period, pay date, run type, and funding account;
- reconcile earning totals to approved time/compensation records;
- review every employee's residence and each earning line's work jurisdiction;
- confirm W-4 year, filing status, Steps 2–4, exemption status, and additional withholding;
- inspect benefit taxability rather than relying on its display name;
- inspect tax-line sources, taxable wages, year-to-date wage bases, and warnings;
- confirm employer-specific unemployment rates and notices are current; and
- compare gross, deductions, taxes, liabilities, and net pay to the payroll register.

After posting, reconcile the payroll journal, funding account, and payroll liabilities. Reverse through payroll rather than editing a posted run or deleting its journal.

## Current release boundary

The lifecycle, protected fields, multi-location earning storage, 2026 FIT/FICA engine, ledger posting, and reversal controls are implemented and tested. BrassLedger is not yet ready for live payroll until the remaining payroll goal is completed: timecards, detailed line-entry UI, deduction/benefit setup and limits, direct-deposit/check output, liability deposits, quarter/year close, federal/state/local forms and e-file-ready exports, all required state/local executable content, and independent accounting/tax review.
