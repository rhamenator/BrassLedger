using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BrassLedger.Web.Hosting;

public sealed record DesktopHostOptions(
    bool UseDynamicLoopbackBinding,
    bool LaunchBrowserOnStartup)
{
    public static DesktopHostOptions Resolve(IConfiguration configuration, IHostEnvironment environment, IReadOnlyList<string> args)
    {
        if (environment.IsDevelopment())
        {
            return Disabled;
        }

        if (HasExplicitUrls(configuration, args))
        {
            return Disabled;
        }

        if (!ShouldLaunchBrowser(configuration))
        {
            return new DesktopHostOptions(UseDynamicLoopbackBinding: true, LaunchBrowserOnStartup: false);
        }

        return new DesktopHostOptions(UseDynamicLoopbackBinding: true, LaunchBrowserOnStartup: true);
    }

    public static string? ResolveLaunchUrl(IEnumerable<string> addresses)
    {
        var candidates = addresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToArray();

        return candidates.FirstOrDefault(address => address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(address => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    private static bool HasExplicitUrls(IConfiguration configuration, IReadOnlyList<string> args)
    {
        if (!string.IsNullOrWhiteSpace(configuration[WebHostDefaults.ServerUrlsKey]))
        {
            return true;
        }

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--urls", StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Count && !string.IsNullOrWhiteSpace(args[index + 1]);
            }

            if (arg.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase))
            {
                return arg.Length > "--urls=".Length;
            }
        }

        return false;
    }

    private static bool ShouldLaunchBrowser(IConfiguration configuration)
    {
        var configuredValue = configuration["DesktopShell:LaunchBrowser"];
        return !bool.TryParse(configuredValue, out var launchBrowser) || launchBrowser;
    }

    private static DesktopHostOptions Disabled => new(UseDynamicLoopbackBinding: false, LaunchBrowserOnStartup: false);
}
