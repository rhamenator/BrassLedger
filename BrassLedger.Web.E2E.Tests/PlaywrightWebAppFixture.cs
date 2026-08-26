using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

public sealed class PlaywrightWebAppFixture : IAsyncLifetime
{
    private static readonly Regex ListeningUrlRegex = new(@"Now listening on:\s+(https?://\S+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly string _solutionRoot;
    private readonly string _projectRoot;
    private readonly string _applicationPath;
    private readonly string _buildConfiguration;
    private readonly string _dataRootPath;
    private readonly string _sqliteConnectionString;
    private readonly List<Task> _logPumpTasks = new();
    private readonly TaskCompletionSource<string> _listeningUrlSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _appProcess;
    private string? _baseUrl;

    public PlaywrightWebAppFixture()
    {
        _solutionRoot = ResolveSolutionRoot();
        _projectRoot = Path.Combine(_solutionRoot, "BrassLedger.Web");
        _buildConfiguration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the E2E test build configuration.");
        _applicationPath = Path.Combine(_projectRoot, "bin", _buildConfiguration, "net8.0", "BrassLedger.Web.dll");
        _dataRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.Web.E2E.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRootPath);
        _sqliteConnectionString = $"Data Source={Path.Combine(_dataRootPath, "brassledger.e2e.db")}";
    }

    public string BaseUrl => _baseUrl ?? throw new InvalidOperationException("The web app has not finished starting yet.");
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

    public async Task CreateQuickBooksAdministratorAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO "AccessRoles" ("Id", "CompanyId", "Name", "Description", "TemplateCode", "Permissions", "IsSystemRole", "IsActive", "RequiresMfa")
            SELECT $roleId, "CompanyId", 'Integration Test Administrator',
                   (SELECT "Description" FROM "AccessRoles" WHERE "CompanyId" = "Users"."CompanyId" AND "Name" = 'Controller'),
                   'e2e-integration-admin',
                   (SELECT "Permissions" FROM "AccessRoles" WHERE "CompanyId" = "Users"."CompanyId" AND "Name" = 'Controller') || '|security.users.manage',
                   0, 1, 0
            FROM "Users" WHERE "UserName" = 'controller';
            INSERT OR IGNORE INTO "Users" (
                "Id", "CompanyId", "UserName", "DisplayName", "Email", "EmailLookupHash", "EmailConfirmedAtUtc",
                "PasswordHash", "SecurityStamp", "Role", "IsActive", "FailedSignInCount", "LastFailedSignInUtc",
                "LockoutEndUtc", "LastSuccessfulSignInUtc", "LastPasswordChangedUtc", "MfaEnabled", "MfaSecret",
                "MfaEnrolledAtUtc", "MfaLastAcceptedTimeStep", "MfaFailedAttemptCount", "MfaLockoutEndUtc")
            SELECT $userId, "CompanyId", 'integration-admin', "DisplayName", "Email", NULL, "EmailConfirmedAtUtc",
                   "PasswordHash", $securityStamp, 'Integration Test Administrator', 1, 0, NULL,
                   NULL, NULL, "LastPasswordChangedUtc", 0, "MfaSecret", NULL, NULL, 0, NULL
            FROM "Users" WHERE "UserName" = 'controller';
            INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
            SELECT $membershipId, "Id", "CompanyId", 'Integration Test Administrator', 0, 1, $grantedAtUtc
            FROM "Users" WHERE "UserName" = 'integration-admin';
            """;
        command.Parameters.AddWithValue("$roleId", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$securityStamp", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$membershipId", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$grantedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task CreateSubledgerWorkflowUsersAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        foreach (var (userName, roleName) in new[]
        {
            ("e2e-ar-approver", "Receivables Approver"),
            ("e2e-ar-poster", "Receivables Poster"),
            ("e2e-ap-approver", "Payables Approver"),
            ("e2e-ap-poster", "Payables Poster")
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO "Users" (
                    "Id", "CompanyId", "UserName", "DisplayName", "Email", "EmailLookupHash", "EmailConfirmedAtUtc",
                    "PasswordHash", "SecurityStamp", "Role", "IsActive", "FailedSignInCount", "LastFailedSignInUtc",
                    "LockoutEndUtc", "LastSuccessfulSignInUtc", "LastPasswordChangedUtc", "MfaEnabled", "MfaSecret",
                    "MfaEnrolledAtUtc", "MfaLastAcceptedTimeStep", "MfaFailedAttemptCount", "MfaLockoutEndUtc")
                SELECT $userId, "CompanyId", $userName, $userName, "Email", NULL, "EmailConfirmedAtUtc",
                       "PasswordHash", $securityStamp, $roleName, 1, 0, NULL,
                       NULL, NULL, "LastPasswordChangedUtc", 0, "MfaSecret", NULL, NULL, 0, NULL
                FROM "Users" WHERE "UserName" = 'controller';
                INSERT OR IGNORE INTO "CompanyMemberships" ("Id", "UserId", "CompanyId", "Role", "IsOwner", "IsActive", "GrantedAtUtc")
                SELECT $membershipId, "Id", "CompanyId", $roleName, 0, 1, $grantedAtUtc
                FROM "Users" WHERE "UserName" = $userName;
                """;
            command.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$userName", userName);
            command.Parameters.AddWithValue("$roleName", roleName);
            command.Parameters.AddWithValue("$securityStamp", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$membershipId", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$grantedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task RemoveQuickBooksAdministratorAsync()
    {
        await using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM "CompanyMemberships"
            WHERE "UserId" = (SELECT "Id" FROM "Users" WHERE "UserName" = 'integration-admin');
            DELETE FROM "Users" WHERE "UserName" = 'integration-admin';
            DELETE FROM "AccessRoles" WHERE "TemplateCode" = 'e2e-integration-admin';
            """;
        await command.ExecuteNonQueryAsync();
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

        startInfo.ArgumentList.Add(_applicationPath);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("http://127.0.0.1:0");

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

            if (_baseUrl is null && _listeningUrlSource.Task.IsCompletedSuccessfully)
            {
                _baseUrl = _listeningUrlSource.Task.Result.TrimEnd('/');
            }

            if (_baseUrl is null)
            {
                await Task.Delay(250);
                continue;
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

                var match = ListeningUrlRegex.Match(line);
                if (match.Success)
                {
                    _listeningUrlSource.TrySetResult(match.Groups[1].Value);
                }
            }
        }));
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
        var browserRoot = ResolveBrowserRoot();

        if (!Directory.Exists(browserRoot))
        {
            return Array.Empty<BrowserKind>();
        }

        var installedBrowsers = new List<BrowserKind>();

        if (TryResolveChromiumExecutablePath(out _))
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
                ExecutablePath = ResolveChromiumExecutablePath(),
                Args = ["--disable-gpu", "--font-render-hinting=none"]
            }),
            BrowserKind.Edge => await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = ResolveEdgeExecutablePath(),
                Args = ["--disable-gpu", "--font-render-hinting=none"]
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

    private static string ResolveBrowserRoot()
    {
        var configuredPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && configuredPath != "0")
        {
            return Path.GetFullPath(configuredPath);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ms-playwright");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Caches", "ms-playwright")
            : Path.Combine(home, ".cache", "ms-playwright");
    }

    private static string ResolveChromiumExecutablePath()
    {
        return TryResolveChromiumExecutablePath(out var executable)
            ? executable
            : throw new FileNotFoundException("Chromium was not found in the Playwright cache. Run playwright.ps1 install chromium.");
    }

    private static bool TryResolveChromiumExecutablePath(out string executablePath)
    {
        var relativeExecutablePaths = OperatingSystem.IsWindows()
            ? new[] { Path.Combine("chrome-win", "chrome.exe") }
            : OperatingSystem.IsMacOS()
                ? new[] { Path.Combine("chrome-mac", "Chromium.app", "Contents", "MacOS", "Chromium") }
                : new[]
                {
                    Path.Combine("chrome-linux", "chrome"),
                    Path.Combine("chrome-linux64", "chrome")
                };

        executablePath = Directory.Exists(ResolveBrowserRoot())
            ? Directory
                .EnumerateDirectories(ResolveBrowserRoot(), "chromium-*", SearchOption.TopDirectoryOnly)
                .SelectMany(path => relativeExecutablePaths.Select(relativePath => Path.Combine(path, relativePath)))
                .FirstOrDefault(File.Exists) ?? string.Empty
            : string.Empty;
        return !string.IsNullOrWhiteSpace(executablePath);
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
