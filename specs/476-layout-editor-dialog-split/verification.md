# Verification 476 — `LayoutEditorDialog` is broken up by responsibility

Phase 5 (ADR-0037). Observed on branch `476-layout-editor-dialog-split` on 2026-11-01, base `develop` after #3215. Phase 4a:
**characterisation** — the seven existing `LayoutEditorDialog*.test.tsx` files unmodified, plus four new test files for behaviour the
family left unpinned, each written first and seen green on the unsplit dialog. No red test: no new behaviour.

## Before (T001, unsplit tree)

| Record | Value |
|---|---|
| The seven family files | 50 tests, 148 assertion lines, all passing |
| `LayoutEditorDialog.tsx` | 500 lines; the component 444; **21 hook calls** (the issue said 11); one `eslint-disable` (`react-hooks/refs`) |
| Whole `management-web` suite | 90 files, 684 tests, 99.1 s |

## The probes (T002): which deliberate breaks does the family notice?

Each break was applied once to the unsplit dialog, the family run, and the dialog restored from git. **Before** = the seven existing files;
**after** = those plus the four new files (63 tests).

| Break | Before | After | Caught by |
|---|---|---|---|
| B1 reset only `createState` on close | caught (1) | caught (1) | "Does not carry a refused layout edit banner over…" |
| B2 reset only `editState` on close | caught (1) | caught (1) | "Does not carry a refused layout create banner over…" |
| F1 Save gate drops `isLoading` | caught (1) | caught (1) | `…SaveGate` focus test |
| **F2 Save gate drops `knownCameras.size === 0`** | **none** | caught (1) | `…Gaps` "Keeps Save unavailable until…" |
| F3 drops `chainFetching` | caught (3) | caught (3) | `…SaveGate`, `…ChainRetention` |
| F4 drops `chainFailed` | caught (1) | caught (1) | `…SaveGate` refused re-read |
| **C1 filter not cleared on close** | **none** | caught (1) | `…CameraFilter` "Starts with an empty filter again…" |
| C2 merge never runs | caught (15) | caught (16) | `…Retention`, `…ChainRetention` |
| **C3a "Searching…" removed** | **none** | caught (1) | `…CameraFilter` |
| **C3b "could not be loaded" removed** | **none** | caught (1) | `…CameraFilter` |
| C3c "No camera matches" removed | caught (1) | caught (1) | `…Retention` |
| **C3d "matches so far — incomplete" changed** | **none** | caught (1) | `…CameraFilter` |
| **C4 `aria-live` / `aria-describedby` removed** | not probed | caught (1 each) | `…CameraFilter` "Announces its status politely…" |
| D1 re-seed effect dropped | caught (1) | caught (1) | "Does not carry a refused layout edit banner over to a different draft" |
| **E1 stale-conflict fallback wording** | **none** | caught (1) | `…Gaps` "Falls back to the conflict wording…" |
| A1 `data` instead of `currentData` | none | **none** | see below |
| **A2 no `refetchOnMountOrArgChange`** | **none** | caught (1) | `…ChainRead` "Reads the chain again when the same layout is reopened…" |
| **G1 stale token (ref not refreshed)** | **none** | caught (1) | `…Token` "Resolves the latest access token…" |

**Nine breaks went unnoticed by the family; eight are now pinned, each by a test that fails for the right reason (messages below).**
The one that remains uncaught on its own is A1, and that is a finding, not an omission.

### A1: `currentData` is defended jointly, so it cannot be pinned alone

`LayoutEditorDialogChainRetention.test.tsx`'s header says its third case "actually discriminates `data` from `currentData`" (spec 153).
It no longer does: reading `data` instead of `currentData` leaves **all 63 tests green**. Reason, checked by double breaks:

| Break | Result |
|---|---|
| A1 alone (`data`) | 63 passed |
| A1 **and** F4 (no `chainFailed` term) | 2 failed: the refused-re-read case and "Never submits the previous layout's version when the new layout's own chain read fails outright" |
| A1 **and** F3 (no `chainFetching` term) | 5 failed, including "Never lets Save read or submit the previous layout's version while the new one is still loading" |

When the two fields differ, the gate's `chainFetching` term (while loading) or `chainFailed` term (after a failed read, added by spec 160 after
spec 153's comment was written) blocks Save anyway. So `currentData` is real defence in depth with no observable consequence of its own. It is
protected here by a **verbatim move** (the line is unchanged apart from the hook taking the identifier) and by the two double-break results above, not
by a test of its own. The stale comment in the ChainRetention header is left as it is (a test file; FR-001) and recorded here.

## The new tests (T003/T004), each proved able to fail (messages read)

| File (tests) | Break | Failure |
|---|---|---|
| `…CameraFilter` (5) | no "Searching…" / no "could not be loaded" / sentence changed / no `aria-live` / no `aria-describedby` / filter not cleared | `Expected element to have text content` / `…attribute` / `…value` (each names the missing sentence, attribute or value) |
| `…Gaps` (4) | Save gate without the camera term | `Expected the element to have attribute: aria-disabled="true"` |
| | always the retry wording | `expected 'Could not save the layout. Try again.…' to contain 'Someone else changed this while you w…'` |
| `…Token` (3) | token ref not refreshed | `AssertionError: expected 'token-1' to be 'token-2'` |
| `…ChainRead` (1) | no `refetchOnMountOrArgChange` | `AssertionError: expected 1 to be 2` (the second request never happens) |

