param([string]$SshTarget = '', [string]$HubRoot = '', [string]$HubEndpoint = '')
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
if (Test-Path (Join-Path $PSScriptRoot 'deployment.json')) {
    $deployment = Get-Content (Join-Path $PSScriptRoot 'deployment.json') -Raw | ConvertFrom-Json
    if (!$SshTarget) { $SshTarget = $deployment.sshTarget }
    if (!$HubRoot) { $HubRoot = $deployment.hubRoot }
}
if (!$SshTarget) { $SshTarget = Read-Host 'SSH target (user@hub-host)' }
if (!$HubRoot) { $HubRoot = Read-Host 'Hub application root shown by Updater' }
if ($SshTarget -notmatch '^[A-Za-z0-9_.@-]+$' -or $SshTarget.StartsWith('-')) { throw 'Invalid SSH target.' }
if ($HubRoot -notmatch '^/[A-Za-z0-9_./-]+$' -or $HubRoot.Contains('/../')) { throw 'Invalid Hub root.' }
# Read only the shared subsystem's non-secret HubEndpoint preference.
if (!$HubEndpoint) {
    $candidates = @()
    $preferences = Join-Path $env:LOCALAPPDATA 'FLAMORIS'
    if (Test-Path $preferences) {
        foreach ($file in (Get-ChildItem $preferences -Filter '*.json' -Recurse -File -ErrorAction SilentlyContinue | Where-Object Length -lt 65536)) {
            try {
                $settings = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                $address = $settings.HubEndpoint
                if ($address -and $address -match '^wss://[^/?#]+/.*/?desktop/[^/]+/?$') {
                    $candidates += ($address -replace '/desktop/[^/]+/?$', '/desktop/capture')
                }
            } catch { }
        }
    }
    $candidates = @($candidates | Select-Object -Unique)
    if ($candidates.Count -eq 1) { $HubEndpoint = $candidates[0] }
    else { $HubEndpoint = Read-Host 'Paste the Hub WSS URL from Cutwork/Kachinco settings (its /desktop/name suffix is changed to /desktop/capture)' }
}
$HubEndpoint = $HubEndpoint.Trim() -replace '/desktop/[^/]+/?$', '/desktop/capture'
$uri = [Uri]$HubEndpoint
if ($uri.Scheme -ne 'wss' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or !$uri.AbsolutePath.EndsWith('/desktop/capture')) { throw 'Expected a WSS Hub desktop endpoint.' }
$catalogPath = Join-Path $PSScriptRoot 'hub-capture.example.json'
$hostScript = Join-Path $PSScriptRoot 'register_capture.py'
if (!(Test-Path $catalogPath)) { $catalogPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'config/hub-capture.example.json' }
if (!(Test-Path $catalogPath) -or !(Test-Path $hostScript)) { throw 'Extract the entire ZIP first.' }
$bytes = New-Object byte[] 40
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$token = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
$localStage = Join-Path ([IO.Path]::GetTempPath()) ('flamoris-capture-' + [Guid]::NewGuid().ToString('N'))
$remoteStage = $null
New-Item -ItemType Directory -Path $localStage | Out-Null
try {
    $tokenFile = Join-Path $localStage 'capture.token'
    [IO.File]::WriteAllText($tokenFile, $token, [Text.Encoding]::ASCII)
    $remoteStage = (& ssh $SshTarget 'umask 077; mktemp -d /tmp/flamoris-capture.XXXXXXXX').Trim()
    if ($LASTEXITCODE -ne 0 -or $remoteStage -notmatch '^/tmp/flamoris-capture\.[A-Za-z0-9]+$') { throw 'Could not create private remote staging directory.' }
    & scp -q $hostScript $catalogPath $tokenFile "${SshTarget}:$remoteStage/"
    if ($LASTEXITCODE -ne 0) { throw 'Upload failed.' }
    & ssh $SshTarget "chmod 600 -- '$remoteStage/capture.token'"
    if ($LASTEXITCODE -ne 0) { throw 'Credential file permission setup failed.' }
    Write-Host 'Registering Capture. Only the Hub container is recreated; current image, configuration and security options are preserved.'
    Write-Host 'Enter your server sudo password if prompted. Never paste it or the connector credential into chat.'
    & ssh -t $SshTarget "sudo python3 '$remoteStage/register_capture.py' --root '$HubRoot' --catalog '$remoteStage/hub-capture.example.json' --token-file '$remoteStage/capture.token'"
    $registrationExit = $LASTEXITCODE
    if ($registrationExit -eq 3) {
        Write-Host 'Capture is already registered; no credential was rotated. Use the previously saved Capture credential.'
    } elseif ($registrationExit -ne 0) { throw 'Registration failed. See the sanitized error above; do not connect the desktop yet.' }
    else {
        Set-Clipboard -Value $token
        Write-Host 'Capture credential copied to clipboard (not printed). Paste it into the Capture MCP settings password field.'
    }
    Write-Host "Hub endpoint: $HubEndpoint"
    Write-Host 'Capture: MCP / AI > Settings > FLAMORIS MCP Hub; Read only; paste endpoint and credential; Save.'
    Write-Host 'Then select/allow a window, check MCP sharing, and use MCP / AI > Connect with Read only.'
    Write-Host 'Existing desktop connections may need reconnecting after the Hub restart.'
} finally {
    if ($remoteStage -and $remoteStage -match '^/tmp/flamoris-capture\.[A-Za-z0-9]+$') { & ssh $SshTarget "rm -rf -- '$remoteStage'" }
    if (Test-Path $localStage) { Remove-Item -LiteralPath $localStage -Recurse -Force }
    $token = $null
}
