using System.Globalization;
using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class QuickBooksOnlineInterchangeService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    IAccountingTransactionService transactionService) : IAccountingInterchangeService
{
    private const int MaximumRows = 1000;

    public async Task<AccountingInterchangeExport?> ExportQuickBooksOnlineCsvAsync(string entity, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var normalizedEntity = NormalizeEntity(entity);
        var rows = normalizedEntity switch
        {
            "chart-of-accounts" => (await db.Accounts.Where(x => x.CompanyId == companyId).OrderBy(x => x.Number).ToListAsync(cancellationToken))
                .Select(x => new[] { x.Name, ToQuickBooksType(x), ToQuickBooksDetailType(x), x.Number }),
            "customers" => (await db.Customers.Where(x => x.CompanyId == companyId).OrderBy(x => x.CustomerNumber).ToListAsync(cancellationToken))
                .Select(x => new[] { x.Name, x.Name, x.Email, x.CustomerNumber }),
            "vendors" => (await db.Vendors.Where(x => x.CompanyId == companyId).OrderBy(x => x.VendorNumber).ToListAsync(cancellationToken))
                .Select(x => new[] { x.Name, x.Name, x.Email, x.VendorNumber }),
            "journal-entries" => await ExportJournalEntriesAsync(db, companyId, cancellationToken),
            _ => null
        };
        if (rows is null) return null;

        var header = normalizedEntity == "journal-entries"
            ? new[] { "Journal Number", "Journal Date", "Reference", "Description", "Account Number", "Debit", "Credit", "Line Description" }
            : normalizedEntity == "chart-of-accounts"
            ? new[] { "Name", "Type", "Detail Type", "Number" }
            : new[] { "Display Name", "Company Name", "Email", normalizedEntity == "customers" ? "Customer Number" : "Vendor Number" };
        var csv = string.Join("\r\n", new[] { header }.Concat(rows).Select(x => string.Join(',', x.Select(EscapeCsv))));
        return new AccountingInterchangeExport($"brassledger-{normalizedEntity}-quickbooks-online.csv", "text/csv", System.Text.Encoding.UTF8.GetBytes(csv));
    }

    public async Task<AccountingInterchangeImportResult> ImportQuickBooksOnlineCsvAsync(string entity, Stream content, CancellationToken cancellationToken = default)
    {
        var normalizedEntity = NormalizeEntity(entity);
        if (normalizedEntity is not ("chart-of-accounts" or "customers" or "vendors" or "journal-entries"))
            return AccountingInterchangeImportResult.Failure("Supported imports are chart-of-accounts, customers, vendors, and general journal entries.");

        var parsed = await ReadCsvAsync(content, cancellationToken);
        if (!parsed.Succeeded) return AccountingInterchangeImportResult.Failure(parsed.Error!);
        var rows = parsed.Rows!;
        if (rows.Count == 0) return AccountingInterchangeImportResult.Failure("The CSV contains no data rows.");
        if (rows.Count > MaximumRows) return AccountingInterchangeImportResult.Failure($"A QuickBooks import can contain at most {MaximumRows} rows.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        return normalizedEntity switch
        {
            "chart-of-accounts" => await ImportAccountsAsync(db, companyId, rows, cancellationToken),
            "customers" => await ImportCustomersAsync(db, companyId, rows, cancellationToken),
            "vendors" => await ImportVendorsAsync(db, companyId, rows, cancellationToken),
            _ => await ImportJournalEntriesAsync(db, companyId, rows, cancellationToken)
        };
    }

    private static async Task<IEnumerable<string[]>> ExportJournalEntriesAsync(BrassLedgerDbContext db, Guid companyId, CancellationToken ct)
    {
        var entries = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && entry.SourceModule == "General Ledger").OrderBy(entry => entry.PostedOn).ThenBy(entry => entry.EntryNumber).ToListAsync(ct);
        var entryIds = entries.Select(entry => entry.Id).ToArray();
        var lines = await db.JournalEntryLines.Where(line => entryIds.Contains(line.JournalEntryId)).ToListAsync(ct);
        var accountNumbers = await db.Accounts.Where(account => account.CompanyId == companyId).ToDictionaryAsync(account => account.Id, account => account.Number, ct);
        return entries.SelectMany(entry => lines.Where(line => line.JournalEntryId == entry.Id).Select(line => new[]
        {
            entry.EntryNumber, entry.PostedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), entry.Reference, entry.Description,
            accountNumbers.GetValueOrDefault(line.AccountId, string.Empty), line.Debit.ToString("0.00", CultureInfo.InvariantCulture), line.Credit.ToString("0.00", CultureInfo.InvariantCulture), line.Description
        }));
    }

    private async Task<AccountingInterchangeImportResult> ImportJournalEntriesAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, CancellationToken ct)
    {
        var errors = new List<string>();
        var imports = new Dictionary<string, (DateOnly Date, string Reference, string Description, List<JournalLineRequest> Lines)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var journalNumber = Value(row, "journal number", "journal no", "entry number");
            var dateText = Value(row, "journal date", "date");
            var accountNumber = Value(row, "account number", "account");
            if (string.IsNullOrWhiteSpace(journalNumber) || string.IsNullOrWhiteSpace(accountNumber) || !DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(Value(row, "debit"), NumberStyles.Number, CultureInfo.InvariantCulture, out var debit)
                || !decimal.TryParse(Value(row, "credit"), NumberStyles.Number, CultureInfo.InvariantCulture, out var credit))
            {
                errors.Add($"Row {index + 2}: Journal Number, Journal Date, Account Number, Debit, and Credit are required.");
                continue;
            }
            if (!imports.TryGetValue(journalNumber, out var journal))
            {
                journal = (date, Value(row, "reference"), Value(row, "description"), []);
                imports.Add(journalNumber, journal);
            }
            else if (journal.Date != date)
            {
                errors.Add($"Row {index + 2}: every line in journal '{journalNumber}' must have the same date.");
                continue;
            }
            journal.Lines.Add(new JournalLineRequest(accountNumber, debit, credit, Value(row, "line description", "description")));
        }
        foreach (var (number, journal) in imports)
        {
            if (journal.Lines.Count < 2 || RoundCurrency(journal.Lines.Sum(line => line.Debit)) != RoundCurrency(journal.Lines.Sum(line => line.Credit)))
                errors.Add($"Journal '{number}' must contain at least two balanced lines.");
        }
        var accountNumbers = imports.Values.SelectMany(journal => journal.Lines).Select(line => line.AccountNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var allowedAccountNumbers = await db.Accounts.Where(account => account.CompanyId == companyId && account.IsActive && !account.IsControlAccount && accountNumbers.Contains(account.Number))
            .Select(account => account.Number).ToListAsync(ct);
        errors.AddRange(accountNumbers.Where(number => !allowedAccountNumbers.Contains(number, StringComparer.OrdinalIgnoreCase)).Select(number => $"Account '{number}' is inactive, a control account, or does not exist."));
        var importReferences = imports.Keys.Select(BuildJournalImportReference).ToArray();
        var previouslyImported = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && entry.SourceModule == "General Ledger" && importReferences.Contains(entry.Reference)).Select(entry => entry.Reference).ToListAsync(ct);
        errors.AddRange(previouslyImported.Select(reference => $"QuickBooks journal '{imports.Single(item => BuildJournalImportReference(item.Key) == reference).Key}' was already imported. A file retry will not double-post it."));
        if (errors.Count > 0) return AccountingInterchangeImportResult.Failure(errors.ToArray());
        var posted = await transactionService.PostJournalEntriesAsync(imports.Select(pair => new PostJournalEntryRequest(pair.Value.Date, BuildJournalImportReference(pair.Key), BuildImportedJournalDescription(pair.Key, pair.Value.Reference, pair.Value.Description), pair.Value.Lines)).ToArray(), ct);
        if (!posted.Succeeded) return AccountingInterchangeImportResult.Failure($"Journal import was not committed: {posted.ErrorMessage}");
        return AccountingInterchangeImportResult.Success(imports.Count);
    }

    private static async Task<AccountingInterchangeImportResult> ImportAccountsAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, CancellationToken ct)
    {
        var errors = new List<string>(); var accounts = new List<GeneralLedgerAccount>(); var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]; var name = Value(row, "name", "account name"); var number = Value(row, "number", "account number");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(number)) { errors.Add($"Row {index + 2}: Name and Number are required."); continue; }
            if (!numbers.Add(number)) { errors.Add($"Row {index + 2}: duplicate account number '{number}'."); continue; }
            if (!TryParseAccountType(Value(row, "type", "account type"), out var type)) { errors.Add($"Row {index + 2}: unsupported QuickBooks account type."); continue; }
            accounts.Add(new GeneralLedgerAccount { Id = Guid.NewGuid(), CompanyId = companyId, Name = name, Number = number, Type = type, IsActive = true });
        }
        var existing = await db.Accounts.Where(x => x.CompanyId == companyId && numbers.Contains(x.Number)).Select(x => x.Number).ToListAsync(ct);
        errors.AddRange(existing.Select(x => $"An account with number '{x}' already exists."));
        if (errors.Count > 0) return AccountingInterchangeImportResult.Failure(errors.ToArray());
        db.Accounts.AddRange(accounts); await db.SaveChangesAsync(ct); return AccountingInterchangeImportResult.Success(accounts.Count);
    }

    private static async Task<AccountingInterchangeImportResult> ImportCustomersAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, CancellationToken ct)
    {
        var errors = new List<string>(); var customers = new List<Customer>(); var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]; var name = Value(row, "display name", "name", "company name"); var number = Value(row, "customer number");
            if (string.IsNullOrWhiteSpace(name)) { errors.Add($"Row {index + 2}: Display Name is required."); continue; }
            if (string.IsNullOrWhiteSpace(number)) number = $"QBO-C-{index + 1:D5}";
            if (!numbers.Add(number)) { errors.Add($"Row {index + 2}: duplicate customer number '{number}'."); continue; }
            customers.Add(new Customer { Id = Guid.NewGuid(), CompanyId = companyId, Name = name, CustomerNumber = number, Email = Value(row, "email") });
        }
        var existing = await db.Customers.Where(x => x.CompanyId == companyId && numbers.Contains(x.CustomerNumber)).Select(x => x.CustomerNumber).ToListAsync(ct);
        errors.AddRange(existing.Select(x => $"A customer with number '{x}' already exists."));
        if (errors.Count > 0) return AccountingInterchangeImportResult.Failure(errors.ToArray());
        db.Customers.AddRange(customers); await db.SaveChangesAsync(ct); return AccountingInterchangeImportResult.Success(customers.Count);
    }

    private static async Task<AccountingInterchangeImportResult> ImportVendorsAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, CancellationToken ct)
    {
        var errors = new List<string>(); var vendors = new List<Vendor>(); var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]; var name = Value(row, "display name", "name", "company name"); var number = Value(row, "vendor number");
            if (string.IsNullOrWhiteSpace(name)) { errors.Add($"Row {index + 2}: Display Name is required."); continue; }
            if (string.IsNullOrWhiteSpace(number)) number = $"QBO-V-{index + 1:D5}";
            if (!numbers.Add(number)) { errors.Add($"Row {index + 2}: duplicate vendor number '{number}'."); continue; }
            vendors.Add(new Vendor { Id = Guid.NewGuid(), CompanyId = companyId, Name = name, VendorNumber = number, Email = Value(row, "email") });
        }
        var existing = await db.Vendors.Where(x => x.CompanyId == companyId && numbers.Contains(x.VendorNumber)).Select(x => x.VendorNumber).ToListAsync(ct);
        errors.AddRange(existing.Select(x => $"A vendor with number '{x}' already exists."));
        if (errors.Count > 0) return AccountingInterchangeImportResult.Failure(errors.ToArray());
        db.Vendors.AddRange(vendors); await db.SaveChangesAsync(ct); return AccountingInterchangeImportResult.Success(vendors.Count);
    }

    private static string NormalizeEntity(string entity) => entity.Trim().ToLowerInvariant();
    private static string BuildJournalImportReference(string journalNumber)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"quickbooks-online-journal:{journalNumber.Trim()}"));
        return $"QBO-{Convert.ToHexString(hash)[..24]}";
    }
    private static string BuildImportedJournalDescription(string journalNumber, string sourceReference, string description)
    {
        var provenance = string.IsNullOrWhiteSpace(sourceReference) ? $"QuickBooks journal {journalNumber}" : $"QuickBooks journal {journalNumber}; source reference {sourceReference}";
        return string.IsNullOrWhiteSpace(description) ? provenance : $"{provenance}. {description}";
    }
    private static decimal RoundCurrency(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static string Value(IReadOnlyDictionary<string, string> row, params string[] names) => names.Select(name => row.GetValueOrDefault(name, string.Empty).Trim()).FirstOrDefault(value => value.Length > 0) ?? string.Empty;
    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string ToQuickBooksType(GeneralLedgerAccount account) => account.Type switch
    {
        AccountType.Asset when account.Number == "1100" => "Accounts Receivable",
        AccountType.Asset when account.Name.Contains("inventory", StringComparison.OrdinalIgnoreCase) => "Other Current Asset",
        AccountType.Asset when account.Name.Contains("cash", StringComparison.OrdinalIgnoreCase) || account.Name.Contains("bank", StringComparison.OrdinalIgnoreCase) || account.Name.Contains("clearing", StringComparison.OrdinalIgnoreCase) => "Bank",
        AccountType.Asset => "Other Current Asset",
        AccountType.Liability when account.Number == "2000" => "Accounts Payable",
        AccountType.Liability => "Other Current Liability",
        AccountType.Equity => "Equity",
        AccountType.Revenue => "Income",
        _ => "Expense"
    };

    private static string ToQuickBooksDetailType(GeneralLedgerAccount account) => account.Number switch
    {
        "1000" or "1010" => "Cash on hand",
        "1100" => "Accounts Receivable",
        "1200" => "Inventory",
        "2000" => "Accounts Payable",
        "2100" => "Sales tax payable",
        "2200" => "Payroll tax payables",
        "3000" => "Owner's equity",
        "4000" => "Sales of Product Income",
        "5100" => "Supplies & materials - COGS",
        "6100" => "Payroll Expenses",
        _ => account.Type.ToString()
    };
    private static bool TryParseAccountType(string value, out AccountType type)
    {
        type = value.Trim().ToLowerInvariant() switch { "bank" or "accounts receivable" or "other current asset" or "fixed asset" or "asset" => AccountType.Asset, "accounts payable" or "credit card" or "other current liability" or "long term liability" or "liability" => AccountType.Liability, "equity" => AccountType.Equity, "income" or "other income" or "revenue" => AccountType.Revenue, "expense" or "cost of goods sold" => AccountType.Expense, _ => (AccountType)(-1) };
        return Enum.IsDefined(type);
    }

    private static async Task<(bool Succeeded, List<Dictionary<string, string>>? Rows, string? Error)> ReadCsvAsync(Stream content, CancellationToken ct)
    {
        using var reader = new StreamReader(content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        var records = ParseCsvRecords(text);
        if (records is null) return (false, null, "The CSV contains an unterminated or malformed quoted field.");
        if (records.Count == 0) return (false, null, "The uploaded file is empty.");
        var header = records[0];
        if (header.Count == 0) return (false, null, "The CSV header is malformed.");
        var normalizedHeader = header.Select(x => x.Trim().ToLowerInvariant()).ToArray();
        if (normalizedHeader.Distinct(StringComparer.Ordinal).Count() != normalizedHeader.Length) return (false, null, "CSV headers must be unique.");
        var rows = new List<Dictionary<string, string>>();
        for (var index = 1; index < records.Count; index++)
        {
            var fields = records[index];
            if (fields.Count != normalizedHeader.Length) return (false, null, $"Row {index + 1} has the wrong number of columns.");
            rows.Add(normalizedHeader.Zip(fields, (key, value) => new KeyValuePair<string, string>(key, value)).ToDictionary());
        }
        return (true, rows, null);
    }

    private static List<List<string>>? ParseCsvRecords(string text)
    {
        var records = new List<List<string>>(); var row = new List<string>(); var field = new System.Text.StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character == '"' && quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append(character); i++; }
            else if (character == '"' && (quoted || field.Length == 0)) quoted = !quoted;
            else if (character == '"') return null;
            else if (character == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Any(value => value.Length > 0)) records.Add(row);
                row = new List<string>();
            }
            else field.Append(character);
        }
        if (quoted) return null;
        row.Add(field.ToString());
        if (row.Any(value => value.Length > 0)) records.Add(row);
        return records;
    }

    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken ct)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var claim = httpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (Guid.TryParse(claim, out var id)) return id;
        if (httpContext is not null) throw new UnauthorizedAccessException("An authenticated company context is required.");
        return await db.Companies.Select(x => x.Id).FirstAsync(ct);
    }
}
