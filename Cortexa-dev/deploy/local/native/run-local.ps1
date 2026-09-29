#Requires -Version 7.4
<#
.SYNOPSIS
    Runs the whole Cortexa stack natively on Windows - no Docker.

.DESCRIPTION
    start  - checks the local dependencies, starts Azurite if needed, declares the RabbitMQ
             topology, then starts every service and the frontend as background processes
             on their own localhost ports and waits until each one listens.
    stop   - stops every process started by 'start' (whole process trees).
    status - shows which ports are listening.

    Dependencies expected to be running: RabbitMQ (5672, management 15672), PostgreSQL (5432),
    Cosmos DB emulator (8081). Azurite (10000) is started by this script.

    Settings and secrets come from deploy/local/native/.env.native (gitignored). Required:
    GEMINI_API_KEY, QDRANT_URL, QDRANT_API_KEY, POSTGRES_CONNECTION_STRING,
    RABBITMQ_ADMIN_USER, RABBITMQ_ADMIN_PASSWORD. JWT_SIGNING_KEY, INTERNAL_SHARED_SECRET and
    RABBITMQ_APP_PASSWORD are generated into that file on first start.
    Logs: deploy/local/native/.run/logs/<service>.log
#>
[CmdletBinding()]
param(
    [ValidateSet('start', 'stop', 'status')]
    [string]$Command = 'start'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$NativeDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $NativeDir '..' '..' '..')).Path
$EnvFile = Join-Path $NativeDir '.env.native'
$RunDir = Join-Path $NativeDir '.run'
$LogDir = Join-Path $RunDir 'logs'
$PidFile = Join-Path $RunDir 'pids.json'
$CaBundle = Join-Path $RepoRoot 'deploy' 'local' 'certs' 'ca-bundle.pem'

$LocalHost = '127.0.0.1'
$HealthTimeoutSeconds = 300
$RabbitMqAppUser = 'cortexa'
$Ports = [ordered]@{
    'frontend' = 5070; 'api-gateway' = 5080; 'identity' = 5081; 'job-orchestrator' = 5082
    'model-router' = 5083; 'evidence' = 5084; 'extraction' = 5085; 'harvesting' = 5086
    'ingestion' = 5087; 'scoring' = 5088; 'seeding' = 5089; 'vector-router' = 5090
}
$DependencyPorts = [ordered]@{ 'RabbitMQ' = 5672; 'PostgreSQL' = 5432; 'Cosmos DB emulator' = 8081 }
$AzuritePort = 10000

# Publicly documented emulator credentials (Cosmos DB emulator / Azurite), not secrets.
$CosmosUri = 'https://localhost:8081'
$CosmosEmulatorKey = 'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=='
$AzuriteConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://${LocalHost}:$AzuritePort/devstoreaccount1;"

# ───────────────────────────────────────────── helpers ──
function Update-SessionPath {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}

function Read-EnvFile {
    if (-not (Test-Path $EnvFile)) { throw "Missing $EnvFile." }
    $values = @{}
    foreach ($line in Get-Content $EnvFile) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
    }
    return $values
}

function New-RandomSecret([int]$Length) {
    $chars = [char[]](48..57 + 65..90 + 97..122)
    return -join (1..$Length | ForEach-Object { $chars[[Security.Cryptography.RandomNumberGenerator]::GetInt32($chars.Length)] })
}

function Add-GeneratedSecret([hashtable]$Config, [string]$Name, [int]$Length) {
    if ($Config.ContainsKey($Name) -and $Config[$Name]) { return }
    $Config[$Name] = New-RandomSecret $Length
    $content = Get-Content -Raw $EnvFile
    $separator = if ($content -and -not $content.EndsWith("`n")) { "`n" } else { '' }
    Add-Content $EnvFile "$separator$Name=$($Config[$Name])"
    Write-Host "Generated $Name into .env.native"
}

