# Implementation Plan: `LayoutEditorDialog` is broken up by responsibility

**Branch**: `476-layout-editor-dialog-split` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/476-layout-editor-dialog-split/spec.md`

## Summary

Move the seven concerns of the 444-line `LayoutEditorDialog` component into small hooks and two sub-components **next to the
file**, one extraction per commit, each green on its own. First, **find what the covering family does not pin** and write
characterisation tests for it **in new files** (the seven existing files must stay unmodified). Behaviour-preserving, so
**characterisation** (ADR-0139/0144 phase 4a).

**Sizing, stated before building (the lessons of #3190 and #3198).** This is a decomposition: the total line count will not fall
(signatures and imports are added). The value is a component that reads as a list of what it composes and units under the
issue's 50-line bar. ADR-0084's limit is recorded as not enforced, so no failing gate is being fixed. The plan has a
**measure-and-ask step** after the first real extraction (T-D1).

## Technical Context

- **Stack**: TypeScript, React 19.3, React Hook Form + Zod, RTK Query, Vitest + Testing Library, `apps/management-web` (jsdom).
- **Baseline (2026-11-01, `develop` after #3215):** `LayoutEditorDialog.tsx` 501 lines (component 444, 21 hook calls); family
  7 files / 2,757 lines; whole `management-web` suite 684 tests / ~99 s.
- **Touches**: new files in `apps/management-web/src/features/layouts/`, the dialog, and new test files for the gaps. No
  existing test file, no caller, no `shared`, no config.
- **Latency budget**: none.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | Characterisation: the family unmodified, plus gap tests written first and seen green on the unmigrated tree. |
| ADR-0036 — smallest change, no speculative generality | Nothing moves to `apps/shared` (no second caller); each unit is used once, which is the point of a decomposition and is stated, not hidden. |
| ADR-0084 (50 LOC, not enforced) | Used as the issue's bar for extracted units "where the logic allows". |
| Lane may not weaken a gate | No test edited; the one `react-hooks/refs` suppression (#2118) is kept and isolated, none added. |
| No new ADR | Decomposition inside existing conventions. |

## Research (decisions that shaped the plan)

### R1 — What pins what (read from the family's test names, then probed)

| Concern | Pinned by | Gap? |
|---|---|---|
| A chain read, `currentData` not `data`, refetch | `…ChainRetention` (3), `…ChainRecovery` (8), `…ChainAnnouncement` (5) | none visible |
| B close resets **both** mutation states | `LayoutEditorDialog.test` "Does not carry a refused layout create/edit banner over…" (2) | none visible |
| D form, re-seed, create/edit `onSubmit` | `LayoutEditorDialog.test` create (8) and edit (3) groups | none visible |
| E stale vs retry copy | `LayoutEditorDialog.test` "Conflict copy" (3) | none visible |
| F Save gate, chain terms | `…SaveGate` (3), `…ChainRetention`, `…ChainRecovery` | `isLoading` and `knownCameras.size === 0` terms: **probe** |
| C camera picker: truncation notice, empty/loading, 250 cameras, late list, mislead | `LayoutEditorDialog.test` spec-048 groups (≈11), `…Retention` (2: retained camera, "N of M cameras match") | **filter status sentences are unpinned** (see below) |
| G tile view open/assign/close | `…TileView` (3, with `TileView` stubbed) | **`getToken` / `canEditOverlays` unpinned** |

Greps over the family (2026-11-01): `Searching`, `matches so far`, `No camera matches`, `could not be loaded`, `aria-live`,
`getToken`, `access_token`, `useCanEditOverlays` appear in **no** test; `cameras match` appears once (`Retention`, the
complete-and-filtered sentence). `LayoutEditorDialogTileView.test` stubs `TileView` and ignores `getToken`/`canEditOverlays`.

### R2 — Method for the rest: break it, see if anyone notices (mutation probing, then write what is missing)

For each concern, apply **one small deliberate break** to the unmigrated dialog (scratch, not committed), run the whole
family, and record whether any test fails **and read why**. A break nobody notices means an unpinned behaviour, and a
characterisation test for it is written **before** the extraction touches that concern. Probes, one per row: B (reset only
`editState`), the `saveBlocked` terms one at a time (drop `isLoading`; drop `knownCameras.size === 0`; drop `chainFetching`;
drop `chainFailed`), C (drop the `wasOpen` filter clear; make the merge key on array identity; swap the order of
"searching" and "nothing matched" in the status), D (drop the re-seed effect), G (read a stale token). A probe that bites is
recorded in `verification.md` and needs no new test.

### R3 — The new tests go in **new files** (FR-001 forbids editing the seven)

- `LayoutEditorDialogCameraFilter.test.tsx`: typing a fragment shows "Searching…" during the debounce, then each of the final
  sentences ("N of M cameras match.", "No camera matches “x”.", "K matches so far — the list is incomplete.", "The camera
  list could not be loaded."), the `aria-live="polite"` region, `aria-describedby` on the field, and clearing the filter on close.
- `LayoutEditorDialogToken.test.tsx`: with `TileView` stubbed to expose the `getToken` it receives, the callback resolves the
  **latest** token after a re-render with a new one (the `react-hooks/refs` behaviour), and `null` with no auth provider.
- Any further gap found by R2.

Each is written first and **seen green on the unmigrated tree**, then proved able to fail by a break in the dialog (and the
failure message is read, not just counted).

### R4 — The units (names are proposals; each ≤ 50 lines where the logic allows)

| Unit | Kind | Holds | Concern |
|---|---|---|---|
| `useLatestAccessToken()` → `getToken` | hook | `useAuth`, the ref, the one `react-hooks/refs` suppression and its justification | G |
| `useResetMutationsOnClose(open, createState, editState)` | hook | the close effect, deps `[open, createState, editState]` kept, its "both states" comment | B |
| `cameraFilterStatus({…})` | pure function (in its own file) | the status sentence ladder with its ordering comments | C |
| `useKnownCameras(cameras)` | hook | the accumulator and the **during-render** contents-based merge | C |
| `useCameraPicker(open)` | hook | filter text, debounce, the page-walk query, `useKnownCameras`, the `wasOpen` filter-clear adjustment, derived flags; returns what the JSX and the gate need | C |
| `useLayoutChain(editTarget)` | hook | `useGetLayoutQuery` with `currentData` and `refetchOnMountOrArgChange` and its long comment | A |
| `useLayoutEditorForm({…})` | hook | `defaultValues`, `useForm`, the re-seed effect, `onSubmit` for create and edit | D |
| `useSaveGate({…})` | hook | `saveRef`, `saveBlocked` (all six terms and the long comment), `handleFormSubmit` | F |
| `useTileView(form)` | hook | the open index, the watched grid/cells, `assignOverlay` | G |
| `CameraFilterField` | component | the truncation notice, the filter input, the status `<p>` | C |

`LayoutEditorDialog` then composes these and keeps the `Dialog`/`form` JSX, `GridDesigner`, `ChainRecoveryNotice`, the
button row and `TileView`. `E` (the two refusal strings) stays inline: 4 lines, used once, no unit earns its signature.
Everything stays beside the dialog; `LayoutEditTarget`, `LayoutEditorDialogProps` and `LayoutEditorDialog` keep their exports.

### R5 — What must not change (checked per extraction)

- **Render-time adjustments stay during render.** `useKnownCameras`'s merge and `useCameraPicker`'s `wasOpen` clear call
  `setState` in the render body, exactly as now; neither moves into an effect.
- **No hook becomes conditional**, and the relative order of the stateful hooks is kept within each moved cluster.
- **Dependency arrays are copied verbatim**, including `[open, createState, editState]` and `[defaultValues, reset]`.
- **Each comment moves with its code** and appears once (the grep of distinctive phrases is a gate).

### R6 — Not in this plan

`saveBlocked` adopting `useSubmitGuard` (a separate trivial follow-up after #3215, now merged); `OverlayDraftForm` (#3192);
`GridDesigner` / `TileView` / `ChainRecoveryNotice`; any behaviour change; `apps/shared`.

## Project Structure

```text
apps/management-web/src/features/layouts/
  LayoutEditorDialog.tsx                      (shrinks: composition + JSX)
  layoutEditor/                               (new folder beside the dialog)
    useLatestAccessToken.ts
    useResetMutationsOnClose.ts
    cameraFilterStatus.ts
    useKnownCameras.ts
    useCameraPicker.ts
    useLayoutChain.ts
    useLayoutEditorForm.ts
    useSaveGate.ts
    useTileView.ts
    CameraFilterField.tsx
  LayoutEditorDialogCameraFilter.test.tsx     (new: gap tests)
  LayoutEditorDialogToken.test.tsx            (new: gap tests)
