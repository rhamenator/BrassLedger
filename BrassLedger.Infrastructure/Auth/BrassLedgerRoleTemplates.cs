namespace BrassLedger.Infrastructure.Auth;

public static class BrassLedgerRoleTemplates
{
    public static IReadOnlyList<RoleTemplateDefinition> BuiltIn { get; } =
    [
        new("administrator", "Administrator", "Full access to every module plus role and user administration.", true, true, BrassLedgerPermissions.All.ToArray()),
        new("owner-ceo", "Owner/CEO", "Executive-level access to every module so the business is never blocked by one operator account.", true, true, BrassLedgerPermissions.All.ToArray()),
        new("controller", "Controller", "Broad accounting oversight without user or role administration.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.LedgerManage,
            BrassLedgerPermissions.JournalPrepare,
            BrassLedgerPermissions.JournalApprove,
            BrassLedgerPermissions.JournalPost,
            BrassLedgerPermissions.JournalReverse,
            BrassLedgerPermissions.ReceivablesManage,
            BrassLedgerPermissions.PayablesManage,
            BrassLedgerPermissions.PaymentReverse,
            BrassLedgerPermissions.SubledgerPrepare,
            BrassLedgerPermissions.SubledgerApprove,
            BrassLedgerPermissions.SubledgerPost,
            BrassLedgerPermissions.ReportingManage,
            BrassLedgerPermissions.TaxManage,
            BrassLedgerPermissions.PublishManage,
            BrassLedgerPermissions.ProjectsManage
        ]),
        new("journal-preparer", "Journal Preparer", "Creates journal drafts without authority to approve, post, or reverse them.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.LedgerManage,
            BrassLedgerPermissions.JournalPrepare,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("journal-approver", "Journal Approver", "Reviews and approves journal drafts without authority to prepare, post, or reverse them.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.LedgerManage,
            BrassLedgerPermissions.JournalApprove,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("journal-poster", "Journal Poster", "Posts approved journals and creates controlled reversals without editing their preparation.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.LedgerManage,
            BrassLedgerPermissions.JournalPost,
            BrassLedgerPermissions.JournalReverse,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("requisitioning", "Requisitioning Clerk", "Can prepare requisitions without approving purchasing or writing checks.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.RequisitionManage,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("purchasing", "Purchasing Manager", "Approves and issues purchase orders without payment authority.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PurchasingManage,
            BrassLedgerPermissions.PayablesManage,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("sales", "Sales Clerk", "Prepares and approves quotes and sales orders without inventory-shipment or receivables posting authority.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.SalesManage,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("cash-disbursements", "Cash Disbursements", "Handles payment preparation and checks separately from requisitioning and purchasing.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PayablesManage,
            BrassLedgerPermissions.CheckDisbursementManage,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("payroll-manager", "Payroll Manager", "Maintains payroll and employee-sensitive records.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PayrollManage,
            BrassLedgerPermissions.PayrollPrepare,
            BrassLedgerPermissions.PayrollApprove,
            BrassLedgerPermissions.PayrollPost,
            BrassLedgerPermissions.PayrollReverse,
            BrassLedgerPermissions.PayrollSensitiveData,
            BrassLedgerPermissions.ReportingManage,
            BrassLedgerPermissions.TaxManage
        ]),
        new("payroll-preparer", "Payroll Preparer", "Prepares payroll drafts without approval, posting, reversal, or protected-record authority.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PayrollPrepare,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("payroll-approver", "Payroll Approver", "Reviews and approves payroll drafts without preparation, posting, reversal, or protected-record authority.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PayrollApprove,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("payroll-poster", "Payroll Poster", "Posts approved payroll and performs controlled reversals without changing employee setup.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.PayrollPost,
            BrassLedgerPermissions.PayrollReverse,
            BrassLedgerPermissions.ReportingManage
        ]),
        new("warehouse", "Warehouse Operator", "Maintains inventory and operational activity without payment authority.", false, false,
        [
            BrassLedgerPermissions.WorkspaceView,
            BrassLedgerPermissions.RequisitionManage,
            BrassLedgerPermissions.PurchasingManage,
            BrassLedgerPermissions.FulfillmentManage,
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
    bool RequiresMfa,
    IReadOnlyList<string> Permissions);
