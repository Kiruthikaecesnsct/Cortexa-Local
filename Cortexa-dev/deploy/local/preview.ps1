# Cortexa local preview stack - Docker Compose mode. PowerShell 5.1 peer of
# preview.sh - same actions, same switches, same output text.
#
# Previews the checked-out working tree exactly as it sits on disk, including
# uncommitted changes. Performs NO git operations - it never switches
# branches, stashes, or pulls. Run it again after `git switch` yourself if you
# want the new branch's code.
#
# Cloud-backed dependencies (see services.yaml): Gemini (LLM - no local
# equivalent), Qdrant Cloud (vector-router's VECTOR_BACKEND=qdrant), Sentry
# (optional, disabled when blank). Everything else - PostgreSQL, Cosmos DB,
# Blob Storage, Service Bus - runs locally via emulators/containers.
#
# Usage:
#   preview.ps1 start   [-Only a,b] [-SkipSeed]
#   preview.ps1 update  [-Only a,b] [-SkipSeed]
#   preview.ps1 seed
#   preview.ps1 stop    [-DeepClean] [-WipeLocalVolumes]
#   preview.ps1 reset   [-SkipSeed]
#   preview.ps1 status
#   preview.ps1 logs    [-Service <name>]
#   preview.ps1 doctor

param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'update', 'seed', 'stop', 'reset', 'status', 'logs', 'doctor')]
    [string]$Action,
    [string]$Only = '',
    [switch]$SkipSeed,
    [switch]$DeepClean,
    [switch]$WipeLocalVolumes,
    [string]$Service = ''
)

$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
$ProjectName = 'cortexa'
$ComposeProject = "$ProjectName-preview"
$EnvFile = Join-Path $ScriptDir '.env.preview'
$ServicesYaml = Join-Path $ScriptDir 'services.yaml'
$CertsDir = Join-Path $ScriptDir 'certs'
$BaseCompose = Join-Path $RepoRoot 'deploy\docker-compose.yml'
$PreviewCompose = Join-Path $ScriptDir 'docker-compose.preview.yml'

$AppServices = @('api-gateway', 'identity', 'job-orchestrator', 'model-router', 'evidence', 'extraction', 'harvesting', 'ingestion', 'scoring', 'seeding', 'vector-router', 'frontend')
$DepServices = @('postgres', 'cosmos', 'azurite', 'mssql', 'servicebus')
$PublishedPorts = @(5070, 5080, 5081, 5082, 5083, 5084, 5085, 5086, 5087, 5088, 5089, 5090, 5100)

function Write-Log  { param([string]$Message) Write-Host "[preview] $Message" }
function Write-Warn { param([string]$Message) Write-Host "[preview] WARN: $Message" -ForegroundColor Yellow }
function Write-Fail { param([string]$Message) Write-Host "[preview] FAIL: $Message" -ForegroundColor Red; exit 1 }

# ──────────────────────────────────────────────────── services.yaml parse ──
$Svc = @()
$script:PortBase = 5070
$script:PreviewModeRecorded = ''

function Parse-ServicesYaml {
    if (-not (Test-Path $ServicesYaml)) { Write-Fail "$ServicesYaml not found." }
    $script:Svc = @()
    $cur = -1
    $lines = Get-Content -LiteralPath $ServicesYaml
    foreach ($line in $lines) {
        if ($line -match '^\s*#' -or $line -eq '') { continue }
        if ($line -match '^preview_mode:\s*(.*)$') { $script:PreviewModeRecorded = $Matches[1].Trim(); continue }
        if ($line -match '^port_base:\s*(\d+)$') { $script:PortBase = [int]$Matches[1]; continue }
        if ($line -match '^project_name:') { continue }
        if ($line -match '^services:\s*$') { continue }
        if ($line -match '^  ([a-z][a-z0-9_-]*):\s*$') {
            $cur = $cur + 1
            $script:Svc += New-Object PSObject -Property @{
                Name = $Matches[1]; Kind = ''; Placement = ''; Env = ''
                LocalUrl = ''; LocalImage = ''; LocalUnit = ''; CloudNote = ''; Required = 'true'
            }
            continue
        }
        if ($line -match '^    ([a-z_]+):\s*(.*)$') {
            $key = $Matches[1]; $val = $Matches[2].Trim()
            if ($val.StartsWith("'") -and $val.EndsWith("'")) { $val = $val.Substring(1, $val.Length - 2) }
            switch ($key) {
                'kind'        { $script:Svc[$cur].Kind = $val }
                'placement'   { $script:Svc[$cur].Placement = $val }
                'env'         { $script:Svc[$cur].Env = $val }
                'local_url'   { $script:Svc[$cur].LocalUrl = $val }
                'local_image' { $script:Svc[$cur].LocalImage = $val }
                'local_unit'  { $script:Svc[$cur].LocalUnit = $val }
                'cloud_note'  { $script:Svc[$cur].CloudNote = $val }
                'required'    { $script:Svc[$cur].Required = $val }
            }
        }
    }

    if ($script:PreviewModeRecorded -ne 'compose') {
        Write-Fail "services.yaml records preview_mode: $($script:PreviewModeRecorded), but preview.ps1 is the Compose-mode script. Run the matching script for that mode instead."
    }

    foreach ($s in $script:Svc) {
        if ($s.Placement -ne 'local' -and $s.Placement -ne 'cloud') {
            Write-Fail "services.yaml: '$($s.Name)' has an unknown placement '$($s.Placement)' (must be local or cloud)."
        }
        if ($s.Placement -eq 'local' -and $s.LocalImage -eq '' -and $s.LocalUnit -ne '') {
            $hasImage = $false
            foreach ($t in $script:Svc) {
                if ($t.LocalUnit -eq $s.LocalUnit -and $t.LocalImage -ne '') { $hasImage = $true }
            }
            if (-not $hasImage) {
                Write-Fail "services.yaml: '$($s.Name)' is placed local but its local_unit '$($s.LocalUnit)' has no local_image anywhere."
            }
        }
    }
}

