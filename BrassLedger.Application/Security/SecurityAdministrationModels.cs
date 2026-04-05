namespace BrassLedger.Application.Security;

public interface ISecurityAdministrationService
{
    Task<SecurityAdministrationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<SecurityOperationResult> CreateRoleAsync(CreateAccessRoleRequest request, CancellationToken cancellationToken = default);
    Task<SecurityOperationResult> CreateOperatorAsync(CreateOperatorRequest request, CancellationToken cancellationToken = default);
}

public sealed record SecurityAdministrationSnapshot(
    IReadOnlyList<PermissionDefinitionSnapshot> Permissions,
    IReadOnlyList<AccessRoleSnapshot> Roles,
    IReadOnlyList<OperatorAccountSnapshot> Operators);

public sealed record PermissionDefinitionSnapshot(
    string Code,
    string Name,
    string Description);

public sealed record AccessRoleSnapshot(
    string Name,
    string Description,
    string TemplateCode,
    bool IsSystemRole,
    int AssignedUserCount,
    IReadOnlyList<string> Permissions);

public sealed record OperatorAccountSnapshot(
    string UserName,
    string DisplayName,
    string Email,
    string Role,
    bool IsActive,
    DateTimeOffset? LastSuccessfulSignInUtc);

public sealed record CreateAccessRoleRequest(
    string Name,
    string Description,
    IReadOnlyList<string> Permissions);

public sealed record CreateOperatorRequest(
    string UserName,
    string DisplayName,
    string Email,
    string Password,
    string ConfirmPassword,
    string RoleName);

public sealed record SecurityOperationResult(
    bool Succeeded,
    string ErrorMessage)
{
    public static SecurityOperationResult Success() => new(true, string.Empty);
    public static SecurityOperationResult Failure(string errorMessage) => new(false, errorMessage);
}
