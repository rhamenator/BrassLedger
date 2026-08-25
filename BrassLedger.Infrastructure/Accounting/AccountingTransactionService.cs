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
        if (await IsInCompletedReconciliationAsync(db, original.Id, cancellationToken)) return TransactionResult.Failure("A reconciled journal entry cannot be reversed until the reconciliation is reopened.");
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
        if (await IsInCompletedReconciliationAsync(db, payment.JournalEntryId, cancellationToken)) return TransactionResult.Failure("A reconciled payment cannot be reversed until its bank reconciliation is reopened.");
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
        if (await IsInCompletedReconciliationAsync(db, adjustment.JournalEntryId, cancellationToken)) return TransactionResult.Failure("A reconciled adjustment cannot be reversed until its bank reconciliation is reopened.");
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
        if (!HasPermission(BrassLedgerPermissions.LedgerManage)) return TransactionResult.Failure("You are not authorized to complete bank reconciliations.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var bank = await db.BankAccounts.SingleOrDefaultAsync(x => x.Id == request.BankAccountId && x.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Bank account not found.");
        if (request.StatementDate < bank.LastReconciledOn) return TransactionResult.Failure("Statement date cannot precede the last reconciliation date.");
        var reconciliation = await db.BankReconciliations.SingleOrDefaultAsync(item => item.BankAccountId == bank.Id && item.StatementDate == request.StatementDate, cancellationToken);
        if (reconciliation?.Status == "Completed") return TransactionResult.Failure("This bank account already has a completed reconciliation for that statement date.");
        var candidateEntryIds = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && entry.BankAccountId == bank.Id && entry.IsPosted && entry.Status == "Posted" && entry.PostedOn > bank.LastReconciledOn && entry.PostedOn <= request.StatementDate).Select(entry => entry.Id).ToListAsync(cancellationToken);
        var selectedEntryIds = request.ClearedJournalEntryIds?.Distinct().ToArray() ?? candidateEntryIds.ToArray();
        if (selectedEntryIds.Any(entryId => !candidateEntryIds.Contains(entryId)))
            return TransactionResult.Failure("A selected cleared item does not belong to this bank account or statement period.");
        var selectedLines = await db.JournalEntryLines.Where(line => selectedEntryIds.Contains(line.JournalEntryId) && line.AccountId == bank.LedgerAccountId).ToListAsync(cancellationToken);
        var candidateLines = await db.JournalEntryLines.Where(line => candidateEntryIds.Contains(line.JournalEntryId) && line.AccountId == bank.LedgerAccountId).ToListAsync(cancellationToken);
        var clearedAmount = selectedLines.Sum(line => line.Debit - line.Credit);
        var expectedStatementBalance = decimal.Round(bank.LastReconciledBalance + clearedAmount, 2, MidpointRounding.AwayFromZero);
        var bookBalance = decimal.Round(bank.LastReconciledBalance + candidateLines.Sum(line => line.Debit - line.Credit), 2, MidpointRounding.AwayFromZero);
        var variance = decimal.Round(request.StatementClosingBalance - expectedStatementBalance, 2, MidpointRounding.AwayFromZero);
        if (variance != 0) return TransactionResult.Failure($"Statement balance differs from the cleared book activity by {variance:C}. Review the selected transactions or investigate the difference before reconciling.");
        if (reconciliation is null) { reconciliation = new BankReconciliation { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = bank.Id }; db.BankReconciliations.Add(reconciliation); }
        else db.BankReconciliationItems.RemoveRange(await db.BankReconciliationItems.Where(item => item.BankReconciliationId == reconciliation.Id).ToListAsync(cancellationToken));
        reconciliation.StatementDate = request.StatementDate; reconciliation.OpeningBalance = bank.LastReconciledBalance; reconciliation.ClearedAmount = clearedAmount; reconciliation.StatementClosingBalance = request.StatementClosingBalance; reconciliation.BookBalance = bookBalance; reconciliation.Variance = variance; reconciliation.Status = "Completed"; reconciliation.Notes = (request.Notes ?? string.Empty).Trim(); reconciliation.ReconciledByUserId = ResolveUserId(); reconciliation.ReconciledAtUtc = DateTimeOffset.UtcNow; reconciliation.ReopenedByUserId = null; reconciliation.ReopenedAtUtc = null; reconciliation.ReopenReason = string.Empty; reconciliation.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BankReconciliationItems.AddRange(selectedEntryIds.Select(entryId => new BankReconciliationItem { Id = Guid.NewGuid(), BankReconciliationId = reconciliation.Id, JournalEntryId = entryId }));
        bank.UnreconciledAmount = decimal.Round(decimal.Abs(bank.CurrentBalance - request.StatementClosingBalance), 2, MidpointRounding.AwayFromZero);
        bank.LastReconciledOn = request.StatementDate;
        bank.LastReconciledBalance = request.StatementClosingBalance;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddBankAudit(db, companyId, "bank-reconciliation.completed", reconciliation.Id, new { reconciliation.StatementDate, reconciliation.OpeningBalance, reconciliation.ClearedAmount, reconciliation.StatementClosingBalance, reconciliation.BookBalance, reconciliation.Variance, reconciliation.Notes, itemCount = selectedEntryIds.Length });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The bank account changed while reconciliation was in progress. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("This reconciliation was completed concurrently. Refresh and review the resulting report."); }
        return TransactionResult.Success(reconciliation.Id);
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

    public async Task<BankStatementImportResult> ImportBankStatementAsync(ImportBankStatementRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage)) return BankStatementImportResult.Failure("You are not authorized to import bank statements.");
        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.Content)) return BankStatementImportResult.Failure("A statement file name and content are required.");
        IReadOnlyList<ParsedBankRow> rows; IReadOnlyList<string> rejections;
        try { (rows, rejections) = ParseBankStatement(request.Format, request.Content); }
        catch (InvalidDataException exception) { return BankStatementImportResult.Failure(exception.Message); }
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.BankAccounts.AnyAsync(item => item.Id == request.BankAccountId && item.CompanyId == companyId, cancellationToken)) return BankStatementImportResult.Failure("Bank account not found.");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Content))).ToLowerInvariant();
        if (await db.BankStatementImportBatches.AnyAsync(item => item.CompanyId == companyId && item.BankAccountId == request.BankAccountId && item.ContentSha256 == hash, cancellationToken)) return BankStatementImportResult.Failure("This exact statement file was already imported.");
        var externalIds = rows.Select(item => item.ExternalId).ToArray();
        var existing = await db.BankStatementTransactions.Where(item => item.CompanyId == companyId && item.BankAccountId == request.BankAccountId && externalIds.Contains(item.ExternalId)).Select(item => item.ExternalId).ToListAsync(cancellationToken);
        var accepted = rows.Where(item => !existing.Contains(item.ExternalId, StringComparer.Ordinal)).ToArray();
        var debitTotal = accepted.Where(item => item.Amount < 0).Sum(item => -item.Amount); var creditTotal = accepted.Where(item => item.Amount > 0).Sum(item => item.Amount);
        if (request.DryRun) return new BankStatementImportResult(true, null, accepted.Length, rows.Count - accepted.Length, rejections.Count, debitTotal, creditTotal, rejections, string.Empty);
        var batch = new BankStatementImportBatch { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = request.BankAccountId, FileName = Path.GetFileName(request.FileName), Format = NormalizeBankFormat(request.Format), ContentSha256 = hash, ImportedCount = accepted.Length, DuplicateCount = rows.Count - accepted.Length, RejectedCount = rejections.Count, DebitTotal = debitTotal, CreditTotal = creditTotal, RejectionJson = System.Text.Json.JsonSerializer.Serialize(rejections), ImportedByUserId = ResolveUserId(), ImportedAtUtc = DateTimeOffset.UtcNow, Status = rejections.Count == 0 ? "Imported" : "ImportedWithRejections" };
        db.BankStatementImportBatches.Add(batch);
        db.BankStatementTransactions.AddRange(accepted.Select(row => new BankStatementTransaction { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = request.BankAccountId, ImportBatchId = batch.Id, ExternalId = row.ExternalId, TransactionDate = row.Date, PostedDate = row.PostedDate, Amount = row.Amount, TransactionType = row.Type, Payee = row.Payee, Memo = row.Memo, Reference = row.Reference, RawJson = row.RawJson, Status = "Unmatched", ConcurrencyToken = Guid.NewGuid().ToString("N") }));
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = "bank-statement.imported", EntityType = "BankStatementImportBatch", EntityId = batch.Id, DetailJson = System.Text.Json.JsonSerializer.Serialize(new { batch.FileName, batch.Format, batch.ContentSha256, batch.ImportedCount, batch.DuplicateCount, batch.RejectedCount, batch.DebitTotal, batch.CreditTotal }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return BankStatementImportResult.Failure("The statement or one of its transactions was imported concurrently."); }
        return new BankStatementImportResult(true, batch.Id, batch.ImportedCount, batch.DuplicateCount, batch.RejectedCount, batch.DebitTotal, batch.CreditTotal, rejections, string.Empty);
    }

    public async Task<TransactionResult> MatchBankTransactionAsync(MatchBankTransactionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage)) return TransactionResult.Failure("You are not authorized to match bank transactions.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var transaction = await db.BankStatementTransactions.SingleOrDefaultAsync(item => item.Id == request.BankStatementTransactionId && item.CompanyId == companyId, cancellationToken);
        if (transaction is null || transaction.Status != "Unmatched") return TransactionResult.Failure("Only an unmatched bank transaction can be matched.");
        if (await db.BankStatementTransactions.AnyAsync(item => item.CompanyId == companyId && item.MatchedJournalEntryId == request.JournalEntryId, cancellationToken)) return TransactionResult.Failure("That journal entry is already matched to a statement transaction.");
        var bank = await db.BankAccounts.SingleAsync(item => item.Id == transaction.BankAccountId && item.CompanyId == companyId, cancellationToken);
        var entry = await db.JournalEntries.SingleOrDefaultAsync(item => item.Id == request.JournalEntryId && item.CompanyId == companyId && item.BankAccountId == bank.Id && item.IsPosted && item.Status == "Posted", cancellationToken);
        if (entry is null) return TransactionResult.Failure("The journal entry is not posted to this bank account.");
        var bankLines = await db.JournalEntryLines.Where(line => line.JournalEntryId == entry.Id && line.AccountId == bank.LedgerAccountId).ToListAsync(cancellationToken);
        var signedAmount = bankLines.Sum(line => line.Debit - line.Credit);
        if (RoundCurrency(signedAmount) != RoundCurrency(transaction.Amount)) return TransactionResult.Failure("The statement amount does not equal the journal's bank-account amount.");
        transaction.Status = "Matched"; transaction.MatchedJournalEntryId = entry.Id; transaction.MatchedAtUtc = DateTimeOffset.UtcNow; transaction.MatchedByUserId = ResolveUserId(); transaction.MatchNote = (request.Note ?? string.Empty).Trim(); transaction.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddBankAudit(db, companyId, "bank-transaction.matched", transaction.Id, new { entry.Id, signedAmount, transaction.MatchNote });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The bank transaction changed while it was being matched."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The bank transaction or journal was matched concurrently. Refresh and try again."); }
        return TransactionResult.Success(transaction.Id);
    }

    public async Task<TransactionResult> UnmatchBankTransactionAsync(Guid bankStatementTransactionId, string reason, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage)) return TransactionResult.Failure("You are not authorized to unmatch bank transactions.");
        if (string.IsNullOrWhiteSpace(reason)) return TransactionResult.Failure("An unmatch reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var item = await db.BankStatementTransactions.SingleOrDefaultAsync(transaction => transaction.Id == bankStatementTransactionId && transaction.CompanyId == companyId, cancellationToken);
        if (item is null || item.Status != "Matched" || !item.MatchedJournalEntryId.HasValue) return TransactionResult.Failure("Only a matched bank transaction can be unmatched.");
        if (await IsInCompletedReconciliationAsync(db, item.MatchedJournalEntryId.Value, cancellationToken)) return TransactionResult.Failure("A reconciled match cannot be removed until the reconciliation is reopened.");
        var oldJournalId = item.MatchedJournalEntryId; item.Status = "Unmatched"; item.MatchedJournalEntryId = null; item.MatchedAtUtc = null; item.MatchedByUserId = null; item.MatchNote = string.Empty; item.ConcurrencyToken = Guid.NewGuid().ToString("N"); AddBankAudit(db, companyId, "bank-transaction.unmatched", item.Id, new { oldJournalId, reason = reason.Trim() });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The bank transaction changed while it was being unmatched. Refresh and try again."); }
        return TransactionResult.Success(item.Id);
    }

    public async Task<TransactionResult> CreateBankTransferAsync(CreateBankTransferRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage)) return TransactionResult.Failure("You are not authorized to transfer funds.");
        if (request.FromBankAccountId == request.ToBankAccountId || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Memo)) return TransactionResult.Failure("A transfer requires different bank accounts, a positive amount, reference, and memo.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.BankTransfers.AnyAsync(item => item.CompanyId == companyId && item.Reference == request.Reference.Trim(), cancellationToken)) return TransactionResult.Failure("That transfer reference already exists.");
        var banks = await db.BankAccounts.Where(item => item.CompanyId == companyId && (item.Id == request.FromBankAccountId || item.Id == request.ToBankAccountId)).ToListAsync(cancellationToken);
        if (banks.Count != 2) return TransactionResult.Failure("Both transfer bank accounts must belong to the active company.");
        var from = banks.Single(item => item.Id == request.FromBankAccountId); var to = banks.Single(item => item.Id == request.ToBankAccountId); var amount = RoundCurrency(request.Amount);
        if (from.CurrentBalance < amount) return TransactionResult.Failure("The source bank account does not have sufficient book balance.");
        var fromAccount = await ResolveBankLedgerAccountNumberAsync(db, companyId, from, cancellationToken); var toAccount = await ResolveBankLedgerAccountNumberAsync(db, companyId, to, cancellationToken);
        if (string.IsNullOrWhiteSpace(fromAccount) || string.IsNullOrWhiteSpace(toAccount)) return TransactionResult.Failure("Both bank accounts require active ledger mappings.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var transferId = Guid.NewGuid();
        var outbound = await PostAsync(db, companyId, request.TransferDate, "Banking", $"{request.Reference.Trim()}-OUT", request.Memo, [new("1050", amount, 0, "Transfer clearing"), new(fromAccount, 0, amount, "Transfer out")], cancellationToken, from.Id, false, transferId, "BankTransferOutbound");
        if (!outbound.Succeeded) return outbound;
        var inbound = await PostAsync(db, companyId, request.TransferDate, "Banking", $"{request.Reference.Trim()}-IN", request.Memo, [new(toAccount, amount, 0, "Transfer in"), new("1050", 0, amount, "Transfer clearing")], cancellationToken, to.Id, false, transferId, "BankTransferInbound");
        if (!inbound.Succeeded) return inbound;
        from.CurrentBalance -= amount; to.CurrentBalance += amount; from.UnreconciledAmount += amount; to.UnreconciledAmount += amount; from.ConcurrencyToken = Guid.NewGuid().ToString("N"); to.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var transferRecord = new BankTransfer { Id = transferId, CompanyId = companyId, FromBankAccountId = from.Id, ToBankAccountId = to.Id, TransferDate = request.TransferDate, Amount = amount, Reference = request.Reference.Trim(), Memo = request.Memo.Trim(), JournalEntryId = outbound.Id!.Value, InboundJournalEntryId = inbound.Id!.Value, CreatedByUserId = ResolveUserId(), CreatedAtUtc = DateTimeOffset.UtcNow, Status = "Posted", ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.BankTransfers.Add(transferRecord); AddBankAudit(db, companyId, "bank-transfer.posted", transferId, new { fromBankAccountId = from.Id, toBankAccountId = to.Id, amount, outbound = outbound.Id, inbound = inbound.Id });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return TransactionResult.Failure("The transfer changed concurrently or its reference already exists."); }
        await transaction.CommitAsync(cancellationToken); return TransactionResult.Success(transferId);
    }

    public async Task<TransactionResult> ReverseBankTransferAsync(ReverseBankTransferRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage) || !HasPermission(BrassLedgerPermissions.JournalReverse)) return TransactionResult.Failure("You are not authorized to reverse bank transfers.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A transfer reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var transfer = await db.BankTransfers.SingleOrDefaultAsync(item => item.Id == request.BankTransferId && item.CompanyId == companyId, cancellationToken);
        if (transfer is null || transfer.Status != "Posted") return TransactionResult.Failure("Only a posted bank transfer can be reversed.");
        if (request.ReversalDate < transfer.TransferDate) return TransactionResult.Failure("The reversal date cannot precede the transfer date.");
        if (await IsInCompletedReconciliationAsync(db, transfer.JournalEntryId, cancellationToken) || await IsInCompletedReconciliationAsync(db, transfer.InboundJournalEntryId, cancellationToken)) return TransactionResult.Failure("A reconciled bank transfer cannot be reversed until both affected reconciliations are reopened.");

        var banks = await db.BankAccounts.Where(item => item.CompanyId == companyId && (item.Id == transfer.FromBankAccountId || item.Id == transfer.ToBankAccountId)).ToListAsync(cancellationToken);
        if (banks.Count != 2) return TransactionResult.Failure("Both transfer bank accounts must still belong to the active company.");
        var from = banks.Single(item => item.Id == transfer.FromBankAccountId);
        var to = banks.Single(item => item.Id == transfer.ToBankAccountId);
        var fromAccount = await ResolveBankLedgerAccountNumberAsync(db, companyId, from, cancellationToken);
        var toAccount = await ResolveBankLedgerAccountNumberAsync(db, companyId, to, cancellationToken);
        if (string.IsNullOrWhiteSpace(fromAccount) || string.IsNullOrWhiteSpace(toAccount)) return TransactionResult.Failure("Both bank accounts require active ledger mappings.");

        var outboundReversal = await PostAsync(db, companyId, request.ReversalDate, "Banking", $"REV-{transfer.Reference}-OUT", request.Reason,
            [new(fromAccount, transfer.Amount, 0, "Reverse transfer out"), new("1050", 0, transfer.Amount, "Reverse transfer clearing")],
            cancellationToken, from.Id, false, transfer.Id, "BankTransferOutboundReversal");
        if (!outboundReversal.Succeeded) return outboundReversal;
        var inboundReversal = await PostAsync(db, companyId, request.ReversalDate, "Banking", $"REV-{transfer.Reference}-IN", request.Reason,
            [new("1050", transfer.Amount, 0, "Reverse transfer clearing"), new(toAccount, 0, transfer.Amount, "Reverse transfer in")],
            cancellationToken, to.Id, false, transfer.Id, "BankTransferInboundReversal");
        if (!inboundReversal.Succeeded) return inboundReversal;

        var journalIds = new[] { transfer.JournalEntryId, transfer.InboundJournalEntryId, outboundReversal.Id!.Value, inboundReversal.Id!.Value };
        var journals = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && journalIds.Contains(entry.Id)).ToDictionaryAsync(entry => entry.Id, cancellationToken);
        journals[transfer.JournalEntryId].Status = "Reversed";
        journals[transfer.JournalEntryId].ReversedByJournalEntryId = outboundReversal.Id;
        journals[transfer.InboundJournalEntryId].Status = "Reversed";
        journals[transfer.InboundJournalEntryId].ReversedByJournalEntryId = inboundReversal.Id;
        journals[outboundReversal.Id.Value].ReversalOfJournalEntryId = transfer.JournalEntryId;
        journals[inboundReversal.Id.Value].ReversalOfJournalEntryId = transfer.InboundJournalEntryId;
        foreach (var journal in journals.Values) journal.ConcurrencyToken = Guid.NewGuid().ToString("N");

        from.CurrentBalance += transfer.Amount;
        to.CurrentBalance -= transfer.Amount;
        from.UnreconciledAmount += transfer.Amount;
        to.UnreconciledAmount += transfer.Amount;
        from.ConcurrencyToken = Guid.NewGuid().ToString("N");
        to.ConcurrencyToken = Guid.NewGuid().ToString("N");
        transfer.Status = "Reversed";
        transfer.ReversalJournalEntryId = outboundReversal.Id;
        transfer.InboundReversalJournalEntryId = inboundReversal.Id;
        transfer.ReversedByUserId = ResolveUserId();
        transfer.ReversedAtUtc = DateTimeOffset.UtcNow;
        transfer.ReversalDate = request.ReversalDate;
        transfer.ReversalReason = request.Reason.Trim();
        transfer.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddBankAudit(db, companyId, "bank-transfer.reversed", transfer.Id, new { transfer.ReversalDate, transfer.ReversalReason, transfer.ReversalJournalEntryId, transfer.InboundReversalJournalEntryId });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The transfer changed during reversal. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(transfer.Id);
    }

    public async Task<TransactionResult> CreateReconciliationAdjustmentAsync(CreateReconciliationAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage)) return TransactionResult.Failure("You are not authorized to create reconciliation adjustments.");
        if (request.Amount == 0 || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Description)) return TransactionResult.Failure("A non-zero amount, reference, and description are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.SubledgerAdjustments.AnyAsync(item => item.CompanyId == companyId && item.Subledger == "Banking" && item.Reference == request.Reference.Trim(), cancellationToken)) return TransactionResult.Failure("That reconciliation adjustment reference already exists.");
        var bank = await db.BankAccounts.SingleOrDefaultAsync(item => item.Id == request.BankAccountId && item.CompanyId == companyId, cancellationToken); if (bank is null) return TransactionResult.Failure("Bank account not found.");
        var cash = await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken); var offset = await db.Accounts.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Number == request.OffsetAccountNumber.Trim() && item.IsActive && !item.IsControlAccount, cancellationToken);
        if (string.IsNullOrWhiteSpace(cash) || offset is null || offset.Number == cash) return TransactionResult.Failure("Select a valid non-control offset account different from the bank account.");
        var amount = RoundCurrency(decimal.Abs(request.Amount)); var increase = request.Amount > 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var adjustmentId = Guid.NewGuid();
        var posting = await PostAsync(db, companyId, request.AdjustmentDate, "Banking", request.Reference, request.Description, increase ? [new(cash, amount, 0, "Bank adjustment"), new(offset.Number, 0, amount, "Reconciliation offset")] : [new(offset.Number, amount, 0, "Reconciliation offset"), new(cash, 0, amount, "Bank adjustment")], cancellationToken, bank.Id, sourceDocumentId: adjustmentId, sourceDocumentType: "BankReconciliationAdjustment");
        if (!posting.Succeeded) return posting;
        bank.CurrentBalance += request.Amount;
        bank.UnreconciledAmount += amount;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        var adjustment = CreateAdjustment(adjustmentId, companyId, "Banking", "BankReconciliationAdjustment", bank.Id, null, null, bank.Id, request.AdjustmentDate, RoundCurrency(request.Amount), request.Reference, request.Description, offset.Number, posting.Id!.Value);
        db.SubledgerAdjustments.Add(adjustment);
        AddAdjustmentAudit(db, adjustment, "bank-reconciliation.adjustment-posted");
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return TransactionResult.Failure("The reconciliation adjustment changed concurrently or its reference already exists."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(adjustment.Id);
    }

    public async Task<TransactionResult> ReverseReconciliationAdjustmentAsync(ReverseReconciliationAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage) || !HasPermission(BrassLedgerPermissions.JournalReverse)) return TransactionResult.Failure("You are not authorized to reverse reconciliation adjustments.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("An adjustment reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var adjustment = await db.SubledgerAdjustments.SingleOrDefaultAsync(item => item.Id == request.AdjustmentId && item.CompanyId == companyId && item.Subledger == "Banking" && item.Kind == "BankReconciliationAdjustment", cancellationToken);
        if (adjustment is null || adjustment.Status != "Posted") return TransactionResult.Failure("Only a posted reconciliation adjustment can be reversed.");
        if (request.ReversalDate < adjustment.AdjustmentDate) return TransactionResult.Failure("The reversal date cannot precede the adjustment date.");
        if (await IsInCompletedReconciliationAsync(db, adjustment.JournalEntryId, cancellationToken)) return TransactionResult.Failure("A reconciled adjustment cannot be reversed until its reconciliation is reopened.");
        var reversal = await PostInverseAsync(db, companyId, adjustment.JournalEntryId, request.ReversalDate, $"REV-{adjustment.Reference}", request.Reason, adjustment.Id, "BankReconciliationAdjustmentReversal", adjustment.BankAccountId, cancellationToken);
        if (!reversal.Succeeded) return reversal;
        var bank = await db.BankAccounts.SingleAsync(item => item.Id == adjustment.BankAccountId && item.CompanyId == companyId, cancellationToken);
        bank.CurrentBalance -= adjustment.Amount;
        bank.UnreconciledAmount += decimal.Abs(adjustment.Amount);
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        adjustment.Status = "Reversed";
        adjustment.ReversalJournalEntryId = reversal.Id;
        adjustment.ReversedByUserId = ResolveUserId();
        adjustment.ReversedAtUtc = DateTimeOffset.UtcNow;
        adjustment.ReversalReason = request.Reason.Trim();
        adjustment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddAdjustmentAudit(db, adjustment, "bank-reconciliation.adjustment-reversed");
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The adjustment changed during reversal. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(adjustment.Id);
    }

    public async Task<TransactionResult> ReopenBankReconciliationAsync(ReopenBankReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage) || !HasPermission(BrassLedgerPermissions.JournalReverse)) return TransactionResult.Failure("You are not authorized to reopen bank reconciliations.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A reopen reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var reconciliation = await db.BankReconciliations.SingleOrDefaultAsync(item => item.Id == request.ReconciliationId && item.CompanyId == companyId, cancellationToken); if (reconciliation is null || reconciliation.Status != "Completed") return TransactionResult.Failure("Only a completed reconciliation can be reopened.");
        if (await db.BankReconciliations.AnyAsync(item => item.BankAccountId == reconciliation.BankAccountId && item.Status == "Completed" && item.StatementDate > reconciliation.StatementDate, cancellationToken)) return TransactionResult.Failure("Reopen later reconciliations first.");
        var bank = await db.BankAccounts.SingleAsync(item => item.Id == reconciliation.BankAccountId && item.CompanyId == companyId, cancellationToken); var previous = await db.BankReconciliations.Where(item => item.BankAccountId == bank.Id && item.Status == "Completed" && item.StatementDate < reconciliation.StatementDate).OrderByDescending(item => item.StatementDate).FirstOrDefaultAsync(cancellationToken);
        reconciliation.Status = "Reopened"; reconciliation.ReopenedByUserId = ResolveUserId(); reconciliation.ReopenedAtUtc = DateTimeOffset.UtcNow; reconciliation.ReopenReason = request.Reason.Trim(); reconciliation.ConcurrencyToken = Guid.NewGuid().ToString("N"); bank.LastReconciledOn = previous?.StatementDate ?? DateOnly.MinValue; bank.LastReconciledBalance = previous?.StatementClosingBalance ?? 0; bank.UnreconciledAmount = decimal.Abs(bank.CurrentBalance - bank.LastReconciledBalance); bank.ConcurrencyToken = Guid.NewGuid().ToString("N"); AddBankAudit(db, companyId, "bank-reconciliation.reopened", reconciliation.Id, new { reconciliation.StatementDate, reconciliation.ReopenReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The reconciliation changed while it was being reopened. Refresh and try again."); }
        return TransactionResult.Success(reconciliation.Id);
    }

    public async Task<TransactionResult> PostPayrollRunAsync(PostPayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPost)) return TransactionResult.Failure("You are not authorized to post payroll runs.");
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
        if (!HasPermission(BrassLedgerPermissions.PayrollPrepare)) return null;
        if (request.Employees.Count == 0 || request.Employees.Any(line => line.EmployeeId == Guid.Empty) || request.Employees.Select(line => line.EmployeeId).Distinct().Count() != request.Employees.Count)
            return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var expansion = await ExpandApprovedTimecardsAsync(db, companyId, request, cancellationToken);
        if (expansion.Request is null || expansion.Request.Employees.Any(line => ResolveGrossPay(line) <= 0)) return null;
        return await CalculateEmployeePayrollAsync(db, companyId, expansion.Request, cancellationToken);
    }

    public async Task<TransactionResult> PostEmployeePayrollRunAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPrepare) || !HasPermission(BrassLedgerPermissions.PayrollApprove) || !HasPermission(BrassLedgerPermissions.PayrollPost))
            return TransactionResult.Failure("You are not authorized to prepare, approve, and post payroll in one operation. Use the separated payroll workflow.");
        var draft = await SaveEmployeePayrollRunDraftAsync(request, cancellationToken);
        if (!draft.Succeeded) return draft;
        var token = await GetPayrollRunConcurrencyTokenAsync(draft.Id!.Value, cancellationToken);
        var approval = await ApprovePayrollRunAsync(new ApprovePayrollRunRequest(draft.Id.Value, token), cancellationToken);
        if (!approval.Succeeded) return approval;
        token = await GetPayrollRunConcurrencyTokenAsync(draft.Id.Value, cancellationToken);
        return await PostApprovedPayrollRunAsync(new PostApprovedPayrollRunRequest(draft.Id.Value, token), cancellationToken);
    }

    public async Task<TransactionResult> SaveEmployeePayrollRunDraftAsync(PostEmployeePayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPrepare)) return TransactionResult.Failure("You are not authorized to prepare payroll runs.");
        if (string.IsNullOrWhiteSpace(request.Reference)) return TransactionResult.Failure("A payroll run reference is required.");
        if (request.Employees.Count == 0 || request.Employees.Any(line => line.EmployeeId == Guid.Empty) || request.Employees.Select(line => line.EmployeeId).Distinct().Count() != request.Employees.Count)
            return TransactionResult.Failure("Provide one unique employee for each payroll line.");
        if (request.Employees.Any(line => line.Earnings?.Any(earning => earning.Amount < 0 || earning.Hours < 0 || earning.Rate < 0) == true))
            return TransactionResult.Failure("Payroll earning amounts, hours, and rates cannot be negative.");
        if (request.Employees.Any(line => line.Deductions?.Any(deduction => deduction.EmployeeAmount < 0 || deduction.EmployerAmount < 0) == true))
            return TransactionResult.Failure("Payroll deduction amounts cannot be negative.");
        var periodStart = request.PeriodStart ?? request.PayDate;
        var periodEnd = request.PeriodEnd ?? request.PayDate;
        if (periodEnd < periodStart || request.PayDate < periodEnd) return TransactionResult.Failure("The payroll period must end on or before the pay date and cannot end before it starts.");
        var runType = request.RunType.Trim();
        if (runType is not ("Regular" or "OffCycle" or "Correction" or "Adjustment")) return TransactionResult.Failure("Payroll run type must be Regular, OffCycle, Correction, or Adjustment.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var expansion = await ExpandApprovedTimecardsAsync(db, companyId, request, cancellationToken);
        if (expansion.Request is null) return TransactionResult.Failure(expansion.ErrorMessage);
        var expandedRequest = expansion.Request;
        if (expandedRequest.Employees.Any(line => ResolveGrossPay(line) <= 0)) return TransactionResult.Failure("Provide positive earnings for each payroll employee, either directly or through an approved timecard.");
        var deductionLiabilityAccounts = expandedRequest.Employees.SelectMany(employee => employee.Deductions ?? []).Select(deduction => NormalizeLiabilityAccountNumber(deduction.LiabilityAccountNumber)).Append("2200").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var validLiabilityAccountCount = await db.Accounts.CountAsync(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Liability && deductionLiabilityAccounts.Contains(account.Number), cancellationToken);
        if (validLiabilityAccountCount != deductionLiabilityAccounts.Length) return TransactionResult.Failure("Every payroll deduction liability account must be an active liability account in this company.");
        if (await db.PayrollRuns.AnyAsync(run => run.CompanyId == companyId && run.Reference == request.Reference.Trim(), cancellationToken))
            return TransactionResult.Failure("Payroll run reference already exists.");
        var estimate = await CalculateEmployeePayrollAsync(db, companyId, expandedRequest, cancellationToken);
        if (estimate is null) return TransactionResult.Failure("Each payroll employee must be active and have applicable effective Federal or work-state tax profiles.");
        var runEmployees = await db.Employees.Where(employee => employee.CompanyId == companyId && estimate.Employees.Select(line => line.EmployeeId).Contains(employee.Id)).ToDictionaryAsync(employee => employee.Id, cancellationToken);
        foreach (var estimateLine in estimate.Employees)
        {
            var input = expandedRequest.Employees.Single(candidate => candidate.EmployeeId == estimateLine.EmployeeId);
            var deductions = ResolvePayrollDeductions(input, runEmployees[estimateLine.EmployeeId]);
            if (RoundCurrency(deductions.Where(deduction => deduction.IsPreTax).Sum(deduction => deduction.EmployeeAmount)) != estimateLine.PreTaxDeductions || RoundCurrency(deductions.Where(deduction => !deduction.IsPreTax).Sum(deduction => deduction.EmployeeAmount)) != estimateLine.PostTaxDeductions)
                return TransactionResult.Failure($"Payroll deductions for {estimateLine.EmployeeName} exceed available gross or net pay. Reduce them before saving the draft.");
        }
        var bank = await db.BankAccounts.SingleOrDefaultAsync(account => account.Id == request.BankAccountId && account.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Payroll funding account not found.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var contentSnapshot = await db.TaxContentPackages.Where(package => package.CompanyId == companyId && package.Status == "Approved" && package.EffectiveOn <= request.PayDate).OrderBy(package => package.PackageCode).ThenBy(package => package.Version).Select(package => new { package.PackageCode, package.Version, package.EffectiveOn, package.MinimumEngineVersion }).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var run = new PayrollRun { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = bank.Id, PayDate = request.PayDate, PeriodStart = periodStart, PeriodEnd = periodEnd, RunType = runType, Status = "Draft", Reference = request.Reference.Trim(), GrossPayroll = estimate.GrossPayroll, PreTaxDeductions = estimate.PreTaxDeductions, EmployeeWithholdings = estimate.EmployeeWithholdings, PostTaxDeductions = estimate.PostTaxDeductions, EmployerPayrollTaxes = estimate.EmployerPayrollTaxes, EmployerBenefitContributions = estimate.EmployerBenefitContributions, NetPay = estimate.NetPay, PreparedByUserId = ResolveUserId(), PreparedAtUtc = now, TaxContentSnapshotJson = System.Text.Json.JsonSerializer.Serialize(contentSnapshot), ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.PayrollRuns.Add(run);
        foreach (var estimateLine in estimate.Employees)
        {
            var employee = runEmployees[estimateLine.EmployeeId];
            var input = expandedRequest.Employees.Single(candidate => candidate.EmployeeId == estimateLine.EmployeeId);
            var line = new PayrollRunEmployeeLine { Id = Guid.NewGuid(), PayrollRunId = run.Id, EmployeeId = estimateLine.EmployeeId, WorkState = estimateLine.WorkState, WorkCity = employee.WorkCity, ResidenceState = string.IsNullOrWhiteSpace(employee.ResidenceState) ? employee.State : employee.ResidenceState, ResidenceCity = employee.ResidenceCity, FilingStatus = estimateLine.FilingStatus, PayrollFrequency = employee.PayrollFrequency, GrossPay = estimateLine.GrossPay, TaxableWages = estimateLine.GrossPay - estimateLine.PreTaxDeductions, YearToDateGrossBefore = estimateLine.YearToDateGrossBefore, YearToDateGrossAfter = estimateLine.YearToDateGrossBefore + estimateLine.GrossPay, PreTaxDeductions = estimateLine.PreTaxDeductions, EmployeeWithholdings = estimateLine.EmployeeWithholdings, PostTaxDeductions = estimateLine.PostTaxDeductions, EmployerPayrollTaxes = estimateLine.EmployerPayrollTaxes, EmployerBenefitContributions = estimateLine.EmployerBenefitContributions, NetPay = estimateLine.NetPay };
            db.PayrollRunEmployeeLines.Add(line);
            var earnings = input.Earnings is { Count: > 0 } ? input.Earnings : [new PayrollEarningInput("REGULAR", "Regular", 0, 0, estimateLine.GrossPay, true, null, employee.State, employee.WorkCounty, employee.WorkCity, employee.WorkSchoolDistrict)];
            db.PayrollEarningLines.AddRange(earnings.Select((earning, index) => new PayrollEarningLine { Id = Guid.NewGuid(), PayrollRunEmployeeLineId = line.Id, PayrollTimeEntryId = earning.SourceTimeEntryId, Sequence = index + 1, EarningCode = earning.EarningCode.Trim(), EarningType = earning.EarningType.Trim(), Hours = earning.Hours, Rate = earning.Rate, Amount = RoundCurrency(earning.Amount), IsTaxable = earning.IsTaxable, WorkedOn = earning.WorkedOn, WorkState = string.IsNullOrWhiteSpace(earning.WorkState) ? employee.State : earning.WorkState.Trim(), WorkCounty = string.IsNullOrWhiteSpace(earning.WorkCounty) ? employee.WorkCounty : earning.WorkCounty.Trim(), WorkCity = string.IsNullOrWhiteSpace(earning.WorkCity) ? employee.WorkCity : earning.WorkCity.Trim(), WorkSchoolDistrict = string.IsNullOrWhiteSpace(earning.WorkSchoolDistrict) ? employee.WorkSchoolDistrict : earning.WorkSchoolDistrict.Trim() }));
            var deductions = ResolvePayrollDeductions(input, employee);
            db.PayrollDeductionLines.AddRange(deductions.Select((deduction, index) => new PayrollDeductionLine { Id = Guid.NewGuid(), PayrollRunEmployeeLineId = line.Id, Sequence = index + 1, DeductionCode = deduction.DeductionCode.Trim(), DeductionType = deduction.DeductionType.Trim(), EmployeeAmount = RoundCurrency(deduction.EmployeeAmount), EmployerAmount = RoundCurrency(deduction.EmployerAmount), IsPreTax = deduction.IsPreTax, ExemptFromFederalIncomeTax = deduction.ExemptFromFederalIncomeTax, ExemptFromFica = deduction.ExemptFromFica, ExemptFromFuta = deduction.ExemptFromFuta, LiabilityAccountNumber = NormalizeLiabilityAccountNumber(deduction.LiabilityAccountNumber) }));
            db.PayrollTaxLines.AddRange((estimateLine.Taxes ?? []).Select((tax, index) => new PayrollTaxLine { Id = Guid.NewGuid(), PayrollRunEmployeeLineId = line.Id, Sequence = index + 1, ObligationCode = tax.ObligationCode, JurisdictionCode = tax.JurisdictionCode, JurisdictionName = tax.JurisdictionName, TaxType = tax.TaxType, TaxableWages = tax.TaxableWages, YearToDateTaxableWagesBefore = tax.YearToDateTaxableWagesBefore, EmployeeAmount = tax.EmployeeAmount, EmployerAmount = tax.EmployerAmount, TaxRuleSetId = tax.TaxRuleSetId, TaxContentPackageId = tax.TaxContentPackageId, ContentVersion = tax.ContentVersion, Source = tax.Source, CalculationTraceJson = tax.CalculationTraceJson }));
        }
        foreach (var timecard in expansion.Timecards)
        {
            timecard.Status = "Consumed";
            timecard.PayrollRunId = run.Id;
            timecard.ConcurrencyToken = Guid.NewGuid().ToString("N");
            AddTimecardAudit(db, companyId, "payroll-timecard.consumed", timecard, new { payrollRunId = run.Id, run.Reference });
        }
        AddPayrollAudit(db, companyId, "payroll-run.prepared", run, new { run.PeriodStart, run.PeriodEnd, run.RunType, employeeCount = estimate.Employees.Count, run.GrossPayroll, run.NetPay });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("An approved timecard or payroll record changed while the draft was being prepared. Refresh and review the payroll again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("Payroll draft could not be saved because the reference was already used, a time entry was already consumed, or its data changed. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(run.Id);
    }

    public async Task<TransactionResult> ApprovePayrollRunAsync(ApprovePayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollApprove)) return TransactionResult.Failure("You are not authorized to approve payroll runs.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var run = await db.PayrollRuns.SingleOrDefaultAsync(candidate => candidate.Id == request.PayrollRunId && candidate.CompanyId == companyId, cancellationToken);
        if (run is null) return TransactionResult.Failure("Payroll run not found.");
        if (run.Status != "Draft") return TransactionResult.Failure("Only a draft payroll run can be approved.");
        if (!string.Equals(run.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll run changed after it was opened. Refresh and review it again.");
        run.Status = "Approved"; run.ApprovedByUserId = ResolveUserId(); run.ApprovedAtUtc = DateTimeOffset.UtcNow; run.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPayrollAudit(db, companyId, "payroll-run.approved", run, new { run.GrossPayroll, run.EmployeeWithholdings, run.NetPay });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payroll run changed while it was being approved. Refresh and try again."); }
        return TransactionResult.Success(run.Id);
    }

    public async Task<TransactionResult> PostApprovedPayrollRunAsync(PostApprovedPayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPost)) return TransactionResult.Failure("You are not authorized to post payroll runs.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var run = await db.PayrollRuns.SingleOrDefaultAsync(candidate => candidate.Id == request.PayrollRunId && candidate.CompanyId == companyId, cancellationToken);
        if (run is null) return TransactionResult.Failure("Payroll run not found.");
        if (run.Status != "Approved") return TransactionResult.Failure("Only an approved payroll run can be posted.");
        if (!string.Equals(run.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll run changed after it was approved. Refresh and review it again.");
        var bank = await db.BankAccounts.SingleOrDefaultAsync(account => account.Id == run.BankAccountId && account.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Payroll funding account not found.");
        if (bank.CurrentBalance < run.NetPay) return TransactionResult.Failure("Payroll funding account does not have sufficient book balance for net pay.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var employeeLines = await db.PayrollRunEmployeeLines.Where(line => line.PayrollRunId == run.Id).ToListAsync(cancellationToken);
        var employeeLineIds = employeeLines.Select(line => line.Id).ToArray();
        var taxLines = await db.PayrollTaxLines.Where(line => employeeLineIds.Contains(line.PayrollRunEmployeeLineId)).ToListAsync(cancellationToken);
        var deductionLines = await db.PayrollDeductionLines.Where(line => employeeLineIds.Contains(line.PayrollRunEmployeeLineId)).ToListAsync(cancellationToken);
        if (await db.PayrollLiabilities.AnyAsync(liability => liability.PayrollRunId == run.Id, cancellationToken)) return TransactionResult.Failure("Payroll liabilities were already created for this run.");
        var liabilityAmounts = taxLines.Where(line => line.EmployeeAmount + line.EmployerAmount > 0).Select(line => new { AccountNumber = "2200", Amount = line.EmployeeAmount + line.EmployerAmount })
            .Concat(deductionLines.Where(line => line.EmployeeAmount + line.EmployerAmount > 0).Select(line => new { AccountNumber = NormalizeLiabilityAccountNumber(line.LiabilityAccountNumber), Amount = line.EmployeeAmount + line.EmployerAmount }))
            .ToArray();
        var expectedLiabilities = RoundCurrency(run.PreTaxDeductions + run.EmployeeWithholdings + run.PostTaxDeductions + run.EmployerPayrollTaxes + run.EmployerBenefitContributions);
        if (RoundCurrency(liabilityAmounts.Sum(item => item.Amount)) != expectedLiabilities) return TransactionResult.Failure("Payroll tax and deduction details do not reconcile to the run liabilities. Cancel and recalculate the draft.");
        var postingLines = new List<JournalLineRequest> { new("6100", run.GrossPayroll + run.EmployerPayrollTaxes + run.EmployerBenefitContributions, 0, "Gross payroll, employer taxes, and employer benefit contributions"), new(await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken), 0, run.NetPay, "Net payroll funding") };
        postingLines.AddRange(liabilityAmounts.GroupBy(item => item.AccountNumber, StringComparer.OrdinalIgnoreCase).Select(group => new JournalLineRequest(group.Key, 0, RoundCurrency(group.Sum(item => item.Amount)), "Payroll liabilities")));
        var posting = await PostAsync(db, companyId, run.PayDate, "Payroll", run.Reference, "Employee payroll run", postingLines, cancellationToken, bank.Id, allowControlAccounts: true, sourceDocumentId: run.Id, sourceDocumentType: "PayrollRun");
        if (!posting.Succeeded) return posting;
        db.PayrollLiabilities.AddRange(taxLines.Where(line => line.EmployeeAmount + line.EmployerAmount > 0).Select(line => new PayrollLiability { Id = Guid.NewGuid(), CompanyId = companyId, PayrollRunId = run.Id, PayrollRunEmployeeLineId = line.PayrollRunEmployeeLineId, SourceType = "Tax", SourceLineId = line.Id, ObligationCode = line.ObligationCode, JurisdictionCode = line.JurisdictionCode, JurisdictionName = line.JurisdictionName, Description = line.TaxType, LiabilityAccountNumber = "2200", OriginalAmount = RoundCurrency(line.EmployeeAmount + line.EmployerAmount), OutstandingAmount = RoundCurrency(line.EmployeeAmount + line.EmployerAmount), Status = "Open", ConcurrencyToken = Guid.NewGuid().ToString("N") }));
        db.PayrollLiabilities.AddRange(deductionLines.Where(line => line.EmployeeAmount + line.EmployerAmount > 0).Select(line => new PayrollLiability { Id = Guid.NewGuid(), CompanyId = companyId, PayrollRunId = run.Id, PayrollRunEmployeeLineId = line.PayrollRunEmployeeLineId, SourceType = "Deduction", SourceLineId = line.Id, ObligationCode = line.DeductionCode, Description = line.DeductionType, LiabilityAccountNumber = NormalizeLiabilityAccountNumber(line.LiabilityAccountNumber), OriginalAmount = RoundCurrency(line.EmployeeAmount + line.EmployerAmount), OutstandingAmount = RoundCurrency(line.EmployeeAmount + line.EmployerAmount), Status = "Open", ConcurrencyToken = Guid.NewGuid().ToString("N") }));
        run.Status = "Posted"; run.JournalEntryId = posting.Id; run.PostedByUserId = ResolveUserId(); run.PostedAtUtc = DateTimeOffset.UtcNow; run.ConcurrencyToken = Guid.NewGuid().ToString("N");
        bank.CurrentBalance -= run.NetPay; bank.UnreconciledAmount += run.NetPay; bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPayrollAudit(db, companyId, "payroll-run.posted", run, new { run.JournalEntryId, run.GrossPayroll, run.NetPay, liabilityCount = liabilityAmounts.Length, liabilityTotal = expectedLiabilities });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payroll run or funding account changed while posting. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(run.Id);
    }

    public async Task<TransactionResult> CancelPayrollRunAsync(CancelPayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollReverse)) return TransactionResult.Failure("You are not authorized to cancel payroll runs.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A payroll cancellation reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var run = await db.PayrollRuns.SingleOrDefaultAsync(candidate => candidate.Id == request.PayrollRunId && candidate.CompanyId == companyId, cancellationToken);
        if (run is null) return TransactionResult.Failure("Payroll run not found.");
        if (run.Status != "Draft") return TransactionResult.Failure("Only a draft payroll run can be cancelled. Posted payroll must be reversed.");
        if (!string.Equals(run.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll run changed after it was opened. Refresh and review it again.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var timecards = await db.PayrollTimecards.Where(card => card.CompanyId == companyId && card.PayrollRunId == run.Id).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        run.Status = "Cancelled";
        run.CancelledByUserId = ResolveUserId();
        run.CancelledAtUtc = now;
        run.CancellationReason = request.Reason.Trim();
        run.ConcurrencyToken = Guid.NewGuid().ToString("N");
        foreach (var timecard in timecards)
        {
            timecard.Status = "Approved";
            timecard.PayrollRunId = null;
            timecard.ConcurrencyToken = Guid.NewGuid().ToString("N");
            AddTimecardAudit(db, companyId, "payroll-timecard.released", timecard, new { cancelledPayrollRunId = run.Id, run.Reference, run.CancellationReason });
        }
        AddPayrollAudit(db, companyId, "payroll-run.cancelled", run, new { run.CancellationReason, releasedTimecardCount = timecards.Count });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payroll run or one of its timecards changed while it was being cancelled. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(run.Id);
    }

    public async Task<TransactionResult> RecordPayrollLiabilityPaymentAsync(RecordPayrollLiabilityPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPost)) return TransactionResult.Failure("You are not authorized to remit payroll liabilities.");
        if (request.BankAccountId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Payee)) return TransactionResult.Failure("Select a funding account and enter the remittance reference and payee.");
        var method = request.Method?.Trim() ?? string.Empty;
        if (method is not ("EFT" or "ACH" or "Check" or "Wire" or "Other")) return TransactionResult.Failure("Payroll liability payment method must be EFT, ACH, Check, Wire, or Other.");
        var applications = request.Applications?.Select(application => application with { Amount = RoundCurrency(application.Amount) }).ToArray() ?? [];
        if (applications.Length == 0 || applications.Length > 500 || applications.Any(application => application.PayrollLiabilityId == Guid.Empty || application.Amount <= 0) || applications.Select(application => application.PayrollLiabilityId).Distinct().Count() != applications.Length)
            return TransactionResult.Failure("Select between 1 and 500 unique payroll liabilities with positive payment amounts.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await db.PayrollLiabilityPayments.AnyAsync(payment => payment.CompanyId == companyId && payment.Reference == request.Reference.Trim(), cancellationToken)) return TransactionResult.Failure("That payroll remittance reference already exists.");
        var bank = await db.BankAccounts.SingleOrDefaultAsync(account => account.Id == request.BankAccountId && account.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Payroll remittance funding account not found.");
        var liabilityIds = applications.Select(application => application.PayrollLiabilityId).ToArray();
        var liabilities = await db.PayrollLiabilities.Where(liability => liability.CompanyId == companyId && liabilityIds.Contains(liability.Id)).ToDictionaryAsync(liability => liability.Id, cancellationToken);
        if (liabilities.Count != applications.Length) return TransactionResult.Failure("Every selected payroll liability must belong to the active company.");
        var payrollRunIds = liabilities.Values.Select(liability => liability.PayrollRunId).Distinct().ToArray();
        if (await db.PayrollRuns.AnyAsync(run => run.CompanyId == companyId && payrollRunIds.Contains(run.Id) && run.PayDate > request.PaymentDate, cancellationToken)) return TransactionResult.Failure("A payroll liability payment cannot precede the pay date that created the obligation.");
        foreach (var application in applications)
        {
            var liability = liabilities[application.PayrollLiabilityId];
            if (liability.Status is not ("Open" or "PartiallyPaid") || application.Amount > liability.OutstandingAmount) return TransactionResult.Failure($"Payment for {liability.ObligationCode} exceeds its open balance or the liability is not payable.");
        }
        var amount = RoundCurrency(applications.Sum(application => application.Amount));
        if (bank.CurrentBalance < amount) return TransactionResult.Failure("The payroll remittance funding account does not have sufficient book balance.");
        var accountNumbers = liabilities.Values.Select(liability => liability.LiabilityAccountNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (await db.Accounts.CountAsync(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Liability && accountNumbers.Contains(account.Number), cancellationToken) != accountNumbers.Length)
            return TransactionResult.Failure("One or more payroll liability accounts are unavailable or no longer classified as liabilities.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var paymentId = Guid.NewGuid();
        var postingLines = applications.GroupBy(application => liabilities[application.PayrollLiabilityId].LiabilityAccountNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => new JournalLineRequest(group.Key, RoundCurrency(group.Sum(application => application.Amount)), 0, "Payroll liability remittance"))
            .Append(new JournalLineRequest(await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken), 0, amount, $"Payment to {request.Payee.Trim()}"))
            .ToArray();
        var posting = await PostAsync(db, companyId, request.PaymentDate, "Payroll", request.Reference, $"Payroll liability payment to {request.Payee.Trim()}", postingLines, cancellationToken, bank.Id, allowControlAccounts: true, sourceDocumentId: paymentId, sourceDocumentType: "PayrollLiabilityPayment");
        if (!posting.Succeeded) return posting;
        var now = DateTimeOffset.UtcNow;
        var payment = new PayrollLiabilityPayment { Id = paymentId, CompanyId = companyId, BankAccountId = bank.Id, PaymentDate = request.PaymentDate, Reference = request.Reference.Trim(), Payee = request.Payee.Trim(), Method = method, Amount = amount, Status = "Posted", JournalEntryId = posting.Id!.Value, CreatedByUserId = ResolveUserId(), CreatedAtUtc = now, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        db.PayrollLiabilityPayments.Add(payment);
        foreach (var application in applications)
        {
            var liability = liabilities[application.PayrollLiabilityId];
            liability.OutstandingAmount = RoundCurrency(liability.OutstandingAmount - application.Amount);
            liability.Status = liability.OutstandingAmount == 0 ? "Paid" : "PartiallyPaid";
            liability.ConcurrencyToken = Guid.NewGuid().ToString("N");
            db.PayrollLiabilityPaymentApplications.Add(new PayrollLiabilityPaymentApplication { Id = Guid.NewGuid(), PayrollLiabilityPaymentId = payment.Id, PayrollLiabilityId = liability.Id, Amount = RoundCurrency(application.Amount) });
        }
        bank.CurrentBalance -= amount; bank.UnreconciledAmount += amount; bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPayrollLiabilityPaymentAudit(db, companyId, "payroll-liability-payment.posted", payment, new { applicationCount = applications.Length, payment.Amount, payment.Payee, payment.Method, payment.JournalEntryId });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("A payroll liability or funding account changed while the remittance was posting. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The payroll remittance reference was already used or an application changed concurrently."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(payment.Id);
    }

    public async Task<TransactionResult> ReversePayrollLiabilityPaymentAsync(ReversePayrollLiabilityPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollReverse)) return TransactionResult.Failure("You are not authorized to reverse payroll liability payments.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A payroll remittance reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var payment = await db.PayrollLiabilityPayments.SingleOrDefaultAsync(candidate => candidate.Id == request.PaymentId && candidate.CompanyId == companyId, cancellationToken);
        if (payment is null) return TransactionResult.Failure("Payroll liability payment not found.");
        if (payment.Status != "Posted" || payment.ReversalJournalEntryId.HasValue) return TransactionResult.Failure("Only an unreversed posted payroll liability payment can be reversed.");
        if (!string.Equals(payment.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll liability payment changed after it was opened. Refresh and try again.");
        if (request.ReversalDate < payment.PaymentDate) return TransactionResult.Failure("A payroll remittance reversal cannot precede the payment date.");
        if (await IsInCompletedReconciliationAsync(db, payment.JournalEntryId, cancellationToken)) return TransactionResult.Failure("Reopen the bank reconciliation before reversing this payroll liability payment.");
        var applications = await db.PayrollLiabilityPaymentApplications.Where(application => application.PayrollLiabilityPaymentId == payment.Id).ToListAsync(cancellationToken);
        var liabilityIds = applications.Select(application => application.PayrollLiabilityId).ToArray();
        var liabilities = await db.PayrollLiabilities.Where(liability => liability.CompanyId == companyId && liabilityIds.Contains(liability.Id)).ToDictionaryAsync(liability => liability.Id, cancellationToken);
        if (liabilities.Count != applications.Count || applications.Any(application => liabilities[application.PayrollLiabilityId].Status == "Reversed" || liabilities[application.PayrollLiabilityId].OutstandingAmount + application.Amount > liabilities[application.PayrollLiabilityId].OriginalAmount))
            return TransactionResult.Failure("One or more applied payroll liabilities can no longer accept this payment reversal.");
        var bank = await db.BankAccounts.SingleAsync(account => account.Id == payment.BankAccountId && account.CompanyId == companyId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reversal = await PostInverseAsync(db, companyId, payment.JournalEntryId, request.ReversalDate, $"REV-{payment.Reference}", request.Reason.Trim(), payment.Id, "PayrollLiabilityPaymentReversal", bank.Id, cancellationToken, "Payroll");
        if (!reversal.Succeeded) return reversal;
        foreach (var application in applications)
        {
            var liability = liabilities[application.PayrollLiabilityId];
            liability.OutstandingAmount = RoundCurrency(liability.OutstandingAmount + application.Amount);
            liability.Status = liability.OutstandingAmount == liability.OriginalAmount ? "Open" : "PartiallyPaid";
            liability.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }
        payment.Status = "Reversed"; payment.ReversalJournalEntryId = reversal.Id; payment.ReversedByUserId = ResolveUserId(); payment.ReversedAtUtc = DateTimeOffset.UtcNow; payment.ReversalDate = request.ReversalDate; payment.ReversalReason = request.Reason.Trim(); payment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        bank.CurrentBalance += payment.Amount; bank.UnreconciledAmount += payment.Amount; bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPayrollLiabilityPaymentAudit(db, companyId, "payroll-liability-payment.reversed", payment, new { payment.ReversalJournalEntryId, payment.ReversalDate, payment.ReversalReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("A payroll liability, payment, or funding account changed while reversing. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(payment.Id);
    }

    public async Task<TransactionResult> ReversePayrollRunAsync(ReversePayrollRunRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollReverse)) return TransactionResult.Failure("You are not authorized to reverse payroll runs.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A payroll reversal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var run = await db.PayrollRuns.SingleOrDefaultAsync(candidate => candidate.Id == request.PayrollRunId && candidate.CompanyId == companyId, cancellationToken);
        if (run is null) return TransactionResult.Failure("Payroll run not found.");
        if (run.Status != "Posted" || !run.JournalEntryId.HasValue || run.ReversalJournalEntryId.HasValue) return TransactionResult.Failure("Only an unreversed posted payroll run can be reversed.");
        if (!string.Equals(run.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The payroll run changed after it was opened. Refresh and try again.");
        if (request.ReversalDate < run.PayDate) return TransactionResult.Failure("A payroll reversal cannot precede the original pay date.");
        if (await IsInCompletedReconciliationAsync(db, run.JournalEntryId.Value, cancellationToken)) return TransactionResult.Failure("Reopen the bank reconciliation before reversing this payroll run.");
        var liabilities = await db.PayrollLiabilities.Where(liability => liability.CompanyId == companyId && liability.PayrollRunId == run.Id).ToListAsync(cancellationToken);
        if (liabilities.Any(liability => liability.OutstandingAmount != liability.OriginalAmount)) return TransactionResult.Failure("Reverse every payment applied to this payroll run's liabilities before reversing the payroll run.");
        var original = await db.JournalEntries.SingleAsync(entry => entry.Id == run.JournalEntryId && entry.CompanyId == companyId, cancellationToken);
        var originalLines = await db.JournalEntryLines.Where(line => line.JournalEntryId == original.Id).ToListAsync(cancellationToken);
        var accountIds = originalLines.Select(line => line.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.Where(account => account.CompanyId == companyId && accountIds.Contains(account.Id)).ToDictionaryAsync(account => account.Id, cancellationToken);
        var bank = await db.BankAccounts.SingleAsync(account => account.Id == run.BankAccountId && account.CompanyId == companyId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reversingLines = originalLines.Select(line => new JournalLineRequest(accounts[line.AccountId].Number, line.Credit, line.Debit, $"Reversal: {line.Description}")).ToArray();
        var posting = await PostAsync(db, companyId, request.ReversalDate, "Payroll", $"REV-{run.Reference}", request.Reason.Trim(), reversingLines, cancellationToken, bank.Id, allowControlAccounts: true, sourceDocumentId: run.Id, sourceDocumentType: "PayrollRunReversal");
        if (!posting.Succeeded) return posting;
        var reversal = await db.JournalEntries.SingleAsync(entry => entry.Id == posting.Id, cancellationToken);
        reversal.ReversalOfJournalEntryId = original.Id; reversal.ConcurrencyToken = Guid.NewGuid().ToString("N");
        original.Status = "Reversed"; original.ReversedByJournalEntryId = reversal.Id; original.ConcurrencyToken = Guid.NewGuid().ToString("N");
        run.Status = "Reversed"; run.ReversalJournalEntryId = reversal.Id; run.ReversedByUserId = ResolveUserId(); run.ReversedAtUtc = DateTimeOffset.UtcNow; run.ReversalDate = request.ReversalDate; run.ReversalReason = request.Reason.Trim(); run.ConcurrencyToken = Guid.NewGuid().ToString("N");
        foreach (var liability in liabilities) { liability.Status = "Reversed"; liability.OutstandingAmount = 0; liability.ConcurrencyToken = Guid.NewGuid().ToString("N"); }
        bank.CurrentBalance += run.NetPay; bank.UnreconciledAmount += run.NetPay; bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPayrollAudit(db, companyId, "payroll-run.reversed", run, new { run.JournalEntryId, run.ReversalJournalEntryId, run.ReversalDate, run.ReversalReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The payroll run or funding account changed while reversing. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(run.Id);
    }

    public async Task<TransactionResult> SaveEmployeePayrollSetupAsync(SaveEmployeePayrollSetupRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollManage) || !HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to maintain protected employee payroll elections.");
        if (request.EmployeeId == Guid.Empty || request.Allowances < 0 || request.AdditionalWithholding < 0 || request.PreTaxBenefitDeductions < 0 || request.PostTaxBenefitDeductions < 0 || request.FederalStep3Credits < 0 || request.FederalStep4OtherIncome < 0 || request.FederalStep4Deductions < 0)
            return TransactionResult.Failure("Payroll elections and benefit deductions must be non-negative.");
        if (request.FederalFormW4Year is < 1987 or > 2026) return TransactionResult.Failure("Enter the year of the employee's valid Form W-4 (1987 through 2026).");
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
        employee.FederalFormW4Year = request.FederalFormW4Year;
        employee.FederalStep2MultipleJobs = request.FederalStep2MultipleJobs;
        employee.FederalStep3Credits = request.FederalStep3Credits;
        employee.FederalStep4OtherIncome = request.FederalStep4OtherIncome;
        employee.FederalStep4Deductions = request.FederalStep4Deductions;
        employee.FederalWithholdingExempt = request.FederalWithholdingExempt;
        employee.AdditionalWithholding = request.AdditionalWithholding;
        employee.PreTaxBenefitDeductions = request.PreTaxBenefitDeductions;
        employee.PostTaxBenefitDeductions = request.PostTaxBenefitDeductions;
        employee.ResidenceState = request.ResidenceState.Trim();
        employee.ResidenceCity = request.ResidenceCity.Trim();
        if (!string.IsNullOrWhiteSpace(request.WorkState)) employee.State = request.WorkState.Trim();
        employee.WorkCity = request.WorkCity.Trim();
        employee.ConcurrencyToken = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(cancellationToken);
        return TransactionResult.Success(employee.Id);
    }

    public async Task<TransactionResult> SaveEmployeeEmploymentDetailsAsync(SaveEmployeeEmploymentDetailsRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollSensitiveData)) return TransactionResult.Failure("You are not authorized to maintain protected employee details.");
        if (request.EmployeeId == Guid.Empty || request.HourlyRate < 0 || request.OvertimeRate < 0) return TransactionResult.Failure("Select an employee and provide non-negative pay rates.");
        if (request.EmploymentStartedOn is { } startDate && request.EmploymentEndedOn is { } endDate && endDate < startDate) return TransactionResult.Failure("Employment end date cannot precede the start date.");
        var socialSecurityNumber = NormalizeDigits(request.SocialSecurityNumber);
        var routingNumber = NormalizeDigits(request.BankRoutingNumber);
        var accountNumber = NormalizeDigits(request.BankAccountNumber);
        if (!string.IsNullOrWhiteSpace(request.SocialSecurityNumber) && socialSecurityNumber.Length != 9) return TransactionResult.Failure("A Social Security number must contain exactly nine digits.");
        if (!string.IsNullOrWhiteSpace(request.BankRoutingNumber) && (routingNumber.Length != 9 || !IsValidAbaRoutingNumber(routingNumber))) return TransactionResult.Failure("Enter a valid nine-digit ABA routing number.");
        if (!string.IsNullOrWhiteSpace(request.BankAccountNumber) && accountNumber.Length is < 4 or > 17) return TransactionResult.Failure("A bank account number must contain 4 to 17 digits.");
        var bankAccountType = request.BankAccountType.Trim();
        if (!string.IsNullOrWhiteSpace(bankAccountType) && bankAccountType is not ("Checking" or "Savings")) return TransactionResult.Failure("Bank account type must be Checking or Savings.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var employee = await db.Employees.SingleOrDefaultAsync(candidate => candidate.Id == request.EmployeeId && candidate.CompanyId == companyId, cancellationToken);
        if (employee is null) return TransactionResult.Failure("Employee not found.");
        if (!string.Equals(employee.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The employee record changed after it was opened. Refresh and review it again.");
        if (request.ClearSocialSecurityNumber) employee.SocialSecurityNumber = string.Empty;
        else if (!string.IsNullOrWhiteSpace(socialSecurityNumber)) employee.SocialSecurityNumber = socialSecurityNumber;
        if (request.ClearBankDetails)
        {
            employee.BankRoutingNumber = string.Empty; employee.BankAccountNumber = string.Empty; employee.BankAccountType = string.Empty;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(routingNumber)) employee.BankRoutingNumber = routingNumber;
            if (!string.IsNullOrWhiteSpace(accountNumber)) employee.BankAccountNumber = accountNumber;
            if (!string.IsNullOrWhiteSpace(bankAccountType)) employee.BankAccountType = bankAccountType;
        }
        if (request.DirectDepositEnabled && (string.IsNullOrWhiteSpace(employee.BankRoutingNumber) || string.IsNullOrWhiteSpace(employee.BankAccountNumber) || string.IsNullOrWhiteSpace(employee.BankAccountType)))
            return TransactionResult.Failure("Direct deposit requires a valid routing number, bank account number, and account type.");
        employee.AddressLine1 = request.AddressLine1.Trim(); employee.AddressLine2 = request.AddressLine2.Trim(); employee.PostalCode = request.PostalCode.Trim();
        employee.ResidenceCounty = request.ResidenceCounty.Trim(); employee.ResidenceSchoolDistrict = request.ResidenceSchoolDistrict.Trim(); employee.WorkCounty = request.WorkCounty.Trim(); employee.WorkSchoolDistrict = request.WorkSchoolDistrict.Trim();
        employee.EmploymentStartedOn = request.EmploymentStartedOn; employee.EmploymentEndedOn = request.EmploymentEndedOn; employee.HourlyRate = request.HourlyRate; employee.OvertimeRate = request.OvertimeRate; employee.DirectDepositEnabled = request.DirectDepositEnabled;
        employee.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = "employee.protected-details.updated", EntityType = "Employee", EntityId = employee.Id, DetailJson = System.Text.Json.JsonSerializer.Serialize(new { employee.EmploymentStartedOn, employee.EmploymentEndedOn, employee.ResidenceCounty, employee.WorkCounty, employee.DirectDepositEnabled, hasSocialSecurityNumber = !string.IsNullOrWhiteSpace(employee.SocialSecurityNumber), hasBankAccount = !string.IsNullOrWhiteSpace(employee.BankAccountNumber) }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The employee record changed while it was being saved. Refresh and try again."); }
        return TransactionResult.Success(employee.Id);
    }

    public async Task<TransactionResult> SavePayrollTimecardDraftAsync(SavePayrollTimecardDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPrepare)) return TransactionResult.Failure("You are not authorized to prepare payroll timecards.");
        if (request.EmployeeId == Guid.Empty || request.PeriodEnd < request.PeriodStart || request.PeriodEnd.DayNumber - request.PeriodStart.DayNumber > 30)
            return TransactionResult.Failure("Select an employee and use a timecard period of no more than 31 days.");
        if (request.Entries.Count == 0 || request.Entries.Count > 200) return TransactionResult.Failure("A timecard must contain between 1 and 200 earning entries.");
        if (request.Entries.Any(entry => entry.WorkDate < request.PeriodStart || entry.WorkDate > request.PeriodEnd)) return TransactionResult.Failure("Every time entry must fall within the timecard period.");
        if (request.Entries.Any(entry => string.IsNullOrWhiteSpace(entry.EarningCode) || string.IsNullOrWhiteSpace(entry.EarningType) || entry.Hours < 0 || entry.Rate < 0 || entry.Amount < 0))
            return TransactionResult.Failure("Each time entry requires an earning code and type with non-negative hours, rate, and amount.");
        if (request.Entries.GroupBy(entry => entry.WorkDate).Any(group => group.Sum(entry => entry.Hours) > 24m)) return TransactionResult.Failure("Timecard hours cannot exceed 24 hours on one work date.");
        foreach (var entry in request.Entries)
        {
            var calculated = RoundCurrency(entry.Hours * entry.Rate);
            if (entry.Amount <= 0 && calculated <= 0) return TransactionResult.Failure("Each time entry must have a positive amount or positive hours and rate.");
            if (entry.Amount > 0 && entry.Hours > 0 && entry.Rate > 0 && Math.Abs(entry.Amount - calculated) > 0.01m)
                return TransactionResult.Failure("A time entry amount must equal hours multiplied by rate when all three values are supplied.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var employee = await db.Employees.SingleOrDefaultAsync(candidate => candidate.Id == request.EmployeeId && candidate.CompanyId == companyId && candidate.IsActive, cancellationToken);
        if (employee is null) return TransactionResult.Failure("Active employee not found.");
        if (employee.EmploymentStartedOn is { } employmentStart && request.PeriodEnd < employmentStart) return TransactionResult.Failure("The timecard period precedes the employee's start date.");
        if (employee.EmploymentEndedOn is { } employmentEnd && request.PeriodStart > employmentEnd) return TransactionResult.Failure("The timecard period follows the employee's end date.");
        if (await db.PayrollTimecards.AnyAsync(card => card.CompanyId == companyId && card.EmployeeId == employee.Id && (card.Status == "Draft" || card.Status == "Submitted" || card.Status == "Approved") && card.Id != request.TimecardId && card.PeriodStart <= request.PeriodEnd && card.PeriodEnd >= request.PeriodStart, cancellationToken))
            return TransactionResult.Failure("This employee already has an overlapping active timecard.");
        var projectIds = request.Entries.Where(entry => entry.ProjectJobId.HasValue).Select(entry => entry.ProjectJobId!.Value).Distinct().ToArray();
        if (projectIds.Length > 0 && await db.ProjectJobs.CountAsync(project => projectIds.Contains(project.Id) && project.CompanyId == companyId, cancellationToken) != projectIds.Length)
            return TransactionResult.Failure("One or more selected projects do not belong to the active company.");

        PayrollTimecard timecard;
        if (request.TimecardId is { } timecardId && timecardId != Guid.Empty)
        {
            timecard = await db.PayrollTimecards.SingleOrDefaultAsync(card => card.Id == timecardId && card.CompanyId == companyId, cancellationToken) ?? null!;
            if (timecard is null) return TransactionResult.Failure("Payroll timecard not found.");
            if (timecard.Status != "Draft") return TransactionResult.Failure("Only a draft timecard can be edited.");
            if (!string.Equals(timecard.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The timecard changed after it was opened. Refresh and review it again.");
            db.PayrollTimeEntries.RemoveRange(await db.PayrollTimeEntries.Where(entry => entry.PayrollTimecardId == timecard.Id).ToListAsync(cancellationToken));
        }
        else
        {
            timecard = new PayrollTimecard { Id = Guid.NewGuid(), CompanyId = companyId, Status = "Draft", PreparedByUserId = ResolveUserId(), PreparedAtUtc = DateTimeOffset.UtcNow };
            db.PayrollTimecards.Add(timecard);
        }
        timecard.EmployeeId = employee.Id; timecard.PeriodStart = request.PeriodStart; timecard.PeriodEnd = request.PeriodEnd; timecard.Notes = request.Notes.Trim(); timecard.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.PayrollTimeEntries.AddRange(request.Entries.Select((entry, index) => new PayrollTimeEntry
        {
            Id = Guid.NewGuid(), PayrollTimecardId = timecard.Id, Sequence = index + 1, WorkDate = entry.WorkDate,
            EarningCode = entry.EarningCode.Trim().ToUpperInvariant(), EarningType = entry.EarningType.Trim(), Hours = entry.Hours, Rate = entry.Rate,
            Amount = RoundCurrency(entry.Amount > 0 ? entry.Amount : entry.Hours * entry.Rate), IsTaxable = entry.IsTaxable,
            WorkState = string.IsNullOrWhiteSpace(entry.WorkState) ? employee.State : entry.WorkState.Trim(), WorkCounty = string.IsNullOrWhiteSpace(entry.WorkCounty) ? employee.WorkCounty : entry.WorkCounty.Trim(),
            WorkCity = string.IsNullOrWhiteSpace(entry.WorkCity) ? employee.WorkCity : entry.WorkCity.Trim(), WorkSchoolDistrict = string.IsNullOrWhiteSpace(entry.WorkSchoolDistrict) ? employee.WorkSchoolDistrict : entry.WorkSchoolDistrict.Trim(),
            ProjectJobId = entry.ProjectJobId, Notes = entry.Notes.Trim()
        }));
        AddTimecardAudit(db, companyId, "payroll-timecard.saved", timecard, new { employee.EmployeeNumber, timecard.PeriodStart, timecard.PeriodEnd, entryCount = request.Entries.Count, totalHours = request.Entries.Sum(entry => entry.Hours), totalAmount = request.Entries.Sum(entry => entry.Amount > 0 ? entry.Amount : RoundCurrency(entry.Hours * entry.Rate)) });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The timecard changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The timecard could not be saved because it overlaps or conflicts with existing data."); }
        return TransactionResult.Success(timecard.Id);
    }

    public async Task<TransactionResult> SubmitPayrollTimecardAsync(SubmitPayrollTimecardRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollPrepare)) return TransactionResult.Failure("You are not authorized to submit payroll timecards.");
        return await TransitionTimecardAsync(request.TimecardId, request.ConcurrencyToken, "Draft", "Submitted", "payroll-timecard.submitted", cancellationToken);
    }

    public async Task<TransactionResult> ApprovePayrollTimecardAsync(ApprovePayrollTimecardRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollApprove)) return TransactionResult.Failure("You are not authorized to approve payroll timecards.");
        return await TransitionTimecardAsync(request.TimecardId, request.ConcurrencyToken, "Submitted", "Approved", "payroll-timecard.approved", cancellationToken);
    }

    public async Task<TransactionResult> VoidPayrollTimecardAsync(VoidPayrollTimecardRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollReverse)) return TransactionResult.Failure("You are not authorized to void payroll timecards.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A timecard void reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var timecard = await db.PayrollTimecards.SingleOrDefaultAsync(card => card.Id == request.TimecardId && card.CompanyId == companyId, cancellationToken);
        if (timecard is null) return TransactionResult.Failure("Payroll timecard not found.");
        if (timecard.Status is "Voided" or "Consumed" || timecard.PayrollRunId.HasValue) return TransactionResult.Failure("A voided or payroll-consumed timecard cannot be voided again.");
        if (!string.Equals(timecard.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The timecard changed after it was opened. Refresh and review it again.");
        timecard.Status = "Voided"; timecard.VoidedByUserId = ResolveUserId(); timecard.VoidedAtUtc = DateTimeOffset.UtcNow; timecard.VoidReason = request.Reason.Trim(); timecard.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddTimecardAudit(db, companyId, "payroll-timecard.voided", timecard, new { timecard.VoidReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The timecard changed while it was being voided. Refresh and try again."); }
        return TransactionResult.Success(timecard.Id);
    }

    public async Task<TransactionResult> SavePayrollJurisdictionRuleAsync(SavePayrollJurisdictionRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayrollManage)) return TransactionResult.Failure("You are not authorized to maintain payroll jurisdiction rules.");
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

    private sealed record PayrollTimecardExpansion(PostEmployeePayrollRunRequest? Request, IReadOnlyList<PayrollTimecard> Timecards, string ErrorMessage)
    {
        public static PayrollTimecardExpansion Failure(string errorMessage) => new(null, [], errorMessage);
    }

    private static async Task<PayrollTimecardExpansion> ExpandApprovedTimecardsAsync(BrassLedgerDbContext db, Guid companyId, PostEmployeePayrollRunRequest request, CancellationToken cancellationToken)
    {
        if (request.Employees.Any(employee => employee.Earnings?.Any(earning => earning.SourceTimeEntryId is not null) == true))
            return PayrollTimecardExpansion.Failure("Payroll time-entry provenance is assigned by the server and cannot be supplied by a payroll request.");

        var selectedIds = request.ApprovedTimecardIds?.ToArray() ?? [];
        if (selectedIds.Length == 0) return new PayrollTimecardExpansion(request, [], string.Empty);
        if (selectedIds.Any(id => id == Guid.Empty) || selectedIds.Distinct().Count() != selectedIds.Length)
            return PayrollTimecardExpansion.Failure("Approved timecards must be selected once each.");
        if (request.PeriodStart is null || request.PeriodEnd is null)
            return PayrollTimecardExpansion.Failure("A pay-period start and end are required when approved timecards are included.");
        if (request.PeriodEnd < request.PeriodStart || request.PayDate < request.PeriodEnd)
            return PayrollTimecardExpansion.Failure("The payroll period must end on or before the pay date and cannot end before it starts.");

        var timecards = await db.PayrollTimecards.Where(card => card.CompanyId == companyId && selectedIds.Contains(card.Id)).ToListAsync(cancellationToken);
        if (timecards.Count != selectedIds.Length)
            return PayrollTimecardExpansion.Failure("One or more selected timecards do not exist in this company.");
        if (timecards.Any(card => card.Status != "Approved" || card.PayrollRunId is not null))
            return PayrollTimecardExpansion.Failure("Every selected timecard must be approved and not already assigned to a payroll run.");

        var requestedEmployeeIds = request.Employees.Select(employee => employee.EmployeeId).ToHashSet();
        if (timecards.Any(card => !requestedEmployeeIds.Contains(card.EmployeeId)))
            return PayrollTimecardExpansion.Failure("Every selected timecard employee must be included in this payroll request.");
        if (timecards.Any(card => card.PeriodStart < request.PeriodStart.Value || card.PeriodEnd > request.PeriodEnd.Value))
            return PayrollTimecardExpansion.Failure("Every selected timecard must fall entirely within the payroll period.");

        var timecardIds = timecards.Select(card => card.Id).ToArray();
        var entries = await db.PayrollTimeEntries.Where(entry => timecardIds.Contains(entry.PayrollTimecardId)).OrderBy(entry => entry.WorkDate).ThenBy(entry => entry.Sequence).ToListAsync(cancellationToken);
        if (timecards.Any(card => !entries.Any(entry => entry.PayrollTimecardId == card.Id)))
            return PayrollTimecardExpansion.Failure("Every selected timecard must contain at least one earning entry.");
        var entryIds = entries.Select(entry => entry.Id).ToArray();
        if (await db.PayrollEarningLines
            .Where(line => line.PayrollTimeEntryId != null && entryIds.Contains(line.PayrollTimeEntryId.Value))
            .Join(db.PayrollRunEmployeeLines, earning => earning.PayrollRunEmployeeLineId, employeeLine => employeeLine.Id, (_, employeeLine) => employeeLine.PayrollRunId)
            .Join(db.PayrollRuns.Where(run => run.Status != "Cancelled"), runId => runId, run => run.Id, (_, _) => true)
            .AnyAsync(cancellationToken))
            return PayrollTimecardExpansion.Failure("One or more selected time entries have already been used by another payroll run.");

        var employees = await db.Employees.Where(employee => employee.CompanyId == companyId && requestedEmployeeIds.Contains(employee.Id)).ToDictionaryAsync(employee => employee.Id, cancellationToken);
        var expandedEmployees = new List<EmployeePayrollInput>(request.Employees.Count);
        foreach (var input in request.Employees)
        {
            var employeeEntries = entries.Where(entry => timecards.Any(card => card.Id == entry.PayrollTimecardId && card.EmployeeId == input.EmployeeId)).ToArray();
            if (employeeEntries.Length == 0)
            {
                expandedEmployees.Add(input);
                continue;
            }

            var earnings = input.Earnings?.ToList() ?? [];
            if (earnings.Count == 0 && employees.TryGetValue(input.EmployeeId, out var employee) && employee.PayType.Contains("Salary", StringComparison.OrdinalIgnoreCase) && input.GrossPay > 0)
                earnings.Add(new PayrollEarningInput("SALARY", "Salary", 0, 0, input.GrossPay, true, null, employee.State, employee.WorkCounty, employee.WorkCity, employee.WorkSchoolDistrict));
            earnings.AddRange(employeeEntries.Select(entry => new PayrollEarningInput(entry.EarningCode, entry.EarningType, entry.Hours, entry.Rate, entry.Amount, entry.IsTaxable, entry.WorkDate, entry.WorkState, entry.WorkCounty, entry.WorkCity, entry.WorkSchoolDistrict, entry.Id)));
            expandedEmployees.Add(input with { GrossPay = RoundCurrency(earnings.Sum(earning => earning.Amount)), Earnings = earnings });
        }

        return new PayrollTimecardExpansion(request with { Employees = expandedEmployees }, timecards, string.Empty);
    }

    private static async Task<PayrollRunEstimate?> CalculateEmployeePayrollAsync(BrassLedgerDbContext db, Guid companyId, PostEmployeePayrollRunRequest request, CancellationToken cancellationToken)
    {
        var ids = request.Employees.Select(line => line.EmployeeId).ToArray();
        var employees = await db.Employees.Where(employee => employee.CompanyId == companyId && employee.IsActive && ids.Contains(employee.Id)).ToListAsync(cancellationToken);
        if (employees.Count != ids.Length) return null;
        var profiles = await db.TaxProfiles.Where(profile => profile.CompanyId == companyId && profile.IsActive && profile.IsVerified && profile.EffectiveOn <= request.PayDate).ToListAsync(cancellationToken);
        var approvedPackageIds = await db.TaxContentPackages.Where(package => package.CompanyId == companyId && package.Status == "Approved" && package.EffectiveOn <= request.PayDate).Select(package => package.Id).ToListAsync(cancellationToken);
        var contentRules = approvedPackageIds.Count == 0 ? [] : await db.TaxRuleSets.Where(rule => rule.CompanyId == companyId && rule.IsActive && rule.EffectiveOn <= request.PayDate && rule.TaxContentPackageId != null && approvedPackageIds.Contains(rule.TaxContentPackageId.Value)).ToListAsync(cancellationToken);
        var contentRuleIds = contentRules.Select(rule => rule.Id).ToArray();
        var contentParameters = contentRuleIds.Length == 0 ? [] : await db.TaxRuleParameters.Where(parameter => contentRuleIds.Contains(parameter.TaxRuleSetId)).ToListAsync(cancellationToken);
        var contentBrackets = contentRuleIds.Length == 0 ? [] : await db.TaxRuleBrackets.Where(bracket => contentRuleIds.Contains(bracket.TaxRuleSetId)).ToListAsync(cancellationToken);
        var jurisdictionRules = await db.PayrollJurisdictionRules.Where(rule => rule.CompanyId == companyId && rule.IsActive).ToListAsync(cancellationToken);
        var priorLines = await db.PayrollRunEmployeeLines.Join(db.PayrollRuns.Where(run => run.CompanyId == companyId && run.Status == "Posted" && run.PayDate.Year == request.PayDate.Year && run.PayDate < request.PayDate), line => line.PayrollRunId, run => run.Id, (line, _) => line).ToListAsync(cancellationToken);
        var priorLineIds = priorLines.Select(line => line.Id).ToArray();
        var priorTaxLines = priorLineIds.Length == 0 ? [] : await db.PayrollTaxLines.Where(line => priorLineIds.Contains(line.PayrollRunEmployeeLineId)).ToListAsync(cancellationToken);
        var estimates = new List<EmployeePayrollEstimate>();
        foreach (var input in request.Employees)
        {
            var employee = employees.Single(candidate => candidate.Id == input.EmployeeId);
            var grossPay = ResolveGrossPay(input);
            var requestedDeductions = input.Deductions ?? [];
            var requestedPreTax = requestedDeductions.Where(deduction => deduction.IsPreTax).Sum(deduction => deduction.EmployeeAmount);
            var requestedPostTax = requestedDeductions.Where(deduction => !deduction.IsPreTax).Sum(deduction => deduction.EmployeeAmount);
            var taxableEarnings = input.Earnings is { Count: > 0 } ? input.Earnings.Where(earning => earning.IsTaxable).Sum(earning => earning.Amount) : grossPay;
            var preTax = Math.Min(grossPay, Math.Max(0, employee.PreTaxBenefitDeductions + requestedPreTax));
            var taxable = Math.Max(0, taxableEarnings - preTax);
            var allocations = BuildPayrollWorkAllocations(input, employee, taxableEarnings, taxable);
            var residenceJurisdictions = ResidenceJurisdictions(employee);
            var workJurisdictions = allocations.SelectMany(AllocationJurisdictions).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var rules = jurisdictionRules.Where(rule => residenceJurisdictions.Any(jurisdiction => JurisdictionEquals(jurisdiction, rule.ResidenceJurisdiction)) && workJurisdictions.Any(jurisdiction => JurisdictionEquals(jurisdiction, rule.WorkJurisdiction))).ToArray();
            var employeePriorLineIds = priorLines.Where(line => line.EmployeeId == employee.Id).Select(line => line.Id).ToHashSet();
            var employeePriorTaxLines = priorTaxLines.Where(line => employeePriorLineIds.Contains(line.PayrollRunEmployeeLineId)).ToArray();

            var matchedRules = contentRules.Select(rule =>
                {
                    var isEmployeeTax = IsEmployeeTax(rule.TaxType);
                    var scope = ResolvePayrollTaxScope(rule.JurisdictionCode, rule.JurisdictionName, isEmployeeTax, taxable, employee, allocations);
                    if (scope is null || IsWorkWithholdingExempt(rule.JurisdictionCode, rule.JurisdictionName, isEmployeeTax, scope, rules)) return null;
                    var context = PayrollTaxContext(scope, employee);
                    return TaxRuleEvaluator.IsApplicable(rule, contentParameters.Where(parameter => parameter.TaxRuleSetId == rule.Id), context) ? new ScopedTaxRule(rule, scope, context) : null;
                })
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .ToArray();
            var applicableRules = matchedRules
                .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.Rule.ExclusiveGroup) ? $"rule:{candidate.Rule.Id}" : $"exclusive:{candidate.Rule.ExclusiveGroup}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(candidate => candidate.Rule.VariantPriority).ThenBy(candidate => candidate.Rule.Code).First())
                .ToArray();
            if (employee.FederalFormW4Year <= 0 || request.PayDate.Year != 2026) return null;
            decimal employeeTaxes = 0, residentEmployeeTaxes = 0, employerTaxes = 0;
            var taxLines = new List<PayrollTaxEstimate>();
            var selectedObligations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var residentObligations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in applicableRules)
            {
                var rule = candidate.Rule;
                var amount = TaxRuleEvaluator.Evaluate(rule, contentParameters.Where(parameter => parameter.TaxRuleSetId == rule.Id), contentBrackets.Where(bracket => bracket.TaxRuleSetId == rule.Id), candidate.Context);
                var isEmployeeTax = IsEmployeeTax(rule.TaxType);
                if (isEmployeeTax) { employeeTaxes += amount; if (candidate.Scope.IsResidence) residentEmployeeTaxes += amount; }
                else employerTaxes += amount;
                var obligation = NormalizeObligation(rule.ObligationCode, rule.TaxType, rule.JurisdictionCode);
                selectedObligations.Add(obligation);
                if (isEmployeeTax && candidate.Scope.IsResidence) residentObligations.Add(obligation);
                var priorTaxable = employeePriorTaxLines.Where(line => line.ObligationCode == obligation).Sum(line => line.TaxableWages);
                taxLines.Add(new PayrollTaxEstimate(obligation, rule.JurisdictionCode, rule.JurisdictionName, rule.TaxType, candidate.Scope.TaxableWages, priorTaxable, isEmployeeTax ? amount : 0, isEmployeeTax ? 0 : amount, rule.Id, rule.TaxContentPackageId, rule.ContentVersion, rule.Source, System.Text.Json.JsonSerializer.Serialize(new { rule.CalculationMethod, rule.CalculationVariant, taxableWages = candidate.Scope.TaxableWages, candidate.Scope.IsResidence, candidate.Scope.WorkState, candidate.Scope.WorkCounty, candidate.Scope.WorkCity, candidate.Scope.WorkSchoolDistrict, amount })));
            }
            foreach (var profile in profiles)
            {
                var obligation = NormalizeObligation(string.Empty, profile.TaxType, profile.Jurisdiction);
                if (selectedObligations.Contains(obligation)) continue;
                var isEmployeeTax = IsEmployeeTax(profile.TaxType);
                var scope = ResolvePayrollTaxScope(profile.Jurisdiction, profile.Jurisdiction, isEmployeeTax, taxable, employee, allocations);
                if (scope is null || IsWorkWithholdingExempt(profile.Jurisdiction, profile.Jurisdiction, isEmployeeTax, scope, rules)) continue;
                selectedObligations.Add(obligation);
                var priorGross = employeePriorTaxLines.Where(line => line.ObligationCode == obligation).Sum(line => line.TaxableWages);
                var cappedTaxable = profile.AnnualWageBase is { } wageBase ? Math.Max(0, Math.Min(scope.TaxableWages, wageBase - priorGross)) : scope.TaxableWages;
                var amount = RoundCurrency(cappedTaxable * profile.Rate);
                if (isEmployeeTax) { employeeTaxes += amount; if (scope.IsResidence) residentEmployeeTaxes += amount; }
                else employerTaxes += amount;
                if (isEmployeeTax && scope.IsResidence) residentObligations.Add(obligation);
                taxLines.Add(new PayrollTaxEstimate(obligation, profile.Jurisdiction, profile.Jurisdiction, profile.TaxType, cappedTaxable, priorGross, isEmployeeTax ? amount : 0, isEmployeeTax ? 0 : amount, null, null, string.Empty, profile.Source, System.Text.Json.JsonSerializer.Serialize(new { method = "profile-rate", profile.Rate, profile.AnnualWageBase, taxableWages = cappedTaxable, scope.IsResidence, scope.WorkState, scope.WorkCounty, scope.WorkCity, scope.WorkSchoolDistrict, amount })));
            }
            var federalIncomeTaxable = Math.Max(0, taxableEarnings - employee.PreTaxBenefitDeductions - requestedDeductions.Where(deduction => deduction.ExemptFromFederalIncomeTax).Sum(deduction => deduction.EmployeeAmount));
            var ficaTaxable = Math.Max(0, taxableEarnings - requestedDeductions.Where(deduction => deduction.ExemptFromFica).Sum(deduction => deduction.EmployeeAmount));
            var priorSocialSecurityWages = employeePriorTaxLines.Where(line => line.ObligationCode == "US-OASDI-EMPLOYEE").Sum(line => line.TaxableWages);
            var priorMedicareWages = employeePriorTaxLines.Where(line => line.ObligationCode == "US-MEDICARE-EMPLOYEE").Sum(line => line.TaxableWages);
            foreach (var federal in FederalPayrollTaxCalculator.Calculate2026(employee, federalIncomeTaxable, ficaTaxable, priorSocialSecurityWages, priorMedicareWages))
            {
                if (selectedObligations.Contains(federal.ObligationCode)) continue;
                employeeTaxes += federal.EmployeeAmount;
                employerTaxes += federal.EmployerAmount;
                taxLines.Add(new PayrollTaxEstimate(federal.ObligationCode, "US", "Federal", federal.TaxType, federal.TaxableWages, federal.YearToDateTaxableWagesBefore, federal.EmployeeAmount, federal.EmployerAmount, null, null, FederalPayrollTaxCalculator.ContentVersion, federal.Source, federal.CalculationTraceJson));
            }
            var residentCredit = RoundCurrency(residentEmployeeTaxes * rules.Select(rule => rule.ResidentCreditRate).DefaultIfEmpty(0m).Max());
            employeeTaxes -= residentCredit;
            ApplyResidentCredit(taxLines, residentObligations, residentCredit);
            var additionalWithholding = employee.FederalWithholdingExempt ? 0 : Math.Max(0, employee.AdditionalWithholding);
            employeeTaxes = RoundCurrency(employeeTaxes + additionalWithholding);
            if (additionalWithholding > 0) taxLines.Add(new PayrollTaxEstimate("FEDERAL-ADDITIONAL-WITHHOLDING", "US", "Federal", "Additional withholding", taxable, priorLines.Where(line => line.EmployeeId == employee.Id).Sum(line => line.TaxableWages), additionalWithholding, 0, null, null, string.Empty, "Employee election", System.Text.Json.JsonSerializer.Serialize(new { amount = additionalWithholding })));
            var postTax = Math.Min(Math.Max(0, employee.PostTaxBenefitDeductions + requestedPostTax), Math.Max(0, taxable - employeeTaxes));
            var net = RoundCurrency(Math.Max(0, grossPay - preTax - employeeTaxes - postTax));
            var ytdGross = priorLines.Where(line => line.EmployeeId == employee.Id).Sum(line => line.GrossPay);
            var employerBenefitContributions = RoundCurrency(requestedDeductions.Sum(deduction => deduction.EmployerAmount));
            estimates.Add(new EmployeePayrollEstimate(employee.Id, $"{employee.FirstName} {employee.LastName}", employee.State, employee.FilingStatus, grossPay, preTax, employeeTaxes, postTax, RoundCurrency(employerTaxes), net, ytdGross, taxLines, employerBenefitContributions));
        }
        return new PayrollRunEstimate(estimates.Sum(line => line.GrossPay), estimates.Sum(line => line.PreTaxDeductions), estimates.Sum(line => line.EmployeeWithholdings), estimates.Sum(line => line.PostTaxDeductions), estimates.Sum(line => line.EmployerPayrollTaxes), estimates.Sum(line => line.NetPay), estimates, estimates.Sum(line => line.EmployerBenefitContributions));
    }

    private sealed record PayrollWorkLocationKey(string WorkState, string WorkCounty, string WorkCity, string WorkSchoolDistrict);
    private sealed record PayrollWorkAllocation(string WorkState, string WorkCounty, string WorkCity, string WorkSchoolDistrict, decimal TaxableWages);
    private sealed record PayrollTaxScope(decimal TaxableWages, bool IsResidence, string WorkState, string WorkCounty, string WorkCity, string WorkSchoolDistrict);
    private sealed record ScopedTaxRule(TaxRuleSet Rule, PayrollTaxScope Scope, TaxRuleEvaluationContext Context);

    private static IReadOnlyList<PayrollWorkAllocation> BuildPayrollWorkAllocations(EmployeePayrollInput input, Employee employee, decimal taxableEarnings, decimal taxableWages)
    {
        if (input.Earnings is not { Count: > 0 })
            return [new PayrollWorkAllocation(employee.State, employee.WorkCounty, employee.WorkCity, employee.WorkSchoolDistrict, taxableWages)];

        var grouped = input.Earnings.Where(earning => earning.IsTaxable && earning.Amount > 0)
            .GroupBy(earning => new PayrollWorkLocationKey(
                NormalizeLocation(earning.WorkState, employee.State),
                NormalizeLocation(earning.WorkCounty, employee.WorkCounty),
                NormalizeLocation(earning.WorkCity, employee.WorkCity),
                NormalizeLocation(earning.WorkSchoolDistrict, employee.WorkSchoolDistrict)))
            .Select(group => new
            {
                Location = group.Key,
                Amount = group.Sum(earning => earning.Amount)
            })
            .OrderBy(group => group.Location.WorkState, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Location.WorkCity, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (grouped.Length == 0 || taxableEarnings <= 0)
            return [new PayrollWorkAllocation(employee.State, employee.WorkCounty, employee.WorkCity, employee.WorkSchoolDistrict, taxableWages)];

        var allocations = new List<PayrollWorkAllocation>(grouped.Length);
        decimal allocated = 0;
        for (var index = 0; index < grouped.Length; index++)
        {
            var wages = index == grouped.Length - 1 ? taxableWages - allocated : RoundCurrency(taxableWages * grouped[index].Amount / taxableEarnings);
            allocated += wages;
            allocations.Add(new PayrollWorkAllocation(grouped[index].Location.WorkState, grouped[index].Location.WorkCounty, grouped[index].Location.WorkCity, grouped[index].Location.WorkSchoolDistrict, Math.Max(0, wages)));
        }
        return allocations;
    }

    private static PayrollTaxScope? ResolvePayrollTaxScope(string jurisdictionCode, string jurisdictionName, bool isEmployeeTax, decimal totalTaxableWages, Employee employee, IReadOnlyList<PayrollWorkAllocation> allocations)
    {
        if (IsFederalJurisdiction(jurisdictionCode, jurisdictionName))
            return new PayrollTaxScope(totalTaxableWages, false, employee.State, employee.WorkCounty, employee.WorkCity, employee.WorkSchoolDistrict);

        var residenceJurisdictions = ResidenceJurisdictions(employee);
        if (isEmployeeTax && TargetMatchesJurisdictions(jurisdictionCode, jurisdictionName, residenceJurisdictions))
            return new PayrollTaxScope(totalTaxableWages, true, employee.State, employee.WorkCounty, employee.WorkCity, employee.WorkSchoolDistrict);

        var matchingAllocations = allocations.Where(allocation => TargetMatchesJurisdictions(jurisdictionCode, jurisdictionName, AllocationJurisdictions(allocation))).ToArray();
        if (matchingAllocations.Length == 0) return null;
        var representative = matchingAllocations[0];
        return new PayrollTaxScope(matchingAllocations.Sum(allocation => allocation.TaxableWages), false, representative.WorkState, representative.WorkCounty, representative.WorkCity, representative.WorkSchoolDistrict);
    }

    private static TaxRuleEvaluationContext PayrollTaxContext(PayrollTaxScope scope, Employee employee) => new(
        scope.TaxableWages,
        employee.Allowances,
        employee.FilingStatus,
        employee.PayrollFrequency,
        string.IsNullOrWhiteSpace(employee.ResidenceState) ? employee.State : employee.ResidenceState,
        employee.ResidenceCity,
        scope.WorkState,
        scope.WorkCity);

    private static bool IsWorkWithholdingExempt(string jurisdictionCode, string jurisdictionName, bool isEmployeeTax, PayrollTaxScope scope, IEnumerable<PayrollJurisdictionRule> rules) =>
        isEmployeeTax && !scope.IsResidence && rules.Any(rule => rule.ExemptWorkWithholding && TargetMatchesJurisdictions(jurisdictionCode, jurisdictionName, [rule.WorkJurisdiction]));

    private static string[] ResidenceJurisdictions(Employee employee) =>
        new[]
        {
            string.IsNullOrWhiteSpace(employee.ResidenceState) ? employee.State : employee.ResidenceState,
            StateJurisdiction(string.IsNullOrWhiteSpace(employee.ResidenceState) ? employee.State : employee.ResidenceState),
            employee.ResidenceCounty,
            employee.ResidenceCity,
            employee.ResidenceSchoolDistrict
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] AllocationJurisdictions(PayrollWorkAllocation allocation) =>
        new[] { allocation.WorkState, StateJurisdiction(allocation.WorkState), allocation.WorkCounty, allocation.WorkCity, allocation.WorkSchoolDistrict }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool TargetMatchesJurisdictions(string jurisdictionCode, string jurisdictionName, IEnumerable<string> jurisdictions) =>
        jurisdictions.Any(jurisdiction => JurisdictionEquals(jurisdictionCode, jurisdiction) || JurisdictionEquals(jurisdictionName, jurisdiction));

    private static bool JurisdictionEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        if (left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        var leftState = TaxRuleCatalog.StateJurisdictions.FirstOrDefault(state => state.Code.Equals(left.Trim(), StringComparison.OrdinalIgnoreCase) || state.Name.Equals(left.Trim(), StringComparison.OrdinalIgnoreCase));
        var rightState = TaxRuleCatalog.StateJurisdictions.FirstOrDefault(state => state.Code.Equals(right.Trim(), StringComparison.OrdinalIgnoreCase) || state.Name.Equals(right.Trim(), StringComparison.OrdinalIgnoreCase));
        return leftState is not null && rightState is not null && leftState.Code.Equals(rightState.Code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFederalJurisdiction(string jurisdictionCode, string jurisdictionName) =>
        jurisdictionCode.Trim().Equals("US", StringComparison.OrdinalIgnoreCase) || jurisdictionCode.Trim().Equals("Federal", StringComparison.OrdinalIgnoreCase) || jurisdictionName.Trim().Equals("Federal", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLocation(string value, string fallback) => (string.IsNullOrWhiteSpace(value) ? fallback : value).Trim().ToUpperInvariant();

    private static string StateJurisdiction(string state) => TaxRuleCatalog.StateJurisdictions.FirstOrDefault(jurisdiction => string.Equals(jurisdiction.Code, state.Trim(), StringComparison.OrdinalIgnoreCase))?.Name ?? state.Trim();

    private static string NormalizeDigits(string value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static bool IsValidAbaRoutingNumber(string value)
    {
        if (value.Length != 9) return false;
        var digits = value.Select(character => character - '0').ToArray();
        return (3 * (digits[0] + digits[3] + digits[6]) + 7 * (digits[1] + digits[4] + digits[7]) + digits[2] + digits[5] + digits[8]) % 10 == 0;
    }

    private static decimal ResolveGrossPay(EmployeePayrollInput input) => RoundCurrency(input.Earnings is { Count: > 0 } ? input.Earnings.Sum(earning => earning.Amount) : input.GrossPay);

    private static IReadOnlyList<PayrollDeductionInput> ResolvePayrollDeductions(EmployeePayrollInput input, Employee employee)
    {
        var deductions = new List<PayrollDeductionInput>();
        if (employee.PreTaxBenefitDeductions > 0)
            deductions.Add(new PayrollDeductionInput("RECURRING-PRE-TAX", "Recurring pre-tax benefit", employee.PreTaxBenefitDeductions, IsPreTax: true, ExemptFromFederalIncomeTax: true));
        if (employee.PostTaxBenefitDeductions > 0)
            deductions.Add(new PayrollDeductionInput("RECURRING-POST-TAX", "Recurring post-tax benefit", employee.PostTaxBenefitDeductions));
        deductions.AddRange(input.Deductions ?? []);
        return deductions;
    }

    private static string NormalizeLiabilityAccountNumber(string accountNumber) => string.IsNullOrWhiteSpace(accountNumber) ? "2200" : accountNumber.Trim();

    private static void ApplyResidentCredit(List<PayrollTaxEstimate> taxLines, IReadOnlySet<string> residentObligations, decimal credit)
    {
        var remaining = credit;
        for (var index = 0; index < taxLines.Count && remaining > 0; index++)
        {
            var tax = taxLines[index];
            if (!residentObligations.Contains(tax.ObligationCode) || tax.EmployeeAmount <= 0) continue;
            var applied = Math.Min(remaining, tax.EmployeeAmount);
            taxLines[index] = tax with
            {
                EmployeeAmount = tax.EmployeeAmount - applied,
                CalculationTraceJson = System.Text.Json.JsonSerializer.Serialize(new { underlyingCalculationTrace = tax.CalculationTraceJson, residentCreditApplied = applied, amountAfterCredit = tax.EmployeeAmount - applied })
            };
            remaining -= applied;
        }
    }

    private static bool IsEmployeeTax(string taxType) => taxType.Contains("withholding", StringComparison.OrdinalIgnoreCase) || taxType.Contains("employee", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObligation(string obligationCode, string taxType, string jurisdiction)
    {
        if (!string.IsNullOrWhiteSpace(obligationCode)) return obligationCode.Trim().ToUpperInvariant();
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var normalizedJurisdiction = Normalize(jurisdiction);
        if (normalizedJurisdiction is "US" or "FEDERAL") normalizedJurisdiction = "FEDERAL";
        if (normalizedJurisdiction == "FEDERAL" && (taxType.Contains("withholding", StringComparison.OrdinalIgnoreCase) || taxType.Contains("FIT", StringComparison.OrdinalIgnoreCase))) return "US-FIT";
        var normalizedTaxType = taxType.Contains("withholding", StringComparison.OrdinalIgnoreCase) ? "WITHHOLDING" : Normalize(taxType);
        return $"{normalizedJurisdiction}-{normalizedTaxType}";
    }

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

    private async Task<string> GetPayrollRunConcurrencyTokenAsync(Guid payrollRunId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        return await db.PayrollRuns.Where(run => run.Id == payrollRunId && run.CompanyId == companyId).Select(run => run.ConcurrencyToken).SingleAsync(cancellationToken);
    }

    private void AddPayrollAudit(BrassLedgerDbContext db, Guid companyId, string action, PayrollRun run, object details) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action,
            EntityType = "PayrollRun", EntityId = run.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow
        });

    private void AddPayrollLiabilityPaymentAudit(BrassLedgerDbContext db, Guid companyId, string action, PayrollLiabilityPayment payment, object details) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action,
            EntityType = "PayrollLiabilityPayment", EntityId = payment.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow
        });

    private async Task<TransactionResult> TransitionTimecardAsync(Guid timecardId, string concurrencyToken, string expectedStatus, string newStatus, string auditAction, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var timecard = await db.PayrollTimecards.SingleOrDefaultAsync(card => card.Id == timecardId && card.CompanyId == companyId, cancellationToken);
        if (timecard is null) return TransactionResult.Failure("Payroll timecard not found.");
        if (timecard.Status != expectedStatus) return TransactionResult.Failure($"Only a {expectedStatus.ToLowerInvariant()} timecard can be moved to {newStatus.ToLowerInvariant()}.");
        if (!string.Equals(timecard.ConcurrencyToken, concurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The timecard changed after it was opened. Refresh and review it again.");
        if (newStatus == "Submitted" && !await db.PayrollTimeEntries.AnyAsync(entry => entry.PayrollTimecardId == timecard.Id, cancellationToken)) return TransactionResult.Failure("A timecard must contain at least one entry before submission.");
        var now = DateTimeOffset.UtcNow;
        timecard.Status = newStatus;
        if (newStatus == "Submitted") { timecard.SubmittedByUserId = ResolveUserId(); timecard.SubmittedAtUtc = now; }
        if (newStatus == "Approved") { timecard.ApprovedByUserId = ResolveUserId(); timecard.ApprovedAtUtc = now; }
        timecard.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddTimecardAudit(db, companyId, auditAction, timecard, new { from = expectedStatus, to = newStatus });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The timecard changed during the workflow transition. Refresh and try again."); }
        return TransactionResult.Success(timecard.Id);
    }

    private void AddTimecardAudit(BrassLedgerDbContext db, Guid companyId, string action, PayrollTimecard timecard, object details) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action,
            EntityType = "PayrollTimecard", EntityId = timecard.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow
        });

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

    private static (IReadOnlyList<ParsedBankRow> Rows, IReadOnlyList<string> Rejections) ParseBankStatement(string format, string content)
    {
        var normalized = NormalizeBankFormat(format);
        if (normalized is "OFX" or "QFX") return ParseOfxStatement(content);
        if (normalized == "CAMT.053") return ParseCamtStatement(content);
        if (normalized == "MT940") return ParseMt940Statement(content);
        if (normalized != "CSV") throw new InvalidDataException("Bank statement format must be CSV, OFX, QFX, CAMT.053, or MT940.");
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new InvalidDataException("The CSV statement requires a header and at least one transaction row.");
        var headers = ParseCsvFields(lines[0]).Select((value, index) => new { Name = value.Trim(), index }).ToDictionary(item => item.Name, item => item.index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "ExternalId", "Date", "Amount" }) if (!headers.ContainsKey(required)) throw new InvalidDataException($"The CSV statement is missing the {required} column.");
        var rows = new List<ParsedBankRow>(); var rejections = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var lineNumber = 2; lineNumber <= lines.Length; lineNumber++)
        {
            var fields = ParseCsvFields(lines[lineNumber - 1]);
            string Field(string name) => headers.TryGetValue(name, out var index) && index < fields.Count ? fields[index].Trim() : string.Empty;
            var externalId = Field("ExternalId");
            if (string.IsNullOrWhiteSpace(externalId) || !seen.Add(externalId) || !DateOnly.TryParse(Field("Date"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) || !decimal.TryParse(Field("Amount"), System.Globalization.NumberStyles.Number | System.Globalization.NumberStyles.AllowCurrencySymbol, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount == 0)
            { rejections.Add($"Line {lineNumber}: requires a unique ExternalId, valid Date, and non-zero Amount."); continue; }
            DateOnly? postedDate = DateOnly.TryParse(Field("PostedDate"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedPosted) ? parsedPosted : null;
            rows.Add(new ParsedBankRow(externalId, date, postedDate, RoundCurrency(amount), Field("Type"), Field("Payee"), Field("Memo"), Field("Reference"), System.Text.Json.JsonSerializer.Serialize(fields)));
        }
        return (rows, rejections);
    }

    private static (IReadOnlyList<ParsedBankRow>, IReadOnlyList<string>) ParseOfxStatement(string content)
    {
        var rows = new List<ParsedBankRow>(); var rejections = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(content, @"<STMTTRN>(.*?)(?:</STMTTRN>|(?=<STMTTRN>)|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        var line = 0;
        foreach (System.Text.RegularExpressions.Match match in matches) { line++; var block = match.Groups[1].Value; string Tag(string name) => System.Text.RegularExpressions.Regex.Match(block, $@"<{name}>([^<\r\n]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value.Trim(); var id = Tag("FITID"); var dateText = Tag("DTPOSTED"); if (dateText.Length >= 8) dateText = dateText[..8]; if (string.IsNullOrWhiteSpace(id) || !DateOnly.TryParseExact(dateText, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) || !decimal.TryParse(Tag("TRNAMT"), System.Globalization.NumberStyles.Number | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount == 0) { rejections.Add($"OFX transaction {line}: requires FITID, DTPOSTED, and non-zero TRNAMT."); continue; } rows.Add(new ParsedBankRow(id, date, date, RoundCurrency(amount), Tag("TRNTYPE"), Tag("NAME"), Tag("MEMO"), Tag("CHECKNUM"), System.Text.Json.JsonSerializer.Serialize(new { block }))); }
        if (matches.Count == 0) throw new InvalidDataException("No OFX/QFX statement transactions were found."); return RemoveDuplicateParsedRows(rows, rejections);
    }

    private static (IReadOnlyList<ParsedBankRow>, IReadOnlyList<string>) ParseCamtStatement(string content)
    {
        System.Xml.Linq.XDocument document; try { document = System.Xml.Linq.XDocument.Parse(content); } catch (System.Xml.XmlException exception) { throw new InvalidDataException($"Invalid CAMT.053 XML: {exception.Message}"); }
        var rows = new List<ParsedBankRow>(); var rejections = new List<string>(); var index = 0;
        foreach (var entry in document.Descendants().Where(element => element.Name.LocalName == "Ntry"))
        {
            index++;
            string Desc(string name) => entry.Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() ?? string.Empty;
            var id = Desc("AcctSvcrRef");
            if (string.IsNullOrWhiteSpace(id)) id = Desc("NtryRef");
            var dateValue = entry.Descendants().FirstOrDefault(element => element.Name.LocalName == "BookgDt")?.Descendants().FirstOrDefault(element => element.Name.LocalName is "Dt" or "DtTm")?.Value.Trim() ?? string.Empty;
            var dateText = dateValue.Length >= 10 ? dateValue[..10] : dateValue;
            if (!DateOnly.TryParse(dateText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) || !decimal.TryParse(Desc("Amt"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount == 0 || string.IsNullOrWhiteSpace(id))
            {
                rejections.Add($"CAMT entry {index}: requires reference, booking date, and non-zero amount.");
                continue;
            }
            if (Desc("CdtDbtInd").Equals("DBIT", StringComparison.OrdinalIgnoreCase)) amount = -amount;
            rows.Add(new ParsedBankRow(id, date, date, RoundCurrency(amount), Desc("BkTxCd"), Desc("Nm"), Desc("AddtlNtryInf"), Desc("EndToEndId"), entry.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)));
        }
        if (index == 0) throw new InvalidDataException("No CAMT.053 entries were found."); return RemoveDuplicateParsedRows(rows, rejections);
    }

    private static (IReadOnlyList<ParsedBankRow>, IReadOnlyList<string>) ParseMt940Statement(string content)
    {
        var rows = new List<ParsedBankRow>();
        var rejections = new List<string>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        ParsedBankRow? pending = null;
        var sequence = 0;
        foreach (var raw in lines)
        {
            if (raw.StartsWith(":61:", StringComparison.Ordinal))
            {
                if (pending is not null) rows.Add(pending);
                sequence++;
                var value = raw[4..].Trim();
                var match = System.Text.RegularExpressions.Regex.Match(value, @"^(?<date>\d{6})(?:\d{4})?(?<sign>R?[CD])(?<amount>\d+(?:,\d{1,2})?)(?<rest>.*)$");
                if (!match.Success || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) || !decimal.TryParse(match.Groups["amount"].Value.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                {
                    rejections.Add($"MT940 :61: record {sequence} is invalid.");
                    pending = null;
                    continue;
                }
                var sign = match.Groups["sign"].Value;
                if (sign is "D" or "RC") amount = -amount;
                var rest = match.Groups["rest"].Value;
                var id = rest.Contains("//", StringComparison.Ordinal) ? rest[(rest.IndexOf("//", StringComparison.Ordinal) + 2)..].Trim() : $"MT940-{date:yyyyMMdd}-{sequence}-{amount}";
                pending = new ParsedBankRow(id, date, date, RoundCurrency(amount), rest.Length >= 4 ? rest[..4] : string.Empty, string.Empty, string.Empty, id, System.Text.Json.JsonSerializer.Serialize(new { raw }));
            }
            else if (raw.StartsWith(":86:", StringComparison.Ordinal) && pending is not null)
            {
                pending = pending with { Memo = raw[4..].Trim() };
            }
        }
        if (pending is not null) rows.Add(pending);
        if (sequence == 0) throw new InvalidDataException("No MT940 :61: transactions were found.");
        return RemoveDuplicateParsedRows(rows, rejections);
    }

    private static (IReadOnlyList<ParsedBankRow>, IReadOnlyList<string>) RemoveDuplicateParsedRows(List<ParsedBankRow> rows, List<string> rejections)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal); var accepted = new List<ParsedBankRow>(); foreach (var row in rows) { if (seen.Add(row.ExternalId)) accepted.Add(row); else rejections.Add($"Duplicate external transaction ID {row.ExternalId} in the statement."); } return (accepted, rejections);
    }

    private static string NormalizeBankFormat(string format) => (format ?? string.Empty).Trim().TrimStart('.').ToUpperInvariant() switch { "QFX" => "QFX", "OFX" => "OFX", "CAMT" or "CAMT.053" or "XML" => "CAMT.053", "MT940" or "STA" => "MT940", "CSV" => "CSV", var value => value };

    private static IReadOnlyList<string> ParseCsvFields(string line)
    {
        var fields = new List<string>(); var value = new System.Text.StringBuilder(); var quoted = false;
        for (var index = 0; index < line.Length; index++) { var character = line[index]; if (character == '"') { if (quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; } else quoted = !quoted; } else if (character == ',' && !quoted) { fields.Add(value.ToString()); value.Clear(); } else value.Append(character); }
        if (quoted) throw new InvalidDataException("The CSV statement contains an unterminated quoted field."); fields.Add(value.ToString()); return fields;
    }

    private void AddBankAudit(BrassLedgerDbContext db, Guid companyId, string action, Guid entityId, object details) => db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = "Banking", EntityId = entityId, DetailJson = System.Text.Json.JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow });

    private static Task<bool> IsInCompletedReconciliationAsync(BrassLedgerDbContext db, Guid journalEntryId, CancellationToken cancellationToken) =>
        db.BankReconciliationItems.AnyAsync(
            item => item.JournalEntryId == journalEntryId && db.BankReconciliations.Any(reconciliation => reconciliation.Id == item.BankReconciliationId && reconciliation.Status == "Completed"),
            cancellationToken);

    private sealed record ParsedBankRow(string ExternalId, DateOnly Date, DateOnly? PostedDate, decimal Amount, string Type, string Payee, string Memo, string Reference, string RawJson);

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

    private async Task<TransactionResult> PostInverseAsync(BrassLedgerDbContext db, Guid companyId, Guid originalJournalEntryId, DateOnly date, string reference, string description, Guid sourceDocumentId, string sourceDocumentType, Guid? bankAccountId, CancellationToken cancellationToken, string sourceModule = "Subledger Adjustment")
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
        var entry = new JournalEntry { Id = Guid.NewGuid(), CompanyId = companyId, BankAccountId = bankAccountId, SourceDocumentId = sourceDocumentId, SourceDocumentType = sourceDocumentType, EntryNumber = $"JE-{date:yyyyMMdd}-{Guid.NewGuid():N}"[..20], PostedOn = date, SourceModule = sourceModule, Reference = reference.Trim(), Description = description.Trim(), TotalAmount = debits, Status = "Posted", IsPosted = true, CreatedByUserId = userId, CreatedAtUtc = now, ApprovedByUserId = userId, ApprovedAtUtc = now, PostedByUserId = userId, PostedAtUtc = now, ReversalOfJournalEntryId = original.Id, ConcurrencyToken = Guid.NewGuid().ToString("N") };
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
