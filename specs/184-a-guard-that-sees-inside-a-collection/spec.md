# Spec 184 — A guard that sees inside a collection

**Issue:** [#2291](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2291)
— on Project #13, status **Todo**, labels `bug`, `tech-debt`, `agent:ready`, no
`agent:blocked`. Verified 2026-09-18 by `content.url` against a `--limit 2000`
dump, not by the number filter. **No `item-add` needed.**

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **182** as the
highest merged. **183 is claimed by an unmerged sibling PR (#2456, the
list-endpoint-leak issue)**, so this spec is **184**. The number one above
develop's highest is not free; it has collided twice this month.

**ADRs referenced:** ADR-0139 (rules that fail the build, not the review — and
the red-first obligation), ADR-0140 (a closed set of primitives, the four
exemptions, and *an identity reference is never a backing value*), ADR-0066
(`IValueObject` marker — the thing that makes the value-object exemption legible
to reflection), ADR-0141 (`Option<T>` for domain absences — relevant because it
puts more generics on the domain surface), ADR-0052 / ADR-0053 / ADR-0054 (xUnit
+ Shouldly, sentence-style naming, hand-written test data — no AutoFixture),
ADR-0036 (smallest possible change), ADR-0037 / ADR-0144 (the phased workflow and
the autonomous lane), ADR-0109 (the `[P]` disjoint-file rule).

**Constitution:** §II — *"A type does not appear on a domain model if it is a C#
predefined type … or one of these BCL types."* This spec does **not** amend §II
and does not need a new ADR. §II already bans the shape; the guard that was built
to make §II fail the build cannot see it. The spec closes the gap between what
§II says and what the build enforces.

**Latency budget (§IV): N/A.** The entire diff lands in
`tests/Architecture.Tests/`. No production assembly changes, so none of the six
legs of `event arrival → overlay rendered` is touched, and no leg's measurement
status changes.

---

## 1. The defect, confirmed by reading the current code

The issue cites `tests/Architecture.Tests/PrimitiveBoundaryTests.cs:189-198` and
`:173` as of 2026-09-13. Re-confirmed against this worktree's base
(`origin/develop` @ `10c5e5de`) on 2026-09-18 — the line numbers still hold.

`WalkAggregateState()` has two branches per property (`:187-198`):

```csharp
Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

if (Banned.Contains(propertyType))
{
    members.Add(new StateMember(type, property.Name, propertyType, isValueObject, IsComputed(type, property)));
    continue;
}

foreach (Type reachable in Unwrap(property.PropertyType))
{
    pending.Enqueue(reachable);
}
```

A property whose **declared** type is banned is **inspected**. Anything else is
**enqueued**, and the queue's first act on dequeue (`:173`) is:

```csharp
if (!seen.Add(type) || type.Namespace?.StartsWith("SmartSentinelEye", StringComparison.Ordinal) != true)
{
    continue;
}
```

So for `public IReadOnlyList<string> Tags`:

1. `IReadOnlyList<string>` is not in `Banned` → the else-branch runs.
2. `Unwrap` yields `String` and `IReadOnlyList<String>`; both are enqueued.
3. `String` is dequeued, its namespace is `System` → dropped, **never compared
   against `Banned`**.
4. `IReadOnlyList<String>` is dequeued, its namespace is
   `System.Collections.Generic` → dropped, so its indexer is never read either.

`Banned` is consulted **only** on a property's declared type. A banned type that
arrives as a *constituent* of the declared type is enqueued into a queue whose
entry condition then discards it. The issue proved this by counterfactual — a
staged `SmartSentinelEye.Counterfactual.Domain.dll` produced
`offenders=1 — ProbeAggregate.RawCount : Int32` with `IReadOnlyList<string> Tags`
absent. This spec does not re-argue it from shape; it makes the counterfactual a
permanent test (§4, §5).

### 1.1 The hole is the namespace filter, not "generics" — and that changes the fix

The issue says *"unwrap generic type arguments"*. That is the right fix but the
wrong diagnosis, and the difference matters for scope. **`Option<string>` is not
affected.** `Option<T>` lives in `SmartSentinelEye.Shared.Kernel`, so the
constructed `Option<String>` passes the namespace filter, is walked, and its
`public T Value` surfaces as a `String`-typed property on a declaring type that
is not an `IValueObject` — an offender, today, with no fix. The generic argument
leaks out through a member the walk can already see.

What actually fails is a generic (or array) whose **own type is outside the
`SmartSentinelEye` namespace**, because the filter drops the container before any
of its members are read, and drops each banned argument in its own right. That
is precisely the BCL collection family, which is what a domain model reaches for:

| Shape on a domain model | Seen today? | Why |
|---|---|---|
| `int RawCount` | **yes** | declared type is banned |
| `int? RawCount` | **yes** | `Nullable.GetUnderlyingType` at `:187` |
| `Option<string> Note` | **yes** | `Option<T>` is ours; `Value` is walked |
| `IReadOnlyList<string> Tags` | **no** | argument dropped at `:173` |
| `string[] Labels` | **no** | `Unwrap` does not handle arrays at all; the array type itself is dropped at `:173` |
| `IReadOnlyDictionary<ProbeName, int> Counters` | **no** | value argument dropped |
| `IReadOnlyList<IReadOnlyList<DateTimeOffset>> Windows` | **no** | needs recursion, not one level |
| `IReadOnlyList<int?> Optionals` | **no** | needs `Nullable` stripped *per argument* |

**Arrays are in scope, deliberately, though the issue text says "generic".** They
are the same hole with a different spelling and the same cause, and `Unwrap`
already ignores them (`IsGenericType` is false for `String[]`, so it yields only
the array type). Fixing `IReadOnlyList<string>` and leaving `string[]` open would
leave a guard that is correct about one half of the shape it claims — the failure
mode this repo has already had to correct in §II twice. Flagged here as a
deliberate widening beyond the issue text so a reviewer sees the decision rather
than a scope creep.

### 1.2 There is no live violation — measured, not assumed

Every collection-typed or array-typed public property in `src/*/Domain` and
`src/Shared.Kernel`, as of `10c5e5de`:

- `src/LayoutComposition/Domain/Layout/Layout.cs:47` — `IReadOnlyList<Revision>`
- `src/LayoutComposition/Domain/Layout/Revision.cs:37` — `IReadOnlyList<Tile>`
- `src/LayoutComposition/Domain/Layout/Tile.cs:64` — `Option<OverlayIdentifier>`
- `src/OverlayDesigner/Domain/Overlay/Overlay.cs:18` — `IReadOnlyList<Revision>`
- `src/Shared.Kernel/AggregateRoot.cs:18` — `IReadOnlyList<IDomainEvent>`
  (`PendingEvents`, skipped by name at `:182`)

Every argument is domain-typed. A sweep for `byte[]`, `ReadOnlyMemory<>`,
`ISet<>`, `IList<>`, `ICollection<>`, `IDictionary<>`, `ImmutableArray<>`,
`FrozenSet<>`, `JsonElement`, `string[]`, `object[]`, `Guid[]` over the same
directories finds only **method locals** (`BearerTokenHash.cs:34,51,52`,
`ClientSecret.cs:76`) and one repository **method parameter**
(`IEventRepository.cs:32`). The walk inspects properties, not locals or
parameters, so none of these is reachable.

`Shared.Contracts` — §II's wire-format exemption — is not reachable from the
walk either: no `src/*/Domain/*.csproj` references it.

**So the fixed guard is expected to produce zero offenders against today's
domain.** That expectation is a task (T007), measured and written down, not a
claim carried by this paragraph — the standing lesson is that a measurement
reported only in conversation is invisible to every later grep.

### 1.3 Why it matters that it is latent

CLAUDE.md says of this guard: *"`PrimitiveBoundaryTests` now fails the build when
a domain model exposes primitive-typed state, so this is no longer a rule a
reviewer has to remember."* A reviewer who has read that sentence will not
re-derive §II by hand for `IReadOnlyList<string>`. The rule is therefore *less*
defended for collections than it was before the guard existed, because the guard
displaced the human check without covering the shape. ADR-0141 makes this worse
over time, not better: it pushes domain signatures toward generic wrappers.

## 2. Scope

**In scope:** `tests/Architecture.Tests/PrimitiveBoundaryTests.cs` and one new
file beside it holding the probe corpus.

**Out of scope, explicitly:**

- **Any change to `src/`.** There is no live violation (§1.2). If T007's
  measurement finds one, that is a **scope question for the orchestrator**, not
  a same-PR fix: a guard fix and a domain fix are two changes, and folding them
  together would make the red ambiguous.
- **Widening the computed carve-out.** `IsComputed` is restricted to `bool`
  deliberately (`:215-233`), because reflection cannot tell `IsRevoked =>
  RevokedAt is not null` from `Row => row`. The fix does **not** extend it to
  collections. Consequence, stated so it is not discovered later: an
  expression-bodied `IReadOnlyList<string> X => backing` on a non-value-object
  **will** be flagged. That is correct — it is storage, exactly the `Tile.Row`
  shape the doc block already describes.
- **Method parameters, constructor parameters and fields.** §II binds them
  (ADR-0140: *"properties, record components, and the constructor or factory
  parameters that set them"*), and this guard has only ever read properties. For
  a record, the component *is* the property, so positional records are covered.
  A non-record constructor parameter that is never surfaced as a property remains
  outside this guard — a **pre-existing, declared limit**, unchanged by this
  spec, recorded here because an undeclared blind spot is the overselling
  pattern.
- **`BoundaryTests` and the other 35 architecture guards.** Untouched.

### 2.1 Two observations recorded, not silently folded in

- **The class doc's own counts are stale.** `PrimitiveBoundaryTests.cs:56-59`
  says *"Nine aggregates reach it through `AggregateRoot<T>`"* and `:22` says
  *"From eleven roots the walk reaches 133 types"*, while
  `The_walk_reaches_every_aggregate_and_a_useful_amount_of_state` pins
  `roots.Count.ShouldBe(12)`. Measured on this base: **11** types derive from
  `AggregateRoot<T>`, plus `AuditEvent` = **12** roots. Both prose figures are
  wrong. The engineer is already rewriting that doc block to describe the
  collection walk, and leaving a knowingly-wrong count inside the paragraph being
  edited is the exact defect class this repository keeps correcting. **T006
  corrects the two counts** and nothing else in that doc.
- **`reached` is asserted as a floor (`> 100`), not pinned.**
  [[prove-a-guard-by-counterfactual]] argues for pinning knowable populations.
  Tightening it is a **separate** change with its own maintenance cost per new
  aggregate, and is not needed by this fix (the fix can only grow `reached`).
  **Recommendation: leave it, and raise it with the orchestrator as a possible
  follow-up.** Not done here.

## 3. User stories

### US1 (P1) — A primitive inside a collection fails the build

*As the maintainer of constitution §II, a domain model that exposes a primitive
through a collection, dictionary, array or nested generic fails the build in the
same way and with the same message as one that exposes it directly.*

Independently observable: a probe aggregate carrying
`IReadOnlyList<string> Tags` is reported by name, in the same
`Type.Property : Constituent` form the rule already uses, by a test that is red
before the walk changes and green after.

**There is no P2.** The two halves of US1 — catching the new shapes, and not
catching the exempt ones — cannot ship apart: a walk that sees into collections
without honouring the value-object exemption would flag every composite value
object that holds a collection and would be switched off, which is the failure
mode the class doc's opening paragraph exists to prevent.

## 4. The red test, and why it has to be constructed

**This guard cannot be red-tested against the real domain models, because they
are compliant** (§1.2). The subject of the rule is an *absence*, and the standing
lesson [[an-assertion-must-not-check-its-own-input]] says an assertion that
cannot fail is worse than no assertion. So the red has to be built.

**Mechanism: an in-repo probe corpus, walked by the same `WalkAggregateState`
over a different assembly set.** The walk's assembly source becomes a parameter
whose default is the existing `SmartSentinelEye.*.Domain.dll` disk glob; a new
fact passes the **test assembly itself**, which carries a deliberately-violating
`ProbeAggregate`. Full design in `plan.md` §3.

**Why not the issue's own method — a staged out-of-tree
`SmartSentinelEye.Counterfactual.Domain.dll`.** It was the right instrument for a
one-off proof and is the wrong one for a permanent test: `DomainAssemblies()`
globs `SmartSentinelEye.*.Domain.dll` from `AppContext.BaseDirectory`, so any
probe assembly matching that name would be picked up by
`No_domain_model_exposes_primitive_typed_state` itself and fail the real build.
Naming it outside the glob then requires build plumbing to stage it, and a
staging step that silently stops running is a guard that silently stops guarding.
Passing the assembly set as a parameter removes the filename coupling entirely.

**Why a probe inside the test assembly is safe.** Checked, not assumed:

- `DomainAssemblies()` matches `SmartSentinelEye.*.Domain.dll`;
  `SmartSentinelEye.Architecture.Tests.dll` does not match, so the probe cannot
  leak into the real corpus.
- If it ever did, `The_walk_reaches_every_aggregate_and_a_useful_amount_of_state`
  pins `roots.Count.ShouldBe(12)` and would turn red — the leak has a tripwire.
- Every other assembly-reflecting architecture test targets a **named** assembly
  (`Assembly.Load("SmartSentinelEye.<Context>.<Layer>")`) — `BoundaryTests`,
  `OutboxCommitTests`, `PostgresPoolBoundTests`, `StatusProducerDeclarationTests`
  — so none of them sees the probe.
- Every source-scanning architecture test enumerates
  `Path.Combine(root, "src")` only — `NameMutabilityConventionTests:188`,
  `StaleCodeConventionTests:142`, `HandlerDeconstructionTests:259` — so a probe
  `.cs` under `tests/` is invisible to them.
- Coverage gates (ADR-0065) bind Domain / Application / Shared. The probe is in
  a test project and is not counted.

**The probe carries a positive control.** `public int RawCount` is a shape the
**unfixed** walk already catches. If the probe assembly were not reached at all,
the control would be missing and the test would fail for that reason — so the
test cannot pass by accident of the walk finding nothing.

## 5. Acceptance scenarios (Gherkin)

Probe corpus for every scenario as specified in `plan.md` §3.2.

### AS-1 — the defect, exactly as the issue states it

```gherkin
Given a probe aggregate with public IReadOnlyList<string> Tags
  And a public int RawCount on the same aggregate as a control
When the primitive-boundary walk runs over the probe assembly
Then the offender list contains "ProbeAggregate.RawCount : Int32"
  And the offender list contains "ProbeAggregate.Tags : String"
```

**Before the fix this is red with the control present and `Tags` absent** — the
same asymmetry the issue's counterfactual produced (`offenders=1`). A red that
reports *both* missing would mean the probe is not being walked at all, and is a
different failure; see §7 SC-003.

### AS-2 — the other spellings of the same hole

```gherkin
Given a probe aggregate carrying
      public string[] Labels
      public IReadOnlyDictionary<ProbeName, int> Counters
      public IReadOnlyList<IReadOnlyList<DateTimeOffset>> Windows
      public IReadOnlyList<int?> Optionals
When the walk runs over the probe assembly
Then the offender list contains "ProbeAggregate.Labels : String"
  And the offender list contains "ProbeAggregate.Counters : Int32"
  And the offender list contains "ProbeAggregate.Windows : DateTimeOffset"
  And the offender list contains "ProbeAggregate.Optionals : Int32"
```

Array element type, dictionary value argument, two levels of nesting, and
`Nullable<>` stripped inside a type argument. `Counters` also asserts that a
value-object **key** is not flagged while a primitive value is — the argument
list is checked per argument, not as a unit.

### AS-3 — the conflict case: the exemption must survive the widening

```gherkin
Given a probe value object ProbeTagSet(IReadOnlyList<string> Values) marked IValueObject
  And a probe aggregate property public ProbeTagSet Tagging
  And a probe aggregate property public IReadOnlyList<ProbeName> Names
When the walk runs over the probe assembly
Then the offender list does NOT contain "ProbeTagSet.Values"
  And the offender list does NOT contain any entry for "ProbeAggregate.Names"
  And the exempt list contains "ProbeTagSet.Values"
```

This is the scenario that decides whether the fix is usable. §II exempts *"a
value object's own backing **values** — plural"*, and a composite value object
whose backing value is a collection of primitives is inside that exemption.
A fix that flags it flags legitimate types and gets switched off — the outcome
the class doc's first paragraph was written to prevent. `Names` proves the
collection of a **domain** type is still simply walked, not flagged.

**The exempt-list half is red before the fix too**, for the same root cause:
`ProbeTagSet.Values` is never recorded at all today, so it appears in neither
list.

### AS-4 — ADR-0140's identity-reference rule reaches inside a collection

```gherkin
Given a probe composite value object ProbeHighlight(IReadOnlyList<Guid> Overlays, ProbeName Label)
  And ProbeHighlight is marked IValueObject but not IValueObject<Guid>
When the walk runs over the probe assembly
Then the offender list contains "ProbeHighlight.Overlays : Guid"
```

ADR-0140: *"an identity reference is never a backing value"* — `GridPosition`
**is** its two ints; a `HighlightOverlay` action is **not** the overlay it points
at. A *list* of overlay references is not a backing value either, and the
existing `IsIdentityReferenceInsideValueObject` check applies unchanged once the
constituent `Guid` is what gets recorded.

### AS-5 — the real domain is unchanged (the characterisation half)

```gherkin
Given the nine SmartSentinelEye.*.Domain.dll assemblies as they are on develop
When the fixed walk runs over them
Then the offender list is empty
  And the walk still reports 12 roots and more than 100 reached types
  And CameraName.NormalizedValue, GridPosition.Row and NormalizedPosition.X
      are still reported as exempt
```

The three existing facts —
`No_domain_model_exposes_primitive_typed_state`,
`The_walk_reaches_every_aggregate_and_a_useful_amount_of_state`,
`A_value_objects_own_backing_values_are_exempt` — **must pass unmodified** after
the change. An assertion that has to be edited is evidence the behaviour moved
against real code, which this fix claims it does not; per ADR-0144 that is a
block, not an adjustment.

### AS-6 — bad-input analogue: a shape the walk cannot read is not silently passed

```gherkin
Given an open generic or a type the walk cannot resolve a constituent for
When the walk runs
Then it neither throws nor silently drops the declaring property
```

The guard's inputs are types, not user data, so there is no HTTP bad-request
case. The equivalent obligation is under-recognition:
[[prove-a-guard-by-counterfactual]] — *"report what cannot be parsed, by name;
input the guard cannot read is input it does not check."* Concretely: recursion
must terminate on a self-referential generic (`Node<T>` reaching `Node<T>`) via
the existing `seen` set or a local visited set, and must not throw on a generic
parameter type (`T` itself, which has a null `Namespace`).

### AS-7 — auth analogue: N/A, stated rather than omitted

There is no authenticated surface, no endpoint, no scope, and no fab in this
change. The standard auth scenario does not apply and is recorded as **N/A** so
its absence is a decision rather than an oversight.

## 6. Independent end-to-end test procedure

No stack boot is required — the subject is a test project. The procedure is a
build and two test runs, and its whole value is that step 2 is performed
**before** step 4.

1. `git switch 2291-primitive-boundary-collection-walk` (the work branch).
2. **Before touching the walk**, with the probe corpus and the new facts in
   place:

   ```sh
   dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj \
     --filter "FullyQualifiedName~PrimitiveBoundaryTests"
   ```

   Expect the probe facts **red**, with the failure naming the missing entries
   (`ProbeAggregate.Tags : String` and the AS-2 set) while
   `ProbeAggregate.RawCount : Int32` is **present**. Capture the output verbatim.
   Expect the three pre-existing facts **green**.
3. Apply the walk fix.
4. Re-run the same command. All facts green, the three pre-existing ones
   **unmodified**.
5. Prove the guard now guards, by counterfactual against **real** code:
   temporarily add `public IReadOnlyList<string> Tags { get; private set; } = [];`
   to `src/CameraCatalog/Domain/Camera/Camera.cs`, run

   ```sh
   dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj \
     --filter "FullyQualifiedName~No_domain_model_exposes_primitive_typed_state"
   ```

   and observe **`Camera.Tags : String`** in the failure. **Revert, and re-run to
   confirm green.** A restored file keeps its old timestamp and MSBuild will skip
   the rebuild, so `dotnet build --no-incremental` (or `touch`) the Domain project
   before the confirming run, or the revert appears not to have worked.
6. Run the whole architecture suite to confirm the probe corpus disturbs nothing:

   ```sh
   dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj
   ```

Step 5 is the part that matters most. Steps 2–4 prove the walk changed; only
step 5 proves it changed **on the real corpus the guard is pointed at**, and
[[prove-a-guard-by-counterfactual]] is explicit that these are not the same
experiment.

## 7. Success criteria

- **SC-001** — AS-1's `ProbeAggregate.Tags : String` is observed **red** against
  the unfixed walk, and the verbatim failure is quoted in the PR body (ADR-0139,
  ADR-0144 phase 4a).
- **SC-002** — the red is the **asymmetric** one: `RawCount` present, `Tags`
  absent. That is the issue's own finding reproduced, and it is what distinguishes
  "the walk does not see into collections" from "the probe is not being walked".
- **SC-003** — if both entries are missing, **stop and report**. The probe is not
  reaching the walk, and no walk fix will correct that.
- **SC-004** — after the fix, AS-1 through AS-4 are green, and AS-3's exempt
  assertion is green — the value-object exemption survived the widening.
- **SC-005** — AS-5: the three pre-existing facts pass **unmodified**, and the
  real-domain offender list is measured as **empty** and that figure is written
  into the PR body. Not "expected empty" — read from a run.
- **SC-006** — §6 step 5's counterfactual against `Camera` is performed, its
  failure line quoted, and the revert confirmed green with a forced rebuild.
- **SC-007** — the full `Architecture.Tests` suite is green; no other guard's
  corpus changed.
- **SC-008** — §2.1's two observations are carried into the PR body: the doc
  counts corrected (T006), and the `reached` floor left alone with a follow-up
  recommended to the orchestrator rather than filed by the lane (ADR-0144 may not
  make that call).

## 8. Assumptions, marked

- **A1 — no live violation exists.** Established by grep over `src/*/Domain` and
  `src/Shared.Kernel` (§1.2), **not** by running the fixed guard. T007 converts it
  to a measurement. If it turns out false, §2 says what happens: raise it, do not
  fold it in.
- **A2 — the probe types in the test assembly disturb no other guard.**
  Established by reading each reflecting and source-scanning guard's corpus
  selection (§4). To be re-confirmed empirically by SC-007, which is the whole
  suite going green.
- **A3 — `Assembly.LoadFrom` versus the default load context is not a difference
  the probe needs to model.** The real path loads Domain DLLs from
  `AppContext.BaseDirectory` with `LoadFrom`; the probe path passes an
  already-loaded assembly. Type identity for `IValueObject` and
  `AggregateRoot<>` resolves to the same `Shared.Kernel` in both, because both
  resolve from the same directory. The probe therefore exercises **the walk**,
  not the loader — a declared limit, not a hidden one. `DomainAssemblies()` and
  its doc comment are unchanged, so the loader behaviour the doc describes stays
  covered by `The_walk_reaches_every_aggregate_and_a_useful_amount_of_state`.
- **A4 — `AggregateVersion` and the identifier value objects are already exempt.**
  They must be, or the real run would already be red; the probe's inherited `Id`
  and `Version` therefore contribute nothing to its expected offender list. To be
  confirmed by the probe's expected list being exact rather than a superset.

## 9. Locked tech choices (no new ones)

| Concern | Choice | Source |
|---|---|---|
| Test framework | xUnit + Shouldly | ADR-0052 |
| Test naming | sentence-style with underscores | ADR-0053 |
| Test data | hand-written probe types, no AutoFixture | ADR-0054 |
| Value-object marker | `IValueObject` / `IValueObject<T>` — what makes the exemption legible | ADR-0066 |
| Identifier shape | `readonly record struct X(Guid Value) : IStronglyTypedId<Guid>` | ADR-0039, ADR-0090 |
| Collection declarations | explicit type + collection expression (`List<Type> x = [];`) | CLAUDE.md house rule, `dotnet_style_prefer_collection_expression` at `warning` |
| Guards | none needed — no argument preconditions are added | ADR-0105 |
| Scope of the ban | constitution §II's `Banned` set, **unchanged** | ADR-0139, ADR-0140 |
