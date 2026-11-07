# Spec 484 — `useBackdropCapture` returns three groups, not eleven values

**Feature Branch**: `484-backdrop-capture-surface`
**Created**: 2026-11-07
**Status**: Draft
**Issue:** #3225 (feature-level; follow-up recorded by the frontend refactoring survey, #3190–#3198, via #3191 / `OverlayEditor`).
**ADRs:** ADR-0036 (smallest change, no speculative generality), ADR-0139 / ADR-0144 (phase 4a), ADR-0169 (the frame grabber is
rendered by the caller), ADR-0037 (phases).
**Refs:** spec 147 (frame capture), spec 478 (the `OverlayEditorCapture` gap-fill), FR-010–FR-014 / FR-017.
**No new ADR.** A narrowing of an internal hook's surface.
**Latency budget (constitution §IV): NOT on the path.** `OverlayEditor` is the operator's authoring canvas. A search of `kiosk-web`
and `management-cameras` finds no importer; the two importers are `management-web`'s `OverlayDraftForm` and `TextErrorAlert`. Nothing
here sits between event arrival and overlay render on the wall; no leg is touched. The PR says so rather than leaving the section
blank.
**Phase 4a colour:** behaviour-preserving → **characterisation**. Existing suites pass **unmodified**; an assertion that has to be
edited means behaviour moved: block (CLAUDE.md). A refactor that is also a bug fix is two issues.

## Problem

`useBackdropCapture(initialCamera)` (`apps/shared/src/ui/composites/overlayEditor/useBackdropCapture.ts`) returns **eleven** values,
and `OverlayEditor.tsx` destructures all eleven on one line-per-name:

| Returned             | Used by the editor for                                                    | Where                                                         |
| -------------------- | ------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `backdrop`           | the canvas background **and** `BackdropControls`                          | `canvasBackgroundStyle(backdrop, capturedFrame)`; `backdrop=` |
| `setBackdrop`        | `BackdropControls` (`onBackdropChange`)                                   | one use                                                       |
| `capturedFrame`      | the canvas background **and** `hasCapturedFrame={capturedFrame !== null}` | two uses                                                      |
| `selectedCamera`     | `BackdropControls`                                                        | one use                                                       |
| `handleCameraChange` | `BackdropControls` (`onCameraChange`)                                     | one use                                                       |
| `captureState`       | `BackdropControls`                                                        | one use                                                       |
| `handleCapture`      | `BackdropControls` (`onCapture`)                                          | one use                                                       |
| `cancel`             | `BackdropControls` (`onCancelCapture`)                                    | one use                                                       |
| `activeCamera`       | whether and under which key the frame grabber is mounted                  | two uses                                                      |
| `handleCaptured`     | the grabber's `onCaptured`                                                | one use                                                       |
| `fail`               | the grabber's `onFailed`                                                  | one use                                                       |

(Premise check on `develop` @ `e9b973a3`, 2026-11-07: the issue is right that there are eleven. It says "group **or narrow** what the
editor actually consumes"; the table shows the editor consumes **all eleven**, each at least once, so nothing can be dropped. The
honest move is to **group**: the eleven values serve three different consumers, and the editor currently has to know which belongs to
which.)

The three consumers: the **canvas** (`backdrop`, `capturedFrame`), the **`BackdropControls`** (eight values, four of which are
renamed on the way in: `setBackdrop`→`onBackdropChange`, `handleCameraChange`→`onCameraChange`, `handleCapture`→`onCapture`,
`cancel`→`onCancelCapture`), and the **frame grabber** (`activeCamera`, `handleCaptured`, `fail`).

### What the issue does not settle

1. **The grouping.** Candidates: three named groups (canvas, controls, grabber); or two (what `BackdropControls` takes, what the
   grabber takes) with the canvas reading from the first. The plan decides, with the editor's actual JSX as the test of fit.
