namespace BrassLedger.Application.Accounting;

public sealed record PayrollStatementEarning(int Sequence, string Code, string Type, decimal Hours, decimal Rate, decimal Amount, DateOnly? WorkedOn, string WorkState, string WorkCounty, string WorkCity, string WorkSchoolDistrict);
public sealed record PayrollStatementDeduction(int Sequence, string Code, string Type, decimal EmployeeAmount, decimal EmployerAmount, bool IsPreTax);
public sealed record PayrollStatementTax(int Sequence, string ObligationCode, string JurisdictionCode, string JurisdictionName, string TaxType, decimal TaxableWages, decimal EmployeeAmount, decimal EmployerAmount);

public sealed record PayrollPayStatement(
    Guid PayrollRunId, Guid EmployeeId, string CompanyName, string CompanyLegalName, string PayrollReference,
    DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly PayDate, string EmployeeNumber, string EmployeeName,
    string PaymentMethod, string PaymentReference, string MaskedDestination, string PaymentStatus,
    decimal GrossPay, decimal TaxableWages, decimal PreTaxDeductions, decimal EmployeeWithholdings,
    decimal PostTaxDeductions, decimal EmployerPayrollTaxes, decimal EmployerBenefitContributions,
    decimal NetPay, decimal YearToDateGross, decimal YearToDateEmployeeTaxes,
    decimal YearToDateEmployeeDeductions, decimal YearToDateNetPay,
    IReadOnlyList<PayrollStatementEarning> Earnings, IReadOnlyList<PayrollStatementDeduction> Deductions,
    IReadOnlyList<PayrollStatementTax> Taxes);

public sealed record PayrollRegisterEmployee(
    Guid EmployeeId, string EmployeeNumber, string EmployeeName, string PaymentMethod, string PaymentStatus,
    decimal GrossPay, decimal PreTaxDeductions, decimal EmployeeWithholdings, decimal PostTaxDeductions,
    decimal EmployerPayrollTaxes, decimal EmployerBenefitContributions, decimal NetPay);

public sealed record PayrollRegister(
    Guid PayrollRunId, string CompanyName, string PayrollReference, DateOnly PeriodStart, DateOnly PeriodEnd,
    DateOnly PayDate, string Status, decimal GrossPayroll, decimal PreTaxDeductions,
    decimal EmployeeWithholdings, decimal PostTaxDeductions, decimal EmployerPayrollTaxes,
    decimal EmployerBenefitContributions, decimal NetPay, IReadOnlyList<PayrollRegisterEmployee> Employees);

public interface IPayrollReportingService
{
    Task<PayrollRegister?> GetRegisterAsync(Guid payrollRunId, CancellationToken cancellationToken = default);
    Task<PayrollPayStatement?> GetPayStatementAsync(Guid payrollRunId, Guid employeeId, CancellationToken cancellationToken = default);
    Task<string?> ExportRegisterCsvAsync(Guid payrollRunId, CancellationToken cancellationToken = default);
}
