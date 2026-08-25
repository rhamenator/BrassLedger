namespace BrassLedger.Infrastructure.Auth;

public static class BrassLedgerAuthorizationPolicies
{
    public const string AdministerSystem = "AdministerSystem";
    public const string ManageTaxes = "ManageTaxes";
    public const string ViewWorkspace = "ViewWorkspace";
    public const string ManageLedger = "ManageLedger";
    public const string PrepareJournals = "PrepareJournals";
    public const string ApproveJournals = "ApproveJournals";
    public const string PostJournals = "PostJournals";
    public const string ReverseJournals = "ReverseJournals";
    public const string ManageReceivables = "ManageReceivables";
    public const string ManagePayables = "ManagePayables";
    public const string ReversePayments = "ReversePayments";
    public const string ManageOperations = "ManageOperations";
    public const string ManagePayroll = "ManagePayroll";
    public const string ManageProjects = "ManageProjects";
    public const string ManageReporting = "ManageReporting";
    public const string ManagePublishing = "ManagePublishing";
    public const string ManageUsers = "ManageUsers";
    public const string ManageRoles = "ManageRoles";
}
