using System.Globalization;
using System.Text;
using BrassLedger.Application.Accounting;

namespace BrassLedger.Infrastructure.Accounting;

public static class SsaEfw2cFileBuilder
{
    public static SsaWageFileBuildResult Build(W2cPackageData package, SsaEfw2cSubmitter submitter)
    {
        var errors = Validate(package, submitter);
        var employees = (package.Employees ?? []).Where(item => item.SubmitToSsa).ToArray();
        if (errors.Count > 0) return new(false, [], errors, 0, employees.Length, submitter.SpecificationVersion);
        try
        {
            var records = new List<string> { BuildRca(submitter), BuildRce(package, submitter) };
            foreach (var employee in employees)
            {
                records.Add(BuildRcw(employee));
                if (RequiresRco(employee)) records.Add(BuildRco(employee));
            }
            records.Add(BuildRct(employees));
            var optionalEmployees = employees.Where(RequiresRco).ToArray();
            if (optionalEmployees.Length > 0) records.Add(BuildRcu(optionalEmployees));
            records.Add(BuildRcf(employees.Length));
            if (records.Any(record => Encoding.ASCII.GetByteCount(record) != 1024)) return SsaWageFileBuildResult.Failure("Every EFW2C record must contain exactly 1,024 ASCII bytes.");
            return new(true, Encoding.ASCII.GetBytes(string.Join("\r\n", records)), [], records.Count, employees.Length, submitter.SpecificationVersion);
        }
        catch (ArgumentException exception) { return SsaWageFileBuildResult.Failure(exception.Message); }
    }

