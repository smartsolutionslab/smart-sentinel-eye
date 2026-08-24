# Tasks: A name is mutable exactly when it is not an address

**Feature**: `033-rename-convention` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: #1850

**26 tasks across five phases.**

**No migration.** The `name_normalized` generated column, the
`ux_cameras_fab_name_normalized_active` partial index and `CameraName`'s
normalisation all already exist ([research.md](./research.md) §2).

**No UI**, and no setup phase.

**Phase 2 is one small interface change and the highest-risk task in the
feature.** That predicate has already been enforced inconsistently once, and the
in-memory double is what the unit tests actually exercise — so a rule that holds
only in the fake looks green all the way to production.

---

## Phase 1: The convention, and its enforcement

**Goal**: The part that outlives this feature.

**Independent of Phases 2–5.** Can go first or last.

- [x] T001 [US-CONV] Write `docs/adr/0120-name-mutability.md` — a name may be changed only where the aggregate is **not addressed by it**. State the rule generally and enumerate today's surfaces as *evidence*, **not** as a closed list: [research.md](./research.md) §5 found the spec's inventory of five was already short — `{integrationName}` (EventIngestion) and `{clientId}` (Identity) are also non-identifier addresses
- [x] T002 [US-CONV] In `docs/adr/0120-name-mutability.md`, record the ruling per aggregate: `Camera`, `Layout`, `Overlay` renameable (identifier-addressed); `Rule`, `Variable` not (name-addressed). Give the reason the line falls where it does — a rename of a name-addressed aggregate is an **identity change**, not an attribute edit
- [x] T003 [P] [US-CONV] In `docs/adr/0120-name-mutability.md`, record why **`Variable` is the sharpest exclusion**: `Automation` references it by name in `RuleAction.SetVariableValue`, persisted via `RuleConfiguration` and read by `RuleEvaluator`, **across a boundary ADR-0016 forbids a project reference across** — so a rename would leave rules that silently stop firing with nothing able to detect it. `Rule`'s exclusion costs a bookmark; `Variable`'s costs working automation. Do not lump them together
- [x] T004 [US-CONV] Create `tests/Architecture.Tests/NameMutabilityConventionTests.cs`, source-scanning like `tests/Architecture.Tests/StaleCodeConventionTests.cs`. Signal: a context whose `src/<Context>/Api/*.cs` binds a route parameter with **no type constraint** (`{name}`, not `{x:guid}` / `{x:int}`) is addressed by that value and must not also expose a rename
- [x] T005 [US-CONV] The failure message in `tests/Architecture.Tests/NameMutabilityConventionTests.cs` must name the **context**, the **offending route parameter** and the **rule**, so someone hitting it in CI can act without finding this spec
- [x] T006 [US-CONV] **Prove the test fires.** Temporarily add a `RenameRuleCommand` to `src/Automation/Application/Commands/` — a context whose endpoints bind `{name}` — watch `tests/Architecture.Tests` go red, then remove it. A check that only recognises today's five aggregates passes forever for a sixth; **the test for the test is that it fails for a context that does not exist yet**

**Checkpoint**: the convention is recorded and enforced, with nothing renamed yet.

---

## Phase 2: Asking the right question

**Goal**: An existence check a rename can actually use.

**Blocks Phase 3.**

`ExistsByNameAsync(fab, name)` asks *does any active camera in this fab hold this
name*. The camera being renamed **is one** — active, in that fab, holding that
name whenever the rename is a no-op or case-only. It finds itself
([research.md](./research.md) §1).

- [x] T007 Extend `ICameraRepository` in `src/CameraCatalog/Domain/Camera/ICameraRepository.cs` so the existence question can **exclude one camera** — *does any camera **other than this one** hold this name in this fab*. Document on the method why registration's question is not the rename's
- [x] T008 Implement it in `src/CameraCatalog/Infrastructure/Persistence/CameraRepository.cs` **and** mirror it in `tests/CameraCatalog.Application.Tests/Fakes/InMemoryCameraRepository.cs` — **in this one task, not two adjacent ones**. These two diverged before, on this exact predicate: spec 028 found the repository missing the status filter the index had, and every unit test stayed green because the fake was the thing under test. Keep the status filter (`!= Decommissioned`) and the normalised-column comparison in both
- [x] T009 [P] Tests in `tests/CameraCatalog.Application.Tests/` for the new question directly: a camera **does not** count as holding its own name; another active camera in the same fab **does**; a retired camera **does not**; a camera in another fab **does not**

**Checkpoint**: the question is askable and provably excludes the subject.

---

## Phase 3: The rename, and the two conflicts

**Goal**: An operator can correct a misnamed camera, and every refusal says which problem it is.

**Needs Phase 2.**

