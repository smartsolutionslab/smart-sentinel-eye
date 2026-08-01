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
        Identity.Domain                >= 90%
        Identity.Application           >= 80%
        AuditObservability.Domain      >= 90%
        AuditObservability.Application >= 80%
        Shared.Kernel                  >= 90%
        Shared.Contracts               >= 90%

    Run before opening a PR. Use `-OpenReport` to launch the HTML report.
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = "$PSScriptRoot/../artifacts/coverage",
    [switch]$OpenReport,
    # CI passes -NoBuild so this reuses the build job's output instead of
    # rebuilding (and re-running) the whole solution a second time.
    [switch]$NoBuild
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
    # One project at a time, each writing into its own results directory.
    # Running the whole solution in one `dotnet test` let MSBuild start several
    # test hosts at once; coverlet instruments an assembly by swapping in a
    # modified copy and restoring it at host exit, so concurrent hosts race over
    # the same files. The loser drops that assembly's coverage rows *and* leaves
    # its DLL and PDB mismatched, which makes every later --no-build run report
    # it as 0.0% until the output is rebuilt (#1142). Sequencing removes the
    # contention rather than detecting it after the fact.
    #
    # Selecting projects directly also retires the old FullyQualifiedName
    # exclusion filter, which existed only to keep Integration.Tests out of a
    # solution-wide run and had to be written against the assembly name because
    # class names like `WebhookIntegration` matched an `!~Integration` substring.
    $testProjects = Get-ChildItem -Path (Join-Path $repoRoot 'tests') -Filter '*.csproj' -Recurse |
        Where-Object { $_.Name -ne 'SmartSentinelEye.Integration.Tests.csproj' } |
        Sort-Object Name
    if (-not $testProjects) { throw 'No test projects found under tests/.' }

    $testOutput = @()
    foreach ($proj in $testProjects) {
        $projectName = [IO.Path]::GetFileNameWithoutExtension($proj.Name)
        Write-Host "  -> $projectName"
        $testArgs = @(
            'test', $proj.FullName
            '-c', $Configuration
            '--collect:XPlat Code Coverage'
            '--results-directory', (Join-Path $rawDir $projectName)
        )
        if ($NoBuild) { $testArgs += '--no-build' }
        & dotnet @testArgs 2>&1 | Tee-Object -Variable projectOutput
        $exitCode = $LASTEXITCODE
        $testOutput += $projectOutput
        if ($exitCode -ne 0) { throw "dotnet test failed for $projectName (exit $exitCode)." }
    }

    # When parallel test hosts race to restore an instrumented module, the
    # coverlet collector gives up on that assembly and emits no coverage rows
    # for it — it does not fail the run. A gate computed from the merged report
    # then measures less code than it appears to, so it can pass for the wrong
    # reason. Silent green is the dangerous direction, so treat any collector
    # failure as fatal rather than gating on data known to be incomplete (#1142).
    if ($testOutput | Select-String -Pattern 'CoverletDataCollectorException' -Quiet) {
        $dropped = $testOutput |
            Select-String -Pattern "cannot access the file '([^']+\.dll)'" -AllMatches |
            ForEach-Object { $_.Matches } |
            ForEach-Object { Split-Path $_.Groups[1].Value -Leaf } |
            Sort-Object -Unique
        $which = if ($dropped) { $dropped -join ', ' } else { '(assembly not identified)' }
        Write-Warning ("Coverlet failed to restore $which, so it contributed no coverage rows. " +
                       "Dropped data can only lower a figure, never raise one, so this cannot " +
                       "turn a failing gate green — but it can depress one unfairly. A failed " +
                       "restore also leaves that DLL and its PDB mismatched, which makes every " +
                       "later --no-build run report the assembly as 0.0%: delete the affected " +
                       "test project's bin/obj and rebuild rather than just re-running. " +
                       "The checks below will fail the run if a gated assembly is affected.")
    }

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
        'SmartSentinelEye.Identity.Domain'                  = 90
        'SmartSentinelEye.Identity.Application'             = 80
        'SmartSentinelEye.AuditObservability.Domain'        = 90
        'SmartSentinelEye.AuditObservability.Application'   = 80
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

    # The gate loop below only evaluates thresholds it finds a package for, so a
    # gated assembly missing from the merged report is skipped in silence and the
    # run still reports "All gates pass". That is the one way this script can go
    # green without having measured the code, so check for it explicitly (#1142).
    $reported = @($report.coverage.packages.package | ForEach-Object { $_.name })
    $absent = @($thresholds.Keys | Where-Object { $reported -notcontains $_ })
    if ($absent) {
        throw ("These gated assemblies are absent from the coverage report: $($absent -join ', '). " +
               "Their thresholds would be skipped silently rather than enforced, so the run is " +
               "failing instead of reporting a pass it has not earned. Usually a lost or " +
               "incomplete coverlet collection — rebuild the affected test projects. See #1142.")
    }

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

    # A gated assembly reading exactly 0.0% is not a coverage problem — every
    # gated project has tests, and they are asserted to pass above. It means the
    # coverage data for that assembly was lost. The usual cause is a stale
    # DLL/PDB pair left behind when a coverlet host failed to restore an
    # instrumented module: the mismatch stops line mapping silently, and every
    # later --no-build run repeats the 0.0% until the output is rebuilt (#1142).
    $lost = $failed | Where-Object { $_.Measured -eq 0 }
    if ($lost) {
        Write-Host ''
        throw ("Coverage data was lost for: $($lost.Assembly -join ', '). A gated assembly " +
               "cannot genuinely be at 0.0% when its tests pass, so the gate result is " +
               "meaningless rather than failing. This is usually a stale DLL/PDB pair from an " +
               "interrupted coverlet run — delete the affected test project's bin/obj, rebuild, " +
               "and re-run. See #1142.")
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
