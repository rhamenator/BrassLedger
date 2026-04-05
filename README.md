# BrassLedger

This folder now contains a compileable cross-platform .NET application shell for the new open-source accounting platform.

License: `GPL-3.0-only`. See [LICENSE](LICENSE).

Projects:

- `BrassLedger.Domain`: core business concepts and legacy inventory records
- `BrassLedger.Application`: product catalog contracts and planning models
- `BrassLedger.Infrastructure`: concrete catalog and platform service implementations
- `BrassLedger.Api`: JSON endpoints for assessment and source inventories
- `BrassLedger.Web`: primary end-user web application shell

The current user-facing application is `BrassLedger.Web`.

Why this shape:

- it publishes as a Windows `.exe`
- it can also be published for Linux and macOS
- it keeps the UI cross-platform without locking the product into Windows-only desktop technology

Helpful commands:

```powershell
dotnet build .\BrassLedger.slnx
```

```powershell
.\publish-brassledger.ps1 -Runtime win-x64
```

```powershell
.\publish-brassledger.ps1 -Runtime osx-x64
```

```powershell
.\publish-brassledger.ps1 -Runtime osx-arm64
```

```powershell
.\publish-brassledger.ps1 -Runtime linux-x64
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

Current prerelease:

- The current consumer-facing prerelease is `v0.1.0-pre.1`.
- Release assets are published for Windows, macOS Intel, macOS Apple silicon, and Linux.
- GitHub Actions workflow `.github/workflows/release-installers.yml` builds the unsigned installers and attaches them to tagged prereleases.
- Installer signing is intentionally disabled for now and can be added later once certificate material is available.

Current security baseline:

- authenticated access is required before accounting data loads in the web app or API
- password hashes use ASP.NET Core Identity hashing
- sensitive fields are protected at rest with ASP.NET Core Data Protection
- Data Protection keys are persisted under `App_Data\keys`
- security headers are applied to both the web app and API
- a first non-development startup requires `Bootstrap:AdminPassword` so the system creates a real administrator account instead of demo users

Bootstrap configuration:

- development starts with sample data automatically
- non-development first run requires `Bootstrap:AdminPassword`
- optional bootstrap settings include `Bootstrap:CompanyName`, `Bootstrap:LegalName`, `Bootstrap:AdminUserName`, `Bootstrap:AdminDisplayName`, and `Bootstrap:AdminEmail`

Help and operator guidance:

- In-app help pages live under `BrassLedger.Web/Components/Pages/Help*.razor`.
- Written guides live in `docs/user-guide.md`, `docs/reporting-guide.md`, and `docs/administration-guide.md`.
- Source-controlled web assets belong under `BrassLedger.Web/wwwroot`; generated publish output stays under `artifacts` and remains ignored by Git.

