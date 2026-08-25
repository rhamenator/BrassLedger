# Payroll operator and tax-calculation guide

## Controlled payroll workflow

BrassLedger payroll uses a durable subledger workflow. A calculation preview is read-only. **Save reviewed draft** preserves the pay period, employees, earnings, deductions, tax lines, year-to-date inputs, rule versions, and source trace without changing cash or the general ledger.

A draft must move through these states:

1. `Draft` — prepared details can be reviewed; no financial posting exists.
2. `Approved` — the reviewed calculation is locked for posting.
3. `Posted` — a balanced payroll journal and funding-account movement exist.
4. `Reversed` — the original remains in history and a linked equal-and-opposite journal records the correction.
5. `Cancelled` — a draft was rejected before posting; its calculation and audit history remain, and any assigned approved timecards are released for a replacement draft.

Preparation, approval, posting, reversal, general payroll maintenance, and access to protected employee fields are separate permissions. A stale browser or API request is rejected by the payroll run's concurrency token. Payroll cannot post into a closed accounting period, and a reconciled payroll journal cannot be reversed until its bank reconciliation is reopened.

Cancelling requires the payroll-reversal permission, a current concurrency token, and a reason. It is valid only for a draft. The cancellation and release of every source timecard occur in one transaction. Cancelled earning lines retain their original source-entry links for review, but they do not prevent those entries from feeding one later active payroll run.

## Employee and earning setup

Maintain the employee's residence and primary work state/city in **Employee tax elections and benefits**. County and school-district identifiers, employment dates, hourly/overtime rates, SSN, and direct-deposit fields are maintained in the permission-protected employee panel.

SSNs, routing numbers, bank account numbers, direct-deposit authorization references, court/order details, bank-origin identifiers, and generated payment-file contents are encrypted before database persistence. The application reports only masked values or whether a value is on file and does not return stored secrets to the payroll workspace. Leaving a protected input blank keeps its current value; use the explicit removal controls to clear it. Enabling direct deposit requires a signed authorization date and a reference to the retained evidence; bank details alone are not treated as employee authorization.

The payroll contract supports multiple earning lines for one employee in one run. Each line preserves its earning code/type, hours, rate, amount, work date, and state/county/city/school-district allocation. A non-taxable earning is excluded from taxable earnings. Deduction lines separately record employee/employer amounts and whether the deduction is exempt from federal income tax, FICA, or FUTA; do not assume every “pre-tax” benefit has the same tax treatment.

Timecards are durable records with draft, submission, approval, consumption, and void controls. Only an approved, unconsumed timecard for an employee and period in the payroll request can feed a run. Previewing leaves it approved. Saving a reviewed payroll draft copies the server-stored entries and assigns the card to the run in one database transaction. Each resulting earning line links to its exact source time entry, and a unique database constraint prevents reuse. For hourly employees, selected timecards replace the screen's gross-pay placeholder; for salaried employees, their entries are added to entered base salary. A failed draft leaves the cards available for correction and retry.

The operator screen supports regular, overtime, double-time, leave, holiday, bonus, commission, tips, piecework, shift-differential, on-call, severance, fringe-benefit, and reimbursement entries, plus optional project/job and work-jurisdiction detail.

## Deduction, benefit, and legal-order plans

Payroll managers with protected-data access can maintain effective-dated plans and employee elections for medical, dental, vision, HSA/FSA, retirement, insurance, commuter, union, charitable, loan, support, garnishment, levy, bankruptcy, federal-agency debt, PTO-purchase, and user-defined deductions. A plan can use a fixed amount, percentage of gross, or—after taxes—a percentage of disposable earnings. It separately records employee and employer values, tax exemptions, priority, liability account, per-pay and annual employee limits, minimum net pay, official-source metadata, and extensible JSON legal-rule parameters. Overlapping active elections for the same employee and plan are rejected.

The calculation pipeline applies capped pretax deductions before computing tax, computes employee taxes, and then applies post-tax deductions in priority order. It stores the requested amount, applied amount, plan/election identity, rule code, source, and calculation trace on every resulting payroll deduction line. Employer contributions remain employer expense/liability and never reduce employee net pay. Annual limits use only prior posted, unreversed payroll in the same year.

