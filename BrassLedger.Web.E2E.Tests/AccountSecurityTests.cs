using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BrassLedger.Web.E2E.Tests;

[Collection("Playwright E2E")]
public sealed class AccountSecurityTests
{
    private readonly PlaywrightWebAppFixture _fixture;

    public AccountSecurityTests(PlaywrightWebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Operator_CanEnrollUseRecoveryCodeAndDisableAuthenticatorMfa(BrowserKind browserKind)
    {
        await using var session = await _fixture.CreateSessionAsync(browserKind);
        await session.SignInAsync("sales");
        await session.GotoAsync("/account/security");
        await session.WaitForHeadingAsync("Protect your operator account and review recent access.");

        await session.Page.Locator("#enrollmentPassword").FillAsync("BrassLedger!2026");
        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Set up authenticator" }).ClickAsync();
        var secretLocator = session.Page.Locator("code.security-secret");
        await secretLocator.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Visible });
        var secret = (await secretLocator.InnerTextAsync()).Trim();
        var recoveryCodes = (await session.Page.Locator(".recovery-code-grid code").AllTextContentsAsync())
            .Select(code => code.Trim())
            .ToArray();
        Assert.Equal(10, recoveryCodes.Length);

        await session.Page.Locator("#enrollmentCode").FillAsync(ComputeCurrentTotp(secret));
        await session.Page.GetByLabel("I saved the recovery codes in a secure location.").CheckAsync();
        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Verify and enable MFA" }).ClickAsync();
        await session.Page.GetByText("Authenticator MFA is enabled and prior sessions are invalid.").WaitForAsync();
        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Sign in again" }).ClickAsync();

        await session.WaitForHeadingAsync("Sign in to BrassLedger.");
        await session.Page.Locator("input[name='userName']").FillAsync("sales");
        await session.Page.Locator("input[name='password']").FillAsync("BrassLedger!2026");
        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await session.WaitForHeadingAsync("Verify your sign-in.");
        await session.Page.Locator("input[name='verificationCode']").FillAsync(recoveryCodes[0]);
        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Verify and sign in" }).ClickAsync();
        await session.WaitForHeadingAsync("Brass Ledger Manufacturing coordinates finance, payroll, operations, reporting, and tax work from one workspace.");

        await session.GotoAsync("/account/security");
        await session.WaitForHeadingAsync("Protect your operator account and review recent access.");
        Assert.Contains("9 recovery code(s) remain", await session.Page.Locator("body").InnerTextAsync());
        await session.Page.GetByText("Disable authenticator MFA", new() { Exact = true }).ClickAsync();
        await session.Page.Locator("#disableMfaPassword").FillAsync("BrassLedger!2026");
        await session.Page.Locator("#disableMfaCode").FillAsync(recoveryCodes[1]);
        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Disable MFA and revoke sessions" }).ClickAsync();
        await session.WaitForHeadingAsync("Protect your operator account and review recent access.");

        var finalContent = await session.Page.Locator("body").InnerTextAsync();
        Assert.Contains("Authenticator MFA and all remaining recovery codes were disabled", finalContent);
        Assert.Contains("Not enabled", finalContent);
        await session.AssertNoUiFailuresAsync("authenticator MFA lifecycle");
    }

    [Theory]
    [MemberData(nameof(BrowserMatrix.InstalledBrowsers), MemberType = typeof(BrowserMatrix))]
    public async Task Operator_CanReviewAndIndividuallyRevokeNamedBrowserSessions(BrowserKind browserKind)
    {
        await using var currentSession = await _fixture.CreateSessionAsync(browserKind);
        await using var otherSession = await _fixture.CreateSessionAsync(browserKind);
        await currentSession.SignInAsync("operations");
        await otherSession.SignInAsync("operations");
        await currentSession.GotoAsync("/account/security");
        await currentSession.WaitForHeadingAsync("Protect your operator account and review recent access.");

        var body = await currentSession.Page.Locator("body").InnerTextAsync();
        Assert.Contains("Signed-in browsers", body);
        Assert.Contains("This browser", body);
        var signOutButtons = currentSession.Page.Locator("table.data-table button").GetByText("Sign out", new() { Exact = true });
        Assert.True(await signOutButtons.CountAsync() >= 1);
        await signOutButtons.First.EvaluateAsync("element => element.click()");
        await currentSession.Page.GetByText("The selected browser session was signed out.").WaitForAsync();

        await otherSession.GotoAsync("/");
        await otherSession.WaitForHeadingAsync("Sign in to BrassLedger.");
        await currentSession.AssertNoUiFailuresAsync("named browser-session revocation");
        await otherSession.AssertNoUiFailuresAsync("revoked browser-session rejection");
    }

    private static string ComputeCurrentTotp(string secret)
    {
        var secretBytes = DecodeBase32(secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secretBytes, counter, hash);
        var offset = hash[^1] & 0x0f;
        var binaryCode = BinaryPrimitives.ReadInt32BigEndian(hash.Slice(offset, 4)) & 0x7fffffff;
        return (binaryCode % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in value.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant())
        {
            var item = character is >= 'A' and <= 'Z' ? character - 'A' : character - '2' + 26;
            buffer = (buffer << 5) | item;
            bitsLeft += 5;
            if (bitsLeft < 8) continue;
            bitsLeft -= 8;
            output.Add((byte)((buffer >> bitsLeft) & 0xff));
        }
        return output.ToArray();
    }
}
