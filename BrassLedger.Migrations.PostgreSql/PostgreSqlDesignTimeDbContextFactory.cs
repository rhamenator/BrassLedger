using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BrassLedger.Migrations.PostgreSql;

public sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BrassLedgerDbContext>
{
    public BrassLedgerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BrassLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=brassledger_migrations;Username=brassledger;Password=design-time-only", postgres => postgres.MigrationsAssembly(typeof(PostgreSqlDesignTimeDbContextFactory).Assembly.FullName))
            .Options;
        return new BrassLedgerDbContext(options, PassthroughSensitiveDataProtector.Instance);
    }

    private sealed class PassthroughSensitiveDataProtector : ISensitiveDataProtector
    {
        public static PassthroughSensitiveDataProtector Instance { get; } = new();
        public bool IsProtected(string value) => false;
        public string Protect(string value) => value;
        public string Unprotect(string value) => value;
    }
}
