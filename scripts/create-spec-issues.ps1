<#
.SYNOPSIS
    Parses a Spec-Kit tasks.md and creates one GH issue per task.

.DESCRIPTION
    Looks for lines matching ``- [ ] **TNNN [P?] [STORY]** Description``,
    extracts the ID/story/description, and creates a labelled GH issue.
    Idempotent: skips tasks whose IDs already appear in an open or
    closed issue title for the same feature label.

.PARAMETER TasksFile
    Path to the tasks.md to parse.

.PARAMETER FeatureLabel
    Feature label to attach (e.g. ``feature:005-system-variables``).
    Must already exist (create via ``gh label create`` first).

.PARAMETER DryRun
    Only print what would be created; don't actually call ``gh``.

.EXAMPLE
    pwsh scripts/create-spec-issues.ps1 `
        -TasksFile specs/005-system-variables/tasks.md `
        -FeatureLabel feature:005-system-variables
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$TasksFile,
    [Parameter(Mandatory)] [string]$FeatureLabel,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $TasksFile)) {
    throw "Tasks file not found: $TasksFile"
}

# Pre-load existing issue titles for this feature so we can skip
# anything already created (idempotent re-runs after a partial failure).
Write-Host "==> Loading existing issues for $FeatureLabel..."
$existing = & gh issue list --label $FeatureLabel --state all --limit 200 --json title | ConvertFrom-Json
$existingIds = @{}
foreach ($issue in $existing) {
    if ($issue.title -match '^\[(?<id>T\d{3})\]') {
        $existingIds[$Matches.id] = $true
    }
}
Write-Host "    found $($existingIds.Count) issues already in place."

# Parse the tasks. Each task line looks like:
#   - [ ] **T001 [P] [FOUND]** Add `system-variables-db` to ...
# Capture: ID, optional [P], story bucket, description.
$pattern = '^\s*-\s*\[\s\]\s*\*\*(?<id>T\d{3})\s*(?:\[P\]\s*)?\[(?<story>[A-Z0-9]+)\]\*\*\s*(?<desc>.*)$'

$tasks = @()
foreach ($line in Get-Content $TasksFile) {
    if ($line -match $pattern) {
        $tasks += [pscustomobject]@{
            Id          = $Matches.id
            Story       = $Matches.story
            Description = $Matches.desc.Trim()
        }
    }
}
Write-Host "==> Parsed $($tasks.Count) tasks from $TasksFile."

$created = 0
$skipped = 0
foreach ($task in $tasks) {
    if ($existingIds.ContainsKey($task.Id)) {
        $skipped++
        continue
    }

    # Compose title: truncate the description so titles stay readable.
    $shortDesc = if ($task.Description.Length -gt 110) {
        $task.Description.Substring(0, 110) + '...'
    } else {
        $task.Description
    }
    $title = "[$($task.Id)] $shortDesc"

    $storyLabel = "story:" + $task.Story.ToLowerInvariant()
    $labels = @($FeatureLabel, $storyLabel, 'task')

    if ($DryRun) {
        Write-Host "DRY: $title  [$($labels -join ', ')]"
        continue
    }

    Write-Host -NoNewline "==> $($task.Id) ..."
    $null = & gh issue create `
        --title $title `
        --body $task.Description `
        --label ($labels -join ',') 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh issue create failed for $($task.Id)"
    }
    Write-Host " ok"
    $created++

    # GitHub's per-IP rate limit is generous but a tiny pause keeps
    # the run polite on shared CI runners.
    Start-Sleep -Milliseconds 200
}

Write-Host ""
Write-Host "==> Done. Created: $created. Skipped (already existed): $skipped."