- [x] T010 [US1] Add `Rename(CameraName, OperatorIdentifier, IClock)` to `src/CameraCatalog/Domain/Camera/Camera.cs` — refuses when retired (**FR-009**), raises no event when the name is unchanged (**FR-010**), and advances the version otherwise. **The terminal guard lives here and only here**: two copies of a rule is how spec 028's defect happened
- [x] T011 [US1] Create `src/CameraCatalog/Application/Commands/RenameCameraCommand.cs` and `RenameCameraErrors.cs` — `CameraNotFound` (404), `CameraRetired` (409), `CameraNameTaken` (409), `VersionStale` (412). **`CAMERA_NAME_TAKEN` must not end `_STALE`** (ADR-0119): it is not a lost update and re-reading does not help
- [x] T012 [US1] Create `src/CameraCatalog/Application/Commands/Handlers/RenameCameraCommandHandler.cs`, calling the Phase 2 question with the camera excluded, then delegating the terminal refusal to the aggregate rather than re-checking it
- [x] T013 [US1] Extend the `PATCH /cameras/{camera}` endpoint in `src/CameraCatalog/Api/CameraEndpoints.cs` to accept a name, requiring `If-Match`. **A rename is version-checked, unlike retire** — it changes an attribute other writers may be looking at
- [x] T014 [US1] **The self-collision pair — both tests, in one task, in `tests/CameraCatalog.Application.Tests/Commands/RenameCameraCommandHandlerTests.cs`.** (e) renaming a camera to the name it already has **succeeds and raises no event**; (f) renaming `Line-4-Inlet` to `line-4-inlet` — case only, same camera — **also succeeds**. They are one task deliberately: a handler short-circuit on *new name equals current name* makes (e) pass and leaves (f) failing, so a task containing only (e) **actively rewards the wrong fix**. (f) is a real change to what is displayed that normalises to the same value
- [x] T015 [P] [US2] Collision tests in `tests/CameraCatalog.Application.Tests/Commands/RenameCameraCommandHandlerTests.cs`: renaming onto another **active** camera's name in the same fab is refused, **and** refused when the names differ only in case. Asserting only the exact match passes against a case-sensitive comparison — which is precisely defect #1434
- [x] T016 [P] [US2] The refusal-must-not-fire tests in `tests/CameraCatalog.Application.Tests/Commands/RenameCameraCommandHandlerTests.cs`: a camera in **another fab** holding the name does **not** block the rename, and a **retired** camera holding it does not either. Both are absences a happy-path suite never covers, and both are behaviour spec 015 and spec 028 already decided
- [x] T017 [US2] **The two conflicts, asserted apart**, in `tests/CameraCatalog.Application.Tests/Commands/RenameCameraCommandHandlerTests.cs`: `CAMERA_NAME_TAKEN` and `CAMERA_VERSION_STALE` differ in **code**; `CAMERA_NAME_TAKEN` does **not** end `_STALE`; and the two do **not share a status**. Spec 031's architecture test catches a wrong suffix but says nothing about status, so the status distinction needs its own line. A caller that cannot tell them apart re-reads and retries forever against a name that belongs to someone else
- [x] T018 [P] [US1] Assert in `tests/CameraCatalog.Application.Tests/Commands/RenameCameraCommandHandlerTests.cs` that a retired camera's rename is refused **by the aggregate** — the handler translates, it does not re-implement (FR-009)

**Checkpoint**: US1 and US2 are shippable. The rename works and every refusal is actionable.

---

## Phase 4: Announcing it

**Goal**: The rename reaches the audit trail, and history stays as it was.

- [x] T019 [US1] Create `src/Shared.Contracts/CameraCatalog/CameraRenamedV1.cs`, mirroring `CameraAddressChangedV1`. Carry **`PreviousName` as well as `Name`** — an audit entry reading *"renamed to line-4-inlet"* does not say what was corrected
- [x] T020 [US1] Add the domain event in `src/CameraCatalog/Domain/Camera/Events/` and its handler in `src/CameraCatalog/Application/EventHandlers/`, publishing through the outbox exactly as `CameraAddressChangedDomainEventHandler` does
- [x] T021 [US1] Add one `Handle(CameraRenamedV1 …)` line to `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs`. **A different bounded context**, reached via `Shared.Contracts` as the other sixteen events are — the sanctioned route, not a new project reference. Omitting this line means the event is silently never audited and **nothing fails**
- [x] T022 [P] [US3] Assert **FR-013** in `tests/CameraCatalog.Application.Tests/`: a rename does not revisit past events. `CameraRegisteredV1` carries the name as it was at registration and stays that way — the audit trail records what was true when, not what is true now

---

## Phase 5: Proving it end to end, and the findings

