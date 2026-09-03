# Implementation Plan: A position and a size, not four loose decimals

**Branch**: `refactor/2051-a-position-and-a-size` | **Date**: 2026-09-03 |
**Spec**: [spec.md](./spec.md) | **Issue**: #2051

**Input**: Feature specification from `/specs/060-a-position-and-a-size/spec.md`

## Summary

Two new value objects in `OverlayDesigner.Domain.Overlay` —
`NormalizedPosition(decimal X, decimal Y)` and
`NormalizedSize(decimal Width, decimal Height)` — replace the four loose
`decimal`s on `Label`. The range guards move with them and become
`Ensure.That(...)` (ADR-0105). Six call sites follow the compiler. Nothing on
the wire, in the database, or in either React app changes.

The order is the design decision, not the types. Three of the six places the
four numbers travel have no assertion on them today, so **the covering tests
come first, are observed green against the unchanged code, and their output is
the artifact carried into the PR** — ADR-0144's characterisation path. Only
then does the shape move.

The types themselves are a copy of `GridPosition`/`GridDimensions`, which
`LayoutComposition` has had since spec 010 and which spec 057 finished. The
mapping is a copy of spec 058's owned reference on pinned columns, one nesting
level deeper — the one genuine unknown, held open in [research.md](./research.md)
R1 with both branches specified.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: EF Core 10.0.11 + Npgsql (owned references); xUnit +
Shouldly + Moq; SonarAnalyzer (ADR-0084 metrics)

**Storage**: PostgreSQL. **No schema change.** The four columns `label_x`,
`label_y`, `label_width`, `label_height` on `overlay_revisions` keep their
names, types and non-nullability. Verified per SC-004, not assumed.

**Testing**: xUnit unit tests for the two new value objects; existing
`OverlayDesigner` domain and application suites; `Architecture.Tests`;
`Integration.Tests` via `AspireFixture` (ADR-0103) — **CI only**, this machine
has no Docker.

**Project Type**: Backend only. `apps/kiosk-web` and `apps/management-web` both
read `normalizedX`/`normalizedY`/`normalizedWidth`/`normalizedHeight` off the
frozen wire shapes and MUST NOT be touched (FR-011, SC-008).

**Performance Goals**: None. §IV: **N/A** — no code on any of the six legs
changes; see spec.md.

**Constraints**: Behaviour-preserving throughout, with one declared exception —
the `(0, 1]` guard message gains a colon (research R2). Constitution §Testing's
green-throughout obligation applies; its red-first obligation does not.

**Scale/Scope**: 2 new value objects, 1 changed value object, 1 EF
configuration, 2 API endpoints, 2 application handlers, 1 architecture
assertion, 36 test construction sites across two test projects.

## Constitution Check

*GATE: passed before Phase 0. Re-checked after design.*

| Principle | Assessment |
|---|---|
| **§II — DDD with value objects** | Applied, not violated-then-fixed. `Label`'s decimals are already exempt as a value object's backing values; this groups them into two concepts whose components are still exempt by the same mechanism. `PrimitiveBoundaryTests` passes before and after. |
| **§III — bounded contexts** | No cross-context reference introduced. The reason `OverlayLifecyclePublishedNotification` is out of scope *is* this principle. |
| **§IV — latency budget** | N/A, with the reason named in spec.md rather than waved off. No leg's figure is claimed or disturbed. |
| **§Testing** | Green-throughout. The characterisation set exists before the change (FR-013) and its assertions survive it (FR-014, SC-003). |
| **ADR-0084 — metrics** | `Label.cs` shrinks: the two `Ensure*` helpers leave, so file LOC and method count both fall. The two new files are ~25 lines each. |
| **ADR-0065 — coverage** | `OverlayDesigner.Domain` is gated at 90%. Two new types with guarded factories need their own tests, which T503/T504 supply. |
| **ADR-0087 — linear history** | Commits sequenced so each builds alone (see *Sequencing*). |
| **ADR-0105 — guards** | The two bare `throw new ArgumentException` helpers are removed. This is the ADR being satisfied, not excepted. |
| **ADR-0144 — the lane** | Behaviour-preserving → characterisation. No ADR written. Declared in spec.md. |

