# BrassLedger tax content

Tax content is maintained as versioned JSON and imported as an inactive draft. A package may be approved only after its official sources are captured, checksummed, independently reviewed, and its required regression examples pass in the current tax engine.

## Jurisdictions

Jurisdiction identity is separate from its name, code, type, country, and parent. Relationships are effective-dated, so annexation, dissolution, renaming, a county changing states, or a jurisdiction changing countries adds a new relationship interval without rewriting history. Proposed or disputed changes can coexist with the active relationship and do not affect payroll until approved and effective.

Use `schema/jurisdiction-catalog-v1.schema.json` for catalogs and `schema/tax-content-package-v1.schema.json` for calculation packages. State, county, city, school-district, special-district, tribal, and other jurisdiction types use the same model.

## Capture workflow

1. Capture the official publication and raw bytes; record its URL, retrieval time, revision/effective date, and SHA-256.
2. Create a new package version. Never overwrite an approved version.
3. Transcribe applicability, employee inputs, calculation parameters, brackets/tables, filing rules, exceptions, and official examples.
4. An LLM may prepare a draft, but its output must identify every source and remain inactive.
5. Independently compare the draft with the official publication.
6. Run required regression examples and boundary cases.
7. Approve and activate the package for its effective interval.

`us/state-reference-2026.json` is a coverage inventory. Its PIT classifications and SUTA wage bases are reference data, not withholding formulas. `formulaCoverage` explicitly distinguishes uncaptured, official-source-only, draft, approved, and non-applicable PIT coverage. The 2026 inventory now links every state/DC wage-withholding jurisdiction to an official-source capture; this means the publication was located, not that its rules are executable or approved. Every `OfficialSourceCaptured` entry must point to a source-capture JSON document with activation explicitly disabled. A source capture becomes an importable draft package only after its formula, inputs, applicability rules, and regression examples are complete enough for the engine to execute.

Maryland's local capture is in `us/md/2026-local-source-capture.json`. It selects the local schedule from the employee's residence, represents all 23 counties and Baltimore City as stable jurisdictions, and preserves the income-tiered filing-status schedules used by Anne Arundel and Frederick rather than reducing local tax to a single flat-rate field. The combined Maryland state/local formula and its regression cases remain required before activation.

Indiana's state-and-local capture is in `us/in/2026-source-capture.json`. It includes the state deduction formula, all 92 county rates, the January 1 residence-before-work selection rule, and the qualifying nonresident 30-day branch. The shared `us/state-withholding-sources-2026.json` file is the stable source index for states that do not yet have their own rule-level capture; its name describes its content rather than the order in which states happened to be researched.

New York's local capture is in `us/ny/2026-local-source-capture.json`. New York City resident withholding, Yonkers resident surcharge withholding, and Yonkers nonresident earnings-tax withholding are separate rule branches because their applicability, methods, rates, allowances, and supplemental-wage treatment differ.

Michigan's state-and-local capture is in `us/mi/2026-source-capture.json`. It transcribes the state formula and reciprocity rules, inventories all 24 taxing cities, and captures Detroit's resident, nonresident, exemption, predominant-workplace, and resident-credit rules. The other 23 cities still require their own current official publications, and Michigan's server rejected direct PDF-byte retrieval during capture, so the package remains inactive with checksums pending.

Ohio's state-and-local capture is in `us/oh/2026-source-capture.json`. It preserves separate state withholding tables through July 31 and from August 1, 2026, and models residence-selected school-district withholding separately from work-location municipal withholding. Official school-district and municipal rate-and-boundary exports, examples, and executable day-allocation logic remain required before activation.

Illinois's capture is in `us/il/2026-source-capture.json`. It preserves both allowance classes, the optional exact formula, table-versus-formula behavior, reciprocity, the nonlocalized 30-working-day allocation rule, disaster-response exclusions, and filing requirements. Rounding and the absence of a distinct supplemental method still require independent verification.

Colorado's capture is in `us/co/2026-source-capture.json`. It models the DR 0004/W-4 precedence rules and the annualized DR 1098 calculation without reducing certificate fields to fixed allowances. Colorado's server rejected direct PDF retrieval, and current filing thresholds and rounding remain verification blockers.

North Carolina's capture is in `us/nc/2026-source-capture.json`. It includes percentage and annualized formulas, payroll-period deductions, whole-dollar rounding, supplemental-pay alternatives, residence/work allocation, filing thresholds, and the separate nonresident-alien low-wage cap. The full wage-bracket matrices remain in the checksummed official publication.

Arizona's capture is in `us/az/2026-source-capture.json`. It preserves the employee-elected gross-wage percentages, missing and expired-certificate defaults, conditional 60-day nonresident rule, special-worker branches, filing schedules, and the unusual employer election to suspend withholding during December. Filing-page checksums, rounding, and executable effective-period behavior remain blockers.

