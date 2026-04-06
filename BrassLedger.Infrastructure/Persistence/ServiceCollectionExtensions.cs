using System.Data;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Application.Accounting;
using BrassLedger.Application.Catalog;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Catalog;
using BrassLedger.Infrastructure.SecurityAdministration;
using BrassLedger.Infrastructure.Security;
using BrassLedger.Infrastructure.Taxation;
using BrassLedger.Application.Security;
using BrassLedger.Application.Taxation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BrassLedger.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBrassLedgerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath,
        bool seedSampleData = false)
    {
        var dataDirectory = BuildDataDirectory(configuration, contentRootPath);
        var keysDirectory = Path.Combine(dataDirectory, "keys");
        Directory.CreateDirectory(keysDirectory);

        var postgresConnectionString =
            configuration.GetConnectionString("Postgres")
            ?? configuration.GetConnectionString("PostgreSql")
            ?? configuration.GetConnectionString("BrassLedgerPostgres");

        var sqliteConnectionString =
            configuration.GetConnectionString("Sqlite")
            ?? configuration.GetConnectionString("BrassLedgerSqlite")
            ?? BuildDefaultSqliteConnectionString(dataDirectory);

        var dataProtectionBuilder = services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
            .SetApplicationName("BrassLedger");

        if (OperatingSystem.IsWindows())
        {
            dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        services.AddDbContextFactory<BrassLedgerDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(postgresConnectionString))
            {
                options.UseNpgsql(postgresConnectionString);
            }
            else
            {
                options.UseSqlite(sqliteConnectionString);
            }
        });

        services.AddHttpContextAccessor();
        services.AddSingleton(Options.Create(BuildBootstrapOptions(configuration, seedSampleData)));
        services.AddSingleton<ISensitiveDataProtector, SensitiveDataProtector>();
        services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IBootstrapWorkspaceService, BootstrapWorkspaceService>();
        services.AddScoped<IBusinessWorkspaceService, BusinessWorkspaceService>();
        services.AddScoped<ISecurityAdministrationService, SecurityAdministrationService>();
        services.AddScoped<ITaxAdministrationService, TaxAdministrationService>();
        services.AddScoped<IFakeDataPopulationService, FakeDataPopulationService>();
        services.AddSingleton<IProductCatalogService, StaticProductCatalogService>();

        return services;
    }

    public static async Task InitializeBrassLedgerAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var bootstrapOptions = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapOptions>>().Value;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureLegacySchemaCompatibilityAsync(dbContext, cancellationToken);
        await BrassLedgerSeedData.SeedAsync(dbContext, passwordHasher, bootstrapOptions, cancellationToken);
    }

    private static BootstrapOptions BuildBootstrapOptions(IConfiguration configuration, bool seedSampleData)
    {
        var options = configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new BootstrapOptions();
        options.SeedSampleData = options.SeedSampleData || seedSampleData;
        return options;
    }

    private static string BuildDataDirectory(IConfiguration configuration, string contentRootPath)
    {
        var configuredDataRoot =
            configuration["Storage:DataRoot"]
            ?? configuration["BrassLedger:DataRoot"];

        if (!string.IsNullOrWhiteSpace(configuredDataRoot))
        {
            var explicitDataDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredDataRoot));
            Directory.CreateDirectory(explicitDataDirectory);
            return explicitDataDirectory;
        }

        var contentRootDataDirectory = Path.Combine(contentRootPath, "App_Data");
        if (TryEnsureWritableDirectory(contentRootDataDirectory))
        {
            return contentRootDataDirectory;
        }

        var localApplicationDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationDataRoot))
        {
            localApplicationDataRoot = Path.GetTempPath();
        }

        var fallbackDataDirectory = Path.Combine(localApplicationDataRoot, "BrassLedger", "App_Data");
        Directory.CreateDirectory(fallbackDataDirectory);
        return fallbackDataDirectory;
    }

    private static bool TryEnsureWritableDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);

            var probeFilePath = Path.Combine(directoryPath, $".write-test-{Guid.NewGuid():N}.tmp");
            using var probeStream = new FileStream(
                probeFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.DeleteOnClose);

            probeStream.WriteByte(0);
            probeStream.Flush();

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string BuildDefaultSqliteConnectionString(string dataDirectory)
    {
        return $"Data Source={Path.Combine(dataDirectory, "brassledger.db")}";
    }

    private static async Task EnsureLegacySchemaCompatibilityAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""AccessRoles"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AccessRoles"" PRIMARY KEY,
                    ""CompanyId"" TEXT NOT NULL,
                    ""Name"" TEXT NOT NULL,
                    ""Description"" TEXT NOT NULL,
                    ""TemplateCode"" TEXT NOT NULL,
                    ""Permissions"" TEXT NOT NULL,
                    ""IsSystemRole"" INTEGER NOT NULL,
                    ""IsActive"" INTEGER NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccessRoles_CompanyId_Name"" ON ""AccessRoles"" (""CompanyId"", ""Name"");",
                cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "UserName", @"ALTER TABLE ""Users"" ADD COLUMN ""UserName"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "PasswordHash", @"ALTER TABLE ""Users"" ADD COLUMN ""PasswordHash"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "SecurityStamp", @"ALTER TABLE ""Users"" ADD COLUMN ""SecurityStamp"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "FailedSignInCount", @"ALTER TABLE ""Users"" ADD COLUMN ""FailedSignInCount"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "LastFailedSignInUtc", @"ALTER TABLE ""Users"" ADD COLUMN ""LastFailedSignInUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "LockoutEndUtc", @"ALTER TABLE ""Users"" ADD COLUMN ""LockoutEndUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "LastSuccessfulSignInUtc", @"ALTER TABLE ""Users"" ADD COLUMN ""LastSuccessfulSignInUtc"" TEXT NULL;", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "LastPasswordChangedUtc", @"ALTER TABLE ""Users"" ADD COLUMN ""LastPasswordChangedUtc"" TEXT NULL;", cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""AuthenticationAuditEntries"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AuthenticationAuditEntries"" PRIMARY KEY,
                    ""UserId"" TEXT NULL,
                    ""CompanyId"" TEXT NULL,
                    ""UserName"" TEXT NOT NULL,
                    ""EventType"" TEXT NOT NULL,
                    ""Succeeded"" INTEGER NOT NULL,
                    ""OccurredUtc"" TEXT NOT NULL,
                    ""IpAddress"" TEXT NOT NULL,
                    ""UserAgent"" TEXT NOT NULL,
                    ""Detail"" TEXT NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleSets"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TaxRuleSets"" PRIMARY KEY,
                    ""CompanyId"" TEXT NOT NULL,
                    ""Code"" TEXT NOT NULL,
                    ""JurisdictionCode"" TEXT NOT NULL,
                    ""JurisdictionName"" TEXT NOT NULL,
                    ""JurisdictionType"" TEXT NOT NULL,
                    ""TaxType"" TEXT NOT NULL,
                    ""CalculationMethod"" TEXT NOT NULL,
                    ""WithholdingFrequency"" TEXT NOT NULL,
                    ""EffectiveOn"" TEXT NOT NULL,
                    ""Source"" TEXT NOT NULL,
                    ""Notes"" TEXT NOT NULL,
                    ""IsEmployerSpecific"" INTEGER NOT NULL,
                    ""SupportsBracketTable"" INTEGER NOT NULL,
                    ""SupportsParameterEditing"" INTEGER NOT NULL,
                    ""IsActive"" INTEGER NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleSets_CompanyId_Code"" ON ""TaxRuleSets"" (""CompanyId"", ""Code"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleParameters"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TaxRuleParameters"" PRIMARY KEY,
                    ""TaxRuleSetId"" TEXT NOT NULL,
                    ""ParameterCode"" TEXT NOT NULL,
                    ""Label"" TEXT NOT NULL,
                    ""ValueType"" TEXT NOT NULL,
                    ""NumericValue"" TEXT NULL,
                    ""TextValue"" TEXT NOT NULL,
                    ""BooleanValue"" INTEGER NULL,
                    ""Notes"" TEXT NOT NULL,
                    ""DisplayOrder"" INTEGER NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleParameters_TaxRuleSetId_ParameterCode"" ON ""TaxRuleParameters"" (""TaxRuleSetId"", ""ParameterCode"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleBrackets"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TaxRuleBrackets"" PRIMARY KEY,
                    ""TaxRuleSetId"" TEXT NOT NULL,
                    ""Sequence"" INTEGER NOT NULL,
                    ""UpperBoundAmount"" TEXT NOT NULL,
                    ""FixedAmount"" TEXT NOT NULL,
                    ""Rate"" TEXT NOT NULL,
                    ""Notes"" TEXT NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleBrackets_TaxRuleSetId_Sequence"" ON ""TaxRuleBrackets"" (""TaxRuleSetId"", ""Sequence"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxFormRequirements"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TaxFormRequirements"" PRIMARY KEY,
                    ""TaxRuleSetId"" TEXT NOT NULL,
                    ""FormCode"" TEXT NOT NULL,
                    ""Name"" TEXT NOT NULL,
                    ""FilingFrequency"" TEXT NOT NULL,
                    ""DeliveryChannel"" TEXT NOT NULL,
                    ""DueRule"" TEXT NOT NULL,
                    ""Notes"" TEXT NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxFormRequirements_TaxRuleSetId_FormCode"" ON ""TaxFormRequirements"" (""TaxRuleSetId"", ""FormCode"");",
                cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""AccessRoles"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""CompanyId"" uuid NOT NULL,
                    ""Name"" text NOT NULL,
                    ""Description"" text NOT NULL,
                    ""TemplateCode"" text NOT NULL,
                    ""Permissions"" text NOT NULL,
                    ""IsSystemRole"" boolean NOT NULL,
                    ""IsActive"" boolean NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccessRoles_CompanyId_Name"" ON ""AccessRoles"" (""CompanyId"", ""Name"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "UserName" text NOT NULL DEFAULT '';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordHash" text NOT NULL DEFAULT '';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SecurityStamp" text NOT NULL DEFAULT '';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "FailedSignInCount" integer NOT NULL DEFAULT 0;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastFailedSignInUtc" timestamptz NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LockoutEndUtc" timestamptz NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastSuccessfulSignInUtc" timestamptz NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LastPasswordChangedUtc" timestamptz NULL;""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""AuthenticationAuditEntries"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""UserId"" uuid NULL,
                    ""CompanyId"" uuid NULL,
                    ""UserName"" text NOT NULL,
                    ""EventType"" text NOT NULL,
                    ""Succeeded"" boolean NOT NULL,
                    ""OccurredUtc"" timestamptz NOT NULL,
                    ""IpAddress"" text NOT NULL,
                    ""UserAgent"" text NOT NULL,
                    ""Detail"" text NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleSets"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""CompanyId"" uuid NOT NULL,
                    ""Code"" text NOT NULL,
                    ""JurisdictionCode"" text NOT NULL,
                    ""JurisdictionName"" text NOT NULL,
                    ""JurisdictionType"" text NOT NULL,
                    ""TaxType"" text NOT NULL,
                    ""CalculationMethod"" text NOT NULL,
                    ""WithholdingFrequency"" text NOT NULL,
                    ""EffectiveOn"" date NOT NULL,
                    ""Source"" text NOT NULL,
                    ""Notes"" text NOT NULL,
                    ""IsEmployerSpecific"" boolean NOT NULL,
                    ""SupportsBracketTable"" boolean NOT NULL,
                    ""SupportsParameterEditing"" boolean NOT NULL,
                    ""IsActive"" boolean NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleSets_CompanyId_Code"" ON ""TaxRuleSets"" (""CompanyId"", ""Code"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleParameters"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""TaxRuleSetId"" uuid NOT NULL,
                    ""ParameterCode"" text NOT NULL,
                    ""Label"" text NOT NULL,
                    ""ValueType"" text NOT NULL,
                    ""NumericValue"" numeric(18,4) NULL,
                    ""TextValue"" text NOT NULL,
                    ""BooleanValue"" boolean NULL,
                    ""Notes"" text NOT NULL,
                    ""DisplayOrder"" integer NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleParameters_TaxRuleSetId_ParameterCode"" ON ""TaxRuleParameters"" (""TaxRuleSetId"", ""ParameterCode"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxRuleBrackets"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""TaxRuleSetId"" uuid NOT NULL,
                    ""Sequence"" integer NOT NULL,
                    ""UpperBoundAmount"" numeric(18,2) NOT NULL,
                    ""FixedAmount"" numeric(18,2) NOT NULL,
                    ""Rate"" numeric(9,5) NOT NULL,
                    ""Notes"" text NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxRuleBrackets_TaxRuleSetId_Sequence"" ON ""TaxRuleBrackets"" (""TaxRuleSetId"", ""Sequence"");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""TaxFormRequirements"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""TaxRuleSetId"" uuid NOT NULL,
                    ""FormCode"" text NOT NULL,
                    ""Name"" text NOT NULL,
                    ""FilingFrequency"" text NOT NULL,
                    ""DeliveryChannel"" text NOT NULL,
                    ""DueRule"" text NOT NULL,
                    ""Notes"" text NOT NULL
                );",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TaxFormRequirements_TaxRuleSetId_FormCode"" ON ""TaxFormRequirements"" (""TaxRuleSetId"", ""FormCode"");",
                cancellationToken);
        }
    }

    private static async Task EnsureSqliteColumnAsync(
        BrassLedgerDbContext dbContext,
        string tableName,
        string columnName,
        string alterSql,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{tableName}');";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await dbContext.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}

