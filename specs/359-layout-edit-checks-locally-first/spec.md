# Spec 359: A layout draft edit runs its local refusals before asking CameraCatalog

**Feature Branch**: `fix/2937-layout-editdraft-call-order` (cut from `origin/develop` at `0b6940a3`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[JUDGEMENT]** (§6).

**Input**: Issue [#2937](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2937),
*LayoutComposition EditDraft makes a remote CameraCatalog call before the local stale/version
checks.* Split out of tracker #2916; found in the ADR-0084 refactors (#2887-#2895) and pinned as-is
by `DraftRefusalOrderCharacterisationTests` (spec 345, plan C2). The only issue comment is the lane
pick-up note.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (which refusal a request gets, and whether a
remote call is made): Phase 4a is **RED** (constitution §Testing; ADR-0139).

**Spec number.** 359. Checked 2026-10-14 after `git fetch origin --prune`: `origin/develop`
(`0b6940a3`) and every remote head via `git ls-tree <ref> specs/` — highest 357; 358 is claimed by the
unpushed local branch for #2935 (`specs/358-system-variables-four-defects` in worktree `sse-2935`).
Re-check before the PR is opened.

**ADRs.** ADR-0113 (two-layer optimistic concurrency: compare the expected version "after load and
before mutating" — it fixes the check's position relative to the mutation only, not relative to other
refusals), ADR-0047 / ADR-0089 (`Result` + `ApiError` for refusals; an unreachable dependency is an
exception, i.e. 5xx), ADR-0116 + spec 017 plan §III (the CameraCatalog call is the context's one
bounded synchronous cross-context exception — the reason to make it as rarely as possible),
ADR-0037 / ADR-0144 (workflow, lane), ADR-0139 (red first), ADR-0036 (smallest change). **No new ADR
is needed.**

**Latency budget.** N/A — layout authoring endpoint, not on the event-to-overlay path
(constitution §IV). The change can only remove a remote round trip, never add one.

---

## 1. Premise verified (Phase 1 investigation)

The sibling item #2936 was closed because its premise was false; this one was checked against the
same questions.

| Question | Finding (at `0b6940a3`) |
|---|---|
| What is the order today? | `EditDraftRevisionCommandHandler.HandleAsync`: grid validation (in-memory) -> `layouts.GetByIdentifierAsync` (404) -> **`RefuseCrossFabTilesAsync` (remote)** -> `RefuseRevision`: version mismatch (409 `LAYOUT_REVISION_STALE`), revision missing (404 `LAYOUT_REVISION_NOT_FOUND`), not a draft (409 `LAYOUT_REVISION_NOT_DRAFT`) -> mutate + save. |
| Is the cross-fab check remote? | **Yes.** `ICameraFabGuard` is bound to `CameraCatalogFabGuard` (`LayoutCompositionInfrastructureModule.cs:111`), which pages `GET /cameras?fabId=…` over HTTP with the caller's forwarded token and calls `EnsureSuccessStatusCode()`. Grid validation guarantees at least one tile, so every edit that gets past the lookup makes at least one HTTP request. |
| Which cheap checks exist, and do they need the remote result? | Three, all in `RefuseRevision`, reading only `layout.Version`, `layout.Revisions`, and the revision's `State` — all on the already-loaded aggregate. **None needs the remote answer.** The remote call needs only `layout.Fab` and the tiles. |
| Is the remote-first order documented as intended? | **No.** Introduced by `181cf247` (spec 017 FR-014); the commit and spec 017 justify *enforcing* the check on edit, never its position. Spec 017's spec/plan say nothing about refusal precedence. ADR-0113 says only "after load and before mutating". Spec 345 pinned the order explicitly *as found* ("Order kept"; plan C2), and #2916 lists it as a defect. |
| Would reordering open an oracle (spec 017 FR-006)? | **No.** The layout lookup is already fab-scoped to the caller, so stale / missing-revision / not-a-draft describe a layout the caller may see. The cross-fab refusal's content (names the cameras, never their fab or existence) is unchanged; it is simply reached later. |
| What does the endpoint answer today for stale + bad camera? | `400 LAYOUT_TILE_CAMERA_OUTSIDE_FAB` (pinned by `Edit_with_a_camera_outside_the_fab_and_a_stale_version_returns_TileCameraOutsideFab`). The client re-reads, retries with the fresh version, and would get the 400 anyway — so the stale-first answer loses the caller nothing. |
| What does a stale edit answer while CameraCatalog is down? | The guard throws (`HttpRequestException` from `EnsureSuccessStatusCode` / transport), which escapes the handler -> **5xx**, although the request is refusable as `409 LAYOUT_REVISION_STALE` without CameraCatalog at all. This is the user-visible half of the defect. |
| Does anything depend on the order? | Only the characterisation pin above. No integration test edits a draft with stale + cross-fab together (`LayoutFabScopingIntegrationTests` exercises publish/create/list); `StaleVersionRejectionTests.EditDraft_*` and `EditDraftRevisionCommandHandlerTests` use `FakeCameraFabGuard.Permissive()`; `management-web` reacts to each code independently. No `Shared.Contracts` change. |

**Verdict: proceed.** The premise holds, and it points the opposite way from #2936: there validation
was the *cheaper* check and already ran first; here the expensive remote check runs before cheaper
local ones that do not depend on it.

## 2. Decision: the new order

`grid -> layout not found -> stale -> revision missing -> not a draft -> cross-fab (remote) -> mutate`

Every refusal decidable from the request body or the loaded aggregate is made before the one that
needs another context. The relative order of the local refusals is unchanged.

## 3. User stories

### US1 (P1) — A stale or impossible edit is refused without a call to CameraCatalog

**Why P1**: it is the whole issue. A stale edit costs a remote round trip (paged, with the caller's
token) for an answer that never depended on it, and turns into a 5xx while CameraCatalog is down
even though the correct answer, 409, needs nothing from it.

**Independent test**: the handler unit tests in §4, driven with a counting / unreachable
`FakeCameraFabGuard`: a stale, missing-revision or non-draft edit is refused with the local code and
the guard is observed never to have been called.

## 4. Acceptance scenarios

```gherkin
Scenario: AS-1 conflict — stale edit naming a camera outside the fab answers stale, CameraCatalog untouched
  Given a draft layout in fab "munich" at version V
  And a tile naming a camera CameraCatalog does not place in "munich"
  When the draft is edited with expected version 41 (not V)
  Then the result is LAYOUT_REVISION_STALE with expected 41 and actual V
  And CameraCatalog was asked 0 times

Scenario: AS-2 conflict — stale edit never asks CameraCatalog
  Given a draft layout at version V and a permissive CameraCatalog
  When the draft is edited with expected version 41
  Then the result is LAYOUT_REVISION_STALE
  And CameraCatalog was asked 0 times

Scenario: AS-3 not found — an edit naming a missing revision never asks CameraCatalog
  Given a layout at version V without revision 42
  When revision 42 is edited with expected version V
  Then the result is LAYOUT_REVISION_NOT_FOUND
  And CameraCatalog was asked 0 times

Scenario: AS-4 conflict — an edit of a published revision never asks CameraCatalog
  Given a layout whose revision 1 is Published, at version V
  When revision 1 is edited with expected version V
  Then the result is LAYOUT_REVISION_NOT_DRAFT
  And CameraCatalog was asked 0 times

Scenario: AS-5 dependency down — a stale edit is still answered stale while CameraCatalog is unreachable
  Given CameraCatalog throws HttpRequestException on every call
  When a draft is edited with a stale expected version
  Then the result is LAYOUT_REVISION_STALE (no exception escapes)

Scenario: AS-6 happy path guard — a valid edit still asks CameraCatalog exactly once and is refused when cross-fab (unchanged)
  Given a draft at version V and a tile naming a camera outside the fab
  When the draft is edited with expected version V
  Then the result is LAYOUT_TILE_CAMERA_OUTSIDE_FAB naming that camera, with the same warning log
  And CameraCatalog was asked exactly once

Scenario: AS-7 bad request and auth (unchanged)
  An empty / overlapping / out-of-bounds / too-large grid is refused 400 before any lookup or call;
  a layout outside the caller's fabs is 404; a missing If-Match is 428; missing scope is 403.
  No code on those paths changes.
```

## 5. Functional requirements

- **FR-001** `EditDraftRevisionCommandHandler` MUST evaluate the stale-version, missing-revision and
  not-a-draft refusals before calling `ICameraFabGuard`.
- **FR-002** When any of those refusals applies, `ICameraFabGuard` MUST NOT be called.
- **FR-003** The relative order grid -> not found -> stale -> missing -> not draft is unchanged, and
  grid validation still precedes the repository lookup.
- **FR-004** The cross-fab refusal's code, status, payload and warning log are unchanged; a
  well-formed, current, draft edit still calls the guard exactly once, before any mutation.
- **FR-005** No change to `EditDraftRevisionError`, `Shared.Contracts`, the endpoint, the guard's
  implementation, `CreateLayoutDraftCommandHandler`, or any ADR.

## 6. Judgement calls

- **J1 [JUDGEMENT]** Not-a-draft and missing-revision move ahead of the remote call along with the
  version check, although the issue names only "stale". They are the same cheap, aggregate-only
  refusals inside the same extracted `RefuseRevision`; splitting it to keep them after the remote
  call would leave the defect half-fixed and the method torn in two.
- **J2 [JUDGEMENT]** Precedence between two simultaneous refusals changes from 400 (cross-fab) to
  409/404 (stale / missing / not draft). No convention binds "400 before 409" in this repository —
  #2936 found no 404-first rule either — and the caller of a stale request must re-read before any
  retry, at which point the cross-fab 400 is reported. Recorded so a reviewer does not read it as an
  accidental side effect.
- **J3 [JUDGEMENT]** Out of scope: `CreateLayoutDraftCommandHandler` also calls the guard, but it has
  no stale/revision checks to reorder.

## 7. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052, ADR-0054), sentence-style names (ADR-0053), `var`
wherever it compiles. No new test class (no shard-filter entry needed); the fake gains a call count
and an unreachable mode.