- [x] T023 [US1] Create `tests/Integration.Tests/CameraCatalog/RenameCameraIntegrationTests.cs` against the real stack: rename succeeds and the **identifier is unchanged** (SC-001); exact and case-only collisions are refused; another fab's name does not block; a retired camera's rename is refused; **a new camera registers under the freed old name** (FR-011 — chosen behaviour, tested as such, because spec 028's research read this same index and was wrong about the layer above it). Also assert the **ordering** from [contracts/rename-camera.md](./contracts/rename-camera.md): a camera in another fab answers **404**, never 428 or 409, because a precondition failure would confirm it exists
- [x] T024 Update spec 029's FR-012 in `specs/029-camera-read-edit/spec.md` to point here rather than reading as a permanent exclusion (**FR-015**), noting the correction rather than silently overwriting it
- [x] T025 **File the two findings from [research.md](./research.md) as GitHub issues** — (a) nothing translates a unique-index violation into a usable response, so a rename losing the check-to-commit race yields a 500 (§3); (b) publishing an integration event is never a one-context change, because every new event needs a line in `AuditObservability`'s per-event overload list and omitting it silently drops the audit (§4). **Raise, do not fix here.** Filed as issues 1869 and 1870. When citing those numbers in a task, a commit or the PR body, write them **without the `#`** — this repo's automation closes a merely-mentioned issue on merge, with no closing keyword needed. It did exactly that to issue 1866 two seconds after PR 1867 merged, marking a deliberately-unfixed finding COMPLETED. After merging, re-check both with `gh issue view <n> --json state -q .state`
- [x] T026 Full suite — Release build with analyzers, all unit projects, then `tests/Integration.Tests` against Docker. Verification note on the PR following [quickstart.md](./quickstart.md), including the deliberately-broken architecture test from T006 and the self-collision pair from T014

---

## Dependencies

```
T001 ─▶ T002, T003        (the ADR)
T004 ─▶ T005 ─▶ T006      (the check — independent of everything below)

T007 ─▶ T008 ─▶ T009      (the question)
          │
          ▼
T010 ─▶ T011 ─▶ T012 ─▶ T013
          │
          ├─▶ T014, T018        (self-collision, terminal)
          └─▶ T015, T016, T017  (collisions, scoping, the two conflicts)
                    │
                    ▼
        T019 ─▶ T020 ─▶ T021, T022
                    │
                    ▼
              T023 ─▶ T026
        T024, T025 (independent — any time)
```

**Phase 1 is independent of Phases 2–5.** Phase 2 blocks Phase 3.

---

## Parallel opportunities

- **The whole of Phase 1 (T001–T006)** alongside Phases 2–4. Different files, no shared state.
- **T003** with T002 — same ADR, but distinct sections.
- **T009** with the start of Phase 3 — the question is done once T008 lands.
- **T015, T016, T017, T018** — same test file, so one author, but independent of T019–T022 entirely.
- **T022** with T021 — different files.
- **T024, T025** — a spec edit and two GitHub issues, dependent on nothing.

---

## Implementation strategy

**MVP is Phase 3's checkpoint.** Once T018 lands, a camera can be renamed and
every refusal is actionable — US1 and US2 are both real. Phase 4 makes it
auditable (required by FR-012, so not optional, but not what makes the feature
work) and Phase 5 proves it.

**Do Phase 2 first and do not rush it.** It is three tasks and the highest risk
in the feature. Everything in Phase 3 rests on the question being asked
correctly, and the failure mode is a green unit suite over a broken production
predicate.

**Do Phase 1 whenever.** It is genuinely independent, and it is the part that
still matters in a year. If the feature were cancelled tomorrow, T001–T006 would
still be worth having.

---

## Three things most likely to go wrong

1. **The camera collides with itself.** `ExistsByNameAsync` finds the camera
   being renamed and refuses the rename as `CAMERA_NAME_TAKEN` against its own
   name. Confirmed reachable — this is not hypothetical. The tempting fix, a
   short-circuit when the new name equals the current one, **passes T014(e) and
   fails T014(f)**, because a case-only rename is a real change that normalises
   to the same value. That is why both live in one task.

2. **The name collision is reported as a lost update.** Both are conflicts and
   the nearest existing failure is `CAMERA_VERSION_STALE`. Sharing a suffix is
   caught by spec 031's architecture test; **sharing a status is not**, and a
   caller keying on status would then re-read and retry forever against a name
   that belongs to somebody else. T017 asserts the status distinction directly.

3. **The repository and its in-memory double drift.** They already did once, on
   this predicate, and every unit test stayed green throughout because the fake
   was the thing under test. T008 changes both or the feature ships a rule that
   holds only in tests.
