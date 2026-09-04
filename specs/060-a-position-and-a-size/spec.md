# Feature Specification: A position and a size, not four loose decimals

**Feature Branch**: `refactor/2051-a-position-and-a-size`

**Issue**: #2051 (`agent:ready`, `tech-debt`)

**Created**: 2026-09-03

**Status**: Draft

**Lane**: ADR-0144 autonomous. Phases 1–3 by `architect`; 4a by `test-writer`;
4b–5 by `backend-engineer`; 6 by `backend-reviewer`.

**Input**: Issue #2051 — "`OverlayDesigner.Domain.Overlay.Label` carries a
position and a size as four loose `decimal`s. Transposing X with Y, or Width
with Height, compiles cleanly and changes behaviour silently."

## Context

`Label` is the one domain type in this repository that still describes a
rectangle as four unrelated numbers:

```csharp
public sealed record Label(
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx) : IValueObject
```

`Label.From(...)` takes them the same way — four same-typed positional
arguments in a row. Two things follow, and only the first is in the issue.

**First**, a transposition compiles. `Label.From(text, y, x, h, w, size)` is a
legal call. This is the defect `HandlerDeconstructionTests` exists to catch for
`Shared.Contracts` records, and CLAUDE.md says so in as many words: value
objects make most such swaps a type error, and records carrying primitives do
not. The two pairs are not independently meaningful either — a position without
both coordinates is nothing, a size without both extents is nothing — and the
guards already treat them as two concepts, `[0, 1]` for the coordinates and
`(0, 1]` for the extents.

**Second, and this is what shapes the plan: three of the six places the four
numbers travel through have no assertion on them at all.** Verified by reading
each one:

| Where the four numbers travel | Asserted today? |
|---|---|
| `Label.From` → `Label` (Domain) | **Yes** — all four, `LabelTests.From_accepts_a_normal_label_payload` |
| `Overlay.BranchDraft` recovery of a label | **Partly** — X and Y only |
| `OverlayRevisionPublishedDomainEventHandler` → `OverlayRevisionPublishedV1` | **Partly** — X and Height only |
| `GetOverlayQueryHandler` → `OverlayDto` | **No** — none of the four |
| `OverlayEndpoints.Commands` → 400 on an out-of-range coordinate | **No** — no test exists |
| `OverlayConfiguration` label columns → Postgres round trip | **No** — `OverlayPushIntegrationTests` parses all four off the SignalR frame and asserts only `Text` and `FontSizePx` |

So the very defect the issue is about — a silent transposition — is today
unobservable at three of the six sites. A refactor that moves all six at once,
on that net, is not a refactor; it is a rewrite. Constitution §Testing and
ADR-0144's characterisation path both say the net comes first.

### The sweep, re-run independently

The issue's sweep was re-run here rather than inherited, over every
`src/*/Domain` for any numeric member whose name reads as a coordinate or an
extent. **Two hits, and only two** — the same two the issue reports:

1. `OverlayDesigner.Domain.Overlay.Label` — this feature.
2. `LayoutComposition.Domain.Layout.OverlayLifecyclePublishedNotification` —
   decided in *Out of Scope* below, explicitly rather than by default.

Every other numeric member in any Domain project is either a `const` length
bound on a text value object, or the already-converted grid pair
`GridPosition(int Row, int Col)` / `GridDimensions(int Rows, int Cols)`.
`LayoutComposition` solved this problem for the grid under spec 057, and `Tile`
carries the XML doc that argues the EF case against constitution §II. That is
the template, and it is why this feature makes no new architectural decision.

### Governing decisions

- **Constitution §II** (ADR-0139, amended by ADR-0140) — a domain model does
  not carry primitive-typed state. **This feature is not a §II violation being
  fixed.** `Label` implements `IValueObject`, so its four decimals are already
  exempt as a value object's own backing values, and
  `PrimitiveBoundaryTests` passes today. What §II supplies is the direction of
  travel; what the issue supplies is the reason to travel further.
- **ADR-0038 / ADR-0046 / ADR-0066** — maximalist hand-written value objects,
  `IValueObject` marker, `.From(...)` + `Ensure.That(...)`.
- **ADR-0105** — argument preconditions use `Ensure.That(...)`. `Label`'s
  `EnsureNormalized` / `EnsurePositiveNormalized` use bare
  `throw new ArgumentException`, which is none of ADR-0105's three exemptions.
