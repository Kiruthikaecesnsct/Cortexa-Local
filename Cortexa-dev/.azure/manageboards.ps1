<#
.SYNOPSIS
    Push a User Story or Bug JSON to Azure DevOps Boards.

.DESCRIPTION
    Reads work item data from a JSON file (or every JSON file in a folder) and
    creates the corresponding User Story (with child Tasks) or Bug in Azure
    DevOps via the REST API. Config (org, project, PAT, assignee) is read from
    .azure/config.json; --Project and --Assignee flags override config values.

    Every push is recorded in boards_tracker.csv (next to this script) mapping
    the work-item key (e.g. US001, BUG012) to the created Azure DevOps ID. On
    re-run, items already present in the tracker are skipped unless -Force is
    passed.

.EXAMPLE
    .\manageboards.ps1 --json .notes/Agile/Sprint1/US001_AddAuth.json
    .\manageboards.ps1 --userstory --json path/to/us.json --project WiseMaestro
    .\manageboards.ps1 --bug --json path/to/bug.json --assignee dev@example.com
    .\manageboards.ps1 --json .notes/Agile/Sprint1 -Force
#>

[CmdletBinding()]
param(
    [Parameter()] [switch]$userstory,
    [Parameter()] [switch]$bug,
    [Parameter(Mandatory = $true)] [string]$json,
    [Parameter()] [string]$project  = "",
    [Parameter()] [string]$assignee = "",
    [Parameter()] [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── helpers ───────────────────────────────────────────────────────────────────

function Die([string]$msg) { Write-Host "❌ ERROR: $msg" -ForegroundColor Red; exit 1 }
function Info([string]$msg) { Write-Host "   $msg" -ForegroundColor White }
function Ok([string]$msg) { Write-Host "✅ $msg" -ForegroundColor Green }

function Get-AuthHeader([string]$pat) {
    $bytes = [Text.Encoding]::ASCII.GetBytes(":$pat")
    $b64 = [Convert]::ToBase64String($bytes)
    return @{
        Authorization  = "Basic $b64"
        "Content-Type" = "application/json-patch+json"
    }
}

function Invoke-AzureDevOpsPost([string]$url, [hashtable]$headers, [string]$body) {
    try {
        return Invoke-RestMethod -Uri $url -Method POST -Headers $headers -Body $body
    }
    catch {
        $detail = ""
        if ($_.Exception.Response) {
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $detail = $reader.ReadToEnd()
            } catch {}
        }
        throw "API POST failed: $($_.Exception.Message)$(if ($detail) {"`n   Response: $detail"})"
    }
}

function Invoke-AzureDevOpsPatch([string]$url, [hashtable]$headers, [string]$body) {
    try {
        Invoke-RestMethod -Uri $url -Method PATCH -Headers $headers -Body $body | Out-Null
    }
    catch {
        Write-Warning "API PATCH failed (non-fatal): $($_.Exception.Message)"
    }
}

function New-PatchOp([string]$path, $value) {
    return [ordered]@{ op = "add"; path = $path; value = $value }
}

# StrictMode-safe optional property read: returns the value or $null when the
# property (or a parent object) is absent, instead of throwing.
function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $prop = $obj.PSObject.Properties[$name]
    if ($prop) { return $prop.Value }
    return $null
}

function ConvertTo-HtmlParagraphs([string]$text) {
    $text = $text -replace '"', '&quot;'
    $lines = $text -split "`n"
    $result = ($lines | Where-Object { $_ -ne "" } | ForEach-Object { "<p>$_</p>" }) -join ""
    if (-not $result) { $result = "<p>$text</p>" }
    return $result
}

# ── load config ───────────────────────────────────────────────────────────────

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $scriptDir "config.json"
$TrackerFile = Join-Path $scriptDir "boards_tracker.csv"

if (-not (Test-Path $configPath)) {
    Die "config.json not found at $configPath. Copy config.json.example → config.json and fill it in."
}

$cfg = Get-Content $configPath -Raw | ConvertFrom-Json

$Organization = $cfg.organization
$Pat          = $cfg.pat
$Team         = if ($cfg.team) { $cfg.team } else { "" }
$Project      = if ($project)  { $project }  elseif ($cfg.project)         { $cfg.project }         else { "" }
$Assignee     = if ($assignee) { $assignee } elseif ($cfg.defaultAssignee) { $cfg.defaultAssignee } else { "" }

[string]::IsNullOrWhiteSpace($Organization) -and $(Die "config.json: 'organization' is required.")
[string]::IsNullOrWhiteSpace($Pat)          -and $(Die "config.json: 'pat' is required.")
[string]::IsNullOrWhiteSpace($Project)      -and $(Die "'project' must be set via --project or config.json.")

# ── validate inputs ───────────────────────────────────────────────────────────

if (-not (Test-Path $json)) { Die "Path not found: $json" }

$jsonItem = Get-Item $json
$WorkItemType = ""

if (-not $jsonItem.PSIsContainer) {
    # Single-file mode: resolve type upfront (unchanged behavior)
    $itemData = Get-Content $json -Raw | ConvertFrom-Json
    if ($userstory) { $WorkItemType = "User Story" }
    elseif ($bug)   { $WorkItemType = "Bug" }
    else {
        $WorkItemType = Get-Prop $itemData "workItemType"
        if ([string]::IsNullOrWhiteSpace($WorkItemType)) {
            Die "Cannot determine work item type. Pass -userstory or -bug, or add 'workItemType' to the JSON."
        }
    }

    if ($WorkItemType -notin @("User Story", "Bug")) {
        Die "Unsupported work item type: '$WorkItemType'. Expected 'User Story' or 'Bug'."
    }
}
# Folder mode: type is resolved per-file inside Push-WorkItemJson

# ── connection test ───────────────────────────────────────────────────────────

Write-Host "🔗 Testing connection to Azure DevOps..." -ForegroundColor Cyan
$headers = Get-AuthHeader $Pat
$testUrl = "https://dev.azure.com/$Organization/$Project/_apis/wit/workitemtypes?api-version=7.0"

try {
    $testResult = Invoke-RestMethod -Uri $testUrl -Method GET -Headers $headers
    # PowerShell 7.6+ ConvertFrom-Json rejects the empty-named property Azure DevOps
    # returns in the workitemtypes payload, so Invoke-RestMethod hands back the raw
    # JSON string. Re-parse with -AsHashtable to read the type count.
    $typeCount = if ($testResult -is [string]) {
        ($testResult | ConvertFrom-Json -AsHashtable)['count']
    } else { $testResult.count }
    Ok "Connected — org: $Organization | project: $Project | work item types: $typeCount"
}
catch {
    Die "Connection failed: $($_.Exception.Message). Check organization, project, and PAT."
}

$BaseUrl = "https://dev.azure.com/$Organization/$Project/_apis/wit"

# ── create Task ───────────────────────────────────────────────────────────────

function New-DevOpsTask([PSObject]$task, [int]$parentId) {
    $taskTitle    = Get-Prop $task "Title"
    $taskDesc     = if (Get-Prop $task "Description") { Get-Prop $task "Description" } else { "" }
    $taskAssignee = if (Get-Prop $task "AssignedTo") { Get-Prop $task "AssignedTo" } elseif ($Assignee) { $Assignee } else { "" }

    if ([string]::IsNullOrWhiteSpace($taskTitle)) {
        Write-Warning "Skipping task with missing Title."
        return
    }

    $ops = @(New-PatchOp "/fields/System.Title" $taskTitle)
    if ($taskDesc)     { $ops += New-PatchOp "/fields/System.Description" (ConvertTo-HtmlParagraphs $taskDesc) }
    if ($taskAssignee) { $ops += New-PatchOp "/fields/System.AssignedTo"  $taskAssignee }

    $body     = ConvertTo-Json $ops -Depth 5 -Compress
    $createUrl = "$BaseUrl/workitems/`$Task?api-version=7.0"
    $response  = Invoke-AzureDevOpsPost $createUrl $headers $body
    $taskId    = $response.id

    Start-Sleep -Seconds 1

    # Update optional fields
    $updateOps = @()
    $taskTags     = Get-Prop $task "Tags"
    $taskPlanning = Get-Prop $task "Planning"
    $taskEfforts  = Get-Prop $task "Efforts"
    if ($taskTags) {
        $updateOps += New-PatchOp "/fields/System.Tags" (@($taskTags) -join "; ")
    }
    if (Get-Prop $taskPlanning "Priority") {
        $updateOps += New-PatchOp "/fields/Microsoft.VSTS.Common.Priority" ([int](Get-Prop $taskPlanning "Priority"))
    }
    if (Get-Prop $taskPlanning "Activity") {
        $updateOps += New-PatchOp "/fields/Microsoft.VSTS.Common.Activity" (Get-Prop $taskPlanning "Activity")
    }
    if (Get-Prop $taskEfforts "OriginalEstimate") {
        $updateOps += New-PatchOp "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate" ([double](Get-Prop $taskEfforts "OriginalEstimate"))
    }

    if ($updateOps.Count -gt 0) {
        $updateBody = ConvertTo-Json $updateOps -Depth 5 -Compress
        Invoke-AzureDevOpsPatch "$BaseUrl/workitems/${taskId}?api-version=7.0" $headers $updateBody
    }

    # Link to parent
    $linkOps = @(@{
        op    = "add"
        path  = "/relations/-"
        value = @{
            rel = "System.LinkTypes.Hierarchy-Reverse"
            url = "https://dev.azure.com/$Organization/$Project/_apis/wit/workItems/$parentId"
        }
    })
    $linkBody = ConvertTo-Json $linkOps -Depth 5 -Compress
    Invoke-AzureDevOpsPatch "$BaseUrl/workitems/${taskId}?api-version=7.0" $headers $linkBody

    Write-Host "   ✅ Task: $taskTitle (ID: $taskId)" -ForegroundColor Green
}

# ── create Test Case ─────────────────────────────────────────────────────────

function New-DevOpsTestCase([PSObject]$tc, [int]$parentId) {
    $tcTitle = Get-Prop $tc "Title"
    if ([string]::IsNullOrWhiteSpace($tcTitle)) {
        Write-Warning "Skipping test case with missing Title."
        return
    }

    # Build ADO steps XML
    $tcSteps   = @(Get-Prop $tc "Steps" | Where-Object { $_ })
    $stepCount = $tcSteps.Count
    $stepsXml = "<steps id=`"0`" last=`"$stepCount`">"
    $stepNum = 1
    foreach ($step in $tcSteps) {
        $action   = [System.Security.SecurityElement]::Escape([string](Get-Prop $step "Action"))
        $expected = [System.Security.SecurityElement]::Escape([string](Get-Prop $step "Expected"))
        $stepsXml += "<step id=`"$stepNum`" type=`"ValidateStep`"><parameterizedString isformatted=`"true`">&lt;DIV&gt;$action&lt;/DIV&gt;</parameterizedString><parameterizedString isformatted=`"true`">&lt;DIV&gt;$expected&lt;/DIV&gt;</parameterizedString><description/></step>"
        $stepNum++
    }
    $stepsXml += "</steps>"

    # Create the Test Case work item
    $ops = @(New-PatchOp "/fields/System.Title" $tcTitle)
    if ($Assignee) { $ops += New-PatchOp "/fields/System.AssignedTo" $Assignee }

    $body      = ConvertTo-Json $ops -Depth 5 -Compress
    $createUrl = "$BaseUrl/workitems/`$Test Case?api-version=7.0"
    $response  = Invoke-AzureDevOpsPost $createUrl $headers $body
    $tcId      = $response.id

    Start-Sleep -Seconds 1

    # Patch steps
    $stepsOps  = @(New-PatchOp "/fields/Microsoft.VSTS.TCM.Steps" $stepsXml)
    $stepsBody = ConvertTo-Json $stepsOps -Depth 5 -Compress
    Invoke-AzureDevOpsPatch "$BaseUrl/workitems/${tcId}?api-version=7.0" $headers $stepsBody

    # Link test case to user story via TestedBy relationship
    $linkOps = @(@{
        op    = "add"
        path  = "/relations/-"
        value = @{
            rel = "Microsoft.VSTS.Common.TestedBy-Reverse"
            url = "https://dev.azure.com/$Organization/$Project/_apis/wit/workItems/$parentId"
        }
    })
    $linkBody = ConvertTo-Json $linkOps -Depth 5 -Compress
    Invoke-AzureDevOpsPatch "$BaseUrl/workitems/${tcId}?api-version=7.0" $headers $linkBody

    Write-Host "   ✅ Test Case: $tcTitle (ID: $tcId)" -ForegroundColor Green
}

# ── create User Story ─────────────────────────────────────────────────────────

function New-DevOpsUserStory([PSObject]$data) {
    Write-Host "`n📝 Creating User Story..." -ForegroundColor Cyan

    $title = Get-Prop $data "Title"
    if ([string]::IsNullOrWhiteSpace($title)) { throw "User Story JSON must have a 'Title' field." }

    $description = Get-Prop $data "Description"
    $acceptance  = Get-Prop $data "AcceptanceCriteria"
    $planning    = Get-Prop $data "Planning"
    $classify    = Get-Prop $data "Classification"
    $tags        = Get-Prop $data "Tags"
    $talking     = Get-Prop $data "TalkingPoints"

    $ops = @(New-PatchOp "/fields/System.Title" $title)

    if ($description) {
        $ops += New-PatchOp "/fields/System.Description" (ConvertTo-HtmlParagraphs $description)
    }
    if ($acceptance) {
        $ac = if ($acceptance -is [array]) {
            ($acceptance | ForEach-Object { "• $_" }) -join "`n"
        } else { [string]$acceptance }
        $ops += New-PatchOp "/fields/Microsoft.VSTS.Common.AcceptanceCriteria" (ConvertTo-HtmlParagraphs $ac)
    }
    if (Get-Prop $planning "StoryPoints") {
        $ops += New-PatchOp "/fields/Microsoft.VSTS.Scheduling.StoryPoints" ([double](Get-Prop $planning "StoryPoints"))
    }
    if (Get-Prop $planning "Priority") {
        $ops += New-PatchOp "/fields/Microsoft.VSTS.Common.Priority" ([int](Get-Prop $planning "Priority"))
    }
    if (Get-Prop $classify "RemainingWork") {
        $ops += New-PatchOp "/fields/Microsoft.VSTS.Scheduling.RemainingWork" ([double](Get-Prop $classify "RemainingWork"))
    }
    if ($tags) {
        $ops += New-PatchOp "/fields/System.Tags" (@($tags) -join "; ")
    }
    if ($talking) {
        $ops += New-PatchOp "/fields/System.History" (ConvertTo-HtmlParagraphs $talking)
    }
    if ($Assignee) {
        $ops += New-PatchOp "/fields/System.AssignedTo" $Assignee
    }

    $body      = ConvertTo-Json $ops -Depth 5 -Compress
    $createUrl = "$BaseUrl/workitems/`$User Story?api-version=7.0"
    $response  = Invoke-AzureDevOpsPost $createUrl $headers $body
    $wiId      = $response.id
    $wiUrl     = "https://dev.azure.com/$Organization/$Project/_workitems/edit/$wiId"

    Ok "User Story created"
    Info "ID   : $wiId"
    Info "Title: $title"
    Info "URL  : $wiUrl"

    $tasks = @(Get-Prop $data "Tasks" | Where-Object { $_ })
    if ($tasks.Count -gt 0) {
        Write-Host "`n📋 Creating $($tasks.Count) child Task(s)..." -ForegroundColor Yellow
        $ok = 0; $fail = 0
        foreach ($task in $tasks) {
            try   { New-DevOpsTask $task $wiId; $ok++ }
            catch { Write-Host "   ❌ Failed task '$(Get-Prop $task 'Title')': $_" -ForegroundColor Red; $fail++ }
        }
        Info "Tasks created: $ok$(if ($fail -gt 0) { " | failed: $fail" })"
    }

    $tests = @(Get-Prop $data "Tests" | Where-Object { $_ })
    if ($tests.Count -gt 0) {
        Write-Host "`n🧪 Creating $($tests.Count) Test Case(s)..." -ForegroundColor Yellow
        $tcOk = 0; $tcFail = 0
        foreach ($tc in $tests) {
            try   { New-DevOpsTestCase $tc $wiId; $tcOk++ }
            catch { Write-Host "   ❌ Failed test case '$(Get-Prop $tc 'Title')': $_" -ForegroundColor Red; $tcFail++ }
        }
        Info "Test Cases created: $tcOk$(if ($tcFail -gt 0) { " | failed: $tcFail" })"
    }

    return $wiUrl
}

# ── create Bug ────────────────────────────────────────────────────────────────

function New-DevOpsBug([PSObject]$data) {
    Write-Host "`n🐛 Creating Bug..." -ForegroundColor Cyan

    $title = Get-Prop $data "Title"
    if ([string]::IsNullOrWhiteSpace($title)) { throw "Bug JSON must have a 'Title' field." }

    $rawDescription = Get-Prop $data "Description"
    $rawRepro       = Get-Prop $data "ReproSteps"

    # Description must be a plain string. If an agent wrote it as an object, flatten it.
    $description = if ($rawDescription -is [PSCustomObject]) {
        $cb = if (Get-Prop $rawDescription 'Current behaviour') { "Current behaviour: $(Get-Prop $rawDescription 'Current behaviour')" } else { "" }
        $eb = if (Get-Prop $rawDescription 'Expected behaviour') { "Expected behaviour: $(Get-Prop $rawDescription 'Expected behaviour')" } else { "" }
        $af = if (Get-Prop $rawDescription 'Affected files')     { "Affected files: $(Get-Prop $rawDescription 'Affected files')" }     else { "" }
        $im = if (Get-Prop $rawDescription 'Impact')             { "Impact: $(Get-Prop $rawDescription 'Impact')" }                     else { "" }
        (@($cb, $eb, $af, $im) | Where-Object { $_ -ne "" }) -join "`n"
    } else { [string]$rawDescription }

    # ReproSteps must be a plain string. If an agent wrote it as an array, join the items.
    $reproSteps = if ($rawRepro -is [array]) {
        $rawRepro -join "`n"
    } else { [string]$rawRepro }

    $ops = @(New-PatchOp "/fields/System.Title" $title)
    if ($description) { $ops += New-PatchOp "/fields/System.Description" (ConvertTo-HtmlParagraphs $description) }
    if ($reproSteps)  { $ops += New-PatchOp "/fields/Microsoft.VSTS.TCM.ReproSteps" (ConvertTo-HtmlParagraphs $reproSteps) }
    if ($Assignee)         { $ops += New-PatchOp "/fields/System.AssignedTo" $Assignee }

    $body      = ConvertTo-Json $ops -Depth 5 -Compress
    $createUrl = "$BaseUrl/workitems/`$Bug?api-version=7.0"
    $response  = Invoke-AzureDevOpsPost $createUrl $headers $body
    $wiId      = $response.id

    Start-Sleep -Seconds 2

    # Update fields requiring a second pass
    $updateOps = @()
    $bugPlanning = Get-Prop $data "Planning"
    $bugEfforts  = Get-Prop $data "Efforts"
    $bugDiscuss  = Get-Prop $data "Discussion"
    if (Get-Prop $bugPlanning "Priority") {
        $updateOps += New-PatchOp "/fields/Microsoft.VSTS.Common.Priority" ([int](Get-Prop $bugPlanning "Priority"))
    }
    if (Get-Prop $bugPlanning "Severity") {
        $sevMap = @{ "1" = "1 - Critical"; "2" = "2 - High"; "3" = "3 - Medium"; "4" = "4 - Low" }
        $sevKey = (Get-Prop $bugPlanning "Severity").ToString()
        if ($sevMap.ContainsKey($sevKey)) {
            $updateOps += New-PatchOp "/fields/Microsoft.VSTS.Common.Severity" $sevMap[$sevKey]
        }
    }
    if (Get-Prop $bugEfforts "OriginalEstimate") {
        $updateOps += New-PatchOp "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate" ([double](Get-Prop $bugEfforts "OriginalEstimate"))
    }
    if ($bugDiscuss) {
        $updateOps += New-PatchOp "/fields/System.History" (ConvertTo-HtmlParagraphs $bugDiscuss)
    }

    if ($updateOps.Count -gt 0) {
        $updateBody = ConvertTo-Json $updateOps -Depth 5 -Compress
        Invoke-AzureDevOpsPatch "$BaseUrl/workitems/${wiId}?api-version=7.0" $headers $updateBody
    }

    $wiUrl = "https://dev.azure.com/$Organization/$Project/_workitems/edit/$wiId"
    Ok "Bug created"
    Info "ID   : $wiId"
    Info "Title: $title"
    Info "URL  : $wiUrl"

    return $wiUrl
}

# ── tracker (US/BUG number → ADO work item id) ────────────────────────────────

function Get-WorkItemKey([string]$filePath) {
    $base = [System.IO.Path]::GetFileNameWithoutExtension($filePath)
    if ($base -match '^(?i)(US|BUG)[-_]?([0-9]+)') {
        return "$($Matches[1].ToUpper())$($Matches[2])"
    }
    return $base
}

function Test-ValidWorkItemJson([string]$filePath) {
    try {
        $data = Get-Content $filePath -Raw | ConvertFrom-Json
        return -not [string]::IsNullOrWhiteSpace((Get-Prop $data "Title"))
    }
    catch { return $false }
}

function Get-TrackerEntry([string]$key) {
    if (-not (Test-Path $TrackerFile)) { return $null }
    Import-Csv $TrackerFile | Where-Object { $_.WorkItemKey -eq $key } | Select-Object -Last 1
}

function Set-TrackerEntry([string]$key, [string]$adoId, [string]$type, [string]$title, [string]$url, [string]$src) {
    $rows = @()
    if (Test-Path $TrackerFile) {
        $rows = @(Import-Csv $TrackerFile | Where-Object { $_.WorkItemKey -ne $key })
    }
    $rows += [PSCustomObject]@{
        WorkItemKey = $key
        AdoId       = $adoId
        Type        = $type
        Title       = ($title -replace ',', ';')
        Url         = $url
        SourceFile  = $src
        PushedAt    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    }
    $rows | Export-Csv -Path $TrackerFile -NoTypeInformation
}

# Pushes a single JSON file. Returns the work item URL, or $null if skipped/failed.
function Push-WorkItemJson([string]$filePath, [string]$typeOverride) {
    $key = Get-WorkItemKey $filePath

    if (-not $Force) {
        $existing = Get-TrackerEntry $key
        if ($existing) {
            Write-Host "⏭  Skipping $filePath — already pushed as $key (ADO ID: $($existing.AdoId)). Use -Force to re-push." -ForegroundColor Yellow
            return $null
        }
    }

    $data = Get-Content $filePath -Raw | ConvertFrom-Json

    $type = $typeOverride
    if ([string]::IsNullOrWhiteSpace($type)) { $type = Get-Prop $data "workItemType" }
    if ([string]::IsNullOrWhiteSpace($type)) {
        if ($key -match '^US')  { $type = "User Story" }
        elseif ($key -match '^BUG') { $type = "Bug" }
    }
    if ($type -notin @("User Story", "Bug")) {
        Write-Warning "Skipping $filePath — cannot determine/unsupported work item type."
        return $null
    }

    try {
        $url = if ($type -eq "User Story") { New-DevOpsUserStory $data } else { New-DevOpsBug $data }
    }
    catch {
        Write-Host "❌ Failed to push $filePath : $_" -ForegroundColor Red
        return $null
    }

    $wiId = $url.Substring($url.LastIndexOf('/') + 1)
    Set-TrackerEntry $key $wiId $type (Get-Prop $data "Title") $url $filePath
    return $url
}

# ── dispatch ──────────────────────────────────────────────────────────────────

Write-Host ""

if ($jsonItem.PSIsContainer) {
    Write-Host "📂 Scanning folder: $json" -ForegroundColor Cyan
    $files = Get-ChildItem -Path $json -Filter *.json -Recurse -File | Sort-Object FullName
    if ($files.Count -eq 0) { Die "No .json files found in $json" }
    Write-Host "Found $($files.Count) JSON file(s)."

    $typeOverride = if ($userstory) { "User Story" } elseif ($bug) { "Bug" } else { "" }
    $pushed = 0; $skipped = 0; $failed = 0; $invalid = 0

    foreach ($f in $files) {
        if (-not (Test-ValidWorkItemJson $f.FullName)) {
            Write-Warning "Skipping $($f.FullName) — not a valid work item JSON (missing 'Title')."
            $invalid++
            continue
        }
        Write-Host "`n── $($f.FullName) ──" -ForegroundColor DarkGray
        $result = Push-WorkItemJson $f.FullName $typeOverride
        if ($result) { $pushed++ }
        elseif (Get-TrackerEntry (Get-WorkItemKey $f.FullName)) { $skipped++ }
        else { $failed++ }
    }

    Write-Host "`n════════════════════════════════════"
    Write-Host "Summary: $pushed pushed | $skipped skipped (already tracked) | $failed failed | $invalid invalid"
    Write-Host "Tracker: $TrackerFile"
}
else {
    Push-WorkItemJson $json $WorkItemType | Out-Null
}
