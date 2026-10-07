using System.Globalization;
using System.Security.Claims;
using System.Text;
using BrassLedger.Application.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class PayrollReportingService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : IPayrollReportingService
{
    public async Task<PayrollRegister?> GetRegisterAsync(Guid payrollRunId, CancellationToken cancellationToken = default)
    {
        RequirePayrollAccess(sensitive: false);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var run = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(item => item.Id == payrollRunId && item.CompanyId == companyId, cancellationToken);
        if (run is null) return null;
        var company = await db.Companies.AsNoTracking().SingleAsync(item => item.Id == companyId, cancellationToken);
        var lines = await db.PayrollRunEmployeeLines.AsNoTracking().Where(item => item.PayrollRunId == run.Id).OrderBy(item => item.EmployeeId).ToListAsync(cancellationToken);
        var payments = await db.PayrollEmployeePayments.AsNoTracking().Where(item => item.PayrollRunId == run.Id && item.CompanyId == companyId).ToDictionaryAsync(item => item.PayrollRunEmployeeLineId, cancellationToken);
        EnsureRunReconciles(run.GrossPayroll, run.PreTaxDeductions, run.EmployeeWithholdings, run.PostTaxDeductions, run.EmployerPayrollTaxes, run.EmployerBenefitContributions, run.NetPay, lines);
        if (run.Status is "Posted" or "Reversed" && payments.Count != lines.Count) throw new InvalidOperationException("Posted payroll payment records do not reconcile to the employee register.");

        var employees = lines.Select(line =>
        {
            payments.TryGetValue(line.Id, out var payment);
            return new PayrollRegisterEmployee(line.EmployeeId, payment?.EmployeeNumber ?? string.Empty,
                payment?.EmployeeName ?? "Payment pending", payment?.Method ?? "Pending", payment?.Status ?? "Pending",
                line.GrossPay, line.PreTaxDeductions, line.EmployeeWithholdings, line.PostTaxDeductions,
                line.EmployerPayrollTaxes, line.EmployerBenefitContributions, line.NetPay);
        }).ToArray();
        return new PayrollRegister(run.Id, company.Name, run.Reference, run.PeriodStart, run.PeriodEnd, run.PayDate,
            run.Status, run.GrossPayroll, run.PreTaxDeductions, run.EmployeeWithholdings, run.PostTaxDeductions,
            run.EmployerPayrollTaxes, run.EmployerBenefitContributions, run.NetPay, employees);
    }

    public async Task<PayrollPayStatement?> GetPayStatementAsync(Guid payrollRunId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        RequirePayrollAccess(sensitive: true);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var run = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(item => item.Id == payrollRunId && item.CompanyId == companyId, cancellationToken);
        if (run is null) return null;
        var company = await db.Companies.AsNoTracking().SingleAsync(item => item.Id == companyId, cancellationToken);
        var line = await db.PayrollRunEmployeeLines.AsNoTracking().SingleOrDefaultAsync(item => item.PayrollRunId == run.Id && item.EmployeeId == employeeId, cancellationToken);
        if (line is null) return null;
        var payment = await db.PayrollEmployeePayments.AsNoTracking().SingleOrDefaultAsync(item => item.PayrollRunEmployeeLineId == line.Id && item.CompanyId == companyId, cancellationToken);
        if (payment is null && run.Status is "Posted" or "Reversed") throw new InvalidOperationException("The posted payroll does not have an employee payment record.");
        var earnings = await db.PayrollEarningLines.AsNoTracking().Where(item => item.PayrollRunEmployeeLineId == line.Id).OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var deductions = await db.PayrollDeductionLines.AsNoTracking().Where(item => item.PayrollRunEmployeeLineId == line.Id).OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var taxes = await db.PayrollTaxLines.AsNoTracking().Where(item => item.PayrollRunEmployeeLineId == line.Id).OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        EnsureEmployeeReconciles(line.GrossPay, line.PreTaxDeductions, line.EmployeeWithholdings, line.PostTaxDeductions, line.EmployerPayrollTaxes, line.EmployerBenefitContributions, line.NetPay, earnings.Sum(item => item.Amount), deductions.Where(item => item.IsPreTax).Sum(item => item.EmployeeAmount), deductions.Where(item => !item.IsPreTax).Sum(item => item.EmployeeAmount), deductions.Sum(item => item.EmployerAmount), taxes.Sum(item => item.EmployeeAmount), taxes.Sum(item => item.EmployerAmount));
        if (payment is not null && payment.Amount != line.NetPay) throw new InvalidOperationException("The employee payment does not reconcile to net pay.");

        return new PayrollPayStatement(run.Id, line.EmployeeId, company.Name, company.LegalName, run.Reference,
            run.PeriodStart, run.PeriodEnd, run.PayDate, payment?.EmployeeNumber ?? string.Empty,
            payment?.EmployeeName ?? "Payment pending", payment?.Method ?? "Pending", payment?.Reference ?? string.Empty,
            payment is { Method: "DirectDeposit" } ? $"{payment.BankAccountType} ending {payment.DestinationLastFour}" : "Check",
            payment?.Status ?? "Pending", line.GrossPay, line.TaxableWages, line.PreTaxDeductions,
            line.EmployeeWithholdings, line.PostTaxDeductions, line.EmployerPayrollTaxes,
            line.EmployerBenefitContributions, line.NetPay, payment?.YearToDateGross ?? line.YearToDateGrossAfter,
            payment?.YearToDateEmployeeTaxes ?? line.EmployeeWithholdings,
            payment?.YearToDateEmployeeDeductions ?? line.PreTaxDeductions + line.PostTaxDeductions,
            payment?.YearToDateNetPay ?? line.NetPay,
            earnings.Select(item => new PayrollStatementEarning(item.Sequence, item.EarningCode, item.EarningType, item.Hours, item.Rate, item.Amount, item.WorkedOn, item.WorkState, item.WorkCounty, item.WorkCity, item.WorkSchoolDistrict)).ToArray(),
            deductions.Select(item => new PayrollStatementDeduction(item.Sequence, item.DeductionCode, item.DeductionType, item.EmployeeAmount, item.EmployerAmount, item.IsPreTax)).ToArray(),
            taxes.Select(item => new PayrollStatementTax(item.Sequence, item.ObligationCode, item.JurisdictionCode, item.JurisdictionName, item.TaxType, item.TaxableWages, item.EmployeeAmount, item.EmployerAmount)).ToArray());
    }

    public async Task<string?> ExportRegisterCsvAsync(Guid payrollRunId, CancellationToken cancellationToken = default)
    {
        var report = await GetRegisterAsync(payrollRunId, cancellationToken);
        if (report is null) return null;
        var output = new StringBuilder();
        output.AppendLine("Employee number,Employee name,Payment method,Payment status,Gross pay,Pre-tax deductions,Employee taxes,Post-tax deductions,Employer taxes,Employer benefits,Net pay");
        foreach (var item in report.Employees)
            output.AppendLine(string.Join(',', Csv(item.EmployeeNumber), Csv(item.EmployeeName), Csv(item.PaymentMethod), Csv(item.PaymentStatus), Money(item.GrossPay), Money(item.PreTaxDeductions), Money(item.EmployeeWithholdings), Money(item.PostTaxDeductions), Money(item.EmployerPayrollTaxes), Money(item.EmployerBenefitContributions), Money(item.NetPay)));
        output.AppendLine(string.Join(',', Csv("TOTAL"), Csv(string.Empty), Csv(string.Empty), Csv(report.Status), Money(report.GrossPayroll), Money(report.PreTaxDeductions), Money(report.EmployeeWithholdings), Money(report.PostTaxDeductions), Money(report.EmployerPayrollTaxes), Money(report.EmployerBenefitContributions), Money(report.NetPay)));
        return output.ToString();
    }

    private void RequirePayrollAccess(bool sensitive)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null) return;
        var required = sensitive ? BrassLedgerPermissions.PayrollSensitiveData : null;
        var allowed = required is not null
            ? context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, required)
            : new[] { BrassLedgerPermissions.PayrollManage, BrassLedgerPermissions.PayrollPrepare, BrassLedgerPermissions.PayrollApprove, BrassLedgerPermissions.PayrollPost, BrassLedgerPermissions.PayrollReverse, BrassLedgerPermissions.PayrollSensitiveData }.Any(permission => context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission));
        if (!allowed) throw new UnauthorizedAccessException("You are not authorized to view this payroll report.");
    }

    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext db, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        var claim = context?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (context is not null && !Guid.TryParse(claim, out _)) throw new UnauthorizedAccessException("An authenticated company context is required.");
        if (Guid.TryParse(claim, out var id) && await db.Companies.AnyAsync(item => item.Id == id, cancellationToken)) return id;
        return await db.Companies.OrderBy(item => item.Name).Select(item => item.Id).FirstAsync(cancellationToken);
    }

    private static void EnsureRunReconciles(decimal gross, decimal preTax, decimal taxes, decimal postTax, decimal employerTaxes, decimal employerBenefits, decimal net, IReadOnlyCollection<Domain.Accounting.PayrollRunEmployeeLine> lines)
    {
        if (gross != lines.Sum(item => item.GrossPay) || preTax != lines.Sum(item => item.PreTaxDeductions) || taxes != lines.Sum(item => item.EmployeeWithholdings) || postTax != lines.Sum(item => item.PostTaxDeductions) || employerTaxes != lines.Sum(item => item.EmployerPayrollTaxes) || employerBenefits != lines.Sum(item => item.EmployerBenefitContributions) || net != lines.Sum(item => item.NetPay))
            throw new InvalidOperationException("Payroll register details do not reconcile to the run totals.");
    }

    private static void EnsureEmployeeReconciles(decimal gross, decimal preTax, decimal withholdings, decimal postTax, decimal employerTaxes, decimal employerBenefits, decimal net, decimal earningTotal, decimal preTaxDetail, decimal postTaxDetail, decimal benefitDetail, decimal taxDetail, decimal employerTaxDetail)
    {
        if (gross != earningTotal || preTax != preTaxDetail || postTax != postTaxDetail || withholdings != taxDetail || employerTaxes != employerTaxDetail || employerBenefits != benefitDetail || net != gross - preTax - withholdings - postTax)
            throw new InvalidOperationException("Pay statement details do not reconcile to the employee payroll totals.");
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
