# Implementation Plan: `OverlayDraftForm` is broken up by responsibility

**Branch**: `477-overlay-draft-form-split` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/477-overlay-draft-form-split/spec.md`

## Summary

Move the eleven concerns of the 476-line `OverlayDraftForm` component into small hooks, one pure function and a few
sub-components **next to the file**, one extraction per commit, each green on its own. **First probe, then pin, then
move**: break each concern once in the unsplit form, see whether the 3,857-line family notices, and write
characterisation tests, in **new files**, for what it does not. Behaviour-preserving, so **characterisation**
(ADR-0139/0144 phase 4a). Reuses the method of spec 476 (`LayoutEditorDialog`), which found nine of nineteen breaks
unnoticed in its family.

**Sizing, stated before building.** A decomposition: the file total will rise (spec 476: component −68 %, total +42 %). The
value is a component that reads as a list of what it composes; ADR-0084's 50-LOC row is recorded as not enforced. There is
a **measure-and-ask step** after the first four extractions (T-D1), as in #3190, #3198 and #3193.

## Technical Context

- **Stack**: TypeScript, React 19.3, React Hook Form (`useForm`, `useFieldArray`, `useWatch`, `Controller`) + Zod, RTK Query,
  Vitest + Testing Library, `apps/management-web` (jsdom).
- **Baseline (2026-11-02, `develop` after #3217):** `OverlayDraftForm.tsx` 627 lines (component 476, 21 hook calls, 3
  `eslint-disable`); family 11 files / 3,857 lines; whole `management-web` suite 697 tests / ~102 s.
- **Touches**: new files in `apps/management-web/src/features/overlays/`, the form, and new test files for the gaps. No
  existing test file, no caller, no `shared`, no config.
- **Latency budget**: none.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | Characterisation: the family unmodified, plus gap tests written first and seen green on the unsplit form. |
| ADR-0036 — smallest change, no speculative generality | Nothing moves to `apps/shared`; each unit has one caller, which is the point of a decomposition and is stated, not hidden (see R6). |
| ADR-0084 (50 LOC, not enforced) | The issue's bar for "units where the logic allows". |
| Lane may not weaken a gate | No test edited; the three suppressions (#2118) kept and isolated, none added. |
| No new ADR | Decomposition inside existing conventions. |

No violation.

## Research (decisions that shaped the plan)

### R1 — What pins what (read from the family's test names, then probed)

| Concern | Visibly pinned by | Probe expectation |
|---|---|---|
| A chain read | `…ChainRecovery` (8), `…ChainAnnouncement` (5), `…SaveGate` (3), "The version has to be read" (2) | `currentData`/`data` likely **masked** again (the gate's `chainFetching`/`chainFailed`); `refetchOnMountOrArgChange` **unknown** |
| B token callback | `…GetToken` (2: current token, identity across renders) | pinned |
| C unmount reset of both mutation states | one test near `OverlayDraftForm.test.tsx:493` refers to it | **probe** both halves |
| D form, re-seed, create/edit `onSubmit` | create/edit/three-label tests, `OverlayEditorReseedRegression`, "Mounts the label editor once…" | probe the re-seed and the `onDone` call |
| E selection and element operations | `…Layers` (≈13), `…Shapes` (≈13), `…Review` | `readOnly` early returns **unpinned**: no test in the family mentions `readOnly` |
| F `onElementsChange` | one test, `OverlayDraftForm.test.tsx` "telling a host the live elements (spec 466)" | probe stale-callback and the effect |
| G resolve preview | `…ResolvePreview` (6) | probe skip, `settled`, `isResolving` |
| H camera choices | two "Makes a camera-choices request…" tests | probe `skip` |
| I refusal text | "Conflict copy" (3), "A conflict names what to do" (3), `…Layers`/`…Shapes`/`…Colour` reload cases | probe each wording and `offerReload` |
| J Save gate | `…SaveGate` (3), "Disables Save while the chain query…" (2), `…Colour` refused colour | probe the five terms one at a time |
| K JSX | most of the above | probe `readOnly` wrapper (`aria-busy`, `onChange` no-op), the text-error alert |

### R2 — Method: break, observe, then write what is missing (spec 476's method)

For each row, apply **one small deliberate break** to the unmigrated form (scratch, not committed), run the whole family, and
record which test failed and why, or "none". Planned probes (a script of named breaks, each asserted to match exactly once and
restored from git after):

- **A** `data` for `currentData`; drop `refetchOnMountOrArgChange`.
- **B** stale token (do not refresh the ref).
- **C** no unmount reset; reset only `editState`; reset only `createState`.
- **D** drop the re-seed effect; edit success no longer calls `onDone`; edit sends only one element.
- **E** removal picks the first remaining row; the added row is not selected (drop the `addedAtCount` adjustment); the editor
  key is always `'default'`; a refused submit does not select the invalid element; `addElement` ignores `readOnly`;
  `addElement` ignores `MAX_ELEMENTS`; `removeElement` ignores `readOnly`; `moveElement` ignores `readOnly`.
- **F** stale `onElementsChange` (ref not refreshed); the notify effect removed.
- **G** `shouldResolve` always true; `settled` always true; `isResolving` ignores the debounce.
- **H** the camera query skipped.
- **I** `notDraft` wording; `nameTaken` wording; `offerReload` only for stale; stale uses the retry wording.
- **J** drop each of `isLoading`, `colourRefused`, `currentChain === undefined`, `chainFetching`, `chainFailed`.
- **K** `onChange` not a no-op when `readOnly`; `aria-busy` removed; the text-error alert removed.

A probe that bites is recorded in `verification.md` and needs no new test. One nobody notices gets a new test **before** the
extraction touches that concern; a break masked by another term (as `currentData` was) is checked with a **double break** and,
if it cannot be pinned alone, recorded as defended jointly.

### R3 — New tests go in **new files** (FR-001 forbids editing the eleven)

Names proposed by concern, created only for what the probes find unpinned, e.g. `OverlayDraftFormReadOnly.test.tsx` (the
placeholder gating), `OverlayDraftFormUnmount.test.tsx` (the mount-once reset), `OverlayDraftFormHost.test.tsx` (the
`onElementsChange` stale-callback case), plus any other the probes name. Each is **green on the unsplit form**, then proved able
to fail by a break in it, with the failure **message read**, not just counted.

### R4 — The units (names are proposals; each ≤ 50 code lines where the logic allows)

New folder `apps/management-web/src/features/overlays/overlayDraftForm/` (mirroring the `tileView/` and `layoutEditor/` folders
that sit beside their dialogs):

| Unit | Kind | Holds | Concern |
|---|---|---|---|
| `useLatestAccessToken` | hook | `useAuth`, the ref, **suppression 1 (`refs`)**, `getToken` | B |
| `useUnmountMutationReset(createState, editState)` | hook | the mount-once cleanup, **suppression 2 (`exhaustive-deps`)**, the "BOTH, mount-once" comment | C |
| `useElementsChangeNotifier(watchedElements, onElementsChange)` | hook | the callback ref, **suppression 3 (`refs`)**, the effect | F |
| `describeSaveRefusal(error)` | pure function | `staleConflict`, `notDraft`, `nameTaken`, `backendError`, `offerReload` with their ADR-0119 comments | I |
| `useDraftChain(overlayIdentifier)` | hook | `useGetOverlayQuery` with `currentData` and `refetchOnMountOrArgChange` and its long comment | A |
| `useResolvedPreview(selectedElement)` | hook | `labelText`, the debounce, the skip-able query, `settled`, `isResolving`, `resolveFailed` | G |
| `useCameraChoices()` | hook | the camera-choices query and its destructure | H |
| `useElementSelection(…)` | hook | `chosenId`, the **render-time** `addedAtCount` adjustment, `selectedIndex`, `editorKey`, `addElement`/`moveElement`/`removeElement`, `selectFirstInvalidElement` | E |
| `useOverlayDraftFormState({…})` | hook | `defaultValues`, `useForm`, `useFieldArray`, `useWatch`, the re-seed effect, `onSubmit` for create and edit | D |
| `useSaveGate({…})` | hook | `saveRef`, `colourRefused`, `onColourValidityChange`, `saveBlocked`, `handleFormSubmit` | J |
| `ElementEditorPane`, `OverlapStatus`, `TextErrorAlert` | components | the `Controller` + `OverlayEditor` block, the overlap status list, the text-error IIFE | K |

`OverlayDraftForm` keeps the composition, the `form`/`FormField`/layer-list/`ChainRecoveryNotice`/button-row JSX and its exports.
`OverlayDraftFormFrameGrabber`, `textIndexOf` and `noop` stay where they are or move with the unit that owns them (decided at the move).

### R5 — What must not change (checked per extraction)

- **Render-time adjustments stay during render.** `addedAtCount`'s "select the row just added" `setState` stays in the render body
  of `useElementSelection`; it does not become an effect.
- **No hook becomes conditional**; the relative order of stateful hooks within a moved cluster is kept. `useForm` →
  `useFieldArray` → `useWatch` stays contiguous and in that order.
- **Dependency arrays are copied verbatim**, including C's empty array and `[defaultValues, reset]`.
- **`editorKey` and `key={editorKey}` stay as they are** (the editor must not remount on a default selection).
- **Each comment moves with its code once, and any "above"/"below" pointer is rewritten to name the new unit** (spec 476's
  QA found six stale pointers; FR-006 here says it up front). Verified by a grep for the words after each move.
- **A type-only import back into the form is avoided**: `OverlayEditTarget` is moved to a neutral file in the same commit as the
  first unit that needs it, and the form re-exports it (spec 476's QA found the cycle).

### R6 — A second caller now exists for one unit (stated, not hidden)

`layouts/layoutEditor/useLatestAccessToken.ts` (spec 476, merged as #3217) has the same body as unit B here. Two identical hooks
in two features is the textbook moment to share, but sharing means moving a unit to `apps/shared` and editing the layouts
feature, which is outside this spec's scope (FR-008) and outside a behaviour-preserving split. **Recorded as a follow-up**
(`useLatestAccessToken` → `@smart-sentinel-eye/shared/hooks`), not done here. Spec 477's "no second caller" statement is
corrected accordingly in `verification.md`.

### R7 — Not in this plan

`useSubmitGuard` for `saveBlocked`; `OverlayEditor`; callers; the suppressions' judgement; any behaviour change; `apps/shared`.

## Project Structure

```text
apps/management-web/src/features/overlays/
  OverlayDraftForm.tsx                          (shrinks: composition + JSX)
  overlayDraftForm/                             (new folder beside the form)
    overlayEditTarget.ts                        (neutral home of OverlayEditTarget; the form re-exports it)
    useLatestAccessToken.ts
    useUnmountMutationReset.ts
    useElementsChangeNotifier.ts
    describeSaveRefusal.ts
    useDraftChain.ts
    useResolvedPreview.ts
    useCameraChoices.ts
    useElementSelection.ts
    useOverlayDraftFormState.ts
    useSaveGate.ts
    ElementEditorPane.tsx
    OverlapStatus.tsx
    TextErrorAlert.tsx
  OverlayDraftForm<Concern>.test.tsx            (new: gap tests, as the probes decide)
