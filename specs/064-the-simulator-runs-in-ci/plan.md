# Implementation Plan: The simulator runs in CI

**Feature Branch**: `fix/2013-the-simulator-runs-in-ci`

**Spec**: [`spec.md`](./spec.md)

**Issues**: #2013

**ADRs**: ADR-0111 (implemented, not amended), ADR-0138 (closes its open
finding), ADR-0024/0025 (Aspire composition root), ADR-0144 (lane), ADR-0091
(naming), ADR-0037 (phases), ADR-0108 (the e2e job this changes).

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**Infra.** Every file touched is infrastructure or a comment about it:

- `src/AppHost/AppHost.cs` — the Aspire composition root (ADR-0024). The
  infra-engineer brief owns this file and is the only brief that records the
  AppHost's editorconfig exemption and the run-mode/E2E divergence.
- `.github/workflows/ci.yml` — the e2e job's boot line.
- `tests/Integration.Tests/AppHostE2ESwitchTests.cs` — a model-only AppHost
  test, sibling to the file being changed.

The comment corrections land in `e2e/*.ts` and one `apps/shared` test file,
which would normally be frontend territory. They are **comments only** — no
assertion, no locator, no import changes — and they describe the AppHost's
composition, which is why they were wrong. Splitting them to a second engineer
would hand the frontend brief a file whose truth it cannot verify.

**No backend engineer is needed.** No `src/<Context>/` code is touched; the
`ScenarioSimulator` project itself is not modified, only whether the AppHost
composes it.

### Declaration 2 — is the honest answer a new ADR or an amendment?

**No. The run is not blocked.**

This was the orchestrator's central question and `spec.md` answers it at length.
The short form:

- **ADR-0111 already decided** the simulator is dev-only, with *"prod/CI are
  untouched"* as its stated cost basis. That decision is not in question and
  gains no new words.
- The defect is that `E2ETests` was the wrong *mechanism* for delivering that
  decision, because it also gates the three Vite apps the Playwright suite
  drives (`AppHost.cs:452`) and spec 056's `fixture-video` (`AppHost.cs:163`).
  Choosing a correct mechanism for an already-recorded decision is
  implementation, not architecture.
- ADR-0138 already recorded the discrepancy as an **open finding**, in the
  repository's own words. This feature closes that finding. It does not
  contradict, extend or reinterpret any ADR.

**The branch that would have blocked the run** was the issue's second option —
amend ADR-0111 to say CI legitimately runs the simulator. It was rejected on
evidence, not preference: nothing in CI reads the simulator's cameras or its
video (spec.md, Finding A), so there is nothing to acknowledge. Had a single
e2e assertion depended on those 12 cameras, that branch would have become the
honest answer and this plan would say **blocked** instead.

**The one thing that could still block it at phase 6.** If a reviewer concludes
the new switch is itself an architectural decision requiring an ADR, the lane
must stop rather than write one. The argument against that is above and in
`spec.md`; it is recorded here so the disagreement is visible rather than
discovered.

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing. Phase 4a is red.** The mix is real and is stated:

| Change | Colour | Why |
|---|---|---|
| The `ScenarioSimulator` switch + the guard | **changing** | Two resources stop being composed under an argument that today does nothing |
| The `ci.yml` boot argument | **changing** | The CI stack's resource set changes; two containers stop starting |
| Six comment corrections | preserving | No executable line moves |

**It resolves to changing, and it is one issue, not two.** The comments are only
true *because* of the code change — committing them alone would land a comment
that is false until its successor arrives, which is the defect being fixed,
delivered twice. And ambiguity resolves to red by rule (CLAUDE.md §the
autonomous lane): red fails loudly if the test was never actually failing;
characterisation passes quietly and would prove nothing here.

**What "red" means concretely.** Not a hypothetical. Today, building the
application model with `ScenarioSimulator=false` yields a model that **contains
`camera-sim`**, because nothing reads that key. The new test asserts it does
not, and must be observed failing on that exact assertion before the guard is
written. The verbatim output is quoted in the PR (ADR-0139).

