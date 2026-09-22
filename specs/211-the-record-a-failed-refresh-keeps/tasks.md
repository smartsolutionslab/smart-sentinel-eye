# Tasks — Spec 211, the record a failed refresh keeps

**Spec:** `specs/211-the-record-a-failed-refresh-keeps/spec.md`
**Plan:** `specs/211-the-record-a-failed-refresh-keeps/plan.md`
**Issue:** #2432 (feature-level; add it to Project #13 — `/speckit-tasks` adds nothing to the board)
**Branch:** `2432-transient-refetch-camera-exists` (worktree `D:/Github/sse-2432`)
**Phase-4a colour:** **RED** — behaviour-changing. New tests must be observed **failing** and the verbatim output quoted in the PR (ADR-0139, ADR-0144).

---

## Ordering

```
T001 [P] ─┐
T002 [P] ─┴─► T003 ─► T004 ─► T005 ─► T006
(4a red)     (4b)     (verify) (QA)   (PR)
```

`T001` and `T002` are the phase-4a red. **Both must be red-observed before `T003` touches production code.** `T003` is the only production edit. Nothing here is foundational to anything else in the repo — no `Shared.Kernel`, no `Shared.Contracts`, no AppHost resource — so there is nothing for an orchestrator to fan out beyond the one `[P]` pair below.

## Parallelism (ADR-0109)

`T001` and `T002` own **disjoint files** — one unit test file, one e2e spec file — and are marked `[P]`. They can run in two `test-writer` passes at once, or in one pass; either satisfies the gate as long as both colours are reported separately.

Everything after `T003` is strictly serial: one file, one reviewer, one PR.

---

## US1 (P1) — A failed refresh does not deny the camera

### Phase 4a — RED (`test-writer`)

#### `[T001] [P] [US1]` — Unit tests for the split gate

**File (sole owner):** `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`

Add tests to the existing `describe('CameraDetailPage')`. **Edit no existing test and no existing `mockReturnValue`** — the plan shows why none needs it. Each new test sets its own hook shape including `currentData` and `refetch`.

| Scenario | Mock shape | Assert | Expected today |
|---|---|---|---|
| US1-A | `{ data: camera, currentData: camera, isLoading: false, error: { status: 503 }, refetch }` | heading = camera name; fab, RTSP URL and status present; `getByRole('alert')` contains `/could not refresh/i`; a `Retry` button exists; **no** heading `/no such camera/i` | **RED** |
| US1-B | same | clicking `Retry` calls `refetch` exactly once | **RED** |
| US1-C | `{ data: camera, currentData: undefined, isLoading: false, error: { status: 404 }, refetch }` | heading `/no such camera/i`; `queryByText(camera.name)` is null; `queryByText(camera.rtspUrl)` is null; no `Retry`; `queryByRole('alert')` is null | green today, **must stay green** |
| US1-E (alert wording) | US1-A's shape | the rendered text does not match `/access\|permission\|not yours\|another fab/i` | green today |
| US1-F | `{ data: camera, currentData: camera, isLoading: false, error: undefined, refetch }` | record present; `queryByRole('alert')` is null | green today |
| US1-G | retired camera + `error: { status: 503 }`, `currentData` set | alert present; retired notice present; no viewer, no Rename, no Correct-the-address, no Retire | **RED** |

**The US1-A / US1-C pair is the load-bearing part.** A `data`-gated implementation passes A and fails C; only `currentData` passes both. Write them adjacent, with a comment saying so, so a later reader cannot delete C as redundant.

**Report:** run `pnpm vitest run apps/management-web/src/features/cameras/CameraDetailPage.test.tsx` and return the **verbatim** output. State per test which were red and which were green-by-design — a single colour for the file is not a sufficient report.

---

#### `[T002] [P] [US1]` — E2E: a failed refresh after a rename keeps the camera

**File (sole owner):** `e2e/camera-detail.spec.ts`

Add one test, reusing the file's existing `signInAsOperator`, `registerCamera`, `FIRST_WRITE_TEST_TIMEOUT_MS` helpers.

1. Sign in, register a camera, open it from the list, assert the heading.
2. Install `page.route` on a URL predicate matching the detail path. The handler must:
   - `route.fallback()` for any method other than `GET` — **the `PATCH` is the same URL**, and a handler that did not check would fail the rename itself, making the test red for the wrong reason and red *after* the fix too (`system-variables.spec.ts:110-130` records exactly this trap);
   - let the **first** `GET` through — a route installed before the initial load exercises US1-D, not US1-A;
   - `route.fulfill({ status: 503 })` for subsequent `GET`s.
3. Rename the camera via the dialog.
4. Assert: the heading is still the camera (old or new name — the assertion is that the record is on screen, not which revision), **and** `getByRole('alert')` matches `/could not refresh/i`, **and** no `/no such camera/i` heading.
5. `page.unroute(...)`, press `Retry`, assert the alert is gone and the **new** name is shown.

**Report:** run the spec against the live stack and return the verbatim output. Expected **RED** at step 4 with the current code — the page shows "No such camera".

