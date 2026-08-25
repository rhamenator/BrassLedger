namespace BrassLedger.Application.Accounting;

public sealed record SsaEfw2cSubmitter(
    int SpecificationTaxYear, string SpecificationVersion, string OfficialSpecificationUrl,
    string SubmitterEin, string BsoUserId, string SubmitterName,
    string LocationAddress, string DeliveryAddress, string City, string State, string PostalCode,
    string ContactName, string ContactPhone, string ContactEmail, string PreparerCode,
    string EmployerLocationAddress, string EmployerDeliveryAddress, string EmployerCity,
    string EmployerState, string EmployerPostalCode, string EmployerContactName,
    string EmployerContactPhone, string EmployerContactEmail);

public sealed record SsaWageFileBuildResult(
    bool Succeeded, byte[] Content, IReadOnlyList<string> Errors,
    int RecordCount, int EmployeeRecordCount, string SpecificationVersion)
{
    public static SsaWageFileBuildResult Failure(params string[] errors) => new(false, [], errors, 0, 0, string.Empty);
}
