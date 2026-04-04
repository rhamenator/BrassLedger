using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

public sealed class PlaywrightWebAppFixture : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly string _solutionRoot;
    private readonly string _projectRoot;
    private readonly string _projectPath;
    private readonly string _dataRootPath;
    private readonly string _sqliteConnectionString;
    private readonly string _baseUrl;
    private readonly List<Task> _logPumpTasks = new();
    private Process? _appProcess;

    public PlaywrightWebAppFixture()
    {
        _solutionRoot = ResolveSolutionRoot();
        _projectRoot = Path.Combine(_solutionRoot, "BrassLedger.Web");
        _projectPath = Path.Combine(_solutionRoot, "BrassLedger.Web", "BrassLedger.Web.csproj");
        _dataRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.Web.E2E.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRootPath);
        _sqliteConnectionString = $"Data Source={Path.Combine(_dataRootPath, "brassledger.e2e.db")}";
        _baseUrl = $"http://127.0.0.1:{GetOpenPort()}";
    }

    public string BaseUrl => _baseUrl;
    public IPlaywright Playwright { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        StartApplication();
        await WaitForServerAsync();

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        Playwright?.Dispose();

        if (_appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            await _appProcess.WaitForExitAsync();
        }

        await Task.WhenAll(_logPumpTasks);

        if (Directory.Exists(_dataRootPath))
        {
            try
            {
                Directory.Delete(_dataRootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public string GetLogs()
    {
        return string.Join(Environment.NewLine, _logs);
    }

    public async Task<UiSession> CreateSessionAsync(BrowserKind browserKind, int width = 1440, int height = 1600)
    {
        var browser = await LaunchBrowserAsync(browserKind);
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height
            }
        });

        return new UiSession(this, browserKind, browser, page);
    }

    private void StartApplication()
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(_projectPath);
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(_baseUrl);

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__BrassLedgerSqlite"] = _sqliteConnectionString;

        _appProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start BrassLedger.Web for Playwright tests.");
        PumpLogs(_appProcess.StandardOutput, "stdout");
        PumpLogs(_appProcess.StandardError, "stderr");
    }

    private async Task WaitForServerAsync()
    {
        using var httpClient = new HttpClient();
        var timeoutAt = DateTime.UtcNow.AddSeconds(45);

        while (DateTime.UtcNow < timeoutAt)
        {
            if (_appProcess is { HasExited: true })
            {
                throw new InvalidOperationException($"The web app exited before it started listening.{Environment.NewLine}{GetLogs()}");
            }

            try
            {
                using var response = await httpClient.GetAsync(_baseUrl);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Timed out waiting for BrassLedger.Web at {_baseUrl}.{Environment.NewLine}{GetLogs()}");
    }

    private void PumpLogs(StreamReader reader, string source)
    {
        _logPumpTasks.Add(Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                _logs.Enqueue($"[{source}] {line}");
            }
        }));
    }

    private static int GetOpenPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string ResolveSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrassLedger.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate BrassLedger.slnx from the test assembly path.");
    }

    public static IReadOnlyList<BrowserKind> GetInstalledBrowsers()
    {
        var browserRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ms-playwright");

        if (!Directory.Exists(browserRoot))
        {
            return Array.Empty<BrowserKind>();
        }

        var installedBrowsers = new List<BrowserKind>();

        if (Directory.EnumerateDirectories(browserRoot, "chromium-*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.Combine(path, "chrome-win", "chrome.exe"))
            .Any(File.Exists))
        {
            installedBrowsers.Add(BrowserKind.Chromium);
        }

        if (TryResolveEdgeExecutablePath(out _))
        {
            installedBrowsers.Add(BrowserKind.Edge);
        }

        if (Directory.EnumerateDirectories(browserRoot, "firefox-*", SearchOption.TopDirectoryOnly).Any())
        {
            installedBrowsers.Add(BrowserKind.Firefox);
        }

        if (Directory.EnumerateDirectories(browserRoot, "webkit-*", SearchOption.TopDirectoryOnly).Any())
        {
            installedBrowsers.Add(BrowserKind.WebKit);
        }

        return installedBrowsers;
    }

    private async Task<IBrowser> LaunchBrowserAsync(BrowserKind browserKind)
    {
        return browserKind switch
        {
            BrowserKind.Chromium => await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = ResolveChromiumExecutablePath()
            }),
            BrowserKind.Edge => await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = ResolveEdgeExecutablePath()
            }),
            BrowserKind.Firefox => await Playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            }),
            BrowserKind.WebKit => await Playwright.Webkit.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(browserKind), browserKind, "Unsupported browser kind.")
        };
    }

    private static string ResolveChromiumExecutablePath()
    {
        var browserRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ms-playwright");

        var executable = Directory
            .EnumerateDirectories(browserRoot, "chromium-*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.Combine(path, "chrome-win", "chrome.exe"))
            .FirstOrDefault(File.Exists);

        return executable ?? throw new FileNotFoundException("Chromium was not found in the local Playwright cache. Run playwright.ps1 install chromium.");
    }

    private static string ResolveEdgeExecutablePath()
    {
        return TryResolveEdgeExecutablePath(out var executable)
            ? executable
            : throw new FileNotFoundException("Microsoft Edge was not found on this machine.");
    }

    private static bool TryResolveEdgeExecutablePath(out string executablePath)
    {
        var candidatePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };

        executablePath = candidatePaths.FirstOrDefault(File.Exists) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(executablePath);
    }
}

