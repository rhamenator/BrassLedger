using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Taxation;

namespace BrassLedger.Infrastructure.Tests;

public sealed class PayrollTaxRuleAccuracyTests
{
    private static decimal EvaluateFuta(decimal pay, decimal priorTaxableWages)
    {
        var rule = new TaxRuleSet { CalculationMethod = "employer-rate-wage-base", TaxType = "Employer unemployment" };
        TaxRuleParameter[] parameters =
        [
            new() { ParameterCode = "employer-rate", NumericValue = 0.006m },
            new() { ParameterCode = "wage-base", NumericValue = 7000m }
        ];
        return TaxRuleEvaluator.Evaluate(rule, parameters, [], new TaxRuleEvaluationContext(pay, 0, "Single", "Biweekly", PriorTaxableWages: priorTaxableWages));
    }

    [Theory]
    [InlineData(5000, 0, 30.00)]
    [InlineData(5000, 5000, 12.00)]
    [InlineData(5000, 7000, 0.00)]
    [InlineData(5000, 9000, 0.00)]
    public void EmployerWageBaseRule_SubtractsPriorYearToDateWages(double pay, double prior, double expected) =>
        Assert.Equal((decimal)expected, EvaluateFuta((decimal)pay, (decimal)prior));

    [Fact]
    public void FutaDetection_MatchesCodeObligationAndTaxType()
    {
        Assert.True(AccountingTransactionService.IsFutaTax("FED-FUTA", "", "Employer unemployment"));
        Assert.True(AccountingTransactionService.IsFutaTax("", "", "US-FUTA"));
        Assert.False(AccountingTransactionService.IsFutaTax("AZ-SUI", "", "Employer unemployment"));
    }

    [Fact]
    public void ResidentCredit_AppliesEachWorkJurisdictionRateOnlyToItsShareOfWages()
    {
        var allocations = new[]
        {
            new AccountingTransactionService.PayrollWorkAllocation("PA", "", "", "", 600m),
            new AccountingTransactionService.PayrollWorkAllocation("NJ", "", "", "", 400m)
        };
        var rules = new[] { new PayrollJurisdictionRule { ResidenceJurisdiction = "NJ", WorkJurisdiction = "PA", ResidentCreditRate = 1m } };

        Assert.Equal(0.6m, AccountingTransactionService.ResidentCreditShare(allocations, rules));
        Assert.Equal(0m, AccountingTransactionService.ResidentCreditShare([], rules));
        Assert.Equal(0m, AccountingTransactionService.ResidentCreditShare(allocations, []));
    }
}
