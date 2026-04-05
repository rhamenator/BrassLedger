# BrassLedger Quick Start

This guide helps first-time users install BrassLedger, sign in, and start exploring the main work areas.

## Download an installer

Get the installer that matches your operating system from the current prerelease on GitHub Releases:

- Windows: `BrassLedger-setup-win-x64.exe`
- macOS Intel: `BrassLedger-<version>-osx-x64.pkg`
- macOS Apple silicon: `BrassLedger-<version>-osx-arm64.pkg`
- Linux: `brassledger_<version>_amd64.deb`

## Install BrassLedger

### Windows

1. Download `BrassLedger-setup-win-x64.exe`.
2. Run the installer.
3. Keep the default install directory unless you have a deployment reason to change it.
4. Start BrassLedger from the Start menu or desktop shortcut.

### macOS

1. Download the `.pkg` for your Mac architecture.
2. Open the package.
3. Follow the installer prompts.
4. Start BrassLedger from Applications.

### Linux

1. Download the `.deb` package.
2. Install it with your package manager or:

```bash
sudo dpkg -i brassledger_<version>_amd64.deb
```

3. Start BrassLedger from your desktop launcher or by running `brassledger`.

## First launch

BrassLedger runs as a local web application. On first launch:

1. Open the sign-in page.
2. Sign in with the administrator account created during setup.
3. Confirm the company, tax identifier, base currency, and fiscal settings are correct.
4. Review the dashboard totals before entering live work.

## Initial setup notes

For a brand-new non-development installation, the deployer must provide:

- `Bootstrap:AdminPassword`
- optionally `Bootstrap:CompanyName`
- optionally `Bootstrap:LegalName`
- optionally `Bootstrap:AdminUserName`
- optionally `Bootstrap:AdminDisplayName`
- optionally `Bootstrap:AdminEmail`

## First-day walkthrough

1. Review `Overview`.
2. Confirm the chart of accounts in `Ledger`.
3. Review open customer and invoice activity in `Receivables`.
4. Review vendor balances and due items in `Payables`.
5. Review inventory and order flow in `Operations`.
6. Review employee and payroll setup in `Payroll`.
7. Review jobs in `Projects`.
8. Review output options in `Reporting` and `Taxes`.

## Next guides

- Main operator guide: [user-guide.md](user-guide.md)
- Reporting, checks, forms, and labels: [reporting-guide.md](reporting-guide.md)
- Administration guidance: [administration-guide.md](administration-guide.md)
