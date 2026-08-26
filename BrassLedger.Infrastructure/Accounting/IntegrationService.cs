using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class IntegrationService(IDbContextFactory<BrassLedgerDbContext> dbContextFactory, IHttpContextAccessor httpContextAccessor) : IIntegrationService
{
    private static readonly IReadOnlyList<IntegrationProviderSnapshot> Catalog =
    [
        new("quickbooks-online", "QuickBooks Online", "Accounting", "Secure OAuth, explicit mapping, and operator-controlled API import are implemented for accounts, customers, and vendors; transactional API synchronization is still being completed.", true, "Controlled master-data API import", "Protected OAuth lifecycle; snapshot-bound account, customer, and vendor API import and mapping; plus reviewed CSV lists, non-control journals, and zero-tax invoice draft interchange", true),
        new("plaid", "Plaid", "Banking", "Connection profile only; no transaction-feed adapter is deployed.", false),
        new("stripe", "Stripe", "Payments", "Connection profile only; no payment-event adapter is deployed.", false),
        new("square", "Square", "Payments", "Connection profile only; no point-of-sale adapter is deployed.", false),
        new("paypal", "PayPal", "Payments", "Connection profile only; no settlement adapter is deployed.", false),
        new("gusto", "Gusto", "Payroll", "Connection profile only; no payroll adapter is deployed.", false),
        new("adp", "ADP", "Payroll", "Connection profile only; no payroll adapter is deployed.", false),
        new("paychex", "Paychex", "Payroll", "Connection profile only; no payroll adapter is deployed.", false),
        new("avalara", "Avalara", "Tax", "Connection profile only; no calculation or filing adapter is deployed.", false),
        new("taxjar", "TaxJar", "Tax", "Connection profile only; no calculation adapter is deployed.", false),
        new("docusign", "DocuSign", "Documents", "Connection profile only; no signature adapter is deployed.", false),
        new("microsoft-365", "Microsoft 365", "Documents", "Connection profile only; no SharePoint or OneDrive adapter is deployed.", false)
    ];

    public Task<IReadOnlyList<IntegrationProviderSnapshot>> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(Catalog);

    public async Task<IReadOnlyList<IntegrationConnectionSnapshot>> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = CompanyId(); if (companyId is null) return [];
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.IntegrationConnections.AsNoTracking().Where(connection => connection.CompanyId == companyId).OrderBy(connection => connection.ProviderCode).ThenBy(connection => connection.Name).Select(connection => new IntegrationConnectionSnapshot(connection.Id, connection.ProviderCode, connection.Name, connection.Status, connection.SettingsJson, connection.LastValidatedAtUtc)).ToArrayAsync(cancellationToken);
    }

    public async Task<TransactionResult> SaveConnectionAsync(SaveIntegrationConnectionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || !Catalog.Any(provider => provider.Code == request.ProviderCode) || !IsJson(request.SettingsJson)) return TransactionResult.Failure("Select a known provider and provide valid JSON settings.");
        if (request.ProviderCode == "quickbooks-online") return TransactionResult.Failure("Use the protected QuickBooks connect or reconnect workflow; QuickBooks credentials cannot be entered as JSON.");
        var companyId = CompanyId(); if (companyId is null) return TransactionResult.Failure("An active company is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var entity = request.Id is { } id ? await db.IntegrationConnections.SingleOrDefaultAsync(connection => connection.CompanyId == companyId && connection.Id == id, cancellationToken) : null;
        if (request.Id.HasValue && entity is null) return TransactionResult.Failure("Integration connection not found.");
        if (entity is null && (string.IsNullOrWhiteSpace(request.CredentialsJson) || !IsJson(request.CredentialsJson))) return TransactionResult.Failure("Provide valid JSON credentials for a new integration connection.");
        if (!string.IsNullOrWhiteSpace(request.CredentialsJson) && !IsJson(request.CredentialsJson)) return TransactionResult.Failure("Credentials must be valid JSON when supplied.");
        entity ??= new IntegrationConnection { Id = Guid.NewGuid(), CompanyId = companyId.Value }; entity.ProviderCode = request.ProviderCode; entity.Name = request.Name.Trim(); entity.SettingsJson = request.SettingsJson.Trim(); if (!string.IsNullOrWhiteSpace(request.CredentialsJson)) entity.CredentialsJson = request.CredentialsJson.Trim(); entity.Status = request.Enable ? "ProfileOnly" : "Disabled";
        if (db.Entry(entity).State == EntityState.Detached) db.IntegrationConnections.Add(entity);
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = UserId(), Action = "integration.configured", EntityType = "IntegrationConnection", EntityId = entity.Id, DetailJson = System.Text.Json.JsonSerializer.Serialize(new { entity.ProviderCode, entity.Name, entity.Status }), OccurredAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken); return TransactionResult.Success(entity.Id);
    }

    private static bool IsJson(string value) { try { using var _ = System.Text.Json.JsonDocument.Parse(value); return true; } catch { return false; } }
    private Guid? CompanyId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var id) ? id : null;
    private Guid? UserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
