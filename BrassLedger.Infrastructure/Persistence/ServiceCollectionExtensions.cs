using System.Data;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Application.Accounting;
using BrassLedger.Application.Modernization;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Modernization;
using BrassLedger.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBrassLedgerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        var dataDirectory = BuildDataDirectory(contentRootPath);
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

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
            .SetApplicationName("BrassLedger");

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
        services.AddSingleton<ISensitiveDataProtector, SensitiveDataProtector>();
        services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IBusinessWorkspaceService, BusinessWorkspaceService>();
        services.AddSingleton<IModernizationAssessmentService, StaticModernizationAssessmentService>();

        return services;
    }

    public static async Task InitializeBrassLedgerAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureLegacySchemaCompatibilityAsync(dbContext, cancellationToken);
        await BrassLedgerSeedData.SeedAsync(dbContext, passwordHasher, cancellationToken);
    }

    private static string BuildDataDirectory(string contentRootPath)
    {
        var dataDirectory = Path.Combine(contentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        return dataDirectory;
    }

    private static string BuildDefaultSqliteConnectionString(string dataDirectory)
    {
        return $"Data Source={Path.Combine(dataDirectory, "brassledger.db")}";
    }

    private static async Task EnsureLegacySchemaCompatibilityAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteColumnAsync(dbContext, "Users", "UserName", @"ALTER TABLE ""Users"" ADD COLUMN ""UserName"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            await EnsureSqliteColumnAsync(dbContext, "Users", "PasswordHash", @"ALTER TABLE ""Users"" ADD COLUMN ""PasswordHash"" TEXT NOT NULL DEFAULT '';", cancellationToken);
            return;
        }

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "UserName" text NOT NULL DEFAULT '';""",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordHash" text NOT NULL DEFAULT '';""",
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
