using BrassLedger.Application.Accounting;
using BrassLedger.Application.Catalog;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BrassLedger.Infrastructure.Accounting;

public sealed class BusinessWorkspaceService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IProductCatalogService assessmentService,
    IHttpContextAccessor httpContextAccessor) : IBusinessWorkspaceService
{
    public async Task<BusinessWorkspaceSnapshot> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var httpContext = httpContextAccessor.HttpContext;
        var canViewPayrollSensitiveData = httpContext is null || httpContext.User.HasClaim(
            BrassLedgerAuthenticationDefaults.PermissionClaimType,
            BrassLedgerPermissions.PayrollSensitiveData);
        var payrollPermissions = new[] { BrassLedgerPermissions.PayrollManage, BrassLedgerPermissions.PayrollPrepare, BrassLedgerPermissions.PayrollApprove, BrassLedgerPermissions.PayrollPost, BrassLedgerPermissions.PayrollReverse, BrassLedgerPermissions.PayrollSensitiveData };
        var canAccessPayroll = httpContext is null || payrollPermissions.Any(permission => httpContext.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission));
        var claimValue = httpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (httpContext is not null && !Guid.TryParse(claimValue, out _)) throw new UnauthorizedAccessException("An authenticated company context is required.");
        var companies = dbContext.Companies.AsNoTracking();
        var company = Guid.TryParse(claimValue, out var companyId)
            ? await companies.SingleAsync(x => x.Id == companyId, cancellationToken)
            : await companies.OrderBy(x => x.Name).FirstAsync(cancellationToken);
        var users = await dbContext.Users.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).ToListAsync(cancellationToken);
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).OrderBy(x => x.Number).ToListAsync(cancellationToken);
        var journalEntries = await dbContext.JournalEntries.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.PostedOn).ThenByDescending(x => x.EntryNumber).Take(20).ToListAsync(cancellationToken);
        var customers = await dbContext.Customers.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.CustomerNumber).ToListAsync(cancellationToken);
        var invoices = await dbContext.SalesInvoices.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.InvoiceDate).ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(invoice => invoice.Id).ToArray();
        var invoiceLines = invoiceIds.Length == 0 ? [] : await dbContext.SalesInvoiceLines.AsNoTracking().Where(line => invoiceIds.Contains(line.SalesInvoiceId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var vendors = await dbContext.Vendors.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.VendorNumber).ToListAsync(cancellationToken);
        var vendorBills = await dbContext.VendorBills.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.DueDate).ToListAsync(cancellationToken);
        var vendorBillIds = vendorBills.Select(bill => bill.Id).ToArray();
        var vendorBillLines = vendorBillIds.Length == 0 ? [] : await dbContext.VendorBillLines.AsNoTracking().Where(line => vendorBillIds.Contains(line.VendorBillId)).OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var subledgerPayments = (await dbContext.SubledgerPayments.AsNoTracking()
                .Where(payment => payment.CompanyId == company.Id)
                .ToListAsync(cancellationToken))
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.CreatedAtUtc)
            .ToList();
        var paymentIds = subledgerPayments.Select(payment => payment.Id).ToArray();
        var paymentApplications = paymentIds.Length == 0 ? [] : await dbContext.SubledgerPaymentApplications.AsNoTracking().Where(application => paymentIds.Contains(application.SubledgerPaymentId)).ToListAsync(cancellationToken);
        var subledgerAdjustments = await dbContext.SubledgerAdjustments.AsNoTracking().Where(adjustment => adjustment.CompanyId == company.Id).OrderByDescending(adjustment => adjustment.AdjustmentDate).ToListAsync(cancellationToken);
        var subledgerWorkflows = (await dbContext.SubledgerDocumentWorkflows.AsNoTracking().Where(workflow => workflow.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(workflow => workflow.CreatedAtUtc).ToList();
        var inventoryItems = await dbContext.InventoryItems.AsNoTracking().Where(x => x.CompanyId == company.Id && x.IsActive).OrderBy(x => x.Sku).ToListAsync(cancellationToken);
        var salesOrders = await dbContext.SalesOrders.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.OrderedOn).ToListAsync(cancellationToken);
        var purchaseOrders = await dbContext.PurchaseOrders.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderByDescending(x => x.OrderedOn).ToListAsync(cancellationToken);
        var bankAccounts = await dbContext.BankAccounts.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var statementTransactions = await dbContext.BankStatementTransactions.AsNoTracking().Where(item => item.CompanyId == company.Id).OrderByDescending(item => item.TransactionDate).ToListAsync(cancellationToken);
        var importBatches = (await dbContext.BankStatementImportBatches.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(item => item.ImportedAtUtc).ToList();
        var reconciliations = (await dbContext.BankReconciliations.AsNoTracking().Where(item => item.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(item => item.StatementDate).ToList();
        var reconciliationIds = reconciliations.Select(item => item.Id).ToArray();
        var reconciliationItems = reconciliationIds.Length == 0 ? [] : await dbContext.BankReconciliationItems.AsNoTracking().Where(item => reconciliationIds.Contains(item.BankReconciliationId)).ToListAsync(cancellationToken);
        var bankTransfers = await dbContext.BankTransfers.AsNoTracking().Where(item => item.CompanyId == company.Id).OrderByDescending(item => item.TransferDate).ToListAsync(cancellationToken);
        var bankEntryCandidates = await dbContext.JournalEntries.AsNoTracking()
            .Where(entry => entry.CompanyId == company.Id && entry.BankAccountId != null && entry.IsPosted && entry.Status == "Posted")
            .OrderBy(entry => entry.PostedOn)
            .ToListAsync(cancellationToken);
        var bankEntryIds = bankEntryCandidates.Select(entry => entry.Id).ToArray();
        var bankEntryLines = bankEntryIds.Length == 0
            ? new List<JournalEntryLine>()
            : await dbContext.JournalEntryLines.AsNoTracking().Where(line => bankEntryIds.Contains(line.JournalEntryId)).ToListAsync(cancellationToken);
        var employees = await dbContext.Employees.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.EmployeeNumber).ToListAsync(cancellationToken);
        var payrollJurisdictionRules = await dbContext.PayrollJurisdictionRules.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.ResidenceJurisdiction).ThenBy(x => x.WorkJurisdiction).ToListAsync(cancellationToken);
        var payrollRuns = (await dbContext.PayrollRuns.AsNoTracking().Where(x => x.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(x => x.PayDate).ThenByDescending(x => x.PreparedAtUtc).ToList();
        var payrollTimecards = canAccessPayroll ? (await dbContext.PayrollTimecards.AsNoTracking().Where(x => x.CompanyId == company.Id).ToListAsync(cancellationToken)).OrderByDescending(x => x.PeriodEnd).ThenBy(x => x.EmployeeId).ToList() : [];
        var payrollTimecardIds = payrollTimecards.Select(timecard => timecard.Id).ToArray();
        var payrollTimeEntries = payrollTimecardIds.Length == 0 ? [] : await dbContext.PayrollTimeEntries.AsNoTracking().Where(entry => payrollTimecardIds.Contains(entry.PayrollTimecardId)).OrderBy(entry => entry.Sequence).ToListAsync(cancellationToken);
        var projectJobs = await dbContext.ProjectJobs.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.JobNumber).ToListAsync(cancellationToken);
        var taxProfiles = await dbContext.TaxProfiles.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Jurisdiction).ThenBy(x => x.TaxType).ToListAsync(cancellationToken);
        var reports = await dbContext.ReportCatalogItems.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        var labels = await dbContext.LabelTemplates.AsNoTracking().Where(x => x.CompanyId == company.Id).OrderBy(x => x.Name).ToListAsync(cancellationToken);

        var customerNames = customers.ToDictionary(x => x.Id, x => x.Name);
        var vendorNames = vendors.ToDictionary(x => x.Id, x => x.Name);
        var accountNumbersById = accounts.ToDictionary(account => account.Id, account => account.Number);
        var invoiceLineLookup = invoiceLines.ToLookup(line => line.SalesInvoiceId);
        var vendorBillLineLookup = vendorBillLines.ToLookup(line => line.VendorBillId);
        var paymentApplicationLookup = paymentApplications.ToLookup(application => application.SubledgerPaymentId);
        var payrollTimeEntryLookup = payrollTimeEntries.ToLookup(entry => entry.PayrollTimecardId);
        var employeeById = employees.ToDictionary(employee => employee.Id);
        var invoiceNumbersById = invoices.ToDictionary(invoice => invoice.Id, invoice => invoice.InvoiceNumber);
        var billNumbersById = vendorBills.ToDictionary(bill => bill.Id, bill => bill.BillNumber);

        var moduleCounts = BuildModuleCounts(
            accounts.Count + journalEntries.Count,
            customers.Count + invoices.Count,
            vendors.Count + vendorBills.Count,
            employees.Count + taxProfiles.Count,
            inventoryItems.Count,
            salesOrders.Count,
            purchaseOrders.Count,
            bankAccounts.Count,
            projectJobs.Count);

        var assessment = assessmentService.GetCatalog();

        return new BusinessWorkspaceSnapshot(
            GeneratedAtUtc: DateTime.UtcNow,
            Company: new CompanySnapshot(
                Name: company.Name,
                LegalName: company.LegalName,
                TaxId: MaskTaxId(company.TaxId),
                BaseCurrency: company.BaseCurrency,
                FiscalYearStartMonth: company.FiscalYearStartMonth,
                ActiveUsers: users.Count),
            Dashboard: new DashboardSnapshot(
                CashOnHand: bankAccounts.Sum(x => x.CurrentBalance),
                ReceivablesOpen: invoices.Sum(x => x.BalanceDue),
                PayablesOpen: vendorBills.Sum(x => x.BalanceDue),
                MonthlyPayroll: employees.Where(x => x.IsActive).Sum(x => x.MonthlyBasePay),
                InventoryItems: inventoryItems.Count,
                OpenSalesOrders: salesOrders.Count(x => x.Status is "Open" or "Picking" or "Allocated"),
                OpenProjects: projectJobs.Count(x => x.Status is "Open" or "Billing"),
                EnabledModules: assessment.Modules.Count,
                ReportsReady: reports.Count + labels.Count),
            Modules: assessment.Modules
                .Select(module =>
                {
                    var details = moduleCounts.GetValueOrDefault(module.Code);
                    return new ModuleWorkspaceSnapshot(
                        module.Code,
                        module.Name,
                        module.Area,
                        string.IsNullOrWhiteSpace(details.Status) ? "Planned" : details.Status,
                        string.IsNullOrWhiteSpace(details.Summary) ? "Modeled in the open-source roadmap." : details.Summary,
                        details.RecordCount);
                })
                .ToArray(),
            GeneralLedger: new GeneralLedgerWorkspace(
                Assets: SumByType(accounts, AccountType.Asset),
                Liabilities: SumByType(accounts, AccountType.Liability),
                Equity: SumByType(accounts, AccountType.Equity),
                Revenue: SumByType(accounts, AccountType.Revenue),
                Expenses: SumByType(accounts, AccountType.Expense),
                Accounts: accounts.Select(x => new AccountSnapshot(x.Number, x.Name, x.Type.ToString(), x.CurrentBalance, x.IsControlAccount)).ToArray(),
                RecentEntries: journalEntries.Select(x => new JournalEntrySnapshot(x.EntryNumber, x.PostedOn, x.SourceModule, x.Description, x.TotalAmount, x.Id, x.Reference, x.Status, x.ReversalOfJournalEntryId, x.ReversedByJournalEntryId)).ToArray()),
            Receivables: new ReceivablesWorkspace(
                OpenBalance: invoices.Sum(x => x.BalanceDue),
                PastDueCount: invoices.Count(x => x.DueDate < DateOnly.FromDateTime(DateTime.Today) && x.BalanceDue > 0m),
                Customers: customers.Select(x => new CustomerSnapshot(x.CustomerNumber, x.Name, x.State, x.CreditLimit, x.OpenBalance, x.Id)).ToArray(),
                Invoices: invoices.Select(x => new InvoiceSnapshot(
                    x.InvoiceNumber,
                    customerNames.GetValueOrDefault(x.CustomerId, "Unknown customer"),
                    x.InvoiceDate,
                    x.DueDate,
                    x.Status,
                    x.TotalAmount,
                    x.BalanceDue,
                    x.Id,
                    invoiceLineLookup[x.Id].Select(line => new InvoiceLineSnapshot(line.Sequence, line.Description, line.Quantity, line.UnitPrice, line.DiscountAmount, line.TaxAmount, line.LineTotal, accountNumbersById.GetValueOrDefault(line.RevenueAccountId, "Unavailable"))).ToArray(),
                    x.CustomerId)).ToArray(),
                Payments: subledgerPayments.Where(payment => payment.Direction == "CustomerReceipt").Select(payment => new SubledgerPaymentSnapshot(payment.Id, payment.Direction, customerNames.GetValueOrDefault(payment.CounterpartyId, "Unknown customer"), payment.PaymentDate, payment.Amount, payment.AppliedAmount, payment.UnappliedAmount, payment.Reference, payment.Method, payment.Status, paymentApplicationLookup[payment.Id].Select(application => new PaymentApplicationSnapshot(application.DocumentId, invoiceNumbersById.GetValueOrDefault(application.DocumentId, "Unavailable"), application.Amount)).ToArray())).ToArray(),
                Adjustments: subledgerAdjustments.Where(adjustment => adjustment.Subledger == "Receivables").Select(adjustment => new SubledgerAdjustmentSnapshot(adjustment.Id, adjustment.Subledger, adjustment.Kind, adjustment.CounterpartyId, customerNames.GetValueOrDefault(adjustment.CounterpartyId, "Unknown customer"), adjustment.DocumentId, adjustment.DocumentId.HasValue ? invoiceNumbersById.GetValueOrDefault(adjustment.DocumentId.Value, "Unavailable") : string.Empty, adjustment.PaymentId, adjustment.AdjustmentDate, adjustment.Amount, adjustment.Reference, adjustment.Reason, adjustment.OffsetAccountNumber, adjustment.Status, adjustment.JournalEntryId, adjustment.ReversalJournalEntryId)).ToArray(),
                Workflows: subledgerWorkflows.Where(workflow => workflow.DocumentType == "Invoice").Select(ToWorkflowSnapshot).ToArray()),
            Payables: new PayablesWorkspace(
                OpenBalance: vendorBills.Sum(x => x.BalanceDue),
                DueThisWeekCount: vendorBills.Count(x => x.DueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(7)) && x.BalanceDue > 0m),
                Vendors: vendors.Select(x => new VendorSnapshot(x.VendorNumber, x.Name, x.State, x.PaymentTerms, x.OpenBalance, x.Id)).ToArray(),
                Bills: vendorBills.Select(x => new BillSnapshot(
                    x.BillNumber,
                    vendorNames.GetValueOrDefault(x.VendorId, "Unknown vendor"),
                    x.BillDate,
                    x.DueDate,
                    x.Status,
                    x.TotalAmount,
                    x.BalanceDue,
                    x.Id,
                    vendorBillLineLookup[x.Id].Select(line => new BillLineSnapshot(line.Sequence, line.Description, line.Quantity, line.UnitCost, line.DiscountAmount, line.TaxAmount, line.LineTotal, accountNumbersById.GetValueOrDefault(line.ExpenseAccountId, "Unavailable"))).ToArray(),
                    x.VendorId)).ToArray(),
                Payments: subledgerPayments.Where(payment => payment.Direction == "VendorDisbursement").Select(payment => new SubledgerPaymentSnapshot(payment.Id, payment.Direction, vendorNames.GetValueOrDefault(payment.CounterpartyId, "Unknown vendor"), payment.PaymentDate, payment.Amount, payment.AppliedAmount, payment.UnappliedAmount, payment.Reference, payment.Method, payment.Status, paymentApplicationLookup[payment.Id].Select(application => new PaymentApplicationSnapshot(application.DocumentId, billNumbersById.GetValueOrDefault(application.DocumentId, "Unavailable"), application.Amount)).ToArray())).ToArray(),
                Adjustments: subledgerAdjustments.Where(adjustment => adjustment.Subledger == "Payables").Select(adjustment => new SubledgerAdjustmentSnapshot(adjustment.Id, adjustment.Subledger, adjustment.Kind, adjustment.CounterpartyId, vendorNames.GetValueOrDefault(adjustment.CounterpartyId, "Unknown vendor"), adjustment.DocumentId, adjustment.DocumentId.HasValue ? billNumbersById.GetValueOrDefault(adjustment.DocumentId.Value, "Unavailable") : string.Empty, adjustment.PaymentId, adjustment.AdjustmentDate, adjustment.Amount, adjustment.Reference, adjustment.Reason, adjustment.OffsetAccountNumber, adjustment.Status, adjustment.JournalEntryId, adjustment.ReversalJournalEntryId)).ToArray(),
                Workflows: subledgerWorkflows.Where(workflow => workflow.DocumentType == "VendorBill").Select(ToWorkflowSnapshot).ToArray()),
            Operations: new OperationsWorkspace(
                InventoryItemCount: inventoryItems.Count,
                ReorderAlerts: inventoryItems.Count(x => x.QuantityOnHand <= x.ReorderPoint),
                OpenSalesOrderCount: salesOrders.Count(x => x.Status is "Open" or "Picking" or "Allocated"),
                OpenPurchaseOrderCount: purchaseOrders.Count(x => x.Status is "Issued" or "Approved"),
                InventoryItems: inventoryItems.Select(x => new InventoryItemSnapshot(x.Sku, x.Description, x.UnitPrice, x.QuantityOnHand, x.ReorderPoint, x.Id)).ToArray(),
                SalesOrders: salesOrders.Select(x => new SalesOrderSnapshot(
                    x.OrderNumber,
                    customerNames.GetValueOrDefault(x.CustomerId, "Unknown customer"),
                    x.OrderedOn,
                    x.Status,
                    x.TotalAmount)).ToArray(),
                PurchaseOrders: purchaseOrders.Select(x => new PurchaseOrderSnapshot(
                    x.OrderNumber,
                    vendorNames.GetValueOrDefault(x.VendorId, "Unknown vendor"),
                    x.OrderedOn,
                    x.Status,
                    x.TotalAmount)).ToArray()),
            Treasury: new TreasuryWorkspace(
                CashOnHand: bankAccounts.Sum(x => x.CurrentBalance),
                UnreconciledBalance: bankAccounts.Sum(x => x.UnreconciledAmount),
                BankAccounts: bankAccounts.Select(x => new BankAccountSnapshot(x.Name, x.AccountNumberMasked, x.CurrentBalance, x.UnreconciledAmount, x.LastReconciledOn, x.Id, accounts.FirstOrDefault(account => account.Id == x.LedgerAccountId)?.Number ?? "Unmapped", x.LastReconciledBalance)).ToArray(),
                ReconciliationCandidates: bankEntryCandidates
                    .Where(entry => entry.BankAccountId is not null)
                    .Where(entry => entry.PostedOn > bankAccounts.Single(bank => bank.Id == entry.BankAccountId!.Value).LastReconciledOn)
                    .Select(entry => new BankReconciliationCandidateSnapshot(
                        entry.BankAccountId!.Value,
                        entry.Id,
                        entry.PostedOn,
                        entry.Reference,
                        entry.Description,
                        entry.SourceModule,
                        bankEntryLines.Where(line => line.JournalEntryId == entry.Id && line.AccountId == bankAccounts.Single(bank => bank.Id == entry.BankAccountId!.Value).LedgerAccountId).Sum(line => line.Debit - line.Credit)))
                    .ToArray(),
                StatementTransactions: statementTransactions.Select(item => new BankStatementTransactionSnapshot(item.Id, item.BankAccountId, item.ImportBatchId, item.ExternalId, item.TransactionDate, item.Amount, item.TransactionType, item.Payee, item.Memo, item.Reference, item.Status, item.MatchedJournalEntryId, item.MatchNote)).ToArray(),
                ImportBatches: importBatches.Select(item => new BankStatementImportBatchSnapshot(item.Id, item.BankAccountId, item.FileName, item.Format, item.Status, item.ImportedCount, item.DuplicateCount, item.RejectedCount, item.DebitTotal, item.CreditTotal, item.ImportedAtUtc)).ToArray(),
                Reconciliations: reconciliations.Select(item => new BankReconciliationSnapshot(item.Id, item.BankAccountId, item.StatementDate, item.OpeningBalance, item.ClearedAmount, item.StatementClosingBalance, item.BookBalance, item.Variance, item.Status, item.Notes, item.ReconciledAtUtc, item.ReopenedAtUtc, item.ReopenReason, reconciliationItems.Count(reconciliationItem => reconciliationItem.BankReconciliationId == item.Id))).ToArray(),
                Transfers: bankTransfers.Select(item => new BankTransferSnapshot(item.Id, item.FromBankAccountId, item.ToBankAccountId, item.TransferDate, item.Amount, item.Reference, item.Memo, item.Status, item.JournalEntryId, item.InboundJournalEntryId, item.ReversalJournalEntryId, item.InboundReversalJournalEntryId, item.ReversalDate, item.ReversalReason)).ToArray(),
                Adjustments: subledgerAdjustments.Where(item => item.Subledger == "Banking").Select(item => new BankAdjustmentSnapshot(item.Id, item.BankAccountId!.Value, item.AdjustmentDate, item.Amount, item.Reference, item.Reason, item.OffsetAccountNumber, item.Status, item.JournalEntryId, item.ReversalJournalEntryId)).ToArray()),
            Payroll: new PayrollWorkspace(
                ActiveEmployees: employees.Count(x => x.IsActive),
                MonthlyGross: employees.Where(x => x.IsActive).Sum(x => x.MonthlyBasePay),
                Employees: employees.Select(x => new EmployeeSnapshot(
                    x.EmployeeNumber,
                    $"{x.FirstName} {x.LastName}",
                    x.Department,
                    x.State,
                    x.PayType,
                    x.MonthlyBasePay,
                    x.IsActive,
                    x.Id,
                    x.FilingStatus,
                    x.Allowances,
                    x.AdditionalWithholding,
                    x.PreTaxBenefitDeductions,
                    x.PostTaxBenefitDeductions,
                    string.IsNullOrWhiteSpace(x.ResidenceState) ? x.State : x.ResidenceState,
                    x.ResidenceCity,
                    x.WorkCity,
                    x.PayrollFrequency,
                    canViewPayrollSensitiveData ? x.ResidenceCounty : string.Empty,
                    canViewPayrollSensitiveData ? x.ResidenceSchoolDistrict : string.Empty,
                    canViewPayrollSensitiveData ? x.WorkCounty : string.Empty,
                    canViewPayrollSensitiveData ? x.WorkSchoolDistrict : string.Empty,
                    canViewPayrollSensitiveData ? x.EmploymentStartedOn : null,
                    canViewPayrollSensitiveData ? x.EmploymentEndedOn : null,
                    canViewPayrollSensitiveData ? x.HourlyRate : 0m,
                    canViewPayrollSensitiveData ? x.OvertimeRate : 0m,
                    canViewPayrollSensitiveData && x.DirectDepositEnabled,
                    canViewPayrollSensitiveData && !string.IsNullOrWhiteSpace(x.SocialSecurityNumber),
                    canViewPayrollSensitiveData && !string.IsNullOrWhiteSpace(x.BankAccountNumber),
                    x.ConcurrencyToken,
                    canViewPayrollSensitiveData ? x.AddressLine1 : string.Empty,
                    canViewPayrollSensitiveData ? x.AddressLine2 : string.Empty,
                    canViewPayrollSensitiveData ? x.PostalCode : string.Empty,
                    canViewPayrollSensitiveData ? x.FederalFormW4Year : 0,
                    canViewPayrollSensitiveData && x.FederalStep2MultipleJobs,
                    canViewPayrollSensitiveData ? x.FederalStep3Credits : 0m,
                    canViewPayrollSensitiveData ? x.FederalStep4OtherIncome : 0m,
                    canViewPayrollSensitiveData ? x.FederalStep4Deductions : 0m,
                    canViewPayrollSensitiveData && x.FederalWithholdingExempt)).ToArray(),
                JurisdictionRules: payrollJurisdictionRules.Select(rule => new PayrollJurisdictionRuleSnapshot(rule.Id, rule.ResidenceJurisdiction, rule.WorkJurisdiction, rule.ExemptWorkWithholding, rule.ResidentCreditRate, rule.IsActive, rule.Notes)).ToArray(),
                Runs: payrollRuns.Select(run => new PayrollRunSnapshot(run.Id, run.Reference, run.PeriodStart, run.PeriodEnd, run.PayDate, run.RunType, run.Status, run.GrossPayroll, run.EmployeeWithholdings, run.EmployerPayrollTaxes, run.NetPay, run.ConcurrencyToken, run.JournalEntryId, run.ReversalJournalEntryId, run.PreparedAtUtc, run.ApprovedAtUtc, run.PostedAtUtc, run.ReversedAtUtc, run.ReversalReason, run.CancelledAtUtc, run.CancellationReason)).ToArray(),
                Timecards: payrollTimecards.Select(timecard =>
                {
                    var employee = employeeById[timecard.EmployeeId];
                    var entries = payrollTimeEntryLookup[timecard.Id].Select(entry => new PayrollTimeEntrySnapshot(entry.Id, entry.Sequence, entry.WorkDate, entry.EarningCode, entry.EarningType, entry.Hours, entry.Rate, entry.Amount, entry.IsTaxable, entry.WorkState, entry.WorkCounty, entry.WorkCity, entry.WorkSchoolDistrict, entry.ProjectJobId, entry.Notes)).ToArray();
                    return new PayrollTimecardSnapshot(timecard.Id, employee.Id, employee.EmployeeNumber, $"{employee.FirstName} {employee.LastName}", timecard.PeriodStart, timecard.PeriodEnd, timecard.Status, entries.Sum(entry => entry.Hours), entries.Sum(entry => entry.Amount), timecard.Notes, timecard.ConcurrencyToken, timecard.PayrollRunId, timecard.PreparedAtUtc, timecard.SubmittedAtUtc, timecard.ApprovedAtUtc, timecard.VoidedAtUtc, timecard.VoidReason, entries);
                }).ToArray()),
            Projects: new ProjectsWorkspace(
                OpenJobs: projectJobs.Count(x => x.Status is "Open" or "Billing"),
                BudgetAmount: projectJobs.Sum(x => x.BudgetAmount),
                ActualCost: projectJobs.Sum(x => x.ActualCost),
                Jobs: projectJobs.Select(x => new ProjectJobSnapshot(x.JobNumber, x.Name, x.CustomerName, x.Status, x.BudgetAmount, x.ActualCost, x.Id)).ToArray()),
            Reporting: new ReportingWorkspace(
                ReportCount: reports.Count,
                LabelCount: labels.Count,
                PreferredDesigner: "Visual Studio RDL/RDLC",
                RenderingStrategy: "Use RDLC-authored operational reports plus server-side PDF exports for dashboards and special forms.",
                Reports: reports.Select(x => new ReportCatalogSnapshot(x.Code, x.Name, x.Category, x.LayoutType, x.Description, x.SupportsVisualStudioDesign)).ToArray(),
                Labels: labels.Select(x => new LabelTemplateSnapshot(x.Code, x.Name, x.StockType, x.Description)).ToArray()),
            Taxes: new TaxWorkspace(
                ProfileCount: taxProfiles.Count,
                EmployerSpecificCount: taxProfiles.Count(x => x.IsEmployerSpecific),
                Profiles: taxProfiles.Select(x => new TaxProfileSnapshot(x.Jurisdiction, x.TaxType, x.Rate, x.EffectiveOn, x.Source, x.IsEmployerSpecific, x.IsActive, x.IsVerified, x.VerificationNotes)).ToArray()));
    }

    private static Dictionary<string, (string Status, string Summary, int RecordCount)> BuildModuleCounts(
        int ledgerCount,
        int receivablesCount,
        int payablesCount,
        int payrollCount,
        int inventoryCount,
        int orderCount,
        int purchaseCount,
        int bankCount,
        int projectCount)
    {
        return new Dictionary<string, (string Status, string Summary, int RecordCount)>
        {
            ["J"] = ("Live foundation", "Chart of accounts, journal history, and balances are seeded in the shared workspace.", ledgerCount),
            ["F"] = ("Live foundation", "Customers and invoices are active with aging-ready open balances.", receivablesCount),
            ["E"] = ("Live foundation", "Vendor master data and open bills are wired for payables workflows.", payablesCount),
            ["Q"] = ("Live foundation", "Employees, payroll cost, and tax profiles are in the operational data model.", payrollCount),
            ["K"] = ("Live foundation", "Item master, reorder thresholds, and stock counts are available now.", inventoryCount),
            ["O"] = ("Live foundation", "Sales orders feed the operational pipeline and reporting surface.", orderCount),
            ["S"] = ("Live foundation", "Purchase orders are present and aligned to vendor workflows.", purchaseCount),
            ["P"] = ("Modeled foundation", "Point-of-sale can ride the order pipeline while dedicated ticket workflows are added.", orderCount),
            ["G"] = ("Live foundation", "Bank balances and reconciliation deltas are available in treasury.", bankCount),
            ["U"] = ("Modeled foundation", "Zero-balance behavior will build on the banking model rather than legacy switches.", bankCount),
            ["L"] = ("Live foundation", "Jobs carry budget, cost, and customer linkage for project accounting.", projectCount),
            ["B"] = ("Modeled foundation", "Property workflows can extend the job ledger without module gating.", projectCount),
            ["T"] = ("Modeled foundation", "Time capture will attach to employees and jobs in the same payroll model.", payrollCount),
            ["I"] = ("Live foundation", "CRM data is represented through the live customer workspace.", receivablesCount)
        };
    }

    private static decimal SumByType(IEnumerable<GeneralLedgerAccount> accounts, AccountType accountType)
    {
        return accounts.Where(x => x.Type == accountType).Sum(x => x.CurrentBalance);
    }

    private static SubledgerDocumentWorkflowSnapshot ToWorkflowSnapshot(SubledgerDocumentWorkflow workflow) => new(workflow.Id, workflow.DocumentType, workflow.DocumentNumber, workflow.Status, workflow.IsRecurringTemplate, workflow.Frequency, workflow.FrequencyInterval, workflow.NextOccurrenceDate, workflow.EndDate, workflow.SourceTemplateId, workflow.PostedDocumentId, workflow.CreatedAtUtc, workflow.ApprovedAtUtc, workflow.PostedAtUtc);

    private static string MaskTaxId(string taxId)
    {
        var digits = new string(taxId.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4)
        {
            return $"***-**-{digits[^4..]}";
        }

        return "***";
    }
}