    private static List<string> Validate(W2cPackageData package, SsaEfw2cSubmitter value)
    {
        var errors = new List<string>();
        if (package.TaxYear != value.SpecificationTaxYear) errors.Add("The correction tax year must exactly match the approved SSA EFW2C specification tax year.");
        if (string.IsNullOrWhiteSpace(value.SpecificationVersion) || !Uri.TryCreate(value.OfficialSpecificationUrl, UriKind.Absolute, out var source) || source.Scheme != "https" || !(source.Host.Equals("ssa.gov", StringComparison.OrdinalIgnoreCase) || source.Host.EndsWith(".ssa.gov", StringComparison.OrdinalIgnoreCase))) errors.Add("An exact official HTTPS SSA specification URL and version are required.");
        if (Digits(value.SubmitterEin).Length != 9 || Digits(package.EmployerEin).Length != 9) errors.Add("Submitter and employer EINs must each contain nine digits.");
        if (value.BsoUserId.Trim().Length != 8) errors.Add("The attesting employee's BSO User ID must contain exactly eight characters.");
        if (value.State.Trim().Length != 2 || value.EmployerState.Trim().Length != 2) errors.Add("Submitter and employer addresses require two-letter state abbreviations.");
        if (Digits(value.PostalCode).Length is not (5 or 9) || Digits(value.EmployerPostalCode).Length is not (5 or 9)) errors.Add("Submitter and employer postal codes must contain five or nine digits.");
        if (!value.ContactEmail.Contains('@') || !value.EmployerContactEmail.Contains('@')) errors.Add("Submitter and employer contact email addresses are required.");
        if (value.PreparerCode is not ("A" or "L" or "S" or "P" or "O")) errors.Add("Preparer code must be A, L, S, P, or O.");
        var included = (package.Employees ?? []).Where(item => item.SubmitToSsa).ToArray();
        if (included.Length == 0) errors.Add("The package contains no federal or identity correction eligible for SSA submission.");
        foreach (var employee in included)
        {
            var correct = employee.CorrectInformation;
            if (Digits(correct.SocialSecurityNumber).Length != 9 || string.IsNullOrWhiteSpace(correct.FirstName) || string.IsNullOrWhiteSpace(correct.LastName)) errors.Add($"Employee {correct.EmployeeNumber} requires a nine-digit SSN and separate first and last names.");
            if (string.IsNullOrWhiteSpace(correct.AddressLine1) || string.IsNullOrWhiteSpace(correct.City) || correct.State.Trim().Length != 2 || Digits(correct.PostalCode).Length is not (5 or 9)) errors.Add($"Employee {correct.EmployeeNumber} requires a complete domestic mailing address.");
            if (MoneyValues(employee.PreviouslyReported).Concat(MoneyValues(correct)).Any(amount => amount < 0 || amount > 99_999_999.99m)) errors.Add($"Employee {correct.EmployeeNumber} contains a negative or overlength EFW2C money amount.");
            foreach (var wageStatement in new[] { employee.PreviouslyReported, correct })
            {
                if (wageStatement.Box5MedicareWagesAndTips < wageStatement.Box3SocialSecurityWages + wageStatement.Box7SocialSecurityTips) errors.Add($"Employee {correct.EmployeeNumber} has Medicare wages below Social Security wages plus Social Security tips.");
                if (package.TaxYear == 2026 && wageStatement.Box3SocialSecurityWages + wageStatement.Box7SocialSecurityTips > 184_500m) errors.Add($"Employee {correct.EmployeeNumber} exceeds the 2026 Social Security wage base across boxes 3 and 7.");
                var box12 = wageStatement.Box12Amounts ?? [];
                if (box12.GroupBy(item => item.Code, StringComparer.Ordinal).Any(group => group.Count() > 1) || box12.Any(item => item.Code is not ("TP" or "TT"))) errors.Add($"Employee {correct.EmployeeNumber} contains duplicate or unsupported EFW2C box 12 codes; the reviewed 2026 optional layout supports TP and TT once each.");
                var occupations = wageStatement.TreasuryTippedOccupationCodes ?? [];
                if (occupations.Count > 2 || occupations.Any(code => code.Length != 3 || code.Any(character => !char.IsDigit(character)))) errors.Add($"Employee {correct.EmployeeNumber} has invalid Treasury Tipped Occupation Codes.");
                if (Box12(wageStatement, "TP") > 0 && occupations.Count == 0) errors.Add($"Employee {correct.EmployeeNumber} requires a Treasury Tipped Occupation Code when code TP cash tips are reported.");
                if (Box12(wageStatement, "TP") == 0 && occupations.Count > 0) errors.Add($"Employee {correct.EmployeeNumber} cannot report Treasury Tipped Occupation Codes without code TP cash tips.");
                if (package.TaxYear < 2026 && (wageStatement.Box7SocialSecurityTips != 0 || box12.Count > 0 || occupations.Count > 0)) errors.Add($"Employee {correct.EmployeeNumber} contains fields that are available only in the reviewed 2026 EFW2C layout.");
            }
        }
        return errors.Distinct().ToList();
    }

    private static string BuildRca(SsaEfw2cSubmitter s)
    {
        var record = Blank(); Put(record, 1, 3, "RCA"); Put(record, 4, 9, Digits(s.SubmitterEin)); Put(record, 13, 8, s.BsoUserId); Put(record, 30, 2, "98");
        Put(record, 32, 57, s.SubmitterName); Put(record, 89, 22, s.LocationAddress); Put(record, 111, 22, s.DeliveryAddress); Put(record, 133, 22, s.City); Put(record, 155, 2, s.State); PutZip(record, 157, s.PostalCode);
        Put(record, 212, 27, s.ContactName); Put(record, 239, 15, Digits(s.ContactPhone)); Put(record, 262, 40, s.ContactEmail, false); Put(record, 316, 1, s.PreparerCode); Put(record, 317, 1, "0"); return new(record);
    }

    private static string BuildRce(W2cPackageData p, SsaEfw2cSubmitter s)
    {
        var record = Blank(); Put(record, 1, 3, "RCE"); Put(record, 4, 4, p.TaxYear.ToString(CultureInfo.InvariantCulture)); Put(record, 17, 9, Digits(p.EmployerEin));
        Put(record, 44, 57, p.EmployerLegalName); Put(record, 101, 22, s.EmployerLocationAddress); Put(record, 123, 22, s.EmployerDeliveryAddress); Put(record, 145, 22, s.EmployerCity); Put(record, 167, 2, s.EmployerState); PutZip(record, 169, s.EmployerPostalCode);
        Put(record, 223, 1, "R"); Put(record, 227, 1, "N"); Put(record, 228, 27, s.EmployerContactName); Put(record, 255, 15, Digits(s.EmployerContactPhone)); Put(record, 285, 40, s.EmployerContactEmail, false); return new(record);
    }

