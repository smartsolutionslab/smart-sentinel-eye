# Verification — Spec 128 (#2137)

Phase 5. Branch `fix/2137-a-failed-migration-is-loud`.

## Gap 1 — run mode over a failed migration

**How the failure was made on demand.** No product code was edited to
produce it, and no realm or database was mutated. The
`MigrationRunnerClientSecret` parameter's dev default was changed to
`deliberately-wrong-2137` in the working tree only, never committed, and
reverted before the final build. `migrations` mints a `migration-runner`
token to read `/fabs` (spec 019 FR-011), so a wrong secret makes the last
migrator throw and the process exit 1.

Two earlier attempts are recorded because they are the ones a reader would
otherwise repeat:

- `-- Parameters:MigrationRunnerClientSecret=...` on the command line does
  **nothing**. `AddParameter(name, value)` is a literal, not a
  configuration-backed parameter, so the argument was ignored and the run
  succeeded. (The same is true of the `Parameters:` arguments
  `AppHostE2ESwitchTests` and `AspireFixture` pass — they happen to match
  the defaults, so nothing has ever depended on them taking effect.)
- Renaming `__EFMigrationsHistory` in a database would also have worked and
  was refused by the permission system, correctly: it is destructive DDL
  against a developer's data.

**Red** — pre-change AppHost (`9c722947`), run mode, wrong secret:

```
migrations  Finished  exit_code=1
  "Migrations failed at EventIngestion.PartitionRollover. MigrationRunner is
   exiting non-zero; nothing after this point was migrated."

audit-observability  Running      layout-composition  Running
automation           Running      overlay-designer    Running
camera-catalog       Running      stream-distribution Running
event-ingestion      Running      system-variables    Running
identity             Running
```

Nine services Running over a migration run that exited 1.

**Green** — same injected failure, AppHost at `8c4afe9d`:

```
migrations  Finished  exit_code=1

audit-observability  FailedToStart   layout-composition   FailedToStart
automation           FailedToStart   overlay-designer     FailedToStart
camera-catalog       FailedToStart   stream-distribution  FailedToStart
event-ingestion      FailedToStart   system-variables     Waiting
identity             FailedToStart
```

None Running. `system-variables` reads `Waiting` rather than `FailedToStart`
because it also waits for `overlay-designer`, which failed first.

**The success path, third boot, committed tree, real secret:**

```
migrations  Finished  exit_code=0   08:21:12 -> 08:21:47   (35s)
all nine    Running / Healthy       08:21:47.8 - 08:21:51.1
```

The nine services start 0.7–4 s after the run finishes. That is the whole
cost of the gate on the common path, and it is the answer to "does this make
run mode refuse to start when it previously started": no. States read from
the Aspire dashboard resource list, not from a log.

## Gap 2 — the stack wait

`scripts/wait-for-e2e-stack.sh`, run against the live run-mode stack above:

```
Waiting for the migration run to finish ...
  every database carries applied migrations after ~5s
```

`node --test scripts/wait-for-e2e-stack.test.mjs`, before and after the
script change (the doubles are identical; only the script differs):

| | before | after |
|---|---|---|
| a fully migrated stack is reported ready | pass | pass |
| a migration run that aborted partway | **fail** (exit 0) | pass |
| a stack it cannot ask about migrations | **fail** (exit 0) | pass |

The gateway probe below it still reports "inconclusive" on Windows — it
reads the served bundle through Vite's `@fs`, which does not resolve a Git
Bash path. Pre-existing, untouched, and works on the Linux runner the script
is for.

## Gap 3 — no honest red

`if: failure()` versus `if: failure() || cancelled()` is a decision the
GitHub Actions runner makes about a job it has cancelled. Nothing local
exercises it, and a test that read the YAML and asserted the string would
pass whether or not the step ever ran. So there is no red for this one, and
none was manufactured.

The evidence is the CI history instead, fetched 2026-09-11:

- Run `33623647778`, e2e job: step 15 *Run the Playwright e2e suite*
  **cancelled** (40-minute job timeout), step 16 *AppHost log (on failure)*
  **skipped**, step 17 *Stop the AppHost* (`if: always()`) **success**,
  step 18 *Upload Playwright report* (`if: always()`) **success**.
- Run `34504807442`, backend job (#2247): step 8 **cancelled** at the
  20-minute timeout, step 9 *Upload coverage report* (`if: always()`)
  **success**.

Two jobs, two cancellations, four `always()` steps that ran and one
`failure()` step that did not. The proof will be the next cancelled e2e job
printing 300 lines of `apphost.log`; until one happens this is inference
from observed runner behaviour, and is recorded as such.

## Latency (§IV)

**Not on the path.** Startup ordering happens before any event exists, and
gaps 2 and 3 are CI wiring. No leg of the budget is affected and none is
cited.

## Suites

| | |
|---|---|
| `dotnet build SmartSentinelEye.slnx -c Release` | succeeded, 0 warnings |
| `Category=FixtureLogic` (the backend job's Docker-free step) | 134 passed, 0 failed |
| `Architecture.Tests` | 361 passed |
| `MigrationRunner.Tests` | 10 passed |
| `pnpm test:guards` | 6 passed |

`scripts/coverage-check.ps1` needs PowerShell 7, which is not installed on
this machine; the coverage gate is the `backend` job's to run. No test was
removed, weakened or skipped, so no threshold moves.
