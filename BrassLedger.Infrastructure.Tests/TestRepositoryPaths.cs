using System.Runtime.CompilerServices;

namespace BrassLedger.Infrastructure.Tests;

internal static class TestRepositoryPaths
{
    public static string TaxContent(string relativePath, [CallerFilePath] string callerFilePath = "")
    {
        foreach (var startingPath in StartingPaths(callerFilePath))
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory is not null)
            {
                var taxContentRoot = Path.Combine(directory.FullName, "tax-content");
                if (Directory.Exists(taxContentRoot))
                    return Path.GetFullPath(Path.Combine(taxContentRoot, relativePath));

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the BrassLedger tax-content directory. Set BRASSLEDGER_REPOSITORY_ROOT when tests run outside the repository checkout.");
    }

    private static IEnumerable<string> StartingPaths(string callerFilePath)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("BRASSLEDGER_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot)) yield return configuredRoot;

        yield return Directory.GetCurrentDirectory();

        var callerDirectory = Path.GetDirectoryName(callerFilePath);
        if (!string.IsNullOrWhiteSpace(callerDirectory)) yield return callerDirectory;

        yield return AppContext.BaseDirectory;
    }
}
