using BrassLedger.Application.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class BackupService(IDbContextFactory<BrassLedgerDbContext> dbContextFactory, BrassLedgerStoragePaths storagePaths) : IBackupService
{
    public async Task<BackupResult> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!db.Database.IsSqlite()) return BackupResult.Failure("PostgreSQL backups require the database platform backup service; no unsafe file-copy fallback is used.");
        var backupId = $"brassledger-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var destination = Path.Combine(storagePaths.DataDirectory, "backups", backupId);
        Directory.CreateDirectory(destination);
        var databasePath = Path.Combine(destination, "brassledger.db");
        try
        {
            var escapedPath = databasePath.Replace("'", "''", StringComparison.Ordinal);
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"VACUUM INTO '{escapedPath}'";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            finally { await connection.CloseAsync(); }
            var keyDestination = Path.Combine(destination, "keys"); Directory.CreateDirectory(keyDestination);
            foreach (var keyFile in Directory.EnumerateFiles(storagePaths.KeysDirectory)) File.Copy(keyFile, Path.Combine(keyDestination, Path.GetFileName(keyFile)), overwrite: true);
            var manifest = System.Text.Json.JsonSerializer.Serialize(new { backupId, createdAtUtc = DateTimeOffset.UtcNow, database = "brassledger.db", keyFiles = Directory.EnumerateFiles(keyDestination).Select(Path.GetFileName).ToArray(), format = "sqlite-vacuum-into-v1" });
            await File.WriteAllTextAsync(Path.Combine(destination, "manifest.json"), manifest, cancellationToken);
            return BackupResult.Success(backupId);
        }
        catch (Exception exception) { return BackupResult.Failure($"Backup failed: {exception.Message}"); }
    }

    public async Task<BackupVerificationResult> VerifyBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
        if (!IsValidBackupId(backupId)) return new(false, "Invalid backup identifier.");
        var directory = Path.Combine(storagePaths.DataDirectory, "backups", backupId); var databasePath = Path.Combine(directory, "brassledger.db");
        if (!File.Exists(databasePath) || !File.Exists(Path.Combine(directory, "manifest.json"))) return new(false, "Backup database or manifest was not found.");
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        try { await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check;"; var result = (await command.ExecuteScalarAsync(cancellationToken))?.ToString(); return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase) ? new(true, string.Empty, backupId, DateTimeOffset.UtcNow) : new(false, $"SQLite integrity check returned '{result}'.", backupId); }
        catch (Exception exception) { return new(false, $"Backup verification failed: {exception.Message}", backupId); }
    }

    public async Task<BackupRecoveryRehearsalResult> RehearseRestoreAsync(string backupId, CancellationToken cancellationToken = default)
    {
        if (!IsValidBackupId(backupId)) return new(false, "Invalid backup identifier.");
        var source = Path.Combine(storagePaths.DataDirectory, "backups", backupId);
        var sourceDatabase = Path.Combine(source, "brassledger.db");
        var sourceKeys = Path.Combine(source, "keys");
        if (!File.Exists(sourceDatabase) || !File.Exists(Path.Combine(source, "manifest.json"))) return new(false, "Backup database or manifest was not found.");
        var staging = Path.Combine(storagePaths.DataDirectory, "recovery-rehearsals", $"{backupId}-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            var restoredDatabase = Path.Combine(staging, "brassledger.db");
            File.Copy(sourceDatabase, restoredDatabase);
            var restoredKeys = Path.Combine(staging, "keys");
            if (Directory.Exists(sourceKeys))
            {
                Directory.CreateDirectory(restoredKeys);
                foreach (var keyFile in Directory.EnumerateFiles(sourceKeys)) File.Copy(keyFile, Path.Combine(restoredKeys, Path.GetFileName(keyFile)));
            }

            await using var connection = new SqliteConnection($"Data Source={restoredDatabase};Mode=ReadOnly");
            await connection.OpenAsync(cancellationToken);
            await using var integrity = connection.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check;";
            var integrityResult = (await integrity.ExecuteScalarAsync(cancellationToken))?.ToString();
            if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase)) return new(false, $"Restored SQLite integrity check returned '{integrityResult}'.", backupId);
            var companies = await ReadCountAsync(connection, "Companies", cancellationToken);
            var journals = await ReadCountAsync(connection, "JournalEntries", cancellationToken);
            var payrollRuns = await ReadCountAsync(connection, "PayrollRuns", cancellationToken);
            var keys = Directory.Exists(restoredKeys) ? Directory.EnumerateFiles(restoredKeys).Count() : 0;
            return new(true, string.Empty, backupId, DateTimeOffset.UtcNow, companies, journals, payrollRuns, keys);
        }
        catch (Exception exception) { return new(false, $"Recovery rehearsal failed: {exception.Message}", backupId); }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static bool IsValidBackupId(string backupId) => !string.IsNullOrWhiteSpace(backupId) && backupId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !backupId.Contains("..", StringComparison.Ordinal) && !backupId.Contains(Path.DirectorySeparatorChar) && !backupId.Contains(Path.AltDirectorySeparatorChar);
    private static async Task<int> ReadCountAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
