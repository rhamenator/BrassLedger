using System.Security.Claims;
using BrassLedger.Application.Taxation;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Taxation;

public sealed class TaxAdministrationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : ITaxAdministrationService
{
    public async Task<TaxAdministrationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);

        await EnsureBaselineTaxRulesAsync(dbContext, companyId, cancellationToken);

        var ruleSets = await dbContext.TaxRuleSets
            .AsNoTracking()
            .Where(rule => rule.CompanyId == companyId)
            .OrderBy(rule => rule.JurisdictionType)
            .ThenBy(rule => rule.JurisdictionName)
            .ThenBy(rule => rule.TaxType)
            .ToListAsync(cancellationToken);
        var ruleSetIds = ruleSets.Select(rule => rule.Id).ToArray();

        var parameters = await dbContext.TaxRuleParameters
            .AsNoTracking()
            .Where(parameter => ruleSetIds.Contains(parameter.TaxRuleSetId))
            .OrderBy(parameter => parameter.DisplayOrder)
            .ThenBy(parameter => parameter.Label)
            .ToListAsync(cancellationToken);
        var brackets = await dbContext.TaxRuleBrackets
            .AsNoTracking()
            .Where(bracket => ruleSetIds.Contains(bracket.TaxRuleSetId))
            .OrderBy(bracket => bracket.Sequence)
            .ToListAsync(cancellationToken);
        var forms = await dbContext.TaxFormRequirements
            .AsNoTracking()
            .Where(form => ruleSetIds.Contains(form.TaxRuleSetId))
            .OrderBy(form => form.FormCode)
            .ToListAsync(cancellationToken);

        return new TaxAdministrationSnapshot(
            TaxRuleCatalog.Methods
                .Select(method => new TaxCalculationMethodSnapshot(method.Code, method.Name, method.Description))
                .ToArray(),
            TaxRuleCatalog.LegacyArtifacts
                .Select(artifact => new LegacyTaxArtifactSnapshot(artifact.Name, artifact.SourcePath, artifact.Notes))
                .ToArray(),
            ruleSets.Select(rule => new TaxRuleSetSnapshot(
                    rule.Id,
                    rule.Code,
                    rule.JurisdictionCode,
                    rule.JurisdictionName,
                    rule.JurisdictionType,
                    rule.TaxType,
                    rule.CalculationMethod,
                    rule.WithholdingFrequency,
                    rule.EffectiveOn,
                    rule.Source,
                    rule.Notes,
                    rule.IsEmployerSpecific,
                    rule.SupportsBracketTable,
                    rule.SupportsParameterEditing,
                    rule.IsActive,
                    parameters.Where(parameter => parameter.TaxRuleSetId == rule.Id)
                        .Select(parameter => new TaxRuleParameterSnapshot(
                            parameter.Id,
                            parameter.ParameterCode,
                            parameter.Label,
                            parameter.ValueType,
                            parameter.NumericValue,
                            parameter.TextValue,
                            parameter.BooleanValue,
                            parameter.Notes,
                            parameter.DisplayOrder))
                        .ToArray(),
                    brackets.Where(bracket => bracket.TaxRuleSetId == rule.Id)
                        .Select(bracket => new TaxRuleBracketSnapshot(
                            bracket.Id,
                            bracket.Sequence,
                            bracket.UpperBoundAmount,
                            bracket.FixedAmount,
                            bracket.Rate,
                            bracket.Notes))
                        .ToArray(),
                    forms.Where(form => form.TaxRuleSetId == rule.Id)
                        .Select(form => new TaxFormRequirementSnapshot(
                            form.Id,
                            form.FormCode,
                            form.Name,
                            form.FilingFrequency,
                            form.DeliveryChannel,
                            form.DueRule,
                            form.Notes))
                        .ToArray()))
                .ToArray());
    }

    public async Task<TaxAdministrationResult> SaveRuleSetAsync(SaveTaxRuleSetRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return TaxAdministrationResult.Failure("Enter a rule code.");
        }

        if (string.IsNullOrWhiteSpace(request.JurisdictionName))
        {
            return TaxAdministrationResult.Failure("Enter a jurisdiction name.");
        }

        if (string.IsNullOrWhiteSpace(request.TaxType))
        {
            return TaxAdministrationResult.Failure("Enter a tax type.");
        }

        if (string.IsNullOrWhiteSpace(request.CalculationMethod))
        {
            return TaxAdministrationResult.Failure("Choose a calculation method.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);
        await EnsureBaselineTaxRulesAsync(dbContext, companyId, cancellationToken);

        var code = request.Code.Trim().ToUpperInvariant();
        var duplicate = await dbContext.TaxRuleSets.AnyAsync(
            rule => rule.CompanyId == companyId
                && rule.Code == code
                && (!request.Id.HasValue || rule.Id != request.Id.Value),
            cancellationToken);
        if (duplicate)
        {
            return TaxAdministrationResult.Failure("A tax rule with that code already exists.");
        }

        var entity = request.Id.HasValue
            ? await dbContext.TaxRuleSets.SingleOrDefaultAsync(rule => rule.CompanyId == companyId && rule.Id == request.Id.Value, cancellationToken)
            : null;
        if (request.Id.HasValue && entity is null)
        {
            return TaxAdministrationResult.Failure("The selected tax rule could not be found.");
        }

        entity ??= new TaxRuleSet
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId
        };

        entity.Code = code;
        entity.JurisdictionCode = string.IsNullOrWhiteSpace(request.JurisdictionCode)
            ? code
            : request.JurisdictionCode.Trim().ToUpperInvariant();
        entity.JurisdictionName = request.JurisdictionName.Trim();
        entity.JurisdictionType = string.IsNullOrWhiteSpace(request.JurisdictionType) ? "State" : request.JurisdictionType.Trim();
        entity.TaxType = request.TaxType.Trim();
        entity.CalculationMethod = request.CalculationMethod.Trim();
        entity.WithholdingFrequency = string.IsNullOrWhiteSpace(request.WithholdingFrequency) ? "Per payroll" : request.WithholdingFrequency.Trim();
        entity.EffectiveOn = request.EffectiveOn;
        entity.Source = request.Source.Trim();
        entity.Notes = request.Notes.Trim();
        entity.IsEmployerSpecific = request.IsEmployerSpecific;
        entity.SupportsBracketTable = request.SupportsBracketTable;
        entity.SupportsParameterEditing = request.SupportsParameterEditing;
        entity.IsActive = request.IsActive;

        if (request.Id is null)
        {
            dbContext.TaxRuleSets.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TaxAdministrationResult.Success(entity.Id);
    }

    public async Task<TaxAdministrationResult> SaveParameterAsync(SaveTaxRuleParameterRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ParameterCode))
        {
            return TaxAdministrationResult.Failure("Enter a parameter code.");
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return TaxAdministrationResult.Failure("Enter a parameter label.");
        }

        var valueType = NormalizeValueType(request.ValueType);
        if (valueType.Length == 0)
        {
            return TaxAdministrationResult.Failure("Choose a parameter value type.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);
        var ruleSet = await dbContext.TaxRuleSets.SingleOrDefaultAsync(rule => rule.CompanyId == companyId && rule.Id == request.RuleSetId, cancellationToken);
        if (ruleSet is null)
        {
            return TaxAdministrationResult.Failure("Select a valid tax rule before saving parameters.");
        }

        var entity = request.Id.HasValue
            ? await dbContext.TaxRuleParameters.SingleOrDefaultAsync(parameter => parameter.TaxRuleSetId == request.RuleSetId && parameter.Id == request.Id.Value, cancellationToken)
            : null;
        if (request.Id.HasValue && entity is null)
        {
            return TaxAdministrationResult.Failure("The selected parameter could not be found.");
        }

        entity ??= new TaxRuleParameter
        {
            Id = Guid.NewGuid(),
            TaxRuleSetId = request.RuleSetId
        };

        entity.ParameterCode = request.ParameterCode.Trim().ToLowerInvariant();
        entity.Label = request.Label.Trim();
        entity.ValueType = valueType;
        entity.NumericValue = valueType == "number" ? request.NumericValue : null;
        entity.TextValue = valueType == "text" ? request.TextValue.Trim() : string.Empty;
        entity.BooleanValue = valueType == "bool" ? request.BooleanValue ?? false : null;
        entity.Notes = request.Notes.Trim();
        entity.DisplayOrder = request.DisplayOrder;

        if (request.Id is null)
        {
            dbContext.TaxRuleParameters.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TaxAdministrationResult.Success(entity.Id);
    }

    public async Task<TaxAdministrationResult> SaveBracketAsync(SaveTaxRuleBracketRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Sequence <= 0)
        {
            return TaxAdministrationResult.Failure("Bracket sequence must be greater than zero.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);
        var ruleSet = await dbContext.TaxRuleSets.SingleOrDefaultAsync(rule => rule.CompanyId == companyId && rule.Id == request.RuleSetId, cancellationToken);
        if (ruleSet is null)
        {
            return TaxAdministrationResult.Failure("Select a valid tax rule before saving brackets.");
        }

        var entity = request.Id.HasValue
            ? await dbContext.TaxRuleBrackets.SingleOrDefaultAsync(bracket => bracket.TaxRuleSetId == request.RuleSetId && bracket.Id == request.Id.Value, cancellationToken)
            : null;
        if (request.Id.HasValue && entity is null)
        {
            return TaxAdministrationResult.Failure("The selected bracket row could not be found.");
        }

        entity ??= new TaxRuleBracket
        {
            Id = Guid.NewGuid(),
            TaxRuleSetId = request.RuleSetId
        };

        entity.Sequence = request.Sequence;
        entity.UpperBoundAmount = request.UpperBoundAmount;
        entity.FixedAmount = request.FixedAmount;
        entity.Rate = request.Rate;
        entity.Notes = request.Notes.Trim();

        if (request.Id is null)
        {
            dbContext.TaxRuleBrackets.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TaxAdministrationResult.Success(entity.Id);
    }

    public async Task<TaxAdministrationResult> SaveFormRequirementAsync(SaveTaxFormRequirementRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FormCode))
        {
            return TaxAdministrationResult.Failure("Enter a form code.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TaxAdministrationResult.Failure("Enter a form name.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);
        var ruleSet = await dbContext.TaxRuleSets.SingleOrDefaultAsync(rule => rule.CompanyId == companyId && rule.Id == request.RuleSetId, cancellationToken);
        if (ruleSet is null)
        {
            return TaxAdministrationResult.Failure("Select a valid tax rule before saving filing requirements.");
        }

        var entity = request.Id.HasValue
            ? await dbContext.TaxFormRequirements.SingleOrDefaultAsync(form => form.TaxRuleSetId == request.RuleSetId && form.Id == request.Id.Value, cancellationToken)
            : null;
        if (request.Id.HasValue && entity is null)
        {
            return TaxAdministrationResult.Failure("The selected filing requirement could not be found.");
        }

        entity ??= new TaxFormRequirement
        {
            Id = Guid.NewGuid(),
            TaxRuleSetId = request.RuleSetId
        };

        entity.FormCode = request.FormCode.Trim().ToUpperInvariant();
        entity.Name = request.Name.Trim();
        entity.FilingFrequency = string.IsNullOrWhiteSpace(request.FilingFrequency) ? "As required" : request.FilingFrequency.Trim();
        entity.DeliveryChannel = request.DeliveryChannel.Trim();
        entity.DueRule = request.DueRule.Trim();
        entity.Notes = request.Notes.Trim();

        if (request.Id is null)
        {
            dbContext.TaxFormRequirements.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TaxAdministrationResult.Success(entity.Id);
    }

    internal static async Task EnsureBaselineTaxRulesAsync(
        BrassLedgerDbContext dbContext,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var existingCodes = await dbContext.TaxRuleSets
            .Where(rule => rule.CompanyId == companyId)
            .Select(rule => rule.Code)
            .ToListAsync(cancellationToken);

        var hasChanges = false;
        foreach (var template in TaxRuleCatalog.Templates)
        {
            if (existingCodes.Contains(template.Code, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var ruleSetId = Guid.NewGuid();
            dbContext.TaxRuleSets.Add(new TaxRuleSet
            {
                Id = ruleSetId,
                CompanyId = companyId,
                Code = template.Code,
                JurisdictionCode = template.JurisdictionCode,
                JurisdictionName = template.JurisdictionName,
                JurisdictionType = template.JurisdictionType,
                TaxType = template.TaxType,
                CalculationMethod = template.CalculationMethod,
                WithholdingFrequency = template.WithholdingFrequency,
                EffectiveOn = template.EffectiveOn,
                Source = template.Source,
                Notes = template.Notes,
                IsEmployerSpecific = template.IsEmployerSpecific,
                SupportsBracketTable = template.SupportsBracketTable,
                SupportsParameterEditing = template.SupportsParameterEditing,
                IsActive = true
            });

            foreach (var parameter in template.Parameters)
            {
                dbContext.TaxRuleParameters.Add(new TaxRuleParameter
                {
                    Id = Guid.NewGuid(),
                    TaxRuleSetId = ruleSetId,
                    ParameterCode = parameter.ParameterCode,
                    Label = parameter.Label,
                    ValueType = NormalizeValueType(parameter.ValueType),
                    NumericValue = parameter.NumericValue,
                    TextValue = parameter.TextValue,
                    BooleanValue = parameter.BooleanValue,
                    Notes = parameter.Notes,
                    DisplayOrder = parameter.DisplayOrder
                });
            }

            foreach (var bracket in template.Brackets)
            {
                dbContext.TaxRuleBrackets.Add(new TaxRuleBracket
                {
                    Id = Guid.NewGuid(),
                    TaxRuleSetId = ruleSetId,
                    Sequence = bracket.Sequence,
                    UpperBoundAmount = bracket.UpperBoundAmount,
                    FixedAmount = bracket.FixedAmount,
                    Rate = bracket.Rate,
                    Notes = bracket.Notes
                });
            }

            foreach (var form in template.FormRequirements)
            {
                dbContext.TaxFormRequirements.Add(new TaxFormRequirement
                {
                    Id = Guid.NewGuid(),
                    TaxRuleSetId = ruleSetId,
                    FormCode = form.FormCode,
                    Name = form.Name,
                    FilingFrequency = form.FilingFrequency,
                    DeliveryChannel = form.DeliveryChannel,
                    DueRule = form.DueRule,
                    Notes = form.Notes
                });
            }

            hasChanges = true;
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        var claimValue = httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (Guid.TryParse(claimValue, out var companyId))
        {
            return companyId;
        }

        return await dbContext.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .Select(company => company.Id)
            .FirstAsync(cancellationToken);
    }

    private static string NormalizeValueType(string valueType)
    {
        return valueType.Trim().ToLowerInvariant() switch
        {
            "number" => "number",
            "numeric" => "number",
            "text" => "text",
            "string" => "text",
            "bool" => "bool",
            "boolean" => "bool",
            _ => string.Empty
        };
    }
}
