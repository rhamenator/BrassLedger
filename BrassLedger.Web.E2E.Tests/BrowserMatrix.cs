namespace BrassLedger.Web.E2E.Tests;

public static class BrowserMatrix
{
    public static IEnumerable<object[]> InstalledBrowsers()
    {
        foreach (var browser in PlaywrightWebAppFixture.GetInstalledBrowsers())
        {
            yield return new object[] { browser };
        }
    }

    public static IEnumerable<object[]> SnapshotBrowsers()
    {
        var installedBrowsers = PlaywrightWebAppFixture.GetInstalledBrowsers();

        foreach (var browser in new[] { BrowserKind.Chromium, BrowserKind.Edge, BrowserKind.Firefox })
        {
            if (installedBrowsers.Contains(browser))
            {
                yield return new object[] { browser };
            }
        }
    }
}
