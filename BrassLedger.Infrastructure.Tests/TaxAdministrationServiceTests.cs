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
        Assert.Equal(41, jurisdictions.Count(item => item.GetProperty("formulaCoverage").GetString() == "OfficialSourceCaptured"));
        Assert.Equal(9, jurisdictions.Count(item => item.GetProperty("formulaCoverage").GetString() == "NotApplicableForPIT"));
        Assert.Equal("DraftCaptured", jurisdictions.Single(item => item.GetProperty("code").GetString() == "UT").GetProperty("formulaCoverage").GetString());
        Assert.Equal("OfficialSourceCaptured", jurisdictions.Single(item => item.GetProperty("code").GetString() == "ME").GetProperty("formulaCoverage").GetString());

        foreach (var jurisdiction in jurisdictions.Where(item => item.GetProperty("formulaCoverage").GetString() == "OfficialSourceCaptured"))
        {
            var relativePath = jurisdiction.GetProperty("sourceCapture").GetString();
            Assert.False(string.IsNullOrWhiteSpace(relativePath));
            var sourceCapturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(catalogPath)!, relativePath!));
            Assert.True(File.Exists(sourceCapturePath), $"Missing source capture for {jurisdiction.GetProperty("code").GetString()}: {sourceCapturePath}");
            using var sourceCapture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(sourceCapturePath));
            var expectedJurisdictionId = jurisdiction.GetProperty("id").GetString();
            var root = sourceCapture.RootElement;
            var captureContainsJurisdiction = root.TryGetProperty("jurisdictionId", out var capturedJurisdictionId)
                ? string.Equals(expectedJurisdictionId, capturedJurisdictionId.GetString(), StringComparison.OrdinalIgnoreCase)
                : root.GetProperty("jurisdictions").EnumerateArray().Any(item =>
                    string.Equals(expectedJurisdictionId, item.GetProperty("jurisdictionId").GetString(), StringComparison.OrdinalIgnoreCase));
            Assert.True(captureContainsJurisdiction, $"Source capture does not contain {expectedJurisdictionId}: {sourceCapturePath}");
            Assert.False(sourceCapture.RootElement.GetProperty("review").GetProperty("activationAllowed").GetBoolean());

            if (jurisdiction.TryGetProperty("localSourceCapture", out var localSourceCaptureProperty))
            {
                var localRelativePath = localSourceCaptureProperty.GetString();
                var localSourceCapturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(catalogPath)!, localRelativePath!));
                Assert.True(File.Exists(localSourceCapturePath), $"Missing local source capture for {jurisdiction.GetProperty("code").GetString()}: {localSourceCapturePath}");
                using var localSourceCapture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(localSourceCapturePath));
                Assert.False(localSourceCapture.RootElement.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
            }
        }
    }

    [Fact]
    public async Task MarylandLocalSourceCapture_CoversEveryCountyAndBaltimoreCityWithoutAssumingFlatRates()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/state-reference-2026.json"));
        using var catalog = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath));
        var maryland = catalog.RootElement.GetProperty("jurisdictions").EnumerateArray()
            .Single(item => item.GetProperty("code").GetString() == "MD");
        var relativePath = maryland.GetProperty("localSourceCapture").GetString();
        var capturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(catalogPath)!, relativePath!));

        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var localJurisdictions = root.GetProperty("localJurisdictions").EnumerateArray().ToArray();

        Assert.Equal("employeeResidence", root.GetProperty("selection").GetProperty("basis").GetString());
        Assert.True(root.GetProperty("selection").GetProperty("workLocationIsNotSelectionBasis").GetBoolean());
        Assert.Equal(24, localJurisdictions.Length);
        Assert.Equal(23, localJurisdictions.Count(item => item.GetProperty("type").GetString() == "County"));
        Assert.Single(localJurisdictions, item => item.GetProperty("type").GetString() == "City" && item.GetProperty("name").GetString() == "Baltimore City");
        Assert.Equal(24, localJurisdictions.Select(item => item.GetProperty("jurisdictionId").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, localJurisdictions.Count(item => item.GetProperty("rateSchedule").GetProperty("type").GetString() == "IncomeTieredRateByFilingStatus"));
        Assert.Contains(localJurisdictions, item => item.GetProperty("name").GetString() == "Anne Arundel County" && item.GetProperty("rateSchedule").GetProperty("singleGroup").GetArrayLength() == 3);
        Assert.Contains(localJurisdictions, item => item.GetProperty("name").GetString() == "Frederick County" && item.GetProperty("rateSchedule").GetProperty("singleGroup").GetArrayLength() == 4);
        Assert.Equal("ContainedBy", root.GetProperty("sharedRelationship").GetProperty("type").GetString());
        Assert.Equal("jurisdiction-us-md", root.GetProperty("sharedRelationship").GetProperty("targetJurisdictionId").GetString());
        Assert.Equal(0.0225m, root.GetProperty("specialRules").GetProperty("nonresidentSpecialTaxRate").GetDecimal());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task IndianaSourceCapture_CoversStateFormulaCountyPrecedenceAndAllCountyRates()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/in/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");
        var counties = root.GetProperty("localJurisdictions").EnumerateArray().ToArray();

        Assert.Equal("jurisdiction-us-in", root.GetProperty("jurisdictionId").GetString());
        Assert.Equal(0.0295m, calculation.GetProperty("stateRate").GetDecimal());
        Assert.Equal(1000, calculation.GetProperty("annualDeductions").GetProperty("personalExemption").GetInt32());
        Assert.Equal(1500, calculation.GetProperty("annualDeductions").GetProperty("additionalDependentExemption").GetInt32());
        Assert.Equal(3000, calculation.GetProperty("annualDeductions").GetProperty("adoptedChildDependentExemption").GetInt32());
        Assert.Equal("residenceCounty", root.GetProperty("selection").GetProperty("precedence")[0].GetProperty("jurisdictionBasis").GetString());
        Assert.Equal("principalWorkCounty", root.GetProperty("selection").GetProperty("precedence")[1].GetProperty("jurisdictionBasis").GetString());
        Assert.True(root.GetProperty("selection").GetProperty("midyearMoveDoesNotChangeSelectedCounty").GetBoolean());

        Assert.Equal(92, counties.Length);
        Assert.Equal(92, counties.Select(item => item.GetProperty("jurisdictionId").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Enumerable.Range(1, 92).Select(value => $"IN-{value:00}"), counties.Select(item => item.GetProperty("code").GetString()));
        Assert.All(counties, item => Assert.Equal("County", item.GetProperty("type").GetString()));
        Assert.Equal(6, counties.Count(item => item.TryGetProperty("changedAfterOctober2025Publication", out var changed) && changed.GetBoolean()));

        var example = root.GetProperty("officialExample");
        Assert.Equal(473.08m, example.GetProperty("taxableWages").GetDecimal());
        Assert.Equal(13.96m, example.GetProperty("stateWithholding").GetDecimal());
        Assert.Equal(4.73m, example.GetProperty("countyWithholding").GetDecimal());
        Assert.Equal(30, root.GetProperty("specialRules").GetProperty("qualifyingNonresidentThirtyDayRule").GetProperty("thresholdDays").GetInt32());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task NewYorkLocalSourceCapture_KeepsCityAndYonkersResidentAndNonresidentEnginesSeparate()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ny/2026-local-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var rules = root.GetProperty("localRules").EnumerateArray().ToArray();

        Assert.Equal(3, rules.Length);
        Assert.Contains(rules, item => item.GetProperty("code").GetString() == "NYC" && item.GetProperty("supplementalWageRate").GetDecimal() == 0.0425m);
        Assert.Contains(rules, item => item.GetProperty("code").GetString() == "YONKERS-RESIDENT" && item.GetProperty("topIncomeMethodStateTaxMultiplier").GetDecimal() == 0.1675m);
        Assert.Contains(rules, item => item.GetProperty("code").GetString() == "YONKERS-NONRESIDENT" && item.GetProperty("rate").GetDecimal() == 0.005m);
        Assert.Equal(6, rules.SelectMany(item => item.GetProperty("officialExamples").EnumerateArray()).Count());
        Assert.All(root.GetProperty("sources").EnumerateArray().Where(item => item.GetProperty("url").GetString()!.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)), source =>
            Assert.Matches("^[a-f0-9]{64}$", source.GetProperty("sha256").GetString()));
        Assert.False(root.GetProperty("review").GetProperty("completeExactCalculationTablesTranscribed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
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

    [Fact]
    public async Task MichiganSourceCapture_SeparatesStateDetroitAndPendingCityRules()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/mi/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var state = root.GetProperty("stateWithholding");
        var cities = root.GetProperty("localJurisdictions").EnumerateArray().ToArray();
        var detroit = Assert.Single(cities, item => item.GetProperty("code").GetString() == "MI-DETROIT");

        Assert.Equal(0.0425m, state.GetProperty("rate").GetDecimal());
        Assert.Equal(5900, state.GetProperty("annualPersonalAndDependencyExemption").GetInt32());
        Assert.Equal(6, state.GetProperty("reciprocalStates").GetArrayLength());
        Assert.Equal(24, cities.Length);
        Assert.Equal(24, cities.Select(item => item.GetProperty("jurisdictionId").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(23, cities.Count(item => item.GetProperty("rateCaptureStatus").GetString() == "PendingOfficialLocalPublication"));
        Assert.Equal("Exact2026Publication", detroit.GetProperty("rateCaptureStatus").GetString());
        Assert.Equal(0.024m, detroit.GetProperty("residentRate").GetDecimal());
        Assert.Equal(0.012m, detroit.GetProperty("nonresidentRate").GetDecimal());
        Assert.Equal(600, detroit.GetProperty("annualExemption").GetInt32());
        Assert.Equal(3.97m, detroit.GetProperty("officialExample").GetProperty("withholding").GetDecimal());
        Assert.True(root.GetProperty("localSelectionModel").GetProperty("multipleCityWithholdingRequired").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("rawSourcesChecksummed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task OhioSourceCapture_PreservesMidyearTablesAndSeparateLocalSelectionModels()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/oh/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var versions = root.GetProperty("stateWithholdingVersions").EnumerateArray().ToArray();
        var former = versions[0];
        var current = versions[1];

        Assert.Equal(2, versions.Length);
        Assert.Equal("2026-01-01", former.GetProperty("effectiveOn").GetString());
        Assert.Equal("2026-07-31", former.GetProperty("effectiveThrough").GetString());
        Assert.Equal("2026-08-01", current.GetProperty("effectiveOn").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, current.GetProperty("effectiveThrough").ValueKind);
        Assert.All(versions, version => Assert.Equal(5, version.GetProperty("tables").GetArrayLength()));
        Assert.All(versions.SelectMany(version => version.GetProperty("tables").EnumerateArray()), table => Assert.Equal(3, table.GetProperty("brackets").GetArrayLength()));

        var formerWeekly = former.GetProperty("tables")[0];
        var currentWeekly = current.GetProperty("tables")[0];
        var currentMonthlyTop = current.GetProperty("tables")[3].GetProperty("brackets")[2];
        Assert.Equal(0.01775m, formerWeekly.GetProperty("brackets")[0].GetProperty("rate").GetDecimal());
        Assert.Equal(0.03640m, formerWeekly.GetProperty("brackets")[2].GetProperty("rate").GetDecimal());
        Assert.Equal(0.01600m, currentWeekly.GetProperty("brackets")[0].GetProperty("rate").GetDecimal());
        Assert.Equal(8.02m, currentWeekly.GetProperty("brackets")[1].GetProperty("baseTax").GetDecimal());
        Assert.Equal(50.54m, currentWeekly.GetProperty("brackets")[2].GetProperty("baseTax").GetDecimal());
        Assert.Equal(218.99m, currentMonthlyTop.GetProperty("baseTax").GetDecimal());
        Assert.Equal(0.03400m, currentMonthlyTop.GetProperty("rate").GetDecimal());

        var school = root.GetProperty("schoolDistrictWithholding");
        var municipal = root.GetProperty("municipalWithholding");
        Assert.Equal("employeeResidenceAddress", school.GetProperty("selectionBasis").GetString());
        Assert.Contains("exemption", school.GetProperty("traditionalTaxBase").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without personal-exemption", school.GetProperty("earnedIncomeTaxBase").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(20, municipal.GetProperty("generalTransientWorkThresholdDays").GetInt32());
        Assert.Equal(12, municipal.GetProperty("petroleumRefineryThresholdDays").GetInt32());
        Assert.False(root.GetProperty("review").GetProperty("schoolDistrictRatesTranscribed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("municipalRatesTranscribed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task IllinoisSourceCapture_PreservesAllowanceClassesAndMultiStateAllocation()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/il/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");
        var workRules = root.GetProperty("residencyAndWorkRules");

        Assert.Equal("jurisdiction-us-il", root.GetProperty("jurisdictionId").GetString());
        Assert.Equal(0.0495m, calculation.GetProperty("rate").GetDecimal());
        Assert.Equal(2925, calculation.GetProperty("annualLine1AllowanceValue").GetInt32());
        Assert.Equal(1000, calculation.GetProperty("annualLine2AllowanceValue").GetInt32());
        Assert.Equal(8.38m, calculation.GetProperty("officialTableExample").GetProperty("withholding").GetDecimal());
        Assert.Equal(30, workRules.GetProperty("nonlocalizedThirtyDayThreshold").GetInt32());
        Assert.Equal(0.14m, workRules.GetProperty("officialAllocationExample").GetProperty("illinoisFraction").GetDecimal());
        Assert.Equal(4, workRules.GetProperty("reciprocalStates").GetArrayLength());
        Assert.All(root.GetProperty("sources").EnumerateArray(), source => Assert.Matches("^[a-f0-9]{64}$", source.GetProperty("sha256").GetString()));
        Assert.False(root.GetProperty("review").GetProperty("roundingIndependentlyVerified").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task ColoradoSourceCapture_PreservesCertificatePrecedenceAndAnnualizedFormula()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/co/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");
        var defaults = calculation.GetProperty("defaultAnnualReduction");
        var periods = calculation.GetProperty("payPeriodsPerYear");

        Assert.Equal("jurisdiction-us-co", root.GetProperty("jurisdictionId").GetString());
        Assert.Equal(0.044m, calculation.GetProperty("rate").GetDecimal());
        Assert.Equal(11000, defaults.GetProperty("MarriedFilingJointly").GetInt32());
        Assert.Equal(5500, defaults.GetProperty("Other").GetInt32());
        Assert.Equal(5500, defaults.GetProperty("MissingCertificates").GetInt32());
        Assert.Equal(52, periods.GetProperty("Weekly").GetInt32());
        Assert.Equal(260, periods.GetProperty("Daily").GetInt32());
        Assert.Equal(4, calculation.GetProperty("certificatePrecedence").GetArrayLength());
        Assert.False(calculation.GetProperty("outsideAdjustmentAllowed").GetBoolean());
        Assert.Empty(root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").EnumerateArray());
        Assert.False(root.GetProperty("review").GetProperty("rawSourcesChecksummed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task NorthCarolinaSourceCapture_PreservesRoundingSupplementalAndNonresidentAlienBranches()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/nc/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");
        var periods = calculation.GetProperty("percentageMethodPeriods").EnumerateArray().ToArray();
        var nonresidentAlien = root.GetProperty("nonresidentAlienBranch");

        Assert.Equal("jurisdiction-us-nc", root.GetProperty("jurisdictionId").GetString());
        Assert.Equal(0.0399m, calculation.GetProperty("individualIncomeTaxRate").GetDecimal());
        Assert.Equal(0.0409m, calculation.GetProperty("withholdingRate").GetDecimal());
        Assert.Equal(2500, calculation.GetProperty("annualAllowanceValue").GetInt32());
        Assert.Equal(4, periods.Length);
        Assert.Contains(periods, period => period.GetProperty("frequency").GetString() == "Weekly" && period.GetProperty("allowanceValue").GetDecimal() == 48.08m);
        Assert.Contains(periods, period => period.GetProperty("frequency").GetString() == "Monthly" && period.GetProperty("standardDeduction").GetProperty("HeadOfHousehold").GetDecimal() == 1593.75m);
        Assert.Equal(4, calculation.GetProperty("officialPercentageExample").GetProperty("withholding").GetDecimal());
        Assert.Equal(2, calculation.GetProperty("supplementalWages").GetProperty("whenRegularWagesHadWithholding").GetArrayLength());
        Assert.Equal(44, nonresidentAlien.GetProperty("additionalWithholding").GetProperty("Monthly").GetInt32());
        Assert.Equal(21, nonresidentAlien.GetProperty("officialLowWageExample").GetProperty("limitedWithholding").GetInt32());
        Assert.Equal(3, root.GetProperty("filing").GetProperty("frequencies").GetArrayLength());
        Assert.Matches("^[a-f0-9]{64}$", root.GetProperty("sources")[0].GetProperty("sha256").GetString());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task ArizonaSourceCapture_PreservesElectionDefaultsAndConditionalNonresidentThreshold()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/az/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");
        var workRules = root.GetProperty("residencyAndWorkRules");

        Assert.Equal(7, calculation.GetProperty("allowedRates").GetArrayLength());
        Assert.Equal(0.02m, calculation.GetProperty("missingCertificateDefaultRate").GetDecimal());
        Assert.Equal(5, calculation.GetProperty("missingCertificateAfterDays").GetInt32());
        Assert.Equal(60, workRules.GetProperty("nonresidentGeneralThresholdDays").GetInt32());
        Assert.True(workRules.GetProperty("underThresholdExemptionIsConditional").GetBoolean());
        Assert.Equal(3, workRules.GetProperty("underThresholdConditions").GetArrayLength());
        Assert.Equal("A1-E", root.GetProperty("decemberEmployerElection").GetProperty("form").GetString());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task IdahoSourceCapture_PreservesBoth2026VersionsAndSourceConflict()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/id/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var versions = root.GetProperty("calculationVersions").EnumerateArray().ToArray();

        Assert.Equal(2, versions.Length);
        Assert.Equal(15000, versions[0].GetProperty("percentageThresholds").GetProperty("Annual").GetProperty("SingleOrHeadOfHousehold").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, versions[0].GetProperty("childTaxCreditAllowanceValue").ValueKind);
        Assert.Equal(16100, versions[1].GetProperty("percentageThresholds").GetProperty("Annual").GetProperty("SingleOrHeadOfHousehold").GetInt32());
        Assert.Equal(0, versions[1].GetProperty("childTaxCreditAllowanceValue").GetInt32());
        Assert.Equal(31, root.GetProperty("calculation").GetProperty("officialRevisedExample").GetProperty("withholding").GetInt32());
        Assert.False(root.GetProperty("calculation").GetProperty("officialAnnualizedExampleConflict").GetProperty("mayBeUsedAsVerifiedRegressionCase").GetBoolean());
        Assert.All(root.GetProperty("sources").EnumerateArray(), source => Assert.Matches("^[a-f0-9]{64}$", source.GetProperty("sha256").GetString()));
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task MississippiSourceCapture_PreservesDollarExemptionAndMultiStateAllocation()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ms/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");
        var deductions = calculation.GetProperty("standardDeductions");

        Assert.Equal(0.04m, calculation.GetProperty("rate").GetDecimal());
        Assert.Equal(10000, calculation.GetProperty("annualZeroRateAmount").GetInt32());
        Assert.Equal(2300, deductions.GetProperty("Single").GetInt32());
        Assert.Equal(4600, deductions.GetProperty("MarriedSpouseNotEmployed").GetInt32());
        Assert.Equal("money", root.GetProperty("employeeInputs")[1].GetProperty("dataType").GetString());
        Assert.Equal(6, calculation.GetProperty("derivedValidationCase").GetProperty("withholding").GetInt32());
        Assert.Contains("mississippiEarnings / totalEarnings", root.GetProperty("residencyAndWorkRules").GetProperty("partlyInsideAndOutsideAllocation").GetProperty("formula").GetString());
        Assert.False(root.GetProperty("review").GetProperty("rawSourcesChecksummed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task AlabamaSourceCapture_PreservesIncomeSensitiveDeductionsAndSafeHarbor()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/al/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(4, calculation.GetProperty("standardDeductionSchedules").EnumerateObject().Count());
        Assert.Equal(8500, calculation.GetProperty("standardDeductionSchedules").GetProperty("MarriedFilingJointly").GetProperty("lowAmount").GetInt32());
        Assert.Equal(300, calculation.GetProperty("dependentDeductionPerDependent")[2].GetProperty("amount").GetInt32());
        Assert.Equal(29.59m, calculation.GetProperty("officialExample").GetProperty("periodicWithholding").GetDecimal());
        Assert.Equal(0.05m, calculation.GetProperty("supplementalFlatRate").GetDecimal());
        Assert.Equal(30, root.GetProperty("residencyAndWorkRules").GetProperty("nonresidentSafeHarborDays").GetInt32());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task ArkansasSourceCapture_PreservesMidpointFormulaHighIncomePhaseInAndTexarkana()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ar/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");
        var brackets = calculation.GetProperty("annualGrossTaxBrackets").EnumerateArray().ToArray();

        Assert.Equal(2470, calculation.GetProperty("annualStandardDeduction").GetInt32());
        Assert.Equal(37, brackets.Length);
        Assert.Equal(29, calculation.GetProperty("annualPersonalCreditPerExemption").GetInt32());
        Assert.Equal(23050, calculation.GetProperty("officialExample").GetProperty("normalizedIncome").GetInt32());
        Assert.Equal(36.50m, calculation.GetProperty("officialExample").GetProperty("periodicWithholding").GetDecimal());
        Assert.True(root.GetProperty("residencyAndWorkRules").GetProperty("estimateTrueUpRequired").GetBoolean());
        Assert.True(root.GetProperty("texarkanaBorderCityExemption").GetProperty("addressBoundaryRequired").GetBoolean());
        Assert.All(root.GetProperty("sources").EnumerateArray(), source => Assert.Matches("^[a-f0-9]{64}$", source.GetProperty("sha256").GetString()));
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task DelawareSourceCapture_PreservesBracketsExamplesAndEighthMonthlyFiling()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/de/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(7, calculation.GetProperty("annualTaxBrackets").GetArrayLength());
        Assert.Equal(0.066m, calculation.GetProperty("annualTaxBrackets")[6].GetProperty("rate").GetDecimal());
        Assert.Equal(110, calculation.GetProperty("annualExemptionCreditPerAllowance").GetInt32());
        Assert.Equal(3, calculation.GetProperty("officialExamples").GetArrayLength());
        Assert.Equal(3, root.GetProperty("filing").GetProperty("frequencies").GetArrayLength());
        Assert.Equal(8, root.GetProperty("filing").GetProperty("frequencies")[2].GetProperty("periodEndDays").GetArrayLength());
        Assert.Empty(root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").EnumerateArray());
        Assert.All(root.GetProperty("sources").EnumerateArray(), source => Assert.Matches("^[a-f0-9]{64}$", source.GetProperty("sha256").GetString()));
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task DistrictOfColumbiaSourceCapture_DoesNotPromoteLegacyTablesAs2026Rules()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/dc/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal("MissingOfficialPublication", calculation.GetProperty("current2026FormulaStatus").GetString());
        Assert.False(calculation.GetProperty("legacyPublicationMayBeExecuted").GetBoolean());
        Assert.False(calculation.GetProperty("current2026BracketsTranscribed").GetBoolean());
        Assert.Equal(2016, root.GetProperty("sources")[1].GetProperty("publicationYear").GetInt32());
        Assert.True(root.GetProperty("filing").GetProperty("monthlyOrQuarterly").GetProperty("electronicFilingRequired").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task GeorgiaSourceCapture_PreservesMayRateBoundaryAndNonresidentDualThreshold()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ga/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var versions = root.GetProperty("calculationVersions").EnumerateArray().ToArray();

        Assert.Equal(2, versions.Length);
        Assert.Equal("2026-05-10", versions[0].GetProperty("effectiveThrough").GetString());
        Assert.Equal(0.0519m, versions[0].GetProperty("rate").GetDecimal());
        Assert.Equal("2026-05-11", versions[1].GetProperty("effectiveOn").GetString());
        Assert.Equal(0.0499m, versions[1].GetProperty("rate").GetDecimal());
        Assert.Equal(8, root.GetProperty("calculation").GetProperty("postHb463PeriodicDeductions").GetArrayLength());
        Assert.Equal(0.05m, root.GetProperty("residencyAndWorkRules").GetProperty("percentageThreshold").GetDecimal());
        Assert.Equal(5000, root.GetProperty("residencyAndWorkRules").GetProperty("dollarThreshold").GetInt32());
        Assert.False(root.GetProperty("review").GetProperty("preChangeFormulaTranscribed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task HawaiiSourceCapture_PreservesAnnualBracketsAndConditionalSixtyDayRule()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/hi/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(1144, calculation.GetProperty("annualRegularAllowance").GetInt32());
        Assert.Equal(4350, calculation.GetProperty("annualExtraLumpSumAllowance").GetInt32());
        Assert.Equal(8, calculation.GetProperty("annualBrackets").GetProperty("SingleOrHeadOfHousehold").GetArrayLength());
        Assert.Equal(8, calculation.GetProperty("annualBrackets").GetProperty("Married").GetArrayLength());
        Assert.Equal(9.58m, calculation.GetProperty("officialAnnualizedExample").GetProperty("periodicWithholding").GetDecimal());
        Assert.Equal(60, root.GetProperty("residencyAndWorkRules").GetProperty("qualifyingNonresidentDayLimit").GetInt32());
        Assert.Equal(5, root.GetProperty("residencyAndWorkRules").GetProperty("shortTermConditions").GetArrayLength());
        Assert.All(root.GetProperty("sources").EnumerateArray(), source => Assert.Matches("^[a-f0-9]{64}$", source.GetProperty("sha256").GetString()));
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task IowaSourceCapture_PreservesCertificateGenerationsFormulaAndReciprocity()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ia/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(0.038m, calculation.GetProperty("rate").GetDecimal());
        Assert.Equal(40, calculation.GetProperty("legacyCertificate").GetProperty("annualAllowanceDollarsPerClaimedAllowance").GetInt32());
        Assert.Equal(6, calculation.GetProperty("modernPeriodicStandardDeductions").GetArrayLength());
        Assert.Equal(59.26m, calculation.GetProperty("officialExamples")[0].GetProperty("withholding").GetDecimal());
        Assert.Equal("IL", root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates")[0].GetString());
        Assert.All(root.GetProperty("sources").EnumerateArray(), source => Assert.Matches("^[a-f0-9]{64}$", source.GetProperty("sha256").GetString()));
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task KansasSourceCapture_PreservesPercentageSchedulesResidentCreditAndAllocation()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ks/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(8, calculation.GetProperty("percentageTables").GetArrayLength());
        Assert.Equal(0.0558m, calculation.GetProperty("percentageTables")[7].GetProperty("married").GetProperty("topRate").GetDecimal());
        Assert.Equal(9160, calculation.GetProperty("allowances").GetProperty("singleHeadOrMarriedSeparate").GetInt32());
        Assert.Equal(41, calculation.GetProperty("officialExample").GetProperty("roundedWithholding").GetInt32());
        Assert.Equal(120, root.GetProperty("residencyAndWorkRules").GetProperty("officialExamples")[0].GetProperty("kansasWithholding").GetInt32());
        Assert.Equal(5, root.GetProperty("filing").GetProperty("frequencies").GetArrayLength());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task KentuckySourceCapture_PreservesFormulaConditionalReciprocityAndPublishedDiscrepancy()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ky/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(3360, calculation.GetProperty("annualStandardDeduction").GetInt32());
        Assert.Equal(0.035m, calculation.GetProperty("rate").GetDecimal());
        Assert.Equal(35640, calculation.GetProperty("officialExamples")[1].GetProperty("statedAnnualTaxableWages").GetInt32());
        Assert.Equal(35730, calculation.GetProperty("officialExamples")[1].GetProperty("formulaExampleThenUsesAnnualTaxableWages").GetInt32());
        Assert.Equal(7, root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").GetArrayLength());
        Assert.True(root.GetProperty("localWithholding").GetProperty("separateLocalOccupationalTaxSupportRequired").GetBoolean());
        Assert.True(root.GetProperty("review").GetProperty("exampleDiscrepancyRecorded").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task LouisianaSourceCapture_PreservesOfficialRateDeductionChoicesAndNoTaxStateRule()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/la/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(0.0309m, calculation.GetProperty("rate").GetDecimal());
        Assert.Equal(12500, calculation.GetProperty("annualStandardDeductions").GetProperty("SingleOrMarriedSeparate").GetInt32());
        Assert.Equal(25000, calculation.GetProperty("annualStandardDeductions").GetProperty("MarriedJointHeadOrQualifyingSurvivingSpouse").GetInt32());
        Assert.Equal(112.432m, calculation.GetProperty("officialExamples")[1].GetProperty("publishedWithholding").GetDecimal());
        Assert.Contains("no-tax state", root.GetProperty("residencyAndWorkRules").GetProperty("residentOtherStateWithoutIncomeTax").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task MarylandSourceCapture_LinksLocalSchedulesAndPreservesCombinedMethod()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/md/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(7, calculation.GetProperty("periodicDeductions").GetArrayLength());
        Assert.Equal(10, calculation.GetProperty("stateAnnualBrackets").GetProperty("SingleMarriedSeparateOrDependent").GetArrayLength());
        Assert.Equal(0.065m, calculation.GetProperty("lumpSumAnnualBonus").GetProperty("stateRate").GetDecimal());
        Assert.Equal(4, root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").GetArrayLength());
        Assert.Equal("2026-local-source-capture.json", root.GetProperty("localWithholding").GetProperty("sourceCapture").GetString());
        Assert.True(root.GetProperty("localWithholding").GetProperty("stateFormulaDependsOnLocalJurisdiction").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task MassachusettsSourceCapture_PreservesSurtaxStatefulCapAndSupplementalMethod()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ma/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(1107750, calculation.GetProperty("annualSurtaxThreshold").GetInt32());
        Assert.Equal(2000, calculation.GetProperty("annualRetirementContributionDeductionCap").GetInt32());
        Assert.Equal(6, calculation.GetProperty("exemptionFactors").GetArrayLength());
        Assert.Equal(25010, calculation.GetProperty("supplementalWages").GetProperty("officialExample").GetProperty("withholding").GetInt32());
        Assert.Equal(4, root.GetProperty("filing").GetProperty("frequencies").GetArrayLength());
        Assert.False(root.GetProperty("review").GetProperty("rawSourcesChecksummed").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task MinnesotaSourceCapture_PreservesSchedulesReciprocityAndResidentCredit()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/mn/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(5300, calculation.GetProperty("annualAllowanceAmount").GetInt32());
        Assert.Equal(5, calculation.GetProperty("annualSchedules").GetProperty("Single").GetArrayLength());
        Assert.Equal(0.0985m, calculation.GetProperty("annualSchedules").GetProperty("Married")[4].GetProperty("rate").GetDecimal());
        Assert.Equal(0.0625m, calculation.GetProperty("supplementalWages").GetProperty("separatelyPaidAndSeparatelyRecordedFlatRate").GetDecimal());
        Assert.Equal(2, root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").GetArrayLength());
        Assert.Contains("actual", root.GetProperty("residencyAndWorkRules").GetProperty("residentWorkingOutsideMinnesota").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task MissouriSourceCapture_PreservesFormulaAllocationAndThresholdConflict()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/mo/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(5, calculation.GetProperty("annualStandardDeductions").EnumerateObject().Count());
        Assert.Equal(8, calculation.GetProperty("annualBrackets").GetArrayLength());
        Assert.Equal(0.047m, calculation.GetProperty("supplementalWages").GetProperty("flatRate").GetDecimal());
        Assert.Equal(59, calculation.GetProperty("officialExample").GetProperty("roundedPeriodicWithholding").GetInt32());
        Assert.Equal(0.60m, root.GetProperty("residencyAndWorkRules").GetProperty("officialAllocationExample").GetProperty("allocationPercentage").GetDecimal());
        Assert.False(root.GetProperty("review").GetProperty("filingThresholdConflictResolved").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task MontanaSourceCapture_PreservesThreeSchedulesCeilingAndThirtyDayExceptions()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/mt/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(3, calculation.GetProperty("annualSchedules").EnumerateObject().Count());
        Assert.Equal(0.0565m, calculation.GetProperty("annualSchedules").GetProperty("HeadOfHousehold")[2].GetProperty("rate").GetDecimal());
        Assert.Equal("RoundUpToWholeDollar", calculation.GetProperty("rounding").GetProperty("method").GetString());
        Assert.Equal(232, calculation.GetProperty("officialExamples")[1].GetProperty("withholding").GetInt32());
        Assert.Equal(8, root.GetProperty("residencyAndWorkRules").GetProperty("thirtyDayNonresidentExemption").GetProperty("excludedWorkers").GetArrayLength());
        Assert.Equal("ND", root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates")[0].GetString());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task NebraskaSourceCapture_PreservesMinimumAndRetroactiveSevenDayRule()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ne/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(2440, calculation.GetProperty("allowanceValues").GetProperty("Annual").GetInt32());
        Assert.Equal(7, calculation.GetProperty("annualSchedules").GetProperty("SingleOrHeadOfHousehold").GetArrayLength());
        Assert.Equal(0.015m, calculation.GetProperty("specialMinimumWithholding").GetProperty("nominalMinimumRate").GetDecimal());
        Assert.True(calculation.GetProperty("specialMinimumWithholding").GetProperty("lowerAmountAllowedWithDocumentation").GetBoolean());
        Assert.Equal(7, root.GetProperty("residencyAndWorkRules").GetProperty("conferenceOrTrainingExemption").GetProperty("maximumNebraskaDutyDays").GetInt32());
        Assert.Contains("retroactive", root.GetProperty("residencyAndWorkRules").GetProperty("convenienceRule").GetProperty("moreThanSevenDays").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task NewJerseySourceCapture_PreservesFiveTablesDynamicConvenienceAndPaLocalDependency()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/nj/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(5, calculation.GetProperty("annualRateTables").EnumerateObject().Count());
        Assert.Equal(0.118m, calculation.GetProperty("annualRateTables").GetProperty("A")[6].GetProperty("rate").GetDecimal());
        Assert.Equal(1000, calculation.GetProperty("allowanceValues").GetProperty("Annual").GetInt32());
        Assert.Equal(3, root.GetProperty("residencyAndWorkRules").GetProperty("convenienceRule").GetProperty("examplesCurrentlyNamedByAgency").GetArrayLength());
        Assert.Contains("fixed enumeration", root.GetProperty("residencyAndWorkRules").GetProperty("convenienceRule").GetProperty("warning").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(root.GetProperty("localWithholding").GetProperty("crossBorderPennsylvaniaLocalTaxDependency").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task NorthDakotaSourceCapture_PreservesW4GenerationsAndConditionalReciprocity()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/nd/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(4, calculation.GetProperty("methods").GetArrayLength());
        Assert.Equal(5050, calculation.GetProperty("legacyAllowanceValues").GetProperty("Annual").GetInt32());
        Assert.Equal(0.025m, calculation.GetProperty("annualSchedules").GetProperty("HeadOfHousehold")[2].GetProperty("rate").GetDecimal());
        Assert.Equal(14, calculation.GetProperty("officialModernExample").GetProperty("roundedPeriodicWithholding").GetInt32());
        Assert.Equal(2, root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").GetArrayLength());
        Assert.Contains("month", root.GetProperty("residencyAndWorkRules").GetProperty("minnesotaAdditionalCondition").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task NewMexicoSourceCapture_PreservesExact2026SchedulesAndFifteenDayRule()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/nm/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(10, calculation.GetProperty("annualSchedules").GetProperty("Single").GetArrayLength());
        Assert.Equal(0.059m, calculation.GetProperty("annualSchedules").GetProperty("HeadOfHousehold")[9].GetProperty("rate").GetDecimal());
        Assert.Equal(41.80m, calculation.GetProperty("officialExample").GetProperty("totalWithholding").GetDecimal());
        Assert.Equal(15, root.GetProperty("residencyAndWorkRules").GetProperty("shortTermNonresidentException").GetProperty("maximumNewMexicoWorkDays").GetInt32());
        Assert.Equal("2026-01-01", root.GetProperty("filing").GetProperty("electronicFilingAndPaymentRequiredBeginning").GetString());
        Assert.All(root.GetProperty("sources").EnumerateArray(), source => Assert.Matches("^[a-f0-9]{64}$", source.GetProperty("sha256").GetString()));
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task NewYorkSourceCapture_PreservesWholeWageTopMethodAndLocalLink()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ny/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(1000, calculation.GetProperty("annualExemptionAmount").GetInt32());
        Assert.Equal(10, calculation.GetProperty("annualExactSchedules").GetProperty("Single").GetArrayLength());
        Assert.Equal(12, calculation.GetProperty("annualExactSchedules").GetProperty("Married").GetArrayLength());
        Assert.Equal(0.1045m, calculation.GetProperty("topIncomeWholeWageRates").GetProperty("bands")[0].GetProperty("rateOnAllAnnualizedNetWages").GetDecimal());
        Assert.Contains("not an ordinary marginal", calculation.GetProperty("topIncomeWholeWageRates").GetProperty("warning").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("2026-local-source-capture.json", root.GetProperty("localWithholding").GetProperty("sourceCapture").GetString());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task OklahomaSourceCapture_PreservesSchedulesRoundingAndNonresidentThreshold()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ok/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(1000, calculation.GetProperty("annualAllowanceValue").GetInt32());
        Assert.Equal(4, calculation.GetProperty("annualPercentageSchedules").GetProperty("Single").GetArrayLength());
        Assert.Equal(0.045m, calculation.GetProperty("annualPercentageSchedules").GetProperty("Married")[3].GetProperty("rate").GetDecimal());
        Assert.Equal("NearestWholeDollarHalfUp", calculation.GetProperty("rounding").GetProperty("method").GetString());
        Assert.Equal(37, calculation.GetProperty("officialExample").GetProperty("roundedWithholding").GetInt32());
        Assert.Equal(300, root.GetProperty("residencyAndWorkRules").GetProperty("nonresidentQuarterlyDeMinimis").GetProperty("maximumCompensation").GetInt32());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task OregonSourceCapture_PreservesPhaseOutsConflictsAndSupplementalRate()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/or/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(8750, calculation.GetProperty("federalSubtractionCaps").GetProperty("ordinaryAnnualMaximum").GetInt32());
        Assert.Equal(6, calculation.GetProperty("federalSubtractionCaps").GetProperty("Single").GetArrayLength());
        Assert.Equal(0.099m, calculation.GetProperty("annualWagesAtLeast50000Schedules").GetProperty("SingleWithFewerThanThreeAllowances")[1].GetProperty("rate").GetDecimal());
        Assert.Equal(0.08m, calculation.GetProperty("supplementalWages").GetProperty("separatelyPaidAtDifferentTimeOptionalFlatRate").GetDecimal());
        Assert.Equal(1789, calculation.GetProperty("officialExamples")[0].GetProperty("annualWithholding").GetInt32());
        Assert.False(root.GetProperty("review").GetProperty("publicationConflictsResolved").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task PennsylvaniaSourceCapture_PreservesStateReciprocityAndStatefulLocalSelection()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/pa/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var local = root.GetProperty("localWithholding");

        Assert.Equal(0.0307m, root.GetProperty("calculation").GetProperty("rate").GetDecimal());
        Assert.Equal(6, root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").GetArrayLength());
        Assert.Contains("max", local.GetProperty("earnedIncomeTax").GetProperty("formula").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(52, local.GetProperty("localServicesTax").GetProperty("annualCombinedCap").GetInt32());
        Assert.Equal(12000, local.GetProperty("localServicesTax").GetProperty("mandatoryLowIncomeExemptionWhenRateOver10").GetInt32());
        Assert.False(root.GetProperty("review").GetProperty("completeOfficialLocalRateRegisterImported").GetBoolean());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task RhodeIslandSourceCapture_PreservesExemptionPhaseOutAndAllStatusSchedule()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/ri/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var calculation = capture.RootElement.GetProperty("calculation");

        Assert.False(calculation.GetProperty("filingStatusAffectsSchedule").GetBoolean());
        Assert.Equal(290800, calculation.GetProperty("allowanceZeroThresholds").GetProperty("AnnualWagesOver").GetInt32());
        Assert.Equal(3, calculation.GetProperty("annualScheduleAllFilingStatuses").GetArrayLength());
        Assert.Equal(0.0599m, calculation.GetProperty("supplementalWages").GetProperty("flatRate").GetDecimal());
        Assert.Equal(87.57m, calculation.GetProperty("officialExample").GetProperty("withholding").GetDecimal());
        Assert.False(capture.RootElement.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task SouthCarolinaSourceCapture_PreservesEquivalentMethodsAndNoTaxStateBranch()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/sc/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(5000, calculation.GetProperty("annualPersonalAllowance").GetInt32());
        Assert.Equal(7500, calculation.GetProperty("standardDeduction").GetProperty("annualMaximum").GetInt32());
        Assert.Equal(3, calculation.GetProperty("annualSubtractionMethod").GetArrayLength());
        Assert.Equal(549.90m, calculation.GetProperty("officialExample").GetProperty("annualWithholding").GetDecimal());
        Assert.Contains("without", root.GetProperty("residencyAndWorkRules").GetProperty("residentNoTaxStateRule").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task VermontSourceCapture_PreservesHourAllocationAndChildCareContribution()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/vt/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(5400, calculation.GetProperty("annualAllowanceValue").GetInt32());
        Assert.Equal(5, calculation.GetProperty("annualSchedules").GetProperty("Married").GetArrayLength());
        Assert.Equal(19.20m, root.GetProperty("residencyAndWorkRules").GetProperty("nonresidentOfficialExample").GetProperty("vermontWithholding").GetDecimal());
        Assert.Equal(0.06m, calculation.GetProperty("supplementalAndNonwage").GetProperty("nonqualifiedDeferredCompensationRate").GetDecimal());
        Assert.Equal(0.0044m, root.GetProperty("otherPayrollTaxes").GetProperty("childCareContribution").GetProperty("rate").GetDecimal());
        Assert.Equal(0.0011m, root.GetProperty("otherPayrollTaxes").GetProperty("childCareContribution").GetProperty("maximumEmployeeWageRate").GetDecimal());
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task VirginiaSourceCapture_PreservesExemptionClassesReciprocityAndSunsetWarning()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/va/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(8750, calculation.GetProperty("annualWithholdingStandardDeduction").GetInt32());
        Assert.Equal(930, calculation.GetProperty("personalAndDependentExemptionValue").GetInt32());
        Assert.Equal(800, calculation.GetProperty("age65AndBlindExemptionValue").GetInt32());
        Assert.Equal(0.0575m, calculation.GetProperty("supplementalWages").GetProperty("optionalFlatRateWhenRegularWagesHadWithholding").GetDecimal());
        Assert.Equal(5, root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalJurisdictions").GetArrayLength());
        Assert.Contains("sunset", root.GetProperty("captureWarnings")[1].GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task WestVirginiaSourceCapture_PreservesTwoSchedulesAndRetroactiveThirtyDayRule()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/wv/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal("TwoEarnerOrTwoOrMoreJobs", calculation.GetProperty("defaultSchedule").GetString());
        Assert.Equal(5, calculation.GetProperty("annualSchedules").GetProperty("OneEarnerOrOneJob").GetArrayLength());
        Assert.Equal(0.0458m, calculation.GetProperty("annualSchedules").GetProperty("TwoEarnerOrTwoOrMoreJobs")[4].GetProperty("rate").GetDecimal());
        Assert.Equal(5, root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").GetArrayLength());
        Assert.Contains("first 30", root.GetProperty("residencyAndWorkRules").GetProperty("nonresidentThirtyDayException").GetProperty("overThresholdEffect").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task WisconsinSourceCapture_PreservesPhaseOutSupplementalMenuAndStatefulNonresidentRule()
    {
        var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/wi/2026-source-capture.json"));
        using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var root = capture.RootElement;
        var calculation = root.GetProperty("calculation");

        Assert.Equal(400, calculation.GetProperty("annualExemptionDeduction").GetInt32());
        Assert.Equal(0.12m, calculation.GetProperty("alternateDeduction").GetProperty("Single").GetProperty("phaseOutRate").GetDecimal());
        Assert.Equal(4, calculation.GetProperty("supplementalWages").GetProperty("optionalFlatRatesByEstimatedAnnualGross").GetArrayLength());
        Assert.Equal(22.08m, calculation.GetProperty("officialExamples")[2].GetProperty("periodWithholding").GetDecimal());
        Assert.Equal(4, root.GetProperty("residencyAndWorkRules").GetProperty("reciprocalStates").GetArrayLength());
        Assert.Contains("offset", root.GetProperty("residencyAndWorkRules").GetProperty("nonresidentAnnualDeMinimis").GetProperty("thresholdCrossing").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("review").GetProperty("activationAllowed").GetBoolean());
    }

    [Fact]
    public async Task RemainingThirtyFiveStateCaptures_HaveIndividualInactiveAuditableEnvelopes()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tax-content/us/state-reference-2026.json"));
        using var catalog = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath));
        var expectedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AL", "AZ", "AR", "CO", "DE", "DC", "GA", "HI", "ID", "IL", "IA", "KS", "KY", "LA", "MD", "MA", "MN", "MS",
            "MO", "MT", "NE", "NJ", "NM", "NY", "NC", "ND", "OK", "OR", "PA", "RI", "SC", "VT", "VA", "WV", "WI"
        };
        var jurisdictions = catalog.RootElement.GetProperty("jurisdictions").EnumerateArray()
            .Where(item => expectedCodes.Contains(item.GetProperty("code").GetString()!))
            .ToArray();

        Assert.Equal(expectedCodes.Count, jurisdictions.Length);
        foreach (var jurisdiction in jurisdictions)
        {
            var code = jurisdiction.GetProperty("code").GetString()!;
            var relativePath = jurisdiction.GetProperty("sourceCapture").GetString();
            Assert.NotEqual("state-withholding-sources-2026.json", relativePath);
            Assert.Equal($"{code.ToLowerInvariant()}/2026-source-capture.json", relativePath);

            var capturePath = Path.Combine(Path.GetDirectoryName(catalogPath)!, relativePath!);
            using var capture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
            var root = capture.RootElement;
            Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
            Assert.Equal(2026, root.GetProperty("taxYear").GetInt32());
            Assert.Equal("OfficialSourceCaptured", root.GetProperty("status").GetString());
            Assert.Equal(jurisdiction.GetProperty("id").GetString(), root.GetProperty("jurisdictionId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("capturedAtUtc").GetString()));
            Assert.NotEmpty(root.GetProperty("sources").EnumerateArray());
            Assert.All(root.GetProperty("sources").EnumerateArray(), source =>
            {
                Assert.True(Uri.TryCreate(source.GetProperty("url").GetString(), UriKind.Absolute, out _));
                Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("title").GetString()));
                if (source.TryGetProperty("sha256", out var checksum) && checksum.ValueKind == System.Text.Json.JsonValueKind.String)
                    Assert.Matches("^[a-f0-9]{64}$", checksum.GetString());
                else
                {
                    var unavailableReason = source.TryGetProperty("retrievalStatus", out var retrievalStatus)
                        ? retrievalStatus.GetString()
                        : source.TryGetProperty("rawCaptureStatus", out var rawCaptureStatus)
                            ? rawCaptureStatus.GetString()
                            : null;
                    Assert.False(string.IsNullOrWhiteSpace(unavailableReason));
                }
            });
            var review = root.GetProperty("review");
            Assert.False(review.GetProperty("activationAllowed").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(review.GetProperty("requiredNextStep").GetString()));
        }
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
