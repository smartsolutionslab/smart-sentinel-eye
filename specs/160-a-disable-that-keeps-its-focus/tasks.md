# Tasks 160 — A disable that keeps its focus

**Phase:** 3 (Tasks) — ADR-0037 · **Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md) · **Issue:** #2387
**Engineer:** `frontend-engineer` — every task. No backend, no infra, no `src/`.
**Base:** `develop` at `b7c8332b`.

---

## Phase 4a colour — read this before writing a line

**All three stories are behaviour-changing. Every phase-4a test is RED.**
There is no characterisation obligation anywhere in this spec (constitution
§Testing, ADR-0139). Declared positively, because ambiguity resolves to red and a
silent declaration is how that gets missed.

| Story | Tasks | Colour | The red that must be observed and quoted |
|---|---|---|---|
| **US1** | T001, T002 | **RED** | `expect(document.activeElement).toBe(saveButton)` fails with the received element being `<body>` (or `div[role="dialog"]`). Quote the received value — it is the finding. |
| **US2** | T006, T007 | **RED** | `aria-disabled` is absent/`"false"` on a refused re-read, **and** the attempted-submit call count is 1 where 0 is expected. |
| **US3** | T010, T011 | **RED** | The `*-chain-recovery-status` region is **empty** where the announcement is expected. An empty-vs-expected diff is the proof the path was silent. |

**Two rules that apply to every test task below, and that phase 6 will check:**

1. **Never** `expect(document.activeElement).not.toBe(document.body)`. It passes
   against the container-refocus accident in spec §1.1 and against any future
   change that parks focus anywhere in the dialog. Assert the element focus is
   *on*, against a reference captured **before** the transition.
2. **Never** assert `aria-disabled` alone. An implementation that renders the
   attribute and submits anyway would pass an attribute-only suite while being
   strictly worse than `develop`. Every gate-closed assertion is paired with a
   mutation-call-count assertion; every gate-open assertion is followed by a click
   that actually calls the mutation with the expected version.

A test that is **green on its first run** is a phase-4 failure. Diagnose the
harness (plan §5.2, §5.4) — never the assertion.

## Parallelism (ADR-0109)

- **No foundational blocker.** Nothing in `Shared.Kernel`, `Shared.Contracts`,
  `AppHost` or any Aspire resource is touched. This spec blocks nothing else in
  the run and is blocked by nothing.
- `[P]` pairs are always `features/overlays/**` against `features/layouts/**` —
  disjoint directories, no shared fixture, no shared file.
- **T012 is the one task that is `[P]` with nothing**: it is the only task that
  touches `apps/shared`, and it edits both dialogs.
- **Before starting, confirm no open PR touches these files.** At `b7c8332b` the
  only open PR is **#2391** (`docs/adr-0149-create-and-edit-are-routes`), which is
  documentation only — clear.

---

## Tasks

### `[T001] [P] [US1]` — RED: the overlay Save keeps focus through the whole conflict cycle

**File (new):** `apps/management-web/src/features/overlays/OverlayEditorDialogSaveGate.test.tsx`

A new file, not an append (plan §5.1): T003 rewrites assertions inside the
existing `*ChainRecovery.test.tsx`, and the phase-4a red must be attributable to
the new behaviour alone.

**Harness.** Copy the `useSyncExternalStore` chain-query harness from
`OverlayEditorDialogChainRecovery.test.tsx:31-114` and **extend it** (plan §5.2):
the existing mutation mock hard-codes `isLoading: false` (`:74-79`) and
`editDraftMock` resolves immediately, so it cannot drive the moment that actually
destroys focus. Add a second steppable source for the mutation's `isLoading` and
a deferred promise for its trigger.

**Scenario:** spec §5.1, verbatim. Focus Save, click, hold the mutation pending,
refuse it with 409 `OVERLAY_REVISION_STALE`, step the chain to in-flight, then to
version 8 — asserting `document.activeElement` is the Save button at **every**
step, and closing with a click that submits version 8.

**Plus** spec §5.3 as a second `it`: focus the label text field, press Enter while
the chain is in flight, assert `editDraftMock` was not called. This path exists
only after T003 (a natively disabled default button suppresses implicit
submission), so on `develop` it passes vacuously — say so in the `it`'s doc
comment, and keep it: it is the assertion that catches R1, the way this change
could end up worse than the bug.

**Done when:** `pnpm --filter @smart-sentinel-eye/management-web test` runs this
file and **fails on the first focus assertion**, and the verbatim output —
including the received element — is captured for the PR body.

**Depends on:** nothing.

### `[T002] [P] [US1]` — RED: the layout Save keeps focus through the whole conflict cycle

**File (new):** `apps/management-web/src/features/layouts/LayoutEditorDialogSaveGate.test.tsx`

