namespace BrassLedger.Infrastructure.Auth;

public sealed class AccountEmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "BrassLedger Security";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int InvitationLifetimeHours { get; set; } = 24;
    public int EmailVerificationLifetimeHours { get; set; } = 24;
    public int PasswordResetLifetimeMinutes { get; set; } = 30;
    public int MaximumDeliveryAttempts { get; set; } = 8;
    public int DeliveryTimeoutSeconds { get; set; } = 30;
}
