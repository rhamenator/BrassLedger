# BrassLedger Modern

This folder now contains a compileable cross-platform .NET application shell for the FoxPro remake.

Projects:

- `BrassLedger.Domain`: core business concepts and legacy inventory records
- `BrassLedger.Application`: modernization assessment contracts and planning models
- `BrassLedger.Infrastructure`: concrete modernization service implementations
- `BrassLedger.Api`: JSON endpoints for assessment and source inventories
- `BrassLedger.Web`: primary end-user web application shell

The current user-facing application is `BrassLedger.Web`.

Seeded demo access:

- usernames: `controller`, `operations`, `payroll`, `sales`
- initial password: `BrassLedger!2026`

Why this shape:

- it publishes as a Windows `.exe`
- it can also be published for Linux and macOS
- it keeps the UI cross-platform without locking the product into Windows-only desktop technology

Helpful commands:

```powershell
dotnet build .\BrassLedger.Modern.slnx
```

```powershell
.\publish-brassledger.ps1 -Runtime win-x64
```

```powershell
.\run-ui-tests.ps1 -InstallBrowsers
```

```powershell
.\run-ui-tests.ps1 -UpdateBaselines
```

See `docs/publish-guide.md` for platform-specific publish commands.
UI visual baselines live in `BrassLedger.Web.E2E.Tests\Snapshots` so they can be reviewed and committed with the test suite.
The Playwright matrix auto-detects Microsoft Edge on Windows and will include it in browser-based test runs when available.
Firefox is also supported in the local Playwright browser matrix when installed.

Current security baseline:

- authenticated access is required before accounting data loads in the web app or API
- password hashes use ASP.NET Core Identity hashing
- sensitive fields are protected at rest with ASP.NET Core Data Protection
- Data Protection keys are persisted under `App_Data\keys`
- security headers are applied to both the web app and API
