namespace BrassLedger.Application.Accounting;

public interface IBackupService
{
    Task<BackupResult> CreateBackupAsync(CancellationToken cancellationToken = default);
    Task<BackupVerificationResult> VerifyBackupAsync(string backupId, CancellationToken cancellationToken = default);
    Task<BackupRecoveryRehearsalResult> RehearseRestoreAsync(string backupId, CancellationToken cancellationToken = default);
}

public sealed record BackupResult(bool Succeeded, string ErrorMessage, string? BackupId = null, DateTimeOffset? CreatedAtUtc = null)
{
    public static BackupResult Success(string id) => new(true, string.Empty, id, DateTimeOffset.UtcNow);
    public static BackupResult Failure(string error) => new(false, error);
}
public sealed record BackupVerificationResult(bool Succeeded, string ErrorMessage, string? BackupId = null, DateTimeOffset? VerifiedAtUtc = null);
public sealed record BackupRecoveryRehearsalResult(bool Succeeded, string ErrorMessage, string? BackupId = null, DateTimeOffset? RehearsedAtUtc = null, int CompanyCount = 0, int JournalEntryCount = 0, int PayrollRunCount = 0, int DataProtectionKeyCount = 0);