---

## Phase 4a/4b design — the shape the change lands in

### The switch — one line, mirroring the one beside it

`src/AppHost/AppHost.cs:15` already reads:

```csharp
bool isE2ETests = bool.TryParse(builder.Configuration["E2ETests"], out bool e2e) && e2e;
```

The new line sits directly beneath it and is deliberately the same shape,
inverted so the default is *on*:

```csharp
// ADR-0111 records the simulator as dev-only. `E2ETests` alone never delivered
// that: CI's e2e job boots with a plain `dotnet run` — run mode, flag unset —
// so the simulator has started there for as long as it has existed (#2013). It
// cannot simply join the `E2ETests` gate, because that flag also removes the
// three Vite apps the Playwright suite drives and spec 056's `fixture-video`.
// Absent or unparseable means a developer's `aspire run`, the only place it is
// wanted.
bool isScenarioSimulatorEnabled =
    !bool.TryParse(builder.Configuration["ScenarioSimulator"], out bool simulator) || simulator;
```

**Naming (ADR-0091):** `ScenarioSimulator` in full, matching the resource name
`scenario-simulator` and the project `src/ScenarioSimulator`. No `Sim`, no
`Enabled` suffix on the key — the key names the thing, the value says whether.

**Why a command-line argument and not an environment variable.** `E2ETests`
already travels as a bare `key=value` argument, and that path is *proven* to
reach `builder.Configuration`: `AspireFixture.cs:85` and
`AppHostE2ESwitchTests.cs:25` both pass it that way and the entire integration
suite rests on it working. An environment variable would be a second mechanism
that needs its own proof. One mechanism.

### The guard — one conjunct added, one comment corrected

`src/AppHost/AppHost.cs:548` (`:518` before this change):

```csharp
if (isRunMode && !isE2ETests && isScenarioSimulatorEnabled)
```

and the block comment at `:508`, which currently says *"so CI/E2E/prod never see
it"* and is the sentence the issue is about, is rewritten to describe what the
three conjuncts each exclude.

**Nothing else in the file changes.** Not `fixture-video`, not the Vite apps,
not the data-volume or pgAdmin guards. Their `E2ETests` gate means *"this is the
integration fixture"* and is correct (spec.md scope ruling 3).

### The CI boot line

`.github/workflows/ci.yml:250` today:

```yaml
nohup dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj -c Release --no-build > apphost.log 2>&1 &
```

becomes:

```yaml
nohup dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj -c Release --no-build -- ScenarioSimulator=false > apphost.log 2>&1 &
```

**The `--` separator is load-bearing** and is the one thing phase 4b must verify
at runtime rather than assume: `dotnet run` consumes options before `--` and
forwards everything after it to the application. Without it, `dotnet run` would
try to interpret `ScenarioSimulator=false` itself. This is the single mechanical
risk in the change, and step 3 of the verification procedure (`spec.md`) is what
catches it — the dashboard either shows the two resources or it does not.

### Files touched

| File | Change | Colour |
|---|---|---|
| `src/AppHost/AppHost.cs` | switch (~6 lines incl. comment), one guard conjunct, block comment at `:508` | changing |
| `.github/workflows/ci.yml` | `-- ScenarioSimulator=false` on the boot line | changing |
| `tests/Integration.Tests/AppHostE2ESwitchTests.cs` | new tests; **existing two untouched** | the red |
| `e2e/kiosk-shows-a-wall.spec.ts` | comment | preserving |
| `e2e/support/seed-published-layout.setup.ts` | comment | preserving |
| `e2e/camera-detail.spec.ts` | comment | preserving |
| `e2e/layouts.spec.ts` | comment | preserving |
| `apps/shared/src/observability/kioskLatency.test.ts` | comment | preserving |
| `src/AppHost/Resources/README.md` | quoted guard expression | preserving |
| `docs/design/scenario-simulator-m2.md` | quoted guard expression (3 places) | preserving |

