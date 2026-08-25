using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BrassLedger.Infrastructure.Accounting;

public interface IQuickBooksOnlineClient
{
    string BuildAuthorizationUrl(string state);
    Task<QuickBooksTokenResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<QuickBooksTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<QuickBooksProviderResult> RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<QuickBooksCompanyInfoResponse> GetCompanyInfoAsync(string environment, string realmId, string accessToken, CancellationToken cancellationToken = default);
    Task<QuickBooksEntityQueryResponse> QueryEntitiesAsync(string environment, string realmId, string accessToken, string entityType, CancellationToken cancellationToken = default);
}

public sealed record QuickBooksTokenResponse(
    bool Succeeded,
    string ErrorCode,
    string AccessToken,
    string RefreshToken,
    string TokenType,
    string Scope,
    int AccessTokenExpiresInSeconds,
    int RefreshTokenExpiresInSeconds);

public sealed record QuickBooksProviderResult(bool Succeeded, string ErrorCode);
public sealed record QuickBooksCompanyInfoResponse(bool Succeeded, string ErrorCode, string CompanyName, string LegalName, string Country);
public sealed record QuickBooksRemoteEntity(string Id, string SyncToken, bool Active, string Name, string Number, string Email, string AccountType, string AccountSubType);
public sealed record QuickBooksEntityQueryResponse(bool Succeeded, string ErrorCode, IReadOnlyList<QuickBooksRemoteEntity> Entities);

