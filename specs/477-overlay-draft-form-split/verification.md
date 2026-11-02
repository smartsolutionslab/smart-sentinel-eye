# Verification 477 — `OverlayDraftForm` is broken up by responsibility

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). Not observed in a browser; the evidence is
the family under jsdom plus the new gap files.

## Size, as measured (it is a decomposition: the total rises)

|                                  | Before | After                 |
| -------------------------------- | ------ | --------------------- |
| `OverlayDraftForm.tsx`           | 627    | **295**               |
| the component                    | 476    | **194** (−59 %)       |
| hook calls in the component      | 21     | **10**                |
| **File total (form + 13 units)** | 627    | **793** (+166, +26 %) |

Units (`overlayDraftForm/`, total / code lines): `useElementSelection` 93 / 66, `useOverlayDraftFormState` 68 / 39,
`useSaveGate` 66 / 24, `useResolvedPreview` 38 / 17, `useDraftChain` 33 / 11, `describeSaveRefusal` 36 / 23,
`useUnmountMutationReset` 31 / 9, `OverlapStatus` 26 / 24, `TextErrorAlert` 23 / 18, `useLatestAccessToken` 24 / 8,
`useCameraChoices` 21 / 5, `useElementsChangeNotifier` 15 / 12, `overlayEditTarget` 24 / 7. Only `useElementSelection`
(66 code lines) is over the issue's 50. The measure-and-ask (T009) was answered "continue" with the first four units done.

**Not extracted:** `ElementEditorPane`. It would take 14 props (`control`, `editorKey`, `selectedIndex`, `elements`,
`readOnly`, `canvasWidthPx`, `canvasHeightPx`, `getToken`, `backdropCameraIdentifier`, the three preview values,
`cameras`, `onColourValidityChange`) to move a block that is itself a prop list: the signature would be as long as the JSX
it removes. `onSubmit` also stays in the form: moving it needs nine inputs, over the plan's stop-and-ask bar, and it is
the one piece of the component that is a composition of the others.

## Characterisation (FR-001)

- 11 family files: **89 test names with status identical** before and after; **296 assertion lines identical**; `git diff
origin/develop...HEAD` on them is empty. The only test files added are `OverlayDraftFormHost.test.tsx` (19),
  `OverlayDraftFormProps.test.tsx` (9) and `overlayDraftForm/TextErrorAlert.test.tsx` (2).
- Whole `management-web` suite: **727 passed** (697 + 30), ~138 s; `eslint --max-warnings 0`, `tsc --noEmit`, `prettier
--check` clean; `Architecture.Tests` **3254 / 3254**.
- `eslint-disable`: exactly **three**, one per unit, rule and justification unchanged — `useLatestAccessToken`
  (`react-hooks/refs`), `useUnmountMutationReset` (`react-hooks/exhaustive-deps`), `useElementsChangeNotifier`
  (`react-hooks/refs`). The form has none.

## Probing before moving: 30 of 59 deliberate breaks went unnoticed by the family

Each break was one named, exactly-once substitution into the unsplit form (`probe477.cjs`), the family run, the file
restored from git. Matrix: 29 noticed, **30 not**. The 30: chain read `data` for `currentData` and
`refetchOnMountOrArgChange`; the unmount reset (none / edit only / create only); the re-seed; `onDone` after a create;
the form reset after a successful edit and a successful create; removal picking the first row, no reselect on removing the
selected row, the `editorKey`, `addElement` ignoring `readOnly` and `MAX_ELEMENTS`, `removeElement` ignoring `readOnly`
and the last-row guard, `moveElement` ignoring `readOnly`; a stale `onElementsChange`; `isResolving` ignoring the debounce
and `data` for `currentData` in the resolve query; the camera error dropped; the `notDraft` wording; `preventDefault` on a
blocked submit; `onChange` live while `readOnly`; `aria-busy` on both wrappers; the list's `readOnly`; the initial camera;
the canvas width; the `Controller` `key`. Every `readOnly` gate was unpinned, as predicted.

**28 tests in two new files pin 27 of them**, each green on the unsplit form and proved able to fail (the failure message
read, not counted). Examples: A2 `expected "vi.fn()" to be called with arguments: [ …(2) ]`; C1–C3 `expected "vi.fn()" to be
called 1 times, but got 0 times`; D6 `expect(element).toHaveValue(Seeded)`; E7 `expected [ 'B' ] to deeply equal [ 'A',
'B' ]`; G5 `expected '23.4' to be '{{pressure}}'`; K1 `expect(element).toHaveValue(Seeded)`.

### Defended jointly, recorded rather than pinned alone

