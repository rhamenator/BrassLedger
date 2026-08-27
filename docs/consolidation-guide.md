# Consolidation and currency reporting

BrassLedger's current consolidation report is a controlled as-of balance foundation. It derives each member company's natural account balances from posted journal lines through the requested date. It does not read today's mutable account balance for a historical report, and it retains inactive accounts that had historical activity.

## Ownership periods

Create a consolidation group in **Administration → Consolidation and currencies** only when the active operator is an owner of every selected company. Give each initial member an effective-from date, optional effective-through date, and ownership percentage above 0% and no more than 100%.

Ownership is retained as periods rather than a replaceable current percentage. Periods for the same group and company cannot overlap. Closing or correcting a period requires its current concurrency token; a stale screen cannot overwrite another operator's change. Adding or revising a period also changes the group concurrency token so concurrent ownership changes serialize. Every accepted group and ownership change creates a company-scoped business-audit event. Historical reports select the ownership period effective on their as-of date.

Existing undated memberships are migrated with an open period beginning at the minimum supported date. Migration assigns nonblank concurrency tokens. The former one-row-per-company index becomes a group/company/effective-from unique index so later ownership periods can coexist with history.

## Controlled translation behavior

Before relying on a report, choose **Map accounts** for the group and map every member-company source account with a material balance to an explicit reporting account number and name. Mapping periods are effective-dated and cannot overlap for the same source account. A reporting number must retain one name and account type during overlapping periods, preventing unrelated accounts from being merged merely because their local numbers happen to match. The source company, source account, reporting identity, dates, status, concurrency token, operator, and audit event remain retained. Close a mapping with an effective-through date before adding its successor; deactivation also requires an end date so past reports remain reproducible.

The report uses only the mapping effective on its as-of date. An unmapped or multiply mapped nonzero balance is excluded with a warning that identifies the company, source account, source-currency amount, and reason. A zero balance does not create noise. Inactive source accounts remain available because they may contain historical activity. Account mappings classify balances but do not alter any member company's chart or ledger.

Each account mapping retains an explicit translation method. New asset and liability mappings default to **Closing**, revenue and expense mappings default to **Average**, and equity mappings default to **Historical**. The operator can deliberately choose another method, but one reporting account cannot use inconsistent methods during overlapping mapping periods.

Maintain rates as separately typed evidence:

- A closing rate is the latest closing observation effective on or before the report date. It converts the applicable account balance at the report date.
- An average rate covers an explicit start and end date. Each included nominal-account journal line uses the average period covering its posting date. Average periods for one directed currency pair cannot overlap.
- A historical rate is the latest historical observation effective on or before each applicable journal line's posting date. Equity is therefore not translated at today's closing rate.

Every rate retains its type, period, source label, optional source reference, retrieval date, approval state, concurrency token, operator, and audit event. Only an active-company owner can maintain rates. Stale corrections are rejected. BrassLedger does not fall back from a missing average or historical rate to a closing rate, and it does not silently choose between conflicting direct and inverse rate series. Same-currency translation uses one without requiring a rate.

The report accepts a report-period start and as-of date; callers that omit the start use the active parent company's fiscal-year start. Average-translated nominal activity before the period start is excluded. If the resulting source-currency selection does not balance—usually because nominal activity was not closed before the selected period—BrassLedger reports the source imbalance and refuses to disguise it as CTA. Missing, ambiguous, unmapped, or type-inconsistent material balances are likewise excluded with precise warnings, and CTA is not calculated for an incomplete report.

Configure a dedicated CTA reporting account number and name on the consolidation group. When every included member selection balances in its source currency, BrassLedger computes the reporting-currency imbalance created solely by the different controlled translation methods and inserts that amount as an equity balance. The CTA number cannot also be used by a source-account mapping. The report exposes the calculated adjustment and translation method on each row, and rounds ownership-adjusted account results to cents.

## Controlled adjustments and eliminations

Use **Reporting → Consolidation adjustments and eliminations** for reporting-only entries. Each retained batch belongs to one group and an exact period-start/as-of pair; it never changes a member company's general ledger. Lines must use reporting identities established by mappings effective on the as-of date, contain exactly one positive debit or credit within the supported currency range, balance to the cent, and cannot target the system-controlled CTA account. Posting or reversing is blocked when any parent-company accounting period overlaps the reporting range and remains closed.

A preparer can save or correct a Draft or Rejected batch. A different operator must approve or reject it, and the approver cannot post it. Every transition requires its displayed concurrency token. Rejection requires a correction reason. Posted batches are immutable: reversal creates and posts an exact opposite batch, retains both records and their linkage, and marks the original Reversed. Preparation, revision, decision, posting, and reversal create company-scoped business-audit events.

An **Intercompany elimination** additionally requires a match reference and, on every line, two different companies that are effective members on the report date. A manual adjustment cannot carry intercompany-only provenance. Reports revalidate retained balance, account, date, CTA, and company-pair invariants. A corrupt or no-longer-supported batch is excluded with a warning and prevents CTA from concealing the incomplete selection.

Configure reciprocal, effective-dated customer and vendor links with **Administration → Consolidation and currencies → Trading partners**. Only an active owner of the parent and both linked member companies can create or close a link, and the link period must have continuous retained ownership coverage for both companies (it may cross adjacent ownership-percentage periods). A retained link cannot be reassigned to another record; close it and create a successor so historical discovery remains reproducible.

**Reporting → Reviewed intercompany matches** discovers only unambiguous posted invoice/bill pairs whose configured reciprocal links are effective on each document date and whose trimmed references and total amounts match exactly. Discovery currently requires both companies to have the same base currency because source documents do not yet retain transaction currency. Ambiguous and cross-currency candidates remain unlinked and produce warnings. Suggestions and refreshed open balances are retained; an operator can exclude one with an audited reason, restore it, or use it to prepare an elimination. Using a suggestion preserves its generated match reference and exact company pair, but deliberately does not infer accounts, debit/credit direction, or post anything. The operator must choose and balance effective reporting accounts, and the normal independent approval and posting controls still apply.

The consolidated balance includes only batches posted for the requested exact period. Original and reversal entries are both included, producing a transparent zero net effect after reversal. When translated source activity and reporting adjustments use the same reporting identity, the report combines the value and labels its derivation **Mixed**.

This remains short of a complete consolidated-financial-statement workflow. Broader fuzzy/partial/settlement and cross-currency matching, controlled account-line derivation, noncontrolling-interest presentation, and consolidated balance sheet, income statement, equity, and cash-flow presentation remain required. Do not distribute the foundational report as framework-compliant consolidated financial statements until those controls and an independent accounting review are complete.
