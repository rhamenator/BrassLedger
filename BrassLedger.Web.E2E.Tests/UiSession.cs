using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Playwright;

namespace BrassLedger.Web.E2E.Tests;

public sealed class UiSession : IAsyncDisposable
{
    private readonly PlaywrightWebAppFixture _fixture;
    private readonly IBrowser _browser;
    private readonly ConcurrentQueue<string> _consoleErrors = new();
    private readonly ConcurrentQueue<string> _pageErrors = new();
    private readonly ConcurrentQueue<string> _requestFailures = new();
    private readonly ConcurrentQueue<string> _serverFailures = new();
    private bool _isAuthenticated;

    public UiSession(PlaywrightWebAppFixture fixture, BrowserKind browserKind, IBrowser browser, IPage page)
    {
        _fixture = fixture;
        BrowserKind = browserKind;
        _browser = browser;
        Page = page;
        HookDiagnostics();
    }

    public BrowserKind BrowserKind { get; }
    public IPage Page { get; }
    public string BaseUrl => _fixture.BaseUrl;

    public async ValueTask DisposeAsync()
    {
        await Page.CloseAsync();
        await _browser.DisposeAsync();
    }

    public async Task GotoAsync(string relativePath, bool allowHttpError = false)
    {
        try
        {
            await Page.GotoAsync($"{BaseUrl}{relativePath}");
        }
        catch (PlaywrightException exception) when (allowHttpError && exception.Message.Contains("ERR_HTTP_RESPONSE_CODE_FAILURE", StringComparison.Ordinal))
        {
        }
    }

    public async Task SignInAsync(string userName = "controller", string password = "BrassLedger!2026", string returnPath = "/")
    {
        if (_isAuthenticated)
        {
            return;
        }

        await GotoAsync($"/login?returnUrl={Uri.EscapeDataString(returnPath)}");
        await WaitForHeadingAsync("Sign in to BrassLedger.");
        await Page.Locator("input[name='userName']").FillAsync(userName);
        await Page.Locator("input[name='password']").FillAsync(password);
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForURLAsync(
            url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = 15000 });