function Assert-ConfigKeys([hashtable]$Config) {
    $required = 'GEMINI_API_KEY', 'QDRANT_URL', 'QDRANT_API_KEY', 'POSTGRES_CONNECTION_STRING', 'RABBITMQ_ADMIN_USER', 'RABBITMQ_ADMIN_PASSWORD'
    $missing = $required | Where-Object { -not ($Config.ContainsKey($_) -and $Config[$_]) }
    if ($missing) { throw "Set these in ${EnvFile}: $($missing -join ', ')" }
}

function Test-Port([int]$Port) {
    $client = [Net.Sockets.TcpClient]::new()
    try { return $client.ConnectAsync($LocalHost, $Port).Wait(1000) -and $client.Connected }
    catch { return $false }
    finally { $client.Dispose() }
}

function Get-RabbitMqUrl([hashtable]$Config) {
    return "amqp://${RabbitMqAppUser}:$($Config['RABBITMQ_APP_PASSWORD'])@${LocalHost}:5672/"
}

# ─────────────────────────────────────────── dependencies ──
function Assert-Dependencies {
    foreach ($name in $DependencyPorts.Keys) {
        if (-not (Test-Port $DependencyPorts[$name])) { throw "$name is not listening on port $($DependencyPorts[$name]). Start it first." }
    }
}

function Start-Azurite {
    if (Test-Port $AzuritePort) { return }
    $data = Join-Path $RunDir 'azurite'
    New-Item -ItemType Directory -Force $data | Out-Null
    $arguments = @('--blobHost', $LocalHost, '--blobPort', $AzuritePort, '--location', $data, '--skipApiVersionCheck')
    Start-Tracked -Name 'azurite' -FilePath 'azurite-blob.cmd' -Arguments $arguments -WorkingDirectory $RunDir -Environment @{}
}

function Wait-Port([int]$Port, [string]$Name) {
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
    while (-not (Test-Port $Port)) {
        if ((Get-Date) -gt $deadline) { throw "$Name did not start listening on port $Port." }
        Start-Sleep -Seconds 1
    }
}

# Creates the Cosmos database/containers and the blob containers the services expect
# (both scripts are idempotent), using the ingestion service's venv for the Azure SDKs.
function Initialize-Storage {
    Wait-Port $AzuritePort 'Azurite'
    $environment = @{
        COSMOS_URI = $CosmosUri; COSMOS_KEY = $CosmosEmulatorKey; BLOB_CONNECTION_STRING = $AzuriteConnectionString
        SSL_CERT_FILE = $CaBundle; REQUESTS_CA_BUNDLE = $CaBundle
    }
    foreach ($script in 'cosmos_bootstrap.py', 'blob_bootstrap.py') {
        $process = Start-Process -FilePath 'uv' -ArgumentList @('run', 'python', (Join-Path $NativeDir $script)) `
            -WorkingDirectory (Join-Path $RepoRoot 'services' 'ingestion') -Environment $environment `
            -RedirectStandardOutput (Join-Path $LogDir "$script.log") -RedirectStandardError (Join-Path $LogDir "$script.err.log") `
            -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode) { throw "$script failed - see $(Join-Path $LogDir "$script.err.log")" }
    }
    Write-Host 'Cosmos database and blob containers are in place.'
}

function Invoke-RabbitMqAdmin([hashtable]$Config, [string]$Path, [hashtable]$Body) {
    $secure = ConvertTo-SecureString $Config['RABBITMQ_ADMIN_PASSWORD'] -AsPlainText -Force
    $credential = [pscredential]::new($Config['RABBITMQ_ADMIN_USER'], $secure)
    Invoke-RestMethod -Method Put -Uri "http://${LocalHost}:15672/api/$Path" -Credential $credential -Authentication Basic `
        -AllowUnencryptedAuthentication -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Compress) | Out-Null
}

