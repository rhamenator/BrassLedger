using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    public async Task<TransactionResult> SaveSalesQuoteAsync(SaveSalesQuoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to prepare sales quotes.");
        var requestedLines = request.Lines?.ToArray() ?? [];
        if (request.CustomerId == Guid.Empty || string.IsNullOrWhiteSpace(request.QuoteNumber) || requestedLines.Length == 0)
            return TransactionResult.Failure("A customer, quote number, and at least one line are required.");
        if (request.ExpiresOn < request.QuotedOn) return TransactionResult.Failure("The quote expiration date cannot precede the quote date.");
        if (requestedLines.Any(line => line.InventoryItemId == Guid.Empty || string.IsNullOrWhiteSpace(line.Description) || RoundQuantity(line.Quantity) <= 0m || line.UnitPrice < 0m || line.DiscountAmount < 0m || line.DiscountAmount > RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice) || line.TaxAmount < 0m || string.IsNullOrWhiteSpace(line.RevenueAccountNumber)))
            return TransactionResult.Failure("Every quote line requires an item, description, positive quantity, valid price and discount, non-negative tax, and revenue account.");
        if (requestedLines.Select(line => line.InventoryItemId).Distinct().Count() != requestedLines.Length)
            return TransactionResult.Failure("Combine duplicate inventory items into one quote line.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.Customers.AnyAsync(customer => customer.Id == request.CustomerId && customer.CompanyId == companyId, cancellationToken))
            return TransactionResult.Failure("Customer not found in the active company.");
        var itemIds = requestedLines.Select(line => line.InventoryItemId).ToArray();
        if (await db.InventoryItems.CountAsync(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id), cancellationToken) != itemIds.Length)
            return TransactionResult.Failure("Every quote item must be active in the current company.");
        if (!await AreActiveProjectDimensionsAsync(db, companyId, requestedLines.Select(line => (line.ProjectJobId, line.ProjectPhaseId, line.ProjectCostCodeId)), cancellationToken))
            return TransactionResult.Failure("Every quoted project must be active and belong to this company.");
        if (!await AreActiveTrackingDimensionsAsync(db, companyId, request.QuotedOn, requestedLines.Select(line => (line.DepartmentId, line.ClassId)), cancellationToken))
            return TransactionResult.Failure("Every quote department and class must be active, effective on the quote date, correctly typed, and belong to this company.");
        var revenueNumbers = requestedLines.Select(line => line.RevenueAccountNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var revenueAccounts = await db.Accounts
            .Where(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Revenue && !account.IsControlAccount && revenueNumbers.Contains(account.Number))
            .ToDictionaryAsync(account => account.Number, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (revenueAccounts.Count != revenueNumbers.Length) return TransactionResult.Failure("Every quote distribution must use an active, non-control revenue account.");
        var quoteNumber = request.QuoteNumber.Trim();
        if (await db.SalesQuotes.AnyAsync(quote => quote.CompanyId == companyId && quote.QuoteNumber == quoteNumber && quote.Id != request.Id, cancellationToken))
            return TransactionResult.Failure("Quote number already exists.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        SalesQuote quote;
        if (request.Id.HasValue)
        {
            quote = await db.SalesQuotes.SingleOrDefaultAsync(candidate => candidate.Id == request.Id.Value && candidate.CompanyId == companyId, cancellationToken) ?? new SalesQuote();
            if (quote.Id == Guid.Empty) return TransactionResult.Failure("Sales quote not found.");
            if (quote.Status != "Draft") return TransactionResult.Failure("Only a draft quote can be edited.");
            if (!string.Equals(quote.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The quote changed after it was opened. Refresh and review it again.");
            db.SalesQuoteLines.RemoveRange(await db.SalesQuoteLines.Where(line => line.SalesQuoteId == quote.Id).ToListAsync(cancellationToken));
        }
        else
        {
            quote = new SalesQuote { Id = Guid.NewGuid(), CompanyId = companyId, PreparedByUserId = ResolveUserId(), PreparedAtUtc = DateTimeOffset.UtcNow };
            db.SalesQuotes.Add(quote);
        }

        quote.CustomerId = request.CustomerId;
        quote.QuoteNumber = quoteNumber;
        quote.QuotedOn = request.QuotedOn;
        quote.ExpiresOn = request.ExpiresOn;
        quote.Status = "Draft";
        quote.Notes = request.Notes?.Trim() ?? string.Empty;
        quote.TotalAmount = requestedLines.Sum(line => RoundCurrency(RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice) - RoundCurrency(line.DiscountAmount) + RoundCurrency(line.TaxAmount)));
        if (quote.TotalAmount <= 0m) return TransactionResult.Failure("The quote total must be greater than zero.");
        quote.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.SalesQuoteLines.AddRange(requestedLines.Select((line, index) => new SalesQuoteLine
        {
            Id = Guid.NewGuid(),
            SalesQuoteId = quote.Id,
            Sequence = index + 1,
            InventoryItemId = line.InventoryItemId,
            RevenueAccountId = revenueAccounts[line.RevenueAccountNumber.Trim()].Id,
            ProjectJobId = line.ProjectJobId,
            ProjectPhaseId = line.ProjectPhaseId,
            ProjectCostCodeId = line.ProjectCostCodeId,
            DepartmentId = line.DepartmentId,
            ClassId = line.ClassId,
            Description = line.Description.Trim(),
            Quantity = RoundQuantity(line.Quantity),
            UnitPrice = RoundCurrency(line.UnitPrice),
            DiscountAmount = RoundCurrency(line.DiscountAmount),
            TaxAmount = RoundCurrency(line.TaxAmount),
            LineTotal = RoundCurrency(RoundQuantity(line.Quantity) * RoundCurrency(line.UnitPrice) - RoundCurrency(line.DiscountAmount) + RoundCurrency(line.TaxAmount))
        }));
        AddSalesQuoteAudit(db, companyId, "sales-quote.draft.saved", quote.Id, new { quote.QuoteNumber, quote.CustomerId, lineCount = requestedLines.Length, quote.TotalAmount, quote.ExpiresOn });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The quote changed while it was being saved. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The quote number or lines changed concurrently. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(quote.Id);
    }

    public async Task<TransactionResult> ApproveSalesQuoteAsync(ApproveSalesQuoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to approve sales quotes.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var quote = await db.SalesQuotes.SingleOrDefaultAsync(candidate => candidate.Id == request.SalesQuoteId && candidate.CompanyId == companyId, cancellationToken);
        if (quote is null) return TransactionResult.Failure("Sales quote not found.");
        if (quote.Status != "Draft") return TransactionResult.Failure("Only a draft quote can be approved.");
        if (!string.Equals(quote.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The quote changed after it was opened. Refresh and review it again.");
        if (quote.ExpiresOn < DateOnly.FromDateTime(DateTime.UtcNow)) return TransactionResult.Failure("The quote has expired. Extend it as a draft before approval.");
        if (!await db.SalesQuoteLines.AnyAsync(line => line.SalesQuoteId == quote.Id, cancellationToken)) return TransactionResult.Failure("A quote must contain at least one line before approval.");
        quote.Status = "Approved";
        quote.ApprovedByUserId = ResolveUserId();
        quote.ApprovedAtUtc = DateTimeOffset.UtcNow;
        quote.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesQuoteAudit(db, companyId, "sales-quote.approved", quote.Id, new { quote.QuoteNumber, quote.TotalAmount, quote.ExpiresOn });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The quote changed while it was being approved. Refresh and try again."); }
        return TransactionResult.Success(quote.Id);
    }

    public async Task<TransactionResult> WithdrawSalesQuoteAsync(WithdrawSalesQuoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to withdraw sales quotes.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return TransactionResult.Failure("A quote withdrawal reason is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var quote = await db.SalesQuotes.SingleOrDefaultAsync(candidate => candidate.Id == request.SalesQuoteId && candidate.CompanyId == companyId, cancellationToken);
        if (quote is null) return TransactionResult.Failure("Sales quote not found.");
        if (quote.Status is not ("Draft" or "Approved")) return TransactionResult.Failure("Only a draft or approved quote can be withdrawn.");
        if (!string.Equals(quote.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The quote changed after it was opened. Refresh and review it again.");
        quote.Status = "Withdrawn";
        quote.WithdrawalReason = request.Reason.Trim();
        quote.WithdrawnByUserId = ResolveUserId();
        quote.WithdrawnAtUtc = DateTimeOffset.UtcNow;
        quote.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesQuoteAudit(db, companyId, "sales-quote.withdrawn", quote.Id, new { quote.QuoteNumber, quote.WithdrawalReason });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The quote changed while it was being withdrawn. Refresh and try again."); }
        return TransactionResult.Success(quote.Id);
    }

    public async Task<TransactionResult> ConvertSalesQuoteAsync(ConvertSalesQuoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.SalesManage)) return TransactionResult.Failure("You are not authorized to convert sales quotes.");
        if (string.IsNullOrWhiteSpace(request.OrderNumber)) return TransactionResult.Failure("A sales-order number is required.");
        if (request.RequestedShipOn.HasValue && request.RequestedShipOn.Value < request.OrderedOn) return TransactionResult.Failure("The requested ship date cannot precede the order date.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var quote = await db.SalesQuotes.SingleOrDefaultAsync(candidate => candidate.Id == request.SalesQuoteId && candidate.CompanyId == companyId, cancellationToken);
        if (quote is null) return TransactionResult.Failure("Sales quote not found.");
        if (quote.Status != "Approved" || await db.SalesOrders.AnyAsync(order => order.SalesQuoteId == quote.Id, cancellationToken)) return TransactionResult.Failure("Only an approved, unconverted quote can become a sales order.");
        if (!string.Equals(quote.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal)) return TransactionResult.Failure("The quote changed after it was opened. Refresh and review it again.");
        if (request.OrderedOn < quote.QuotedOn) return TransactionResult.Failure("The sales-order date cannot precede the quote date.");
        if (quote.ExpiresOn < DateOnly.FromDateTime(DateTime.UtcNow) || request.OrderedOn > quote.ExpiresOn) return TransactionResult.Failure("The quote has expired and cannot be converted, including by backdating the sales order. Prepare and approve a current quote.");
        var orderNumber = request.OrderNumber.Trim();
        if (await db.SalesOrders.AnyAsync(order => order.CompanyId == companyId && order.OrderNumber == orderNumber, cancellationToken)) return TransactionResult.Failure("Sales-order number already exists.");
        if (!await db.Customers.AnyAsync(customer => customer.Id == quote.CustomerId && customer.CompanyId == companyId, cancellationToken)) return TransactionResult.Failure("The quote customer is unavailable in this company.");
        var quoteLines = await db.SalesQuoteLines.Where(line => line.SalesQuoteId == quote.Id).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        if (quoteLines.Count == 0) return TransactionResult.Failure("The quote has no lines to convert.");
        var itemIds = quoteLines.Select(line => line.InventoryItemId).Distinct().ToArray();
        if (await db.InventoryItems.CountAsync(item => item.CompanyId == companyId && item.IsActive && itemIds.Contains(item.Id), cancellationToken) != itemIds.Length) return TransactionResult.Failure("One or more quoted items are no longer active in this company.");
        var revenueAccountIds = quoteLines.Select(line => line.RevenueAccountId).Distinct().ToArray();
        if (await db.Accounts.CountAsync(account => account.CompanyId == companyId && account.IsActive && account.Type == AccountType.Revenue && !account.IsControlAccount && revenueAccountIds.Contains(account.Id), cancellationToken) != revenueAccountIds.Length) return TransactionResult.Failure("One or more quoted revenue accounts are no longer available in this company.");
        if (!await AreActiveProjectDimensionsAsync(db, companyId, quoteLines.Select(line => (line.ProjectJobId, line.ProjectPhaseId, line.ProjectCostCodeId)), cancellationToken)) return TransactionResult.Failure("One or more quoted project dimensions are closed or no longer available in this company.");
        if (!await AreActiveTrackingDimensionsAsync(db, companyId, request.OrderedOn, quoteLines.Select(line => (line.DepartmentId, line.ClassId)), cancellationToken)) return TransactionResult.Failure("One or more quoted departments or classes are inactive, out of period, unavailable, or incorrectly typed for the sales-order date.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var order = new SalesOrder
        {
            Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = quote.CustomerId, SalesQuoteId = quote.Id, OrderNumber = orderNumber,
            OrderedOn = request.OrderedOn, RequestedShipOn = request.RequestedShipOn, Status = "Draft", TotalAmount = quote.TotalAmount,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? quote.Notes : request.Notes.Trim(), PreparedByUserId = ResolveUserId(), PreparedAtUtc = now,
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        db.SalesOrders.Add(order);
        db.SalesOrderLines.AddRange(quoteLines.Select(line => new SalesOrderLine
        {
            Id = Guid.NewGuid(), SalesOrderId = order.Id, Sequence = line.Sequence, InventoryItemId = line.InventoryItemId, RevenueAccountId = line.RevenueAccountId,
            ProjectJobId = line.ProjectJobId,
            ProjectPhaseId = line.ProjectPhaseId,
            ProjectCostCodeId = line.ProjectCostCodeId,
            DepartmentId = line.DepartmentId,
            ClassId = line.ClassId,
            Description = line.Description, OrderedQuantity = line.Quantity, UnitPrice = line.UnitPrice, DiscountAmount = line.DiscountAmount,
            TaxAmount = line.TaxAmount, LineTotal = line.LineTotal
        }));
        quote.Status = "Converted";
        quote.ConvertedByUserId = ResolveUserId();
        quote.ConvertedAtUtc = now;
        quote.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddSalesQuoteAudit(db, companyId, "sales-quote.converted", quote.Id, new { quote.QuoteNumber, salesOrderId = order.Id, order.OrderNumber, order.TotalAmount });
        AddSalesQuoteAudit(db, companyId, "sales-order.created-from-quote", order.Id, new { salesQuoteId = quote.Id, quote.QuoteNumber, order.OrderNumber, order.TotalAmount });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The quote changed while it was being converted. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The quote was converted concurrently or the sales-order number already exists. Refresh and try again."); }
        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(order.Id);
    }

    private void AddSalesQuoteAudit(BrassLedgerDbContext db, Guid companyId, string action, Guid entityId, object details) =>
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId, UserId = ResolveUserId(), Action = action, EntityType = nameof(SalesQuote), EntityId = entityId, DetailJson = JsonSerializer.Serialize(details), OccurredAtUtc = DateTimeOffset.UtcNow });
}