    private static string BuildRcw(W2cEmployeeData item)
    {
        var old = item.PreviouslyReported; var current = item.CorrectInformation; var record = Blank(); Put(record, 1, 3, "RCW");
        if (Digits(old.SocialSecurityNumber) != Digits(current.SocialSecurityNumber)) Put(record, 4, 9, Digits(old.SocialSecurityNumber)); Put(record, 13, 9, Digits(current.SocialSecurityNumber));
        if (old.FirstName != current.FirstName || old.MiddleName != current.MiddleName || old.LastName != current.LastName) { Put(record, 22, 15, old.FirstName); Put(record, 37, 15, old.MiddleName); Put(record, 52, 20, old.LastName); }
        Put(record, 72, 15, current.FirstName); Put(record, 87, 15, current.MiddleName); Put(record, 102, 20, current.LastName); Put(record, 122, 22, current.AddressLine2); Put(record, 144, 22, current.AddressLine1); Put(record, 166, 22, current.City); Put(record, 188, 2, current.State); PutZip(record, 190, current.PostalCode);
        PutMoneyPair(record, 244, old.Box1WagesTipsOtherCompensation, current.Box1WagesTipsOtherCompensation, 11); PutMoneyPair(record, 266, old.Box2FederalIncomeTaxWithheld, current.Box2FederalIncomeTaxWithheld, 11);
        PutMoneyPair(record, 288, old.Box3SocialSecurityWages, current.Box3SocialSecurityWages, 11); PutMoneyPair(record, 310, old.Box4SocialSecurityTaxWithheld, current.Box4SocialSecurityTaxWithheld, 11);
        PutMoneyPair(record, 332, old.Box5MedicareWagesAndTips, current.Box5MedicareWagesAndTips, 11); PutMoneyPair(record, 354, old.Box6MedicareTaxWithheld, current.Box6MedicareTaxWithheld, 11);
        PutMoneyPair(record, 376, old.Box7SocialSecurityTips, current.Box7SocialSecurityTips, 11);
        if (!(old.TreasuryTippedOccupationCodes ?? []).SequenceEqual(current.TreasuryTippedOccupationCodes ?? []))
        {
            var oldCodes = old.TreasuryTippedOccupationCodes ?? []; var currentCodes = current.TreasuryTippedOccupationCodes ?? [];
            if (oldCodes.Count > 0) Put(record, 1009, 3, oldCodes[0]); if (currentCodes.Count > 0) Put(record, 1012, 3, currentCodes[0]);
            if (oldCodes.Count > 1) Put(record, 1015, 3, oldCodes[1]); if (currentCodes.Count > 1) Put(record, 1018, 3, currentCodes[1]);
        }
        return new(record);
    }

    private static string BuildRco(W2cEmployeeData item)
    {
        var record = Blank(); Put(record, 1, 3, "RCO");
        PutMoneyPair(record, 453, Box12(item.PreviouslyReported, "TP"), Box12(item.CorrectInformation, "TP"), 11);
        PutMoneyPair(record, 475, Box12(item.PreviouslyReported, "TT"), Box12(item.CorrectInformation, "TT"), 11);
        return new(record);
    }

    private static string BuildRct(IReadOnlyList<W2cEmployeeData> employees)
    {
        var record = Blank(); Put(record, 1, 3, "RCT"); PutNumeric(record, 4, 7, employees.Count);
        PutTotalPair(record, 11, employees, x => x.Box1WagesTipsOtherCompensation); PutTotalPair(record, 41, employees, x => x.Box2FederalIncomeTaxWithheld);
        PutTotalPair(record, 71, employees, x => x.Box3SocialSecurityWages); PutTotalPair(record, 101, employees, x => x.Box4SocialSecurityTaxWithheld);
        PutTotalPair(record, 131, employees, x => x.Box5MedicareWagesAndTips); PutTotalPair(record, 161, employees, x => x.Box6MedicareTaxWithheld);
        PutTotalPair(record, 191, employees, x => x.Box7SocialSecurityTips); return new(record);
    }