function Initialize-RabbitMq([hashtable]$Config) {
    Invoke-RabbitMqAdmin $Config "users/$RabbitMqAppUser" @{ password = $Config['RABBITMQ_APP_PASSWORD']; tags = '' }
    Invoke-RabbitMqAdmin $Config "permissions/%2F/$RabbitMqAppUser" @{ configure = '.*'; write = '.*'; read = '.*' }
    & (Join-Path $NativeDir 'rabbitmq-topology.ps1') -User $Config['RABBITMQ_ADMIN_USER'] -Password $Config['RABBITMQ_ADMIN_PASSWORD'] 6>$null | Out-Null
    Write-Host 'RabbitMQ user and topology are in place.'
}

# ──────────────────────────────────────────── service env ──
function Get-Url([string]$Service) { "http://${LocalHost}:$($Ports[$Service])" }

function Get-PythonCommonEnv([hashtable]$Config) {
    return @{
        SENTRY_DSN = $Config['SENTRY_DSN'] ?? ''; KEYVAULT_URI = ''; LOCAL_DEV = 'true'; PYTHONUNBUFFERED = '1'
        COSMOS_URI = $CosmosUri; COSMOS_KEY = $CosmosEmulatorKey
        MESSAGING_BACKEND = 'rabbitmq'; RABBITMQ_URL = (Get-RabbitMqUrl $Config)
        # Required by the scoring/harvesting settings validators; unused with the rabbitmq backend.
        SERVICEBUS_NAMESPACE_FQDN = 'rabbitmq.local'
        SSL_CERT_FILE = $CaBundle; REQUESTS_CA_BUNDLE = $CaBundle
    }
}

function Get-DotnetCommonEnv([hashtable]$Config, [string]$Service) {
    return @{
        ASPNETCORE_URLS = (Get-Url $Service); KeyVault__Uri = ''; SENTRY_DSN = $Config['SENTRY_DSN'] ?? ''
        Jwt__SigningKey = $Config['JWT_SIGNING_KEY']
    }
}

function Get-GatewayEnv([hashtable]$Config) {
    $envVars = Get-DotnetCommonEnv $Config 'api-gateway'
    $envVars['UserStatus__IdentityInternalBaseUrl'] = Get-Url 'identity'
    $envVars['UserStatus__InternalKey'] = $Config['INTERNAL_SHARED_SECRET']
    foreach ($cluster in $Ports.Keys | Where-Object { $_ -notin 'frontend', 'api-gateway' }) {
        $envVars["ReverseProxy__Clusters__${cluster}__Destinations__d1__Address"] = Get-Url $cluster
    }
    return $envVars
}

function Get-IdentityEnv([hashtable]$Config) {
    $envVars = Get-DotnetCommonEnv $Config 'identity'
    $envVars['ConnectionStrings__Postgres'] = $Config['POSTGRES_CONNECTION_STRING']
    $envVars['Internal__SharedSecret'] = $Config['INTERNAL_SHARED_SECRET']
    return $envVars
}

function Get-OrchestratorEnv([hashtable]$Config) {
    $envVars = Get-DotnetCommonEnv $Config 'job-orchestrator'
    $envVars['Cosmos__Uri'] = $CosmosUri
    $envVars['Cosmos__Key'] = $CosmosEmulatorKey
    $envVars['Blob__ConnectionString'] = $AzuriteConnectionString
    $envVars['Messaging__Backend'] = 'rabbitmq'
    $envVars['RabbitMq__Uri'] = Get-RabbitMqUrl $Config
    $envVars['ModelRouter__BaseUrl'] = Get-Url 'model-router'
    $envVars['VectorRouter__Url'] = Get-Url 'vector-router'
    return $envVars
}

function Get-ModelRouterEnv([hashtable]$Config) {
    $envVars = Get-DotnetCommonEnv $Config 'model-router'
    foreach ($key in 'GEMINI_API_KEY', 'MODEL_ROUTER_FOUNDRY_API_KEY', 'MODEL_ROUTER_ANTHROPIC_API_KEY') {
        if ($Config.ContainsKey($key)) { $envVars[$key] = $Config[$key] }
    }
    return $envVars
}

