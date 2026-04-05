using BrassLedger.Web.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BrassLedger.Web.Tests;

public sealed class DesktopHostOptionsTests
{
    [Fact]
    public void Resolve_InProductionWithoutExplicitUrls_UsesDynamicLoopbackAndLaunchesBrowser()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var options = DesktopHostOptions.Resolve(configuration, new StubHostEnvironment("Production"), Array.Empty<string>());

        Assert.True(options.UseDynamicLoopbackBinding);
        Assert.True(options.LaunchBrowserOnStartup);
    }

    [Fact]
    public void Resolve_WithExplicitUrls_DisablesDesktopShellBehavior()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://127.0.0.1:5000"
            })
            .Build();

        var options = DesktopHostOptions.Resolve(configuration, new StubHostEnvironment("Production"), Array.Empty<string>());

        Assert.False(options.UseDynamicLoopbackBinding);
        Assert.False(options.LaunchBrowserOnStartup);
    }

    [Fact]
    public void Resolve_InDevelopment_DisablesDesktopShellBehavior()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var options = DesktopHostOptions.Resolve(configuration, new StubHostEnvironment(Environments.Development), Array.Empty<string>());

        Assert.False(options.UseDynamicLoopbackBinding);
        Assert.False(options.LaunchBrowserOnStartup);
    }

    [Fact]
    public void ResolveLaunchUrl_PrefersHttpAddress()
    {
        var url = DesktopHostOptions.ResolveLaunchUrl(new[]
        {
            "https://127.0.0.1:7193",
            "http://127.0.0.1:58421"
        });

        Assert.Equal("http://127.0.0.1:58421", url);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BrassLedger.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
