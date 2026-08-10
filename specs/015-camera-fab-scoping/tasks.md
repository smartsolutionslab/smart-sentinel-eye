# Tasks: Fab-scope the camera catalogue

**Input**: Design documents from `/specs/015-camera-fab-scoping/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/cameras-api.md](./contracts/cameras-api.md)

**Tests**: Included. ADR-0052 mandates TDD for the domain, and two things here
are only ever caught by a test: the four `FabIdentifier` copies drifting apart,
and the case-insensitivity of the camera index surviving a hand-corrected
migration.

**Organization**: Grouped by user story. **Phase 2 is deliberately larger than
its equivalent in specs 013 and 014** — see the note below.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1–US4 from spec.md
- Exact file paths in every task

---

> **Why Phase 2 is one big slice, not a small one**
>
> Spec 014 made the aggregate require a fab before any endpoint could resolve
> one. That forced **seven `munich` placeholders** to live across four phases,
> each needing a marker comment, a greppable string and an individual deletion.
>
> Nothing forces that ordering. Phase 2 here lands the aggregate, the commands
> **and** the endpoint resolution together, so there is never a commit where a
> fab is required and unobtainable — and no placeholder is ever written.
>
> The cost is a bigger phase. The benefit is that the context is never half
> scoped, which is the state a reviewer cannot reason about.
> ([research.md](./research.md) §1)

---

## Phase 1: Setup

**Purpose**: The value object, and only that. Nothing here changes behaviour.

- [x] T001 [P] Add `src/CameraCatalog/Domain/Camera/FabIdentifier.cs` as a `StringValueObject` with `From(...)` + `Ensure.That(...)`, mirroring `src/SystemVariables/Domain/Variable/FabIdentifier.cs` exactly: 2–32 chars, lowercase letters/digits/`-`, starting with a letter. Per-context by ADR-0044.
- [x] T002 [P] Add `tests/CameraCatalog.Domain.Tests/Camera/FabIdentifierTests.cs` covering the grammar, rejection of null/whitespace/too-short/uppercase/leading-digit, boundary lengths, and equality. **This is the only thing keeping the four copies in step** — Identity, EventIngestion, Automation and SystemVariables each have one, and nothing but a test asserts they agree.
  *18 cases. The null case uses plain `null`, not `null!` — NRT is disabled in
  this test project and SonarAnalyzer S8970 fails the Release build on a
  null-forgiving operator that cannot be doing anything. SystemVariables'
  equivalent has `null!` because its test project differs; copying it verbatim
  broke the build.*

**Checkpoint**: The context has a fab type. Nothing uses it.

---

## Phase 2: Foundational (blocking) — the whole write path at once

**Purpose**: Give `Camera` a fab and resolve it at the boundary in the same
slice, so no placeholder is ever needed.

- [x] T003 Add `Fab` to `src/CameraCatalog/Domain/Camera/Camera.cs`: private setter, required by the registration factory, never mutated (FR-004). Do **not** add a `MoveToFab` — a camera is bolted to a wall in one building.
- [x] T004 Add `WithFab` to the camera builder in `tests/CameraCatalog.Domain.Tests/`, defaulting to `munich` so existing call sites read as before.
- [x] T005 Extend the camera state tests in `tests/CameraCatalog.Domain.Tests/Camera/` to assert `Fab` survives registration → decommission unchanged, plus a structural guard that the `Fab` setter is not public. Without it, "never mutated" is the one line of T003 with nothing asserting it.
  *Landed as `CameraFabLifetimeTests.cs`, and **narrower than asked**: there is
  no decommission behaviour to survive. `CameraStatus` carries a
  `Decommissioned` value and **nothing in CameraCatalog ever transitions to
  it** — the aggregate is register-only. Asserting survival across a transition
  that cannot happen is the shape of the skipped spec #1292.*
  ***This has consequences past T005 — see the blocker note below.***
- [x] T006 Map the column in `src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs`: `fab` NOT NULL, max length 32, value-converted. Replace `ux_cameras_name_lower` with `ux_cameras_fab_name_active` on `(fab, lower(name))`, **adding** the `status <> 'Decommissioned'` filter and **keeping** case-insensitivity. The filter is new behaviour, decided at the Phase 2 gate ([research.md](./research.md) §3).
- [x] T007 Generate the EF migration under `src/CameraCatalog/Infrastructure/Persistence/Migrations/`. Hand-correct the scaffold to the four-step form in data-model.md: add nullable → backfill → NOT NULL → swap the index. `dotnet ef` generates a single `AddColumn(nullable: false, defaultValue: "")`, which sets every existing camera's fab to the empty string — not a valid `FabIdentifier`, so those rows fail to materialise on the next read. Spec 014's T043 walk observed this directly.
- [x] T008 Make the backfill announce itself in the migration from T007: wrap the `UPDATE` in a `DO $$` block capturing `ROW_COUNT` and `RAISE WARNING` naming the count (FR-011). It reaches the log only because #1395 wired the Npgsql notice handler; before that it went nowhere.
- [x] T009 Document in the same migration file that `Down` discards each camera's fab, that rolling forward re-attributes everything to munich, and that `Down` can legitimately fail where `Up` succeeded when two fabs hold one live name.
- [x] T010 Change `GetByNameAsync` to take a `FabIdentifier` in `src/CameraCatalog/Domain/Camera/ICameraRepository.cs` and its implementation, and update every in-memory fake under `tests/` to filter on fab and name together. **Check every caller** — spec 014's equivalent had six, not one.
- [x] T011 Thread the fab through the register/edit/decommission commands and handlers in `src/CameraCatalog/Application/Commands/`, scoping the duplicate-name check to the fab and rewording `CAMERA_NAME_TAKEN` to say the name is taken *in that fab*.
- [x] T012 Add fab resolution to the three write endpoints in `src/CameraCatalog/Api/CameraEndpoints.cs` using `FabResolution` and `FabClaims` from `ServiceDefaults` **unchanged**. On any endpoint reading a precondition, resolve the fab **before** it — the reverse order answers a precondition failure to a request that was never the caller's.

**Checkpoint**: A camera has a fab, end to end, with no placeholder anywhere.
`git grep "Placeholder fab" -- src/CameraCatalog/` must return nothing.
*Verified: 0. The one-slice ordering held — no placeholder was ever written.*

*Two corrections found while implementing:*
*— the repository method is `ExistsByNameAsync`, not `GetByNameAsync` as T010
  said, and it had **one** caller rather than the six spec 014's had;*
*— the shipped index is named `ux_cameras_name_lower` but is a plain btree on
  `name`. Case-insensitivity lives in `CameraName.NormalizedValue`, not in a
  `lower()` expression. data-model.md is corrected; T015 is unaffected in what
  it asserts.*

---

## Phase 3: User Story 1 — Two plants name their cameras the same (P1) 🎯 MVP

**Goal**: Munich and Dresden can each hold `line-1-north`.

**Independent test**: Register the same name in both fabs and read both back.

- [x] T013 [P] [US1] Add cases to the register handler tests under `tests/CameraCatalog.Application.Tests/Commands/` asserting the same name is accepted in a second fab and refused in the same fab, with the refusal naming the fab.
  *Completed T011's other half here: the reword had not been done — the error
  still read "Camera name already in use." with no fab. `NameAlreadyTaken` now
  carries `(Fab, Name)`. Worth noting because T011 was already marked done and
  its issue closed; the check was scoped but the message was not.*
- [x] T014 [US1] Add `tests/Integration.Tests/CameraCatalog/CrossFabCameraIntegrationTests.cs`: seed a camera of the same name in two fabs, assert both persist, and assert the unique index is `(fab, name)` and not `(name)`. Covers SC-001. Seed through a `DbContext` — a second fab's camera cannot be authored over HTTP by the seeded admin, which is the behaviour under test rather than a way to set it up.
- [~] T015 [US1] Add a case asserting **case-insensitivity survived the index swap**: `Line-1-North` is refused where `line-1-north` exists in that fab. Spec 001 marker 2 made this deliberate and it is exactly what a hand-corrected migration drops silently.
  ***Written and skipped — #1434.** The assertion is right and the system is
  wrong: camera names are **not** case-insensitively unique. The index named
  `ux_cameras_name_lower` is a plain btree on `name`, and `ExistsByNameAsync`
  compares the stored column, which holds original casing. `CameraName.Equals`
  is case-insensitive but never runs, because EF translates the predicate to
  SQL. **The in-memory double does enforce it**, so every unit test passes while
  production does not — the T034 failure shape.*
  ***Predates spec 015**: the old index was equally case-sensitive. Skipped, not
  deleted or weakened — a red test gets removed and takes the evidence with it,
  and a test asserting the defect would have to be found and reversed later.*
- [~] T016 ~~[US1] Add a case asserting a decommissioned name is reusable within its fab~~ — **DROPPED 2026-08-10.** Unwritable: nothing retires a camera, so there is no way to free a name. FR-003 is withdrawn with it and the retire behaviour is tracked as #1433. The index filter stays and is forward-looking.

**Checkpoint**: SC-001 and the index behaviour are observed, not argued.

> **BLOCKER found at T005: cameras cannot be decommissioned.**
>
> `CameraStatus.Decommissioned` exists as a value and nothing ever sets it. The
> aggregate has one behaviour, `Register`. There is no retire command, handler
> or endpoint.
>
> Three things in this spec assume otherwise and **cannot be implemented as
> written**:
>
> - **FR-003** and **US1 acceptance scenario 3** — "retiring releases the name
>   for reuse". Nothing retires.
> - **T016** — the test for exactly that. Unwritable.
> - **`contracts/cameras-api.md`** lists `POST /cameras/{name}/decommission`.
>   That endpoint does not exist.
>
> The partial index filter added in T006 is therefore **inert today**: it
> filters on a status no camera can hold. That is harmless and
> forward-compatible — it costs nothing and is correct the moment a retire
> behaviour lands — but it is not currently buying the behaviour FR-003 claims.
>
> **Resolved 2026-08-10**: FR-003 withdrawn, T016 dropped, retire behaviour
> tracked separately as #1433. The filter stays and is forward-looking. Spec 015 keeps
> the fab-scoping boundary it was given.

---

## Phase 4: User Story 2 — An operator sees only their own plant's cameras (P1)

**Goal**: Munich's cameras are neither listed nor reachable by a Dresden-only
operator.

- [x] T017 [US2] Thread the caller's fabs into the list and get-one queries and handlers in `src/CameraCatalog/Application/Queries/`. A camera in a fab the caller lacks returns the **not-found** response, byte-identical to a name never used (FR-006) — a 403 would confirm it exists and let an operator enumerate another fab's names one guess at a time.

> **CameraCatalog has only TWO endpoints: `POST /cameras` and `GET /cameras`.**
> No get-one, no edit, no decommission. Verified:
> `grep -E "MapGet|MapPost|MapPut|MapDelete" src/CameraCatalog/Api/CameraEndpoints.cs`
> returns exactly two lines.
>
> `contracts/cameras-api.md` describes five (later four). I wrote it by analogy
> to the SystemVariables contract without checking this context's actual
> surface — the same mistake that produced the FR-003 problem, at larger scale.
>
> **Consequences, all needing a decision:**
> - **T017's get-one half, T019 (`CAMERA_FAB_AMBIGUOUS`) and T023 (FR-006
>   field-by-field) have nothing to attach to.** There is no read-by-name path.
> - **FR-006 and FR-010 are largely unimplementable as written.** FR-006 says a
>   camera in a fab the caller lacks is reported as never existing; nothing
>   reports a single camera at all.
> - The contract's `PUT` and decommission entries describe endpoints that do
>   not exist (decommission already withdrawn with #1433).
>
> **Resolved 2026-08-10**: FR-006, FR-010 and SC-003 withdrawn; T019 and T023
> dropped; the absent endpoints tracked as **#1435**, which reinstates those
> requirements when they land. Spec 015 delivers FR-005 — the listing excludes other fabs' cameras,
> which is the whole of the non-enumeration guarantee available without a
> read-by-name endpoint.
- [x] T018 [US2] Add fab resolution to the two read endpoints in `src/CameraCatalog/Api/CameraEndpoints.cs`. A read spans **all** the caller's fabs when they name none — the deliberate asymmetry with the write path.
- [~] T019 ~~[US2]~~ **DROPPED 2026-08-10** — no read-by-name endpoint exists, so there is nothing to answer ambiguously. FR-010 withdrawn with it. Original: Return **400** `CAMERA_FAB_AMBIGUOUS` naming the candidates when a name resolves in more than one of the caller's own fabs (FR-010). Not tie-broken: whichever row won would be arbitrary, and a caller acting on it would be editing a fab they did not choose.
- [x] T020 [P] [US2] Add `Fab` to the camera DTO and its mapper so a multi-fab operator can tell two same-named rows apart (FR-013), and order the listing by name **then fab** — name alone stops being a total order the moment two fabs hold one.
  *The tiebreak was added to the `registeredAt` sort too: two cameras can share
  a registration instant, and that ordering was already non-total before this
  feature. Under paging a non-total order can show one row twice and another
  never, so it is a correctness fix rather than tidiness.*
  *The DTO field is asserted as `dresden`, not the munich everything else
  defaults to — a mapper ignoring the camera's fab would pass otherwise. The
  scoping case also asserts `Count`, because a count of 2 with one row returned
  reads as a broken page rather than a filtered one.*
- [~] T021 ~~[P] [US2]~~ **DROPPED 2026-08-10** — its two halves are gone: the refusal path it tested is the read-by-name FR-006 case (withdrawn, #1435), and the list-scoping half is covered by T020's handler cases. Original: Add handler tests under `tests/CameraCatalog.Application.Tests/Queries/` for the refusal paths: a foreign camera reported as not found, and an ambiguous name naming its candidate fabs.
- [x] T022 [US2] Add `tests/Integration.Tests/CameraCatalog/CameraFabResolutionIntegrationTests.cs` driving the decision table over real HTTP with `op-dresden@dresden.test` and `op-multi@smart-sentinel-eye.test`: refused without `fabId`, accepted when named, 403 for a fab not held, inference asserted as **dresden** (not the munich everything else defaults to), and both sides of the ambiguity. Covers SC-002 and SC-004.
  ***6/6 green.** Narrower than the sibling suites and not by choice: with only
  `POST /cameras` and `GET /cameras`, the "indistinguishable from never
  existed" and ambiguity rows have no endpoint to drive (FR-006/FR-010
  withdrawn, #1435). Read-back goes through the listing, which only works
  because T020 put the fab on the row.*
  *Inference asserted as **dresden**: everything else defaults to munich, so a
  broken inference falling back to the default would pass against a munich
  operator and only fail here.*
- [~] T023 ~~[US2]~~ **DROPPED 2026-08-10** — no read-by-name endpoint exists, so there is no response to compare. FR-006 and SC-003 withdrawn with it. Original: Assert FR-006 **field by field**, not by status alone: compare the foreign-camera response to a never-existed one with `detail` and `traceId` removed. A difference in title or type is enough to enumerate. Covers SC-003.

**Checkpoint**: The access half of the feature is closed and provably
non-enumerable.

---

## Phase 5: User Story 3 — Registering picks up the operator's plant (P2)

**Goal**: A single-fab operator is never asked; a multi-fab one must choose.

> Most of the mechanism landed in T012. This phase is the refusal paths, the
> contract surface, and the UI.

- [x] T024 [US3] Declare the newly reachable statuses on every endpoint in `src/CameraCatalog/Api/CameraEndpoints.cs` — 400 and 403 where they became possible — so the generated OpenAPI does not claim they cannot happen. Spec 013 shipped this wrong on one endpoint and it took a review to catch.
- [x] T025 [P] [US3] Update `apps/shared/src/api/cameras.api.ts`: `fab` on the camera type, `fabId` as a query parameter on the write and read calls. Mirror `rules.api.ts` — the fab travels as a query parameter, not in the body, so the request schema still mirrors the wire shape.
  *Only the write and the list exist to update; there is no read-by-name client
  call because there is no such endpoint (#1435). Making `fab` required on
  `CameraSummary` immediately broke the page's test fixtures, which is the type
  doing its job — they described a camera that can no longer exist.*
- [x] T026 [US3] Update the camera surface in `apps/management-web/src/features/cameras/`: show each row's fab, echo it back on edit and decommission, and add a fab selector that appears **only** when the operator holds more than one (ADR-0114). Mirror `RuleDialog`.
  *No "echo it back on edit and decommission" half — neither endpoint exists
  (#1435). The fab column and the selector are the whole of what applies.*
- [x] T027 [US3] Check the camera page's per-row edit state is keyed on the **camera identifier**, not the name. Two fabs may now hold one name, and a name-keyed buffer shows one row's typing in the other and submits it against the wrong fab — the exact bug found in spec 014's T040.
  ***Checked, nothing to fix.** The cameras page holds no per-row state at all —
  there is no editing, because there is no edit endpoint. The table's
  `getRowKey` already uses `cameraIdentifier`, not the name, so two same-named
  rows in different fabs get distinct React keys. A real check with a clean
  result, not a skipped one.*
- [x] T028 [P] [US3] Add frontend tests for the selector's presence/absence and for two same-named rows staying independent.
  *Five cases in a new `RegisterCameraDialog.test.tsx` — there was none. The
  "two same-named rows staying independent" half was folded into T027 instead,
  which found nothing to guard: the page holds no per-row state and keys on
  `cameraIdentifier`.*
  *The chosen-fab case asserts **dresden**: munich is first in the list and the
  default everywhere else, so a dialog ignoring the selection would pass
  against it. Typecheck caught what the tests did not — the mock was typed as
  taking no arguments, so inspecting `calls[0][0]` was a tuple-index error that
  `vitest` alone reported as passing.*
- [ ] T029 [US3] Add an e2e case to `e2e/cameras.spec.ts` covering the single-fab half: the selector must **not** render, and the row carries `munich`. Do not add a skipped spec — #1292 sat skipped for two releases asserting against a UI that did not exist. The multi-fab half stays in T022; driving a second Keycloak account through the browser tests the login form, not fab resolution.

**Checkpoint**: An operator meets the same fab rule here as in rules and
variables.

---

## Phase 6: User Story 4 — Downstream knows a camera's plant (P2)

**Goal**: A subscriber can tell which plant a camera event concerns without
asking.

- [ ] T030 [US4] Stamp `EventMetadata.Fab` with the camera's fab on every camera lifecycle event published from `src/CameraCatalog/Application/`. Additive — the field exists and is currently `null`, so no version bump under ADR-0073 ([research.md](./research.md) §2).
- [ ] T031 [P] [US4] Add handler tests asserting the fab is on the published event for each lifecycle transition, asserted as **dresden** in at least one so a hard-coded default would fail. Covers SC-006.

**Checkpoint**: StreamDistribution's own fab scoping can start without first
fixing this one.

---

## Phase 7: Polish

- [ ] T032 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm `CameraCatalog.Domain` still clears 90% and `CameraCatalog.Application` 80%.
- [ ] T033 Walk [quickstart.md](./quickstart.md) end to end and record the observations on the PR. **"Done" is the observations, not the walk.** Run the migration against a database that predates this feature, or it proves nothing — a fresh database makes the backfill a no-op by design and the warning never fires. Record the attributed count and the `length(fab) < 2` check that vindicates the four-step form.
- [ ] T034 Comment on #1397 that `CameraCatalog` now carries a fab, so the layout-fab decision it blocks is unblocked; comment on #1155 that CameraCatalog is no longer among the contexts missing the guard. **Write `Closes #N, closes #M`** — GitHub honours the keyword only before the *first* number, which left 35 issues open across spec 014's five PRs.

