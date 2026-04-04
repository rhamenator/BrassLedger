namespace BrassLedger.Application.Catalog;

public sealed record OfficialTaxSource(
    string Jurisdiction,
    string SourceName,
    string Url,
    string Notes);

