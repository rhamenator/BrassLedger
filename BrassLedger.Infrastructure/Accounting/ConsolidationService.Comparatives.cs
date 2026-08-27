using System.Globalization;
using System.Text;
using BrassLedger.Application.Accounting;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class ConsolidationService
{
    public async Task<ConsolidatedComparativeStatementPackage?> GetComparativeStatementPackageAsync(Guid groupId, DateOnly currentPeriodStart, DateOnly currentAsOf,
        DateOnly comparisonPeriodStart, DateOnly comparisonAsOf, CancellationToken cancellationToken = default)
    {
        if (currentPeriodStart == DateOnly.MinValue || comparisonPeriodStart == DateOnly.MinValue || currentPeriodStart > currentAsOf
            || comparisonPeriodStart > comparisonAsOf || comparisonAsOf >= currentAsOf) return null;
        var current = await GetStatementPackageAsync(groupId, currentPeriodStart, currentAsOf, cancellationToken);
        var comparison = await GetStatementPackageAsync(groupId, comparisonPeriodStart, comparisonAsOf, cancellationToken);
        if (current is null || comparison is null || current.GroupId != comparison.GroupId || current.ReportingCurrency != comparison.ReportingCurrency) return null;

        var statements = new[]
        {
            CompareStatement(current.BalanceSheet, comparison.BalanceSheet),
            CompareStatement(current.IncomeStatement, comparison.IncomeStatement),
            CompareStatement(current.EquityStatement, comparison.EquityStatement),
            CompareStatement(current.CashFlowStatement, comparison.CashFlowStatement)
        };
        var warnings = current.Warnings.Select(warning => $"Current period: {warning}")
            .Concat(comparison.Warnings.Select(warning => $"Comparison period: {warning}"))
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(current.GroupId, current.GroupName, current.ReportingCurrency, current, comparison, statements, warnings, current.IsComplete && comparison.IsComplete);
    }

    public async Task<string?> ExportComparativeStatementPackageCsvAsync(Guid groupId, DateOnly currentPeriodStart, DateOnly currentAsOf,
        DateOnly comparisonPeriodStart, DateOnly comparisonAsOf, CancellationToken cancellationToken = default)
    {
        var package = await GetComparativeStatementPackageAsync(groupId, currentPeriodStart, currentAsOf, comparisonPeriodStart, comparisonAsOf, cancellationToken);
        if (package is null) return null;
        var csv = new StringBuilder("Record Type,Statement,Account Number,Account Type,Current Section Code,Current Section,Current Caption,Current Amount,Comparison Section Code,Comparison Section,Comparison Caption,Comparison Amount,Variance,Currency,Current Period Start,Current As Of,Comparison Period Start,Comparison As Of,Status\n");
        foreach (var statement in package.Statements)
        foreach (var line in statement.Lines)
            AppendComparativeCsvRow(csv, "Line", statement.Name, line.AccountNumber, line.AccountType, line.CurrentSectionCode, line.CurrentSectionName, line.CurrentLineCaption, line.CurrentAmount,
                line.ComparisonSectionCode, line.ComparisonSectionName, line.ComparisonLineCaption, line.ComparisonAmount, line.Variance, package, string.Empty);
        foreach (var statement in package.Statements)
            AppendComparativeCsvRow(csv, "Statement total", statement.Name, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, statement.CurrentTotal,
                string.Empty, string.Empty, string.Empty, statement.ComparisonTotal, statement.Variance, package, package.IsComplete ? "Complete" : "Incomplete");
        foreach (var control in ComparativeControls(package))
            AppendComparativeCsvRow(csv, "Reconciliation", string.Empty, control.Name, string.Empty, string.Empty, string.Empty, string.Empty, control.Current,
                string.Empty, string.Empty, string.Empty, control.Comparison, control.Current - control.Comparison, package, package.IsComplete ? "Complete" : "Incomplete");
        foreach (var warning in package.Warnings)
            AppendComparativeCsvRow(csv, "Warning", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, warning, 0m, string.Empty, string.Empty, string.Empty, 0m, 0m,
                package, package.IsComplete ? "Complete" : "Incomplete");
        return csv.ToString();
    }

    private static ConsolidatedComparativeFinancialStatement CompareStatement(ConsolidatedFinancialStatement current, ConsolidatedFinancialStatement comparison)
    {
        var currentLines = FlattenStatement(current);
        var comparisonLines = FlattenStatement(comparison);
        var keys = StatementAccountKeys(current).Concat(StatementAccountKeys(comparison)).Distinct().ToArray();
        var lines = keys.Select(key =>
        {
            currentLines.TryGetValue(key, out var currentLine); comparisonLines.TryGetValue(key, out var comparisonLine);
            var currentAmount = currentLine?.Account.Amount ?? 0m; var comparisonAmount = comparisonLine?.Account.Amount ?? 0m;
            return new ConsolidatedComparativeStatementLine(key.Number, key.Type, currentLine?.SectionCode ?? string.Empty, currentLine?.SectionName ?? string.Empty,
                currentLine?.Account.AccountName ?? string.Empty, currentAmount, comparisonLine?.SectionCode ?? string.Empty, comparisonLine?.SectionName ?? string.Empty,
                comparisonLine?.Account.AccountName ?? string.Empty, comparisonAmount, decimal.Round(currentAmount - comparisonAmount, 2, MidpointRounding.AwayFromZero));
        }).ToArray();
        return new(current.Code, current.Name, current.Total, comparison.Total, decimal.Round(current.Total - comparison.Total, 2, MidpointRounding.AwayFromZero), lines);
    }

    private static Dictionary<(string Number, string Type), ComparativeSourceLine> FlattenStatement(ConsolidatedFinancialStatement statement)
    {
        var result = new Dictionary<(string Number, string Type), ComparativeSourceLine>();
        foreach (var section in statement.Sections)
        foreach (var account in section.Accounts)
            result[(account.AccountNumber, account.AccountType)] = new(section.Code, section.Name, account);
        return result;
    }

    private static IEnumerable<(string Number, string Type)> StatementAccountKeys(ConsolidatedFinancialStatement statement) =>
        statement.Sections.SelectMany(section => section.Accounts).Select(account => (account.AccountNumber, account.AccountType));

    private static IEnumerable<(string Name, decimal Current, decimal Comparison)> ComparativeControls(ConsolidatedComparativeStatementPackage package)
    {
        yield return ("Balance sheet difference", package.Current.Reconciliation.BalanceSheetDifference, package.Comparison.Reconciliation.BalanceSheetDifference);
        yield return ("Equity statement difference", package.Current.Reconciliation.EquityStatementDifference, package.Comparison.Reconciliation.EquityStatementDifference);
        yield return ("Net cash change", package.Current.Reconciliation.NetCashChange, package.Comparison.Reconciliation.NetCashChange);
        yield return ("Cash-flow difference", package.Current.Reconciliation.CashFlowDifference, package.Comparison.Reconciliation.CashFlowDifference);
    }

    private static void AppendComparativeCsvRow(StringBuilder csv, string recordType, string statement, string accountNumber, string accountType,
        string currentSectionCode, string currentSection, string currentCaption, decimal currentAmount, string comparisonSectionCode, string comparisonSection, string comparisonCaption, decimal comparisonAmount,
        decimal variance, ConsolidatedComparativeStatementPackage package, string status) =>
        csv.AppendJoin(',', new[] { recordType, statement, accountNumber, accountType, currentSectionCode, currentSection, currentCaption }.Select(StatementCsv))
            .Append(',').Append(currentAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
            .AppendJoin(',', new[] { comparisonSectionCode, comparisonSection, comparisonCaption }.Select(StatementCsv)).Append(',')
            .Append(comparisonAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(variance.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
            .Append(StatementCsv(package.ReportingCurrency)).Append(',').Append(package.Current.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
            .Append(package.Current.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(package.Comparison.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
            .Append(package.Comparison.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(StatementCsv(status)).AppendLine();

    private sealed record ComparativeSourceLine(string SectionCode, string SectionName, ConsolidatedStatementAccount Account);
}
