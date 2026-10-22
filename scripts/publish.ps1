$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $output = Join-Path $root 'artifacts/win-x64'
    dotnet publish src/Flamoris.Capture.App -c Release -r win-x64 --self-contained true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    dotnet publish src/Flamoris.Capture.Bridge -c Release -r win-x64 --self-contained true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }
    Copy-Item LICENSE $output
    Write-Host "Preview bundle: $output"
} finally { Pop-Location }
