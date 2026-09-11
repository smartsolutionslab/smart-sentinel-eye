# Spec 128 — A failed migration is loud

**Issue:** #2137 (three gaps, one file each)
**Branch:** `fix/2137-a-failed-migration-is-loud`
**Status:** Phase 3 complete — awaiting gate
**Lane:** autonomous (ADR-0144)
**ADRs:** 0037 (phases), 0144 (lane; 4a colour **red**), 0036 (smallest
change), 0067 (MigrationRunner), 0024/0025 (Aspire is the composition root),
0109 (contention files — `AppHost.cs` and `ci.yml` are both on the list),
0108 (the e2e gate this script guards), 0103 (Aspire fixture),
0052/0053 (xUnit + Shouldly, test naming).

## Problem

#2064 closed the **integration** lane's half: `AspireFixture` waits for
`migrations` to reach `Finished`, reads its exit code, fetches its log and
throws at 1m38s. That work is not the subject here and is not touched.

The **run-mode** and **e2e** lanes still have the original gap, in three
independent places.

### Gap 1 — run mode lets every service boot over a failed migration

`src/AppHost/AppHost.cs` applies `WaitForCompletion(migrations)` inside
`if (isE2ETests)` (lines 436–452 today; the issue cites 411–425, which is
where it sat when the issue was written). A developer's `aspire run` — and
the **e2e CI job**, which also boots run mode — therefore starts the nine
context services regardless of what `migrations` exited with. They come up
against a partial schema and report healthy.

The issue's own framing: *"The stack reports healthy and misbehaves, which
is strictly worse than failing: the integration lane at least went red."*

### Gap 2 — `wait-for-e2e-stack.sh` never asks about migrations

`scripts/wait-for-e2e-stack.sh` waits on three Vite ports and one gateway
route. It asks nothing about the schema. A stack whose migrations aborted
satisfies it, and the Playwright suite then fails in ways that describe the
product rather than the boot.

### Gap 3 — the diagnostic step is skipped on the failure that needs it

`.github/workflows/ci.yml`'s `AppHost log (on failure)` step is guarded by
`if: failure()`, which is **false when a job is cancelled**. A job-level
`timeout-minutes` expiry is a cancellation.

Observed, in run `33623647778`'s e2e job (fetched 2026-09-11):

| # | conclusion | step | guard |
|---|---|---|---|
| 15 | cancelled | Run the Playwright e2e suite | — |
| 16 | **skipped** | **AppHost log (on failure)** | `if: failure()` |
| 17 | success | Stop the AppHost | `if: always()` |
| 18 | success | Upload Playwright report | `if: always()` |

Two siblings guarded `always()` ran in that same cancelled job; the one
guarded `failure()` did not. That is the whole defect, and it is already
recorded in CI history rather than inferred.

**A second instance, #2247.** The `backend` job was cancelled at its
20-minute timeout in run `34504807442` (`develop`). Its `always()` coverage
upload ran (step 9, success) while the cancelled test step (step 8) produced
nothing. `backend` has **no** `failure()`-guarded step to lose, so it is not
an instance of *this* defect — it is the absence of any diagnostic step at
all, which is #2247's subject and out of scope here. Recorded because the
brief asked for the audit, and because the audit's result is "one step in the
file has this defect", not "several".

## Scope

| | In | Out |
|---|---|---|
| Gap 1 | the `WaitForCompletion(migrations)` edge in run mode | any change to `MigrationRunner` itself |
| Gap 2 | a migrations probe in `wait-for-e2e-stack.sh` | rewriting the port/gateway probes |
| Gap 3 | the guard on `AppHost log (on failure)` | adding diagnostics to `backend` (#2247) |
| | | anything `AspireFixture` already does (#2064) |

## Requirements

- **FR-001** In run mode, each of the nine context services waits for
  `migrations` to complete **with exit code 0** before it starts. The
  existing `isE2ETests` gate is not weakened: it becomes unconditional.
- **FR-002** `scripts/wait-for-e2e-stack.sh` fails, loudly and by name, when
  the stack's databases do not carry applied migrations — before it spends
  its port-and-gateway budget.
- **FR-003** The probe in FR-002 fails when it **cannot determine** the
  answer. "I could not ask" is not "the schema is there".
- **FR-004** `AppHost log (on failure)` runs when the job is cancelled as
  well as when it fails, and stays quiet on success.

## What this is not

**Not a §IV change.** Nothing here sits on `event arrival → overlay
rendered`. Gap 1 is startup ordering (before any event exists), gaps 2 and 3
are CI wiring. No leg of the budget is affected, and none is cited.

**Not a new gate on the developer loop's success path.** FR-001 costs
ordering, not capability: the nine services already `WaitFor(rabbitmq)` and
`WaitFor(keycloak)`, and `migrations` itself already waits for all nine
databases and Keycloak. When migrations succeed — the common case — the
only cost is that the services start a few seconds later, in an order the
stack already half-imposed. There is no "run without a database" mode to
break: `postgres` is unconditional in the graph and every service takes a
`WithReference` on its database.

**Not exact.** The FR-002 probe reads `__EFMigrationsHistory` in each of the
nine databases. A run that aborts partway leaves later contexts with no
table at all, which it catches. A run that aborts on the **last** migration
of the **last** context leaves a populated table and is not caught. The
alternative — pinning the expected migration count per context — would go red
on every migration anyone adds. Recorded so the next reader knows the shape
of what it proves, rather than discovering it.
