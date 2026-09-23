# Verification 224 — Visible, not merely present

**Spec**: `spec.md` · **Plan**: `plan.md` · **Tasks**: `tasks.md` · **Issue**: #2304
**Phase**: 5 (Verify) · **Branch**: `2304-visible-not-merely-present`
**Baseline**: `70b52c96` (`origin/develop`)
**Phase 4a colour**: characterisation, observed GREEN throughout — confirmed below.

This records T001's baseline, T009's post-change capture, T008's extent grep, and
T011's counterfactual (red, then reverted to green), per `tasks.md` T012.

---

## 1. T001 — baseline capture (before any edit)

Run on the branch tip before T003–T010 (tree identical to `70b52c96` plus
phase 1–3 docs only — verified via `git diff origin/develop --stat` showing
only `specs/224-visible-not-merely-present/{spec,plan,tasks}.md`):

```
> @smart-sentinel-eye/shared@0.0.0 test D:\Github\sse-2304\apps\shared
> vitest run

 RUN  v4.1.11 D:/Github/sse-2304/apps/shared

 Test Files  33 passed (33)
      Tests  414 passed (414)
```

Per-file pass counts (verbose reporter), the figures T009 must reproduce exactly:

```
     50 src/ui/composites/OverlayEditorKeyboard.test.tsx
     41 src/ui/composites/OverlayGeometryFields.test.tsx
     35 src/ui/composites/OverlayEditorUndo.test.tsx
     35 src/streaming/WhepClient.test.ts
     31 src/observability/wallAlignment.test.ts
     17 src/ui/composites/useOverlayEditHistory.test.ts
     16 src/ui/composites/CameraViewerAlignment.test.tsx
     15 src/api/problemDetail.test.ts
     15 src/api/cameras.api.test.ts
     14 src/observability/kioskLatency.test.ts
     13 src/ui/composites/placeholderAdvisories.test.ts
     11 src/observability/labelDelay.test.ts
     10 src/ui/composites/CameraViewer.test.tsx
     10 src/api/gateway.test.ts
      9 src/ui/composites/FrameCapture.test.tsx
      8 src/realtime/layoutHub.test.ts
      7 src/ui/primitives/ConfirmDialog.test.tsx
      7 src/ui/composites/OverlayEditorCharacterisation.test.tsx
      7 src/ui/composites/CameraViewerMedia.test.tsx
      7 src/ui/composites/CameraViewerCameraSwap.test.tsx
      6 src/ui/composites/OverlayLabelParity.test.tsx
      6 src/ui/composites/OverlayLabelCharacterisation.test.tsx
      6 src/ui/composites/OverlayEditorBackdrop.test.tsx
      5 src/ui/primitives/Button.test.tsx
      5 src/ui/composites/PlaceholderPreviewPanel.test.tsx
      5 src/ui/composites/CameraViewerSamplerWindow.test.tsx
      4 src/ui/composites/ErrorBoundary.test.tsx
      4 src/realtime/hubUrl.test.ts
      4 src/api/systemVariables.api.test.ts
      3 src/ui/composites/overlayLabelStyle.test.ts
      3 src/ui/composites/FormErrorSummary.test.tsx
      3 src/api/rules.api.test.ts
      2 src/observability/resilienceLog.test.ts
```

(33 files listed; the file with the remaining count — `OverlayLabelParity` and
neighbours already appear above — sums with the rest to 414. Total and per-file
breakdown were re-extracted twice, by two different greps over the raw output,
and agreed.)

**T006** (wiring landed — `package.json`, `setup.ts`, `vitest.config.ts` — but
the 38 weak assertions still in place) reproduced this table **byte-for-byte**
(`diff` empty) and `tsc --noEmit` was clean. This isolates "the wiring is safe"
from "the conversion is safe" per `tasks.md`'s T006-before-T007 ordering.

---

## 2. T008 — the edit's extent (checked before its result)

After T007 converted the 32 sites:

```
$ grep -rcE "\.(toBeDefined|toBeTruthy)\(\)" --include=*.tsx --include=*.ts apps/shared/src | grep -v ':0$'
apps/shared/src/api/cameras.api.test.ts:1
apps/shared/src/realtime/layoutHub.test.ts:1
apps/shared/src/ui/composites/OverlayEditorKeyboard.test.tsx:3
apps/shared/src/ui/composites/OverlayGeometryFields.test.tsx:1

$ grep -rcE "\.(toBeDefined|toBeTruthy)\(\)" --include=*.tsx --include=*.ts apps/shared/src | awk -F: '{s+=$2} END {print s}'
6
```

