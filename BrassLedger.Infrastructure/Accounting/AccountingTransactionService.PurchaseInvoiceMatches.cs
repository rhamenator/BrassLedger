using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    private static readonly string[] ReservingPurchaseInvoiceMatchStatuses = ["Draft", "Submitted", "Approved"];

    public async Task<TransactionResult> SavePurchaseInvoiceMatchAsync(
        SavePurchaseInvoiceMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to prepare purchase-invoice matches.");

        var requestedLines = request.Lines?.Where(line => line.Quantity != 0m).ToArray() ?? [];
        if (request.InventoryReceiptId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.BillNumber)
            || string.IsNullOrWhiteSpace(request.Description)
            || request.DueDate < request.BillDate
            || requestedLines.Length == 0)
            return TransactionResult.Failure("A receipt, bill number, description, valid dates, and at least one invoice line are required.");
        if (requestedLines.Any(line =>
                line.InventoryReceiptLineId == Guid.Empty
                || RoundQuantity(line.Quantity) <= 0m
                || RoundCurrency(line.UnitCost) < 0m))
            return TransactionResult.Failure("Every invoice line requires a receipt line, positive quantity, and nonnegative unit cost.");
        if (requestedLines.Select(line => line.InventoryReceiptLineId).Distinct().Count() != requestedLines.Length)
            return TransactionResult.Failure("Combine duplicate invoice lines for the same receipt line.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var receipt = await db.InventoryReceipts.SingleOrDefaultAsync(
            item => item.Id == request.InventoryReceiptId && item.CompanyId == companyId,
            cancellationToken);
        if (receipt is null || receipt.Status != "Posted")
            return TransactionResult.Failure("Select a posted inventory receipt in this company.");
        if (!string.Equals(receipt.ConcurrencyToken, request.ReceiptConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The receipt changed after the invoice match was opened. Refresh and review it again.");
        if (request.BillDate < receipt.ReceivedOn)
            return TransactionResult.Failure("The vendor bill date cannot precede the receipt date.");

        var order = await db.PurchaseOrders.SingleAsync(
            item => item.Id == receipt.PurchaseOrderId && item.CompanyId == companyId,
            cancellationToken);
        var vendor = await db.Vendors.SingleAsync(
            item => item.Id == order.VendorId && item.CompanyId == companyId,
            cancellationToken);
        var billNumber = request.BillNumber.Trim();
        if (await db.VendorBills.AnyAsync(
                bill => bill.CompanyId == companyId && bill.BillNumber == billNumber,
                cancellationToken)
            || await db.PurchaseInvoiceMatches.AnyAsync(
                match => match.CompanyId == companyId && match.BillNumber == billNumber && match.Id != request.Id,
                cancellationToken))
            return TransactionResult.Failure("Vendor bill number already exists.");

        var receiptLines = await db.InventoryReceiptLines
            .Where(line => line.InventoryReceiptId == receipt.Id)
            .OrderBy(line => line.Sequence)
            .ToListAsync(cancellationToken);
        var requestedIds = requestedLines.Select(line => line.InventoryReceiptLineId).ToArray();
        if (receiptLines.Count(line => requestedIds.Contains(line.Id)) != requestedIds.Length)
            return TransactionResult.Failure("Every invoice line must belong to the selected receipt.");

        var postedMatchQuantities = await (
            from billLine in db.VendorBillLines
            join postedBill in db.VendorBills on billLine.VendorBillId equals postedBill.Id
            where postedBill.CompanyId == companyId
                && postedBill.InventoryReceiptId == receipt.Id
                && postedBill.Status != "Voided"
                && billLine.InventoryReceiptLineId.HasValue
            select new { ReceiptLineId = billLine.InventoryReceiptLineId.GetValueOrDefault(), billLine.MatchedQuantity })
            .ToListAsync(cancellationToken);
        var reservedMatchQuantities = await (
            from line in db.PurchaseInvoiceMatchLines
            join matchedDocument in db.PurchaseInvoiceMatches on line.PurchaseInvoiceMatchId equals matchedDocument.Id
            where matchedDocument.CompanyId == companyId
                && matchedDocument.InventoryReceiptId == receipt.Id
                && matchedDocument.Id != request.Id
                && ReservingPurchaseInvoiceMatchStatuses.Contains(matchedDocument.Status)
            select new { line.InventoryReceiptLineId, line.MatchedQuantity })
            .ToListAsync(cancellationToken);

        var calculatedLines = requestedLines
            .Select((requested, index) =>
            {
                var source = receiptLines.Single(line => line.Id == requested.InventoryReceiptLineId);
                var retainedQuantity = source.Quantity - source.ReturnedQuantity;
                var alreadyMatched = postedMatchQuantities
                    .Where(line => line.ReceiptLineId == source.Id)
                    .Sum(line => line.MatchedQuantity);
                var alreadyReserved = reservedMatchQuantities
                    .Where(line => line.InventoryReceiptLineId == source.Id)
                    .Sum(line => line.MatchedQuantity);
                var availableQuantity = Math.Max(0m, RoundQuantity(retainedQuantity - alreadyMatched - alreadyReserved));
                var invoiceQuantity = RoundQuantity(requested.Quantity);
                var invoiceUnitCost = RoundCurrency(requested.UnitCost);
                var matchedQuantity = Math.Min(invoiceQuantity, availableQuantity);
                var quantityVarianceQuantity = invoiceQuantity - matchedQuantity;
                var accrualAmount = RoundCurrency(matchedQuantity * source.UnitCost);
                var invoiceAmount = RoundCurrency(invoiceQuantity * invoiceUnitCost);
                var matchedInvoiceAmount = RoundCurrency(matchedQuantity * invoiceUnitCost);
                var priceVarianceAmount = matchedInvoiceAmount - accrualAmount;
                var quantityVarianceAmount = invoiceAmount - matchedInvoiceAmount;
                return new CalculatedPurchaseInvoiceMatchLine(
                    source,
                    index + 1,
                    availableQuantity,
                    invoiceQuantity,
                    matchedQuantity,
                    quantityVarianceQuantity,
                    invoiceUnitCost,
                    accrualAmount,
                    invoiceAmount,
                    priceVarianceAmount,
                    quantityVarianceAmount);
            })
            .ToArray();
        var invoiceAmount = calculatedLines.Sum(line => line.InvoiceAmount);
        if (invoiceAmount <= 0m)
            return TransactionResult.Failure("The vendor invoice total must be greater than zero.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        PurchaseInvoiceMatch match;
        if (request.Id.HasValue)
        {
            match = await db.PurchaseInvoiceMatches.SingleOrDefaultAsync(
                    item => item.Id == request.Id.Value && item.CompanyId == companyId,
                    cancellationToken)
                ?? new();
            if (match.Id == Guid.Empty)
                return TransactionResult.Failure("Purchase-invoice match not found.");
            if (match.Status is not ("Draft" or "Rejected"))
                return TransactionResult.Failure("Only a draft or rejected purchase-invoice match can be edited.");
            if (!string.Equals(match.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
                return TransactionResult.Failure("The invoice match changed after it was opened. Refresh and review it again.");

            db.PurchaseInvoiceMatchLines.RemoveRange(await db.PurchaseInvoiceMatchLines
                .Where(line => line.PurchaseInvoiceMatchId == match.Id)
                .ToListAsync(cancellationToken));
        }
        else
        {
            match = new PurchaseInvoiceMatch { Id = Guid.NewGuid(), CompanyId = companyId };
            db.PurchaseInvoiceMatches.Add(match);
        }

        receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        match.InventoryReceiptId = receipt.Id;
        match.PurchaseOrderId = order.Id;
        match.VendorId = vendor.Id;
        match.BillNumber = billNumber;
        match.BillDate = request.BillDate;
        match.DueDate = request.DueDate;
        match.Description = request.Description.Trim();
        match.Status = "Draft";
        match.InvoiceAmount = invoiceAmount;
        match.AccrualAmount = calculatedLines.Sum(line => line.AccrualAmount);
        match.PriceVarianceAmount = calculatedLines.Sum(line => line.PriceVarianceAmount);
        match.QuantityVarianceQuantity = calculatedLines.Sum(line => line.QuantityVarianceQuantity);
        match.QuantityVarianceAmount = calculatedLines.Sum(line => line.QuantityVarianceAmount);
        match.SourceReceiptConcurrencyToken = receipt.ConcurrencyToken;
        match.PreparedByUserId = ResolveUserId();
        match.PreparedAtUtc = DateTimeOffset.UtcNow;
        match.SubmittedByUserId = null;
        match.SubmittedAtUtc = null;
        match.DecidedByUserId = null;
        match.DecidedAtUtc = null;
        match.DecisionReason = string.Empty;
        match.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.PurchaseInvoiceMatchLines.AddRange(calculatedLines.Select(line => new PurchaseInvoiceMatchLine
        {
            Id = Guid.NewGuid(),
            PurchaseInvoiceMatchId = match.Id,
            InventoryReceiptLineId = line.Source.Id,
            PurchaseOrderLineId = line.Source.PurchaseOrderLineId,
            InventoryItemId = line.Source.InventoryItemId,
            Sequence = line.Sequence,
            AvailableQuantity = line.AvailableQuantity,
            InvoiceQuantity = line.InvoiceQuantity,
            MatchedQuantity = line.MatchedQuantity,
            QuantityVarianceQuantity = line.QuantityVarianceQuantity,
            ReceiptUnitCost = line.Source.UnitCost,
            InvoiceUnitCost = line.InvoiceUnitCost,
            AccrualAmount = line.AccrualAmount,
            InvoiceAmount = line.InvoiceAmount,
            PriceVarianceAmount = line.PriceVarianceAmount,
            QuantityVarianceAmount = line.QuantityVarianceAmount
        }));
        AddPurchasingAudit(
            db,
            companyId,
            request.Id.HasValue ? "purchase-invoice-match.draft.updated" : "purchase-invoice-match.draft.created",
            nameof(PurchaseInvoiceMatch),
            match.Id,
            new
            {
                match.BillNumber,
                receipt.ReceiptNumber,
                match.InvoiceAmount,
                match.AccrualAmount,
                match.PriceVarianceAmount,
                match.QuantityVarianceQuantity,
                match.QuantityVarianceAmount,
                lineCount = calculatedLines.Length
            });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The receipt or invoice-match reservation changed while saving. Refresh and try again.");
        }
        catch (DbUpdateException)
        {
            return TransactionResult.Failure("The bill number or invoice-match reservation changed concurrently. Refresh and try again.");
        }

        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(match.Id);
    }

    public async Task<TransactionResult> SubmitPurchaseInvoiceMatchAsync(
        SubmitPurchaseInvoiceMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to submit purchase-invoice matches.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var match = await db.PurchaseInvoiceMatches.SingleOrDefaultAsync(
            item => item.Id == request.PurchaseInvoiceMatchId && item.CompanyId == companyId,
            cancellationToken);
        if (match is null)
            return TransactionResult.Failure("Purchase-invoice match not found.");
        if (match.Status != "Draft")
            return TransactionResult.Failure("Only a draft purchase-invoice match can be submitted.");
        if (!string.Equals(match.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The invoice match changed after it was opened. Refresh and review it again.");
        if (!await db.PurchaseInvoiceMatchLines.AnyAsync(
                line => line.PurchaseInvoiceMatchId == match.Id,
                cancellationToken))
            return TransactionResult.Failure("The invoice match has no lines.");

        match.Status = "Submitted";
        match.SubmittedByUserId = ResolveUserId();
        match.SubmittedAtUtc = DateTimeOffset.UtcNow;
        match.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(
            db,
            companyId,
            "purchase-invoice-match.submitted",
            nameof(PurchaseInvoiceMatch),
            match.Id,
            new
            {
                match.BillNumber,
                match.InvoiceAmount,
                match.AccrualAmount,
                match.PriceVarianceAmount,
                match.QuantityVarianceQuantity,
                match.QuantityVarianceAmount
            });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The invoice match changed while submitting. Refresh and try again.");
        }

        return TransactionResult.Success(match.Id);
    }

    public async Task<TransactionResult> DecidePurchaseInvoiceMatchAsync(
        DecidePurchaseInvoiceMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage))
            return TransactionResult.Failure("You are not authorized to review purchase-invoice matches.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return TransactionResult.Failure("An approval or rejection reason is required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var match = await db.PurchaseInvoiceMatches.SingleOrDefaultAsync(
            item => item.Id == request.PurchaseInvoiceMatchId && item.CompanyId == companyId,
            cancellationToken);
        if (match is null)
            return TransactionResult.Failure("Purchase-invoice match not found.");
        if (match.Status != "Submitted")
            return TransactionResult.Failure("Only a submitted purchase-invoice match can be reviewed.");
        if (!string.Equals(match.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The invoice match changed after it was opened. Refresh and review it again.");

        var decidingUserId = ResolveUserId();
        if (decidingUserId.HasValue && match.PreparedByUserId == decidingUserId)
            return TransactionResult.Failure("The person who prepared a purchase-invoice match cannot approve or reject it.");

        match.Status = request.Approve ? "Approved" : "Rejected";
        match.DecidedByUserId = decidingUserId;
        match.DecidedAtUtc = DateTimeOffset.UtcNow;
        match.DecisionReason = request.Reason.Trim();
        match.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (!request.Approve)
        {
            var receipt = await db.InventoryReceipts.SingleAsync(
                item => item.Id == match.InventoryReceiptId && item.CompanyId == companyId,
                cancellationToken);
            receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }

        AddPurchasingAudit(
            db,
            companyId,
            request.Approve ? "purchase-invoice-match.approved" : "purchase-invoice-match.rejected",
            nameof(PurchaseInvoiceMatch),
            match.Id,
            new
            {
                match.BillNumber,
                match.InvoiceAmount,
                match.AccrualAmount,
                match.PriceVarianceAmount,
                match.QuantityVarianceQuantity,
                match.QuantityVarianceAmount,
                match.DecisionReason
            });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The invoice match changed while reviewing. Refresh and try again.");
        }

        return TransactionResult.Success(match.Id);
    }

    public async Task<TransactionResult> CancelPurchaseInvoiceMatchAsync(
        CancelPurchaseInvoiceMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to cancel purchase-invoice matches.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return TransactionResult.Failure("A cancellation reason is required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var match = await db.PurchaseInvoiceMatches.SingleOrDefaultAsync(
            item => item.Id == request.PurchaseInvoiceMatchId && item.CompanyId == companyId,
            cancellationToken);
        if (match is null)
            return TransactionResult.Failure("Purchase-invoice match not found.");
        if (match.Status is not ("Draft" or "Submitted" or "Approved" or "Rejected"))
            return TransactionResult.Failure("This purchase-invoice match can no longer be cancelled.");
        if (!string.Equals(match.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The invoice match changed after it was opened. Refresh and review it again.");

        var receipt = await db.InventoryReceipts.SingleAsync(
            item => item.Id == match.InventoryReceiptId && item.CompanyId == companyId,
            cancellationToken);
        match.Status = "Cancelled";
        match.CancelledByUserId = ResolveUserId();
        match.CancelledAtUtc = DateTimeOffset.UtcNow;
        match.CancellationReason = request.Reason.Trim();
        match.ConcurrencyToken = Guid.NewGuid().ToString("N");
        receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        AddPurchasingAudit(
            db,
            companyId,
            "purchase-invoice-match.cancelled",
            nameof(PurchaseInvoiceMatch),
            match.Id,
            new { match.BillNumber, match.CancellationReason });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The invoice match changed while cancelling. Refresh and try again.");
        }

        return TransactionResult.Success(match.Id);
    }

    public async Task<TransactionResult> PostPurchaseInvoiceMatchAsync(
        PostPurchaseInvoiceMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PayablesManage))
            return TransactionResult.Failure("You are not authorized to post purchase-invoice matches.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var match = await db.PurchaseInvoiceMatches.SingleOrDefaultAsync(
            item => item.Id == request.PurchaseInvoiceMatchId && item.CompanyId == companyId,
            cancellationToken);
        if (match is null)
            return TransactionResult.Failure("Purchase-invoice match not found.");
        if (match.Status != "Approved")
            return TransactionResult.Failure("Only an approved purchase-invoice match can be posted.");
        if (!string.Equals(match.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The invoice match changed after it was opened. Refresh and review it again.");

        var postingUserId = ResolveUserId();
        if (postingUserId.HasValue && match.DecidedByUserId == postingUserId)
            return TransactionResult.Failure("The person who reviewed a purchase-invoice match cannot post it.");

        var receipt = await db.InventoryReceipts.SingleAsync(
            item => item.Id == match.InventoryReceiptId && item.CompanyId == companyId,
            cancellationToken);
        if (receipt.Status != "Posted")
            return TransactionResult.Failure("The source receipt is no longer posted. Return the invoice match to draft and review it again.");
        if (await db.VendorBills.AnyAsync(
                bill => bill.CompanyId == companyId && bill.BillNumber == match.BillNumber,
                cancellationToken))
            return TransactionResult.Failure("Vendor bill number already exists.");

        var lines = await db.PurchaseInvoiceMatchLines
            .Where(line => line.PurchaseInvoiceMatchId == match.Id)
            .OrderBy(line => line.Sequence)
            .ToListAsync(cancellationToken);
        if (lines.Count == 0
            || lines.Sum(line => line.InvoiceAmount) != match.InvoiceAmount
            || lines.Sum(line => line.AccrualAmount) != match.AccrualAmount
            || lines.Sum(line => line.PriceVarianceAmount) != match.PriceVarianceAmount
            || lines.Sum(line => line.QuantityVarianceQuantity) != match.QuantityVarianceQuantity
            || lines.Sum(line => line.QuantityVarianceAmount) != match.QuantityVarianceAmount
            || match.InvoiceAmount <= 0m)
            return TransactionResult.Failure("The approved invoice-match detail no longer reconciles. Return it to draft and review it again.");

        var receiptLines = await db.InventoryReceiptLines
            .Where(line => line.InventoryReceiptId == receipt.Id)
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        var postedMatches = await (
            from billLine in db.VendorBillLines
            join postedBill in db.VendorBills on billLine.VendorBillId equals postedBill.Id
            where postedBill.CompanyId == companyId
                && postedBill.InventoryReceiptId == receipt.Id
                && postedBill.Status != "Voided"
                && billLine.InventoryReceiptLineId.HasValue
            select new { ReceiptLineId = billLine.InventoryReceiptLineId.GetValueOrDefault(), billLine.MatchedQuantity })
            .ToListAsync(cancellationToken);
        var activeReservations = await (
            from reservedLine in db.PurchaseInvoiceMatchLines
            join reservedMatch in db.PurchaseInvoiceMatches on reservedLine.PurchaseInvoiceMatchId equals reservedMatch.Id
            where reservedMatch.CompanyId == companyId
                && reservedMatch.InventoryReceiptId == receipt.Id
                && ReservingPurchaseInvoiceMatchStatuses.Contains(reservedMatch.Status)
            select new { reservedLine.InventoryReceiptLineId, reservedLine.MatchedQuantity })
            .ToListAsync(cancellationToken);
        if (lines.Any(line => !receiptLines.ContainsKey(line.InventoryReceiptLineId))
            || receiptLines.Any(source =>
                postedMatches.Where(line => line.ReceiptLineId == source.Key).Sum(line => line.MatchedQuantity)
                + activeReservations.Where(line => line.InventoryReceiptLineId == source.Key).Sum(line => line.MatchedQuantity)
                > source.Value.Quantity - source.Value.ReturnedQuantity))
            return TransactionResult.Failure("The retained or already-invoiced receipt quantity changed after preparation. Return the invoice match to draft and recalculate it.");

        var grni = await db.Accounts.SingleOrDefaultAsync(
            account => account.CompanyId == companyId
                && account.IsActive
                && account.OperationalRole == AccountingAccountRoles.GoodsReceivedNotInvoiced,
            cancellationToken);
        if (grni is null)
            return TransactionResult.Failure("Configure an active goods received not invoiced account before posting this bill.");

        var journalLines = new List<JournalLineRequest>();
        if (match.AccrualAmount > 0m)
            journalLines.Add(new(
                OperationalRoleReference(AccountingAccountRoles.GoodsReceivedNotInvoiced),
                match.AccrualAmount,
                0m,
                "Clear matched receipt accrual"));
        var totalVariance = match.InvoiceAmount - match.AccrualAmount;
        if (totalVariance > 0m)
            journalLines.Add(new(
                OperationalRoleReference(AccountingAccountRoles.PurchasePriceVariance),
                totalVariance,
                0m,
                "Approved purchase price and quantity variance"));
        else if (totalVariance < 0m)
            journalLines.Add(new(
                OperationalRoleReference(AccountingAccountRoles.PurchasePriceVariance),
                0m,
                -totalVariance,
                "Approved favorable purchase variance"));
        journalLines.Add(new(
            OperationalRoleReference(AccountingAccountRoles.AccountsPayable),
            0m,
            match.InvoiceAmount,
            "Vendor invoice"));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var billId = Guid.NewGuid();
        var posting = await PostAsync(
            db,
            companyId,
            match.BillDate,
            "Accounts Payable",
            match.BillNumber,
            match.Description,
            journalLines,
            cancellationToken,
            allowControlAccounts: true,
            sourceDocumentId: billId,
            sourceDocumentType: "VendorBill",
            resolveOperationalRoles: true);
        if (!posting.Succeeded)
            return posting;

        var bill = new VendorBill
        {
            Id = billId,
            CompanyId = companyId,
            VendorId = match.VendorId,
            BillNumber = match.BillNumber,
            BillDate = match.BillDate,
            DueDate = match.DueDate,
            Status = "Open",
            TotalAmount = match.InvoiceAmount,
            BalanceDue = match.InvoiceAmount,
            PurchaseOrderId = match.PurchaseOrderId,
            InventoryReceiptId = receipt.Id,
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        db.VendorBills.Add(bill);
        db.VendorBillLines.AddRange(lines.Select(line => new VendorBillLine
        {
            Id = Guid.NewGuid(),
            VendorBillId = bill.Id,
            InventoryReceiptLineId = line.InventoryReceiptLineId,
            Sequence = line.Sequence,
            ExpenseAccountId = grni.Id,
            Description = $"Receipt {receipt.ReceiptNumber}, line {line.Sequence}",
            Quantity = line.InvoiceQuantity,
            UnitCost = line.InvoiceUnitCost,
            LineTotal = line.InvoiceAmount,
            MatchedQuantity = line.MatchedQuantity,
            QuantityVarianceQuantity = line.QuantityVarianceQuantity,
            ReceiptUnitCost = line.ReceiptUnitCost,
            AccrualAmount = line.AccrualAmount,
            PriceVarianceAmount = line.PriceVarianceAmount,
            QuantityVarianceAmount = line.QuantityVarianceAmount
        }));

        var order = await db.PurchaseOrders.SingleAsync(
            item => item.Id == match.PurchaseOrderId && item.CompanyId == companyId,
            cancellationToken);
        var orderLines = await db.PurchaseOrderLines
            .Where(line => line.PurchaseOrderId == order.Id)
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        foreach (var line in lines)
            orderLines[line.PurchaseOrderLineId].InvoicedQuantity += line.InvoiceQuantity;

        var vendor = await db.Vendors.SingleAsync(
            item => item.Id == match.VendorId && item.CompanyId == companyId,
            cancellationToken);
        vendor.OpenBalance += match.InvoiceAmount;
        match.Status = "Posted";
        match.VendorBillId = bill.Id;
        match.JournalEntryId = posting.Id;
        match.PostedByUserId = postingUserId;
        match.PostedAtUtc = DateTimeOffset.UtcNow;
        match.ConcurrencyToken = Guid.NewGuid().ToString("N");
        receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        SetPurchaseOrderReturnStatus(order, orderLines.Values);
        AddPurchasingAudit(
            db,
            companyId,
            "purchase-invoice-match.posted",
            nameof(PurchaseInvoiceMatch),
            match.Id,
            new
            {
                match.BillNumber,
                receipt.ReceiptNumber,
                match.InvoiceAmount,
                match.AccrualAmount,
                match.PriceVarianceAmount,
                match.QuantityVarianceQuantity,
                match.QuantityVarianceAmount,
                totalVariance,
                vendorBillId = bill.Id,
                journalEntryId = posting.Id
            });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The receipt, purchase order, vendor, or invoice match changed while posting. Refresh and try again.");
        }
        catch (DbUpdateException)
        {
            return TransactionResult.Failure("The vendor bill or invoice match changed concurrently. Refresh and try again.");
        }

        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(match.Id);
    }

    public async Task<TransactionResult> ReversePurchaseInvoiceMatchAsync(
        ReversePurchaseInvoiceMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.PurchasingManage)
            || !HasPermission(BrassLedgerPermissions.PaymentReverse))
            return TransactionResult.Failure("You are not authorized to reverse purchase-invoice matches.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return TransactionResult.Failure("A reversal reason is required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var match = await db.PurchaseInvoiceMatches.SingleOrDefaultAsync(
            item => item.Id == request.PurchaseInvoiceMatchId && item.CompanyId == companyId,
            cancellationToken);
        if (match is null
            || match.Status != "Posted"
            || !match.VendorBillId.HasValue
            || !match.JournalEntryId.HasValue)
            return TransactionResult.Failure("Only a posted purchase-invoice match can be reversed.");
        if (!string.Equals(match.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
            return TransactionResult.Failure("The invoice match changed after it was opened. Refresh and review it again.");
        if (request.ReversalDate < match.BillDate)
            return TransactionResult.Failure("The reversal date cannot precede the vendor bill date.");

        var bill = await db.VendorBills.SingleAsync(
            item => item.Id == match.VendorBillId.Value && item.CompanyId == companyId,
            cancellationToken);
        var matchedReceiptLineIds = await db.PurchaseInvoiceMatchLines
            .Where(line => line.PurchaseInvoiceMatchId == match.Id && line.MatchedQuantity > 0m)
            .Select(line => line.InventoryReceiptLineId)
            .ToArrayAsync(cancellationToken);
        var hasRelatedSupplierReturn = await (
            from returnLine in db.SupplierReturnShipmentLines
            join shipment in db.SupplierReturnShipments on returnLine.SupplierReturnShipmentId equals shipment.Id
            where shipment.CompanyId == companyId
                && shipment.Status == "Posted"
                && returnLine.InvoicedQuantity > 0m
                && matchedReceiptLineIds.Contains(returnLine.InventoryReceiptLineId)
            select returnLine.Id)
            .AnyAsync(cancellationToken);
        if (bill.Status != "Open"
            || bill.BalanceDue != bill.TotalAmount
            || await db.SubledgerPaymentApplications.AnyAsync(item => item.DocumentId == bill.Id, cancellationToken)
            || await db.SubledgerAdjustments.AnyAsync(
                item => item.CompanyId == companyId && item.DocumentId == bill.Id,
                cancellationToken)
            || await db.SupplierReturnShipments.AnyAsync(
                item => item.CompanyId == companyId && item.SourceVendorBillId == bill.Id && item.Status == "Posted",
                cancellationToken)
            || await db.SupplierReturnCreditApplications.AnyAsync(
                item => item.CompanyId == companyId && item.VendorBillId == bill.Id && item.Status == "Posted",
                cancellationToken)
            || hasRelatedSupplierReturn)
            return TransactionResult.Failure("Reverse all payment, adjustment, and supplier-return activity before reversing this invoice match.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reversal = await PostInverseAsync(
            db,
            companyId,
            match.JournalEntryId.Value,
            request.ReversalDate,
            $"VOID-{match.BillNumber}",
            request.Reason.Trim(),
            match.Id,
            "PurchaseInvoiceMatchReversal",
            null,
            cancellationToken,
            "Accounts Payable");
        if (!reversal.Succeeded)
            return reversal;

        var lines = await db.PurchaseInvoiceMatchLines
            .Where(line => line.PurchaseInvoiceMatchId == match.Id)
            .ToListAsync(cancellationToken);
        var order = await db.PurchaseOrders.SingleAsync(
            item => item.Id == match.PurchaseOrderId && item.CompanyId == companyId,
            cancellationToken);
        var orderLines = await db.PurchaseOrderLines
            .Where(line => line.PurchaseOrderId == order.Id)
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        foreach (var line in lines)
        {
            orderLines[line.PurchaseOrderLineId].InvoicedQuantity -= line.InvoiceQuantity;
            if (orderLines[line.PurchaseOrderLineId].InvoicedQuantity < 0m)
                return TransactionResult.Failure("Purchase-order invoiced quantity would become negative. Correct the source provenance before reversing.");
        }

        var receipt = await db.InventoryReceipts.SingleAsync(
            item => item.Id == match.InventoryReceiptId && item.CompanyId == companyId,
            cancellationToken);
        var vendor = await db.Vendors.SingleAsync(
            item => item.Id == match.VendorId && item.CompanyId == companyId,
            cancellationToken);
        vendor.OpenBalance -= bill.TotalAmount;
        bill.Status = "Voided";
        bill.BalanceDue = 0m;
        bill.ConcurrencyToken = Guid.NewGuid().ToString("N");
        match.Status = "Reversed";
        match.ReversalJournalEntryId = reversal.Id;
        match.ReversedByUserId = ResolveUserId();
        match.ReversedAtUtc = DateTimeOffset.UtcNow;
        match.ReversalDate = request.ReversalDate;
        match.ReversalReason = request.Reason.Trim();
        match.ConcurrencyToken = Guid.NewGuid().ToString("N");
        receipt.ConcurrencyToken = Guid.NewGuid().ToString("N");
        SetPurchaseOrderReturnStatus(order, orderLines.Values);
        AddPurchasingAudit(
            db,
            companyId,
            "purchase-invoice-match.reversed",
            nameof(PurchaseInvoiceMatch),
            match.Id,
            new
            {
                match.BillNumber,
                match.InvoiceAmount,
                match.AccrualAmount,
                match.PriceVarianceAmount,
                match.QuantityVarianceQuantity,
                match.QuantityVarianceAmount,
                match.ReversalDate,
                match.ReversalReason,
                reversalJournalEntryId = reversal.Id
            });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionResult.Failure("The invoice match, bill, receipt, or purchase order changed while reversing. Refresh and try again.");
        }

        await transaction.CommitAsync(cancellationToken);
        return TransactionResult.Success(match.Id);
    }

    private sealed record CalculatedPurchaseInvoiceMatchLine(
        InventoryReceiptLine Source,
        int Sequence,
        decimal AvailableQuantity,
        decimal InvoiceQuantity,
        decimal MatchedQuantity,
        decimal QuantityVarianceQuantity,
        decimal InvoiceUnitCost,
        decimal AccrualAmount,
        decimal InvoiceAmount,
        decimal PriceVarianceAmount,
        decimal QuantityVarianceAmount);
}
