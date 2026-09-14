# Tasks — Spec 153: A layout edit reads its own version

**Issue:** #2368 · **Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Phase 4a colour: RED.** Behaviour changes — a version that was submitted is
no longer submitted, and a Save that was enabled is now disabled.

**Phase 2 (plan.md): skipped — no server-side change; no bounded context,
entity, aggregate, contract or integration event is touched.** A plan.md here
would be five empty headings. Record `Phase 2: skipped — client-only cache
semantics, no domain surface` in the PR body.

## Parallelism

Everything is in **one file plus its two mock sites**, so there is nothing to
fan out — no `[P]` markers earn their place here (ADR-0109). T001 must be
observed red before T002 exists; T002 and T003 are one commit because splitting
them leaves the suite red for a reason unrelated to the defect.

## Tasks

### US1 (P1) — An edit submits the version of the layout it is editing

- **[T001] [US1] Write the discriminating test, observe it RED.**
  New file `apps/management-web/src/features/layouts/LayoutEditorDialogChainRetention.test.tsx`.
  **The name matters**: `LayoutEditorDialogRetention.test.tsx` already exists and
  is about *camera* retention. Do not extend it.

  **It must drive the real `useGetLayoutQuery`.** Every existing layouts test
  mocks the hook, and a mocked hook returns the same object regardless of its
  argument — it cannot express the `data` / `currentData` distinction at all, so
  no assertion written against a mock can fail for this defect. Template, to be
  followed closely: `OverlayEditorDialogChainRetention.test.tsx` on
  `feat/2364-an-overlay-draft-you-can-edit` (commit `ad308eb7`). Shape:

  - `vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test')` **before** any
    dynamic import — `gateway.ts` resolves the origin at module load and Node's
    `Request` rejects a relative URL.
  - Mock only the *neighbouring* queries (`cameras.api`'s
    `useListAllCameraChoicesQuery`, `overlays.api`'s `useListOverlaysQuery`) so
    the layouts gateway is this suite's only live network surface. Supply
    **both** `data` and `currentData` on those mocks.
  - Real store over `layoutsApi.reducer` + `layoutsApi.middleware`.
  - `vi.stubGlobal('fetch', …)`: GET for layout A answers version 7; GET for
    layout B is **held open** for the whole test; PATCH answers 200.
  - `render` with `editTarget = A`, wait for Save to enable; `rerender` with
    `open={false} editTarget={undefined}`; `rerender` with `editTarget = B`.
    This is the real `A → undefined → B` transition `LayoutsPage.tsx:333-340`
    produces, not a test-only shortcut.
  - Assert (a) Save is **disabled** while B's GET is held, and (b) defence in
    depth, that no `PATCH` was ever issued carrying `If-Match: "7"`.

  Second case, **window 3**: A's GET answers 7, dialog settles; rerender to
  `undefined` then back to **A** while a second GET for A is held; assert Save
  is disabled rather than submitting the stale 7.

  **Capture the verbatim failure output.** It is the phase-4 gate evidence and
  goes in the PR body (ADR-0139).

- **[T002] [US1] FR-001 + FR-003: read the chain the dialog is actually editing.**
  `LayoutEditorDialog.tsx:65` — `data: currentChain` becomes
  `currentData: currentChain`, and the hook takes
  `{ refetchOnMountOrArgChange: true }`. Also destructure `isError` and
  `isFetching` for T004/T003. Replace the existing comment block's silence on
  this point with the *why*, mirroring the wording committed at `9357b65e`:
  `data` is the last successful result for any argument the hook has ever
  taken; this dialog is permanently mounted and driven `A → undefined → B`.

- **[T003] [US1] FR-002: a visible Save gate, not a silent return.**
  `disabled={isLoading || knownCameras.size === 0}` gains
  `|| (isEdit && (currentChain === undefined || chainFetching))`. The
  `if (currentChain === undefined) return;` inside `onSubmit` stays as a
  type narrowing, but is no longer the only thing standing between the
  operator and a no-op click.

  **Corrected in phase 4a**: `chainFetching` is *not* what closes window 3.
  Driving the real hook against a raw store showed `branchDraftRevision`
  invalidates `{type:'Layout', id}` while the dialog has zero subscribers
  (`LayoutsPage.onEdit` awaits the branch before `setEditTarget`), and RTK
  Query evicts an invalidated entry with no subscriber outright rather than
  refetching it in the background — so `currentData` is `undefined` on
  reopen, same as windows 1/2, and `currentChain === undefined` alone closes
  window 3. `chainFetching` is included anyway because it closes a *fourth*
  window the original sweep missed: Reload (`refetchChain()`) keeps the
  dialog subscribed, so `currentData` does stay stale while that refetch is
  in flight. Its outcome is a safe 412, not a silent wrong write, and neither
  red test in T001 pins it. A commoner trigger than Reload was found in phase
  6: RTK Query applies a mutation's `invalidatesTags` on a *rejected*
  response too, so a stale-version 412 from `editDraftRevision` starts the
  same kind of background refetch while the dialog stays subscribed — see
  spec.md "The refetch-in-flight windows, found in phase 4a/6".

- **[T004] [US1] FR-004: a failed chain read says so.**
  A `role="alert"` paragraph with a Retry calling `refetchChain`, shown when
  `isEdit && chainFailed`. It takes **priority** over `backendError` — one
  alert, never two siblings — using the ternary form committed at `9357b65e`.
  Without this, T003 turns a failed read into a permanently disabled Save with
  nothing on screen.

- **[T005] [US1] Update the two mocks that now under-supply the hook.**
  Same commit as T002-T004 or the suite goes red for the wrong reason:
  - `LayoutEditorDialog.test.tsx:24-28` — add `currentData` beside `data`, and
    `isError: false`, `isFetching: false`.
  - `LayoutsPage.test.tsx:20` — same.
  - `LayoutEditorDialogRetention.test.tsx:36` already supplies
    `data: undefined` and renders create-mode only, so FR-005 keeps it green;
    add `currentData: undefined` for consistency and confirm, do not assume.

- **[T006] [US1] Run the full management-web suite and the layouts e2e.**
  `e2e/layouts.spec.ts` exercises the edit path against the real gateway and is
  the strongest existing guard that FR-002's gate has not disabled Save for
  everyone. Quote its result in the PR.

## Guards — existing tests that must stay green, unmodified

| File | What it pins |
|---|---|
| `apps/management-web/src/features/layouts/LayoutEditorDialog.test.tsx` (`version: 7` at :257) | The edit still submits the chain version it read, and the Reload-refetch path still works (:319) |
| `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` | Branch-then-open still reaches the dialog with the new revision number |
| `apps/management-web/src/features/layouts/LayoutEditorDialogRetention.test.tsx` | FR-005 — create mode's Save is not gated; a fragment matching nothing still leaves Save enabled |
| `e2e/layouts.spec.ts` | The whole edit-after-publish path end to end |

An assertion in any of these that has to be **edited** to pass is evidence the
change moved more behaviour than intended — block, do not adjust.

## Dependencies

```
T001 (RED, verbatim output captured)
  └─> T002 ─┬─> T003 ─┬─> T005 ─> T006
            └─> T004 ─┘
```

T002-T005 land as **one commit**: T005 without T002 is meaningless and T002
without T005 leaves the suite red on the mocks rather than on the defect.

## Out of scope, to be filed as separate issues

1. **Reset-on-close in the same file** (`LayoutEditorDialog.tsx:66,70-72`) — a
   refused edit's error survives into the next open, because `isEdit` has
   already gone false when the effect fires. Twin of the second defect in
   `9357b65e`. Different user-visible behaviour, so a different issue.
2. **`CameraViewer.tsx:100`** — `data:` on `useGetStreamQuery`; a reassigned
   tile keeps playing the previous camera's WHEP session under the new
   camera's identity, because the session effect is keyed on `whepUrl`, not on
   `cameraIdentifier`. Highest consequence of the sweep.
3. **`FrameGrabber.tsx:37`** — same hook, same shape.
4. **`CellPage.tsx:435,469`** — previous overlay's label renders over the new one.
5. **An ESLint rule for the whole category** — ADR-class, outside the lane.
