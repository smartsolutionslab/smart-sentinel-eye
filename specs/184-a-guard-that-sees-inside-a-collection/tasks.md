# Tasks 184 — A guard that sees inside a collection

**Spec:** `specs/184-a-guard-that-sees-inside-a-collection/spec.md`
**Plan:** `specs/184-a-guard-that-sees-inside-a-collection/plan.md`
**Issue:** [#2291](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2291)
— **already on Project #13, status Todo**, labels `bug`, `tech-debt`,
`agent:ready`, no `agent:blocked`. Verified 2026-09-18 by `content.url` against a
`--limit 2000` dump (the number filter returns zero). **No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED — and the red is a constructed counterfactual, not a live violation**

Behaviour-changing: the guard starts reporting shapes it does not report today.
Not ambiguous, and ambiguity would resolve to red anyway.

**But the red has an unusual shape for this repo, and getting it wrong is the
main risk in this feature.** There is no live §II violation to catch — spec §1.2
measures zero across `src/*/Domain` and `src/Shared.Kernel`. A test pointed at
the real domain models would be green before the fix and green after, which is
an assertion that cannot fail ([[an-assertion-must-not-check-its-own-input]]).

**So the red is manufactured, deliberately and permanently:**

1. `test-writer` adds `tests/Architecture.Tests/PrimitiveBoundaryProbe.cs`, a
   probe corpus that violates §II on purpose (`plan.md` §3.2).
2. `test-writer` adds two facts that run the **existing, unfixed** walk over the
   probe assembly and assert the exact 7-entry offender list and the exempt set.
3. **Those two facts must be observed failing**, with the verbatim output
   returned as `backend-engineer`'s brief and quoted in the PR body.

**The red must be asymmetric, and this is the acceptance test for the red
itself:**

- `ProbeAggregate.RawCount : Int32` **present** — the positive control; the
  unfixed walk already catches a declared `int`, so its presence proves the probe
  assembly is genuinely being walked.
- `ProbeAggregate.Tags : String` **absent**, along with `Labels`, `Counters`,
  `Windows`, `Optionals`, `ProbeHighlight.Overlays`.

That is the issue's own transcript (`offenders=1`) reproduced in the suite.

**If both entries are missing, STOP and report.** The probe is not reaching the
walk at all, no walk change will fix that, and a red produced by a mis-wired
fixture is [[the-wrong-red-matches-the-filed-number]] — a failure that looks like
the filed defect and is not it.

**If either probe fact passes before the fix, STOP and report.** The walk
already sees the shape, which contradicts the reading in `spec.md` §1, and the
premise needs re-checking before anything is changed
([[verify-the-issue-premise-before-planning]]).

**The green half, declared alongside the red.** The three pre-existing facts —
`No_domain_model_exposes_primitive_typed_state`,
`The_walk_reaches_every_aggregate_and_a_useful_amount_of_state`,
`A_value_objects_own_backing_values_are_exempt` — are the characterisation of the
real corpus. They are green before and **must be green after, unmodified**. An
assertion that has to be edited is evidence the behaviour moved against real
code, which this fix claims it does not: **block, do not adjust** (ADR-0144).

### Phase 4 agents

- **4a — `test-writer`.** Writes the probe corpus and the two new facts, runs
  them, observes the asymmetric red, and returns the **verbatim** output. That
  output is `backend-engineer`'s brief and is quoted in the PR body (ADR-0139).
- **4b — `backend-engineer`.** C# reflection over `System.Reflection`; the walk
  change in `PrimitiveBoundaryTests.cs`. **May not edit
  `PrimitiveBoundaryProbe.cs` and may not edit the two new facts to pass.** The
  probe corpus and its expected list are the specification of the fix, not
  material for the fix.

  *Note on the agent choice:* `backend-engineer`'s brief is C#/.NET, DDD and
  persistence. This change is C# reflection in a test project with no domain or
  persistence content. It is still the right agent — it is the only C# implementer
  in the roster, the subject matter is constitution §II which is squarely its
  domain, and the alternative (`infra-engineer`) is scoped to Aspire, CI, Docker
  and Keycloak, none of which appear here.

- **`test-adversary` is not added.** The adversarial cases *are* the primary
  cases: AS-2's four alternative spellings, AS-3's false-positive controls, AS-4's
  ADR-0140 edge and AS-6's termination/open-generic hazards are all written by
  `test-writer` as first-class scenarios. Adding an adversary over a 9-member
  hand-written fixture would duplicate the corpus rather than extend it.
- **No `frontend-engineer`.** No frontend file is touched.
- **No `infra-engineer`.** No `.csproj`, CI workflow, Docker or Aspire change is
  expected (`plan.md` §6). If one turns out to be needed, that is a report, not a
  silent edit.

### Phase 6 reviewers: **`backend-reviewer` only. `security-reviewer` is NOT warranted.**

Stated with reasoning, because "skip the security review" is the kind of call
that must be visible rather than inferred from an omission.

**Why `security-reviewer` is not warranted here:**

1. **The diff does not reach production.** Every changed file is under
   `tests/Architecture.Tests/`. No endpoint, no handler, no repository, no DI
   registration, no `Shared.Contracts` message, no migration, no realm file, no
   Aspire resource. Nothing this change touches is deployed or reachable by any
   caller.
2. **Nothing in the `security-reviewer` brief intersects the diff.** That agent's
   subject matter is Keycloak/OIDC, the `sse.*` scope catalogue and
   `RequireScope`, fab authorization, idempotency-key scoping, retry safety,
   secrets and trust boundaries. This change has no principal, no scope, no fab,
   no key, no secret and no trust boundary — its only inputs are `System.Type`
   objects obtained by reflecting over our own assemblies.
3. **Constitution §II is a modelling rule, not a security boundary.** Its
   downstream implications are real — a value object's `.From(...)` is where
   validation lives, so a raw `string` on a domain model is validation that was
   never written — but this change neither adds nor removes a single validation.
   It widens a guard's field of view over a corpus with **zero** live violations
   (spec §1.2), so **no production behaviour changes at all**. There is nothing
   for a security review to review.
4. **The one security-adjacent hazard in the lane is the opposite of this
   change.** ADR-0144 forbids weakening a gate to reach green — a deleted test, a
   lowered threshold, a new suppression, a narrowed analyzer. This PR
   *strengthens* a gate. Policing that direction is `backend-reviewer`'s job under
   the same ADR, and T010 makes it an explicit review question.

**If any of the following turns out to be true during phase 4, escalate for a
security review before opening the PR** — recorded so the decision is
conditional rather than final:

- a `src/` file is modified for any reason;
- T007's measurement finds a live §II violation on a type that carries
  credentials, tokens, hashes or fab identity;
- the fix requires suppressing or narrowing any analyzer or existing assertion.

**`backend-reviewer` is asked to answer, in writing:**

1. Is the walk change the smallest one that closes the hole (ADR-0036), or has
   the constituent closure been generalised beyond what `spec.md` §1.1's table
   requires?
2. Does the recursion terminate on a self-referential generic, and does it
   survive a generic **parameter** type (`T`, whose `Namespace` is null) without
   throwing? Construct one if the probe does not already carry it.
3. Is `IsComputed` still restricted to `bool`? A widening there silently re-opens
   the `Tile.Row` hole that the class doc at `:215-233` exists to describe.
4. Is the exemption filter shared between the real fact and the probe fact rather
   than duplicated? A duplicated filter can drift so the probe stops testing what
   the real rule does.
5. Is the probe's expected offender list asserted **exactly**, not as a superset?
   A `ShouldContain`-only assertion lets an over-flagging fix pass.
6. Were the three pre-existing facts left **byte-identical**? If any was edited,
   that is a block under ADR-0144, not a judgement call.
7. Is anything in the diff a gate being weakened — a suppression, a narrowed
   analyzer, a relaxed assertion?

### Files this feature may NOT touch

Any change to these is out of scope and must be raised as a scope question, not
made:

- **Anything under `src/`.** Including any domain model, even if T007 finds a
  violation — spec §2.
- `tests/Architecture.Tests/*.cs` other than `PrimitiveBoundaryTests.cs` and the
  new `PrimitiveBoundaryProbe.cs`.
- `tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj` — no new
  reference is expected (`plan.md` §6).
- `.specify/memory/constitution.md` and `docs/adr/**` — the lane may not amend
  either (ADR-0144), and this fix needs neither: §II already bans the shape.
- `CLAUDE.md` — its claim about this guard becomes *more* true, not less, so no
  correction is owed. If the engineer believes one is, report it.

### Latency budget

**N/A.** Test-project-only change; none of §IV's six legs is on the diff, and no
leg's measurement status moves.

---

## Task list

`[ID] [P?] [Story]` — `[P]` marks tasks that own disjoint files and may run
concurrently (ADR-0109).

### Phase 4a — the red (`test-writer`)

**T001 [US1]** — Add `tests/Architecture.Tests/PrimitiveBoundaryProbe.cs` with
the corpus in `plan.md` §3.2: `ProbeIdentifier`, `ProbeName`, `ProbeTagSet`,
`ProbeHighlight`, `ProbeAggregate` and its nine members. Namespace
`SmartSentinelEye.Architecture.Tests`. File-level doc comment whose **first
sentence** states that these types violate constitution §II deliberately, exist
to be caught, and must never be "fixed"; it also records that the expected list
in the test is asserted exactly, so adding a probe member requires editing it.
House rules apply to the probe as written code: collection expressions with
explicit types (`IReadOnlyList<string> Tags { get; private set; } = [];`), no
leading-underscore fields, `Identifier` suffix, no `Ensure.That` needed (no
argument preconditions).
*Depends on: nothing.*

**T002 [US1]** — Make the assembly set a parameter: two overloads each of
`Roots()` and `WalkAggregateState()`, the no-argument one delegating to
`DomainAssemblies()` (`plan.md` §3.1). `allDomainTypes` is computed from the
same set. **This is the only edit to `PrimitiveBoundaryTests.cs` in phase 4a**,
it is mechanical, and it must leave the three pre-existing facts passing
unchanged — run them to confirm before proceeding.
*Depends on: nothing. Not `[P]` with T003 — same file.*

**T003 [US1]** — Add the two facts of `plan.md` §3.3:
`The_walk_sees_a_primitive_reached_through_a_collection_or_array` (exact 7-entry
offender list) and `A_value_objects_own_backing_collection_is_exempt`. Extract
the offender filter chain once and call it from both the real fact and the probe
fact rather than duplicating it.
*Depends on: T001, T002.*

**T004 [US1]** — Run
`dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj --filter "FullyQualifiedName~PrimitiveBoundaryTests"`
and **observe the red**. Check it against the acceptance test for the red above:
`RawCount` present, the other six absent, and the three pre-existing facts green.
Return the **verbatim** output. **Stop and report** if the red is symmetric
(both missing) or if either probe fact passes.
*Depends on: T003. This is the phase-4a gate.*

### Phase 4b — the fix (`backend-engineer`)

**T005 [US1]** — Replace the property loop's either/or with the constituent
closure of `plan.md` §2.2: recursive over generic arguments and array element
types, `Nullable<>` stripped at every level, banned constituents **recorded**,
non-banned constituents **enqueued**. Must terminate on a self-referential
generic and must not throw on a generic parameter type. `Banned`, `IsComputed`,
`IsIdentityReferenceInsideValueObject`, the `DeclaringTypeIsValueObject` filter,
the `PendingEvents` skip and the `System`-namespace drop at `:173` all stay as
they are (`plan.md` §2.3). Optionally carry the declared type on `StateMember`
for a clearer message (`plan.md` §2.4) — the probe facts still assert the
primary form.
*Depends on: T004.*

**T006 [US1]** — In the same doc block being rewritten to describe the
collection walk, correct the two stale counts: `:56-59` says *"Nine
aggregates"* (measured: **11** types derive from `AggregateRoot<T>`) and `:22`
says *"From eleven roots"* (measured: **12**, the 11 plus `AuditEvent`, which is
what `roots.Count.ShouldBe(12)` already pins). Either re-measure the *"reaches
133 types"* figure from the running walk or replace it with the floor the test
actually asserts. **Nothing else in the doc changes**, and no `src/` file is
touched by this task.
*Depends on: T005. Same file, so not `[P]` with it.*

**T007 [US1]** — **Measure** the fixed walk against the real corpus and write
the figure down: run
`--filter "FullyQualifiedName~No_domain_model_exposes_primitive_typed_state"` and
record the offender count (expected **0**, spec §1.2 A1) in the PR body as an
observed number, not an expectation. If it is non-zero, **stop**: that is a live
§II violation, it is out of scope per spec §2, and it is a scope question for the
orchestrator.
*Depends on: T005.*

**T008 [US1]** — Re-run the probe facts and the three pre-existing facts. All
five green; the three pre-existing ones **byte-identical** to their state on
`origin/develop` (`git diff origin/develop -- tests/Architecture.Tests/PrimitiveBoundaryTests.cs`
must show no change inside their bodies).
*Depends on: T005, T006.*

**T009 [P] [US1]** — Run the **whole** architecture suite
(`dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj`)
to confirm the probe corpus disturbs no other guard (spec §8 A2, SC-007).
*Depends on: T005. Disjoint from T007/T008 in what it reads; may run alongside.*

### Phase 5 — verify (`/verify`)

**T010 [US1]** — The counterfactual against **real** code, spec §6 step 5:
temporarily add `public IReadOnlyList<string> Tags { get; private set; } = [];`
to `src/CameraCatalog/Domain/Camera/Camera.cs`, run the real fact, observe
**`Camera.Tags : String`** in the failure, quote it, then **revert and re-run to
confirm green — with a forced rebuild** (`dotnet build --no-incremental` on the
Domain project, or touch the file), because a restored file keeps its old
timestamp and MSBuild will skip it, leaving the test red after a correct revert.

**This task is the point of the feature and is not optional.** T004's red proves
the walk changed; only this proves the walk guards **the corpus it is pointed
at**. [[prove-a-guard-by-counterfactual]] is explicit that these are different
experiments, and that "remove the instrument, suite still green" has already
misled this repo once. Confirm `git status` is clean afterwards.
*Depends on: T008, T009.*

**T011 [US1]** — Write `specs/184-a-guard-that-sees-inside-a-collection/verification.md`:
the verbatim phase-4a red, the post-fix green, T007's measured offender count,
T010's counterfactual failure line and its confirmed revert, and the full-suite
result. Every measurement written down as observed — a figure reported only to
the orchestrator is invisible to every later grep
([[self-review-catches-contradictions-never-omissions]]).
*Depends on: T010.*

### Phase 6 — review

**T012 [US1]** — `backend-reviewer` over the diff, answering the seven questions
above in writing. No `security-reviewer` — reasoning above; escalate only on the
three stated conditions.
*Depends on: T011.*

### Phase 7 — PR

**T013 [US1]** — `gh pr create --base develop`. Body carries: the verbatim
phase-4a red (asymmetric, with `RawCount` present); the post-fix green; T007's
measured count; T010's counterfactual; the phase-4a colour declaration and why it
is a constructed red; the explicit **"security review not warranted"** reasoning;
spec §2.1's two recorded observations, with the `reached` floor recommended to
the orchestrator as a follow-up rather than filed by the lane (ADR-0144 may not
make that call). Closing keyword for #2291, and check the issue state after the
merge — a mention alone closes roughly one time in three. No `Co-Authored-By`
(ADR-0086).
*Depends on: T012.*

---

## Dependency summary

```
T001 ─┐
T002 ─┴─> T003 ─> T004(gate) ─> T005 ─┬─> T006 ─┬─> T008 ─┬─> T010 ─> T011 ─> T012 ─> T013
                                      ├─> T007 ─┘         │
                                      └─> T009 [P] ───────┘
```

**Foundational:** T001 + T002 gate everything — the probe corpus and the
parameterised assembly set are what make a red possible at all. **T004 is the
phase-4a gate** and hands its verbatim output to phase 4b. Only T009 is `[P]`;
this feature is two files and is genuinely sequential, and marking more of it
parallel would be decoration.
