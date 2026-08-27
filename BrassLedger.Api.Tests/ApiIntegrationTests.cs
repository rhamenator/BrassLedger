using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.SecurityAdministration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrassLedger.Api.Tests;

public sealed class ApiIntegrationTests : IClassFixture<BrassLedgerApiFactory>
{
    private readonly BrassLedgerApiFactory _factory;

    public ApiIntegrationTests(BrassLedgerApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDashboard_RejectsAnonymousRequests()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_ReturnsSeededFinancialSnapshot()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await response.Content.ReadFromJsonAsync<DashboardSnapshot>();
        Assert.NotNull(dashboard);
        Assert.Equal(112540.32m, dashboard.CashOnHand);
        Assert.Equal(34715.75m, dashboard.ReceivablesOpen);
        Assert.Equal(31844.77m, dashboard.PayablesOpen);
        Assert.Equal(14, dashboard.EnabledModules);
    }

    [Fact]
    public async Task GetWorkspace_ReturnsModulesAndReportingCatalog()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");

        Assert.NotNull(workspace);
        Assert.Equal("Brass Ledger Manufacturing", workspace.Company.Name);
        Assert.Contains(workspace.Modules, module => module.Code == "J" && module.Status == "Live foundation");
        Assert.Contains(workspace.Reporting.Reports, report => report.Code == "RDL-GL-TRIAL");
        Assert.Contains(workspace.Taxes.Profiles, profile => profile.Jurisdiction == "Federal" && profile.TaxType == "FUTA");
    }

    [Fact]
    public async Task TrackingDimensionApi_RequiresAntiforgeryAndProvidesControlledCompanyMaintenance()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var missingToken = await CreateAuthenticatedClientAsync(isolatedFactory, includeAntiforgery: false);
        var request = new SaveTrackingDimensionValueRequest(null, "Department", null, "API-OPS", "API operations", "Created through the controlled API", new DateOnly(2026, 1, 1), null);
        Assert.Equal(HttpStatusCode.BadRequest, (await missingToken.PostAsJsonAsync("/api/tracking-dimensions", request)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/tracking-dimensions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TransactionResult>();
        var created = Assert.Single((await client.GetFromJsonAsync<GeneralLedgerWorkspace>("/api/general-ledger"))!.TrackingDimensions!, value => value.Id == result!.Id);
        Assert.Equal("API-OPS", created.Code);
        var update = new SaveTrackingDimensionValueRequest(created.Id, created.DimensionType, null, created.Code, "API operations revised", created.Description, created.EffectiveFrom, created.EffectiveThrough, created.IsActive, created.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/tracking-dimensions/{Guid.NewGuid()}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tracking-dimensions/{created.Id}", update)).StatusCode);
        Assert.Contains((await client.GetFromJsonAsync<GeneralLedgerWorkspace>("/api/general-ledger"))!.TrackingDimensions!, value => value.Id == created.Id && value.Name == "API operations revised");
    }

    [Fact]
    public async Task ConsolidationApi_RetainsEffectiveOwnershipAndRejectsOverlapAndRouteMismatch()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var existingCompany = Assert.Single(await client.GetFromJsonAsync<IReadOnlyList<CompanyMembershipSnapshot>>("/api/companies") ?? []);
        Guid subsidiaryId;
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>().CreateDbContextAsync();
            var controller = await db.Users.SingleAsync(user => user.UserName == "controller");
            subsidiaryId = Guid.NewGuid();
            db.Companies.Add(new Company { Id = subsidiaryId, Name = "API subsidiary", LegalName = "API subsidiary Ltd.", TaxId = "API-SUB", BaseCurrency = "CAD", FiscalYearStartMonth = 1 });
            db.CompanyMemberships.Add(new CompanyMembership { Id = Guid.NewGuid(), UserId = controller.Id, CompanyId = subsidiaryId, Role = controller.Role, IsOwner = true, IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var exchangeRateResponse = await client.PutAsJsonAsync("/api/exchange-rates", new SaveExchangeRateRequest("USD", "CAD", 1.25m, new DateOnly(2026, 6, 30), "API verified average", RateType: "Average", PeriodStartOn: new DateOnly(2026, 1, 1), SourceReference: "https://example.test/rates", RetrievedOn: new DateOnly(2026, 6, 30)));
        Assert.Equal(HttpStatusCode.OK, exchangeRateResponse.StatusCode);
        var exchangeRates = await client.GetFromJsonAsync<IReadOnlyList<ExchangeRateSnapshot>>("/api/exchange-rates");
        Assert.Contains(exchangeRates!, rate => rate.RateType == "Average" && rate.PeriodStartOn == new DateOnly(2026, 1, 1) && rate.SourceReference == "https://example.test/rates");
        var createGroup = await client.PutAsJsonAsync("/api/consolidation-groups", new SaveConsolidationGroupRequest(null, "API group", "USD",
        [
            new ConsolidationMemberRequest(existingCompany.CompanyId, 1m, new DateOnly(2026, 1, 1)),
            new ConsolidationMemberRequest(subsidiaryId, .75m, new DateOnly(2026, 2, 1))
        ], CtaAccountNumber: "39999", CtaAccountName: "Cumulative translation adjustment"));
        Assert.Equal(HttpStatusCode.OK, createGroup.StatusCode);
        var groupResult = await createGroup.Content.ReadFromJsonAsync<TransactionResult>();
        var group = Assert.Single(await client.GetFromJsonAsync<IReadOnlyList<ConsolidationGroupSnapshot>>("/api/consolidation-groups") ?? [], item => item.Id == groupResult!.Id);
        Assert.Equal("39999", group.CtaAccountNumber);
        Assert.Contains(group.Members, member => member.CompanyId == subsidiaryId && member.EffectiveFrom == new DateOnly(2026, 2, 1) && member.OwnershipPercentage == .75m);
        var overlapping = new SaveConsolidationOwnershipPeriodRequest(null, group.Id, subsidiaryId, .8m, new DateOnly(2026, 3, 1), null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/consolidation-groups/{Guid.NewGuid()}/ownership-periods", overlapping)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/consolidation-groups/{group.Id}/ownership-periods", overlapping)).StatusCode);
        var mappingWorkspace = await client.GetFromJsonAsync<ConsolidationAccountMappingWorkspace>($"/api/consolidation-groups/{group.Id}/account-mappings");
        var sourceAccount = mappingWorkspace!.SourceAccounts.First(account => account.CompanyId == existingCompany.CompanyId);
        var mappingRequest = new SaveConsolidationAccountMappingRequest(null, group.Id, sourceAccount.CompanyId, sourceAccount.AccountId, "CON-" + sourceAccount.AccountNumber, sourceAccount.AccountName, new DateOnly(2026, 1, 1), null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/consolidation-groups/{Guid.NewGuid()}/account-mappings", mappingRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/consolidation-groups/{group.Id}/account-mappings", mappingRequest)).StatusCode);
        var savedMappings = await client.GetFromJsonAsync<ConsolidationAccountMappingWorkspace>($"/api/consolidation-groups/{group.Id}/account-mappings");
        Assert.Contains(savedMappings!.Mappings, mapping => mapping.AccountId == sourceAccount.AccountId && mapping.ReportingAccountNumber == "CON-" + sourceAccount.AccountNumber);
        Assert.Contains(savedMappings.Mappings, mapping => mapping.AccountId == sourceAccount.AccountId && mapping.TranslationMethod is "Closing" or "Average" or "Historical");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/consolidation-groups/{group.Id}/account-mappings", mappingRequest with { EffectiveFrom = new DateOnly(2026, 2, 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/consolidation-groups/{group.Id}/balances?periodStart=2026-01-01&asOf=2026-06-30")).StatusCode);
    }

    [Fact]
    public async Task UnsafeApiRoutes_RequireAntiforgeryAndRejectMissingTokensBeforeMutation()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        var unsafeMethods = new HashSet<string>([HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete], StringComparer.OrdinalIgnoreCase);
        var unprotectedRoutes = isolatedFactory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true)
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Any(unsafeMethods.Contains) == true)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>()?.RequiresValidation != true)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(unprotectedRoutes);

        using var client = await CreateAuthenticatedClientAsync(isolatedFactory, includeAntiforgery: false);
        var request = new SaveJournalEntryDraftRequest(null, new DateOnly(2026, 8, 26), "CSRF-REJECT", "Must not save", [new("1000", 1m, 0m, "Cash"), new("4000", 0m, 1m, "Revenue")]);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/journal-entries", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/payroll-runs", new { bankAccountId = Guid.NewGuid(), payDate = "2026-08-26", reference = "DIRECT-PAYROLL", grossPayroll = 100m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/payroll-runs/employee", new { bankAccountId = Guid.NewGuid(), payDate = "2026-08-26", reference = "DIRECT-EMPLOYEE-PAYROLL", employees = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/journal-entry-drafts", request)).StatusCode);
        Assert.DoesNotContain((await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"))!.GeneralLedger.RecentEntries, entry => entry.Reference == request.Reference);

        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/journal-entry-drafts", request)).StatusCode);
    }

    [Fact]
    public async Task PurchaseOrderApi_SeparatesPreparationFromApproval_AndRejectsMissingAntiforgery()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var preparerWithoutToken = await CreateAuthenticatedClientAsync(isolatedFactory, "requisition", includeAntiforgery: false);
        using var preparer = await CreateAuthenticatedClientAsync(isolatedFactory, "requisition");
        using var purchasing = await CreateAuthenticatedClientAsync(isolatedFactory, "operations");
        using var payables = await CreateAuthenticatedClientAsync(isolatedFactory, "controller");
        var workspaceResponse = await preparer.GetAsync("/api/workspace");
        Assert.True(workspaceResponse.IsSuccessStatusCode, await workspaceResponse.Content.ReadAsStringAsync());
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<BusinessWorkspaceSnapshot>();
        var vendor = workspace!.Payables.Vendors.First();
        var item = workspace.Operations.InventoryItems.First();
        var request = new SavePurchaseRequisitionRequest(null, vendor.Id, "REQ-API-1", new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27), "API purchase workflow", [new PurchaseRequisitionLineRequest(item.Id, "API inventory", 2m, 15m)]);

        Assert.Equal(HttpStatusCode.BadRequest, (await preparerWithoutToken.PostAsJsonAsync("/api/purchase-requisitions", request)).StatusCode);
        Assert.DoesNotContain((await preparer.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseRequisitions!, requisition => requisition.RequisitionNumber == request.RequisitionNumber);
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsJsonAsync("/api/purchase-orders", new SavePurchaseOrderRequest(null, vendor.Id, "PO-BYPASS-API-1", request.RequestedOn, request.NeededBy, request.Purpose, [new PurchaseOrderLineRequest(item.Id, "Bypass", 2m, 15m)]))).StatusCode);
        var savedResponse = await preparer.PostAsJsonAsync("/api/purchase-requisitions", request);
        Assert.Equal(HttpStatusCode.Created, savedResponse.StatusCode);
        var saved = await savedResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var requisition = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseRequisitions!, candidate => candidate.Id == saved!.Id);
        Assert.Equal("Draft", requisition.Status);
        Assert.Equal(2m, Assert.Single(requisition.Lines).RequestedQuantity);
        Assert.Equal(HttpStatusCode.OK, (await preparer.PostAsJsonAsync($"/api/purchase-requisitions/{requisition.Id}/submission", new SubmitPurchaseRequisitionRequest(requisition.Id, requisition.ConcurrencyToken))).StatusCode);
        requisition = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseRequisitions!, candidate => candidate.Id == requisition.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsJsonAsync($"/api/purchase-requisitions/{requisition.Id}/decision", new DecidePurchaseRequisitionRequest(requisition.Id, true, "Bypass", requisition.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await purchasing.PostAsJsonAsync($"/api/purchase-requisitions/{requisition.Id}/decision", new DecidePurchaseRequisitionRequest(requisition.Id, true, "Approved API purchase", requisition.ConcurrencyToken))).StatusCode);
        requisition = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseRequisitions!, candidate => candidate.Id == requisition.Id);
        var conversion = new ConvertPurchaseRequisitionRequest(requisition.Id, vendor.Id, "PO-API-1", new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27), "API purchase workflow", requisition.ConcurrencyToken);
        var conversionResponse = await purchasing.PostAsJsonAsync($"/api/purchase-requisitions/{requisition.Id}/purchase-order", conversion);
        Assert.Equal(HttpStatusCode.Created, conversionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await purchasing.PostAsJsonAsync($"/api/purchase-requisitions/{requisition.Id}/purchase-order", conversion)).StatusCode);
        var draft = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseOrders, order => order.OrderNumber == conversion.OrderNumber);
        Assert.Equal("Draft", draft.Status);
        Assert.Equal(2m, Assert.Single(draft.Lines!).OrderedQuantity);

        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsJsonAsync($"/api/purchase-orders/{draft.Id}/approval", new ApprovePurchaseOrderRequest(draft.Id, draft.ConcurrencyToken))).StatusCode);
        var approval = await purchasing.PostAsJsonAsync($"/api/purchase-orders/{draft.Id}/approval", new ApprovePurchaseOrderRequest(draft.Id, draft.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var approved = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseOrders, order => order.Id == draft.Id);
        var line = Assert.Single(approved.Lines!);
        var receiptResponse = await purchasing.PostAsJsonAsync($"/api/purchase-orders/{approved.Id}/receipts", new ReceivePurchaseOrderRequest(approved.Id, "RCV-API-1", new DateOnly(2026, 8, 21), [new ReceivePurchaseOrderLineRequest(line.Id, 1m)], approved.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Created, receiptResponse.StatusCode);
        var receipt = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryReceipts!, candidate => candidate.ReceiptNumber == "RCV-API-1");
        var receiptLine = Assert.Single(receipt.Lines);
        var billResponse = await payables.PostAsJsonAsync("/api/purchase-invoice-matches", new SavePurchaseInvoiceMatchRequest(null, receipt.Id, "BILL-API-1", new DateOnly(2026, 8, 22), new DateOnly(2026, 9, 21), "API matched bill", [new(receiptLine.Id, 1m, 15m)], receipt.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Created, billResponse.StatusCode); var matchResult = await billResponse.Content.ReadFromJsonAsync<TransactionResult>(); var invoiceMatch = Assert.Single((await payables.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseInvoiceMatches!, match => match.Id == matchResult!.Id);
        Assert.Equal(HttpStatusCode.OK, (await payables.PostAsJsonAsync($"/api/purchase-invoice-matches/{invoiceMatch.Id}/submission", new SubmitPurchaseInvoiceMatchRequest(invoiceMatch.Id, invoiceMatch.ConcurrencyToken))).StatusCode); invoiceMatch = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseInvoiceMatches!, match => match.Id == invoiceMatch.Id);
        Assert.Equal(HttpStatusCode.OK, (await purchasing.PostAsJsonAsync($"/api/purchase-invoice-matches/{invoiceMatch.Id}/decision", new DecidePurchaseInvoiceMatchRequest(invoiceMatch.Id, true, "Receipt and invoice reviewed", invoiceMatch.ConcurrencyToken))).StatusCode); invoiceMatch = Assert.Single((await payables.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseInvoiceMatches!, match => match.Id == invoiceMatch.Id);
        Assert.Equal(HttpStatusCode.OK, (await payables.PostAsJsonAsync($"/api/purchase-invoice-matches/{invoiceMatch.Id}/posting", new PostPurchaseInvoiceMatchRequest(invoiceMatch.Id, invoiceMatch.ConcurrencyToken))).StatusCode);
        Assert.Contains((await purchasing.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"))!.Payables.Bills, bill => bill.BillNumber == "BILL-API-1" && bill.TotalAmount == 15m);
    }

    [Fact]
    public async Task SupplierReturnApi_PreservesReceiptProvenance_AndSeparatesPurchasingFromCreditReversal()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var purchasing = await CreateAuthenticatedClientAsync(isolatedFactory, "operations");
        using var controller = await CreateAuthenticatedClientAsync(isolatedFactory, "controller");
        using var sales = await CreateAuthenticatedClientAsync(isolatedFactory, "sales");
        var workspace = await purchasing.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var vendor = workspace!.Payables.Vendors.First(); var item = workspace.Operations.InventoryItems.First(); var bank = workspace.Treasury.BankAccounts.First(); var suffix = Guid.NewGuid().ToString("N")[..8]; var date = new DateOnly(2026, 8, 20);

        var orderResponse = await purchasing.PostAsJsonAsync("/api/purchase-orders", new SavePurchaseOrderRequest(null, vendor.Id, $"PO-SR-{suffix}", date, date.AddDays(2), "Supplier return API", [new PurchaseOrderLineRequest(item.Id, "Returnable inventory", 2m, 17m)]));
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode); var orderResult = await orderResponse.Content.ReadFromJsonAsync<TransactionResult>(); var order = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseOrders, candidate => candidate.Id == orderResult!.Id);
        Assert.Equal(HttpStatusCode.OK, (await purchasing.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/approval", new ApprovePurchaseOrderRequest(order.Id, order.ConcurrencyToken))).StatusCode);
        order = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseOrders, candidate => candidate.Id == order.Id); var orderLine = Assert.Single(order.Lines!);
        Assert.Equal(HttpStatusCode.Created, (await purchasing.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new ReceivePurchaseOrderRequest(order.Id, $"RCV-SR-{suffix}", date.AddDays(1), [new ReceivePurchaseOrderLineRequest(orderLine.Id, 2m)], order.ConcurrencyToken))).StatusCode);
        var receipt = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryReceipts!, candidate => candidate.PurchaseOrderId == order.Id); var receiptLine = Assert.Single(receipt.Lines);
        var sourceMatchResponse = await controller.PostAsJsonAsync("/api/purchase-invoice-matches", new SavePurchaseInvoiceMatchRequest(null, receipt.Id, $"BILL-SR-{suffix}", date.AddDays(2), date.AddDays(32), "Source receipt bill", [new(receiptLine.Id, 2m, 17m)], receipt.ConcurrencyToken)); Assert.Equal(HttpStatusCode.Created, sourceMatchResponse.StatusCode); var sourceMatchResult = await sourceMatchResponse.Content.ReadFromJsonAsync<TransactionResult>(); var sourceMatch = Assert.Single((await controller.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseInvoiceMatches!, match => match.Id == sourceMatchResult!.Id);
        Assert.Equal(HttpStatusCode.OK, (await controller.PostAsJsonAsync($"/api/purchase-invoice-matches/{sourceMatch.Id}/submission", new SubmitPurchaseInvoiceMatchRequest(sourceMatch.Id, sourceMatch.ConcurrencyToken))).StatusCode); sourceMatch = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseInvoiceMatches!, match => match.Id == sourceMatch.Id); Assert.Equal(HttpStatusCode.OK, (await purchasing.PostAsJsonAsync($"/api/purchase-invoice-matches/{sourceMatch.Id}/decision", new DecidePurchaseInvoiceMatchRequest(sourceMatch.Id, true, "Source receipt reviewed", sourceMatch.ConcurrencyToken))).StatusCode); sourceMatch = Assert.Single((await controller.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseInvoiceMatches!, match => match.Id == sourceMatch.Id); Assert.Equal(HttpStatusCode.OK, (await controller.PostAsJsonAsync($"/api/purchase-invoice-matches/{sourceMatch.Id}/posting", new PostPurchaseInvoiceMatchRequest(sourceMatch.Id, sourceMatch.ConcurrencyToken))).StatusCode);
        workspace = await purchasing.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"); var sourceBill = Assert.Single(workspace!.Payables.Bills, candidate => candidate.BillNumber == $"BILL-SR-{suffix}");
        Assert.Equal(HttpStatusCode.Created, (await controller.PostAsJsonAsync("/api/vendor-payments", new RecordVendorPaymentRequest(vendor.Id, bank.Id, date.AddDays(3), 34m, $"PAY-SR-{suffix}", "ACH", [new PaymentDocumentApplicationRequest(sourceBill.Id, 34m)]))).StatusCode);
        Guid targetBillId;
        var targetBill = await PostVendorBillThroughWorkflowAsync(isolatedFactory.Services, new(vendor.Id, $"TARGET-SR-{suffix}", date.AddDays(3), date.AddDays(33), 25m, "5100", "Credit application target"));
        Assert.True(targetBill.Succeeded, targetBill.ErrorMessage);
        targetBillId = targetBill.Id!.Value;

        receipt = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryReceipts!, candidate => candidate.Id == receipt.Id); var authorizationRequest = new AuthorizeSupplierReturnRequest(receipt.Id, $"SRA-{suffix}", date.AddDays(4), "Damaged inventory", [new(receiptLine.Id, 1m)], receipt.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsJsonAsync($"/api/inventory-receipts/{receipt.Id}/supplier-returns", authorizationRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await purchasing.PostAsJsonAsync($"/api/inventory-receipts/{Guid.NewGuid()}/supplier-returns", authorizationRequest)).StatusCode);
        var authorizationResponse = await purchasing.PostAsJsonAsync($"/api/inventory-receipts/{receipt.Id}/supplier-returns", authorizationRequest); Assert.Equal(HttpStatusCode.Created, authorizationResponse.StatusCode);
        var authorization = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SupplierReturnAuthorizations!, candidate => candidate.ReturnNumber == authorizationRequest.ReturnNumber); var authorizationLine = Assert.Single(authorization.Lines);
        var staleShipment = new ShipSupplierReturnRequest(authorization.Id, $"SRS-STALE-{suffix}", date.AddDays(5), null, null, [new(authorizationLine.Id, 1m)], "stale-token"); Assert.Equal(HttpStatusCode.BadRequest, (await purchasing.PostAsJsonAsync($"/api/supplier-returns/{authorization.Id}/shipments", staleShipment)).StatusCode);
        var shipmentResponse = await purchasing.PostAsJsonAsync($"/api/supplier-returns/{authorization.Id}/shipments", staleShipment with { ShipmentNumber = $"SRS-{suffix}", ConcurrencyToken = authorization.ConcurrencyToken }); Assert.Equal(HttpStatusCode.Created, shipmentResponse.StatusCode);
        var shipment = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SupplierReturnShipments!, candidate => candidate.ShipmentNumber == $"SRS-{suffix}"); Assert.True(shipment.CreatesVendorCredit); Assert.Equal(sourceBill.Id, shipment.SourceVendorBillId); Assert.Equal(17m, shipment.AvailableAmount);

        var applyRequest = new ApplySupplierReturnCreditRequest(shipment.Id, targetBillId, date.AddDays(6), 10m, shipment.ConcurrencyToken); Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsJsonAsync($"/api/supplier-return-shipments/{shipment.Id}/applications", applyRequest)).StatusCode); var applyResponse = await controller.PostAsJsonAsync($"/api/supplier-return-shipments/{shipment.Id}/applications", applyRequest); Assert.Equal(HttpStatusCode.Created, applyResponse.StatusCode); var applyResult = await applyResponse.Content.ReadFromJsonAsync<TransactionResult>();
        shipment = Assert.Single((await controller.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SupplierReturnShipments!, candidate => candidate.Id == shipment.Id); var refundRequest = new RefundSupplierReturnCreditRequest(shipment.Id, bank.Id, $"REF-SR-{suffix}", date.AddDays(7), 7m, shipment.ConcurrencyToken); var refundResponse = await controller.PostAsJsonAsync($"/api/supplier-return-shipments/{shipment.Id}/refunds", refundRequest); Assert.Equal(HttpStatusCode.Created, refundResponse.StatusCode); var refundResult = await refundResponse.Content.ReadFromJsonAsync<TransactionResult>();
        shipment = Assert.Single((await controller.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SupplierReturnShipments!, candidate => candidate.Id == shipment.Id); Assert.Equal(0m, shipment.AvailableAmount); var application = Assert.Single(shipment.Applications, candidate => candidate.Id == applyResult!.Id); var refund = Assert.Single(shipment.Refunds, candidate => candidate.Id == refundResult!.Id);
        Assert.Equal(HttpStatusCode.OK, (await controller.PostAsJsonAsync($"/api/supplier-return-credit-refunds/{refund.Id}/reversal", new ReverseSupplierReturnCreditRefundRequest(refund.Id, date.AddDays(8), "Refund correction", refund.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await controller.PostAsJsonAsync($"/api/supplier-return-credit-applications/{application.Id}/reversal", new ReverseSupplierReturnCreditApplicationRequest(application.Id, date.AddDays(8), "Application correction", application.ConcurrencyToken))).StatusCode);
        shipment = Assert.Single((await controller.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SupplierReturnShipments!, candidate => candidate.Id == shipment.Id); Assert.Equal(17m, shipment.AvailableAmount); Assert.Equal(HttpStatusCode.Forbidden, (await controller.PostAsJsonAsync($"/api/supplier-return-shipments/{shipment.Id}/reversal", new ReverseSupplierReturnShipmentRequest(shipment.Id, date.AddDays(9), "Controller lacks purchasing authority", shipment.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task LandedCostApi_SeparatesPayablesPreparationFromPurchasingApprovalAndPostsTheVendorBill()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var purchasing = await CreateAuthenticatedClientAsync(isolatedFactory, "operations");
        using var payables = await CreateAuthenticatedClientAsync(isolatedFactory, "controller");
        using var sales = await CreateAuthenticatedClientAsync(isolatedFactory, "sales");
        var workspace = await purchasing.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"); var vendor = workspace!.Payables.Vendors.First(); var item = workspace.Operations.InventoryItems.First(); var suffix = Guid.NewGuid().ToString("N")[..8]; var date = new DateOnly(2026, 8, 20);
        var orderResponse = await purchasing.PostAsJsonAsync("/api/purchase-orders", new SavePurchaseOrderRequest(null, vendor.Id, $"PO-LC-{suffix}", date, date.AddDays(2), "Landed cost API", [new PurchaseOrderLineRequest(item.Id, "Imported inventory", 4m, 25m)])); Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode); var orderResult = await orderResponse.Content.ReadFromJsonAsync<TransactionResult>(); var order = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseOrders, candidate => candidate.Id == orderResult!.Id);
        Assert.Equal(HttpStatusCode.OK, (await purchasing.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/approval", new ApprovePurchaseOrderRequest(order.Id, order.ConcurrencyToken))).StatusCode); order = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.PurchaseOrders, candidate => candidate.Id == order.Id); var orderLine = Assert.Single(order.Lines!);
        Assert.Equal(HttpStatusCode.Created, (await purchasing.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new ReceivePurchaseOrderRequest(order.Id, $"RCV-LC-{suffix}", date.AddDays(1), [new ReceivePurchaseOrderLineRequest(orderLine.Id, 4m)], order.ConcurrencyToken))).StatusCode); var receipt = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryReceipts!, candidate => candidate.PurchaseOrderId == order.Id);
        var saveRequest = new SaveLandedCostAllocationRequest(null, receipt.Id, vendor.Id, $"LC-{suffix}", $"LCB-{suffix}", date.AddDays(2), date.AddDays(32), "Quantity", "API freight allocation", [new LandedCostChargeRequest("Freight", "Inbound freight", 20m)], null, receipt.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsJsonAsync("/api/landed-cost-allocations", saveRequest)).StatusCode); var saveResponse = await payables.PostAsJsonAsync("/api/landed-cost-allocations", saveRequest); Assert.Equal(HttpStatusCode.Created, saveResponse.StatusCode); var saved = await saveResponse.Content.ReadFromJsonAsync<TransactionResult>(); var allocation = Assert.Single((await payables.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.LandedCostAllocations!, candidate => candidate.Id == saved!.Id); Assert.Equal("Draft", allocation.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await payables.PostAsJsonAsync($"/api/landed-cost-allocations/{Guid.NewGuid()}/submission", new SubmitLandedCostAllocationRequest(allocation.Id, allocation.ConcurrencyToken))).StatusCode); Assert.Equal(HttpStatusCode.OK, (await payables.PostAsJsonAsync($"/api/landed-cost-allocations/{allocation.Id}/submission", new SubmitLandedCostAllocationRequest(allocation.Id, allocation.ConcurrencyToken))).StatusCode); allocation = Assert.Single((await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.LandedCostAllocations!, candidate => candidate.Id == allocation.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await payables.PostAsJsonAsync($"/api/landed-cost-allocations/{allocation.Id}/decision", new DecideLandedCostAllocationRequest(allocation.Id, true, "Bypass purchasing", allocation.ConcurrencyToken))).StatusCode); Assert.Equal(HttpStatusCode.OK, (await purchasing.PostAsJsonAsync($"/api/landed-cost-allocations/{allocation.Id}/decision", new DecideLandedCostAllocationRequest(allocation.Id, true, "Freight invoice reviewed", allocation.ConcurrencyToken))).StatusCode); allocation = Assert.Single((await payables.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.LandedCostAllocations!, candidate => candidate.Id == allocation.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await purchasing.PostAsJsonAsync($"/api/landed-cost-allocations/{allocation.Id}/posting", new PostLandedCostAllocationRequest(allocation.Id, allocation.ConcurrencyToken))).StatusCode); Assert.Equal(HttpStatusCode.OK, (await payables.PostAsJsonAsync($"/api/landed-cost-allocations/{allocation.Id}/posting", new PostLandedCostAllocationRequest(allocation.Id, allocation.ConcurrencyToken))).StatusCode); workspace = await payables.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"); allocation = Assert.Single(workspace!.Operations.LandedCostAllocations!, candidate => candidate.Id == allocation.Id); Assert.Equal("Posted", allocation.Status); Assert.NotNull(allocation.VendorBillId); Assert.Contains(workspace.Payables.Bills, bill => bill.Id == allocation.VendorBillId && bill.TotalAmount == 20m);
        Assert.Equal(HttpStatusCode.Forbidden, (await payables.PostAsJsonAsync($"/api/landed-cost-allocations/{allocation.Id}/reversal", new ReverseLandedCostAllocationRequest(allocation.Id, date.AddDays(3), "Controller lacks purchasing authority", allocation.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task InventoryLocationApi_SeparatesConfigurationFromMovement_AndTransfersWithoutPosting()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var purchasing = await CreateAuthenticatedClientAsync(isolatedFactory, "operations");
        using var warehouse = await CreateAuthenticatedClientAsync(isolatedFactory, "warehouse");
        using var sales = await CreateAuthenticatedClientAsync(isolatedFactory, "sales");
        var before = await purchasing.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var item = before!.Operations.InventoryItems.Single(candidate => candidate.Sku == "RM-220");
        var main = Assert.Single(before.Operations.Warehouses!, candidate => candidate.IsDefault);
        var mainBin = Assert.Single(main.Bins, candidate => candidate.IsDefault);
        var journalCount = before.GeneralLedger.RecentEntries.Count;
        var reference = $"XFER-API-{Guid.NewGuid():N}";

        var warehouseRequest = new SaveInventoryWarehouseRequest(null, "EAST", "East distribution", "100 River Road", "", "Detroit", "MI", "48201", "US", false, true);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsJsonAsync("/api/inventory/warehouses", warehouseRequest)).StatusCode);
        var warehouseResponse = await purchasing.PostAsJsonAsync("/api/inventory/warehouses", warehouseRequest);
        Assert.Equal(HttpStatusCode.Created, warehouseResponse.StatusCode);
        var warehouseResult = await warehouseResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var configured = await purchasing.GetFromJsonAsync<OperationsWorkspace>("/api/operations");
        var east = Assert.Single(configured!.Warehouses!, candidate => candidate.Id == warehouseResult!.Id);
        var eastStock = Assert.Single(east.Bins, candidate => candidate.IsDefault);

        var binRequest = new SaveInventoryBinRequest(null, east.Id, "PICK", "Primary picking", false, true);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsJsonAsync("/api/inventory/bins", binRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await purchasing.PostAsJsonAsync("/api/inventory/bins", binRequest)).StatusCode);

        var transferRequest = new TransferInventoryRequest(item.Id, main.Id, mainBin.Id, east.Id, eastStock.Id, 2m, new DateOnly(2026, 8, 26), reference, "Stage eastern orders");
        Assert.Equal(HttpStatusCode.Forbidden, (await purchasing.PostAsJsonAsync("/api/inventory/transfers", transferRequest)).StatusCode);
        var transferResponse = await warehouse.PostAsJsonAsync("/api/inventory/transfers", transferRequest);
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);
        var transferResult = await transferResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.Equal(HttpStatusCode.BadRequest, (await warehouse.PostAsJsonAsync("/api/inventory/transfers", transferRequest)).StatusCode);

        var moved = await warehouse.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.Equal(item.QuantityOnHand, moved!.Operations.InventoryItems.Single(candidate => candidate.Id == item.Id).QuantityOnHand);
        Assert.Equal(journalCount, moved.GeneralLedger.RecentEntries.Count);
        var transfer = Assert.Single(moved.Operations.InventoryTransfers!, candidate => candidate.Id == transferResult!.Id);
        Assert.Equal("Posted", transfer.Status);
        var movedEast = Assert.Single(moved.Operations.Warehouses!, candidate => candidate.Id == east.Id);
        Assert.Equal(2m, movedEast.Bins.Single(candidate => candidate.Id == eastStock.Id).Balances.Single(candidate => candidate.InventoryItemId == item.Id).QuantityOnHand);

        var staleReversal = new ReverseInventoryTransferRequest(transfer.Id, new DateOnly(2026, 8, 26), "Stale API reversal", "stale-token");
        Assert.Equal(HttpStatusCode.BadRequest, (await warehouse.PostAsJsonAsync($"/api/inventory/transfers/{transfer.Id}/reversal", staleReversal)).StatusCode);
        var reversal = new ReverseInventoryTransferRequest(transfer.Id, new DateOnly(2026, 8, 26), "Return staged inventory", transfer.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/inventory/transfers/{transfer.Id}/reversal", reversal)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await warehouse.PostAsJsonAsync($"/api/inventory/transfers/{transfer.Id}/reversal", reversal)).StatusCode);
        Assert.Equal("Reversed", Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryTransfers!, candidate => candidate.Id == transfer.Id).Status);
    }

    [Fact]
    public async Task PickPackBackorderApi_EnforcesRolesAndPreservesPackingSlipShipmentProvenance()
    {
        using var isolatedFactory = new BrassLedgerApiFactory(); using var sales = await CreateAuthenticatedClientAsync(isolatedFactory, "sales"); using var warehouse = await CreateAuthenticatedClientAsync(isolatedFactory, "warehouse"); var workspace = await sales.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"); var customer = workspace!.Receivables.Customers.First(); var item = workspace.Operations.InventoryItems.Single(candidate => candidate.Sku == "RM-220"); var today = DateOnly.FromDateTime(DateTime.Today); var suffix = Guid.NewGuid().ToString("N")[..8];
        var savedResponse = await sales.PostAsJsonAsync("/api/sales-orders", new SaveSalesOrderRequest(null, customer.Id, $"SO-PP-{suffix}", today, today.AddDays(1), "API pick pack", [new SalesOrderLineRequest(item.Id, "API pick pack item", 2m, 20m, 0m, 0m, "4000")])); Assert.Equal(HttpStatusCode.Created, savedResponse.StatusCode); var saved = await savedResponse.Content.ReadFromJsonAsync<TransactionResult>(); var order = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, candidate => candidate.Id == saved!.Id); Assert.Equal(HttpStatusCode.OK, (await sales.PostAsJsonAsync($"/api/sales-orders/{order.Id}/approval", new ApproveSalesOrderRequest(order.Id, order.ConcurrencyToken))).StatusCode);
        order = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, candidate => candidate.Id == order.Id); var line = Assert.Single(order.Lines!); var backorder = new PromiseSalesOrderBackorderRequest(order.Id, line.Id, 1m, today.AddDays(3), "API replenishment promise", order.ConcurrencyToken); Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/sales-orders/{order.Id}/backorders", backorder)).StatusCode); Assert.Equal(HttpStatusCode.Created, (await sales.PostAsJsonAsync($"/api/sales-orders/{order.Id}/backorders", backorder)).StatusCode);
        order = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, candidate => candidate.Id == order.Id); var pickRequest = new CreateInventoryPickRequest(order.Id, $"PICK-{suffix}", today, [new CreateInventoryPickLineRequest(line.Id, 1m)], order.ConcurrencyToken); Assert.Equal(HttpStatusCode.BadRequest, (await warehouse.PostAsJsonAsync($"/api/sales-orders/{order.Id}/picks", pickRequest)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/sales-orders/{order.Id}/allocation", new AllocateSalesOrderRequest(order.Id, [new(line.Id, 1m)], order.ConcurrencyToken))).StatusCode);
        order = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, candidate => candidate.Id == order.Id); pickRequest = pickRequest with { SalesOrderConcurrencyToken = order.ConcurrencyToken }; var pickResponse = await warehouse.PostAsJsonAsync($"/api/sales-orders/{order.Id}/picks", pickRequest); Assert.Equal(HttpStatusCode.Created, pickResponse.StatusCode); var pickResult = await pickResponse.Content.ReadFromJsonAsync<TransactionResult>(); var pick = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryPicks!, candidate => candidate.Id == pickResult!.Id); var pickLine = Assert.Single(pick.Lines); var completion = new CompleteInventoryPickRequest(pick.Id, [new(pickLine.Id, 1m)], pick.ConcurrencyToken); Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsJsonAsync($"/api/inventory-picks/{pick.Id}/completion", completion)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/inventory-picks/{pick.Id}/completion", completion)).StatusCode);
        pick = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryPicks!, candidate => candidate.Id == pick.Id); var packResponse = await warehouse.PostAsJsonAsync($"/api/inventory-picks/{pick.Id}/packing-slips", new PackInventoryPickRequest(pick.Id, $"PACK-{suffix}", today, [new(pickLine.Id, 1m)], pick.ConcurrencyToken)); Assert.Equal(HttpStatusCode.Created, packResponse.StatusCode); var packResult = await packResponse.Content.ReadFromJsonAsync<TransactionResult>(); var operations = await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"); var pack = Assert.Single(operations!.InventoryPackingSlips!, candidate => candidate.Id == packResult!.Id); order = Assert.Single(operations.SalesOrders, candidate => candidate.Id == order.Id);
        var shipmentResponse = await warehouse.PostAsJsonAsync($"/api/sales-orders/{order.Id}/shipments", new ShipSalesOrderRequest(order.Id, $"SHIP-PP-{suffix}", today.AddDays(1), [new(line.Id, 1m)], order.ConcurrencyToken, pack.Id, pack.ConcurrencyToken)); Assert.Equal(HttpStatusCode.Created, shipmentResponse.StatusCode); operations = await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"); pack = Assert.Single(operations!.InventoryPackingSlips!, candidate => candidate.Id == pack.Id); var shipment = Assert.Single(operations.InventoryShipments!, candidate => candidate.InventoryPackingSlipId == pack.Id); Assert.Equal("Shipped", pack.Status); Assert.Equal(shipment.Id, pack.InventoryShipmentId); Assert.Equal("Fulfilled", Assert.Single(operations.BackorderPromises!, candidate => candidate.SalesOrderId == order.Id).Status);
    }

    [Fact]
    public async Task SalesFulfillmentApi_SeparatesSalesWarehouseAndReceivables_AndPreservesShipmentProvenance()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var sales = await CreateAuthenticatedClientAsync(isolatedFactory, "sales");
        using var warehouse = await CreateAuthenticatedClientAsync(isolatedFactory, "warehouse");
        using var receivables = await CreateAuthenticatedClientAsync(isolatedFactory, "controller");
        var workspace = await sales.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var customer = workspace!.Receivables.Customers.First();
        var item = workspace.Operations.InventoryItems.Single(candidate => candidate.Sku == "RM-220");
        var orderNumber = $"SO-API-{Guid.NewGuid():N}";
        var request = new SaveSalesOrderRequest(null, customer.Id, orderNumber, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 22), "API sales fulfillment", [new SalesOrderLineRequest(item.Id, "API fasteners", 3m, 20m, 0m, 3m, "4000")]);

        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync("/api/sales-orders", request)).StatusCode);
        var savedResponse = await sales.PostAsJsonAsync("/api/sales-orders", request); Assert.Equal(HttpStatusCode.Created, savedResponse.StatusCode);
        var saved = await savedResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var draft = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, order => order.Id == saved!.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/sales-orders/{draft.Id}/approval", new ApproveSalesOrderRequest(draft.Id, draft.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsJsonAsync($"/api/sales-orders/{draft.Id}/approval", new ApproveSalesOrderRequest(draft.Id, draft.ConcurrencyToken))).StatusCode);
        var approved = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, order => order.Id == draft.Id);
        var line = Assert.Single(approved.Lines!);
        var amendment = new AmendSalesOrderRequest(approved.Id, approved.OrderedOn, approved.RequestedShipOn, "API sales fulfillment amended", "Customer confirmed delivery notes", [new SalesOrderLineRequest(line.InventoryItemId, line.Description, line.OrderedQuantity, line.UnitPrice, line.DiscountAmount, line.TaxAmount, line.RevenueAccountNumber)], approved.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/sales-orders/{approved.Id}/amendment", amendment)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsJsonAsync($"/api/sales-orders/{approved.Id}/amendment", amendment)).StatusCode);
        var amended = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, order => order.Id == approved.Id); Assert.Equal("Draft", amended.Status);
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsJsonAsync($"/api/sales-orders/{amended.Id}/approval", new ApproveSalesOrderRequest(amended.Id, amended.ConcurrencyToken))).StatusCode);
        approved = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, order => order.Id == draft.Id); line = Assert.Single(approved.Lines!);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsJsonAsync($"/api/sales-orders/{approved.Id}/allocation", new AllocateSalesOrderRequest(approved.Id, [new(line.Id, 3m)], approved.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/sales-orders/{approved.Id}/allocation", new AllocateSalesOrderRequest(approved.Id, [new(line.Id, 3m)], approved.ConcurrencyToken))).StatusCode);
        var allocated = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, order => order.Id == approved.Id);
        var shipmentResponse = await warehouse.PostAsJsonAsync($"/api/sales-orders/{allocated.Id}/shipments", new ShipSalesOrderRequest(allocated.Id, $"SHIP-{orderNumber}", new DateOnly(2026, 8, 22), [new(line.Id, 2m)], allocated.ConcurrencyToken)); Assert.Equal(HttpStatusCode.Created, shipmentResponse.StatusCode);
        var shipment = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryShipments!, candidate => candidate.SalesOrderId == allocated.Id);
        var invoiceRequest = new InvoiceInventoryShipmentRequest(shipment.Id, $"INV-{orderNumber}", new DateOnly(2026, 8, 22), new DateOnly(2026, 9, 21), "API shipment invoice", shipment.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/inventory-shipments/{shipment.Id}/invoice", invoiceRequest)).StatusCode);
        var invoiceResponse = await receivables.PostAsJsonAsync($"/api/inventory-shipments/{shipment.Id}/invoice", invoiceRequest); Assert.Equal(HttpStatusCode.Created, invoiceResponse.StatusCode);
        var invoicedShipment = Assert.Single((await receivables.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.InventoryShipments!, candidate => candidate.Id == shipment.Id);
        Assert.NotNull(invoicedShipment.SalesInvoiceId);
        Assert.Contains((await receivables.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"))!.Receivables.Invoices, invoice => invoice.Id == invoicedShipment.SalesInvoiceId && invoice.TotalAmount == 42m);
        var returnRequest = new AuthorizeCustomerReturnRequest(invoicedShipment.Id, $"RMA-{orderNumber}", new DateOnly(2026, 8, 23), "API customer return", [new(invoicedShipment.Lines.Single().Id, 1m)], invoicedShipment.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/inventory-shipments/{invoicedShipment.Id}/customer-returns", returnRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await sales.PostAsJsonAsync($"/api/inventory-shipments/{invoicedShipment.Id}/customer-returns", returnRequest)).StatusCode);
        var authorization = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.CustomerReturnAuthorizations!, item => item.ReturnNumber == returnRequest.ReturnNumber); var returnLine = Assert.Single(authorization.Lines);
        var receiveRequest = new ReceiveCustomerReturnRequest(authorization.Id, $"CRCV-{orderNumber}", new DateOnly(2026, 8, 24), null, null, [new(returnLine.Id, 1m)], authorization.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsJsonAsync($"/api/customer-returns/{authorization.Id}/receipts", receiveRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await warehouse.PostAsJsonAsync($"/api/customer-returns/{authorization.Id}/receipts", receiveRequest)).StatusCode);
        var returnReceipt = Assert.Single((await warehouse.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.CustomerReturnReceipts!, item => item.ReceiptNumber == receiveRequest.ReceiptNumber);
        var creditRequest = new CreditCustomerReturnRequest(returnReceipt.Id, $"CM-{orderNumber}", new DateOnly(2026, 8, 25), "API accepted return", returnReceipt.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/customer-return-receipts/{returnReceipt.Id}/credit", creditRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await receivables.PostAsJsonAsync($"/api/customer-return-receipts/{returnReceipt.Id}/credit", creditRequest)).StatusCode);
        var returnCredit = Assert.Single((await receivables.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.CustomerReturnCredits!, item => item.CreditNumber == creditRequest.CreditNumber); Assert.Equal(21m, returnCredit.TotalAmount); Assert.Equal(21m, returnCredit.SourceAppliedAmount); Assert.Equal(0m, returnCredit.AvailableAmount);
        var partiallyFulfilled = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, order => order.Id == approved.Id);
        var cancellation = new CancelSalesOrderRequest(partiallyFulfilled.Id, "Customer cancelled final unit", partiallyFulfilled.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/sales-orders/{partiallyFulfilled.Id}/cancellation", cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsJsonAsync($"/api/sales-orders/{partiallyFulfilled.Id}/cancellation", cancellation)).StatusCode);
        var closed = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesOrders, order => order.Id == approved.Id); Assert.Equal("Closed", closed.Status); Assert.Equal(1m, closed.Lines!.Single().CancelledQuantity); Assert.Equal(42m, closed.TotalAmount);
    }

    [Fact]
    public async Task SalesQuoteApi_RequiresSalesAuthorityAndConvertsApprovedQuoteWithProvenance()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var sales = await CreateAuthenticatedClientAsync(isolatedFactory, "sales");
        using var warehouse = await CreateAuthenticatedClientAsync(isolatedFactory, "warehouse");
        var workspace = await sales.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var customer = workspace!.Receivables.Customers.First(); var item = workspace.Operations.InventoryItems.First(); var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quoteNumber = $"QUO-API-{Guid.NewGuid():N}"; var request = new SaveSalesQuoteRequest(null, customer.Id, quoteNumber, today, today.AddDays(30), "API quote", [new SalesOrderLineRequest(item.Id, "Quoted inventory", 2m, 30m, 5m, 3m, "4000")]);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync("/api/sales-quotes", request)).StatusCode);
        var savedResponse = await sales.PostAsJsonAsync("/api/sales-quotes", request); Assert.Equal(HttpStatusCode.Created, savedResponse.StatusCode);
        var saved = await savedResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var quote = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesQuotes!, candidate => candidate.Id == saved!.Id); Assert.Equal(58m, quote.TotalAmount);
        Assert.Equal(HttpStatusCode.BadRequest, (await sales.PostAsJsonAsync($"/api/sales-quotes/{Guid.NewGuid()}/approval", new ApproveSalesQuoteRequest(quote.Id, quote.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/sales-quotes/{quote.Id}/approval", new ApproveSalesQuoteRequest(quote.Id, quote.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsJsonAsync($"/api/sales-quotes/{quote.Id}/approval", new ApproveSalesQuoteRequest(quote.Id, quote.ConcurrencyToken))).StatusCode);
        quote = Assert.Single((await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"))!.SalesQuotes!, candidate => candidate.Id == quote.Id);
        var orderNumber = $"SO-{quoteNumber}"; var conversion = new ConvertSalesQuoteRequest(quote.Id, orderNumber, today.AddDays(1), today.AddDays(4), "Accepted through API", quote.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Created, (await sales.PostAsJsonAsync($"/api/sales-quotes/{quote.Id}/conversion", conversion)).StatusCode);
        var operations = await sales.GetFromJsonAsync<OperationsWorkspace>("/api/operations"); quote = Assert.Single(operations!.SalesQuotes!, candidate => candidate.Id == quote.Id); var order = Assert.Single(operations.SalesOrders, candidate => candidate.OrderNumber == orderNumber);
        Assert.Equal("Converted", quote.Status); Assert.Equal(order.Id, quote.ConvertedSalesOrderId); Assert.Equal(quote.TotalAmount, order.TotalAmount); Assert.Equal(quote.Lines.Single().Quantity, order.Lines!.Single().OrderedQuantity);
    }

    [Fact]
    public async Task ApiLogin_LocksOperatorAfterRepeatedFailures()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        for (var attempt = 0; attempt < BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts - 1; attempt++)
        {
            var failedResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                UserName = "controller",
                Password = "wrong-password"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, failedResponse.StatusCode);
        }

        var lockedResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Locked, lockedResponse.StatusCode);
    }

    [Fact]
    public async Task ExistingSession_IsRejectedAfterSecurityStampChanges()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);

        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var user = await dbContext.Users.SingleAsync(x => x.UserName == "controller");
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ReissuesCurrentSession_RevokesOtherSessions_AndAuditsTheChange()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var currentClient = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var otherClient = await CreateAuthenticatedClientAsync(isolatedFactory);
        var token = await GetAntiforgeryTokenAsync(currentClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/change-password")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["currentPassword"] = BrassLedgerAuthenticationDefaults.SeededPassword,
                ["newPassword"] = "Changed password! 2026",
                ["confirmPassword"] = "Changed password! 2026"
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await currentClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/security?status=password-changed", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await currentClient.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherClient.GetAsync("/api/dashboard")).StatusCode);

        using var oldPasswordClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var oldPasswordResponse = await oldPasswordClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);

        using var newPasswordClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var newPasswordResponse = await newPasswordClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = "Changed password! 2026"
        });
        Assert.Equal(HttpStatusCode.OK, newPasswordResponse.StatusCode);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        Assert.True(await dbContext.AuthenticationAuditEntries.AnyAsync(entry =>
            entry.UserName == "controller" && entry.EventType == "password_changed" && entry.Succeeded));
    }

    [Fact]
    public async Task ChangePassword_RejectsInvalidCurrentPassword_AndMissingAntiforgeryToken()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory, includeAntiforgery: false);
        using var missingTokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["currentPassword"] = BrassLedgerAuthenticationDefaults.SeededPassword,
            ["newPassword"] = "Changed password! 2026",
            ["confirmPassword"] = "Changed password! 2026"
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/change-password", missingTokenContent)).StatusCode);

        var token = await GetAntiforgeryTokenAsync(client);
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/account/change-password")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["currentPassword"] = "not-the-current-password",
                ["newPassword"] = "Changed password! 2026",
                ["confirmPassword"] = "Changed password! 2026"
            })
        };
        invalidRequest.Headers.Add("X-CSRF-TOKEN", token);
        var invalidResponse = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.Redirect, invalidResponse.StatusCode);
        Assert.Equal("/account/security?error=current-password", invalidResponse.Headers.Location?.OriginalString);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        Assert.True(await dbContext.AuthenticationAuditEntries.AnyAsync(entry =>
            entry.UserName == "controller" && entry.EventType == "password_change_failed" && !entry.Succeeded));
    }

    [Fact]
    public async Task RevokeOtherSessions_KeepsCurrentBrowserSignedIn()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var currentClient = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var otherClient = await CreateAuthenticatedClientAsync(isolatedFactory);
        var token = await GetAntiforgeryTokenAsync(currentClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/revoke-other-sessions");
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await currentClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await currentClient.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherClient.GetAsync("/api/dashboard")).StatusCode);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(candidate => candidate.UserName == "controller");
        var sessions = await db.UserSessions.Where(session => session.UserId == user.Id).ToListAsync();
        Assert.Single(sessions, session => session.RevokedAtUtc is null && session.SecurityStamp == user.SecurityStamp);
        Assert.Equal(2, sessions.Count(session => session.RevokedAtUtc is not null));
    }

    [Fact]
    public async Task Logout_RevokesCurrentNamedSessionAndRecordsCompanyAudit()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var missingTokenClient = await CreateAuthenticatedClientAsync(isolatedFactory, includeAntiforgery: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await missingTokenClient.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await missingTokenClient.GetAsync("/api/dashboard")).StatusCode);

        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard")).StatusCode);
        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(candidate => candidate.UserName == "controller");
        var session = Assert.Single(await db.UserSessions.Where(candidate => candidate.UserId == user.Id && candidate.RevokedAtUtc != null).ToListAsync());
        Assert.NotNull(session.RevokedAtUtc);
        Assert.Contains(await db.AuthenticationAuditEntries.Where(entry => entry.UserId == user.Id).ToListAsync(),
            entry => entry.EventType == "logout" && entry.CompanyId == user.CompanyId && entry.Succeeded);
    }

    [Fact]
    public async Task AccountRecoveryApi_VerifiesEmailUsesUniformResetResponseAndConsumesTokensOnce()
    {
        using var isolatedFactory = new BrassLedgerApiFactory(configureSecurityEmail: true);
        using var authenticatedClient = await CreateAuthenticatedClientAsync(isolatedFactory);

        var verificationRequest = await authenticatedClient.PostAsync("/api/auth/email-verification/request", null);
        Assert.Equal(HttpStatusCode.Accepted, verificationRequest.StatusCode);
        await DispatchAllSecurityEmailAsync(isolatedFactory);
        var verificationMessage = Assert.Single(isolatedFactory.SecurityEmailTransport.Messages);
        var verificationToken = ExtractAccountActionToken(verificationMessage.Body);

        using var anonymousClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var verification = await anonymousClient.PostAsJsonAsync("/api/auth/email-verification/complete", new { Token = verificationToken });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymousClient.PostAsJsonAsync(
            "/api/auth/email-verification/complete", new { Token = verificationToken })).StatusCode);

        var emailChange = await authenticatedClient.PostAsJsonAsync("/api/auth/email/change", new
        {
            NewEmail = "controller-replacement@example.test",
            CurrentPassword = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, emailChange.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await authenticatedClient.GetAsync("/api/dashboard")).StatusCode);
        await DispatchAllSecurityEmailAsync(isolatedFactory);
        var replacementMessage = Assert.Single(isolatedFactory.SecurityEmailTransport.Messages, message => message.Subject.Contains("new BrassLedger email", StringComparison.Ordinal));
        Assert.Single(isolatedFactory.SecurityEmailTransport.Messages, message => message.Subject.Contains("email address was changed", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await anonymousClient.PostAsJsonAsync("/api/auth/email-verification/complete", new
        {
            Token = ExtractAccountActionToken(replacementMessage.Body)
        })).StatusCode);

        var unknownReset = await anonymousClient.PostAsJsonAsync("/api/auth/password-reset/request", new { Identifier = "missing@example.test" });
        var knownReset = await anonymousClient.PostAsJsonAsync("/api/auth/password-reset/request", new { Identifier = "controller" });
        Assert.Equal(HttpStatusCode.Accepted, unknownReset.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, knownReset.StatusCode);
        Assert.Equal(await unknownReset.Content.ReadAsStringAsync(), await knownReset.Content.ReadAsStringAsync());

        await DispatchAllSecurityEmailAsync(isolatedFactory);
        var resetMessage = Assert.Single(isolatedFactory.SecurityEmailTransport.Messages, message => message.Subject.Contains("Reset", StringComparison.Ordinal));
        var resetToken = ExtractAccountActionToken(resetMessage.Body);
        var reset = await anonymousClient.PostAsJsonAsync("/api/auth/account-action", new
        {
            Token = resetToken,
            NewPassword = "API recovery password 2026",
            ConfirmPassword = "API recovery password 2026"
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymousClient.PostAsJsonAsync("/api/auth/account-action", new
        {
            Token = resetToken,
            NewPassword = "Another API recovery password 2026",
            ConfirmPassword = "Another API recovery password 2026"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymousClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = "API recovery password 2026"
        })).StatusCode);
    }

    [Fact]
    public async Task ActiveCompanySwitch_ToPrivilegedMembershipRequiresMfaEnrollment()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var companyId = Guid.NewGuid();
        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.UserName == "controller");
            dbContext.Companies.Add(new Company
            {
                Id = companyId,
                Name = "Secondary company",
                LegalName = "Secondary Company LLC",
                TaxId = "12-3456789",
                BaseCurrency = "CAD",
                FiscalYearStartMonth = 1
            });
            await SecurityAdministrationService.EnsureBuiltInRolesAsync(dbContext, companyId);
            dbContext.CompanyMemberships.Add(new CompanyMembership
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CompanyId = companyId,
                Role = "Administrator",
                IsOwner = true,
                IsActive = true,
                GrantedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var switchResponse = await client.PostAsJsonAsync("/api/auth/active-company", new { CompanyId = companyId });

        Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.Equal(companyId.ToString(), me.GetProperty("companyId").GetString());
        Assert.True(me.GetProperty("mfaEnrollmentRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_ThrottlesExcessiveRequestsFromOneNetworkAddress()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var attempt = 0; attempt < BrassLedgerAuthenticationDefaults.LoginRequestsPerMinute; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                UserName = $"missing-user-{attempt}",
                Password = "invalid-password"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "one-request-too-many",
            Password = "invalid-password"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), throttled.Headers.RetryAfter?.Delta);
        var problem = await throttled.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("too_many_login_attempts", problem.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ApiLogin_RequiresAndCompletesMfa_AndConsumesRecoveryCodeOnce()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        var recoveryCodes = await EnrollMfaAsync(isolatedFactory, "controller");
        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var passwordResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, passwordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard")).StatusCode);
        var challenge = await passwordResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(challenge.GetProperty("mfaRequired").GetBoolean());
        var challengeToken = challenge.GetProperty("challengeToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(challengeToken));

        var mfaResponse = await client.PostAsJsonAsync("/api/auth/mfa", new
        {
            ChallengeToken = challengeToken,
            VerificationCode = recoveryCodes[0]
        });
        Assert.Equal(HttpStatusCode.OK, mfaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard")).StatusCode);
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("mfaAuthenticated").GetBoolean());

        using var replayClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var replayResponse = await replayClient.PostAsJsonAsync("/api/auth/mfa", new
        {
            ChallengeToken = challengeToken,
            VerificationCode = recoveryCodes[0]
        });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        var nextPasswordResponse = await replayClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        var nextChallenge = await nextPasswordResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var reusedRecoveryResponse = await replayClient.PostAsJsonAsync("/api/auth/mfa", new
        {
            ChallengeToken = nextChallenge.GetProperty("challengeToken").GetString(),
            VerificationCode = recoveryCodes[0]
        });
        Assert.Equal(HttpStatusCode.Unauthorized, reusedRecoveryResponse.StatusCode);
    }

    [Fact]
    public async Task PrivilegedRoleWithoutMfa_IsRestrictedToAccountSecurityUntilEnrollment()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrassLedgerDbContext>();
            var controllerRole = await db.AccessRoles.SingleAsync(role => role.Name == "Controller");
            controllerRole.RequiresMfa = true;
            await db.SaveChangesAsync();
        }

        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var restrictedLogin = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.OK, restrictedLogin.StatusCode);
        var restrictedIdentity = await restrictedLogin.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(restrictedIdentity.GetProperty("mfaEnrollmentRequired").GetBoolean());
        Assert.False(restrictedIdentity.GetProperty("mfaAuthenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard")).StatusCode);
        var securityIdentity = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.True(securityIdentity.GetProperty("mfaEnrollmentRequired").GetBoolean());

        var recoveryCodes = await EnrollMfaAsync(isolatedFactory, "controller");
        using var verifiedClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var passwordStage = await verifiedClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, passwordStage.StatusCode);
        var challenge = await passwordStage.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var verification = await verifiedClient.PostAsJsonAsync("/api/auth/mfa", new
        {
            ChallengeToken = challenge.GetProperty("challengeToken").GetString(),
            VerificationCode = recoveryCodes[0]
        });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await verifiedClient.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task TrialBalanceReport_ReturnsCsvForReportingUser()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/reports/trial-balance.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("Account,Type,Balance", csv);
        Assert.Contains("1000", csv);
    }

    [Fact]
    public async Task InvoiceApi_RequiresDraftApprovalAndSeparatedPosting()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        await EnsureControllerCloneAsync(isolatedFactory, "subledger-approver-api");
        await EnsureControllerCloneAsync(isolatedFactory, "subledger-poster-api");
        using var preparer = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var approver = await CreateAuthenticatedClientAsync(isolatedFactory, "subledger-approver-api");
        using var poster = await CreateAuthenticatedClientAsync(isolatedFactory, "subledger-poster-api");
        var before = await preparer.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var customer = before!.Receivables.Customers.First();
        var vendor = before.Payables.Vendors.First();
        var request = new CreateInvoiceRequest(
            customer.Id,
            "INV-API-TEST-1",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            125m,
            0m,
            "4000",
            "API workflow test");

        Assert.Equal(HttpStatusCode.NotFound, (await preparer.PostAsJsonAsync("/api/invoices", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await preparer.PostAsJsonAsync("/api/vendor-bills", new CreateVendorBillRequest(
            vendor.Id, "BILL-API-BYPASS-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 25m, "5100", "Bypass must not exist"))).StatusCode);
        var draftResponse = await preparer.PostAsJsonAsync("/api/invoice-drafts", request);
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draftResult = await draftResponse.Content.ReadFromJsonAsync<TransactionResult>();

        var rejectionRequest = request with { InvoiceNumber = "INV-API-REJECT-1", Description = "Needs review" };
        var rejectionDraftResponse = await preparer.PostAsJsonAsync("/api/invoice-drafts", rejectionRequest);
        Assert.Equal(HttpStatusCode.Created, rejectionDraftResponse.StatusCode);
        var rejectionDraft = await rejectionDraftResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var reviewWorkspace = await preparer.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var workflow = Assert.Single(reviewWorkspace!.Receivables.Workflows ?? [], item => item.Id == rejectionDraft!.Id);
        var rejectCommand = new RejectSubledgerDocumentRequest(workflow.Id, "Correct the customer-facing description.", workflow.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await preparer.PostAsJsonAsync($"/api/subledger-document-workflows/{workflow.Id}/reject", rejectCommand)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsJsonAsync($"/api/subledger-document-workflows/{Guid.NewGuid()}/reject", rejectCommand)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsJsonAsync($"/api/subledger-document-workflows/{workflow.Id}/reject", rejectCommand)).StatusCode);
        var rejectedWorkspace = await preparer.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var rejectedWorkflow = Assert.Single(rejectedWorkspace!.Receivables.Workflows ?? [], item => item.Id == workflow.Id);
        Assert.Equal("Rejected", rejectedWorkflow.Status);
        Assert.Equal("Correct the customer-facing description.", rejectedWorkflow.DecisionReason);
        var revisedResponse = await preparer.PostAsJsonAsync("/api/invoice-drafts", rejectionRequest with { Description = "Corrected customer-facing description" });
        Assert.Equal(HttpStatusCode.Created, revisedResponse.StatusCode);
        var revisedResult = await revisedResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.Equal(workflow.Id, revisedResult!.Id);
        var revisedWorkspace = await preparer.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var revisedWorkflow = Assert.Single(revisedWorkspace!.Receivables.Workflows ?? [], item => item.Id == workflow.Id);
        Assert.Equal("Draft", revisedWorkflow.Status);
        Assert.Empty(revisedWorkflow.DecisionReason);
        Assert.NotEqual(rejectedWorkflow.ConcurrencyToken, revisedWorkflow.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, (await preparer.PostAsync($"/api/subledger-document-workflows/{draftResult!.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsync($"/api/subledger-document-workflows/{draftResult.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"/api/subledger-document-workflows/{draftResult.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await poster.PostAsync($"/api/subledger-document-workflows/{draftResult.Id}/post", null)).StatusCode);
        var after = await preparer.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(after);
        Assert.Equal(before.Receivables.OpenBalance + 125m, after!.Receivables.OpenBalance);
        Assert.Contains(after.Receivables.Invoices, invoice => invoice.InvoiceNumber == "INV-API-TEST-1" && invoice.BalanceDue == 125m);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var posting = await dbContext.JournalEntries.SingleAsync(entry => entry.Reference == "INV-API-TEST-1");
        Assert.NotNull(posting.PostedByUserId);
        Assert.True(posting.PostedAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CustomerPaymentApi_AppliesMultipleInvoices_PreservesDeposit_AndReturnsPayment()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var customer = before!.Receivables.Customers.First();
        var bank = before.Treasury.BankAccounts.First();

        async Task<Guid> PostInvoiceForPaymentSetupAsync(string number, decimal amount)
        {
            var result = await PostInvoiceThroughWorkflowAsync(isolatedFactory.Services, new(
                customer.Id, number, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), amount, 0m, "4000", "Payment API workflow"));
            Assert.True(result.Succeeded, result.ErrorMessage);
            return result.Id!.Value;
        }

        var firstInvoiceId = await PostInvoiceForPaymentSetupAsync("INV-API-PAY-1", 40m);
        var secondInvoiceId = await PostInvoiceForPaymentSetupAsync("INV-API-PAY-2", 35m);
        var paymentResponse = await client.PostAsJsonAsync("/api/customer-payments", new RecordCustomerPaymentRequest(
            customer.Id, bank.Id, new DateOnly(2026, 5, 2), 90m, "DEP-API-PAY-1", "ACH",
            [new PaymentDocumentApplicationRequest(firstInvoiceId, 40m), new PaymentDocumentApplicationRequest(secondInvoiceId, 35m)]));
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var paymentResult = await paymentResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(paymentResult?.Id);

        var paid = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(paid);
        var recorded = Assert.Single(paid!.Receivables.Payments!, payment => payment.Id == paymentResult!.Id);
        Assert.Equal(75m, recorded.AppliedAmount);
        Assert.Equal(15m, recorded.UnappliedAmount);
        Assert.Equal(2, recorded.Applications.Count);

        var returnResponse = await client.PostAsJsonAsync("/api/subledger-payments/reverse", new ReverseSubledgerPaymentRequest(
            paymentResult!.Id!.Value, new DateOnly(2026, 5, 3), "Bank returned the ACH", "Returned"));
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        var returned = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(returned);
        Assert.Equal("Returned", returned!.Receivables.Payments!.Single(payment => payment.Id == paymentResult.Id).Status);
        Assert.Equal(40m, returned.Receivables.Invoices.Single(invoice => invoice.Id == firstInvoiceId).BalanceDue);
        Assert.Equal(35m, returned.Receivables.Invoices.Single(invoice => invoice.Id == secondInvoiceId).BalanceDue);
    }

    [Fact]
    public async Task BankingApi_ImportsStatementsAndReversesTransfersAndAdjustments()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var fromBank = before!.Treasury.BankAccounts.First();
        var toBank = before.Treasury.BankAccounts.Last();

        var importResponse = await client.PostAsJsonAsync("/api/bank-statements/import", new ImportBankStatementRequest(
            fromBank.Id, "api-statement.csv", "CSV", "ExternalId,Date,Amount,Payee\nAPI-BANK-1,2026-05-01,15.00,Customer"));
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var imported = await importResponse.Content.ReadFromJsonAsync<BankStatementImportResult>();
        Assert.Equal(1, imported?.ImportedCount);

        var transferResponse = await client.PostAsJsonAsync("/api/bank-transfers", new CreateBankTransferRequest(
            fromBank.Id, toBank.Id, new DateOnly(2026, 5, 2), 25m, "TR-API-BANK-1", "API transfer"));
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);
        var transfer = await transferResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(transfer?.Id);
        var reverseTransferResponse = await client.PostAsJsonAsync("/api/bank-transfers/reverse", new ReverseBankTransferRequest(
            transfer!.Id!.Value, new DateOnly(2026, 5, 3), "API correction"));
        Assert.Equal(HttpStatusCode.OK, reverseTransferResponse.StatusCode);

        var offsetAccount = before.GeneralLedger.Accounts.First(account => account.Type == "Expense" && !account.IsControlAccount).Number;
        var adjustmentResponse = await client.PostAsJsonAsync("/api/bank-reconciliation-adjustments", new CreateReconciliationAdjustmentRequest(
            fromBank.Id, new DateOnly(2026, 5, 4), 5m, offsetAccount, "ADJ-API-BANK-1", "API bank interest"));
        Assert.Equal(HttpStatusCode.Created, adjustmentResponse.StatusCode);
        var adjustment = await adjustmentResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(adjustment?.Id);
        var reverseAdjustmentResponse = await client.PostAsJsonAsync("/api/bank-reconciliation-adjustments/reverse", new ReverseReconciliationAdjustmentRequest(
            adjustment!.Id!.Value, new DateOnly(2026, 5, 5), "API correction"));
        Assert.Equal(HttpStatusCode.OK, reverseAdjustmentResponse.StatusCode);

        var after = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(after);
        Assert.Equal(before.Treasury.BankAccounts.Single(bank => bank.Id == fromBank.Id).CurrentBalance, after!.Treasury.BankAccounts.Single(bank => bank.Id == fromBank.Id).CurrentBalance);
        Assert.Equal(before.Treasury.BankAccounts.Single(bank => bank.Id == toBank.Id).CurrentBalance, after.Treasury.BankAccounts.Single(bank => bank.Id == toBank.Id).CurrentBalance);
        Assert.Equal("Reversed", after.Treasury.Transfers!.Single(item => item.Id == transfer.Id).Status);
        Assert.Equal("Reversed", after.Treasury.Adjustments!.Single(item => item.Id == adjustment.Id).Status);
    }

    [Fact]
    public async Task JournalDraftApi_RequiresApprovalBeforePostingAndPreservesReversalLinks()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        await EnsureControllerCloneAsync(isolatedFactory, "journal-reviewer-api");
        await EnsureControllerCloneAsync(isolatedFactory, "journal-poster-api");
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var reviewer = await CreateAuthenticatedClientAsync(isolatedFactory, "journal-reviewer-api");
        using var poster = await CreateAuthenticatedClientAsync(isolatedFactory, "journal-poster-api");
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);

        var draftResponse = await client.PostAsJsonAsync("/api/journal-entry-drafts", new SaveJournalEntryDraftRequest(
            null,
            new DateOnly(2026, 5, 4),
            "JE-API-LIFECYCLE-1",
            "API journal lifecycle",
            [new JournalLineRequest("1000", 40m, 0m, "Cash"), new JournalLineRequest("4000", 0m, 40m, "Revenue")]));
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = await draftResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(draft?.Id);

        var prematurePost = await client.PostAsync($"/api/journal-entry-drafts/{draft!.Id}/post", null);
        Assert.Equal(HttpStatusCode.BadRequest, prematurePost.StatusCode);
        var current = (await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"))!.GeneralLedger.RecentEntries.Single(entry => entry.Id == draft.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/journal-entry-drafts/{draft.Id}/reject", new RejectJournalEntryRequest(draft.Id.Value, "Self-review must fail.", current.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.PostAsJsonAsync($"/api/journal-entry-drafts/{draft.Id}/reject", new RejectJournalEntryRequest(draft.Id.Value, "Stale review.", "stale-token"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync($"/api/journal-entry-drafts/{draft.Id}/reject", new RejectJournalEntryRequest(draft.Id.Value, "Attach supporting documentation.", current.ConcurrencyToken))).StatusCode);
        current = (await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"))!.GeneralLedger.RecentEntries.Single(entry => entry.Id == draft.Id);
        Assert.Equal("Rejected", current.Status);
        Assert.Equal("Attach supporting documentation.", current.DecisionReason);
        var correctionResponse = await client.PostAsJsonAsync("/api/journal-entry-drafts", new SaveJournalEntryDraftRequest(
            draft.Id,
            new DateOnly(2026, 5, 4),
            "JE-API-LIFECYCLE-1",
            "API journal lifecycle — support attached",
            [new JournalLineRequest("1000", 40m, 0m, "Cash"), new JournalLineRequest("4000", 0m, 40m, "Revenue")],
            current.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/journal-entry-drafts/{draft.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsync($"/api/journal-entry-drafts/{draft.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.PostAsync($"/api/journal-entry-drafts/{draft.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await poster.PostAsync($"/api/journal-entry-drafts/{draft.Id}/post", null)).StatusCode);

        var afterPosting = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(afterPosting);
        Assert.Equal(before!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance + 40m, afterPosting!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);

        var reversalResponse = await client.PostAsJsonAsync("/api/journal-entries/reverse", new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 5), "API correction"));
        Assert.Equal(HttpStatusCode.Created, reversalResponse.StatusCode);
        var afterReversal = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(afterReversal);
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance, afterReversal!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);
        Assert.Contains(afterReversal.GeneralLedger.RecentEntries, entry => entry.Id == draft.Id && entry.Status == "Reversed" && entry.ReversedByJournalEntryId.HasValue);
        Assert.Contains(afterReversal.GeneralLedger.RecentEntries, entry => entry.ReversalOfJournalEntryId == draft.Id);
    }

    [Fact]
    public async Task PayrollApi_PreservesDraftApprovalPostingAndReversalWorkflow()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        await EnsureControllerCloneAsync(isolatedFactory, "payroll-reviewer-api", "payroll");
        await EnsureControllerCloneAsync(isolatedFactory, "payroll-poster-api", "payroll");
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory, "payroll");
        using var reviewer = await CreateAuthenticatedClientAsync(isolatedFactory, "payroll-reviewer-api");
        using var poster = await CreateAuthenticatedClientAsync(isolatedFactory, "payroll-poster-api");
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var employee = before!.Payroll.Employees.First();
        var bank = before.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010");
        var timecardRequest = new SavePayrollTimecardDraftRequest(null, employee.Id, new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 6),
            [new PayrollTimeEntryInput(new DateOnly(2026, 6, 1), "REG", "Regular", 8m, 25m, 200m, WorkState: employee.State)], "API timecard");
        var timecardResponse = await client.PostAsJsonAsync("/api/payroll-timecards/drafts", timecardRequest);
        Assert.Equal(HttpStatusCode.Created, timecardResponse.StatusCode);
        var timecardResult = await timecardResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var timecardWorkspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var timecard = timecardWorkspace!.Payroll.Timecards!.Single(candidate => candidate.Id == timecardResult!.Id);
        Assert.Equal("Draft", timecard.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-timecards/submit", new SubmitPayrollTimecardRequest(timecard.Id, timecard.ConcurrencyToken))).StatusCode);
        timecardWorkspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        timecard = timecardWorkspace!.Payroll.Timecards!.Single(candidate => candidate.Id == timecard.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-timecards/approve", new ApprovePayrollTimecardRequest(timecard.Id, timecard.ConcurrencyToken))).StatusCode);
        timecardWorkspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        timecard = timecardWorkspace!.Payroll.Timecards!.Single(candidate => candidate.Id == timecard.Id);
        Assert.Equal("Approved", timecard.Status);

        var request = new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 6, 12), "PR-API-LIFECYCLE-1", [new EmployeePayrollInput(employee.Id, 500m)], new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 6), ApprovedTimecardIds: [timecard.Id]);

        var preview = await client.PostAsJsonAsync("/api/payroll-runs/employee-preview", request);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewResult = await preview.Content.ReadFromJsonAsync<PayrollRunEstimate>();
        Assert.Equal(200m, previewResult!.GrossPayroll);
        var draftResponse = await client.PostAsJsonAsync("/api/payroll-runs/drafts", request);
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draftResult = await draftResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(draftResult?.Id);

        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == draftResult!.Id);
        Assert.Equal("Draft", run.Status);
        Assert.Equal(200m, run.GrossPayroll);
        timecard = workspace.Payroll.Timecards!.Single(candidate => candidate.Id == timecard.Id);
        Assert.Equal("Consumed", timecard.Status);
        Assert.Equal(run.Id, timecard.PayrollRunId);
        var reused = request with { Reference = "PR-API-LIFECYCLE-REUSE" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/payroll-runs/drafts", reused)).StatusCode);
        Assert.Equal(bank.CurrentBalance, workspace.Treasury.BankAccounts.Single(account => account.Id == bank.Id).CurrentBalance);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/payroll-runs/post", new PostApprovedPayrollRunRequest(run.Id, run.ConcurrencyToken))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/payroll-runs/reject", new RejectPayrollRunRequest(run.Id, "Self-review must fail.", run.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.PostAsJsonAsync("/api/payroll-runs/reject", new RejectPayrollRunRequest(run.Id, "Stale review.", "stale-token"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync("/api/payroll-runs/reject", new RejectPayrollRunRequest(run.Id, "Confirm the approved timecard and resubmit.", run.ConcurrencyToken))).StatusCode);
        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal(("Rejected", "Confirm the approved timecard and resubmit."), (run.Status, run.RejectionReason));
        var correction = await client.GetFromJsonAsync<PostEmployeePayrollRunRequest>($"/api/payroll-runs/{run.Id}/draft");
        Assert.NotNull(correction); Assert.Equal(run.Id, correction!.Id); Assert.Contains(timecard.Id, correction.ApprovedTimecardIds!);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/payroll-runs/drafts", correction)).StatusCode);
        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/payroll-runs/approve", new ApprovePayrollRunRequest(run.Id, run.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync("/api/payroll-runs/approve", new ApprovePayrollRunRequest(run.Id, run.ConcurrencyToken))).StatusCode);
        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal("Approved", run.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.PostAsJsonAsync("/api/payroll-runs/post", new PostApprovedPayrollRunRequest(run.Id, run.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await poster.PostAsJsonAsync("/api/payroll-runs/post", new PostApprovedPayrollRunRequest(run.Id, run.ConcurrencyToken))).StatusCode);

        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal("Posted", run.Status);
        Assert.NotNull(run.JournalEntryId);
        Assert.Equal(bank.CurrentBalance - run.NetPay, workspace.Treasury.BankAccounts.Single(account => account.Id == bank.Id).CurrentBalance);
        var register = await client.GetFromJsonAsync<PayrollRegister>($"/api/payroll-runs/{run.Id}/register");
        Assert.NotNull(register);
        Assert.Equal(run.NetPay, register!.Employees.Sum(item => item.NetPay));
        var statement = await client.GetFromJsonAsync<PayrollPayStatement>($"/api/payroll-runs/{run.Id}/employees/{employee.Id}/pay-statement");
        Assert.NotNull(statement);
        Assert.Equal(run.NetPay, statement!.NetPay);
        Assert.Equal(statement.GrossPay, statement.Earnings.Sum(item => item.Amount));
        var registerCsv = await client.GetAsync($"/api/payroll-runs/{run.Id}/register.csv");
        Assert.Equal("text/csv", registerCsv.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"TOTAL\"", await registerCsv.Content.ReadAsStringAsync());
        var depositScheduleResponse = await client.PutAsJsonAsync("/api/payroll-deposit-schedules", new SavePayrollDepositScheduleRequest(null, 2026, "Monthly", 40000m, new DateOnly(2024, 7, 1), new DateOnly(2025, 6, 30), 50000m, 100000m, 2500m, "[]", "[\"2026-01-01\",\"2026-01-19\",\"2026-02-16\",\"2026-04-16\",\"2026-05-25\",\"2026-06-19\",\"2026-07-03\",\"2026-09-07\",\"2026-10-12\",\"2026-11-11\",\"2026-11-26\",\"2026-12-25\"]", "https://www.irs.gov/publications/p15", "https://www.irs.gov/publications/p509", new DateOnly(2026, 8, 25), "API approval test", true, true));
        Assert.Equal(HttpStatusCode.OK, depositScheduleResponse.StatusCode);
        var depositWorkspace = await client.GetFromJsonAsync<PayrollDepositScheduleWorkspace>("/api/payroll-deposit-schedules");
        Assert.Contains(depositWorkspace!.Configurations, item => item.TaxYear == 2026 && item.IsApproved);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/payroll-disaster-relief")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ssa-wage-files")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ssa-original-wage-files")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/payroll-deduction-configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/payroll-payment-files")).StatusCode);
        var paymentFileResponse = await client.PostAsJsonAsync("/api/payroll-payment-files", new GeneratePayrollPaymentFileRequest(run.Id, "CheckRegisterCsv"));
        Assert.Equal(HttpStatusCode.Created, paymentFileResponse.StatusCode);
        var paymentFileResult = await paymentFileResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var paymentFileDownload = await client.GetAsync($"/api/payroll-payment-files/{paymentFileResult!.Id}/download");
        Assert.Equal("text/csv", paymentFileDownload.Content.Headers.ContentType?.MediaType);
        Assert.Contains("CheckReference", await paymentFileDownload.Content.ReadAsStringAsync());
        using (var nonPayrollClient = await CreateAuthenticatedClientAsync(isolatedFactory, "controller"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync($"/api/payroll-runs/{run.Id}/register")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync($"/api/payroll-runs/{run.Id}/employees/{employee.Id}/pay-statement")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-filings")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-filing-corrections")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.PostAsJsonAsync("/api/payroll-filing-corrections/w2c/drafts", new SaveW2CorrectionDraftRequest(null, Guid.NewGuid(), new DateOnly(2026, 8, 25), "Unauthorized correction attempt must never reach the protected service.", true, "TEST-EVIDENCE"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-deposit-schedules")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-disaster-relief")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/ssa-wage-files")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/ssa-original-wage-files")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-deduction-configuration")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-payment-files")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync($"/api/payroll-payment-files/{paymentFileResult.Id}/download")).StatusCode);
        }
        var filingResponse = await client.PostAsJsonAsync("/api/payroll-filings/drafts", new SavePayrollFilingDraftRequest(null, "941", 2026, 2));
        Assert.Equal(HttpStatusCode.Created, filingResponse.StatusCode);
        var filingResult = await filingResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var filing = await client.GetFromJsonAsync<PayrollFilingSnapshot>($"/api/payroll-filings/{filingResult!.Id}");
        Assert.NotNull(filing);
        Assert.True(filing!.Data.GetProperty("WagesTipsAndOtherCompensation").GetDecimal() > 0);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-filings/approve", new ApprovePayrollFilingRequest(filing.Id, filing.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/payroll-filing-corrections")).StatusCode);
        filing = await client.GetFromJsonAsync<PayrollFilingSnapshot>($"/api/payroll-filings/{filing.Id}");
        Assert.Equal("Approved", filing!.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-filings/reopen", new ReopenPayrollFilingRequest(filing.Id, "API correction test", filing.ConcurrencyToken))).StatusCode);
        var liability = workspace.Payroll.Liabilities!.First(item => item.Status == "Open");
        var liabilityPaymentResponse = await client.PostAsJsonAsync("/api/payroll-liability-payments", new RecordPayrollLiabilityPaymentRequest(bank.Id, new DateOnly(2026, 6, 13), "API-TAX-PAY-1", "Tax agency", "EFT", [new PayrollLiabilityPaymentApplicationInput(liability.Id, liability.OutstandingAmount)]));
        Assert.Equal(HttpStatusCode.Created, liabilityPaymentResponse.StatusCode);
        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var liabilityPayment = workspace!.Payroll.LiabilityPayments!.Single(item => item.Reference == "API-TAX-PAY-1");
        Assert.Equal("Paid", workspace.Payroll.Liabilities!.Single(item => item.Id == liability.Id).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-liability-payments/reverse", new ReversePayrollLiabilityPaymentRequest(liabilityPayment.Id, new DateOnly(2026, 6, 13), "API correction", liabilityPayment.ConcurrencyToken))).StatusCode);
        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-runs/reverse", new ReversePayrollRunRequest(run.Id, new DateOnly(2026, 6, 13), "API payroll correction", run.ConcurrencyToken))).StatusCode);

        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal("Reversed", run.Status);
        Assert.NotNull(run.ReversalJournalEntryId);
        Assert.Equal(bank.CurrentBalance, workspace.Treasury.BankAccounts.Single(account => account.Id == bank.Id).CurrentBalance);
        var paymentFileWorkspace = await client.GetFromJsonAsync<PayrollPaymentFileWorkspace>("/api/payroll-payment-files");
        Assert.Equal("Voided", paymentFileWorkspace!.Files.Single(item => item.Id == paymentFileResult.Id).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/payroll-runs/cancel", new CancelPayrollRunRequest(run.Id, "Too late", run.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task QuickBooksOnlineInterchange_ExportsAndImportsCoreLists()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        await EnsureControllerCloneAsync(isolatedFactory, "qbo-approver-api");
        await EnsureControllerCloneAsync(isolatedFactory, "qbo-poster-api");
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var invoiceApprover = await CreateAuthenticatedClientAsync(isolatedFactory, "qbo-approver-api");
        using var invoicePoster = await CreateAuthenticatedClientAsync(isolatedFactory, "qbo-poster-api");

        var export = await client.GetAsync("/api/interchange/quickbooks-online/chart-of-accounts.csv");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportedCsv = await export.Content.ReadAsStringAsync();
        Assert.Contains("\"Account Name\",\"Type\",\"Detail Type\",\"Account Number\"", exportedCsv);
        Assert.Contains("\"Accounts Receivable\",\"Accounts Receivable\",\"Accounts Receivable\",\"1100\"", exportedCsv);
        Assert.Contains("\"Sales Tax Payable\",\"Other Current Liability\",\"Sales tax payable\",\"2100\"", exportedCsv);
        var invoiceExport = await client.GetStringAsync("/api/interchange/quickbooks-online/invoices.csv");
        Assert.Contains("\"Invoice No.\",\"Customer\",\"Invoice Date\",\"Due Date\",\"Item Amount\",\"Item Description\",\"Quantity\",\"Rate\",\"Project / Job\"", invoiceExport);
        Assert.Contains("INV-24021", invoiceExport);
        Assert.DoesNotContain("INV-24015", invoiceExport);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Display Name,Company Name,Email,Customer Number\r\n\"QuickBooks\nImport Co\",QuickBooks Import Co,import@example.test,QBO-IMPORT-1"), "file", "quickbooks-customers.csv");
        var preview = await client.PostAsync("/api/interchange/quickbooks-online/customers?dryRun=true", form);
        Assert.True(preview.StatusCode == HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var previewResult = await preview.Content.ReadFromJsonAsync<AccountingInterchangeImportResult>();
        Assert.True(previewResult!.DryRun); Assert.Equal(1, previewResult.ImportedCount); Assert.Equal(64, previewResult.ContentSha256.Length);
        var previewWorkspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.DoesNotContain(previewWorkspace!.Receivables.Customers, customer => customer.CustomerNumber == "QBO-IMPORT-1");
        using var importForm = new MultipartFormDataContent();
        importForm.Add(new StringContent("Display Name,Company Name,Email,Customer Number\r\n\"QuickBooks\nImport Co\",QuickBooks Import Co,import@example.test,QBO-IMPORT-1"), "file", "quickbooks-customers.csv");
        var import = await client.PostAsync("/api/interchange/quickbooks-online/customers", importForm);
        Assert.True(import.StatusCode == HttpStatusCode.OK, await import.Content.ReadAsStringAsync());
        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(workspace);
        Assert.Contains(workspace!.Receivables.Customers, customer => customer.CustomerNumber == "QBO-IMPORT-1" && customer.Name == "QuickBooks\nImport Co");

        var controlsResponse = await client.GetAsync("/api/accounting-controls?auditEntryLimit=20");
        Assert.True(controlsResponse.StatusCode == HttpStatusCode.OK, await controlsResponse.Content.ReadAsStringAsync());
        var controls = await controlsResponse.Content.ReadFromJsonAsync<AccountingControlsSnapshot>();
        var validationAudit = Assert.Single(controls!.AuditEntries, entry => entry.Action == "accounting-interchange.quickbooks.validated");
        var importAudit = Assert.Single(controls.AuditEntries, entry => entry.Action == "accounting-interchange.quickbooks.imported");
        Assert.Contains(previewResult.ContentSha256, validationAudit.DetailJson);
        Assert.Contains(previewResult.ContentSha256, importAudit.DetailJson);
        Assert.Contains("quickbooks-customers.csv", importAudit.DetailJson);

        using var journalForm = new MultipartFormDataContent();
        const string journalCsv = "Journal No.,Journal Date,Reference,Journal/Description,Account Name,Debits,Credits,Line Description,Project / Job\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,Operating Cash,25.00,0.00,Cash,\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,Product Revenue,0.00,25.00,Revenue,JOB-5007";
        journalForm.Add(new StringContent(journalCsv), "file", "quickbooks-journals.csv");
        var journalImport = await client.PostAsync("/api/interchange/quickbooks-online/journal-entries", journalForm);
        Assert.True(journalImport.StatusCode == HttpStatusCode.OK, await journalImport.Content.ReadAsStringAsync());
        var afterJournalImport = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var importedJournalDraft = Assert.Single(afterJournalImport!.GeneralLedger.RecentEntries, entry => entry.Status == "Draft" && entry.Description.Contains("QBO-JE-1", StringComparison.Ordinal));
        Assert.Equal("Draft", importedJournalDraft.Status);
        Assert.Contains(importedJournalDraft.Lines ?? [], line => line.ProjectJobNumber == "JOB-5007");
        var draftJournalExport = await client.GetStringAsync("/api/interchange/quickbooks-online/journal-entries.csv");
        Assert.DoesNotContain("QBO-JE-1", draftJournalExport);
        using var duplicateJournalForm = new MultipartFormDataContent();
        duplicateJournalForm.Add(new StringContent(journalCsv), "file", "quickbooks-journals-retry.csv");
        var duplicateJournalImport = await client.PostAsync("/api/interchange/quickbooks-online/journal-entries", duplicateJournalForm);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateJournalImport.StatusCode);
        using var invalidJournalForm = new MultipartFormDataContent();
        invalidJournalForm.Add(new StringContent("Journal No.,Journal Date,Account Name,Debits,Credits\r\nQBO-JE-BAD,2026-05-01,Operating Cash,-25.00,0.00\r\nQBO-JE-BAD,2026-05-01,Product Revenue,0.00,-25.00"), "file", "invalid-quickbooks-journals.csv");
        var invalidJournalImport = await client.PostAsync("/api/interchange/quickbooks-online/journal-entries?dryRun=true", invalidJournalForm);
        Assert.Equal(HttpStatusCode.BadRequest, invalidJournalImport.StatusCode);
        using var malformedForm = new MultipartFormDataContent();
        malformedForm.Add(new StringContent("Display Name,Customer Number\r\n\"unterminated,QBO-BAD-1"), "file", "malformed-customers.csv");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/interchange/quickbooks-online/customers?dryRun=true", malformedForm)).StatusCode);
        using var unavailableProjectForm = new MultipartFormDataContent();
        unavailableProjectForm.Add(new StringContent("Journal No.,Journal Date,Account Name,Debits,Credits,Project / Job\r\nQBO-JE-PROJECT-BAD,2026-05-01,Operating Cash,25.00,0.00,DOES-NOT-EXIST\r\nQBO-JE-PROJECT-BAD,2026-05-01,Product Revenue,0.00,25.00,DOES-NOT-EXIST"), "file", "unavailable-project-journals.csv");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/interchange/quickbooks-online/journal-entries?dryRun=true", unavailableProjectForm)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await invoiceApprover.PostAsync($"/api/journal-entry-drafts/{importedJournalDraft.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await invoicePoster.PostAsync($"/api/journal-entry-drafts/{importedJournalDraft.Id}/post", null)).StatusCode);
        var journalExport = await client.GetStringAsync("/api/interchange/quickbooks-online/journal-entries.csv");
        Assert.Contains("\"Journal No.\",\"Journal Date\",\"Reference\",\"Journal/Description\",\"Account Name\",\"Debits\",\"Credits\",\"Line Description\",\"Project / Job\"", journalExport);
        Assert.Contains("QBO-JE-1", journalExport);
        Assert.Contains("JOB-5007", journalExport);
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrassLedgerDbContext>();
            var otherCompany = new Company { Id = Guid.NewGuid(), Name = $"Other Company {Guid.NewGuid():N}", LegalName = "Other Company", BaseCurrency = "USD", FiscalYearStartMonth = 1 };
            db.Companies.Add(otherCompany);
            db.AccountingInterchangeBatches.Add(new AccountingInterchangeBatch { Id = Guid.NewGuid(), CompanyId = otherCompany.Id, ProviderCode = "quickbooks-online", EntityType = "customers", FileName = "other-company.csv", ContentSha256 = new string('a', 64), Status = "Imported", RowCount = 1, ImportedCount = 1, ProcessedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var batches = await client.GetFromJsonAsync<AccountingInterchangeBatchSnapshot[]>("/api/interchange/batches");
        Assert.Equal(7, batches!.Length);
        Assert.DoesNotContain(batches, batch => batch.FileName == "other-company.csv");
        Assert.Contains(batches, batch => batch.Status == "Validated" && batch.IsDryRun && batch.EntityType == "customers");
        Assert.Contains(batches, batch => batch.Status == "Imported" && !batch.IsDryRun && batch.ImportedCount == 1);
        Assert.Contains(batches, batch => batch.Status == "DraftsCreated" && batch.EntityType == "journal-entries" && batch.ImportedCount == 1);
        Assert.Contains(batches, batch => batch.Status == "DuplicateRejected" && batch.DuplicateCount == 2 && batch.RejectedCount == 2 && batch.Rejections.Count == 1);
        Assert.Contains(batches, batch => batch.Status == "Rejected" && batch.FileName == "invalid-quickbooks-journals.csv" && batch.RejectedCount == 2);
        Assert.Contains(batches, batch => batch.Status == "Rejected" && batch.FileName == "malformed-customers.csv" && batch.RejectedCount == 1 && batch.ContentSha256.Length == 64);
        Assert.Contains(batches, batch => batch.Status == "Rejected" && batch.FileName == "unavailable-project-journals.csv" && batch.RejectedCount == 2);

        var beforeInvoiceImport = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var receivablesBefore = beforeInvoiceImport!.Receivables.OpenBalance;
        const string invoiceCsv = "Invoice No.,Customer,Invoice Date,Due Date,Item Amount,Item Description,Quantity,Rate,Income Account,Project / Job\r\nQBO-INV-1,C-1003,2026-05-10,2026-06-09,50.00,Imported service,2,25.00,Product Revenue,JOB-5007\r\nQBO-INV-1,C-1003,2026-05-10,2026-06-09,25.00,Imported materials,1,25.00,4000,JOB-5007";
        using var invoicePreviewForm = new MultipartFormDataContent();
        invoicePreviewForm.Add(new StringContent(invoiceCsv), "file", "quickbooks-invoices.csv");
        var invoicePreview = await client.PostAsync("/api/interchange/quickbooks-online/invoices?dryRun=true", invoicePreviewForm);
        Assert.True(invoicePreview.StatusCode == HttpStatusCode.OK, await invoicePreview.Content.ReadAsStringAsync());
        var afterInvoicePreview = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.DoesNotContain(afterInvoicePreview!.Receivables.Workflows ?? [], workflow => workflow.DocumentNumber == "QBO-INV-1");
        Assert.Equal(receivablesBefore, afterInvoicePreview.Receivables.OpenBalance);

        using var invoiceImportForm = new MultipartFormDataContent();
        invoiceImportForm.Add(new StringContent(invoiceCsv), "file", "quickbooks-invoices.csv");
        var invoiceImport = await client.PostAsync("/api/interchange/quickbooks-online/invoices", invoiceImportForm);
        Assert.True(invoiceImport.StatusCode == HttpStatusCode.OK, await invoiceImport.Content.ReadAsStringAsync());
        var afterInvoiceImport = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var importedDraft = Assert.Single(afterInvoiceImport!.Receivables.Workflows ?? [], workflow => workflow.DocumentNumber == "QBO-INV-1");
        Assert.Equal("Draft", importedDraft.Status);
        Assert.Equal(receivablesBefore, afterInvoiceImport.Receivables.OpenBalance);

        Assert.Equal(HttpStatusCode.OK, (await invoiceApprover.PostAsync($"/api/subledger-document-workflows/{importedDraft.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await invoicePoster.PostAsync($"/api/subledger-document-workflows/{importedDraft.Id}/post", null)).StatusCode);
        var afterInvoicePost = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.Equal(receivablesBefore + 75m, afterInvoicePost!.Receivables.OpenBalance);
        Assert.Contains(afterInvoicePost.Receivables.Invoices, invoice => invoice.InvoiceNumber == "QBO-INV-1" && invoice.TotalAmount == 75m);
        var invoiceRoundTrip = await client.GetStringAsync("/api/interchange/quickbooks-online/invoices.csv");
        Assert.Contains("QBO-INV-1", invoiceRoundTrip);
        Assert.Contains("JOB-5007", invoiceRoundTrip);
        using var taxableInvoiceForm = new MultipartFormDataContent();
        taxableInvoiceForm.Add(new StringContent("Invoice No.,Customer,Invoice Date,Due Date,Item Amount,Tax Amount\r\nQBO-TAX-1,C-1003,2026-05-10,2026-06-09,50.00,3.00"), "file", "taxable-quickbooks-invoices.csv");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/interchange/quickbooks-online/invoices?dryRun=true", taxableInvoiceForm)).StatusCode);
        var invoiceBatches = await client.GetFromJsonAsync<AccountingInterchangeBatchSnapshot[]>("/api/interchange/batches");
        Assert.Contains(invoiceBatches!, batch => batch.EntityType == "invoices" && batch.Status == "Validated" && batch.ImportedCount == 1);
        Assert.Contains(invoiceBatches!, batch => batch.EntityType == "invoices" && batch.Status == "DraftsCreated" && batch.ImportedCount == 1);
        Assert.Contains(invoiceBatches!, batch => batch.EntityType == "invoices" && batch.Status == "Rejected" && batch.FileName == "taxable-quickbooks-invoices.csv");
        Assert.DoesNotContain((await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"))!.Receivables.Workflows ?? [], workflow => workflow.DocumentNumber == "QBO-TAX-1");

        using var unauthorizedClient = await CreateAuthenticatedClientAsync(isolatedFactory, "operations");
        Assert.Equal(HttpStatusCode.Forbidden, (await unauthorizedClient.GetAsync("/api/interchange/quickbooks-online/chart-of-accounts.csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unauthorizedClient.GetAsync("/api/interchange/batches")).StatusCode);
    }

    [Fact]
    public async Task OperationalAccountRoleApi_RequiresCombinedAuthorityAntiforgeryConfirmationAndCurrentState()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        Guid replacementId;
        Guid? expectedCurrentId;
        await using (var setupScope = isolatedFactory.Services.CreateAsyncScope())
        {
            var dbFactory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var controllerRole = await db.AccessRoles.SingleAsync(role => role.Name == "Controller");
            controllerRole.Permissions = string.Join('|', controllerRole.Permissions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(BrassLedgerPermissions.UserManage).Distinct(StringComparer.OrdinalIgnoreCase));
            var companyId = await db.Companies.Select(company => company.Id).FirstAsync();
            expectedCurrentId = await db.Accounts.Where(account => account.CompanyId == companyId && account.OperationalRole == AccountingAccountRoles.DefaultRevenue).Select(account => (Guid?)account.Id).SingleAsync();
            replacementId = Guid.NewGuid();
            db.Accounts.Add(new GeneralLedgerAccount { Id = replacementId, CompanyId = companyId, Number = "4998", Name = "API configured revenue", Type = AccountType.Revenue, IsActive = true });
            await db.SaveChangesAsync();
        }

        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var missingTokenClient = await CreateAuthenticatedClientAsync(isolatedFactory, includeAntiforgery: false);
        var workspaceResponse = await client.GetAsync("/api/accounting/operational-account-roles");
        Assert.Equal(HttpStatusCode.OK, workspaceResponse.StatusCode);
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<AccountingAccountRoleWorkspace>();
        Assert.True(workspace!.Authorized);
        Assert.Equal(expectedCurrentId, Assert.Single(workspace.Roles, role => role.Code == AccountingAccountRoles.DefaultRevenue).AccountId);

        var request = new AssignAccountingAccountRoleRequest(AccountingAccountRoles.DefaultRevenue, replacementId, expectedCurrentId, true);
        Assert.Equal(HttpStatusCode.BadRequest, (await missingTokenClient.PutAsJsonAsync("/api/accounting/operational-account-roles", request)).StatusCode);
        var antiforgery = await GetAntiforgeryTokenAsync(client);
        using var unconfirmedRequest = new HttpRequestMessage(HttpMethod.Put, "/api/accounting/operational-account-roles")
        {
            Content = JsonContent.Create(request with { ConfirmAssignment = false })
        };
        unconfirmedRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(unconfirmedRequest)).StatusCode);
        using var confirmedRequest = new HttpRequestMessage(HttpMethod.Put, "/api/accounting/operational-account-roles") { Content = JsonContent.Create(request) };
        confirmedRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(confirmedRequest)).StatusCode);
        using var staleRequest = new HttpRequestMessage(HttpMethod.Put, "/api/accounting/operational-account-roles") { Content = JsonContent.Create(request) };
        staleRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(staleRequest)).StatusCode);

        await using (var verifyScope = isolatedFactory.Services.CreateAsyncScope())
        {
            var dbFactory = verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            Assert.Equal(AccountingAccountRoles.DefaultRevenue, (await db.Accounts.SingleAsync(account => account.Id == replacementId)).OperationalRole);
            Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "accounting.operational_account_role_assigned" && audit.EntityId == replacementId);
        }

        using var operations = await CreateAuthenticatedClientAsync(isolatedFactory, "operations");
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.GetAsync("/api/accounting/operational-account-roles")).StatusCode);
    }

    [Fact]
    public async Task AccountingScheduleApi_RequiresAuthorityAntiforgeryAndPreservesReviewWorkflow()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        await EnsureControllerCloneAsync(isolatedFactory, "schedule-journal-approver-api");
        await EnsureControllerCloneAsync(isolatedFactory, "schedule-journal-poster-api");
        Guid assetId;
        Guid accumulatedId;
        Guid expenseId;
        Guid bankId;
        Guid gainId;
        Guid lossId;
        await using (var setupScope = isolatedFactory.Services.CreateAsyncScope())
        {
            var factory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var companyId = await db.Companies.Select(company => company.Id).FirstAsync();
            assetId = Guid.NewGuid(); accumulatedId = Guid.NewGuid(); expenseId = Guid.NewGuid();
            db.Accounts.AddRange(
                new GeneralLedgerAccount { Id = assetId, CompanyId = companyId, Number = "1501", Name = "API fixed assets", Type = AccountType.Asset, IsActive = true },
                new GeneralLedgerAccount { Id = accumulatedId, CompanyId = companyId, Number = "1591", Name = "API accumulated depreciation", Type = AccountType.Asset, IsActive = true },
                new GeneralLedgerAccount { Id = expenseId, CompanyId = companyId, Number = "6201", Name = "API depreciation expense", Type = AccountType.Expense, IsActive = true });
            await db.SaveChangesAsync();
            bankId = await db.BankAccounts.Where(bank => bank.CompanyId == companyId).Select(bank => bank.Id).FirstAsync();
            gainId = await db.Accounts.Where(account => account.CompanyId == companyId && account.Number == "4400").Select(account => account.Id).SingleAsync();
            lossId = await db.Accounts.Where(account => account.CompanyId == companyId && account.Number == "6500").Select(account => account.Id).SingleAsync();
        }

        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var journalApprover = await CreateAuthenticatedClientAsync(isolatedFactory, "schedule-journal-approver-api");
        using var journalPoster = await CreateAuthenticatedClientAsync(isolatedFactory, "schedule-journal-poster-api");
        using var missingTokenClient = await CreateAuthenticatedClientAsync(isolatedFactory, includeAntiforgery: false);
        var acquisitionResponse = await client.PostAsJsonAsync("/api/journal-entry-drafts", new SaveJournalEntryDraftRequest(null, new DateOnly(2026, 1, 1), "API-FA-ACQ", "Record API equipment", [new("1501", 400m, 0m, "Equipment cost"), new("3000", 0m, 400m, "Opening financing")]));
        Assert.Equal(HttpStatusCode.Created, acquisitionResponse.StatusCode);
        var acquisition = Assert.IsType<TransactionResult>(await acquisitionResponse.Content.ReadFromJsonAsync<TransactionResult>());
        Assert.Equal(HttpStatusCode.OK, (await journalApprover.PostAsync($"/api/journal-entry-drafts/{acquisition.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await journalPoster.PostAsync($"/api/journal-entry-drafts/{acquisition.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/accounting-schedules")).StatusCode);
        var save = new SaveAccountingScheduleRequest(null, "API-FA-1", "API equipment", "FixedAsset", new DateOnly(2026, 1, 31), 4, 400m, 0m, 0m, assetId, accumulatedId, expenseId, null, "API lifecycle");
        Assert.Equal(HttpStatusCode.BadRequest, (await missingTokenClient.PutAsJsonAsync("/api/accounting-schedules", save)).StatusCode);
        var token = await GetAntiforgeryTokenAsync(client);
        using var saveRequest = new HttpRequestMessage(HttpMethod.Put, "/api/accounting-schedules") { Content = JsonContent.Create(save) };
        saveRequest.Headers.Add("X-CSRF-TOKEN", token);
        var saveResponse = await client.SendAsync(saveRequest);
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        var saved = Assert.IsType<TransactionResult>(await saveResponse.Content.ReadFromJsonAsync<TransactionResult>());
        var workspace = Assert.IsType<AccountingScheduleWorkspace>(await client.GetFromJsonAsync<AccountingScheduleWorkspace>("/api/accounting-schedules"));
        var schedule = Assert.Single(workspace.Schedules, candidate => candidate.Id == saved.Id);
        Assert.Equal("Draft", schedule.Status);
        Assert.Equal(400m, schedule.Installments.Sum(installment => installment.ExpenseAmount));

        using var approveRequest = new HttpRequestMessage(HttpMethod.Post, "/api/accounting-schedules/approve") { Content = JsonContent.Create(new ApproveAccountingScheduleRequest(schedule.Id, schedule.ConcurrencyToken)) };
        approveRequest.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(approveRequest)).StatusCode);
        workspace = Assert.IsType<AccountingScheduleWorkspace>(await client.GetFromJsonAsync<AccountingScheduleWorkspace>("/api/accounting-schedules"));
        schedule = Assert.Single(workspace.Schedules, candidate => candidate.Id == saved.Id);
        using var prepareRequest = new HttpRequestMessage(HttpMethod.Post, "/api/accounting-schedules/prepare-installments") { Content = JsonContent.Create(new PrepareAccountingScheduleInstallmentsRequest(schedule.Id, schedule.StartDate, schedule.ConcurrencyToken)) };
        prepareRequest.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(prepareRequest)).StatusCode);
        workspace = Assert.IsType<AccountingScheduleWorkspace>(await client.GetFromJsonAsync<AccountingScheduleWorkspace>("/api/accounting-schedules"));
        schedule = workspace.Schedules.Single(candidate => candidate.Id == saved.Id);
        var installment = Assert.Single(schedule.Installments, candidate => candidate.JournalStatus == "Draft");
        Assert.Equal(HttpStatusCode.OK, (await journalApprover.PostAsync($"/api/journal-entry-drafts/{installment.JournalEntryId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await journalPoster.PostAsync($"/api/journal-entry-drafts/{installment.JournalEntryId}/post", null)).StatusCode);
        workspace = Assert.IsType<AccountingScheduleWorkspace>(await client.GetFromJsonAsync<AccountingScheduleWorkspace>("/api/accounting-schedules"));
        schedule = workspace.Schedules.Single(candidate => candidate.Id == saved.Id);
        var disposal = new PrepareFixedAssetDisposalRequest(schedule.Id, new DateOnly(2026, 2, 15), 400m, bankId, gainId, lossId, "API disposal", schedule.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await missingTokenClient.PostAsJsonAsync("/api/accounting-schedules/prepare-disposal", disposal)).StatusCode);
        using var disposalRequest = new HttpRequestMessage(HttpMethod.Post, "/api/accounting-schedules/prepare-disposal") { Content = JsonContent.Create(disposal) };
        disposalRequest.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(disposalRequest)).StatusCode);
        workspace = Assert.IsType<AccountingScheduleWorkspace>(await client.GetFromJsonAsync<AccountingScheduleWorkspace>("/api/accounting-schedules"));
        schedule = workspace.Schedules.Single(candidate => candidate.Id == saved.Id);
        Assert.Equal("DisposalPending", schedule.Status);
        Assert.NotNull(schedule.DisposalJournalEntryId);
        Assert.Equal(HttpStatusCode.OK, (await journalApprover.PostAsync($"/api/journal-entry-drafts/{schedule.DisposalJournalEntryId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await journalPoster.PostAsync($"/api/journal-entry-drafts/{schedule.DisposalJournalEntryId}/post", null)).StatusCode);
        workspace = Assert.IsType<AccountingScheduleWorkspace>(await client.GetFromJsonAsync<AccountingScheduleWorkspace>("/api/accounting-schedules"));
        schedule = workspace.Schedules.Single(candidate => candidate.Id == saved.Id);
        Assert.Equal("Disposed", schedule.Status);
        var reversal = new ReverseFixedAssetDisposalRequest(schedule.Id, new DateOnly(2026, 2, 16), "Correct API disposal", schedule.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await missingTokenClient.PostAsJsonAsync("/api/accounting-schedules/reverse-disposal", reversal)).StatusCode);
        using var reversalRequest = new HttpRequestMessage(HttpMethod.Post, "/api/accounting-schedules/reverse-disposal") { Content = JsonContent.Create(reversal) };
        reversalRequest.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(reversalRequest)).StatusCode);
        workspace = Assert.IsType<AccountingScheduleWorkspace>(await client.GetFromJsonAsync<AccountingScheduleWorkspace>("/api/accounting-schedules"));
        Assert.Equal("DisposalReversed", workspace.Schedules.Single(candidate => candidate.Id == saved.Id).Status);

        using var operations = await CreateAuthenticatedClientAsync(isolatedFactory, "operations");
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.GetAsync("/api/accounting-schedules")).StatusCode);
    }

    [Fact]
    public async Task QuickBooksOAuthApi_RequiresAntiforgeryAndCompletesAuditedConnectionLifecycle()
    {
        using var isolatedFactory = new BrassLedgerApiFactory(configureSecurityEmail: false, configureQuickBooks: true);
        await using (var permissionScope = isolatedFactory.Services.CreateAsyncScope())
        {
            var permissionDbFactory = permissionScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var permissionDb = await permissionDbFactory.CreateDbContextAsync();
            var controllerRole = await permissionDb.AccessRoles.SingleAsync(role => role.Name == "Controller");
            controllerRole.Permissions = string.Join('|', controllerRole.Permissions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(BrassLedgerPermissions.UserManage).Distinct(StringComparer.OrdinalIgnoreCase));
            await permissionDb.SaveChangesAsync();
        }
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var missingTokenClient = await CreateAuthenticatedClientAsync(isolatedFactory, includeAntiforgery: false);
        var missingToken = await missingTokenClient.PostAsJsonAsync("/api/integrations/quickbooks-online/connect", new BeginQuickBooksAuthorizationRequest(null, "API books", "Sandbox"));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);

        var antiforgery = await GetAntiforgeryTokenAsync(client);
        using var connectRequest = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/quickbooks-online/connect")
        {
            Content = JsonContent.Create(new BeginQuickBooksAuthorizationRequest(null, "API books", "Sandbox"))
        };
        connectRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        var connectResponse = await client.SendAsync(connectRequest);
        Assert.Equal(HttpStatusCode.OK, connectResponse.StatusCode);
        var start = await connectResponse.Content.ReadFromJsonAsync<QuickBooksAuthorizationStartResult>();
        Assert.True(start!.Succeeded);
        Assert.DoesNotContain("api-client-secret", start.AuthorizationUrl, StringComparison.Ordinal);
        var state = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl!).Query)["state"].ToString();

        var callback = await client.GetAsync($"/api/integrations/quickbooks-online/callback?state={Uri.EscapeDataString(state)}&code=api-code&realmId=24680");
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        var completion = await callback.Content.ReadFromJsonAsync<QuickBooksAuthorizationCompletionResult>();
        Assert.True(completion!.Succeeded);

        var connections = await client.GetFromJsonAsync<IntegrationConnectionSnapshot[]>("/api/integrations");
        var connected = Assert.Single(connections!, connection => connection.Id == completion.ConnectionId);
        Assert.Equal("Connected", connected.Status);
        Assert.Contains("API QuickBooks Company", connected.SettingsJson, StringComparison.Ordinal);

        isolatedFactory.QuickBooksClient.Entities["accounts"] = [new("API-A-1", "0", true, "API integration expense", "7988", string.Empty, "Expense", "OtherBusinessExpenses")];
        using var previewRequest = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/quickbooks-online/sync")
        {
            Content = JsonContent.Create(new QuickBooksSyncRequest(connected.Id, "accounts", true))
        };
        previewRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        var previewResponse = await client.SendAsync(previewRequest);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<QuickBooksSyncResult>();
        Assert.True(preview!.DryRun);
        Assert.Equal(1, preview.CreatedCount);
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/quickbooks-online/sync")
        {
            Content = JsonContent.Create(new QuickBooksSyncRequest(connected.Id, "accounts", false, preview.SnapshotSha256))
        };
        commitRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        var commitResponse = await client.SendAsync(commitRequest);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        var committed = await commitResponse.Content.ReadFromJsonAsync<QuickBooksSyncResult>();
        Assert.Equal(1, committed!.CreatedCount);
        var syncRuns = await client.GetFromJsonAsync<QuickBooksSyncRunSnapshot[]>($"/api/integrations/quickbooks-online/sync-runs?connectionId={connected.Id}");
        Assert.Equal(2, syncRuns!.Length);

        using var mappingPreviewRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/quickbooks-online/{connected.Id}/mappings/accounts/preview");
        mappingPreviewRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        var mappingPreviewResponse = await client.SendAsync(mappingPreviewRequest);
        Assert.Equal(HttpStatusCode.OK, mappingPreviewResponse.StatusCode);
        var mappingWorkspace = await mappingPreviewResponse.Content.ReadFromJsonAsync<QuickBooksMappingWorkspace>();
        Assert.True(mappingWorkspace!.Succeeded);
        var mappedRemote = Assert.Single(mappingWorkspace.RemoteCandidates);
        Assert.NotNull(mappedRemote.MappedLocalEntityId);

        using var unconfirmedRemovalRequest = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/quickbooks-online/mappings/remove")
        {
            Content = JsonContent.Create(new RemoveQuickBooksMappingRequest(connected.Id, "accounts", mappedRemote.ProviderEntityId, mappedRemote.MappedLocalEntityId!.Value))
        };
        unconfirmedRemovalRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(unconfirmedRemovalRequest)).StatusCode);
        using var removalRequest = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/quickbooks-online/mappings/remove")
        {
            Content = JsonContent.Create(new RemoveQuickBooksMappingRequest(connected.Id, "accounts", mappedRemote.ProviderEntityId, mappedRemote.MappedLocalEntityId.Value, true))
        };
        removalRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(removalRequest)).StatusCode);

        using var refreshedMappingPreviewRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/quickbooks-online/{connected.Id}/mappings/accounts/preview");
        refreshedMappingPreviewRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        var refreshedMappingPreviewResponse = await client.SendAsync(refreshedMappingPreviewRequest);
        mappingWorkspace = await refreshedMappingPreviewResponse.Content.ReadFromJsonAsync<QuickBooksMappingWorkspace>();
        var localTarget = Assert.Single(mappingWorkspace!.LocalCandidates, candidate => candidate.LocalEntityId == mappedRemote.MappedLocalEntityId.Value);
        using var saveMappingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/quickbooks-online/mappings")
        {
            Content = JsonContent.Create(new SaveQuickBooksMappingRequest(
                connected.Id, "accounts", mappingWorkspace.PreviewRunId!.Value, mappingWorkspace.SnapshotSha256,
                mappedRemote.ProviderEntityId, localTarget.LocalEntityId, null, localTarget.MappedProviderEntityId ?? string.Empty))
        };
        saveMappingRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(saveMappingRequest)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/integrations/quickbooks-online/callback?state={Uri.EscapeDataString(state)}&code=replay&realmId=24680")).StatusCode);
        using var validateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/quickbooks-online/{connected.Id}/validate");
        validateRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(validateRequest)).StatusCode);
        using var disconnectRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/quickbooks-online/{connected.Id}/disconnect");
        disconnectRequest.Headers.Add("X-CSRF-TOKEN", antiforgery);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(disconnectRequest)).StatusCode);
        Assert.Equal("api-refresh-token", isolatedFactory.QuickBooksClient.LastRevokedToken);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.IntegrationConnections.SingleAsync(connection => connection.Id == connected.Id);
        Assert.Equal("Disconnected", stored.Status);
        Assert.Equal("{}", stored.CredentialsJson);
        Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.connected" && audit.EntityId == connected.Id);
        Assert.Contains(await db.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "integration.disconnected" && audit.EntityId == connected.Id);
    }

    [Fact]
    public async Task ProjectApi_ProvidesControlledMaintenanceCloseAndReopenWorkflow()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var missingToken = await CreateAuthenticatedClientAsync(isolatedFactory, includeAntiforgery: false);
        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var customerId = workspace!.Receivables.Customers.First().Id;
        var createRequest = new SaveProjectJobRequest(null, "JOB-API-PROJECT", "API project", customerId, new DateOnly(2026, 8, 26), new DateOnly(2027, 2, 28), "TimeAndMaterials", 25_000m, 18_000m, 0.05m);
        Assert.Equal(HttpStatusCode.BadRequest, (await missingToken.PostAsJsonAsync("/api/projects", createRequest)).StatusCode);
        var createdResponse = await client.PostAsJsonAsync("/api/projects", createRequest);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var project = Assert.Single((await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects"))!.Jobs, candidate => candidate.Id == created!.Id);
        Assert.Equal("Active", project.Status);
        Assert.Equal(18_000m, project.BudgetAmount);

        var updateRequest = new SaveProjectJobRequest(project.Id, project.JobNumber, "API project revised", customerId, project.StartDate!.Value, project.ExpectedEndDate, "FixedPrice", 26_000m, 19_000m, 0.1m, project.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/projects/{Guid.NewGuid()}", updateRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/projects/{project.Id}", updateRequest)).StatusCode);
        project = Assert.Single((await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects"))!.Jobs, candidate => candidate.Id == project.Id);
        Assert.Equal("FixedPrice", project.BillingMethod);

        var phaseRequest = new SaveProjectPhaseRequest(null, project.Id, null, "API.01", "API delivery", "Phase", "Controlled API phase", new DateOnly(2026, 8, 26), new DateOnly(2026, 12, 31));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/phases", phaseRequest)).StatusCode);
        var phaseResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/phases", phaseRequest);
        Assert.Equal(HttpStatusCode.Created, phaseResponse.StatusCode);
        var phaseResult = await phaseResponse.Content.ReadFromJsonAsync<TransactionResult>();

        var costCodeRequest = new SaveProjectCostCodeRequest(null, "API-LAB", "API labor", "Direct cost", "Controlled API cost code");
        var costCodeResponse = await client.PostAsJsonAsync("/api/project-cost-codes", costCodeRequest);
        Assert.Equal(HttpStatusCode.Created, costCodeResponse.StatusCode);
        var costCodeResult = await costCodeResponse.Content.ReadFromJsonAsync<TransactionResult>();

        var allocationRequest = new SaveProjectBudgetAllocationRequest(null, project.Id, phaseResult!.Id, costCodeResult!.Id, "5100", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 1_000m, 1_200m, "API delivery forecast");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/budget-allocations", allocationRequest)).StatusCode);
        var allocationResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/budget-allocations", allocationRequest);
        Assert.Equal(HttpStatusCode.Created, allocationResponse.StatusCode);
        var allocationResult = await allocationResponse.Content.ReadFromJsonAsync<TransactionResult>();

        var dimensionWorkspace = await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects");
        Assert.Contains(dimensionWorkspace!.Phases!, candidate => candidate.Id == phaseResult.Id && candidate.ProjectJobId == project.Id);
        Assert.Contains(dimensionWorkspace.CostCodes!, candidate => candidate.Id == costCodeResult.Id && candidate.Code == "API-LAB");
        Assert.Contains(dimensionWorkspace.BudgetAllocations!, candidate => candidate.Id == allocationResult!.Id && candidate.ProjectPhaseId == phaseResult.Id && candidate.ProjectCostCodeId == costCodeResult.Id);

        var changeRequest = new SaveProjectChangeOrderDraftRequest(null, project.Id, "CO-API-001", "API-approved scope", "Customer approved added integration work", new DateOnly(2026, 8, 27), new DateOnly(2026, 9, 1), 1_500m, 900m);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/change-orders", changeRequest)).StatusCode);
        var changeResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/change-orders", changeRequest);
        Assert.Equal(HttpStatusCode.Created, changeResponse.StatusCode);
        var changeResult = await changeResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var changeOrder = Assert.Single((await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects"))!.ChangeOrders!, candidate => candidate.Id == changeResult!.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/project-change-orders/{changeOrder.Id}/submission", new SubmitProjectChangeOrderRequest(changeOrder.Id, changeOrder.ConcurrencyToken))).StatusCode);
        changeOrder = Assert.Single((await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects"))!.ChangeOrders!, candidate => candidate.Id == changeOrder.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/project-change-orders/{changeOrder.Id}/decision", new DecideProjectChangeOrderRequest(changeOrder.Id, true, "Self approval is forbidden", changeOrder.ConcurrencyToken))).StatusCode);
        await EnsureControllerCloneAsync(isolatedFactory, "project-approver");
        using var approver = await CreateAuthenticatedClientAsync(isolatedFactory, "project-approver");
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsJsonAsync($"/api/project-change-orders/{changeOrder.Id}/decision", new DecideProjectChangeOrderRequest(changeOrder.Id, true, "Scope authorization independently verified", changeOrder.ConcurrencyToken))).StatusCode);
        project = Assert.Single((await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects"))!.Jobs, candidate => candidate.Id == project.Id);
        Assert.Equal(27_500m, project.ContractAmount);
        Assert.Equal(19_900m, project.BudgetAmount);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{project.Id}/close", new CloseProjectJobRequest(project.Id, new DateOnly(2026, 8, 31), "Stale close", "stale"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/projects/{project.Id}/close", new CloseProjectJobRequest(project.Id, new DateOnly(2026, 8, 31), "Project work completed", project.ConcurrencyToken))).StatusCode);
        project = Assert.Single((await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects"))!.Jobs, candidate => candidate.Id == project.Id);
        Assert.Equal("Closed", project.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/reopen", new ReopenProjectJobRequest(project.Id, "Mismatched route", project.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/projects/{project.Id}/reopen", new ReopenProjectJobRequest(project.Id, "Approved follow-up scope", project.ConcurrencyToken))).StatusCode);
        project = Assert.Single((await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects"))!.Jobs, candidate => candidate.Id == project.Id);
        Assert.Equal("Active", project.Status);
        var billingRequest = new ProjectBillingPreviewRequest(project.Id, "PB-API-001", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 30), "4000", "API milestone billing", MilestoneAmount: 1_000m, IncludeLabor: false, IncludeCosts: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/billing-preview", billingRequest)).StatusCode);
        var previewResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/billing-preview", billingRequest);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<ProjectBillingPreview>();
        Assert.True(preview!.Succeeded); Assert.Equal(1_000m, preview.GrossAmount);
        var saveBillingRequest = new SaveProjectBillingProposalRequest(null, billingRequest, preview.Fingerprint, preview.ProjectConcurrencyToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/billing-proposals", saveBillingRequest)).StatusCode);
        var billingResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/billing-proposals", saveBillingRequest);
        Assert.Equal(HttpStatusCode.Created, billingResponse.StatusCode);
        var billingResult = await billingResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var billing = Assert.Single((await client.GetFromJsonAsync<ProjectsWorkspace>("/api/projects"))!.BillingProposals!, candidate => candidate.Id == billingResult!.Id);
        Assert.Equal("Draft", billing.Status); Assert.Equal("FixedPriceMilestone", billing.BillingBasis);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/project-billing-proposals/{Guid.NewGuid()}/cancellation", new CancelProjectBillingProposalRequest(billing.Id, "Mismatched route", billing.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/project-billing-proposals/{billing.Id}/cancellation", new CancelProjectBillingProposalRequest(billing.Id, "Customer deferred milestone billing", billing.ConcurrencyToken))).StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(WebApplicationFactory<Program>? factory = null, string userName = "controller", bool includeAntiforgery = true)
    {
        var testFactory = factory ?? _factory;
        var client = testFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = userName,
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (includeAntiforgery) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        return client;
    }

    private static async Task EnsureControllerCloneAsync(BrassLedgerApiFactory factory, string userName, string sourceUserName = "controller")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Users.AnyAsync(user => user.UserName == userName)) return;
        var source = await db.Users.SingleAsync(user => user.UserName == sourceUserName);
        var clone = new AppUser
        {
            Id = Guid.NewGuid(),
            CompanyId = source.CompanyId,
            UserName = userName,
            DisplayName = userName,
            Email = $"{userName}@example.test",
            EmailConfirmedAtUtc = DateTimeOffset.UtcNow,
            PasswordHash = source.PasswordHash,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = source.Role,
            IsActive = true,
            LastPasswordChangedUtc = DateTimeOffset.UtcNow
        };
        db.Users.Add(clone);
        db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Guid.NewGuid(),
            UserId = clone.Id,
            CompanyId = clone.CompanyId,
            Role = clone.Role,
            IsActive = true,
            GrantedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<TransactionResult> PostInvoiceThroughWorkflowAsync(IServiceProvider services, CreateInvoiceRequest request)
    {
        await using var scope = services.CreateAsyncScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var draft = await transactions.SaveInvoiceDraftAsync(request);
        if (!draft.Succeeded) return draft;
        var approval = await transactions.ApproveSubledgerDocumentAsync(draft.Id!.Value);
        return approval.Succeeded ? await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value) : approval;
    }

    private static async Task<TransactionResult> PostVendorBillThroughWorkflowAsync(IServiceProvider services, CreateVendorBillRequest request)
    {
        await using var scope = services.CreateAsyncScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var draft = await transactions.SaveVendorBillDraftAsync(request);
        if (!draft.Succeeded) return draft;
        var approval = await transactions.ApproveSubledgerDocumentAsync(draft.Id!.Value);
        return approval.Succeeded ? await transactions.PostApprovedSubledgerDocumentAsync(draft.Id.Value) : approval;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/antiforgery/token");
        return token.GetProperty("requestToken").GetString()!;
    }

    private static async Task<IReadOnlyList<string>> EnrollMfaAsync(BrassLedgerApiFactory factory, string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var signedIn = await authentication.AuthenticateAsync(
            userName,
            BrassLedgerAuthenticationDefaults.SeededPassword,
            "127.0.0.1",
            "api-mfa-setup");
        Assert.Equal(AuthenticationOutcome.Succeeded, signedIn.Outcome);
        var enrollment = await authentication.BeginMfaEnrollmentAsync(
            signedIn.User!.UserId,
            signedIn.User.CompanyId,
            BrassLedgerAuthenticationDefaults.SeededPassword,
            "127.0.0.1",
            "api-mfa-setup");
        Assert.Equal(MfaOperationOutcome.Succeeded, enrollment.Outcome);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / TotpService.TimeStepSeconds;
        var code = TotpService.ComputeCode(TotpService.DecodeBase32(enrollment.Secret), step);
        var enabled = await authentication.EnableMfaAsync(
            signedIn.User.UserId,
            signedIn.User.CompanyId,
            code,
            "127.0.0.1",
            "api-mfa-setup");
        Assert.Equal(MfaOperationOutcome.Succeeded, enabled.Outcome);
        return enrollment.RecoveryCodes!;
    }

    private static async Task DispatchAllSecurityEmailAsync(BrassLedgerApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
        while (await dispatcher.DispatchNextAsync()) { }
    }

    private static string ExtractAccountActionToken(string body)
    {
        var match = Regex.Match(body, @"https://\S+", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return QueryHelpers.ParseQuery(new Uri(match.Value.Trim()).Query)["token"].ToString();
    }
}

