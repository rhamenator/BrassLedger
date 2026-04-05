namespace BrassLedger.Infrastructure.Auth;

public static class BrassLedgerPermissions
{
    public const string WorkspaceView = "workspace.view";
    public const string LedgerManage = "ledger.manage";
    public const string ReceivablesManage = "receivables.manage";
    public const string PayablesManage = "payables.manage";
    public const string RequisitionManage = "operations.requisition.manage";
    public const string PurchasingManage = "operations.purchasing.manage";
    public const string CheckDisbursementManage = "treasury.check-disbursement.manage";
    public const string PayrollManage = "payroll.manage";
    public const string ProjectsManage = "projects.manage";
    public const string ReportingManage = "reporting.manage";
    public const string TaxManage = "tax.manage";
    public const string PublishManage = "publish.manage";
    public const string UserManage = "security.users.manage";
    public const string RoleManage = "security.roles.manage";

    public static IReadOnlyList<PermissionDefinition> Definitions { get; } =
    [
        new(WorkspaceView, "Workspace access", "Sign in and review the shared accounting workspace."),
        new(LedgerManage, "Ledger", "Post and review general ledger activity."),
        new(ReceivablesManage, "Receivables", "Work customer balances, invoices, and cash application."),
        new(PayablesManage, "Payables", "Review vendor balances and payable obligations."),
        new(RequisitionManage, "Requisitioning", "Create and route purchase requisitions."),
        new(PurchasingManage, "Purchasing", "Approve and issue purchase orders."),
        new(CheckDisbursementManage, "Check disbursement", "Prepare payments, checks, and cash disbursements."),
        new(PayrollManage, "Payroll", "Maintain payroll-sensitive records and processing."),
        new(ProjectsManage, "Projects", "Review and manage project accounting."),
        new(ReportingManage, "Reporting", "Run operational reports, forms, and labels."),
        new(TaxManage, "Taxes", "Maintain tax profiles and tax-facing workflows."),
        new(PublishManage, "Publishing", "Prepare packaged outputs and release artifacts."),
        new(UserManage, "User administration", "Create and maintain operator accounts."),
        new(RoleManage, "Role administration", "Create and maintain access roles.")
    ];

    public static ISet<string> All { get; } = new HashSet<string>(Definitions.Select(x => x.Code), StringComparer.OrdinalIgnoreCase);

    public sealed record PermissionDefinition(string Code, string Name, string Description);
}
