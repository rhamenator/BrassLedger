using BrassLedger.Application.Taxation;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Infrastructure.Tests;

public sealed class TaxAdministrationServiceTests : IDisposable
{
    private readonly string _contentRootPath;

    public TaxAdministrationServiceTests()
    {
        _contentRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.TaxAdministration.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRootPath);
    }

    [Fact]
    public async Task GetSnapshotAsync_SeedsEditableTaxRuleLibrary()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaxAdministrationService>();

        var snapshot = await service.GetSnapshotAsync();

        Assert.Contains(snapshot.RuleSets, rule => rule.Code == "UT-WH");
        Assert.Contains(snapshot.RuleSets, rule => rule.Code == "NJ-WH");
        Assert.Equal(50, snapshot.StateJurisdictions.Count);
        Assert.Contains(snapshot.StateJurisdictions, state => state.Code == "MI" && state.Name == "Michigan");
        Assert.Contains(snapshot.RuleSets, rule => rule.Code == "LOCAL-E");
        Assert.Contains(snapshot.LegacyArtifacts, artifact => artifact.SourcePath.EndsWith("calc.ovr", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Methods, method => method.Code == "local-code-e");
    }

    [Fact]
    public async Task SaveParameterAsync_PersistsUpdatedTaxRuleValues()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();

        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaxAdministrationService>();
        var snapshot = await service.GetSnapshotAsync();
        var utahRule = snapshot.RuleSets.Single(rule => rule.Code == "UT-WH");
        var allowanceParameter = utahRule.Parameters.Single(parameter => parameter.ParameterCode == "allowance-credit");

        var result = await service.SaveParameterAsync(new SaveTaxRuleParameterRequest(
            utahRule.Id,
            allowanceParameter.Id,
            allowanceParameter.ParameterCode,
            allowanceParameter.Label,
            allowanceParameter.ValueType,
            42.5m,
            allowanceParameter.TextValue,
            allowanceParameter.BooleanValue,
            "Adjusted during regression test.",
            allowanceParameter.DisplayOrder));

        Assert.True(result.Succeeded, result.ErrorMessage);

        var refreshed = await service.GetSnapshotAsync();
        var updatedParameter = refreshed.RuleSets
            .Single(rule => rule.Code == "UT-WH")
            .Parameters
            .Single(parameter => parameter.ParameterCode == "allowance-credit");

        Assert.Equal(42.5m, updatedParameter.NumericValue);
        Assert.Equal("Adjusted during regression test.", updatedParameter.Notes);
    }

    [Fact]
    public async Task TaxContentPackage_ActivatesOnlyAfterLinkedRuleAndFlexibleRegressionCaseValidate()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaxAdministrationService>();
        var packageResult = await service.SaveContentPackageAsync(new SaveTaxContentPackageRequest(null, "NJ-2026", "2026.2", new DateOnly(2026, 7, 1), "Draft", "1.0", "{\"jurisdiction\":\"NJ\"}", "State notice", "Adds a future input field."));
        Assert.True(packageResult.Succeeded, packageResult.ErrorMessage);
        var snapshot = await service.GetSnapshotAsync();
        var rule = snapshot.RuleSets.Single(item => item.Code == "NJ-WH");
        var factory = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BrassLedgerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.TaxRuleSets.Single(item => item.Id == rule.Id).TaxContentPackageId = packageResult.SavedId;
            await db.SaveChangesAsync();
        }
        var fieldResult = await service.SaveFieldDefinitionAsync(new SaveTaxRuleFieldDefinitionRequest(rule.Id, null, "nj_new_election", "New Jersey election", "text", true, "null", "{\"maxLength\":20}", 10, "Future state-provided field."));
        var testResult = await service.SaveTestCaseAsync(new SaveTaxRuleTestCaseRequest(rule.Id, null, "Future field baseline", "{\"grossPay\":1000,\"nj_new_election\":\"A\"}", "{\"withholding\":0}", true));
        Assert.True(fieldResult.Succeeded, fieldResult.ErrorMessage);
        Assert.True(testResult.Succeeded, testResult.ErrorMessage);
        var validation = await service.ValidateContentPackageAsync(packageResult.SavedId!.Value);
        Assert.True(validation.Succeeded, string.Join("; ", validation.Errors));
        var activation = await service.ActivateContentPackageAsync(packageResult.SavedId!.Value);
        Assert.True(activation.Succeeded, activation.ErrorMessage);
        Assert.Equal("Approved", (await service.GetSnapshotAsync()).Packages.Single(package => package.Id == packageResult.SavedId).Status);

        var editApprovedPackage = await service.SaveContentPackageAsync(new SaveTaxContentPackageRequest(packageResult.SavedId, "NJ-2026", "2026.2", new DateOnly(2026, 7, 1), "Draft", "1.0", "{\"jurisdiction\":\"NJ\"}", "State notice", "Attempted rewrite."));
        var editApprovedRule = await service.SaveFieldDefinitionAsync(new SaveTaxRuleFieldDefinitionRequest(rule.Id, null, "after_approval", "After approval", "text", false, "null", "{}", 20, "Must be rejected."));
        Assert.False(editApprovedPackage.Succeeded);
        Assert.False(editApprovedRule.Succeeded);
    }

    [Fact]
    public async Task TaxContentPackage_ValidatesArchivedLocalAllowanceMethod()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaxAdministrationService>();
        var package = await service.SaveContentPackageAsync(new SaveTaxContentPackageRequest(null, "LOCAL-2026", "1.0", new DateOnly(2026, 1, 1), "Draft", "1.0", "{}", "Test", "Local allowance method."));
        Assert.True(package.Succeeded, package.ErrorMessage);
        var rule = await service.SaveRuleSetAsync(new SaveTaxRuleSetRequest(null, "LOCAL-ALLOWANCE", "CITY-TEST", "Test City", "Local", "Employee withholding", "local-code-e", "Per payroll", new DateOnly(2026, 1, 1), "Test", "", false, false, true, true, package.SavedId, "1.0", "1.0"));
        Assert.True(rule.Succeeded, rule.ErrorMessage);
        foreach (var parameter in new[]
        {
            new SaveTaxRuleParameterRequest(rule.SavedId!.Value, null, "allowance-percent", "Allowance percent", "number", .10m, "", null, "", 1),
            new SaveTaxRuleParameterRequest(rule.SavedId!.Value, null, "allowance-minimum", "Allowance minimum", "number", 0m, "", null, "", 2),
            new SaveTaxRuleParameterRequest(rule.SavedId!.Value, null, "allowance-maximum", "Allowance maximum", "number", 250m, "", null, "", 3),
            new SaveTaxRuleParameterRequest(rule.SavedId!.Value, null, "dependent-allowance", "Dependent allowance", "number", 25m, "", null, "", 4),
            new SaveTaxRuleParameterRequest(rule.SavedId!.Value, null, "tax-rate", "Tax rate", "number", .01m, "", null, "", 5)
        }) Assert.True((await service.SaveParameterAsync(parameter)).Succeeded);
        var regression = await service.SaveTestCaseAsync(new SaveTaxRuleTestCaseRequest(rule.SavedId!.Value, null, "Two dependent allowances", "{\"grossPay\":1000,\"allowances\":2}", "{\"amount\":8.50}", true));
        Assert.True(regression.Succeeded, regression.ErrorMessage);
        var validation = await service.ValidateContentPackageAsync(package.SavedId!.Value);
        Assert.True(validation.Succeeded, string.Join("; ", validation.Errors));
    }

    [Fact]
    public async Task ImportTaxContentDocumentAsync_ImportsUtahPackageAsInactiveDraft()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaxAdministrationService>();
        var packagePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ut/2026-04-01.json"));

        var result = await service.ImportTaxContentDocumentAsync(await File.ReadAllTextAsync(packagePath));

        Assert.True(result.Succeeded, result.ErrorMessage);
        var snapshot = await service.GetSnapshotAsync();
        var package = Assert.Single(snapshot.Packages, item => item.Id == result.SavedId);
        var rule = Assert.Single(snapshot.RuleSets, item => item.TaxContentPackageId == package.Id);
        Assert.Equal("Draft", package.Status);
        Assert.Equal("allowance-phaseout", rule.CalculationMethod);
        Assert.False(rule.IsActive);
        Assert.Equal(4, rule.FieldDefinitions.Count);
        Assert.Equal(2, rule.FormRequirements.Count);
        Assert.Equal(6, rule.TestCases.Count);
        Assert.Contains(rule.Parameters, parameter => parameter.ParameterCode == "daily-married-threshold" && parameter.NumericValue == 72m);
        var validation = await service.ValidateContentPackageAsync(package.Id);
        Assert.True(validation.Succeeded, string.Join("; ", validation.Errors));
    }

    [Fact]
    public async Task StateReferenceCatalog_CoversEveryStateAndDcWithStableRelationships()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/state-reference-2026.json"));
        using var catalog = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath));
        var jurisdictions = catalog.RootElement.GetProperty("jurisdictions").EnumerateArray().ToArray();

        Assert.Equal(51, jurisdictions.Length);
        Assert.Equal(51, jurisdictions.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(51, jurisdictions.Select(item => item.GetProperty("code").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(15, jurisdictions.Count(item => item.GetProperty("pit").GetProperty("type").GetString() == "flat"));
        Assert.Equal(27, jurisdictions.Count(item => item.GetProperty("pit").GetProperty("type").GetString() == "progressive"));
        Assert.Equal(9, jurisdictions.Count(item => item.GetProperty("pit").GetProperty("type").GetString() == "none"));
        Assert.All(jurisdictions, item => Assert.Contains(item.GetProperty("relationships").EnumerateArray(), relationship => relationship.GetProperty("type").GetString() == "ContainedBy" && relationship.GetProperty("targetJurisdictionId").GetString() == "jurisdiction-us"));
        Assert.Equal("DraftCaptured", jurisdictions.Single(item => item.GetProperty("code").GetString() == "UT").GetProperty("formulaCoverage").GetString());
        Assert.Equal("OfficialSourceCaptured", jurisdictions.Single(item => item.GetProperty("code").GetString() == "ME").GetProperty("formulaCoverage").GetString());

        foreach (var jurisdiction in jurisdictions.Where(item => item.GetProperty("formulaCoverage").GetString() == "OfficialSourceCaptured"))
        {
            var relativePath = jurisdiction.GetProperty("sourceCapture").GetString();
            Assert.False(string.IsNullOrWhiteSpace(relativePath));
            var sourceCapturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(catalogPath)!, relativePath!));
            Assert.True(File.Exists(sourceCapturePath), $"Missing source capture for {jurisdiction.GetProperty("code").GetString()}: {sourceCapturePath}");
            using var sourceCapture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(sourceCapturePath));
            Assert.Equal(jurisdiction.GetProperty("id").GetString(), sourceCapture.RootElement.GetProperty("jurisdictionId").GetString());
            Assert.False(sourceCapture.RootElement.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
        }
    }

    [Fact]
    public async Task MaineSourceCapture_PreservesOfficialFormulaAndActivationBlockers()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/me/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var rules = root.GetProperty("capturedRules");

        Assert.Equal("OfficialSourceCaptured", root.GetProperty("status").GetString());
        Assert.Equal(5300, rules.GetProperty("allowanceDeduction").GetInt32());
        Assert.Equal(3, rules.GetProperty("annualWithholdingSchedules").GetProperty("single").GetArrayLength());
        Assert.Equal(3, rules.GetProperty("annualWithholdingSchedules").GetProperty("married").GetArrayLength());
        Assert.Equal(3, rules.GetProperty("officialExamples").GetArrayLength());
        Assert.True(root.GetProperty("review").GetProperty("formulaTranscribed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    private ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBrassLedgerInfrastructure(configuration, _contentRootPath, seedSampleData: true);
        return serviceCollection.BuildServiceProvider();
    }

    public void Dispose()
    {
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
}
