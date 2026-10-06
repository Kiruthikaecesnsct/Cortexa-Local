[CmdletBinding()]
param(
    [switch]$ProvisionRabbitMqUser,
    [pscredential]$AdminCredential
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServerRoot = $PSScriptRoot
$EnvFile = Join-Path $ServerRoot '.env.collector-server'
$ExampleFile = Join-Path $ServerRoot '.env.collector-server.example'
$ApiProject = Join-Path $ServerRoot 'Collector.Server.Api'
$PasswordKey = 'Messaging__RabbitMq__Password'
$ConnectTimeoutMs = 3000
$PasswordByteCount = 32

function Import-EnvFile {
    if (-not (Test-Path -LiteralPath $EnvFile)) {
        throw "Missing $EnvFile. Copy $ExampleFile to $EnvFile and fill in the values."
    }

    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#') -or -not $trimmed.Contains('=')) {
            continue
        }

        $separator = $trimmed.IndexOf('=')
        $name = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}

function Get-EnvValue([string]$Name) {
    return [Environment]::GetEnvironmentVariable($Name, 'Process')
}

function Set-EnvFileValue([string]$Name, [string]$Value) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $replaced = $false

    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line.TrimStart().StartsWith("$Name=")) {
            $lines.Add("$Name=$Value")
            $replaced = $true
        }
        else {
            $lines.Add($line)
        }
    }

    if (-not $replaced) {
        $lines.Add("$Name=$Value")
    }

    [System.IO.File]::WriteAllLines($EnvFile, $lines, [System.Text.UTF8Encoding]::new($false))
}

function Get-ServerPort {
    $urls = Get-EnvValue 'ASPNETCORE_URLS'
    if ([string]::IsNullOrWhiteSpace($urls)) {
        throw 'ASPNETCORE_URLS is not set in the env file.'
    }

    $first = ($urls -split ';')[0]
    return ([System.Uri]$first.Replace('*', 'localhost').Replace('+', 'localhost')).Port
}

function Test-PortFree([int]$Port) {
    $listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
    return -not ($listeners | Where-Object { $_.Port -eq $Port })
}

function Test-TcpReachable([string]$HostName, [int]$Port) {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.ConnectAsync($HostName, $Port)
        return $connect.Wait($ConnectTimeoutMs) -and $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Assert-Reachable([string]$Label, [string]$HostName, [int]$Port) {
    if (-not (Test-TcpReachable $HostName $Port)) {
        throw "$Label is not reachable at ${HostName}:$Port. Start it first."
    }
}

function Test-Prerequisites {
    $port = Get-ServerPort
    if (-not (Test-PortFree $port)) {
        throw "Port $port is already in use. Stop the process using it or change ASPNETCORE_URLS."
    }

    $cosmos = [System.Uri](Get-EnvValue 'Cosmos__Endpoint')
    Assert-Reachable 'Cosmos DB' $cosmos.Host $cosmos.Port

    if ((Get-EnvValue 'Messaging__Provider') -eq 'RabbitMq') {
        Assert-Reachable 'RabbitMQ' (Get-EnvValue 'Messaging__RabbitMq__HostName') ([int](Get-EnvValue 'Messaging__RabbitMq__Port'))
    }
}

function New-RandomPassword {
    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes($PasswordByteCount)
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-BasicAuthHeader([pscredential]$Credential) {
    $pair = '{0}:{1}' -f $Credential.UserName, $Credential.GetNetworkCredential().Password
    $token = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($pair))
    return @{ Authorization = "Basic $token" }
}

function Invoke-Management([string]$Method, [string]$Url, [hashtable]$Headers, $Body) {
    $parameters = @{ Method = $Method; Uri = $Url; Headers = $Headers; SkipHttpErrorCheck = $true; StatusCodeVariable = 'status' }
    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Compress
        $parameters.ContentType = 'application/json'
    }

    Invoke-RestMethod @parameters | Out-Null
    return $status
}

function Assert-ManagementSuccess([int]$Status, [string]$Action) {
    if ($Status -lt 200 -or $Status -ge 300) {
        throw "RabbitMQ management call failed ($Action): HTTP $Status."
    }
}

function Initialize-RabbitMqUser {
    if ($null -eq $AdminCredential) {
        throw '-AdminCredential is required with -ProvisionRabbitMqUser.'
    }

    $managementUrl = (Get-EnvValue 'Messaging__RabbitMq__ManagementUrl').TrimEnd('/')
    $userName = Get-EnvValue 'Messaging__RabbitMq__UserName'
    $virtualHost = [System.Uri]::EscapeDataString((Get-EnvValue 'Messaging__RabbitMq__VirtualHost'))
    $topic = Get-EnvValue 'Messaging__Topics__IngestionCompleted'
    $headers = New-BasicAuthHeader $AdminCredential
    $userUrl = "$managementUrl/api/users/$([System.Uri]::EscapeDataString($userName))"

    $existing = Invoke-Management 'Get' $userUrl $headers $null
    $password = Get-EnvValue $PasswordKey
    $needsPassword = [string]::IsNullOrWhiteSpace($password)

    if ($existing -ne 200 -or $needsPassword) {
        $password = if ($needsPassword) { New-RandomPassword } else { $password }
        $created = Invoke-Management 'Put' $userUrl $headers @{ password = $password; tags = '' }
        Assert-ManagementSuccess $created 'create user'
        Set-EnvFileValue $PasswordKey $password
    }

    $permissions = @{ configure = ''; write = "^$([regex]::Escape($topic))$"; read = '' }
    $permissionUrl = "$managementUrl/api/permissions/$virtualHost/$([System.Uri]::EscapeDataString($userName))"
    Assert-ManagementSuccess (Invoke-Management 'Put' $permissionUrl $headers $permissions) 'set permissions'
    Write-Host "RabbitMQ user '$userName' is ready."
}

Import-EnvFile

if ($ProvisionRabbitMqUser) {
    Initialize-RabbitMqUser
    Import-EnvFile
}

Test-Prerequisites

dotnet run --project $ApiProject --no-launch-profile