public sealed class QuickBooksOnlineClient(
    IHttpClientFactory httpClientFactory,
    IOptions<QuickBooksOnlineOptions> optionsAccessor) : IQuickBooksOnlineClient
{
    private const int MaximumProviderResponseCharacters = 64 * 1024;
    private const int MaximumQueryResponseCharacters = 4 * 1024 * 1024;
    private readonly QuickBooksOnlineOptions _options = optionsAccessor.Value;

    public string BuildAuthorizationUrl(string state)
    {
        return QueryHelpers.AddQueryString(_options.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["scope"] = "com.intuit.quickbooks.accounting",
            ["redirect_uri"] = _options.RedirectUri,
            ["state"] = state
        });
    }

    public Task<QuickBooksTokenResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri
            },
            cancellationToken);
    }

    public Task<QuickBooksTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        return RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            },
            cancellationToken);
    }

    public async Task<QuickBooksProviderResult> RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.RevocationEndpoint);
        request.Headers.Authorization = BuildClientAuthentication();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(new { token = refreshToken }), Encoding.UTF8, "application/json");
        using var response = await httpClientFactory.CreateClient("QuickBooksOnline").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await ReadBoundedResponseAsync(response, cancellationToken);
        if (response.IsSuccessStatusCode) return new(true, string.Empty);
        var errorCode = ParseErrorCode(payload);
        return new(errorCode is "invalid_grant" or "invalid_token", errorCode);
    }

    public async Task<QuickBooksCompanyInfoResponse> GetCompanyInfoAsync(string environment, string realmId, string accessToken, CancellationToken cancellationToken = default)
    {
        var baseUrl = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
            ? _options.ProductionApiBaseUrl
            : _options.SandboxApiBaseUrl;
        var requestUri = $"{baseUrl.TrimEnd('/')}/v3/company/{Uri.EscapeDataString(realmId)}/companyinfo/{Uri.EscapeDataString(realmId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await httpClientFactory.CreateClient("QuickBooksOnline").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await ReadBoundedResponseAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode) return new(false, ParseErrorCode(payload), string.Empty, string.Empty, string.Empty);
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!TryGetProperty(document.RootElement, "CompanyInfo", out var companyInfo))
                return new(false, "invalid_company_response", string.Empty, string.Empty, string.Empty);
            return new(
                true,
                string.Empty,
                GetString(companyInfo, "CompanyName"),
                GetString(companyInfo, "LegalName"),
                GetString(companyInfo, "Country"));
        }
        catch (JsonException)
        {
            return new(false, "invalid_company_response", string.Empty, string.Empty, string.Empty);
        }
    }

    public async Task<QuickBooksEntityQueryResponse> QueryEntitiesAsync(string environment, string realmId, string accessToken, string entityType, CancellationToken cancellationToken = default)
    {
        var providerEntityName = entityType.Trim().ToLowerInvariant() switch
        {
            "accounts" => "Account",
            "customers" => "Customer",
            "vendors" => "Vendor",
            _ => string.Empty
        };
        if (providerEntityName.Length == 0) return new(false, "unsupported_entity_type", []);
        var baseUrl = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
            ? _options.ProductionApiBaseUrl
            : _options.SandboxApiBaseUrl;
        var entities = new List<QuickBooksRemoteEntity>();
        const int pageSize = 1000;
        for (var startPosition = 1; entities.Count < 10_000; startPosition += pageSize)
        {
            var query = $"select * from {providerEntityName} startposition {startPosition} maxresults {pageSize}";
            var requestUri = QueryHelpers.AddQueryString(
                $"{baseUrl.TrimEnd('/')}/v3/company/{Uri.EscapeDataString(realmId)}/query",
                "query",
                query);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClientFactory.CreateClient("QuickBooksOnline").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var payload = await ReadBoundedResponseAsync(response, cancellationToken, MaximumQueryResponseCharacters);
            if (!response.IsSuccessStatusCode) return new(false, ParseErrorCode(payload), []);
            List<QuickBooksRemoteEntity> page;
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (!TryGetProperty(document.RootElement, "QueryResponse", out var queryResponse))
                    return new(false, "invalid_query_response", []);
                if (!TryGetProperty(queryResponse, providerEntityName, out var items)) break;
                if (items.ValueKind != JsonValueKind.Array) return new(false, "invalid_query_response", []);
                page = items.EnumerateArray().Select(item => ParseRemoteEntity(item, providerEntityName)).ToList();
                if (page.Any(item => string.IsNullOrWhiteSpace(item.Id))) return new(false, "invalid_query_response", []);
            }
            catch (JsonException)
            {
                return new(false, "invalid_query_response", []);
            }
            entities.AddRange(page);
            if (page.Count < pageSize) break;
        }
        if (entities.Count >= 10_000) return new(false, "result_limit_exceeded", []);
        return new(true, string.Empty, entities.OrderBy(entity => entity.Id, StringComparer.Ordinal).ToArray());
    }

    private async Task<QuickBooksTokenResponse> RequestTokenAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint);
        request.Headers.Authorization = BuildClientAuthentication();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(values);
        using var response = await httpClientFactory.CreateClient("QuickBooksOnline").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await ReadBoundedResponseAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new(false, ParseErrorCode(payload), string.Empty, string.Empty, string.Empty, string.Empty, 0, 0);
        try
        {
            var token = JsonSerializer.Deserialize<TokenDocument>(payload);
            if (token is null
                || string.IsNullOrWhiteSpace(token.AccessToken)
                || string.IsNullOrWhiteSpace(token.RefreshToken)
                || token.ExpiresIn <= 0
                || token.RefreshTokenExpiresIn <= 0)
                return new(false, "invalid_token_response", string.Empty, string.Empty, string.Empty, string.Empty, 0, 0);
            return new(
                true,
                string.Empty,
                token.AccessToken,
                token.RefreshToken,
                string.IsNullOrWhiteSpace(token.TokenType) ? "bearer" : token.TokenType,
                token.Scope ?? string.Empty,
                token.ExpiresIn,
                token.RefreshTokenExpiresIn);
        }
        catch (JsonException)
        {
            return new(false, "invalid_token_response", string.Empty, string.Empty, string.Empty, string.Empty, 0, 0);
        }
    }

    private AuthenticationHeaderValue BuildClientAuthentication()
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static async Task<string> ReadBoundedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken, int maximumCharacters = MaximumProviderResponseCharacters)
    {
        if (response.Content.Headers.ContentLength > maximumCharacters)
            throw new HttpRequestException("The QuickBooks provider returned an unexpectedly large response.");
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (payload.Length > maximumCharacters)
            throw new HttpRequestException("The QuickBooks provider returned an unexpectedly large response.");
        return payload;
    }

    private static string ParseErrorCode(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var code = GetString(root, "error");
            if (!string.IsNullOrWhiteSpace(code)) return SanitizeCode(code);
            if (TryGetProperty(root, "Fault", out var fault)
                && TryGetProperty(fault, "Error", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                code = GetString(errors[0], "code");
                if (!string.IsNullOrWhiteSpace(code)) return SanitizeCode(code);
            }
        }
        catch (JsonException)
        {
        }
        return "provider_request_failed";
    }

    private static string SanitizeCode(string value)
    {
        var sanitized = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '_' or '-').Take(64).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "provider_request_failed" : sanitized;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static QuickBooksRemoteEntity ParseRemoteEntity(JsonElement item, string providerEntityName)
    {
        var name = providerEntityName == "Account" ? GetString(item, "Name") : GetString(item, "DisplayName");
        var number = providerEntityName switch
        {
            "Account" => GetString(item, "AcctNum"),
            "Customer" => GetString(item, "ResaleNum"),
            "Vendor" => GetString(item, "AcctNum"),
            _ => string.Empty
        };
        var email = string.Empty;
        if (TryGetProperty(item, "PrimaryEmailAddr", out var emailAddress)) email = GetString(emailAddress, "Address");
        if (email.Length == 0 && TryGetProperty(item, "PrimaryEmailAddr", out emailAddress)) email = GetString(emailAddress, "Addr");
        var active = !TryGetProperty(item, "Active", out var activeValue) || activeValue.ValueKind != JsonValueKind.False;
        return new(
            GetString(item, "Id"),
            GetString(item, "SyncToken"),
            active,
            name,
            number,
            email,
            GetString(item, "AccountType"),
            GetString(item, "AccountSubType"));
    }

    private sealed class TokenDocument
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("x_refresh_token_expires_in")]
        public int RefreshTokenExpiresIn { get; set; }
    }
}
