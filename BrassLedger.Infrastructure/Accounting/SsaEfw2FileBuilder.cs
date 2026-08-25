using System.Globalization;
using System.Text;
using BrassLedger.Application.Accounting;

namespace BrassLedger.Infrastructure.Accounting;

public static class SsaEfw2FileBuilder
{
    private static readonly int[] EmployeeMoneyStarts = [188, 199, 210, 221, 232, 243, 254, 276, 287, 298, 309, 320, 331, 353, 364, 375, 386, 408, 419, 430, 441, 452, 463, 474];
    private static readonly int[] TotalMoneyStarts = [10, 25, 40, 55, 70, 85, 100, 130, 145, 160, 175, 190, 205, 235, 250, 265, 280, 295, 310, 325, 340, 355, 370, 385, 400];

    public static SsaWageFileBuildResult Build(W2PackageData package, SsaEfw2Submitter submitter)
    {
        var errors = Validate(package, submitter);
        var employees = package.Employees?.ToArray() ?? [];
        if (errors.Count > 0) return new(false, [], errors, 0, employees.Length, submitter.SpecificationVersion);
        try
        {
            var records = new List<string> { BuildRa(submitter), BuildRe(package, submitter) };
            records.AddRange(employees.Select(BuildRw));
            records.Add(BuildRt(employees));
            records.Add(BuildRf(employees.Length));
            if (records.Any(record => Encoding.ASCII.GetByteCount(record) != 512)) return SsaWageFileBuildResult.Failure("Every EFW2 record must contain exactly 512 ASCII bytes.");
            return new(true, Encoding.ASCII.GetBytes(string.Join("\r\n", records)), [], records.Count, employees.Length, submitter.SpecificationVersion);
        }
        catch (ArgumentException exception) { return SsaWageFileBuildResult.Failure(exception.Message); }
        catch (OverflowException) { return SsaWageFileBuildResult.Failure("An EFW2 money or count value exceeds its positional field."); }
    }

    private static List<string> Validate(W2PackageData package, SsaEfw2Submitter value)
    {
        var errors = new List<string>();
        if (package.TaxYear != value.SpecificationTaxYear) errors.Add("The wage-report tax year must exactly match the approved SSA EFW2 specification tax year.");
        var expectedFile = $"{value.SpecificationTaxYear % 100:00}efw2.pdf";
        if (string.IsNullOrWhiteSpace(value.SpecificationVersion) || !Uri.TryCreate(value.OfficialSpecificationUrl, UriKind.Absolute, out var source) || source.Scheme != "https" || !(source.Host.Equals("ssa.gov", StringComparison.OrdinalIgnoreCase) || source.Host.EndsWith(".ssa.gov", StringComparison.OrdinalIgnoreCase)) || !source.AbsolutePath.EndsWith(expectedFile, StringComparison.OrdinalIgnoreCase)) errors.Add($"Use an exact official HTTPS SSA EFW2 specification URL ending in {expectedFile} and retain its version.");
        if (Digits(value.SubmitterEin).Length != 9 || Digits(package.EmployerEin).Length != 9) errors.Add("Submitter and employer EINs must each contain nine digits.");
        if (value.BsoUserId.Trim().Length != 8) errors.Add("The attesting employee's BSO User ID must contain exactly eight characters.");
        if (value.State.Trim().Length != 2 || value.EmployerState.Trim().Length != 2) errors.Add("Submitter and employer addresses require two-letter state abbreviations.");
        if (Digits(value.PostalCode).Length is not (5 or 9) || Digits(value.EmployerPostalCode).Length is not (5 or 9)) errors.Add("Submitter and employer postal codes must contain five or nine digits.");
        if (!value.ContactEmail.Contains('@') || !value.EmployerContactEmail.Contains('@')) errors.Add("Submitter and employer contact email addresses are required.");
        if (value.PreparerCode is not ("A" or "L" or "S" or "P" or "O")) errors.Add("Preparer code must be A, L, S, P, or O.");
        if (value.KindOfEmployer is not ("F" or "S" or "T" or "Y" or "N")) errors.Add("Kind of employer must be F, S, T, Y, or N.");
        if (value.EmploymentCode is not ("A" or "H" or "M" or "Q" or "X" or "F" or "R")) errors.Add("Employment code must be A, H, M, Q, X, F, or R.");
        if (value.EmployerSignaturePin.Length is not (0 or 10)) errors.Add("Employer Signature PIN must be blank or exactly ten characters.");
        var employees = package.Employees?.ToArray() ?? [];
        if (employees.Length == 0) errors.Add("The approved W-2/W-3 package contains no employees.");
        if (employees.Length > 1_000_000) errors.Add("An SSA EFW2 file cannot contain more than 1,000,000 RW employee records.");
        foreach (var employee in employees)
        {
            var ssn = Digits(employee.SocialSecurityNumber);
            if (ssn.Length != 9 || ssn == "000000000" || ssn.StartsWith("666", StringComparison.Ordinal) || ssn.StartsWith('9')) errors.Add($"Employee {employee.EmployeeNumber} requires a valid nine-digit SSN for EFW2 filing.");
            if (string.IsNullOrWhiteSpace(employee.FirstName) || string.IsNullOrWhiteSpace(employee.LastName)) errors.Add($"Employee {employee.EmployeeNumber} requires separate first and last names.");
            if (string.IsNullOrWhiteSpace(employee.AddressLine1) || string.IsNullOrWhiteSpace(employee.City) || employee.State.Trim().Length != 2 || Digits(employee.PostalCode).Length is not (5 or 9)) errors.Add($"Employee {employee.EmployeeNumber} requires a complete domestic mailing address.");
            if (MoneyValues(employee).Any(amount => amount < 0 || amount > 999_999_999.99m)) errors.Add($"Employee {employee.EmployeeNumber} contains a negative or overlength EFW2 money amount.");
            if (employee.Box5MedicareWagesAndTips < employee.Box3SocialSecurityWages) errors.Add($"Employee {employee.EmployeeNumber} has Medicare wages below Social Security wages.");
        }
        if (employees.Length > 0 && !TotalsMatch(package, employees)) errors.Add("The immutable W-3 totals do not reconcile to the W-2 employee records.");
        return errors.Distinct().ToList();
    }