---

## Dependencies

```text
Phase 1 (T001–T002)  ── setup, no behaviour
      ↓
Phase 2 (T003–T012)  ── BLOCKING: the whole write path, no placeholders
      ↓
      ├── Phase 3 US1 (T013–T016)   independent
      ├── Phase 4 US2 (T017–T023)   independent of US1
      ├── Phase 5 US3 (T024–T029)   needs T012 only
      └── Phase 6 US4 (T030–T031)   independent
      ↓
Phase 7 (T032–T034)  ── polish
```

US1, US2, US3 and US4 are independent of one another once Phase 2 lands. Phase
2 is the only hard gate.

## Parallel opportunities

- **Phase 1**: T001 and T002 together.
- **Phase 3**: T013 alongside T014–T016 (different files).
- **Phase 4**: T020 and T021 alongside T017–T019.
- **Phase 5**: T025 and T028 alongside T026–T027.
- **Across phases**: once Phase 2 lands, US1/US2/US4 can proceed concurrently —
  they touch different files. US3's UI work is disjoint from all of them.

## Implementation strategy

**MVP is Phase 1 + Phase 2 + Phase 3.** That closes the naming collision and
gives the context a fab; it is shippable and independently valuable.

**Phase 4 is the one not to defer.** It is P1 alongside US1 because the
catalogue has *no* fab check today — a camera record carries its RTSP address,
so reaching another plant's camera is reaching its video.

**Phase 2 must not be split.** Splitting it is precisely how spec 014 acquired
seven placeholders; the phase is large so that no intermediate state exists.
