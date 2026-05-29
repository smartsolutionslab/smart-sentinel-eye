<#
.SYNOPSIS
    Runs unit tests with code coverage and enforces ADR-0065 thresholds.

.DESCRIPTION
    Skips integration tests (they require Docker). Collects coverage via
    coverlet, merges per-project reports with reportgenerator, then fails
    if any of the gated projects falls below its threshold:

        CameraCatalog.Domain           >= 90%
        CameraCatalog.Application      >= 80%
        StreamDistribution.Domain      >= 90%
        StreamDistribution.Application >= 80%
        LayoutComposition.Domain       >= 90%
        LayoutComposition.Application  >= 80%
        OverlayDesigner.Domain         >= 90%
        OverlayDesigner.Application    >= 80%
        SystemVariables.Domain         >= 90%
        SystemVariables.Application    >= 80%
        EventIngestion.Domain          >= 90%
        EventIngestion.Application     >= 80%
        Automation.Domain              >= 90%
        Automation.Application         >= 80%
        Shared.Kernel                  >= 90%
        Shared.Contracts               >= 90%

    Run before opening a PR. Use `-OpenReport` to launch the HTML report.
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = "$PSScriptRoot/../artifacts/coverage",
    [switch]$OpenReport
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path "$PSScriptRoot/.."
$rawDir = Join-Path $OutputDirectory 'raw'
$reportDir = Join-Path $OutputDirectory 'report'

if (Test-Path $OutputDirectory) {
    Remove-Item -Recurse -Force $OutputDirectory
}
New-Item -ItemType Directory -Force -Path $rawDir | Out-Null

Push-Location $repoRoot
try {
    Write-Host "==> Running unit tests with coverage ($Configuration)..."
    # Exclude the Integration.Tests project by assembly name, NOT by
    # FullyQualifiedName substring — spec 006 introduces classes
    # named `WebhookIntegration` whose tests would otherwise be
    # filtered out by an `!~Integration` substring match.
    & dotnet test --filter 'FullyQualifiedName!~SmartSentinelEye.Integration.Tests' `
        -c $Configuration `
        --collect:'XPlat Code Coverage' `
        --results-directory $rawDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed (exit $LASTEXITCODE)." }

    Write-Host "==> Restoring local tools..."
    & dotnet tool restore | Out-Null

    # Single source of truth: every gated assembly + its threshold.
    # Adding a new context-layer is a one-line edit here.
    $thresholds = @{
        'SmartSentinelEye.CameraCatalog.Domain'             = 90
        'SmartSentinelEye.CameraCatalog.Application'        = 80
        'SmartSentinelEye.StreamDistribution.Domain'        = 90
        'SmartSentinelEye.StreamDistribution.Application'   = 80
        'SmartSentinelEye.LayoutComposition.Domain'         = 90
        'SmartSentinelEye.LayoutComposition.Application'    = 80
        'SmartSentinelEye.OverlayDesigner.Domain'           = 90
        'SmartSentinelEye.OverlayDesigner.Application'      = 80
        'SmartSentinelEye.SystemVariables.Domain'           = 90
        'SmartSentinelEye.SystemVariables.Application'      = 80
        'SmartSentinelEye.EventIngestion.Domain'            = 90
        'SmartSentinelEye.EventIngestion.Application'       = 80
        'SmartSentinelEye.Automation.Domain'                = 90
        'SmartSentinelEye.Automation.Application'           = 80
        'SmartSentinelEye.Shared.Kernel'                    = 90
        'SmartSentinelEye.Shared.Contracts'                 = 90
    }

    Write-Host "==> Merging coverage reports..."
    $assemblyFilter = ($thresholds.Keys | ForEach-Object { "+$_" }) -join ';'

    & dotnet reportgenerator `
        "-reports:$rawDir/**/coverage.cobertura.xml" `
        "-targetdir:$reportDir" `
        "-reporttypes:Html;TextSummary;Cobertura" `
        "-assemblyfilters:$assemblyFilter" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "reportgenerator failed (exit $LASTEXITCODE)." }

    $cobertura = Join-Path $reportDir 'Cobertura.xml'
    [xml]$report = Get-Content $cobertura

    Write-Host "`n==> Coverage gate (ADR-0065):"
    $failed = @()
    foreach ($pkg in $report.coverage.packages.package) {
        if (-not $thresholds.ContainsKey($pkg.name)) { continue }
        $rate = [double]$pkg.'line-rate' * 100.0
        $gate = $thresholds[$pkg.name]
        $status = if ($rate -ge $gate) { 'PASS' } else { 'FAIL' }
        $line = "{0,-50} {1,7:F1}%   (gate >= {2}%)  {3}" -f $pkg.name, $rate, $gate, $status
        Write-Host $line
        if ($rate -lt $gate) {
            $failed += [pscustomobject]@{ Assembly = $pkg.name; Measured = $rate; Gate = $gate }
        }
    }

    if ($OpenReport) {
        $indexHtml = Join-Path $reportDir 'index.html'
        if (Test-Path $indexHtml) { Start-Process $indexHtml }
    }

    if ($failed.Count -gt 0) {
        Write-Host "`nCoverage gate failed. See report: $reportDir/index.html" -ForegroundColor Red
        exit 1
    }
    Write-Host "`nAll gates pass. Report: $reportDir/index.html"
}
finally {
    Pop-Location
}
