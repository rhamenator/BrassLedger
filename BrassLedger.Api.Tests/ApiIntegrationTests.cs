using System.Net;
using System.Net.Http.Json;
using BrassLedger.Application.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Api.Tests;

public sealed class ApiIntegrationTests : IClassFixture<BrassLedgerApiFactory>
{
    private readonly BrassLedgerApiFactory _factory;

    public ApiIntegrationTests(BrassLedgerApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDashboard_RejectsAnonymousRequests()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_ReturnsSeededFinancialSnapshot()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await response.Content.ReadFromJsonAsync<DashboardSnapshot>();
        Assert.NotNull(dashboard);
        Assert.Equal(112540.32m, dashboard.CashOnHand);
        Assert.Equal(34715.75m, dashboard.ReceivablesOpen);
        Assert.Equal(31844.77m, dashboard.PayablesOpen);
        Assert.Equal(14, dashboard.EnabledModules);
    }

    [Fact]
    public async Task GetWorkspace_ReturnsModulesAndReportingCatalog()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");

        Assert.NotNull(workspace);
        Assert.Equal("Brass Ledger Manufacturing", workspace.Company.Name);
        Assert.Contains(workspace.Modules, module => module.Code == "J" && module.Status == "Live foundation");
        Assert.Contains(workspace.Reporting.Reports, report => report.Code == "RDL-GL-TRIAL");
        Assert.Contains(workspace.Taxes.Profiles, profile => profile.Jurisdiction == "Federal" && profile.TaxType == "FUTA");
    }

    [Fact]
    public async Task ApiLogin_LocksOperatorAfterRepeatedFailures()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        for (var attempt = 0; attempt < BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts - 1; attempt++)
        {
            var failedResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                UserName = "controller",
                Password = "wrong-password"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, failedResponse.StatusCode);
        }

        var lockedResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Locked, lockedResponse.StatusCode);
    }

    [Fact]
    public async Task ExistingSession_IsRejectedAfterSecurityStampChanges()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);

        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var user = await dbContext.Users.SingleAsync(x => x.UserName == "controller");
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TrialBalanceReport_ReturnsCsvForReportingUser()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/reports/trial-balance.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("Account,Type,Balance", csv);
        Assert.Contains("1000", csv);
    }

    [Fact]
    public async Task CreateInvoice_PostsAndUpdatesReceivablesWorkspace()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var customer = before!.Receivables.Customers.First();

        var response = await client.PostAsJsonAsync("/api/invoices", new CreateInvoiceRequest(
            customer.Id,
            "INV-API-TEST-1",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            125m,
            0m,
            "4000",
            "API workflow test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(after);
        Assert.Equal(before.Receivables.OpenBalance + 125m, after!.Receivables.OpenBalance);
        Assert.Contains(after.Receivables.Invoices, invoice => invoice.InvoiceNumber == "INV-API-TEST-1" && invoice.BalanceDue == 125m);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var posting = await dbContext.JournalEntries.SingleAsync(entry => entry.Reference == "INV-API-TEST-1");
        Assert.NotNull(posting.PostedByUserId);
        Assert.True(posting.PostedAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CustomerPaymentApi_AppliesMultipleInvoices_PreservesDeposit_AndReturnsPayment()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var customer = before!.Receivables.Customers.First();
        var bank = before.Treasury.BankAccounts.First();

        async Task<Guid> CreateInvoiceAsync(string number, decimal amount)
        {
            var response = await client.PostAsJsonAsync("/api/invoices", new CreateInvoiceRequest(
                customer.Id, number, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), amount, 0m, "4000", "Payment API workflow"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<TransactionResult>();
            Assert.NotNull(result?.Id);
            return result!.Id!.Value;
        }

        var firstInvoiceId = await CreateInvoiceAsync("INV-API-PAY-1", 40m);
        var secondInvoiceId = await CreateInvoiceAsync("INV-API-PAY-2", 35m);
        var paymentResponse = await client.PostAsJsonAsync("/api/customer-payments", new RecordCustomerPaymentRequest(
            customer.Id, bank.Id, new DateOnly(2026, 5, 2), 90m, "DEP-API-PAY-1", "ACH",
            [new PaymentDocumentApplicationRequest(firstInvoiceId, 40m), new PaymentDocumentApplicationRequest(secondInvoiceId, 35m)]));
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var paymentResult = await paymentResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(paymentResult?.Id);

        var paid = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(paid);
        var recorded = Assert.Single(paid!.Receivables.Payments!, payment => payment.Id == paymentResult!.Id);
        Assert.Equal(75m, recorded.AppliedAmount);
        Assert.Equal(15m, recorded.UnappliedAmount);
        Assert.Equal(2, recorded.Applications.Count);

        var returnResponse = await client.PostAsJsonAsync("/api/subledger-payments/reverse", new ReverseSubledgerPaymentRequest(
            paymentResult!.Id!.Value, new DateOnly(2026, 5, 3), "Bank returned the ACH", "Returned"));
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        var returned = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(returned);
        Assert.Equal("Returned", returned!.Receivables.Payments!.Single(payment => payment.Id == paymentResult.Id).Status);
        Assert.Equal(40m, returned.Receivables.Invoices.Single(invoice => invoice.Id == firstInvoiceId).BalanceDue);
        Assert.Equal(35m, returned.Receivables.Invoices.Single(invoice => invoice.Id == secondInvoiceId).BalanceDue);
    }

    [Fact]
    public async Task BankingApi_ImportsStatementsAndReversesTransfersAndAdjustments()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var fromBank = before!.Treasury.BankAccounts.First();
        var toBank = before.Treasury.BankAccounts.Last();

        var importResponse = await client.PostAsJsonAsync("/api/bank-statements/import", new ImportBankStatementRequest(
            fromBank.Id, "api-statement.csv", "CSV", "ExternalId,Date,Amount,Payee\nAPI-BANK-1,2026-05-01,15.00,Customer"));
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var imported = await importResponse.Content.ReadFromJsonAsync<BankStatementImportResult>();
        Assert.Equal(1, imported?.ImportedCount);

        var transferResponse = await client.PostAsJsonAsync("/api/bank-transfers", new CreateBankTransferRequest(
            fromBank.Id, toBank.Id, new DateOnly(2026, 5, 2), 25m, "TR-API-BANK-1", "API transfer"));
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);
        var transfer = await transferResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(transfer?.Id);
        var reverseTransferResponse = await client.PostAsJsonAsync("/api/bank-transfers/reverse", new ReverseBankTransferRequest(
            transfer!.Id!.Value, new DateOnly(2026, 5, 3), "API correction"));
        Assert.Equal(HttpStatusCode.OK, reverseTransferResponse.StatusCode);

        var offsetAccount = before.GeneralLedger.Accounts.First(account => account.Type == "Expense" && !account.IsControlAccount).Number;
        var adjustmentResponse = await client.PostAsJsonAsync("/api/bank-reconciliation-adjustments", new CreateReconciliationAdjustmentRequest(
            fromBank.Id, new DateOnly(2026, 5, 4), 5m, offsetAccount, "ADJ-API-BANK-1", "API bank interest"));
        Assert.Equal(HttpStatusCode.Created, adjustmentResponse.StatusCode);
        var adjustment = await adjustmentResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(adjustment?.Id);
        var reverseAdjustmentResponse = await client.PostAsJsonAsync("/api/bank-reconciliation-adjustments/reverse", new ReverseReconciliationAdjustmentRequest(
            adjustment!.Id!.Value, new DateOnly(2026, 5, 5), "API correction"));
        Assert.Equal(HttpStatusCode.OK, reverseAdjustmentResponse.StatusCode);

        var after = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(after);
        Assert.Equal(before.Treasury.BankAccounts.Single(bank => bank.Id == fromBank.Id).CurrentBalance, after!.Treasury.BankAccounts.Single(bank => bank.Id == fromBank.Id).CurrentBalance);
        Assert.Equal(before.Treasury.BankAccounts.Single(bank => bank.Id == toBank.Id).CurrentBalance, after.Treasury.BankAccounts.Single(bank => bank.Id == toBank.Id).CurrentBalance);
        Assert.Equal("Reversed", after.Treasury.Transfers!.Single(item => item.Id == transfer.Id).Status);
        Assert.Equal("Reversed", after.Treasury.Adjustments!.Single(item => item.Id == adjustment.Id).Status);
    }

    [Fact]
    public async Task JournalDraftApi_RequiresApprovalBeforePostingAndPreservesReversalLinks()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);

        var draftResponse = await client.PostAsJsonAsync("/api/journal-entry-drafts", new SaveJournalEntryDraftRequest(
            null,
            new DateOnly(2026, 5, 4),
            "JE-API-LIFECYCLE-1",
            "API journal lifecycle",
            [new JournalLineRequest("1000", 40m, 0m, "Cash"), new JournalLineRequest("4000", 0m, 40m, "Revenue")]));
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = await draftResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(draft?.Id);

        var prematurePost = await client.PostAsync($"/api/journal-entry-drafts/{draft!.Id}/post", null);
        Assert.Equal(HttpStatusCode.BadRequest, prematurePost.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/journal-entry-drafts/{draft.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/journal-entry-drafts/{draft.Id}/post", null)).StatusCode);

        var afterPosting = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(afterPosting);
        Assert.Equal(before!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance + 40m, afterPosting!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);

        var reversalResponse = await client.PostAsJsonAsync("/api/journal-entries/reverse", new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 5), "API correction"));
        Assert.Equal(HttpStatusCode.Created, reversalResponse.StatusCode);
        var afterReversal = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(afterReversal);
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance, afterReversal!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);
        Assert.Contains(afterReversal.GeneralLedger.RecentEntries, entry => entry.Id == draft.Id && entry.Status == "Reversed" && entry.ReversedByJournalEntryId.HasValue);
        Assert.Contains(afterReversal.GeneralLedger.RecentEntries, entry => entry.ReversalOfJournalEntryId == draft.Id);
    }

    [Fact]
    public async Task QuickBooksOnlineInterchange_ExportsAndImportsCoreLists()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);

        var export = await client.GetAsync("/api/interchange/quickbooks-online/chart-of-accounts.csv");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportedCsv = await export.Content.ReadAsStringAsync();
        Assert.Contains("\"Name\",\"Type\",\"Detail Type\",\"Number\"", exportedCsv);
        Assert.Contains("\"Accounts Receivable\",\"Accounts Receivable\",\"Accounts Receivable\",\"1100\"", exportedCsv);
        Assert.Contains("\"Sales Tax Payable\",\"Other Current Liability\",\"Sales tax payable\",\"2100\"", exportedCsv);

        var token = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/antiforgery/token");
        Assert.NotNull(token);
        using var form = new MultipartFormDataContent();
        form.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        form.Add(new StringContent("Display Name,Company Name,Email,Customer Number\r\n\"QuickBooks\nImport Co\",QuickBooks Import Co,import@example.test,QBO-IMPORT-1"), "file", "quickbooks-customers.csv");
        var import = await client.PostAsync("/api/interchange/quickbooks-online/customers", form);
        Assert.True(import.StatusCode == HttpStatusCode.OK, await import.Content.ReadAsStringAsync());
        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(workspace);
        Assert.Contains(workspace!.Receivables.Customers, customer => customer.CustomerNumber == "QBO-IMPORT-1" && customer.Name == "QuickBooks\nImport Co");

        using var journalForm = new MultipartFormDataContent();
        journalForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        journalForm.Add(new StringContent("Journal Number,Journal Date,Reference,Description,Account Number,Debit,Credit,Line Description\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,1000,25.00,0.00,Cash\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,4000,0.00,25.00,Revenue"), "file", "quickbooks-journals.csv");
        var journalImport = await client.PostAsync("/api/interchange/quickbooks-online/journal-entries", journalForm);
        Assert.True(journalImport.StatusCode == HttpStatusCode.OK, await journalImport.Content.ReadAsStringAsync());
        using var duplicateJournalForm = new MultipartFormDataContent();
        duplicateJournalForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        duplicateJournalForm.Add(new StringContent("Journal Number,Journal Date,Reference,Description,Account Number,Debit,Credit,Line Description\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,1000,25.00,0.00,Cash\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,4000,0.00,25.00,Revenue"), "file", "quickbooks-journals-retry.csv");
        var duplicateJournalImport = await client.PostAsync("/api/interchange/quickbooks-online/journal-entries", duplicateJournalForm);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateJournalImport.StatusCode);
        var journalExport = await client.GetStringAsync("/api/interchange/quickbooks-online/journal-entries.csv");
        Assert.Contains("QBO-JE-1", journalExport);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(WebApplicationFactory<Program>? factory = null)
    {
        var testFactory = factory ?? _factory;
        var client = testFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}

public sealed class BrassLedgerApiFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.Api.Tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRootPath);

        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, _contentRootPath);
    }

    public new void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(_contentRootPath))
        {
            try
            {
                Directory.Delete(_contentRootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
