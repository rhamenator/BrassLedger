using ClosedXML.Excel;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.WPFonts;
using BrassLedger.Application.Accounting;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class ConsolidationService
{
    public async Task<byte[]?> ExportStatementPackageExcelAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var package = await GetStatementPackageAsync(groupId, periodStart, asOf, cancellationToken);
        return package is null ? null : ConsolidatedStatementDocumentExporter.CreateExcel(package);
    }

    public async Task<byte[]?> ExportStatementPackagePdfAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var package = await GetStatementPackageAsync(groupId, periodStart, asOf, cancellationToken);
        return package is null ? null : ConsolidatedStatementDocumentExporter.CreatePdf(package);
    }

    public async Task<byte[]?> ExportComparativeStatementPackageExcelAsync(Guid groupId, DateOnly currentPeriodStart, DateOnly currentAsOf,
        DateOnly comparisonPeriodStart, DateOnly comparisonAsOf, CancellationToken cancellationToken = default)
    {
        var package = await GetComparativeStatementPackageAsync(groupId, currentPeriodStart, currentAsOf, comparisonPeriodStart, comparisonAsOf, cancellationToken);
        return package is null ? null : ConsolidatedStatementDocumentExporter.CreateComparativeExcel(package);
    }

    public async Task<byte[]?> ExportComparativeStatementPackagePdfAsync(Guid groupId, DateOnly currentPeriodStart, DateOnly currentAsOf,
        DateOnly comparisonPeriodStart, DateOnly comparisonAsOf, CancellationToken cancellationToken = default)
    {
        var package = await GetComparativeStatementPackageAsync(groupId, currentPeriodStart, currentAsOf, comparisonPeriodStart, comparisonAsOf, cancellationToken);
        return package is null ? null : ConsolidatedStatementDocumentExporter.CreateComparativePdf(package);
    }
}

internal static class ConsolidatedStatementDocumentExporter
{
    private const string MoneyFormat = "#,##0.00;[Red](#,##0.00)";
    private static readonly XLColor Navy = XLColor.FromHtml("#17324D");
    private static readonly XLColor Brass = XLColor.FromHtml("#B88746");
    private static readonly XLColor PaleBrass = XLColor.FromHtml("#F4EBDD");
    private static readonly XLColor Warning = XLColor.FromHtml("#FDE8E8");

    public static byte[] CreateExcel(ConsolidatedStatementPackage package)
    {
        using var workbook = NewWorkbook($"{package.GroupName} consolidated statements", package.IsComplete);
        AddStatementSummary(workbook, package);
        foreach (var statement in PackageStatements(package)) AddStatementWorksheet(workbook, package, statement);
        AddSourceWorksheet(workbook, "Source detail", package, "Current");
        AddOwnershipEventWorksheet(workbook, package, "Ownership schedules", "Current");
        AddDisclosureWorksheets(workbook, package, "Current");
        return SaveWorkbook(workbook);
    }

    public static byte[] CreateComparativeExcel(ConsolidatedComparativeStatementPackage package)
    {
        using var workbook = NewWorkbook($"{package.GroupName} comparative consolidated statements", package.IsComplete);
        AddComparativeSummary(workbook, package);
        foreach (var statement in package.Statements) AddComparativeWorksheet(workbook, package, statement);
        AddSourceWorksheet(workbook, "Current sources", package.Current, "Current");
        AddSourceWorksheet(workbook, "Comparison sources", package.Comparison, "Comparison");
        AddOwnershipEventWorksheet(workbook, package.Current, "Current ownership", "Current");
        AddOwnershipEventWorksheet(workbook, package.Comparison, "Comparison ownership", "Comparison");
        AddDisclosureWorksheets(workbook, package.Current, "Current");
        AddDisclosureWorksheets(workbook, package.Comparison, "Comparison");
        return SaveWorkbook(workbook);
    }

    public static byte[] CreatePdf(ConsolidatedStatementPackage package)
    {
        var report = new PdfReport($"{package.GroupName} consolidated statements", package.ReportingCurrency, package.IsComplete,
            $"Period {package.PeriodStart:yyyy-MM-dd} to {package.AsOf:yyyy-MM-dd}", true);
        report.AddSummary(package.Warnings, StatementControls(package));
        foreach (var statement in PackageStatements(package)) report.AddStatement(statement);
        report.AddSources(package, "Source detail");
        report.AddOwnershipEvents(package, "Current-period");
        report.AddDisclosures(package, "Current-period");
        return report.Save();
    }

    public static byte[] CreateComparativePdf(ConsolidatedComparativeStatementPackage package)
    {
        var period = $"Current {package.Current.PeriodStart:yyyy-MM-dd} to {package.Current.AsOf:yyyy-MM-dd} | Comparison {package.Comparison.PeriodStart:yyyy-MM-dd} to {package.Comparison.AsOf:yyyy-MM-dd}";
        var report = new PdfReport($"{package.GroupName} comparative consolidated statements", package.ReportingCurrency, package.IsComplete, period, true);
        report.AddComparativeSummary(package.Warnings, ComparativeControlsForDocuments(package));
        foreach (var statement in package.Statements) report.AddComparativeStatement(statement, package.Current.AsOf, package.Comparison.AsOf);
        report.AddSources(package.Current, "Current-period source detail");
        report.AddSources(package.Comparison, "Comparison-period source detail");
        report.AddOwnershipEvents(package.Current, "Current-period");
        report.AddOwnershipEvents(package.Comparison, "Comparison-period");
        report.AddDisclosures(package.Current, "Current-period");
        report.AddDisclosures(package.Comparison, "Comparison-period");
        return report.Save();
    }

    private static XLWorkbook NewWorkbook(string title, bool isComplete)
    {
        var workbook = new XLWorkbook();
        workbook.Properties.Title = title;
        workbook.Properties.Subject = isComplete ? "Complete controlled consolidated statement package" : "Incomplete controlled consolidated statement package";
        workbook.Properties.Author = "BrassLedger";
        workbook.Properties.Company = "BrassLedger";
        return workbook;
    }