## Characterisation after the split (T016)

| Check | Result |
|---|---|
| Family names with status | 50; `diff` against before **empty** |
| Family assertion lines | 148; `diff` **empty** |
| `git diff origin/develop...HEAD` on the seven existing test files | **empty** — none edited (FR-001) |
| New test files | 4 (13 tests), all passing |
| Whole suite | 94 files, **697** tests (684 + 13), all passing; **102.4 s** vs 99.1 s |
| `eslint-disable` across the dialog and `layoutEditor/` | exactly **one**, `react-hooks/refs`, in `useLatestAccessToken.ts` (SC-003) |
| Distinctive comments ("`currentData`, never `data`", "contents, not on the array", "BOTH mutation states", "Every camera seen so far", "The order matters", …) | each **once**, in a unit; **zero** in the dialog |
| `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` | clean |
| `dotnet test tests/Architecture.Tests -c Release` | 3254/3254 |
| Files changed outside the allowed set | none |

After each extraction the key break was applied to the **new unit** (not the old location) and the matching test failed there:
`useResetMutationsOnClose` (reset only `editState` → "…create banner…"), `describeCameraFilter` (3 sentence breaks → 1 each),
`useKnownCameras` (merge never runs → 16), `useCameraPicker` (filter clear → 1), `CameraFilterField` (`aria-live`, `aria-describedby` → 1 each),
`useLayoutChain` (no refetch option → 1; `data` alone → 0, as A1), `useLayoutEditorForm` (no re-seed → 1), `useSaveGate` (each of four terms → 1, 1, 3, 1),
`useLatestAccessToken` (stale token → 1).

## The size, as measured (the line total rose, as the plan said it would)

| | Before | After |
|---|---|---|
| `LayoutEditorDialog.tsx` | 500 | **189** |
| the component | 444 | **144** (−68 %) |
| hook calls in the component | 21 | **11** |
| **File total (dialog + 10 units)** | 500 | **712** (+212, +42 %) |

| Unit | Lines | Code lines | Inputs |
|---|---|---|---|
| `useLatestAccessToken` | 11 | 8 | 0 |
| `useTileView` | 14 | 11 | 1 |
| `useResetMutationsOnClose` | 23 | 12 | 3 |
| `useLayoutChain` | 39 | 11 | 1 |
| `describeCameraFilter` | 42 | 28 | 1 object (8 fields) |
| `useKnownCameras` | 42 | 16 | 1 |
| `CameraFilterField` | 66 | 59 | 1 object (7 props) |
| `useSaveGate` | 93 | 33 | 1 object (7 fields) |
| `useLayoutEditorForm` | 94 | 81 | 1 object (5 fields) |
| `useCameraPicker` | 99 | 51 | 1 |

The issue's bar is "each extracted unit under 50 lines where the logic allows". **Three units are over it in code lines**: `useLayoutEditorForm`
(81: its `onSubmit` is ~30 and the input type and imports ~25), `CameraFilterField` (59, almost all JSX) and `useCameraPicker` (51, of which the 14-line return
object is the largest part). The comments that moved with the code make the *line* counts larger still; they are kept on purpose. No unit needed more than the plan's
8-input limit, so neither stop rule fired.

## The decision gate (T009)

After four extractions (component 444 → 379, file total 500 → 557) the maintainer was asked whether to continue with the wider ones, with the
measurements above and the probe work already added. The answer was to continue.

## What went wrong on the way (kept visible)

- **My first hook count was wrong again** (16 for the component): the regex skipped hooks with generic arguments (`useState<…>(`, `useForm<…>(`, `useMemo<…>(`).
  The count of 21 comes from listing them. The issue's 11 is, coincidentally, exactly the count now.
- **My first batch of `grep` probes searched for nothing**: I used an escaped pipe under `-E`, which matches a literal pipe, so five phrases "appeared in no test" for the wrong reason.
  Redone with proper alternation; the conclusion (no test mentions them) held for `Searching`, `matches so far`, `aria-live`, `getToken`.
- **`cameras.count` lost its type narrowing** when `camerasTruncated` stopped being a local `const` derived from `cameras` (TypeScript narrowed through the alias).
  `cameras?.count` renders the identical text and was applied in the dialog, then carried as a prop into `CameraFilterField`.
- **Import orphans were found by checking uses, not assumed**, and checked per move (`useAuth`, `useCallback`, `useRef`, `useEffect`, `CameraSummary`, `skipToken`, …);
  nothing was removed that was still used (the lesson of #3198).
- **The plan expected two new test files; the probes needed four** (and a different split of cases than planned): the plan's R2 said the probes decide, and they did.
- **My first attempt to write this note failed on shell quoting** (a long heredoc); the note was rewritten with the file tool. No content was lost.

## Not observed

- CI on Linux: the first run is the PR's.
- The dialog in a browser; the evidence is the family under jsdom plus the four new files.
- `currentData` vs `data` alone: not observable through the dialog (A1).
