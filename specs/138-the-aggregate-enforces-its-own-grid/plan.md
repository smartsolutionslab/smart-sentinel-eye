# Plan 138 — The `Layout` aggregate enforces its own grid invariants

**Spec:** `specs/138-the-aggregate-enforces-its-own-grid/spec.md`
**Issue:** #2187
**Phase:** 2 — Plan
**Engineer:** `backend-engineer` (single agent; see §7)

---

## 1. Bounded context and layers

**One context, one layer.** `LayoutComposition` → `Domain` only.

| Layer | Touched | Why |
|---|---|---|
| `LayoutComposition/Domain` | **yes** | The guard and the four doc corrections |
| `LayoutComposition/Application` | **no** | Handlers keep their `ValidateGrid` call and their `GridViolation` → `LAYOUT_GRID_*` mapping verbatim (spec §3) |
| `LayoutComposition/Infrastructure` | **no** | No schema, no EF configuration, no migration |
| `LayoutComposition/Api` | **no** | No endpoint, no request/response DTO, no scope |
| `Shared.Kernel` / `Shared.Contracts` | **no** | No new guard member, no new contract, no event-shape change |
| `apps/*` | **no** | No frontend change |
| `tests/LayoutComposition.Domain.Tests` | **yes** | The red tests |

**Boundary rules (ADR-0051 / NetArchTest):** unaffected. Nothing crosses a
context boundary; no project reference is added; `Shared.Contracts` is untouched.
`PrimitiveBoundaryTests` and `HandlerDeconstructionTests` are unaffected — no
domain property changes and no handler is edited.

## 2. Entities, value objects, invariants

Nothing new is introduced. The existing shapes:

| Type | Kind | Role here |
|---|---|---|
| `Layout` | Aggregate root (`AggregateRoot<LayoutIdentifier>`) | Gains the guard on two of its five mutators |
| `Revision` | Owned sub-entity | Unchanged code; two doc comments become true |
| `GridDimensions(Rows, Cols)` | Value object, `IValueObject` | Unchanged. `MaxTiles = 4`, `MaxCells = 4`, `Contains(position)` |
| `GridPosition(Row, Col)` | Value object | Unchanged code; its doc comment becomes true |
| `Tile(Camera, Overlay, Position)` | Value object | Unchanged |
| `GridViolation` | Enum: `Empty`, `DuplicatePosition`, `OutOfBounds`, `TooLarge` | Unchanged values; doc records the two-tier arrangement |

**The invariant this plan enforces** (ADR-0112 §2, unchanged wording):

> Every persisted `Revision` carries **≥ 1 tile**, **≤ `MaxTiles`** populated
> tiles on a grid of **≤ `MaxCells`** cells, with **every tile in-bounds** and
> **no two tiles at the same `GridPosition`**.

**Where it is now checked** — the change in one line: `Layout.ValidateGrid` gains
its two missing callers, both inside the aggregate.

## 3. The mechanism, concretely

`Layout.cs` gains one private helper and two call sites.

```csharp
/// <summary>
/// The aggregate's own backstop for the four spec-010 grid invariants
/// (ADR-0112 §2). Reached only when a caller skipped <see cref="ValidateGrid"/>:
/// both command handlers validate first and map the violation to a
/// LAYOUT_GRID_* 400, so an operator's bad input is a Result failure and never
/// this throw (ADR-0047). A violation arriving here is programmer error, the
/// same category as the illegal state transitions above.
/// </summary>
private static void RequireValidGrid(GridDimensions grid, IReadOnlyList<Tile> tiles)
{
    Option<GridViolation> violation = ValidateGrid(grid, tiles);
    if (violation.HasValue)
    {
        throw new InvalidOperationException(
            $"Grid {grid} with {tiles.Count} tile(s) violates {violation.Value}.");
    }
}
```

**Call ordering, and why:**

- `CreateDraft` — after the four existing `Ensure.That(...)` guards, before
  `clock.UtcNow` and the `Layout` construction. A null argument must still throw
  `ArgumentNullException` (three existing tests assert this:
  `LayoutGuardTests.cs:23-45`, `LayoutTests.cs:329`), and nothing is constructed
  before the refusal.
- `EditDraft` — after the two existing `Ensure.That(...)` guards and **before**
  `RequireRevision(number)`. Deliberate: the grid + tiles are bad regardless of
  which revision they target, so the argument fault is reported ahead of the
  lookup fault, and no revision is mutated. The existing `ReplaceTiles` Draft-state
  guard (`Revision.cs:114-118`) is untouched and still fires for a valid grid
  aimed at a Published revision — `LayoutTests.cs:245` and
  `LayoutRevisionStateMachineTests.cs:108` both stay green.

`RequireValidGrid` is `private static`, sits beside `ValidateGrid`, and is named
after the existing `RequireRevision` (`Layout.cs:291`) so the file keeps one
vocabulary for "the aggregate insists".

**Rejected alternatives** are argued in spec §3: a `Result`-returning signature
change, a bespoke `GridViolationException`, and an `Ensure.That` chain (which
`EnsuredObject<T>` cannot express — it offers only `IsNotNull()`).

**Metrics (ADR-0084) — measured, not assumed.** S104's 300-LOC limit counts
*lines of code*, not physical lines: comment-only and blank lines do not count.
`Layout.cs` is **295 physical lines but ~144 LOC** (`grep -vE '^\s*($|///|//|/\*|\*)'`),
because more than half the file is XML documentation. The helper adds ~8 code
lines and the two call sites add 2, landing near **154** — nowhere near the
limit, and the doc corrections cost nothing at all against it. S104 is active for
production code (`Directory.Build.props:108` NoWarns it for test projects only)
and warnings are errors in Release, so this was worth measuring rather than
eyeballing the line count. Method length (≤30), parameter count (≤4), complexity
(≤10) and nesting (≤3) are all comfortably met.