**No violations. No complexity deviation to record.**

## Bounded context and layers

One context, four layers, all inside `OverlayDesigner`:

```
src/OverlayDesigner/
  Domain/Overlay/
    NormalizedPosition.cs          NEW   value object, [0, 1] per component
    NormalizedSize.cs              NEW   value object, (0, 1] per component
    Label.cs                       CHANGED  four decimals → two value objects
  Application/
    Queries/Handlers/GetOverlayQueryHandler.cs        member paths only
    EventHandlers/OverlayRevisionPublishedDomainEventHandler.cs   member paths only
  Infrastructure/Persistence/Configurations/
    OverlayConfiguration.cs        CHANGED  nested owned reference, pinned columns
  Api/
    OverlayEndpoints.Commands.cs   CHANGED  two Label.From call sites, both
                                            inside the existing try/catch
```

Untouched by design, and each for a stated reason:

| File | Why it does not change |
|---|---|
| `Api/Requests/LabelRequest.cs` | Wire shape at the trust boundary. §II exempts `Shared.Contracts`; this is the same category. |
| `Application/DTOs/OverlayDto.cs` | Response shape. Both React apps read it. |
| `Shared.Contracts/OverlayDesigner/OverlayRevisionPublishedV1.cs` | Integration event, `V1`-suffixed and versioned (ADR-0073). |
| `LayoutComposition/Domain/Layout/ILayoutLifecycleBroadcaster.cs` | Cross-context reference forbidden — spec.md *Out of Scope*. |
| `LayoutComposition/Infrastructure/Broadcasting/OverlayRevisionPublishedHubMessage.cs` | SignalR frame the kiosk parses. |
| `ScenarioSimulator/Seeding/OverlayLabel.cs` | Mirrors `LabelRequest` and posts JSON. |
| `apps/**` | FR-011. |

## Entities, value objects, invariants

### `NormalizedPosition`

```
NormalizedPosition(decimal X, decimal Y) : IValueObject
  From(decimal normalizedX, decimal normalizedY)
```

- **Invariant**: `X ∈ [0, 1]` and `Y ∈ [0, 1]`. Both guarded with
  `Ensure.That(normalizedX).InRange(0m, 1m)`, which reproduces the current
  message character-for-character (research R2).
- **Why the parameter names differ from the properties**: FR-007. The message
  is copied into the API's `400` detail; keeping `normalizedX` keeps the detail
  byte-identical. Deliberate asymmetry, recorded so it is not "cleaned up".
- **`ToString()`**: `$"({X},{Y})"`, mirroring `GridPosition`.

### `NormalizedSize`

```
NormalizedSize(decimal Width, decimal Height) : IValueObject
  From(decimal normalizedWidth, decimal normalizedHeight)
```

- **Invariant**: `Width ∈ (0, 1]` and `Height ∈ (0, 1]`. Zero refused — a label
  with no area is not a label. `InRange` has no exclusive-lower overload, so
  this is `Ensure.That(normalizedWidth).Satisfies(v => v is > 0m and <= 1m,
  "must be in (0, 1].")`, as `GridDimensions` already does for its cell cap.
- **Declared behaviour change**: the message gains a colon. Research R2, §"Ruling".
- **`ToString()`**: `$"{Width}x{Height}"`, mirroring `GridDimensions`.

Neither type validates against the other. A position plus a size can describe a
rectangle running off the right edge, and that is true today too — the label is
clipped by the kiosk composite, and no aggregate invariant says otherwise.
**Not adding that rule** is deliberate: it would be new behaviour in a
behaviour-preserving change, which ADR-0144 splits into two issues.

### `Label`

