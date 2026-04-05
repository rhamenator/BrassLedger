param(
    [string]$DataRoot = ""
)

$project = Join-Path $PSScriptRoot "BrassLedger.Tools\BrassLedger.Tools.csproj"
$arguments = @("run", "--project", $project, "--", "populate-fake-data")

if (-not [string]::IsNullOrWhiteSpace($DataRoot)) {
    $arguments += @("--data-root", $DataRoot)
}

dotnet @arguments