function Get-EnabledProfiles {
    $seen = @()
    foreach ($s in $script:Svc) {
        if ($s.Placement -eq 'local' -and $s.LocalUnit -ne '' -and ($seen -notcontains $s.LocalUnit)) {
            $seen += $s.LocalUnit
        }
    }
    return $seen
}

# ─────────────────────────────────────────────────────────────── compose ──
function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$ComposeArgs)
    $profileArgs = @()
    foreach ($p in (Get-EnabledProfiles)) { $profileArgs += @('--profile', $p) }
    $allArgs = @('-p', $ComposeProject, '--env-file', $EnvFile, '-f', $BaseCompose, '-f', $PreviewCompose) + $profileArgs + $ComposeArgs
    & docker compose @allArgs
    if ($LASTEXITCODE -ne 0) { Write-Fail "docker compose $($ComposeArgs -join ' ') failed (exit $LASTEXITCODE)." }
}

function Invoke-ComposeNoFail {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$ComposeArgs)
    $profileArgs = @()
    foreach ($p in (Get-EnabledProfiles)) { $profileArgs += @('--profile', $p) }
    $allArgs = @('-p', $ComposeProject, '--env-file', $EnvFile, '-f', $BaseCompose, '-f', $PreviewCompose) + $profileArgs + $ComposeArgs
    & docker compose @allArgs
    return $LASTEXITCODE
}

# ───────────────────────────────────────────────────────────── runtime ──
function Assert-Runtime {
    $dockerCmd = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $dockerCmd) { Write-Fail 'Docker is not installed or not on PATH.' }
    & docker info *> $null
    if ($LASTEXITCODE -ne 0) { Write-Fail 'Docker daemon is not reachable. Is Docker Desktop running?' }
    $verRaw = & docker compose version --short 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $verRaw) { $verRaw = '0.0.0' }
    $parts = $verRaw.Trim().Split('.')
    $verMajor = [int]$parts[0]
    $verMinor = 0
    if ($parts.Length -gt 1) { $verMinor = [int]$parts[1] }
    if ($verMajor -lt 2 -or ($verMajor -eq 2 -and $verMinor -lt 24)) {
        Write-Fail "Docker Compose $verRaw found; this stack needs >= 2.24 (uses !override/!reset). Update Docker Desktop."
    }
}

# ─────────────────────────────────────────────────────────────── env ──
function Get-EnvVal {
    param([string]$Key)
    if (-not (Test-Path $EnvFile)) { return '' }
    $lines = Get-Content -LiteralPath $EnvFile | Where-Object { $_ -match "^$Key=" }
    if ($lines.Count -eq 0) { return '' }
    $last = $lines[$lines.Count - 1]
    return $last.Substring($Key.Length + 1)
}

