# Tasks 181 — A gate that knows its resource set

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2268

---

## Declarations required at the phase-3 gate

| Declaration | Value |
|---|---|
| **Phase 4a colour** | **RED.** Behaviour-changing: a case that passes today must fail. Three tests must be observed failing, with verbatim output in the PR (ADR-0139). The three *existing* tests in `wait-for-e2e-stack.test.mjs` are characterisation and must pass **unmodified** either side. |
| **Phase 4a agent** | `test-writer` |
| **Phase 4b agent** | `infra-engineer` |
| **Phase 6 agent** | `infra-reviewer` |
| **Security review** | **Not warranted.** No auth surface, no endpoint, no secret handling. The one trust question — what may appear in a file printed into a public CI log — is an acceptance criterion (spec §3) with a test, not a deferred review item. |
| **Phase 5** | **Mandatory, by hand.** Five of six tests cannot prove the AppHost writes a live file. See `plan.md` §3. |
| **New ADR** | No — with one named escalation, spec §8. |
| **Latency budget** | N/A, CI infrastructure. CI budget impact recorded in spec §6 and measured by T010. |
| **Board** | #2268 already on Project #13, status Todo, `agent:ready`, no `agent:blocked`. Verified with `--limit 2000`. Nothing to add. |

### The complete edit set

`src/AppHost/StackStatusReport.cs` (new) · `src/AppHost/AppHost.cs` ·
`scripts/wait-for-e2e-stack.sh` · `scripts/wait-for-e2e-stack.test.mjs` ·
`.github/workflows/ci.yml` ·
`tests/Integration.Tests/AppHostStackStatusTests.cs` (new) · `.gitignore`

Anything outside this list is out of scope.

---

## Parallelism (ADR-0109)

**T001 is foundational and blocks everything**: the report format in `plan.md`
§2.4 is the contract two programs and three tests are written against. It is
already fixed in the plan, so T001 is a confirmation, not a design step — but if
T007 forces a format change, every downstream task is affected.

After T001, two disjoint-file tracks run in parallel:

- **Track A** — the gate: `wait-for-e2e-stack.test.mjs`, `wait-for-e2e-stack.sh`
- **Track B** — the writer: `StackStatusReport.cs`, `AppHost.cs`,
  `AppHostStackStatusTests.cs`

They share no file. `ci.yml` (T008) joins them and is not parallel with either.

Within phase 4 the colour ordering still binds: **every test in a track is
written and observed red before that track's implementation.**

---

## US1 (P1) — A resource that never started closes the gate

### `[T001]` Confirm the report format against a live boot — *blocks everything*

`infra-engineer`. Boot the AppHost by hand with a throwaway path:

```sh
dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj \
  -- ScenarioSimulator=false StackStatusFile="$PWD/scratch-status.tsv"
```

This is T007's experiment run early, because its outcome can invalidate the
plan. It cannot literally run before `StackStatusReport` exists, so run it as
a **throwaway spike**: a few lines wired directly in `AppHost.cs`, observed,
then discarded — not committed.

**Done when** the throwaway dump is pasted into the PR body showing (a) lines
appearing while resources are still `Waiting`/`Starting`, (b) which of
`AfterResourcesCreatedEvent` / `ApplicationStarted` produced that, (c) the full
set of distinct states observed, and (d) whether any `IResourceWithoutLifetime`
resource reached the stream. **Blocked outcome:** if no hook yields a live
watch, stop and escalate per spec §8 — do not reach for the gRPC resource
service.

---

### `[T002] [P] [US1]` `test-writer` — the gate opens over a missing resource

Add to `scripts/wait-for-e2e-stack.test.mjs`. Extend the existing stub
machinery — do **not** rewrite it, and do **not** touch the three existing
tests.

The stubs describe a stack that satisfies every existing probe: three ports
serving, gateway 401, all nine databases migrated. The only thing wrong is a
status report naming `minio` as `FailedToStart` and `audit-observability` as
`Waiting`. Point the script at it with `STACK_STATUS_FILE`.

**Assert** the script exits non-zero.

**Done when** the test is observed **FAILING** and the output is captured
verbatim. Expected failure: the script exits 0. Message should say so in the
issue's own words — *the gate opened over a stack missing minio (#2268)*.

---

### `[T003] [P] [US1]` `test-writer` — "I could not ask" is not "it started"

Same file. Two more tests, both red:

1. No status report at all for the whole window → non-zero exit, and the
   message names `StackStatusFile`.
2. A report in which `migrations` is `Finished` with exit code `1` → non-zero
   exit, and it does not spend the full window first (assert it returns quickly
   — the `sleep` stub is instant, so assert on output shape, not wall clock).

**Done when** all three new tests are red and the three pre-existing ones are
green and unmodified. Paste `node --test scripts/wait-for-e2e-stack.test.mjs`
output verbatim.

---

### `[T004] [US1]` `infra-engineer` — the gate reads the report

Depends on T002, T003. Add the new first section to
`scripts/wait-for-e2e-stack.sh` per `plan.md` §2.5.

Constraints: path from `${STACK_STATUS_FILE:-${GITHUB_WORKSPACE:-$PWD}/stack-status.tsv}`;
`Running` or `Finished`+`0` is started, everything else is not; non-zero
`Finished`/`Exited` is immediately fatal; a missing file is fatal; keep
`set -uo pipefail` without `-e`; do not change the migration, port or gateway
sections.

