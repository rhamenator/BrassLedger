using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Domain.Accounting;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Sockets;
using System.Security.Authentication;

namespace BrassLedger.Infrastructure.Auth;

public interface ISecurityEmailOutboxDispatcher
{
    Task<bool> DispatchNextAsync(CancellationToken cancellationToken = default);
}

public sealed class SecurityEmailOutboxDispatcher(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    ISecurityEmailTransport transport,
    IOptions<AccountEmailOptions> options,
    TimeProvider timeProvider) : ISecurityEmailOutboxDispatcher
{
    public async Task<bool> DispatchNextAsync(CancellationToken cancellationToken = default)
    {
        if (!transport.IsConfigured) return false;
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await CancelInvalidActionMessagesAsync(db, now, cancellationToken);
        var candidateId = db.Database.IsSqlite()
            ? await db.SecurityEmailOutboxMessages
                .FromSqlInterpolated($"""SELECT message.* FROM "SecurityEmailOutboxMessages" AS message WHERE message."DeliveredAtUtc" IS NULL AND message."Status" IN ('Pending', 'Failed', 'Sending') AND message."AttemptCount" < {options.Value.MaximumDeliveryAttempts} AND julianday(message."NextAttemptAtUtc") <= julianday({now.ToString("O")}) AND (message."LeaseExpiresAtUtc" IS NULL OR julianday(message."LeaseExpiresAtUtc") <= julianday({now.ToString("O")})) AND (message."RequiresUsableAction" = 0 OR EXISTS (SELECT 1 FROM "AccountActionTokens" AS action WHERE action."Id" = message."AccountActionTokenId" AND action."ConsumedAtUtc" IS NULL AND julianday(action."ExpiresAtUtc") > julianday({now.ToString("O")}))) ORDER BY julianday(message."CreatedAtUtc") LIMIT 1""")
                .AsNoTracking()
                .Select(message => (Guid?)message.Id)
                .SingleOrDefaultAsync(cancellationToken)
            : await db.SecurityEmailOutboxMessages.AsNoTracking()
                .Where(message => message.DeliveredAtUtc == null
                    && (message.Status == "Pending" || message.Status == "Failed" || message.Status == "Sending")
                    && message.AttemptCount < options.Value.MaximumDeliveryAttempts
                    && message.NextAttemptAtUtc <= now
                    && (message.LeaseExpiresAtUtc == null || message.LeaseExpiresAtUtc <= now)
                    && (!message.RequiresUsableAction || db.AccountActionTokens.Any(action => action.Id == message.AccountActionTokenId && action.ConsumedAtUtc == null && action.ExpiresAtUtc > now)))
                .OrderBy(message => message.CreatedAtUtc)
                .Select(message => (Guid?)message.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (!candidateId.HasValue) return false;

        var claimed = db.Database.IsSqlite()
            ? await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "SecurityEmailOutboxMessages" SET "Status" = 'Sending', "AttemptCount" = "AttemptCount" + 1, "LeaseExpiresAtUtc" = {now.AddMinutes(5).ToString("O")} WHERE "Id" = {candidateId.Value} AND "DeliveredAtUtc" IS NULL AND "Status" IN ('Pending', 'Failed', 'Sending') AND "AttemptCount" < {options.Value.MaximumDeliveryAttempts} AND julianday("NextAttemptAtUtc") <= julianday({now.ToString("O")}) AND ("LeaseExpiresAtUtc" IS NULL OR julianday("LeaseExpiresAtUtc") <= julianday({now.ToString("O")})) AND ("RequiresUsableAction" = 0 OR EXISTS (SELECT 1 FROM "AccountActionTokens" AS action WHERE action."Id" = "SecurityEmailOutboxMessages"."AccountActionTokenId" AND action."ConsumedAtUtc" IS NULL AND julianday(action."ExpiresAtUtc") > julianday({now.ToString("O")})))""",
                cancellationToken)
            : await db.SecurityEmailOutboxMessages
                .Where(message => message.Id == candidateId.Value
                    && message.DeliveredAtUtc == null
                    && (message.Status == "Pending" || message.Status == "Failed" || message.Status == "Sending")
                    && message.AttemptCount < options.Value.MaximumDeliveryAttempts
                    && message.NextAttemptAtUtc <= now
                    && (message.LeaseExpiresAtUtc == null || message.LeaseExpiresAtUtc <= now)
                    && (!message.RequiresUsableAction || db.AccountActionTokens.Any(action => action.Id == message.AccountActionTokenId && action.ConsumedAtUtc == null && action.ExpiresAtUtc > now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.Status, "Sending")
                    .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1)
                    .SetProperty(message => message.LeaseExpiresAtUtc, now.AddMinutes(5)), cancellationToken);
        if (claimed != 1) return true;

        var message = await db.SecurityEmailOutboxMessages.SingleAsync(item => item.Id == candidateId.Value, cancellationToken);
        if (message.RequiresUsableAction)
        {
            var action = await db.AccountActionTokens.AsNoTracking().SingleOrDefaultAsync(item => item.Id == message.AccountActionTokenId, cancellationToken);
            if (action is null || action.ConsumedAtUtc is not null || action.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                CancelMessage(message);
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
        }
        try
        {
            message.ProviderMessageId = await transport.SendAsync(message.RecipientEmail, message.Subject, message.Body, cancellationToken);
            message.Status = "Delivered";
            message.DeliveredAtUtc = timeProvider.GetUtcNow();
            message.LeaseExpiresAtUtc = null;
            message.LastError = string.Empty;
            message.Body = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            message.Status = message.AttemptCount >= options.Value.MaximumDeliveryAttempts ? "FailedPermanent" : "Failed";
            message.LeaseExpiresAtUtc = null;
            message.NextAttemptAtUtc = timeProvider.GetUtcNow().AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(message.AttemptCount, 5))));
            message.LastError = SanitizeError(exception);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task CancelInvalidActionMessagesAsync(BrassLedgerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "SecurityEmailOutboxMessages" SET "Status" = 'Cancelled', "Body" = '', "LeaseExpiresAtUtc" = NULL, "LastError" = 'Account action expired or was invalidated before SMTP acceptance.' WHERE "DeliveredAtUtc" IS NULL AND "RequiresUsableAction" = 1 AND "Status" IN ('Pending', 'Failed', 'FailedPermanent', 'Sending') AND NOT EXISTS (SELECT 1 FROM "AccountActionTokens" AS action WHERE action."Id" = "SecurityEmailOutboxMessages"."AccountActionTokenId" AND action."ConsumedAtUtc" IS NULL AND julianday(action."ExpiresAtUtc") > julianday({now.ToString("O")}))""",
                cancellationToken);
            return;
        }

        await db.SecurityEmailOutboxMessages
            .Where(message => message.DeliveredAtUtc == null
                && message.RequiresUsableAction
                && (message.Status == "Pending" || message.Status == "Failed" || message.Status == "FailedPermanent" || message.Status == "Sending")
                && !db.AccountActionTokens.Any(action => action.Id == message.AccountActionTokenId && action.ConsumedAtUtc == null && action.ExpiresAtUtc > now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, "Cancelled")
                .SetProperty(message => message.Body, string.Empty)
                .SetProperty(message => message.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(message => message.LastError, "Account action expired or was invalidated before SMTP acceptance."), cancellationToken);
    }

    private static void CancelMessage(SecurityEmailOutboxMessage message)
    {
        message.Status = "Cancelled";
        message.Body = string.Empty;
        message.LeaseExpiresAtUtc = null;
        message.LastError = "Account action expired or was invalidated before SMTP acceptance.";
    }

    private static string SanitizeError(Exception exception)
    {
        return exception switch
        {
            SmtpCommandException smtp => $"SmtpCommandException: SMTP command rejected ({smtp.StatusCode}, {smtp.ErrorCode}).",
            SmtpProtocolException => "SmtpProtocolException: SMTP protocol failure.",
            SocketException socket => $"SocketException: SMTP network failure ({socket.SocketErrorCode}).",
            AuthenticationException => "AuthenticationException: SMTP transport or credential authentication failed.",
            _ => $"{exception.GetType().Name}: Security-email delivery failed."
        };
    }
}

public sealed class SecurityEmailOutboxWorker(
    ISecurityEmailOutboxDispatcher dispatcher,
    ISecurityEmailTransport transport,
    ILogger<SecurityEmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (transport.IsConfigured)
                {
                    while (await dispatcher.DispatchNextAsync(stoppingToken)) { }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Security-email outbox processing failed with {ExceptionType}; delivery will be retried on the next worker interval.", exception.GetType().Name);
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
