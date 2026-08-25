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
        new("quickbooks-online", "QuickBooks Online", "Accounting", "CSV interchange and future OAuth synchronization.", true),
        new("plaid", "Plaid", "Banking", "Bank-account aggregation and transaction feeds.", true),
        new("stripe", "Stripe", "Payments", "Card, ACH, invoice, and payout events.", true),
        new("square", "Square", "Payments", "Point-of-sale payments and settlements.", true),
        new("paypal", "PayPal", "Payments", "Payment and settlement events.", true),
        new("gusto", "Gusto", "Payroll", "Payroll, direct deposit, and filing provider boundary.", true),
        new("adp", "ADP", "Payroll", "Enterprise payroll provider boundary.", true),
        new("paychex", "Paychex", "Payroll", "Payroll provider boundary.", true),
        new("avalara", "Avalara", "Tax", "Sales-tax calculation and filing provider boundary.", true),
        new("taxjar", "TaxJar", "Tax", "Sales-tax calculation provider boundary.", true),
        new("docusign", "DocuSign", "Documents", "Signed-document workflow boundary.", true),
        new("microsoft-365", "Microsoft 365", "Documents", "SharePoint/OneDrive document storage boundary.", true)
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
        var companyId = CompanyId(); if (companyId is null) return TransactionResult.Failure("An active company is required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var entity = request.Id is { } id ? await db.IntegrationConnections.SingleOrDefaultAsync(connection => connection.CompanyId == companyId && connection.Id == id, cancellationToken) : null;
        if (request.Id.HasValue && entity is null) return TransactionResult.Failure("Integration connection not found.");
        if (entity is null && (string.IsNullOrWhiteSpace(request.CredentialsJson) || !IsJson(request.CredentialsJson))) return TransactionResult.Failure("Provide valid JSON credentials for a new integration connection.");
        if (!string.IsNullOrWhiteSpace(request.CredentialsJson) && !IsJson(request.CredentialsJson)) return TransactionResult.Failure("Credentials must be valid JSON when supplied.");
        entity ??= new IntegrationConnection { Id = Guid.NewGuid(), CompanyId = companyId.Value }; entity.ProviderCode = request.ProviderCode; entity.Name = request.Name.Trim(); entity.SettingsJson = request.SettingsJson.Trim(); if (!string.IsNullOrWhiteSpace(request.CredentialsJson)) entity.CredentialsJson = request.CredentialsJson.Trim(); entity.Status = request.Enable ? "Configured" : "Disabled";
        if (db.Entry(entity).State == EntityState.Detached) db.IntegrationConnections.Add(entity);
        db.BusinessAuditEntries.Add(new BusinessAuditEntry { Id = Guid.NewGuid(), CompanyId = companyId.Value, UserId = UserId(), Action = "integration.configured", EntityType = "IntegrationConnection", EntityId = entity.Id, DetailJson = System.Text.Json.JsonSerializer.Serialize(new { entity.ProviderCode, entity.Name, entity.Status }), OccurredAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken); return TransactionResult.Success(entity.Id);
    }

    private static bool IsJson(string value) { try { using var _ = System.Text.Json.JsonDocument.Parse(value); return true; } catch { return false; } }
    private Guid? CompanyId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var id) ? id : null;
    private Guid? UserId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
