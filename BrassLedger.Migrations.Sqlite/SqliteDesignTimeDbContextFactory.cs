using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BrassLedger.Migrations.Sqlite;

public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BrassLedgerDbContext>
{
    public BrassLedgerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BrassLedgerDbContext>()
            .UseSqlite("Data Source=brassledger-migrations.db", sqlite => sqlite.MigrationsAssembly(typeof(SqliteDesignTimeDbContextFactory).Assembly.FullName))
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
