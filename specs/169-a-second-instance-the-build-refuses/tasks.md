# Tasks 169 — A second instance the build refuses

**Spec:** `specs/169-a-second-instance-the-build-refuses/spec.md`
**Plan:** `specs/169-a-second-instance-the-build-refuses/plan.md`
**Issue:** #2404 (on Project #13, status Todo — verified, no `item-add` needed)

---

## Declarations (ADR-0144, restated here so phase 4 need not go hunting)

- **Engineer role:** `infra-engineer`.
- **Phase 4a colour:** **RED — behaviour-changing.** The guard is green against
  today's correct code, so **green is not evidence**. The red is T002's four
  constructed counterfactuals, each observed failing, output quoted verbatim in
  the PR body. **CF-2 is mandatory.**
- **New ADR needed:** **No.** ADR-0153 is accepted and decides everything this
  slice implements. Stop and escalate if the work drifts into deciding the
  gateway's replica count (#2283), designing clause 3's harness, or editing
  ADR-0153 or the constitution.
- **Files phase 4 may touch:**
  - `tests/Integration.Tests/AppHostReplicaCountTests.cs` (new)
  - `src/AppHost/AppHost.cs` — the `// HA (#1005)` comment block **only**
  - `specs/169-a-second-instance-the-build-refuses/*`
  - Nothing else. No ADR, no constitution, no `deploy/`, no bounded-context
    source, no `ci.yml`.

**On parallelism, honestly.** This slice is small and mostly sequential. Exactly
one task is genuinely `[P]` (T003 — a disjoint file whose only dependency, the
guard's class name, is fixed by the plan rather than by T001's output).
Marking more would be decoration.

---

## User story US-1 (P1) — Adding a second instance fails the build

### T001 — [US-1] Write the replica guard

**File:** `tests/Integration.Tests/AppHostReplicaCountTests.cs` (new)

Write `AppHostReplicaCountTests` with `[Trait("Category", "FixtureLogic")]` and
**no** `[Collection(AspireCollection.Name)]`.

Three tests, per plan §3.2:

1. `A_run_mode_stack_composes_only_the_recorded_exception_above_one_instance`
   — compose with the four `Parameters:*` arguments and no `E2ETests`; assert the
   set of `(name, count)` pairs with count > 1 equals exactly
   `{ ("api-gateway", 2) }`, as **one set comparison**, not a loop of checks.
2. `The_integration_lane_composes_every_service_at_one_instance`
   — compose with those arguments plus `E2ETests=true`; assert no resource
   exceeds 1 replica **and** `api-gateway` is present at exactly 1.
3. `The_composed_model_is_read_before_any_replica_count_is_judged`
   — the population gate: the model is non-empty and contains `api-gateway`, so
   a model that failed to compose fails rather than passing silently.

Requirements:

- Read counts with `ResourceExtensions.GetReplicaCount(IResource)`, not by
  poking `Annotations.OfType<ReplicaAnnotation>()`.
- `CreateAsync` only — never `BuildAsync` or `StartAsync`. An Aspire stack is
  running on this machine (pid 3312) and this must be safe beside it.
- Mirror `AppHostE2ESwitchTests` for the argument arrays and general shape; do
  not invent a new pattern.
- Failure messages per plan §3.4: name the resource and its count; name
  ADR-0153 clause 1 and its mechanism; name clause 3 as the only route to an
  exception; for `api-gateway`, name #2283 as the owner of its count.
- Cite `AppHost.cs` by quoted comment text, **never by line number** (spec §2.1).
- Conventions per plan §3.5 (Shouldly, sentence-style names, collection
  expressions with explicit types, no leading underscore).

**Done when:** the class compiles and all three tests pass against unmodified
`develop`. *This alone proves nothing* — T002 is the evidence.

**Depends on:** nothing.

---

### T002 — [US-1] Construct the four counterfactuals and capture the red

**Files:** temporary edits to `src/AppHost/AppHost.cs`, each **reverted** before
the next. No committed change.

Run each counterfactual from spec §6, capture **verbatim** test output, revert:

| # | Construct | Must be |
|---|---|---|
| CF-1 | `.WithReplicas(2)` on `layoutComposition` | red, naming `layout-composition`, citing clause 3 |
| CF-2 | a replica count on `automation` set through an indirection a source search for `WithReplicas` on that line would miss (e.g. a local `static void ScaleOut(IResourceBuilder<ProjectResource> r) => r.WithReplicas(3);`) | red, naming `automation` |
| CF-3 | `apiGateway.WithReplicas(2)` → `WithReplicas(3)` | red, naming #2283 |
| CF-4 | remove the `if (!isE2ETests)` gate | the **E2E-lane** test red, naming `api-gateway` |

