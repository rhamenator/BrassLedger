using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class ConsolidationService
{
    public async Task<string?> ExportStatementPackageCsvAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var package = await GetStatementPackageAsync(groupId, periodStart, asOf, cancellationToken);
        if (package is null) return null;
        var csv = new StringBuilder("Record Type,Statement,Section,Line Code,Line Name,Account Type,Company,Source Account,Source Kind,Reference,Translation,Amount,Currency,Period Start,As Of,Status\n");
        foreach (var statement in new[] { package.BalanceSheet, package.IncomeStatement, package.EquityStatement, package.CashFlowStatement })
        foreach (var section in statement.Sections)
        foreach (var account in section.Accounts)
        {
            foreach (var contribution in account.Contributions)
                AppendStatementCsvRow(csv, "Contribution", statement.Name, section.Name, account.AccountNumber, account.AccountName, account.AccountType, contribution.CompanyName,
                    $"{contribution.SourceAccountNumber} · {contribution.SourceAccountName}", contribution.SourceKind, contribution.Reference, contribution.TranslationMethod, contribution.ConvertedBalance, package, string.Empty);
            AppendStatementCsvRow(csv, "Line total", statement.Name, section.Name, account.AccountNumber, account.AccountName, account.AccountType, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, account.Amount, package, string.Empty);
        }
        foreach (var warning in package.Warnings)
            AppendStatementCsvRow(csv, "Warning", string.Empty, string.Empty, string.Empty, warning, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0m, package, package.IsComplete ? "Complete" : "Incomplete");
        foreach (var control in new[]
        {
            ("Balance sheet difference", package.Reconciliation.BalanceSheetDifference),
            ("Equity statement difference", package.Reconciliation.EquityStatementDifference),
            ("Net cash change", package.Reconciliation.NetCashChange),
            ("Cash-flow difference", package.Reconciliation.CashFlowDifference)
        })
            AppendStatementCsvRow(csv, "Reconciliation", string.Empty, string.Empty, string.Empty, control.Item1, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, control.Item2, package, package.IsComplete ? "Complete" : "Incomplete");
        return csv.ToString();
    }

    public async Task<ConsolidatedStatementPackage?> GetStatementPackageAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        if (periodStart > asOf || periodStart == DateOnly.MinValue) return null;
        var current = await GetBalanceReportAsync(groupId, periodStart, asOf, cancellationToken);
        var openingAsOf = periodStart.AddDays(-1);
        var opening = await GetBalanceReportAsync(groupId, openingAsOf, cancellationToken);
        if (current is null || opening is null) return null;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var currentCashAccounts = await EffectiveCashReportingAccountsAsync(db, groupId, asOf, cancellationToken);
        var openingCashAccounts = await EffectiveCashReportingAccountsAsync(db, groupId, openingAsOf, cancellationToken);

        var assets = StatementAccounts(current.Accounts, nameof(AccountType.Asset));
        var liabilities = StatementAccounts(current.Accounts, nameof(AccountType.Liability));
        var recordedEquity = StatementAccounts(current.Accounts, nameof(AccountType.Equity));
        var revenue = StatementAccounts(current.Accounts, nameof(AccountType.Revenue));
        var expenses = StatementAccounts(current.Accounts, nameof(AccountType.Expense));
        var totalRevenue = revenue.Sum(item => item.Amount);
        var totalExpenses = expenses.Sum(item => item.Amount);
        var netIncome = decimal.Round(totalRevenue - totalExpenses, 2, MidpointRounding.AwayFromZero);
        var currentEarnings = new ConsolidatedStatementAccount("CURRENT-EARNINGS", "Current-period earnings", nameof(AccountType.Equity), netIncome,
            revenue.SelectMany(item => item.Contributions).Concat(expenses.SelectMany(item => item.Contributions).Select(item => item with { ConvertedBalance = -item.ConvertedBalance })).ToArray());

        var totalAssets = assets.Sum(item => item.Amount);
        var totalLiabilities = liabilities.Sum(item => item.Amount);
        var totalRecordedEquity = recordedEquity.Sum(item => item.Amount);
        var liabilitiesAndEquity = totalLiabilities + totalRecordedEquity + netIncome;
        var balanceDifference = decimal.Round(totalAssets - liabilitiesAndEquity, 2, MidpointRounding.AwayFromZero);
        var balanceSheet = new ConsolidatedFinancialStatement("BALANCE-SHEET", "Consolidated balance sheet",
            [Section("ASSETS", "Assets", assets), Section("LIABILITIES", "Liabilities", liabilities), Section("EQUITY", "Equity", [.. recordedEquity, currentEarnings])], totalAssets, balanceDifference);

        var incomeStatement = new ConsolidatedFinancialStatement("INCOME-STATEMENT", "Consolidated income statement",
            [Section("REVENUE", "Revenue", revenue), Section("EXPENSES", "Expenses", expenses)], netIncome, 0m);

        var openingEquityAccounts = StatementAccounts(opening.Accounts, nameof(AccountType.Equity));
        var openingEquity = openingEquityAccounts.Sum(item => item.Amount);
        var directEquityMovement = decimal.Round(totalRecordedEquity - openingEquity, 2, MidpointRounding.AwayFromZero);
        var directMovementLine = new ConsolidatedStatementAccount("DIRECT-EQUITY-MOVEMENT", "Direct owner and other recorded equity movements", nameof(AccountType.Equity), directEquityMovement, []);
        var equityEnding = decimal.Round(openingEquity + directEquityMovement + netIncome, 2, MidpointRounding.AwayFromZero);
        var equityDifference = decimal.Round(equityEnding - (totalRecordedEquity + netIncome), 2, MidpointRounding.AwayFromZero);
        var equityStatement = new ConsolidatedFinancialStatement("EQUITY-STATEMENT", "Consolidated statement of changes in equity",
            [Section("OPENING-EQUITY", "Opening recorded equity", openingEquityAccounts), Section("EQUITY-MOVEMENTS", "Changes during the period", [directMovementLine, currentEarnings])], equityEnding, equityDifference);

        var openingCash = CashBalance(opening.Accounts, openingCashAccounts);
        var endingCash = CashBalance(current.Accounts, currentCashAccounts);
        var netCashChange = decimal.Round(endingCash - openingCash, 2, MidpointRounding.AwayFromZero);
        var cashFlowResult = await BuildCashFlowSectionsAsync(db, groupId, periodStart, asOf, current.ReportingCurrency, netCashChange, cancellationToken);
        var cashFlow = new ConsolidatedFinancialStatement("CASH-FLOW", "Consolidated statement of cash flows",
            cashFlowResult.Sections, cashFlowResult.PresentedChange, decimal.Round(netCashChange - cashFlowResult.PresentedChange, 2, MidpointRounding.AwayFromZero));

        var warnings = current.Warnings.Select(warning => $"Closing report: {warning}")
            .Concat(opening.Warnings.Select(warning => $"Opening report: {warning}"))
            .Distinct(StringComparer.Ordinal).ToList();
        var statementPeriods = await db.ConsolidationGroupCompanies.AsNoTracking().Where(period => period.ConsolidationGroupId == groupId && period.EffectiveFrom <= asOf && (period.EffectiveThrough == null || period.EffectiveThrough >= periodStart)).ToListAsync(cancellationToken);
        var statementCompanyIds = statementPeriods.Select(period => period.MemberCompanyId).Distinct().ToArray();
        var statementCompanyNames = await db.Companies.AsNoTracking().Where(company => statementCompanyIds.Contains(company.Id)).ToDictionaryAsync(company => company.Id, company => company.Name, cancellationToken);
        foreach (var companyPeriods in statementPeriods.GroupBy(period => period.MemberCompanyId))
        {
            var ordered = companyPeriods.OrderBy(period => period.EffectiveFrom).ToArray();
            if (ordered[0].EffectiveFrom > periodStart || (ordered[^1].EffectiveThrough is { } finalThrough && finalThrough < asOf)
                || ordered.Select(period => (period.ConsolidationBasis, period.OwnershipPercentage)).Distinct().Count() > 1)
                warnings.Add($"{statementCompanyNames[companyPeriods.Key]} was acquired, disposed, or changed basis/ownership within the statement period. Cash flows use the effective-dated policy, but income, equity, acquisition/disposal presentation, and attribution require a reviewed schedule that is not yet implemented.");
        }
        if (currentCashAccounts.Count == 0 || openingCashAccounts.Count == 0)
            warnings.Add("Cash and cash equivalents could not be identified from effective bank-account consolidation mappings for both statement dates.");
        warnings.AddRange(cashFlowResult.Warnings);
        if (balanceDifference != 0m) warnings.Add($"The balance sheet is out of balance by {balanceDifference:N2} {current.ReportingCurrency}.");
        if (equityDifference != 0m) warnings.Add($"The equity statement does not reconcile to closing presented equity by {equityDifference:N2} {current.ReportingCurrency}.");

        var reconciliation = new ConsolidatedStatementReconciliation(totalAssets, totalLiabilities, totalRecordedEquity, netIncome, liabilitiesAndEquity,
            balanceDifference, openingEquity, directEquityMovement, equityEnding, equityDifference, openingCash, endingCash, netCashChange, cashFlowResult.PresentedChange, cashFlow.ReconciliationDifference);
        var isComplete = warnings.Count == 0 && balanceDifference == 0m && equityDifference == 0m && cashFlow.ReconciliationDifference == 0m;
        return new(current.GroupId, current.GroupName, current.ReportingCurrency, periodStart, asOf, balanceSheet, incomeStatement, equityStatement, cashFlow,
            reconciliation, warnings, isComplete);
    }

    private static ConsolidatedStatementSection Section(string code, string name, IReadOnlyList<ConsolidatedStatementAccount> accounts) =>
        new(code, name, accounts, decimal.Round(accounts.Sum(item => item.Amount), 2, MidpointRounding.AwayFromZero));

    private static ConsolidatedStatementAccount[] StatementAccounts(IReadOnlyList<ConsolidatedAccountBalance> accounts, string accountType) =>
        accounts.Where(account => account.AccountType == accountType).OrderBy(account => account.AccountNumber).ThenBy(account => account.AccountName)
            .Select(account => new ConsolidatedStatementAccount(account.AccountNumber, account.AccountName, account.AccountType, account.ConvertedBalance, account.Contributions ?? [])).ToArray();

    private static decimal CashBalance(IReadOnlyList<ConsolidatedAccountBalance> accounts, IReadOnlySet<(string Number, string Name)> cashAccounts) =>
        decimal.Round(accounts.Where(account => account.AccountType == nameof(AccountType.Asset) && cashAccounts.Contains((account.AccountNumber, account.AccountName))).Sum(account => account.ConvertedBalance), 2, MidpointRounding.AwayFromZero);

    private static void AppendStatementCsvRow(StringBuilder csv, string recordType, string statement, string section, string lineCode, string lineName, string accountType, string company, string sourceAccount, string sourceKind, string reference, string translation, decimal amount, ConsolidatedStatementPackage package, string status) =>
        csv.AppendJoin(',', new[] { recordType, statement, section, lineCode, lineName, accountType, company, sourceAccount, sourceKind, reference, translation }.Select(StatementCsv))
            .Append(',').Append(amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(StatementCsv(package.ReportingCurrency)).Append(',')
            .Append(package.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(package.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(StatementCsv(status)).AppendLine();

    private static string StatementCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static async Task<IReadOnlySet<(string Number, string Name)>> EffectiveCashReportingAccountsAsync(BrassLedgerDbContext db, Guid groupId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var accounts = await (from mapping in db.ConsolidationAccountMappings.AsNoTracking()
                              join bank in db.BankAccounts.AsNoTracking() on new { CompanyId = mapping.MemberCompanyId, AccountId = mapping.MemberAccountId } equals new { bank.CompanyId, AccountId = bank.LedgerAccountId }
                              where mapping.ConsolidationGroupId == groupId && mapping.EffectiveFrom <= asOf && (mapping.EffectiveThrough == null || mapping.EffectiveThrough >= asOf)
                              select new { mapping.ReportingAccountNumber, mapping.ReportingAccountName }).Distinct().ToListAsync(cancellationToken);
        return accounts.Select(account => (account.ReportingAccountNumber, account.ReportingAccountName)).ToHashSet();
    }

    private async Task<CashFlowBuildResult> BuildCashFlowSectionsAsync(BrassLedgerDbContext db, Guid groupId, DateOnly periodStart, DateOnly asOf, string reportingCurrency, decimal netCashChange, CancellationToken cancellationToken)
    {
        var group = await db.ConsolidationGroups.AsNoTracking().SingleAsync(item => item.Id == groupId, cancellationToken);
        var members = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == groupId && item.EffectiveFrom <= asOf && (item.EffectiveThrough == null || item.EffectiveThrough >= periodStart)).ToListAsync(cancellationToken);
        var memberIds = members.Select(item => item.MemberCompanyId).Distinct().ToArray();
        var companies = await db.Companies.AsNoTracking().Where(company => memberIds.Contains(company.Id)).ToDictionaryAsync(company => company.Id, cancellationToken);
        var mappings = await db.ConsolidationAccountMappings.AsNoTracking().Where(mapping => mapping.ConsolidationGroupId == groupId && mapping.EffectiveFrom <= asOf && (mapping.EffectiveThrough == null || mapping.EffectiveThrough >= periodStart)).ToListAsync(cancellationToken);
        var cashAccounts = await db.BankAccounts.AsNoTracking().Where(bank => memberIds.Contains(bank.CompanyId)).Select(bank => new { bank.CompanyId, bank.LedgerAccountId }).ToListAsync(cancellationToken);
        var cashAccountIds = cashAccounts.Select(item => item.LedgerAccountId).ToHashSet();
        var journals = await db.JournalEntries.AsNoTracking().Where(journal => memberIds.Contains(journal.CompanyId) && journal.IsPosted && journal.PostedOn >= periodStart && journal.PostedOn <= asOf).ToListAsync(cancellationToken);
        var journalIds = journals.Select(journal => journal.Id).ToArray();
        var lines = await db.JournalEntryLines.AsNoTracking().Where(line => journalIds.Contains(line.JournalEntryId)).ToListAsync(cancellationToken);
        var accountIds = lines.Select(line => line.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.AsNoTracking().Where(account => accountIds.Contains(account.Id)).ToDictionaryAsync(account => account.Id, cancellationToken);
        var rates = await db.CurrencyExchangeRates.AsNoTracking().Where(rate => rate.CompanyId == group.CompanyId && rate.IsActive && (rate.EffectiveOn <= asOf || (rate.RateType == CurrencyRateType.Average && rate.PeriodStartOn <= asOf))).OrderByDescending(rate => rate.EffectiveOn).ToListAsync(cancellationToken);
        var amounts = new Dictionary<ConsolidationCashFlowActivity, decimal>();
        var details = new Dictionary<ConsolidationCashFlowActivity, List<ConsolidatedAccountContribution>>();
        var warnings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var memberCompanyId in memberIds)
        {
            var company = companies[memberCompanyId];
            var companyPeriods = members.Where(item => item.MemberCompanyId == memberCompanyId).ToArray();
            var memberCashIds = cashAccounts.Where(item => item.CompanyId == memberCompanyId).Select(item => item.LedgerAccountId).ToHashSet();
            foreach (var journal in journals.Where(item => item.CompanyId == memberCompanyId))
            {
                var effectivePeriod = companyPeriods.SingleOrDefault(period => period.EffectiveFrom <= journal.PostedOn && (period.EffectiveThrough == null || period.EffectiveThrough >= journal.PostedOn));
                if (effectivePeriod is null) continue;
                var inclusionFactor = effectivePeriod.ConsolidationBasis == ConsolidationBasis.ProportionateInterest ? effectivePeriod.OwnershipPercentage : 1m;
                var journalLines = lines.Where(line => line.JournalEntryId == journal.Id).ToArray();
                if (!journalLines.Any(line => memberCashIds.Contains(line.AccountId))) continue;
                var rawCashMovement = journalLines.Where(line => memberCashIds.Contains(line.AccountId)).Sum(line => line.Debit - line.Credit);
                if (rawCashMovement == 0m) continue;
                foreach (var line in journalLines.Where(line => !cashAccountIds.Contains(line.AccountId) && line.Debit != line.Credit))
                {
                    var account = accounts[line.AccountId];
                    var mapping = mappings.SingleOrDefault(item => item.MemberCompanyId == memberCompanyId && item.MemberAccountId == line.AccountId && item.EffectiveFrom <= journal.PostedOn && (item.EffectiveThrough == null || item.EffectiveThrough >= journal.PostedOn));
                    var activity = mapping?.CashFlowActivity ?? ConsolidationCashFlowActivity.Unclassified;
                    if (activity != ConsolidationCashFlowActivity.Unclassified && (string.IsNullOrWhiteSpace(mapping!.CashFlowRationale) || !mapping.CashFlowReviewedOn.HasValue))
                    {
                        warnings.Add($"{company.Name} account {account.Number} · {account.Name} has a cash-flow category without retained review evidence and was treated as unclassified.");
                        activity = ConsolidationCashFlowActivity.Unclassified;
                    }
                    var resolution = ResolveRate(company.BaseCurrency, reportingCurrency, CurrencyRateType.Average, journal.PostedOn, rates);
                    if (resolution.Factor is null)
                    {
                        warnings.Add($"{company.Name} cash journal {journal.Reference} on {journal.PostedOn:yyyy-MM-dd} {resolution.Error}; its cash activity was excluded.");
                        continue;
                    }
                    var amount = decimal.Round(-(line.Debit - line.Credit) * resolution.Factor.Value * inclusionFactor, 2, MidpointRounding.AwayFromZero);
                    amounts[activity] = amounts.GetValueOrDefault(activity) + amount;
                    if (!details.TryGetValue(activity, out var activityDetails)) details[activity] = activityDetails = [];
                    activityDetails.Add(new(memberCompanyId, company.Name, account.Number, account.Name, "CashFlow", journal.Reference, amount, "Average"));
                    if (activity == ConsolidationCashFlowActivity.Unclassified)
                        warnings.Add($"{company.Name} account {account.Number} · {account.Name} participates in cash journal {journal.Reference} but has no effective operating, investing, or financing classification.");
                }
            }
        }

        ConsolidatedStatementAccount ActivityLine(ConsolidationCashFlowActivity activity, string name) =>
            new($"CASH-{activity.ToString().ToUpperInvariant()}", name, nameof(AccountType.Asset), decimal.Round(amounts.GetValueOrDefault(activity), 2, MidpointRounding.AwayFromZero), details.GetValueOrDefault(activity) ?? []);
        var classifiedSections = new List<ConsolidatedStatementSection>
        {
            Section("OPERATING", "Operating activities", [ActivityLine(ConsolidationCashFlowActivity.Operating, "Net cash from operating activities")]),
            Section("INVESTING", "Investing activities", [ActivityLine(ConsolidationCashFlowActivity.Investing, "Net cash from investing activities")]),
            Section("FINANCING", "Financing activities", [ActivityLine(ConsolidationCashFlowActivity.Financing, "Net cash from financing activities")])
        };
        if (amounts.GetValueOrDefault(ConsolidationCashFlowActivity.Unclassified) != 0m)
            classifiedSections.Add(Section("UNCLASSIFIED", "Unclassified cash activity", [ActivityLine(ConsolidationCashFlowActivity.Unclassified, "Cash activity requiring reviewed classification")]));
        var classifiedChange = decimal.Round(amounts.Values.Sum(), 2, MidpointRounding.AwayFromZero);
        var exchangeAndConsolidationEffect = decimal.Round(netCashChange - classifiedChange, 2, MidpointRounding.AwayFromZero);
        classifiedSections.Add(Section("RECONCILING-EFFECTS", "Exchange-rate and consolidation effects", [new("CASH-RECONCILING-EFFECT", "Effect of exchange rates, consolidation entries, and rounding on cash", nameof(AccountType.Asset), exchangeAndConsolidationEffect, [])]));
        return new(classifiedSections, decimal.Round(classifiedChange + exchangeAndConsolidationEffect, 2, MidpointRounding.AwayFromZero), warnings.ToArray());
    }

    private sealed record CashFlowBuildResult(IReadOnlyList<ConsolidatedStatementSection> Sections, decimal PresentedChange, IReadOnlyList<string> Warnings);
}