function Get-PythonServiceEnv([hashtable]$Config, [string]$Service) {
    $envVars = Get-PythonCommonEnv $Config
    $extra = @{
        'evidence'   = @{ MODEL_ROUTER_URL = (Get-Url 'model-router'); VECTOR_ROUTER_URL = (Get-Url 'vector-router') }
        'extraction' = @{ MODEL_ROUTER_URL = (Get-Url 'model-router') }
        'ingestion'  = @{ BLOB_CONNECTION_STRING = $AzuriteConnectionString }
        'scoring'    = @{ MODEL_ROUTER_URL = (Get-Url 'model-router') }
        'seeding'    = @{ MODEL_ROUTER_URL = (Get-Url 'model-router'); VECTOR_ROUTER_URL = (Get-Url 'vector-router'); EVIDENCE_URL = (Get-Url 'evidence') }
    }
    if ($extra.ContainsKey($Service)) { $extra[$Service].GetEnumerator() | ForEach-Object { $envVars[$_.Key] = $_.Value } }
    return $envVars
}

function Get-VectorRouterEnv([hashtable]$Config) {
    return @{
        SENTRY_DSN = $Config['SENTRY_DSN'] ?? ''; PYTHONUNBUFFERED = '1'; VECTOR_BACKEND = 'qdrant'
        QDRANT_URL = $Config['QDRANT_URL']; QDRANT_API_KEY = $Config['QDRANT_API_KEY']
    }
}

# ──────────────────────────────────────── service launch ──
function Get-ServiceDefinitions([hashtable]$Config) {
    $dotnet = @{
        'api-gateway'      = @{ Project = 'src/Api/Cortexa.ApiGateway.Api.csproj'; Env = (Get-GatewayEnv $Config) }
        'identity'         = @{ Project = 'src/Api/Cortexa.Identity.Api.csproj'; Env = (Get-IdentityEnv $Config) }
        'job-orchestrator' = @{ Project = 'src/Api/Cortexa.JobOrchestrator.Api.csproj'; Env = (Get-OrchestratorEnv $Config) }
        'model-router'     = @{ Project = 'src/Api/Cortexa.ModelRouter.Api.csproj'; Env = (Get-ModelRouterEnv $Config) }
    }
    $definitions = foreach ($name in $dotnet.Keys) { New-DotnetDefinition $name $dotnet[$name] }
    $definitions += foreach ($name in 'evidence', 'extraction', 'harvesting', 'ingestion', 'scoring', 'seeding') {
        New-PythonDefinition $name (Get-PythonServiceEnv $Config $name)
    }
    $definitions += New-PythonDefinition 'vector-router' (Get-VectorRouterEnv $Config)
    $definitions += New-FrontendDefinition
    return $definitions
}

function New-DotnetDefinition([string]$Name, [hashtable]$Spec) {
    [pscustomobject]@{
        Name = $Name; FilePath = 'dotnet'; WorkingDirectory = (Join-Path $RepoRoot 'services' $Name)
        Arguments = @('run', '--project', $Spec.Project, '--no-launch-profile'); Environment = $Spec.Env
    }
}

function New-PythonDefinition([string]$Name, [hashtable]$Environment) {
    $module = ($Name -replace '-', '_') + '.main:app'
    [pscustomobject]@{
        Name = $Name; FilePath = 'uv'; WorkingDirectory = (Join-Path $RepoRoot 'services' $Name)
        Arguments = @('run', 'uvicorn', $module, '--host', $LocalHost, '--port', $Ports[$Name]); Environment = $Environment
    }
}

function New-FrontendDefinition {
    [pscustomobject]@{
        Name = 'frontend'; FilePath = 'pnpm.cmd'; WorkingDirectory = (Join-Path $RepoRoot 'frontend')
        Arguments = @('exec', 'vite', '--host', $LocalHost, '--port', $Ports['frontend'], '--strictPort')
        # Relative base URL: the Vite dev server proxies /api to the local api-gateway.
        Environment = @{ VITE_DEV_PROXY_TARGET = (Get-Url 'api-gateway'); VITE_API_BASE_URL = '/api' }
    }
}

