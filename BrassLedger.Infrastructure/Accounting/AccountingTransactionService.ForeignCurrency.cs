using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed partial class AccountingTransactionService
{
    private sealed record TransactionRate(
        string TransactionCurrency,
        string BaseCurrency,
        Guid? ExchangeRateId,
        decimal FactorToBase,
        DateOnly? EffectiveOn,
        string Source,
        string SourceReference)
    {
        public bool IsForeign => TransactionCurrency != BaseCurrency;
        public decimal ToBase(decimal amount) => RoundCurrency(amount * FactorToBase);
    }

    private async Task<(TransactionRate? Rate, string? Error)> ResolveTransactionRateAsync(
        BrassLedgerDbContext db,
        Guid companyId,
        string? requestedCurrency,
        Guid? exchangeRateId,
        DateOnly transactionDate,
        CancellationToken cancellationToken)
    {
        var baseCurrency = await db.Companies.AsNoTracking().Where(company => company.Id == companyId).Select(company => company.BaseCurrency).SingleAsync(cancellationToken);
        var currency = NormalizeTransactionCurrency(requestedCurrency, baseCurrency);
        if (currency is null) return (null, "Transaction currency must be a three-letter ISO-style code.");
        if (currency == baseCurrency)
        {
            if (exchangeRateId.HasValue) return (null, "Do not select an exchange rate for a base-currency transaction.");
            return (new(currency, baseCurrency, null, 1m, transactionDate, "Company base currency", string.Empty), null);
        }
        if (!exchangeRateId.HasValue) return (null, $"Select a retained closing rate converting {currency} to {baseCurrency} for the transaction date.");
        var retained = await db.CurrencyExchangeRates.AsNoTracking().SingleOrDefaultAsync(rate => rate.Id == exchangeRateId && rate.CompanyId == companyId && rate.IsActive, cancellationToken);
        if (retained is null || retained.RateType != CurrencyRateType.Closing || retained.EffectiveOn > transactionDate || retained.Rate <= 0m)
            return (null, "The selected exchange rate must be an active closing rate effective on or before the transaction date.");
        decimal factor;
        if (retained.BaseCurrency == currency && retained.QuoteCurrency == baseCurrency) factor = retained.Rate;
        else if (retained.BaseCurrency == baseCurrency && retained.QuoteCurrency == currency) factor = 1m / retained.Rate;
        else return (null, $"The selected exchange rate does not convert {currency} and {baseCurrency}.");
        factor = decimal.Round(factor, 10, MidpointRounding.AwayFromZero);
        if (factor <= 0m) return (null, "The selected exchange rate cannot produce a positive conversion factor.");
        return (new(currency, baseCurrency, retained.Id, factor, retained.EffectiveOn, retained.Source, retained.SourceReference), null);
    }

    private static string? NormalizeTransactionCurrency(string? requestedCurrency, string baseCurrency)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedCurrency) ? baseCurrency : requestedCurrency.Trim().ToUpperInvariant();
        return candidate.Length == 3 && candidate.All(character => character is >= 'A' and <= 'Z') ? candidate : null;
    }
}