    private static string BuildRa(SsaEfw2Submitter s)
    {
        var record = Blank(); Put(record, 1, 2, "RA"); Put(record, 3, 9, Digits(s.SubmitterEin)); Put(record, 12, 8, s.BsoUserId); Put(record, 36, 2, "98");
        Put(record, 38, 57, s.SubmitterName); Put(record, 95, 22, s.LocationAddress); Put(record, 117, 22, s.DeliveryAddress); Put(record, 139, 22, s.City); Put(record, 161, 2, s.State); PutZip(record, 163, s.PostalCode);
        Put(record, 217, 57, s.SubmitterName); Put(record, 274, 22, s.LocationAddress); Put(record, 296, 22, s.DeliveryAddress); Put(record, 318, 22, s.City); Put(record, 340, 2, s.State); PutZip(record, 342, s.PostalCode);
        Put(record, 396, 27, s.ContactName); Put(record, 423, 15, Digits(s.ContactPhone)); Put(record, 446, 40, s.ContactEmail, false); Put(record, 500, 1, s.PreparerCode); return new(record);
    }

    private static string BuildRe(W2PackageData p, SsaEfw2Submitter s)
    {
        var record = Blank(); Put(record, 1, 2, "RE"); Put(record, 3, 4, p.TaxYear.ToString(CultureInfo.InvariantCulture)); Put(record, 8, 9, Digits(p.EmployerEin)); Put(record, 40, 57, p.EmployerLegalName);
        Put(record, 97, 22, s.EmployerLocationAddress); Put(record, 119, 22, s.EmployerDeliveryAddress); Put(record, 141, 22, s.EmployerCity); Put(record, 163, 2, s.EmployerState); PutZip(record, 165, s.EmployerPostalCode);
        Put(record, 174, 1, s.KindOfEmployer); Put(record, 219, 1, s.EmploymentCode); Put(record, 221, 1, "0"); Put(record, 222, 27, s.EmployerContactName); Put(record, 249, 15, Digits(s.EmployerContactPhone)); Put(record, 279, 40, s.EmployerContactEmail, false); Put(record, 319, 10, s.EmployerSignaturePin); return new(record);
    }

