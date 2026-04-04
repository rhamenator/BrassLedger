param(
    [switch]$InstallBrowsers,
    [switch]$InstallAllBrowsers,
    [switch]$UpdateBaselines,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$webTestsProject = Join-Path $root "BrassLedger.Web.Tests\BrassLedger.Web.Tests.csproj"
$e2eTestsProject = Join-Path $root "BrassLedger.Web.E2E.Tests\BrassLedger.Web.E2E.Tests.csproj"

Write-Host "Building UI test projects..."
dotnet build $webTestsProject -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $e2eTestsProject -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$playwrightScript = Join-Path $root "BrassLedger.Web.E2E.Tests\bin\$Configuration\net8.0\playwright.ps1"

if ($InstallBrowsers -or $InstallAllBrowsers)
{
    if (-not (Test-Path $playwrightScript))
    {
        throw "Playwright install script was not found at $playwrightScript."
    }

    $browserList = if ($InstallAllBrowsers) { @("chromium", "firefox", "webkit") } else { @("chromium") }
    Write-Host "Installing Playwright browsers: $($browserList -join ', ')"
    powershell -ExecutionPolicy Bypass -File $playwrightScript install @browserList
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($UpdateBaselines)
{
    $env:UPDATE_UI_BASELINES = "1"
    Write-Host "Updating visual regression baselines..."
}
else
{
    Remove-Item Env:UPDATE_UI_BASELINES -ErrorAction SilentlyContinue
}

Write-Host "Running Blazor component tests..."
dotnet test $webTestsProject -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Running Playwright end-to-end tests..."
dotnet test $e2eTestsProject -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "UI test suite completed successfully."