## 4. Messaging — domain event → integration event

**None.** No domain event is raised or changed. `Layout.CreateDraft` raises
nothing by design (*"drafts are not observable to kiosks"*, `Layout.cs:91-92`),
and `EditDraft` raises nothing either. `LayoutRevisionPublishedV2` /
`LayoutRevisionArchivedV1` are untouched, so no `Shared.Contracts` version bump
and no subscriber change (Audit's `IntegrationEventAuditHandler` is unaffected).

The *effect* on messaging is negative-space: a malformed revision can no longer
reach `Publish`, so `LayoutRevisionPublishedV2` can no longer carry an
out-of-bounds tile to the kiosk.

## 5. Persistence and concurrency

Unchanged. No migration, no EF configuration change, no index. The two-layer
optimistic concurrency (ADR-0043/0113: `If-Match` + EF token) is untouched.
`IdempotencyKey` handling on `POST /layouts` (ADR-0142) is untouched.

**EF materialisation is explicitly not guarded**: `Revision`'s private
parameterless constructor and property setters are how EF rehydrates a row, and
a guard there would make a malformed legacy row unreadable rather than
unwritable. Recorded so the next reader does not "complete" the fix.

## 6. Doc corrections — the four statements, made true or made accurate

| File:line | Today | After |
|---|---|---|
| `Layout.cs:93` | *"The grid + tiles must already be valid (`ValidateGrid`)."* | The aggregate validates; handlers validate first and own the 400 mapping |
| `Layout.cs:154` | same | same |
| `Layout.cs:16-19` | *"command handlers call it and map the first violation…"* | extended: the aggregate calls it too, as the backstop |
| `GridViolation.cs:5-9` | *"an operator input error is a `Result` failure, not a thrown exception (ADR-0047)"* | keeps that sentence and adds the second tier, so the line is not read as forbidding the backstop |
| `Revision.cs:16-17`, `:108-109` | *"validated by the owning aggregate"* | **no edit** — these become true |
| `GridPosition.cs:9-11` | *"validated by the owning `Layout` aggregate"* | **no edit** — becomes true |

## 7. Engineer assignment and parallelism (ADR-0109)

**One engineer: `backend-engineer`.** The entire change is C# in one bounded
context's Domain project plus one test project. No frontend file, no Aspire
resource, no CI workflow, no Helm chart, no migration — so neither
`frontend-engineer` nor `infra-engineer` has a slice.

**No `[P]` markers.** Every task touches `Layout.cs` or the one new test file,
and the phase-4a/4b split (`test-writer` → `backend-engineer`) is strictly
sequential by ADR-0144: the red output is the engineer's brief. There is no
disjoint-file fan-out to exploit, and manufacturing one would slow the slice.

**No foundational blocker.** Nothing in `Shared.Kernel`, `Shared.Contracts`,
`AppHost`, or an Aspire resource changes, so no task gates any other work on the
board.

## 8. Constitution and ADR alignment

| Rule | Status |
|---|---|
| §II — no primitives on a domain model | Unaffected. No property is added. The helper's parameters are the existing `GridDimensions` / `IReadOnlyList<Tile>` value-object types |
| §III — bounded-context isolation | Unaffected; no cross-context reference |
| §IV — latency budget | **N/A**, argued in spec §6. No leg touched, no §VII dashboard obligation |
| §IX / Karpathy — no speculative generality | Honoured: no new type, no new config knob, no repair path for a population that does not exist, `BranchDraft` left alone |
| §Testing — new behaviour starts red | **Red colour**, ADR-0139. The four × two refusals must be observed failing for the right reason |
| ADR-0047 | Honoured: `Result` for the operator, exception for the programmer |
| ADR-0105 | Honoured: `Ensure.That` guards keep their position and their precedence |
| ADR-0112 §2 | **Implemented** — this is the ADR the code was not honouring |
| ADR-0144 | No ADR written, no gate weakened, phase 4a not skipped |

## 9. Risks

1. **The red tests could arrive green.** If a test builds an invalid grid through
   a path that already throws — e.g. `GridDimensions.From(3,3)`, whose
   `Ensure.That(rows * cols).Satisfies(...)` refuses > `MaxCells` — the test
   proves nothing about the aggregate. **Mitigation:** the `TooLarge` cases must
   construct `new GridDimensions(3, 3)` directly, exactly as
   `LayoutTests.cs:305-311` already does, and the phase-4a report must quote a
   failure naming the *missing throw*, not an `ArgumentException` from the value
   object.
2. **A guard placed too early breaks the null-guard tests.** Mitigated by the
   ordering in §3 and by `LayoutGuardTests` staying green unmodified.
3. **~~`Layout.cs` crossing 300 LOC~~ — measured and dismissed.** S104 counts
   lines of code, not physical lines; the file is ~144 LOC and lands near 154.
   Listed because the physical line count (295) *looks* like a near-miss and
   would have been reported as a risk on inspection alone. See §3.
4. **The handler check being "cleaned up" as redundant during review.** Removing
   it turns every operator input error into a `500`. Spec §3's table and the
   end-to-end step 2 exist to catch exactly that; the helper's doc comment says
   so at the site.
