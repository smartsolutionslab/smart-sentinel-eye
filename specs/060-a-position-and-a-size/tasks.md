---

description: "Task list for 060 — a position and a size, not four loose decimals"
---

# Tasks: A position and a size, not four loose decimals

**Input**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md)

**Issue**: #2051 · **Branch**: `refactor/2051-a-position-and-a-size`

**Tests**: Not optional. This is ADR-0144's **characterisation** path — Phase 3
below is phase 4a and its output is the transported artifact, captured
**green**, that goes in the PR where a red output would go for new behaviour.

**Organization**: Grouped by user story. Phase 3 (US1) must be complete and
green before Phase 5 (US2) touches a production type.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel — different files, no dependency (ADR-0109)
- **[Story]**: `[US1]` or `[US2]` from [spec.md](./spec.md)

## The three declarations (ADR-0144)

| Declaration | Answer |
|---|---|
| **Colour of phase 4a** | **Behaviour-preserving → characterisation, observed green** — with the compile-time guarantee carried by the type system and **no runtime test manufactured for it**. Mixed, and the mix is spelled out in plan.md §"Phase 4a". |
| **Engineer** | **`backend-engineer`**. C# only: Domain value objects, EF configuration, Application handlers, Api endpoints, xUnit. No React, no Aspire/CI/Docker. |
| **New ADR?** | **No.** This applies §II / ADR-0139 / ADR-0140 / ADR-0038 / ADR-0066 / ADR-0105 and copies two in-repo shapes (`GridPosition`, spec 058's owned reference). Argued in spec.md §"Declaration". |

**Latency (§IV): N/A** — no code on any of the six legs changes; the wire
shapes on the event→overlay-state leg are byte-identical.

---

## Phase 1: Setup — establish what "unchanged" means

**Purpose**: A baseline recorded after the change proves nothing. Both tasks
produce a figure that later verification is compared against, so a pre-existing
problem is never read as this feature's doing.

- [ ] T001 Record the baseline for `OverlayDesigner`: run
  `dotnet ef migrations has-pending-model-changes` and note whether the
  `version` nullability drift of issue #2022 is already reported, so SC-004
  compares against the right starting point
- [ ] T002 [P] Record the baseline `PrimitiveBoundaryTests` figures — the type
  count `The_walk_reaches_every_aggregate_and_a_useful_amount_of_state`
  reaches, and the current contents of the `exempted` list for `Label`. This is
  the before-figure FR-012 requires in the PR
- [ ] T003 [P] Record the baseline test counts for
  `OverlayDesigner.Domain.Tests` and `OverlayDesigner.Application.Tests`, so a
  silently dropped `[InlineData]` row is visible as a falling number

---

## Phase 2: Foundational — settle the one unknown

**Purpose**: The single technical risk, settled by experiment before any
production edit. Blocks Phase 5 only; Phase 3 does not depend on it.

- [ ] T101 Run research R1's experiment: build the `OverlayDesignerDbContext`
  model in a scratch harness with `Label` nested as
  `OwnsMany(Revisions) → OwnsOne(Label) → OwnsOne(Position)` and read the
  relational model — table, column name, CLR type, nullability for all four.
  Record the result in [research.md](./research.md) R1 and take the branch it
  selects. **Do not proceed to T501 until this is recorded.**

---

## Phase 3: User Story 1 — The geometry is asserted before anything moves (P1)

**This phase is ADR-0144 phase 4a.** Agent: `test-writer`.

**Goal**: All six sites in spec.md's Context table assert all four numbers,
against the **unchanged** production code.

**Independent test**: With the four decimals still on `Label`, transpose
`label_x`/`label_y` in `OverlayConfiguration` and the width/height projections
in `GetOverlayQueryHandler`; confirm a test fails for each; revert; confirm
green.

**The rule for this phase**: every test written here must be **green on today's
code**. A red one is a pre-existing defect — stop, report it, and file it as
its own issue. A refactor and a bug fix do not travel together (ADR-0144).

- [ ] T301 [US1] **G1 — the `400` that does not exist yet.** Add integration
  coverage that `POST /overlays` and `PATCH /overlays/{id}/revisions/{n}`
  each answer `400` with title `OVERLAY_INVALID_INPUT` for a coordinate
  outside `[0, 1]` and for an extent outside `(0, 1]`, in
  `tests/Integration.Tests/OverlayDesigner/`. **Highest-value task in the
  feature**: both endpoints catch `ArgumentException` around `Label.From`, and
  constructing the new value objects one statement above that `try` turns every
  such request into a `500` with nothing failing
- [ ] T302 [P] [US1] **G2 — the query projection.** In
  `tests/OverlayDesigner.Application.Tests/Queries/GetOverlayQueryHandlerTests.cs`,
  assert all four numbers on the projected `OverlayDto`, each against its own
  field. Today it asserts `Text` and `Version` only, so a transposition in
  `GetOverlayQueryHandler` — one of the sites this feature rewrites — is
  currently invisible
- [ ] T303 [P] [US1] **G3 — the Postgres round trip.** In
  `tests/Integration.Tests/OverlayDesigner/OverlayPushIntegrationTests.cs`,
  assert the four numbers the test already parses off the SignalR frame (lines
  190–193) and currently discards. This is the only end-to-end net over the EF
  mapping, which is where a `label_x`/`label_y` transposition would land
- [ ] T304 [P] [US1] **G4 — `BranchDraft` recovery.** In
  `tests/OverlayDesigner.Domain.Tests/Overlay/OverlayTests.cs`, add width and
  height assertions to `BranchDraft_on_a_fully_archived_chain_recovers_the_label`
  beside the X, Y and font size it already asserts. Adding to a passing test,
  not changing one
- [ ] T305 [P] [US1] **G5 — the integration event.** In
  `tests/OverlayDesigner.Application.Tests/EventHandlers/OverlayRevisionPublishedDomainEventHandlerTests.cs`,
  add the `NormalizedY` and `NormalizedWidth` assertions beside the
  `NormalizedX` and `NormalizedHeight` already there
- [ ] T306 [US1] Run the full `OverlayDesigner` domain + application +
  architecture suites and the `OverlayDesigner` integration suite. **Capture
  the passing output verbatim** — this is ADR-0144's transported artifact and
  goes in the PR body in the slot a red output would occupy. Record the case
  counts against T003's baseline
- [ ] T307 [US1] Commit Phase 3 alone. It builds and is green with no
  production change, and is worth having on `develop` even if the rest is
  abandoned

**Checkpoint**: FR-013 satisfied. Phase 5 may begin.

---

## Phase 4: (deliberately empty)

There is no shared abstraction, base type, or helper for the two new value
objects to depend on — FR-004 wants two concrete types, and ADR-0036 forbids
inventing a generic one for two instances. The absence is a design outcome, not
an omission.

---

## Phase 5: User Story 2 — A transposed coordinate stops compiling (P2)

Agent: `backend-engineer`. **Depends on T101 and T307.**

**Goal**: `Label` carries a `NormalizedPosition` and a `NormalizedSize`; a
transposition is a compile error; nothing observable changes.

**Independent test**: write `Label.From(text, size, position, fontSizePx)` with
the two swapped and read the compiler error; then run every Phase 3 test with
no asserted literal changed.

### Slice 1 — the two types, referenced by nothing

- [ ] T501 [P] [US2] Create
  `src/OverlayDesigner/Domain/Overlay/NormalizedPosition.cs`:
  `record NormalizedPosition(decimal X, decimal Y) : IValueObject` with
  `From(decimal normalizedX, decimal normalizedY)` guarding each with
  `Ensure.That(…).InRange(0m, 1m)`. **Keep the `normalized*` parameter names**
  — FR-007: the message is copied into the API's `400` detail and `InRange`
  reproduces the current text character-for-character only with those names.
  XML doc states that, so it is not "cleaned up" later
- [ ] T502 [P] [US2] Create
  `src/OverlayDesigner/Domain/Overlay/NormalizedSize.cs`:
  `record NormalizedSize(decimal Width, decimal Height) : IValueObject` with
  `From(decimal normalizedWidth, decimal normalizedHeight)` guarding each with
  `Ensure.That(…).Satisfies(v => v is > 0m and <= 1m, "must be in (0, 1].")`,
  mirroring `GridDimensions`' use of `Satisfies` for its cell cap. XML doc
  records that zero is refused because a label with no area is not a label
- [ ] T503 [P] [US2] Create
  `tests/OverlayDesigner.Domain.Tests/Overlay/NormalizedPositionTests.cs` —
  **relocating** `LabelTests`' `From_rejects_normalizedX_outside_0_to_1` and
  `From_rejects_normalizedY_outside_0_to_1` with every `[InlineData]` row and
  the `ArgumentException` expectation **verbatim**, plus acceptance of the
  bounds and value equality (ADR-0065's 90% gate)
- [ ] T504 [P] [US2] Create
  `tests/OverlayDesigner.Domain.Tests/Overlay/NormalizedSizeTests.cs` the same
  way from `From_rejects_normalizedWidth_outside_0_exclusive_to_1` and
  `From_rejects_normalizedHeight_outside_0_exclusive_to_1`, carrying the
  `[InlineData(0)]` rows that pin zero as refused
- [ ] T505 [US2] Commit slice 1. It builds and is green on its own; nothing
  references the two new types yet

### Slice 2 — the shape moves (one commit; it cannot be split)

The moment `Label`'s constructor changes, `Api`, `Application`,
`Infrastructure` and both test projects stop compiling. Splitting this would
produce a commit that only builds with its successor, which rebase-merge
(ADR-0087) makes permanent.

- [ ] T506 [US2] Change `src/OverlayDesigner/Domain/Overlay/Label.cs` to
  `Label(string Text, NormalizedPosition Position, NormalizedSize Size, int FontSizePx)`;
  `From` takes the two value objects with `Ensure.That(…).IsNotNull()` on each;
  **delete** `EnsureNormalized` and `EnsurePositiveNormalized` (ADR-0105 —
  neither is one of its three exemptions). Text and font-size guards unchanged
- [ ] T507 [US2] Update
  `src/OverlayDesigner/Infrastructure/Persistence/Configurations/OverlayConfiguration.cs`
  per the shape T101 selected, pinning `label_x`, `label_y`, `label_width`,
  `label_height` and **including `label.Navigation(l => l.Position).IsRequired()`
  and `label.Navigation(l => l.Size).IsRequired()`**. Spec 058's research names
  these as load-bearing: without them all four columns silently become nullable
  against a `NOT NULL` schema and nothing fails
- [ ] T508 [US2] Update both `Label.From` call sites in
  `src/OverlayDesigner/Api/OverlayEndpoints.Commands.cs`. **Construct
  `NormalizedPosition.From(...)` and `NormalizedSize.From(...)` inside the
  existing `try { … } catch (ArgumentException ex) { 400 }` block** — above it,
  every out-of-range coordinate becomes a `500`. T301 is the test that catches
  this
- [ ] T509 [US2] Update the member paths in
  `src/OverlayDesigner/Application/Queries/Handlers/GetOverlayQueryHandler.cs`
  and
  `src/OverlayDesigner/Application/EventHandlers/OverlayRevisionPublishedDomainEventHandler.cs`
  (`label.NormalizedX` → `label.Position.X`, and so on). **Field names on
  `OverlayDto` and `OverlayRevisionPublishedV1` do not change** (FR-009)
- [ ] T510 [US2] Follow the compiler through the 36 `Label.From(...)`
  construction sites in `tests/OverlayDesigner.Domain.Tests/**` and
  `tests/OverlayDesigner.Application.Tests/**`, wrapping the four decimals in
  the two factories. **Every literal is carried unchanged** — `0.5m` stays
  `0.5m`. Any urge to change one means the refactor changed behaviour: stop
- [ ] T511 [US2] Verify slice 2 with the three checks that would each catch a
  different silent failure: (a) every Phase 3 test green with no asserted
  literal, exception type, status code or `[InlineData]` row changed; (b)
  `has-pending-model-changes` reports nothing beyond T001's baseline and no
  migration file is added (SC-004); (c) `dotnet build -c Release` clean,
  SonarAnalyzer included (SC-005)
- [ ] T512 [US2] Commit slice 2

### Slice 3 — the guard assertion

- [ ] T513 [US2] Repoint `exempted.ShouldContain("Label.NormalizedX")` to
  `exempted.ShouldContain("NormalizedPosition.X")` in
  `tests/Architecture.Tests/PrimitiveBoundaryTests.cs`. **Change nothing else
  in that file** — not `roots.Count.ShouldBe(11)`, not
  `reached.ShouldBeGreaterThan(100)`, not the `CameraName.NormalizedValue` or
  `GridPosition.Row` siblings
- [ ] T514 [US2] Record the walk's `reached` figure and compare against T002's
  baseline. It must **rise by two** (the two new value-object types), and the
  offender list must stay empty. **If it falls, something stopped being walked
  — that is a weakened gate; stop and block** (FR-012)
- [ ] T515 [US2] Commit slice 3 separately, so the guard change is
  individually reviewable and individually revertable

---

## Phase 6: Cross-cutting

- [ ] T601 [P] Add one sentence to
  `src/LayoutComposition/Domain/Layout/ILayoutLifecycleBroadcaster.cs`'s
  `OverlayLifecyclePublishedNotification` XML doc recording that the value
  objects `OverlayDesigner` introduced for these same four numbers were
  considered and declined here, because taking them would be a cross-context
  project reference. Prevents a third sweep re-raising it. **No issue number in
  the comment** (ADR-0036)
- [ ] T602 Confirm `git status` shows no file under `apps/` modified (SC-008)
- [ ] T603 Confirm `OverlayDesigner.Domain` coverage is at or above 90%
  (ADR-0065, SC-007)

---

## Dependencies

```
T001–T003  (baselines)
   ↓
T101 (R1 experiment) ──────────┐
                               │
T301 … T305 [P] → T306 → T307  │   ← phase 4a, must be green first
                               ↓
                    T501–T504 [P] → T505
                               ↓
              T506 → T507 → T508 → T509 → T510 → T511 → T512
                               ↓
                    T513 → T514 → T515
                               ↓
                       T601 [P], T602, T603
```

- **T101 blocks T507** (which EF shape) and therefore T506 onward.
- **T307 blocks T506** — FR-013: the net exists before the shape moves.
- **T301–T305 are `[P]`**: five disjoint files across three test projects.
- **T501–T504 are `[P]`**: four new files.
- **T506–T512 are strictly serial** and are one commit: nothing between T506
  and T510 compiles.

## Parallelism note for the orchestrator

Only two fan-outs exist, and both are small. This feature is a single
independently-shippable vertical slice and is not worth splitting across
engineers: T506–T512 are one atomic commit touching four projects, so a second
engineer would have nothing to do but wait. **One `backend-engineer`, start to
finish.**

## What "done" means

- Transposing a coordinate with an extent fails the build (SC-001), shown by
  writing the swapped call and reading the compiler error — **not** by a runtime
  test, which ADR-0144 forbids manufacturing for a compile-time guarantee.
- Six of six sites assert all four numbers; three of six did before (SC-002).
- No asserted literal, exception type, status code or `[InlineData]` row changed
  (SC-003).
- No migration, no column change (SC-004).
- Release build clean (SC-005); `PrimitiveBoundaryTests` green with `reached`
  not falling (SC-006); Domain coverage ≥ 90% (SC-007); `apps/` untouched
  (SC-008).

## For the PR body

1. The **green** output from T306, verbatim — ADR-0144's transported artifact.
2. The declaration that the compile-time guarantee has **no runtime test and no
   architecture guard**, and is asserted by the type system alone (research R3).
3. The `reached` before/after figures from T002 and T514, with the statement
   that the `PrimitiveBoundaryTests` repoint is a factual update — argued in
   plan.md §"The guard assertion".
4. The one declared observable difference: the `(0, 1]` guard message gains a
   colon (research R2).
5. Which R1 branch was taken, and why.
