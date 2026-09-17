# Spec 174 — Tasks

**Issue**: #2209 · **Branch**: `fix/2209-a-limit-the-build-can-see` · **Phase**: 3 (Tasks)

**Spec**: `spec.md` · **Plan**: `plan.md`

---

## Declarations (ADR-0144)

| Declaration | Value |
|---|---|
| **Engineer (phase 4b)** | **`infra-engineer`** — confirmed. The whole change is build configuration (`.editorconfig`, a new `SonarLint.xml`, `Directory.Build.props`) plus documentation. No `.cs` is written, so `backend-engineer` has nothing to do here |
| **Phase 4a colour** | **Mixed, and the split is declared explicitly** — see below. **GREEN characterisation** for the configuration/documentation change itself; **plus one small RED→GREEN counterfactual pair** that is the actual evidence the rules now fire |
| **New ADR** | **None. ADR-0084 is amended, and that amendment is explicitly authorized** — the repository owner made the decision in this session (spec.md, opening section). ADR-0144's "the lane may not write an ADR" bars the lane from *deciding*; the decision is made, recorded, and phase 4 is transcribing it. **Phase 6 must not raise this as an overstep** |
| **Board** | #2209 is already on Project #13, status **Todo** — `gh issue view 2209` reports `projects: Smart Sentinel Eye (Todo)`. **No `item-add` needed** |
| **Skipped phases** | **None.** Phase 5 (`/verify`) is *not* skipped despite this being documentation-adjacent: the observable behaviour — a build that warns where it was silent — is exactly what phase 5 exists to witness |
| **Latency budget** | **N/A.** No leg of constitution §IV is touched (spec.md, final section) |
| **Aspire stack** | **Running, pid 3312, `SmartSentinelEye.AppHost` — confirmed by `tasklist`. It is not needed by this change and must not be stopped.** See "The stack and the measurement build", below |

### Phase 4a colour, stated precisely

The bulk of the diff is configuration and prose, which changes no application
behaviour — **characterisation, observed green**: the existing suite must pass
before and after, unmodified.

But the *point* of the change is a behaviour the build did not have, and
constitution §Testing's red obligation attaches to it. That behaviour is not
testable by xUnit — it is a compiler diagnostic — so the red is produced and
quoted as a **build counterfactual** (T001–T003), not as a test file.

**This is deliberately not an architecture test that greps `.editorconfig` for
`S107`.** Such a test proves the design was written down, not that it holds, and
its assertion would check its own input — two failure modes this repository has
recorded and been bitten by. The subject here is the *compiler's output*, and
that is what gets asserted.

### Files phase 4 may touch — exhaustive

**Configuration:**

- `.editorconfig`
- `SonarLint.xml` *(new, repository root)*
- `Directory.Build.props`

**Documentation:**

- `docs/adr/0084-code-metrics-sonaranalyzer.md`
- `CLAUDE.md` *(line 489 only)*
- `CONTRIBUTING.md` *(lines ~207-216 only)*
- `.claude/agents/backend-engineer.md` *(line 18 only)*
- `.claude/agents/backend-reviewer.md` *(line 15 only)*
- `docs/design/scenario-simulator-m2.md` *(line ~533 only)*

**Transient, never committed:**

- `src/Shared.Kernel/MetricProbe.cs`
- `tests/Shared.Kernel.Tests/MetricProbe.cs`

**Anything else is out of scope**, in particular: `.specify/memory/constitution.md`
(it says nothing about these limits — verified), any file under `src/**/*.cs` or
`tests/**/*.cs` other than the two transient probes, `Directory.Packages.props`,
`.github/workflows/`, and `CONTRIBUTING.md`'s `var` / `dotnet format` paragraphs
(lines ~218-235 — a separate defect, filed in T012, not fixed here).

### The stack and the measurement build

The counterfactual (T001-T003, T007) builds **one project at a time**
(`src/Shared.Kernel`, `tests/Shared.Kernel.Tests`), whose Release output
directories the running Debug stack does not hold. That works with pid 3312 up.

**T008's solution-wide measurement build does not.** A full
`dotnet build SmartSentinelEye.slnx -c Release` against a running AppHost fails
with **MSB3027** on locked service binaries, and that failure reads exactly like
a broken build. Phase 4 must, in order:

1. **Ask the orchestrator** whether the stack may be stopped for the measurement,
   and if so stop it, measure, and say so. **Phase 4 does not stop pid 3312 on
   its own initiative.**
2. If the answer is no, redirect the output —
   `-p:BaseOutputPath=<scratch>/metricsbin/` — and say in the PR that the count
   came from a redirected build.
3. If neither works, **report and park**. Do not record a partial count as the
   baseline: a build-based count needs the build to finish, or the number is a
   floor and the ADR would carry a lie.

---

## Phase 4a — the counterfactual and the characterisation (`test-writer`)