*If the stack cannot be booted in this pass, say so explicitly and hand `T002` on as unexecuted rather than reporting a colour it did not observe. `T001`'s red is sufficient to unblock `T003`; `T002`'s red is required before `T006`.*

---

### Phase 4b — GREEN (`frontend-engineer`)

#### `[T003] [US1]` — Split the gate on `currentData` and add the staleness alert

**File (sole owner):** `apps/management-web/src/features/cameras/CameraDetailPage.tsx`
**Depends on:** T001 red observed (T002 red observed if the stack was available)
**Brief:** the verbatim red output from T001/T002. **You may not edit the tests to pass.**

1. `:42` — destructure `currentData` and `refetch` alongside `data`, `isLoading`, `error`.
2. `:57` — replace the `||` with the condition from `plan.md` §"The rendering contract":
   `camera === undefined || (error !== undefined && currentData === undefined)`.
3. `:48-56` — **extend** the FR-008 comment (do not replace it) to say why the second clause exists: the page refuses to render a record belonging to an identifier other than the one in the URL, which is what keeps FR-008 true across a navigation.
4. Inside the returned page, after `</header>` and before the `CameraViewer` block, render the alert when `error !== undefined` — markup, `role`, Tailwind classes and `void refetch()` **copied verbatim** from `CamerasPage.tsx:123-133`; only the sentence differs:
   `Could not refresh this camera — what you see may be out of date.` + a `Retry` button.

**Forbidden in this task:** reading `error.status`; gating on `data` instead of `currentData`; adding dismiss state; extracting a shared banner component; editing `cameras.api.ts`, anything under `apps/shared/`, `kiosk-web`, `CamerasPage.tsx`, or any other page; changing the `isLoading` branch or the "No such camera" markup.

**Done when:** every T001 test is green with its assertions unmodified, all 13 pre-existing tests in the file are still green **unmodified**, and `pnpm lint && pnpm typecheck && pnpm typecheck:e2e && pnpm test` are clean.

---

## Phase 5 — Verify

#### `[T004]` — Observe it end to end (`/verify`)

Run the procedure in `spec.md` §"Independent end-to-end test procedure" against the live Aspire stack and write `specs/211-the-record-a-failed-refresh-keeps/verification.md`.

Must record: the camera stays on screen with the alert after the injected `GET` failure; Retry clears the alert and shows the refreshed record; a bogus identifier still renders "No such camera" with no Retry. **Latency: N/A** — the management console is not on the event→overlay path (constitution §IV); say so explicitly rather than omitting it.

Before trusting any manual observation, check the AppHost process's start time against the commit — a persistent stack keeps serving the binaries it booted with.

## Phase 6 — QA

#### `[T005]` — Review

`frontend-reviewer` **and** `security-reviewer` (the change sits on the FR-008 information-disclosure control). Review focus is the numbered list in `plan.md` §"Review focus for phase 6" — above all: the gate reads `currentData`, no `error.status` is read, and the FR-008 `innerHTML`-equality test at `CameraDetailPage.test.tsx:312` still passes unmodified.

## Phase 7 — PR

#### `[T006]` — Open the PR

`gh pr create --base develop`. Body must carry:

- `Closes #2432`.
- The **verbatim** red output from T001 (and T002 if executed) — ADR-0139; this is the only form of the red-first evidence a later reader can check.
- The narrowing this spec made to the issue's proposed fix: the gate is `currentData`, not `data`, because `data` can hold the previously-viewed camera's record across a navigation.
- Assumption **A1** (the banner's wording is chosen, not specified) marked as a guess.
- `Phase 3: feature issue #2432 on Project #13` — no per-task issues (the board has been feature-level since spec 028).

## Follow-ups, filed (not in this PR)

| Issue | What | Why it is not here |
|---|---|---|
| **#2522** | The navigation flash: `CameraDetailPage` renders the previously-viewed camera at the new camera's URL while the new one loads, because `isLoading` is false whenever `data` carried over from a previous argument | Pre-existing and distinct; a bug fix changes the bug, nothing else |
| **#2523** | Six inline copies of the same `role="alert"` + Retry banner across `CamerasPage`, `AuditPage`, `LayoutsPage`, `OverlaysPage`, `SystemVariablesPage` and now `CameraDetailPage` | Extraction touches five already-correct files; a refactor, not this fix |

---

## Task table

| ID | [P] | Story | Agent | File(s) | Depends on |
|---|---|---|---|---|---|
| T001 | `[P]` | US1 | `test-writer` | `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx` | — |
| T002 | `[P]` | US1 | `test-writer` | `e2e/camera-detail.spec.ts` | — |
| T003 | | US1 | `frontend-engineer` | `apps/management-web/src/features/cameras/CameraDetailPage.tsx` | T001 (red observed), T002 (red observed, if run) |
| T004 | | — | `/verify` | `specs/211-.../verification.md` | T003 |
| T005 | | — | `frontend-reviewer` + `security-reviewer` | — | T004 |
| T006 | | — | orchestrator | — | T005 |
