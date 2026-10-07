using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace BrassLedger.Infrastructure.Auth;

public interface ISecurityEmailTransport
{
    bool IsConfigured { get; }
    Task<string> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);
}

public sealed class MailKitSecurityEmailTransport(IOptions<AccountEmailOptions> options) : ISecurityEmailTransport
{
    private readonly AccountEmailOptions _options = options.Value;

    public bool IsConfigured =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.Host)
        && _options.Port is > 0 and <= 65535
        && IsSecureMode(_options.Security)
        && MailboxAddress.TryParse(_options.FromAddress, out _)
        && Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var baseUri)
        && string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(baseUri.Host)
        && string.IsNullOrEmpty(baseUri.UserInfo)
        && string.IsNullOrEmpty(baseUri.Query)
        && string.IsNullOrEmpty(baseUri.Fragment);

    public async Task<string> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Security email delivery is not configured.");
        if (!MailboxAddress.TryParse(recipient, out var recipientAddress)) throw new InvalidOperationException("The recipient email address is invalid.");

        if (!MailboxAddress.TryParse(_options.FromAddress, out var senderAddress)) throw new InvalidOperationException("The security-email sender address is invalid.");
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new TextPart("plain") { Text = body }
        };
        message.From.Add(new MailboxAddress(_options.FromName, senderAddress.Address));
        message.To.Add(recipientAddress);
        message.MessageId = MimeUtils.GenerateMessageId();

        using var client = new SmtpClient();
        client.Timeout = _options.DeliveryTimeoutSeconds * 1000;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.DeliveryTimeoutSeconds));
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, ResolveSocketOptions(_options.Security), timeout.Token);
            if (!string.IsNullOrWhiteSpace(_options.UserName))
                await client.AuthenticateAsync(_options.UserName, _options.Password, timeout.Token);
            await client.SendAsync(message, timeout.Token, null);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Security-email delivery exceeded the configured timeout.");
        }
        return message.MessageId;
    }

    private static SecureSocketOptions ResolveSocketOptions(string value) => value.Trim().ToUpperInvariant() switch
    {
        "STARTTLS" => SecureSocketOptions.StartTls,
        "SSL" or "SSLONCONNECT" => SecureSocketOptions.SslOnConnect,
        _ => throw new InvalidOperationException("Security email requires StartTls or SslOnConnect transport security.")
    };

    private static bool IsSecureMode(string value) => value.Trim().ToUpperInvariant() is "STARTTLS" or "SSL" or "SSLONCONNECT";
}
