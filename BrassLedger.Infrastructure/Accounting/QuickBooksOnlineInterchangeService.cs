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
    IHttpContextAccessor httpContextAccessor) : IAccountingInterchangeService
{
    private const int MaximumRows = 1000;
    private const int MaximumBytes = 2 * 1024 * 1024;

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
            "invoices" => await ExportInvoicesAsync(db, companyId, cancellationToken),
            _ => null
        };
        if (rows is null) return null;

        var header = normalizedEntity == "journal-entries"
            ? new[] { "Journal No.", "Journal Date", "Reference", "Journal/Description", "Account Name", "Debits", "Credits", "Line Description" }
            : normalizedEntity == "invoices"
            ? new[] { "Invoice No.", "Customer", "Invoice Date", "Due Date", "Item Amount", "Item Description", "Quantity", "Rate" }
            : normalizedEntity == "chart-of-accounts"
            ? new[] { "Account Name", "Type", "Detail Type", "Account Number" }
            : new[] { "Display Name", "Company Name", "Email", normalizedEntity == "customers" ? "Customer Number" : "Vendor Number" };
        var csv = string.Join("\r\n", new[] { header }.Concat(rows).Select(x => string.Join(',', x.Select(EscapeCsv))));
        return new AccountingInterchangeExport($"brassledger-{normalizedEntity}-quickbooks-online.csv", "text/csv", System.Text.Encoding.UTF8.GetBytes(csv));
    }

    public async Task<AccountingInterchangeImportResult> ImportQuickBooksOnlineCsvAsync(string entity, Stream content, AccountingInterchangeImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var normalizedEntity = NormalizeEntity(entity);
        if (normalizedEntity is not ("chart-of-accounts" or "customers" or "vendors" or "journal-entries" or "invoices"))
            return AccountingInterchangeImportResult.Failure("Supported imports are chart-of-accounts, customers, vendors, general journal entries, and zero-tax invoices.");
        if (normalizedEntity == "invoices" && (!HasPermission(BrassLedgerPermissions.SubledgerPrepare) || !HasPermission(BrassLedgerPermissions.ReceivablesManage)))
            return AccountingInterchangeImportResult.Failure("You are not authorized to prepare accounts-receivable invoice drafts.");
        if (normalizedEntity == "journal-entries" && !HasPermission(BrassLedgerPermissions.JournalPrepare))
            return AccountingInterchangeImportResult.Failure("You are not authorized to prepare general journal drafts.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var parsed = await ReadCsvAsync(content, cancellationToken);
        if (!parsed.Succeeded)
            return await RecordRejectedBatchAsync(db, companyId, normalizedEntity, options, parsed.ContentSha256, 0, [parsed.Error!], cancellationToken);
        var rows = parsed.Rows!;
        if (rows.Count == 0)
            return await RecordRejectedBatchAsync(db, companyId, normalizedEntity, options, parsed.ContentSha256, 0, ["The CSV contains no data rows."], cancellationToken);
        if (rows.Count > MaximumRows)
            return await RecordRejectedBatchAsync(db, companyId, normalizedEntity, options, parsed.ContentSha256, rows.Count, [$"A QuickBooks import can contain at most {MaximumRows} rows."], cancellationToken);

        var committedImportKey = BuildCommittedImportKey(normalizedEntity, parsed.ContentSha256);
        if (!options.DryRun && await db.AccountingInterchangeBatches.AnyAsync(batch => batch.CompanyId == companyId && batch.CommittedImportKey == committedImportKey, cancellationToken))
            return await RecordRejectedBatchAsync(db, companyId, normalizedEntity, options, parsed.ContentSha256, rows.Count, ["This exact QuickBooks file and data type were already imported. Use the recorded batch to reconcile it rather than importing it again."], cancellationToken, duplicateCount: rows.Count);
        var result = normalizedEntity switch
        {
            "chart-of-accounts" => await ImportAccountsAsync(db, companyId, rows, options.DryRun, cancellationToken),
            "customers" => await ImportCustomersAsync(db, companyId, rows, options.DryRun, cancellationToken),
            "vendors" => await ImportVendorsAsync(db, companyId, rows, options.DryRun, cancellationToken),
            "journal-entries" => await ImportJournalEntriesAsync(db, companyId, rows, options.DryRun, cancellationToken),
            _ => await ImportInvoicesAsync(db, companyId, rows, options.DryRun, parsed.ContentSha256, options.FileName, cancellationToken)
        };
        if (!result.Succeeded)
            return await RecordRejectedBatchAsync(db, companyId, normalizedEntity, options, parsed.ContentSha256, rows.Count, result.Errors, cancellationToken);

        var batchStatus = options.DryRun ? "Validated" : normalizedEntity is "invoices" or "journal-entries" ? "DraftsCreated" : "Imported";
        var batch = NewBatch(companyId, normalizedEntity, options, parsed.ContentSha256, batchStatus, rows.Count, result.ImportedCount, [], options.DryRun ? null : committedImportKey);
        db.AccountingInterchangeBatches.Add(batch);
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserId = ResolveUserId(),
            Action = options.DryRun ? "accounting-interchange.quickbooks.validated" : "accounting-interchange.quickbooks.imported",
            EntityType = nameof(AccountingInterchangeBatch),
            EntityId = batch.Id,
            OccurredAtUtc = batch.ProcessedAtUtc,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { provider = "quickbooks-online", entity = normalizedEntity, fileName = Path.GetFileName(options.FileName), contentSha256 = parsed.ContentSha256, rowCount = rows.Count, importedCount = result.ImportedCount, options.DryRun })
        });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) when (!options.DryRun)
        {
            return AccountingInterchangeImportResult.Failure("This exact QuickBooks file and data type were imported concurrently. Refresh the batch history before retrying.") with { DryRun = false, RowCount = rows.Count, ContentSha256 = parsed.ContentSha256, DuplicateCount = rows.Count, RejectedCount = rows.Count };
        }
        return result with { DryRun = options.DryRun, RowCount = rows.Count, ContentSha256 = parsed.ContentSha256, BatchId = batch.Id };
    }

    public async Task<IReadOnlyList<AccountingInterchangeBatchSnapshot>> GetRecentBatchesAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var batches = db.Database.IsSqlite()
            ? await db.AccountingInterchangeBatches.FromSqlInterpolated($"""SELECT * FROM "AccountingInterchangeBatches" WHERE "CompanyId" = {companyId} ORDER BY "ProcessedAtUtc" DESC LIMIT {boundedLimit}""").AsNoTracking().ToListAsync(cancellationToken)
            : await db.AccountingInterchangeBatches.AsNoTracking().Where(batch => batch.CompanyId == companyId).OrderByDescending(batch => batch.ProcessedAtUtc).Take(boundedLimit).ToListAsync(cancellationToken);
        var userIds = batches.Where(batch => batch.ProcessedByUserId.HasValue).Select(batch => batch.ProcessedByUserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        return batches.Select(batch => new AccountingInterchangeBatchSnapshot(batch.Id, batch.ProviderCode, batch.EntityType, batch.FileName, batch.ContentSha256, batch.Status, batch.IsDryRun, batch.RowCount, batch.ImportedCount, batch.DuplicateCount, batch.RejectedCount, DeserializeRejections(batch.RejectionJson), batch.ProcessedByUserId is { } userId ? users.GetValueOrDefault(userId) : null, batch.ProcessedAtUtc)).ToArray();
    }

    private static async Task<IEnumerable<string[]>> ExportJournalEntriesAsync(BrassLedgerDbContext db, Guid companyId, CancellationToken ct)
    {
        var entries = await db.JournalEntries
            .Where(entry => entry.CompanyId == companyId && entry.SourceModule == "General Ledger" && entry.IsPosted && entry.Status == "Posted")
            .OrderBy(entry => entry.PostedOn)
            .ThenBy(entry => entry.EntryNumber)
            .ToListAsync(ct);
        var entryIds = entries.Select(entry => entry.Id).ToArray();
        var lines = await db.JournalEntryLines.Where(line => entryIds.Contains(line.JournalEntryId)).ToListAsync(ct);
        var accountNames = await db.Accounts.Where(account => account.CompanyId == companyId).ToDictionaryAsync(account => account.Id, account => account.Name, ct);
        return entries.SelectMany(entry => lines.Where(line => line.JournalEntryId == entry.Id).Select(line => new[]
        {
            entry.EntryNumber, entry.PostedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), entry.Reference, entry.Description,
            accountNames.GetValueOrDefault(line.AccountId, string.Empty), line.Debit.ToString("0.00", CultureInfo.InvariantCulture), line.Credit.ToString("0.00", CultureInfo.InvariantCulture), line.Description
        }));
    }

    private static async Task<IEnumerable<string[]>> ExportInvoicesAsync(BrassLedgerDbContext db, Guid companyId, CancellationToken ct)
    {
        var invoices = await db.SalesInvoices.AsNoTracking().Where(invoice => invoice.CompanyId == companyId && invoice.Status != "Voided" && invoice.TaxAmount == 0).OrderBy(invoice => invoice.InvoiceDate).ThenBy(invoice => invoice.InvoiceNumber).ToListAsync(ct);
        var invoiceIds = invoices.Select(invoice => invoice.Id).ToArray();
        var lines = await db.SalesInvoiceLines.AsNoTracking().Where(line => invoiceIds.Contains(line.SalesInvoiceId)).OrderBy(line => line.Sequence).ToListAsync(ct);
        var customerIds = invoices.Select(invoice => invoice.CustomerId).Distinct().ToArray();
        var customers = await db.Customers.AsNoTracking().Where(customer => customerIds.Contains(customer.Id)).ToDictionaryAsync(customer => customer.Id, customer => customer.Name, ct);
        return invoices.SelectMany(invoice =>
        {
            var invoiceLines = lines.Where(line => line.SalesInvoiceId == invoice.Id).ToArray();
            if (invoiceLines.Length == 0)
                return new[] { new[] { invoice.InvoiceNumber, customers.GetValueOrDefault(invoice.CustomerId, string.Empty), invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), invoice.Subtotal.ToString("0.00", CultureInfo.InvariantCulture), "Imported invoice", "1", invoice.Subtotal.ToString("0.00", CultureInfo.InvariantCulture) } };
            return invoiceLines.Select(line => new[] { invoice.InvoiceNumber, customers.GetValueOrDefault(invoice.CustomerId, string.Empty), invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), RoundCurrency(line.Quantity * line.UnitPrice - line.DiscountAmount).ToString("0.00", CultureInfo.InvariantCulture), line.Description, line.DiscountAmount == 0 ? line.Quantity.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty, line.DiscountAmount == 0 ? line.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty });
        });
    }

    private async Task<AccountingInterchangeImportResult> ImportJournalEntriesAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, bool dryRun, CancellationToken ct)
    {
        var errors = new List<string>();
        var eligibleAccounts = await db.Accounts.Where(account => account.CompanyId == companyId && account.IsActive && !account.IsControlAccount).Select(account => new { account.Id, account.Number, account.Name }).ToListAsync(ct);
        var imports = new Dictionary<string, (DateOnly Date, string Reference, string Description, List<JournalLineRequest> Lines)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var journalNumber = Value(row, "journal number", "journal no.", "journal no", "entry number");
            var dateText = Value(row, "journal date", "date");
            var accountReference = Value(row, "account name", "account number", "account");
            var accountMatches = eligibleAccounts.Where(account => account.Number.Equals(accountReference, StringComparison.OrdinalIgnoreCase) || account.Name.Equals(accountReference, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (string.IsNullOrWhiteSpace(journalNumber) || accountMatches.Length != 1 || !DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(Value(row, "debits", "debit"), NumberStyles.Number, CultureInfo.InvariantCulture, out var debit)
                || !decimal.TryParse(Value(row, "credits", "credit"), NumberStyles.Number, CultureInfo.InvariantCulture, out var credit))
            {
                errors.Add($"Row {index + 2}: Journal No., Journal Date, one unique active non-control Account Name (or BrassLedger account number), Debits, and Credits are required.");
                continue;
            }
            if (debit < 0 || credit < 0 || (debit == 0 && credit == 0) || (debit > 0 && credit > 0))
            {
                errors.Add($"Row {index + 2}: provide a positive debit or a positive credit, but not both.");
                continue;
            }
            if (!imports.TryGetValue(journalNumber, out var journal))
            {
                journal = (date, Value(row, "reference"), Value(row, "journal/description", "journal", "description"), []);
                imports.Add(journalNumber, journal);
            }
            else if (journal.Date != date)
            {
                errors.Add($"Row {index + 2}: every line in journal '{journalNumber}' must have the same date.");
                continue;
            }
            journal.Lines.Add(new JournalLineRequest(accountMatches[0].Number, debit, credit, Value(row, "line description", "description")));
        }
        foreach (var (number, journal) in imports)
        {
            if (journal.Lines.Count < 2 || RoundCurrency(journal.Lines.Sum(line => line.Debit)) != RoundCurrency(journal.Lines.Sum(line => line.Credit)))
                errors.Add($"Journal '{number}' must contain at least two balanced lines.");
        }
        var importReferences = imports.Keys.Select(BuildJournalImportReference).ToArray();
        var previouslyImported = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && entry.SourceModule == "General Ledger" && importReferences.Contains(entry.Reference)).Select(entry => entry.Reference).ToListAsync(ct);
        errors.AddRange(previouslyImported.Select(reference => $"QuickBooks journal '{imports.Single(item => BuildJournalImportReference(item.Key) == reference).Key}' was already imported. A file retry will not double-post it."));
        if (errors.Count > 0) return AccountingInterchangeImportResult.Failure(errors.ToArray());
        if (dryRun) return AccountingInterchangeImportResult.Success(imports.Count, true, rows.Count);
        var importedByUserId = ResolveUserId();
        var importedAtUtc = DateTimeOffset.UtcNow;
        foreach (var (sourceNumber, journal) in imports)
        {
            var entry = new JournalEntry
            {
                Id = Guid.NewGuid(), CompanyId = companyId, EntryNumber = $"DRAFT-{Guid.NewGuid():N}"[..20], PostedOn = journal.Date,
                SourceModule = "General Ledger", Reference = BuildJournalImportReference(sourceNumber),
                Description = BuildImportedJournalDescription(sourceNumber, journal.Reference, journal.Description),
                TotalAmount = RoundCurrency(journal.Lines.Sum(line => line.Debit)), Status = "Draft", IsPosted = false,
                CreatedByUserId = importedByUserId, CreatedAtUtc = importedAtUtc, ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            db.JournalEntries.Add(entry);
            db.JournalEntryLines.AddRange(journal.Lines.Select(line => new JournalEntryLine
            {
                Id = Guid.NewGuid(), JournalEntryId = entry.Id,
                AccountId = eligibleAccounts.Single(account => account.Number.Equals(line.AccountNumber, StringComparison.OrdinalIgnoreCase)).Id,
                Description = line.Description.Trim(), Debit = line.Debit, Credit = line.Credit
            }));
            db.BusinessAuditEntries.Add(new BusinessAuditEntry
            {
                Id = Guid.NewGuid(), CompanyId = companyId, UserId = importedByUserId, Action = "journal.draft.imported",
                EntityType = nameof(JournalEntry), EntityId = entry.Id, OccurredAtUtc = importedAtUtc,
                DetailJson = System.Text.Json.JsonSerializer.Serialize(new { provider = "quickbooks-online", sourceNumber, entry.Reference, lineCount = journal.Lines.Count })
            });
        }
        return AccountingInterchangeImportResult.Success(imports.Count);
    }

    private async Task<AccountingInterchangeImportResult> ImportInvoicesAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, bool dryRun, string contentSha256, string fileName, CancellationToken ct)
    {
        var errors = new List<string>();
        var customers = await db.Customers.Where(customer => customer.CompanyId == companyId).Select(customer => new { customer.Id, customer.CustomerNumber, customer.Name }).ToListAsync(ct);
        var revenueAccounts = await db.Accounts.Where(account => account.CompanyId == companyId && account.IsActive && !account.IsControlAccount && account.Type == AccountType.Revenue).Select(account => new { account.Number, account.Name, account.OperationalRole }).ToListAsync(ct);
        var defaultRevenueAccount = revenueAccounts.SingleOrDefault(account => account.OperationalRole == AccountingAccountRoles.DefaultRevenue)?.Number ?? string.Empty;
        var imports = new Dictionary<string, (Guid CustomerId, DateOnly InvoiceDate, DateOnly DueDate, List<SalesInvoiceLineRequest> Lines)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var invoiceNumber = Value(row, "invoice no.", "invoice no", "invoice number");
            var customerReference = Value(row, "customer", "customer name");
            var customerMatches = customers.Where(customer => customer.CustomerNumber.Equals(customerReference, StringComparison.OrdinalIgnoreCase) || customer.Name.Equals(customerReference, StringComparison.OrdinalIgnoreCase)).ToArray();
            var accountReference = Value(row, "income account", "revenue account", "account");
            if (string.IsNullOrWhiteSpace(accountReference)) accountReference = defaultRevenueAccount;
            var accountMatches = revenueAccounts.Where(account => account.Number.Equals(accountReference, StringComparison.OrdinalIgnoreCase) || account.Name.Equals(accountReference, StringComparison.OrdinalIgnoreCase)).ToArray();
            var amountText = Value(row, "item amount", "line amount", "amount");
            var taxText = Value(row, "tax amount", "line tax amount");
            if (string.IsNullOrWhiteSpace(invoiceNumber) || customerMatches.Length != 1 || accountMatches.Length != 1
                || !DateOnly.TryParse(Value(row, "invoice date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var invoiceDate)
                || !DateOnly.TryParse(Value(row, "due date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate)
                || dueDate < invoiceDate || !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var itemAmount) || itemAmount <= 0
                || (!string.IsNullOrWhiteSpace(taxText) && (!decimal.TryParse(taxText, NumberStyles.Number, CultureInfo.InvariantCulture, out var taxAmount) || taxAmount != 0)))
            {
                errors.Add($"Row {index + 2}: Invoice No., one existing Customer, Invoice Date, Due Date, positive Item Amount, zero Tax Amount, and one active revenue account are required.");
                continue;
            }
            var quantity = 1m; var rate = itemAmount;
            var quantityText = Value(row, "quantity"); var rateText = Value(row, "rate", "unit price");
            if (!string.IsNullOrWhiteSpace(quantityText) || !string.IsNullOrWhiteSpace(rateText))
            {
                if (!decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity) || quantity <= 0
                    || !decimal.TryParse(rateText, NumberStyles.Number, CultureInfo.InvariantCulture, out rate) || rate < 0
                    || RoundCurrency(quantity * rate) != RoundCurrency(itemAmount))
                {
                    errors.Add($"Row {index + 2}: Quantity multiplied by Rate must equal Item Amount to the cent.");
                    continue;
                }
            }
            if (!imports.TryGetValue(invoiceNumber, out var invoice))
            {
                invoice = (customerMatches[0].Id, invoiceDate, dueDate, []);
                imports.Add(invoiceNumber, invoice);
            }
            else if (invoice.CustomerId != customerMatches[0].Id || invoice.InvoiceDate != invoiceDate || invoice.DueDate != dueDate)
            {
                errors.Add($"Row {index + 2}: every line in invoice '{invoiceNumber}' must use the same customer and dates.");
                continue;
            }
            var description = Value(row, "item description", "description");
            invoice.Lines.Add(new SalesInvoiceLineRequest(string.IsNullOrWhiteSpace(description) ? $"QuickBooks invoice {invoiceNumber}" : description, quantity, rate, 0, 0, accountMatches[0].Number));
        }
        if (imports.Count > 100) errors.Add("A QuickBooks invoice batch can contain at most 100 invoices.");
        var numbers = imports.Keys.ToArray();
        var postedNumbers = await db.SalesInvoices.Where(invoice => invoice.CompanyId == companyId).Select(invoice => invoice.InvoiceNumber).ToListAsync(ct);
        var draftNumbers = await db.SubledgerDocumentWorkflows.Where(workflow => workflow.CompanyId == companyId && workflow.DocumentType == "Invoice" && !workflow.IsRecurringTemplate).Select(workflow => workflow.DocumentNumber).ToListAsync(ct);
        var existingPosted = postedNumbers.Where(existing => numbers.Contains(existing, StringComparer.OrdinalIgnoreCase));
        var existingDrafts = draftNumbers.Where(existing => numbers.Contains(existing, StringComparer.OrdinalIgnoreCase));
        errors.AddRange(existingPosted.Concat(existingDrafts).Distinct(StringComparer.OrdinalIgnoreCase).Select(number => $"Invoice '{number}' already exists as a posted invoice or draft."));
        if (errors.Count > 0) return AccountingInterchangeImportResult.Failure(errors.ToArray());
        if (dryRun) return AccountingInterchangeImportResult.Success(imports.Count, true, rows.Count);
        var now = DateTimeOffset.UtcNow; var userId = ResolveUserId(); var safeFileName = Path.GetFileName(fileName);
        foreach (var (number, invoice) in imports)
        {
            var request = new CreateInvoiceRequest(invoice.CustomerId, number, invoice.InvoiceDate, invoice.DueDate, invoice.Lines.Sum(line => line.Quantity * line.UnitPrice), 0, invoice.Lines[0].RevenueAccountNumber, $"Imported from QuickBooks invoice {number}", invoice.Lines);
            var workflow = new SubledgerDocumentWorkflow { Id = Guid.NewGuid(), CompanyId = companyId, DocumentType = "Invoice", DocumentScope = "company", DocumentNumber = number.Trim(), PayloadJson = System.Text.Json.JsonSerializer.Serialize(request), Status = "Draft", CreatedByUserId = userId, CreatedAtUtc = now, ConcurrencyToken = Guid.NewGuid().ToString("N") };
            db.SubledgerDocumentWorkflows.Add(workflow);
            db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = userId, Action = "subledger-document.draft.imported", EntityType = nameof(SubledgerDocumentWorkflow), EntityId = workflow.Id, OccurredAtUtc = now, DetailJson = System.Text.Json.JsonSerializer.Serialize(new { provider = "quickbooks-online", safeFileName, contentSha256, workflow.DocumentType, workflow.DocumentNumber, lineCount = invoice.Lines.Count }) });
        }
        return AccountingInterchangeImportResult.Success(imports.Count);
    }

    private static async Task<AccountingInterchangeImportResult> ImportAccountsAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, bool dryRun, CancellationToken ct)
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
        if (!dryRun) db.Accounts.AddRange(accounts); return AccountingInterchangeImportResult.Success(accounts.Count, dryRun, rows.Count);
    }

    private static async Task<AccountingInterchangeImportResult> ImportCustomersAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, bool dryRun, CancellationToken ct)
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
        if (!dryRun) db.Customers.AddRange(customers); return AccountingInterchangeImportResult.Success(customers.Count, dryRun, rows.Count);
    }

    private static async Task<AccountingInterchangeImportResult> ImportVendorsAsync(BrassLedgerDbContext db, Guid companyId, List<Dictionary<string, string>> rows, bool dryRun, CancellationToken ct)
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
        if (!dryRun) db.Vendors.AddRange(vendors); return AccountingInterchangeImportResult.Success(vendors.Count, dryRun, rows.Count);
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
        AccountType.Asset when account.OperationalRole == AccountingAccountRoles.AccountsReceivable => "Accounts Receivable",
        AccountType.Asset when account.Name.Contains("inventory", StringComparison.OrdinalIgnoreCase) => "Other Current Asset",
        AccountType.Asset when account.Name.Contains("cash", StringComparison.OrdinalIgnoreCase) || account.Name.Contains("bank", StringComparison.OrdinalIgnoreCase) || account.Name.Contains("clearing", StringComparison.OrdinalIgnoreCase) => "Bank",
        AccountType.Asset => "Other Current Asset",
        AccountType.Liability when account.OperationalRole == AccountingAccountRoles.AccountsPayable => "Accounts Payable",
        AccountType.Liability => "Other Current Liability",
        AccountType.Equity => "Equity",
        AccountType.Revenue => "Income",
        _ => "Expense"
    };

    private static string ToQuickBooksDetailType(GeneralLedgerAccount account) => account.OperationalRole switch
    {
        AccountingAccountRoles.OperatingCash or AccountingAccountRoles.PayrollClearing or AccountingAccountRoles.BankTransferClearing => "Cash on hand",
        AccountingAccountRoles.AccountsReceivable => "Accounts Receivable",
        AccountingAccountRoles.InventoryAsset => "Inventory",
        AccountingAccountRoles.AccountsPayable => "Accounts Payable",
        AccountingAccountRoles.SalesTaxPayable => "Sales tax payable",
        AccountingAccountRoles.PayrollLiabilities => "Payroll tax payables",
        AccountingAccountRoles.OwnerEquity => "Owner's equity",
        AccountingAccountRoles.DefaultRevenue => "Sales of Product Income",
        AccountingAccountRoles.CostOfGoodsSold => "Supplies & materials - COGS",
        AccountingAccountRoles.PayrollExpense => "Payroll Expenses",
        _ => account.Type.ToString()
    };
    private static bool TryParseAccountType(string value, out AccountType type)
    {
        type = value.Trim().ToLowerInvariant() switch { "bank" or "accounts receivable" or "other current asset" or "fixed asset" or "asset" => AccountType.Asset, "accounts payable" or "credit card" or "other current liability" or "long term liability" or "liability" => AccountType.Liability, "equity" => AccountType.Equity, "income" or "other income" or "revenue" => AccountType.Revenue, "expense" or "cost of goods sold" => AccountType.Expense, _ => (AccountType)(-1) };
        return Enum.IsDefined(type);
    }

    private static async Task<(bool Succeeded, List<Dictionary<string, string>>? Rows, string? Error, string ContentSha256)> ReadCsvAsync(Stream content, CancellationToken ct)
    {
        using var buffered = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await content.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (buffered.Length + read > MaximumBytes) return (false, null, "QuickBooks CSV imports are limited to 2 MB.", string.Empty);
            await buffered.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var bytes = buffered.ToArray();
        var contentSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        buffered.Position = 0;
        using var reader = new StreamReader(buffered, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        var records = ParseCsvRecords(text);
        if (records is null) return (false, null, "The CSV contains an unterminated or malformed quoted field.", contentSha256);
        if (records.Count == 0) return (false, null, "The uploaded file is empty.", contentSha256);
        var header = records[0];
        if (header.Count == 0) return (false, null, "The CSV header is malformed.", contentSha256);
        var normalizedHeader = header.Select(x => x.Trim().ToLowerInvariant()).ToArray();
        if (normalizedHeader.Distinct(StringComparer.Ordinal).Count() != normalizedHeader.Length) return (false, null, "CSV headers must be unique.", contentSha256);
        var rows = new List<Dictionary<string, string>>();
        for (var index = 1; index < records.Count; index++)
        {
            var fields = records[index];
            if (fields.Count != normalizedHeader.Length) return (false, null, $"Row {index + 1} has the wrong number of columns.", contentSha256);
            rows.Add(normalizedHeader.Zip(fields, (key, value) => new KeyValuePair<string, string>(key, value)).ToDictionary());
        }
        return (true, rows, null, contentSha256);
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

    private async Task<AccountingInterchangeImportResult> RecordRejectedBatchAsync(BrassLedgerDbContext db, Guid companyId, string entityType, AccountingInterchangeImportOptions options, string contentSha256, int rowCount, IReadOnlyList<string> errors, CancellationToken cancellationToken, int duplicateCount = 0)
    {
        var batch = NewBatch(companyId, entityType, options, contentSha256, duplicateCount > 0 ? "DuplicateRejected" : "Rejected", rowCount, 0, errors, null, duplicateCount);
        db.AccountingInterchangeBatches.Add(batch);
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserId = ResolveUserId(),
            Action = duplicateCount > 0 ? "accounting-interchange.quickbooks.duplicate-rejected" : "accounting-interchange.quickbooks.rejected",
            EntityType = nameof(AccountingInterchangeBatch),
            EntityId = batch.Id,
            OccurredAtUtc = batch.ProcessedAtUtc,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { provider = "quickbooks-online", entity = entityType, batch.FileName, batch.ContentSha256, batch.RowCount, batch.DuplicateCount, batch.RejectedCount, errors, options.DryRun })
        });
        await db.SaveChangesAsync(cancellationToken);
        return AccountingInterchangeImportResult.Failure(errors.ToArray()) with { DryRun = options.DryRun, RowCount = rowCount, ContentSha256 = contentSha256, BatchId = batch.Id, DuplicateCount = duplicateCount, RejectedCount = batch.RejectedCount };
    }

    private AccountingInterchangeBatch NewBatch(Guid companyId, string entityType, AccountingInterchangeImportOptions options, string contentSha256, string status, int rowCount, int importedCount, IReadOnlyList<string> errors, string? committedImportKey, int duplicateCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        ProviderCode = "quickbooks-online",
        EntityType = entityType,
        FileName = Path.GetFileName(options.FileName),
        ContentSha256 = contentSha256,
        CommittedImportKey = committedImportKey,
        Status = status,
        IsDryRun = options.DryRun,
        RowCount = rowCount,
        ImportedCount = importedCount,
        DuplicateCount = duplicateCount,
        RejectedCount = errors.Count == 0 ? 0 : Math.Max(1, rowCount),
        RejectionJson = System.Text.Json.JsonSerializer.Serialize(errors),
        ProcessedByUserId = ResolveUserId(),
        ProcessedAtUtc = DateTimeOffset.UtcNow
    };

    private static string BuildCommittedImportKey(string entityType, string contentSha256) => $"quickbooks-online:{entityType}:{contentSha256}";
    private static IReadOnlyList<string> DeserializeRejections(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (System.Text.Json.JsonException) { return ["Stored rejection details could not be read."]; }
    }

    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken ct)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var claim = httpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (Guid.TryParse(claim, out var id)) return id;
        if (httpContext is not null) throw new UnauthorizedAccessException("An authenticated company context is required.");
        return await db.Companies.Select(x => x.Id).FirstAsync(ct);
    }
    private bool HasPermission(string permission)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal is null
            || (!principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")
                && (principal.IsInRole("Administrator") || principal.IsInRole("Owner/CEO") || principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
    }
    private Guid? ResolveUserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