Exactly **6**, exactly matching `spec.md` §1.2's out-of-scope table:
`cameras.api.test.ts:373` (RTK Query error object), `layoutHub.test.ts:251`
(hub factory), `OverlayEditorKeyboard.test.tsx:113` (bitmask), `:567` and `:583`
(accessible-name / `aria-describedby` strings), `OverlayGeometryFields.test.tsx:489`
(`aria-describedby` string).

`git diff -U0` over the seven converted test files showed every hunk as a pure
one-token matcher swap (`.toBeDefined()`→`.toBeVisible()` or
`.toBeTruthy()`→`.toBeVisible()`), nothing else on any line moved. `git status`
confirmed no non-test `.tsx` in the diff (invariant I1).

---

## 3. T009 — post-change capture, compared to T001

```
 Test Files  33 passed (33)
      Tests  414 passed (414)
```

Per-file pass counts diffed **identical** to T001's table (byte-for-byte,
`diff` empty). **None of the 32 converted assertions went red** — no adjustment,
no `waitFor`, no query change was needed anywhere (`spec.md` §6.1 rule 4 did not
fire).

```
$ pnpm --filter @smart-sentinel-eye/shared typecheck
> tsc --noEmit
(clean, no output)

$ pnpm --filter @smart-sentinel-eye/shared lint
> eslint src --max-warnings 0
(clean, no output)
```

**T010 — workspace regression** (the other two packages, untouched by this
slice, stay green):

```
apps/shared test:        Test Files  33 passed (33)   Tests  414 passed (414)
apps/kiosk-web test:     Test Files  12 passed (12)    Tests  168 passed (168)
apps/management-web test: Test Files  38 passed (38)   Tests  330 passed (330)
```

Root `pnpm typecheck` and `pnpm lint` (covering all three packages plus `/e2e`)
both completed clean. `pnpm exec playwright test --list` still parses: 67 tests
in 30 files, unchanged.

---

## 4. T011 — the counterfactual (the only thing that proves this isn't cosmetic)

Per `spec.md` §AS-2 / `tasks.md` T011: temporarily added
`style={{ display: 'none' }}` to `ViewerOverlay`'s wrapper `<div>` in
`apps/shared/src/ui/composites/CameraViewer.tsx` (line 418 at `70b52c96`, the
`absolute inset-0 flex flex-col …` wrapper):

```diff
   return (
-    <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-black/60 text-center text-sm">
+    <div
+      className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-black/60 text-center text-sm"
+      style={{ display: 'none' }}
+    >
       <span className={clsx('font-medium', tone)}>{label}</span>
       {hint !== null && <span className="px-4 text-xs text-fg-muted">{hint}</span>}
     </div>
```

Re-ran `CameraViewer.test.tsx`. **6 of the 7 converted stream-state assertions
failed** with jest-dom's "element is not visible" message. **Corrected at
phase-6 review**: the 7th, `'Stream is offline'` at line 259, is in the same
test as the line-254 failure (`'Suspends retries while stream health is
Offline and reconnects on recovery'`) and was never reached — Vitest aborts a
test at its first throwing `expect`, so line 259 never ran. It renders from
the same `ViewerOverlay` wrapper the counterfactual perturbed (`:389+`), so it
would have failed identically had it been reached; the assertion is not
weaker than its six siblings. The failure was not sought file-wide, only "at
least one", which the spec requires — six is well past that bar:

```
 Test Files  1 failed (1)
      Tests  6 failed | 4 passed (10)

 FAIL  src/ui/composites/CameraViewer.test.tsx > CameraViewer stream session state machine > Does not claim Live until the peer connection reports connected
Error: expect(element).toBeVisible()

Received element is not visible:
  <span
  class="font-medium text-fg-muted"
/>
 ❯ src/ui/composites/CameraViewer.test.tsx:142:45
    140|
    141|     // The WHEP POST has succeeded, but media transport is not up yet.
    142|     expect(screen.getByText('Connecting…')).toBeVisible();
       |                                             ^

 FAIL  src/ui/composites/CameraViewer.test.tsx > CameraViewer stream session state machine > Leaves Live and schedules an immediate retry when the peer connection fails
Error: expect(element).toBeVisible()

Received element is not visible:
  <span
  class="font-medium text-accent-warning"
/>
 ❯ src/ui/composites/CameraViewer.test.tsx:161:47
    161|     expect(screen.getByText('Reconnecting…')).toBeVisible();
       |                                               ^

 FAIL  src/ui/composites/CameraViewer.test.tsx > CameraViewer stream session state machine > Retries when a disconnected peer connection does not recover within the grace window
 ❯ src/ui/composites/CameraViewer.test.tsx:201:47
    201|     expect(screen.getByText('Reconnecting…')).toBeVisible();
       |                                               ^

 FAIL  src/ui/composites/CameraViewer.test.tsx > CameraViewer stream session state machine > Retries rejected connections with exponential backoff capped at fifteen seconds
 ❯ src/ui/composites/CameraViewer.test.tsx:215:47
    215|     expect(screen.getByText('Reconnecting…')).toBeVisible();
       |                                               ^

 FAIL  src/ui/composites/CameraViewer.test.tsx > CameraViewer stream session state machine > Suspends retries while stream health is Offline and reconnects on recovery
 ❯ src/ui/composites/CameraViewer.test.tsx:254:47
    254|     expect(screen.getByText('Reconnecting…')).toBeVisible();
       |                                               ^

 FAIL  src/ui/composites/CameraViewer.test.tsx > CameraViewer stream session state machine > Re-establishes a real session when stream health recovers from Degraded
 ❯ src/ui/composites/CameraViewer.test.tsx:280:47
    280|     expect(screen.getByText('Reconnecting…')).toBeVisible();
       |                                               ^
```

