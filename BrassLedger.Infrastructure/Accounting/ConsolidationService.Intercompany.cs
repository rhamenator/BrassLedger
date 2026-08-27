using System.Text.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class ConsolidationService
{
    public async Task<TransactionResult> SaveTradingPartnerAsync(SaveConsolidationTradingPartnerRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage)) return TransactionResult.Failure("You are not authorized to configure intercompany trading partners.");
        if (request.ConsolidationGroupId == Guid.Empty || request.MemberCompanyId == Guid.Empty || request.CounterpartyCompanyId == Guid.Empty
            || request.MemberCompanyId == request.CounterpartyCompanyId || request.CustomerId.HasValue == request.VendorId.HasValue
            || request.EffectiveThrough < request.EffectiveFrom || (!request.IsActive && !request.EffectiveThrough.HasValue))
            return TransactionResult.Failure("Choose two different member companies, exactly one customer or vendor record, and a valid effective period. Inactive links require an end date.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId();
        if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.Id == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (group is null) return TransactionResult.Failure("The consolidation group was not found in the active company.");
        var requiredOwners = new[] { companyId.Value, request.MemberCompanyId, request.CounterpartyCompanyId }.Distinct().ToArray();
        var owned = await db.CompanyMemberships.AsNoTracking().Where(item => item.UserId == userId && item.IsOwner && item.IsActive && requiredOwners.Contains(item.CompanyId)).Select(item => item.CompanyId).Distinct().ToArrayAsync(cancellationToken);
        if (owned.Length != requiredOwners.Length) return TransactionResult.Failure("Only an active owner of the parent and both member companies can configure an intercompany trading partner.");
        var requiredThrough = request.EffectiveThrough ?? DateOnly.MaxValue;
        foreach (var memberId in new[] { request.MemberCompanyId, request.CounterpartyCompanyId })
        {
            if (!await HasContinuousOwnershipCoverageAsync(db, group.Id, memberId, request.EffectiveFrom, requiredThrough, cancellationToken))
                return TransactionResult.Failure("The trading-partner period must have continuous retained ownership coverage for both member companies.");
        }
        string recordNumber; string recordName; Guid recordId;
        if (request.CustomerId.HasValue)
        {
            var customer = await db.Customers.SingleOrDefaultAsync(item => item.Id == request.CustomerId && item.CompanyId == request.MemberCompanyId, cancellationToken);
            if (customer is null) return TransactionResult.Failure("The customer was not found in the selected member company.");
            recordId = customer.Id; recordNumber = customer.CustomerNumber; recordName = customer.Name;
        }
        else
        {
            var vendor = await db.Vendors.SingleOrDefaultAsync(item => item.Id == request.VendorId && item.CompanyId == request.MemberCompanyId, cancellationToken);
            if (vendor is null) return TransactionResult.Failure("The vendor was not found in the selected member company.");
            recordId = vendor.Id; recordNumber = vendor.VendorNumber; recordName = vendor.Name;
        }
        var link = request.Id is { } linkId ? await db.ConsolidationTradingPartners.SingleOrDefaultAsync(item => item.Id == linkId && item.CompanyId == companyId && item.ConsolidationGroupId == group.Id, cancellationToken) : null;
        if (request.Id is not null && link is null) return TransactionResult.Failure("The trading-partner link was not found in this consolidation group.");
        if (link is not null && (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || link.ConcurrencyToken != request.ConcurrencyToken)) return TransactionResult.Failure("The trading-partner link changed after it was displayed. Refresh before saving it.");
        if (link is not null && (link.MemberCompanyId != request.MemberCompanyId || link.CounterpartyCompanyId != request.CounterpartyCompanyId || link.CustomerId != request.CustomerId || link.VendorId != request.VendorId))
            return TransactionResult.Failure("A retained trading-partner link cannot be moved to another company or record. Close it and add a successor instead.");
        var overlaps = await db.ConsolidationTradingPartners.AnyAsync(item => item.ConsolidationGroupId == group.Id && item.Id != request.Id && item.MemberCompanyId == request.MemberCompanyId
            && item.CustomerId == request.CustomerId && item.VendorId == request.VendorId && item.EffectiveFrom <= requiredThrough && (item.EffectiveThrough == null || item.EffectiveThrough >= request.EffectiveFrom), cancellationToken);
        if (overlaps) return TransactionResult.Failure("Effective periods for the same member customer or vendor cannot overlap.");
        link ??= new ConsolidationTradingPartner { Id = Guid.NewGuid(), CompanyId = companyId.Value, ConsolidationGroupId = group.Id, MemberCompanyId = request.MemberCompanyId, CounterpartyCompanyId = request.CounterpartyCompanyId, CustomerId = request.CustomerId, VendorId = request.VendorId };
        link.EffectiveFrom = request.EffectiveFrom; link.EffectiveThrough = request.EffectiveThrough; link.IsActive = request.IsActive; link.ConcurrencyToken = Guid.NewGuid().ToString("N");
        if (db.Entry(link).State == EntityState.Detached) db.ConsolidationTradingPartners.Add(link);
        group.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = request.Id is null ? "consolidation-trading-partner.created" : "consolidation-trading-partner.updated", EntityType = nameof(ConsolidationTradingPartner), EntityId = link.Id, DetailJson = JsonSerializer.Serialize(new { link.ConsolidationGroupId, link.MemberCompanyId, link.CounterpartyCompanyId, kind = link.CustomerId.HasValue ? "Customer" : "Vendor", recordId, recordNumber, recordName, link.EffectiveFrom, link.EffectiveThrough, link.IsActive }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The consolidation group or trading-partner link changed concurrently. Refresh and try again."); }
        catch (DbUpdateException) { return TransactionResult.Failure("The trading-partner link conflicts with another retained effective period."); }
        return TransactionResult.Success(link.Id);
    }

    public async Task<ConsolidationTradingPartnerWorkspace?> GetTradingPartnerWorkspaceAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage)) return null;
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == groupId && item.CompanyId == companyId, cancellationToken); if (group is null) return null;
        var members = await db.ConsolidationGroupCompanies.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id).ToListAsync(cancellationToken);
        var companyIds = members.Select(item => item.MemberCompanyId).Distinct().ToArray();
        var permitted = await db.CompanyMemberships.AsNoTracking().Where(item => item.UserId == userId && item.IsActive && companyIds.Contains(item.CompanyId)).Select(item => item.CompanyId).Distinct().ToArrayAsync(cancellationToken);
        if (permitted.Length != companyIds.Length) return null;
        var companies = await db.Companies.AsNoTracking().Where(item => companyIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var customers = await db.Customers.AsNoTracking().Where(item => companyIds.Contains(item.CompanyId)).OrderBy(item => item.CustomerNumber).ToListAsync(cancellationToken);
        var vendors = await db.Vendors.AsNoTracking().Where(item => companyIds.Contains(item.CompanyId)).OrderBy(item => item.VendorNumber).ToListAsync(cancellationToken);
        var links = await db.ConsolidationTradingPartners.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id && item.CompanyId == companyId).OrderBy(item => item.MemberCompanyId).ThenBy(item => item.EffectiveFrom).ToListAsync(cancellationToken);
        var candidates = customers.Select(item => new ConsolidationTradingPartnerCandidateSnapshot(item.CompanyId, companies[item.CompanyId].Name, "Customer", item.Id, item.CustomerNumber, item.Name))
            .Concat(vendors.Select(item => new ConsolidationTradingPartnerCandidateSnapshot(item.CompanyId, companies[item.CompanyId].Name, "Vendor", item.Id, item.VendorNumber, item.Name))).ToArray();
        return new ConsolidationTradingPartnerWorkspace(group.Id, group.Name,
            members.OrderBy(item => companies[item.MemberCompanyId].Name).ThenBy(item => item.EffectiveFrom).Select(item => new ConsolidationGroupMemberSnapshot(item.Id, item.MemberCompanyId, companies[item.MemberCompanyId].Name, companies[item.MemberCompanyId].BaseCurrency, item.OwnershipPercentage, item.EffectiveFrom, item.EffectiveThrough, item.ConcurrencyToken)).ToArray(),
            candidates,
            links.Select(link =>
            {
                var candidate = candidates.Single(item => item.CompanyId == link.MemberCompanyId && item.Kind == (link.CustomerId.HasValue ? "Customer" : "Vendor") && item.CounterpartyRecordId == (link.CustomerId ?? link.VendorId));
                return new ConsolidationTradingPartnerSnapshot(link.Id, link.MemberCompanyId, companies[link.MemberCompanyId].Name, link.CounterpartyCompanyId, companies[link.CounterpartyCompanyId].Name, candidate.Kind, candidate.CounterpartyRecordId, candidate.Number, candidate.Name, link.EffectiveFrom, link.EffectiveThrough, link.IsActive, link.ConcurrencyToken);
            }).ToArray());
    }

    public async Task<ConsolidationIntercompanyDiscoveryResult> DiscoverIntercompanyMatchesAsync(DiscoverConsolidationIntercompanyMatchesRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedgerPermissions.JournalPrepare)) return new(false, "You are not authorized to discover intercompany matches.", 0, 0, []);
        if (request.ConsolidationGroupId == Guid.Empty || request.PeriodStart > request.AsOf) return new(false, "Choose a consolidation group and valid discovery period.", 0, 0, []);
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return new(false, "An active company and user are required.", 0, 0, []);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.SingleOrDefaultAsync(item => item.Id == request.ConsolidationGroupId && item.CompanyId == companyId && item.IsActive, cancellationToken);
        if (group is null) return new(false, "The active consolidation group was not found in the active company.", 0, 0, []);
        var accessError = await ValidateGroupAccessAsync(db, group.Id, userId.Value, request.AsOf, cancellationToken); if (accessError is not null) return new(false, accessError, 0, 0, []);
        var effectiveMembers = await EffectiveMemberCompanyIdsAsync(db, group.Id, request.AsOf, cancellationToken);
        var companies = await db.Companies.AsNoTracking().Where(item => effectiveMembers.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var links = await db.ConsolidationTradingPartners.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id && item.IsActive && effectiveMembers.Contains(item.MemberCompanyId) && effectiveMembers.Contains(item.CounterpartyCompanyId) && item.EffectiveFrom <= request.AsOf && (item.EffectiveThrough == null || item.EffectiveThrough >= request.PeriodStart)).ToListAsync(cancellationToken);
        var invoices = await db.SalesInvoices.AsNoTracking().Where(item => effectiveMembers.Contains(item.CompanyId) && item.InvoiceDate >= request.PeriodStart && item.InvoiceDate <= request.AsOf && item.Status != "Voided" && item.TotalAmount > 0m).ToListAsync(cancellationToken);
        var bills = await db.VendorBills.AsNoTracking().Where(item => effectiveMembers.Contains(item.CompanyId) && item.BillDate >= request.PeriodStart && item.BillDate <= request.AsOf && item.Status != "Voided" && item.TotalAmount > 0m).ToListAsync(cancellationToken);
        var candidates = new List<(SalesInvoice Invoice, VendorBill Bill, Company Seller, Company Buyer)>(); var warnings = new List<string>();
        foreach (var invoice in invoices)
        {
            var customerLinks = links.Where(link => link.MemberCompanyId == invoice.CompanyId && link.CustomerId == invoice.CustomerId && EffectiveOn(link, invoice.InvoiceDate)).ToArray();
            foreach (var customerLink in customerLinks)
            {
                var seller = companies[invoice.CompanyId]; var buyer = companies[customerLink.CounterpartyCompanyId];
                var vendorIds = links.Where(link => link.MemberCompanyId == buyer.Id && link.CounterpartyCompanyId == seller.Id && link.VendorId.HasValue).Select(link => link.VendorId!.Value).ToHashSet();
                foreach (var bill in bills.Where(item => item.CompanyId == buyer.Id && vendorIds.Contains(item.VendorId) && SameReference(item.BillNumber, invoice.InvoiceNumber) && item.TotalAmount == invoice.TotalAmount))
                {
                    if (!links.Any(link => link.MemberCompanyId == buyer.Id && link.CounterpartyCompanyId == seller.Id && link.VendorId == bill.VendorId && EffectiveOn(link, bill.BillDate))) continue;
                    if (!string.Equals(seller.BaseCurrency, buyer.BaseCurrency, StringComparison.OrdinalIgnoreCase)) { warnings.Add($"{seller.Name} invoice {invoice.InvoiceNumber} and {buyer.Name} bill {bill.BillNumber} matched by reference and amount but use different base currencies; no automatic suggestion was retained."); continue; }
                    candidates.Add((invoice, bill, seller, buyer));
                }
            }
        }
        var unambiguous = candidates.Where(candidate => candidates.Count(item => item.Invoice.Id == candidate.Invoice.Id) == 1 && candidates.Count(item => item.Bill.Id == candidate.Bill.Id) == 1).DistinctBy(item => (item.Invoice.Id, item.Bill.Id)).ToArray();
        var ambiguousCount = candidates.Count - unambiguous.Length;
        if (ambiguousCount > 0) warnings.Add($"{ambiguousCount} candidate pairing(s) were not retained because an invoice or bill had more than one exact candidate.");
        var existing = await db.ConsolidationIntercompanyMatches.Where(item => item.ConsolidationGroupId == group.Id).ToListAsync(cancellationToken); var created = 0; var refreshed = 0;
        foreach (var candidate in unambiguous)
        {
            var match = existing.SingleOrDefault(item => item.SalesInvoiceId == candidate.Invoice.Id || item.VendorBillId == candidate.Bill.Id);
            if (match is null)
            {
                match = new ConsolidationIntercompanyMatch { Id = Guid.NewGuid(), CompanyId = companyId.Value, ConsolidationGroupId = group.Id, SellerCompanyId = candidate.Seller.Id, BuyerCompanyId = candidate.Buyer.Id, SalesInvoiceId = candidate.Invoice.Id, VendorBillId = candidate.Bill.Id, MatchReference = BuildMatchReference(candidate.Invoice.Id, candidate.Bill.Id), Currency = candidate.Seller.BaseCurrency, Status = "Suggested", DiscoveredAtUtc = DateTimeOffset.UtcNow };
                db.ConsolidationIntercompanyMatches.Add(match); existing.Add(match); created++;
            }
            else if (match.SalesInvoiceId != candidate.Invoice.Id || match.VendorBillId != candidate.Bill.Id) { warnings.Add($"Invoice {candidate.Invoice.InvoiceNumber} or bill {candidate.Bill.BillNumber} is already retained in another match and was not reassigned."); continue; }
            match.Amount = candidate.Invoice.TotalAmount; match.SellerBalanceDue = candidate.Invoice.BalanceDue; match.BuyerBalanceDue = candidate.Bill.BalanceDue;
            if (match.Status == "Suggested" && db.Entry(match).State != EntityState.Added) { match.DiscoveredAtUtc = DateTimeOffset.UtcNow; match.ConcurrencyToken = Guid.NewGuid().ToString("N"); refreshed++; }
        }
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = "consolidation-intercompany-match.discovered", EntityType = nameof(ConsolidationGroup), EntityId = group.Id, DetailJson = JsonSerializer.Serialize(new { request.PeriodStart, request.AsOf, created, refreshed, warningCount = warnings.Count }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return new(false, "Intercompany matches changed concurrently. Refresh and run discovery again.", 0, 0, warnings); }
        catch (DbUpdateException) { return new(false, "Another discovery retained one of the same invoices or bills. Refresh before continuing.", 0, 0, warnings); }
        return new(true, string.Empty, created, refreshed, warnings.Distinct().ToArray());
    }

    public async Task<TransactionResult> SetIntercompanyMatchDecisionAsync(SetConsolidationIntercompanyMatchDecisionRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || !HasPermission(BrassLedgerPermissions.JournalPrepare)) return TransactionResult.Failure("You are not authorized to review intercompany match suggestions.");
        var decision = request.Decision?.Trim(); var reason = request.Reason?.Trim() ?? string.Empty;
        if (decision is not ("Exclude" or "Restore") || (decision == "Exclude" && string.IsNullOrWhiteSpace(reason)) || reason.Length > 1000) return TransactionResult.Failure("Choose Exclude with a concise reason, or Restore.");
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return TransactionResult.Failure("An active company and user are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var match = await db.ConsolidationIntercompanyMatches.SingleOrDefaultAsync(item => item.Id == request.MatchId && item.ConsolidationGroupId == request.ConsolidationGroupId && item.CompanyId == companyId, cancellationToken);
        if (match is null) return TransactionResult.Failure("The intercompany match was not found in this consolidation group.");
        if (match.Status == "Controlled") return TransactionResult.Failure("A match linked to a consolidation adjustment cannot be excluded or restored.");
        if (string.IsNullOrWhiteSpace(request.ConcurrencyToken) || match.ConcurrencyToken != request.ConcurrencyToken) return TransactionResult.Failure("The intercompany match changed after it was displayed. Refresh before deciding it.");
        var invoiceDate = await db.SalesInvoices.Where(item => item.Id == match.SalesInvoiceId).Select(item => item.InvoiceDate).SingleAsync(cancellationToken);
        var billDate = await db.VendorBills.Where(item => item.Id == match.VendorBillId).Select(item => item.BillDate).SingleAsync(cancellationToken);
        var accessError = await ValidateGroupAccessAsync(db, match.ConsolidationGroupId, userId.Value, invoiceDate > billDate ? invoiceDate : billDate, cancellationToken); if (accessError is not null) return TransactionResult.Failure(accessError);
        match.Status = decision == "Exclude" ? "Excluded" : "Suggested"; match.ReviewReason = decision == "Exclude" ? reason : string.Empty; match.ReviewedByUserId = userId; match.ReviewedAtUtc = DateTimeOffset.UtcNow; match.ConcurrencyToken = Guid.NewGuid().ToString("N");
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = userId, Action = decision == "Exclude" ? "consolidation-intercompany-match.excluded" : "consolidation-intercompany-match.restored", EntityType = nameof(ConsolidationIntercompanyMatch), EntityId = match.Id, DetailJson = JsonSerializer.Serialize(new { reason = match.ReviewReason, match.MatchReference, match.SalesInvoiceId, match.VendorBillId }), OccurredAtUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { return TransactionResult.Failure("The intercompany match changed concurrently. Refresh and try again."); }
        return TransactionResult.Success(match.Id);
    }

    public async Task<ConsolidationIntercompanyMatchWorkspace?> GetIntercompanyMatchWorkspaceAsync(Guid groupId, DateOnly periodStart, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        if (!HasPermission(BrassLedgerPermissions.ReportingManage) || periodStart > asOf) return null;
        var companyId = CurrentCompanyId(); var userId = CurrentUserId(); if (companyId is null || userId is null) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.ConsolidationGroups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == groupId && item.CompanyId == companyId, cancellationToken); if (group is null) return null;
        if (await ValidateGroupAccessAsync(db, group.Id, userId.Value, asOf, cancellationToken) is not null) return null;
        var matches = await db.ConsolidationIntercompanyMatches.AsNoTracking().Where(item => item.ConsolidationGroupId == group.Id && item.CompanyId == companyId).ToListAsync(cancellationToken);
        var matchedCompanyIds = matches.SelectMany(item => new[] { item.SellerCompanyId, item.BuyerCompanyId }).Distinct().ToArray();
        var permittedCompanyIds = await db.CompanyMemberships.AsNoTracking().Where(item => item.UserId == userId && item.IsActive && matchedCompanyIds.Contains(item.CompanyId)).Select(item => item.CompanyId).Distinct().ToArrayAsync(cancellationToken);
        if (permittedCompanyIds.Length != matchedCompanyIds.Length) return null;
        var invoiceIds = matches.Select(item => item.SalesInvoiceId).ToArray(); var billIds = matches.Select(item => item.VendorBillId).ToArray();
        var invoices = await db.SalesInvoices.AsNoTracking().Where(item => invoiceIds.Contains(item.Id) && item.InvoiceDate >= periodStart && item.InvoiceDate <= asOf).ToDictionaryAsync(item => item.Id, cancellationToken);
        var bills = await db.VendorBills.AsNoTracking().Where(item => billIds.Contains(item.Id) && item.BillDate >= periodStart && item.BillDate <= asOf).ToDictionaryAsync(item => item.Id, cancellationToken);
        matches = matches.Where(item => invoices.ContainsKey(item.SalesInvoiceId) && bills.ContainsKey(item.VendorBillId)).ToList();
        var companyIds = matches.SelectMany(item => new[] { item.SellerCompanyId, item.BuyerCompanyId }).Distinct().ToArray(); var companies = await db.Companies.AsNoTracking().Where(item => companyIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var userIds = matches.Where(item => item.ReviewedByUserId.HasValue).Select(item => item.ReviewedByUserId!.Value).Distinct().ToArray(); var users = await db.Users.AsNoTracking().Where(item => userIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => string.IsNullOrWhiteSpace(item.DisplayName) ? item.UserName : item.DisplayName, cancellationToken);
        return new ConsolidationIntercompanyMatchWorkspace(group.Id, group.Name, periodStart, asOf, matches.OrderBy(item => invoices[item.SalesInvoiceId].InvoiceDate).ThenBy(item => invoices[item.SalesInvoiceId].InvoiceNumber).Select(item =>
        {
            var invoice = invoices[item.SalesInvoiceId]; var bill = bills[item.VendorBillId];
            return new ConsolidationIntercompanyMatchSnapshot(item.Id, item.SellerCompanyId, companies[item.SellerCompanyId].Name, item.BuyerCompanyId, companies[item.BuyerCompanyId].Name, invoice.Id, invoice.InvoiceNumber, invoice.InvoiceDate, bill.Id, bill.BillNumber, bill.BillDate, item.MatchReference, item.Currency, item.Amount, item.SellerBalanceDue, item.BuyerBalanceDue, item.Status, item.ReviewReason, item.ReviewedByUserId.HasValue ? users.GetValueOrDefault(item.ReviewedByUserId.Value, "Unavailable user") : null, item.ReviewedAtUtc, item.ConsolidationAdjustmentBatchId, item.ConcurrencyToken);
        }).ToArray());
    }

    private static async Task<string?> ControlIntercompanyMatchAsync(BrassLedgerDbContext db, ConsolidationAdjustmentBatch batch, ConsolidationAdjustmentKind kind, string matchReference, IReadOnlyList<ConsolidationAdjustmentLineRequest> lines, Guid userId, CancellationToken cancellationToken)
    {
        var linked = await db.ConsolidationIntercompanyMatches.SingleOrDefaultAsync(item => item.ConsolidationGroupId == batch.ConsolidationGroupId && item.ConsolidationAdjustmentBatchId == batch.Id, cancellationToken);
        if (linked is not null && (kind != ConsolidationAdjustmentKind.IntercompanyElimination || linked.MatchReference != matchReference)) return "A retained automatically discovered match cannot be detached from its elimination. Correct the existing elimination without changing its match reference.";
        if (kind != ConsolidationAdjustmentKind.IntercompanyElimination) return null;
        var match = await db.ConsolidationIntercompanyMatches.SingleOrDefaultAsync(item => item.ConsolidationGroupId == batch.ConsolidationGroupId && item.MatchReference == matchReference, cancellationToken);
        if (match is null) return null;
        if (match.Status == "Excluded") return "Restore the excluded intercompany match before using it in an elimination.";
        if (match.ConsolidationAdjustmentBatchId.HasValue && match.ConsolidationAdjustmentBatchId != batch.Id) return "The intercompany match is already linked to another retained consolidation adjustment.";
        if (lines.Any(line => !SameCompanyPair(line.SourceCompanyId, line.CounterpartyCompanyId, match.SellerCompanyId, match.BuyerCompanyId))) return "Every line using an automatically discovered match must retain its exact seller and buyer company pair.";
        if (match.Status != "Controlled")
        {
            match.Status = "Controlled"; match.ReviewedByUserId = userId; match.ReviewedAtUtc = DateTimeOffset.UtcNow; match.ReviewReason = "Prepared as a controlled intercompany elimination."; match.ConsolidationAdjustmentBatchId = batch.Id; match.ConcurrencyToken = Guid.NewGuid().ToString("N");
            db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = batch.CompanyId, UserId = userId, Action = "consolidation-intercompany-match.controlled", EntityType = nameof(ConsolidationIntercompanyMatch), EntityId = match.Id, DetailJson = JsonSerializer.Serialize(new { adjustmentBatchId = batch.Id, match.MatchReference, match.SalesInvoiceId, match.VendorBillId }), OccurredAtUtc = DateTimeOffset.UtcNow });
        }
        return null;
    }

    private static bool EffectiveOn(ConsolidationTradingPartner link, DateOnly date) => link.EffectiveFrom <= date && (link.EffectiveThrough is null || link.EffectiveThrough >= date);
    private static async Task<bool> HasContinuousOwnershipCoverageAsync(BrassLedgerDbContext db, Guid groupId, Guid memberCompanyId, DateOnly effectiveFrom, DateOnly effectiveThrough, CancellationToken cancellationToken)
    {
        var periods = await db.ConsolidationGroupCompanies.AsNoTracking()
            .Where(period => period.ConsolidationGroupId == groupId && period.MemberCompanyId == memberCompanyId && period.EffectiveFrom <= effectiveThrough && (period.EffectiveThrough == null || period.EffectiveThrough >= effectiveFrom))
            .OrderBy(period => period.EffectiveFrom).ToListAsync(cancellationToken);
        var nextUncovered = effectiveFrom;
        foreach (var period in periods)
        {
            if (period.EffectiveFrom > nextUncovered) return false;
            var coveredThrough = period.EffectiveThrough ?? DateOnly.MaxValue;
            if (coveredThrough >= effectiveThrough) return true;
            if (coveredThrough >= nextUncovered) nextUncovered = coveredThrough.AddDays(1);
        }
        return false;
    }
    private static bool SameReference(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool SameCompanyPair(Guid? source, Guid? counterparty, Guid seller, Guid buyer) => source.HasValue && counterparty.HasValue && ((source == seller && counterparty == buyer) || (source == buyer && counterparty == seller));
    private static string BuildMatchReference(Guid invoiceId, Guid billId) => $"IC-{invoiceId:N}-{billId:N}";
}
