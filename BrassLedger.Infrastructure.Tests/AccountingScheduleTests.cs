using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Infrastructure.Tests;

public sealed class AccountingScheduleTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine("/home/rich/temp", "BrassLedger.AccountingSchedules.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FixedAssetSchedule_GeneratesBalancedDraftPostsAndReversesWithAudit()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var actor = await GetActorAsync(factory);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost, BrassLedgerPermissions.JournalReverse);
        var accounts = await AddScheduleAccountsAsync(factory, actor.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        var saved = await service.SaveAccountingScheduleAsync(new(null, "FA-001", "Delivery truck", "FixedAsset", new DateOnly(2026, 1, 31), 12, 1200m, 200m, 0m, accounts.FixedAsset, accounts.AccumulatedDepreciation, accounts.DepreciationExpense, null, "Straight-line book depreciation."));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.Equal(12, schedule.Installments.Count);
        Assert.Equal(1000m, schedule.Installments.Sum(installment => installment.PrincipalAmount));
        Assert.Equal(1000m, schedule.Installments.Sum(installment => installment.ExpenseAmount));
        Assert.Equal(new DateOnly(2026, 1, 31), schedule.Installments[0].DueOn);
        Assert.Equal(new DateOnly(2026, 2, 28), schedule.Installments[1].DueOn);

        var approved = await service.ApproveAccountingScheduleAsync(new(schedule.Id, schedule.ConcurrencyToken));
        Assert.True(approved.Succeeded, approved.ErrorMessage);
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        var prepared = await service.PrepareAccountingScheduleInstallmentsAsync(new(schedule.Id, new DateOnly(2026, 1, 31), schedule.ConcurrencyToken));
        Assert.True(prepared.Succeeded, prepared.ErrorMessage);
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        var installment = schedule.Installments[0];
        Assert.Equal("Draft", installment.JournalStatus);
        Assert.NotNull(installment.JournalEntryId);
        var posting = await ApproveAndPostJournalAsSeparateActorsAsync(scope.ServiceProvider, service, actor.CompanyId, installment.JournalEntryId.Value);
        Assert.True(posting.Succeeded, posting.ErrorMessage);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost, BrassLedgerPermissions.JournalReverse);

        await using (var posted = await factory.CreateDbContextAsync())
        {
            var lines = await posted.JournalEntryLines.Where(line => line.JournalEntryId == installment.JournalEntryId).ToArrayAsync();
            Assert.Contains(lines, line => line.AccountId == accounts.DepreciationExpense && line.Debit == 83.33m);
            Assert.Contains(lines, line => line.AccountId == accounts.AccumulatedDepreciation && line.Credit == 83.33m);
            Assert.Equal(83.33m, (await posted.Accounts.SingleAsync(account => account.Id == accounts.DepreciationExpense)).CurrentBalance);
            Assert.Equal(-83.33m, (await posted.Accounts.SingleAsync(account => account.Id == accounts.AccumulatedDepreciation)).CurrentBalance);
        }

        var installmentReconciliationId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BankReconciliations.Add(new BankReconciliation { Id = installmentReconciliationId, CompanyId = actor.CompanyId, BankAccountId = accounts.PaymentBank, StatementDate = new DateOnly(2026, 2, 1), Status = "Completed" });
            db.BankReconciliationItems.Add(new BankReconciliationItem { Id = Guid.NewGuid(), BankReconciliationId = installmentReconciliationId, JournalEntryId = installment.JournalEntryId!.Value });
            await db.SaveChangesAsync();
        }
        var reconciledInstallmentReversal = await service.ReverseAccountingScheduleInstallmentAsync(new(installment.Id, new DateOnly(2026, 2, 1), "Blocked by completed reconciliation."));
        Assert.False(reconciledInstallmentReversal.Succeeded);
        Assert.Contains("reconcil", reconciledInstallmentReversal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BankReconciliations.Remove(await db.BankReconciliations.SingleAsync(item => item.Id == installmentReconciliationId));
            await db.SaveChangesAsync();
        }

        var reversed = await service.ReverseAccountingScheduleInstallmentAsync(new(installment.Id, new DateOnly(2026, 2, 1), "Asset was not placed in service."));
        Assert.True(reversed.Succeeded, reversed.ErrorMessage);
        await using var verified = await factory.CreateDbContextAsync();
        Assert.Equal(0m, (await verified.Accounts.SingleAsync(account => account.Id == accounts.DepreciationExpense)).CurrentBalance);
        Assert.Equal(0m, (await verified.Accounts.SingleAsync(account => account.Id == accounts.AccumulatedDepreciation)).CurrentBalance);
        var reversalAudit = Assert.Single(await verified.BusinessAuditEntries.Where(audit => audit.Action == "accounting-schedule.installment.reversed" && audit.EntityId == schedule.Id).ToArrayAsync());
        Assert.Equal(actor.UserId, reversalAudit.UserId);
    }

    [Fact]
    public async Task LoanSchedule_AmortizesPrincipalAndEnforcesPermissionsAndCompanyIsolation()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var actor = await GetActorAsync(factory);
        var accounts = await AddScheduleAccountsAsync(factory, actor.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId);
        var denied = await service.SaveAccountingScheduleAsync(new(null, "LN-DENIED", "Denied", "Loan", new DateOnly(2026, 1, 1), 12, 1200m, 0m, 6m, null, accounts.LoanLiability, accounts.InterestExpense, accounts.PaymentBank, ""));
        Assert.False(denied.Succeeded);
        Assert.Contains("not authorized", denied.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare);
        var saved = await service.SaveAccountingScheduleAsync(new(null, "LN-001", "Equipment note", "Loan", new DateOnly(2026, 4, 30), 12, 1200m, 0m, 6m, null, accounts.LoanLiability, accounts.InterestExpense, accounts.PaymentBank, "Monthly amortizing note."));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.Equal(1200m, schedule.Installments.Sum(installment => installment.PrincipalAmount));
        Assert.True(schedule.Installments[0].ExpenseAmount > schedule.Installments[^1].ExpenseAmount);
        Assert.All(schedule.Installments, installment => Assert.Equal(installment.PrincipalAmount + installment.ExpenseAmount, installment.PaymentAmount));

        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost, BrassLedgerPermissions.JournalReverse);
        decimal startingBankBalance;
        await using (var db = await factory.CreateDbContextAsync()) startingBankBalance = (await db.BankAccounts.SingleAsync(bank => bank.Id == accounts.PaymentBank)).CurrentBalance;
        Assert.True((await service.ApproveAccountingScheduleAsync(new(schedule.Id, schedule.ConcurrencyToken))).Succeeded);
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.True((await service.PrepareAccountingScheduleInstallmentsAsync(new(schedule.Id, schedule.StartDate, schedule.ConcurrencyToken))).Succeeded);
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        var paymentInstallment = schedule.Installments[0];
        var paymentPosting = await ApproveAndPostJournalAsSeparateActorsAsync(scope.ServiceProvider, service, actor.CompanyId, paymentInstallment.JournalEntryId!.Value);
        Assert.True(paymentPosting.Succeeded, paymentPosting.ErrorMessage);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost, BrassLedgerPermissions.JournalReverse);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var journal = await db.JournalEntries.SingleAsync(entry => entry.Id == paymentInstallment.JournalEntryId);
            Assert.Equal(accounts.PaymentBank, journal.BankAccountId);
            Assert.Equal(startingBankBalance - paymentInstallment.PaymentAmount, (await db.BankAccounts.SingleAsync(bank => bank.Id == accounts.PaymentBank)).CurrentBalance);
        }
        var businessWorkspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        Assert.Contains(businessWorkspace.Treasury.ReconciliationCandidates ?? [], candidate => candidate.JournalEntryId == paymentInstallment.JournalEntryId && candidate.BankAccountId == accounts.PaymentBank);
        Assert.True((await service.ReverseAccountingScheduleInstallmentAsync(new(paymentInstallment.Id, paymentInstallment.DueOn.AddDays(1), "Correct loan payment."))).Succeeded);
        await using (var db = await factory.CreateDbContextAsync()) Assert.Equal(startingBankBalance, (await db.BankAccounts.SingleAsync(bank => bank.Id == accounts.PaymentBank)).CurrentBalance);

        var otherCompanyId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Companies.Add(new Company { Id = otherCompanyId, Name = "Isolated company", LegalName = "Isolated company", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
            await db.SaveChangesAsync();
        }
        SetContext(scope.ServiceProvider, actor.UserId, otherCompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove);
        Assert.Empty((await service.GetAccountingScheduleWorkspaceAsync()).Schedules);
        var crossCompanyApproval = await service.ApproveAccountingScheduleAsync(new(schedule.Id, schedule.ConcurrencyToken));
        Assert.False(crossCompanyApproval.Succeeded);
    }

    [Fact]
    public async Task PrepaidSchedule_AllocatesFinalRoundingAndRejectsStaleOrIncompatibleAccounts()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var actor = await GetActorAsync(factory);
        var accounts = await AddScheduleAccountsAsync(factory, actor.CompanyId);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare);
        var service = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();

        var saved = await service.SaveAccountingScheduleAsync(new(null, "PP-001", "Annual insurance", "Prepaid", new DateOnly(2026, 1, 31), 3, 100m, 0m, 0m, null, accounts.PrepaidAsset, accounts.PrepaidExpense, null, "Three-month test."));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.Equal([33.33m, 33.33m, 33.34m], schedule.Installments.Select(installment => installment.ExpenseAmount));

        var stale = await service.SaveAccountingScheduleAsync(new(schedule.Id, schedule.ScheduleNumber, schedule.Name, schedule.ScheduleType, schedule.StartDate, schedule.PeriodCount, schedule.OriginalAmount, 0m, 0m, null, schedule.BalanceAccountId, schedule.ExpenseAccountId, null, schedule.Notes, "stale-token"));
        Assert.False(stale.Succeeded);
        Assert.Contains("changed", stale.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        await using (var firstWriter = await factory.CreateDbContextAsync())
        await using (var secondWriter = await factory.CreateDbContextAsync())
        {
            var first = await firstWriter.AccountingSchedules.SingleAsync(candidate => candidate.Id == schedule.Id);
            var second = await secondWriter.AccountingSchedules.SingleAsync(candidate => candidate.Id == schedule.Id);
            first.ConcurrencyToken = Guid.NewGuid().ToString("N");
            second.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await firstWriter.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondWriter.SaveChangesAsync());
        }

        var invalid = await service.SaveAccountingScheduleAsync(new(null, "FA-BANK", "Invalid asset", "FixedAsset", new DateOnly(2026, 1, 31), 3, 100m, 10m, 0m, accounts.FixedAsset, accounts.AccumulatedDepreciation, accounts.DepreciationExpense, accounts.PaymentBank, ""));
        Assert.False(invalid.Succeeded);
        Assert.Contains("no payment bank", invalid.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FixedAssetDisposal_RecognizesGainUsesBankWorkflowAndReversesAuditably()
    {
        using var services = CreateServiceProvider();
        await services.InitializeBrassLedgerAsync();
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        var actor = await GetActorAsync(factory);
        var accounts = await AddScheduleAccountsAsync(factory, actor.CompanyId);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost, BrassLedgerPermissions.JournalReverse);
        var service = scope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
        var acquisitionDraft = await service.SaveJournalEntryDraftAsync(new(null, new DateOnly(2026, 4, 1), "FA-DISP-ACQ", "Record forklift acquisition", [new("1500", 1200m, 0m, "Forklift cost"), new("3000", 0m, 1200m, "Opening asset financing")]));
        Assert.True(acquisitionDraft.Succeeded, acquisitionDraft.ErrorMessage);
        var acquisition = await ApproveAndPostJournalAsSeparateActorsAsync(scope.ServiceProvider, service, actor.CompanyId, acquisitionDraft.Id!.Value);
        Assert.True(acquisition.Succeeded, acquisition.ErrorMessage);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost, BrassLedgerPermissions.JournalReverse);

        var saved = await service.SaveAccountingScheduleAsync(new(null, "FA-DISP", "Forklift", "FixedAsset", new DateOnly(2026, 4, 30), 12, 1200m, 0m, 0m, accounts.FixedAsset, accounts.AccumulatedDepreciation, accounts.DepreciationExpense, null, "Disposal lifecycle."));
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.True((await service.ApproveAccountingScheduleAsync(new(schedule.Id, schedule.ConcurrencyToken))).Succeeded);
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.True((await service.PrepareAccountingScheduleInstallmentsAsync(new(schedule.Id, schedule.StartDate, schedule.ConcurrencyToken))).Succeeded);
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        var depreciation = schedule.Installments[0];
        var depreciationPosting = await ApproveAndPostJournalAsSeparateActorsAsync(scope.ServiceProvider, service, actor.CompanyId, depreciation.JournalEntryId!.Value);
        Assert.True(depreciationPosting.Succeeded, depreciationPosting.ErrorMessage);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost, BrassLedgerPermissions.JournalReverse);

        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        decimal bankBalanceBeforeDisposal;
        await using (var db = await factory.CreateDbContextAsync()) bankBalanceBeforeDisposal = (await db.BankAccounts.SingleAsync(bank => bank.Id == accounts.PaymentBank)).CurrentBalance;
        var disposal = await service.PrepareFixedAssetDisposalAsync(new(schedule.Id, new DateOnly(2026, 5, 15), 1200m, accounts.PaymentBank, accounts.DisposalGain, accounts.DisposalLoss, "Sell forklift", schedule.ConcurrencyToken));
        Assert.True(disposal.Succeeded, disposal.ErrorMessage);
        var disposalId = disposal.Id!.Value;
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.Equal("DisposalPending", schedule.Status);
        Assert.Equal(disposalId, schedule.DisposalJournalEntryId);
        Assert.False((await service.PrepareAccountingScheduleInstallmentsAsync(new(schedule.Id, new DateOnly(2026, 6, 30), schedule.ConcurrencyToken))).Succeeded);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var entry = await db.JournalEntries.SingleAsync(candidate => candidate.Id == disposalId);
            var lines = await db.JournalEntryLines.Where(line => line.JournalEntryId == disposalId).ToArrayAsync();
            Assert.Equal(accounts.PaymentBank, entry.BankAccountId);
            Assert.Equal(1300m, lines.Sum(line => line.Debit));
            Assert.Equal(1300m, lines.Sum(line => line.Credit));
            Assert.Contains(lines, line => line.AccountId == accounts.AccumulatedDepreciation && line.Debit == 100m);
            Assert.Contains(lines, line => line.AccountId == accounts.FixedAsset && line.Credit == 1200m);
            Assert.Contains(lines, line => line.AccountId == accounts.DisposalGain && line.Credit == 100m);
        }
        var editAttempt = await service.SaveJournalEntryDraftAsync(new(disposalId, new DateOnly(2026, 5, 15), "ALTER", "Alter disposal", [new("1000", 1m, 0m, "Cash"), new("4400", 0m, 1m, "Gain")]));
        Assert.False(editAttempt.Succeeded);
        Assert.Contains("originating workflow", editAttempt.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var disposalPosting = await ApproveAndPostJournalAsSeparateActorsAsync(scope.ServiceProvider, service, actor.CompanyId, disposalId);
        Assert.True(disposalPosting.Succeeded, disposalPosting.ErrorMessage);
        SetContext(scope.ServiceProvider, actor.UserId, actor.CompanyId, BrassLedgerPermissions.JournalPrepare, BrassLedgerPermissions.JournalApprove, BrassLedgerPermissions.JournalPost, BrassLedgerPermissions.JournalReverse);
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.Equal("Disposed", schedule.Status);
        decimal bankBalanceAfterDisposal;
        await using (var db = await factory.CreateDbContextAsync()) bankBalanceAfterDisposal = (await db.BankAccounts.SingleAsync(bank => bank.Id == accounts.PaymentBank)).CurrentBalance;
        Assert.Equal(bankBalanceBeforeDisposal + 1200m, bankBalanceAfterDisposal);
        var businessWorkspace = await scope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
        Assert.Contains(businessWorkspace.Treasury.ReconciliationCandidates ?? [], candidate => candidate.JournalEntryId == disposalId && candidate.BankAccountId == accounts.PaymentBank && candidate.SignedAmount == 1200m);

        var disposalReconciliationId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BankReconciliations.Add(new BankReconciliation { Id = disposalReconciliationId, CompanyId = actor.CompanyId, BankAccountId = accounts.PaymentBank, StatementDate = new DateOnly(2026, 5, 16), Status = "Completed" });
            db.BankReconciliationItems.Add(new BankReconciliationItem { Id = Guid.NewGuid(), BankReconciliationId = disposalReconciliationId, JournalEntryId = disposalId });
            await db.SaveChangesAsync();
        }
        var reconciledDisposalReversal = await service.ReverseFixedAssetDisposalAsync(new(schedule.Id, new DateOnly(2026, 5, 16), "Blocked by completed reconciliation.", schedule.ConcurrencyToken));
        Assert.False(reconciledDisposalReversal.Succeeded);
        Assert.Contains("reconcil", reconciledDisposalReversal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BankReconciliations.Remove(await db.BankReconciliations.SingleAsync(item => item.Id == disposalReconciliationId));
            await db.SaveChangesAsync();
        }

        var reversed = await service.ReverseFixedAssetDisposalAsync(new(schedule.Id, new DateOnly(2026, 5, 16), "Sale was cancelled.", schedule.ConcurrencyToken));
        Assert.True(reversed.Succeeded, reversed.ErrorMessage);
        schedule = Assert.Single((await service.GetAccountingScheduleWorkspaceAsync()).Schedules, candidate => candidate.Id == saved.Id);
        Assert.Equal("DisposalReversed", schedule.Status);
        await using (var db = await factory.CreateDbContextAsync()) Assert.Equal(bankBalanceBeforeDisposal, (await db.BankAccounts.SingleAsync(bank => bank.Id == accounts.PaymentBank)).CurrentBalance);
        var retirement = await service.PrepareFixedAssetDisposalAsync(new(schedule.Id, new DateOnly(2026, 5, 17), 0m, null, accounts.DisposalGain, accounts.DisposalLoss, "Retire forklift without proceeds", schedule.ConcurrencyToken));
        Assert.True(retirement.Succeeded, retirement.ErrorMessage);
        await using var verified = await factory.CreateDbContextAsync();
        Assert.Contains(await verified.BusinessAuditEntries.ToArrayAsync(), audit => audit.Action == "accounting-schedule.disposal.reversed" && audit.EntityId == schedule.Id);
        var retirementEntry = await verified.JournalEntries.SingleAsync(entry => entry.Id == retirement.Id);
        var retirementLines = await verified.JournalEntryLines.Where(line => line.JournalEntryId == retirement.Id).ToArrayAsync();
        Assert.Null(retirementEntry.BankAccountId);
        Assert.Contains(retirementLines, line => line.AccountId == accounts.DisposalLoss && line.Debit == 1100m);
        Assert.DoesNotContain(retirementLines, line => line.AccountId == accounts.DisposalGain);
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddBrassLedgerInfrastructure(new ConfigurationBuilder().Build(), _contentRootPath, seedSampleData: true);
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid UserId, Guid CompanyId)> GetActorAsync(IDbContextFactory<BrassLedgerDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(candidate => candidate.UserName == "controller");
        return (user.Id, user.CompanyId);
    }

    private static async Task<ScheduleAccounts> AddScheduleAccountsAsync(IDbContextFactory<BrassLedgerDbContext> factory, Guid companyId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var cashAccountId = await db.Accounts.Where(account => account.CompanyId == companyId && account.OperationalRole == AccountingAccountRoles.OperatingCash).Select(account => account.Id).SingleAsync();
        var accounts = await db.Accounts.Where(account => account.CompanyId == companyId && new[] { "1400", "1500", "1590", "2500", "4400", "6200", "6250", "6400", "6500" }.Contains(account.Number)).ToDictionaryAsync(account => account.Number, account => account.Id);
        return new ScheduleAccounts(accounts["1400"], accounts["1500"], accounts["1590"], accounts["6200"], accounts["6400"], accounts["2500"], accounts["6250"], accounts["4400"], accounts["6500"], await db.BankAccounts.Where(bank => bank.CompanyId == companyId && bank.LedgerAccountId == cashAccountId).Select(bank => bank.Id).SingleAsync());
    }

    private static void SetContext(IServiceProvider services, Guid userId, Guid companyId, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()) };
        claims.AddRange(permissions.Select(permission => new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
    }

    private static async Task<TransactionResult> ApproveAndPostJournalAsSeparateActorsAsync(
        IServiceProvider services,
        IAccountingTransactionService transactions,
        Guid companyId,
        Guid journalEntryId)
    {
        SetContext(services, Guid.NewGuid(), companyId, BrassLedgerPermissions.JournalApprove);
        var approval = await transactions.ApproveJournalEntryAsync(journalEntryId);
        if (!approval.Succeeded)
        {
            return approval;
        }

        SetContext(services, Guid.NewGuid(), companyId, BrassLedgerPermissions.JournalPost);
        return await transactions.PostApprovedJournalEntryAsync(journalEntryId);
    }

    private sealed record ScheduleAccounts(Guid PrepaidAsset, Guid FixedAsset, Guid AccumulatedDepreciation, Guid DepreciationExpense, Guid PrepaidExpense, Guid LoanLiability, Guid InterestExpense, Guid DisposalGain, Guid DisposalLoss, Guid PaymentBank);

    public void Dispose()
    {
        if (!Directory.Exists(_contentRootPath)) return;
        try { Directory.Delete(_contentRootPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