function Start-Tracked {
    param([string]$Name, [string]$FilePath, [object[]]$Arguments, [string]$WorkingDirectory, [hashtable]$Environment)
    $log = Join-Path $LogDir "$Name.log"
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory `
        -Environment $Environment -RedirectStandardOutput $log -RedirectStandardError (Join-Path $LogDir "$Name.err.log") `
        -WindowStyle Hidden -PassThru
    $script:Started[$Name] = $process.Id
}

function Install-FrontendPackages {
    $frontend = Join-Path $RepoRoot 'frontend'
    if (Test-Path (Join-Path $frontend 'node_modules')) { return }
    Write-Host 'Installing frontend packages (first run)...'
    Push-Location $frontend
    try { & pnpm.cmd install --frozen-lockfile; if ($LASTEXITCODE) { throw 'pnpm install failed.' } }
    finally { Pop-Location }
}

function Wait-ServicesListening {
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
    $pending = [Collections.Generic.List[string]]::new([string[]]$Ports.Keys)
    while ($pending.Count -and (Get-Date) -lt $deadline) {
        foreach ($name in @($pending)) {
            if (Test-Port $Ports[$name]) { Write-Host "  up   $name  $(Get-Url $name)"; [void]$pending.Remove($name) }
        }
        if ($pending.Count) { Start-Sleep -Seconds 3 }
    }
    foreach ($name in $pending) { Write-Warning "$name did not start listening - see $(Join-Path $LogDir "$name.err.log")" }
}

# ──────────────────────────────────────────────── commands ──
function Invoke-Start {
    Update-SessionPath
    if (Test-Path $PidFile) { throw "Stack looks already started ($PidFile exists). Run 'stop' first." }
    New-Item -ItemType Directory -Force $LogDir | Out-Null
    $config = Read-EnvFile
    Add-GeneratedSecret $config 'JWT_SIGNING_KEY' 64
    Add-GeneratedSecret $config 'INTERNAL_SHARED_SECRET' 48
    Add-GeneratedSecret $config 'RABBITMQ_APP_PASSWORD' 32
    Assert-ConfigKeys $config
    Assert-Dependencies
    Initialize-RabbitMq $config
    Install-FrontendPackages

    $script:Started = [ordered]@{}
    try {
        Start-Azurite
        Initialize-Storage
        foreach ($service in Get-ServiceDefinitions $config) {
            Start-Tracked -Name $service.Name -FilePath $service.FilePath -Arguments $service.Arguments `
                -WorkingDirectory $service.WorkingDirectory -Environment $service.Environment
        }
    }
    finally { $script:Started | ConvertTo-Json | Set-Content $PidFile }

    Write-Host "Started $($script:Started.Count) processes. Waiting for ports (first run builds everything)..."
    Wait-ServicesListening
    Write-Host "App: $(Get-Url 'frontend')   Logs: $LogDir"
}

function Invoke-Stop {
    if (-not (Test-Path $PidFile)) { Write-Host 'Nothing to stop.'; return }
    $pids = Get-Content -Raw $PidFile | ConvertFrom-Json
    foreach ($entry in $pids.PSObject.Properties) {
        & taskkill.exe /PID $entry.Value /T /F 2>&1 | Out-Null
        Write-Host "  stopped $($entry.Name)"
    }
    Remove-Item $PidFile
}

function Invoke-Status {
    foreach ($name in $Ports.Keys) {
        $state = if (Test-Port $Ports[$name]) { 'up  ' } else { 'down' }
        Write-Host "  $state $name  $(Get-Url $name)"
    }
}

switch ($Command) {
    'start' { Invoke-Start }
    'stop' { Invoke-Stop }
    'status' { Invoke-Status }
}
