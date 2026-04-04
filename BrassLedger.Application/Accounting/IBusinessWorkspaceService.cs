namespace BrassLedger.Application.Accounting;

public interface IBusinessWorkspaceService
{
    Task<BusinessWorkspaceSnapshot> GetWorkspaceAsync(CancellationToken cancellationToken = default);
}
