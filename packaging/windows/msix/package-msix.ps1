param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,
    [string]$Publisher = "CN=Technolgy For All Development",
    [string]$PublisherDisplayName = "Technolgy For All Development",
    [string]$PackageName = "TechnolgyForAllDevelopment.BrassLedger"
)

function Normalize-Version {
    param([string]$SemanticVersion)

    $coreVersion = $SemanticVersion.Split('-', 2)[0]
    $parts = $coreVersion.Split('.')
    while ($parts.Count -lt 4) {
        $parts += "0"
    }

    return ($parts[0..3] -join '.')
}

$publishDir = (Resolve-Path $PublishDir).Path
$outputDir = [System.IO.Path]::GetFullPath($OutputDir)
$msixDir = Join-Path $env:TEMP ("brassledger-msix-" + [Guid]::NewGuid().ToString("N"))
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path

New-Item -ItemType Directory -Force -Path $outputDir, $msixDir | Out-Null

$brandScript = Join-Path $root "packaging\branding\Generate-BrandAssets.ps1"
& $brandScript -Root $root

Copy-Item -Path (Join-Path $publishDir "*") -Destination $msixDir -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Assets") -Destination (Join-Path $msixDir "Assets") -Recurse -Force

$manifestTemplate = Get-Content (Join-Path $PSScriptRoot "AppxManifest.xml.template") -Raw
$manifest = $manifestTemplate.Replace("{{PACKAGE_NAME}}", $PackageName)
$manifest = $manifest.Replace("{{PUBLISHER}}", $Publisher)
$manifest = $manifest.Replace("{{PUBLISHER_DISPLAY_NAME}}", $PublisherDisplayName)
$manifest = $manifest.Replace("{{VERSION}}", (Normalize-Version $Version))

Set-Content -Path (Join-Path $msixDir "AppxManifest.xml") -Value $manifest -Encoding UTF8

$makeAppx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter makeappx.exe |
    Where-Object { $_.DirectoryName -match "\\x64$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeAppx) {
    throw "makeappx.exe was not found."
}

$msixPath = Join-Path $outputDir ("BrassLedger-{0}-win-x64.msix" -f $Version)
if (Test-Path $msixPath) {
    Remove-Item $msixPath -Force
}

& $makeAppx pack /d $msixDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx.exe failed with exit code $LASTEXITCODE."
}

Write-Host "Created $msixPath"