`docs/adr/0111-scenario-simulator.md` — **not touched.** `docs/adr/0138-*.md` —
**not touched**; its finding is closed by the code, not by editing the record.

### The comment rewrites — the rule they must follow

Two of the six comments do not simply become true (`spec.md`, the table). The
rule for all six:

> **Say what this test is blind to and why, from its own facts. Do not restate
> a stack-wide claim about video.**

- `e2e/camera-detail.spec.ts` — its camera points at an address nothing serves,
  so it gets no picture because it never asked for one. The simulator is
  irrelevant to it and should not be mentioned.
- `apps/shared/src/observability/kioskLatency.test.ts` — a vitest unit test in
  Node. It has no browser, no WebRTC and no stack at all; *"CI has no video"*
  was never the reason it cannot prove a number came from a frame.
- `e2e/kiosk-shows-a-wall.spec.ts` — it must not say CI has no video:
  `fixture-video` exists and spec 056 uses it. Nor may it name *which* wall it
  opens: `openFirstLayout` takes whichever published layout sorts first by name
  (`ListLayoutsQueryHandler`, `OrdinalIgnoreCase`), and by the time the `kiosk`
  project runs, `layouts.spec.ts` has already published `E2E Race Layout
  <stamp>`, which sorts ahead of `Kiosk Seed Wall`. The invariant is that every
  wall this suite seeds *except spec 056's* points at an unserved `10.0.5.x`
  address — so no frame can arrive whichever one wins the sort.
- `e2e/support/seed-published-layout.setup.ts`, `e2e/layouts.spec.ts` — the
  empty-catalogue claim becomes true and the citation should be the new guard.
- `src/AppHost/AppHost.cs:525` (`:508` before this change) — the guard describes
  itself accurately.
- **A seventh, created by the change itself:** `src/AppHost/AppHost.cs:148`
  said `fixture-video` was *"gated exactly as `camera-sim` is"* — true until
  `camera-sim` gained its third conjunct, false after. It now records that the
  two gates deliberately differ, because folding them together would delete
  spec 056's video source from CI.

**No assertion, locator, timeout or import may change in these files.** If a
diff in `e2e/` or `apps/shared/` contains anything but comment lines, the change
overreached.

---

## Architecture

### Bounded context and layers

**None.** This is composition and CI configuration. No bounded context is
entered; no Domain, Application, Infrastructure or Api file is opened.
`src/ScenarioSimulator` is not modified — only whether the AppHost composes it.

### Entities, value objects, invariants

**None, and §II does not bite.** The one new local is a `bool` in
`src/AppHost/AppHost.cs`. Constitution §II bans primitive-typed state on a
**domain model**; `PrimitiveBoundaryTests` scans `src/*/Domain`. The AppHost is
not a domain model, holds no state, and is separately exempt from the var/braces
editorconfig rules — a value object here would be the speculative generality
CLAUDE.md forbids, wrapping a switch that is read exactly once.

The existing `isE2ETests` line beside it is the precedent and the reason the new
line copies its shape.

### Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change.
The simulator's own `CameraRegisteredV1` subscription is untouched; it simply
does not run in CI.

### Boundary rules

No cross-context project reference is added or removed. NetArchTest's rules are
unaffected — nothing under `src/<Context>/` is opened.

### Invariants this change must not break

1. `E2ETests=true` still removes `camera-sim`, `scenario-simulator` and
   `pgadmin` — `AppHostE2ESwitchTests` must pass **unmodified**.
2. `E2ETests=true` still leaves Postgres without a data volume — same file, same
   rule.
3. A developer's `aspire run` with no arguments still composes the simulator.
4. `fixture-video`, `management-web`, `kiosk-web` and `kiosk-wall` are still
   composed in CI's run-mode boot.

Invariant 1 and 2 are guarded by tests that already exist and are not edited.
Invariants 3 and 4 need the new tests in T002.

---

## Constitution and ADR alignment

