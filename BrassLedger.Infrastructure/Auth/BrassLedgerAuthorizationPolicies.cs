namespace BrassLedger.Infrastructure.Auth;

public static class BrassLedgerAuthorizationPolicies
{
    public const string ManageAccountSecurity = "ManageAccountSecurity";
    public const string AdministerSystem = "AdministerSystem";
    public const string ManageTaxes = "ManageTaxes";
    public const string ViewWorkspace = "ViewWorkspace";
    public const string ManageLedger = "ManageLedger";
    public const string ManageAccountingDimensions = "ManageAccountingDimensions";
    public const string PrepareJournals = "PrepareJournals";
    public const string ApproveJournals = "ApproveJournals";
    public const string PostJournals = "PostJournals";
    public const string ReverseJournals = "ReverseJournals";
    public const string ManageReceivables = "ManageReceivables";
    public const string ManagePayables = "ManagePayables";
    public const string ReversePayments = "ReversePayments";
    public const string PrepareSubledgerDocuments = "PrepareSubledgerDocuments";
    public const string ApproveSubledgerDocuments = "ApproveSubledgerDocuments";
    public const string PostSubledgerDocuments = "PostSubledgerDocuments";
    public const string ManageOperations = "ManageOperations";
    public const string AccessPayroll = "AccessPayroll";
    public const string ManagePayroll = "ManagePayroll";
    public const string PreparePayroll = "PreparePayroll";
    public const string ApprovePayroll = "ApprovePayroll";
    public const string PostPayroll = "PostPayroll";
    public const string ReversePayroll = "ReversePayroll";
    public const string ManagePayrollSensitiveData = "ManagePayrollSensitiveData";
    public const string MaintainEmployeePayrollSetup = "MaintainEmployeePayrollSetup";
    public const string ManageProjects = "ManageProjects";
    public const string AccessProjects = "AccessProjects";
    public const string PrepareProjectChangeOrders = "PrepareProjectChangeOrders";
    public const string ApproveProjectChangeOrders = "ApproveProjectChangeOrders";
    public const string PrepareProjectBilling = "PrepareProjectBilling";
    public const string PrepareProjectWip = "PrepareProjectWip";
    public const string ApproveProjectWip = "ApproveProjectWip";
    public const string PostProjectWip = "PostProjectWip";
    public const string ReverseProjectWip = "ReverseProjectWip";
    public const string ManageReporting = "ManageReporting";
    public const string ManagePublishing = "ManagePublishing";
    public const string ManageUsers = "ManageUsers";
    public const string ManageRoles = "ManageRoles";
}
