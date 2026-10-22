param([string]$OutputDirectory = 'artifacts/win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $output = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        [IO.Path]::GetFullPath($OutputDirectory)
    } else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
    dotnet publish src/Flamoris.Capture.App -c Release -r win-x64 --self-contained true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    dotnet publish src/Flamoris.Capture.Bridge -c Release -r win-x64 --self-contained true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }
    Copy-Item LICENSE $output
    foreach ($name in @('Flamoris.Capture.App.exe', 'Flamoris.Capture.Bridge.exe',
        'Flamoris.Capture.Windows.dll', 'Flamoris.Mcp.Core.dll', 'Flamoris.Mcp.Wpf.dll',
        'coreclr.dll', 'PresentationFramework.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $output $name) -PathType Leaf)) {
            throw "Required publish output is missing: $name"
        }
    }
    Write-Host "Preview bundle: $output"
} finally { Pop-Location }
