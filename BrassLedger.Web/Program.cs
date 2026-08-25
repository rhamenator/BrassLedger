using System.Globalization;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.Security;
using BrassLedger.Web.Components;
using BrassLedger.Web.Hosting;
using Microsoft.AspNetCore.StaticFiles;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

var builder = WebApplication.CreateBuilder(args);
var desktopHostOptions = DesktopHostOptions.Resolve(builder.Configuration, builder.Environment, args);

if (desktopHostOptions.UseDynamicLoopbackBinding)
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddBrassLedgerCookieAuthentication();
builder.Services.AddBrassLedgerInfrastructure(builder.Configuration, builder.Environment.ContentRootPath, builder.Environment.IsDevelopment());

var app = builder.Build();
await app.Services.InitializeBrassLedgerAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

var contentTypeProvider = new FileExtensionContentTypeProvider();
var webRootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");

app.UseBrassLedgerSecurityHeaders();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
    {
        var requestedPath = context.Request.Path.Value;
        if (!string.IsNullOrWhiteSpace(requestedPath) && Path.HasExtension(requestedPath))
        {
            var relativePath = requestedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physicalPath = Path.GetFullPath(Path.Combine(webRootPath, relativePath));

            if (physicalPath.StartsWith(webRootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(physicalPath))
            {
                if (!contentTypeProvider.TryGetContentType(physicalPath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                context.Response.ContentType = contentType;
                await context.Response.SendFileAsync(physicalPath);
                return;
            }
        }
    }

    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapBrassLedgerAuthenticationEndpoints();

app.MapGet("/interchange/quickbooks-online/{entity}.csv", async (string entity, BrassLedger.Application.Accounting.IAccountingInterchangeService service, CancellationToken cancellationToken) =>
{
    var export = await service.ExportQuickBooksOnlineCsvAsync(entity, cancellationToken);
    return export is null ? Results.NotFound() : Results.File(export.Content, export.ContentType, export.FileName);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageLedger);

app.MapGet("/reports/{code}.csv", async (string code, BrassLedger.Application.Accounting.IBusinessWorkspaceService workspaceService, CancellationToken cancellationToken) =>
{
    var workspace = await workspaceService.GetWorkspaceAsync(cancellationToken);
    var csv = code.ToUpperInvariant() switch
    {
        "TRIAL-BALANCE" => "Account,Type,Balance\n" + string.Join("\n", workspace.GeneralLedger.Accounts.Select(x => $"\"{x.Number}\",\"{x.Type}\",{x.Balance:0.00}")),
        "AR-AGING" => "Invoice,Customer,Invoice Date,Due Date,Status,Balance Due\n" + string.Join("\n", workspace.Receivables.Invoices.Select(x => $"\"{x.InvoiceNumber}\",\"{x.CustomerName.Replace("\"", "\"\"")}\",{x.InvoiceDate:yyyy-MM-dd},{x.DueDate:yyyy-MM-dd},\"{x.Status}\",{x.BalanceDue:0.00}")),
        "AP-AGING" => "Bill,Vendor,Bill Date,Due Date,Status,Balance Due\n" + string.Join("\n", workspace.Payables.Bills.Select(x => $"\"{x.BillNumber}\",\"{x.VendorName.Replace("\"", "\"\"")}\",{x.BillDate:yyyy-MM-dd},{x.DueDate:yyyy-MM-dd},\"{x.Status}\",{x.BalanceDue:0.00}")),
        _ => string.Empty
    };
    return string.IsNullOrEmpty(csv) ? Results.NotFound() : Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"{code.ToLowerInvariant()}.csv");
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageReporting);

app.MapGet("/payroll/reports/{payrollRunId:guid}/register.csv", async (Guid payrollRunId, BrassLedger.Application.Accounting.IPayrollReportingService service, CancellationToken cancellationToken) =>
{
    var csv = await service.ExportRegisterCsvAsync(payrollRunId, cancellationToken);
    return csv is null ? Results.NotFound() : Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"payroll-register-{payrollRunId:N}.csv");
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.AccessPayroll);

app.MapGet("/payroll/filings/{filingId:guid}/data.json", async (Guid filingId, BrassLedger.Application.Accounting.IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var filing = await service.GetFilingAsync(filingId, cancellationToken);
    return filing is null ? Results.NotFound() : Results.File(System.Text.Encoding.UTF8.GetBytes(filing.Data.GetRawText()), "application/json", $"payroll-{filing.FormCode.ToLowerInvariant()}-{filing.TaxYear}{(filing.Quarter.HasValue ? $"-q{filing.Quarter}" : string.Empty)}.json");
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

app.MapGet("/payroll/filing-corrections/{correctionId:guid}/data.json", async (Guid correctionId, BrassLedger.Application.Accounting.IPayrollFilingService service, CancellationToken cancellationToken) =>
{
    var correction = await service.GetCorrectionAsync(correctionId, cancellationToken);
    return correction is null ? Results.NotFound() : Results.File(System.Text.Encoding.UTF8.GetBytes(correction.Data.GetRawText()), "application/json", $"payroll-941x-{correction.TaxYear}-q{correction.Quarter}-correction-{correction.Sequence}.json");
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

app.MapGet("/payroll/payment-files/{paymentFileId:guid}/download", async (Guid paymentFileId, BrassLedger.Application.Accounting.IPayrollPaymentFileService service, CancellationToken cancellationToken) =>
{
    var file = await service.DownloadAsync(paymentFileId, cancellationToken);
    return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (desktopHostOptions.LaunchBrowserOnStartup)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var launchUrl = DesktopHostOptions.ResolveLaunchUrl(app.Urls);
        if (!string.IsNullOrWhiteSpace(launchUrl))
        {
            LocalBrowserLauncher.TryOpen(launchUrl);
        }
    });
}

app.Run();

public partial class Program;