- **ADR-0091** — no shortcuts or aliases in names.
- **ADR-0144** — the delivery lane, and phase 4a's two colours.
- **Spec 057** (`Tile`'s private scalars) and **spec 058** (a composite mapped
  onto its components' existing columns, `Navigation(...).IsRequired()`) are
  the two in-repo precedents this feature copies.

### Latency budget (constitution §IV)

**N/A — no code on any of the six legs changes.**

Named rather than waved off: the event→overlay-state leg runs
`SystemVariables` → `LayoutComposition` → the SignalR frame → the kiosk. Every
type on that path is a wire shape that this feature leaves byte-identical —
`OverlayRevisionPublishedV1`, `OverlayLifecyclePublishedNotification`,
`OverlayRevisionPublishedHubMessage`. `OverlayDesigner` is the authoring path
an operator uses to design a label, not the runtime path an event travels. No
leg's figure is claimed, disturbed, or discharged here.

## User Scenarios & Testing *(mandatory)*

The "users" are the engineers working in this repository, plus the operator
whose label geometry must survive unchanged.

---

### User Story 1 — The geometry is asserted before anything moves (Priority: P1)

Today an engineer can transpose `label_x` and `label_y` in the EF mapping,
transpose width and height in the query projection, or drop the API's 400 for
an out-of-range coordinate, and the whole suite stays green. The three
uncovered rows in the table above are that hole.

After this story, each of the six sites asserts all four numbers, in the
current shape, with the current member names. Nothing has been refactored yet.

**Why this priority**: It is the safety net the rest of the feature is
performed over, and ADR-0144 requires it to exist *before* the change, not
after. It is also independently valuable — shipped alone and with #2051 then
closed as wontfix, it still closes a real coverage hole on `develop`.

**Independent Test**: With the four decimals still on `Label`, transpose
`label_x` and `label_y` in `OverlayConfiguration`, then transpose the width
and height projections in `GetOverlayQueryHandler`. Confirm a test fails for
each. Revert; confirm green.

**Acceptance Scenarios**:

1. **Given** an overlay is created and published through the HTTP API,
   **When** the SignalR frame arrives, **Then** all four numbers are asserted
   to equal the four submitted, each against its own field.
2. **Given** an overlay is stored and read back, **When** the query handler
   projects it, **Then** all four numbers are asserted, each against its own
   field.
3. **Given** a create or edit request carrying a coordinate outside `[0, 1]`,
   **When** it is submitted, **Then** the response is `400` with title
   `OVERLAY_INVALID_INPUT` — and the same for an extent outside `(0, 1]`.
4. **Given** a fully archived chain is recovered by branching, **When** the
   recovered draft is read, **Then** width and height are asserted alongside
   the position and font size that are asserted today.
5. **Given** a revision is published, **When** the integration event is
   inspected, **Then** all four numbers are asserted, not two.
6. **Given** every test in this story, **When** it is run against the code as
   it stands today, **Then** it **passes**. A red test here is a defect
   discovered, not progress — and it stops the feature (see Edge Cases).

---

### User Story 2 — A transposed coordinate stops compiling (Priority: P2)

`Label` gains a `NormalizedPosition` and a `NormalizedSize`. Passing a
coordinate where an extent is expected, or an extent where a coordinate is
expected, becomes a compile error. The range guards move into the two new
factories and are expressed with `Ensure.That(...)`.

The request body, the response body, the integration event, the SignalR frame
and the four database columns are all untouched. **Two things about a rejected
request do change, both confined to the `400`'s `detail`, and both are declared
rather than discovered** — see FR-007:

1. The extent messages gain a colon after the parameter name, because
   `Satisfies` formats as `$"{parameter}: {message}"` (research R2).
2. A request that is wrong in more than one way can now name a different field
   first. `NormalizedPosition.From(...)` and `NormalizedSize.From(...)` are
   argument expressions, so they run before `Label.From`'s body: the order was
   text → X → Y → W → H → font size and is now X → Y → W → H → text → font
   size. A body with both a blank `text` and `normalizedX: 2` used to be told
   about the text and is now told about the coordinate. Each individual
   rejection is unchanged; only which one wins a race between two is.

Restoring the old order is **not** wanted. The value objects are constructed by
the caller, so it would mean duplicating the text guard outside `Label.From` —
a worse shape than the reordering it undoes.

**Why this priority**: It is the issue. It is second only because it must be
performed over Story 1's net.

**Independent Test**: Write `Label.From(text, size, position, fontSizePx)` with
the two arguments swapped and confirm the build fails. Then run every test from
Story 1 unchanged in what it asserts, and confirm all still pass.

**Acceptance Scenarios**:

1. **Given** the new shape, **When** a caller passes a `NormalizedSize` where a
   `NormalizedPosition` is expected, **Then** the code does not compile.
2. **Given** the new shape, **When** a caller constructs a position from a
   coordinate outside `[0, 1]`, **Then** construction is refused with an
   `ArgumentException`.
3. **Given** the new shape, **When** a caller constructs a size from an extent
   outside `(0, 1]` — zero included — **Then** construction is refused with an
   `ArgumentException`.
4. **Given** a create or edit request with an out-of-range coordinate,
   **When** it is submitted, **Then** the response is still `400` with title
   `OVERLAY_INVALID_INPUT` and the same detail text as before — for an
   out-of-range **extent**, the same text plus the colon FR-007 declares.
5. **Given** an overlay stored under the old shape, **When** it is read back
   under the new shape, **Then** the four numbers are unchanged and in the same
   four columns.
6. **Given** the refactor is complete, **When** the HTTP request body, the
   response body, the integration event and the SignalR frame are compared
   before and after, **Then** every field name and every value is identical.
7. **Given** the refactor is complete, **When** every Story 1 test is run,
   **Then** all pass with no asserted literal, expected exception type,
   expected status code or `[InlineData]` row changed.

### Edge Cases

- **A Story 1 test comes out red.** That is a pre-existing defect, not this
  feature's. It is a **bug fix**, and ADR-0144 forbids a refactor and a bug fix
  travelling together — characterisation would otherwise lock the defect into
  a test and call it a net. Stop, report the failing test, and file the fix as
  its own issue.
- **The new value objects are built outside the endpoint's `try` block.** Both
  `Label.From` call sites sit inside `try { … } catch (ArgumentException) { 400 }`.
  Constructing `NormalizedPosition.From(...)` in the argument list of a call
  that is itself inside the `try` is fine; constructing it in a statement above
  the `try` turns every out-of-range coordinate into a `500`. Story 1
  scenario 3 is the test that catches this, and it must exist first.
- **The guard message changes.** The `ArgumentException` message and
  `paramName` currently read `normalizedX must be in [0, 1]; got 1.01.`, and
  the API copies that message into the `400`'s `detail`. Naming the new
  factories' parameters `normalizedX`/`normalizedY`/`normalizedWidth`/
  `normalizedHeight` keeps it byte-identical; naming them `x`/`y`/`width`/
  `height` does not. See FR-007.
- **The EF mapping nests one level deeper than anything in the repo.** Spec 058
  verified an owned reference inside an owned *collection*. Label's position
  would be an owned reference inside an owned reference inside an owned
  collection. Settled by experiment before any production edit — plan R1.
- **`Navigation(...).IsRequired()` is omitted.** Spec 058's research records
  this exactly: without it, both columns silently become nullable against a
  `NOT NULL` schema, every test passes, and it surfaces when someone generates
  an unrelated migration. Two navigations need the line here.
- **A commit that only builds with its successor.** Rebase-merge lands commits
  individually (ADR-0087), so each must build alone.

## Requirements *(mandatory)*

### Functional Requirements

**Coverage first (Story 1)**

- **FR-001**: Each of the six sites in the Context table MUST assert all four
  numbers, each against its own named field, before any production type
  changes.
- **FR-002**: An out-of-range coordinate and an out-of-range extent MUST each
  be shown to produce a `400` from both the create and the edit endpoint.
- **FR-003**: Every test added under FR-001 and FR-002 MUST be observed
  **passing** against the unchanged code, and that output MUST be carried into
  the PR body as ADR-0144's transported artifact.

**The shape (Story 2)**

- **FR-004**: `Label` MUST expose one `NormalizedPosition` and one
  `NormalizedSize`, and MUST NOT expose a `decimal` position or extent.
- **FR-005**: Passing a `NormalizedSize` where a `NormalizedPosition` is
  expected, or the reverse, MUST fail to compile.
- **FR-006**: The `[0, 1]` and `(0, 1]` range guards MUST live in the two new
  value objects' `From(...)` and MUST use `Ensure.That(...)` (ADR-0105). The
  two bare `throw new ArgumentException` helpers MUST be gone.
- **FR-007**: The `ArgumentException` type and `paramName` produced for each of
  the four out-of-range cases MUST be unchanged, because the message is copied
  verbatim into the API's `400` detail. The **message text** MUST be unchanged
  for the two coordinates, and for the two extents MUST differ only by the
  colon `Satisfies` puts after the parameter name (research R2). In particular
  the offending value MUST still be echoed: `normalizedWidth: must be in
  (0, 1]; got 0.`, not `normalizedWidth: must be in (0, 1].`. All four are
  asserted character-for-character by
  `OverlayGeometryValidationIntegrationTests`.
- **FR-007a**: Guard **order** within a rejected request MAY change, and does:
  see the US2 preamble. What MUST NOT change is any individual guard's verdict
  or its message.
- **FR-008**: Both new types MUST implement `IValueObject` (ADR-0066), so
  their backing decimals are exempt under §II by the same mechanism as
  `GridPosition.Row`.

**Nothing else moves (Story 2)**

- **FR-009**: `LabelRequest`, `OverlayDto`, `OverlayRevisionPublishedV1`,
  `OverlayLifecyclePublishedNotification`, `OverlayRevisionPublishedHubMessage`
  and `ScenarioSimulator.Seeding.OverlayLabel` MUST keep their primitives and
  their field names.
- **FR-010**: The four columns `label_x`, `label_y`, `label_width`,
  `label_height` on `overlay_revisions` MUST be unchanged in name, type and
  nullability, and no migration MUST be generated.
- **FR-011**: The two React apps MUST NOT be touched. Both read
  `normalizedX`/`normalizedY`/`normalizedWidth`/`normalizedHeight` off the wire
  shapes FR-009 freezes.

**The guard assertion**

- **FR-012**: `PrimitiveBoundaryTests`' exemption assertion naming
  `Label.NormalizedX` MUST be repointed to the new value object's own backing
  value, and the repoint MUST be shown not to weaken the gate: the type count
  the walk reaches MUST NOT fall, and the offender list MUST stay empty.

**Sequencing**

- **FR-013**: Story 1 MUST be complete and green before any production type
  changes.
- **FR-014**: Each commit MUST build on its own (ADR-0087).

### Key Entities

- **`NormalizedPosition`** — a resolution-independent point, `X` and `Y` each
  in `[0, 1]`. Meaningless without both.
- **`NormalizedSize`** — a resolution-independent extent, `Width` and `Height`
  each in `(0, 1]`. Zero is refused; a label with no area is not a label.
- **`Label`** — text, one position, one size, one font size. Unchanged in what
  it means, changed in what it can be built from wrongly.

## Success Criteria *(mandatory)*

- **SC-001**: Transposing a coordinate with an extent fails the build. Verified
  by writing the swapped call and reading the compiler error, not by a runtime
  test — ADR-0144 forbids manufacturing one for a compile-time guarantee.
- **SC-002**: Every one of the six sites in the Context table asserts all four
  numbers. Three of six today; six of six after.
- **SC-003**: Every test written under Story 1 passes before the refactor and
  after it, with no asserted literal, expected exception type, expected status
  code or `[InlineData]` row changed.
- **SC-004**: `dotnet ef migrations has-pending-model-changes` reports nothing
  new for `OverlayDesigner` beyond the pre-existing `version` drift of issue
  #2022, and no migration file is added.
- **SC-005**: `dotnet build -c Release` is clean — SonarAnalyzer metrics
  (ADR-0084) and the collection-expression warning included.
- **SC-006**: `PrimitiveBoundaryTests` is green with all three of its tests, and
  the type count its walk reaches is greater than or equal to the count before.
- **SC-007**: Domain coverage for `OverlayDesigner.Domain` stays at or above
  ADR-0065's 90%.
- **SC-008**: No file under `apps/` is modified.

## Assumptions

- **The record stays positional.** `Label` remains
  `record Label(string Text, NormalizedPosition Position, NormalizedSize Size,
  int FontSizePx)` rather than adopting `Tile`'s private-scalar shape.
  `Tile` needs private scalars because EF cannot key
  `layout_revision_tiles` on `(revision_id, row, col)` through an owned
  navigation; `Label` is not part of any key, so the constraint that forced
  `Tile`'s shape does not reach it. If R1 shows EF cannot map the nesting,
  the fallback is `Tile`'s shape exactly — this is stated as a fallback, not
  left as a discovery.
- **The properties are `X`, `Y`, `Width`, `Height`, not `NormalizedX` and so
  on.** The type name already carries "normalized", and `GridPosition.Row` is
  the precedent. `X` is not a shortcut under ADR-0091 — it is the name of an
  axis, not an abbreviation of a longer word.
- **The factory parameters keep the old names** (`normalizedX`, …) even though
  the properties do not, purely so FR-007's message text is preserved. The
  asymmetry is deliberate and is the cheapest way to make the refactor
  message-preserving rather than message-approximate.
- **`Ensure`'s value guards raise `ArgumentException`.** Read, not assumed:
  `EnsuredValue<T>.InRange` and `.Satisfies` both
  `throw new ArgumentException($"…", parameter)`. `InRange(0m, 1m)` reproduces
  `EnsureNormalized` exactly, including the `[0, 1]` wording. `(0, 1]` has no
  `InRange` overload and takes `Satisfies`, as `GridDimensions` already does
  for its cell cap — that one message does change wording, and FR-007 is
  therefore satisfied for three of the four cases by `InRange` and for the
  extents by writing the `Satisfies` message to match.
- **No migration is needed.** Spec 057's commit `8674d0b` reshaped `Tile`'s
  model exactly this way and touched no migration or snapshot file, because
  the column names were pinned. Verified by SC-004 rather than trusted.
- **There is no production deployment**, so FR-010's schema neutrality is a
  correctness property to check, not a migration to coordinate.

## Out of Scope

- **`OverlayLifecyclePublishedNotification` keeps its four decimals.** Decided
  explicitly, as the issue asked, and the decision is forced rather than
  preferred. The record lives in `LayoutComposition.Domain`; the new value
  objects would live in `OverlayDesigner.Domain`. Giving it those types is a
  cross-context project reference, which CLAUDE.md forbids outright and
  NetArchTest fails. Its own XML doc already states the reason — "primitive
  types only so the broadcaster contract does not need to reference
  OverlayDesigner.Domain" — alongside the `Guid Overlay`, `int RevisionNumber`
  and `string Name` it carries for the same reason. Duplicating the two value
  objects into `LayoutComposition.Domain` is rejected as well: it would create
  a second, independently-drifting definition of the same two concepts, for a
  record whose only consumer is `OverlayRevisionPublishedHubMessage`, which
  would still be primitives on the wire. It is also outside §II's binding
  surface as `PrimitiveBoundaryTests` draws it — the walk starts at aggregate
  roots, and a notification record is not reachable from `Layout`'s state.
- **`Label.Text` (raw `string`) and `Label.FontSizePx` (raw `int`).** Same
  category of finding, not a position or a size. Separate issue.
- **`GridPosition.Col` / `GridDimensions.Cols`.** ADR-0091 bans the shortcut,
  but renaming touches the EF column mapping. Separate issue.
- **Any behaviour an operator, a kiosk or an HTTP client can observe**, with
  the two declared exceptions inside a `400`'s `detail` — the extents' colon
  and the guard order — set out in the US2 preamble and FR-007/FR-007a.
- **The React apps.**
- **A new ADR.** None is needed — see the note below, which is a declaration
  the lane is required to make.

## Declaration: this applies a decision, it does not make one

ADR-0144 forbids the autonomous lane from writing an ADR, and requires an
issue whose honest answer is a new architectural decision to be blocked. This
one is not.

Every element is already decided and already built somewhere in this
repository: value objects are maximalist and hand-written (ADR-0038, ADR-0046);
they carry an `IValueObject` marker and a guarded `From(...)` (ADR-0066);
argument preconditions use `Ensure.That(...)` (ADR-0105); a domain model does
not carry primitive-typed state (§II, ADR-0139/0140); a composite maps onto its
components' existing columns as an owned reference (spec 058); and the
identical two-value coordinate pair already exists as `GridPosition` and
`GridDimensions` in a neighbouring context. The only judgement this feature
makes that is not already written down is which of two existing EF shapes to
copy, and both are in-repo.

**No ADR is required, and none is written.**
