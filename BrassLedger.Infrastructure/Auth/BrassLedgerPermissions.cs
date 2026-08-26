namespace BrassLedger.Infrastructure.Auth;

public static class BrassLedgerPermissions
{
    public const string WorkspaceView = "workspace.view";
    public const string LedgerManage = "ledger.manage";
    public const string JournalPrepare = "ledger.journal.prepare";
    public const string JournalApprove = "ledger.journal.approve";
    public const string JournalPost = "ledger.journal.post";
    public const string JournalReverse = "ledger.journal.reverse";
    public const string ReceivablesManage = "receivables.manage";
    public const string PayablesManage = "payables.manage";
    public const string PaymentReverse = "payments.reverse";
    public const string SubledgerPrepare = "subledger.documents.prepare";
    public const string SubledgerApprove = "subledger.documents.approve";
    public const string SubledgerPost = "subledger.documents.post";
    public const string RequisitionManage = "operations.requisition.manage";
    public const string PurchasingManage = "operations.purchasing.manage";
    public const string SalesManage = "operations.sales.manage";
    public const string FulfillmentManage = "operations.fulfillment.manage";
    public const string CheckDisbursementManage = "treasury.check-disbursement.manage";
    public const string PayrollManage = "payroll.manage";
    public const string PayrollPrepare = "payroll.prepare";
    public const string PayrollApprove = "payroll.approve";
    public const string PayrollPost = "payroll.post";
    public const string PayrollReverse = "payroll.reverse";
    public const string PayrollSensitiveData = "payroll.sensitive-data";
    public const string ProjectsManage = "projects.manage";
    public const string ProjectChangeOrderPrepare = "projects.change-orders.prepare";
    public const string ProjectChangeOrderApprove = "projects.change-orders.approve";
    public const string ProjectBillingPrepare = "projects.billing.prepare";
    public const string ReportingManage = "reporting.manage";
    public const string TaxManage = "tax.manage";
    public const string PublishManage = "publish.manage";
    public const string UserManage = "security.users.manage";
    public const string RoleManage = "security.roles.manage";

    public static IReadOnlyList<PermissionDefinition> Definitions { get; } =
    [
        new(WorkspaceView, "Workspace access", "Sign in and review the shared accounting workspace."),
        new(LedgerManage, "Ledger", "Review general ledger activity and accounting balances."),
        new(JournalPrepare, "Journal preparation", "Create and edit unposted general journal drafts."),
        new(JournalApprove, "Journal approval", "Approve balanced general journal drafts after review."),
        new(JournalPost, "Journal posting", "Post approved general journals and change account balances."),
        new(JournalReverse, "Journal reversal", "Create auditable reversals of posted general journals."),
        new(ReceivablesManage, "Receivables", "Work customer balances, invoices, and cash application."),
        new(PayablesManage, "Payables", "Review vendor balances and payable obligations."),
        new(PaymentReverse, "Payment reversals", "Return, void, or reverse posted customer and vendor payments."),
        new(SubledgerPrepare, "Subledger preparation", "Prepare invoice, bill, and recurring transaction drafts."),
        new(SubledgerApprove, "Subledger approval", "Approve invoice and bill drafts after review."),
        new(SubledgerPost, "Subledger posting", "Post approved invoice and bill drafts to their control accounts."),
        new(RequisitionManage, "Requisitioning", "Create and route purchase requisitions."),
        new(PurchasingManage, "Purchasing", "Approve and issue purchase orders."),
        new(SalesManage, "Sales orders", "Prepare and approve customer quotes and sales orders."),
        new(FulfillmentManage, "Order fulfillment", "Allocate inventory and post or correct customer shipments."),
        new(CheckDisbursementManage, "Check disbursement", "Prepare payments, checks, and cash disbursements."),
        new(PayrollManage, "Payroll", "Maintain payroll-sensitive records and processing."),
        new(PayrollPrepare, "Payroll preparation", "Prepare payroll drafts, earnings, deductions, and calculation previews."),
        new(PayrollApprove, "Payroll approval", "Approve reviewed payroll drafts without posting or reversing them."),
        new(PayrollPost, "Payroll posting", "Post approved payroll runs to the ledger and funding account."),
        new(PayrollReverse, "Payroll reversal", "Create auditable reversals of posted payroll runs."),
        new(PayrollSensitiveData, "Payroll sensitive data", "View and maintain protected employee tax and banking fields."),
        new(ProjectsManage, "Projects", "Review and manage project accounting."),
        new(ProjectChangeOrderPrepare, "Project change-order preparation", "Prepare, correct, submit, and cancel project change orders."),
        new(ProjectChangeOrderApprove, "Project change-order approval", "Approve or reject independently prepared project change orders."),
        new(ProjectBillingPrepare, "Project billing preparation", "Maintain billing rates and prepare, correct, or cancel source-derived project billing proposals."),
        new(ReportingManage, "Reporting", "Run operational reports, forms, and labels."),
        new(TaxManage, "Taxes", "Maintain tax profiles and tax-facing workflows."),
        new(PublishManage, "Publishing", "Prepare packaged outputs and release artifacts."),
        new(UserManage, "User administration", "Create and maintain operator accounts."),
        new(RoleManage, "Role administration", "Create and maintain access roles.")
    ];

    public static ISet<string> All { get; } = new HashSet<string>(Definitions.Select(x => x.Code), StringComparer.OrdinalIgnoreCase);

    public sealed record PermissionDefinition(string Code, string Name, string Description);
}