specs/476-layout-editor-dialog-split/
```

(`layoutEditor/` mirrors the existing `tileView/` folder beside the dialog.)

## Delivery: one PR, commits that each stay green

1. Gap tests (new files), **green on the unmigrated tree**, with their breaks proved (one commit each).
2. One extraction per commit in this order, least coupled first: `useLatestAccessToken`, `useResetMutationsOnClose`,
   `cameraFilterStatus`, `useKnownCameras` → **T-D1 measure-and-ask** (component length, file total, hook count) →
   `useCameraPicker`, `CameraFilterField`, `useLayoutChain`, `useLayoutEditorForm`, `useSaveGate`, `useTileView`.
3. Task ticks and the verification note.

Every commit: the family (7 files) and the two gap files run, `eslint --max-warnings 0`, `tsc --noEmit`.

## Verification (what "unchanged" means, concretely)

Recorded on the unmigrated tree, repeated after:

- **Family:** sorted `file :: full name :: status` and sorted assertion lines of the seven files — `diff` empty; `git diff
  origin/develop...HEAD` on them empty (FR-001).
- **Gap tests:** green before and after; each seen failing for the right reason under its break (message read).
- **R2 probes** table in `verification.md`: which breaks the family caught, which needed a new test.
- **Lengths (SC-002):** the component before/after, every new unit, and the **file total** (not the component alone).
- **Greps:** exactly one `eslint-disable` for `react-hooks/refs` across the dialog and `layoutEditor/` (SC-003); distinctive
  comment phrases once ("`currentData`, never `data`", "contents, not on the array's identity", "BOTH mutation states").
- Whole `management-web` suite (684 → 684 + the new tests), durations; `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check`; `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`).

## Risks

| Risk | Mitigation |
|---|---|
| A moved `setState`-in-render becomes an effect and the cascade/loop bug returns | R5; the Retention tests plus a probe that makes the merge key on array identity. |
| A hook's `open`-driven reaction changes (B, C, D react to `open`/`editTarget`) | Deps copied verbatim; probes B and D; the "banner not carried over" tests. |
| The decomposition makes the code longer and no easier to read | Stated up front; T-D1 stop point after the first four extractions. |
| A gap test is vacuous (green because it asserts its own input) | Each proved by a break in the dialog; failure message read. |
| A custom hook ends up with so many parameters it is worse than inline | Judged at T-D1 and again at `useLayoutEditorForm`/`useSaveGate` (the widest); stop and ask if a unit needs more than ~8 inputs. |
