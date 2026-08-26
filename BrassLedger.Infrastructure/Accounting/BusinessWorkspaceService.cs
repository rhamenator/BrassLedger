using BrassLedger.Application.Accounting;
using BrassLedger.Application.Catalog;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class BusinessWorkspaceService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IProductCatalogService assessmentService,
    IHttpContextAccessor httpContextAccessor) : IBusinessWorkspaceService
{
    public async Task<BusinessWorkspaceSnapshot> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var httpContext = httpContextAccessor.HttpContext;
        var canViewPayrollSensitiveData = httpContext is null || httpContext.User.HasClaim(
            BrassLedgerAuthenticationDefaults.PermissionClaimType,
            BrassLedgerPermissions.PayrollSensitiveData);
        var payrollPermissions = new[] { BrassLedgerPermissions.PayrollManage, BrassLedgerPermissions.PayrollPrepare, BrassLedgerPermissions.PayrollApprove, BrassLedgerPermissions.PayrollPost, BrassLedgerPermissions.PayrollReverse, BrassLedgerPermissions.PayrollSensitiveData };
        var canAccessPayroll = httpContext is null || payrollPermissions.Any(permission => httpContext.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission));
        var claimValue = httpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (httpContext is not null && !Guid.TryParse(claimValue, out _)) throw new UnauthorizedAccessException("An authenticated company context is required.");
        var companies = dbContext.Companies.AsNoTracking();
        var company = Guid.TryParse(claimValue, out var companyId)
            ? await companies.SingleAsync(x => x.Id == companyId, cancellationToken)
            : await companies.OrderBy(x => x.Name).FirstAsync(cancellationToken);
        var users = await dbContext.Users.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).ToListAsync(cancellationToken);
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).OrderBy(x => x.Number).ToListAsync(cancellationToken);
        var journalEntries = await dbContext.JournalEntries.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.PostedOn).ThenByDescending(x => x.EntryNumber).Take(20).ToListAsync(cancellationToken);
        var journalEntryIds = journalEntries.Select(entry => entry.Id).ToArray();
        var journalEntryLines = journalEntryIds.Length == 0 ? [] : await dbContext.JournalEntryLines.AsNoTracking().Where(line => journalEntryIds.Contains(line.JournalEntryId)).ToListAsync(cancellationToken);
        var customers = await dbContext.Customers.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.CustomerNumber).ToListAsync(cancellationToken);
        var invoices = await dbContext.SalesInvoices.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.InvoiceDate).ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(invoice => invoice.Id).ToArray();
        var invoiceLines = invoiceIds.Length == 0 ? [] : await dbContext.SalesInvoiceLines.AsNoTracking().Where(line => invoiceIds.Contains(line.SalesInvoiceId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var vendors = await dbContext.Vendors.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.VendorNumber).ToListAsync(cancellationToken);
        var vendorBills = await dbContext.VendorBills.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.DueDate).ToListAsync(cancellationToken);
        var vendorBillIds = vendorBills.Select(bill => bill.Id).ToArray();
        var vendorBillLines = vendorBillIds.Length == 0 ? [] : await dbContext.VendorBillLines.AsNoTracking().Where(line => vendorBillIds.Contains(line.VendorBillId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var subledgerPayments = (await dbContext.SubledgerPayments.AsNoTracking()
                .Where(payment => payment.CompanyId == company.Id)
                .ToListAsync(cancellationToken))
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.CreatedAtUtc)
            .ToList();
        var paymentIds = subledgerPayments.Select(payment => payment.Id).ToArray();
        var paymentApplications = paymentIds.Length == 0 ? [] : await dbContext.SubledgerPaymentApplications.AsNoTracking().Where(application => paymentIds.Contains(application.SubledgerPaymentId)).ToListAsync(cancellationToken);
        var subledgerAdjustments = await dbContext.SubledgerAdjustments.AsNoTracking().Where(adjustment => adjustment.CompanyId == company.Id).OrderByDescending(adjustment => adjustment.AdjustmentDate).ToListAsync(cancellationToken);
        var subledgerWorkflows = (await dbContext.SubledgerDocumentWorkflows.AsNoTracking().Where(workflow => workflow.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(workflow => workflow.CreatedAtUtc).ToList();
        var inventoryItems = await dbContext.InventoryItems.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).OrderBy(x => x.Sku).ToListAsync(cancellationToken);
        var inventoryWarehouses = await dbContext.InventoryWarehouses.AsNoTracking().Where(warehouse => warehouse.CompanyId == company.Id).OrderByDescending(warehouse => warehouse.IsDefault).ThenBy(warehouse => warehouse.Code).ToListAsync(cancellationToken);
        var inventoryWarehouseIds = inventoryWarehouses.Select(warehouse => warehouse.Id).ToArray();
        var inventoryBins = inventoryWarehouseIds.Length == 0 ? [] : await dbContext.InventoryBins.AsNoTracking().Where(bin => inventoryWarehouseIds.Contains(bin.WarehouseId)).OrderByDescending(bin => bin.IsDefault).ThenBy(bin => bin.Code).ToListAsync(cancellationToken);
        var inventoryBinIds = inventoryBins.Select(bin => bin.Id).ToArray();
        var inventoryLocationBalances = inventoryBinIds.Length == 0 ? [] : await dbContext.InventoryLocationBalances.AsNoTracking().Where(balance => balance.CompanyId == company.Id && inventoryBinIds.Contains(balance.BinId)).ToListAsync(cancellationToken);
        var inventoryTransfers = (await dbContext.InventoryTransfers.AsNoTracking().Where(transfer => transfer.CompanyId == company.Id).ToListAsync(cancellationToken))
            .OrderByDescending(transfer => transfer.TransferDate)
            .ThenByDescending(transfer => transfer.TransferredAtUtc)
            .ToList();
        var salesQuotes = await dbContext.SalesQuotes.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.QuotedOn).ThenByDescending(x => x.QuoteNumber).ToListAsync(cancellationToken);
        var salesQuoteIds = salesQuotes.Select(quote => quote.Id).ToArray();
        var salesQuoteLines = salesQuoteIds.Length == 0 ? [] : await dbContext.SalesQuoteLines.AsNoTracking().Where(line => salesQuoteIds.Contains(line.SalesQuoteId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var salesOrders = await dbContext.SalesOrders.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.OrderedOn).ToListAsync(cancellationToken);
        var salesOrderIds = salesOrders.Select(order => order.Id).ToArray();
        var salesOrderLines = salesOrderIds.Length == 0 ? [] : await dbContext.SalesOrderLines.AsNoTracking().Where(line => salesOrderIds.Contains(line.SalesOrderId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var inventoryPicks = (await dbContext.InventoryPicks.AsNoTracking().Where(pick => pick.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(pick => pick.PickDate).ThenByDescending(pick => pick.CreatedAtUtc).ToList();
        var inventoryPickIds = inventoryPicks.Select(pick => pick.Id).ToArray(); var inventoryPickLines = inventoryPickIds.Length == 0 ? [] : await dbContext.InventoryPickLines.AsNoTracking().Where(line => inventoryPickIds.Contains(line.InventoryPickId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var inventoryPackingSlips = (await dbContext.InventoryPackingSlips.AsNoTracking().Where(pack => pack.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(pack => pack.PackedOn).ThenByDescending(pack => pack.PackedAtUtc).ToList();
        var inventoryPackingSlipIds = inventoryPackingSlips.Select(pack => pack.Id).ToArray(); var inventoryPackingSlipLines = inventoryPackingSlipIds.Length == 0 ? [] : await dbContext.InventoryPackingSlipLines.AsNoTracking().Where(line => inventoryPackingSlipIds.Contains(line.InventoryPackingSlipId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var backorderPromises = (await dbContext.SalesOrderBackorderPromises.AsNoTracking().Where(backorder => backorder.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderBy(backorder => backorder.Status is "Open" or "PartiallyFulfilled" ? 0 : 1).ThenBy(backorder => backorder.PromisedShipOn).ThenBy(backorder => backorder.CreatedAtUtc).ToList();
        var inventoryShipments = (await dbContext.InventoryShipments.AsNoTracking().Where(shipment => shipment.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(shipment => shipment.ShippedOn).ThenByDescending(shipment => shipment.ShippedAtUtc).ToList();
        var inventoryShipmentIds = inventoryShipments.Select(shipment => shipment.Id).ToArray();
        var inventoryShipmentLines = inventoryShipmentIds.Length == 0 ? [] : await dbContext.InventoryShipmentLines.AsNoTracking().Where(line => inventoryShipmentIds.Contains(line.InventoryShipmentId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var customerReturnAuthorizations = (await dbContext.CustomerReturnAuthorizations.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderBy(item => item.Status is "Open" or "PartiallyReceived" ? 0 : 1).ThenByDescending(item => item.AuthorizedOn).ThenByDescending(item => item.AuthorizedAtUtc).ToList();
        var customerReturnAuthorizationIds = customerReturnAuthorizations.Select(item => item.Id).ToArray();
        var customerReturnAuthorizationLines = customerReturnAuthorizationIds.Length == 0 ? [] : await dbContext.CustomerReturnAuthorizationLines.AsNoTracking().Where(line => customerReturnAuthorizationIds.Contains(line.CustomerReturnAuthorizationId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var customerReturnReceipts = (await dbContext.CustomerReturnReceipts.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(item => item.ReceivedOn).ThenByDescending(item => item.ReceivedAtUtc).ToList();
        var customerReturnReceiptIds = customerReturnReceipts.Select(item => item.Id).ToArray();
        var customerReturnReceiptLines = customerReturnReceiptIds.Length == 0 ? [] : await dbContext.CustomerReturnReceiptLines.AsNoTracking().Where(line => customerReturnReceiptIds.Contains(line.CustomerReturnReceiptId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var customerReturnCredits = (await dbContext.CustomerReturnCredits.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(item => item.CreditDate).ThenByDescending(item => item.CreatedAtUtc).ToList();
        var customerReturnCreditIds = customerReturnCredits.Select(item => item.Id).ToArray();
        var customerReturnCreditApplications = customerReturnCreditIds.Length == 0 ? [] : await dbContext.CustomerReturnCreditApplications.AsNoTracking().Where(item => item.CompanyId == company.Id && customerReturnCreditIds.Contains(item.CustomerReturnCreditId)).OrderByDescending(item => item.AppliedOn).ToListAsync(cancellationToken);
        var customerReturnCreditRefunds = customerReturnCreditIds.Length == 0 ? [] : await dbContext.CustomerReturnCreditRefunds.AsNoTracking().Where(item => item.CompanyId == company.Id && customerReturnCreditIds.Contains(item.CustomerReturnCreditId)).OrderByDescending(item => item.RefundDate).ToListAsync(cancellationToken);
        var purchaseRequisitions = (await dbContext.PurchaseRequisitions.AsNoTracking().Where(x => x.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderBy(x => x.Status is "Draft" or "Submitted" or "Approved" ? 0 : 1).ThenByDescending(x => x.RequestedOn).ThenByDescending(x => x.PreparedAtUtc).ToList();
        var purchaseRequisitionIds = purchaseRequisitions.Select(requisition => requisition.Id).ToArray();
        var purchaseRequisitionLines = purchaseRequisitionIds.Length == 0 ? [] : await dbContext.PurchaseRequisitionLines.AsNoTracking().Where(line => purchaseRequisitionIds.Contains(line.PurchaseRequisitionId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var purchaseOrders = await dbContext.PurchaseOrders.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.OrderedOn).ToListAsync(cancellationToken);
        var purchaseOrderIds = purchaseOrders.Select(order => order.Id).ToArray();
        var purchaseOrderLines = purchaseOrderIds.Length == 0 ? [] : await dbContext.PurchaseOrderLines.AsNoTracking().Where(line => purchaseOrderIds.Contains(line.PurchaseOrderId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var inventoryReceipts = (await dbContext.InventoryReceipts.AsNoTracking().Where(receipt => receipt.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(receipt => receipt.ReceivedOn).ThenByDescending(receipt => receipt.ReceivedAtUtc).ToList();
        var inventoryReceiptIds = inventoryReceipts.Select(receipt => receipt.Id).ToArray();
        var inventoryReceiptLines = inventoryReceiptIds.Length == 0 ? [] : await dbContext.InventoryReceiptLines.AsNoTracking().Where(line => inventoryReceiptIds.Contains(line.InventoryReceiptId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var supplierReturnAuthorizations = (await dbContext.SupplierReturnAuthorizations.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderBy(item => item.Status is "Open" or "PartiallyShipped" ? 0 : 1).ThenByDescending(item => item.AuthorizedOn).ThenByDescending(item => item.AuthorizedAtUtc).ToList();
        var supplierReturnAuthorizationIds = supplierReturnAuthorizations.Select(item => item.Id).ToArray();
        var supplierReturnAuthorizationLines = supplierReturnAuthorizationIds.Length == 0 ? [] : await dbContext.SupplierReturnAuthorizationLines.AsNoTracking().Where(line => supplierReturnAuthorizationIds.Contains(line.SupplierReturnAuthorizationId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var supplierReturnShipments = (await dbContext.SupplierReturnShipments.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(item => item.ShippedOn).ThenByDescending(item => item.ShippedAtUtc).ToList();
        var supplierReturnShipmentIds = supplierReturnShipments.Select(item => item.Id).ToArray();
        var supplierReturnShipmentLines = supplierReturnShipmentIds.Length == 0 ? [] : await dbContext.SupplierReturnShipmentLines.AsNoTracking().Where(line => supplierReturnShipmentIds.Contains(line.SupplierReturnShipmentId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var supplierReturnCreditApplications = supplierReturnShipmentIds.Length == 0 ? [] : await dbContext.SupplierReturnCreditApplications.AsNoTracking().Where(item => item.CompanyId == company.Id && supplierReturnShipmentIds.Contains(item.SupplierReturnShipmentId)).OrderByDescending(item => item.AppliedOn).ToListAsync(cancellationToken);
        var supplierReturnCreditRefunds = supplierReturnShipmentIds.Length == 0 ? [] : await dbContext.SupplierReturnCreditRefunds.AsNoTracking().Where(item => item.CompanyId == company.Id && supplierReturnShipmentIds.Contains(item.SupplierReturnShipmentId)).OrderByDescending(item => item.RefundDate).ToListAsync(cancellationToken);
        var landedCostAllocations = (await dbContext.LandedCostAllocations.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderBy(item => item.Status is "Draft" or "Submitted" or "Approved" ? 0 : 1).ThenByDescending(item => item.BillDate).ThenByDescending(item => item.PreparedAtUtc).ToList();
        var landedCostAllocationIds = landedCostAllocations.Select(item => item.Id).ToArray();
        var landedCostCharges = landedCostAllocationIds.Length == 0 ? [] : await dbContext.LandedCostCharges.AsNoTracking().Where(item => landedCostAllocationIds.Contains(item.LandedCostAllocationId)).OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var landedCostLines = landedCostAllocationIds.Length == 0 ? [] : await dbContext.LandedCostAllocationLines.AsNoTracking().Where(item => landedCostAllocationIds.Contains(item.LandedCostAllocationId)).OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var purchaseInvoiceMatches = (await dbContext.PurchaseInvoiceMatches.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderBy(item => item.Status is "Draft" or "Submitted" or "Approved" or "Rejected" ? 0 : 1).ThenByDescending(item => item.BillDate).ThenByDescending(item => item.PreparedAtUtc).ToList();
        var purchaseInvoiceMatchIds = purchaseInvoiceMatches.Select(item => item.Id).ToArray();
        var purchaseInvoiceMatchLines = purchaseInvoiceMatchIds.Length == 0 ? [] : await dbContext.PurchaseInvoiceMatchLines.AsNoTracking().Where(item => purchaseInvoiceMatchIds.Contains(item.PurchaseInvoiceMatchId)).OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var bankAccounts = await dbContext.BankAccounts.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var statementTransactions = await dbContext.BankStatementTransactions.AsNoTracking().Where(item => item.CompanyId == company.Id).OrderByDescending(item => item.TransactionDate).ToListAsync(cancellationToken);
        var importBatches = (await dbContext.BankStatementImportBatches.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(item => item.ImportedAtUtc).ToList();
        var reconciliations = (await dbContext.BankReconciliations.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(item => item.StatementDate).ToList();
        var reconciliationIds = reconciliations.Select(item => item.Id).ToArray();
        var reconciliationItems = reconciliationIds.Length == 0 ? [] : await dbContext.BankReconciliationItems.AsNoTracking().Where(item => reconciliationIds.Contains(item.BankReconciliationId)).ToListAsync(cancellationToken);
        var bankTransfers = await dbContext.BankTransfers.AsNoTracking().Where(item => item.CompanyId == company.Id).OrderByDescending(item => item.TransferDate).ToListAsync(cancellationToken);
        var bankEntryCandidates = await dbContext.JournalEntries.AsNoTracking()
            .Where(entry => entry.CompanyId == company.Id && entry.BankAccountId != null && entry.IsPosted && entry.Status == "Posted")
            .OrderBy(entry => entry.PostedOn)
            .ToListAsync(cancellationToken);
        var bankEntryIds = bankEntryCandidates.Select(entry => entry.Id).ToArray();
        var bankEntryLines = bankEntryIds.Length == 0
            ? new List<JournalEntryLine>()
            : await dbContext.JournalEntryLines.AsNoTracking().Where(line => bankEntryIds.Contains(line.JournalEntryId)).ToListAsync(cancellationToken);
        var employeeSummary = await dbContext.Employees.AsNoTracking().Where(x => x.CompanyId == company.Id).Select(x => new { x.IsActive, x.MonthlyBasePay }).ToListAsync(cancellationToken);
        var employees = canAccessPayroll ? await dbContext.Employees.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.EmployeeNumber).ToListAsync(cancellationToken) : [];
        var payrollJurisdictionRules = canAccessPayroll ? await dbContext.PayrollJurisdictionRules.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.ResidenceJurisdiction).ThenBy(x => x.WorkJurisdiction).ToListAsync(cancellationToken) : [];
        var payrollRuns = canAccessPayroll ? (await dbContext.PayrollRuns.AsNoTracking().Where(x => x.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(x => x.PayDate).ThenByDescending(x => x.PreparedAtUtc).ToList() : [];
        var payrollTimecards = canAccessPayroll ? (await dbContext.PayrollTimecards.AsNoTracking().Where(x => x.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(x => x.PeriodEnd).ThenBy(x => x.EmployeeId).ToList() : [];
        var payrollTimecardIds = payrollTimecards.Select(timecard => timecard.Id).ToArray();
        var payrollTimeEntries = payrollTimecardIds.Length == 0 ? [] : await dbContext.PayrollTimeEntries.AsNoTracking().Where(entry => payrollTimecardIds.Contains(entry.PayrollTimecardId)).OrderBy(entry => entry.Sequence).ToListAsync(cancellationToken);
        var payrollLiabilities = canAccessPayroll ? await dbContext.PayrollLiabilities.AsNoTracking().Where(liability => liability.CompanyId == company.Id).OrderBy(liability => liability.Status).ThenBy(liability => liability.DueDate).ThenBy(liability => liability.ObligationCode).ToListAsync(cancellationToken) : [];
        var payrollLiabilityPayments = canAccessPayroll ? (await dbContext.PayrollLiabilityPayments.AsNoTracking().Where(payment => payment.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(payment => payment.PaymentDate).ThenByDescending(payment => payment.CreatedAtUtc).ToList() : [];
        var payrollLiabilityPaymentIds = payrollLiabilityPayments.Select(payment => payment.Id).ToArray();
        var payrollLiabilityPaymentApplications = payrollLiabilityPaymentIds.Length == 0 ? [] : await dbContext.PayrollLiabilityPaymentApplications.AsNoTracking().Where(application => payrollLiabilityPaymentIds.Contains(application.PayrollLiabilityPaymentId)).ToListAsync(cancellationToken);
        var payrollLiabilityEmployeeLineIds = payrollLiabilities.Select(liability => liability.PayrollRunEmployeeLineId).Distinct().ToArray();
        var payrollLiabilityEmployeeLines = payrollLiabilityEmployeeLineIds.Length == 0 ? [] : await dbContext.PayrollRunEmployeeLines.AsNoTracking().Where(line => payrollLiabilityEmployeeLineIds.Contains(line.Id)).ToListAsync(cancellationToken);
        var projectJobs = await dbContext.ProjectJobs.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.JobNumber).ToListAsync(cancellationToken);
        var taxProfiles = await dbContext.TaxProfiles.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Jurisdiction).ThenBy(x => x.TaxType).ToListAsync(cancellationToken);
        var reports = await dbContext.ReportCatalogItems.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        var labels = await dbContext.LabelTemplates.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Name).ToListAsync(cancellationToken);

        var customerNames = customers.ToDictionary(x => x.Id, x => x.Name);
        var vendorNames = vendors.ToDictionary(x => x.Id, x => x.Name);
        var accountNumbersById = accounts.ToDictionary(account => account.Id, account => account.Number);
        var invoiceLineLookup = invoiceLines.ToLookup(line => line.SalesInvoiceId);
        var vendorBillLineLookup = vendorBillLines.ToLookup(line => line.VendorBillId);
        var paymentApplicationLookup = paymentApplications.ToLookup(application => application.SubledgerPaymentId);
        var payrollTimeEntryLookup = payrollTimeEntries.ToLookup(entry => entry.PayrollTimecardId);
        var payrollLiabilityEmployeeByLineId = payrollLiabilityEmployeeLines.ToDictionary(line => line.Id, line => line.EmployeeId);
        var payrollLiabilityById = payrollLiabilities.ToDictionary(liability => liability.Id);
        var payrollLiabilityPaymentApplicationLookup = payrollLiabilityPaymentApplications.ToLookup(application => application.PayrollLiabilityPaymentId);
        var employeeById = employees.ToDictionary(employee => employee.Id);
        var invoiceNumbersById = invoices.ToDictionary(invoice => invoice.Id, invoice => invoice.InvoiceNumber);
        var bankAccountNamesById = bankAccounts.ToDictionary(account => account.Id, account => account.Name);
        var billNumbersById = vendorBills.ToDictionary(bill => bill.Id, bill => bill.BillNumber);
        var vendorBillsByReceiptId = vendorBills.Where(bill => bill.InventoryReceiptId.HasValue).ToLookup(bill => bill.InventoryReceiptId!.Value);
        var purchaseRequisitionLineLookup = purchaseRequisitionLines.ToLookup(line => line.PurchaseRequisitionId);
        var purchaseOrderLineLookup = purchaseOrderLines.ToLookup(line => line.PurchaseOrderId);
        var inventoryReceiptLineLookup = inventoryReceiptLines.ToLookup(line => line.InventoryReceiptId);
        var inventoryItemById = inventoryItems.ToDictionary(item => item.Id);
        var inventoryWarehouseById = inventoryWarehouses.ToDictionary(warehouse => warehouse.Id);
        var inventoryBinById = inventoryBins.ToDictionary(bin => bin.Id);
        string InventoryLocationLabel(Guid? warehouseId, Guid? binId) => warehouseId.HasValue && binId.HasValue && inventoryWarehouseById.TryGetValue(warehouseId.Value, out var warehouse) && inventoryBinById.TryGetValue(binId.Value, out var bin) ? $"{warehouse.Code}/{bin.Code}" : "Unassigned";
        var purchaseOrderById = purchaseOrders.ToDictionary(order => order.Id);
        var purchaseOrderByRequisitionId = purchaseOrders.Where(order => order.PurchaseRequisitionId.HasValue).ToDictionary(order => order.PurchaseRequisitionId!.Value);
        var salesOrderById = salesOrders.ToDictionary(order => order.Id);
        var salesOrderByQuoteId = salesOrders.Where(order => order.SalesQuoteId.HasValue).ToDictionary(order => order.SalesQuoteId!.Value);
        var salesQuoteLineLookup = salesQuoteLines.ToLookup(line => line.SalesQuoteId);
        var salesOrderLineLookup = salesOrderLines.ToLookup(line => line.SalesOrderId);
        var inventoryShipmentLineLookup = inventoryShipmentLines.ToLookup(line => line.InventoryShipmentId);
        var inventoryShipmentById = inventoryShipments.ToDictionary(item => item.Id);
        var customerReturnAuthorizationById = customerReturnAuthorizations.ToDictionary(item => item.Id);
        var customerReturnAuthorizationLineLookup = customerReturnAuthorizationLines.ToLookup(line => line.CustomerReturnAuthorizationId);
        var customerReturnReceiptLineLookup = customerReturnReceiptLines.ToLookup(line => line.CustomerReturnReceiptId);
        var customerReturnReceiptById = customerReturnReceipts.ToDictionary(item => item.Id);
        var customerReturnCreditApplicationLookup = customerReturnCreditApplications.ToLookup(item => item.CustomerReturnCreditId);
        var customerReturnCreditRefundLookup = customerReturnCreditRefunds.ToLookup(item => item.CustomerReturnCreditId);
        var supplierReturnAuthorizationById = supplierReturnAuthorizations.ToDictionary(item => item.Id);
        var supplierReturnAuthorizationLineLookup = supplierReturnAuthorizationLines.ToLookup(line => line.SupplierReturnAuthorizationId);
        var supplierReturnShipmentLineLookup = supplierReturnShipmentLines.ToLookup(line => line.SupplierReturnShipmentId);
        var supplierReturnCreditApplicationLookup = supplierReturnCreditApplications.ToLookup(item => item.SupplierReturnShipmentId);
        var supplierReturnCreditRefundLookup = supplierReturnCreditRefunds.ToLookup(item => item.SupplierReturnShipmentId);
        var landedCostChargeLookup = landedCostCharges.ToLookup(item => item.LandedCostAllocationId);
        var landedCostLineLookup = landedCostLines.ToLookup(item => item.LandedCostAllocationId);
        var purchaseInvoiceMatchLineLookup = purchaseInvoiceMatchLines.ToLookup(item => item.PurchaseInvoiceMatchId);
        var inventoryReceiptById = inventoryReceipts.ToDictionary(receipt => receipt.Id);

        var moduleCounts = BuildModuleCounts(
            accounts.Count + journalEntries.Count,
            customers.Count + invoices.Count,
            vendors.Count + vendorBills.Count,
            employeeSummary.Count + taxProfiles.Count,
            inventoryItems.Count,
            salesQuotes.Count + salesOrders.Count,
            purchaseOrders.Count,
            bankAccounts.Count,
            projectJobs.Count);

        var assessment = assessmentService.GetCatalog();

        return new BusinessWorkspaceSnapshot(
            GeneratedAtUtc: DateTime.UtcNow,
            Company: new CompanySnapshot(
                Name: company.Name,
                LegalName: company.LegalName,
                TaxId: MaskTaxId(company.TaxId),
                BaseCurrency: company.BaseCurrency,
                FiscalYearStartMonth: company.FiscalYearStartMonth,
                ActiveUsers: users.Count),
            Dashboard: new DashboardSnapshot(
                CashOnHand: bankAccounts.Sum(x => x.CurrentBalance),
                ReceivablesOpen: invoices.Sum(x => x.BalanceDue),
                PayablesOpen: vendorBills.Sum(x => x.BalanceDue),
                MonthlyPayroll: employeeSummary.Where(x => x.IsActive).Sum(x => x.MonthlyBasePay),
                InventoryItems: inventoryItems.Count,
                OpenSalesOrders: salesOrders.Count(x => x.Status is "Draft" or "Approved" or "Allocated" or "PartiallyShipped" or "Shipped" or "ClosedPendingInvoice"),
                OpenProjects: projectJobs.Count(x => x.Status is "Open" or "Billing"),
                EnabledModules: assessment.Modules.Count,
                ReportsReady: reports.Count + labels.Count),
            Modules: assessment.Modules
                .Select(module =>
                {
                    var details = moduleCounts.GetValueOrDefault(module.Code);
                    return new ModuleWorkspaceSnapshot(
                        module.Code,
                        module.Name,
                        module.Area,
                        string.IsNullOrWhiteSpace(details.Status) ? "Planned" : details.Status,
                        string.IsNullOrWhiteSpace(details.Summary) ? "Modeled in the open-source roadmap." : details.Summary,
                        details.RecordCount);
                })
                .ToArray(),
            GeneralLedger: new GeneralLedgerWorkspace(
                Assets: SumByType(accounts, AccountType.Asset),
                Liabilities: SumByType(accounts, AccountType.Liability),
                Equity: SumByType(accounts, AccountType.Equity),
                Revenue: SumByType(accounts, AccountType.Revenue),
                Expenses: SumByType(accounts, AccountType.Expense),
                Accounts: accounts.Select(x => new AccountSnapshot(x.Number, x.Name, x.Type.ToString(), x.CurrentBalance, x.IsControlAccount, x.OperationalRole ?? string.Empty)).ToArray(),
                RecentEntries: journalEntries.Select(x => new JournalEntrySnapshot(x.EntryNumber, x.PostedOn, x.SourceModule, x.Description, x.TotalAmount, x.Id, x.Reference, x.Status, x.ReversalOfJournalEntryId, x.ReversedByJournalEntryId, x.RejectedAtUtc, x.DecisionReason, x.ConcurrencyToken, journalEntryLines.Where(line => line.JournalEntryId == x.Id).Select(line => new JournalEntryLineSnapshot(accounts.Single(account => account.Id == line.AccountId).Number, line.Description, line.Debit, line.Credit)).ToArray())).ToArray()),
            Receivables: new ReceivablesWorkspace(
                OpenBalance: invoices.Sum(x => x.BalanceDue),
                PastDueCount: invoices.Count(x => x.DueDate < DateOnly.FromDateTime(DateTime.Today) && x.BalanceDue > 0m),
                Customers: customers.Select(x => new CustomerSnapshot(x.CustomerNumber, x.Name, x.State, x.CreditLimit, x.OpenBalance, x.Id)).ToArray(),
                Invoices: invoices.Select(x => new InvoiceSnapshot(
                    x.InvoiceNumber,
                    customerNames.GetValueOrDefault(x.CustomerId, "Unknown customer"),
                    x.InvoiceDate,
                    x.DueDate,
                    x.Status,
                    x.TotalAmount,
                    x.BalanceDue,
                    x.Id,
                    invoiceLineLookup[x.Id].Select(line => new InvoiceLineSnapshot(line.Sequence, line.Description, line.Quantity, line.UnitPrice, line.DiscountAmount, line.TaxAmount, line.LineTotal, accountNumbersById.GetValueOrDefault(line.RevenueAccountId, "Unavailable"))).ToArray(),
                    x.CustomerId)).ToArray(),
                Payments: subledgerPayments.Where(payment => payment.Direction == "CustomerReceipt").Select(payment => new SubledgerPaymentSnapshot(payment.Id, payment.Direction, customerNames.GetValueOrDefault(payment.CounterpartyId, "Unknown customer"), payment.PaymentDate, payment.Amount, payment.AppliedAmount, payment.UnappliedAmount, payment.Reference, payment.Method, payment.Status, paymentApplicationLookup[payment.Id].Select(application => new PaymentApplicationSnapshot(application.DocumentId, invoiceNumbersById.GetValueOrDefault(application.DocumentId, "Unavailable"), application.Amount)).ToArray())).ToArray(),
                Adjustments: subledgerAdjustments.Where(adjustment => adjustment.Subledger == "Receivables").Select(adjustment => new SubledgerAdjustmentSnapshot(adjustment.Id, adjustment.Subledger, adjustment.Kind, adjustment.CounterpartyId, customerNames.GetValueOrDefault(adjustment.CounterpartyId, "Unknown customer"), adjustment.DocumentId, adjustment.DocumentId.HasValue ? invoiceNumbersById.GetValueOrDefault(adjustment.DocumentId.Value, "Unavailable") : string.Empty, adjustment.PaymentId, adjustment.AdjustmentDate, adjustment.Amount, adjustment.Reference, adjustment.Reason, adjustment.OffsetAccountNumber, adjustment.Status, adjustment.JournalEntryId, adjustment.ReversalJournalEntryId)).ToArray(),
                Workflows: subledgerWorkflows.Where(workflow => workflow.DocumentType == "Invoice").Select(ToWorkflowSnapshot).ToArray()),
            Payables: new PayablesWorkspace(
                OpenBalance: vendorBills.Sum(x => x.BalanceDue),
                DueThisWeekCount: vendorBills.Count(x => x.DueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(7)) && x.BalanceDue > 0m),
                Vendors: vendors.Select(x => new VendorSnapshot(x.VendorNumber, x.Name, x.State, x.PaymentTerms, x.OpenBalance, x.Id)).ToArray(),
                Bills: vendorBills.Select(x => new BillSnapshot(
                    x.BillNumber,
                    vendorNames.GetValueOrDefault(x.VendorId, "Unknown vendor"),
                    x.BillDate,
                    x.DueDate,
                    x.Status,
                    x.TotalAmount,
                    x.BalanceDue,
                    x.Id,
                    vendorBillLineLookup[x.Id].Select(line => new BillLineSnapshot(line.Sequence, line.Description, line.Quantity, line.UnitCost, line.DiscountAmount, line.TaxAmount, line.LineTotal, accountNumbersById.GetValueOrDefault(line.ExpenseAccountId, "Unavailable"))).ToArray(),
                    x.VendorId)).ToArray(),
                Payments: subledgerPayments.Where(payment => payment.Direction == "VendorDisbursement").Select(payment => new SubledgerPaymentSnapshot(payment.Id, payment.Direction, vendorNames.GetValueOrDefault(payment.CounterpartyId, "Unknown vendor"), payment.PaymentDate, payment.Amount, payment.AppliedAmount, payment.UnappliedAmount, payment.Reference, payment.Method, payment.Status, paymentApplicationLookup[payment.Id].Select(application => new PaymentApplicationSnapshot(application.DocumentId, billNumbersById.GetValueOrDefault(application.DocumentId, "Unavailable"), application.Amount)).ToArray())).ToArray(),
                Adjustments: subledgerAdjustments.Where(adjustment => adjustment.Subledger == "Payables").Select(adjustment => new SubledgerAdjustmentSnapshot(adjustment.Id, adjustment.Subledger, adjustment.Kind, adjustment.CounterpartyId, vendorNames.GetValueOrDefault(adjustment.CounterpartyId, "Unknown vendor"), adjustment.DocumentId, adjustment.DocumentId.HasValue ? billNumbersById.GetValueOrDefault(adjustment.DocumentId.Value, "Unavailable") : string.Empty, adjustment.PaymentId, adjustment.AdjustmentDate, adjustment.Amount, adjustment.Reference, adjustment.Reason, adjustment.OffsetAccountNumber, adjustment.Status, adjustment.JournalEntryId, adjustment.ReversalJournalEntryId)).ToArray(),
                Workflows: subledgerWorkflows.Where(workflow => workflow.DocumentType == "VendorBill").Select(ToWorkflowSnapshot).ToArray()),
            Operations: new OperationsWorkspace(
                InventoryItemCount: inventoryItems.Count,
                ReorderAlerts: inventoryItems.Count(x => x.QuantityOnHand <= x.ReorderPoint),
                OpenSalesOrderCount: salesOrders.Count(x => x.Status is "Draft" or "Approved" or "Allocated" or "PartiallyShipped" or "Shipped" or "ClosedPendingInvoice"),
                OpenPurchaseOrderCount: purchaseOrders.Count(x => x.Status is "Draft" or "Issued" or "Approved" or "PartiallyReceived" or "Received"),
                InventoryItems: inventoryItems.Select(x => new InventoryItemSnapshot(x.Sku, x.Description, x.UnitPrice, x.QuantityOnHand, x.ReorderPoint, x.Id, x.UnitCost)).ToArray(),
                SalesOrders: salesOrders.Select(x => new SalesOrderSnapshot(
                    x.OrderNumber,
                    customerNames.GetValueOrDefault(x.CustomerId, "Unknown customer"),
                    x.OrderedOn,
                    x.Status,
                    x.TotalAmount,
                    x.Id,
                    x.CustomerId,
                    x.RequestedShipOn,
                    x.Notes,
                    x.ConcurrencyToken,
                    salesOrderLineLookup[x.Id].Select(line => new SalesOrderLineSnapshot(line.Id, line.Sequence, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Description, line.OrderedQuantity, line.AllocatedQuantity, line.ShippedQuantity, line.CancelledQuantity, line.ReturnedQuantity, line.InvoicedQuantity, line.UnitPrice, line.DiscountAmount, line.TaxAmount, line.LineTotal, accountNumbersById.GetValueOrDefault(line.RevenueAccountId, "Unavailable"), line.AllocationWarehouseId, line.AllocationBinId, InventoryLocationLabel(line.AllocationWarehouseId, line.AllocationBinId))).ToArray(),
                    x.SalesQuoteId)).ToArray(),
                PurchaseOrders: purchaseOrders.Select(x => new PurchaseOrderSnapshot(
                    x.OrderNumber,
                    vendorNames.GetValueOrDefault(x.VendorId, "Unknown vendor"),
                    x.OrderedOn,
                    x.Status,
                    x.TotalAmount,
                    x.Id,
                    x.VendorId,
                    x.ExpectedOn,
                    x.Notes,
                    x.ConcurrencyToken,
                    purchaseOrderLineLookup[x.Id].Select(line => new PurchaseOrderLineSnapshot(line.Id, line.Sequence, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Description, line.OrderedQuantity, line.UnitCost, line.ReceivedQuantity, line.InvoicedQuantity, line.LineTotal, line.ReturnedQuantity, line.CreditedQuantity)).ToArray())).ToArray(),
                InventoryReceipts: inventoryReceipts.Select(receipt =>
                {
                    var matchedBills = vendorBillsByReceiptId[receipt.Id].OrderByDescending(bill => bill.BillDate).ToArray();
                    return new InventoryReceiptSnapshot(receipt.Id, receipt.PurchaseOrderId, purchaseOrderById.GetValueOrDefault(receipt.PurchaseOrderId)?.OrderNumber ?? "Unavailable", receipt.ReceiptNumber, receipt.ReceivedOn, receipt.Status, receipt.TotalAmount, matchedBills.FirstOrDefault(bill => bill.Status != "Voided")?.Id, receipt.ConcurrencyToken, inventoryReceiptLineLookup[receipt.Id].Select(line => new InventoryReceiptLineSnapshot(line.Id, line.PurchaseOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.Quantity, line.UnitCost, line.LineTotal, line.ReturnedQuantity)).ToArray(), receipt.JournalEntryId, receipt.ReversalJournalEntryId, receipt.WarehouseId, receipt.BinId, InventoryLocationLabel(receipt.WarehouseId, receipt.BinId), matchedBills.Select(bill => new MatchedVendorBillSnapshot(bill.Id, bill.BillNumber, bill.TotalAmount, bill.BalanceDue, bill.Status)).ToArray());
                }).ToArray(),
                InventoryShipments: inventoryShipments.Select(shipment => new InventoryShipmentSnapshot(shipment.Id, shipment.SalesOrderId, salesOrderById.GetValueOrDefault(shipment.SalesOrderId)?.OrderNumber ?? "Unavailable", shipment.ShipmentNumber, shipment.ShippedOn, shipment.Status, shipment.TotalCost, shipment.SalesInvoiceId, shipment.ConcurrencyToken, inventoryShipmentLineLookup[shipment.Id].Select(line => new InventoryShipmentLineSnapshot(line.Id, line.SalesOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.Quantity, line.UnitCost, line.TotalCost)).ToArray(), shipment.JournalEntryId, shipment.ReversalJournalEntryId, shipment.WarehouseId, shipment.BinId, InventoryLocationLabel(shipment.WarehouseId, shipment.BinId), shipment.InventoryPackingSlipId)).ToArray(),
                SalesQuotes: salesQuotes.Select(quote => new SalesQuoteSnapshot(
                    quote.Id, quote.CustomerId, quote.QuoteNumber, customerNames.GetValueOrDefault(quote.CustomerId, "Unknown customer"), quote.QuotedOn, quote.ExpiresOn,
                    quote.Status, quote.Status == "Approved" && quote.ExpiresOn < DateOnly.FromDateTime(DateTime.UtcNow), quote.TotalAmount, quote.Notes,
                    salesOrderByQuoteId.GetValueOrDefault(quote.Id)?.Id, quote.ConcurrencyToken,
                    salesQuoteLineLookup[quote.Id].Select(line => new SalesQuoteLineSnapshot(line.Id, line.Sequence, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Description, line.Quantity, line.UnitPrice, line.DiscountAmount, line.TaxAmount, line.LineTotal, accountNumbersById.GetValueOrDefault(line.RevenueAccountId, "Unavailable"))).ToArray())).ToArray(),
                Warehouses: inventoryWarehouses.Select(warehouse => new InventoryWarehouseSnapshot(warehouse.Id, warehouse.Code, warehouse.Name, warehouse.AddressLine1, warehouse.AddressLine2, warehouse.City, warehouse.StateOrProvince, warehouse.PostalCode, warehouse.CountryCode, warehouse.IsDefault, warehouse.IsActive, warehouse.ConcurrencyToken,
                    inventoryBins.Where(bin => bin.WarehouseId == warehouse.Id).Select(bin => new InventoryBinSnapshot(bin.Id, bin.WarehouseId, bin.Code, bin.Name, bin.IsDefault, bin.IsActive, bin.ConcurrencyToken,
                        inventoryLocationBalances.Where(balance => balance.BinId == bin.Id).Select(balance => new InventoryLocationBalanceSnapshot(balance.Id, balance.InventoryItemId, inventoryItemById.GetValueOrDefault(balance.InventoryItemId)?.Sku ?? "Unavailable", balance.QuantityOnHand, balance.ConcurrencyToken)).ToArray())).ToArray())).ToArray(),
                InventoryTransfers: inventoryTransfers.Select(transfer =>
                {
                    var sourceWarehouse = inventoryWarehouses.Single(warehouse => warehouse.Id == transfer.SourceWarehouseId); var sourceBin = inventoryBins.Single(bin => bin.Id == transfer.SourceBinId); var destinationWarehouse = inventoryWarehouses.Single(warehouse => warehouse.Id == transfer.DestinationWarehouseId); var destinationBin = inventoryBins.Single(bin => bin.Id == transfer.DestinationBinId);
                    return new InventoryTransferSnapshot(transfer.Id, transfer.InventoryItemId, inventoryItemById.GetValueOrDefault(transfer.InventoryItemId)?.Sku ?? "Unavailable", transfer.SourceWarehouseId, transfer.SourceBinId, $"{sourceWarehouse.Code}/{sourceBin.Code}", transfer.DestinationWarehouseId, transfer.DestinationBinId, $"{destinationWarehouse.Code}/{destinationBin.Code}", transfer.Quantity, transfer.UnitCost, transfer.TransferDate, transfer.Reference, transfer.Reason, transfer.Status, transfer.ConcurrencyToken);
                }).ToArray(),
                InventoryPicks: inventoryPicks.Select(pick => new InventoryPickSnapshot(pick.Id, pick.SalesOrderId, salesOrderById.GetValueOrDefault(pick.SalesOrderId)?.OrderNumber ?? "Unavailable", pick.WarehouseId, pick.BinId, InventoryLocationLabel(pick.WarehouseId, pick.BinId), pick.PickNumber, pick.PickDate, pick.Status, pick.ConcurrencyToken, inventoryPickLines.Where(line => line.InventoryPickId == pick.Id).Select(line => new InventoryPickLineSnapshot(line.Id, line.SalesOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.RequestedQuantity, line.PickedQuantity, inventoryPackingSlipLines.Where(packed => packed.InventoryPickLineId == line.Id && inventoryPackingSlips.Any(pack => pack.Id == packed.InventoryPackingSlipId && pack.Status != "Cancelled")).Sum(packed => packed.Quantity))).ToArray())).ToArray(),
                InventoryPackingSlips: inventoryPackingSlips.Select(pack => new InventoryPackingSlipSnapshot(pack.Id, pack.SalesOrderId, salesOrderById.GetValueOrDefault(pack.SalesOrderId)?.OrderNumber ?? "Unavailable", pack.InventoryPickId, pack.WarehouseId, pack.BinId, InventoryLocationLabel(pack.WarehouseId, pack.BinId), pack.PackingSlipNumber, pack.PackedOn, pack.Status, inventoryShipments.Where(shipment => shipment.InventoryPackingSlipId == pack.Id).OrderBy(shipment => shipment.Status == "Posted" ? 0 : 1).ThenByDescending(shipment => shipment.ShippedOn).ThenByDescending(shipment => shipment.ShippedAtUtc).FirstOrDefault()?.Id, pack.ConcurrencyToken, inventoryPackingSlipLines.Where(line => line.InventoryPackingSlipId == pack.Id).Select(line => new InventoryPackingSlipLineSnapshot(line.Id, line.InventoryPickLineId, line.SalesOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.Quantity)).ToArray())).ToArray(),
                BackorderPromises: backorderPromises.Select(backorder => { var line = salesOrderLines.Single(candidate => candidate.Id == backorder.SalesOrderLineId); return new SalesOrderBackorderPromiseSnapshot(backorder.Id, backorder.SalesOrderId, salesOrderById.GetValueOrDefault(backorder.SalesOrderId)?.OrderNumber ?? "Unavailable", backorder.SalesOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", backorder.PromisedQuantity, backorder.FulfilledQuantity, backorder.PromisedQuantity - backorder.FulfilledQuantity, backorder.PromisedShipOn, backorder.Reason, backorder.Status, backorder.ConcurrencyToken); }).ToArray(),
                CustomerReturnAuthorizations: customerReturnAuthorizations.Select(item => new CustomerReturnAuthorizationSnapshot(item.Id, item.InventoryShipmentId, inventoryShipmentById.GetValueOrDefault(item.InventoryShipmentId)?.ShipmentNumber ?? "Unavailable", item.SalesOrderId, salesOrderById.GetValueOrDefault(item.SalesOrderId)?.OrderNumber ?? "Unavailable", item.CustomerId, customerNames.GetValueOrDefault(item.CustomerId, "Unknown customer"), item.ReturnNumber, item.AuthorizedOn, item.Reason, item.Status, item.ConcurrencyToken, customerReturnAuthorizationLineLookup[item.Id].Select(line => new CustomerReturnAuthorizationLineSnapshot(line.Id, line.InventoryShipmentLineId, line.SalesOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.AuthorizedQuantity, line.ReceivedQuantity)).ToArray())).ToArray(),
                CustomerReturnReceipts: customerReturnReceipts.Select(item => new CustomerReturnReceiptSnapshot(item.Id, item.CustomerReturnAuthorizationId, customerReturnAuthorizationById.GetValueOrDefault(item.CustomerReturnAuthorizationId)?.ReturnNumber ?? "Unavailable", item.ReceiptNumber, item.ReceivedOn, item.Status, item.TotalCost, item.WarehouseId, item.BinId, InventoryLocationLabel(item.WarehouseId, item.BinId), item.JournalEntryId, item.ReversalJournalEntryId, item.ConcurrencyToken, customerReturnReceiptLineLookup[item.Id].Select(line => new CustomerReturnReceiptLineSnapshot(line.Id, line.CustomerReturnAuthorizationLineId, line.InventoryShipmentLineId, line.SalesOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.Quantity, line.UnitCost, line.TotalCost)).ToArray())).ToArray(),
                CustomerReturnCredits: customerReturnCredits.Select(item => new CustomerReturnCreditSnapshot(item.Id, item.CustomerReturnReceiptId, customerReturnReceiptById.GetValueOrDefault(item.CustomerReturnReceiptId)?.ReceiptNumber ?? "Unavailable", item.SalesInvoiceId, invoiceNumbersById.GetValueOrDefault(item.SalesInvoiceId, "Unavailable"), item.CustomerId, customerNames.GetValueOrDefault(item.CustomerId, "Unknown customer"), item.CreditNumber, item.CreditDate, item.Reason, item.Status, item.Subtotal, item.TaxAmount, item.TotalAmount, item.SourceAppliedAmount, item.AppliedAmount, item.RefundedAmount, item.TotalAmount - item.AppliedAmount - item.RefundedAmount, item.JournalEntryId, item.ReversalJournalEntryId, item.ConcurrencyToken, customerReturnCreditApplicationLookup[item.Id].Select(application => new CustomerReturnCreditApplicationSnapshot(application.Id, application.SalesInvoiceId, invoiceNumbersById.GetValueOrDefault(application.SalesInvoiceId, "Unavailable"), application.AppliedOn, application.Amount, application.Status, application.ConcurrencyToken)).ToArray(), customerReturnCreditRefundLookup[item.Id].Select(refund => new CustomerReturnCreditRefundSnapshot(refund.Id, refund.BankAccountId, bankAccountNamesById.GetValueOrDefault(refund.BankAccountId, "Unavailable"), refund.Reference, refund.RefundDate, refund.Amount, refund.Status, refund.JournalEntryId, refund.ReversalJournalEntryId, refund.ConcurrencyToken)).ToArray())).ToArray(),
                PurchaseRequisitions: purchaseRequisitions.Select(requisition => { var order = purchaseOrderByRequisitionId.GetValueOrDefault(requisition.Id); return new PurchaseRequisitionSnapshot(requisition.Id, requisition.RequestedVendorId, requisition.RequestedVendorId.HasValue ? vendorNames.GetValueOrDefault(requisition.RequestedVendorId.Value, "Unavailable") : "Vendor to be selected", requisition.RequisitionNumber, requisition.RequestedOn, requisition.NeededBy, requisition.Purpose, requisition.Status, requisition.TotalEstimatedAmount, requisition.DecisionReason, requisition.CancellationReason, order?.Id, order?.OrderNumber ?? string.Empty, requisition.ConcurrencyToken, purchaseRequisitionLineLookup[requisition.Id].Select(line => new PurchaseRequisitionLineSnapshot(line.Id, line.Sequence, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Description, line.RequestedQuantity, line.EstimatedUnitCost, line.EstimatedLineTotal)).ToArray()); }).ToArray(),
                SupplierReturnAuthorizations: supplierReturnAuthorizations.Select(item => new SupplierReturnAuthorizationSnapshot(item.Id, item.InventoryReceiptId, inventoryReceiptById.GetValueOrDefault(item.InventoryReceiptId)?.ReceiptNumber ?? "Unavailable", item.PurchaseOrderId, purchaseOrderById.GetValueOrDefault(item.PurchaseOrderId)?.OrderNumber ?? "Unavailable", item.VendorId, vendorNames.GetValueOrDefault(item.VendorId, "Unknown vendor"), item.ReturnNumber, item.AuthorizedOn, item.Reason, item.Status, item.CancellationReason, item.ConcurrencyToken, supplierReturnAuthorizationLineLookup[item.Id].Select(line => new SupplierReturnAuthorizationLineSnapshot(line.Id, line.InventoryReceiptLineId, line.PurchaseOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.AuthorizedQuantity, line.ShippedQuantity, line.UnitCost, line.ReceiptUnitCost)).ToArray())).ToArray(),
                SupplierReturnShipments: supplierReturnShipments.Select(item =>
                {
                    var authorization = supplierReturnAuthorizationById[item.SupplierReturnAuthorizationId];
                    return new SupplierReturnShipmentSnapshot(
                        item.Id, item.SupplierReturnAuthorizationId, authorization.ReturnNumber, item.SourceVendorBillId,
                        item.SourceVendorBillId.HasValue ? billNumbersById.GetValueOrDefault(item.SourceVendorBillId.Value, "Unavailable") : string.Empty,
                        authorization.VendorId, vendorNames.GetValueOrDefault(authorization.VendorId, "Unknown vendor"), item.ShipmentNumber,
                        item.ShippedOn, item.Status, item.TotalAmount, item.VendorCreditAmount, item.CreatesVendorCredit,
                        item.SourceAppliedAmount, item.AppliedAmount, item.RefundedAmount, item.VendorCreditAmount - item.AppliedAmount - item.RefundedAmount,
                        item.WarehouseId, item.BinId, InventoryLocationLabel(item.WarehouseId, item.BinId), item.JournalEntryId,
                        item.ReversalJournalEntryId, item.ConcurrencyToken,
                        supplierReturnShipmentLineLookup[item.Id].Select(line => new SupplierReturnShipmentLineSnapshot(
                            line.Id, line.SupplierReturnAuthorizationLineId, line.InventoryReceiptLineId, line.PurchaseOrderLineId,
                            line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence,
                            line.Quantity, line.UnitCost, line.TotalAmount, line.InvoicedQuantity, line.GrniReductionAmount,
                            line.VendorCreditUnitCost, line.VendorCreditAmount)).ToArray(),
                        supplierReturnCreditApplicationLookup[item.Id].Select(application => new SupplierReturnCreditApplicationSnapshot(
                            application.Id, application.VendorBillId, billNumbersById.GetValueOrDefault(application.VendorBillId, "Unavailable"),
                            application.AppliedOn, application.Amount, application.Status, application.ConcurrencyToken)).ToArray(),
                        supplierReturnCreditRefundLookup[item.Id].Select(refund => new SupplierReturnCreditRefundSnapshot(
                            refund.Id, refund.BankAccountId, bankAccountNamesById.GetValueOrDefault(refund.BankAccountId, "Unavailable"),
                            refund.Reference, refund.RefundDate, refund.Amount, refund.Status, refund.JournalEntryId,
                            refund.ReversalJournalEntryId, refund.ConcurrencyToken)).ToArray());
                }).ToArray(),
                LandedCostAllocations: landedCostAllocations.Select(item => new LandedCostAllocationSnapshot(item.Id, item.InventoryReceiptId, inventoryReceiptById.GetValueOrDefault(item.InventoryReceiptId)?.ReceiptNumber ?? "Unavailable", item.VendorId, vendorNames.GetValueOrDefault(item.VendorId, "Unknown vendor"), item.VendorBillId, item.AllocationNumber, item.BillNumber, item.BillDate, item.DueDate, item.AllocationMethod, item.Description, item.Status, item.TotalAmount, item.DecisionReason, item.CancellationReason, item.JournalEntryId, item.ReversalJournalEntryId, item.ConcurrencyToken, landedCostChargeLookup[item.Id].Select(charge => new LandedCostChargeSnapshot(charge.Id, charge.Sequence, charge.ChargeType, charge.Description, charge.Amount)).ToArray(), landedCostLineLookup[item.Id].Select(line => new LandedCostAllocationLineSnapshot(line.Id, line.InventoryReceiptLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.BasisQuantity, line.BasisAmount, line.AllocatedAmount, line.PriorQuantityOnHand, line.PriorUnitCost, line.ResultingUnitCost)).ToArray())).ToArray(),
                PurchaseInvoiceMatches: purchaseInvoiceMatches.Select(item => new PurchaseInvoiceMatchSnapshot(item.Id, item.InventoryReceiptId, inventoryReceiptById.GetValueOrDefault(item.InventoryReceiptId)?.ReceiptNumber ?? "Unavailable", item.PurchaseOrderId, purchaseOrderById.GetValueOrDefault(item.PurchaseOrderId)?.OrderNumber ?? "Unavailable", item.VendorId, vendorNames.GetValueOrDefault(item.VendorId, "Unknown vendor"), item.VendorBillId, item.BillNumber, item.BillDate, item.DueDate, item.Description, item.Status, item.InvoiceAmount, item.AccrualAmount, item.PriceVarianceAmount, item.QuantityVarianceQuantity, item.QuantityVarianceAmount, item.DecisionReason, item.CancellationReason, item.JournalEntryId, item.ReversalJournalEntryId, item.ConcurrencyToken, purchaseInvoiceMatchLineLookup[item.Id].Select(line => new PurchaseInvoiceMatchLineSnapshot(line.Id, line.InventoryReceiptLineId, line.PurchaseOrderLineId, line.InventoryItemId, inventoryItemById.GetValueOrDefault(line.InventoryItemId)?.Sku ?? "Unavailable", line.Sequence, line.AvailableQuantity, line.InvoiceQuantity, line.MatchedQuantity, line.QuantityVarianceQuantity, line.ReceiptUnitCost, line.InvoiceUnitCost, line.AccrualAmount, line.InvoiceAmount, line.PriceVarianceAmount, line.QuantityVarianceAmount)).ToArray())).ToArray()),
            Treasury: new TreasuryWorkspace(
                CashOnHand: bankAccounts.Sum(x => x.CurrentBalance),
                UnreconciledBalance: bankAccounts.Sum(x => x.UnreconciledAmount),
                BankAccounts: bankAccounts.Select(x => new BankAccountSnapshot(x.Name, x.AccountNumberMasked, x.CurrentBalance, x.UnreconciledAmount, x.LastReconciledOn, x.Id, accounts.FirstOrDefault(account => account.Id == x.LedgerAccountId)?.Number ?? "Unmapped", x.LastReconciledBalance)).ToArray(),
                ReconciliationCandidates: bankEntryCandidates
                    .Where(entry => entry.BankAccountId is not null)
                    .Where(entry => entry.PostedOn > bankAccounts.Single(bank => bank.Id == entry.BankAccountId!.Value).LastReconciledOn)
                    .Select(entry => new BankReconciliationCandidateSnapshot(
                        entry.BankAccountId!.Value,
                        entry.Id,
                        entry.PostedOn,
                        entry.Reference,
                        entry.Description,
                        entry.SourceModule,
                        bankEntryLines.Where(line => line.JournalEntryId == entry.Id && line.AccountId == bankAccounts.Single(bank => bank.Id == entry.BankAccountId!.Value).LedgerAccountId).Sum(line => line.Debit - line.Credit)))
                    .ToArray(),
                StatementTransactions: statementTransactions.Select(item => new BankStatementTransactionSnapshot(item.Id, item.BankAccountId, item.ImportBatchId, item.ExternalId, item.TransactionDate, item.Amount, item.TransactionType, item.Payee, item.Memo, item.Reference, item.Status, item.MatchedJournalEntryId, item.MatchNote)).ToArray(),
                ImportBatches: importBatches.Select(item => new BankStatementImportBatchSnapshot(item.Id, item.BankAccountId, item.FileName, item.Format, item.Status, item.ImportedCount, item.DuplicateCount, item.RejectedCount, item.DebitTotal, item.CreditTotal, item.ImportedAtUtc)).ToArray(),
                Reconciliations: reconciliations.Select(item => new BankReconciliationSnapshot(item.Id, item.BankAccountId, item.StatementDate, item.OpeningBalance, item.ClearedAmount, item.StatementClosingBalance, item.BookBalance, item.Variance, item.Status, item.Notes, item.ReconciledAtUtc, item.ReopenedAtUtc, item.ReopenReason, reconciliationItems.Count(reconciliationItem => reconciliationItem.BankReconciliationId == item.Id))).ToArray(),
                Transfers: bankTransfers.Select(item => new BankTransferSnapshot(item.Id, item.FromBankAccountId, item.ToBankAccountId, item.TransferDate, item.Amount, item.Reference, item.Memo, item.Status, item.JournalEntryId, item.InboundJournalEntryId, item.ReversalJournalEntryId, item.InboundReversalJournalEntryId, item.ReversalDate, item.ReversalReason)).ToArray(),
                Adjustments: subledgerAdjustments.Where(item => item.Subledger == "Banking").Select(item => new BankAdjustmentSnapshot(item.Id, item.BankAccountId!.Value, item.AdjustmentDate, item.Amount, item.Reference, item.Reason, item.OffsetAccountNumber, item.Status, item.JournalEntryId, item.ReversalJournalEntryId)).ToArray()),
            Payroll: new PayrollWorkspace(
                ActiveEmployees: employeeSummary.Count(x => x.IsActive),
                MonthlyGross: employeeSummary.Where(x => x.IsActive).Sum(x => x.MonthlyBasePay),
                Employees: employees.Select(x => new EmployeeSnapshot(
                    x.EmployeeNumber,
                    $"{x.FirstName} {x.LastName}",
                    x.Department,
                    x.State,
                    x.PayType,
                    x.MonthlyBasePay,
                    x.IsActive,
                    x.Id,
                    x.FilingStatus,
                    x.Allowances,
                    x.AdditionalWithholding,
                    x.PreTaxBenefitDeductions,
                    x.PostTaxBenefitDeductions,
                    string.IsNullOrWhiteSpace(x.ResidenceState) ? x.State : x.ResidenceState,
                    x.ResidenceCity,
                    x.WorkCity,
                    x.PayrollFrequency,
                    canViewPayrollSensitiveData ? x.ResidenceCounty : string.Empty,
                    canViewPayrollSensitiveData ? x.ResidenceSchoolDistrict : string.Empty,
                    canViewPayrollSensitiveData ? x.WorkCounty : string.Empty,
                    canViewPayrollSensitiveData ? x.WorkSchoolDistrict : string.Empty,
                    canViewPayrollSensitiveData ? x.EmploymentStartedOn : null,
                    canViewPayrollSensitiveData ? x.EmploymentEndedOn : null,
                    canViewPayrollSensitiveData ? x.HourlyRate : 0m,
                    canViewPayrollSensitiveData ? x.OvertimeRate : 0m,
                    canViewPayrollSensitiveData && x.DirectDepositEnabled,
                    canViewPayrollSensitiveData && !string.IsNullOrWhiteSpace(x.SocialSecurityNumber),
                    canViewPayrollSensitiveData && !string.IsNullOrWhiteSpace(x.BankAccountNumber),
                    x.ConcurrencyToken,
                    canViewPayrollSensitiveData ? x.AddressLine1 : string.Empty,
                    canViewPayrollSensitiveData ? x.AddressLine2 : string.Empty,
                    canViewPayrollSensitiveData ? x.PostalCode : string.Empty,
                    canViewPayrollSensitiveData ? x.FederalFormW4Year : 0,
                    canViewPayrollSensitiveData && x.FederalStep2MultipleJobs,
                    canViewPayrollSensitiveData ? x.FederalStep3Credits : 0m,
                    canViewPayrollSensitiveData ? x.FederalStep4OtherIncome : 0m,
                    canViewPayrollSensitiveData ? x.FederalStep4Deductions : 0m,
                    canViewPayrollSensitiveData && x.FederalWithholdingExempt,
                    canViewPayrollSensitiveData ? x.DirectDepositAuthorizationOn : null,
                    canViewPayrollSensitiveData && !string.IsNullOrWhiteSpace(x.DirectDepositAuthorizationReference),
                    canViewPayrollSensitiveData ? x.AddressCity : string.Empty,
                    canViewPayrollSensitiveData ? x.AddressState : string.Empty)).ToArray(),
                JurisdictionRules: payrollJurisdictionRules.Select(rule => new PayrollJurisdictionRuleSnapshot(rule.Id, rule.ResidenceJurisdiction, rule.WorkJurisdiction, rule.ExemptWorkWithholding, rule.ResidentCreditRate, rule.IsActive, rule.Notes)).ToArray(),
                Runs: payrollRuns.Select(run => new PayrollRunSnapshot(run.Id, run.Reference, run.PeriodStart, run.PeriodEnd, run.PayDate, run.RunType, run.Status, run.GrossPayroll, run.EmployeeWithholdings, run.EmployerPayrollTaxes, run.NetPay, run.ConcurrencyToken, run.JournalEntryId, run.ReversalJournalEntryId, run.PreparedAtUtc, run.ApprovedAtUtc, run.PostedAtUtc, run.ReversedAtUtc, run.ReversalReason, run.CancelledAtUtc, run.CancellationReason, run.EmployerBenefitContributions)).ToArray(),
                Timecards: payrollTimecards.Select(timecard =>
                {
                    var employee = employeeById[timecard.EmployeeId];
                    var entries = payrollTimeEntryLookup[timecard.Id].Select(entry => new PayrollTimeEntrySnapshot(entry.Id, entry.Sequence, entry.WorkDate, entry.EarningCode, entry.EarningType, entry.Hours, entry.Rate, entry.Amount, entry.IsTaxable, entry.WorkState, entry.WorkCounty, entry.WorkCity, entry.WorkSchoolDistrict, entry.ProjectJobId, entry.Notes, ParseW2Reporting(entry.W2ReportingJson))).ToArray();
                    return new PayrollTimecardSnapshot(timecard.Id, employee.Id, employee.EmployeeNumber, $"{employee.FirstName} {employee.LastName}", timecard.PeriodStart, timecard.PeriodEnd, timecard.Status, entries.Sum(entry => entry.Hours), entries.Sum(entry => entry.Amount), timecard.Notes, timecard.ConcurrencyToken, timecard.PayrollRunId, timecard.PreparedAtUtc, timecard.SubmittedAtUtc, timecard.ApprovedAtUtc, timecard.VoidedAtUtc, timecard.VoidReason, entries);
                }).ToArray(),
                Liabilities: payrollLiabilities.Select(liability =>
                {
                    var employeeId = payrollLiabilityEmployeeByLineId[liability.PayrollRunEmployeeLineId];
                    var employee = employeeById[employeeId];
                    return new PayrollLiabilitySnapshot(liability.Id, liability.PayrollRunId, employeeId, $"{employee.FirstName} {employee.LastName}", liability.SourceType, liability.ObligationCode, liability.JurisdictionCode, liability.JurisdictionName, liability.Description, liability.LiabilityAccountNumber, liability.OriginalAmount, liability.OutstandingAmount, liability.Status, liability.DueDate, liability.DepositScheduleType, liability.DepositRuleCode, liability.DepositRuleSource, liability.DepositScheduleConfigurationId, liability.ConcurrencyToken);
                }).ToArray(),
                LiabilityPayments: payrollLiabilityPayments.Select(payment => new PayrollLiabilityPaymentSnapshot(payment.Id, payment.BankAccountId, payment.PaymentDate, payment.Reference, payment.Payee, payment.Method, payment.Amount, payment.Status, payment.JournalEntryId, payment.ReversalJournalEntryId, payment.ConcurrencyToken, payrollLiabilityPaymentApplicationLookup[payment.Id].Select(application => new PayrollLiabilityPaymentApplicationSnapshot(application.PayrollLiabilityId, payrollLiabilityById[application.PayrollLiabilityId].ObligationCode, application.Amount)).ToArray())).ToArray()),
            Projects: new ProjectsWorkspace(
                OpenJobs: projectJobs.Count(x => x.Status is "Open" or "Billing"),
                BudgetAmount: projectJobs.Sum(x => x.BudgetAmount),
                ActualCost: projectJobs.Sum(x => x.ActualCost),
                Jobs: projectJobs.Select(x => new ProjectJobSnapshot(x.JobNumber, x.Name, x.CustomerName, x.Status, x.BudgetAmount, x.ActualCost, x.Id)).ToArray()),
            Reporting: new ReportingWorkspace(
                ReportCount: reports.Count,
                LabelCount: labels.Count,
                PreferredDesigner: "Visual Studio RDL/RDLC",
                RenderingStrategy: "Use RDLC-authored operational reports plus server-side PDF exports for dashboards and special forms.",
                Reports: reports.Select(x => new ReportCatalogSnapshot(x.Code, x.Name, x.Category, x.LayoutType, x.Description, x.SupportsVisualStudioDesign)).ToArray(),
                Labels: labels.Select(x => new LabelTemplateSnapshot(x.Code, x.Name, x.StockType, x.Description)).ToArray()),
            Taxes: new TaxWorkspace(
                ProfileCount: taxProfiles.Count,
                EmployerSpecificCount: taxProfiles.Count(x => x.IsEmployerSpecific),
                Profiles: taxProfiles.Select(x => new TaxProfileSnapshot(x.Jurisdiction, x.TaxType, x.Rate, x.EffectiveOn, x.Source, x.IsEmployerSpecific, x.IsActive, x.IsVerified, x.VerificationNotes)).ToArray()));
    }

    private static PayrollW2ReportingInput ParseW2Reporting(string json)
    {
        try { return JsonSerializer.Deserialize<PayrollW2ReportingInput>(json) ?? throw new JsonException("The W-2 reporting payload is null."); }
        catch (JsonException exception) { throw new InvalidOperationException("Stored W-2 reporting data is invalid; the payroll workspace cannot omit it silently.", exception); }
    }

    private static Dictionary<string, (string Status, string Summary, int RecordCount)> BuildModuleCounts(
        int ledgerCount,
        int receivablesCount,
        int payablesCount,
        int payrollCount,
        int inventoryCount,
        int orderCount,
        int purchaseCount,
        int bankCount,
        int projectCount)
    {
        return new Dictionary<string, (string Status, string Summary, int RecordCount)>
        {
            ["J"] = ("Live foundation", "Chart of accounts, journal history, and balances are seeded in the shared workspace.", ledgerCount),
            ["F"] = ("Live foundation", "Customers and invoices are active with aging-ready open balances.", receivablesCount),
            ["E"] = ("Live foundation", "Vendor master data and open bills are wired for payables workflows.", payablesCount),
            ["Q"] = ("Live foundation", "Employees, payroll cost, and tax profiles are in the operational data model.", payrollCount),
            ["K"] = ("Live foundation", "Item master, reorder thresholds, and stock counts are available now.", inventoryCount),
            ["O"] = ("Live foundation", "Sales orders feed the operational pipeline and reporting surface.", orderCount),
            ["S"] = ("Live foundation", "Purchase orders are present and aligned to vendor workflows.", purchaseCount),
            ["P"] = ("Modeled foundation", "Point-of-sale can ride the order pipeline while dedicated ticket workflows are added.", orderCount),
            ["G"] = ("Live foundation", "Bank balances and reconciliation deltas are available in treasury.", bankCount),
            ["U"] = ("Modeled foundation", "Zero-balance behavior will build on the banking model rather than legacy switches.", bankCount),
            ["L"] = ("Live foundation", "Jobs carry budget, cost, and customer linkage for project accounting.", projectCount),
            ["B"] = ("Modeled foundation", "Property workflows can extend the job ledger without module gating.", projectCount),
            ["T"] = ("Modeled foundation", "Time capture will attach to employees and jobs in the same payroll model.", payrollCount),
            ["I"] = ("Live foundation", "CRM data is represented through the live customer workspace.", receivablesCount)
        };
    }

    private static decimal SumByType(IEnumerable<GeneralLedgerAccount> accounts, AccountType accountType)
    {
        return accounts.Where(x => x.Type == accountType).Sum(x => x.CurrentBalance);
    }

    private static SubledgerDocumentWorkflowSnapshot ToWorkflowSnapshot(SubledgerDocumentWorkflow workflow) => new(workflow.Id, workflow.DocumentType, workflow.DocumentNumber, workflow.Status, workflow.IsRecurringTemplate, workflow.Frequency, workflow.FrequencyInterval, workflow.NextOccurrenceDate, workflow.EndDate, workflow.SourceTemplateId, workflow.PostedDocumentId, workflow.CreatedAtUtc, workflow.ApprovedAtUtc, workflow.RejectedAtUtc, workflow.DecisionReason, workflow.PostedAtUtc, workflow.ConcurrencyToken);

    private static string MaskTaxId(string taxId)
    {
        var digits = new string(taxId.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4)
        {
            return $"***-**-{digits[^4..]}";
        }

        return "***";
    }
}
