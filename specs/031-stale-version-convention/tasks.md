# Tasks: One way to say a version is stale

**Input**: Design documents from `/specs/031-stale-version-convention/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/refusal-vocabulary.md)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to
- Exact file paths in every description

## No setup phase, no migration, and no new dependency

Nothing to initialise. No persisted state changes, so no migration. The
architecture test uses the source-scanning shape
`tests/Architecture.Tests/HandlerDeconstructionTests.cs` already establishes.

**This is a correction.** Sixteen declaration sites across six contexts are
already right and are not touched. One is renamed. If a task here turns out to
need one of the sixteen changed, that contradicts the spec's central assumption
— raise it, do not absorb it.

## The decision comes first

Phase 1 is the ADR, before any code. The complaint this feature answers is that
a decision was deferred and became a comment in shared code; writing the
decision first makes this an implementation of it rather than a decision
inferred later from a diff.

---

## Phase 1: Write the decision down

**Goal**: The convention exists as a decision, with the trade it refuses.

- [ ] T001 [US3] `docs/adr/0119-stale-version-vocabulary.md` — a new ADR **amending ADR-0113**, in the same header style ADR-0113 uses to amend ADR-0043. Must state that the **code** is authoritative and the status is not, and why: both statuses are overloaded in both directions, `409` also carrying name collisions and a terminal refusal, `412` also carrying Identity's existence preconditions
- [ ] T002 [US3] In the same ADR, record **the trade refused**: the outlier's `412` is the *more* correct status per RFC 9110 §15.5.13, so standardising on the sixteen would make the newest endpoint less correct. An ADR that records only what was done, without what was rejected and why, gets reversed by the next person who rediscovers the correctness argument
- [ ] T003 [P] [US3] In the same ADR, record the **cost asymmetry** that decided it — 16 declaration sites across six contexts versus 1 — and that this rename is cheapest now because the code has never had a consumer outside this repository

**Checkpoint**: someone can follow the convention without reading any code.

---

## Phase 2: Make the outlier conform

**Goal**: One spelling of a stale refusal, on the wire.

- [ ] T004 [US1] Rename the code to `CAMERA_VERSION_STALE` in `src/CameraCatalog/Application/Commands/ChangeCameraAddressErrors.cs` — the literal, the record (`VersionMismatch` → `VersionStale`) and the failure factory. **The status stays `412`**: it is the more correct reading and is being made irrelevant to the advice, not standardised
- [ ] T005 [US1] Update the factory call in `src/CameraCatalog/Application/Commands/Handlers/ChangeCameraAddressCommandHandler.cs`
- [ ] T006 [P] [US1] Update the type reference in `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressCommandHandlerTests.cs`, and assert the **code string** rather than only the record type — the string is what a client keys on and the type rename would otherwise pass silently
- [ ] T007 [P] [US1] Correct `specs/029-camera-read-edit/contracts/cameras-api.md` — it documents the 412 as carrying `PRECONDITION_FAILED`, **a code that exists nowhere in `src/`** (research §4). Set it to the value the implementation returns, and note the correction rather than silently overwriting it

**Checkpoint**: the wire is consistent; the frontend's provisional branch is now dead code that still passes.

---

## Phase 3: Make the convention hold for the next context

**Goal**: An eighth context cannot miss it quietly.

- [ ] T008 [US3] `tests/Architecture.Tests/StaleCodeConventionTests.cs` — scans `src/**/*.cs` for `ApiError` code literals and fails when one names a version conflict without ending `_STALE`. Reads **source**, not assemblies: `ApiError` takes its code as a constructor argument, so the value exists only on an instance, and `HandlerDeconstructionTests` already reads source for a comparable reason
- [ ] T009 [US3] The failure message must name the offending code **and** the convention it missed, so someone hitting it in CI can fix it without finding this spec
- [ ] T010 [US3] **Prove the test fires.** Temporarily add a plausible wrong code — `WIDGET_VERSION_MISMATCH` — to any errors file, watch the suite go red, then remove it. A check that only looks for the exact string being removed passes forever and catches nothing; the test for the test is that it fails for a code a *future* context would invent

---

## Phase 4: Simplify the client

**Goal**: The shared helper says what its own comment always said.

**Needs #1859 merged.** That PR added the provisional branch this deletes.

- [ ] T011 [US1] Simplify `isStaleConflict` in `apps/shared/src/api/problemDetail.ts` to `problemCode(error)?.endsWith('_STALE')` — **delete** the status test and the separate 412 branch rather than extending them. That is the doctrine the file's own comment already states
- [ ] T012 [US1] Remove the `Provisional, pending #1857` note and the helper it documents (FR-008). A deferred decision that leaves a permanent comment is the failure this feature exists to correct, in miniature
- [ ] T013 [P] [US1] Update `apps/shared/src/api/problemDetail.test.ts` — every one of the seven contexts' codes is recognised as stale, `LAYOUT_NAME_TAKEN` still is not, and Identity's **412** existence preconditions (`WEBHOOK_CLIENT_ALREADY_EXISTS`, `WEBHOOK_CLIENT_NOT_FOUND`) still are not. That last case is the one the old status branch could have swept in
- [ ] T014 [P] [US2] Assert `isTerminalRefusal` still separates `CAMERA_RETIRED` from a lost update, and that the two produce different advice — both are `409`, which is why status alone cannot tell them apart