The same two `it`s against the layout harness
(`LayoutEditorDialogChainRecovery.test.tsx`), with `LAYOUT_REVISION_STALE`,
`layoutIdentifier`, and at least one camera present so the pre-existing
`knownCameras.size === 0` term is not what closes the gate — otherwise the test
passes for the wrong reason.

**Done when:** the file fails on the first focus assertion, captured verbatim.

**Depends on:** nothing. `[P]` with T001.

### `[T003] [P] [US1]` — the overlay Save gate, and its assertion migration

**Files:**
- `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx`
- `apps/management-web/src/features/overlays/OverlayEditorDialog.test.tsx` (297, 305)
- `apps/management-web/src/features/overlays/OverlayEditorDialogChainRecovery.test.tsx` (235, 257, 500, 519)
- `apps/management-web/src/features/overlays/OverlayEditorDialogChainRetention.test.tsx` (142, 168)
- `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx` (244)

**One commit.** The assertion rewrites and the production change land together:
split, the intervening commit leaves the suite red, and ADR-0087's rebase-merge
lands commits individually on `develop`.

Production change, per plan §3.1:

1. `saveBlocked = isLoading || (isEdit && (currentChain === undefined || chainFetching))`
   as a named local next to `saveRef` (`:240`). **`chainFailed` is NOT added here**
   — that is T008, and keeping it out is what makes T006's red attributable.
2. `:314` — `disabled={...}` → `aria-disabled={saveBlocked}`, plus FR-005's
   `aria-disabled:opacity-50 aria-disabled:cursor-progress` className, mirroring
   `ChainRecoveryNotice.tsx:179`. **No `pointer-events-none`.**
3. The form at `:258` gains the submit guard that short-circuits on `saveBlocked`
   **before** `handleSubmit`. Type it through `ComponentProps<'form'>` /
   `ComponentRef<'form'>` — naming `HTMLFormElement` trips `no-undef` in this
   app's eslint config, and **widening that config is the gate-weakening ADR-0144
   rules out** (plan §3.1c).
4. Keep `onSubmit`'s `currentChain === undefined` guard (`:189`); correct its
   comment — "defensive rather than reachable through the UI" stops being true.
5. Replace the button's existing comment with a shorter one pointing at
   `ChainRecoveryNotice.tsx:31-33` for the `aria-disabled` reasoning. Do not
   restate it. Net line change close to zero (ADR-0084, plan §4).

Assertion migration, for **each** line listed above: same claim, new mechanism
(spec §5.4). `toBeDisabled()` → the `aria-disabled` attribute **plus** an
attempted submit that calls no mutation; `not.toBeDisabled()` → the attribute
**plus** a submit that does. **If an assertion cannot be restated as the same
claim, stop and report** — that is evidence behaviour moved somewhere unintended,
not something to adjust.

**Must stay green, unmodified:** every focus assertion in
`OverlayEditorDialogChainRecovery.test.tsx` (`:206`, `:233`, `:256`, `:377`,
`:388`, `:422`, `:435`). They encode spec 156's Retry/Reload behaviour and are not
in the migration list (plan §7 R4).

**Done when:** T001 goes green; the whole `management-web` suite is green;
`lint` and `typecheck` clean.

**Depends on:** T001.

### `[T004] [P] [US1]` — the layout Save gate, its assertion migration, and FR-011

**Files:**
- `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`
- `apps/management-web/src/features/layouts/LayoutEditorDialogChainRecovery.test.tsx` (211, 232, 476, 494, and the doc comment at 433-441)
- `apps/management-web/src/features/layouts/LayoutEditorDialogChainRetention.test.tsx` (207, 332, 409)
- `apps/management-web/src/features/layouts/LayoutEditorDialogRetention.test.tsx` (200)

**One commit**, same reasoning as T003. The same five production changes at
`:93`, `:261`, `:322`, `:418-424`, with the layout-only
`knownCameras.size === 0` term joining `saveBlocked` (spec §11 A1), and the
`onSubmit` guard at `:221` kept exactly as it is.

**Plus FR-011:** the comment block at `:389-417` has its two `412`s corrected to
**`409`** — `LAYOUT_REVISION_STALE` is `HttpStatusCode.Conflict`
(`src/LayoutComposition/Application/Commands/EditDraftRevisionErrors.cs:83-87`),
and that file's own comment says *"409 rather than 412 so it reads as the domain
conflict it is"*. The block must come out **shorter**: 29 lines of comment above
a 6-line button, in a file near the 300-LOC cap.

**Done when:** T002 goes green; the suite, `lint` and `typecheck` are clean.

**Depends on:** T002. `[P]` with T003.

### `[T005] [P] [US1]` — make the e2e Save click wait for availability

**File:** `e2e/overlays.spec.ts` (line 145)

