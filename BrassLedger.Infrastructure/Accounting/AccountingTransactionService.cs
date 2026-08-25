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
        if (request.Subtotal < 0 || request.TaxAmount < 0 || request.DueDate < request.InvoiceDate)
            return TransactionResult.Failure("Invoice amounts must be non-negative and the due date cannot precede the invoice date.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == request.CustomerId && x.CompanyId == companyId, cancellationToken);
        if (customer is null) return TransactionResult.Failure("Customer not found.");
        if (await db.SalesInvoices.AnyAsync(x => x.CompanyId == companyId && x.InvoiceNumber == request.InvoiceNumber.Trim(), cancellationToken)) return TransactionResult.Failure("Invoice number already exists.");
        var total = request.Subtotal + request.TaxAmount;
        if (total <= 0) return TransactionResult.Failure("Invoice total must be greater than zero.");
        if (customer.CreditLimit > 0 && customer.OpenBalance + total > customer.CreditLimit)
            return TransactionResult.Failure("Posting this invoice would exceed the customer's credit limit.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lines = new List<JournalLineRequest>
        {
            new("1100", total, 0, "Invoice receivable"),
            new(request.RevenueAccountNumber, 0, request.Subtotal, "Invoice revenue")
        };
        if (request.TaxAmount > 0)
            lines.Add(new JournalLineRequest("2100", 0, request.TaxAmount, "Sales tax payable"));
        var invoiceId = Guid.NewGuid();
        var posting = await PostAsync(db, companyId, request.InvoiceDate, "Accounts Receivable", request.InvoiceNumber, request.Description, lines, cancellationToken, allowControlAccounts: true, sourceDocumentId: invoiceId, sourceDocumentType: "SalesInvoice");
        if (!posting.Succeeded) return posting;
        var invoice = new SalesInvoice { Id = invoiceId, CompanyId = companyId, CustomerId = request.CustomerId, InvoiceNumber = request.InvoiceNumber.Trim(), InvoiceDate = request.InvoiceDate, DueDate = request.DueDate, Status = "Open", Subtotal = request.Subtotal, TaxAmount = request.TaxAmount, TotalAmount = total, BalanceDue = total };
        db.SalesInvoices.Add(invoice);
        customer.OpenBalance += total;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return TransactionResult.Failure("Invoice number already exists or was posted concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(invoice.Id);
    }

    public async Task<TransactionResult> CreateVendorBillAsync(CreateVendorBillRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TotalAmount <= 0 || request.DueDate < request.BillDate) return TransactionResult.Failure("Bill amount must be positive and its due date cannot precede the bill date.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.Vendors.AnyAsync(x => x.Id == request.VendorId && x.CompanyId == companyId, cancellationToken)) return TransactionResult.Failure("Vendor not found.");
        if (await db.VendorBills.AnyAsync(x => x.CompanyId == companyId && x.BillNumber == request.BillNumber.Trim(), cancellationToken)) return TransactionResult.Failure("Bill number already exists.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var billId = Guid.NewGuid();
        var posting = await PostAsync(db, companyId, request.BillDate, "Accounts Payable", request.BillNumber, request.Description,
            [new(request.ExpenseAccountNumber, request.TotalAmount, 0, "Bill expense"), new("2000", 0, request.TotalAmount, "Accounts payable")], cancellationToken, allowControlAccounts: true, sourceDocumentId: billId, sourceDocumentType: "VendorBill");
        if (!posting.Succeeded) return posting;
        var bill = new VendorBill { Id = billId, CompanyId = companyId, VendorId = request.VendorId, BillNumber = request.BillNumber.Trim(), BillDate = request.BillDate, DueDate = request.DueDate, Status = "Open", TotalAmount = request.TotalAmount, BalanceDue = request.TotalAmount };
        db.VendorBills.Add(bill);
        var vendor = await db.Vendors.SingleAsync(x => x.Id == request.VendorId, cancellationToken);
        vendor.OpenBalance += request.TotalAmount;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return TransactionResult.Failure("Bill number already exists or was posted concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(bill.Id);
    }

    public Task<TransactionResult> ApplyInvoicePaymentAsync(ApplyInvoicePaymentRequest request, CancellationToken cancellationToken = default) => ApplyPaymentAsync(request.InvoiceId, request.BankAccountId, request.PaymentDate, request.Amount, request.Reference, true, cancellationToken);
    public Task<TransactionResult> ApplyBillPaymentAsync(ApplyBillPaymentRequest request, CancellationToken cancellationToken = default) => ApplyPaymentAsync(request.VendorBillId, request.BankAccountId, request.PaymentDate, request.Amount, request.Reference, false, cancellationToken);

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

    private async Task<TransactionResult> ApplyPaymentAsync(Guid documentId, Guid bankAccountId, DateOnly date, decimal amount, string reference, bool receivable, CancellationToken cancellationToken)
    {
        if (amount <= 0) return TransactionResult.Failure("Payment amount must be greater than zero.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var bank = await db.BankAccounts.SingleOrDefaultAsync(x => x.Id == bankAccountId && x.CompanyId == companyId, cancellationToken);
        if (bank is null) return TransactionResult.Failure("Bank account not found.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (receivable)
        {
            var invoice = await db.SalesInvoices.SingleOrDefaultAsync(x => x.Id == documentId && x.CompanyId == companyId, cancellationToken);
            if (invoice is null || amount > invoice.BalanceDue) return TransactionResult.Failure("Payment cannot exceed the remaining invoice balance.");
            var cashAccountNumber = await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken);
            var post = await PostAsync(db, companyId, date, "Accounts Receivable", reference, "Customer payment", [new(cashAccountNumber, amount, 0, "Cash received"), new("1100", 0, amount, "Invoice payment")], cancellationToken, bank.Id, allowControlAccounts: true, sourceDocumentId: invoice.Id, sourceDocumentType: "SalesInvoice");
            if (!post.Succeeded) return post;
            invoice.BalanceDue -= amount; invoice.Status = invoice.BalanceDue == 0 ? "Paid" : "Partial"; invoice.ConcurrencyToken = Guid.NewGuid().ToString("N");
            var customer = await db.Customers.SingleAsync(x => x.Id == invoice.CustomerId, cancellationToken); customer.OpenBalance -= amount;
            bank.CurrentBalance += amount;
        }
        else
        {
            var bill = await db.VendorBills.SingleOrDefaultAsync(x => x.Id == documentId && x.CompanyId == companyId, cancellationToken);
            if (bill is null || amount > bill.BalanceDue) return TransactionResult.Failure("Payment cannot exceed the remaining bill balance.");
            var cashAccountNumber = await ResolveBankLedgerAccountNumberAsync(db, companyId, bank, cancellationToken);
            var post = await PostAsync(db, companyId, date, "Accounts Payable", reference, "Vendor payment", [new("2000", amount, 0, "Bill payment"), new(cashAccountNumber, 0, amount, "Cash paid")], cancellationToken, bank.Id, allowControlAccounts: true, sourceDocumentId: bill.Id, sourceDocumentType: "VendorBill");
            if (!post.Succeeded) return post;
            bill.BalanceDue -= amount; bill.Status = bill.BalanceDue == 0 ? "Paid" : "Partial"; bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
            var vendor = await db.Vendors.SingleAsync(x => x.Id == bill.VendorId, cancellationToken); vendor.OpenBalance -= amount;
            bank.CurrentBalance -= amount;
        }
        bank.UnreconciledAmount += amount;
        bank.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("This payment was changed by another user. Refresh the document and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(documentId);
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

    private static void AddJournalAudit(BrassLedgerDbContext db, Guid companyId, Guid? userId, string action, JournalEntry entry, object details)
    {
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = userId, Action = action, EntityType = "JournalEntry", EntityId = entry.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { entry.EntryNumber, entry.PostedOn, entry.SourceModule, entry.Reference, entry.Description, entry.TotalAmount, entry.Status, details }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
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
