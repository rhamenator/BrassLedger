namespace BrassLedger.Application.Accounting;

public interface ICompanyManagementService
{
    Task<CompanyManagementResult> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyMembershipSnapshot>> GetMyCompaniesAsync(CancellationToken cancellationToken = default);
}

public sealed record CreateCompanyRequest(string Name, string LegalName, string TaxId, string BaseCurrency, int FiscalYearStartMonth);
public sealed record CompanyMembershipSnapshot(Guid CompanyId, string Name, string LegalName, string BaseCurrency, string Role, bool IsOwner, bool IsActive);
public sealed record CompanyManagementResult(bool Succeeded, string ErrorMessage, Guid? CompanyId = null)
{
    public static CompanyManagementResult Success(Guid companyId) => new(true, string.Empty, companyId);
    public static CompanyManagementResult Failure(string error) => new(false, error);
}
