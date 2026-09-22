# The same gates as scripts/gate.sh, for a PowerShell prompt.
#
#   .\scripts\gate.ps1            all gates
#   .\scripts\gate.ps1 format     one gate by name
param([string]$Only = "")

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

function Invoke-Gate {
    param([string]$Name, [scriptblock]$Body)
    if ($Only -and $Only -ne $Name) { return }
    Write-Host "==> $Name"
    & $Body
    if ($LASTEXITCODE -ne 0) { throw "gate '$Name' failed with exit code $LASTEXITCODE" }
}

Invoke-Gate format { dotnet format SysmlStudio.slnx --verify-no-changes --severity info }
Invoke-Gate build  { dotnet build SysmlStudio.slnx -warnaserror --nologo }
Invoke-Gate test   { dotnet test SysmlStudio.slnx --no-build --nologo }

$model = $env:SYSML_STUDIO_MODEL
if (-not $model) { $model = "..\os\docs\sysml" }
if (Test-Path $model) {
    Invoke-Gate model { dotnet run --project tools/ParseCheck --no-build -- $model }
} else {
    Write-Host "==> model (skipped: $model does not exist)"
}

Write-Host "all gates passed"
