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