specs/477-overlay-draft-form-split/
```

## Delivery: one PR, commits that each stay green

1. Gap tests (new files), **green on the unsplit form**, with their breaks proved.
2. Extractions, least coupled first: `useLatestAccessToken`, `useUnmountMutationReset`, `useElementsChangeNotifier`,
   `describeSaveRefusal` → **T-D1 measure-and-ask** (component length, file total, hook calls left, inputs per unit) → then
   `useDraftChain`, `useResolvedPreview`, `useCameraChoices`, `useElementSelection`, `useOverlayDraftFormState`, `useSaveGate`,
   and the three components.
3. Task ticks and the verification note.

Every commit: the family (11 files) and the new gap files run, `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check`.

## Verification (what "unchanged" means, concretely)

Recorded on the unmigrated tree, repeated after:

- **Family:** sorted `file :: full name :: status` and sorted assertion lines of the 11 files — `diff` empty; `git diff
  origin/develop...HEAD` on them empty (FR-001).
- **Probe matrix:** every R2 break, before (family) and after (family + gap files), in `verification.md`; double breaks for any
  jointly defended behaviour.
- **Gap tests:** green before and after; each failing for the right reason under its break (message quoted).
- **Per extraction:** the key break applied to the **new unit** (not the old location) fails the matching test there.
- **Lengths (SC-002):** the component, every unit (code lines and comment lines separately) and the **file total**.
- **Greps:** exactly three `eslint-disable` across the form and `overlayDraftForm/`, rule and justification unchanged (SC-003);
  distinctive comment phrases once; no stale "above"/"below" pointers.
- Whole `management-web` suite (697 → 697 + the new tests), durations; `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check`; `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`).

## Lessons carried from specs 470–476 (each cost time once)

Anchor the working directory in every command (`cd "$(git rev-parse --show-toplevel)/apps/management-web"`, print `pwd`); a
long note written with a heredoc can fail on quoting, use the file tool; `grep -E` with `\|` matches a literal pipe; the hook-count
regex must allow generic arguments (`useState<…>(`); grep for **other uses** before removing an import; format every generated
file each step; add only the step's files to a commit; a counterfactual must fail for the right reason, so read the message.

## Risks

| Risk | Mitigation |
|---|---|
| The render-time `addedAtCount` adjustment becomes an effect and the cascade/flicker returns | R5; the `…Layers`/`…Shapes` add-and-select tests plus probe E2. |
| `useFieldArray`'s regenerated row ids interact badly with a moved `chosenId` | The selection stays in one hook with `fields`; `…Layers` "Mounts the label editor once…" and `OverlayEditorReseedRegression`; probe E3. |
| `readOnly` gating is unpinned and breaks silently | Probes E5–E8 and K1–K2 find it; a new test is written first. |
| `onElementsChange` fires at a different moment after the split | F1/F2 probes; the spec-466 test. |
| The decomposition is longer and no easier to read | Stated up front; T-D1 stop point. |
| A unit needs more than ~8 inputs (`useOverlayDraftFormState`, `useSaveGate` are the widest) | Stop and ask rather than push on. |