public sealed class BrassLedgerApiFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.Api.Tests", Guid.NewGuid().ToString("N"));
    private readonly bool _configureSecurityEmail;
    private readonly bool _configureQuickBooks;

    public BrassLedgerApiFactory() : this(false, false)
    {
    }

    internal BrassLedgerApiFactory(bool configureSecurityEmail) : this(configureSecurityEmail, false)
    {
    }

    internal BrassLedgerApiFactory(bool configureSecurityEmail, bool configureQuickBooks)
    {
        _configureSecurityEmail = configureSecurityEmail;
        _configureQuickBooks = configureQuickBooks;
    }

    public RecordingSecurityEmailTransport SecurityEmailTransport { get; } = new();
    public RecordingQuickBooksOnlineClient QuickBooksClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRootPath);

        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, _contentRootPath);
        if (_configureSecurityEmail)
        {
            builder.UseSetting("AccountEmail:Enabled", "true");
            builder.UseSetting("AccountEmail:PublicBaseUrl", "https://ledger.example.test");
            builder.UseSetting("AccountEmail:Host", "smtp.example.test");
            builder.UseSetting("AccountEmail:FromAddress", "security@example.test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISecurityEmailTransport>();
                services.AddSingleton<ISecurityEmailTransport>(SecurityEmailTransport);
            });
        }
        if (_configureQuickBooks)
        {
            builder.UseSetting("QuickBooksOnline:Enabled", "true");
            builder.UseSetting("QuickBooksOnline:Environment", "Sandbox");
            builder.UseSetting("QuickBooksOnline:ClientId", "api-client");
            builder.UseSetting("QuickBooksOnline:ClientSecret", "api-client-secret");
            builder.UseSetting("QuickBooksOnline:RedirectUri", "http://127.0.0.1:5099/api/integrations/quickbooks-online/callback");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQuickBooksOnlineClient>();
                services.AddSingleton<IQuickBooksOnlineClient>(QuickBooksClient);
            });
        }
    }

    public new void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(_contentRootPath))
        {
            try
            {
                Directory.Delete(_contentRootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public sealed class RecordingSecurityEmailTransport : ISecurityEmailTransport
    {
        public bool IsConfigured => true;
        public List<RecordedSecurityEmail> Messages { get; } = [];

        public Task<string> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
        {
            Messages.Add(new RecordedSecurityEmail(recipient, subject, body));
            return Task.FromResult($"<{Guid.NewGuid():N}@example.test>");
        }
    }

    public sealed class RecordingQuickBooksOnlineClient : IQuickBooksOnlineClient
    {
        public string LastRevokedToken { get; private set; } = string.Empty;
        public Dictionary<string, List<QuickBooksRemoteEntity>> Entities { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string BuildAuthorizationUrl(string state) => QueryHelpers.AddQueryString("https://appcenter.intuit.com/connect/oauth2", new Dictionary<string, string?>
        {
            ["client_id"] = "api-client",
            ["response_type"] = "code",
            ["scope"] = "com.intuit.quickbooks.accounting",
            ["redirect_uri"] = "http://127.0.0.1:5099/api/integrations/quickbooks-online/callback",
            ["state"] = state
        });

        public Task<QuickBooksTokenResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickBooksTokenResponse(true, string.Empty, "api-access-token", "api-refresh-token", "bearer", "com.intuit.quickbooks.accounting", 3600, 8_726_400));

        public Task<QuickBooksTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickBooksTokenResponse(true, string.Empty, "api-access-token-two", "api-refresh-token-two", "bearer", "com.intuit.quickbooks.accounting", 3600, 8_726_400));

        public Task<QuickBooksProviderResult> RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            LastRevokedToken = refreshToken;
            return Task.FromResult(new QuickBooksProviderResult(true, string.Empty));
        }

        public Task<QuickBooksCompanyInfoResponse> GetCompanyInfoAsync(string environment, string realmId, string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickBooksCompanyInfoResponse(true, string.Empty, "API QuickBooks Company", "API QuickBooks Company LLC", "US"));

        public Task<QuickBooksEntityQueryResponse> QueryEntitiesAsync(string environment, string realmId, string accessToken, string entityType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickBooksEntityQueryResponse(true, string.Empty, Entities.GetValueOrDefault(entityType, [])));
    }

    public sealed record RecordedSecurityEmail(string Recipient, string Subject, string Body);
}
