# BrassLedger Publish Guide

## Primary application

The current end-user application is the Blazor-based web shell in:

- `BrassLedger.Web`

It can be published as a self-contained executable or platform-native bundle.

## Windows

```powershell
dotnet publish .\BrassLedger.Web\BrassLedger.Web.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

## Linux

```bash
dotnet publish ./BrassLedger.Web/BrassLedger.Web.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true
```

## macOS

```bash
dotnet publish ./BrassLedger.Web/BrassLedger.Web.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true
```

## Helper script

A PowerShell helper script exists at:

- `publish-brassledger.ps1`

Example:

```powershell
.\publish-brassledger.ps1 -Runtime win-x64
```