| Break                                                           | Result                                                                                                                                                                           |
| --------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A1 `data` alone, in the family + gap files                      | still green, because the Save gate's `chainFetching` term blocks Save in the moments the two fields differ (as in spec 476)                                                      |
| A1 `data` **and** no `chainFetching` term                       | 5 failed, incl. "Keeps Save closed and unsubmittable while the retained result is stale and the read is in flight" (checked on the final units: `useDraftChain` + `useSaveGate`) |
| A1 `data` alone, on the final tree, **whole `overlays` folder** | 1 failed: `OverlayEditPage.test.tsx` "Save success navigates to /overlays" (10 s timeout) — a page-level test notices it; the family's probe run did not include that file       |
| K10 `Controller` without `key` alone                            | green: the inner `<OverlayEditor key={editorKey}>` still remounts it                                                                                                             |
| K10 **and** the inner `key`                                     | "Remounts the editor when the operator explicitly selects another layer" fails (`expected 1 to be 2`)                                                                            |

**Caveat on E5–E9:** pinned through a stub seam, calling the form's callbacks directly. In the real tree
`OverlayLayerList` repeats the guards (`readOnly`, `canAdd`, `canRemove`), so these breaks are not visible through it.
No double break was run against that shared file.

## Each unit, against the break that matters there

After each move the matching break was applied to the **new unit** and a test failed there: `useLatestAccessToken` (stale
token: `expected 'operator-access-token' to be 'operator-access-token-after-silent-re…'`); `useUnmountMutationReset`
(each half); `useElementsChangeNotifier` (stale callback, no notify); `describeSaveRefusal` (stale / notDraft / nameTaken
wording, `offerReload`); `useDraftChain` (`refetchOnMountOrArgChange`); `useResolvedPreview` (skip, `settled`,
`isResolving`, `data`); `useCameraChoices` (skip, error); `useElementSelection` (nine breaks, 1/4/1/1/1/1/1/2/4
failures); `useOverlayDraftFormState` (re-seed, `defaultValues` deps, the live-values fallback: 1/1/10); `useSaveGate`
(its eight terms: 1/1/1/3/2/1/4/36); `OverlapStatus`, `TextErrorAlert`.

## One gap found during extraction

Removing `role="alert"` from the text-error paragraph was **not noticed** by anything (the tests address it by test id).
`TextErrorAlert.test.tsx` now pins it, and the empty case; both proved able to fail.

## Slips and judgements, kept visible

- **The branch was cut from a stale local `develop`** (the #3217 work was not in it, so the layouts hook this plan
  compares with did not exist locally). Rebased onto `origin/develop` before the first extraction; the overlays code had
  not moved, so the probe matrix stands.
- **Corrected from spec.md:** it said "no second caller is created". `layouts/layoutEditor/useLatestAccessToken.ts` has the
  same body as this feature's. Sharing it needs an edit to the layouts feature (outside FR-008); recorded as a follow-up.
- **Effect order preserved**, not merely equivalent: the notifier effect still runs before the re-seed effect, because
  `useElementsChangeNotifier` is called inside `useOverlayDraftFormState` just ahead of the re-seed `useEffect`.
- The first background probe run died after one break and left the form modified; the second was stopped for memory with
  `B1-stale-token` applied. Both restored with `git checkout` before anything else ran; the matrix was then run in batches
  of ten, one vitest at a time.
- One `sed` counterfactual (`? CONFLICT_FALLBACK` → `'x'`) stayed green: the tests assert the stale fallback is not the
  retry wording, not its text. The retry-wording variant fails three tests.
- A `// @vitest-environment jsdom` header was not needed in `management-web` (jsdom is its default).

## Follow-ups (not done here)

- Share `useLatestAccessToken` through `@smart-sentinel-eye/shared/hooks`.
- `useSubmitGuard(saveBlocked, onSubmit)` in `useSaveGate` here and in the layouts unit.
- An `OverlayLayerList` double break for E5–E9 if those guards should be pinned independently.

## QA (`/code-review medium`): 10 findings, 1 fixed

Fixed: `useSaveGate`'s `currentChain` was typed `unknown`, so a `null` would have passed the compiler and left the gate open;
it is now `{ version: number } | undefined` (`tsc`, `eslint`, `prettier`, 197 overlays tests green).

Accepted, in writing:

- **Third copy of the latest-token hook; `useSaveGate` and the mutation-reset hooks duplicated across layouts and
  overlays.** True, and already recorded as follow-ups (shared `useLatestAccessToken`; `useSubmitGuard`). Sharing means
  editing the layouts feature, outside FR-008. The reset hooks differ in trigger (mount-once vs `open`), so a shared helper
  is a design question for its own issue. The inline original in `CameraDetailPage` is a third site to fold in then.
- **The mount-once reset closes over the first render's states (stale `reset` risk).** Moved verbatim (the suppression is
  #2118's judgement); the unit lives in this form's private folder and takes the two states the form already holds.
- **`useCameraChoices`'s `{ skip: false }` and its fresh result object each render.** The option is pinned by tests
  (`Makes a camera-choices request…`, break H1) and the inline `cameras={{…}}` literal built a new object every render
  before the split too: no change.
- **`elements` rebuilt each render; `addElement` using the render's `fields.length`.** Both pre-existing and moved
  verbatim; a refactor that also changed them would be two issues.
- **Two findings about `layouts/layoutEditor/` (`cameraNoticeId`, `useTileView`'s `assignOverlay`).** Spec 476, already
  merged, out of scope here.