**CF-2 is mandatory and must not be skipped.** It is the only run that
distinguishes the guard from the source-text scan the spec rejected. **If CF-2
comes back green, stop — the guard has been built as a grep and T001 must be
redone.**

Note the recorded hazard: a restored file keeps its old timestamp, so MSBuild
may skip the rebuild and the test still shows the counterfactual's result after
you revert. Force a rebuild or touch the file between runs, and confirm the
guard is green again before moving on.

**Done when:** four verbatim failure outputs are captured, each reverted, and
the guard is green again on an unmodified tree.

**Depends on:** T001.

---

### T003 — [P] [US-1] Record the exception at the composition root

**File:** `src/AppHost/AppHost.cs` — the `// HA (#1005)` comment block **only**.

Amend the comment to record that `apiGateway.WithReplicas(2)` is ADR-0153's
recorded **clause-2 exception**, that **#2283** owns the choice between a shared
rate-limiter store and returning to one replica, and that
`AppHostReplicaCountTests` pins both the exception and the one-instance rule.

Do **not** change `WithReplicas(2)`, the `if (!isE2ETests)` gate, or any other
line. The runtime shape is identical after this task.

If it can be done in one clause without enlarging the task, also disambiguate
the existing phrase *"Kept to one instance under E2E tests"* — it means the
**integration fixture** (`E2ETests=true`), not the Playwright e2e job, and that
ambiguity is what misled ADR-0153 (spec §2.5). If it cannot be done cleanly in
one clause, leave it and let spec §9 route it.

**`[P]` because:** the file is disjoint from T001's and T002's committed set, and
its only dependency — the guard's class name — is fixed by the plan, not
produced by T001.

**Depends on:** nothing.

---

### T004 — [US-1] Green build, clean analyzers, artefacts committed

Confirm on an unmodified tree:

- The guard's three tests pass.
- `IntegrationTestSelectionTests` still passes — it will fail the build if T001's
  trait is missing or misspelled, so this is the check that the guard will
  actually be selected by the cheap CI step.
- Release build clean: format and analyzers, including
  `dotnet_style_prefer_collection_expression` (warning → fails Release) and the
  SonarAnalyzer metric limits.

**If the build is blocked by the running Aspire stack holding service binaries
(MSB3027), report it. Do not stop the stack** — it reads as a broken build but
is not one.

Commit with Conventional Commits and **no `Co-Authored-By`** (ADR-0086).

**Depends on:** T001, T002, T003.

---

### T005 — [US-1] Phase 5 verification note

Write the verification note recording:

1. The four counterfactual outputs from T002, verbatim.
2. The guard green on unmodified `develop`.
3. **The guard observed executing in CI's Docker-free `Category=FixtureLogic`
   step** — quote the step name from the log. A green run somewhere is not this;
   a guard that ran only in the thirty-minute Docker job is the exact omission
   `IntegrationTestSelectionTests` exists to close.
4. **Latency: N/A**, per spec §7 — no leg of the §IV budget is touched, and the
   reason is stated rather than the row left blank.

**Depends on:** T004.

---

## Dependency graph

```
T001 ──► T002 ──┐
                ├──► T004 ──► T005
T003 [P] ───────┘
```

Foundational: **T001**. It blocks T002 and T004. **T003** runs beside it.

---

## Out of scope — do not let these grow into the slice

Each is real and each is routed in spec §9. None is this slice's work:

- A SignalR backplane (ADR-0153 clause 5 — already answered "no").
- Changing `api-gateway`'s replica count in either direction (#2283).
- Clause 3's two-instance harness (spec §3.2 — its own slice).
- Correcting ADR-0153's "no lane runs two replicas of anything" (spec §2.5) or
  its Identity row (spec §2.3) — `docs(adr)` work, ideally folded into the
  already-open PR #2417, and **not the autonomous lane's to write**.
- A `deploy/` replica scan (attaches to the first spec that adds a chart).
- The `SignalRLayoutLifecycleBroadcaster` FR-012/FR-008 miscitation.
- Any fix to the five contexts' per-instance state.
