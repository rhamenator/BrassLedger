using BrassLedger.Application.Accounting;
using BrassLedger.Application.Catalog;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddBrassLedgerCookieAuthentication();
builder.Services.AddBrassLedgerInfrastructure(builder.Configuration, builder.Environment.ContentRootPath, builder.Environment.IsDevelopment());

var app = builder.Build();
await app.Services.InitializeBrassLedgerAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseBrassLedgerSecurityHeaders();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAntiforgery();
app.UseAuthorization();
app.MapBrassLedgerAuthenticationEndpoints();

var api = app.MapGroup("/api").RequireAuthorization(BrassLedgerAuthorizationPolicies.ViewWorkspace);

api.MapGet("/antiforgery/token", (Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken });
});

api.MapGet("/assessment", (IProductCatalogService service) =>
{
    return Results.Ok(service.GetCatalog());
})
.WithName("GetProductCatalog")
.WithOpenApi();

api.MapGet("/modules", (IProductCatalogService service) =>
{
    return Results.Ok(service.GetCatalog().Modules);
})
.WithName("GetLegacyModules")
.WithOpenApi();

api.MapGet("/tax-sources", (IProductCatalogService service) =>
{
    return Results.Ok(service.GetCatalog().TaxSources);
})
.WithName("GetTaxSources")
.WithOpenApi();

api.MapGet("/workspace", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetWorkspaceAsync(cancellationToken));
})
.WithName("GetBusinessWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ViewWorkspace);

api.MapGet("/dashboard", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Dashboard);
})
.WithName("GetDashboard")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ViewWorkspace);

api.MapGet("/general-ledger", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).GeneralLedger);
})
.WithName("GetGeneralLedgerWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapGet("/receivables", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Receivables);
})
.WithName("GetReceivablesWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReceivables);

api.MapGet("/payables", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Payables);
})
.WithName("GetPayablesWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayables);

api.MapGet("/operations", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Operations);
})
.WithName("GetOperationsWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageOperations);

api.MapGet("/payroll", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Payroll);
})
.WithName("GetPayrollWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

api.MapGet("/projects", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Projects);
})
.WithName("GetProjectsWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageProjects);

api.MapGet("/reporting-catalog", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Reporting);
})
.WithName("GetReportingWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReporting);

api.MapGet("/tax-workspace", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Taxes);
})
.WithName("GetTaxWorkspace")
.WithOpenApi()
.RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageTaxes);

