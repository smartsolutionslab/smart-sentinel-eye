# Verification 184 — A guard that sees inside a collection (#2291)

**Latency: N/A.** Test-project-only change; no leg of constitution §IV's
event→overlay path is touched by this diff.

## Phase 4a — red, constructed (ADR-0139)

There is no live constitution §II violation in the codebase (measured zero,
confirmed again below) — the red for this feature had to be manufactured
rather than found, via a deliberately-violating probe corpus
(`PrimitiveBoundaryProbe.cs`). Quoted verbatim from commit `5fc862ac`
(rebased/re-run identically after the S1144 fix in `af218d14`):

```
Failed SmartSentinelEye.Architecture.Tests.PrimitiveBoundaryTests.A_value_objects_own_backing_collection_is_exempt [46 ms]
  Error Message:
   Shouldly.ShouldAssertException : exempted
    should contain
"ProbeTagSet.Values"
    but was actually
["ProbeName.Value", "ProbeIdentifier.Value", "AggregateVersion.Value"]

Failed SmartSentinelEye.Architecture.Tests.PrimitiveBoundaryTests.The_walk_sees_a_primitive_reached_through_a_collection_or_array [11 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be
["ProbeAggregate.Counters : Int32", "ProbeAggregate.Labels : String", "ProbeAggregate.Optionals : Int32", "ProbeAggregate.RawCount : Int32", "ProbeAggregate.Tags : String", "ProbeAggregate.Windows : DateTimeOffset", "ProbeHighlight.Overlays : Guid"]
    but was (case sensitive comparison)
["ProbeAggregate.RawCount : Int32"]

Failed!  - Failed:     2, Passed:     3, Skipped:     0, Total:     5, Duration: 126 ms
```