2. **Identity.** The handlers are `useCallback`s with stable identity today; a group object built per render is not stable. Whether
   anything downstream depends on that (`BackdropControls`, the `Fragment key`, the grabber's callbacks) is for the probe, not for
   this spec to assume.
3. **Does the existing suite notice a break?** `OverlayEditorCapture.test.tsx` (six cases: picker starts on `initialCamera`; no
   capture while no camera; grabber released on a mid-capture camera change; failed alert cleared on a different camera; grabber
   released on delivery; grabber released if the token goes away) plus the editor family exist. Which behaviours they pin, and which
   they do not (the backdrop switching to `captured` on delivery, a captured frame painted on the canvas, `Capture` refusing with no
   camera chosen, `cancel` idempotent against `idle`), is for the probe to answer.
4. **No other consumer.** `useBackdropCapture` is imported only by `OverlayEditor.tsx` (grep, 2026-11-07); its return type is
   inferred, not exported.

## Scope

**In:** `useBackdropCapture` returns grouped values; `OverlayEditor.tsx` destructures and spreads them; a one-line type if the plan
needs one.

**Out (explicitly):** `BackdropControls`' own props (its interface is a public composite; a prop rename is a different change);
`useFrameCapture`; `canvasBackgroundStyle`; what a backdrop is or how a frame is captured; the `getToken`-gating comment at
`OverlayEditor.tsx:380-388` ("without reshaping the props into one object (out of scope for this issue)"); any new suppression; any
change to a `data-testid`, label or accessible name.

## User Scenarios & Testing

### User Story 1 (P1) — The editor captures and paints exactly as before

As the operator authoring an overlay, I pick a camera, press Capture, and the captured frame becomes the canvas background; changing
the camera mid-capture, cancelling, or a failed capture behaves as it did. As the maintainer I can prove it.

**Independent test:** the existing capture and editor tests pass with no line edited.

```gherkin
Scenario: a delivered frame becomes the backdrop
  Given a capture in flight
  When the grabber delivers a frame
  Then the canvas background shows that frame
  And the grabber is released
  And the backdrop choice is "captured"

Scenario: changing the camera mid-capture releases the grabber
  Given a capture in flight
  When another camera is picked
  Then the old grabber is released and no frame from it is ever applied

Scenario: Capture does nothing with no camera chosen
  Given no camera is selected
  When Capture is pressed
  Then no capture starts and no grabber is mounted

Scenario: the existing tests are unchanged
  Given OverlayEditorCapture and the OverlayEditor family tests
  When they are run after each switch
  Then every test passes and git diff shows none of them edited
```

### User Story 2 (P2) — The editor no longer lists eleven names

```gherkin
Scenario: one place says who gets what
  When the change is done
  Then OverlayEditor destructures at most three values from the hook
  And the hook's return is the only place that says which value serves the canvas, the controls and the grabber
```

### Edge cases

- **`backdrop` is read twice** (canvas and controls) and **`capturedFrame` twice** (canvas and `hasCapturedFrame`); the grouping must
  not make either stale or give the two readers different values.
- **`Fragment key={activeCamera}`:** the key's value, not just its presence, releases the old grabber; it must stay the active camera.
- **`getToken` gating:** `BackdropControls`' `getToken` is still passed conditionally by the editor (the all-or-nothing degradation,
  FR-017); the hook knows nothing of it.
- **Hook order:** `useBackdropCapture` is called at the same position among the editor's hooks.

## Requirements

- **FR-001** Every existing test passes after each commit; none is modified.
- **FR-002** `useBackdropCapture` returns its eleven values as grouped objects; the groups follow the three consumers (canvas,
  `BackdropControls`, frame grabber), the exact shape decided in the plan.
- **FR-003** `OverlayEditor.tsx` takes at most three names from the hook and uses them where the eleven were used; its rendered output
  is unchanged (same DOM, same props reaching `BackdropControls`, same grabber render call).
- **FR-004** Behaviour is unchanged: backdrop switches to `captured` on delivery and the grabber is released; changing the camera
  cancels; Capture with no camera does nothing; `cancel` stays idempotent against `idle`.
- **FR-005** No `BackdropControls` prop, `data-testid`, label or accessible name changes; no suppression is added.
- **FR-006** Each commit builds and passes the affected suites on its own (bisect).
- **FR-007** `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check` clean for `shared`, `management-web`;
  `Architecture.Tests` pass (they scan `.tsx`) or CI is named as the evidence if they cannot run locally.
- **FR-008** No change outside `useBackdropCapture.ts`, `OverlayEditor.tsx`, new test files, `specs/484-…` and
  `.specify/feature.json`.
- **FR-009** The PR states that no §IV leg is touched.

## Success Criteria

- **SC-001** The covering suites: same test names before and after, all passing, `git diff` on them empty.
- **SC-002** The editor's destructuring of the hook drops from eleven names to at most three (counted by search).
- **SC-003** For each concern (initial camera; Capture refuses with no camera; camera change cancels; delivered frame sets the
  backdrop to `captured` and keeps the frame; the frame paints the canvas; `cancel` releases; `fail` releases; the grabber key follows
  the active camera; backdrop choice reaches the controls), a deliberate break is noticed by a named test; where nothing notices, a
  new test in a **new file** is written first, green on today's code, proved able to fail (message read), and kept; equivalents and
  jointly defended behaviours are recorded with evidence.
- **SC-004** Line counts reported before/after for the touched files, with the total.

## Assumptions

- Grouping into objects does not change what React renders: the groups are plain objects spread or read by the editor in the same
  render; whether any needs memoising is decided by the probe on identity, not assumed.
- `BackdropControls`' prop names are fixed (a public composite); the hook may return its controls group already in those names, so the
  editor spreads it instead of renaming four values by hand.

## Dependencies

Independent of #3226. Builds on #3191 (the `OverlayEditor` split that created the hook) and spec 478's gap-fill.