`await page.getByRole('button', { name: /^save draft$/i }).click()` runs with no
wait for the gate to open. Whether Playwright 1.62's click actionability treats
`aria-disabled="true"` as "not enabled" is **unverified** (spec §11 A3); if it
does not, that click can land on a closed gate, do nothing, and the spec still
passes on the assertion below it — a green e2e proving less than it did.

Add an explicit `await expect(save).not.toHaveAttribute('aria-disabled', 'true')`
before the click. One line, and it removes the dependency on the answer. Leave
the three create-mode `save as draft` clicks in `e2e/layouts.spec.ts` alone —
their gate has no chain term.

**Done when:** the line is in and `e2e/overlays.spec.ts` still passes.

**Depends on:** T003. `[P]` with T004, T006, T007.

### `[T006] [P] [US2]` — RED: a refused overlay re-read keeps Save unavailable

**File:** `apps/management-web/src/features/overlays/OverlayEditorDialogSaveGate.test.tsx` (append one `it`)

Spec §5.2, verbatim — including the FR-008 half: assert the **Retry** button is
on screen while the gate is closed, and that a successful Retry both opens the
gate and leaves focus on Save. Without that half this task would be specifying a
dead end for a keyboard operator.

Written **after** T003 so its red is attributable to the missing `chainFailed`
term alone, not to the mechanism change.

**Done when:** it fails on the `aria-disabled` assertion **and** the call-count
assertion (call count 1 where 0 is expected), captured verbatim.

**Depends on:** T003.

### `[T007] [P] [US2]` — RED: a refused layout re-read keeps Save unavailable

**File:** `apps/management-web/src/features/layouts/LayoutEditorDialogSaveGate.test.tsx` (append one `it`)

The same `it` against the layout harness.

**Done when:** the same two assertions fail, captured verbatim.

**Depends on:** T004. `[P]` with T006.

### `[T008] [US2]` — add the `chainFailed` term to both gates

**Files:**
- `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx`
- `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`

One term in each `saveBlocked`, inside the `isEdit` guard (the chain query is
`skipToken` in create mode, so it is already inert there):

```
... (currentChain === undefined || chainFetching || chainFailed)
```

`chainFailed` is **already bound** — `OverlayEditorDialog.tsx:91`,
`LayoutEditorDialog.tsx:89` — and today reaches only `ChainRecoveryNotice`'s
`readFailed` prop. Add no hook, no state, no import.

One `why` line: RTK Query's `queryThunk.rejected` writes only `status` and
`error` and **retains `data`**, and `currentData` is that raw substate `data`, so
a refused re-read leaves a version already known to be stale
(`@reduxjs/toolkit` 2.12.0, `dist/query/rtk-query.modern.mjs:1443-1455` and
`dist/query/react/rtk-query-react.modern.mjs:155`). Cite, do not restate.

**Done when:** T006 and T007 go green; the whole suite, `lint`, `typecheck` clean.

**Depends on:** T006, T007.

### `[T009] [US2]` — prove the `chainFailed` term by counterfactual

**Files:** none committed. A local, reverted edit.

Delete ` || chainFailed` from **each** dialog in turn, run that feature
directory's tests, and confirm the T006 / T007 `it` fails. Restore both
(`git checkout -- <file>`) and confirm the working tree is clean.

A guard that cannot be shown failing has not been shown to guard anything, and
this repository has been wrong about exactly that twice (#2371's
`createState.reset()`; the `data`/`currentData` note at
`LayoutEditorDialogChainRetention.test.tsx:33-38`). If a test stays green with
the term deleted, it is not a pin — rewrite it before proceeding.

**Done when:** both counterfactual failures are captured for the PR body and both
terms are restored.

**Depends on:** T008.

### `[T010] [P] [US3]` — RED: an unrequested overlay re-read announces itself, and a first read does not

**File (new):** `apps/management-web/src/features/overlays/OverlayEditorDialogChainAnnouncement.test.tsx`

Two `it`s, and **both are mandatory**:

1. Spec §5.5 — with nothing clicked in the recovery notice, step the chain to
   in-flight and assert `getByTestId('overlay-chain-recovery-status')` reads
   `Re-reading the overlay…`; step it to version 8 and assert
   `The overlay was read. Save is available.`; assert `document.activeElement` is
   **unchanged** across both (FR-009 — this path must not move focus).
2. Spec §5.6, **the trap** — open the dialog with `currentData: undefined` and
   `isFetching: true`, assert the region is **empty**, step to a resolved read,
   assert it is **still empty**. Without this, the obvious implementation
   announces "Re-reading the overlay…" on every dialog open. It is spec 156's
   FR-007 trap (`ChainRecoveryNotice.tsx:71-78`) in a new place.

