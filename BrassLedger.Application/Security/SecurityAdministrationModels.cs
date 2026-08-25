namespace BrassLedger.Application.Security;

public interface ISecurityAdministrationService
{
    Task<SecurityAdministrationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<SecurityOperationResult> CreateRoleAsync(CreateAccessRoleRequest request, CancellationToken cancellationToken = default);
    Task<SecurityOperationResult> SetRoleMfaRequirementAsync(string roleName, bool requiresMfa, CancellationToken cancellationToken = default);
    Task<SecurityOperationResult> InviteOperatorAsync(CreateOperatorInvitationRequest request, CancellationToken cancellationToken = default);
    Task<SecurityOperationResult> RetrySecurityEmailAsync(Guid messageId, CancellationToken cancellationToken = default);
}

public sealed record SecurityAdministrationSnapshot(
    IReadOnlyList<PermissionDefinitionSnapshot> Permissions,
    IReadOnlyList<AccessRoleSnapshot> Roles,
    IReadOnlyList<OperatorAccountSnapshot> Operators,
    bool SecurityEmailDeliveryConfigured,
    IReadOnlyList<SecurityEmailDeliverySnapshot> SecurityEmailDeliveries);

public sealed record SecurityEmailDeliverySnapshot(
    Guid MessageId,
    string Purpose,
    string MaskedRecipient,
    string Status,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset NextAttemptAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    string LastError);

public sealed record PermissionDefinitionSnapshot(
    string Code,
    string Name,
    string Description);

public sealed record AccessRoleSnapshot(
    string Name,
    string Description,
    string TemplateCode,
    bool IsSystemRole,
    bool RequiresMfa,
    int AssignedUserCount,
    IReadOnlyList<string> Permissions);

public sealed record OperatorAccountSnapshot(
    string UserName,
    string DisplayName,
    string Email,
    string Role,
    bool IsActive,
    bool MfaEnabled,
    bool RoleRequiresMfa,
    DateTimeOffset? LastSuccessfulSignInUtc);

public sealed record CreateAccessRoleRequest(
    string Name,
    string Description,
    IReadOnlyList<string> Permissions,
    bool RequiresMfa = false);

public sealed record CreateOperatorInvitationRequest(
    string UserName,
    string DisplayName,
    string Email,
    string RoleName);

public sealed record SecurityOperationResult(
    bool Succeeded,
    string ErrorMessage)
{
    public static SecurityOperationResult Success() => new(true, string.Empty);
    public static SecurityOperationResult Failure(string errorMessage) => new(false, errorMessage);
}
