namespace BrassLedger.Infrastructure.Auth;

public static class BrassLedgerRoleTemplates
{
    public static IReadOnlyList<RoleTemplateDefinition> BuiltIn { get; } =
    [
        new("administrator", "Administrator", "Full access to every module plus role and user administration.", true, BrassLedgerPermissions.All.ToArray()),
        new("owner-ceo", "Owner/CEO", "Executive-level access to every module so the business is never blocked by one operator account.", true, BrassLedgerPermissions.All.ToArray()),
        new("controller", "Controller", "Broad accounting oversight without user or role administration.", false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.LedgerManage,
            BrassLedgerPermissions.ReceivablesManage,
            BrassLedgerPermissions.PayablesManage,
            BrassLedgerPermissions.ReportingManage,
            BrassLedgerPermissions.TaxManage,
            BrassLedgerPermissions.PublishManage,
            BrassLedgerPermissions.ProjectsManage
        ]),
        new("requisitioning", "Requisitioning Clerk", "Can prepare requisitions without approving purchasing or writing checks.", false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.RequisitionManage,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("purchasing", "Purchasing Manager", "Approves and issues purchase orders without payment authority.", false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PurchasingManage,
            BrassLedgerPermissions.PayablesManage,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("cash-disbursements", "Cash Disbursements", "Handles payment preparation and checks separately from requisitioning and purchasing.", false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PayablesManage,
            BrassLedgerPermissions.CheckDisbursementManage,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("payroll-manager", "Payroll Manager", "Maintains payroll and employee-sensitive records.", false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PayrollManage,
            BrassLedgerPermissions.ReportingManage,
            BrassLedgerPermissions.TaxManage
        ]),
        new("warehouse", "Warehouse Operator", "Maintains inventory and operational activity without payment authority.", false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.RequisitionManage,
            BrassLedgerPermissions.PurchasingManage,
            BrassLedgerPermissions.ReportingManage
        ])
    ];

    public static IReadOnlyList<string> NormalizePermissions(IEnumerable<string> permissions)
    {
        return permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Where(permission => BrassLedgerPermissions.All.Contains(permission))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> GetPermissionsForRoleName(string roleName)
    {
        return BuiltIn
            .FirstOrDefault(template => string.Equals(template.Name, roleName, StringComparison.OrdinalIgnoreCase))
            ?.Permissions
            ?? [];
    }
}

public sealed record RoleTemplateDefinition(
    string TemplateCode,
    string Name,
    string Description,
    bool HasFullAccess,
    IReadOnlyList<string> Permissions);