Verbatim output of every run below goes to `infra-engineer` as its brief and into
the PR body. **`infra-engineer` may not edit these probes to change the outcome.**

### The probe, defined once

`src/Shared.Kernel/MetricProbe.cs` — a single file containing exactly two
deliberately non-compliant members and nothing else:

- `public static int EightParameters(int a, int b, int c, int d, int e, int f, int g, int h)`
  — **8 parameters**, above ADR-0084's 4 *and* above S107's own default of 7, so
  the "before" result cannot be explained away as the threshold not yet tightened.
- `public static int FortyEightLines()` — a method body of **48 lines**, above
  ADR-0084's 30 and below S138's default of 80, so its "before" silence is
  explained by the threshold and its "after" warning proves `SonarLint.xml` is
  read.

Between them the two members prove *both* mechanisms: the `.editorconfig`
severity line (S107, which cannot fire at any threshold without it) and the
`SonarLint.xml` threshold (S138, which fires only because 80 became 30).

The file must be short enough not to trip S104 itself, and its two methods must
be simple enough not to trip S1541 or S134 — otherwise the counterfactual
confounds four rules into two. `test-writer` verifies this by the "before" run
producing **zero** diagnostics of any of the five.

- **[T001] [US-1]** Create `src/Shared.Kernel/MetricProbe.cs` as specified above.
  **On the base tree** — no configuration change applied yet.
  Run and capture verbatim:
  `dotnet build src/Shared.Kernel/SmartSentinelEye.Shared.Kernel.csproj -c Release --no-incremental`
  **Expected: succeeds, and zero S104 / S107 / S138 / S1541 / S134.**
  This is **the RED** — the rule that does not fire. Quote the command, the
  warning-free output and the exit code.

- **[T002] [P] [US-1]** Create `tests/Shared.Kernel.Tests/MetricProbe.cs` with the
  same two members (namespace adjusted). **On the base tree.** Build that project
  in Release, capture verbatim. **Expected: zero diagnostics** — the "before" half
  of AS-4, establishing that the test exemption's *observable* state does not
  change across this spec. `[P]` with T001: different projects, different files.

- **[T003] [US-1]** *(after T001)* Capture the existing characterisation baseline:
  run the unit suite (`dotnet test` over the Domain/Application/Shared test
  projects, excluding `Integration.Tests`) and record it **green**. These tests
  must pass **unmodified** after the change; an assertion that has to be edited
  is evidence something moved, and is a block, not an adjustment.

**Gate for 4a:** T001 shows zero of the five. If it shows *any*, the premise is
wrong — the rules are already partly on — and phase 4 stops and reports rather
than proceeding on a measurement that contradicts spec.md's premise table.

---

## Phase 4b — implementation (`infra-engineer`)

### Foundational — blocks everything after it

- **[T004] [US-1]** `.editorconfig`: add the `[src/**.cs]` and `[tests/**.cs]`
  sections exactly as designed in plan.md, **placed after the main `[*.cs]` block
  and before the first `[**/Migrations/*.cs]` section**. Ordering is load-bearing
  (plan.md explains why); do not append at the end of the file.

- **[T005] [US-1]** *(after T004 — same conceptual unit, but a distinct file)*
  Create `SonarLint.xml` at the repository root with all five `<Rule>` entries as
  designed. Exact filename, exact parameter keys.

- **[T006] [US-1]** *(after T005)* `Directory.Build.props`: the three edits of
  plan.md — the `AdditionalFiles` `ItemGroup` scoped to `'$(IsTestProject)' !=
  'true'`, the `WarningsNotAsErrors` line inside the existing production
  `PropertyGroup`, and the three comment corrections.

**T004-T006 are one indivisible change.** Committing any subset leaves the repo in
a state where either the rules fire as Release *errors* (T004 without T006) or the
thresholds are wrong (T004 without T005). Every commit must build on its own
(CLAUDE.md, rebase-merge and `git bisect`), so **these three land in a single
commit.**

### The green half of the counterfactual

- **[T007] [US-1]** *(after T006)* With the probes from T001/T002 still in place,
  re-run both builds and capture verbatim:
  - `src/Shared.Kernel` in Release → **S107 naming `EightParameters`, S138 naming
    `FortyEightLines`, exit code 0.** Exit code 0 is not incidental: it is the
    proof that `WarningsNotAsErrors` neutralises `TreatWarningsAsErrors` for
    these IDs (SC-3), and it is the single most likely thing to be wrong.
  - `tests/Shared.Kernel.Tests` in Release → **still zero of the five** (SC-4,
    AS-4). This is the proof the test exemption survived `.editorconfig`
    outranking `NoWarn`.
  - Re-run T003's suite → **still green, unmodified** (the characterisation).

  **Then delete both probe files.** `git status` must show them gone before any
  commit. SC-6 checks this.

