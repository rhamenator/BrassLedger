using System.Diagnostics;

namespace BrassLedger.Web.Hosting;

public static class LocalBrowserLauncher
{
    public static void TryOpen(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }
}