```
Label(string Text, NormalizedPosition Position, NormalizedSize Size, int FontSizePx) : IValueObject
  From(string text, NormalizedPosition position, NormalizedSize size, int fontSizePx)
```

- Keeps `MaximumTextLength`, `MinimumFontSizePx`, `MaximumFontSizePx` and their
  guards unchanged. `Text` and `FontSizePx` stay primitives — out of scope.
- `EnsureNormalized` and `EnsurePositiveNormalized` are **deleted**, not moved
  wholesale: their logic lives in the two `From` factories as `Ensure` calls.
- `From` gains `Ensure.That(position).IsNotNull()` and
  `Ensure.That(size).IsNotNull()` — both are record classes, and NRT is enabled
  but not proof against a boundary caller (ADR-0141, ADR-0105).
- **Four positional arguments become four differently-typed ones.** That is
  FR-005, and it is the whole feature.

## Persistence

Primary shape (research R1, preferred branch):

```csharp
revisions.OwnsOne(revision => revision.Label, label =>
{
    label.Property(l => l.Text).HasColumnName("label_text")
         .HasMaxLength(Label.MaximumTextLength).IsRequired();

    label.OwnsOne(l => l.Position, position =>
    {
        position.Property(p => p.X).HasColumnName("label_x").IsRequired();
        position.Property(p => p.Y).HasColumnName("label_y").IsRequired();
    });
    label.Navigation(l => l.Position).IsRequired();      // load-bearing

    label.OwnsOne(l => l.Size, size =>
    {
        size.Property(s => s.Width).HasColumnName("label_width").IsRequired();
        size.Property(s => s.Height).HasColumnName("label_height").IsRequired();
    });
    label.Navigation(l => l.Size).IsRequired();          // load-bearing

    label.Property(l => l.FontSizePx).HasColumnName("label_font_size_px").IsRequired();
});
```

**The two `Navigation(...).IsRequired()` lines are the ones spec 058's research
names as load-bearing.** Without them the owned reference is optional, all four
columns become nullable against a `NOT NULL` schema, every test still passes,
and the divergence surfaces when someone generates an unrelated migration —
issue #2022 exactly.

Fallback shape if R1 fails: `Tile`'s private scalars, mapped as field-backed
properties whose names match the fields. Fully specified in research.md; the
engineer takes it without inventing anything.

**No migration.** Spec 057's `8674d0b` reshaped `Tile`'s model this way and
touched no migration or snapshot file, because the column names were pinned.
Verified here by SC-004, not inherited.

## Messaging

**No change.** The domain event `OverlayRevisionPublishedDomainEvent` carries a
`Label`, so it follows the type automatically and its shape is unchanged.
`OverlayRevisionPublishedDomainEventHandler` projects it onto
`OverlayRevisionPublishedV1`, whose four `decimal` fields stay; only the member
paths it reads change (`label.NormalizedX` → `label.Position.X`).

Domain → integration event boundary (ADR-0040, ADR-0073) is untouched: the
integration event keeps primitives because it is a contract, and this is the
same rule that keeps `LabelRequest` primitive.

## Boundary rules

- No new project reference in any direction. The two new types are
  `internal`-adjacent in spirit but `public`, living in
  `OverlayDesigner.Domain` and never named outside it — `Application` and `Api`
  construct and read them, `Shared.Contracts` never sees them.
- The cross-context reason `OverlayLifecyclePublishedNotification` is out of
  scope is this rule; NetArchTest would fail the alternative.
- `Architecture.Tests` runs unchanged apart from one repointed assertion.

## The guard assertion — factual repoint, not a weakened gate

`PrimitiveBoundaryTests.A_value_objects_own_backing_values_are_exempt` asserts

```csharp
exempted.ShouldContain("Label.NormalizedX");
```

After the refactor that member does not exist. The assertion moves to
`NormalizedPosition.X`. **This is a factual update, and it can be shown rather
than claimed:**

