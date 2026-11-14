using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class BackupService(IDbContextFactory<BrassLedgerDbContext> dbContextFactory, BrassLedgerStoragePaths storagePaths, IHttpContextAccessor httpContextAccessor) : IBackupService
{
    private const string BackupFormat = "sqlite-vacuum-into-v2";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record BackupProtectionFileHash(string Name, string Sha256);
    private sealed record BackupManifest(string BackupId, DateTimeOffset CreatedAtUtc, string Database, string DatabaseSha256, BackupProtectionFileHash[] ProtectionFiles, string Format);

    public async Task<BackupResult> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await CanAccessWholeBackupAsync(db, cancellationToken)) return BackupResult.Failure("System-wide backup access requires an active owner with current authority in every company.");
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
            var protectionFileHashes = new List<BackupProtectionFileHash>();
            foreach (var keyFile in Directory.EnumerateFiles(storagePaths.KeysDirectory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(keyFile);
                if (!IsValidDataProtectionFile(keyFile, name, allowSourceLink: true)) throw new InvalidOperationException("A data-protection key or revocation file is missing or malformed.");
                var sourceHash = await HashFileAsync(keyFile, cancellationToken);
                var copiedKey = Path.Combine(keyDestination, name);
                File.Copy(keyFile, copiedKey);
                var copiedHash = await HashFileAsync(copiedKey, cancellationToken);
                if (copiedHash != sourceHash || await HashFileAsync(keyFile, cancellationToken) != sourceHash
                    || !IsValidDataProtectionFile(copiedKey, name))
                    throw new InvalidOperationException("A data-protection file changed during backup.");
                protectionFileHashes.Add(new BackupProtectionFileHash(name, copiedHash));
            }
            if (!protectionFileHashes.Any(file => file.Name.StartsWith("key-", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("No data-protection keys are available to back up.");
            var manifest = new BackupManifest(backupId, DateTimeOffset.UtcNow, "brassledger.db", await HashFileAsync(databasePath, cancellationToken), protectionFileHashes.ToArray(), BackupFormat);
            await File.WriteAllTextAsync(Path.Combine(destination, "manifest.json"), JsonSerializer.Serialize(manifest, ManifestJsonOptions), cancellationToken);
            var verification = await VerifyBackupAsync(backupId, cancellationToken);
            if (!verification.Succeeded) throw new InvalidOperationException(verification.ErrorMessage);
            return BackupResult.Success(backupId);
        }
        catch (Exception exception)
        {
            try { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); } catch { /* Preserve the original backup failure. */ }
            return BackupResult.Failure($"Backup failed: {exception.Message}");
        }
    }

    public async Task<BackupVerificationResult> VerifyBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
        if (!IsValidBackupId(backupId)) return new(false, "Invalid backup identifier.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await CanAccessWholeBackupAsync(db, cancellationToken)) return new(false, "System-wide backup access requires an active owner with current authority in every company.", backupId);
        var directory = Path.Combine(storagePaths.DataDirectory, "backups", backupId);
        return await VerifyBackupDirectoryAsync(directory, backupId, cancellationToken);
    }

    public async Task<BackupRecoveryRehearsalResult> RehearseRestoreAsync(string backupId, CancellationToken cancellationToken = default)
    {
        if (!IsValidBackupId(backupId)) return new(false, "Invalid backup identifier.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await CanAccessWholeBackupAsync(db, cancellationToken)) return new(false, "System-wide backup access requires an active owner with current authority in every company.", backupId);
        var source = Path.Combine(storagePaths.DataDirectory, "backups", backupId);
        var sourceDatabase = Path.Combine(source, "brassledger.db");
        var sourceKeys = Path.Combine(source, "keys");
        var sourceVerification = await VerifyBackupAsync(backupId, cancellationToken);
        if (!sourceVerification.Succeeded) return new(false, sourceVerification.ErrorMessage, backupId);
        var staging = Path.Combine(storagePaths.DataDirectory, "recovery-rehearsals", $"{backupId}-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            var restoredDatabase = Path.Combine(staging, "brassledger.db");
            File.Copy(sourceDatabase, restoredDatabase);
            File.Copy(Path.Combine(source, "manifest.json"), Path.Combine(staging, "manifest.json"));
            var restoredKeys = Path.Combine(staging, "keys");
            if (Directory.Exists(sourceKeys))
            {
                Directory.CreateDirectory(restoredKeys);
                foreach (var keyFile in Directory.EnumerateFiles(sourceKeys)) File.Copy(keyFile, Path.Combine(restoredKeys, Path.GetFileName(keyFile)));
            }

            var restoredVerification = await VerifyBackupDirectoryAsync(staging, backupId, cancellationToken);
            if (!restoredVerification.Succeeded) return new(false, restoredVerification.ErrorMessage, backupId);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = restoredDatabase, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
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
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { /* Preserve the rehearsal result. */ }
        }
    }

    private static async Task<BackupVerificationResult> VerifyBackupDirectoryAsync(string directory, string backupId, CancellationToken cancellationToken)
    {
        var databasePath = Path.Combine(directory, "brassledger.db");
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(databasePath) || !File.Exists(manifestPath)) return new(false, "Backup database or manifest was not found.", backupId);
        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            using var manifestDocument = JsonDocument.Parse(manifestJson);
            if (manifestDocument.RootElement.TryGetProperty("format", out var format)
                && format.ValueKind == JsonValueKind.String && format.GetString() == "sqlite-vacuum-into-v1")
                return new(false, "Legacy v1 backup lacks database and key checksum evidence; create a new v2 backup before relying on verification.", backupId);
            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson, ManifestJsonOptions);
            if (manifest is null || manifest.Format != BackupFormat || manifest.Database != "brassledger.db"
                || !IsSha256(manifest.DatabaseSha256) || manifest.ProtectionFiles is not { Length: > 0 }
                || manifest.ProtectionFiles.Any(file => !IsValidDataProtectionFileName(file.Name) || !IsSha256(file.Sha256))
                || !manifest.ProtectionFiles.Any(file => file.Name.StartsWith("key-", StringComparison.OrdinalIgnoreCase))
                || manifest.ProtectionFiles.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count() != manifest.ProtectionFiles.Length)
                return new(false, "Backup manifest is invalid or uses a format without file-integrity evidence.", backupId);
            if (manifest.BackupId != backupId)
                return new(false, "Backup directory name does not match the manifest identifier.", backupId);
            var keyDirectory = Path.Combine(directory, "keys");
            if (!Directory.Exists(keyDirectory) || IsLink(keyDirectory)
                || !Directory.EnumerateFileSystemEntries(keyDirectory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal)
                    .SequenceEqual(manifest.ProtectionFiles.Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal))
                || IsLink(databasePath) || IsLink(manifestPath)
                || manifest.ProtectionFiles.Any(file => IsLink(Path.Combine(keyDirectory, file.Name))))
                return new(false, "Backup data-protection files are missing, unexpected, or unsafe.", backupId);
            if (!string.Equals(await HashFileAsync(databasePath, cancellationToken), manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                return new(false, "Backup database checksum does not match its manifest.", backupId);
            foreach (var file in manifest.ProtectionFiles)
            {
                if (!string.Equals(await HashFileAsync(Path.Combine(keyDirectory, file.Name), cancellationToken), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, "Backup data-protection file checksum does not match its manifest.", backupId);
                if (!IsValidDataProtectionFile(Path.Combine(keyDirectory, file.Name), file.Name))
                    return new(false, "Backup data-protection key or revocation file is malformed.", backupId);
            }
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check;";
            var result = (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
                ? new(true, string.Empty, backupId, DateTimeOffset.UtcNow)
                : new(false, $"SQLite integrity check returned '{result}'.", backupId);
        }
        catch (Exception exception) { return new(false, $"Backup verification failed: {exception.Message}", backupId); }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static bool IsValidFileName(string? name) => !string.IsNullOrWhiteSpace(name) && name != "." && name != ".."
        && !name.Contains('/') && !name.Contains('\\')
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Path.GetFileName(name) == name;
    private static bool IsValidDataProtectionFileName(string? name) => IsValidFileName(name) && name!.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        && ((name.Length == 44 && name.StartsWith("key-", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(name.AsSpan(4, 36), "D", out _))
            || (name.Length == 51 && name.StartsWith("revocation-", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(name.AsSpan(11, 36), "D", out _)));
    private static bool IsValidDataProtectionFile(string path, string name, bool allowSourceLink = false)
    {
        if (!IsValidDataProtectionFileName(name) || (!allowSourceLink && IsLink(path))) return false;
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader);
            var root = document.Root;
            if (name.StartsWith("revocation-", StringComparison.OrdinalIgnoreCase))
                return root?.Name.LocalName == "revocation" && root.Elements().Any();
            return root?.Name.LocalName == "key"
                && Guid.TryParse(root.Attribute("id")?.Value, out var id)
                && id == Guid.Parse(name.AsSpan(4, 36))
                && new[] { "creationDate", "activationDate", "expirationDate", "descriptor" }
                    .All(element => root.Elements().Any(child => child.Name.LocalName == element));
        }
        catch (XmlException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool IsValidBackupId(string backupId) => !string.IsNullOrWhiteSpace(backupId) && backupId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !backupId.Contains("..", StringComparison.Ordinal) && !backupId.Contains(Path.DirectorySeparatorChar) && !backupId.Contains(Path.AltDirectorySeparatorChar);
    private async Task<bool> CanAccessWholeBackupAsync(BrassLedgerDbContext db, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")
            || !principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage)
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId)) return false;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        if (user is null || (user.MfaEnabled && !string.Equals(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.AuthenticationMethodClaimType), "mfa", StringComparison.Ordinal))) return false;
        var companyIds = await db.Companies.AsNoTracking().Select(company => company.Id).ToArrayAsync(cancellationToken);
        if (companyIds.Length == 0 || !companyIds.Contains(companyId)) return false;
        var memberships = await db.CompanyMemberships.AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.IsActive && membership.IsOwner)
            .ToListAsync(cancellationToken);
        if (memberships.Count != companyIds.Length || memberships.Any(membership => !companyIds.Contains(membership.CompanyId))) return false;
        var activeRoles = await db.AccessRoles.AsNoTracking().Where(role => companyIds.Contains(role.CompanyId) && role.IsActive).ToListAsync(cancellationToken);
        foreach (var membership in memberships)
        {
            var role = activeRoles.SingleOrDefault(candidate => candidate.CompanyId == membership.CompanyId && candidate.Name == membership.Role);
            if (role is null || (role.RequiresMfa && !user.MfaEnabled)
                || !role.Permissions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(BrassLedgerPermissions.UserManage, StringComparer.OrdinalIgnoreCase)) return false;
            if (membership.CompanyId == companyId && !string.Equals(principal.FindFirstValue(ClaimTypes.Role), membership.Role, StringComparison.Ordinal)) return false;
        }
        return true;
    }
    private static async Task<int> ReadCountAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
