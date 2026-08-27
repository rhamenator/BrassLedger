# Consolidation and currency reporting

BrassLedger's current consolidation report is a controlled as-of balance foundation. It derives each member company's natural account balances from posted journal lines through the requested date. It does not read today's mutable account balance for a historical report, and it retains inactive accounts that had historical activity.

## Ownership periods

Create a consolidation group in **Administration → Consolidation and currencies** only when the active operator is an owner of every selected company. Give each initial member an effective-from date, optional effective-through date, and ownership percentage above 0% and no more than 100%.

Ownership is retained as periods rather than a replaceable current percentage. Periods for the same group and company cannot overlap. Closing or correcting a period requires its current concurrency token; a stale screen cannot overwrite another operator's change. Adding or revising a period also changes the group concurrency token so concurrent ownership changes serialize. Every accepted group and ownership change creates a company-scoped business-audit event. Historical reports select the ownership period effective on their as-of date.

Existing undated memberships are migrated with an open period beginning at the minimum supported date. Migration assigns nonblank concurrency tokens. The former one-row-per-company index becomes a group/company/effective-from unique index so later ownership periods can coexist with history.

## Current translation behavior

Before relying on a report, choose **Map accounts** for the group and map every member-company source account with a material balance to an explicit reporting account number and name. Mapping periods are effective-dated and cannot overlap for the same source account. A reporting number must retain one name and account type during overlapping periods, preventing unrelated accounts from being merged merely because their local numbers happen to match. The source company, source account, reporting identity, dates, status, concurrency token, operator, and audit event remain retained. Close a mapping with an effective-through date before adding its successor; deactivation also requires an end date so past reports remain reproducible.

The report uses only the mapping effective on its as-of date. An unmapped or multiply mapped nonzero balance is excluded with a warning that identifies the company, source account, source-currency amount, and reason. A zero balance does not create noise. Inactive source accounts remain available because they may contain historical activity. Account mappings classify balances but do not alter any member company's chart or ledger.

The report uses the latest direct or inverse exchange rate effective on or before its as-of date and rounds each mapped member account's translated, ownership-adjusted balance to cents. A missing member-company rate produces a warning and excludes that company's balances rather than treating the rate as one. Same-currency members use a factor of one.

This is not yet a complete consolidated-financial-statement workflow. Closing/average/historical rate policies, cumulative translation adjustment, noncontrolling-interest presentation, retained controlled consolidation adjustments, intercompany matching and eliminations, and consolidated balance sheet, income statement, and cash-flow presentation remain required. Do not distribute the foundational balance report as framework-compliant consolidated financial statements until those controls and an independent accounting review are complete.
