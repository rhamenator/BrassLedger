using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BrassLedger.Api.Tests;

public sealed class OperationsEndpointTests : IClassFixture<BrassLedgerApiFactory>
{
    private readonly BrassLedgerApiFactory _factory;

    public OperationsEndpointTests(BrassLedgerApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_AreAnonymousAndHealthy(string path)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Responses_EchoSafeCallerCorrelationId()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", "caller-123");

        var response = await client.SendAsync(request);

        Assert.Equal("caller-123", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
    }

    [Fact]
    public async Task Responses_ReplaceUnsafeCallerCorrelationId()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", "bad value with spaces\t<script>");

        var response = await client.SendAsync(request);

        var echoed = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.Matches("^[A-Za-z0-9._-]{1,64}$", echoed);
    }
}