1. **The assertion never asserted the shape this feature changes.** Its stated
   purpose, in the comment above it, is that the *exemption mechanism* still
   fires — "if it ever stops applying, the rule above starts failing on ~79
   legitimate types". Nothing in `PrimitiveBoundaryTests` says `Label` must
   group its coordinates. `Label.NormalizedX` passes today; `NormalizedPosition.X`
   passes after; the gate's strength is identical.
2. **Left unrepointed it would fail for the wrong reason** — a stale name, not
   a rule breach. Repointing restores its stated purpose.
3. **It is, if anything, stronger.** `NormalizedPosition` sits one level deeper
   in the walk (`Overlay → Revision → Label → Position → X`) than anything the
   three assertions reach today, so it additionally proves the walk descends
   through a *nested* composite value object.
4. **The refutable check that this is not a weakening** — and this is the part
   that makes it a claim anyone can test rather than a reassurance:
   `The_walk_reaches_every_aggregate_and_a_useful_amount_of_state` already
   asserts `roots.Count == 11` and `reached > 100`. The engineer records
   `reached` before and after in the PR. It must **rise by two** (the two new
   value-object types), and the offender list in
   `No_domain_model_exposes_primitive_typed_state` must stay empty. **If
   `reached` falls, something stopped being walked, and that is a weakened gate
   — stop and block.**

Nothing else in `Architecture.Tests` is touched. No suppression is added, no
threshold moved, no test deleted.

## Phase 4a — what the test-writer produces

**Colour: characterisation, observed green.** Reasoning in the report; the
mechanics here.

### The characterisation set (existing — identify, run, capture green)

| File | What it locks in |
|---|---|
| `tests/OverlayDesigner.Domain.Tests/Overlay/LabelTests.cs` | 10 methods (4 `[Fact]` + 6 `[Theory]` over 14 `[InlineData]` rows) = 18 executed cases: the four range guards, text trimming and bounds, font bounds, value equality, and all four numbers surviving `From` |
| `tests/OverlayDesigner.Domain.Tests/Overlay/OverlayTests.cs` | Publish raises the event with the label; edit replaces it; `BranchDraft` recovers its geometry |
| `tests/OverlayDesigner.Domain.Tests/Overlay/OverlayRevisionStateMachineTests.cs` | Label edits across the state machine |
| `tests/OverlayDesigner.Application.Tests/**` | 6 handler suites constructing labels |
| `tests/Architecture.Tests/PrimitiveBoundaryTests.cs` | All four tests |
| `tests/Integration.Tests/OverlayDesigner/**` | Create → publish → SignalR, through real Postgres |

### The gaps — written first, observed green against the unchanged code

These are the three uncovered rows plus two thin ones. They are **new tests
that pass on today's code**; a red one is a defect found, not progress, and
stops the feature (spec.md Edge Cases).

- **G1 — the API's `400`.** No test anywhere asserts that an out-of-range
  coordinate or extent produces a `400`. This is the highest-risk regression in
  the feature: both endpoints catch `ArgumentException` around `Label.From`,
  and building the value objects one statement above the `try` turns a `400`
  into a `500` with nothing failing. Needed for create and for edit.
- **G2 — the query projection.** `GetOverlayQueryHandlerTests` asserts `Text`
  and `Version` and none of the four numbers. `GetOverlayQueryHandler` is one
  of the sites the refactor rewrites.
- **G3 — the Postgres round trip.** `OverlayPushIntegrationTests` *parses* all
  four off the SignalR frame (lines 190–193) and asserts only `Text` and
  `FontSizePx`. Asserting the four it already reads closes the EF mapping's
  only end-to-end net — the place a `label_x`/`label_y` transposition would
  actually land.
- **G4 — `BranchDraft` recovery.** Asserts X, Y and font size; not width or
  height. Two assertions added to a passing test.
