# Tasks 158 — Save stays live during a re-read

**Phase:** 3 (Tasks) — ADR-0037 · **Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md) · **Issue:** #2379
**Engineer:** `frontend-engineer` (all tasks). No backend, no infra.
**Base:** `develop` at `37011568`.

---

## Phase 4a colour — read this before writing a line

| Story | Task | Colour | Obligation |
|---|---|---|---|
| **US1** | T001 | **RED** | Observed **failing** against unmodified `OverlayEditorDialog.tsx`; the verbatim failure is the engineer's brief and goes in the PR body (ADR-0139). |
| **US2** | T002 | **GREEN** | Characterisation. Captured **passing before** any production edit; must pass **unmodified** after. An assertion that needs editing means behaviour moved — block, do not adjust. |

Do not merge T001 and T002 into one file or one run. Their outputs are quoted
separately in the PR because they prove different things.

## Parallelism (ADR-0109)

- **T001 `[P]` with T002** — disjoint files, different feature directories
  (`features/overlays/` vs `features/layouts/`), no shared fixture.
- Everything after T002 is sequential: T003 edits the production file T001
  gates, T004 needs T002 to exist, T005 needs T004's result.
- **No foundational blocker.** Nothing in `Shared.Kernel`, `Shared.Contracts`,
  `AppHost` or any Aspire resource is touched, so nothing else in the run is
  blocked by this spec and it is blocked by nothing.
- **Before appending, confirm no open PR touches either test file.** At
  `37011568` the only open PR is #2384 (`fix/2382-a-teardown-that-honours-its-own-budget`,
  e2e teardown) — clear. If that changes, create
  `OverlayEditorDialogChainGate.test.tsx` / `LayoutEditorDialogChainGate.test.tsx`
  instead (plan §4.1).

---

## Tasks

### `[T001] [P] [US1]` — the RED test: Save is unavailable while the overlay re-read is in flight

**File:** `apps/management-web/src/features/overlays/OverlayEditorDialogChainRecovery.test.tsx` (append one `it` to the existing `describe`)

Reuse the harness already in the file: `ChainQueryState` (`:31-35`),
`setChainQueryState` (`:53-58`), `beginReRead` (`:160-162`), and the
`beforeEach` at `:172-176` that gives `refetchChainMock` its `beginReRead`
implementation. Stage it exactly as the FR-008 test at `:360-389` does, then
assert what that test does not:

1. `chainQueryState = { data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 }, isError: false, isFetching: false }`; `editError` = 409 `OVERLAY_REVISION_STALE`.
2. Render, find **Reload**, `await user.click(reload)` — state becomes `{ data: v7, isError: false, isFetching: true }`, `data` **retained**.
3. **Assert `Save draft` is disabled.** ← the failing assertion.
4. Click Save; assert `editDraftMock` call count is **unchanged** (no second PATCH carrying version 7). Attribute alone would pass a cosmetic fix.
5. Step state to `{ data: v8, isError: false, isFetching: false }`; assert Save is **enabled**, click it, assert the mutation was called with `version: 8`. Without this the gate could be stuck-on and still pass.

**Done when:** `pnpm --filter @smart-sentinel-eye/management-web test` runs this
file and **fails at step 3**, and the verbatim output is captured. A green first
run is a phase-4 failure — diagnose the harness (plan §4.3), never the assertion.

**Depends on:** nothing.

### `[T002] [P] [US2]` — the GREEN characterisation test: the layout dialog's `chainFetching` term

**File:** `apps/management-web/src/features/layouts/LayoutEditorDialogChainRecovery.test.tsx` (append one `it`)

The same five steps as T001 against the layout harness (`:147-149`, `:334-363`):
`LAYOUT_REVISION_STALE`, `layoutIdentifier`, `EDIT_TARGET.layoutIdentifier`.

The `it`'s doc comment must state, in one short paragraph, that it exists
because the term was **present but unpinned** — citing
`LayoutEditorDialog.tsx:412-413` and spec §2 — and that T004 proved it by
counterfactual. That sentence is the deliverable answering #2379's second
question; do not leave it only in the spec.

**Done when:** the file runs **green before any production file is touched**,
and that output is captured.

**Depends on:** nothing. `[P]` with T001.

### `[T003] [US1]` — add `chainFetching` to the overlay Save predicate

**File:** `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx` (line 303)

```
disabled={isLoading || (isEdit && currentChain === undefined)}
      →
disabled={isLoading || (isEdit && (currentChain === undefined || chainFetching))}
```

`chainFetching` is already bound at `:92`. Add **no** hook, state, import or
binding. Do **not** touch `onSubmit`'s existing `currentChain === undefined`
guard (plan §3). Do **not** copy the layout-only `knownCameras` term.

