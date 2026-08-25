using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.Taxation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class AccountingTransactionService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : IAccountingTransactionService
{
    public async Task<TransactionResult> SaveJournalEntryDraftAsync(SaveJournalEntryDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalPrepare)) return TransactionResult.Failure("You are not authorized to prepare journal entries.");
        if (string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Description))
            return TransactionResult.Failure("A journal reference and description are required.");
        if (request.Lines.Count < 2 || request.Lines.Any(line => line.Debit < 0 || line.Credit < 0 || (line.Debit == 0 && line.Credit == 0) || (line.Debit > 0 && line.Credit > 0)))
            return TransactionResult.Failure("Journal drafts require at least two valid debit or credit lines.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var accountNumbers = request.Lines.Select(line => line.AccountNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var accounts = await db.Accounts.Where(account => account.CompanyId == companyId && account.IsActive && accountNumbers.Contains(account.Number)).ToListAsync(cancellationToken);
        if (accounts.Count != accountNumbers.Length) return TransactionResult.Failure("One or more active posting accounts could not be found.");
        if (accounts.Any(account => account.IsControlAccount)) return TransactionResult.Failure("General journal drafts cannot use control accounts; use the related subledger workflow.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var userId = ResolveUserId();
        JournalEntry entry;
        if (request.Id.HasValue)
        {
            var existing = await db.JournalEntries.SingleOrDefaultAsync(candidate => candidate.Id == request.Id && candidate.CompanyId == companyId, cancellationToken);
            if (existing is null) return TransactionResult.Failure("Journal draft not found.");
            entry = existing;
            if (!entry.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) return TransactionResult.Failure("Only journal drafts can be edited.");
            db.JournalEntryLines.RemoveRange(await db.JournalEntryLines.Where(line => line.JournalEntryId == entry.Id).ToListAsync(cancellationToken));
        }
        else
        {
            entry = new JournalEntry
            {
                Id = Guid.NewGuid(), CompanyId = companyId, EntryNumber = $"DRAFT-{Guid.NewGuid():N}"[..20],
                SourceModule = "General Ledger", Status = "Draft", CreatedByUserId = userId, CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.JournalEntries.Add(entry);
        }

        entry.PostedOn = request.EntryDate;
        entry.Reference = request.Reference.Trim();
        entry.Description = request.Description.Trim();
        entry.TotalAmount = request.Lines.Sum(line => line.Debit);
        entry.IsPosted = false;
        entry.ConcurrencyToken = Guid.NewGuid().ToString("N");
        foreach (var line in request.Lines)
        {
            var account = accounts.Single(candidate => candidate.Number.Equals(line.AccountNumber.Trim(), StringComparison.OrdinalIgnoreCase));
            db.JournalEntryLines.Add(new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = entry.Id, AccountId = account.Id, Description = line.Description.Trim(), Debit = line.Debit, Credit = line.Credit });
        }
        AddJournalAudit(db, companyId, userId, "journal.draft.saved", entry, new { lineCount = request.Lines.Count });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The journal draft changed while it was being saved. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(entry.Id);
    }

    public async Task<TransactionResult> ApproveJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalApprove)) return TransactionResult.Failure("You are not authorized to approve journal entries.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var entry = await db.JournalEntries.SingleOrDefaultAsync(candidate => candidate.Id == journalEntryId && candidate.CompanyId == companyId, cancellationToken);
        if (entry is null) return TransactionResult.Failure("Journal draft not found.");
        if (!entry.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) return TransactionResult.Failure("Only a journal draft can be approved.");
        if (await IsClosedPeriodAsync(db, companyId, entry.PostedOn, cancellationToken)) return TransactionResult.Failure("This journal date is in a closed accounting period.");
        var lines = await db.JournalEntryLines.Where(line => line.JournalEntryId == entry.Id).ToListAsync(cancellationToken);
        if (lines.Count < 2 || lines.Sum(line => line.Debit) != lines.Sum(line => line.Credit)) return TransactionResult.Failure("The journal draft must balance before approval.");

        var userId = ResolveUserId();
        entry.Status = "Approved";
        entry.ApprovedByUserId = userId;
        entry.ApprovedAtUtc = DateTimeOffset.UtcNow;
        entry.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddJournalAudit(db, companyId, userId, "journal.approved", entry, new { entry.ApprovedAtUtc });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The journal draft changed during approval. Refresh and try again."); }
        return TransactionResult.Success(entry.Id);
    }

    public async Task<TransactionResult> PostApprovedJournalEntryAsync(Guid journalEntryId, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalPost)) return TransactionResult.Failure("You are not authorized to post journal entries.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entry = await db.JournalEntries.SingleOrDefaultAsync(candidate => candidate.Id == journalEntryId && candidate.CompanyId == companyId, cancellationToken);
        if (entry is null) return TransactionResult.Failure("Approved journal entry not found.");
        if (!entry.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase) || entry.IsPosted) return TransactionResult.Failure("Only an approved, unposted journal entry can be posted.");
        if (await IsClosedPeriodAsync(db, companyId, entry.PostedOn, cancellationToken)) return TransactionResult.Failure("This posting date is in a closed accounting period.");
        var lines = await db.JournalEntryLines.Where(line => line.JournalEntryId == entry.Id).ToListAsync(cancellationToken);
        if (lines.Count < 2 || lines.Sum(line => line.Debit) != lines.Sum(line => line.Credit)) return TransactionResult.Failure("The approved journal entry is no longer balanced.");
        var accountIds = lines.Select(line => line.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.Where(account => account.CompanyId == companyId && account.IsActive && accountIds.Contains(account.Id)).ToListAsync(cancellationToken);
        if (accounts.Count != accountIds.Length || accounts.Any(account => account.IsControlAccount)) return TransactionResult.Failure("The approved journal contains an unavailable or control account.");

        foreach (var line in lines)
        {
            var account = accounts.Single(candidate => candidate.Id == line.AccountId);
            account.CurrentBalance += account.Type is AccountType.Asset or AccountType.Expense ? line.Debit - line.Credit : line.Credit - line.Debit;
        }
        var userId = ResolveUserId();
        entry.EntryNumber = $"JE-{entry.PostedOn:yyyyMMdd}-{Guid.NewGuid():N}"[..20];
        entry.Status = "Posted";
        entry.IsPosted = true;
        entry.PostedByUserId = userId;
        entry.PostedAtUtc = DateTimeOffset.UtcNow;
        entry.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddJournalAudit(db, companyId, userId, "journal.posted", entry, new { entry.ApprovedByUserId, entry.ApprovedAtUtc });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The approved journal changed while it was posting. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(entry.Id);
    }

    public async Task<TransactionResult> ReverseJournalEntryAsync(ReverseJournalEntryRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalReverse)) return TransactionResult.Failure("You are not authorized to reverse journal entries.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var original = await db.JournalEntries.SingleOrDefaultAsync(entry => entry.Id == request.JournalEntryId && entry.CompanyId == companyId, cancellationToken);
        if (original is null) return TransactionResult.Failure("Journal entry not found.");
        if (!original.IsPosted || !original.Status.Equals("Posted", StringComparison.OrdinalIgnoreCase) || original.ReversedByJournalEntryId.HasValue) return TransactionResult.Failure("Only an unreversed posted journal entry can be reversed.");
        if (!original.SourceModule.Equals("General Ledger", StringComparison.OrdinalIgnoreCase) || original.SourceDocumentId.HasValue) return TransactionResult.Failure("Reverse subledger transactions through their originating workflow.");
        if (request.ReversalDate < original.PostedOn) return TransactionResult.Failure("A reversal cannot precede the original posting date.");
        if (await db.BankReconciliationItems.AnyAsync(item => item.JournalEntryId == original.Id, cancellationToken)) return TransactionResult.Failure("A reconciled journal entry cannot be reversed until the reconciliation is reopened.");
        var originalLines = await db.JournalEntryLines.Where(line => line.JournalEntryId == original.Id).ToListAsync(cancellationToken);
        var accountIds = originalLines.Select(line => line.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.Where(account => account.CompanyId == companyId && accountIds.Contains(account.Id)).ToDictionaryAsync(account => account.Id, cancellationToken);
        var reversingLines = originalLines.Select(line => new JournalLineRequest(accounts[line.AccountId].Number, line.Credit, line.Debit, $"Reversal: {line.Description}")).ToArray();
        var posting = await PostAsync(db, companyId, request.ReversalDate, "General Ledger", $"REV-{original.Reference}", request.Reason, reversingLines, cancellationToken, sourceDocumentId: original.Id, sourceDocumentType: "JournalEntryReversal");
        if (!posting.Succeeded) return posting;
        var reversal = await db.JournalEntries.SingleAsync(entry => entry.Id == posting.Id, cancellationToken);
        reversal.ReversalOfJournalEntryId = original.Id;
        reversal.ConcurrencyToken = Guid.NewGuid().ToString("N");
        original.Status = "Reversed";
        original.ReversedByJournalEntryId = reversal.Id;
        original.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddJournalAudit(db, companyId, ResolveUserId(), "journal.reversed", original, new { reversalEntryId = reversal.Id, request.ReversalDate, reason = request.Reason.Trim() });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The journal entry changed while it was being reversed. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return posting;
    }

    public async Task<TransactionResult> PostJournalEntryAsync(PostJournalEntryRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalPost)) return TransactionResult.Failure("You are not authorized to post journal entries.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await PostAsync(db, companyId, request.PostedOn, "General Ledger", request.Reference, request.Description, request.Lines, cancellationToken);
        if (!result.Succeeded) return result;
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<TransactionResult> PostJournalEntriesAsync(IReadOnlyList<PostJournalEntryRequest> requests, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalPost)) return TransactionResult.Failure("You are not authorized to post journal entries.");
        if (requests.Count == 0) return TransactionResult.Failure("Provide at least one journal entry to import.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        TransactionResult? lastResult = null;
        foreach (var request in requests)
        {
            lastResult = await PostAsync(db, companyId, request.PostedOn, "General Ledger", request.Reference, request.Description, request.Lines, cancellationToken);
            if (!lastResult.Succeeded) return lastResult;
        }
        await transaction.CommitAsync(cancellationToken);
        return lastResult!;
    }

    public async Task<TransactionResult> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReceivablesManage)) return TransactionResult.Failure("You are not authorized to post invoices.");
        var requestedLines = request.Lines?.ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(request.InvoiceNumber) || string.IsNullOrWhiteSpace(request.Description)) return TransactionResult.Failure("An invoice number and description are required.");
        if (request.DueDate < request.InvoiceDate) return TransactionResult.Failure("The invoice due date cannot precede the invoice date.");
        if (requestedLines.Length > 0 && requestedLines.Any(line => string.IsNullOrWhiteSpace(line.Description) || line.Quantity <= 0 || line.UnitPrice < 0 || line.DiscountAmount < 0 || line.DiscountAmount > line.Quantity * line.UnitPrice || line.TaxAmount < 0 || string.IsNullOrWhiteSpace(line.RevenueAccountNumber)))
            return TransactionResult.Failure("Each invoice line requires a description, positive quantity, valid price and discount, non-negative tax, and revenue account.");
        if (requestedLines.Length == 0 && (request.Subtotal < 0 || request.TaxAmount < 0)) return TransactionResult.Failure("Invoice amounts must be non-negative.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == request.CustomerId && x.CompanyId == companyId, cancellationToken);
        if (customer is null) return TransactionResult.Failure("Customer not found.");
        if (await db.SalesInvoices.AnyAsync(x => x.CompanyId == companyId && x.InvoiceNumber == request.InvoiceNumber.Trim(), cancellationToken)) return TransactionResult.Failure("Invoice number already exists.");
        var revenueNumbers = (requestedLines.Length == 0 ? [request.RevenueAccountNumber] : requestedLines.Select(line => line.RevenueAccountNumber)).Select(number => number?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (revenueNumbers.Any(string.IsNullOrWhiteSpace)) return TransactionResult.Failure("Every invoice line requires a revenue account.");
        var validRevenueAccountCount = await db.Accounts.CountAsync(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Revenue && !account.IsControlAccount && revenueNumbers.Contains(account.Number), cancellationToken);
        if (validRevenueAccountCount != revenueNumbers.Length) return TransactionResult.Failure("Every invoice distribution must use an active, non-control revenue account.");
        var lineAmounts = requestedLines.Select((line, index) => new
        {
            Request = line,
            Sequence = index + 1,
            NetAmount = RoundCurrency(line.Quantity * line.UnitPrice - line.DiscountAmount),
            TaxAmount = RoundCurrency(line.TaxAmount)
        }).ToArray();
        var subtotal = requestedLines.Length == 0 ? RoundCurrency(request.Subtotal) : lineAmounts.Sum(line => line.NetAmount);
        var taxAmount = requestedLines.Length == 0 ? RoundCurrency(request.TaxAmount) : lineAmounts.Sum(line => line.TaxAmount);
        var total = subtotal + taxAmount;
        if (total <= 0) return TransactionResult.Failure("Invoice total must be greater than zero.");
        if (customer.CreditLimit > 0 && customer.OpenBalance + total > customer.CreditLimit)
            return TransactionResult.Failure("Posting this invoice would exceed the customer's credit limit.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lines = new List<JournalLineRequest>
        {
            new("1100", total, 0, "Invoice receivable")
        };
        if (requestedLines.Length == 0) lines.Add(new JournalLineRequest(request.RevenueAccountNumber, 0, subtotal, "Invoice revenue"));
        else lines.AddRange(lineAmounts.GroupBy(line => line.Request.RevenueAccountNumber.Trim(), StringComparer.OrdinalIgnoreCase).Select(group => new JournalLineRequest(group.Key, 0, group.Sum(line => line.NetAmount), "Invoice revenue")));
        if (taxAmount > 0) lines.Add(new JournalLineRequest("2100", 0, taxAmount, "Sales tax payable"));
        var invoiceId = Guid.NewGuid();
        var posting = await PostAsync(db, companyId, request.InvoiceDate, "Accounts Receivable", request.InvoiceNumber, request.Description, lines, cancellationToken, allowControlAccounts: true, sourceDocumentId: invoiceId, sourceDocumentType: "SalesInvoice");
        if (!posting.Succeeded) return posting;
        var invoice = new SalesInvoice { Id = invoiceId, CompanyId = companyId, CustomerId = request.CustomerId, InvoiceNumber = request.InvoiceNumber.Trim(), InvoiceDate = request.InvoiceDate, DueDate = request.DueDate, Status = "Open", Subtotal = subtotal, TaxAmount = taxAmount, TotalAmount = total, BalanceDue = total, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.SalesInvoices.Add(invoice);
        if (lineAmounts.Length > 0)
        {
            var revenueAccounts = await db.Accounts.Where(account => account.CompanyId == companyId && revenueNumbers.Contains(account.Number)).ToDictionaryAsync(account => account.Number, StringComparer.OrdinalIgnoreCase, cancellationToken);
            db.SalesInvoiceLines.AddRange(lineAmounts.Select(line => new SalesInvoiceLine { Id = Guid.NewGuid(), SalesInvoiceId = invoice.Id, Sequence = line.Sequence, RevenueAccountId = revenueAccounts[line.Request.RevenueAccountNumber.Trim()].Id, Description = line.Request.Description.Trim(), Quantity = line.Request.Quantity, UnitPrice = line.Request.UnitPrice, DiscountAmount = line.Request.DiscountAmount, TaxAmount = line.TaxAmount, LineTotal = line.NetAmount + line.TaxAmount }));
        }
        customer.OpenBalance += total;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return TransactionResult.Failure("Invoice number already exists or was posted concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(invoice.Id);
    }

    public async Task<TransactionResult> CreateVendorBillAsync(CreateVendorBillRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage)) return TransactionResult.Failure("You are not authorized to post vendor bills.");
        var requestedLines = request.Lines?.ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(request.BillNumber) || string.IsNullOrWhiteSpace(request.Description)) return TransactionResult.Failure("A bill number and description are required.");
        if (request.DueDate < request.BillDate) return TransactionResult.Failure("The bill due date cannot precede the bill date.");
        if (requestedLines.Length > 0 && requestedLines.Any(line => string.IsNullOrWhiteSpace(line.Description) || line.Quantity <= 0 || line.UnitCost < 0 || line.DiscountAmount < 0 || line.DiscountAmount > line.Quantity * line.UnitCost || line.TaxAmount < 0 || string.IsNullOrWhiteSpace(line.ExpenseAccountNumber)))
            return TransactionResult.Failure("Each bill line requires a description, positive quantity, valid cost and discount, non-negative tax, and expense account.");
        if (requestedLines.Length == 0 && request.TotalAmount <= 0) return TransactionResult.Failure("Bill amount must be positive.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.Vendors.AnyAsync(x => x.Id == request.VendorId && x.CompanyId == companyId, cancellationToken)) return TransactionResult.Failure("Vendor not found.");
        if (await db.VendorBills.AnyAsync(x => x.CompanyId == companyId && x.BillNumber == request.BillNumber.Trim(), cancellationToken)) return TransactionResult.Failure("Bill number already exists.");
        var expenseNumbers = (requestedLines.Length == 0 ? [request.ExpenseAccountNumber] : requestedLines.Select(line => line.ExpenseAccountNumber)).Select(number => number?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (expenseNumbers.Any(string.IsNullOrWhiteSpace)) return TransactionResult.Failure("Every bill line requires an expense account.");
        var validExpenseAccountCount = await db.Accounts.CountAsync(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Expense && !account.IsControlAccount && expenseNumbers.Contains(account.Number), cancellationToken);
        if (validExpenseAccountCount != expenseNumbers.Length) return TransactionResult.Failure("Every bill distribution must use an active, non-control expense account.");
        var lineAmounts = requestedLines.Select((line, index) => new
        {
            Request = line,
            Sequence = index + 1,
            NetAmount = RoundCurrency(line.Quantity * line.UnitCost - line.DiscountAmount),
            TaxAmount = RoundCurrency(line.TaxAmount)
        }).ToArray();
        var total = requestedLines.Length == 0 ? RoundCurrency(request.TotalAmount) : lineAmounts.Sum(line => line.NetAmount + line.TaxAmount);
        if (total <= 0) return TransactionResult.Failure("Bill total must be positive.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var billId = Guid.NewGuid();
        IReadOnlyList<JournalLineRequest> postingLines = requestedLines.Length == 0
            ? [new(request.ExpenseAccountNumber, total, 0, "Bill expense"), new("2000", 0, total, "Accounts payable")]
            : lineAmounts.GroupBy(line => line.Request.ExpenseAccountNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new JournalLineRequest(group.Key, group.Sum(line => line.NetAmount + line.TaxAmount), 0, "Bill expense and tax"))
                .Append(new JournalLineRequest("2000", 0, total, "Accounts payable")).ToArray();
        var posting = await PostAsync(db, companyId, request.BillDate, "Accounts Payable", request.BillNumber, request.Description,
            postingLines, cancellationToken, allowControlAccounts: true, sourceDocumentId: billId, sourceDocumentType: "VendorBill");
        if (!posting.Succeeded) return posting;
        var bill = new VendorBill { Id = billId, CompanyId = companyId, VendorId = request.VendorId, BillNumber = request.BillNumber.Trim(), BillDate = request.BillDate, DueDate = request.DueDate, Status = "Open", TotalAmount = total, BalanceDue = total, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.VendorBills.Add(bill);
        if (lineAmounts.Length > 0)
        {
            var expenseAccounts = await db.Accounts.Where(account => account.CompanyId == companyId && expenseNumbers.Contains(account.Number)).ToDictionaryAsync(account => account.Number, StringComparer.OrdinalIgnoreCase, cancellationToken);
            db.VendorBillLines.AddRange(lineAmounts.Select(line => new VendorBillLine { Id = Guid.NewGuid(), VendorBillId = bill.Id, Sequence = line.Sequence, ExpenseAccountId = expenseAccounts[line.Request.ExpenseAccountNumber.Trim()].Id, Description = line.Request.Description.Trim(), Quantity = line.Request.Quantity, UnitCost = line.Request.UnitCost, DiscountAmount = line.Request.DiscountAmount, TaxAmount = line.TaxAmount, LineTotal = line.NetAmount + line.TaxAmount }));
        }
        var vendor = await db.Vendors.SingleAsync(x => x.Id == request.VendorId, cancellationToken);
        vendor.OpenBalance += total;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return TransactionResult.Failure("Bill number already exists or was posted concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(bill.Id);
    }

    public Task<TransactionResult> SaveInvoiceDraftAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default) =>
        SaveSubledgerWorkflowAsync("Invoice", request.InvoiceNumber, request, false, string.Empty, 1, null, null, cancellationToken);

    public Task<TransactionResult> SaveVendorBillDraftAsync(CreateVendorBillRequest request, CancellationToken cancellationToken = default) =>
        SaveSubledgerWorkflowAsync("VendorBill", request.BillNumber, request, false, string.Empty, 1, null, null, cancellationToken);

    public async Task<TransactionResult> ApproveSubledgerDocumentAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SubledgerApprove)) return TransactionResult.Failure("You are not authorized to approve invoice or bill drafts.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var workflow = await db.SubledgerDocumentWorkflows.SingleOrDefaultAsync(item => item.Id == workflowId && item.CompanyId == companyId, cancellationToken);
        if (workflow is null || workflow.IsRecurringTemplate || workflow.Status != "Draft") return TransactionResult.Failure("Only an invoice or bill draft can be approved.");
        var modulePermission = workflow.DocumentType == "Invoice" ? BrassLedgerPermissions.ReceivablesManage : BrassLedgerPermissions.PayablesManage;
        if (!HasPermission(modulePermission)) return TransactionResult.Failure("You are not authorized for this subledger.");
        workflow.Status = "Approved"; workflow.ApprovedByUserId = ResolveUserId(); workflow.ApprovedAtUtc = DateTimeOffset.UtcNow; workflow.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWorkflowAudit(db, workflow, "subledger-document.approved");
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The draft changed during approval. Refresh and try again."); }
        return TransactionResult.Success(workflow.Id);
    }

    public async Task<TransactionResult> PostApprovedSubledgerDocumentAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SubledgerPost)) return TransactionResult.Failure("You are not authorized to post approved invoice or bill drafts.");
        string documentType; string payload;
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
            var workflow = await db.SubledgerDocumentWorkflows.AsNoTracking().SingleOrDefaultAsync(item => item.Id == workflowId && item.CompanyId == companyId, cancellationToken);
            if (workflow is null || workflow.IsRecurringTemplate || workflow.Status != "Approved") return TransactionResult.Failure("Only an approved invoice or bill draft can be posted.");
            documentType = workflow.DocumentType; payload = workflow.PayloadJson;
        }
        var modulePermission = documentType == "Invoice" ? BrassLedgerPermissions.ReceivablesManage : BrassLedgerPermissions.PayablesManage;
        if (!HasPermission(modulePermission)) return TransactionResult.Failure("You are not authorized for this subledger.");
        TransactionResult posting;
        try
        {
            posting = documentType == "Invoice"
                ? await CreateInvoiceAsync(System.Text.Json.JsonSerializer.Deserialize<CreateInvoiceRequest>(payload)!, cancellationToken)
                : await CreateVendorBillAsync(System.Text.Json.JsonSerializer.Deserialize<CreateVendorBillRequest>(payload)!, cancellationToken);
        }
        catch (System.Text.Json.JsonException) { return TransactionResult.Failure("The approved draft payload is invalid."); }
        if (!posting.Succeeded) return posting;
        await using var updateDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var updateCompanyId = await ResolveCompanyIdAsync(updateDb, cancellationToken);
        var tracked = await updateDb.SubledgerDocumentWorkflows.SingleAsync(item => item.Id == workflowId && item.CompanyId == updateCompanyId, cancellationToken);
        if (tracked.Status != "Approved") return TransactionResult.Failure("The draft changed while it was posting; the source document was posted and requires administrative review.");
        tracked.Status = "Posted"; tracked.PostedDocumentId = posting.Id; tracked.PostedByUserId = ResolveUserId(); tracked.PostedAtUtc = DateTimeOffset.UtcNow; tracked.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddWorkflowAudit(updateDb, tracked, "subledger-document.posted");
        await updateDb.SaveChangesAsync(cancellationToken);
        return TransactionResult.Success(posting.Id!.Value);
    }

    public Task<TransactionResult> SaveRecurringInvoiceTemplateAsync(SaveRecurringInvoiceTemplateRequest request, CancellationToken cancellationToken = default) =>
        SaveSubledgerWorkflowAsync("Invoice", request.Invoice.InvoiceNumber, request.Invoice, true, request.Frequency, request.FrequencyInterval, request.NextOccurrenceDate, request.EndDate, cancellationToken);

    public Task<TransactionResult> SaveRecurringVendorBillTemplateAsync(SaveRecurringVendorBillTemplateRequest request, CancellationToken cancellationToken = default) =>
        SaveSubledgerWorkflowAsync("VendorBill", request.Bill.BillNumber, request.Bill, true, request.Frequency, request.FrequencyInterval, request.NextOccurrenceDate, request.EndDate, cancellationToken);

    public async Task<TransactionResult> GenerateDueRecurringDocumentsAsync(DateOnly throughDate, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SubledgerPrepare)) return TransactionResult.Failure("You are not authorized to generate recurring document drafts.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var templates = await db.SubledgerDocumentWorkflows.Where(item => item.CompanyId == companyId && item.IsRecurringTemplate && item.Status == "Active" && item.NextOccurrenceDate <= throughDate && (item.EndDate == null || item.NextOccurrenceDate <= item.EndDate)).ToListAsync(cancellationToken);
        var generated = 0;
        foreach (var template in templates)
        {
            var occurrence = template.NextOccurrenceDate!.Value;
            while (occurrence <= throughDate && (!template.EndDate.HasValue || occurrence <= template.EndDate.Value))
            {
                var number = $"{template.DocumentNumber}-{occurrence:yyyyMMdd}";
                string payload;
                if (template.DocumentType == "Invoice")
                {
                    var source = System.Text.Json.JsonSerializer.Deserialize<CreateInvoiceRequest>(template.PayloadJson)!;
                    payload = System.Text.Json.JsonSerializer.Serialize(source with { InvoiceNumber = number, InvoiceDate = occurrence, DueDate = occurrence.AddDays(source.DueDate.DayNumber - source.InvoiceDate.DayNumber) });
                }
                else
                {
                    var source = System.Text.Json.JsonSerializer.Deserialize<CreateVendorBillRequest>(template.PayloadJson)!;
                    payload = System.Text.Json.JsonSerializer.Serialize(source with { BillNumber = number, BillDate = occurrence, DueDate = occurrence.AddDays(source.DueDate.DayNumber - source.BillDate.DayNumber) });
                }
                if (!await db.SubledgerDocumentWorkflows.AnyAsync(item => item.CompanyId == companyId && item.DocumentType == template.DocumentType && item.DocumentNumber == number && !item.IsRecurringTemplate, cancellationToken))
                {
                    var draft = new SubledgerDocumentWorkflow { Id = Guid.NewGuid(), CompanyId = companyId, DocumentType = template.DocumentType, DocumentNumber = number, PayloadJson = payload, Status = "Draft", SourceTemplateId = template.Id, CreatedByUserId = ResolveUserId(), CreatedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
                    db.SubledgerDocumentWorkflows.Add(draft); AddWorkflowAudit(db, draft, "subledger-document.generated"); generated++;
                }
                occurrence = AdvanceOccurrence(occurrence, template.Frequency, template.FrequencyInterval);
            }
            template.NextOccurrenceDate = occurrence; template.ConcurrencyToken = Guid.NewGuid().ToString("N");
            if (template.EndDate.HasValue && occurrence > template.EndDate.Value) template.Status = "Completed";
        }
        await db.SaveChangesAsync(cancellationToken);
        return generated == 0 ? TransactionResult.Failure("No recurring templates were due through that date.") : TransactionResult.Success(templates.First().Id);
    }

    public async Task<TransactionResult> ApplyInvoicePaymentAsync(ApplyInvoicePaymentRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var customerId = await db.SalesInvoices.Where(invoice => invoice.Id == request.InvoiceId && invoice.CompanyId == companyId).Select(invoice => (Guid?)invoice.CustomerId).SingleOrDefaultAsync(cancellationToken);
        return customerId is null
            ? TransactionResult.Failure("Invoice not found.")
            : await RecordCustomerPaymentAsync(new RecordCustomerPaymentRequest(customerId.Value, request.BankAccountId, request.PaymentDate, request.Amount, request.Reference, "Other", [new PaymentDocumentApplicationRequest(request.InvoiceId, request.Amount)]), cancellationToken);
    }

    public async Task<TransactionResult> ApplyBillPaymentAsync(ApplyBillPaymentRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var vendorId = await db.VendorBills.Where(bill => bill.Id == request.VendorBillId && bill.CompanyId == companyId).Select(bill => (Guid?)bill.VendorId).SingleOrDefaultAsync(cancellationToken);
        return vendorId is null
            ? TransactionResult.Failure("Vendor bill not found.")
            : await RecordVendorPaymentAsync(new RecordVendorPaymentRequest(vendorId.Value, request.BankAccountId, request.PaymentDate, request.Amount, request.Reference, "Other", [new PaymentDocumentApplicationRequest(request.VendorBillId, request.Amount)]), cancellationToken);
    }

    public Task<TransactionResult> RecordCustomerPaymentAsync(RecordCustomerPaymentRequest request, CancellationToken cancellationToken = default) =>
        !HasPermission(BrassLedgerPermissions.ReceivablesManage)
            ? Task.FromResult(TransactionResult.Failure("You are not authorized to record customer payments."))
            : RecordSubledgerPaymentAsync("CustomerReceipt", request.CustomerId, request.BankAccountId, request.PaymentDate, request.Amount, request.Reference, request.Method, request.Applications, cancellationToken);

    public Task<TransactionResult> RecordVendorPaymentAsync(RecordVendorPaymentRequest request, CancellationToken cancellationToken = default) =>
        !HasPermission(BrassLedgerPermissions.PayablesManage)
            ? Task.FromResult(TransactionResult.Failure("You are not authorized to record vendor payments."))
            : RecordSubledgerPaymentAsync("VendorDisbursement", request.VendorId, request.BankAccountId, request.PaymentDate, request.Amount, request.Reference, request.Method, request.Applications, cancellationToken);

    public async Task<TransactionResult> ReverseSubledgerPaymentAsync(ReverseSubledgerPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PaymentReverse)) return TransactionResult.Failure("You are not authorized to reverse payments.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A payment reversal reason is required.");
        var reversalKind = (request.ReversalKind ?? string.Empty).Trim();
        if (reversalKind is not ("Reversed" or "Returned" or "Voided")) return TransactionResult.Failure("Payment reversal kind must be Reversed, Returned, or Voided.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var payment = await db.SubledgerPayments.SingleOrDefaultAsync(item => item.Id == request.PaymentId && item.CompanyId == companyId, cancellationToken);
        if (payment is null) return TransactionResult.Failure("Payment not found.");
        if (payment.Status != "Posted") return TransactionResult.Failure("Only a posted payment can be reversed, returned, or voided.");
        if (await db.SubledgerAdjustments.AnyAsync(adjustment => adjustment.PaymentId == payment.Id && adjustment.Status == "Posted", cancellationToken)) return TransactionResult.Failure("Reverse active refunds or other payment adjustments before reversing the original payment.");
        if (request.ReversalDate < payment.PaymentDate) return TransactionResult.Failure("A payment reversal cannot precede the payment date.");
        if (await db.BankReconciliationItems.AnyAsync(item => item.JournalEntryId == payment.JournalEntryId, cancellationToken)) return TransactionResult.Failure("A reconciled payment cannot be reversed until its bank reconciliation is reopened.");
        var bank = await db.BankAccounts.SingleAsync(account => account.Id == payment.BankAccountId && account.CompanyId == companyId, cancellationToken);
        var cashAccountNumber = await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken);
        if (string.IsNullOrWhiteSpace(cashAccountNumber)) return TransactionResult.Failure("The payment bank account is not mapped to an active ledger account.");
        var applications = await db.SubledgerPaymentApplications.Where(item => item.SubledgerPaymentId == payment.Id).ToListAsync(cancellationToken);
        IReadOnlyList<JournalLineRequest> lines;
        if (payment.Direction == "CustomerReceipt")
        {
            var invoices = await db.SalesInvoices.Where(invoice => invoice.CompanyId == companyId && applications.Select(item => item.DocumentId).Contains(invoice.Id)).ToDictionaryAsync(invoice => invoice.Id, cancellationToken);
            if (invoices.Count != applications.Count) return TransactionResult.Failure("One or more original invoice applications are unavailable.");
            foreach (var application in applications)
            {
                var invoice = invoices[application.DocumentId];
                invoice.BalanceDue += application.Amount;
                invoice.Status = invoice.BalanceDue == invoice.TotalAmount ? "Open" : "Partial";
                invoice.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
            var customer = await db.Customers.SingleAsync(item => item.Id == payment.CounterpartyId && item.CompanyId == companyId, cancellationToken);
            customer.OpenBalance += payment.AppliedAmount;
            var reversalLines = new List<JournalLineRequest> { new(cashAccountNumber, 0, payment.Amount, $"{reversalKind} customer receipt") };
            if (payment.AppliedAmount > 0) reversalLines.Add(new("1100", payment.AppliedAmount, 0, "Restore invoice balances"));
            if (payment.UnappliedAmount > 0) reversalLines.Add(new("2150", payment.UnappliedAmount, 0, "Remove customer deposit"));
            lines = reversalLines;
            bank.CurrentBalance -= payment.Amount;
        }
        else if (payment.Direction == "VendorDisbursement")
        {
            var bills = await db.VendorBills.Where(bill => bill.CompanyId == companyId && applications.Select(item => item.DocumentId).Contains(bill.Id)).ToDictionaryAsync(bill => bill.Id, cancellationToken);
            if (bills.Count != applications.Count) return TransactionResult.Failure("One or more original bill applications are unavailable.");
            foreach (var application in applications)
            {
                var bill = bills[application.DocumentId];
                bill.BalanceDue += application.Amount;
                bill.Status = bill.BalanceDue == bill.TotalAmount ? "Open" : "Partial";
                bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
            var vendor = await db.Vendors.SingleAsync(item => item.Id == payment.CounterpartyId && item.CompanyId == companyId, cancellationToken);
            vendor.OpenBalance += payment.AppliedAmount;
            var reversalLines = new List<JournalLineRequest> { new(cashAccountNumber, payment.Amount, 0, $"{reversalKind} vendor disbursement") };
            if (payment.AppliedAmount > 0) reversalLines.Add(new("2000", 0, payment.AppliedAmount, "Restore bill balances"));
            if (payment.UnappliedAmount > 0) reversalLines.Add(new("1300", 0, payment.UnappliedAmount, "Remove vendor advance"));
            lines = reversalLines;
            bank.CurrentBalance += payment.Amount;
        }
        else return TransactionResult.Failure("The payment direction is not supported.");

        var posting = await PostAsync(db, companyId, request.ReversalDate, payment.Direction == "CustomerReceipt" ? "Accounts Receivable" : "Accounts Payable", $"REV-{payment.Reference}", $"{reversalKind} payment: {request.Reason.Trim()}", lines, cancellationToken, bank.Id, allowControlAccounts: true, sourceDocumentId: payment.Id, sourceDocumentType: "SubledgerPaymentReversal");
        if (!posting.Succeeded) return posting;
        var originalJournal = await db.JournalEntries.SingleAsync(entry => entry.Id == payment.JournalEntryId && entry.CompanyId == companyId, cancellationToken);
        var reversalJournal = await db.JournalEntries.SingleAsync(entry => entry.Id == posting.Id && entry.CompanyId == companyId, cancellationToken);
        originalJournal.Status = "Reversed"; originalJournal.ReversedByJournalEntryId = reversalJournal.Id; originalJournal.ConcurrencyToken = Guid.NewGuid().ToString("N");
        reversalJournal.ReversalOfJournalEntryId = originalJournal.Id; reversalJournal.ConcurrencyToken = Guid.NewGuid().ToString("N");
        payment.Status = reversalKind;
        payment.ReversalJournalEntryId = posting.Id;
        payment.ReversedByUserId = ResolveUserId();
        payment.ReversedAtUtc = DateTimeOffset.UtcNow;
        payment.ReversalReason = request.Reason.Trim();
        payment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        bank.UnreconciledAmount += payment.Amount;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPaymentAudit(db, companyId, payment, "payment.reversed", new { reversalKind, request.ReversalDate, payment.ReversalJournalEntryId, payment.ReversalReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payment changed during reversal. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(payment.Id);
    }

    public async Task<TransactionResult> RecordCustomerAdjustmentAsync(RecordCustomerAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReceivablesManage)) return TransactionResult.Failure("You are not authorized to adjust customer balances.");
        var kind = (request.Kind ?? string.Empty).Trim();
        if (kind is not ("CreditMemo" or "WriteOff")) return TransactionResult.Failure("Customer adjustment kind must be CreditMemo or WriteOff.");
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A positive amount, reference, and reason are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var amount = RoundCurrency(request.Amount);
        var invoice = await db.SalesInvoices.SingleOrDefaultAsync(item => item.Id == request.InvoiceId && item.CompanyId == companyId, cancellationToken);
        if (invoice is null) return TransactionResult.Failure("Invoice not found.");
        if (invoice.Status == "Voided" || amount > invoice.BalanceDue) return TransactionResult.Failure("The adjustment cannot exceed the open invoice balance or target a voided invoice.");
        if (request.AdjustmentDate < invoice.InvoiceDate) return TransactionResult.Failure("The adjustment date cannot precede the invoice date.");
        var offset = await db.Accounts.SingleOrDefaultAsync(account => account.CompanyId == companyId && account.Number == request.OffsetAccountNumber.Trim() && account.IsActive && !account.IsControlAccount, cancellationToken);
        if (offset is null || (kind == "WriteOff" ? offset.Type != AccountType.Expense : offset.Type is not (AccountType.Revenue or AccountType.Expense))) return TransactionResult.Failure(kind == "WriteOff" ? "A write-off requires an active expense account." : "A credit memo requires an active revenue or expense account.");
        if (await db.SubledgerAdjustments.AnyAsync(item => item.CompanyId == companyId && item.Subledger == "Receivables" && item.Reference == request.Reference.Trim(), cancellationToken)) return TransactionResult.Failure("That receivables adjustment reference already exists.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var adjustmentId = Guid.NewGuid();
        var posting = await PostAsync(db, companyId, request.AdjustmentDate, "Accounts Receivable", request.Reference, request.Reason,
            [new(offset.Number, amount, 0, kind == "WriteOff" ? "Bad debt write-off" : "Customer credit"), new("1100", 0, amount, "Reduce receivable")], cancellationToken,
            allowControlAccounts: true, sourceDocumentId: adjustmentId, sourceDocumentType: "SubledgerAdjustment");
        if (!posting.Succeeded) return posting;
        invoice.BalanceDue -= amount;
        invoice.Status = invoice.BalanceDue == 0 ? "Paid" : "Partial";
        invoice.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var customer = await db.Customers.SingleAsync(item => item.Id == invoice.CustomerId && item.CompanyId == companyId, cancellationToken);
        customer.OpenBalance -= amount;
        var adjustment = CreateAdjustment(adjustmentId, companyId, "Receivables", kind, invoice.CustomerId, request.InvoiceId, null, null, request.AdjustmentDate, amount, request.Reference, request.Reason, offset.Number, posting.Id!.Value);
        db.SubledgerAdjustments.Add(adjustment);
        AddAdjustmentAudit(db, adjustment, "subledger-adjustment.posted");
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return TransactionResult.Failure("The adjustment changed concurrently or its reference already exists. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(adjustment.Id);
    }

    public async Task<TransactionResult> RecordVendorCreditAsync(RecordVendorCreditRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage)) return TransactionResult.Failure("You are not authorized to adjust vendor balances.");
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A positive amount, reference, and reason are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var amount = RoundCurrency(request.Amount);
        var bill = await db.VendorBills.SingleOrDefaultAsync(item => item.Id == request.VendorBillId && item.CompanyId == companyId, cancellationToken);
        if (bill is null) return TransactionResult.Failure("Vendor bill not found.");
        if (bill.Status == "Voided" || amount > bill.BalanceDue) return TransactionResult.Failure("The credit cannot exceed the open bill balance or target a voided bill.");
        if (request.AdjustmentDate < bill.BillDate) return TransactionResult.Failure("The credit date cannot precede the bill date.");
        var offset = await db.Accounts.SingleOrDefaultAsync(account => account.CompanyId == companyId && account.Number == request.OffsetAccountNumber.Trim() && account.IsActive && !account.IsControlAccount && (account.Type == AccountType.Expense || account.Type == AccountType.Asset), cancellationToken);
        if (offset is null) return TransactionResult.Failure("A vendor credit requires an active non-control expense or asset account.");
        if (await db.SubledgerAdjustments.AnyAsync(item => item.CompanyId == companyId && item.Subledger == "Payables" && item.Reference == request.Reference.Trim(), cancellationToken)) return TransactionResult.Failure("That payables adjustment reference already exists.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var adjustmentId = Guid.NewGuid();
        var posting = await PostAsync(db, companyId, request.AdjustmentDate, "Accounts Payable", request.Reference, request.Reason,
            [new("2000", amount, 0, "Reduce payable"), new(offset.Number, 0, amount, "Vendor credit")], cancellationToken,
            allowControlAccounts: true, sourceDocumentId: adjustmentId, sourceDocumentType: "SubledgerAdjustment");
        if (!posting.Succeeded) return posting;
        bill.BalanceDue -= amount;
        bill.Status = bill.BalanceDue == 0 ? "Paid" : "Partial";
        bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var vendor = await db.Vendors.SingleAsync(item => item.Id == bill.VendorId && item.CompanyId == companyId, cancellationToken);
        vendor.OpenBalance -= amount;
        var adjustment = CreateAdjustment(adjustmentId, companyId, "Payables", "VendorCredit", bill.VendorId, request.VendorBillId, null, null, request.AdjustmentDate, amount, request.Reference, request.Reason, offset.Number, posting.Id!.Value);
        db.SubledgerAdjustments.Add(adjustment);
        AddAdjustmentAudit(db, adjustment, "subledger-adjustment.posted");
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return TransactionResult.Failure("The credit changed concurrently or its reference already exists. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(adjustment.Id);
    }

    public async Task<TransactionResult> RefundUnappliedPaymentAsync(RefundUnappliedPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PaymentReverse)) return TransactionResult.Failure("You are not authorized to refund unapplied payments.");
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A positive refund amount, reference, and reason are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var payment = await db.SubledgerPayments.SingleOrDefaultAsync(item => item.Id == request.PaymentId && item.CompanyId == companyId, cancellationToken);
        var amount = RoundCurrency(request.Amount);
        if (payment is null || payment.Status != "Posted" || amount > payment.UnappliedAmount) return TransactionResult.Failure("The refund cannot exceed the unapplied balance of a posted payment.");
        if (request.RefundDate < payment.PaymentDate) return TransactionResult.Failure("The refund date cannot precede the payment date.");
        var bank = await db.BankAccounts.SingleOrDefaultAsync(item => item.Id == request.BankAccountId && item.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Bank account not found.");
        var cashAccount = await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken);
        if (string.IsNullOrWhiteSpace(cashAccount)) return TransactionResult.Failure("The refund bank account is not mapped to an active ledger account.");
        if (payment.Direction == "CustomerReceipt" && bank.CurrentBalance < amount) return TransactionResult.Failure("The bank account does not have sufficient book balance for this refund.");
        var subledger = payment.Direction == "CustomerReceipt" ? "Receivables" : "Payables";
        if (await db.SubledgerAdjustments.AnyAsync(item => item.CompanyId == companyId && item.Subledger == subledger && item.Reference == request.Reference.Trim(), cancellationToken)) return TransactionResult.Failure("That refund reference already exists.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var adjustmentId = Guid.NewGuid();
        var customerRefund = payment.Direction == "CustomerReceipt";
        var lines = customerRefund
            ? new JournalLineRequest[] { new("2150", amount, 0, "Release customer deposit"), new(cashAccount, 0, amount, "Customer refund") }
            : [new(cashAccount, amount, 0, "Vendor refund received"), new("1300", 0, amount, "Release vendor advance")];
        var posting = await PostAsync(db, companyId, request.RefundDate, customerRefund ? "Accounts Receivable" : "Accounts Payable", request.Reference, request.Reason, lines, cancellationToken, bank.Id, true, adjustmentId, "SubledgerAdjustment");
        if (!posting.Succeeded) return posting;
        payment.UnappliedAmount -= amount;
        payment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        bank.CurrentBalance += customerRefund ? -amount : amount;
        bank.UnreconciledAmount += amount;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var adjustment = CreateAdjustment(adjustmentId, companyId, subledger, customerRefund ? "CustomerDepositRefund" : "VendorAdvanceRefund", payment.CounterpartyId, null, payment.Id, bank.Id, request.RefundDate, amount, request.Reference, request.Reason, customerRefund ? "2150" : "1300", posting.Id!.Value);
        db.SubledgerAdjustments.Add(adjustment);
        AddAdjustmentAudit(db, adjustment, "subledger-adjustment.posted");
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return TransactionResult.Failure("The refund changed concurrently or its reference already exists. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(adjustment.Id);
    }

    public Task<TransactionResult> VoidInvoiceAsync(VoidSubledgerDocumentRequest request, CancellationToken cancellationToken = default) =>
        VoidSubledgerDocumentAsync(request, true, cancellationToken);

    public Task<TransactionResult> VoidVendorBillAsync(VoidSubledgerDocumentRequest request, CancellationToken cancellationToken = default) =>
        VoidSubledgerDocumentAsync(request, false, cancellationToken);

    public async Task<TransactionResult> ReverseSubledgerAdjustmentAsync(ReverseSubledgerAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PaymentReverse)) return TransactionResult.Failure("You are not authorized to reverse subledger adjustments.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("An adjustment reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var adjustment = await db.SubledgerAdjustments.SingleOrDefaultAsync(item => item.Id == request.AdjustmentId && item.CompanyId == companyId, cancellationToken);
        if (adjustment is null || adjustment.Status != "Posted") return TransactionResult.Failure("Only a posted adjustment can be reversed.");
        if (request.ReversalDate < adjustment.AdjustmentDate) return TransactionResult.Failure("The reversal date cannot precede the adjustment date.");
        if (await db.BankReconciliationItems.AnyAsync(item => item.JournalEntryId == adjustment.JournalEntryId, cancellationToken)) return TransactionResult.Failure("A reconciled adjustment cannot be reversed until its bank reconciliation is reopened.");
        var reversal = await PostInverseAsync(db, companyId, adjustment.JournalEntryId, request.ReversalDate, $"REV-{adjustment.Reference}", request.Reason, adjustment.Id, "SubledgerAdjustmentReversal", adjustment.BankAccountId, cancellationToken);
        if (!reversal.Succeeded) return reversal;

        if (adjustment.Kind is "CreditMemo" or "WriteOff")
        {
            var invoice = await db.SalesInvoices.SingleAsync(item => item.Id == adjustment.DocumentId && item.CompanyId == companyId, cancellationToken);
            invoice.BalanceDue += adjustment.Amount; invoice.Status = invoice.BalanceDue == invoice.TotalAmount ? "Open" : "Partial"; invoice.ConcurrencyToken = Guid.NewGuid().ToString("N");
            (await db.Customers.SingleAsync(item => item.Id == adjustment.CounterpartyId && item.CompanyId == companyId, cancellationToken)).OpenBalance += adjustment.Amount;
        }
        else if (adjustment.Kind == "VendorCredit")
        {
            var bill = await db.VendorBills.SingleAsync(item => item.Id == adjustment.DocumentId && item.CompanyId == companyId, cancellationToken);
            bill.BalanceDue += adjustment.Amount; bill.Status = bill.BalanceDue == bill.TotalAmount ? "Open" : "Partial"; bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
            (await db.Vendors.SingleAsync(item => item.Id == adjustment.CounterpartyId && item.CompanyId == companyId, cancellationToken)).OpenBalance += adjustment.Amount;
        }
        else if (adjustment.Kind is "CustomerDepositRefund" or "VendorAdvanceRefund")
        {
            var payment = await db.SubledgerPayments.SingleAsync(item => item.Id == adjustment.PaymentId && item.CompanyId == companyId, cancellationToken);
            payment.UnappliedAmount += adjustment.Amount; payment.ConcurrencyToken = Guid.NewGuid().ToString("N");
            var bank = await db.BankAccounts.SingleAsync(item => item.Id == adjustment.BankAccountId && item.CompanyId == companyId, cancellationToken);
            bank.CurrentBalance += adjustment.Kind == "CustomerDepositRefund" ? adjustment.Amount : -adjustment.Amount;
            bank.UnreconciledAmount += adjustment.Amount; bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }
        else if (adjustment.Kind == "InvoiceVoid")
        {
            var invoice = await db.SalesInvoices.SingleAsync(item => item.Id == adjustment.DocumentId && item.CompanyId == companyId, cancellationToken);
            invoice.BalanceDue = invoice.TotalAmount; invoice.Status = "Open"; invoice.ConcurrencyToken = Guid.NewGuid().ToString("N");
            (await db.Customers.SingleAsync(item => item.Id == adjustment.CounterpartyId && item.CompanyId == companyId, cancellationToken)).OpenBalance += adjustment.Amount;
        }
        else if (adjustment.Kind == "VendorBillVoid")
        {
            var bill = await db.VendorBills.SingleAsync(item => item.Id == adjustment.DocumentId && item.CompanyId == companyId, cancellationToken);
            bill.BalanceDue = bill.TotalAmount; bill.Status = "Open"; bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
            (await db.Vendors.SingleAsync(item => item.Id == adjustment.CounterpartyId && item.CompanyId == companyId, cancellationToken)).OpenBalance += adjustment.Amount;
        }
        else return TransactionResult.Failure("The adjustment kind is not reversible.");

        adjustment.Status = "Reversed"; adjustment.ReversalJournalEntryId = reversal.Id; adjustment.ReversedByUserId = ResolveUserId(); adjustment.ReversedAtUtc = DateTimeOffset.UtcNow; adjustment.ReversalReason = request.Reason.Trim(); adjustment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAdjustmentAudit(db, adjustment, "subledger-adjustment.reversed");
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The adjustment changed while it was being reversed. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(adjustment.Id);
    }

    private async Task<TransactionResult> VoidSubledgerDocumentAsync(VoidSubledgerDocumentRequest request, bool receivable, CancellationToken cancellationToken)
    {
        var modulePermission = receivable ? BrassLedgerPermissions.ReceivablesManage : BrassLedgerPermissions.PayablesManage;
        if (!HasPermission(modulePermission) || !HasPermission(BrassLedgerPermissions.PaymentReverse)) return TransactionResult.Failure("You are not authorized to void this document.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A void reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reference = string.Empty; var documentDate = default(DateOnly); var amount = 0m; var counterpartyId = Guid.Empty;
        if (receivable)
        {
            var invoice = await db.SalesInvoices.SingleOrDefaultAsync(item => item.Id == request.DocumentId && item.CompanyId == companyId, cancellationToken);
            if (invoice is null) return TransactionResult.Failure("Invoice not found.");
            if (invoice.Status == "Voided" || invoice.BalanceDue != invoice.TotalAmount) return TransactionResult.Failure("Only a fully open, unadjusted invoice can be voided.");
            reference = invoice.InvoiceNumber; documentDate = invoice.InvoiceDate; amount = invoice.TotalAmount; counterpartyId = invoice.CustomerId;
        }
        else
        {
            var bill = await db.VendorBills.SingleOrDefaultAsync(item => item.Id == request.DocumentId && item.CompanyId == companyId, cancellationToken);
            if (bill is null) return TransactionResult.Failure("Vendor bill not found.");
            if (bill.Status == "Voided" || bill.BalanceDue != bill.TotalAmount) return TransactionResult.Failure("Only a fully open, unadjusted bill can be voided.");
            reference = bill.BillNumber; documentDate = bill.BillDate; amount = bill.TotalAmount; counterpartyId = bill.VendorId;
        }
        if (request.VoidDate < documentDate) return TransactionResult.Failure("The void date cannot precede the document date.");
        if (await db.SubledgerPaymentApplications.AnyAsync(application => application.DocumentId == request.DocumentId, cancellationToken) || await db.SubledgerAdjustments.AnyAsync(item => item.CompanyId == companyId && item.DocumentId == request.DocumentId, cancellationToken)) return TransactionResult.Failure("A document with payment or adjustment history cannot be voided; use a credit or reversal workflow.");
        var sourceType = receivable ? "SalesInvoice" : "VendorBill";
        var originalJournalId = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && entry.SourceDocumentType == sourceType && entry.SourceDocumentId == request.DocumentId && entry.IsPosted).Select(entry => (Guid?)entry.Id).SingleOrDefaultAsync(cancellationToken);
        if (!originalJournalId.HasValue) return TransactionResult.Failure("The document's original posting could not be found.");
        var adjustmentId = Guid.NewGuid();
        var reversal = await PostInverseAsync(db, companyId, originalJournalId.Value, request.VoidDate, $"VOID-{reference}", request.Reason, adjustmentId, "SubledgerAdjustment", null, cancellationToken);
        if (!reversal.Succeeded) return reversal;
        if (receivable)
        {
            var invoice = await db.SalesInvoices.SingleAsync(item => item.Id == request.DocumentId, cancellationToken); invoice.BalanceDue = 0; invoice.Status = "Voided"; invoice.ConcurrencyToken = Guid.NewGuid().ToString("N");
            (await db.Customers.SingleAsync(item => item.Id == counterpartyId, cancellationToken)).OpenBalance -= amount;
        }
        else
        {
            var bill = await db.VendorBills.SingleAsync(item => item.Id == request.DocumentId, cancellationToken); bill.BalanceDue = 0; bill.Status = "Voided"; bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
            (await db.Vendors.SingleAsync(item => item.Id == counterpartyId, cancellationToken)).OpenBalance -= amount;
        }
        var adjustment = CreateAdjustment(adjustmentId, companyId, receivable ? "Receivables" : "Payables", receivable ? "InvoiceVoid" : "VendorBillVoid", counterpartyId, request.DocumentId, null, null, request.VoidDate, amount, $"VOID-{reference}", request.Reason, string.Empty, reversal.Id!.Value);
        db.SubledgerAdjustments.Add(adjustment); AddAdjustmentAudit(db, adjustment, "subledger-adjustment.posted");
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return TransactionResult.Failure("The document changed while it was being voided. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(adjustment.Id);
    }

    public async Task<TransactionResult> ReconcileBankAccountAsync(ReconcileBankAccountRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var bank = await db.BankAccounts.SingleOrDefaultAsync(x => x.Id == request.BankAccountId && x.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Bank account not found.");
        if (request.StatementDate < bank.LastReconciledOn) return TransactionResult.Failure("Statement date cannot precede the last reconciliation date.");
        if (await db.BankReconciliations.AnyAsync(reconciliation => reconciliation.BankAccountId == bank.Id && reconciliation.StatementDate == request.StatementDate, cancellationToken))
            return TransactionResult.Failure("This bank account already has a reconciliation for that statement date.");
        var candidateEntryIds = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && entry.BankAccountId == bank.Id && entry.PostedOn > bank.LastReconciledOn && entry.PostedOn <= request.StatementDate).Select(entry => entry.Id).ToListAsync(cancellationToken);
        var selectedEntryIds = request.ClearedJournalEntryIds?.Distinct().ToArray() ?? candidateEntryIds.ToArray();
        if (selectedEntryIds.Any(entryId => !candidateEntryIds.Contains(entryId)))
            return TransactionResult.Failure("A selected cleared item does not belong to this bank account or statement period.");
        var selectedLines = await db.JournalEntryLines.Where(line => selectedEntryIds.Contains(line.JournalEntryId) && line.AccountId == bank.LedgerAccountId).ToListAsync(cancellationToken);
        var clearedAmount = selectedLines.Sum(line => line.Debit - line.Credit);
        var expectedStatementBalance = decimal.Round(bank.LastReconciledBalance + clearedAmount, 2, MidpointRounding.AwayFromZero);
        var variance = decimal.Round(request.StatementClosingBalance - expectedStatementBalance, 2, MidpointRounding.AwayFromZero);
        if (variance != 0) return TransactionResult.Failure($"Statement balance differs from the cleared book activity by {variance:C}. Review the selected transactions or investigate the difference before reconciling.");
        var reconciliation = new BankReconciliation
        {
            Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = bank.Id, StatementDate = request.StatementDate,
            StatementClosingBalance = request.StatementClosingBalance, BookBalance = expectedStatementBalance,
            ReconciledByUserId = Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null,
            ReconciledAtUtc = DateTimeOffset.UtcNow
        };
        db.BankReconciliations.Add(reconciliation);
        db.BankReconciliationItems.AddRange(selectedEntryIds.Select(entryId => new BankReconciliationItem { Id = Guid.NewGuid(), BankReconciliationId = reconciliation.Id, JournalEntryId = entryId }));
        bank.UnreconciledAmount = decimal.Round(decimal.Abs(bank.CurrentBalance - request.StatementClosingBalance), 2, MidpointRounding.AwayFromZero);
        bank.LastReconciledOn = request.StatementDate;
        bank.LastReconciledBalance = request.StatementClosingBalance;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The bank account changed while reconciliation was in progress. Refresh and try again."); }
        return TransactionResult.Success(bank.Id);
    }

    public async Task<TransactionResult> UpdateBankLedgerMappingAsync(UpdateBankLedgerMappingRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var bank = await db.BankAccounts.SingleOrDefaultAsync(account => account.Id == request.BankAccountId && account.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Bank account not found.");
        var ledgerAccount = await db.Accounts.SingleOrDefaultAsync(account => account.CompanyId == companyId && account.IsActive && account.Number == request.LedgerAccountNumber.Trim(), cancellationToken);
        if (ledgerAccount is null || ledgerAccount.Type != AccountType.Asset || ledgerAccount.IsControlAccount)
            return TransactionResult.Failure("Select an active non-control asset account for the bank ledger mapping.");
        bank.LedgerAccountId = ledgerAccount.Id;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The bank account changed while its mapping was being updated. Refresh and try again."); }
        return TransactionResult.Success(bank.Id);
    }

    public async Task<TransactionResult> PostPayrollRunAsync(PostPayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (request.GrossPayroll <= 0) return TransactionResult.Failure("Gross payroll must be greater than zero.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var jurisdiction = string.IsNullOrWhiteSpace(request.TaxJurisdiction) ? "Federal" : request.TaxJurisdiction.Trim();
        var taxProfiles = await db.TaxProfiles.Where(profile => profile.CompanyId == companyId && profile.EffectiveOn <= request.PayDate && (profile.Jurisdiction == "Federal" || profile.Jurisdiction == jurisdiction)).ToListAsync(cancellationToken);
        if ((request.EmployeeWithholdings is null || request.EmployerPayrollTaxes is null) && taxProfiles.Count == 0)
            return TransactionResult.Failure("Configure effective payroll tax profiles for the selected jurisdiction before posting payroll without tax overrides.");
        var calculatedEmployeeWithholdings = RoundCurrency(request.GrossPayroll * taxProfiles.Where(profile => profile.TaxType.Contains("withholding", StringComparison.OrdinalIgnoreCase)).Sum(profile => profile.Rate));
        var calculatedEmployerPayrollTaxes = RoundCurrency(request.GrossPayroll * taxProfiles.Where(profile => !profile.TaxType.Contains("withholding", StringComparison.OrdinalIgnoreCase)).Sum(profile => profile.Rate));
        var employeeWithholdings = request.EmployeeWithholdings ?? calculatedEmployeeWithholdings;
        var employerPayrollTaxes = request.EmployerPayrollTaxes ?? calculatedEmployerPayrollTaxes;
        var netPay = request.NetPay ?? request.GrossPayroll - employeeWithholdings;
        if (netPay < 0 || employeeWithholdings < 0 || employerPayrollTaxes < 0)
            return TransactionResult.Failure("Payroll amounts must be non-negative.");
        if (RoundCurrency(netPay + employeeWithholdings) != RoundCurrency(request.GrossPayroll))
            return TransactionResult.Failure("Net pay plus employee withholdings must equal gross payroll.");
        var bank = await db.BankAccounts.SingleOrDefaultAsync(x => x.Id == request.BankAccountId && x.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Payroll funding account not found.");
        if (bank.CurrentBalance < netPay) return TransactionResult.Failure("Payroll funding account does not have sufficient book balance for net pay.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var payrollExpense = request.GrossPayroll + employerPayrollTaxes;
        var liabilities = employeeWithholdings + employerPayrollTaxes;
        var lines = new List<JournalLineRequest>
        {
            new("6100", payrollExpense, 0, "Gross payroll and employer taxes"),
            new(await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken), 0, netPay, "Net payroll funding")
        };
        if (liabilities > 0) lines.Add(new JournalLineRequest("2200", 0, liabilities, "Payroll liabilities"));
        var posting = await PostAsync(db, companyId, request.PayDate, "Payroll", request.Reference, "Payroll run",
            lines, cancellationToken, bank.Id, allowControlAccounts: true);
        if (!posting.Succeeded) return posting;
        bank.CurrentBalance -= netPay;
        bank.UnreconciledAmount += netPay;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payroll funding account changed while this run was posting. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return posting;
    }

    public async Task<PayrollRunEstimate?> PreviewEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Employees.Count == 0 || request.Employees.Any(line => line.EmployeeId == Guid.Empty || line.GrossPay <= 0) || request.Employees.Select(line => line.EmployeeId).Distinct().Count() != request.Employees.Count)
            return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        return await CalculateEmployeePayrollAsync(db, companyId, request, cancellationToken);
    }

    public async Task<TransactionResult> PostEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reference)) return TransactionResult.Failure("A payroll run reference is required.");
        if (request.Employees.Count == 0 || request.Employees.Any(line => line.EmployeeId == Guid.Empty || line.GrossPay <= 0) || request.Employees.Select(line => line.EmployeeId).Distinct().Count() != request.Employees.Count)
            return TransactionResult.Failure("Provide one positive gross-pay amount for each employee.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.PayrollRuns.AnyAsync(run => run.CompanyId == companyId && run.Reference == request.Reference.Trim(), cancellationToken))
            return TransactionResult.Failure("Payroll run reference already exists.");
        var estimate = await CalculateEmployeePayrollAsync(db, companyId, request, cancellationToken);
        if (estimate is null) return TransactionResult.Failure("Each payroll employee must be active and have applicable effective Federal or work-state tax profiles.");
        var bank = await db.BankAccounts.SingleOrDefaultAsync(account => account.Id == request.BankAccountId && account.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Payroll funding account not found.");
        if (bank.CurrentBalance < estimate.NetPay) return TransactionResult.Failure("Payroll funding account does not have sufficient book balance for net pay.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var contentSnapshot = await db.TaxContentPackages.Where(package => package.CompanyId == companyId && package.Status == "Approved" && package.EffectiveOn <= request.PayDate).OrderBy(package => package.PackageCode).ThenBy(package => package.Version).Select(package => new { package.PackageCode, package.Version, package.EffectiveOn, package.MinimumEngineVersion }).ToListAsync(cancellationToken);
        var run = new PayrollRun { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = bank.Id, PayDate = request.PayDate, Reference = request.Reference.Trim(), GrossPayroll = estimate.GrossPayroll, PreTaxDeductions = estimate.PreTaxDeductions, EmployeeWithholdings = estimate.EmployeeWithholdings, PostTaxDeductions = estimate.PostTaxDeductions, EmployerPayrollTaxes = estimate.EmployerPayrollTaxes, NetPay = estimate.NetPay, PostedAtUtc = DateTimeOffset.UtcNow, TaxContentSnapshotJson = System.Text.Json.JsonSerializer.Serialize(contentSnapshot) };
        var liabilities = estimate.PreTaxDeductions + estimate.EmployeeWithholdings + estimate.PostTaxDeductions + estimate.EmployerPayrollTaxes;
        var posting = await PostAsync(db, companyId, request.PayDate, "Payroll", run.Reference, "Employee payroll run",
            [new("6100", estimate.GrossPayroll + estimate.EmployerPayrollTaxes, 0, "Gross payroll and employer taxes"), new(await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken), 0, estimate.NetPay, "Net payroll funding"), new("2200", 0, liabilities, "Payroll liabilities")], cancellationToken, bank.Id, allowControlAccounts: true, sourceDocumentId: run.Id, sourceDocumentType: "PayrollRun");
        if (!posting.Succeeded) return posting;
        db.PayrollRuns.Add(run);
        var runEmployees = await db.Employees.Where(employee => employee.CompanyId == companyId && estimate.Employees.Select(line => line.EmployeeId).Contains(employee.Id)).ToDictionaryAsync(employee => employee.Id, cancellationToken);
        db.PayrollRunEmployeeLines.AddRange(estimate.Employees.Select(line => { var employee = runEmployees[line.EmployeeId]; return new PayrollRunEmployeeLine { Id = Guid.NewGuid(), PayrollRunId = run.Id, EmployeeId = line.EmployeeId, WorkState = line.WorkState, WorkCity = employee.WorkCity, ResidenceState = string.IsNullOrWhiteSpace(employee.ResidenceState) ? employee.State : employee.ResidenceState, ResidenceCity = employee.ResidenceCity, FilingStatus = line.FilingStatus, PayrollFrequency = employee.PayrollFrequency, GrossPay = line.GrossPay, PreTaxDeductions = line.PreTaxDeductions, EmployeeWithholdings = line.EmployeeWithholdings, PostTaxDeductions = line.PostTaxDeductions, EmployerPayrollTaxes = line.EmployerPayrollTaxes, NetPay = line.NetPay }; }));
        bank.CurrentBalance -= estimate.NetPay;
        bank.UnreconciledAmount += estimate.NetPay;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return TransactionResult.Failure("Payroll could not be posted because the reference was already used or the data changed. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(run.Id);
    }

    public async Task<TransactionResult> SaveEmployeePayrollSetupAsync(SaveEmployeePayrollSetupRequest request, CancellationToken cancellationToken = default)
    {
        if (request.EmployeeId == Guid.Empty || request.Allowances < 0 || request.AdditionalWithholding < 0 || request.PreTaxBenefitDeductions < 0 || request.PostTaxBenefitDeductions < 0)
            return TransactionResult.Failure("Payroll elections and benefit deductions must be non-negative.");
        var filingStatus = request.FilingStatus.Trim();
        if (filingStatus is not ("Single" or "Married filing jointly" or "Head of household")) return TransactionResult.Failure("Select a supported filing status.");
        var payrollFrequency = request.PayrollFrequency.Trim();
        if (payrollFrequency is not ("Weekly" or "Biweekly" or "Semimonthly" or "Monthly" or "Quarterly" or "Semiannual" or "Annual" or "Daily")) return TransactionResult.Failure("Select a supported payroll frequency.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var employee = await db.Employees.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == request.EmployeeId, cancellationToken);
        if (employee is null) return TransactionResult.Failure("Employee not found.");
        employee.FilingStatus = filingStatus;
        employee.PayrollFrequency = payrollFrequency;
        employee.Allowances = request.Allowances;
        employee.AdditionalWithholding = request.AdditionalWithholding;
        employee.PreTaxBenefitDeductions = request.PreTaxBenefitDeductions;
        employee.PostTaxBenefitDeductions = request.PostTaxBenefitDeductions;
        employee.ResidenceState = request.ResidenceState.Trim();
        employee.ResidenceCity = request.ResidenceCity.Trim();
        if (!string.IsNullOrWhiteSpace(request.WorkState)) employee.State = request.WorkState.Trim();
        employee.WorkCity = request.WorkCity.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return TransactionResult.Success(employee.Id);
    }

    public async Task<TransactionResult> SavePayrollJurisdictionRuleAsync(SavePayrollJurisdictionRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ResidenceJurisdiction) || string.IsNullOrWhiteSpace(request.WorkJurisdiction) || request.ResidentCreditRate is < 0 or > 1)
            return TransactionResult.Failure("Provide residence and work jurisdictions and a resident credit rate between 0% and 100%.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var residence = request.ResidenceJurisdiction.Trim();
        var work = request.WorkJurisdiction.Trim();
        var entity = request.Id is { } id && id != Guid.Empty
            ? await db.PayrollJurisdictionRules.SingleOrDefaultAsync(rule => rule.CompanyId == companyId && rule.Id == id, cancellationToken)
            : null;
        if (entity is null && await db.PayrollJurisdictionRules.AnyAsync(rule => rule.CompanyId == companyId && rule.ResidenceJurisdiction == residence && rule.WorkJurisdiction == work, cancellationToken))
            return TransactionResult.Failure("A rule for this residence/work jurisdiction pair already exists.");
        entity ??= new PayrollJurisdictionRule { Id = Guid.NewGuid(), CompanyId = companyId };
        entity.ResidenceJurisdiction = residence; entity.WorkJurisdiction = work; entity.ExemptWorkWithholding = request.ExemptWorkWithholding; entity.ResidentCreditRate = request.ResidentCreditRate; entity.IsActive = request.IsActive; entity.Notes = request.Notes.Trim();
        if (db.Entry(entity).State == EntityState.Detached) db.PayrollJurisdictionRules.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return TransactionResult.Success(entity.Id);
    }

    public async Task<TransactionResult> RecordInventoryAdjustmentAsync(RecordInventoryAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.InventoryItemId == Guid.Empty || request.QuantityChange == 0 || request.UnitCost <= 0 || string.IsNullOrWhiteSpace(request.Reference)) return TransactionResult.Failure("Provide an inventory item, non-zero quantity, positive unit cost, and reference.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var item = await db.InventoryItems.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == request.InventoryItemId && candidate.IsActive, cancellationToken); if (item is null) return TransactionResult.Failure("Active inventory item not found.");
        if (item.QuantityOnHand + request.QuantityChange < 0) return TransactionResult.Failure("This adjustment would make inventory quantity negative.");
        var totalCost = RoundCurrency(Math.Abs(request.QuantityChange) * request.UnitCost); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lines = request.QuantityChange > 0 ? new[] { new JournalLineRequest("1200", totalCost, 0, "Inventory increase"), new JournalLineRequest("5100", 0, totalCost, "Inventory adjustment offset") } : new[] { new JournalLineRequest("5100", totalCost, 0, "Inventory adjustment offset"), new JournalLineRequest("1200", 0, totalCost, "Inventory decrease") };
        var posting = await PostAsync(db, companyId, request.OccurredOn, "Inventory", request.Reference, request.Description, lines, cancellationToken, allowControlAccounts: true); if (!posting.Succeeded) return posting;
        item.QuantityOnHand += request.QuantityChange; item.UnitPrice = request.UnitCost;
        db.InventoryTransactions.Add(new InventoryTransaction { Id = Guid.NewGuid(), CompanyId = companyId, InventoryItemId = item.Id, OccurredOn = request.OccurredOn, TransactionType = request.QuantityChange > 0 ? "Adjustment increase" : "Adjustment decrease", QuantityChange = request.QuantityChange, UnitCost = request.UnitCost, TotalCost = totalCost, Reference = request.Reference.Trim(), JournalEntryId = posting.Id!.Value });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return TransactionResult.Success(posting.Id!.Value);
    }

    private static async Task<PayrollRunEstimate?> CalculateEmployeePayrollAsync(BrassLedgerDbContext db, Guid companyId, PostEmployeePayrollRunRequest request, CancellationToken cancellationToken)
    {
        var ids = request.Employees.Select(line => line.EmployeeId).ToArray();
        var employees = await db.Employees.Where(employee => employee.CompanyId == companyId && employee.IsActive && ids.Contains(employee.Id)).ToListAsync(cancellationToken);
        if (employees.Count != ids.Length) return null;
        var profiles = await db.TaxProfiles.Where(profile => profile.CompanyId == companyId && profile.EffectiveOn <= request.PayDate).ToListAsync(cancellationToken);
        var approvedPackageIds = await db.TaxContentPackages.Where(package => package.CompanyId == companyId && package.Status == "Approved" && package.EffectiveOn <= request.PayDate).Select(package => package.Id).ToListAsync(cancellationToken);
        var contentRules = approvedPackageIds.Count == 0 ? [] : await db.TaxRuleSets.Where(rule => rule.CompanyId == companyId && rule.IsActive && rule.EffectiveOn <= request.PayDate && rule.TaxContentPackageId != null && approvedPackageIds.Contains(rule.TaxContentPackageId.Value)).ToListAsync(cancellationToken);
        var contentRuleIds = contentRules.Select(rule => rule.Id).ToArray();
        var contentParameters = contentRuleIds.Length == 0 ? [] : await db.TaxRuleParameters.Where(parameter => contentRuleIds.Contains(parameter.TaxRuleSetId)).ToListAsync(cancellationToken);
        var contentBrackets = contentRuleIds.Length == 0 ? [] : await db.TaxRuleBrackets.Where(bracket => contentRuleIds.Contains(bracket.TaxRuleSetId)).ToListAsync(cancellationToken);
        var jurisdictionRules = await db.PayrollJurisdictionRules.Where(rule => rule.CompanyId == companyId && rule.IsActive).ToListAsync(cancellationToken);
        var priorLines = await db.PayrollRunEmployeeLines.Join(db.PayrollRuns.Where(run => run.CompanyId == companyId && run.PayDate.Year == request.PayDate.Year && run.PayDate < request.PayDate), line => line.PayrollRunId, run => run.Id, (line, _) => line).ToListAsync(cancellationToken);
        var estimates = new List<EmployeePayrollEstimate>();
        foreach (var input in request.Employees)
        {
            var employee = employees.Single(candidate => candidate.Id == input.EmployeeId);
            var workJurisdictions = new[] { StateJurisdiction(employee.State), employee.WorkCity }.Where(jurisdiction => !string.IsNullOrWhiteSpace(jurisdiction)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var residenceJurisdictions = new[] { StateJurisdiction(string.IsNullOrWhiteSpace(employee.ResidenceState) ? employee.State : employee.ResidenceState), employee.ResidenceCity }.Where(jurisdiction => !string.IsNullOrWhiteSpace(jurisdiction)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var rules = jurisdictionRules.Where(rule => residenceJurisdictions.Contains(rule.ResidenceJurisdiction, StringComparer.OrdinalIgnoreCase) && workJurisdictions.Contains(rule.WorkJurisdiction, StringComparer.OrdinalIgnoreCase)).ToArray();
            var jurisdictions = new[] { "Federal" }.Concat(workJurisdictions).Concat(residenceJurisdictions).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var applicable = profiles.Where(profile => jurisdictions.Contains(profile.Jurisdiction, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (rules.Any(rule => rule.ExemptWorkWithholding))
                applicable = applicable.Where(profile => !profile.TaxType.Contains("withholding", StringComparison.OrdinalIgnoreCase) || !workJurisdictions.Contains(profile.Jurisdiction, StringComparer.OrdinalIgnoreCase) || residenceJurisdictions.Contains(profile.Jurisdiction, StringComparer.OrdinalIgnoreCase)).ToArray();
            var preTax = Math.Min(input.GrossPay, Math.Max(0, employee.PreTaxBenefitDeductions));
            var taxable = input.GrossPay - preTax;
            var evaluationContext = new TaxRuleEvaluationContext(taxable, employee.Allowances, employee.FilingStatus, employee.PayrollFrequency, string.IsNullOrWhiteSpace(employee.ResidenceState) ? employee.State : employee.ResidenceState, employee.ResidenceCity, employee.State, employee.WorkCity);
            var matchedRules = contentRules.Where(rule => jurisdictions.Contains(rule.JurisdictionCode, StringComparer.OrdinalIgnoreCase) || jurisdictions.Contains(rule.JurisdictionName, StringComparer.OrdinalIgnoreCase) || (string.Equals(rule.JurisdictionCode, "US", StringComparison.OrdinalIgnoreCase) && jurisdictions.Contains("Federal", StringComparer.OrdinalIgnoreCase)))
                .Where(rule => TaxRuleEvaluator.IsApplicable(rule, contentParameters.Where(parameter => parameter.TaxRuleSetId == rule.Id), evaluationContext))
                .ToArray();
            var applicableRules = matchedRules
                .GroupBy(rule => string.IsNullOrWhiteSpace(rule.ExclusiveGroup) ? $"rule:{rule.Id}" : $"exclusive:{rule.ExclusiveGroup}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(rule => rule.VariantPriority).ThenBy(rule => rule.Code).First())
                .ToArray();
            if (applicable.Length == 0 && applicableRules.Length == 0) return null;
            decimal employeeTaxes = 0, residentEmployeeTaxes = 0, employerTaxes = 0;
            if (applicableRules.Length > 0)
            {
                foreach (var rule in applicableRules)
                {
                    var amount = TaxRuleEvaluator.Evaluate(rule, contentParameters.Where(parameter => parameter.TaxRuleSetId == rule.Id), contentBrackets.Where(bracket => bracket.TaxRuleSetId == rule.Id), evaluationContext);
                    if (rule.TaxType.Contains("withholding", StringComparison.OrdinalIgnoreCase) || rule.TaxType.Contains("employee", StringComparison.OrdinalIgnoreCase)) { employeeTaxes += amount; if (residenceJurisdictions.Contains(rule.JurisdictionCode, StringComparer.OrdinalIgnoreCase) || residenceJurisdictions.Contains(rule.JurisdictionName, StringComparer.OrdinalIgnoreCase)) residentEmployeeTaxes += amount; }
                    else employerTaxes += amount;
                }
            }
            else foreach (var profile in applicable)
            {
                var priorGross = priorLines.Where(line => line.EmployeeId == employee.Id).Sum(line => line.GrossPay - line.PreTaxDeductions);
                var cappedTaxable = profile.AnnualWageBase is { } wageBase ? Math.Max(0, Math.Min(taxable, wageBase - priorGross)) : taxable;
                var amount = RoundCurrency(cappedTaxable * profile.Rate);
                if (profile.TaxType.Contains("withholding", StringComparison.OrdinalIgnoreCase)) { employeeTaxes += amount; if (residenceJurisdictions.Contains(profile.Jurisdiction, StringComparer.OrdinalIgnoreCase) && !string.Equals(profile.Jurisdiction, "Federal", StringComparison.OrdinalIgnoreCase)) residentEmployeeTaxes += amount; }
                else employerTaxes += amount;
            }
            employeeTaxes -= RoundCurrency(residentEmployeeTaxes * rules.Select(rule => rule.ResidentCreditRate).DefaultIfEmpty(0m).Max());
            employeeTaxes = RoundCurrency(employeeTaxes + Math.Max(0, employee.AdditionalWithholding));
            var postTax = Math.Min(Math.Max(0, employee.PostTaxBenefitDeductions), Math.Max(0, taxable - employeeTaxes));
            var net = RoundCurrency(Math.Max(0, input.GrossPay - preTax - employeeTaxes - postTax));
            estimates.Add(new EmployeePayrollEstimate(employee.Id, $"{employee.FirstName} {employee.LastName}", employee.State, employee.FilingStatus, input.GrossPay, preTax, employeeTaxes, postTax, RoundCurrency(employerTaxes), net));
        }
        return new PayrollRunEstimate(estimates.Sum(line => line.GrossPay), estimates.Sum(line => line.PreTaxDeductions), estimates.Sum(line => line.EmployeeWithholdings), estimates.Sum(line => line.PostTaxDeductions), estimates.Sum(line => line.EmployerPayrollTaxes), estimates.Sum(line => line.NetPay), estimates);
    }

    private static string StateJurisdiction(string state) => TaxRuleCatalog.StateJurisdictions.FirstOrDefault(jurisdiction => string.Equals(jurisdiction.Code, state.Trim(), StringComparison.OrdinalIgnoreCase))?.Name ?? state.Trim();

    private async Task<TransactionResult> RecordSubledgerPaymentAsync(string direction, Guid counterpartyId, Guid bankAccountId, DateOnly date, decimal amount, string reference, string method, IReadOnlyList<PaymentDocumentApplicationRequest> requestedApplications, CancellationToken cancellationToken)
    {
        if (amount <= 0) return TransactionResult.Failure("Payment amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(reference)) return TransactionResult.Failure("A payment reference is required.");
        var normalizedMethod = method?.Trim() ?? string.Empty;
        if (normalizedMethod is not ("Cash" or "Check" or "ACH" or "Card" or "Wire" or "Other")) return TransactionResult.Failure("Payment method must be Cash, Check, ACH, Card, Wire, or Other.");
        var applications = requestedApplications?.ToArray() ?? [];
        if (applications.Any(application => application.DocumentId == Guid.Empty || application.Amount <= 0) || applications.Select(application => application.DocumentId).Distinct().Count() != applications.Length)
            return TransactionResult.Failure("Payment applications require unique documents and positive amounts.");
        var appliedAmount = RoundCurrency(applications.Sum(application => application.Amount));
        amount = RoundCurrency(amount);
        if (appliedAmount > amount) return TransactionResult.Failure("Applied amounts cannot exceed the total payment.");
        var unappliedAmount = amount - appliedAmount;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.SubledgerPayments.AnyAsync(payment => payment.CompanyId == companyId && payment.Direction == direction && payment.Reference == reference.Trim(), cancellationToken))
            return TransactionResult.Failure("That payment reference has already been recorded for this payment type.");
        var bank = await db.BankAccounts.SingleOrDefaultAsync(account => account.Id == bankAccountId && account.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Bank account not found.");
        var cashAccountNumber = await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken);
        if (string.IsNullOrWhiteSpace(cashAccountNumber)) return TransactionResult.Failure("The payment bank account is not mapped to an active ledger account.");
        if (direction == "VendorDisbursement" && bank.CurrentBalance < amount) return TransactionResult.Failure("The bank account does not have sufficient book balance for this payment.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var paymentId = Guid.NewGuid();
        IReadOnlyList<JournalLineRequest> postingLines;
        if (direction == "CustomerReceipt")
        {
            if (!await db.Customers.AnyAsync(customer => customer.Id == counterpartyId && customer.CompanyId == companyId, cancellationToken)) return TransactionResult.Failure("Customer not found.");
            var ids = applications.Select(application => application.DocumentId).ToArray();
            var invoices = await db.SalesInvoices.Where(invoice => invoice.CompanyId == companyId && invoice.CustomerId == counterpartyId && ids.Contains(invoice.Id)).ToDictionaryAsync(invoice => invoice.Id, cancellationToken);
            if (invoices.Count != applications.Length) return TransactionResult.Failure("Every invoice application must belong to the selected customer.");
            foreach (var application in applications)
            {
                var invoice = invoices[application.DocumentId];
                if (date < invoice.InvoiceDate) return TransactionResult.Failure($"Payment date cannot precede invoice {invoice.InvoiceNumber}.");
                if (application.Amount > invoice.BalanceDue) return TransactionResult.Failure($"Application to invoice {invoice.InvoiceNumber} exceeds its remaining balance.");
                invoice.BalanceDue -= application.Amount;
                invoice.Status = invoice.BalanceDue == 0 ? "Paid" : "Partial";
                invoice.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
            var customer = await db.Customers.SingleAsync(item => item.Id == counterpartyId && item.CompanyId == companyId, cancellationToken);
            customer.OpenBalance -= appliedAmount;
            var lines = new List<JournalLineRequest> { new(cashAccountNumber, amount, 0, "Customer cash receipt") };
            if (appliedAmount > 0) lines.Add(new("1100", 0, appliedAmount, "Invoice applications"));
            if (unappliedAmount > 0) lines.Add(new("2150", 0, unappliedAmount, "Unapplied customer deposit"));
            postingLines = lines;
            bank.CurrentBalance += amount;
        }
        else if (direction == "VendorDisbursement")
        {
            if (!await db.Vendors.AnyAsync(vendor => vendor.Id == counterpartyId && vendor.CompanyId == companyId, cancellationToken)) return TransactionResult.Failure("Vendor not found.");
            var ids = applications.Select(application => application.DocumentId).ToArray();
            var bills = await db.VendorBills.Where(bill => bill.CompanyId == companyId && bill.VendorId == counterpartyId && ids.Contains(bill.Id)).ToDictionaryAsync(bill => bill.Id, cancellationToken);
            if (bills.Count != applications.Length) return TransactionResult.Failure("Every bill application must belong to the selected vendor.");
            foreach (var application in applications)
            {
                var bill = bills[application.DocumentId];
                if (date < bill.BillDate) return TransactionResult.Failure($"Payment date cannot precede bill {bill.BillNumber}.");
                if (application.Amount > bill.BalanceDue) return TransactionResult.Failure($"Application to bill {bill.BillNumber} exceeds its remaining balance.");
                bill.BalanceDue -= application.Amount;
                bill.Status = bill.BalanceDue == 0 ? "Paid" : "Partial";
                bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
            var vendor = await db.Vendors.SingleAsync(item => item.Id == counterpartyId && item.CompanyId == companyId, cancellationToken);
            vendor.OpenBalance -= appliedAmount;
            var lines = new List<JournalLineRequest>();
            if (appliedAmount > 0) lines.Add(new("2000", appliedAmount, 0, "Bill applications"));
            if (unappliedAmount > 0) lines.Add(new("1300", unappliedAmount, 0, "Unapplied vendor advance"));
            lines.Add(new(cashAccountNumber, 0, amount, "Vendor cash disbursement"));
            postingLines = lines;
            bank.CurrentBalance -= amount;
        }
        else return TransactionResult.Failure("The payment direction is not supported.");

        var posting = await PostAsync(db, companyId, date, direction == "CustomerReceipt" ? "Accounts Receivable" : "Accounts Payable", reference, direction == "CustomerReceipt" ? "Customer payment" : "Vendor payment", postingLines, cancellationToken, bank.Id, allowControlAccounts: true, sourceDocumentId: paymentId, sourceDocumentType: "SubledgerPayment");
        if (!posting.Succeeded) return posting;
        var payment = new SubledgerPayment
        {
            Id = paymentId, CompanyId = companyId, Direction = direction, CounterpartyId = counterpartyId, BankAccountId = bank.Id,
            PaymentDate = date, Amount = amount, AppliedAmount = appliedAmount, UnappliedAmount = unappliedAmount,
            Reference = reference.Trim(), Method = normalizedMethod, Status = "Posted", JournalEntryId = posting.Id!.Value,
            CreatedByUserId = ResolveUserId(), CreatedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        db.SubledgerPayments.Add(payment);
        db.SubledgerPaymentApplications.AddRange(applications.Select(application => new SubledgerPaymentApplication { Id = Guid.NewGuid(), SubledgerPaymentId = payment.Id, DocumentId = application.DocumentId, Amount = RoundCurrency(application.Amount) }));
        bank.UnreconciledAmount += amount;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPaymentAudit(db, companyId, payment, "payment.posted", new { applicationCount = applications.Length });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("A document or bank balance changed while the payment was posting. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The payment reference already exists or its documents changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(payment.Id);
    }

    private async Task<TransactionResult> PostAsync(BrassLedgerDbContext db, Guid companyId, DateOnly date, string module, string reference, string description, IReadOnlyList<JournalLineRequest> lines, CancellationToken ct, Guid? bankAccountId = null, bool allowControlAccounts = false, Guid? sourceDocumentId = null, string? sourceDocumentType = null)
    {
        if (await IsClosedPeriodAsync(db, companyId, date, ct)) return TransactionResult.Failure("This posting date is in a closed accounting period.");
        if (lines.Count < 2 || lines.Any(x => x.Debit < 0 || x.Credit < 0 || (x.Debit == 0 && x.Credit == 0) || (x.Debit > 0 && x.Credit > 0))) return TransactionResult.Failure("Journal entries require at least two valid debit or credit lines.");
        var debits = lines.Sum(x => x.Debit); var credits = lines.Sum(x => x.Credit);
        if (debits != credits) return TransactionResult.Failure("Journal entry debits and credits must balance.");
        var numbers = lines.Select(x => x.AccountNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var accounts = await db.Accounts.Where(x => x.CompanyId == companyId && x.IsActive && numbers.Contains(x.Number)).ToListAsync(ct);
        if (accounts.Count != numbers.Length) return TransactionResult.Failure("One or more active posting accounts could not be found.");
        if (!allowControlAccounts && accounts.Any(account => account.IsControlAccount)) return TransactionResult.Failure("General journal entries cannot post directly to control accounts; use the related receivables, payables, inventory, or payroll workflow.");
        var userId = ResolveUserId();
        var now = DateTimeOffset.UtcNow;
        var entry = new JournalEntry { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = bankAccountId, SourceDocumentId = sourceDocumentId, SourceDocumentType = sourceDocumentType ?? string.Empty, EntryNumber = $"JE-{date:yyyyMMdd}-{Guid.NewGuid():N}"[..20], PostedOn = date, SourceModule = module, Reference = reference.Trim(), Description = description.Trim(), TotalAmount = debits, Status = "Posted", IsPosted = true, CreatedByUserId = userId, CreatedAtUtc = now, ApprovedByUserId = userId, ApprovedAtUtc = now, PostedByUserId = userId, PostedAtUtc = now, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.JournalEntries.Add(entry);
        foreach (var line in lines)
        {
            var account = accounts.Single(x => string.Equals(x.Number, line.AccountNumber.Trim(), StringComparison.OrdinalIgnoreCase));
            account.CurrentBalance += account.Type is AccountType.Asset or AccountType.Expense ? line.Debit - line.Credit : line.Credit - line.Debit;
            db.JournalEntryLines.Add(new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = entry.Id, AccountId = account.Id, Description = line.Description.Trim(), Debit = line.Debit, Credit = line.Credit });
        }
        AddJournalAudit(db, companyId, userId, "journal.posted", entry, new { entry.SourceDocumentType, entry.SourceDocumentId });
        await db.SaveChangesAsync(ct);
        return TransactionResult.Success(entry.Id);
    }

    private static Task<bool> IsClosedPeriodAsync(BrassLedgerDbContext db, Guid companyId, DateOnly date, CancellationToken cancellationToken) =>
        db.AccountingPeriods.AnyAsync(period => period.CompanyId == companyId && period.Status == "Closed" && period.StartsOn <= date && period.EndsOn >= date, cancellationToken);

    private Guid? ResolveUserId()
    {
        var userIdValue = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
    }

    private bool HasPermission(string permission)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var principal = httpContext?.User;
        if (principal is null) return true;
        if (!Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out _))
            throw new UnauthorizedAccessException("An authenticated company context is required.");
        return principal.IsInRole("Administrator")
            || principal.IsInRole("Owner/CEO")
            || principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission);
    }

    private static void AddJournalAudit(BrassLedgerDbContext db, Guid companyId, Guid? userId, string action, JournalEntry entry, object details)
    {
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = userId, Action = action, EntityType = "JournalEntry", EntityId = entry.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { entry.EntryNumber, entry.PostedOn, entry.SourceModule, entry.Reference, entry.Description, entry.TotalAmount, entry.Status, details }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void AddPaymentAudit(BrassLedgerDbContext db, Guid companyId, SubledgerPayment payment, string action, object details)
    {
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = "SubledgerPayment", EntityId = payment.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { payment.Direction, payment.CounterpartyId, payment.BankAccountId, payment.PaymentDate, payment.Amount, payment.AppliedAmount, payment.UnappliedAmount, payment.Reference, payment.Method, payment.Status, details }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static SubledgerAdjustment CreateAdjustment(Guid id, Guid companyId, string subledger, string kind, Guid counterpartyId, Guid? documentId, Guid? paymentId, Guid? bankAccountId, DateOnly date, decimal amount, string reference, string reason, string offsetAccountNumber, Guid journalEntryId) => new()
    {
        Id = id, CompanyId = companyId, Subledger = subledger, Kind = kind, CounterpartyId = counterpartyId, DocumentId = documentId, PaymentId = paymentId, BankAccountId = bankAccountId,
        AdjustmentDate = date, Amount = amount, Reference = reference.Trim(), Reason = reason.Trim(), OffsetAccountNumber = offsetAccountNumber, Status = "Posted", JournalEntryId = journalEntryId,
        CreatedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N")
    };

    private async Task<TransactionResult> SaveSubledgerWorkflowAsync<T>(string documentType, string documentNumber, T payload, bool recurring, string frequency, int interval, DateOnly? nextDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        if (!HasPermission(BrassLedgerPermissions.SubledgerPrepare)) return TransactionResult.Failure("You are not authorized to prepare invoice or bill drafts.");
        var modulePermission = documentType == "Invoice" ? BrassLedgerPermissions.ReceivablesManage : BrassLedgerPermissions.PayablesManage;
        if (!HasPermission(modulePermission)) return TransactionResult.Failure("You are not authorized for this subledger.");
        if (string.IsNullOrWhiteSpace(documentNumber)) return TransactionResult.Failure("A document number is required.");
        var normalizedFrequency = (frequency ?? string.Empty).Trim();
        if (recurring && (normalizedFrequency is not ("Weekly" or "Monthly" or "Quarterly" or "Annually") || interval is < 1 or > 12 || !nextDate.HasValue || (endDate.HasValue && endDate.Value < nextDate.Value))) return TransactionResult.Failure("Recurring templates require Weekly, Monthly, Quarterly, or Annually frequency, an interval from 1 to 12, and valid occurrence dates.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.SubledgerDocumentWorkflows.AnyAsync(item => item.CompanyId == companyId && item.DocumentType == documentType && item.DocumentNumber == documentNumber.Trim() && item.IsRecurringTemplate == recurring, cancellationToken)) return TransactionResult.Failure("That draft or recurring template number already exists.");
        var workflow = new SubledgerDocumentWorkflow { Id = Guid.NewGuid(), CompanyId = companyId, DocumentType = documentType, DocumentNumber = documentNumber.Trim(), PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload), Status = recurring ? "Active" : "Draft", IsRecurringTemplate = recurring, Frequency = recurring ? normalizedFrequency : string.Empty, FrequencyInterval = recurring ? interval : 1, NextOccurrenceDate = nextDate, EndDate = endDate, CreatedByUserId = ResolveUserId(), CreatedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.SubledgerDocumentWorkflows.Add(workflow); AddWorkflowAudit(db, workflow, recurring ? "recurring-template.saved" : "subledger-document.draft.saved");
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return TransactionResult.Failure("The draft or recurring template number already exists or changed concurrently."); }
        return TransactionResult.Success(workflow.Id);
    }

    private static DateOnly AdvanceOccurrence(DateOnly date, string frequency, int interval) => frequency switch
    {
        "Weekly" => date.AddDays(7 * interval),
        "Monthly" => date.AddMonths(interval),
        "Quarterly" => date.AddMonths(3 * interval),
        "Annually" => date.AddYears(interval),
        _ => throw new InvalidOperationException("Unsupported recurrence frequency.")
    };

    private void AddWorkflowAudit(BrassLedgerDbContext db, SubledgerDocumentWorkflow workflow, string action) => db.BusinessAuditEntries.Add(new BusinessAuditEntry
    {
        Id = Guid.NewGuid(), CompanyId = workflow.CompanyId, UserId = ResolveUserId(), Action = action, EntityType = "SubledgerDocumentWorkflow", EntityId = workflow.Id,
        DetailJson = System.Text.Json.JsonSerializer.Serialize(new { workflow.DocumentType, workflow.DocumentNumber, workflow.Status, workflow.IsRecurringTemplate, workflow.Frequency, workflow.FrequencyInterval, workflow.NextOccurrenceDate, workflow.EndDate, workflow.SourceTemplateId, workflow.PostedDocumentId }), OccurredAtUtc = DateTimeOffset.UtcNow
    });

    private void AddAdjustmentAudit(BrassLedgerDbContext db, SubledgerAdjustment adjustment, string action)
    {
        adjustment.CreatedByUserId ??= ResolveUserId();
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = adjustment.CompanyId, UserId = ResolveUserId(), Action = action, EntityType = "SubledgerAdjustment", EntityId = adjustment.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { adjustment.Subledger, adjustment.Kind, adjustment.CounterpartyId, adjustment.DocumentId, adjustment.PaymentId, adjustment.BankAccountId, adjustment.AdjustmentDate, adjustment.Amount, adjustment.Reference, adjustment.Reason, adjustment.OffsetAccountNumber, adjustment.Status, adjustment.JournalEntryId, adjustment.ReversalJournalEntryId }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task<TransactionResult> PostInverseAsync(BrassLedgerDbContext db, Guid companyId, Guid originalJournalEntryId, DateOnly date, string reference, string description, Guid sourceDocumentId, string sourceDocumentType, Guid? bankAccountId, CancellationToken cancellationToken)
    {
        var original = await db.JournalEntries.SingleOrDefaultAsync(entry => entry.Id == originalJournalEntryId && entry.CompanyId == companyId && entry.IsPosted, cancellationToken);
        if (original is null || original.ReversedByJournalEntryId.HasValue) return TransactionResult.Failure("The original journal is unavailable or has already been reversed.");
        var originalLines = await db.JournalEntryLines.Where(line => line.JournalEntryId == originalJournalEntryId).ToListAsync(cancellationToken);
        var accountIds = originalLines.Select(line => line.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.Where(account => account.CompanyId == companyId && accountIds.Contains(account.Id)).ToDictionaryAsync(account => account.Id, cancellationToken);
        if (originalLines.Count < 2 || accounts.Count != accountIds.Length) return TransactionResult.Failure("The original journal distribution is unavailable.");
        var lines = originalLines.Select(line => new JournalLineRequest(accounts[line.AccountId].Number, line.Credit, line.Debit, $"Reversal: {line.Description}")).ToArray();
        if (await IsClosedPeriodAsync(db, companyId, date, cancellationToken)) return TransactionResult.Failure("This posting date is in a closed accounting period.");
        var debits = lines.Sum(line => line.Debit); var credits = lines.Sum(line => line.Credit);
        if (debits != credits) return TransactionResult.Failure("The original journal is not balanced.");
        var now = DateTimeOffset.UtcNow;
        var userId = ResolveUserId();
        var entry = new JournalEntry { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = bankAccountId, SourceDocumentId = sourceDocumentId, SourceDocumentType = sourceDocumentType, EntryNumber = $"JE-{date:yyyyMMdd}-{Guid.NewGuid():N}"[..20], PostedOn = date, SourceModule = "Subledger Adjustment", Reference = reference.Trim(), Description = description.Trim(), TotalAmount = debits, Status = "Posted", IsPosted = true, CreatedByUserId = userId, CreatedAtUtc = now, ApprovedByUserId = userId, ApprovedAtUtc = now, PostedByUserId = userId, PostedAtUtc = now, ReversalOfJournalEntryId = original.Id, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.JournalEntries.Add(entry);
        original.Status = "Reversed";
        original.ReversedByJournalEntryId = entry.Id;
        original.ConcurrencyToken = Guid.NewGuid().ToString("N");
        foreach (var line in lines)
        {
            var account = accounts.Values.Single(item => item.Number.Equals(line.AccountNumber, StringComparison.OrdinalIgnoreCase));
            account.CurrentBalance += account.Type is AccountType.Asset or AccountType.Expense ? line.Debit - line.Credit : line.Credit - line.Debit;
            db.JournalEntryLines.Add(new JournalEntryLine { Id = Guid.NewGuid(), JournalEntryId = entry.Id, AccountId = account.Id, Description = line.Description, Debit = line.Debit, Credit = line.Credit });
        }
        AddJournalAudit(db, companyId, userId, "journal.posted", entry, new { entry.SourceDocumentType, entry.SourceDocumentId });
        await db.SaveChangesAsync(cancellationToken);
        return TransactionResult.Success(entry.Id);
    }

    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken ct)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var claim = httpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (Guid.TryParse(claim, out var id)) return id;
        if (httpContext is not null) throw new UnauthorizedAccessException("An authenticated company context is required.");
        return await db.Companies.Select(x => x.Id).FirstAsync(ct);
    }

    private static async Task<string> ResolveBankLedgerAccountNumberAsync(BrassLedgerDbContext db, Guid companyId, BankAccount bank, CancellationToken ct)
    {
        if (bank.LedgerAccountId == Guid.Empty) return string.Empty;
        return await db.Accounts.Where(account => account.CompanyId == companyId && account.Id == bank.LedgerAccountId && account.IsActive)
            .Select(account => account.Number).SingleOrDefaultAsync(ct) ?? string.Empty;
    }

    private static decimal RoundCurrency(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
