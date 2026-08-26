using BrassLedger.Application.Accounting;
using BrassLedger.Infrastructure.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BrassLedger.Infrastructure.Tests;

public sealed class SubledgerWorkflowPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_ConcurrentApprovedInvoicePostingCreatesOneAtomicDocument()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("BRASSLEDGER_TEST_POSTGRES")!;
        var databaseName = $"brassledger_test_subledger_{Guid.NewGuid():N}";
        var administrationBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false };
        var quotedDatabase = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using (var administration = new NpgsqlConnection(administrationBuilder.ConnectionString))
        {
            await administration.OpenAsync();
            await using var create = administration.CreateCommand();
            create.CommandText = $"CREATE DATABASE {quotedDatabase}";
            await create.ExecuteNonQueryAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "BrassLedger.Subledger.Postgres.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = testBuilder.ConnectionString
            }).Build();
            var collection = new ServiceCollection();
            collection.AddBrassLedgerInfrastructure(configuration, contentRoot, seedSampleData: true);
            using var provider = collection.BuildServiceProvider();
            await provider.InitializeBrassLedgerAsync();

            Guid workflowId;
            using (var setupScope = provider.CreateScope())
            {
                var workspace = await setupScope.ServiceProvider.GetRequiredService<IBusinessWorkspaceService>().GetWorkspaceAsync();
                var transactions = setupScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
                var request = new CreateInvoiceRequest(workspace.Receivables.Customers.First().Id, "INV-PG-WF-RACE-1", new DateOnly(2026, 8, 5), new DateOnly(2026, 9, 4), 59m, 0m, "4000", "PostgreSQL concurrent workflow posting");
                var draft = await transactions.SaveInvoiceDraftAsync(request);
                Assert.True(draft.Succeeded, draft.ErrorMessage);
                workflowId = draft.Id!.Value;
                Assert.True((await transactions.ApproveSubledgerDocumentAsync(workflowId)).Succeeded);
            }

            using var firstScope = provider.CreateScope();
            using var secondScope = provider.CreateScope();
            var firstTransactions = firstScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
            var secondTransactions = secondScope.ServiceProvider.GetRequiredService<IAccountingTransactionService>();
            var attempts = await Task.WhenAll(
                firstTransactions.PostApprovedSubledgerDocumentAsync(workflowId),
                secondTransactions.PostApprovedSubledgerDocumentAsync(workflowId));
            Assert.Contains(attempts, attempt => attempt.Succeeded);

            var retry = await secondTransactions.PostApprovedSubledgerDocumentAsync(workflowId);
            Assert.True(retry.Succeeded, retry.ErrorMessage);
            using var verificationScope = provider.CreateScope();
            var factory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var verification = await factory.CreateDbContextAsync();
            var invoice = await verification.SalesInvoices.SingleAsync(item => item.InvoiceNumber == "INV-PG-WF-RACE-1");
            Assert.Equal(invoice.Id, retry.Id);
            Assert.Equal(1, await verification.JournalEntries.CountAsync(item => item.SourceDocumentType == "SalesInvoice" && item.SourceDocumentId == invoice.Id));
            Assert.Equal("Posted", await verification.SubledgerDocumentWorkflows.Where(item => item.Id == workflowId).Select(item => item.Status).SingleAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var administration = new NpgsqlConnection(administrationBuilder.ConnectionString);
            await administration.OpenAsync();
            await using var drop = administration.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
            try { Directory.Delete(contentRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