- **[T008] [US-1]** *(after T007)* Run the baseline measurement — the exact command
  that goes into the ADR — subject to the stack coordination above. Record the
  **observed** per-rule counts. Expected 5 / 116 / 84 / 7 / 2 = **214**.
  - If S1541 ≠ 7 or S134 ≠ 2, `SonarLint.xml` is doing something to rules whose
    threshold it did not change — investigate before recording.
  - If the total is an order of magnitude high, generated code is being analysed
    (AS-5) — check that no S104 names a `Migrations/` file before recording.
  - **Whatever is observed is what the ADR records**, with the discrepancy from
    214 called out in the PR. The figure is a measurement, not a target.

### Documentation — parallelisable, disjoint files (ADR-0109)

All of these depend on **T008** (they quote its number or its conclusion) and on
nothing else. Each owns a distinct file, so they run in parallel.

- **[T009] [US-1]** *(after T008)* `docs/adr/0084-code-metrics-sonaranalyzer.md`:
  Correction A, Correction B, and the appended amendment block, with T008's
  measured figures filled into the baseline table and the reproduction command
  verbatim. This is the authority; the others point at it.

- **[T010] [P] [US-1]** *(after T008)* `CLAUDE.md` line 489 — the designed row.
  **No new prose section** (plan.md explains why). Do not restate the baseline
  count here; point at the ADR.

- **[T011] [P] [US-1]** *(after T008)* `CONTRIBUTING.md` lines ~207-216 only —
  items 5, 6 and 7 of plan.md's sweep table. **Do not touch lines ~218-235.**

- **[T012] [P] [US-1]** *(after T008)* `.claude/agents/backend-engineer.md:18`
  and `.claude/agents/backend-reviewer.md:15` — move the five limits out of the
  "CI-enforced" list and label them advisory. Two files, one task because the
  correction is the same sentence twice and they must not diverge. **These two
  briefs are load-bearing when no human is watching**; CLAUDE.md says so, citing
  the eleven days two briefs said "NRT disabled" after ADR-0141 enabled it.

- **[T013] [P] [US-1]** *(after T008)* `docs/design/scenario-simulator-m2.md`
  line ~533 — "advisory".

- **[T014] [P] [US-1]** File **one** issue for the two `CONTRIBUTING.md` defects
  found in passing (the `var` / "never `var`" contradiction, and the claimed
  `dotnet format --verify-no-changes` CI step that does not exist). Label
  `agent:ready`, add to Project #13. **Do not fix them here.** No file in this
  repository is touched by this task.

### Verification of the sweep

- **[T015] [US-1]** *(after T009-T013)* Run and paste into the PR:
  ```sh
  grep -rniE "0084|300 LOC|code.metric|cyclomatic|nesting" \
    --include=*.md --include=*.props --include=.editorconfig . \
    | grep -v node_modules | grep -v "^./specs/"
  ```
  Every hit must be either corrected or on plan.md's "unchanged, and why" list
  (items 12-15). **A hit that is on neither list is a miss**, and a partial sweep
  is the failure mode this whole spec exists to correct.

---

## Dependency graph

```
T001 ──┬─ T003 ──────────────────────────────────┐
T002 ──┘                                          │
        └─────► T004 ─► T005 ─► T006 ─► T007 ─► T008 ─┬─► T009
                        (one commit)                   ├─► T010 [P]
                                                       ├─► T011 [P]
                                                       ├─► T012 [P]
                                                       ├─► T013 [P]
                                                       └─► T014 [P]
                                                             │
                                                    T009..T013 ─► T015
```

**Foundational block: T004-T006** — one commit, blocks all evidence and all
documentation. Nothing fans out before it.

**Fan-out point: after T008.** Six documentation tasks own six disjoint files and
can run concurrently.

---

## Commits (ADR-0030, ADR-0086 — Conventional Commits, **no `Co-Authored-By`**)

1. `build: configure ADR-0084's five code-metric limits as advisory warnings`
   — T004, T005, T006 together. Body quotes T001's zero-warning "before" output.
2. `docs(adr): record ADR-0084's limits as advisory and their measured baseline`
   — T009.
3. `docs: correct the enforcement claim for ADR-0084's code metrics`
   — T010-T013.

**No `Co-Authored-By` footer on any commit** (ADR-0086). The PR *description*
still carries the Claude Code lines; the commit messages do not.

---

## Phase 5 (`/verify`) — what must be observed, not asserted

Not "tests are green". The observation is: **a production method that was silent
now produces a named diagnostic, and the Release build still succeeds.** Phase 5
re-runs T007's two builds from a clean checkout of the branch and quotes both
outputs and both exit codes, plus T008's count. Latency: **N/A**, stated in the
note (constitution §IV is not on this path).

## Phase 6 (`/code-review`)

`/security-review` is **not** required — no trust boundary, no auth, no secret,
no network surface is touched. Reviewer should specifically check: that no `.cs`
file changed; that the probes are gone; that the sweep of T015 has no unlisted
hit; and that the ADR's recorded number is the one T008 actually measured rather
than the 214 this spec expected.
