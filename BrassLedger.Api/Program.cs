using BrassLedger.Application.Accounting;
using BrassLedger.Application.Modernization;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddBrassLedgerCookieAuthentication();
builder.Services.AddBrassLedgerInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

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
app.UseAuthorization();
app.MapBrassLedgerAuthenticationEndpoints();

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/assessment", (IModernizationAssessmentService service) =>
{
    return Results.Ok(service.GetAssessment());
})
.WithName("GetModernizationAssessment")
.WithOpenApi();

api.MapGet("/modules", (IModernizationAssessmentService service) =>
{
    return Results.Ok(service.GetAssessment().Modules);
})
.WithName("GetLegacyModules")
.WithOpenApi();

api.MapGet("/tax-sources", (IModernizationAssessmentService service) =>
{
    return Results.Ok(service.GetAssessment().TaxSources);
})
.WithName("GetTaxSources")
.WithOpenApi();

api.MapGet("/workspace", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetWorkspaceAsync(cancellationToken));
})
.WithName("GetBusinessWorkspace")
.WithOpenApi();

api.MapGet("/dashboard", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Dashboard);
})
.WithName("GetDashboard")
.WithOpenApi();

api.MapGet("/general-ledger", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).GeneralLedger);
})
.WithName("GetGeneralLedgerWorkspace")
.WithOpenApi();

api.MapGet("/receivables", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Receivables);
})
.WithName("GetReceivablesWorkspace")
.WithOpenApi();

api.MapGet("/payables", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Payables);
})
.WithName("GetPayablesWorkspace")
.WithOpenApi();

api.MapGet("/operations", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Operations);
})
.WithName("GetOperationsWorkspace")
.WithOpenApi();

api.MapGet("/payroll", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Payroll);
})
.WithName("GetPayrollWorkspace")
.WithOpenApi();

api.MapGet("/projects", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Projects);
})
.WithName("GetProjectsWorkspace")
.WithOpenApi();

api.MapGet("/reporting-catalog", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Reporting);
})
.WithName("GetReportingWorkspace")
.WithOpenApi();

api.MapGet("/tax-workspace", async (IBusinessWorkspaceService service, CancellationToken cancellationToken) =>
{
    return Results.Ok((await service.GetWorkspaceAsync(cancellationToken)).Taxes);
})
.WithName("GetTaxWorkspace")
.WithOpenApi();

app.Run();

public partial class Program;