**Asymmetric, as required.** `ProbeAggregate.RawCount : Int32` (the positive
control — a plain declared `int`, already caught by the unfixed walk) is
present; the six primitives hidden behind a collection, array, or nested
combination with `Nullable<>` are absent. Not the both-missing case (which
would mean the probe wasn't being walked at all) and neither new fact passed
early (which would have meant the issue's premise was wrong). The three
pre-existing facts stayed green throughout.

## Phase 5 — the counterfactual against real code, independently performed

**This is the point of the feature, not a formality.** T004's red proves the
walk changed; only a counterfactual against real domain code proves the walk
actually guards the corpus it's pointed at, not only the fixture it was
built against — [[prove-a-guard-by-counterfactual]] is explicit these are
different experiments, and this repo has been misled by skipping the second
one before.

Performed directly by the orchestrator (the phase-6 reviewer flagged this
step as missing and couldn't do it itself — read-only). Temporarily added to
`src/CameraCatalog/Domain/Camera/Camera.cs`:

```csharp
public IReadOnlyList<string> Tags { get; private set; } = [];
```

Ran `No_domain_model_exposes_primitive_typed_state` against the injected
violation:

```
Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
["Camera.Tags : String"]

Additional Info:
    Constitution §II: a domain model does not carry primitive-typed state.

Camera.Tags : String

Introduce a value object, or — if the declaring type IS a value object
and these are its own backing values — mark it with IValueObject
(ADR-0066), which is what makes the exemption legible to this rule.

Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 869 ms
```

The fixed walk catches a real domain property whose primitive is reached
through a collection — exactly the shape #2291 reports, on real code, not
only the probe.

**Reverted** (`git diff --exit-code` on `Camera.cs` confirmed clean
afterward — no residual change). **Forced a rebuild** rather than trusting
the revert alone (a restored file keeps its old timestamp, and MSBuild will
skip recompiling it, leaving the test red after a correct revert —
[[restoring-a-file-keeps-its-old-timestamp]]): `touch`'d the file, ran
`dotnet build -c Release --no-incremental`, then the full suite —
**403/403 green**, matching the pre-counterfactual baseline exactly.

## T007 — measured offender count on real code (no counterfactual injected)

`No_domain_model_exposes_primitive_typed_state` against the actual,
unmodified domain models: **0 offenders**, confirmed independently (both by
the phase-6 reviewer's own run and by the orchestrator's runs before and
after the T010 counterfactual). No live §II violation exists anywhere in the
current codebase; this remains a latent-hole fix, not a live-bug fix.

## Full-suite confirmation

Independently re-verified by the orchestrator at the final commit
(`4f37101c`, after all phase-6 fixes): `dotnet build -c Release
--no-incremental` on the Architecture.Tests project — 0 errors; full
`Architecture.Tests` suite — **403/403 green** (the probe corpus and the
walk change disturb none of the other ~35 architecture guards).

## Phase 6 — review, findings closed

`backend-reviewer` only (`security-reviewer` explicitly declared not
warranted at phase 3 — the diff never leaves `tests/Architecture.Tests/`, no
production behavior changes since zero live violations exist, and the one
lane hazard this session watches for — weakening a gate — is the opposite of
what this PR does; the reviewer independently re-verified that call rather
than accepting it, and agreed). **No blockers.** Seven numbered review
questions answered in full in the reviewer's own report (summarized in the
PR body); findings closed:

- **A lazy-iterator re-enumeration bug in `Constituents(Type)`** — the
  `visited` set was captured once by the iterator's state machine at first
  enumeration; a second enumeration of the same returned sequence would
  silently yield nothing. Latent (only one call site existed), but exactly
  the "guard silently stops guarding, no test goes red" shape this whole
  issue is about. Fixed: the public entry point now eagerly materializes
  (`[.. ConstituentsCore(type, [])]`), so the sequence is a stable list
  rather than a single-use iterator.
- **An array of a domain type would walk into `System.Array`'s own members**
  — the array branch yielded the array type itself alongside the element's
  constituents, and an array type's `Namespace` is its *element's*
  namespace, so it would pass the `SmartSentinelEye` prefix filter and get
  walked into `Length`/`Rank`/`LongLength`/`SyncRoot`, producing spurious
  offenders the first time any domain model declared a primitive array
  property. Pre-existing in the old `Unwrap` too, not a regression from this
  PR — but this is the PR that puts arrays explicitly in scope, so it was
  closed now. Fixed by deleting the array type's own `yield return`; the
  element is already reached through the recursive call, so nothing is
  lost.
- **An unexplained magic initializer** (`RawCount { get; private set; } =
  1;` in the probe, with no comment explaining why) — one-line comment
  added.

**Recorded, not changed — the widening is broader than "collections and
arrays."** The reviewer measured that the constituent closure applies
structurally to *every* constructed generic, not only collection interfaces
— a domain property typed as a tuple, `KeyValuePair<,>`, `Option<T>`,
`Lazy<T>` or similar would now also have its primitive arguments recorded.
Judged correct rather than scope creep: a uniform structural rule over "what
a type is built out of" is smaller and more robust than a curated list of
collection interfaces, and constitution §II bans primitive state regardless
of which generic wrapper carries it. The real corpus measures 0 either way.
Named explicitly here (and in the PR body) as a second deliberate widening,
alongside the array widening spec.md §1.1 already flagged — so the next
reader meets this as a stated decision rather than a surprise the first time
someone adds a tuple-typed domain property.

**Accepted, not fixed:** the exempt-values projection is duplicated between
the two "backing values are exempt" facts (only the offender-computation
chain was extracted into a shared `Offenders()` helper, per the plan's own
scope — extracting the exempt projection too would touch the body of a
protected pre-existing fact, which is a judgement call the reviewer flagged
as optional, not required); the doc's "reaches well over 100 types" phrasing
is weaker than the old (wrong) precise figure it replaced, sanctioned
explicitly by the task that corrected it.

## Doc corrections (T006)

- "Nine aggregates" → "Eleven aggregates" (measured: 11 types derive from
  `AggregateRoot<T>`; confirmed independently by the reviewer's own count).
- "From eleven roots" → "From twelve roots" (11 + `AuditEvent` = 12, matching
  the existing `roots.Count.ShouldBe(12)` assertion elsewhere in the file).
- The unmeasured "133 types" figure replaced with the floor the existing
  fact actually asserts (`reached.ShouldBeGreaterThan(100)`).
