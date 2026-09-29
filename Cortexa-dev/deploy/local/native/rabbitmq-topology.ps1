#Requires -Version 7
<#
.SYNOPSIS
    Declares the Cortexa messaging topology on a local RabbitMQ broker.

.DESCRIPTION
    Reads deploy/local/servicebus/config.json (the Service Bus topology mirrored from
    deploy/modules/service-bus/main.tf) and declares the RabbitMQ equivalent through the
    management HTTP API. Every service running with MESSAGING_BACKEND=rabbitmq (Python)
    or Messaging:Backend=rabbitmq (job-orchestrator) expects exactly these names:

      topic                -> durable fanout exchange "{topic}"
      subscription         -> quorum queue "{topic}.{subscription}", bound to the exchange,
                              single active consumer (per-queue ordering in place of sessions),
                              x-delivery-limit = MaxDeliveryCount, dead-lettering to its .dlq
      dead-letter sub-queue -> classic durable queue "{topic}.{subscription}.dlq"

    Safe to re-run: declaring an entity that already exists with the same arguments is a no-op.
    Requires the management plugin: rabbitmq-plugins enable rabbitmq_management

.EXAMPLE
    $env:RABBITMQ_USER = 'guest'; $env:RABBITMQ_PASSWORD = '<password>'
    ./rabbitmq-topology.ps1
#>
[CmdletBinding()]
param(
    [string]$ManagementUrl = ($env:RABBITMQ_MANAGEMENT_URL ?? 'http://127.0.0.1:15672'),
    [string]$VirtualHost = ($env:RABBITMQ_VHOST ?? '/'),
    [string]$User = $env:RABBITMQ_USER,
    [string]$Password = $env:RABBITMQ_PASSWORD,
    [string]$ConfigPath = (Join-Path $PSScriptRoot '..' 'servicebus' 'config.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DeadLetterSuffix = '.dlq'

function Get-ManagementCredential {
    if ([string]::IsNullOrWhiteSpace($User) -or [string]::IsNullOrWhiteSpace($Password)) {
        throw 'Set RABBITMQ_USER and RABBITMQ_PASSWORD (or pass -User / -Password) for the RabbitMQ management API.'
    }
    $secure = ConvertTo-SecureString $Password -AsPlainText -Force
    return [pscredential]::new($User, $secure)
}

function Invoke-Management {
    param([string]$Method, [string]$Path, [hashtable]$Body)
    $uri = '{0}/api/{1}' -f $ManagementUrl.TrimEnd('/'), $Path
    $json = $Body | ConvertTo-Json -Depth 5 -Compress
    Invoke-RestMethod -Method $Method -Uri $uri -Credential $script:Credential -Authentication Basic `
        -AllowUnencryptedAuthentication -ContentType 'application/json' -Body $json | Out-Null
}

function Get-EncodedName([string]$Name) { [uri]::EscapeDataString($Name) }

function Set-Exchange([string]$Name) {
    Invoke-Management -Method Put -Path "exchanges/$script:VHost/$(Get-EncodedName $Name)" `
        -Body @{ type = 'fanout'; durable = $true; auto_delete = $false }
}

function Set-DeadLetterQueue([string]$Name) {
    Invoke-Management -Method Put -Path "queues/$script:VHost/$(Get-EncodedName $Name)" `
        -Body @{ durable = $true; auto_delete = $false; arguments = @{ 'x-queue-type' = 'classic' } }
}

function Set-SubscriptionQueue([string]$Name, [int]$MaxDeliveryCount) {
    $arguments = @{
        'x-queue-type'              = 'quorum'
        'x-single-active-consumer'  = $true
        'x-delivery-limit'          = $MaxDeliveryCount
        'x-dead-letter-exchange'    = ''
        'x-dead-letter-routing-key' = $Name + $DeadLetterSuffix
    }
    Invoke-Management -Method Put -Path "queues/$script:VHost/$(Get-EncodedName $Name)" `
        -Body @{ durable = $true; auto_delete = $false; arguments = $arguments }
}

function Set-Binding([string]$Exchange, [string]$Queue) {
    Invoke-Management -Method Post -Path "bindings/$script:VHost/e/$(Get-EncodedName $Exchange)/q/$(Get-EncodedName $Queue)" `
        -Body @{ routing_key = '' }
}

function Set-Subscription([string]$Topic, $Subscription) {
    $queue = '{0}.{1}' -f $Topic, $Subscription.Name
    Set-DeadLetterQueue ($queue + $DeadLetterSuffix)
    Set-SubscriptionQueue $queue ([int]$Subscription.Properties.MaxDeliveryCount)
    Set-Binding $Topic $queue
    Write-Host "  queue $queue (+ $DeadLetterSuffix)"
}

function Get-Topics {
    $config = Get-Content -Raw -Path $ConfigPath | ConvertFrom-Json
    return $config.UserConfig.Namespaces | ForEach-Object { $_.Topics }
}

$script:Credential = Get-ManagementCredential
$script:VHost = Get-EncodedName $VirtualHost

foreach ($topic in Get-Topics) {
    Set-Exchange $topic.Name
    Write-Host "exchange $($topic.Name)"
    foreach ($subscription in $topic.Subscriptions) {
        Set-Subscription $topic.Name $subscription
    }
}

Write-Host 'RabbitMQ topology is in place.'
