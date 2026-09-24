# Spec 234 — A capture that fails quietly

**Issue**: #2356 · **Branch**: `fix/2356-frame-capture-resilience-a11y` · **Phase**: 1 (Specify)
**Date**: 2026-09-24 · **Base**: `396c4fa7` (`origin/develop`, fetched 2026-09-24)
**Context**: frontend only — `apps/shared` (the overlay editor's frame capture, spec 147) and one
Playwright spec. No bounded context, no C#, no Contracts change.
**Engineer**: `frontend-engineer` · **Reviewer**: `frontend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **mixed, declared per item (§6)** — items 1 and 3 **RED**; items 2 and 4
**characterisation, observed green**, each with a recorded counterfactual.
**ADRs**: ADR-0076 (the `[resilience]` log channel), ADR-0139 (new behaviour observed red),
ADR-0144 (the lane; 4a colours), ADR-0036 (smallest change; item 5 not guessed at),
ADR-0109 (disjoint files), ADR-0150 (waiting is a condition, not a count — jsdom microtask flush),
ADR-0106 (e2e goes through the gateway)
**Constitution**: §IV — **N/A**. The overlay editor is the management console's authoring
surface, not the event→overlay path; the capture opens its own WHEP session and never touches a
wall. §Testing binds (both obligations, §6). §II does not bind (no domain model).
**New ADR needed**: **No** (§9).

---

## 1. The premise, re-checked against `396c4fa7`

Every file the issue names was re-read. **Items 1–4 hold.** Line numbers drifted (spec 157's
`currentData` edit added lines above the effect); nothing else moved.

| # | Issue claim | Status at `396c4fa7` |
|---|---|---|
| 1 | `FrameGrabber.tsx:78-80` `catch { onFailed(); }` discards the cause; null `videoEl` `:65`, null context `:73` indistinguishable | **Holds; lines are `:84-86`** (catch), **`:70-72`** (null `videoEl`), **`:78-80`** (null 2d context). All three call a bare `onFailed()`. `FrameGrabber.tsx` does not import `logResilienceEvent`. |
| 2 | Tainted-canvas path untested; `FrameCapture.test.tsx` covers nine lifecycle paths | **Holds exactly.** Nine `it(...)` blocks; none makes `toDataURL` throw. `OverlayEditorBackdrop.test.tsx` (spec 147 T002) does not either. |
| 3 | `BackdropControls.tsx:147-161`: no announcement while capturing; Cancel unmounts the focused element | **Holds; lines are `:154-168`.** The only in-flight cues are `disabled` on *Capture frame* and the conditional *Cancel capture* button. No live region exists in `BackdropControls.tsx`. The Cancel button is rendered only while `captureState === 'capturing'`, so any exit from that state — cancel, success, failure, timeout — removes it, focused or not. |
| 4 | No Playwright coverage of the camera-less backdrops; `e2e/overlays.spec.ts`'s create test silently depends on `camera-catalog/cameras` | **Holds.** No e2e test touches `Backdrop`/`White field`. `OverlayEditorDialog.tsx:316` passes `getToken`, so `CameraCaptureSection` mounts and `useListAllCameraChoicesQuery` fires on dialog open. The create test (`:19-35`) asserts nothing about it. |
| 5 | 4K frame → multi-megabyte inline style | **Not measured — §3.** |

**One observation the issue did not make, recorded because it widens item 3.** The issue frames
the focus loss as "clicking Cancel capture unmounts the focused element". It is broader: a
capture that *succeeds or fails while the operator's focus rests on Cancel* removes the same
button. Item 3's fix covers every exit from `capturing`, not only the Cancel click (FR-006).

**A near-collision with spec 233 (#2355), recorded so the rebase is expected, not discovered.**
`origin/fix/2355-whep-refusal-terminal-state` carries spec 233's artifacts only (no code, no PR
at `396c4fa7`). Its tasks edit **`FrameGrabber.tsx:90-103`** (the fail-fast arm and its comment)
and **append** a test to the end of `FrameCapture.test.tsx`. This spec edits `FrameGrabber.tsx`
`:69-86` and inserts its tests mid-file (plan §4) — separate hunks, but within a few lines of each
other in `FrameGrabber.tsx`. Whichever lands second may need a hand-resolved rebase there.

---

## 2. Scope — one PR, four items

Items 1–4 ship together, as the issue asks: "one decision about what this surface reports and to
whom". Each item is small; together they are the capture control's failure-reporting and
accessibility story. Every item is observable in one test run of `apps/shared` plus one
Playwright test, so this is one vertical slice, not four.

## 3. Item 5 — out of scope: not measured, no 4K camera available

**Item 5 is not addressed by this spec, and not because it was judged unimportant.** The issue
itself says "Measure before changing anything … that is the observation that decides whether this
is real". This environment has no 4K camera and no live hardware in the delivery pipeline, so the
measurement cannot be taken, and ADR-0036 forbids guessing a fix (`image/jpeg` at 0.85, or a
1920 px width cap) into place without it.

- `FrameGrabber.tsx:74-83` (canvas sizing and `toDataURL('image/png')`) is **left untouched**.
  Item 1's edit is confined to the three failure branches around it.
- Spec 147's Assumption 3 ("a few hundred KB" at 1080p) stands as the recorded premise.
- **For the PR body:** "Item 5 (4K data-URL size): not measured — no 4K camera is available in
  this environment. `FrameGrabber.tsx:74-83` unchanged. The issue's measure-first condition stands;
  it remains open for whoever has a 4K camera at phase 5."
- The issue stays open for item 5 after this PR merges (the PR references #2356 without a closing
  keyword — see plan §7).

This is not a "needs a human decision" block in the ADR-0144 sense: nothing is waiting on a
policy. It is an empirical question nobody here can currently answer.

## 4. Out of scope, deliberately

- **Contrast check** — excluded by the issue itself (needs a threshold-policy ADR the lane may not
  write).
- **#2344** (the editor's broader keyboard gap) and **#2355** (WHEP refusals slowing capture
  failure) — related, separate.
- **Logging the 10 s timeout and the `reconnecting`/`offline` fast-fail.** The issue's item 1 names
  the three draw-path failures only. The fast-fail path is already on the channel
  (`useWhepSession.ts:143` logs every status transition with `cameraIdentifier`); the bare timeout
  is not, but adding it is a separate decision about `useFrameCapture.ts`, not item 1.
- **The `camera-catalog/cameras` silent dependency (§8).** Not asserted; reason recorded.

---

## 5. User stories

### US1 (P1) — A failed capture tells the logs *why* (items 1 + 2)

*As a support engineer reading a kiosk/console remote-debug session, I want a failed frame
capture to put its cause on the `[resilience]` channel, so that a tainted canvas, a missing video
element and a missing 2d context are distinguishable from the field.*

**Acceptance scenarios**

```gherkin
Scenario: A tainted canvas is logged with its cause and shown to the operator as before
  Given the operator has selected camera "Line-1 Inlet" (cam-42)
  And the browser refuses to export the canvas (toDataURL throws a SecurityError DOMException)
  When the operator presses "Capture frame" and the session goes live
  Then exactly one "[resilience]" line with transition "frame-capture-failed" is written
  And it carries cameraIdentifier "cam-42" and an error string naming "SecurityError"
  And the could-not-capture alert is shown
  And the "Checkerboard" backdrop is still selected
  And the WHEP session is closed and its DELETE issued

Scenario: A missing 2d context is logged with a distinct cause
  Given the canvas offers no 2d context
  When a capture on cam-42 goes live
  Then one "frame-capture-failed" line is written for cam-42
  And its error string differs from the SecurityError case
  And the could-not-capture alert is shown

Scenario: A successful capture logs no failure and never logs the picture
  Given the canvas exports "data:image/png;base64,STUB"
  When a capture on cam-42 goes live
  Then no "frame-capture-failed" line is written
  And no "[resilience]" line carries the data URL
```

(*Bad-request / conflict / auth scenarios*: **N/A** — no HTTP request is added or changed. The
capture's own auth path is spec 147's and is unchanged.)

### US2 (P1) — A capture in flight is announced, and focus survives its end (item 3)

*As an operator using a screen reader, I want to hear that a capture has started, and not lose my
place when it ends, so that the ten-second wait is not silent and I can act again without hunting
for the control.*

```gherkin
Scenario: Starting a capture is announced
  Given the operator has selected camera "Line-1 Inlet"
  When the operator presses "Capture frame"
  Then a polite live region, present before the capture started, reads
       "Capturing a frame from Line-1 Inlet…"

Scenario: The announcement clears when the capture ends
  Given a capture on "Line-1 Inlet" is in flight
  When it is cancelled
  Then the live region is empty

Scenario: Cancel returns focus to the capture control
  Given a capture is in flight and "Cancel capture" has keyboard focus
  When the operator activates "Cancel capture"
  Then keyboard focus is on "Capture frame"

Scenario: A capture ending by itself returns focus from Cancel
  Given a capture is in flight and "Cancel capture" has keyboard focus
  When the capture succeeds
  Then keyboard focus is on "Capture frame"
  (and likewise when it fails on the 10 s timeout)

Scenario: A capture ending does not steal focus from elsewhere
  Given a capture is in flight and the "Label text" input has keyboard focus
  When the capture succeeds
  Then keyboard focus is still on "Label text"
```

### US3 (P2) — The camera-less backdrop is pinned end to end (item 4)

*As a maintainer, I want the half of spec 147 that needs no camera covered through the real
stack, so that a regression in the White-field backdrop fails CI rather than a demo.*

```gherkin
Scenario: The White field backdrop paints the canvas white in the real console
  Given an operator signed in through Keycloak and on the Overlays page
  When the operator opens "New overlay"
  Then the editor canvas is not white
  When the operator selects the "White field" backdrop
  Then the editor canvas's computed background-color is rgb(255, 255, 255)
```

(*Auth*: exercised implicitly — `signInAsOperator`, as every overlays e2e test. No write, so no
disposable is created and the teardown is not involved.)

---

## 6. Phase 4a colour, per item

The lane's rule: behaviour-changing → red; behaviour-preserving → characterisation observed green;
ambiguity resolves to red. This spec has both kinds, so each test is labelled.

| Item | Behaviour | Colour | Why |
|---|---|---|---|
| 1 — cause logged | **New** (no line is written today) | **RED** | A `frame-capture-failed` line does not exist; its tests must fail on its absence. |
| 1 — success logs nothing / no data URL | Existing (nothing logs today) | **Guard, green before and after** | Pins FR-003's negative. It cannot be red first — there is nothing to remove — and is labelled as such rather than passed off as red. It can fail: logging on success, or logging the URL, turns it red. |
| 2 — tainted canvas → alert, checkerboard, session closed | **Existing** (the `catch` already routes to FR-016) | **Characterisation, observed green** | The behaviour is spec 147's and is correct; the gap is coverage. Writing it red would require breaking working code. **Counterfactual required**: with the `catch` temporarily replaced by a rethrow, the test must go red; output quoted, change reverted. |
| 3 — announcement, focus return | **New** | **RED** | No live region and no focus management exist today. |
| 3 — no focus stealing | Existing (nothing moves focus today) | **Guard, green before and after** | FR-007's negative; a naive fix (always focus on exit) turns it red. |
| 4 — White field e2e | **Existing** (works; uncovered at e2e) | **Characterisation, observed green** | The test carries its own counterfactual: it asserts the canvas is **not** white before the click, so an assertion that passes regardless of the click is impossible. |

Item 2's characterisation and item 1's red test share one setup (a throwing `toDataURL`) but are
**separate tests**, so the characterisation's green is observable on unchanged code and item 1's
red is not masked by it.

---

## 7. Functional requirements

- **FR-001** — Each of `FrameGrabber`'s three draw-path failures (missing video element, missing
  2d context, a throw from `drawImage`/`toDataURL`) writes exactly one
  `logResilienceEvent('stream', 'frame-capture-failed', { cameraIdentifier, error })` before
  calling `onFailed()`.
- **FR-002** — `error` is a string: `String(cause)` for a throw; a fixed, distinct string for each
  of the two null branches. The three values are pairwise distinct.
- **FR-003** — No `[resilience]` line ever carries the captured data URL; a successful capture
  writes no `frame-capture-failed` line.
- **FR-004** — The operator-facing behaviour of every failure is unchanged: same alert text,
  backdrop unchanged, session released via the unmount (spec 147 FR-010–FR-016).
- **FR-005** — The capture section contains an `aria-live="polite"` region, mounted whenever the
  section is, that reads `Capturing a frame from <camera name>…` while `captureState` is
  `capturing` and is empty otherwise. `<camera name>` is the selected camera's catalogue name,
  falling back to its identifier if the catalogue has no entry for it.
- **FR-006** — When `captureState` leaves `capturing` (cancel, success, failure, timeout) and
  focus was on *Cancel capture* and has been lost to the document body, focus moves to *Capture
  frame*.
- **FR-007** — If focus is anywhere other than the body when the capture ends, it is not moved.
- **FR-008** — A Playwright test pins the White-field backdrop in the real console (US3).
- **FR-009** — `FrameGrabber.tsx:74-83` is unchanged (§3).

## 8. The `camera-catalog/cameras` dependency — noted, not asserted

The create test's silent dependency on the camera catalogue answering is real, but **no assertion
is added**, for two reasons:

1. **It is already caught elsewhere.** `e2e/cameras.spec.ts:12` ("operator signs in and the
   cameras list loads through the gateway") issues the same authenticated
   `GET /camera-catalog/cameras` as the same operator and fails on an error alert. A scope
   regression on that endpoint fails CI there, with a message that names cameras.
2. **Asserting it in the new White-field test would bind the one test whose point is "needs no
   camera" to the camera catalogue.** It would fail for a reason unrelated to what it pins.

The observation is recorded here and in the PR body; if a maintainer wants the dialog's own
fetch pinned, it is a one-line follow-up, not this spec's.

## 9. ADR check

No new ADR. The log line uses ADR-0076's established channel and shape (`WhepClient.ts:269`
already writes `{ error: String(cause) }`); the live region and focus handling follow patterns
already in `OverlayEditor.tsx` (`aria-live="polite"` regions, `:639`, `:682`) and its
`aria-disabled`-over-`disabled` focus-preservation decision (`:649-657`). The contrast policy that
*would* need an ADR is excluded by the issue.

## 10. Independent end-to-end test procedure

1. `pnpm --filter @smart-sentinel-eye/shared test -- FrameCapture` — the new tests (plan §4) pass,
   the nine existing ones pass unmodified.
2. Full stack (`AppHost`, one stack per machine), then `pnpm exec playwright test
   e2e/overlays.spec.ts -g "White field"` — green.
3. **Phase 5, manual, with a camera** (if one is available): open *New overlay*, pick a camera,
   press *Capture frame*; with a screen reader (NVDA or Narrator) confirm the announcement; focus
   *Cancel capture* with Tab, press Enter, confirm focus lands on *Capture frame*. Force a failure
   (pick a camera whose stream is down) and confirm DevTools shows a `[resilience]` line with
   `transition: "frame-capture-failed"` or — for a stream that never goes live — the
   `useWhepSession` transition lines, per §4. If no camera is available at phase 5, record that
   US1/US2 are component-test-verified only.
4. Item 5 is not part of this procedure (§3).

## 11. Assumptions (explicit)

- **A1** — The null-`videoEl` branch is not reachable through `OverlayEditor`'s public surface in
  jsdom (`videoRef` is attached for the component's whole mounted life). It gets its log line for
  parity (FR-001) but no test; the other two branches are tested. Marked here rather than
  silently left uncovered.
- **A2** — jsdom performs focus fixup (a removed focused element leaves `document.activeElement`
  as `body`), matching browsers. Whether a browser fires `blur` on removal differs by engine;
  FR-006's mechanism must not depend on it (plan §3).
- **A3** — The announcement is visible text as well as a live region (plan §3), so sighted
  operators get the same cue. This is the smallest form that satisfies the issue; an `sr-only`
  region would also satisfy it and is not worse by any stated criterion.