        _isAuthenticated = true;
    }

    public async Task WaitForHeadingAsync(string heading)
    {
        var h1 = Page.Locator("h1").Filter(new() { HasTextString = heading });
        await h1.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
    }

    public async Task AssertNoUiFailuresAsync(string scenario)
    {
        await Page.WaitForTimeoutAsync(300);
        Assert.DoesNotContain("That screen hit a snag.", await Page.ContentAsync());
        Assert.False(_consoleErrors.Any(), BuildFailureMessage($"Browser console errors were detected during {scenario}.", _consoleErrors));
        Assert.False(_pageErrors.Any(), BuildFailureMessage($"Page errors were detected during {scenario}.", _pageErrors));
        Assert.False(_requestFailures.Any(), BuildFailureMessage($"Request failures were detected during {scenario}.", _requestFailures));
        Assert.False(_serverFailures.Any(), BuildFailureMessage($"Server responses >= 500 were detected during {scenario}.", _serverFailures));
    }

    public async Task AssertSingleVisibleHeadingAsync()
    {
        var visibleHeadings = await Page.Locator("h1:visible").AllTextContentsAsync();
        Assert.Single(visibleHeadings);
    }

    public async Task AssertInteractiveElementsHaveNamesAsync()
    {
        var unnamedElements = await Page.EvaluateAsync<string[]>(
            """
            () => {
              const isVisible = (element) => {
                const style = window.getComputedStyle(element);
                return style.display !== 'none' && style.visibility !== 'hidden' && element.getClientRects().length > 0;
              };

              const textFromIds = (ids) =>
                ids
                  .split(/\s+/)
                  .map(id => document.getElementById(id)?.innerText?.trim() ?? '')
                  .join(' ')
                  .trim();

              return Array.from(document.querySelectorAll('a, button, input, select, textarea'))
                .filter(isVisible)
                .filter(element => {
                  const ariaLabel = element.getAttribute('aria-label')?.trim() ?? '';
                  const ariaLabelledBy = element.getAttribute('aria-labelledby') ? textFromIds(element.getAttribute('aria-labelledby')) : '';
                  const title = element.getAttribute('title')?.trim() ?? '';
                  const text = (element.innerText ?? '').trim();
                  const value = 'value' in element ? (element.value ?? '').trim() : '';
                  const placeholder = element.getAttribute('placeholder')?.trim() ?? '';
                  const name = ariaLabel || ariaLabelledBy || title || text || value || placeholder;
                  return name.length === 0;
                })
                .map(element => `${element.tagName.toLowerCase()}${element.getAttribute('href') ? `:${element.getAttribute('href')}` : ''}`);
            }
            """);

        Assert.Empty(unnamedElements);
    }

    public async Task AssertHeadingOrderAsync()
    {
        var headingLevels = await Page.EvaluateAsync<int[]>(
            """
            () => Array.from(document.querySelectorAll('h1, h2, h3, h4')).map(element => Number(element.tagName.substring(1)))
            """);

        Assert.NotEmpty(headingLevels);
        Assert.Equal(1, headingLevels[0]);

        for (var index = 1; index < headingLevels.Length; index++)
        {
            Assert.True(
                headingLevels[index] - headingLevels[index - 1] <= 1,
                $"Heading order skipped from h{headingLevels[index - 1]} to h{headingLevels[index]}.");
        }
    }

    public async Task AssertKeyboardCanFocusAndActivateAsync(string href, string expectedHeading)
    {
        await Page.Keyboard.PressAsync("Tab");

        for (var index = 0; index < 20; index++)
        {
            var focusedElement = await Page.EvaluateAsync<string>(
                """
                () => {
                  const active = document.activeElement;
                  if (!active) {
                    return '||';
                  }

                  const text = active.innerText || active.getAttribute('aria-label') || active.getAttribute('title') || '';
                  const targetHref = active.getAttribute('href') || '';
                  const tagName = active.tagName || '';
                  return `${text}|${targetHref}|${tagName}`;
                }
                """);

            var parts = (focusedElement ?? "||").Split('|');
            var focusedHref = parts.Length > 1 ? parts[1] : string.Empty;

            if (string.Equals(focusedHref, href, StringComparison.OrdinalIgnoreCase))
            {
                await Page.Keyboard.PressAsync("Enter");
                await Page.WaitForURLAsync(url => url.EndsWith($"/{href}", StringComparison.OrdinalIgnoreCase), new PageWaitForURLOptions
                {
                    Timeout = 15000
                });
                await WaitForHeadingAsync(expectedHeading);
                return;
            }

            await Page.Keyboard.PressAsync("Tab");
        }

        throw new Xunit.Sdk.XunitException($"Keyboard navigation never reached the link '{href}'.{Environment.NewLine}{_fixture.GetLogs()}");
    }

    public async Task AssertSnapshotAsync(string snapshotName)
    {
        var snapshotRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Snapshots",
            BrowserKind.ToString().ToLowerInvariant()));
        Directory.CreateDirectory(snapshotRoot);

        var baselinePath = Path.Combine(snapshotRoot, $"{snapshotName}.png");
        var actualPath = Path.Combine(snapshotRoot, $"{snapshotName}.actual.png");

        var bytes = await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = true,
            Animations = ScreenshotAnimations.Disabled
        });

        if (ShouldUpdateSnapshots() || !File.Exists(baselinePath))
        {
            await File.WriteAllBytesAsync(baselinePath, bytes);
            if (File.Exists(actualPath))
            {
                File.Delete(actualPath);
            }
            return;
        }

        var baselineBytes = await File.ReadAllBytesAsync(baselinePath);
        if (ComputeHash(bytes) != ComputeHash(baselineBytes))
        {
            await File.WriteAllBytesAsync(actualPath, bytes);
            throw new Xunit.Sdk.XunitException($"Snapshot mismatch for {snapshotName} on {BrowserKind}. See:{Environment.NewLine}{baselinePath}{Environment.NewLine}{actualPath}");
        }

        if (File.Exists(actualPath))
        {
            File.Delete(actualPath);
        }
    }

    private void HookDiagnostics()
    {
        Page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                _consoleErrors.Enqueue(message.Text);
            }
        };

        Page.PageError += (_, exception) => _pageErrors.Enqueue(exception);
        Page.RequestFailed += (_, request) =>
        {
            if (!ShouldIgnoreRequestFailure(request))
            {
                _requestFailures.Enqueue($"{request.Method} {request.Url} :: {request.Failure}");
            }
        };
        Page.Response += (_, response) =>
        {
            if (response.Status >= 500)
            {
                _serverFailures.Enqueue($"{response.Status} {response.Url}");
            }
        };
    }

    private string BuildFailureMessage(string header, IEnumerable<string> details)
    {
        return $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, details)}{Environment.NewLine}{_fixture.GetLogs()}";
    }

    private static bool ShouldUpdateSnapshots()
    {
        return string.Equals(Environment.GetEnvironmentVariable("UPDATE_UI_BASELINES"), "1", StringComparison.Ordinal);
    }

    private static bool ShouldIgnoreRequestFailure(IRequest request)
    {
        var failure = request.Failure?.ToString() ?? string.Empty;
        return failure.Contains("ERR_ABORTED", StringComparison.OrdinalIgnoreCase)
            || failure.Contains("NS_BINDING_ABORTED", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