function Test-Env {
    if (-not (Test-Path $EnvFile)) {
        Write-Fail "$EnvFile not found. Copy .env.preview.example to .env.preview and fill in the cloud keys, then re-run."
    }
    $missing = $false
    foreach ($s in $script:Svc) {
        if ($s.Placement -ne 'cloud') { continue }
        if ($s.Required -eq 'false') { continue }
        $val = Get-EnvVal -Key $s.Env
        if ([string]::IsNullOrWhiteSpace($val)) {
            Write-Warn "Missing cloud config: $($s.Name) needs $($s.Env) in $EnvFile ($($s.CloudNote))"
            $missing = $true
        }
    }
    if ($missing) { Write-Fail 'One or more required cloud credentials are missing. See warnings above.' }

    if ((Get-EnabledProfiles) -contains 'servicebus') {
        $eula = Get-EnvVal -Key 'ACCEPT_EULA'
        if ($eula -ne 'Y') {
            Write-Fail "ACCEPT_EULA must be Y in $EnvFile to start the Service Bus emulator and its SQL Server Linux companion. See the EULA links in .env.preview.example."
        }
    }
}

# ─────────────────────────────────────────────────────────────── ports ──
function Test-PortBusy {
    param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        $connected = $iar.AsyncWaitHandle.WaitOne(300)
        if ($connected -and $client.Connected) { return $true }
        return $false
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

function Test-Ports {
    $busy = $false
    foreach ($p in $PublishedPorts) {
        if (Test-PortBusy -Port $p) {
            $occupier = (& docker ps --filter "publish=$p" --format '{{.Names}}' 2>$null | Select-Object -First 1)
            if ($occupier) {
                Write-Log "Port $p is already used by container '$occupier' - assuming it's this stack, continuing."
            } else {
                Write-Warn "Port $p is in use by something outside this stack."
                $busy = $true
            }
        }
    }
    if ($busy) { Write-Fail 'One or more preview ports are occupied by a non-preview process. Free them or edit port_base in services.yaml.' }
}

# ─────────────────────────────────────────────── export local placements ──
function Export-LocalEnv {
    foreach ($s in $script:Svc) {
        if ($s.Placement -eq 'local') {
            Set-Item -Path "Env:$($s.Env)" -Value $s.LocalUrl
        }
    }
}

# ────────────────────────────────────────────────────── wait_healthy ──
function Wait-Healthy {
    param([string]$ServiceName, [int]$TimeoutSec = 180)
    Write-Log "Waiting for $ServiceName to be healthy (timeout ${TimeoutSec}s)..."
    $waited = 0
    while ($waited -lt $TimeoutSec) {
        $cid = (Invoke-ComposeCapture -ComposeArgs @('ps', '-q', $ServiceName))
        if ($cid) {
            $status = & docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' $cid.Trim() 2>$null
            if ($status -eq 'healthy') { Write-Log "$ServiceName is healthy."; return }
            if ($status -eq 'none') { Write-Log "$ServiceName has no healthcheck; assuming ready (running)."; return }
        }
        Start-Sleep -Seconds 3
        $waited += 3
    }
    Write-Fail "$ServiceName did not become healthy within ${TimeoutSec}s. Check logs: preview.ps1 logs $ServiceName"
}

function Invoke-ComposeCapture {
    param([string[]]$ComposeArgs)
    $profileArgs = @()
    foreach ($p in (Get-EnabledProfiles)) { $profileArgs += @('--profile', $p) }
    $allArgs = @('-p', $ComposeProject, '--env-file', $EnvFile, '-f', $BaseCompose, '-f', $PreviewCompose) + $profileArgs + $ComposeArgs
    return (& docker compose @allArgs 2>$null)
}

# ────────────────────────────────────────────────── init_cosmos_cert ──
function Init-CosmosCert {
    if ((Get-EnabledProfiles) -notcontains 'cosmos') { return }
    if (-not (Test-Path $CertsDir)) { New-Item -ItemType Directory -Path $CertsDir -Force | Out-Null }
    Write-Log "Fetching the Cosmos DB emulator's TLS certificate..."
    & docker run --rm --network cortexa-network -v "${CertsDir}:/out" curlimages/curl:latest `
        -sk 'https://cosmos:8081/_explorer/emulator.pem' -o '/out/cosmos-emulator.pem'
    if ($LASTEXITCODE -ne 0) {
        Write-Warn 'Could not fetch the Cosmos emulator certificate. Cosmos-dependent services may fail TLS verification - see README troubleshooting.'
    } else {
        Write-Log "Certificate saved to $CertsDir\cosmos-emulator.pem"
    }
}

# ─────────────────────────────────────────────────────────────── actions ──
function Start-Action {
    Assert-Runtime
    Parse-ServicesYaml
    Test-Env
    Test-Ports
    Export-LocalEnv

    $profiles = Get-EnabledProfiles
    $depsEnabled = @()
    foreach ($d in $DepServices) {
        switch ($d) {
            'postgres'   { if ($profiles -contains 'postgres') { $depsEnabled += $d } }
            'cosmos'     { if ($profiles -contains 'cosmos') { $depsEnabled += $d } }
            'azurite'    { if ($profiles -contains 'blob') { $depsEnabled += $d } }
            'mssql'      { if ($profiles -contains 'servicebus') { $depsEnabled += $d } }
            'servicebus' { if ($profiles -contains 'servicebus') { $depsEnabled += $d } }
        }
    }
    if ($depsEnabled.Count -gt 0) {
        Write-Log "Starting dependencies ($($depsEnabled -join ', '))..."
        Invoke-Compose -ComposeArgs (@('up', '-d', '--build') + $depsEnabled)
        foreach ($d in $depsEnabled) { Wait-Healthy -ServiceName $d -TimeoutSec 180 }
    }

    Init-CosmosCert

    Write-Log 'Building and starting app services...'
    if ($Only -ne '') {
        $onlyList = $Only -split ','
        Invoke-Compose -ComposeArgs (@('up', '-d', '--build') + $onlyList)
    } else {
        Invoke-Compose -ComposeArgs @('up', '-d', '--build')
    }

    foreach ($s in $AppServices) {
        if ($Only -ne '' -and (($Only -split ',') -notcontains $s)) { continue }
        Wait-Healthy -ServiceName $s -TimeoutSec 180
    }

    Write-Log 'Migrations: identity applies its own EF Core migrations on startup - nothing further to run.'

    if (-not $SkipSeed) { Seed-Action } else { Write-Log 'Skipping seed (-SkipSeed).' }

    Show-Banner
}

function Update-Action {
    Assert-Runtime
    Parse-ServicesYaml
    Test-Env
    Export-LocalEnv
    Write-Log 'Rebuilding and rolling changed services...'
    if ($Only -ne '') {
        $onlyList = $Only -split ','
        Invoke-Compose -ComposeArgs (@('up', '-d', '--build') + $onlyList)
        Invoke-Compose -ComposeArgs (@('restart') + $onlyList)
    } else {
        Invoke-Compose -ComposeArgs @('up', '-d', '--build')
        Invoke-Compose -ComposeArgs (@('restart') + $AppServices)
    }
    if (-not $SkipSeed) { Seed-Action }
    Show-Banner
}

function Seed-Action {
    Write-Log 'No seed script exists in this project (no scripts/seed.* found under any service). Skipping - nothing to do.'
}

function Stop-Action {
    Parse-ServicesYaml
    if ($WipeLocalVolumes) {
        foreach ($s in $script:Svc) {
            if ($s.Placement -eq 'cloud') {
                Write-Fail "-WipeLocalVolumes refused: '$($s.Name)' is placed cloud. Wiping local volumes never touches a cloud-placed dependency's data, and this stack has no way to distinguish which volumes are safe without your confirmation below."
            }
        }
        Write-Warn "This deletes all local Postgres/Cosmos/Blob data for the $ComposeProject stack. Cannot be undone."
        $confirm = Read-Host "Type the project name ($ComposeProject) to confirm"
        if ($confirm -ne $ComposeProject) { Write-Fail 'Confirmation did not match. Aborting - no volumes removed.' }
        Invoke-Compose -ComposeArgs @('down', '--remove-orphans')
        $vols = & docker volume ls -q --filter "label=com.docker.compose.project=$ComposeProject"
        if ($vols) { $vols | ForEach-Object { & docker volume rm $_ } }
        Write-Log 'Volumes removed.'
        return
    }
    if ($DeepClean) {
        Invoke-Compose -ComposeArgs @('down', '--rmi', 'local', '--remove-orphans')
    } else {
        Invoke-Compose -ComposeArgs @('down', '--remove-orphans')
    }
    Write-Log 'Stopped. State and built images are kept unless -DeepClean or -WipeLocalVolumes was passed.'
}

function Reset-Action {
    Stop-Action
    Start-Action
}

function Status-Action {
    Parse-ServicesYaml
    Invoke-Compose -ComposeArgs @('ps')
}

function Logs-Action {
    Parse-ServicesYaml
    if ($Service -ne '') {
        Invoke-Compose -ComposeArgs @('logs', '-f', '--tail', '200', $Service)
    } else {
        Invoke-Compose -ComposeArgs @('logs', '-f', '--tail', '200')
    }
}

function Doctor-Action {
    $failCount = 0
    Write-Host 'Docker:'
    $dockerOk = $false
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        & docker info *> $null
        if ($LASTEXITCODE -eq 0) { $dockerOk = $true }
    }
    if ($dockerOk) { Write-Host '  OK   Docker daemon reachable.' }
    else { Write-Host '  FAIL Docker not installed or daemon unreachable.'; $failCount++ }

    $verRaw = & docker compose version --short 2>$null
    if (-not $verRaw) { $verRaw = '0.0.0' }
    $verMajor = [int]($verRaw.Trim().Split('.')[0])
    $verLabel = 'FAIL (need >= 2.24)'
    if ($verMajor -ge 2) { $verLabel = 'OK' }
    Write-Host "  Compose version: $verRaw $verLabel"

    Parse-ServicesYaml

    Write-Host 'Env file:'
    if (Test-Path $EnvFile) {
        Write-Host "  OK   $EnvFile exists."
        foreach ($s in $script:Svc) {
            if ($s.Placement -ne 'cloud') { continue }
            $val = Get-EnvVal -Key $s.Env
            if ([string]::IsNullOrWhiteSpace($val)) {
                if ($s.Required -eq 'false') {
                    Write-Host "  WARN $($s.Env) empty ($($s.Name), optional)."
                } else {
                    Write-Host "  FAIL $($s.Env) empty ($($s.Name)) - $EnvFile"; $failCount++
                }
            } else {
                Write-Host "  OK   $($s.Env) set ($($s.Name))."
            }
        }
    } else {
        Write-Host "  FAIL $EnvFile missing - copy .env.preview.example."; $failCount++
    }

    Write-Host 'Ports:'
    foreach ($p in $PublishedPorts) {
        if (Test-PortBusy -Port $p) { Write-Host "  WARN port $p in use" }
        else { Write-Host "  OK   port $p free" }
    }

    Write-Host 'Placement table:'
    foreach ($s in $script:Svc) {
        Write-Host ("  {0,-14} {1,-7} {2}" -f $s.Name, $s.Placement, $s.Env)
    }

    Write-Host 'Workload status:'
    $psExit = Invoke-ComposeNoFail -ComposeArgs @('ps')
    if ($psExit -ne 0) { Write-Host '  (stack not running)' }

    if ($failCount -gt 0) { Write-Host "doctor: $failCount FAIL(s)."; exit 1 }
    Write-Host 'doctor: all checks OK.'
}

function Show-Banner {
    $branch = 'unknown'
    try {
        Push-Location $RepoRoot
        $branch = (& git rev-parse --abbrev-ref HEAD 2>$null)
        if (-not $branch) { $branch = 'unknown' }
    } finally {
        Pop-Location
    }
    Write-Host ''
    Write-Host '────────────────────────────────────────────────────────────────────────────'
    Write-Host "Cortexa preview stack is up (branch: $branch)"
    Write-Host ''
    Write-Host '  Frontend             http://localhost:5070'
    Write-Host '  api-gateway          http://localhost:5080'
    Write-Host '  identity             http://localhost:5081'
    Write-Host '  job-orchestrator     http://localhost:5082'
    Write-Host '  model-router         http://localhost:5083'
    Write-Host '  evidence             http://localhost:5084'
    Write-Host '  extraction           http://localhost:5085'
    Write-Host '  harvesting           http://localhost:5086'
    Write-Host '  ingestion            http://localhost:5087'
    Write-Host '  scoring              http://localhost:5088'
    Write-Host '  seeding              http://localhost:5089'
    Write-Host '  vector-router        http://localhost:5090'
    Write-Host '  Cosmos Data Explorer http://localhost:5100'
    Write-Host ''
    Write-Host "Code hot-reloads on save. Run 'preview.ps1 update' after changing a Dockerfile,"
    Write-Host "lockfile, or dependency version - hot reload alone won't pick those up."
    Write-Host ''
    Write-Host 'On a remote or tunnelled host, replace localhost above with the forwarded URL.'
    Write-Host '────────────────────────────────────────────────────────────────────────────'
}

# ─────────────────────────────────────────────────────────────── main ──
if (-not $Action) {
    Write-Fail 'Usage: preview.ps1 {start|update|seed|stop|reset|status|logs|doctor} [options]'
}

switch ($Action) {
    'start'  { Start-Action }
    'update' { Update-Action }
    'seed'   { Parse-ServicesYaml; Export-LocalEnv; Seed-Action }
    'stop'   { Stop-Action }
    'reset'  { Reset-Action }
    'status' { Status-Action }
    'logs'   { Logs-Action }
    'doctor' { Doctor-Action }
}