- **G5 — the integration event.** `OverlayRevisionPublishedDomainEventHandlerTests`
  asserts `NormalizedX` and `NormalizedHeight`; not Y or Width. Two assertions
  added to a passing test.

G4 and G5 **add** assertions to green tests; they do not change one. Both are
written while the old shape still compiles — spec 057's recorded edge case,
"where covering tests are absent on a path being retyped, they are added first".

### What phase 4b may and may not touch

**Allowed — mechanical, no asserted value moves:**

- Rewriting a member-access path: `label.NormalizedX` → `label.Position.X`.
- Rewriting a construction site: `Label.From("t", 0.5m, 0.05m, 0.3m, 0.08m, 48)`
  → `Label.From("t", NormalizedPosition.From(0.5m, 0.05m),
  NormalizedSize.From(0.3m, 0.08m), 48)`.
- **Relocating** the four range theories from `LabelTests` into
  `NormalizedPositionTests` / `NormalizedSizeTests`, carrying every
  `[InlineData]` row and the expected exception type **verbatim**. After the
  change `Label.From` cannot receive a bad coordinate, so the factory that
  refuses it is a different type; the behaviour asserted is identical and the
  case count does not fall.
- Repointing the one `PrimitiveBoundaryTests` exemption assertion.

**Forbidden — any of these stops the work:**

- Changing any asserted literal. `0.5m` stays `0.5m`; `[InlineData(-0.01)]`,
  `[InlineData(1.01)]`, `[InlineData(0)]` all stay.
- Changing an expected exception type or an expected HTTP status.
- Deleting a test, or removing an `[InlineData]` row.
- Touching `roots.Count.ShouldBe(11)`, `reached.ShouldBeGreaterThan(100)`, or
  the `CameraName.NormalizedValue` / `GridPosition.Row` sibling assertions.
- Touching the six frozen wire shapes or the four column names.
- Adding a suppression, relaxing a threshold, or narrowing an analyzer.

### Correcting the issue's "done means"

Issue #2051 asks for the existing tests green "without modification to their
assertions". Read literally that is unsatisfiable: `label.NormalizedX` stops
compiling, so every geometry assertion must be rewritten as a member path, and
the only literal way to comply would be to keep `NormalizedX` shim properties
on `Label` — reintroducing the exact primitives the issue removes.

The criterion is therefore restated, and this restatement is what phase 6
reviews against: **no asserted literal, expected exception type, expected
status code, `[InlineData]` row or test may change; member paths and
construction sites may.** That is what ADR-0144 actually says, and the issue's
phrasing was shorthand for it.

## Sequencing

Each commit builds alone (ADR-0087, FR-014):

1. **The net.** G1–G5, all green against unchanged production code. Ships alone
   and is worth having alone.
2. **The two types plus their own tests.** Nothing references them yet;
   compiles and is green in isolation.
3. **`Label` + every call site + the EF mapping, in one commit.** These cannot
   be split — the moment `Label`'s constructor changes, `Api`, `Application`,
   `Infrastructure` and both test projects stop compiling. A split here would
   produce a commit that only builds with its successor, which rebase-merge
   makes permanent.
4. **The `PrimitiveBoundaryTests` repoint.** Could ride in 3; kept separate so
   the guard change is individually reviewable and individually revertable.

## Risks

| Risk | Mitigation |
|---|---|
| EF cannot nest three deep | R1's experiment before any production edit; `Tile`'s shape fully specified as the fallback |
| A column silently becomes nullable | Two `Navigation(...).IsRequired()` lines; SC-004's `has-pending-model-changes` check |
| The API's `400` becomes a `500` | G1 exists before the change and would fail |
| A transposition introduced *by* the refactor | G2 and G3 close the two sites that have no assertion; the compiler closes the rest afterwards |
| Coverage gate on `OverlayDesigner.Domain` | Two new types get their own test files (T105, T106) |
| The guard repoint reads as gate-weakening | The `reached` before/after figure is recorded in the PR and must rise |
