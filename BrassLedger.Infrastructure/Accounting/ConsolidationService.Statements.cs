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
        AppendDisclosureCsvRows(csv, package);
        AppendOwnershipEventCsvRows(csv, package);
        return csv.ToString();
    }

    private static void AppendOwnershipEventCsvRows(StringBuilder csv, ConsolidatedStatementPackage package)
    {
        foreach (var ownershipEvent in package.OwnershipEvents ?? [])
        {
            var control = $"{ownershipEvent.FrameworkEdition} | schema {ownershipEvent.SchemaVersion} | SHA-256 {ownershipEvent.ContentSha256} | source {ownershipEvent.Content.SourceReference}";
            foreach (var measurement in OwnershipEventMeasurements(ownershipEvent.Content))
                AppendStatementCsvRow(csv, "Ownership measurement", ownershipEvent.FrameworkCode, ownershipEvent.EventType, ownershipEvent.Reference, measurement.Name,
                    ownershipEvent.Content.NciMeasurementMethod, ownershipEvent.SubjectCompanyName, ownershipEvent.Content.MeasurementRationale, "Posted ownership event", control,
                    ownershipEvent.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), measurement.Amount, package, ownershipEvent.Status);
            foreach (var line in ownershipEvent.Content.PostingLines)
                AppendStatementCsvRow(csv, "Ownership posting", ownershipEvent.FrameworkCode, ownershipEvent.EventType, line.ReportingAccountNumber, line.ReportingAccountName,
                    line.ReportingAccountType, ownershipEvent.SubjectCompanyName, line.Description, "Posted ownership event", ownershipEvent.Reference,
                    ownershipEvent.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), decimal.Round(line.Debit - line.Credit, 2, MidpointRounding.AwayFromZero), package, ownershipEvent.Status);
        }
    }

    private static void AppendDisclosureCsvRows(StringBuilder csv, ConsolidatedStatementPackage package)
    {
        foreach (var disclosure in package.DisclosurePackages ?? [])
        {
            foreach (var item in disclosure.Content.FinancingLiabilities)
            foreach (var movement in new[] { ("Opening balance", item.OpeningBalance), ("Financing cash flows", item.FinancingCashFlows), ("Acquisitions", item.Acquisitions), ("Disposals", item.Disposals), ("Foreign exchange", item.ForeignExchangeChanges), ("Fair value", item.FairValueChanges), ("Other noncash", item.OtherNonCashChanges), ("Closing balance", item.ClosingBalance) })
                AppendStatementCsvRow(csv, "Disclosure", disclosure.FrameworkCode, "Financing liabilities", item.LiabilityCode, item.LiabilityName, item.BalanceSheetLine, movement.Item1, item.SourceReference, "Approved disclosure", item.OtherNonCashExplanation, disclosure.FrameworkEdition, movement.Item2, package, disclosure.Status);
            foreach (var item in disclosure.Content.SupplierFinanceArrangements)
            foreach (var measure in new[] { ("Opening outstanding", item.OpeningOutstanding), ("Obligations confirmed", item.ObligationsConfirmed), ("Obligations paid", item.ObligationsPaid), ("Closing outstanding", item.ClosingOutstanding), ("Suppliers already paid", item.SuppliersAlreadyPaid) })
                AppendStatementCsvRow(csv, "Disclosure", disclosure.FrameworkCode, "Supplier finance", item.ArrangementCode, item.ArrangementName, item.BalanceSheetLine, measure.Item1, item.SourceReference, "Approved disclosure", $"{item.KeyTerms} | Arrangement due {DayRange(item.PaymentDueMinimumDays, item.PaymentDueMaximumDays)} days; comparable {DayRange(item.ComparablePayablesDueMinimumDays, item.ComparablePayablesDueMaximumDays)} days. {item.SecurityOrGuarantees} {item.LiquidityRiskNotes}", disclosure.FrameworkEdition, measure.Item2, package, disclosure.Status);
            foreach (var item in disclosure.Content.NarrativeDisclosures.OrderBy(item => item.SortOrder).ThenBy(item => item.Code))
                AppendStatementCsvRow(csv, "Disclosure", disclosure.FrameworkCode, item.Category, item.Code, item.Title, "Narrative", item.Narrative, item.SourceReference, "Approved disclosure", item.SourceReference, disclosure.FrameworkEdition, 0m, package, disclosure.Status);
        }
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
        var presentations = await db.ConsolidationStatementPresentations.AsNoTracking()
            .Where(item => item.ConsolidationGroupId == groupId && item.EffectiveFrom <= asOf && (item.EffectiveThrough == null || item.EffectiveThrough >= asOf))
            .ToListAsync(cancellationToken);
        var presentationWarnings = new List<string>();

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
        var balanceSheet = new ConsolidatedFinancialStatement(BalanceSheetCode, "Consolidated balance sheet",
            PresentedSections(BalanceSheetCode, [.. assets, .. liabilities, .. recordedEquity, currentEarnings], presentations, presentationWarnings), totalAssets, balanceDifference);

        var incomeStatement = new ConsolidatedFinancialStatement(IncomeStatementCode, "Consolidated income statement",
            PresentedSections(IncomeStatementCode, [.. revenue, .. expenses], presentations, presentationWarnings), netIncome, 0m);

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
            .Concat(presentationWarnings)
            .Distinct(StringComparer.Ordinal).ToList();
        var statementPeriods = await db.ConsolidationGroupCompanies.AsNoTracking().Where(period => period.ConsolidationGroupId == groupId && period.EffectiveFrom <= asOf && (period.EffectiveThrough == null || period.EffectiveThrough >= periodStart)).ToListAsync(cancellationToken);
        var statementCompanyIds = statementPeriods.Select(period => period.MemberCompanyId).Distinct().ToArray();
        var statementCompanyNames = await db.Companies.AsNoTracking().Where(company => statementCompanyIds.Contains(company.Id)).ToDictionaryAsync(company => company.Id, company => company.Name, cancellationToken);
        var reportOwnershipEventEntities = await db.ConsolidationOwnershipEvents.AsNoTracking()
            .Where(item => item.CompanyId == CurrentCompanyId() && item.ConsolidationGroupId == groupId && item.EventDate <= asOf && (item.Status == "Posted" || item.Status == "Reversed"))
            .OrderBy(item => item.EventDate).ThenBy(item => item.Reference).ToArrayAsync(cancellationToken);
        var validReportOwnershipEvents = new List<ConsolidationOwnershipEvent>();
        foreach (var ownershipEvent in reportOwnershipEventEntities)
        {
            var retainedError = await ValidateRetainedOwnershipEventAsync(db, ownershipEvent, cancellationToken);
            if (retainedError is null) validReportOwnershipEvents.Add(ownershipEvent);
        }
        var validPeriodOwnershipEvents = validReportOwnershipEvents.Where(item => item.EventDate >= periodStart).ToArray();
        foreach (var companyPeriods in statementPeriods.GroupBy(period => period.MemberCompanyId))
        {
            var ordered = companyPeriods.OrderBy(period => period.EffectiveFrom).ToArray();
            var companyEvents = validPeriodOwnershipEvents.Where(item => item.SubjectCompanyId == companyPeriods.Key).ToArray();
            if (ordered[0].EffectiveFrom > periodStart && !companyEvents.Any(item => item.EventDate == ordered[0].EffectiveFrom && item.EventType is ConsolidationOwnershipEventType.AcquisitionOfControl or ConsolidationOwnershipEventType.StepAcquisition))
                warnings.Add($"{statementCompanyNames[companyPeriods.Key]} entered the group within the statement period without a posted acquisition-of-control or step-acquisition schedule.");
            if (ordered[^1].EffectiveThrough is { } finalThrough && finalThrough < asOf && !companyEvents.Any(item => item.EventDate == finalThrough && item.EventType == ConsolidationOwnershipEventType.LossOfControl))
                warnings.Add($"{statementCompanyNames[companyPeriods.Key]} left the group within the statement period without a posted loss-of-control schedule.");
            foreach (var transition in ordered.Zip(ordered.Skip(1)))
            {
                var isContiguous = transition.First.EffectiveThrough is { } through && through != DateOnly.MaxValue && through.AddDays(1) == transition.Second.EffectiveFrom;
                if (!isContiguous)
                {
                    if (transition.First.EffectiveThrough is { } departureDate && !companyEvents.Any(item => item.EventDate == departureDate && item.EventType == ConsolidationOwnershipEventType.LossOfControl))
                        warnings.Add($"{statementCompanyNames[companyPeriods.Key]} left the group on {departureDate:yyyy-MM-dd} without a posted loss-of-control schedule.");
                    if (!companyEvents.Any(item => item.EventDate == transition.Second.EffectiveFrom && item.EventType == ConsolidationOwnershipEventType.AcquisitionOfControl))
                        warnings.Add($"{statementCompanyNames[companyPeriods.Key]} reentered the group on {transition.Second.EffectiveFrom:yyyy-MM-dd} without a posted acquisition-of-control schedule.");
                }
                else if ((transition.First.ConsolidationBasis != transition.Second.ConsolidationBasis || transition.First.OwnershipPercentage != transition.Second.OwnershipPercentage)
                    && !companyEvents.Any(item => item.EventDate == transition.Second.EffectiveFrom && item.EventType is ConsolidationOwnershipEventType.StepAcquisition or ConsolidationOwnershipEventType.OwnershipChangeWithoutLossOfControl))
                    warnings.Add($"{statementCompanyNames[companyPeriods.Key]} changed basis or ownership on {transition.Second.EffectiveFrom:yyyy-MM-dd} without a posted step-acquisition or continuing-control ownership-change schedule.");
            }
        }
        if (currentCashAccounts.Count == 0 || openingCashAccounts.Count == 0)
            warnings.Add("Cash and cash equivalents could not be identified from effective bank-account consolidation mappings for both statement dates.");
        warnings.AddRange(cashFlowResult.Warnings);
        if (balanceDifference != 0m) warnings.Add($"The balance sheet is out of balance by {balanceDifference:N2} {current.ReportingCurrency}.");
        if (equityDifference != 0m) warnings.Add($"The equity statement does not reconcile to closing presented equity by {equityDifference:N2} {current.ReportingCurrency}.");

        var currentCompanyId = CurrentCompanyId();
        var disclosureEntities = await db.ConsolidationDisclosurePackages.AsNoTracking()
            .Where(item => item.CompanyId == currentCompanyId && item.ConsolidationGroupId == groupId && item.PeriodStart == periodStart && item.AsOf == asOf)
            .OrderBy(item => item.FrameworkCode).ToArrayAsync(cancellationToken);
        var disclosureUserIds = disclosureEntities.SelectMany(item => new[] { item.PreparedByUserId, item.ApprovedByUserId, item.RejectedByUserId }).Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        var disclosureUsers = await db.Users.AsNoTracking().Where(item => disclosureUserIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => string.IsNullOrWhiteSpace(item.DisplayName) ? item.UserName : item.DisplayName, cancellationToken);
        var approvedDisclosures = new List<ConsolidationDisclosurePackageSnapshot>();
        foreach (var disclosure in disclosureEntities)
        {
            var snapshot = ToDisclosureSnapshot(disclosure, disclosureUsers);
            if (snapshot is null)
            {
                warnings.Add($"The retained {disclosure.FrameworkCode} disclosure package contains unreadable or incompatible JSON and was excluded.");
                continue;
            }
            if (disclosure.Status == "Approved") approvedDisclosures.Add(snapshot);
            else warnings.Add($"The {disclosure.FrameworkCode} {disclosure.FrameworkEdition} disclosure package is {disclosure.Status.ToLowerInvariant()} and was excluded until independent approval.");
        }

        var ownershipReferences = current.Accounts.SelectMany(item => item.Contributions ?? [])
            .Where(item => item.TranslationMethod is "OwnershipEvent" or "OwnershipEventCarryforward").Select(item => item.Reference).ToHashSet(StringComparer.Ordinal);
        var packageOwnershipEvents = validReportOwnershipEvents.Where(item => ownershipReferences.Contains(item.Reference)).ToArray();
        var ownershipUserIds = packageOwnershipEvents.SelectMany(item => new[] { item.PreparedByUserId, item.ApprovedByUserId, item.RejectedByUserId, item.PostedByUserId })
            .Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        var ownershipUsers = await db.Users.AsNoTracking().Where(item => ownershipUserIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => string.IsNullOrWhiteSpace(item.DisplayName) ? item.UserName : item.DisplayName, cancellationToken);
        var ownershipCompanyIds = packageOwnershipEvents.Select(item => item.SubjectCompanyId).Distinct().ToArray();
        var ownershipCompanies = await db.Companies.AsNoTracking().Where(item => ownershipCompanyIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var ownershipSnapshots = packageOwnershipEvents.Select(item => ToOwnershipEventSnapshot(item, ownershipCompanies, ownershipUsers)).Where(item => item is not null).Select(item => item!).ToArray();

        var reconciliation = new ConsolidatedStatementReconciliation(totalAssets, totalLiabilities, totalRecordedEquity, netIncome, liabilitiesAndEquity,
            balanceDifference, openingEquity, directEquityMovement, equityEnding, equityDifference, openingCash, endingCash, netCashChange, cashFlowResult.PresentedChange, cashFlow.ReconciliationDifference);
        var isComplete = warnings.Count == 0 && balanceDifference == 0m && equityDifference == 0m && cashFlow.ReconciliationDifference == 0m;
        return new(current.GroupId, current.GroupName, current.ReportingCurrency, periodStart, asOf, balanceSheet, incomeStatement, equityStatement, cashFlow,
            reconciliation, warnings, isComplete, approvedDisclosures, ownershipSnapshots);
    }

    private static ConsolidatedStatementSection Section(string code, string name, IReadOnlyList<ConsolidatedStatementAccount> accounts) =>
        new(code, name, accounts, decimal.Round(accounts.Sum(item => item.Amount), 2, MidpointRounding.AwayFromZero));

    private static ConsolidatedStatementAccount[] StatementAccounts(IReadOnlyList<ConsolidatedAccountBalance> accounts, string accountType) =>
        accounts.Where(account => account.AccountType == accountType).OrderBy(account => account.AccountNumber).ThenBy(account => account.AccountName)
            .Select(account => new ConsolidatedStatementAccount(account.AccountNumber, account.AccountName, account.AccountType, account.ConvertedBalance, account.Contributions ?? [])).ToArray();

    private static IReadOnlyList<ConsolidatedStatementSection> PresentedSections(string statementCode, IReadOnlyList<ConsolidatedStatementAccount> accounts,
        IReadOnlyList<ConsolidationStatementPresentation> presentations, ICollection<string> warnings)
    {
        var lines = new List<PresentedLine>();
        foreach (var account in accounts)
        {
            var effective = presentations.Where(item => item.StatementCode == statementCode && item.ReportingAccountNumber == account.AccountNumber
                && item.ReportingAccountName == account.AccountName && item.ReportingAccountType.ToString() == account.AccountType).ToArray();
            if (effective.Length == 1)
            {
                var policy = effective[0];
                lines.Add(new(policy.SectionCode, policy.SectionName, policy.SectionSortOrder, policy.LineSortOrder,
                    account with { AccountName = policy.LineCaption }));
                continue;
            }
            var defaultSection = account.AccountType switch
            {
                nameof(AccountType.Asset) => ("UNCONFIGURED-ASSETS", "Unconfigured assets", 900_000),
                nameof(AccountType.Liability) => ("UNCONFIGURED-LIABILITIES", "Unconfigured liabilities", 900_100),
                nameof(AccountType.Equity) => ("UNCONFIGURED-EQUITY", "Unconfigured equity", 900_200),
                nameof(AccountType.Revenue) => ("UNCONFIGURED-REVENUE", "Unconfigured revenue", 900_000),
                _ => ("UNCONFIGURED-EXPENSES", "Unconfigured expenses", 900_100)
            };
            lines.Add(new(defaultSection.Item1, defaultSection.Item2, defaultSection.Item3, 900_000, account));
            if (account.Amount != 0m)
                warnings.Add(effective.Length == 0
                    ? $"{statementCode} account {account.AccountNumber} · {account.AccountName} has no effective reviewed presentation policy on the statement date."
                    : $"{statementCode} account {account.AccountNumber} · {account.AccountName} has overlapping effective presentation policies and was placed in an unconfigured section.");
        }
        return lines.GroupBy(item => new { item.SectionCode, item.SectionName, item.SectionSortOrder })
            .OrderBy(group => group.Key.SectionSortOrder).ThenBy(group => group.Key.SectionCode)
            .Select(group => Section(group.Key.SectionCode, group.Key.SectionName,
                group.OrderBy(item => item.LineSortOrder).ThenBy(item => item.Account.AccountNumber).Select(item => item.Account).ToArray()))
            .ToArray();
    }

    private static decimal CashBalance(IReadOnlyList<ConsolidatedAccountBalance> accounts, IReadOnlySet<(string Number, string Name)> cashAccounts) =>
        decimal.Round(accounts.Where(account => account.AccountType == nameof(AccountType.Asset) && cashAccounts.Contains((account.AccountNumber, account.AccountName))).Sum(account => account.ConvertedBalance), 2, MidpointRounding.AwayFromZero);

    private static void AppendStatementCsvRow(StringBuilder csv, string recordType, string statement, string section, string lineCode, string lineName, string accountType, string company, string sourceAccount, string sourceKind, string reference, string translation, decimal amount, ConsolidatedStatementPackage package, string status) =>
        csv.AppendJoin(',', new[] { recordType, statement, section, lineCode, lineName, accountType, company, sourceAccount, sourceKind, reference, translation }.Select(StatementCsv))
            .Append(',').Append(amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(StatementCsv(package.ReportingCurrency)).Append(',')
            .Append(package.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(package.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(StatementCsv(status)).AppendLine();

    private static string StatementCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string DayRange(int? minimum, int? maximum) => minimum.HasValue && maximum.HasValue ? $"{minimum}-{maximum}" : "Not provided";

    private static IEnumerable<(string Name, decimal Amount)> OwnershipEventMeasurements(ConsolidationOwnershipEventDocument content)
    {
        if (content.Acquisition is { } acquisition)
        {
            yield return ("Consideration transferred", acquisition.ConsiderationTransferred); yield return ("Previous interest fair value", acquisition.PreviousInterestFairValue);
            yield return ("Noncontrolling interest recognized", acquisition.NoncontrollingInterestRecognized); yield return ("Identifiable net assets fair value", acquisition.IdentifiableNetAssetsFairValue);
            yield return ("Goodwill", acquisition.Goodwill); yield return ("Bargain-purchase gain", acquisition.BargainPurchaseGain);
        }
        if (content.OwnershipChange is { } change)
        {
            yield return ("Consideration paid", change.ConsiderationPaid); yield return ("Consideration received", change.ConsiderationReceived);
            yield return ("Noncontrolling interest increase", change.NoncontrollingInterestIncrease); yield return ("Noncontrolling interest decrease", change.NoncontrollingInterestDecrease);
            yield return ("Parent equity debit", change.ParentEquityDebit); yield return ("Parent equity credit", change.ParentEquityCredit);
        }
        if (content.LossOfControl is { } loss)
        {
            yield return ("Consideration received", loss.ConsiderationReceived); yield return ("Retained interest fair value", loss.RetainedInterestFairValue);
            yield return ("Noncontrolling interest derecognized", loss.NoncontrollingInterestDerecognized); yield return ("Net assets derecognized", loss.NetAssetsDerecognized);
            yield return ("Goodwill derecognized", loss.GoodwillDerecognized); yield return ("OCI reclassification", loss.OciReclassification); yield return ("Gain or loss", loss.GainOrLoss);
        }
        if (content.ProfitAttribution is { } attribution)
        {
            yield return ("Subsidiary profit or loss", attribution.SubsidiaryProfitOrLoss); yield return ("Parent profit or loss", attribution.ParentProfitOrLoss);
            yield return ("NCI profit or loss", attribution.NoncontrollingInterestProfitOrLoss); yield return ("Subsidiary other comprehensive income", attribution.SubsidiaryOtherComprehensiveIncome);
            yield return ("Parent other comprehensive income", attribution.ParentOtherComprehensiveIncome); yield return ("NCI other comprehensive income", attribution.NoncontrollingInterestOtherComprehensiveIncome);
        }
    }

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
    private sealed record PresentedLine(string SectionCode, string SectionName, int SectionSortOrder, int LineSortOrder, ConsolidatedStatementAccount Account);
}