---

## Phase 5: Prove nothing else moved

**Goal**: FR-006 — the six contexts that were already right are untouched.

- [ ] T015 Run the suites for the six correct contexts — Automation, LayoutComposition, OverlayDesigner, SystemVariables, EventIngestion, Identity — plus `management-web`. **They must pass with no edits to any of their test files.** `git diff` over those paths must be empty; a suite adjusted until it passes proves nothing
- [ ] T016 [P] Confirm no `*_STALE` code or status changed in the sixteen sites: `grep -rn '_STALE"' --include=*.cs src` before and after must be identical
- [ ] T017 Full suite — backend Release build with analyzers, and `pnpm typecheck && pnpm lint && pnpm test`
- [ ] T018 Verification note on the PR following [quickstart.md](./quickstart.md), including the deliberately-broken architecture test from T010 and the empty `git diff` from T015

---

## Dependencies

```
T001 … T003   (the ADR)
      ↓
T004 … T007   (the rename)          ── the wire is consistent here
      ↓
T008 … T010   (the convention, enforced)
      ↓
T011 … T014   (the client)          ── needs #1859 merged
      ↓
T015 … T018
```

**Phases 1–3 need nothing from #1859.** After the rename the frontend's
provisional branch is harmless dead code, because it matches on the code as well
as the status — so the backend half can land on its own.

## Parallel opportunities

- **T003** — with T001/T002 once the ADR file exists.
- **T006 and T007** — a test and a document, different trees.
- **T013 and T014** — different predicates in the same test file; split if convenient.
- **T016** — alongside T015.

## Implementation strategy

**The ADR first**, for the reason at the top of this file.

**The backend half is shippable alone** (T001–T010, T015–T017). If #1859 stalls,
this does not.

**Phase 4 is a deletion.** If it grows into an extension, something has been
misread — the whole point is that the status test goes away.

---

## Three things most likely to go wrong

**The six contexts change and nobody notices.** FR-006 is invisible: everything
here can pass while a layouts operator starts seeing different words. **T015 is
the only guard**, and its real assertion is not that the suites pass but that
their files are unmodified — `git diff` empty. A suite edited until green proves
the opposite of what it looks like.

**The architecture test never fires.** Written against the string being removed,
it is green forever and worth nothing. **T010 exists to break it on purpose**
and watch it fail, because a guard nobody has seen fail is not known to be a
guard.

**The provisional note outlives the provisional code.** T012. Deleting the
branch and leaving the comment saying "pending #1857" would reproduce, exactly,
the failure this feature was filed to correct.