Add a short `why` comment above the button: `currentData` survives a
same-argument refetch, so the version held during a re-read is the one the
re-read exists to replace; the common trigger is the 412's own
`invalidatesTags`, not the Reload click. Point at `LayoutEditorDialog.tsx:403-413`
rather than restating it.

**Done when:** T001 goes green; the whole `management-web` suite is green;
`pnpm --filter @smart-sentinel-eye/management-web lint` and `typecheck` are clean.

**Depends on:** T001 (red observed first).

### `[T004] [US2]` — prove the layout pin by counterfactual

**Files:** none committed. A local, reverted edit.

Delete the ` || chainFetching` term from `LayoutEditorDialog.tsx:419`, run
`apps/management-web/src/features/layouts/`'s test files, and confirm **T002's
new `it` fails**. Restore the term (`git checkout -- <file>`, and confirm the
working tree is clean before continuing).

This is the task that actually answers #2379's second question with evidence
rather than with a reading. A guard that cannot be shown failing has not been
shown to guard anything — the repository has been wrong about this twice
(#2371's `createState.reset()`; the `data`/`currentData` note at
`LayoutEditorDialogChainRetention.test.tsx:33-38`).

**Done when:** the counterfactual failure output is captured for the PR body and
the term is restored. If T002 stays **green** with the term deleted, T002 is not
a pin — rewrite it (the likely cause is `data` being cleared where it should be
retained) before proceeding.

**Depends on:** T002.

### `[T005] [US2]` — correct the now-false comment in `LayoutEditorDialog.tsx`

**File:** `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx` (lines 412-413)

Replace *"Neither is pinned by a test in this repo, so the outcome is recorded
here rather than asserted"* with the truth after T002/T004: the Reload path is
now pinned by `LayoutEditorDialogChainRecovery.test.tsx` and proved by
counterfactual; the 412-invalidation path remains recorded rather than asserted
(plan §4.5 says why no test drives it). Keep it to two lines. **Comment only —
no code change in this file**, which is what keeps T002 a valid characterisation
test.

**Done when:** the comment matches what the repository does. FR-006.

**Depends on:** T004.

### `[T006] [chore]` — file the follow-up issue this spec refuses to absorb

**Files:** none. A `gh issue create`.

Title, roughly: *"A focused Save disabled mid-re-read loses focus to `<body>` —
both editor dialogs"*. Body must carry, verbatim enough to be checkable:

- The reachable sequence — click Save → focus on Save → 412 → `editDraftOverlayRevision`'s
  `invalidatesTags` (`apps/shared/src/api/overlays.api.ts:124-127`, no error
  branch) starts a background `getOverlay` re-read → Save natively disables →
  the browser drops focus.
- The precedent: `ChainRecoveryNotice.tsx:31-32` chose `aria-disabled` over
  native `disabled` for exactly this (spec 154), and spec 156 (#2372) was an
  entire spec about a recovery control destroying its own focused element.
- That it applies to **both** dialogs, `LayoutEditorDialog` having shipped the
  term since spec 153 — so it is not a regression introduced by #2379, it is a
  gap #2379 makes symmetrical.
- The adjacent, **explicitly unverified** question: whether a *failed* re-read
  leaves Save enabled on a stale `currentData` (RTK Query retains the cache
  entry's `data` on a rejected refetch; whether `currentData` follows was not
  checked). Mark it as needing verification, not as a finding.

Do **not** fix any of it under #2379.

**Done when:** the issue exists, is linked from the PR body, and is added to
Project #13 (`gh project item-add 13 --owner smartsolutionslab --url <issue-url>`).

**Depends on:** nothing; run it last so the PR body can cite the number.

---

## Dependency graph

```
T001 [P] ──► T003 ──┐
T002 [P] ──► T004 ──► T005 ──► (PR)
                     T006 ────┘
```

## Phase 3 gate (ADR-0037)

Tasks are atomic. **#2379 must be on Project #13** before phase 4 begins —
verify by `content.url` with `--limit 2000`, never by the number filter. No
per-task issues: this repo stopped creating them after spec 028, and #2379 is
the feature-level issue this `tasks.md` is tracked against.

## Definition of done for the whole spec

1. T001's captured **red**, and T002's captured **green-before**, both quoted in
   the PR body (ADR-0139).
2. T004's counterfactual failure quoted in the PR body.
3. `pnpm --filter @smart-sentinel-eye/management-web test`, `lint`, `typecheck`
   all clean; the repo-wide `pnpm test` unaffected.
4. Phase 5: the manual observation in spec §8 — one PATCH in the Network panel
   where `develop` shows two.
5. **§IV: N/A**, restated in the PR — no leg, no measurement owed.
6. Commits follow ADR-0030, no `Co-Authored-By` (ADR-0086). PR base `develop`
   (ADR-0028), with `--base develop` passed explicitly.
