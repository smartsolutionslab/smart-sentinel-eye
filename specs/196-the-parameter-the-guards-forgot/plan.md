# Plan 196 — The parameter the guards forgot

**Phase:** 2 (Plan) — ADR-0037
**Spec:** [`spec.md`](./spec.md)
**Issue:** [#2309](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2309)
**ADRs:** ADR-0105, ADR-0059, ADR-0139, ADR-0144, ADR-0112, ADR-0047, ADR-0036, ADR-0052

---

## Bounded context and layers

| | |
|---|---|
| **Context** | LayoutComposition |
| **Layer touched** | **Domain only** — `src/LayoutComposition/Domain/Layout/Layout.cs` |
| **Layers not touched** | Application, Infrastructure, Api, `Shared.Contracts`, `Shared.Kernel` |
| **Tests touched** | `tests/LayoutComposition.Domain.Tests/Layout/LayoutGuardTests.cs` |
| **Cross-context refs** | none added; `BoundaryTests` unaffected |
| **New types** | none |
| **Signature changes** | none |

`Ensure` already lives in `Shared.Kernel` and is already imported by
`Layout.cs` (`using SmartSentinelEye.Shared.Kernel;`, line 2). No new `using`,
no new package, no DI registration.

---

## Entities, value objects, invariants

Nothing changes shape. Restated so the plan is checkable against §II:

| Member | Type | Kind | Relevance |
|---|---|---|---|
| `GridDimensions(int Rows, int Cols)` | `sealed record : IValueObject` | **reference** | the parameter being guarded; null is representable |
| `Tile` | `sealed record` | reference | `IReadOnlyList<Tile> tiles` already guarded |
| `FabIdentifier`, `LayoutName`, `CreatedAt` | `sealed record` | reference | already guarded where they are parameters |
| `OperatorIdentifier`, `LayoutRevisionNumber`, `CameraIdentifier` | `readonly record struct` | **value** | **cannot** be guarded — `Ensure.That<T>` is `where T : class`; their absence from the guard list is correct, not an omission |
| `IClock` | interface | reference | already guarded on all six methods that take it |

**The four grid invariants are untouched** (ADR-0112 §2): `Empty`, `TooLarge`,
`OutOfBounds`, `DuplicatePosition`, and the two-tier treatment — operator-facing
`Result<T, Error>` mapped in the handler (ADR-0047), programmer-error
`InvalidOperationException` in the aggregate's `RequireValidGrid` backstop.

**The new invariant is not a domain invariant at all.** It is an *argument
precondition*, which ADR-0105 places in a different category from both tiers:
it throws `ArgumentNullException` before either tier is consulted. That
ordering is asserted by AS-4.

---

## Messaging — domain event → integration event

**None.** No domain event is raised, changed or consumed.
`LayoutRevisionPublishedDomainEvent` and `LayoutRevisionArchivedDomainEvent` are
untouched, and so is every `Shared.Contracts` message. `CreateDraft` raises no
event by design (drafts are not observable to kiosks); `EditDraft` raises none
either.

---

## Grounding — what the three call sites look like today

`src/LayoutComposition/Domain/Layout/Layout.cs`:

```
 :70   public static Option<GridViolation> ValidateGrid(GridDimensions grid, IReadOnlyList<Tile> tiles)
 :72       Ensure.That(tiles).IsNotNull();          <- grid absent
 :74       if (tiles.Count == 0) ... Empty          <- short-circuit, grid never read
 :78       if (grid.Rows * grid.Cols > ...)         <- the dereference

 :103  private static void RequireValidGrid(GridDimensions grid, IReadOnlyList<Tile> tiles)
 :108-109  throw new InvalidOperationException(
               $"Grid {grid} with {tiles.Count} tile(s) violates {violation.Value}.");

 :121  public static Layout CreateDraft(FabIdentifier fab, LayoutName name, GridDimensions grid,
                                        IReadOnlyList<Tile> tiles, OperatorIdentifier createdBy, IClock clock)
 :129      Ensure.That(fab).IsNotNull();
 :130      Ensure.That(name).IsNotNull();
 :131      Ensure.That(tiles).IsNotNull();          <- grid absent, between name and tiles in signature order
 :132      Ensure.That(clock).IsNotNull();
 :133      RequireValidGrid(grid, tiles);

 :187  public void EditDraft(LayoutRevisionNumber number, GridDimensions grid,
                             IReadOnlyList<Tile> tiles, IClock clock)
 :190      Ensure.That(tiles).IsNotNull();          <- grid absent, first in signature order
 :191      Ensure.That(clock).IsNotNull();
 :192      RequireValidGrid(grid, tiles);
```

Line numbers are the tree at `75ee9545` and will shift by the three inserted
lines; the implementer matches on the guard text, not the number.

---

## Design

### Change 1 — `CreateDraft`

Insert between the `name` and `tiles` guards, preserving parameter order:

```csharp
Ensure.That(fab).IsNotNull();
Ensure.That(name).IsNotNull();
Ensure.That(grid).IsNotNull();
Ensure.That(tiles).IsNotNull();
Ensure.That(clock).IsNotNull();
RequireValidGrid(grid, tiles);
```

Position is load-bearing for AS-4: `name` must keep winning when both are null,
and the existing test
`CreateDraft_with_a_null_name_and_an_invalid_grid_still_throws_ArgumentNullException`
(`LayoutGridInvariantTests.cs:232`) is the net under that claim.

### Change 2 — `EditDraft`

`grid` is the first reference-type parameter in the signature
(`number` is a `readonly record struct`), so the guard goes first:

```csharp
Ensure.That(grid).IsNotNull();
Ensure.That(tiles).IsNotNull();
Ensure.That(clock).IsNotNull();
RequireValidGrid(grid, tiles);
```

### Change 3 — `ValidateGrid`

```csharp
Ensure.That(grid).IsNotNull();
Ensure.That(tiles).IsNotNull();
```

`grid` precedes `tiles` in the signature, so the guard does too. Justified in
`spec.md` §Why `ValidateGrid` gets the third line; both command handlers call
this method directly.

**Ordering consequence worth stating explicitly:** after this change,
`ValidateGrid(null!, [])` throws instead of returning `Some(Empty)`. That is
AS-2/AS-3 and it is the intended behaviour change. No existing test asserts the
old answer — `LayoutTests.cs:272` (`ValidateGrid_rejects_an_empty_tile_set_as_Empty`)
passes a non-null `GridDimensions.Cell` and is unaffected.

### No comment is added

ADR-0036: comments say *why*, only when the why is non-obvious. Five guards in a
row, one per reference-type parameter, need no explanation. A comment saying
"guard grid too" would be the drive-by kind.

### What `[CallerArgumentExpression]` yields

`Ensure.That(grid)` captures the literal expression text `grid` at each of the
three call sites, so `ParamName == "grid"` from every path — including the
aggregate paths, where the throw physically originates inside `ValidateGrid`
after `RequireValidGrid` forwarded the value. The tests assert `ParamName`
rather than only the type, so this is verified rather than assumed.

---

## Phase plan and colours

| Phase | Agent | Output |
|---|---|---|
| 4a | **test-writer** | 3 new `[Fact]`s in `LayoutGuardTests.cs`; verbatim **red** output |
| 4b | **backend-engineer** | 3 lines in `Layout.cs`; the same tests green; full suite green |
| 5 | (orchestrator / `/verify`) | verification note |
| 6 | **backend-reviewer** | findings |
| 7 | (orchestrator) | PR to `develop` quoting the red |

### Why the roles are split for a three-line diff

The diff's size is not the reason to collapse phases; what the change *is* is.
The entire deliverable is **which exception a null `grid` produces**. That
claim exists nowhere except in a test assertion. If one agent writes both the
assertion and the guard, nothing separates "the guard works" from "the assertion
was written to match whatever happened" — which is the failure mode ADR-0144's
test-writer/engineer split exists to prevent, and it is *more* acute here, not
less, because there is no other evidence in the diff.

A single-phase treatment was considered and rejected on that ground. ADR-0037's
skip clause covers "typo, dep bump, comment-only"; this changes an exception
type on a public domain API and does not qualify.

### Colour, restated for the reviewer

- **RED** for the three new tests (AS-1, AS-2, AS-3). They must be **observed
  failing** against the unmodified tree and the output quoted in the PR
  (ADR-0139, constitution §Testing). Expected failures: two
  `NullReferenceException`-where-`ArgumentNullException`-expected, and one
  carrying the literal `Grid  with 0 tile(s) violates Empty.`
- **CHARACTERISATION-GREEN** for everything already in
  `LayoutComposition.Domain.Tests` and `LayoutComposition.Application.Tests`
  (AS-5, AS-6). Captured green at T001, must pass **unmodified** at T005. **An
  assertion that has to be edited blocks the change** — it is evidence the
  behaviour moved somewhere it was not supposed to.

Both obligations, different populations. The full argument against the issue's
"characterisation" self-declaration is in `spec.md` §Phase-4a colour.

---

## Risks and how each is closed

| Risk | Closed by |
|---|---|
| Guard inserted in the wrong position, displacing `name` | AS-4; existing `LayoutGridInvariantTests.cs:232` fails if it does |
| `ValidateGrid` guard breaks a handler's 400 mapping | AS-6 + `LayoutComposition.Application.Tests` run at T005; no existing caller passes null |
| New test written green (the phase-4 failure ADR-0144 names) | T002 must produce a non-empty failure list; a green run at T002 blocks |
| Implementer edits the test to pass | role split; test-writer's verbatim output is the engineer's brief and is quoted in the PR |
| Stale binaries make the post-fix run meaningless | T005 runs the full project, not a filter, after the source edit |
| Scope creep into a guard-coverage arch test | `spec.md` §Out of scope; recommended as its own issue |

---

## Constitution and ADR alignment

- **ADR-0105 / ADR-0059** — the change *is* the convention: `Ensure.That(x).IsNotNull()`,
  never `ArgumentNullException.ThrowIfNull` (which `RS0030` bans at `error`
  severity anyway, per `build/guards/BannedSymbols.txt`).
- **ADR-0139 / constitution §Testing** — new behaviour observed failing first,
  quoted in the PR.
- **ADR-0144** — ambiguity resolves to red; the lane writes no ADR and weakens
  no gate. Nothing is deleted, no threshold moved, no suppression added.
- **ADR-0112** — the four grid invariants and their two-tier handling are
  preserved exactly; the guard sits *before* tier one.
- **ADR-0036** — smallest possible change; three lines, no comment, no
  abstraction, no config knob. The one deliberate step beyond the issue's
  literal two lines is argued in `spec.md` and has a one-deletion trim-back.
- **Constitution §II** — no primitive is introduced on a domain model;
  `PrimitiveBoundaryTests` is unaffected.
- **§IV latency** — N/A, reasoned in `spec.md`.

## Gate — phase 2

Plan aligns with the constitution and the ADRs above. Hand back for review
before phase 3.