api.MapPost("/journal-entries", async (PostJournalEntryRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PostJournalEntryAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/journal-entries/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PostJournals);

api.MapPost("/journal-entry-drafts", async (SaveJournalEntryDraftRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveJournalEntryDraftAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/journal-entry-drafts/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PrepareJournals);

api.MapPost("/journal-entry-drafts/{journalEntryId:guid}/approve", async (Guid journalEntryId, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ApproveJournalEntryAsync(journalEntryId, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ApproveJournals);

api.MapPost("/journal-entry-drafts/{journalEntryId:guid}/post", async (Guid journalEntryId, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PostApprovedJournalEntryAsync(journalEntryId, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PostJournals);

api.MapPost("/journal-entries/reverse", async (ReverseJournalEntryRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReverseJournalEntryAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/journal-entries/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger, BrassLedgerAuthorizationPolicies.ReverseJournals);

api.MapPost("/invoices", async (CreateInvoiceRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.CreateInvoiceAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/invoices/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["invoice"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReceivables);

api.MapPost("/vendor-bills", async (CreateVendorBillRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.CreateVendorBillAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/vendor-bills/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["bill"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayables);

api.MapPost("/invoice-drafts", async (CreateInvoiceRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveInvoiceDraftAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-document-workflows/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["draft"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PrepareSubledgerDocuments);

api.MapPost("/vendor-bill-drafts", async (CreateVendorBillRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveVendorBillDraftAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-document-workflows/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["draft"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PrepareSubledgerDocuments);

api.MapPost("/subledger-document-workflows/{workflowId:guid}/approve", async (Guid workflowId, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ApproveSubledgerDocumentAsync(workflowId, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ApproveSubledgerDocuments);

api.MapPost("/subledger-document-workflows/{workflowId:guid}/post", async (Guid workflowId, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PostApprovedSubledgerDocumentAsync(workflowId, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PostSubledgerDocuments);

api.MapPost("/recurring-invoice-templates", async (SaveRecurringInvoiceTemplateRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveRecurringInvoiceTemplateAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-document-workflows/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["template"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PrepareSubledgerDocuments);

api.MapPost("/recurring-vendor-bill-templates", async (SaveRecurringVendorBillTemplateRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveRecurringVendorBillTemplateAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-document-workflows/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["template"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PrepareSubledgerDocuments);

api.MapPost("/recurring-subledger-documents/generate", async (DateOnly throughDate, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.GenerateDueRecurringDocumentsAsync(throughDate, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PrepareSubledgerDocuments);

api.MapPost("/invoices/payments", async (ApplyInvoicePaymentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ApplyInvoicePaymentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReceivables);

api.MapPost("/vendor-bills/payments", async (ApplyBillPaymentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ApplyBillPaymentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayables);

api.MapPost("/customer-payments", async (RecordCustomerPaymentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.RecordCustomerPaymentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/customer-payments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReceivables);

api.MapPost("/vendor-payments", async (RecordVendorPaymentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.RecordVendorPaymentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/vendor-payments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayables);

api.MapPost("/subledger-payments/reverse", async (ReverseSubledgerPaymentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReverseSubledgerPaymentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayments);

api.MapPost("/customer-adjustments", async (RecordCustomerAdjustmentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.RecordCustomerAdjustmentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-adjustments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["adjustment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReceivables);

api.MapPost("/vendor-credits", async (RecordVendorCreditRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.RecordVendorCreditAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-adjustments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["adjustment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayables);

api.MapPost("/subledger-payments/refund-unapplied", async (RefundUnappliedPaymentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.RefundUnappliedPaymentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-adjustments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["refund"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayments);

api.MapPost("/invoices/void", async (VoidSubledgerDocumentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.VoidInvoiceAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-adjustments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["void"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayments);

api.MapPost("/vendor-bills/void", async (VoidSubledgerDocumentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.VoidVendorBillAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/subledger-adjustments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["void"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayments);

api.MapPost("/subledger-adjustments/reverse", async (ReverseSubledgerAdjustmentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReverseSubledgerAdjustmentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["adjustment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayments);

api.MapPost("/bank-reconciliations", async (ReconcileBankAccountRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReconcileBankAccountAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["reconciliation"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/bank-statements/import", async (ImportBankStatementRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ImportBankStatementAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["statement"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/bank-transactions/match", async (MatchBankTransactionRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.MatchBankTransactionAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["match"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/bank-transactions/{transactionId:guid}/unmatch", async (Guid transactionId, string reason, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.UnmatchBankTransactionAsync(transactionId, reason, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/bank-transfers", async (CreateBankTransferRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.CreateBankTransferAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/bank-transfers/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["transfer"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/bank-transfers/reverse", async (ReverseBankTransferRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReverseBankTransferAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["transfer"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger, BrassLedgerAuthorizationPolicies.ReverseJournals);

api.MapPost("/bank-reconciliation-adjustments", async (CreateReconciliationAdjustmentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.CreateReconciliationAdjustmentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/journal-entries/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["adjustment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/bank-reconciliation-adjustments/reverse", async (ReverseReconciliationAdjustmentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReverseReconciliationAdjustmentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["adjustment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger, BrassLedgerAuthorizationPolicies.ReverseJournals);

api.MapPost("/bank-reconciliations/reopen", async (ReopenBankReconciliationRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReopenBankReconciliationAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["reconciliation"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger, BrassLedgerAuthorizationPolicies.ReverseJournals);

api.MapGet("/bank-accounts/{bankAccountId:guid}/reconciliation-candidates", async (Guid bankAccountId, IDbContextFactory<BrassLedgerDbContext> dbContextFactory, HttpContext context, CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var companyClaim = context.User.FindFirst(BrassLedgerAuthenticationDefaults.CompanyIdClaimType)?.Value;
    if (!Guid.TryParse(companyClaim, out var companyId)) return Results.Forbid();
    var bank = await db.BankAccounts.SingleOrDefaultAsync(account => account.Id == bankAccountId && account.CompanyId == companyId, cancellationToken);
    if (bank is null) return Results.NotFound();
    var candidates = await db.JournalEntries.Where(entry => entry.CompanyId == companyId && entry.BankAccountId == bankAccountId && entry.PostedOn > bank.LastReconciledOn)
        .OrderBy(entry => entry.PostedOn).Select(entry => new { entry.Id, entry.PostedOn, entry.Reference, entry.Description, entry.TotalAmount, entry.SourceModule }).ToListAsync(cancellationToken);
    var candidateIds = candidates.Select(entry => entry.Id).ToArray();
    var signedAmounts = await db.JournalEntryLines.Where(line => candidateIds.Contains(line.JournalEntryId) && line.AccountId == bank.LedgerAccountId)
        .GroupBy(line => line.JournalEntryId).Select(group => new { JournalEntryId = group.Key, SignedAmount = group.Sum(line => line.Debit - line.Credit) }).ToDictionaryAsync(item => item.JournalEntryId, item => item.SignedAmount, cancellationToken);
    return Results.Ok(candidates.Select(entry => new { entry.Id, entry.PostedOn, entry.Reference, entry.Description, entry.TotalAmount, entry.SourceModule, SignedAmount = signedAmounts.GetValueOrDefault(entry.Id) }));
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPut("/bank-accounts/ledger-mapping", async (UpdateBankLedgerMappingRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.UpdateBankLedgerMappingAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["bankAccount"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/payroll-runs", async (PostPayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PostPayrollRunAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-runs/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PreparePayroll, BrassLedgerAuthorizationPolicies.ApprovePayroll, BrassLedgerAuthorizationPolicies.PostPayroll);

api.MapPost("/payroll-runs/employee-preview", async (PostEmployeePayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PreviewEmployeePayrollRunAsync(request, cancellationToken);
    return result is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = ["Provide active employees, positive gross pay, and applicable tax profiles."] }) : Results.Ok(result);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PreparePayroll);

api.MapPost("/payroll-runs/employee", async (PostEmployeePayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PostEmployeePayrollRunAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-runs/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PreparePayroll, BrassLedgerAuthorizationPolicies.ApprovePayroll, BrassLedgerAuthorizationPolicies.PostPayroll);

api.MapPost("/payroll-runs/drafts", async (PostEmployeePayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveEmployeePayrollRunDraftAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-runs/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PreparePayroll);

api.MapPost("/payroll-runs/approve", async (ApprovePayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ApprovePayrollRunAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ApprovePayroll);

api.MapPost("/payroll-runs/post", async (PostApprovedPayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PostApprovedPayrollRunAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PostPayroll);

api.MapPost("/payroll-runs/cancel", async (CancelPayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.CancelPayrollRunAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayroll);

api.MapPost("/payroll-runs/reverse", async (ReversePayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReversePayrollRunAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayroll);

api.MapPost("/payroll-timecards/drafts", async (SavePayrollTimecardDraftRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SavePayrollTimecardDraftAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-timecards/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["timecard"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PreparePayroll);

api.MapPost("/payroll-timecards/submit", async (SubmitPayrollTimecardRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SubmitPayrollTimecardAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["timecard"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PreparePayroll);

api.MapPost("/payroll-timecards/approve", async (ApprovePayrollTimecardRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ApprovePayrollTimecardAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["timecard"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ApprovePayroll);

api.MapPost("/payroll-timecards/void", async (VoidPayrollTimecardRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.VoidPayrollTimecardAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["timecard"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayroll);

api.MapPost("/payroll-liability-payments", async (RecordPayrollLiabilityPaymentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.RecordPayrollLiabilityPaymentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-liability-payments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payrollLiabilityPayment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PostPayroll);

api.MapPost("/payroll-liability-payments/reverse", async (ReversePayrollLiabilityPaymentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReversePayrollLiabilityPaymentAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payrollLiabilityPayment"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayroll);

api.MapGet("/payroll-runs/{payrollRunId:guid}/register", async (Guid payrollRunId, IPayrollReportingService service, CancellationToken cancellationToken) =>
{
    var report = await service.GetRegisterAsync(payrollRunId, cancellationToken);
    return report is null ? Results.NotFound() : Results.Ok(report);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.AccessPayroll);

api.MapGet("/payroll-runs/{payrollRunId:guid}/register.csv", async (Guid payrollRunId, IPayrollReportingService service, CancellationToken cancellationToken) =>
{
    var csv = await service.ExportRegisterCsvAsync(payrollRunId, cancellationToken);
    return csv is null ? Results.NotFound() : Results.Text(csv, "text/csv; charset=utf-8");
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.AccessPayroll);

api.MapGet("/payroll-runs/{payrollRunId:guid}/employees/{employeeId:guid}/pay-statement", async (Guid payrollRunId, Guid employeeId, IPayrollReportingService service, CancellationToken cancellationToken) =>
{
    var statement = await service.GetPayStatementAsync(payrollRunId, employeeId, cancellationToken);
    return statement is null ? Results.NotFound() : Results.Ok(statement);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

api.MapGet("/payroll-filings", async (IPayrollFilingService service, CancellationToken cancellationToken) => Results.Ok(await service.GetFilingsAsync(cancellationToken)))
    .RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);
api.MapGet("/payroll-filings/{filingId:guid}", async (Guid filingId, IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var filing = await service.GetFilingAsync(filingId, cancellationToken);
    return filing is null ? Results.NotFound() : Results.Ok(filing);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);
api.MapGet("/payroll-filings/{filingId:guid}/data.json", async (Guid filingId, IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var filing = await service.GetFilingAsync(filingId, cancellationToken);
    return filing is null ? Results.NotFound() : Results.File(System.Text.Encoding.UTF8.GetBytes(filing.Data.GetRawText()), "application/json", $"payroll-{filing.FormCode.ToLowerInvariant()}-{filing.TaxYear}{(filing.Quarter.HasValue ? $"-q{filing.Quarter}" : string.Empty)}.json");
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);
api.MapPost("/payroll-filings/drafts", async (SavePayrollFilingDraftRequest request, IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveDraftAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-filings/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payrollFiling"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PreparePayroll, BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);
api.MapPost("/payroll-filings/approve", async (ApprovePayrollFilingRequest request, IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var result = await service.ApproveAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payrollFiling"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ApprovePayroll, BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);
api.MapPost("/payroll-filings/reopen", async (ReopenPayrollFilingRequest request, IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReopenFilingAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payrollFiling"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayroll);
api.MapGet("/payroll-close-periods", async (IPayrollFilingService service, CancellationToken cancellationToken) => Results.Ok(await service.GetClosePeriodsAsync(cancellationToken)))
    .RequireAuthorization(BrassLedgerAuthorizationPolicies.AccessPayroll);
api.MapPost("/payroll-close-periods", async (ClosePayrollPeriodRequest request, IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var result = await service.ClosePeriodAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-close-periods/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payrollClosePeriod"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PostPayroll);
api.MapPost("/payroll-close-periods/reopen", async (ReopenPayrollPeriodRequest request, IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReopenPeriodAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payrollClosePeriod"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ReversePayroll);

api.MapPut("/employees/payroll-setup", async (SaveEmployeePayrollSetupRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveEmployeePayrollSetupAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["employee"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll, BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

api.MapPut("/employees/protected-payroll-details", async (SaveEmployeeEmploymentDetailsRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveEmployeeEmploymentDetailsAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["employee"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

api.MapPut("/payroll-jurisdiction-rules", async (SavePayrollJurisdictionRuleRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SavePayrollJurisdictionRuleAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["jurisdictionRule"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

api.MapGet("/payroll-deposit-schedules", async (IPayrollDepositScheduleService service, CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(cancellationToken)))
    .RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

api.MapPut("/payroll-deposit-schedules", async (SavePayrollDepositScheduleRequest request, IPayrollDepositScheduleService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["depositSchedule"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll, BrassLedgerAuthorizationPolicies.ApprovePayroll);

api.MapGet("/payroll-deduction-configuration", async (IPayrollDeductionConfigurationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(cancellationToken)))
    .RequireAuthorization(BrassLedgerAuthorizationPolicies.MaintainEmployeePayrollSetup);

api.MapPut("/payroll-deduction-plans", async (SavePayrollDeductionPlanRequest request, IPayrollDeductionConfigurationService service, CancellationToken cancellationToken) =>
{
    var result = await service.SavePlanAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["deductionPlan"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

api.MapPut("/employee-payroll-deduction-elections", async (SaveEmployeePayrollDeductionElectionRequest request, IPayrollDeductionConfigurationService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveElectionAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["deductionElection"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.MaintainEmployeePayrollSetup);

api.MapGet("/payroll-payment-files", async (IPayrollPaymentFileService service, CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(cancellationToken)))
    .RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

api.MapPut("/payroll-bank-origins", async (SavePayrollBankOriginConfigurationRequest request, IPayrollPaymentFileService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveBankOriginAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["bankOrigin"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.MaintainEmployeePayrollSetup);

api.MapPost("/payroll-payment-files", async (GeneratePayrollPaymentFileRequest request, IPayrollPaymentFileService service, CancellationToken cancellationToken) =>
{
    var result = await service.GenerateAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-payment-files/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["paymentFile"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.PostPayroll, BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

api.MapGet("/payroll-payment-files/{paymentFileId:guid}/download", async (Guid paymentFileId, IPayrollPaymentFileService service, CancellationToken cancellationToken) =>
{
    var file = await service.DownloadAsync(paymentFileId, cancellationToken);
    return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

api.MapGet("/companies", async (ICompanyManagementService service, CancellationToken cancellationToken) => Results.Ok(await service.GetMyCompaniesAsync(cancellationToken))).RequireAuthorization();
api.MapPost("/companies", async (CreateCompanyRequest request, ICompanyManagementService service, CancellationToken cancellationToken) =>
{
    var result = await service.CreateCompanyAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/companies/{result.CompanyId}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["company"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageUsers);

api.MapPut("/exchange-rates", async (SaveExchangeRateRequest request, IConsolidationService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveExchangeRateAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["exchangeRate"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReporting);
api.MapPut("/consolidation-groups", async (SaveConsolidationGroupRequest request, IConsolidationService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveGroupAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["consolidation"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReporting);
api.MapGet("/consolidation-groups", async (IConsolidationService service, CancellationToken cancellationToken) => Results.Ok(await service.GetGroupsAsync(cancellationToken))).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReporting);
api.MapGet("/consolidation-groups/{groupId:guid}/balances", async (Guid groupId, DateOnly? asOf, IConsolidationService service, CancellationToken cancellationToken) =>
{
    var report = await service.GetBalanceReportAsync(groupId, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    return report is null ? Results.NotFound() : Results.Ok(report);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReporting);
api.MapPut("/accounting-periods", async (SaveAccountingPeriodRequest request, IAccountingPeriodService service, CancellationToken cancellationToken) =>
{
    var result = await service.SavePeriodAsync(request, cancellationToken); return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);
api.MapGet("/accounting-controls", async (int? auditEntryLimit, IAccountingPeriodService service, CancellationToken cancellationToken) => Results.Ok(await service.GetSnapshotAsync(auditEntryLimit ?? 100, cancellationToken))).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);
api.MapPost("/accounting-periods/{periodId:guid}/status", async (Guid periodId, bool close, string? notes, IAccountingPeriodService service, CancellationToken cancellationToken) =>
{
    var result = await service.SetPeriodStatusAsync(periodId, close, notes ?? string.Empty, cancellationToken); return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);
api.MapPost("/backups", async (IBackupService service, CancellationToken cancellationToken) =>
{
    var result = await service.CreateBackupAsync(cancellationToken); return result.Succeeded ? Results.Created($"/api/backups/{result.BackupId}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["backup"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageUsers);
api.MapPost("/backups/{backupId}/verify", async (string backupId, IBackupService service, CancellationToken cancellationToken) =>
{
    var result = await service.VerifyBackupAsync(backupId, cancellationToken); return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["backup"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageUsers);
api.MapPost("/backups/{backupId}/rehearse-restore", async (string backupId, IBackupService service, CancellationToken cancellationToken) =>
{
    var result = await service.RehearseRestoreAsync(backupId, cancellationToken); return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["backup"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageUsers);
api.MapGet("/integrations/catalog", async (IIntegrationService service, CancellationToken cancellationToken) => Results.Ok(await service.GetCatalogAsync(cancellationToken))).RequireAuthorization();
api.MapGet("/integrations", async (IIntegrationService service, CancellationToken cancellationToken) => Results.Ok(await service.GetConnectionsAsync(cancellationToken))).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageUsers);
api.MapPut("/integrations", async (SaveIntegrationConnectionRequest request, IIntegrationService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveConnectionAsync(request, cancellationToken); return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["integration"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageUsers);
api.MapPost("/inventory-adjustments", async (RecordInventoryAdjustmentRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.RecordInventoryAdjustmentAsync(request, cancellationToken); return result.Succeeded ? Results.Created($"/api/inventory-adjustments/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["inventory"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageOperations);

api.MapGet("/interchange/quickbooks-online/{entity}.csv", async (string entity, IAccountingInterchangeService service, CancellationToken cancellationToken) =>
{
    var export = await service.ExportQuickBooksOnlineCsvAsync(entity, cancellationToken);
    return export is null ? Results.NotFound() : Results.File(export.Content, export.ContentType, export.FileName);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/interchange/quickbooks-online/{entity}", async (string entity, IFormFile? file, IAccountingInterchangeService service, CancellationToken cancellationToken) =>
{
    if (file is null || file.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Upload a non-empty CSV file."] });
    if (file.Length > 2 * 1024 * 1024) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["QuickBooks CSV imports are limited to 2 MB."] });
    if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Upload a .csv file."] });
    await using var stream = file.OpenReadStream();
    var result = await service.ImportQuickBooksOnlineCsvAsync(entity, stream, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["import"] = result.Errors.ToArray() });
}).Accepts<IFormFile>("multipart/form-data").RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapGet("/reports/{code}.csv", async (string code, IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    var workspace = await service.GetWorkspaceAsync(cancellationToken);
    var csv = code.ToUpperInvariant() switch
    {
        "TRIAL-BALANCE" => "Account,Type,Balance\n" + string.Join("\n", workspace.GeneralLedger.Accounts.Select(x => $"{Csv(x.Number)}, {Csv(x.Type)}, {x.Balance:0.00}")),
        "AR-AGING" => "Invoice,Customer,Invoice Date,Due Date,Status,Balance Due\n" + string.Join("\n", workspace.Receivables.Invoices.Select(x => $"{Csv(x.InvoiceNumber)}, {Csv(x.CustomerName)}, {x.InvoiceDate:yyyy-MM-dd}, {x.DueDate:yyyy-MM-dd}, {Csv(x.Status)}, {x.BalanceDue:0.00}")),
        "AP-AGING" => "Bill,Vendor,Bill Date,Due Date,Status,Balance Due\n" + string.Join("\n", workspace.Payables.Bills.Select(x => $"{Csv(x.BillNumber)}, {Csv(x.VendorName)}, {x.BillDate:yyyy-MM-dd}, {x.DueDate:yyyy-MM-dd}, {Csv(x.Status)}, {x.BalanceDue:0.00}")),
        _ => string.Empty
    };
    return string.IsNullOrEmpty(csv) ? Results.NotFound() : Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"{code.ToLowerInvariant()}.csv");
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReporting);

static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

app.Run();

public partial class Program;
