using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<IReadOnlyList<ForeignCurrencyRemeasurementBatchSnapshot>> GetForeignCurrencyRemeasurementsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.LedgerManage) && !HasPermission(BrassLedgerPermissions.JournalPrepare) && !HasPermission(BrassLedgerPermissions.JournalApprove) && !HasPermission(BrassLedgerPermissions.JournalPost) && !HasPermission(BrassLedgerPermissions.JournalReverse)) return [];
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var batches = await db.ForeignCurrencyRemeasurementBatches.AsNoTracking().Where(batch => batch.CompanyId == companyId).OrderByDescending(batch => batch.AsOf).ThenBy(batch => batch.Reference).ToListAsync(cancellationToken);
        var batchIds = batches.Select(batch => batch.Id).ToArray();
        var lines = await db.ForeignCurrencyRemeasurementLines.AsNoTracking().Where(line => batchIds.Contains(line.ForeignCurrencyRemeasurementBatchId)).OrderBy(line => line.DocumentType).ThenBy(line => line.DocumentNumber).ToListAsync(cancellationToken);
        var customerIds = lines.Where(line => line.DocumentType == "Receivable").Select(line => line.CounterpartyId).Distinct().ToArray();
        var vendorIds = lines.Where(line => line.DocumentType == "Payable").Select(line => line.CounterpartyId).Distinct().ToArray();
        var counterparties = (await db.Customers.AsNoTracking().Where(item => customerIds.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
            .Concat(await db.Vendors.AsNoTracking().Where(item => vendorIds.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken)).ToDictionary(item => item.Id, item => item.Name);
        var userIds = batches.SelectMany(batch => new[] { batch.PreparedByUserId, batch.ApprovedByUserId, batch.RejectedByUserId, batch.PostedByUserId, batch.ReversedByUserId }).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName, cancellationToken);
        return batches.Select(batch => new ForeignCurrencyRemeasurementBatchSnapshot(batch.Id, batch.AsOf, batch.Reference, batch.Status, batch.NetAdjustment, batch.JournalEntryId, batch.ReversalJournalEntryId,
            batch.PreparedByUserId.HasValue ? users.GetValueOrDefault(batch.PreparedByUserId.Value) : null, batch.PreparedAtUtc,
            batch.ApprovedByUserId.HasValue ? users.GetValueOrDefault(batch.ApprovedByUserId.Value) : null, batch.ApprovedAtUtc,
            batch.RejectedByUserId.HasValue ? users.GetValueOrDefault(batch.RejectedByUserId.Value) : null, batch.RejectedAtUtc,
            batch.PostedByUserId.HasValue ? users.GetValueOrDefault(batch.PostedByUserId.Value) : null, batch.PostedAtUtc,
            batch.ReversedByUserId.HasValue ? users.GetValueOrDefault(batch.ReversedByUserId.Value) : null, batch.ReversedAtUtc, batch.ReversalDate, batch.DecisionReason, batch.ReversalReason, batch.ConcurrencyToken,
            lines.Where(line => line.ForeignCurrencyRemeasurementBatchId == batch.Id).Select(line => new ForeignCurrencyRemeasurementLineSnapshot(line.Id, line.DocumentType, line.DocumentId, line.DocumentNumber, counterparties.GetValueOrDefault(line.CounterpartyId, "Unavailable counterparty"), line.TransactionCurrency, line.TransactionBalance, line.PreviousBaseBalance, line.RemeasuredBaseBalance, line.AdjustmentAmount, line.ExchangeRateId, line.ExchangeRateToBase, line.ExchangeRateEffectiveOn, line.ExchangeRateSource, line.ExchangeRateSourceReference)).ToArray())).ToArray();
    }

    public async Task<TransactionResult> PrepareForeignCurrencyRemeasurementAsync(PrepareForeignCurrencyRemeasurementRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalPrepare)) return TransactionResult.Failure("You are not authorized to prepare foreign-currency remeasurements.");
        var reference = request.Reference?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 100) return TransactionResult.Failure("A remeasurement reference of at most 100 characters is required.");
        var selections = request.Rates?.ToArray() ?? [];
        if (selections.Any(selection => selection.ExchangeRateId == Guid.Empty || NormalizeCurrencyCode(selection.Currency) is null) || selections.GroupBy(selection => NormalizeCurrencyCode(selection.Currency)).Any(group => group.Count() != 1))
            return TransactionResult.Failure("Provide one retained closing rate for each selected three-letter transaction currency.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (await IsClosedPeriodAsync(db, companyId, request.AsOf, cancellationToken)) return TransactionResult.Failure("The remeasurement date is in a closed accounting period.");
        if (await db.ForeignCurrencyRemeasurementBatches.AnyAsync(batch => batch.CompanyId == companyId && batch.Reference == reference, cancellationToken)) return TransactionResult.Failure("That remeasurement reference already exists.");
        if (await db.ForeignCurrencyRemeasurementBatches.AnyAsync(batch => batch.CompanyId == companyId && batch.AsOf == request.AsOf && batch.Status != "Rejected" && batch.Status != "Reversed", cancellationToken)) return TransactionResult.Failure("A retained remeasurement already covers that reporting date.");
        if (await db.ForeignCurrencyRemeasurementBatches.AnyAsync(batch => batch.CompanyId == companyId && batch.AsOf > request.AsOf && batch.Status == "Posted", cancellationToken)) return TransactionResult.Failure("A later foreign-currency remeasurement is already posted. Reverse later batches in descending date order before preparing this historical date.");
        var baseCurrency = await db.Companies.AsNoTracking().Where(company => company.Id == companyId).Select(company => company.BaseCurrency).SingleAsync(cancellationToken);
        var invoices = await db.SalesInvoices.Where(invoice => invoice.CompanyId == companyId && invoice.InvoiceDate <= request.AsOf && invoice.TransactionCurrency != baseCurrency).ToListAsync(cancellationToken);
        var bills = await db.VendorBills.Where(bill => bill.CompanyId == companyId && bill.BillDate <= request.AsOf && bill.TransactionCurrency != baseCurrency).ToListAsync(cancellationToken);
        var documentIds = invoices.Select(invoice => invoice.Id).Concat(bills.Select(bill => bill.Id)).ToArray();
        var hasFuturePaymentActivity = await (from application in db.SubledgerPaymentApplications
                                              join payment in db.SubledgerPayments on application.SubledgerPaymentId equals payment.Id
                                              where documentIds.Contains(application.DocumentId) && payment.CompanyId == companyId
                                                  && ((payment.Status == "Posted" && payment.PaymentDate > request.AsOf) || (payment.ReversalDate.HasValue && payment.ReversalDate > request.AsOf))
                                              select application.Id).AnyAsync(cancellationToken);
        var hasFutureAdjustmentActivity = await db.SubledgerAdjustments.AnyAsync(adjustment => adjustment.CompanyId == companyId && adjustment.DocumentId.HasValue && documentIds.Contains(adjustment.DocumentId.Value) && ((adjustment.Status == "Posted" && adjustment.AdjustmentDate > request.AsOf) || (adjustment.ReversalJournalEntryId.HasValue && db.JournalEntries.Any(entry => entry.Id == adjustment.ReversalJournalEntryId && entry.CompanyId == companyId && entry.PostedOn > request.AsOf))), cancellationToken);
        if (hasFuturePaymentActivity || hasFutureAdjustmentActivity) return TransactionResult.Failure("Current document balances include activity after the requested date. Prepare remeasurement before later settlements, reversals, voids, or adjustments, or use the current reporting date.");
        invoices = invoices.Where(invoice => invoice.Status != "Voided" && invoice.TransactionBalanceDue > 0m).ToList();
        bills = bills.Where(bill => bill.Status != "Voided" && bill.TransactionBalanceDue > 0m).ToList();
        if (invoices.Count + bills.Count == 0) return TransactionResult.Failure("No open foreign-currency receivables or payables require remeasurement on that date.");
        var currencies = invoices.Select(invoice => invoice.TransactionCurrency).Concat(bills.Select(bill => bill.TransactionCurrency)).Distinct(StringComparer.Ordinal).OrderBy(currency => currency).ToArray();
        if (currencies.Any(currency => selections.All(selection => NormalizeCurrencyCode(selection.Currency) != currency)) || selections.Any(selection => !currencies.Contains(NormalizeCurrencyCode(selection.Currency), StringComparer.Ordinal)))
            return TransactionResult.Failure($"Select exactly one closing rate for every open currency: {string.Join(", ", currencies)}.");
        var rates = new Dictionary<string, TransactionRate>(StringComparer.Ordinal);
        foreach (var currency in currencies)
        {
            var selection = selections.Single(item => NormalizeCurrencyCode(item.Currency) == currency);
            var (rate, error) = await ResolveTransactionRateAsync(db, companyId, currency, selection.ExchangeRateId, request.AsOf, cancellationToken);
            if (error is not null) return TransactionResult.Failure($"{currency}: {error}");
            rates[currency] = rate!;
        }
        var batch = new ForeignCurrencyRemeasurementBatch { Id = Guid.NewGuid(), CompanyId = companyId, AsOf = request.AsOf, Reference = reference, PreparedByUserId = ResolveUserId(), PreparedAtUtc = DateTimeOffset.UtcNow, ConcurrencyToken = Guid.NewGuid().ToString("N") };
        var retainedLines = invoices.Select(invoice => CreateRemeasurementLine(batch.Id, "Receivable", invoice.Id, invoice.InvoiceNumber, invoice.CustomerId, invoice.TransactionCurrency, invoice.TransactionBalanceDue, invoice.BalanceDue, rates[invoice.TransactionCurrency]))
            .Concat(bills.Select(bill => CreateRemeasurementLine(batch.Id, "Payable", bill.Id, bill.BillNumber, bill.VendorId, bill.TransactionCurrency, bill.TransactionBalanceDue, bill.BalanceDue, rates[bill.TransactionCurrency]))).ToArray();
        batch.NetAdjustment = retainedLines.Sum(line => line.DocumentType == "Receivable" ? line.AdjustmentAmount : -line.AdjustmentAmount);
        db.ForeignCurrencyRemeasurementBatches.Add(batch); db.ForeignCurrencyRemeasurementLines.AddRange(retainedLines);
        AddRemeasurementAudit(db, batch, "foreign-currency-remeasurement.prepared", new { lineCount = retainedLines.Length, currencies, batch.NetAdjustment });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return TransactionResult.Failure("The remeasurement date or reference was retained concurrently. Refresh and try again."); }
        return TransactionResult.Success(batch.Id);
    }

    public async Task<TransactionResult> DecideForeignCurrencyRemeasurementAsync(DecideForeignCurrencyRemeasurementRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalApprove)) return TransactionResult.Failure("You are not authorized to review foreign-currency remeasurements.");
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (!request.Approve && string.IsNullOrWhiteSpace(reason)) return TransactionResult.Failure("A rejection reason is required.");
        if (reason.Length > 1000) return TransactionResult.Failure("The review reason cannot exceed 1,000 characters.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var batch = await db.ForeignCurrencyRemeasurementBatches.SingleOrDefaultAsync(item => item.Id == request.BatchId && item.CompanyId == companyId, cancellationToken);
        if (batch is null || batch.Status != "Draft") return TransactionResult.Failure("Only a draft remeasurement can be reviewed.");
        if (batch.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The remeasurement changed after it was displayed. Refresh before reviewing it.");
        var userId = ResolveUserId(); if (userId.HasValue && batch.PreparedByUserId == userId) return TransactionResult.Failure("The person who prepared a remeasurement cannot review it.");
        batch.Status = request.Approve ? "Approved" : "Rejected"; batch.DecisionReason = reason; batch.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (request.Approve) { batch.ApprovedByUserId = userId; batch.ApprovedAtUtc = DateTimeOffset.UtcNow; }
        else { batch.RejectedByUserId = userId; batch.RejectedAtUtc = DateTimeOffset.UtcNow; }
        AddRemeasurementAudit(db, batch, request.Approve ? "foreign-currency-remeasurement.approved" : "foreign-currency-remeasurement.rejected", new { reason });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The remeasurement changed during review. Refresh and try again."); }
        return TransactionResult.Success(batch.Id);
    }

    public async Task<TransactionResult> PostForeignCurrencyRemeasurementAsync(PostForeignCurrencyRemeasurementRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalPost)) return TransactionResult.Failure("You are not authorized to post foreign-currency remeasurements.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.ForeignCurrencyRemeasurementBatches.SingleOrDefaultAsync(item => item.Id == request.BatchId && item.CompanyId == companyId, cancellationToken);
        if (batch is null || batch.Status != "Approved") return TransactionResult.Failure("Only an approved remeasurement can be posted.");
        if (batch.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The remeasurement changed after it was displayed. Refresh before posting it.");
        var userId = ResolveUserId(); if (userId.HasValue && batch.ApprovedByUserId == userId) return TransactionResult.Failure("The person who approved a remeasurement cannot post it.");
        if (await IsClosedPeriodAsync(db, companyId, batch.AsOf, cancellationToken)) return TransactionResult.Failure("The remeasurement date is in a closed accounting period.");
        var lines = await db.ForeignCurrencyRemeasurementLines.Where(line => line.ForeignCurrencyRemeasurementBatchId == batch.Id).ToListAsync(cancellationToken);
        var invoiceIds = lines.Where(line => line.DocumentType == "Receivable").Select(line => line.DocumentId).ToArray(); var billIds = lines.Where(line => line.DocumentType == "Payable").Select(line => line.DocumentId).ToArray();
        var invoices = await db.SalesInvoices.Where(invoice => invoiceIds.Contains(invoice.Id) && invoice.CompanyId == companyId).ToDictionaryAsync(invoice => invoice.Id, cancellationToken); var bills = await db.VendorBills.Where(bill => billIds.Contains(bill.Id) && bill.CompanyId == companyId).ToDictionaryAsync(bill => bill.Id, cancellationToken);
        if (invoices.Count != invoiceIds.Length || bills.Count != billIds.Length || lines.Any(line => line.DocumentType == "Receivable" ? invoices[line.DocumentId].TransactionBalanceDue != line.TransactionBalance || invoices[line.DocumentId].BalanceDue != line.PreviousBaseBalance : bills[line.DocumentId].TransactionBalanceDue != line.TransactionBalance || bills[line.DocumentId].BalanceDue != line.PreviousBaseBalance))
            return TransactionResult.Failure("An open document balance changed after preparation. Prepare a replacement remeasurement from current balances.");
        var postingLines = BuildRemeasurementJournalLines(lines);
        if (postingLines.Count > 0)
        {
            var posting = await PostAsync(db, companyId, batch.AsOf, "Foreign Currency", batch.Reference, $"Foreign-currency remeasurement through {batch.AsOf}", postingLines, cancellationToken, allowControlAccounts: true, sourceDocumentId: batch.Id, sourceDocumentType: nameof(ForeignCurrencyRemeasurementBatch), resolveOperationalRoles: true);
            if (!posting.Succeeded) return posting; batch.JournalEntryId = posting.Id;
        }
        foreach (var line in lines)
        {
            if (line.DocumentType == "Receivable") { var invoice = invoices[line.DocumentId]; invoice.BalanceDue = line.RemeasuredBaseBalance; invoice.ConcurrencyToken = Guid.NewGuid().ToString("N"); (await db.Customers.SingleAsync(customer => customer.Id == invoice.CustomerId && customer.CompanyId == companyId, cancellationToken)).OpenBalance += line.AdjustmentAmount; }
            else { var bill = bills[line.DocumentId]; bill.BalanceDue = line.RemeasuredBaseBalance; bill.ConcurrencyToken = Guid.NewGuid().ToString("N"); (await db.Vendors.SingleAsync(vendor => vendor.Id == bill.VendorId && vendor.CompanyId == companyId, cancellationToken)).OpenBalance += line.AdjustmentAmount; }
        }
        batch.Status = "Posted"; batch.PostedByUserId = userId; batch.PostedAtUtc = DateTimeOffset.UtcNow; batch.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddRemeasurementAudit(db, batch, "foreign-currency-remeasurement.posted", new { lineCount = lines.Count, batch.NetAdjustment, batch.JournalEntryId });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("A document or remeasurement changed while posting. Refresh and prepare a replacement."); }
        await transaction.CommitAsync(cancellationToken); return TransactionResult.Success(batch.Id);
    }

    public async Task<TransactionResult> ReverseForeignCurrencyRemeasurementAsync(ReverseForeignCurrencyRemeasurementRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.JournalReverse)) return TransactionResult.Failure("You are not authorized to reverse foreign-currency remeasurements.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000) return TransactionResult.Failure("A reversal reason of at most 1,000 characters is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.ForeignCurrencyRemeasurementBatches.SingleOrDefaultAsync(item => item.Id == request.BatchId && item.CompanyId == companyId, cancellationToken);
        if (batch is null || batch.Status != "Posted") return TransactionResult.Failure("Only a posted remeasurement can be reversed.");
        if (batch.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The remeasurement changed after it was displayed. Refresh before reversing it.");
        if (request.ReversalDate < batch.AsOf) return TransactionResult.Failure("The reversal date cannot precede the remeasurement date.");
        if (await IsClosedPeriodAsync(db, companyId, batch.AsOf, cancellationToken)) return TransactionResult.Failure("Reopen the remeasured accounting period before reversing its period-end adjustment.");
        if (await db.ForeignCurrencyRemeasurementBatches.AnyAsync(item => item.CompanyId == companyId && item.AsOf > batch.AsOf && item.Status == "Posted", cancellationToken)) return TransactionResult.Failure("Reverse later posted foreign-currency remeasurements in descending date order before reversing this batch.");
        var latestLaterReversalDate = await db.ForeignCurrencyRemeasurementBatches.Where(item => item.CompanyId == companyId && item.AsOf > batch.AsOf && item.Status == "Reversed").MaxAsync(item => item.ReversalDate, cancellationToken);
        if (latestLaterReversalDate.HasValue && request.ReversalDate < latestLaterReversalDate.Value) return TransactionResult.Failure($"The reversal date cannot precede the {latestLaterReversalDate:yyyy-MM-dd} reversal of a later remeasurement.");
        var lines = await db.ForeignCurrencyRemeasurementLines.Where(line => line.ForeignCurrencyRemeasurementBatchId == batch.Id).ToListAsync(cancellationToken); var invoiceIds = lines.Where(line => line.DocumentType == "Receivable").Select(line => line.DocumentId).ToArray(); var billIds = lines.Where(line => line.DocumentType == "Payable").Select(line => line.DocumentId).ToArray();
        var invoices = await db.SalesInvoices.Where(invoice => invoiceIds.Contains(invoice.Id) && invoice.CompanyId == companyId).ToDictionaryAsync(invoice => invoice.Id, cancellationToken); var bills = await db.VendorBills.Where(bill => billIds.Contains(bill.Id) && bill.CompanyId == companyId).ToDictionaryAsync(bill => bill.Id, cancellationToken);
        if (invoices.Count != invoiceIds.Length || bills.Count != billIds.Length || lines.Any(line => line.DocumentType == "Receivable" ? invoices[line.DocumentId].TransactionBalanceDue != line.TransactionBalance || invoices[line.DocumentId].BalanceDue != line.RemeasuredBaseBalance : bills[line.DocumentId].TransactionBalanceDue != line.TransactionBalance || bills[line.DocumentId].BalanceDue != line.RemeasuredBaseBalance))
            return TransactionResult.Failure("A remeasured document has subsequent activity. Reverse that later activity before reversing this remeasurement.");
        if (batch.JournalEntryId.HasValue) { var reversal = await PostInverseAsync(db, companyId, batch.JournalEntryId.Value, request.ReversalDate, $"REV-{batch.Reference}", request.Reason.Trim(), batch.Id, "ForeignCurrencyRemeasurementReversal", null, cancellationToken); if (!reversal.Succeeded) return reversal; batch.ReversalJournalEntryId = reversal.Id; }
        foreach (var line in lines)
        {
            if (line.DocumentType == "Receivable") { var invoice = invoices[line.DocumentId]; invoice.BalanceDue = line.PreviousBaseBalance; invoice.ConcurrencyToken = Guid.NewGuid().ToString("N"); (await db.Customers.SingleAsync(customer => customer.Id == invoice.CustomerId && customer.CompanyId == companyId, cancellationToken)).OpenBalance -= line.AdjustmentAmount; }
            else { var bill = bills[line.DocumentId]; bill.BalanceDue = line.PreviousBaseBalance; bill.ConcurrencyToken = Guid.NewGuid().ToString("N"); (await db.Vendors.SingleAsync(vendor => vendor.Id == bill.VendorId && vendor.CompanyId == companyId, cancellationToken)).OpenBalance -= line.AdjustmentAmount; }
        }
        batch.Status = "Reversed"; batch.ReversedByUserId = ResolveUserId(); batch.ReversedAtUtc = DateTimeOffset.UtcNow; batch.ReversalDate = request.ReversalDate; batch.ReversalReason = request.Reason.Trim(); batch.ConcurrencyToken = Guid.NewGuid().ToString("N"); AddRemeasurementAudit(db, batch, "foreign-currency-remeasurement.reversed", new { batch.ReversalJournalEntryId, batch.ReversalDate, batch.ReversalReason });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("A document or remeasurement changed while reversing. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken); return TransactionResult.Success(batch.Id);
    }

    private static ForeignCurrencyRemeasurementLine CreateRemeasurementLine(Guid batchId, string documentType, Guid documentId, string documentNumber, Guid counterpartyId, string currency, decimal transactionBalance, decimal previousBaseBalance, TransactionRate rate)
    {
        var remeasured = rate.ToBase(transactionBalance);
        return new() { Id = Guid.NewGuid(), ForeignCurrencyRemeasurementBatchId = batchId, DocumentType = documentType, DocumentId = documentId, DocumentNumber = documentNumber, CounterpartyId = counterpartyId, TransactionCurrency = currency, TransactionBalance = transactionBalance, PreviousBaseBalance = previousBaseBalance, RemeasuredBaseBalance = remeasured, AdjustmentAmount = remeasured - previousBaseBalance, ExchangeRateId = rate.ExchangeRateId!.Value, ExchangeRateToBase = rate.FactorToBase, ExchangeRateEffectiveOn = rate.EffectiveOn!.Value, ExchangeRateSource = rate.Source, ExchangeRateSourceReference = rate.SourceReference };
    }

    private static IReadOnlyList<JournalLineRequest> BuildRemeasurementJournalLines(IReadOnlyList<ForeignCurrencyRemeasurementLine> lines)
    {
        var result = new List<JournalLineRequest>();
        foreach (var line in lines.Where(line => line.AdjustmentAmount != 0m))
        {
            var amount = Math.Abs(line.AdjustmentAmount); var receivable = line.DocumentType == "Receivable"; var assetIncrease = receivable && line.AdjustmentAmount > 0m; var liabilityIncrease = !receivable && line.AdjustmentAmount > 0m; var gain = assetIncrease || (!receivable && line.AdjustmentAmount < 0m);
            result.Add(new(receivable ? OperationalRoleReference(AccountingAccountRoles.AccountsReceivable) : OperationalRoleReference(AccountingAccountRoles.AccountsPayable), assetIncrease || (!receivable && !liabilityIncrease) ? amount : 0m, assetIncrease || (!receivable && !liabilityIncrease) ? 0m : amount, $"Remeasure {line.DocumentNumber} at {line.ExchangeRateToBase:N10}"));
            result.Add(new(OperationalRoleReference(gain ? AccountingAccountRoles.ForeignExchangeGain : AccountingAccountRoles.ForeignExchangeLoss), gain ? 0m : amount, gain ? amount : 0m, $"Unrealized FX {(gain ? "gain" : "loss")} — {line.DocumentNumber}"));
        }
        return result;
    }

    private static string? NormalizeCurrencyCode(string? currency) { var value = currency?.Trim().ToUpperInvariant() ?? string.Empty; return value.Length == 3 && value.All(character => character is >= 'A' and <= 'Z') ? value : null; }
    private void AddRemeasurementAudit(BrassLedgerDbContext db, ForeignCurrencyRemeasurementBatch batch, string action, object detail) => db.BusinessAuditEntries.Add(new() { Id = Guid.NewGuid(), CompanyId = batch.CompanyId, UserId = ResolveUserId(), Action = action, EntityType = nameof(ForeignCurrencyRemeasurementBatch), EntityId = batch.Id, DetailJson = JsonSerializer.Serialize(new { batch.AsOf, batch.Reference, batch.Status, detail }), OccurredAtUtc = DateTimeOffset.UtcNow });
}