**Done when:** `it` 1 fails against an **empty** region (quote the diff) and
`it` 2 **passes** — it is the negative half and it is green on `develop` by
accident. Say so in its doc comment: it exists to stay green through T012, not to
go red now.

**Depends on:** T003.

### `[T011] [P] [US3]` — RED: the same, for the layout dialog

**File (new):** `apps/management-web/src/features/layouts/LayoutEditorDialogChainAnnouncement.test.tsx`

The same two `it`s with `layout-chain-recovery-status` and the layout noun. This
is not redundant with T010: `ChainRecoveryNotice` is shared, but the FR-010
discriminator is computed **per dialog**, so only a layout test catches the
layout dialog being left unwired.

**Done when:** the same red and the same green.

**Depends on:** T004. `[P]` with T010.

### `[T012] [US3]` — announce an unrequested re-read

**Files:**
- `apps/shared/src/ui/composites/ChainRecoveryNotice.tsx`
- `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx` (`:291-298`)
- `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx` (`:376-384`)

`[P]` with **nothing** — the only task touching `apps/shared`, and it edits both
dialogs.

Per plan §3.3:

1. New prop `previouslyRead: boolean`, documented alongside the other six, sourced
   from `currentChain !== undefined` at both call sites.
2. The rising edge goes **inside the existing settle effect** (`:88-132`), which
   already keeps `wasReReadingRef` and already computes the edge — not a second
   effect. Two effects racing the same ref is how the `CameraViewer` camera-swap
   ordering bug was introduced.
3. The `origin === null` bail-out at `:91` becomes: act only when
   `previouslyRead`, and act with the **announcement half only** — on
   `readFailed`, clear the region exactly as `:107` does (the chain arm's own
   `role="alert"` insertion is the announcement); otherwise write
   `The {noun} was read. Save is available.`
4. Extract the `Re-reading the {noun}…` string so `:165` and the new rising edge
   cannot drift apart.
5. **Never**, on this path: `setOrigin(null)` (already `null`),
   `setRetryFailureKey` (that key owns the Retry `<p>`'s remount and bumping it
   here would move focus), `onReadRecovered()` (FR-009).

**Must not change:** `chainArmActive` (`:177`), the `retryFailureKey` remount, the
refocus effect (`:139-145`), `activate()`'s early return at `:151`, and both arms'
JSX. Those encode spec 156's two phase-6 blockers, and all of their existing
tests must stay green **unmodified**.

**Done when:** T010's and T011's first `it` go green, their second `it` is
**still** green, the full `management-web` and `shared` suites are green, `lint`
and `typecheck` clean.

**Depends on:** T010, T011.

---

## Dependency graph

```
T001 [P] ──► T003 [P] ──┬──► T006 [P] ──┐
                        ├──► T005 [P]   ├──► T008 ──► T009
T002 [P] ──► T004 [P] ──┼──► T007 [P] ──┘
                        │
       T003,T004 ───────┴──► T010 [P], T011 [P] ──► T012
```

Four parallel fronts, in order: (T001‖T002) → (T003‖T004) → (T005‖T006‖T007, and
T010‖T011) → T008 → T009, T012.

## Phase 3 gate (ADR-0037)

Tasks are atomic. **#2387 is already on Project #13** — `gh issue view 2387`
reports `projects: Smart Sentinel Eye (In Progress)`, so no `item-add` is needed.
If that is re-verified by listing, use `--limit 2000` and match on `content.url`;
the number filter returns zero on a filled board.

**No per-task issues.** This repo stopped creating them after spec 028; #2387 is
the feature-level issue this `tasks.md` is tracked against.

## Definition of done for the whole spec

1. T001's and T002's captured **red**, quoted in the PR body with the received
   focus element (ADR-0139). T006's and T007's red, with the call count. T010's
   and T011's red, with the empty region.
2. T009's two counterfactual failures quoted in the PR body.
3. The assertion migration (T003, T004) called out explicitly in the PR body as
   *rewritten, not weakened*, with spec §5.4's table linked — 17 assertions, each
   gaining a behavioural half.
4. `pnpm --filter @smart-sentinel-eye/management-web test`, the `shared` suite,
   `lint` and `typecheck` all clean. Both dialog files still under 300 LOC
   (ADR-0084).
5. Phase 5: the manual observation in spec §8, all three parts — including §8.3
   step 10, the negative half that is easiest to skip.
6. **§IV: N/A**, restated in the PR — no leg, no measurement owed.
7. **No ADR written** (spec §6). If phase 4 or 6 concludes a cross-cutting
   `aria-disabled` rule is needed, that is a **blocked** outcome, not a task:
   comment, label `agent:blocked`, and hand it back.
8. Commits follow ADR-0030, **no `Co-Authored-By`** (ADR-0086). PR base `develop`
   (ADR-0028), with `--base develop` passed explicitly.