    private static string BuildRw(W2EmployeeData employee)
    {
        var record = Blank(); Put(record, 1, 2, "RW"); Put(record, 3, 9, Digits(employee.SocialSecurityNumber)); Put(record, 12, 15, employee.FirstName); Put(record, 27, 15, employee.MiddleName); Put(record, 42, 20, employee.LastName);
        Put(record, 66, 22, employee.AddressLine2); Put(record, 88, 22, employee.AddressLine1); Put(record, 110, 22, employee.City); Put(record, 132, 2, employee.State); PutZip(record, 134, employee.PostalCode);
        foreach (var start in EmployeeMoneyStarts) PutMoney(record, start, 11, 0);
        PutMoney(record, 188, 11, employee.Box1WagesTipsOtherCompensation); PutMoney(record, 199, 11, employee.Box2FederalIncomeTaxWithheld); PutMoney(record, 210, 11, employee.Box3SocialSecurityWages); PutMoney(record, 221, 11, employee.Box4SocialSecurityTaxWithheld); PutMoney(record, 232, 11, employee.Box5MedicareWagesAndTips); PutMoney(record, 243, 11, employee.Box6MedicareTaxWithheld);
        Put(record, 486, 1, "0"); Put(record, 488, 1, "0"); Put(record, 489, 1, "0"); return new(record);
    }

    private static string BuildRt(IReadOnlyList<W2EmployeeData> employees)
    {
        var record = Blank(); Put(record, 1, 2, "RT"); PutNumeric(record, 3, 7, employees.Count); foreach (var start in TotalMoneyStarts) PutMoney(record, start, 15, 0);
        PutMoney(record, 10, 15, employees.Sum(item => item.Box1WagesTipsOtherCompensation)); PutMoney(record, 25, 15, employees.Sum(item => item.Box2FederalIncomeTaxWithheld)); PutMoney(record, 40, 15, employees.Sum(item => item.Box3SocialSecurityWages)); PutMoney(record, 55, 15, employees.Sum(item => item.Box4SocialSecurityTaxWithheld)); PutMoney(record, 70, 15, employees.Sum(item => item.Box5MedicareWagesAndTips)); PutMoney(record, 85, 15, employees.Sum(item => item.Box6MedicareTaxWithheld)); return new(record);
    }

    private static string BuildRf(int count) { var record = Blank(); Put(record, 1, 2, "RF"); PutNumeric(record, 8, 9, count); return new(record); }
    private static bool TotalsMatch(W2PackageData p, IReadOnlyList<W2EmployeeData> e) => MoneyEqual(p.W3Box1Total, e.Sum(x => x.Box1WagesTipsOtherCompensation)) && MoneyEqual(p.W3Box2Total, e.Sum(x => x.Box2FederalIncomeTaxWithheld)) && MoneyEqual(p.W3Box3Total, e.Sum(x => x.Box3SocialSecurityWages)) && MoneyEqual(p.W3Box4Total, e.Sum(x => x.Box4SocialSecurityTaxWithheld)) && MoneyEqual(p.W3Box5Total, e.Sum(x => x.Box5MedicareWagesAndTips)) && MoneyEqual(p.W3Box6Total, e.Sum(x => x.Box6MedicareTaxWithheld));
    private static bool MoneyEqual(decimal left, decimal right) => decimal.Round(left, 2, MidpointRounding.AwayFromZero) == decimal.Round(right, 2, MidpointRounding.AwayFromZero);
    private static decimal[] MoneyValues(W2EmployeeData x) => [x.Box1WagesTipsOtherCompensation, x.Box2FederalIncomeTaxWithheld, x.Box3SocialSecurityWages, x.Box4SocialSecurityTaxWithheld, x.Box5MedicareWagesAndTips, x.Box6MedicareTaxWithheld];
    private static char[] Blank() => Enumerable.Repeat(' ', 512).ToArray();
    private static void Put(char[] record, int start, int length, string? value, bool upper = true) { value = (value ?? string.Empty).Trim(); if (upper) value = value.ToUpperInvariant(); if (value.Any(character => character > 127)) throw new ArgumentException($"EFW2 field at position {start} contains a non-ASCII character."); if (value.Length > length) throw new ArgumentException($"EFW2 field at position {start} exceeds its {length}-character limit."); value.CopyTo(0, record, start - 1, value.Length); }
    private static void PutZip(char[] record, int start, string value) { var digits = Digits(value); Put(record, start, 5, digits[..Math.Min(5, digits.Length)]); if (digits.Length == 9) Put(record, start + 5, 4, digits[5..]); }
    private static void PutNumeric(char[] record, int start, int length, long value) { var text = value.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0'); if (text.Length > length) throw new ArgumentException($"Numeric EFW2 field at position {start} exceeds its {length}-digit limit."); text.CopyTo(0, record, start - 1, length); }
    private static void PutMoney(char[] record, int start, int length, decimal value) => PutNumeric(record, start, length, checked((long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero)));
    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
}
