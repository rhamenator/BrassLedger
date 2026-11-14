param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DataRoot,
    [switch]$ConfirmFakeData
)

if (-not $ConfirmFakeData) {
    throw "Pass -ConfirmFakeData to load known-password demo accounts into a new disposable workspace."
}

$project = Join-Path $PSScriptRoot "BrassLedger.Tools\BrassLedger.Tools.csproj"
$arguments = @("run", "--project", $project, "--", "populate-fake-data", "--confirm", "--data-root", $DataRoot)

dotnet @arguments
