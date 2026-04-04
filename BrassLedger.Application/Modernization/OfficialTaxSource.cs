namespace BrassLedger.Application.Modernization;

public sealed record OfficialTaxSource(
    string Jurisdiction,
    string SourceName,
    string Url,
    string Notes);
