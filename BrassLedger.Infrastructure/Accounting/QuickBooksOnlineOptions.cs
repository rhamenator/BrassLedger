namespace BrassLedger.Infrastructure.Accounting;

public sealed class QuickBooksOnlineOptions
{
    public bool Enabled { get; set; }
    public string Environment { get; set; } = "Sandbox";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string AuthorizationEndpoint { get; set; } = "https://appcenter.intuit.com/connect/oauth2";
    public string TokenEndpoint { get; set; } = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";
    public string RevocationEndpoint { get; set; } = "https://developer.api.intuit.com/v2/oauth2/tokens/revoke";
    public string SandboxApiBaseUrl { get; set; } = "https://sandbox-quickbooks.api.intuit.com";
    public string ProductionApiBaseUrl { get; set; } = "https://quickbooks.api.intuit.com";
    public int AuthorizationStateLifetimeMinutes { get; set; } = 10;
}
