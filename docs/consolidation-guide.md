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

This is still a translated-balance foundation, not a complete consolidated-financial-statement workflow. Noncontrolling-interest presentation, retained controlled consolidation adjustments, intercompany matching and eliminations, and consolidated balance sheet, income statement, equity, and cash-flow presentation remain required. Do not distribute the foundational report as framework-compliant consolidated financial statements until those controls and an independent accounting review are complete.