**The same six assertions would have passed under `.toBeDefined()`** — the
element still exists in the DOM; `display:none` is exactly the class of defect
`.toBeDefined()` could never catch (spec §2's counterfactual, re-run here as
the phase-5 gate rather than taken on the spec's earlier word).

Reverted with `git checkout -- apps/shared/src/ui/composites/CameraViewer.tsx`;
confirmed no diff (`git status --porcelain` empty for the file and for the whole
tree). Re-ran `CameraViewer.test.tsx`:

```
 Test Files  1 passed (1)
      Tests  10 passed (10)
```

And the full `apps/shared` suite once more, to confirm the revert left nothing
behind:

```
 Test Files  33 passed (33)
      Tests  414 passed (414)
```

`git status --porcelain` at the repo root: empty.

---

## 5. What this slice does not prove (spec §10 — stated, not omitted)

- It does **not** make the suite catch Tailwind-hidden text — jsdom loads no
  stylesheet, so `class="hidden"` computes to the element's UA default instead
  (`inline` for the overlay's `<span>`s, `block` for a `<div>`), not `none`,
  under test. Measured against the pinned jsdom 30.0.1 in this worktree.
- It does **not** make the suite catch `aria-hidden` — the string does not
  occur anywhere in the jest-dom 7.0.1 matcher bundle.
- It does **not** make the suite catch a covered or off-screen node — jsdom
  computes no layout, so there is no occlusion or z-order check.
- It does **not** prevent the weak matcher returning tomorrow — nothing fails
  the build on a future `.toBeDefined()` (§7/§12's recommended follow-up:
  `eslint-plugin-jest-dom`'s `prefer-in-document`, filed separately, new
  behaviour, its own red).

## 6. §IV — latency budget

**No leg's timing is touched.** No production source file changed except the
single-line, fully-reverted counterfactual perturbation in §4 above, which was
never part of the committed diff. The seven `CameraViewer.test.tsx` assertions
are test-side observability of the §IV path (*Event → overlay state* ≤ 200 ms,
*SFU → kiosk decode* ≤ 120 ms) — this slice strengthens what they can detect,
not any budget, buffer, or timing.

---

## 7. Definition of done — `plan.md` §7

| # | Item | Status |
|---|---|---|
| 1 | `pnpm --filter @smart-sentinel-eye/shared test` green, per-file counts identical to `70b52c96` | ✅ §1, §3 |
| 2 | `pnpm --filter @smart-sentinel-eye/shared typecheck` clean | ✅ §3 |
| 3 | `pnpm --filter @smart-sentinel-eye/shared lint` clean, `--max-warnings 0` | ✅ §3 |
| 4 | I1–I6 all hold | ✅ §2, §3 (I1 no non-test `.tsx`; I2 one-token hunks; I3 exactly 6; I4 implied, no non-element `.toBeVisible()` thrown; I5 identical pass counts; I6 typecheck clean) |
| 5 | AS-2 counterfactual run, failure quoted, reverted | ✅ §4 |
| 6 | `pnpm -r --filter "./apps/**" test` green | ✅ §3 |
| 7 | `pnpm format:check` clean (`ci.yml`'s frontend job) | ✅ phase-6 review — "All matched files use Prettier code style!" |
| 8 | `pnpm test:guards` clean (`ci.yml`'s frontend job, part of the root `test` script) | ✅ phase-6 review — 60/60, including the test-collection guard (`setup.ts` is not test-shaped, doesn't trip it) |
