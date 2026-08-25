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
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/journal-entry-drafts", async (SaveJournalEntryDraftRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveJournalEntryDraftAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/journal-entry-drafts/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/journal-entry-drafts/{journalEntryId:guid}/approve", async (Guid journalEntryId, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ApproveJournalEntryAsync(journalEntryId, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/journal-entry-drafts/{journalEntryId:guid}/post", async (Guid journalEntryId, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PostApprovedJournalEntryAsync(journalEntryId, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

api.MapPost("/journal-entries/reverse", async (ReverseJournalEntryRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReverseJournalEntryAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/journal-entries/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["journal"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

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

api.MapPost("/bank-reconciliations", async (ReconcileBankAccountRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.ReconcileBankAccountAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["reconciliation"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

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
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

api.MapPost("/payroll-runs/employee-preview", async (PostEmployeePayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PreviewEmployeePayrollRunAsync(request, cancellationToken);
    return result is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = ["Provide active employees, positive gross pay, and applicable tax profiles."] }) : Results.Ok(result);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

api.MapPost("/payroll-runs/employee", async (PostEmployeePayrollRunRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.PostEmployeePayrollRunAsync(request, cancellationToken);
    return result.Succeeded ? Results.Created($"/api/payroll-runs/{result.Id}", result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["payroll"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

api.MapPut("/employees/payroll-setup", async (SaveEmployeePayrollSetupRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SaveEmployeePayrollSetupAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["employee"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

api.MapPut("/payroll-jurisdiction-rules", async (SavePayrollJurisdictionRuleRequest request, IAccountingTransactionService service, CancellationToken cancellationToken) =>
{
    var result = await service.SavePayrollJurisdictionRuleAsync(request, cancellationToken);
    return result.Succeeded ? Results.Ok(result) : Results.ValidationProblem(new Dictionary<string, string[]> { ["jurisdictionRule"] = [result.ErrorMessage] });
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayroll);

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