**Done when** T002 and T003 are green, the three existing tests are green and
their assertions byte-identical (`git diff` shows no change inside them), and
`pnpm test:guards` passes.

---

### `[T005] [P] [US1]` `test-writer` — the writer's guard (Track B)

New `tests/Integration.Tests/AppHostStackStatusTests.cs`,
`[Trait("Category", "FixtureLogic")]`. Mirror `AppHostE2ESwitchTests` — read
its conventions first; it already reads `ci.yml` and builds the model without
starting it.

Two facts: (1) the e2e job's boot command in `.github/workflows/ci.yml` sets
`StackStatusFile=`; (2) the expected set computed from a model built with the
e2e job's arguments contains the project and container resources and excludes
every `IResourceWithoutLifetime` resource.

**Assertion discipline:** fact 2 must not be able to pass by checking its own
input. Assert against named resources the composition really has
(`audit-observability`, `minio`, `migrations` present; the secret parameters
absent), so the subject can change without the assertion text changing.

**Done when** both are observed **FAILING** — fact 1 because the workflow has no
such argument, fact 2 because `StackStatusReport` does not exist. Verbatim
output captured.

---

### `[T006] [US1]` `infra-engineer` — `StackStatusReport`

Depends on T001, T005. New `src/AppHost/StackStatusReport.cs` plus two lines in
`AppHost.cs` (`var app = builder.Build(); StackStatusReport.Attach(app, path); await app.RunAsync();`),
reading the path from `builder.Configuration["StackStatusFile"]` and doing
nothing when it is absent.

Per `plan.md` §2.2: seed from the model minus `IResourceWithoutLifetime`, write
immediately, watch and overwrite, write atomically via temp + `File.Move(…,
overwrite: true)`. Three fields per line, nothing else — no properties, no URLs,
no parameter values.

House rules: `Ensure.That` (or the composition-root exemption, stated in the
PR); explicit collection types with collection expressions; no leading
underscore on fields; `CancellationToken` last; no `ConfigureAwait`.

**Done when** T005 is green, `dotnet build -c Release` is clean (warnings are
errors), `dotnet format --verify-no-changes` is clean, and
`--filter "Category=FixtureLogic"` is green.

---

### `[T007] [US1]` `infra-engineer` — observe a live boot end to end

Depends on T004, T006. The half no stub can prove. Run spec §4 steps 3, 4 and 5
verbatim against a real stack.

**Record in the PR body, as observed figures, not as claims:**

- the line count of the written report, and whether it matches the composition
  under `ScenarioSimulator=false`;
- the first dump taken while the stack was still coming up, showing non-Running
  states;
- the wall-clock duration of the gate's new section;
- the full output of the **counterfactual**: edit `minio`'s state to
  `FailedToStart` in the live file, re-run the gate, and show it exiting
  non-zero **and naming minio**.

A counterfactual that fails without naming the resource is a US2 blocker.
Revert the edit afterwards.

---

### `[T008] [US1]` `infra-engineer` — wire the workflow

Depends on T004, T006. `.github/workflows/ci.yml`, e2e job only:

- boot step: `rm -f "$GITHUB_WORKSPACE/stack-status.tsv"` before `nohup`, and
  `StackStatusFile="$GITHUB_WORKSPACE/stack-status.tsv"` after
  `ScenarioSimulator=false`. **Absolute path** — `plan.md` §2.6 says why.
- the `if: failure() || cancelled()` diagnostic step also dumps the report.
- `.gitignore`: `/stack-status.tsv`.

**Done when** T005's fact 1 is green and the workflow parses.

---

## US2 (P2) — The refusal names the offenders

### `[T009] [US2]` `infra-engineer` — the failure report

Folded into T004's section but verified separately: the timeout message lists
every not-started resource as ` <name>(<state>)`, matching
`unmigrated_databases`' existing rendering, and names **no** resource that
started.

**Done when** a test in `wait-for-e2e-stack.test.mjs` asserts both directions —
the offenders present *and* a healthy resource absent from the message — and
T007's counterfactual output shows the real thing.

---

## US3 (P3) — The gate cannot silently stop being asked

### `[T010] [US3]` `infra-engineer` — close out and measure

- Confirm NetArchTest / `PrimitiveBoundaryTests` / `HandlerDeconstructionTests`
  still green (nothing here should touch them; confirm rather than assume).
- From the first green CI run, record the observed duration of the gate's new
  section and the e2e job's total, and compare the total against the 45-minute
  `timeout-minutes`. Write the figures into the PR body — a measurement reported
  only in conversation is invisible to every later grep.
- PR body: the three verbatim red outputs (T002, T003, T005), the counterfactual
  (T007), the measured figures, and `Phase 6: security review not warranted —
  <reason>`.
- Verify #2268's state and labels after the merge; a mention alone rarely closes
  an issue.

---

## Close-out checklist

- [ ] Phase 4a red observed for T002, T003, T005; output quoted in the PR
- [ ] The three pre-existing mjs tests green and **unmodified** either side
- [ ] Phase 5 done by hand — live boot, live gate, live counterfactual
- [ ] Release build, `dotnet format`, `pnpm test`, `Category=FixtureLogic` all green
- [ ] Gate duration and job total recorded as figures
- [ ] PR references #2268 with a closing keyword; issue state checked after merge

## What green CI will *not* prove

A green e2e job after this change proves the gate did not refuse. It does
**not** prove the gate *would* refuse — that is only ever shown by T007's
counterfactual, and it is the single most important line in the PR body.