Idaho's capture is in `us/id/2026-source-capture.json`. It preserves both table versions used during 2026, whole-dollar rounding, supplemental methods, filing schedules, and multi-state applicability. The precise switchover payroll, the historic child-credit allowance, and a conflict between the current HTML example and the revised official PDF must be resolved before activation.

Mississippi's capture is in `us/ms/2026-source-capture.json`. It preserves filing-status deductions, employee-entered exemption dollars, annualization, supplemental aggregation, multi-state wage allocation, certificate administration, and filing thresholds. The official server's invalid TLS chain prevented safe raw-byte capture, and the agency's own rate prose conflicts with its dated table; both remain explicit blockers.

Alabama's capture is in `us/al/2026-source-capture.json`. It preserves income-sensitive standard deductions, federal-withholding and dependent deductions, separate married-joint brackets, supplemental withholding, the nonresident 30-day safe harbor, and filing schedules. Formula rounding and the 31st-day transition still require executable review.

Arkansas's capture is in `us/ar/2026-source-capture.json`. It preserves the complete formula schedule—including the unusual high-income adjustment phase-in—midpoint normalization, personal credits, official example, work-day and sales-volume allocation, and the Texarkana border-city exemption. Wage tables remain in the checksummed official matrix while supplemental and address-boundary behavior remain inactive review gates.

Delaware's capture is in `us/de/2026-source-capture.json`. It preserves the annualized progressive schedule, filing-status deductions, exemption credits, official examples, supplemental aggregation, nonresident rules, and quarterly/monthly/eighth-monthly filing thresholds. The current official schedule is labeled effective in 2025, so unchanged applicability through every 2026 payroll and current certificate compatibility must be confirmed before activation.

The District of Columbia capture is in `us/dc/2026-source-capture.json`. It records current 2026 filing forms and deadlines but deliberately does not turn the last located 2016 FR-230 tables into current calculation rules. OTR also marks D-4 and D-4A as under review, so the capture remains an explicitly incomplete, non-executable record pending a current agency calculation publication.

Georgia's capture is in `us/ga/2026-source-capture.json`. It separates the 5.19% rate through May 10 from the 4.99% rate beginning May 11, preserves the post-change periodic deductions and examples, nonresident dual threshold, and filing schedules. The archived pre-change deduction inputs and rounding must still be obtained before the first interval can be implemented.

Hawaii's capture is in `us/hi/2026-source-capture.json`. It preserves the annualized brackets, allowance deductions, alternative-period allowance values, supplemental aggregation methods, official example, filing schedules, and conditional nonresident 60-day exemption with its construction-contractor exclusion. Periodic bracket matrices, rounding, and state-transition behavior remain activation blockers.

Iowa's capture is in `us/ia/2026-source-capture.json`. It preserves the 2026 four-step 3.8% formula, separate modern and legacy IA W-4 interpretation, filing-status deductions, supplemental rate, reciprocity with Illinois, filing schedules, and official examples. Wage-bracket matrices and generalized rounding remain activation blockers.

Kansas's capture is in `us/ks/2026-source-capture.json`. It preserves all eight single and married percentage schedules, exemption amounts, optional whole-dollar rounding, supplemental treatment, filing thresholds, and distinct resident-credit and multi-state allocation branches. The current guide is dated October 2024, so unchanged 2026 applicability and allocation-fraction rounding must be confirmed before activation.

Kentucky's capture is in `us/ky/2026-source-capture.json`. It preserves the 2026 3.5% annualized formula, $3,360 standard deduction, conditional seven-state reciprocity, filing requirements, and the separate need for work- and residence-aware local occupational taxes. The agency's biweekly example contains an internal typo/inconsistency; rounding, supplemental wages, full tables, and local rules remain activation blockers.

Louisiana's capture is in `us/la/2026-source-capture.json`. It corrects the shared index's rounded 3% rate to the official 3.09%, preserves all three 2026 L-4 standard-deduction choices, negative or positive periodic adjustments, regular treatment of supplemental wages, residency/work-state rules, and filing schedules. Current-publication applicability, rounding, and wage tables remain blockers.

Maryland's state capture is in `us/md/2026-source-capture.json` and links the existing county capture. It preserves state brackets, periodic deductions, reciprocity, nonresident special tax, filing schedules, and the rule that payroll must use Maryland's official combined state/local schedules selected by residence. Those complete combined matrices and locality-boundary tests remain activation blockers.

Massachusetts's capture is in `us/ma/2026-source-capture.json`. It preserves the 5%/9% annualized formula, 2026 surtax threshold, exemption and special-status reductions, the year-to-date retirement deduction cap, supplemental aggregation, nonresident workday allocation, and filing schedules. Mass.gov rejected direct raw-byte retrieval, so checksums, full wage tables, and rounding remain activation blockers.
