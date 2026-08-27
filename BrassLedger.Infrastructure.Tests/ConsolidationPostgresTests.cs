using System.Security.Claims;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BrassLedger.Infrastructure.Tests;

public sealed class ConsolidationPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_ConcurrentOverlappingOwnershipPeriodsRetainExactlyOneSuccessor()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_consolidation_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync();
            await using var create = administration.CreateCommand(); create.CommandText = $"CREATE DATABASE {quotedDatabase}"; await create.ExecuteNonQueryAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "BrassLedger.Consolidation.Postgres.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = testBuilder.ConnectionString }).Build();
            var services = new ServiceCollection(); services.AddBrassLedgerInfrastructure(configuration, contentRoot, seedSampleData: true);
            using var provider = services.BuildServiceProvider(); await provider.InitializeBrassLedgerAsync();
            Guid companyId; Guid ownerId;
            using (var readScope = provider.CreateScope())
            {
                var factory = readScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                companyId = await db.Companies.Select(company => company.Id).SingleAsync();
                var owner = await db.Users.Where(user => user.UserName == "controller").SingleAsync(); ownerId = owner.Id;
                if (!await db.CompanyMemberships.AnyAsync(membership => membership.UserId == ownerId && membership.CompanyId == companyId))
                {
                    db.CompanyMemberships.Add(new BrassLedger.Domain.Accounting.CompanyMembership { Id = Guid.NewGuid(), UserId = ownerId, CompanyId = companyId, Role = owner.Role, IsOwner = true, IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow });
                    await db.SaveChangesAsync();
                }
            }

            Guid groupId;
            using (var setupScope = provider.CreateScope())
            {
                SetContext(setupScope, companyId, ownerId);
                var consolidation = setupScope.ServiceProvider.GetRequiredService<IConsolidationService>();
                var created = await consolidation.SaveGroupAsync(new SaveConsolidationGroupRequest(null, "Concurrent ownership", "USD", [new ConsolidationMemberRequest(companyId, 1m, new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 31), nameof(ConsolidationBasis.ReportingParent))], NciAccountNumber: "39998", NciAccountName: "PostgreSQL noncontrolling interests"));
                Assert.True(created.Succeeded, created.ErrorMessage); groupId = created.Id!.Value;
            }

            using var firstScope = provider.CreateScope(); using var secondScope = provider.CreateScope();
            SetContext(firstScope, companyId, ownerId); SetContext(secondScope, companyId, ownerId);
            var attempts = await Task.WhenAll(
                firstScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveOwnershipPeriodAsync(new(null, groupId, companyId, .5m, new DateOnly(2026, 6, 1), null)),
                secondScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveOwnershipPeriodAsync(new(null, groupId, companyId, .6m, new DateOnly(2026, 7, 1), null)));
            Assert.Single(attempts, result => result.Succeeded);
            Assert.Single(attempts, result => !result.Succeeded);

            Guid sourceAccountId; string sourceNumber; string sourceName; BrassLedger.Domain.Accounting.AccountType sourceType; Guid equityAccountId; string equityNumber; string equityName;
            using (var accountScope = provider.CreateScope())
            {
                var factory = accountScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                var source = await db.Accounts.OrderBy(account => account.Number).FirstAsync(); sourceAccountId = source.Id; sourceNumber = source.Number; sourceName = source.Name; sourceType = source.Type;
                var equity = await db.Accounts.OrderBy(account => account.Number).FirstAsync(account => account.Type == AccountType.Equity); equityAccountId = equity.Id; equityNumber = equity.Number; equityName = equity.Name;
            }
            using var thirdScope = provider.CreateScope(); using var fourthScope = provider.CreateScope(); SetContext(thirdScope, companyId, ownerId); SetContext(fourthScope, companyId, ownerId);
            var mappingAttempts = await Task.WhenAll(
                thirdScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveAccountMappingAsync(new(null, groupId, companyId, sourceAccountId, sourceNumber, sourceName, new DateOnly(2026, 7, 1), null)),
                fourthScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveAccountMappingAsync(new(null, groupId, companyId, sourceAccountId, sourceNumber, sourceName, new DateOnly(2026, 8, 1), null)));
            Assert.Single(mappingAttempts, result => result.Succeeded);
            Assert.Single(mappingAttempts, result => !result.Succeeded);

            var statementCode = sourceType is AccountType.Asset or AccountType.Liability or AccountType.Equity ? "BALANCE-SHEET" : "INCOME-STATEMENT";
            var sectionCode = sourceType.ToString().ToUpperInvariant(); var sectionName = sourceType.ToString();
            using var presentationScopeOne = provider.CreateScope(); using var presentationScopeTwo = provider.CreateScope(); SetContext(presentationScopeOne, companyId, ownerId); SetContext(presentationScopeTwo, companyId, ownerId);
            SaveConsolidationStatementPresentationRequest PresentationRequest(string rationale) => new(null, groupId, statementCode, sourceNumber, sourceName, sourceType.ToString(), sectionCode, sectionName, 100, $"Presented {sourceName}", 100, rationale, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), null);
            var presentationAttempts = await Task.WhenAll(
                presentationScopeOne.ServiceProvider.GetRequiredService<IConsolidationService>().SaveStatementPresentationAsync(PresentationRequest("Concurrent PostgreSQL presentation A")),
                presentationScopeTwo.ServiceProvider.GetRequiredService<IConsolidationService>().SaveStatementPresentationAsync(PresentationRequest("Concurrent PostgreSQL presentation B")));
            Assert.Single(presentationAttempts, result => result.Succeeded);
            Assert.Single(presentationAttempts, result => !result.Succeeded);

            Guid rateId; string rateToken;
            using (var rateSetupScope = provider.CreateScope())
            {
                SetContext(rateSetupScope, companyId, ownerId);
                var service = rateSetupScope.ServiceProvider.GetRequiredService<IConsolidationService>();
                var savedRate = await service.SaveExchangeRateAsync(new("USD", "CAD", 1.25m, new DateOnly(2026, 6, 30), "Concurrent test closing rate"));
                Assert.True(savedRate.Succeeded, savedRate.ErrorMessage); rateId = savedRate.Id!.Value;
                rateToken = (await service.GetExchangeRatesAsync()).Single(rate => rate.Id == rateId).ConcurrencyToken;
            }
            using var fifthScope = provider.CreateScope(); using var sixthScope = provider.CreateScope(); SetContext(fifthScope, companyId, ownerId); SetContext(sixthScope, companyId, ownerId);
            var rateAttempts = await Task.WhenAll(
                fifthScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveExchangeRateAsync(new("USD", "CAD", 1.26m, new DateOnly(2026, 6, 30), "Concurrent correction A", rateId, ConcurrencyToken: rateToken)),
                sixthScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveExchangeRateAsync(new("USD", "CAD", 1.27m, new DateOnly(2026, 6, 30), "Concurrent correction B", rateId, ConcurrencyToken: rateToken)));
            Assert.Single(rateAttempts, result => result.Succeeded);
            Assert.Single(rateAttempts, result => !result.Succeeded);

            Guid affiliateId = Guid.NewGuid(); Guid intercompanyCustomerId = Guid.NewGuid();
            using (var tradingPartnerSetupScope = provider.CreateScope())
            {
                var factory = tradingPartnerSetupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                db.Companies.Add(new BrassLedger.Domain.Accounting.Company { Id = affiliateId, Name = "PostgreSQL intercompany affiliate", LegalName = "PostgreSQL intercompany affiliate LLC", TaxId = $"PG-IC-{Guid.NewGuid():N}", BaseCurrency = "USD", FiscalYearStartMonth = 1 });
                db.CompanyMemberships.Add(new BrassLedger.Domain.Accounting.CompanyMembership { Id = Guid.NewGuid(), UserId = ownerId, CompanyId = affiliateId, Role = "Controller", IsOwner = true, IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow });
                db.ConsolidationGroupCompanies.Add(new BrassLedger.Domain.Accounting.ConsolidationGroupCompany { Id = Guid.NewGuid(), ConsolidationGroupId = groupId, MemberCompanyId = affiliateId, OwnershipPercentage = .75m, ConsolidationBasis = ConsolidationBasis.ControlledSubsidiary, BasisRationale = "PostgreSQL reviewed control conclusion", BasisReviewedOn = new DateOnly(2026, 12, 31), EffectiveFrom = new DateOnly(2027, 1, 1), ConcurrencyToken = Guid.NewGuid().ToString("N") });
                db.Customers.Add(new BrassLedger.Domain.Accounting.Customer { Id = intercompanyCustomerId, CompanyId = companyId, CustomerNumber = "PG-IC-CUST", Name = "PostgreSQL intercompany affiliate", Email = "pg-ic@example.invalid", State = "MI", CreditLimit = 1000m });
                await db.SaveChangesAsync();
            }
            using var seventhScope = provider.CreateScope(); using var eighthScope = provider.CreateScope(); SetContext(seventhScope, companyId, ownerId); SetContext(eighthScope, companyId, ownerId);
            var tradingPartnerAttempts = await Task.WhenAll(
                seventhScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveTradingPartnerAsync(new(null, groupId, companyId, affiliateId, intercompanyCustomerId, null, new DateOnly(2027, 1, 1), null)),
                eighthScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveTradingPartnerAsync(new(null, groupId, companyId, affiliateId, intercompanyCustomerId, null, new DateOnly(2027, 2, 1), null)));
            Assert.Single(tradingPartnerAttempts, result => result.Succeeded);
            Assert.Single(tradingPartnerAttempts, result => !result.Succeeded);

            Guid intercompanyVendorId = Guid.NewGuid(); Guid intercompanyInvoiceId = Guid.NewGuid(); Guid intercompanyBillId = Guid.NewGuid();
            using (var discoverySetupScope = provider.CreateScope())
            {
                var factory = discoverySetupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                db.Vendors.Add(new BrassLedger.Domain.Accounting.Vendor { Id = intercompanyVendorId, CompanyId = affiliateId, VendorNumber = "PG-IC-VEND", Name = "Parent", Email = "pg-ic-parent@example.invalid", State = "MI", PaymentTerms = "Net 30" });
                db.SalesInvoices.Add(new BrassLedger.Domain.Accounting.SalesInvoice { Id = intercompanyInvoiceId, CompanyId = companyId, CustomerId = intercompanyCustomerId, InvoiceNumber = "PG-IC-INV-1", InvoiceDate = new DateOnly(2027, 3, 1), DueDate = new DateOnly(2027, 3, 31), Status = "Open", Subtotal = 50m, TotalAmount = 50m, BalanceDue = 50m, ConcurrencyToken = Guid.NewGuid().ToString("N") });
                db.VendorBills.Add(new BrassLedger.Domain.Accounting.VendorBill { Id = intercompanyBillId, CompanyId = affiliateId, VendorId = intercompanyVendorId, BillNumber = "pg-ic-inv-1", BillDate = new DateOnly(2027, 3, 2), DueDate = new DateOnly(2027, 4, 1), Status = "Open", TotalAmount = 50m, BalanceDue = 50m, ConcurrencyToken = Guid.NewGuid().ToString("N") });
                await db.SaveChangesAsync();
                SetContext(discoverySetupScope, companyId, ownerId, BrassLedgerPermissions.JournalPrepare);
                var reciprocal = await discoverySetupScope.ServiceProvider.GetRequiredService<IConsolidationService>().SaveTradingPartnerAsync(new(null, groupId, affiliateId, companyId, null, intercompanyVendorId, new DateOnly(2027, 1, 1), null));
                Assert.True(reciprocal.Succeeded, reciprocal.ErrorMessage);
            }
            using var ninthScope = provider.CreateScope(); using var tenthScope = provider.CreateScope(); SetContext(ninthScope, companyId, ownerId, BrassLedgerPermissions.JournalPrepare); SetContext(tenthScope, companyId, ownerId, BrassLedgerPermissions.JournalPrepare);
            var discoveryAttempts = await Task.WhenAll(
                ninthScope.ServiceProvider.GetRequiredService<IConsolidationService>().DiscoverIntercompanyMatchesAsync(new(groupId, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31))),
                tenthScope.ServiceProvider.GetRequiredService<IConsolidationService>().DiscoverIntercompanyMatchesAsync(new(groupId, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31))));
            Assert.Contains(discoveryAttempts, result => result.Succeeded);
            Assert.Equal(1, discoveryAttempts.Sum(result => result.CreatedCount));

            Guid offsetAccountId; string offsetNumber; string offsetName; BrassLedger.Domain.Accounting.AccountType offsetType;
            var reviewerOne = Guid.NewGuid(); var reviewerTwo = Guid.NewGuid(); var posterOne = Guid.NewGuid(); var posterTwo = Guid.NewGuid(); var reverserOne = Guid.NewGuid(); var reverserTwo = Guid.NewGuid();
            using (var adjustmentSetupScope = provider.CreateScope())
            {
                var factory = adjustmentSetupScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var db = await factory.CreateDbContextAsync();
                var offset = await db.Accounts.OrderBy(account => account.Number).FirstAsync(account => account.Type != sourceType); offsetAccountId = offset.Id; offsetNumber = offset.Number; offsetName = offset.Name; offsetType = offset.Type;
                foreach (var actor in new[] { reviewerOne, reviewerTwo, posterOne, posterTwo, reverserOne, reverserTwo })
                    db.CompanyMemberships.Add(new BrassLedger.Domain.Accounting.CompanyMembership { Id = Guid.NewGuid(), UserId = actor, CompanyId = companyId, Role = "Accounting", IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
            var disclosureContent = new ConsolidationDisclosureDocument(1,
                [new("PG-DEBT", "PostgreSQL term debt", "Long-term debt", 100m, -10m, 0m, 0m, 2m, 0m, 0m, 92m, string.Empty, "PostgreSQL debt working paper")], [], []);
            using var disclosureScopeOne = provider.CreateScope(); using var disclosureScopeTwo = provider.CreateScope(); SetContext(disclosureScopeOne, companyId, ownerId, BrassLedgerPermissions.JournalPrepare); SetContext(disclosureScopeTwo, companyId, ownerId, BrassLedgerPermissions.JournalPrepare);
            SaveConsolidationDisclosurePackageRequest DisclosureRequest() => new(null, groupId, new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 31), "US-GAAP", "2026 annual", disclosureContent);
            var disclosureAttempts = await Task.WhenAll(
                disclosureScopeOne.ServiceProvider.GetRequiredService<IConsolidationService>().SaveDisclosurePackageAsync(DisclosureRequest()),
                disclosureScopeTwo.ServiceProvider.GetRequiredService<IConsolidationService>().SaveDisclosurePackageAsync(DisclosureRequest()));
            Assert.Single(disclosureAttempts, result => result.Succeeded); Assert.Single(disclosureAttempts, result => !result.Succeeded);
            Guid disclosureId; string disclosureToken;
            using (var disclosureReadScope = provider.CreateScope()) { SetContext(disclosureReadScope, companyId, ownerId); var retained = Assert.Single((await disclosureReadScope.ServiceProvider.GetRequiredService<IConsolidationService>().GetDisclosureWorkspaceAsync(groupId))!.Packages); disclosureId = retained.Id; disclosureToken = retained.ConcurrencyToken; }
            using var disclosureApprovalScopeOne = provider.CreateScope(); using var disclosureApprovalScopeTwo = provider.CreateScope(); SetContext(disclosureApprovalScopeOne, companyId, reviewerOne, BrassLedgerPermissions.JournalApprove); SetContext(disclosureApprovalScopeTwo, companyId, reviewerTwo, BrassLedgerPermissions.JournalApprove);
            var disclosureApprovalAttempts = await Task.WhenAll(
                disclosureApprovalScopeOne.ServiceProvider.GetRequiredService<IConsolidationService>().ApproveDisclosurePackageAsync(new(groupId, disclosureId, disclosureToken)),
                disclosureApprovalScopeTwo.ServiceProvider.GetRequiredService<IConsolidationService>().ApproveDisclosurePackageAsync(new(groupId, disclosureId, disclosureToken)));
            Assert.Single(disclosureApprovalAttempts, result => result.Succeeded); Assert.Single(disclosureApprovalAttempts, result => !result.Succeeded);
            Guid adjustmentId; string draftToken;
            using (var adjustmentPreparationScope = provider.CreateScope())
            {
                SetContext(adjustmentPreparationScope, companyId, ownerId, BrassLedgerPermissions.JournalPrepare);
                var service = adjustmentPreparationScope.ServiceProvider.GetRequiredService<IConsolidationService>();
                var offsetMapping = await service.SaveAccountMappingAsync(new(null, groupId, companyId, offsetAccountId, offsetNumber, offsetName, new DateOnly(2026, 8, 1), null)); Assert.True(offsetMapping.Succeeded, offsetMapping.ErrorMessage);
                if (equityAccountId != sourceAccountId && equityAccountId != offsetAccountId) { var equityMapping = await service.SaveAccountMappingAsync(new(null, groupId, companyId, equityAccountId, equityNumber, equityName, new DateOnly(2026, 8, 1), null)); Assert.True(equityMapping.Succeeded, equityMapping.ErrorMessage); }
                var saved = await service.SaveAdjustmentAsync(new(null, groupId, new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 31), "ManualAdjustment", "PG-CONSOL-ADJ-1", "Concurrent PostgreSQL control test", string.Empty,
                [
                    new(sourceNumber, sourceName, sourceType.ToString(), 10m, 0m),
                    new(offsetNumber, offsetName, offsetType.ToString(), 0m, 10m)
                ]));
                Assert.True(saved.Succeeded, saved.ErrorMessage); adjustmentId = saved.Id!.Value; draftToken = (await service.GetAdjustmentWorkspaceAsync(groupId))!.Adjustments.Single(item => item.Id == adjustmentId).ConcurrencyToken;
            }
            using var nciScopeOne = provider.CreateScope(); using var nciScopeTwo = provider.CreateScope(); SetContext(nciScopeOne, companyId, ownerId, BrassLedgerPermissions.JournalPrepare); SetContext(nciScopeTwo, companyId, ownerId, BrassLedgerPermissions.JournalPrepare);
            SaveConsolidationAdjustmentRequest NciRequest(string reference) => new(null, groupId, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31), nameof(ConsolidationAdjustmentKind.NoncontrollingInterest), reference, "PostgreSQL concurrent NCI control", string.Empty,
                [new(equityNumber, equityName, nameof(AccountType.Equity), 5m, 0m, "Parent equity attribution", affiliateId), new("39998", "PostgreSQL noncontrolling interests", nameof(AccountType.Equity), 0m, 5m, "NCI equity presentation", affiliateId)], SubjectCompanyId: affiliateId);
            var nciAttempts = await Task.WhenAll(
                nciScopeOne.ServiceProvider.GetRequiredService<IConsolidationService>().SaveAdjustmentAsync(NciRequest("PG-NCI-A")),
                nciScopeTwo.ServiceProvider.GetRequiredService<IConsolidationService>().SaveAdjustmentAsync(NciRequest("PG-NCI-B")));
            Assert.Single(nciAttempts, result => result.Succeeded); Assert.Single(nciAttempts, result => !result.Succeeded);
            using var approvalScopeOne = provider.CreateScope(); using var approvalScopeTwo = provider.CreateScope();
            SetContext(approvalScopeOne, companyId, reviewerOne, BrassLedgerPermissions.JournalApprove); SetContext(approvalScopeTwo, companyId, reviewerTwo, BrassLedgerPermissions.JournalApprove);
            var approvalAttempts = await Task.WhenAll(
                approvalScopeOne.ServiceProvider.GetRequiredService<IConsolidationService>().ApproveAdjustmentAsync(new(groupId, adjustmentId, draftToken)),
                approvalScopeTwo.ServiceProvider.GetRequiredService<IConsolidationService>().ApproveAdjustmentAsync(new(groupId, adjustmentId, draftToken)));
            Assert.Single(approvalAttempts, result => result.Succeeded); Assert.Single(approvalAttempts, result => !result.Succeeded);
            string approvedToken;
            using (var adjustmentReadScope = provider.CreateScope()) { SetContext(adjustmentReadScope, companyId, ownerId); approvedToken = (await adjustmentReadScope.ServiceProvider.GetRequiredService<IConsolidationService>().GetAdjustmentWorkspaceAsync(groupId))!.Adjustments.Single(item => item.Id == adjustmentId).ConcurrencyToken; }
            using var postingScopeOne = provider.CreateScope(); using var postingScopeTwo = provider.CreateScope();
            SetContext(postingScopeOne, companyId, posterOne, BrassLedgerPermissions.JournalPost); SetContext(postingScopeTwo, companyId, posterTwo, BrassLedgerPermissions.JournalPost);
            var postingAttempts = await Task.WhenAll(
                postingScopeOne.ServiceProvider.GetRequiredService<IConsolidationService>().PostAdjustmentAsync(new(groupId, adjustmentId, approvedToken)),
                postingScopeTwo.ServiceProvider.GetRequiredService<IConsolidationService>().PostAdjustmentAsync(new(groupId, adjustmentId, approvedToken)));
            Assert.Single(postingAttempts, result => result.Succeeded); Assert.Single(postingAttempts, result => !result.Succeeded);
            string postedToken;
            using (var adjustmentReadScope = provider.CreateScope()) { SetContext(adjustmentReadScope, companyId, ownerId); postedToken = (await adjustmentReadScope.ServiceProvider.GetRequiredService<IConsolidationService>().GetAdjustmentWorkspaceAsync(groupId))!.Adjustments.Single(item => item.Id == adjustmentId).ConcurrencyToken; }
            using var reversalScopeOne = provider.CreateScope(); using var reversalScopeTwo = provider.CreateScope();
            SetContext(reversalScopeOne, companyId, reverserOne, BrassLedgerPermissions.JournalReverse); SetContext(reversalScopeTwo, companyId, reverserTwo, BrassLedgerPermissions.JournalReverse);
            var reversalAttempts = await Task.WhenAll(
                reversalScopeOne.ServiceProvider.GetRequiredService<IConsolidationService>().ReverseAdjustmentAsync(new(groupId, adjustmentId, "Concurrent reversal A", postedToken)),
                reversalScopeTwo.ServiceProvider.GetRequiredService<IConsolidationService>().ReverseAdjustmentAsync(new(groupId, adjustmentId, "Concurrent reversal B", postedToken)));
            Assert.Single(reversalAttempts, result => result.Succeeded); Assert.Single(reversalAttempts, result => !result.Succeeded);

            using var verificationScope = provider.CreateScope(); var verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>(); await using var verification = await verificationFactory.CreateDbContextAsync();
            Assert.Equal(3, await verification.ConsolidationGroupCompanies.CountAsync(period => period.ConsolidationGroupId == groupId));
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry => entry.Action == "consolidation-ownership.created" && entry.EntityType == "ConsolidationGroupCompany"));
            Assert.Equal(1, await verification.ConsolidationAccountMappings.CountAsync(mapping => mapping.ConsolidationGroupId == groupId && mapping.MemberAccountId == sourceAccountId));
            Assert.Equal(1, await verification.ConsolidationStatementPresentations.CountAsync(presentation => presentation.ConsolidationGroupId == groupId && presentation.ReportingAccountNumber == sourceNumber));
            var sourceMappingId = await verification.ConsolidationAccountMappings
                .Where(mapping => mapping.ConsolidationGroupId == groupId && mapping.MemberAccountId == sourceAccountId)
                .Select(mapping => mapping.Id)
                .SingleAsync();
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry =>
                entry.Action == "consolidation-account-mapping.created"
                && entry.EntityType == "ConsolidationAccountMapping"
                && entry.EntityId == sourceMappingId));
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry => entry.Action == "consolidation-statement-presentation.created" && entry.EntityType == nameof(ConsolidationStatementPresentation)));
            Assert.Equal(1, await verification.ConsolidationDisclosurePackages.CountAsync(package => package.ConsolidationGroupId == groupId && package.Status == "Approved"));
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry => entry.Action == "consolidation-disclosure.approved" && entry.EntityType == nameof(ConsolidationDisclosurePackage)));
            Assert.Equal(1, await verification.ConsolidationTradingPartners.CountAsync(link => link.ConsolidationGroupId == groupId && link.CustomerId == intercompanyCustomerId));
            Assert.Equal(2, await verification.BusinessAuditEntries.CountAsync(entry => entry.Action == "consolidation-trading-partner.created" && entry.EntityType == "ConsolidationTradingPartner"));
            Assert.Equal(1, await verification.ConsolidationIntercompanyMatches.CountAsync(match => match.ConsolidationGroupId == groupId && match.SalesInvoiceId == intercompanyInvoiceId && match.VendorBillId == intercompanyBillId));
            Assert.Equal(2, await verification.BusinessAuditEntries.CountAsync(entry => entry.EntityType == nameof(BrassLedger.Domain.Accounting.CurrencyExchangeRate)));
            Assert.Equal("Reversed", await verification.ConsolidationAdjustmentBatches.Where(batch => batch.Id == adjustmentId).Select(batch => batch.Status).SingleAsync());
            Assert.Equal(3, await verification.ConsolidationAdjustmentBatches.CountAsync(batch => batch.ConsolidationGroupId == groupId));
            Assert.Equal(1, await verification.ConsolidationAdjustmentBatches.CountAsync(batch => batch.ConsolidationGroupId == groupId && batch.Kind == ConsolidationAdjustmentKind.NoncontrollingInterest && batch.SubjectCompanyId == affiliateId));
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry => entry.EntityId == adjustmentId && entry.Action == "consolidation-adjustment.approved"));
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry => entry.EntityId == adjustmentId && entry.Action == "consolidation-adjustment.posted"));
            Assert.Equal(1, await verification.BusinessAuditEntries.CountAsync(entry => entry.EntityId == adjustmentId && entry.Action == "consolidation-adjustment.reversed"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString); await administration.OpenAsync();
            await using var drop = administration.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)"; await drop.ExecuteNonQueryAsync();
            try { Directory.Delete(contentRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void SetContext(IServiceScope scope, Guid companyId, Guid userId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
            new(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ReportingManage)
        };
        claims.AddRange(permissions.Select(permission => new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
    }
}
