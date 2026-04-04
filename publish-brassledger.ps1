param(
    [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "win-x64",

    [string]$Configuration = "Release"
)

$project = Join-Path $PSScriptRoot "BrassLedger.Web\BrassLedger.Web.csproj"
$output = Join-Path $PSScriptRoot "artifacts\$Runtime"

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $output