    private static string BuildRcu(IReadOnlyList<W2cEmployeeData> employees)
    {
        var record = Blank(); Put(record, 1, 3, "RCU"); PutNumeric(record, 4, 7, employees.Count);
        PutOptionalTotalPair(record, 611, employees, "TP"); PutOptionalTotalPair(record, 641, employees, "TT");
        return new(record);
    }

    private static string BuildRcf(int count) { var record = Blank(); Put(record, 1, 3, "RCF"); PutNumeric(record, 4, 9, count); return new(record); }
    private static char[] Blank() => Enumerable.Repeat(' ', 1024).ToArray();
    private static void Put(char[] record, int start, int length, string? value, bool upper = true) { value = (value ?? string.Empty).Trim(); if (upper) value = value.ToUpperInvariant(); if (value.Any(character => character > 127)) throw new ArgumentException($"EFW2C field at position {start} contains a non-ASCII character."); if (value.Length > length) throw new ArgumentException($"EFW2C field at position {start} exceeds its {length}-character limit."); value.CopyTo(0, record, start - 1, value.Length); }
    private static void PutZip(char[] record, int start, string value) { var digits = Digits(value); Put(record, start, 5, digits[..Math.Min(5, digits.Length)]); if (digits.Length == 9) Put(record, start + 5, 4, digits[5..]); }
    private static void PutNumeric(char[] record, int start, int length, long value) { var text = value.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0'); if (text.Length > length) throw new ArgumentException($"Numeric EFW2C field at position {start} exceeds its {length}-digit limit."); text.CopyTo(0, record, start - 1, length); }
    private static void PutMoney(char[] record, int start, int length, decimal value) => PutNumeric(record, start, length, checked((long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero)));
    private static void PutMoneyPair(char[] record, int start, decimal old, decimal current, int length) { if (old == current) return; PutMoney(record, start, length, old); PutMoney(record, start + length, length, current); }
    private static void PutTotalPair(char[] record, int start, IReadOnlyList<W2cEmployeeData> employees, Func<W2EmployeeData, decimal> select) { var changed = employees.Where(item => select(item.PreviouslyReported) != select(item.CorrectInformation)).ToArray(); if (changed.Length == 0) return; PutMoney(record, start, 15, changed.Sum(item => select(item.PreviouslyReported))); PutMoney(record, start + 15, 15, changed.Sum(item => select(item.CorrectInformation))); }
    private static void PutOptionalTotalPair(char[] record, int start, IReadOnlyList<W2cEmployeeData> employees, string code) { var changed = employees.Where(item => Box12(item.PreviouslyReported, code) != Box12(item.CorrectInformation, code)).ToArray(); if (changed.Length == 0) return; PutMoney(record, start, 15, changed.Sum(item => Box12(item.PreviouslyReported, code))); PutMoney(record, start + 15, 15, changed.Sum(item => Box12(item.CorrectInformation, code))); }
    private static decimal Box12(W2EmployeeData employee, string code) => (employee.Box12Amounts ?? []).Where(item => item.Code.Equals(code, StringComparison.Ordinal)).Sum(item => item.Amount);
    private static bool RequiresRco(W2cEmployeeData item) => Box12(item.PreviouslyReported, "TP") != Box12(item.CorrectInformation, "TP") || Box12(item.PreviouslyReported, "TT") != Box12(item.CorrectInformation, "TT");
    private static decimal[] MoneyValues(W2EmployeeData value) => [value.Box1WagesTipsOtherCompensation, value.Box2FederalIncomeTaxWithheld, value.Box3SocialSecurityWages, value.Box4SocialSecurityTaxWithheld, value.Box5MedicareWagesAndTips, value.Box6MedicareTaxWithheld, value.Box7SocialSecurityTips, .. (value.Box12Amounts ?? []).Select(item => item.Amount)];
    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
}