    private static void AddStatementSummary(XLWorkbook workbook, ConsolidatedStatementPackage package)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        WriteTitle(sheet, package.GroupName, "Consolidated statement package", package.ReportingCurrency, package.IsComplete);
        WriteLabelValue(sheet, 5, "Period", $"{package.PeriodStart:yyyy-MM-dd} to {package.AsOf:yyyy-MM-dd}");
        var row = 7;
        row = WriteWarnings(sheet, row, package.Warnings);
        row++;
        WriteTableHeader(sheet, row++, ["Reconciliation control", "Amount"]);
        foreach (var control in StatementControls(package))
        {
            sheet.Cell(row, 1).Value = control.Name;
            sheet.Cell(row, 2).Value = control.Amount;
            sheet.Cell(row, 2).Style.NumberFormat.Format = MoneyFormat;
            row++;
        }
        FinishSheet(sheet, 2, row - 1, [42d, 20d]);
    }

    private static void AddComparativeSummary(XLWorkbook workbook, ConsolidatedComparativeStatementPackage package)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        WriteTitle(sheet, package.GroupName, "Comparative consolidated statement package", package.ReportingCurrency, package.IsComplete);
        WriteLabelValue(sheet, 5, "Current period", $"{package.Current.PeriodStart:yyyy-MM-dd} to {package.Current.AsOf:yyyy-MM-dd}");
        WriteLabelValue(sheet, 6, "Comparison period", $"{package.Comparison.PeriodStart:yyyy-MM-dd} to {package.Comparison.AsOf:yyyy-MM-dd}");
        WriteLabelValue(sheet, 7, "Variance convention", "Current minus comparison");
        var row = 9;
        row = WriteWarnings(sheet, row, package.Warnings);
        row++;
        WriteTableHeader(sheet, row++, ["Reconciliation control", "Current", "Comparison", "Variance"]);
        foreach (var control in ComparativeControlsForDocuments(package))
        {
            sheet.Cell(row, 1).Value = control.Name;
            sheet.Cell(row, 2).Value = control.Current;
            sheet.Cell(row, 3).Value = control.Comparison;
            sheet.Cell(row, 4).Value = control.Variance;
            sheet.Range(row, 2, row, 4).Style.NumberFormat.Format = MoneyFormat;
            row++;
        }
        FinishSheet(sheet, 4, row - 1, [42d, 18d, 18d, 18d]);
    }

    private static void AddStatementWorksheet(XLWorkbook workbook, ConsolidatedStatementPackage package, ConsolidatedFinancialStatement statement)
    {
        var sheet = workbook.Worksheets.Add(StatementSheetName(statement.Code));
        WriteTitle(sheet, statement.Name, $"{package.PeriodStart:yyyy-MM-dd} to {package.AsOf:yyyy-MM-dd}", package.ReportingCurrency, package.IsComplete);
        var row = 6;
        WriteTableHeader(sheet, row++, ["Section code", "Section", "Account", "Line caption", "Type", "Amount", "Sources"]);
        foreach (var section in statement.Sections)
        {
            foreach (var account in section.Accounts)
            {
                sheet.Cell(row, 1).Value = section.Code;
                sheet.Cell(row, 2).Value = section.Name;
                sheet.Cell(row, 3).Value = account.AccountNumber;
                sheet.Cell(row, 4).Value = account.AccountName;
                sheet.Cell(row, 5).Value = account.AccountType;
                sheet.Cell(row, 6).Value = account.Amount;
                sheet.Cell(row, 6).Style.NumberFormat.Format = MoneyFormat;
                sheet.Cell(row, 7).Value = account.Contributions.Count;
                row++;
            }
            sheet.Cell(row, 5).Value = $"{section.Name} total";
            sheet.Cell(row, 6).Value = section.Total;
            StyleTotal(sheet.Range(row, 5, row, 6));
            row++;
        }
        sheet.Cell(row, 5).Value = "Statement total";
        sheet.Cell(row, 6).Value = statement.Total;
        StyleGrandTotal(sheet.Range(row, 5, row, 6));
        FinishSheet(sheet, 7, row, [18d, 24d, 16d, 38d, 16d, 18d, 12d], 6);
    }

    private static void AddComparativeWorksheet(XLWorkbook workbook, ConsolidatedComparativeStatementPackage package, ConsolidatedComparativeFinancialStatement statement)
    {
        var sheet = workbook.Worksheets.Add(StatementSheetName(statement.Code));
        WriteTitle(sheet, statement.Name, "Current minus comparison variance", package.ReportingCurrency, package.IsComplete);
        WriteLabelValue(sheet, 5, "Periods", $"{package.Current.AsOf:yyyy-MM-dd} compared with {package.Comparison.AsOf:yyyy-MM-dd}");
        var row = 7;
        WriteTableHeader(sheet, row++, ["Account", "Type", "Current section code", "Current section / caption", "Current", "Comparison section code", "Comparison section / caption", "Comparison", "Variance"]);
        foreach (var line in statement.Lines)
        {
            sheet.Cell(row, 1).Value = line.AccountNumber;
            sheet.Cell(row, 2).Value = line.AccountType;
            sheet.Cell(row, 3).Value = line.CurrentSectionCode;
            sheet.Cell(row, 4).Value = JoinPresentation(line.CurrentSectionName, line.CurrentLineCaption);
            sheet.Cell(row, 5).Value = line.CurrentAmount;
            sheet.Cell(row, 6).Value = line.ComparisonSectionCode;
            sheet.Cell(row, 7).Value = JoinPresentation(line.ComparisonSectionName, line.ComparisonLineCaption);
            sheet.Cell(row, 8).Value = line.ComparisonAmount;
            sheet.Cell(row, 9).Value = line.Variance;
            sheet.Range(row, 5, row, 9).Style.NumberFormat.Format = MoneyFormat;
            row++;
        }
        sheet.Cell(row, 4).Value = "Statement total";
        sheet.Cell(row, 5).Value = statement.CurrentTotal;
        sheet.Cell(row, 8).Value = statement.ComparisonTotal;
        sheet.Cell(row, 9).Value = statement.Variance;
        StyleGrandTotal(sheet.Range(row, 4, row, 9));
        FinishSheet(sheet, 9, row, [16d, 14d, 22d, 40d, 18d, 22d, 40d, 18d, 18d], 7);
    }

    private static void AddSourceWorksheet(XLWorkbook workbook, string name, ConsolidatedStatementPackage package, string periodLabel)
    {
        var sheet = workbook.Worksheets.Add(name);
        WriteTitle(sheet, $"{periodLabel}-period source detail", $"{package.PeriodStart:yyyy-MM-dd} to {package.AsOf:yyyy-MM-dd}", package.ReportingCurrency, package.IsComplete);
        var row = 6;
        WriteTableHeader(sheet, row++, ["Statement", "Section", "Reporting account", "Line caption", "Company", "Source account", "Source kind", "Reference", "Translation", "Amount"]);
        foreach (var statement in PackageStatements(package))
        foreach (var section in statement.Sections)
        foreach (var account in section.Accounts)
        foreach (var source in account.Contributions)
        {
            var values = new[] { statement.Name, section.Name, account.AccountNumber, account.AccountName, source.CompanyName, $"{source.SourceAccountNumber} — {source.SourceAccountName}", source.SourceKind, source.Reference, source.TranslationMethod };
            for (var column = 0; column < values.Length; column++) sheet.Cell(row, column + 1).Value = values[column];
            sheet.Cell(row, 10).Value = source.ConvertedBalance;
            sheet.Cell(row, 10).Style.NumberFormat.Format = MoneyFormat;
            row++;
        }
        if (row == 7) { sheet.Cell(row, 1).Value = "No source contributions were retained for this package."; row++; }
        FinishSheet(sheet, 10, row - 1, [34d, 24d, 18d, 34d, 28d, 34d, 18d, 28d, 16d, 18d], 6);
    }

    private static void AddOwnershipEventWorksheet(XLWorkbook workbook, ConsolidatedStatementPackage package, string name, string periodLabel)
    {
        if ((package.OwnershipEvents?.Count ?? 0) == 0) return;
        var sheet = workbook.Worksheets.Add(name);
        WriteTitle(sheet, $"{periodLabel}-period ownership schedules", $"{package.PeriodStart:yyyy-MM-dd} to {package.AsOf:yyyy-MM-dd}", package.ReportingCurrency, package.IsComplete);
        var row = 6;
        foreach (var ownershipEvent in package.OwnershipEvents ?? [])
        {
            sheet.Cell(row++, 1).Value = $"{ownershipEvent.EventDate:yyyy-MM-dd} · {ownershipEvent.EventType} · {ownershipEvent.Reference}";
            WriteLabelValue(sheet, row++, "Subject", ownershipEvent.SubjectCompanyName);
            WriteLabelValue(sheet, row++, "Framework", $"{ownershipEvent.FrameworkCode} · {ownershipEvent.FrameworkEdition}");
            WriteLabelValue(sheet, row++, "Review control", $"{ownershipEvent.Status}; prepared by {ownershipEvent.PreparedBy} at {ownershipEvent.PreparedAtUtc:O}; approved by {ownershipEvent.ApprovedBy ?? "Unavailable user"} at {ownershipEvent.ApprovedAtUtc:O}; posted by {ownershipEvent.PostedBy ?? "Unavailable user"} at {ownershipEvent.PostedAtUtc:O}");
            WriteLabelValue(sheet, row++, "Retained document", $"JSON schema {ownershipEvent.SchemaVersion}; SHA-256 {ownershipEvent.ContentSha256}; source {ownershipEvent.Content.SourceReference}");
            WriteLabelValue(sheet, row++, "Ownership", $"{ownershipEvent.Content.OwnershipBefore:P4} before; {ownershipEvent.Content.OwnershipAfter:P4} after; NCI method {ownershipEvent.Content.NciMeasurementMethod}");
            WriteLabelValue(sheet, row++, "Rationale", ownershipEvent.Content.MeasurementRationale);
            WriteTableHeader(sheet, row++, ["Measurement", "Amount"]);
            foreach (var measurement in OwnershipEventMeasurements(ownershipEvent.Content))
            {
                sheet.Cell(row, 1).Value = measurement.Name; sheet.Cell(row, 2).Value = measurement.Amount; sheet.Cell(row, 2).Style.NumberFormat.Format = MoneyFormat; row++;
            }
            if (ownershipEvent.Content.Acquisition is { } acquisition)
            {
                WriteLabelValue(sheet, row++, "Measurement period ends", acquisition.MeasurementPeriodEndsOn?.ToString("yyyy-MM-dd") ?? "Not retained in legacy schema");
                if ((acquisition.ConsiderationComponents?.Count ?? 0) > 0)
                {
                    sheet.Cell(row++, 1).Value = "Consideration components";
                    WriteTableHeader(sheet, row++, ["Code", "Description", "Type", "Fair value", "Source"]);
                    foreach (var component in acquisition.ConsiderationComponents ?? [])
                    {
                        sheet.Cell(row, 1).Value = component.Code; sheet.Cell(row, 2).Value = component.Description; sheet.Cell(row, 3).Value = component.ComponentType;
                        sheet.Cell(row, 4).Value = component.FairValue; sheet.Cell(row, 4).Style.NumberFormat.Format = MoneyFormat; sheet.Cell(row, 5).Value = component.SourceReference; row++;
                    }
                }
                if ((acquisition.IdentifiableItems?.Count ?? 0) > 0)
                {
                    sheet.Cell(row++, 1).Value = "Identifiable assets, liabilities, and deferred tax";
                    WriteTableHeader(sheet, row++, ["Code", "Description", "Type", "Fair value", "Deferred-tax asset", "Deferred-tax liability", "Source"]);
                    foreach (var item in acquisition.IdentifiableItems ?? [])
                    {
                        sheet.Cell(row, 1).Value = item.Code; sheet.Cell(row, 2).Value = item.Description; sheet.Cell(row, 3).Value = item.ItemType;
                        sheet.Cell(row, 4).Value = item.FairValue; sheet.Cell(row, 5).Value = item.DeferredTaxAsset; sheet.Cell(row, 6).Value = item.DeferredTaxLiability;
                        sheet.Range(row, 4, row, 6).Style.NumberFormat.Format = MoneyFormat; sheet.Cell(row, 7).Value = item.SourceReference; row++;
                    }
                }
                if ((acquisition.MeasurementPeriodAdjustments?.Count ?? 0) > 0)
                {
                    sheet.Cell(row++, 1).Value = "Measurement-period adjustments";
                    WriteTableHeader(sheet, row++, ["Recognized", "Code", "Description", "Consideration", "Prior interest", "NCI", "Net assets", "Goodwill", "Bargain gain", "Source"]);
                    foreach (var adjustment in acquisition.MeasurementPeriodAdjustments ?? [])
                    {
                        sheet.Cell(row, 1).Value = adjustment.RecognizedOn.ToString("yyyy-MM-dd"); sheet.Cell(row, 2).Value = adjustment.Code; sheet.Cell(row, 3).Value = adjustment.Description;
                        sheet.Cell(row, 4).Value = adjustment.ConsiderationChange; sheet.Cell(row, 5).Value = adjustment.PreviousInterestFairValueChange; sheet.Cell(row, 6).Value = adjustment.NoncontrollingInterestChange;
                        sheet.Cell(row, 7).Value = adjustment.IdentifiableNetAssetsChange; sheet.Cell(row, 8).Value = adjustment.GoodwillChange; sheet.Cell(row, 9).Value = adjustment.BargainPurchaseGainChange;
                        sheet.Range(row, 4, row, 9).Style.NumberFormat.Format = MoneyFormat; sheet.Cell(row, 10).Value = adjustment.SourceReference; row++;
                    }
                }
            }
            WriteTableHeader(sheet, row++, ["Reporting account", "Account name", "Type", "Debit", "Credit", "Description"]);
            foreach (var line in ownershipEvent.Content.PostingLines)
            {
                sheet.Cell(row, 1).Value = line.ReportingAccountNumber; sheet.Cell(row, 2).Value = line.ReportingAccountName; sheet.Cell(row, 3).Value = line.ReportingAccountType;
                sheet.Cell(row, 4).Value = line.Debit; sheet.Cell(row, 5).Value = line.Credit; sheet.Range(row, 4, row, 5).Style.NumberFormat.Format = MoneyFormat; sheet.Cell(row, 6).Value = line.Description; row++;
            }
            row++;
        }
        FinishSheet(sheet, 6, row - 1, [34d, 38d, 18d, 18d, 18d, 52d], 5);
        sheet.RangeUsed()?.Style.Alignment.SetWrapText();
    }

    private static void AddDisclosureWorksheets(XLWorkbook workbook, ConsolidatedStatementPackage package, string periodLabel)
    {
        foreach (var disclosure in package.DisclosurePackages ?? [])
        {
            var proposedName = $"{periodLabel} {disclosure.FrameworkCode} notes";
            var sheet = workbook.Worksheets.Add(proposedName[..Math.Min(31, proposedName.Length)]);
            WriteTitle(sheet, $"{disclosure.FrameworkCode} disclosures", $"{disclosure.FrameworkEdition} · {package.PeriodStart:yyyy-MM-dd} to {package.AsOf:yyyy-MM-dd}", package.ReportingCurrency, true);
            WriteLabelValue(sheet, 5, "Review control", $"Approved by {disclosure.ApprovedBy ?? "Unavailable user"} at {disclosure.ApprovedAtUtc:O}; JSON schema {disclosure.SchemaVersion}; SHA-256 {disclosure.ContentSha256}");
            WriteLabelValue(sheet, 6, "Preparation notes", disclosure.ReviewNotes);
            var row = 8;
            if (disclosure.Content.FinancingLiabilities.Count > 0)
            {
                sheet.Cell(row++, 1).Value = "Financing-liability reconciliation";
                WriteTableHeader(sheet, row++, ["Code", "Liability", "Balance-sheet line", "Opening", "Financing cash flows", "Acquisitions", "Disposals", "Foreign exchange", "Fair value", "Other noncash", "Closing", "Other explanation", "Source"]);
                foreach (var item in disclosure.Content.FinancingLiabilities)
                {
                    var values = new[] { item.LiabilityCode, item.LiabilityName, item.BalanceSheetLine };
                    for (var column = 0; column < values.Length; column++) sheet.Cell(row, column + 1).Value = values[column];
                    var amounts = new[] { item.OpeningBalance, item.FinancingCashFlows, item.Acquisitions, item.Disposals, item.ForeignExchangeChanges, item.FairValueChanges, item.OtherNonCashChanges, item.ClosingBalance };
                    for (var column = 0; column < amounts.Length; column++) { sheet.Cell(row, column + 4).Value = amounts[column]; sheet.Cell(row, column + 4).Style.NumberFormat.Format = MoneyFormat; }
                    sheet.Cell(row, 12).Value = item.OtherNonCashExplanation; sheet.Cell(row, 13).Value = item.SourceReference; row++;
                }
                row++;
            }
            if (disclosure.Content.SupplierFinanceArrangements.Count > 0)
            {
                sheet.Cell(row++, 1).Value = "Supplier-finance arrangements";
                WriteTableHeader(sheet, row++, ["Code", "Arrangement", "Key terms", "Balance-sheet line", "Opening", "Confirmed", "Paid", "Closing", "Suppliers already paid", "Arrangement due days", "Comparable due days", "Security / guarantees", "Liquidity risk", "Source"]);
                foreach (var item in disclosure.Content.SupplierFinanceArrangements)
                {
                    sheet.Cell(row, 1).Value = item.ArrangementCode; sheet.Cell(row, 2).Value = item.ArrangementName; sheet.Cell(row, 3).Value = item.KeyTerms; sheet.Cell(row, 4).Value = item.BalanceSheetLine;
                    var amounts = new[] { item.OpeningOutstanding, item.ObligationsConfirmed, item.ObligationsPaid, item.ClosingOutstanding, item.SuppliersAlreadyPaid };
                    for (var column = 0; column < amounts.Length; column++) { sheet.Cell(row, column + 5).Value = amounts[column]; sheet.Cell(row, column + 5).Style.NumberFormat.Format = MoneyFormat; }
                    sheet.Cell(row, 10).Value = DayRange(item.PaymentDueMinimumDays, item.PaymentDueMaximumDays); sheet.Cell(row, 11).Value = DayRange(item.ComparablePayablesDueMinimumDays, item.ComparablePayablesDueMaximumDays);
                    sheet.Cell(row, 12).Value = item.SecurityOrGuarantees; sheet.Cell(row, 13).Value = item.LiquidityRiskNotes; sheet.Cell(row, 14).Value = item.SourceReference; row++;
                }
                row++;
            }
            if (disclosure.Content.NarrativeDisclosures.Count > 0)
            {
                sheet.Cell(row++, 1).Value = "Other disclosures";
                WriteTableHeader(sheet, row++, ["Category", "Code", "Title", "Order", "Disclosure", "Source"]);
                foreach (var item in disclosure.Content.NarrativeDisclosures.OrderBy(item => item.SortOrder).ThenBy(item => item.Code))
                {
                    sheet.Cell(row, 1).Value = item.Category; sheet.Cell(row, 2).Value = item.Code; sheet.Cell(row, 3).Value = item.Title; sheet.Cell(row, 4).Value = item.SortOrder; sheet.Cell(row, 5).Value = item.Narrative; sheet.Cell(row, 6).Value = item.SourceReference; row++;
                }
            }
            FinishSheet(sheet, 14, row, [15d, 25d, 42d, 24d, 16d, 16d, 16d, 16d, 18d, 22d, 22d, 32d, 38d, 30d], 8);
            sheet.RangeUsed()?.Style.Alignment.SetWrapText();
        }
    }

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

    private static void WriteTitle(IXLWorksheet sheet, string title, string subtitle, string currency, bool isComplete)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Range(1, 1, 1, 10).Merge().Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Navy);
        sheet.Cell(2, 1).Value = subtitle;
        sheet.Range(2, 1, 2, 10).Merge().Style.Font.SetItalic().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Navy);
        sheet.Cell(3, 1).Value = isComplete ? "COMPLETE" : "INCOMPLETE — DO NOT USE EXTERNALLY UNTIL EVERY WARNING IS RESOLVED";
        sheet.Range(3, 1, 3, 10).Merge().Style.Font.SetBold().Fill.SetBackgroundColor(isComplete ? PaleBrass : Warning);
        WriteLabelValue(sheet, 4, "Reporting currency", currency);
    }

    private static void WriteLabelValue(IXLWorksheet sheet, int row, string label, string value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 1).Style.Font.SetBold();
        sheet.Cell(row, 2).Value = value;
    }

    private static int WriteWarnings(IXLWorksheet sheet, int row, IReadOnlyList<string> warnings)
    {
        sheet.Cell(row, 1).Value = "Warnings";
        sheet.Cell(row, 1).Style.Font.SetBold();
        row++;
        if (warnings.Count == 0) { sheet.Cell(row++, 1).Value = "None"; return row; }
        foreach (var warning in warnings)
        {
            sheet.Cell(row, 1).Value = warning;
            sheet.Range(row, 1, row, 4).Merge().Style.Fill.SetBackgroundColor(Warning).Alignment.SetWrapText();
            row++;
        }
        return row;
    }

    private static void WriteTableHeader(IXLWorksheet sheet, int row, IReadOnlyList<string> headings)
    {
        for (var column = 0; column < headings.Count; column++) sheet.Cell(row, column + 1).Value = headings[column];
        sheet.Range(row, 1, row, headings.Count).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Navy).Alignment.SetWrapText();
    }

    private static void FinishSheet(IXLWorksheet sheet, int columnCount, int lastRow, IReadOnlyList<double> widths, int freezeRows = 1)
    {
        for (var column = 1; column <= widths.Count; column++) sheet.Column(column).Width = widths[column - 1];
        sheet.SheetView.FreezeRows(freezeRows);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.Margins.SetLeft(0.25).SetRight(0.25).SetTop(0.5).SetBottom(0.5);
        sheet.Range(1, 1, Math.Max(lastRow, 1), columnCount).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Rows(1, Math.Max(lastRow, 1)).AdjustToContents();
    }

    private static void StyleTotal(IXLRange range) => range.Style.Font.SetBold().Fill.SetBackgroundColor(PaleBrass).NumberFormat.SetFormat(MoneyFormat);
    private static void StyleGrandTotal(IXLRange range) => range.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Brass).NumberFormat.SetFormat(MoneyFormat);
    private static byte[] SaveWorkbook(XLWorkbook workbook) { using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray(); }
    private static string JoinPresentation(string section, string caption) => string.IsNullOrWhiteSpace(section) ? "Not present" : $"{section} — {caption}";
    private static string StatementSheetName(string code) => code switch { "BALANCE-SHEET" => "Balance sheet", "INCOME-STATEMENT" => "Income statement", "EQUITY-STATEMENT" => "Changes in equity", "CASH-FLOW" => "Cash flows", _ => code[..Math.Min(code.Length, 31)] };

    private static IEnumerable<ConsolidatedFinancialStatement> PackageStatements(ConsolidatedStatementPackage package) =>
        [package.BalanceSheet, package.IncomeStatement, package.EquityStatement, package.CashFlowStatement];

    private static IEnumerable<(string Name, decimal Amount)> StatementControls(ConsolidatedStatementPackage package)
    {
        yield return ("Balance sheet difference", package.Reconciliation.BalanceSheetDifference);
        yield return ("Equity statement difference", package.Reconciliation.EquityStatementDifference);
        yield return ("Net cash change", package.Reconciliation.NetCashChange);
        yield return ("Cash-flow difference", package.Reconciliation.CashFlowDifference);
    }

    private static IEnumerable<(string Name, decimal Current, decimal Comparison, decimal Variance)> ComparativeControlsForDocuments(ConsolidatedComparativeStatementPackage package)
    {
        yield return ComparativeControl("Balance sheet difference", package.Current.Reconciliation.BalanceSheetDifference, package.Comparison.Reconciliation.BalanceSheetDifference);
        yield return ComparativeControl("Equity statement difference", package.Current.Reconciliation.EquityStatementDifference, package.Comparison.Reconciliation.EquityStatementDifference);
        yield return ComparativeControl("Net cash change", package.Current.Reconciliation.NetCashChange, package.Comparison.Reconciliation.NetCashChange);
        yield return ComparativeControl("Cash-flow difference", package.Current.Reconciliation.CashFlowDifference, package.Comparison.Reconciliation.CashFlowDifference);
    }

    private static (string Name, decimal Current, decimal Comparison, decimal Variance) ComparativeControl(string name, decimal current, decimal comparison) =>
        (name, current, comparison, decimal.Round(current - comparison, 2, MidpointRounding.AwayFromZero));

    private sealed class PdfReport
    {
        private const double Margin = 36;
        private readonly PdfDocument _document = new();
        private readonly string _title;
        private readonly string _currency;
        private readonly string _period;
        private readonly bool _complete;
        private readonly bool _landscape;
        private readonly XFont _regular;
        private readonly XFont _bold;
        private readonly XFont _small;
        private PdfPage? _page;
        private XGraphics? _graphics;
        private double _y;

        public PdfReport(string title, string currency, bool complete, string period, bool landscape)
        {
            EnsurePdfFontResolver();
            _title = title; _currency = currency; _complete = complete; _period = period; _landscape = landscape;
            var options = new XPdfFontOptions(PdfFontEncoding.Unicode);
            _regular = new XFont("Segoe WP", 8, XFontStyleEx.Regular, options);
            _bold = new XFont("Segoe WP", 8, XFontStyleEx.Bold, options);
            _small = new XFont("Segoe WP", 7, XFontStyleEx.Regular, options);
            _document.Info.Title = title;
            _document.Info.Subject = complete ? "Complete controlled consolidated statement package" : "Incomplete controlled consolidated statement package";
            _document.Info.Author = "BrassLedger";
            _document.Info.Creator = "BrassLedger controlled reporting";
        }

        public void AddSummary(IReadOnlyList<string> warnings, IEnumerable<(string Name, decimal Amount)> controls)
        {
            NewPage("Package summary");
            DrawStatus();
            DrawHeading("Warnings");
            if (warnings.Count == 0) DrawParagraph("None"); else foreach (var warning in warnings) DrawParagraph(warning, true);
            DrawHeading("Reconciliation controls");
            DrawRows(["Control", $"Amount ({_currency})"], [0.72, 0.28], controls.Select(control => new[] { control.Name, Money(control.Amount) }));
        }

        public void AddComparativeSummary(IReadOnlyList<string> warnings, IEnumerable<(string Name, decimal Current, decimal Comparison, decimal Variance)> controls)
        {
            NewPage("Package summary");
            DrawStatus();
            DrawParagraph("Variance convention: current minus comparison.");
            DrawHeading("Warnings");
            if (warnings.Count == 0) DrawParagraph("None"); else foreach (var warning in warnings) DrawParagraph(warning, true);
            DrawHeading("Reconciliation controls");
            DrawRows(["Control", "Current", "Comparison", "Variance"], [0.52, 0.16, 0.16, 0.16], controls.Select(control => new[] { control.Name, Money(control.Current), Money(control.Comparison), Money(control.Variance) }));
        }

        public void AddStatement(ConsolidatedFinancialStatement statement)
        {
            NewPage(statement.Name);
            foreach (var section in statement.Sections)
            {
                DrawSection($"{section.Code} — {section.Name}");
                DrawRows(["Account", "Line caption", "Type", $"Amount ({_currency})"], [0.18, 0.49, 0.15, 0.18],
                    section.Accounts.Select(account => new[] { account.AccountNumber, account.AccountName, account.AccountType, Money(account.Amount) }));
                DrawTotal($"{section.Name} total", section.Total);
            }
            DrawTotal("Statement total", statement.Total, true);
            DrawParagraph($"Reconciliation difference: {Money(statement.ReconciliationDifference)} {_currency}");
        }

        public void AddComparativeStatement(ConsolidatedComparativeFinancialStatement statement, DateOnly currentAsOf, DateOnly comparisonAsOf)
        {
            NewPage(statement.Name);
            DrawRows(["Account", "Current presentation", currentAsOf.ToString("yyyy-MM-dd"), "Comparison presentation", comparisonAsOf.ToString("yyyy-MM-dd"), "Variance"],
                [0.10, 0.24, 0.14, 0.24, 0.14, 0.14], statement.Lines.Select(line => new[]
                {
                    $"{line.AccountNumber} ({line.AccountType})", JoinPresentation(line.CurrentSectionCode, line.CurrentSectionName, line.CurrentLineCaption), Money(line.CurrentAmount),
                    JoinPresentation(line.ComparisonSectionCode, line.ComparisonSectionName, line.ComparisonLineCaption), Money(line.ComparisonAmount), Money(line.Variance)
                }));
            DrawComparativeTotal(statement.CurrentTotal, statement.ComparisonTotal, statement.Variance);
        }

        public void AddSources(ConsolidatedStatementPackage package, string heading)
        {
            NewPage(heading);
            var rows = PackageStatements(package).SelectMany(statement => statement.Sections.SelectMany(section => section.Accounts.SelectMany(account => account.Contributions.Select(source => new[]
            {
                statement.Code, $"{section.Code} — {section.Name}", account.AccountNumber, account.AccountName, source.CompanyName,
                $"{source.SourceAccountNumber} — {source.SourceAccountName}", source.SourceKind, source.Reference, source.TranslationMethod, Money(source.ConvertedBalance)
            }))));
            DrawRows(["Statement", "Section", "Report acct", "Line caption", "Company", "Source account", "Kind", "Reference", "Translation", "Amount"],
                [0.08, 0.10, 0.08, 0.14, 0.12, 0.16, 0.08, 0.10, 0.07, 0.07], rows);
        }

        public void AddOwnershipEvents(ConsolidatedStatementPackage package, string periodLabel)
        {
            foreach (var ownershipEvent in package.OwnershipEvents ?? [])
            {
                NewPage($"{periodLabel} ownership schedule");
                DrawSection($"{ownershipEvent.EventDate:yyyy-MM-dd} · {ownershipEvent.EventType} · {ownershipEvent.Reference}");
                DrawParagraph($"Subject: {ownershipEvent.SubjectCompanyName}. Framework: {ownershipEvent.FrameworkCode} · {ownershipEvent.FrameworkEdition}. Status: {ownershipEvent.Status}.");
                DrawParagraph($"Prepared by {ownershipEvent.PreparedBy} at {ownershipEvent.PreparedAtUtc:O}; approved by {ownershipEvent.ApprovedBy ?? "Unavailable user"} at {ownershipEvent.ApprovedAtUtc:O}; posted by {ownershipEvent.PostedBy ?? "Unavailable user"} at {ownershipEvent.PostedAtUtc:O}.");
                DrawParagraph($"Retained JSON schema {ownershipEvent.SchemaVersion}; SHA-256 {ownershipEvent.ContentSha256}; source: {ownershipEvent.Content.SourceReference}.");
                DrawParagraph($"Ownership before {ownershipEvent.Content.OwnershipBefore:P4}; after {ownershipEvent.Content.OwnershipAfter:P4}; NCI method {ownershipEvent.Content.NciMeasurementMethod}. {ownershipEvent.Content.MeasurementRationale}");
                DrawHeading("Measurement");
                DrawRows(["Measure", $"Amount ({_currency})"], [0.72, 0.28], OwnershipEventMeasurements(ownershipEvent.Content).Select(item => new[] { item.Name, Money(item.Amount) }));
                if (ownershipEvent.Content.Acquisition is { } acquisition)
                {
                    DrawParagraph($"Measurement period ends: {acquisition.MeasurementPeriodEndsOn?.ToString("yyyy-MM-dd") ?? "Not retained in legacy schema"}.");
                    if ((acquisition.ConsiderationComponents?.Count ?? 0) > 0)
                    {
                        DrawHeading("Consideration components");
                        DrawRows(["Code / description", "Type", "Fair value", "Source"], [0.34, 0.18, 0.16, 0.32],
                            acquisition.ConsiderationComponents!.Select(item => new[] { $"{item.Code} — {item.Description}", item.ComponentType, Money(item.FairValue), item.SourceReference }));
                    }
                    if ((acquisition.IdentifiableItems?.Count ?? 0) > 0)
                    {
                        DrawHeading("Identifiable assets, liabilities, and deferred tax");
                        DrawRows(["Code / description", "Type", "Fair value", "Deferred tax", "Source"], [0.30, 0.12, 0.14, 0.18, 0.26],
                            acquisition.IdentifiableItems!.Select(item => new[] { $"{item.Code} — {item.Description}", item.ItemType, Money(item.FairValue), $"Asset {Money(item.DeferredTaxAsset)}; liability {Money(item.DeferredTaxLiability)}", item.SourceReference }));
                    }
                    if ((acquisition.MeasurementPeriodAdjustments?.Count ?? 0) > 0)
                    {
                        DrawHeading("Measurement-period adjustments");
                        DrawRows(["Date / code / description", "Changes", "Source"], [0.34, 0.42, 0.24],
                            acquisition.MeasurementPeriodAdjustments!.Select(item => new[]
                            {
                                $"{item.RecognizedOn:yyyy-MM-dd} · {item.Code} — {item.Description}",
                                $"Consideration {Money(item.ConsiderationChange)}; prior interest {Money(item.PreviousInterestFairValueChange)}; NCI {Money(item.NoncontrollingInterestChange)}; net assets {Money(item.IdentifiableNetAssetsChange)}; goodwill {Money(item.GoodwillChange)}; bargain gain {Money(item.BargainPurchaseGainChange)}",
                                item.SourceReference
                            }));
                    }
                }
                DrawHeading("Posting");
                DrawRows(["Account", "Name", "Type", "Debit", "Credit", "Description"], [0.13, 0.20, 0.12, 0.12, 0.12, 0.31], ownershipEvent.Content.PostingLines.Select(line => new[] { line.ReportingAccountNumber, line.ReportingAccountName, line.ReportingAccountType, Money(line.Debit), Money(line.Credit), line.Description }));
            }
        }

        public void AddDisclosures(ConsolidatedStatementPackage package, string periodLabel)
        {
            foreach (var disclosure in package.DisclosurePackages ?? [])
            {
                NewPage($"{periodLabel} {disclosure.FrameworkCode} disclosures");
                DrawParagraph($"Framework edition: {disclosure.FrameworkEdition}. JSON schema: {disclosure.SchemaVersion}. SHA-256: {disclosure.ContentSha256}. Approved by {disclosure.ApprovedBy ?? "Unavailable user"} at {disclosure.ApprovedAtUtc:O}.");
                if (!string.IsNullOrWhiteSpace(disclosure.ReviewNotes)) DrawParagraph($"Preparation notes: {disclosure.ReviewNotes}");
                if (disclosure.Content.FinancingLiabilities.Count > 0)
                {
                    DrawHeading("Financing-liability reconciliation");
                    DrawRows(["Code / liability", "Balance-sheet line", "Opening", "Cash flows", "Noncash movements", "Closing", "Source"], [0.19, 0.15, 0.10, 0.10, 0.20, 0.10, 0.16],
                        disclosure.Content.FinancingLiabilities.Select(item => new[] { $"{item.LiabilityCode} — {item.LiabilityName}", item.BalanceSheetLine, Money(item.OpeningBalance), Money(item.FinancingCashFlows), $"Acq {Money(item.Acquisitions)}; disp {Money(item.Disposals)}; FX {Money(item.ForeignExchangeChanges)}; FV {Money(item.FairValueChanges)}; other {Money(item.OtherNonCashChanges)}{(string.IsNullOrWhiteSpace(item.OtherNonCashExplanation) ? string.Empty : $" — {item.OtherNonCashExplanation}")}", Money(item.ClosingBalance), item.SourceReference }));
                }
                if (disclosure.Content.SupplierFinanceArrangements.Count > 0)
                {
                    DrawHeading("Supplier-finance arrangements");
                    DrawRows(["Code / arrangement", "Terms / presentation", "Opening", "Confirmed", "Paid", "Closing / supplier paid", "Due ranges / liquidity / source"], [0.17, 0.24, 0.09, 0.09, 0.09, 0.13, 0.19],
                        disclosure.Content.SupplierFinanceArrangements.Select(item => new[] { $"{item.ArrangementCode} — {item.ArrangementName}", $"{item.KeyTerms} | {item.BalanceSheetLine}", Money(item.OpeningOutstanding), Money(item.ObligationsConfirmed), Money(item.ObligationsPaid), $"{Money(item.ClosingOutstanding)} / {Money(item.SuppliersAlreadyPaid)}", $"Arrangement {DayRange(item.PaymentDueMinimumDays, item.PaymentDueMaximumDays)} days; comparable {DayRange(item.ComparablePayablesDueMinimumDays, item.ComparablePayablesDueMaximumDays)} days. {item.SecurityOrGuarantees} {item.LiquidityRiskNotes} Source: {item.SourceReference}" }));
                }
                if (disclosure.Content.NarrativeDisclosures.Count > 0)
                {
                    DrawHeading("Other disclosures");
                    foreach (var item in disclosure.Content.NarrativeDisclosures.OrderBy(item => item.SortOrder).ThenBy(item => item.Code))
                    {
                        DrawSection($"{item.Category} · {item.Code} — {item.Title}");
                        DrawParagraph(item.Narrative);
                        DrawParagraph($"Source: {item.SourceReference}");
                    }
                }
            }
        }

        public byte[] Save()
        {
            _graphics?.Dispose();
            using var stream = new MemoryStream();
            _document.Save(stream);
            return stream.ToArray();
        }

        private void NewPage(string heading)
        {
            _graphics?.Dispose();
            _page = _document.AddPage();
            _page.Size = PageSize.Letter;
            _page.Orientation = _landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
            _graphics = XGraphics.FromPdfPage(_page);
            _y = Margin;
            _graphics.DrawString(_title, new XFont("Segoe WP", 13, XFontStyleEx.Bold, new XPdfFontOptions(PdfFontEncoding.Unicode)), XBrushes.DarkSlateGray, new XRect(Margin, _y, ContentWidth, 18), XStringFormats.TopLeft);
            _y += 20;
            _graphics.DrawString(_period, _small, XBrushes.DarkSlateGray, new XRect(Margin, _y, ContentWidth, 13), XStringFormats.TopLeft);
            _y += 16;
            _graphics.DrawString(heading, new XFont("Segoe WP", 11, XFontStyleEx.Bold, new XPdfFontOptions(PdfFontEncoding.Unicode)), XBrushes.Black, new XRect(Margin, _y, ContentWidth, 16), XStringFormats.TopLeft);
            _y += 22;
            _graphics.DrawString($"Page {_document.PageCount} | {_currency} | {(_complete ? "COMPLETE" : "INCOMPLETE")}", _small, XBrushes.Gray,
                new XRect(Margin, PageHeight - 24, ContentWidth, 10), XStringFormats.TopRight);
        }

        private void EnsureSpace(double height, string continuationHeading = "Continued") { if (_y + height > PageHeight - 34) NewPage(continuationHeading); }
        private double PageHeight => _page!.Height.Point;
        private double ContentWidth => _page!.Width.Point - 2 * Margin;

        private void DrawStatus()
        {
            EnsureSpace(30);
            var text = _complete ? "COMPLETE" : "INCOMPLETE — DO NOT USE EXTERNALLY UNTIL EVERY WARNING IS RESOLVED";
            _graphics!.DrawRectangle(new XSolidBrush(_complete ? XColor.FromArgb(244, 235, 221) : XColor.FromArgb(253, 232, 232)), Margin, _y, ContentWidth, 24);
            _graphics.DrawString(text, _bold, XBrushes.Black, new XRect(Margin + 6, _y + 6, ContentWidth - 12, 12), XStringFormats.TopLeft);
            _y += 32;
        }

        private void DrawHeading(string text)
        {
            EnsureSpace(24, text);
            _graphics!.DrawString(text, _bold, XBrushes.Black, new XRect(Margin, _y, ContentWidth, 14), XStringFormats.TopLeft);
            _y += 18;
        }

        private void DrawSection(string text)
        {
            EnsureSpace(24, text);
            _graphics!.DrawRectangle(new XSolidBrush(XColor.FromArgb(244, 235, 221)), Margin, _y, ContentWidth, 18);
            _graphics.DrawString(text, _bold, XBrushes.Black, new XRect(Margin + 4, _y + 4, ContentWidth - 8, 11), XStringFormats.TopLeft);
            _y += 22;
        }

        private void DrawParagraph(string text, bool warning = false)
        {
            var lines = Wrap(text, ContentWidth - 8, _small);
            var height = Math.Max(22, lines.Count * 10 + 10);
            EnsureSpace(height + 3);
            if (warning) _graphics!.DrawRectangle(new XSolidBrush(XColor.FromArgb(253, 232, 232)), Margin, _y, ContentWidth, height);
            for (var index = 0; index < lines.Count; index++)
                _graphics!.DrawString(lines[index], _small, XBrushes.Black, new XRect(Margin + 4, _y + 5 + index * 10, ContentWidth - 8, 10), XStringFormats.TopLeft);
            _y += height + 3;
        }

        private void DrawRows(IReadOnlyList<string> headings, IReadOnlyList<double> proportions, IEnumerable<string[]> rows)
        {
            EnsureSpace(TableRowHeight(headings, proportions, _bold));
            DrawTableRow(headings, proportions, true);
            foreach (var row in rows)
            {
                var height = TableRowHeight(row, proportions, _small);
                if (_y + height > PageHeight - 34)
                {
                    NewPage("Continued");
                    DrawTableRow(headings, proportions, true);
                }
                DrawTableRow(row, proportions, false);
            }
        }

        private void DrawTableRow(IReadOnlyList<string> values, IReadOnlyList<double> proportions, bool header)
        {
            var graphics = _graphics!;
            var font = header ? _bold : _small;
            var height = TableRowHeight(values, proportions, font);
            var x = Margin;
            for (var index = 0; index < values.Count; index++)
            {
                var width = ContentWidth * proportions[index];
                graphics.DrawRectangle(XPens.LightGray, header ? new XSolidBrush(XColor.FromArgb(23, 50, 77)) : XBrushes.White, x, _y, width, height);
                var lines = Wrap(values[index], width - 6, font);
                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                    graphics.DrawString(lines[lineIndex], font, header ? XBrushes.White : XBrushes.Black,
                        new XRect(x + 3, _y + 4 + lineIndex * 9, width - 6, 9), IsMoney(values[index]) ? XStringFormats.TopRight : XStringFormats.TopLeft);
                x += width;
            }
            _y += height;
        }

        private double TableRowHeight(IReadOnlyList<string> values, IReadOnlyList<double> proportions, XFont font)
        {
            var lines = values.Select((value, index) => Wrap(value, ContentWidth * proportions[index] - 6, font).Count).DefaultIfEmpty(1).Max();
            return Math.Max(18, lines * 9 + 8);
        }

        private void DrawTotal(string label, decimal amount, bool grand = false)
        {
            EnsureSpace(20);
            var brush = new XSolidBrush(grand ? XColor.FromArgb(184, 135, 70) : XColor.FromArgb(244, 235, 221));
            _graphics!.DrawRectangle(brush, Margin, _y, ContentWidth, 18);
            _graphics.DrawString(label, _bold, grand ? XBrushes.White : XBrushes.Black, new XRect(Margin + 4, _y + 4, ContentWidth * .75, 10), XStringFormats.TopLeft);
            _graphics.DrawString(Money(amount), _bold, grand ? XBrushes.White : XBrushes.Black, new XRect(Margin + ContentWidth * .75, _y + 4, ContentWidth * .24, 10), XStringFormats.TopRight);
            _y += 22;
        }

        private void DrawComparativeTotal(decimal current, decimal comparison, decimal variance)
        {
            EnsureSpace(22);
            _graphics!.DrawRectangle(new XSolidBrush(XColor.FromArgb(184, 135, 70)), Margin, _y, ContentWidth, 18);
            _graphics.DrawString("Statement total", _bold, XBrushes.White, new XRect(Margin + 4, _y + 4, ContentWidth * .24, 10), XStringFormats.TopLeft);
            _graphics.DrawString($"Current {Money(current)} | Comparison {Money(comparison)} | Variance {Money(variance)}", _bold, XBrushes.White,
                new XRect(Margin + ContentWidth * .25, _y + 4, ContentWidth * .74, 10), XStringFormats.TopRight);
            _y += 22;
        }

        private IReadOnlyList<string> Wrap(string value, double width, XFont font)
        {
            if (string.IsNullOrEmpty(value)) return [string.Empty];
            var result = new List<string>();
            var remaining = value;
            while (remaining.Length > 0)
            {
                if (_graphics!.MeasureString(remaining, font).Width <= width) { result.Add(remaining); break; }
                var split = remaining.Length;
                while (split > 1 && _graphics.MeasureString(remaining[..split], font).Width > width) split--;
                var wordBreak = remaining.LastIndexOf(' ', Math.Min(split, remaining.Length - 1), Math.Min(split, remaining.Length - 1));
                if (wordBreak > 0) split = wordBreak;
                result.Add(remaining[..split].TrimEnd());
                remaining = remaining[split..].TrimStart();
            }
            return result;
        }

        private static bool IsMoney(string value) => (value.Contains('.') || value.StartsWith('(')) && decimal.TryParse(value.Replace(",", string.Empty).Replace("(", "-").Replace(")", string.Empty), out _);
        private static string Money(decimal value) => value < 0 ? $"({Math.Abs(value):N2})" : value.ToString("N2");
    }

    private static string JoinPresentation(string code, string section, string caption) =>
        string.IsNullOrWhiteSpace(section) ? "Not present" : $"{code} — {section} — {caption}";

    private static readonly object FontResolverLock = new();
    private static void EnsurePdfFontResolver()
    {
        if (GlobalFontSettings.FontResolver is not null) return;
        lock (FontResolverLock)
        {
            GlobalFontSettings.FontResolver ??= new SegoeWpFontResolver();
        }
    }

    private sealed class SegoeWpFontResolver : IFontResolver
    {
        private const string RegularFace = "SegoeWP";
        private const string BoldFace = "SegoeWPBold";
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? BoldFace : RegularFace, false, isItalic);
        public byte[] GetFont(string faceName) => faceName == BoldFace ? FontDataHelper.SegoeWPBold : FontDataHelper.SegoeWP;
    }
}
