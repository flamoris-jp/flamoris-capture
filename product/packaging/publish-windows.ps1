# Match the desktop-family entry point while retaining one publish implementation.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../../scripts/publish.ps1') `
    -OutputDirectory 'artifacts/windows/FLAMORIS-Capture-win-x64'
