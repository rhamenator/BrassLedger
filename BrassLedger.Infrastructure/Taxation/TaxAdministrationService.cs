using System.Security.Claims;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrassLedger.Application.Taxation;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Taxation;

public sealed class TaxAdministrationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    IHttpClientFactory httpClientFactory) : ITaxAdministrationService
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
        var packages = await dbContext.TaxContentPackages.AsNoTracking().Where(package => package.CompanyId == companyId).OrderByDescending(package => package.EffectiveOn).ToListAsync(cancellationToken);
        var captures = (await dbContext.TaxSourceCaptures.AsNoTracking().Where(capture => capture.CompanyId == companyId).ToListAsync(cancellationToken))
            .OrderByDescending(capture => capture.CapturedAtUtc).Take(100).ToArray();
        var fieldDefinitions = await dbContext.TaxRuleFieldDefinitions.AsNoTracking().Where(field => ruleSetIds.Contains(field.TaxRuleSetId)).OrderBy(field => field.DisplayOrder).ToListAsync(cancellationToken);
        var testCases = await dbContext.TaxRuleTestCases.AsNoTracking().Where(testCase => ruleSetIds.Contains(testCase.TaxRuleSetId)).OrderBy(testCase => testCase.Name).ToListAsync(cancellationToken);

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
            TaxRuleCatalog.StateJurisdictions.Select(state => new TaxJurisdictionSnapshot(state.Code, state.Name)).ToArray(),
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
                    rule.TaxContentPackageId,
                    rule.ContentVersion,
                    rule.MinimumEngineVersion,
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
                        .ToArray(),
                    fieldDefinitions.Where(field => field.TaxRuleSetId == rule.Id).Select(field => new TaxRuleFieldDefinitionSnapshot(field.Id, field.FieldCode, field.Label, field.DataType, field.IsRequired, field.DefaultValueJson, field.ValidationJson, field.DisplayOrder, field.HelpText)).ToArray(),
                    testCases.Where(testCase => testCase.TaxRuleSetId == rule.Id).Select(testCase => new TaxRuleTestCaseSnapshot(testCase.Id, testCase.Name, testCase.InputJson, testCase.ExpectedOutputJson, testCase.IsRequiredForActivation)).ToArray(),
                    rule.ParentJurisdictionCode, rule.ObligationCode, rule.CalculationVariant, rule.ExclusiveGroup, rule.VariantPriority, rule.ApplicabilityJson))
                .ToArray(),
            packages.Select(package => new TaxContentPackageSnapshot(package.Id, package.PackageCode, package.Version, package.EffectiveOn, package.Status, package.MinimumEngineVersion, package.ManifestJson, package.Source, package.ChangeSummary, package.CreatedAtUtc, package.ApprovedAtUtc)).ToArray(),
            captures.Select(capture => new TaxSourceCaptureSnapshot(capture.Id, capture.TaxContentPackageId, capture.SourceKind, capture.JurisdictionCode, capture.SourceUrl, capture.ContentType, capture.ContentSha256, capture.RawContent.Length, capture.CapturedAtUtc, capture.Notes)).ToArray());
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

        if (!IsJsonObject(request.ApplicabilityJson))
        {
            return TaxAdministrationResult.Failure("Applicability must be valid JSON.");
        }

        if (!string.IsNullOrWhiteSpace(request.ExclusiveGroup) && string.IsNullOrWhiteSpace(request.ObligationCode))
        {
            return TaxAdministrationResult.Failure("An exclusive calculation variant must identify its tax obligation.");
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

        if (entity is not null && await IsApprovedPackageRuleAsync(dbContext, entity, cancellationToken))
        {
            return TaxAdministrationResult.Failure("This rule belongs to an approved tax-content package and is immutable. Create a new package version and copy the rule instead.");
        }

        if (request.TaxContentPackageId.HasValue && await IsApprovedPackageAsync(dbContext, companyId, request.TaxContentPackageId.Value, cancellationToken))
        {
            return TaxAdministrationResult.Failure("Approved tax-content packages are immutable. Link the rule to a draft package version instead.");
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
        entity.TaxContentPackageId = request.TaxContentPackageId;
        entity.ContentVersion = string.IsNullOrWhiteSpace(request.ContentVersion) ? "1.0" : request.ContentVersion.Trim();
        entity.MinimumEngineVersion = string.IsNullOrWhiteSpace(request.MinimumEngineVersion) ? "1.0" : request.MinimumEngineVersion.Trim();
        entity.ParentJurisdictionCode = request.ParentJurisdictionCode.Trim().ToUpperInvariant();
        entity.ObligationCode = request.ObligationCode.Trim().ToUpperInvariant();
        entity.CalculationVariant = request.CalculationVariant.Trim();
        entity.ExclusiveGroup = request.ExclusiveGroup.Trim().ToUpperInvariant();
        entity.VariantPriority = request.VariantPriority;
        entity.ApplicabilityJson = IsJson(request.ApplicabilityJson) ? request.ApplicabilityJson : "{}";

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
        if (await IsApprovedPackageRuleAsync(dbContext, ruleSet, cancellationToken))
        {
            return TaxAdministrationResult.Failure("This rule belongs to an approved tax-content package and is immutable. Create a new package version instead.");
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
        if (await IsApprovedPackageRuleAsync(dbContext, ruleSet, cancellationToken))
        {
            return TaxAdministrationResult.Failure("This rule belongs to an approved tax-content package and is immutable. Create a new package version instead.");
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
        if (await IsApprovedPackageRuleAsync(dbContext, ruleSet, cancellationToken))
        {
            return TaxAdministrationResult.Failure("This rule belongs to an approved tax-content package and is immutable. Create a new package version instead.");
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

    public async Task<TaxAdministrationResult> SaveContentPackageAsync(SaveTaxContentPackageRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PackageCode) || string.IsNullOrWhiteSpace(request.Version)) return TaxAdministrationResult.Failure("Enter a package code and version.");
        if (!IsJson(request.ManifestJson)) return TaxAdministrationResult.Failure("Package manifest must be valid JSON.");
        var status = request.Status.Trim();
        if (status is not ("Draft" or "Validated" or "Approved" or "Superseded")) return TaxAdministrationResult.Failure("Use Draft, Validated, Approved, or Superseded package status.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var entity = request.Id is { } id ? await db.TaxContentPackages.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, cancellationToken) : null;
        if (request.Id.HasValue && entity is null) return TaxAdministrationResult.Failure("Tax content package not found.");
        if (entity?.Status == "Approved") return TaxAdministrationResult.Failure("Approved tax-content packages are immutable. Create a new version for a rule or data change.");
        var code = request.PackageCode.Trim().ToUpperInvariant(); var version = request.Version.Trim();
        if (entity is null && await db.TaxContentPackages.AnyAsync(item => item.CompanyId == companyId && item.PackageCode == code && item.Version == version, cancellationToken)) return TaxAdministrationResult.Failure("That tax content package version already exists.");
        entity ??= new TaxContentPackage { Id = Guid.NewGuid(), CompanyId = companyId, CreatedAtUtc = DateTimeOffset.UtcNow };
        if (status == "Approved") return TaxAdministrationResult.Failure("Use the activation workflow to approve a package after validation.");
        entity.PackageCode = code; entity.Version = version; entity.EffectiveOn = request.EffectiveOn; entity.Status = status; entity.MinimumEngineVersion = request.MinimumEngineVersion.Trim(); entity.ManifestJson = request.ManifestJson.Trim(); entity.Source = request.Source.Trim(); entity.ChangeSummary = request.ChangeSummary.Trim(); entity.ApprovedAtUtc = null;
        if (db.Entry(entity).State == EntityState.Detached) db.TaxContentPackages.Add(entity);
        await db.SaveChangesAsync(cancellationToken); return TaxAdministrationResult.Success(entity.Id);
    }

    public async Task<TaxAdministrationResult> SaveFieldDefinitionAsync(SaveTaxRuleFieldDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FieldCode) || string.IsNullOrWhiteSpace(request.Label) || !IsJson(request.DefaultValueJson) || !IsJson(request.ValidationJson)) return TaxAdministrationResult.Failure("Field code, label, default value JSON, and validation JSON are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        if (!await db.TaxRuleSets.AnyAsync(rule => rule.CompanyId == companyId && rule.Id == request.RuleSetId, cancellationToken)) return TaxAdministrationResult.Failure("Tax rule not found.");
        var ruleSet = await db.TaxRuleSets.SingleAsync(rule => rule.CompanyId == companyId && rule.Id == request.RuleSetId, cancellationToken);
        if (await IsApprovedPackageRuleAsync(db, ruleSet, cancellationToken)) return TaxAdministrationResult.Failure("This rule belongs to an approved tax-content package and is immutable. Create a new package version instead.");
        var entity = request.Id is { } id ? await db.TaxRuleFieldDefinitions.SingleOrDefaultAsync(field => field.TaxRuleSetId == request.RuleSetId && field.Id == id, cancellationToken) : null;
        entity ??= new TaxRuleFieldDefinition { Id = Guid.NewGuid(), TaxRuleSetId = request.RuleSetId };
        entity.FieldCode = request.FieldCode.Trim().ToLowerInvariant(); entity.Label = request.Label.Trim(); entity.DataType = request.DataType.Trim().ToLowerInvariant(); entity.IsRequired = request.IsRequired; entity.DefaultValueJson = request.DefaultValueJson.Trim(); entity.ValidationJson = request.ValidationJson.Trim(); entity.DisplayOrder = request.DisplayOrder; entity.HelpText = request.HelpText.Trim();
        if (db.Entry(entity).State == EntityState.Detached) db.TaxRuleFieldDefinitions.Add(entity); await db.SaveChangesAsync(cancellationToken); return TaxAdministrationResult.Success(entity.Id);
    }

    public async Task<TaxAdministrationResult> SaveTestCaseAsync(SaveTaxRuleTestCaseRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || !IsJson(request.InputJson) || !IsJson(request.ExpectedOutputJson)) return TaxAdministrationResult.Failure("Test name, input JSON, and expected output JSON are required.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var ruleSet = await db.TaxRuleSets.SingleOrDefaultAsync(rule => rule.CompanyId == companyId && rule.Id == request.RuleSetId, cancellationToken);
        if (ruleSet is null) return TaxAdministrationResult.Failure("Tax rule not found.");
        if (await IsApprovedPackageRuleAsync(db, ruleSet, cancellationToken)) return TaxAdministrationResult.Failure("This rule belongs to an approved tax-content package and is immutable. Create a new package version instead.");
        var entity = request.Id is { } id ? await db.TaxRuleTestCases.SingleOrDefaultAsync(test => test.TaxRuleSetId == request.RuleSetId && test.Id == id, cancellationToken) : null;
        entity ??= new TaxRuleTestCase { Id = Guid.NewGuid(), TaxRuleSetId = request.RuleSetId }; entity.Name = request.Name.Trim(); entity.InputJson = request.InputJson.Trim(); entity.ExpectedOutputJson = request.ExpectedOutputJson.Trim(); entity.IsRequiredForActivation = request.IsRequiredForActivation;
        if (db.Entry(entity).State == EntityState.Detached) db.TaxRuleTestCases.Add(entity); await db.SaveChangesAsync(cancellationToken); return TaxAdministrationResult.Success(entity.Id);
    }

    public async Task<TaxContentValidationResult> ValidateContentPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var package = await db.TaxContentPackages.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == packageId, cancellationToken);
        if (package is null) return TaxContentValidationResult.Failure("Tax content package not found.");
        var errors = new List<string>();
        if (!IsCompatibleWithCurrentEngine(package.MinimumEngineVersion)) errors.Add($"Package requires engine {package.MinimumEngineVersion}; this installation supports {CurrentTaxEngineVersion}.");
        if (!IsJson(package.ManifestJson)) errors.Add("Package manifest is not valid JSON.");
        var rules = await db.TaxRuleSets.Where(rule => rule.CompanyId == companyId && rule.TaxContentPackageId == package.Id).ToListAsync(cancellationToken);
        if (rules.Count == 0) errors.Add("Package has no linked tax rules.");
        var ruleIds = rules.Select(rule => rule.Id).ToArray();
        var tests = await db.TaxRuleTestCases.Where(test => ruleIds.Contains(test.TaxRuleSetId) && test.IsRequiredForActivation).ToListAsync(cancellationToken);
        var parameters = await db.TaxRuleParameters.Where(parameter => ruleIds.Contains(parameter.TaxRuleSetId)).ToListAsync(cancellationToken);
        var brackets = await db.TaxRuleBrackets.Where(bracket => ruleIds.Contains(bracket.TaxRuleSetId)).ToListAsync(cancellationToken);
        foreach (var rule in rules)
        {
            if (!IsCompatibleWithCurrentEngine(rule.MinimumEngineVersion)) errors.Add($"Rule {rule.Code} requires engine {rule.MinimumEngineVersion}.");
            if (!IsJsonObject(rule.ApplicabilityJson)) errors.Add($"Rule {rule.Code} has invalid applicability JSON.");
            if (!string.IsNullOrWhiteSpace(rule.ExclusiveGroup) && string.IsNullOrWhiteSpace(rule.ObligationCode)) errors.Add($"Rule {rule.Code} has an exclusive group but no obligation code.");
            if (!tests.Any(test => test.TaxRuleSetId == rule.Id)) errors.Add($"Rule {rule.Code} has no required activation test case.");
        }
        foreach (var test in tests)
        {
            if (!IsJson(test.InputJson) || !IsJson(test.ExpectedOutputJson)) { errors.Add($"Test case {test.Name} contains invalid JSON."); continue; }
            var rule = rules.Single(rule => rule.Id == test.TaxRuleSetId);
            if (!TryReadNumber(test.InputJson, "grossPay", out var grossPay) || !TryReadNumber(test.ExpectedOutputJson, "amount", out var expectedAmount) && !TryReadNumber(test.ExpectedOutputJson, "withholding", out expectedAmount)) { errors.Add($"Test case {test.Name} must specify numeric grossPay input and amount or withholding output."); continue; }
            var allowances = TryReadNumber(test.InputJson, "allowances", out var allowanceValue) ? (int)allowanceValue : 0;
            using var inputDocument = JsonDocument.Parse(test.InputJson);
            var filingStatus = inputDocument.RootElement.TryGetProperty("filingStatus", out var statusElement) ? statusElement.GetString() ?? "Single" : "Single";
            var payrollFrequency = inputDocument.RootElement.TryGetProperty("payrollFrequency", out var frequencyElement) ? frequencyElement.GetString() ?? "Biweekly" : "Biweekly";
            var otherStateWithholding = TryReadNumber(test.InputJson, "otherStateWithholding", out var otherStateValue) ? otherStateValue : 0m;
            var actualAmount = EvaluateRule(rule, parameters.Where(parameter => parameter.TaxRuleSetId == rule.Id), brackets.Where(bracket => bracket.TaxRuleSetId == rule.Id), grossPay, allowances, filingStatus, payrollFrequency, otherStateWithholding);
            if (actualAmount != decimal.Round(expectedAmount, 2, MidpointRounding.AwayFromZero)) errors.Add($"Test case {test.Name} for {rule.Code} ({rule.CalculationMethod}, brackets: {string.Join(", ", brackets.Where(bracket => bracket.TaxRuleSetId == rule.Id).OrderBy(bracket => bracket.Sequence).Select(bracket => $"{bracket.UpperBoundAmount:0.##}/{bracket.FixedAmount:0.##}/{bracket.Rate:0.#####}"))}) expected {expectedAmount:0.00} but calculated {actualAmount:0.00}.");
        }
        return errors.Count == 0 ? TaxContentValidationResult.Success() : new(false, errors);
    }

    public async Task<TaxAdministrationResult> ActivateContentPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateContentPackageAsync(packageId, cancellationToken);
        if (!validation.Succeeded) return TaxAdministrationResult.Failure(string.Join(" ", validation.Errors));
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var package = await db.TaxContentPackages.SingleAsync(item => item.CompanyId == companyId && item.Id == packageId, cancellationToken);
        package.Status = "Approved"; package.ApprovedAtUtc = DateTimeOffset.UtcNow;
        var supersededPackageIds = await db.TaxContentPackages.Where(item => item.CompanyId == companyId && item.PackageCode == package.PackageCode && item.Id != package.Id && item.Status == "Approved" && item.EffectiveOn <= package.EffectiveOn).Select(item => item.Id).ToArrayAsync(cancellationToken);
        if (supersededPackageIds.Length > 0)
        {
            await db.TaxRuleSets.Where(rule => rule.CompanyId == companyId && rule.TaxContentPackageId.HasValue && supersededPackageIds.Contains(rule.TaxContentPackageId.Value)).ExecuteUpdateAsync(setters => setters.SetProperty(rule => rule.IsActive, false), cancellationToken);
            await db.TaxContentPackages.Where(item => supersededPackageIds.Contains(item.Id)).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "Superseded"), cancellationToken);
        }
        await db.TaxRuleSets.Where(rule => rule.CompanyId == companyId && rule.TaxContentPackageId == package.Id).ExecuteUpdateAsync(setters => setters.SetProperty(rule => rule.IsActive, true), cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return TaxAdministrationResult.Success(package.Id);
    }

    public async Task<TaxAdministrationResult> ImportTaxLocusAsync(CancellationToken cancellationToken = default)
    {
        var sources = new[]
        {
            "payroll_federal_rates", "payroll_state_pit", "payroll_state_suta", "payroll_local_income_tax", "payroll_reciprocity"
        };
        var downloaded = new List<(string Name, string Url, string Content, string ContentType, int Rows, string Hash)>();
        try
        {
            foreach (var source in sources)
            {
                var url = $"https://raw.githubusercontent.com/Ringzero787/taxlocus-data/main/data/{source}.json";
                var response = await DownloadSafeSourceAsync(url, cancellationToken);
                using var document = JsonDocument.Parse(response.Content);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return TaxAdministrationResult.Failure($"TaxLocus file {source}.json is not a JSON array.");
                }
                downloaded.Add((source, url, response.Content, response.ContentType, document.RootElement.GetArrayLength(), response.Hash));
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            return TaxAdministrationResult.Failure($"TaxLocus import could not complete: {exception.Message}");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var combinedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", downloaded.Select(item => item.Hash)))))
            .ToLowerInvariant();
        var version = $"{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}-{combinedHash[..12]}";
        var existing = await db.TaxContentPackages.SingleOrDefaultAsync(package => package.CompanyId == companyId && package.PackageCode == "TAXLOCUS-PAYROLL" && package.Version == version, cancellationToken);
        if (existing is not null)
        {
            return TaxAdministrationResult.Success(existing.Id);
        }

        var package = new TaxContentPackage
        {
            Id = Guid.NewGuid(), CompanyId = companyId, PackageCode = "TAXLOCUS-PAYROLL", Version = version,
            EffectiveOn = DateOnly.FromDateTime(DateTime.UtcNow), Status = "Draft", MinimumEngineVersion = CurrentTaxEngineVersion,
            Source = "https://github.com/Ringzero787/taxlocus-data", CreatedAtUtc = DateTimeOffset.UtcNow,
            ChangeSummary = "Manual TaxLocus payroll reference import. Review citations, implement withholding logic, and add regression cases before activation.",
            ManifestJson = JsonSerializer.Serialize(new
            {
                provider = "TaxLocus", license = "CC-BY-4.0", importedAtUtc = DateTimeOffset.UtcNow,
                aggregateSha256 = combinedHash,
                files = downloaded.Select(item => new { item.Name, item.Url, item.Rows, sha256 = item.Hash }).ToArray(),
                limitations = "Reference rates only; not withholding tables or a calculation engine."
            })
        };
        db.TaxContentPackages.Add(package);
        foreach (var item in downloaded)
        {
            db.TaxSourceCaptures.Add(new TaxSourceCapture
            {
                Id = Guid.NewGuid(), CompanyId = companyId, TaxContentPackageId = package.Id, SourceKind = "TaxLocus",
                JurisdictionCode = string.Empty, SourceUrl = item.Url, ContentType = item.ContentType, ContentSha256 = item.Hash,
                RawContent = item.Content, CapturedAtUtc = DateTimeOffset.UtcNow,
                Notes = $"TaxLocus payroll dataset: {item.Name}.json ({item.Rows:N0} rows)."
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return TaxAdministrationResult.Success(package.Id);
    }

    public async Task<TaxAdministrationResult> CaptureStateSourceAsync(CaptureTaxSourceRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.JurisdictionCode)) return TaxAdministrationResult.Failure("Enter a state or locality code.");
        try
        {
            var response = await DownloadSafeSourceAsync(request.SourceUrl, cancellationToken);
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
            var capture = new TaxSourceCapture
            {
                Id = Guid.NewGuid(), CompanyId = companyId, SourceKind = "State web capture",
                JurisdictionCode = request.JurisdictionCode.Trim().ToUpperInvariant(), SourceUrl = request.SourceUrl.Trim(),
                ContentType = response.ContentType, ContentSha256 = response.Hash, RawContent = response.Content,
                CapturedAtUtc = DateTimeOffset.UtcNow, Notes = request.Notes.Trim()
            };
            db.TaxSourceCaptures.Add(capture);
            await db.SaveChangesAsync(cancellationToken);
            return TaxAdministrationResult.Success(capture.Id);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return TaxAdministrationResult.Failure($"The source was not captured: {exception.Message}");
        }
    }

    public async Task<TaxAdministrationResult> ImportTaxContentDocumentAsync(string documentJson, CancellationToken cancellationToken = default)
    {
        TaxContentImportDocument? document;
        try { document = JsonSerializer.Deserialize<TaxContentImportDocument>(documentJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException exception) { return TaxAdministrationResult.Failure($"Tax-content JSON is invalid: {exception.Message}"); }
        if (document is null || string.IsNullOrWhiteSpace(document.PackageCode) || string.IsNullOrWhiteSpace(document.Version) || document.Rules.Count == 0)
            return TaxAdministrationResult.Failure("The tax-content document needs a package code, version, and at least one rule.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var code = document.PackageCode.Trim().ToUpperInvariant();
        var version = document.Version.Trim();
        if (await db.TaxContentPackages.AnyAsync(package => package.CompanyId == companyId && package.PackageCode == code && package.Version == version, cancellationToken))
            return TaxAdministrationResult.Failure("That tax-content package version already exists; create a new version rather than overwriting evidence.");
        var duplicateRule = document.Rules.GroupBy(rule => rule.Code.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateRule is not null) return TaxAdministrationResult.Failure($"The document repeats rule code {duplicateRule.Key}.");

        var package = new TaxContentPackage { Id = Guid.NewGuid(), CompanyId = companyId, PackageCode = code, Version = version, EffectiveOn = document.EffectiveOn, Status = "Draft", MinimumEngineVersion = CurrentTaxEngineVersion, Source = document.Source?.Trim() ?? string.Empty, ChangeSummary = document.ChangeSummary?.Trim() ?? string.Empty, CreatedAtUtc = DateTimeOffset.UtcNow, ManifestJson = documentJson };
        db.TaxContentPackages.Add(package);
        foreach (var importedRule in document.Rules)
        {
            if (string.IsNullOrWhiteSpace(importedRule.Code) || string.IsNullOrWhiteSpace(importedRule.JurisdictionName) || string.IsNullOrWhiteSpace(importedRule.CalculationMethod))
                return TaxAdministrationResult.Failure("Each imported rule needs code, jurisdiction name, and calculation method.");
            var rule = new TaxRuleSet { Id = Guid.NewGuid(), CompanyId = companyId, TaxContentPackageId = package.Id, Code = importedRule.Code.Trim().ToUpperInvariant(), JurisdictionCode = importedRule.JurisdictionCode?.Trim().ToUpperInvariant() ?? string.Empty, JurisdictionName = importedRule.JurisdictionName.Trim(), JurisdictionType = string.IsNullOrWhiteSpace(importedRule.JurisdictionType) ? "State" : importedRule.JurisdictionType.Trim(), TaxType = importedRule.TaxType?.Trim() ?? "Employee withholding", CalculationMethod = importedRule.CalculationMethod.Trim(), WithholdingFrequency = importedRule.WithholdingFrequency?.Trim() ?? "Per payroll", EffectiveOn = document.EffectiveOn, Source = package.Source, Notes = "Imported draft tax content; review source evidence and activation tests before use.", IsEmployerSpecific = importedRule.IsEmployerSpecific, SupportsBracketTable = importedRule.Brackets?.Count > 0, SupportsParameterEditing = true, IsActive = false, ContentVersion = version, MinimumEngineVersion = CurrentTaxEngineVersion, ParentJurisdictionCode = importedRule.ParentJurisdictionCode?.Trim().ToUpperInvariant() ?? string.Empty, ObligationCode = importedRule.ObligationCode?.Trim().ToUpperInvariant() ?? importedRule.Code.Trim().ToUpperInvariant(), CalculationVariant = importedRule.CalculationVariant?.Trim() ?? importedRule.CalculationMethod.Trim(), ExclusiveGroup = importedRule.ExclusiveGroup?.Trim().ToUpperInvariant() ?? string.Empty, VariantPriority = importedRule.VariantPriority, ApplicabilityJson = importedRule.Applicability?.GetRawText() ?? "{}" };
            db.TaxRuleSets.Add(rule);
            var parameterOrder = 0;
            foreach (var parameter in importedRule.Parameters ?? []) db.TaxRuleParameters.Add(new TaxRuleParameter { Id = Guid.NewGuid(), TaxRuleSetId = rule.Id, ParameterCode = parameter.Code.Trim().ToLowerInvariant(), Label = parameter.Label?.Trim() ?? parameter.Code.Trim(), ValueType = parameter.Number.HasValue ? "number" : parameter.Boolean.HasValue ? "bool" : parameter.Json.HasValue ? "json" : "text", NumericValue = parameter.Number, TextValue = parameter.Json?.GetRawText() ?? parameter.Text?.Trim() ?? string.Empty, BooleanValue = parameter.Boolean, Notes = parameter.Notes?.Trim() ?? string.Empty, DisplayOrder = parameterOrder += 10 });
            var fieldOrder = 0;
            foreach (var field in importedRule.Fields ?? []) db.TaxRuleFieldDefinitions.Add(new TaxRuleFieldDefinition { Id = Guid.NewGuid(), TaxRuleSetId = rule.Id, FieldCode = field.Code.Trim().ToLowerInvariant(), Label = field.Label.Trim(), DataType = field.DataType.Trim().ToLowerInvariant(), IsRequired = field.Required, DefaultValueJson = field.Default?.GetRawText() ?? "null", ValidationJson = field.Validation?.GetRawText() ?? "{}", DisplayOrder = fieldOrder += 10, HelpText = field.HelpText?.Trim() ?? string.Empty });
            foreach (var bracket in importedRule.Brackets ?? []) db.TaxRuleBrackets.Add(new TaxRuleBracket { Id = Guid.NewGuid(), TaxRuleSetId = rule.Id, Sequence = bracket.Sequence, UpperBoundAmount = bracket.UpperBoundAmount, FixedAmount = bracket.FixedAmount, Rate = bracket.Rate, Notes = bracket.Notes?.Trim() ?? string.Empty });
            foreach (var form in importedRule.Forms ?? []) db.TaxFormRequirements.Add(new TaxFormRequirement { Id = Guid.NewGuid(), TaxRuleSetId = rule.Id, FormCode = form.Code.Trim().ToUpperInvariant(), Name = form.Name.Trim(), FilingFrequency = form.FilingFrequency?.Trim() ?? string.Empty, DeliveryChannel = form.DeliveryChannel?.Trim() ?? string.Empty, DueRule = form.DueRule?.Trim() ?? string.Empty, Notes = form.Notes?.Trim() ?? string.Empty });
            foreach (var test in importedRule.Tests ?? []) db.TaxRuleTestCases.Add(new TaxRuleTestCase { Id = Guid.NewGuid(), TaxRuleSetId = rule.Id, Name = test.Name.Trim(), InputJson = test.InputJson, ExpectedOutputJson = test.ExpectedOutputJson, IsRequiredForActivation = true });
        }
        await db.SaveChangesAsync(cancellationToken);
        return TaxAdministrationResult.Success(package.Id);
    }

    private async Task<(string Content, string ContentType, string Hash)> DownloadSafeSourceAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback || IPAddress.TryParse(uri.Host, out _))
            throw new InvalidOperationException("Only public HTTPS host names may be captured.");
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress)) throw new InvalidOperationException("The source host does not resolve to a public address.");
        var client = httpClientFactory.CreateClient("TaxSourceCapture");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Source returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        if (response.Headers.Location is not null) throw new InvalidOperationException("Redirects are not allowed when capturing a tax source.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Source content must be between 1 byte and 8 MB.");
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var content = contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(bytes)
            : "base64:" + Convert.ToBase64String(bytes);
        return (content, contentType, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 || bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
    }

    private const string CurrentTaxEngineVersion = "1.0";
    private static Task<bool> IsApprovedPackageAsync(BrassLedgerDbContext dbContext, Guid companyId, Guid packageId, CancellationToken cancellationToken) =>
        dbContext.TaxContentPackages.AnyAsync(package => package.CompanyId == companyId && package.Id == packageId && package.Status == "Approved", cancellationToken);
    private static Task<bool> IsApprovedPackageRuleAsync(BrassLedgerDbContext dbContext, TaxRuleSet rule, CancellationToken cancellationToken) =>
        rule.TaxContentPackageId.HasValue
            ? dbContext.TaxContentPackages.AnyAsync(package => package.Id == rule.TaxContentPackageId.Value && package.Status == "Approved", cancellationToken)
            : Task.FromResult(false);
    private static bool IsCompatibleWithCurrentEngine(string minimumVersion) => Version.TryParse(minimumVersion, out var required) && Version.TryParse(CurrentTaxEngineVersion, out var current) && required <= current;
    private static bool TryReadNumber(string json, string property, out decimal value) { value = 0; using var document = System.Text.Json.JsonDocument.Parse(json); return document.RootElement.TryGetProperty(property, out var element) && element.TryGetDecimal(out value); }
    private static decimal EvaluateRule(TaxRuleSet rule, IEnumerable<TaxRuleParameter> parameters, IEnumerable<TaxRuleBracket> brackets, decimal grossPay, int allowances, string filingStatus = "Single", string payrollFrequency = "Biweekly", decimal otherStateWithholding = 0m)
        => TaxRuleEvaluator.Evaluate(rule, parameters, brackets, new TaxRuleEvaluationContext(grossPay, allowances, filingStatus, payrollFrequency, OtherStateWithholding: otherStateWithholding));

    private static bool IsJson(string value) { try { using var _ = System.Text.Json.JsonDocument.Parse(value); return true; } catch { return false; } }
    private static bool IsJsonObject(string value) { try { using var document = JsonDocument.Parse(value); return document.RootElement.ValueKind == JsonValueKind.Object; } catch { return false; } }

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
        var httpContext = httpContextAccessor.HttpContext;
        var claimValue = httpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (Guid.TryParse(claimValue, out var companyId))
        {
            return companyId;
        }

        if (httpContext is not null) throw new UnauthorizedAccessException("An authenticated company context is required.");
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