| Rule | How this complies |
|---|---|
| ADR-0024/0025 — Aspire is the composition root | The switch lives in `AppHost.cs`; no connection string is hand-wired |
| ADR-0111 — simulator is dev-only | **Implemented**, not amended. Its stated consequence becomes true |
| ADR-0138 — open finding | Closed by the code; the ADR text is not edited |
| ADR-0144 — the lane may not write or amend an ADR | Nothing in `docs/adr/` is written or edited |
| ADR-0144 — may not weaken a gate to reach green | No test deleted, no threshold lowered, no suppression added, no analyzer narrowed. **Two tests gained; none removed** |
| ADR-0139/CLAUDE.md — new behaviour starts red | T001 observes the failure and quotes it verbatim in the PR |
| ADR-0091 — no shortcuts or aliases | `ScenarioSimulator`, matching the resource and project names |
| ADR-0052/0053 — xUnit + Shouldly, sentence-style names | The new tests follow the file they join |
| ADR-0030/0086 — Conventional Commits, no `Co-Authored-By` | See `tasks.md` |
| CLAUDE.md — smallest possible change | One conjunct, one argument, six comments. No guard is refactored |
| CLAUDE.md — no drive-by comments | Every comment touched is one the issue identifies as false; none is added for decoration |
| Constitution §IV | N/A — no leg touched. Reasoned in `spec.md` |
| Constitution §II | N/A — no domain model touched. Reasoned above |
| ADR-0084 — 300 LOC/file | `AppHost.cs` grows by ~8 lines. It is a composition root and is not subject to the per-file metric the same way a domain file is; the delta is negligible either way |

---

## Risks

**R1 — a spec passes today for a reason nobody wrote down.** The whole change
rests on Finding A, which is a *reading* of the e2e suite. A spec that quietly
depends on a non-empty catalogue at boot would go red in CI and nowhere else.

*Mitigation:* this is exactly what phase 5 is for. The full Playwright suite is
run against a stack booted with the argument (verification step 5), and the PR
is not opened on the strength of the grep. **If a spec fails, the finding is
that the spec has an undeclared dependency — not that the change is wrong** —
and it is fixed by making that spec seed its own data, as every other spec
already does.

**R2 — `--` does not forward the argument.** Then CI boots with the simulator
still running and every test still passes, so **CI cannot detect this failure**.

*Mitigation:* verification step 3 reads the dashboard directly, and step 4 reads
the catalogue count. A green suite is explicitly not accepted as evidence for
this one.

**R3 — a typo in `ci.yml` silently restores the bug.** The fail-open default
(spec.md) means `ScenarioSimulator=flase` composes the simulator with no error.

*Mitigation:* T003 reads `ci.yml` and asserts the argument is present, spelled
exactly. This is the weak kind of guard — it proves the workflow file says
something, not that the stack did something (memory: *"Guards that read the
design artefact"*). It is included anyway because R3 is a defect *in the
workflow file*, which is precisely what such a guard can catch, and it is
labelled with its limits in the test's own comment.

**R4 — a reviewer reads the new switch as an architectural decision.** Then the
lane must stop rather than write the ADR.

*Mitigation:* none available; recorded in Declaration 2 so the disagreement
surfaces at phase 6 rather than after the merge.

**R5 — the e2e job's timing changes.** Removing a container and a worker from a
warm-up window may reorder what becomes ready first, and the wait script's
gateway probe is already best-effort.

*Mitigation:* watched at phase 5. Not expected to be adverse — the change
removes contention rather than adding it — and explicitly **not claimed as an
improvement** without a figure.

---

## What is explicitly not being built

- No wait-for-seeding predicate. The race dissolves (spec.md scope ruling 1).
- No rationalisation of the other `isRunMode && !isE2ETests` guards.
- No edit to `docs/adr/0111-*.md` or `docs/adr/0138-*.md`.
- No change to any e2e assertion.
- No measurement of the e2e job's duration, and no claim about it.