The current federal ordinary-garnishment template follows [U.S. Department of Labor Fact Sheet #30](https://www.dol.gov/agencies/whd/fact-sheets/30-cppa): the lesser of 25% of disposable earnings or the amount above the pay-period equivalent of 30 times the configured federal minimum wage. The support template applies the 50%/60% limits and additional 5% for qualifying arrears from the same source. Disposable earnings exclude amounts required by law, not voluntary benefits merely labeled pretax. A configured state/local percentage or protected floor can make a plan stricter. Priority, tax levies, bankruptcy orders, and state-specific restrictions still require the actual order and applicable authoritative law; operators must not substitute the federal templates for legal review.

When an employee has taxable earnings in more than one work jurisdiction, BrassLedger groups the earning lines by their state, county, city, and school district. Work-jurisdiction rules and rate profiles receive only the taxable wages allocated to their matching locations. Employee resident-jurisdiction rules receive the whole taxable check, while federal obligations remain whole-pay calculations. Shared pre-tax deductions are allocated proportionally across taxable work earnings, with the final location receiving any cent-rounding remainder. The calculation trace records the scope and location used for each obligation. This allocation mechanism does not make an unverified state or locality package safe to activate; reciprocity, convenience-of-employer, resident-credit, and jurisdiction-specific allocation rules still require authoritative content and regression cases.

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

## Liabilities and remittances

Posting a payroll run creates a liability record for every positive tax and deduction line. Each payable links to the exact payroll run, employee calculation line, and originating tax or deduction line. Employer benefit contributions are recorded separately from employer payroll taxes, included in payroll expense, credited to the configured liability account, and never deducted from employee net pay. Recurring employee benefit deductions are also persisted as lines instead of disappearing into run totals.

The sum of the new liability records must reconcile exactly to employee deductions, employee withholding, employer taxes, and employer benefit contributions before posting can proceed. Deduction liability accounts must exist in the active company and be active liability accounts. BrassLedger does not invent deposit due dates: **Schedule required** remains visible until authoritative filing/deposit configuration supplies one.

### Federal Form 941 deposit schedules

An authorized payroll manager and approver can record one annual Form 941 deposit schedule after verifying the employer's official lookback total. The configured lookback period is constrained to July 1 two years before the tax year through June 30 immediately before it. The default 2026 thresholds, schedule selection, next-day rule, and timing rules come from [IRS Publication 15 (2026), section 11](https://www.irs.gov/publications/p15); weekends and District of Columbia legal holidays come from [IRS Publication 509 (2026)](https://www.irs.gov/publications/p509). Source URLs, retrieval date, holiday dates, review notes, approval identity/time, thresholds, and all changes are retained for audit.

For an approved active schedule, monthly liabilities are due on the next business day on or after the 15th of the following month. Semiweekly periods ending Tuesday or Friday are due after three business days, which correctly extends the deadline when a federal or D.C. holiday intervenes. If accumulated Form 941 liability reaches the configured $100,000 threshold during a deposit period, every liability accumulated in that period is due the next business day and subsequent payroll uses the semiweekly schedule. Deposit periods that cross a quarter boundary remain separate, as required by the IRS example. A prior-year trigger carries into the following year when that prior year's approved schedule and liability history are available. Saving a schedule safely recalculates due dates on existing open liabilities; posting later payroll applies it automatically. Each liability retains its schedule classification, rule code, configuration ID, and official-rule URL. Due dates and rule evidence already attached to paid liabilities are historical facts and are never rewritten.

Publication 15 also permits payment with a timely Form 941 when the recorded current or prior quarter liability is below the configured $2,500 threshold and the current quarter has no $100,000 next-day obligation. This exception is never inferred silently. An approver must list the elected quarter numbers in the schedule's JSON field and document the review. For an ongoing quarter, BrassLedger requires a qualifying recorded prior quarter; after quarter end, the recorded current quarter may qualify independently. An election that conflicts with a next-day obligation is rejected. Eligible liabilities are assigned the business-day-adjusted Form 941 return due date.

The screen separately identifies open, overdue, unscheduled, and next-due balances. It also reconciles posted liability-payment applications to each required deposit and applies Publication 15's accuracy rule: a potential safe-harbor shortfall cannot exceed the greater of $100 or 2% of the required deposit, and it must be made up by the applicable monthly or semiweekly deadline. Payment-with-return quarters are excluded because they are not deposit shortfalls. Statuses distinguish deposits made in full, amounts outside the tolerance, pending or overdue makeup, and shortfalls made up within the calculated deadline. These are operational determinations from BrassLedger's recorded payments, not proof that EFTPS received or settled a payment and not an IRS waiver of penalties.

IRS disaster relief is configured per company and per announcement; it is never inferred from a state name or a general disaster list. Approval requires the exact official IRS announcement, covered-area list, FEMA declaration when applicable, affected-taxpayer basis, eligibility evidence reference, retrieval date, review notes, and typed relief-action windows. Supported action types distinguish return-filing postponement, tax-payment postponement, and deposit-penalty abatement. Unknown future action types can be retained in an inactive draft but cannot be approved until the runtime supports them.

Employment-tax deposits are commonly excluded from the general postponement even when a quarterly return or payment is postponed. For that reason, BrassLedger never changes a deposit's legal due date from a general disaster deadline. A `DepositPenaltyAbatement` action instead compares posted remittances with both the original due date and the announcement's make-up deadline, reporting whether the announcement's payment condition was met. This follows the distinction in the [IRS disaster-relief overview](https://www.irs.gov/businesses/small-businesses-self-employed/disaster-assistance-and-emergency-relief-for-individuals-and-businesses) and the exact state/event announcements listed by the [IRS disaster-relief index](https://www.irs.gov/newsroom/tax-relief-in-disaster-situations). Actual IRS account recognition, penalty abatement, and interest remain external facts that must be confirmed; BrassLedger does not claim to make an IRS eligibility determination.

An authorized poster can remit one or many open liabilities from a bank account using EFT, ACH, check, wire, or another documented method. Applications cannot exceed their open balances, cannot cross companies, cannot precede the payroll pay date, and update the liability and bank/ledger balances in one transaction. Remittances retain their application detail and can be reversed with a reason and linked inverse journal, unless their bank journal is part of a completed reconciliation. A payroll run cannot be reversed while any of its liability payments remains applied.

## Employee payments, registers, and pay statements

Posting creates one durable employee payment record for every employee calculation line. Those records must sum exactly to run net pay or posting is rejected. An employee with complete bank details, an effective signed-authorization reference, and direct deposit enabled receives a `DirectDeposit` instruction; everyone else receives a `Check` instruction.

Direct-deposit routing and account snapshots and the employee-name snapshot are encrypted at rest. The payroll screen and APIs expose only account type and last four digits. Reversal retains each instruction and marks it `Reversed`; the original payment history is never deleted.

After posting, an authorized payroll poster with protected-data access can generate an immutable ACH-instruction CSV, check-register CSV, or NACHA PPD file. Every file stores a content SHA-256, source SHA-256, entry count, credit total, routing hash where applicable, creator/time, and specification version. A second file of the same format cannot be generated for the run. Reversing payroll marks every related export `Voided`; downloads of retained voided content are prefixed `VOID-DO-NOT-PROCESS`.

NACHA PPD output follows Nacha's current [ACH file overview](https://achdevguide.nacha.org/ach-file-overview) and [fixed-field layouts](https://achdevguide.nacha.org/ach-file-details): 94-byte ASCII records, credit-only service class 220, PPD entries, transaction codes 22/32, ascending trace numbers, batch/file controls, routing hash, cent totals, and 10-record block padding. Generation requires an effective origin configuration explicitly marked as validated against the originating bank's test process. The resulting status is `GeneratedForBankValidation`, not “transmitted” or “bank accepted.” BrassLedger does not submit files to an ODFI, validate receiver ownership, perform OFAC screening, create IAT entries, or replace the Nacha Operating Rules and the bank's implementation agreement.

Choose **View register** on a posted or reversed run to inspect employee and run totals or download CSV. A user with payroll-sensitive-data permission can open an employee pay statement containing earning dates and work jurisdictions, deductions, employee and employer taxes, payment state, and posting-time year-to-date totals. Report generation fails visibly if stored earning, deduction, tax, employee, payment, or run totals do not reconcile.

## Federal filing data and payroll close

The payroll page can generate encrypted, source-locked preparation data for 2026 Form 941, Form 940, and W-2/W-3. The mappings cite current official IRS guidance: [Form 941 instructions](https://www.irs.gov/instructions/i941), [Form 940](https://www.irs.gov/forms-pubs/about-form-940), and [2026 W-2/W-3 instructions](https://www.irs.gov/instructions/iw2w3). Only posted, unreversed payroll enters a filing draft. The stored SHA-256 digest covers source run states, employee calculation totals, tax lines, content versions, and recorded tax-deposit applications. Approval is rejected if any source value or deposit changes after generation.

Approved filing data locks payroll preparation, posting, and reversal for its date range until an authorized user reopens the filing with a reason. Closing a quarter additionally requires an approved Form 941 data set and no unresolved draft or approved payroll runs. Closing a year requires all four closed quarters plus approved Form 940 and W-2/W-3 data. Reopening retains the close and filing histories; it never deletes or silently replaces them.

Filing JSON contains protected EIN, SSN, name, and address data and is encrypted at rest. Access and download require the payroll-sensitive-data permission. Form 941 output includes federal wages and withholding, Social Security and Medicare wage/tax totals, Additional Medicare amounts, recorded deposits, balance, and pay-date tax liability detail. Approval now retains a separate encrypted immutable baseline even if the working filing is later reopened. Form 940 output includes employee payments, FUTA taxable wages/tax, recorded deposits, and an explicit flag that state credit-reduction and other adjustments still require review. W-2/W-3 output includes core Boxes 1–6 and state/local wage and income-tax aggregates.

For a Form 941 correction, reopen the payroll period and filing, post the authorized reversing or correcting payroll entries, and prepare a Form 941-X draft. BrassLedger compares current posted payroll to the immutable approved 941 baseline—or to the latest approved 941-X for a subsequent correction—without replacing prior filing evidence. The workflow requires the error-discovery date, adjustment-versus-claim process, a detailed explanation, the applicable federal-withholding restriction, employee Social Security/Medicare protection certification and evidence reference, and W-2/W-2c evidence. It rejects claim-process packages containing underreported or mixed tax changes, source-locks approval, encrypts correction data, and retains sequential correction history. Mistaken drafts can be voided with a required reason; their sequence, protected payload, and audit history remain intact. The official mapping is based on [IRS Instructions for Form 941-X (April 2026)](https://www.irs.gov/instructions/i941x).

For wage-statement corrections, BrassLedger compares current posted annual payroll and protected employee identity data with the immutable approved W-2/W-3 baseline, then with each sequential approved W-2c/W-3c package. Each affected employee retains previously reported and corrected values, the package reconciles W-3c boxes 1 through 6, approval source-locks the corrected payroll, and corrected-statement delivery requires an evidence reference. Federal wage/tax or employee name/SSN changes are marked for SSA submission. Address-only and state/local-only changes are explicitly marked not for SSA Copy A, following the [2026 General Instructions for Forms W-2 and W-3](https://www.irs.gov/instructions/iw2w3). Drafts may be voided without reusing their sequence.

These files are filing-ready review data, not transmitted returns or printable official forms. BrassLedger now produces source-locked 941-X and W-2c/W-3c correction packages, but does not yet produce IRS Modernized e-File messages, SSA EFW2/EFW2C files, red-ink or approved substitute forms, qualified-tip/overtime reporting, every W-2 box/code, or jurisdiction return layouts. The payloads deliberately retain `RequiresProfessionalReview` and transmission-status flags until those workflows, SSA AccuWage validation, and independent payroll-tax review are complete.

## Current release boundary

The lifecycle, protected fields, auditable timecards, detailed earning entry, multi-location earning allocation, 2026 FIT/FICA engine, effective-dated deduction/election plans, configurable federal legal limits, reconciled liability/remittance subledger, approved Form 941 monthly/semiweekly/next-day and small-liability return-payment due dates, deposit-shortfall and source-backed disaster-relief monitoring, employee payment instructions, protected ACH/check/NACHA exports, reconciled registers/pay statements, source-locked federal filing preparation data, sequential Form 941-X and W-2c/W-3c correction packages, quarter/year close controls, ledger posting, and reversal controls are implemented and tested. BrassLedger is not yet ready for live payroll until the remaining payroll goal is completed: actual transmission, printable approved substitutes, state/local correction workflows, authoritative reciprocity and special-allocation content, all required state/local executable content and legal-limit packages, bank acceptance of payment formats, and independent accounting/tax/legal review.
