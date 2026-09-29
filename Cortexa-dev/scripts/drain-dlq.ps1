#Requires -Version 7.0
[CmdletBinding()]
param (
    [string] $ResourceGroup = "cortexa-dev-rg",
    [string] $Namespace     = "cortexa-dev-bus",
    [switch] $Purge,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:BaseUrl    = "https://$Namespace.servicebus.windows.net"
$script:ApiVersion = "api-version=2017-04"

# Every consumer subscription that can dead-letter must be covered — a hardcoded subset
# silently hides a real backlog (BUG156: seeding-requested was once omitted). This spans
# the *-requested stage consumers plus the US115 Deep Seeding embedding fan-out
# (asset-embedding-requested → seeding, asset-embedding-completed → orchestrator),
# the US116 Deep Seeding digest fan-out
# (digest-requested → seeding, digest-completed → orchestrator),
# the US117 Deep Seeding landscape fan-out
# (landscape-requested → seeding, landscape-completed → orchestrator),
# and the US119 Deep Seeding grounded ideation loop
# (ideation-completed → orchestrator, seeding-report-requested → seeding).
$script:DlqTargets = @(
    [PSCustomObject]@{ Topic = "ingestion-requested";   Subscription = "ingestion"   }
    [PSCustomObject]@{ Topic = "extraction-requested";  Subscription = "extraction"  }
    [PSCustomObject]@{ Topic = "evidence-requested";    Subscription = "evidence"    }
    [PSCustomObject]@{ Topic = "scoring-requested";     Subscription = "scoring"     }
    [PSCustomObject]@{ Topic = "harvesting-requested";  Subscription = "harvesting"  }
    [PSCustomObject]@{ Topic = "seeding-requested";     Subscription = "seeding"     }
    [PSCustomObject]@{ Topic = "asset-embedding-requested"; Subscription = "seeding"       }
    [PSCustomObject]@{ Topic = "asset-embedding-completed"; Subscription = "orchestrator"  }
    [PSCustomObject]@{ Topic = "digest-requested";      Subscription = "seeding"       }
    [PSCustomObject]@{ Topic = "digest-completed";      Subscription = "orchestrator"  }
    [PSCustomObject]@{ Topic = "landscape-requested";   Subscription = "seeding"       }
    [PSCustomObject]@{ Topic = "landscape-completed";   Subscription = "orchestrator"  }
    [PSCustomObject]@{ Topic = "ideation-completed";    Subscription = "orchestrator"  }
    [PSCustomObject]@{ Topic = "seeding-report-requested"; Subscription = "seeding"    }
)

# Set when a data-plane call fails for a reason other than an empty queue, so the
# script can exit non-zero instead of reporting a false "all clear".
$script:HadError = $false

function Get-ServiceBusToken {
    $token = az account get-access-token --resource "https://servicebus.azure.net/" `
        --query accessToken -o tsv 2>$null
    if (-not $token) {
        throw "Failed to acquire Service Bus access token. Verify az login is current and the account has data-plane access."
    }
    return $token
}

function Get-DlqCount {
    param([string] $ResourceGroup, [string] $Namespace, [string] $Topic, [string] $Subscription)
    $raw = az servicebus topic subscription show `
        --resource-group $ResourceGroup `
        --namespace-name $Namespace `
        --topic-name $Topic `
        --name $Subscription `
        --query "countDetails.deadLetterMessageCount" -o tsv 2>$null
    $cleaned = ($raw ?? "0") -replace '\s', ''
    return [int]($cleaned -match '^\d+$' ? $cleaned : "0")
}

function Get-DlqEntityPath {
    param([string] $Topic, [string] $Subscription)
    return "$Topic/subscriptions/$Subscription/`$deadletterqueue"
}

function Invoke-PeekLock {
    param([string] $EntityPath, [string] $Token)
    $url = "$script:BaseUrl/$EntityPath/messages/head?timeout=30&$script:ApiVersion"
    # Invoke-WebRequest auto-adds Content-Length: 0 on an empty POST body, unlike curl which requires an explicit header
    return Invoke-WebRequest -Method POST -Uri $url `
        -Headers @{ Authorization = "Bearer $Token" } `
        -SkipHttpErrorCheck -ErrorAction SilentlyContinue
}

function Invoke-ReceiveAndDelete {
    param([string] $EntityPath, [string] $Token)
    $url = "$script:BaseUrl/$EntityPath/messages/head?$script:ApiVersion"
    return Invoke-WebRequest -Method DELETE -Uri $url `
        -Headers @{ Authorization = "Bearer $Token" } `
        -SkipHttpErrorCheck -ErrorAction SilentlyContinue
}

function Invoke-AbandonMessage {
    param([string] $EntityPath, [string] $Token, [long] $SequenceNumber, [string] $LockToken)
    $url = "$script:BaseUrl/$EntityPath/messages/$SequenceNumber/$LockToken`?$script:ApiVersion"
    # Invoke-WebRequest auto-adds Content-Length: 0 on an empty PUT body, unlike curl which requires an explicit header
    return Invoke-WebRequest -Method PUT -Uri $url `
        -Headers @{ Authorization = "Bearer $Token" } `
        -SkipHttpErrorCheck -ErrorAction SilentlyContinue
}

function Read-BrokerProperty {
    param([object] $Response, [string] $PropertyName)
    $raw = $Response.Headers["BrokerProperties"]
    if (-not $raw) { return $null }
    $json = if ($raw -is [System.Collections.IEnumerable] -and $raw -isnot [string]) {
        @($raw)[0]
    } else {
        [string]$raw
    }
    try {
        $bp = $json | ConvertFrom-Json
        return $bp.$PropertyName
    } catch {
        return $null
    }
}

function Read-ResponseHeader {
    param([object] $Response, [string] $HeaderName)
    $raw = $Response.Headers[$HeaderName]
    if (-not $raw) { return $null }
    $value = if ($raw -is [System.Collections.IEnumerable] -and $raw -isnot [string]) {
        @($raw)[0]
    } else {
        [string]$raw
    }
    return $value.Trim('"')
}

function Invoke-ClassifyAndRelease {
    param([string] $EntityPath, [string] $Token, [object] $Response, [System.Collections.IDictionary] $Reasons)
    # The dead-letter reason is a standalone HTTP response header, not a field inside
    # the BrokerProperties JSON, so it needs its own header reader.
    $reason       = Read-ResponseHeader -Response $Response -HeaderName "DeadLetterReason"
    $key          = if ($reason) { $reason } else { "(none)" }
    $Reasons[$key] = ($Reasons[$key] ?? 0) + 1

    $lockToken = Read-BrokerProperty -Response $Response -PropertyName "LockToken"
    $seqNum    = Read-BrokerProperty -Response $Response -PropertyName "SequenceNumber"
    if (-not ($lockToken -and $seqNum)) { return }
    try {
        $resp = Invoke-AbandonMessage -EntityPath $EntityPath -Token $Token `
                    -SequenceNumber $seqNum -LockToken $lockToken
        $code = if ($resp) { $resp.StatusCode } else { "no response" }
        if (-not $resp -or $resp.StatusCode -ne 200) {
            Write-Warning "Failed to abandon message (seq=$seqNum, HTTP $code)"
        }
    } catch {
        Write-Warning "Failed to abandon message (seq=$seqNum): $_"
    }
}

function Get-DlqClassification {
    param([string] $ResourceGroup, [string] $Namespace, [string] $Token, [object] $Target)
    $entityPath = Get-DlqEntityPath -Topic $Target.Topic -Subscription $Target.Subscription
    $dlqCount   = Get-DlqCount -ResourceGroup $ResourceGroup -Namespace $Namespace `
                               -Topic $Target.Topic -Subscription $Target.Subscription
    $reasons = [ordered]@{}
    $peeked  = 0

    while ($peeked -lt $dlqCount) {
        $resp = Invoke-PeekLock -EntityPath $entityPath -Token $Token
        # Service Bus peek-lock returns 201 Created (not 200) on success.
        if ($resp -and ($resp.StatusCode -eq 200 -or $resp.StatusCode -eq 201)) {
            Invoke-ClassifyAndRelease -EntityPath $entityPath -Token $Token -Response $resp -Reasons $reasons
            $peeked++
            continue
        }
        # 204 = queue drained early (count is a snapshot, so a race is benign).
        # Any other status — most commonly 401/403 for a missing data-plane role —
        # must not be swallowed: it would report a false "0 peeked / all clear".
        $code = if ($resp) { $resp.StatusCode } else { "<none>" }
        if ($code -ne 204) {
            Write-Warning "Peek on $entityPath failed with HTTP $code (expected data-plane access; see DLQ_RUNBOOK.md prerequisites)"
            $script:HadError = $true
        }
        break
    }

    return [PSCustomObject]@{
        Topic        = $Target.Topic
        Subscription = $Target.Subscription
        TotalCount   = $dlqCount
        Peeked       = $peeked
        Reasons      = $reasons
    }
}

function Write-ClassificationTable {
    param([object[]] $Classifications)
    $divider = "=" * 72
    Write-Host ""
    Write-Host "Dead-Letter Queue Classification" -ForegroundColor Cyan
    Write-Host $divider -ForegroundColor Cyan

    foreach ($c in $Classifications) {
        Write-Host ""
        Write-Host ("  {0,-18} {1}" -f "Topic:", $c.Topic) -ForegroundColor Yellow
        Write-Host ("  {0,-18} {1}" -f "Subscription:", $c.Subscription)
        Write-Host ("  {0,-18} {1}  (peeked: {2})" -f "DLQ count:", $c.TotalCount, $c.Peeked)
        Write-Host "  Reasons:"

        if ($c.Reasons.Count -eq 0) {
            Write-Host "    (no messages found)" -ForegroundColor Gray
            continue
        }

        $sorted = $c.Reasons.GetEnumerator() | Sort-Object Value -Descending
        foreach ($entry in $sorted) {
            Write-Host ("    {0,-50} {1,5}" -f $entry.Key, $entry.Value)
        }
    }

    Write-Host ""
    Write-Host $divider -ForegroundColor Cyan
    Write-Host ""
}

function Invoke-PurgeDlqTarget {
    param([string] $Namespace, [string] $Token, [object] $Target)
    $entityPath = Get-DlqEntityPath -Topic $Target.Topic -Subscription $Target.Subscription
    $deleted    = 0

    Write-Host "  Purging $($Target.Topic)/$($Target.Subscription) ..." -NoNewline

    while ($true) {
        $resp = Invoke-ReceiveAndDelete -EntityPath $entityPath -Token $Token
        # 204 = queue empty (normal stop).
        if ($resp -and $resp.StatusCode -eq 204) { break }
        # Anything other than 200 (e.g. 401/403 for a missing data-plane role) is an
        # error that must be surfaced, not reported as success.
        if (-not $resp -or $resp.StatusCode -ne 200) {
            $code = if ($resp) { $resp.StatusCode } else { "<none>" }
            Write-Host " [HTTP $code — aborting target]" -ForegroundColor Red
            $script:HadError = $true
            break
        }
        $deleted++
        if ($deleted % 25 -eq 0) { Write-Host "." -NoNewline }
    }

    Write-Host " deleted: $deleted" -ForegroundColor Green
    return $deleted
}

function Confirm-Purge {
    Write-Host ""
    Write-Host "WARNING: This will permanently delete all dead-letter messages." -ForegroundColor Red
    Write-Host "Namespace: $Namespace" -ForegroundColor Red
    foreach ($t in $script:DlqTargets) {
        Write-Host "  $($t.Topic)/$($t.Subscription)" -ForegroundColor Red
    }
    Write-Host ""
    $answer = Read-Host "Type YES to confirm"
    return $answer -ceq "YES"
}

# ── Entry point ───────────────────────────────────────────────────────────────

Write-Host "Acquiring Service Bus token ..." -ForegroundColor Gray
$token = Get-ServiceBusToken

Write-Host "Classifying DLQs in namespace: $Namespace" -ForegroundColor Gray
$classifications = foreach ($target in $script:DlqTargets) {
    Write-Host "  Peeking $($target.Topic)/$($target.Subscription) ..." -ForegroundColor Gray
    Get-DlqClassification -ResourceGroup $ResourceGroup -Namespace $Namespace `
                          -Token $token -Target $target
}

Write-ClassificationTable -Classifications $classifications

if (-not $Purge) {
    if ($script:HadError) {
        Write-Error "One or more queues could not be read — classification above is INCOMPLETE. Grant 'Azure Service Bus Data Receiver' on the namespace (see DLQ_RUNBOOK.md) and retry."
        exit 1
    }
    Write-Host "Run with -Purge to drain all dead-letter messages." -ForegroundColor Gray
    exit 0
}

if (-not $Force -and -not (Confirm-Purge)) {
    Write-Host "Purge cancelled." -ForegroundColor Yellow
    exit 0
}

Write-Host "Purging dead-letter queues ..." -ForegroundColor Yellow
$totalDeleted = 0
foreach ($target in $script:DlqTargets) {
    $totalDeleted += Invoke-PurgeDlqTarget -Namespace $Namespace -Token $token -Target $target
}

Write-Host ""
Write-Host "Purge complete. Total messages deleted: $totalDeleted" -ForegroundColor Green

if ($script:HadError) {
    Write-Error "One or more targets aborted before draining — purge is INCOMPLETE. Grant 'Azure Service Bus Data Receiver' on the namespace (see DLQ_RUNBOOK.md) and retry."
    exit 1
}
