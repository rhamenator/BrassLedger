using System.Text.Json;
using BrassLedger.Domain.Accounting;

namespace BrassLedger.Infrastructure.Taxation;

internal sealed record TaxRuleEvaluationContext(
    decimal GrossPay,
    int Allowances,
    string FilingStatus,
    string PayrollFrequency,
    string ResidenceState = "",
    string ResidenceCity = "",
    string WorkState = "",
    string WorkCity = "",
    decimal OtherStateWithholding = 0m);

internal static class TaxRuleEvaluator
{
    public static decimal Evaluate(
        TaxRuleSet rule,
        IEnumerable<TaxRuleParameter> parameters,
        IEnumerable<TaxRuleBracket> brackets,
        TaxRuleEvaluationContext context)
    {
        var parameterList = parameters.ToArray();
        var values = parameterList.Where(parameter => parameter.NumericValue.HasValue)
            .ToDictionary(parameter => parameter.ParameterCode, parameter => parameter.NumericValue!.Value, StringComparer.OrdinalIgnoreCase);
        var annualization = ResolveAnnualization(context.PayrollFrequency, values);
        var configuredAnnualization = values.GetValueOrDefault("annualization-factor", 1m);
        var pay = Math.Max(0, context.GrossPay - context.Allowances * values.GetValueOrDefault("allowance-per-pay", 0m));
        decimal amount;

        if (rule.CalculationMethod.Equals("allowance-phaseout", StringComparison.OrdinalIgnoreCase))
        {
            var statusKey = IsMarried(context.FilingStatus) ? "married" : "single";
            var frequencyKey = context.PayrollFrequency.Trim().ToLowerInvariant();
            var baseAllowance = values.GetValueOrDefault($"{frequencyKey}-{statusKey}-base");
            var threshold = values.GetValueOrDefault($"{frequencyKey}-{statusKey}-threshold");
            var roundingDigits = (int)values.GetValueOrDefault("rounding-digits", 2m);
            var tentative = decimal.Round(context.GrossPay * values.GetValueOrDefault("withholding-rate"), roundingDigits, MidpointRounding.AwayFromZero);
            var phaseout = decimal.Round(Math.Max(0, context.GrossPay - threshold) * values.GetValueOrDefault("allowance-phaseout-rate"), roundingDigits, MidpointRounding.AwayFromZero);
            amount = Math.Max(0, tentative - Math.Max(0, baseAllowance - phaseout) - Math.Max(0, context.OtherStateWithholding));
        }
        else if (rule.CalculationMethod.Equals("base-plus-rate-schedule", StringComparison.OrdinalIgnoreCase))
        {
            var netPay = PeriodNetPay(context, values, annualization);
            var schedule = ReadSchedule(parameterList, context.PayrollFrequency, annualization);
            var bracket = schedule.FirstOrDefault(item => item.Through is null || netPay < item.Through.Value);
            amount = bracket is null ? 0m : bracket.BaseTax + decimal.Round(Math.Max(0, netPay - bracket.Over) * bracket.Rate, 2, MidpointRounding.AwayFromZero);
            amount *= values.GetValueOrDefault("result-multiplier", 1m);
        }
        else if (rule.CalculationMethod.Equals("whole-wage-annualized", StringComparison.OrdinalIgnoreCase))
        {
            var annualNet = PeriodNetPay(context, values, annualization) * annualization;
            var bracket = brackets.OrderBy(item => item.Sequence).FirstOrDefault(item => item.UpperBoundAmount <= 0 || annualNet < item.UpperBoundAmount);
            amount = bracket is null ? 0m : annualNet * bracket.Rate / annualization;
            amount *= values.GetValueOrDefault("result-multiplier", 1m);
        }
        else if (rule.CalculationMethod.Equals("annualized-exclusion-rate", StringComparison.OrdinalIgnoreCase))
        {
            var annualPay = context.GrossPay * annualization;
            var bracket = brackets.OrderBy(item => item.Sequence).FirstOrDefault(item => item.UpperBoundAmount <= 0 || annualPay <= item.UpperBoundAmount);
            amount = bracket is null || bracket.Rate <= 0 ? 0m : Math.Max(0, annualPay - bracket.FixedAmount) * bracket.Rate / annualization;
        }
        else if (rule.CalculationMethod.Equals("wage-bracket", StringComparison.OrdinalIgnoreCase))
        {
            var annualPay = pay * configuredAnnualization;
            var bracket = brackets.OrderBy(item => item.Sequence).FirstOrDefault(item => item.UpperBoundAmount <= 0 || annualPay <= item.UpperBoundAmount);
            amount = bracket is null ? 0m : bracket.FixedAmount / configuredAnnualization;
        }
        else if (rule.CalculationMethod.Equals("progressive-annualized", StringComparison.OrdinalIgnoreCase))
        {
            var annualPay = pay * configuredAnnualization;
            var previous = 0m;
            amount = 0m;
            foreach (var bracket in brackets.OrderBy(item => item.Sequence))
            {
                var ceiling = bracket.UpperBoundAmount <= 0 ? annualPay : bracket.UpperBoundAmount;
                amount += Math.Max(0, Math.Min(annualPay, ceiling) - previous) * bracket.Rate;
                previous = ceiling;
                if (annualPay <= ceiling) break;
            }
            amount /= configuredAnnualization;
        }
        else if (rule.CalculationMethod.Equals("local-code-e", StringComparison.OrdinalIgnoreCase))
        {
            var standardAllowance = Math.Clamp(context.GrossPay * values.GetValueOrDefault("allowance-percent", 0m), values.GetValueOrDefault("allowance-minimum", 0m), values.GetValueOrDefault("allowance-maximum", decimal.MaxValue));
            var localTaxable = Math.Max(0, context.GrossPay - standardAllowance - context.Allowances * values.GetValueOrDefault("dependent-allowance", 0m));
            amount = Math.Min(localTaxable, values.GetValueOrDefault("wage-base", decimal.MaxValue)) * values.GetValueOrDefault("tax-rate", values.GetValueOrDefault("rate", 0m));
        }
        else if (rule.CalculationMethod.Equals("hourly-assessment", StringComparison.OrdinalIgnoreCase))
        {
            amount = Math.Max(0, values.GetValueOrDefault("hours-per-pay", 0m)) * values.GetValueOrDefault("hourly-rate", values.GetValueOrDefault("rate", 0m));
        }
        else
        {
            var rate = values.GetValueOrDefault("flat-rate", values.GetValueOrDefault("employer-rate", values.GetValueOrDefault("tax-rate", values.GetValueOrDefault("rate", 0m))));
            amount = Math.Min(pay, values.GetValueOrDefault("wage-base", decimal.MaxValue)) * rate;
        }

        amount = Math.Max(0, amount - context.Allowances * values.GetValueOrDefault("allowance-credit", 0m));
        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public static bool IsApplicable(TaxRuleSet rule, IEnumerable<TaxRuleParameter> parameters, TaxRuleEvaluationContext context)
    {
        if (string.IsNullOrWhiteSpace(rule.ApplicabilityJson) || rule.ApplicabilityJson == "{}") return true;
        try
        {
            using var document = JsonDocument.Parse(rule.ApplicabilityJson);
            return EvaluateApplicabilityNode(document.RootElement, parameters, context);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool EvaluateApplicabilityNode(JsonElement node, IEnumerable<TaxRuleParameter> parameters, TaxRuleEvaluationContext context)
    {
        if (node.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array && !allOf.EnumerateArray().All(item => EvaluateApplicabilityNode(item, parameters, context))) return false;
        if (node.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array && !anyOf.EnumerateArray().Any(item => EvaluateApplicabilityNode(item, parameters, context))) return false;
        if (!node.TryGetProperty("field", out var fieldElement)) return true;

        var field = fieldElement.GetString() ?? string.Empty;
        var operation = node.TryGetProperty("operator", out var operationElement) ? operationElement.GetString() ?? "equals" : "equals";
        if (field.Equals("annualizedNetWages", StringComparison.OrdinalIgnoreCase))
        {
            var numericParameters = parameters.Where(parameter => parameter.NumericValue.HasValue).ToDictionary(parameter => parameter.ParameterCode, parameter => parameter.NumericValue!.Value, StringComparer.OrdinalIgnoreCase);
            var annualization = ResolveAnnualization(context.PayrollFrequency, numericParameters);
            var actual = PeriodNetPay(context, numericParameters, annualization) * annualization;
            var expected = node.GetProperty("value").GetDecimal();
            return operation switch { "atLeast" => actual >= expected, "greaterThan" => actual > expected, "atMost" => actual <= expected, "lessThan" => actual < expected, _ => actual == expected };
        }

        var actualText = field.ToLowerInvariant() switch
        {
            "filingstatus" => context.FilingStatus,
            "payrollfrequency" => context.PayrollFrequency,
            "residencestate" => context.ResidenceState,
            "residencecity" => context.ResidenceCity,
            "workstate" => context.WorkState,
            "workcity" => context.WorkCity,
            _ => string.Empty
        };
        var expectedText = node.TryGetProperty("value", out var valueElement) ? valueElement.GetString() ?? string.Empty : string.Empty;
        return operation switch
        {
            "notEquals" => !TextEquals(actualText, expectedText),
            _ => TextEquals(actualText, expectedText)
        };
    }

    private static decimal PeriodNetPay(TaxRuleEvaluationContext context, IReadOnlyDictionary<string, decimal> values, decimal annualization)
    {
        var frequency = context.PayrollFrequency.Trim().ToLowerInvariant();
        var deduction = values.GetValueOrDefault($"{frequency}-deduction", values.GetValueOrDefault("annual-deduction", 0m) / annualization);
        var exemption = values.GetValueOrDefault($"{frequency}-exemption", values.GetValueOrDefault("annual-exemption", 0m) / annualization);
        return Math.Max(0, context.GrossPay - deduction - context.Allowances * exemption);
    }

    private static decimal ResolveAnnualization(string payrollFrequency, IReadOnlyDictionary<string, decimal> values)
    {
        var key = payrollFrequency.Trim().ToLowerInvariant();
        if (values.TryGetValue($"{key}-annualization-factor", out var configured)) return configured;
        if (values.TryGetValue("annualization-factor", out configured)) return configured;
        return key switch { "daily" => 260m, "weekly" => 52m, "biweekly" => 26m, "semimonthly" => 24m, "monthly" => 12m, "quarterly" => 4m, "semiannual" => 2m, "annual" => 1m, _ => 1m };
    }

    private static IReadOnlyList<ScheduleBracket> ReadSchedule(IEnumerable<TaxRuleParameter> parameters, string payrollFrequency, decimal annualization)
    {
        var parameter = parameters.FirstOrDefault(item => item.ParameterCode.Equals("schedules-json", StringComparison.OrdinalIgnoreCase));
        if (parameter is null || string.IsNullOrWhiteSpace(parameter.TextValue)) return [];
        using var document = JsonDocument.Parse(parameter.TextValue);
        var key = payrollFrequency.Trim();
        var usingAnnualFallback = !TryGetPropertyIgnoreCase(document.RootElement, key, out var schedule);
        if (usingAnnualFallback && !TryGetPropertyIgnoreCase(document.RootElement, "Annual", out schedule)) return [];
        var scale = usingAnnualFallback ? annualization : 1m;
        return schedule.EnumerateArray().Select(item => new ScheduleBracket(
            item.GetProperty("over").GetDecimal() / scale,
            item.TryGetProperty("through", out var through) && through.ValueKind != JsonValueKind.Null ? through.GetDecimal() / scale : null,
            item.GetProperty("baseTax").GetDecimal() / scale,
            item.GetProperty("rate").GetDecimal())).ToArray();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        }
        value = default;
        return false;
    }

    private static bool TextEquals(string actual, string expected)
    {
        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
        var actualState = TaxRuleCatalog.StateJurisdictions.FirstOrDefault(state => state.Code.Equals(actual, StringComparison.OrdinalIgnoreCase) || state.Name.Equals(actual, StringComparison.OrdinalIgnoreCase));
        var expectedState = TaxRuleCatalog.StateJurisdictions.FirstOrDefault(state => state.Code.Equals(expected, StringComparison.OrdinalIgnoreCase) || state.Name.Equals(expected, StringComparison.OrdinalIgnoreCase));
        return actualState is not null && expectedState is not null && actualState.Code.Equals(expectedState.Code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarried(string filingStatus) => filingStatus.Contains("married", StringComparison.OrdinalIgnoreCase);
    private sealed record ScheduleBracket(decimal Over, decimal? Through, decimal BaseTax, decimal Rate);
}
