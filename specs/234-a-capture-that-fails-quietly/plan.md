# Plan 234 — A capture that fails quietly

**Spec**: [spec.md](./spec.md) · **Issue**: #2356 · **Phase**: 2 (Plan)
**Base**: `396c4fa7` · **Engineer**: `frontend-engineer`

## 1. Where this lives

No bounded context, no layers, no messaging. The whole change is in `apps/shared`'s overlay-editor
composites (spec 147's frame capture) plus one Playwright spec:

| File | Change | Item |
|---|---|---|
| `apps/shared/src/ui/composites/FrameGrabber.tsx` | log the cause on the three draw-path failures | 1 |
| `apps/shared/src/ui/composites/BackdropControls.tsx` | live region + focus return in `CameraCaptureSection` | 3 |
| `apps/shared/src/ui/composites/FrameCapture.test.tsx` | new tests (red, characterisation, guards) | 1, 2, 3 |
| `e2e/overlays.spec.ts` | one new test | 4 |

**Not touched**: `OverlayEditor.tsx` (the props `BackdropControls` needs are already passed),
`useFrameCapture.ts`, `useWhepSession.ts`, `resilienceLog.ts`, `FrameGrabber.tsx:74-83` (item 5).
Boundary rules: nothing crosses a context; `apps/shared` imports only its own modules.

## 2. Item 1 — `FrameGrabber.tsx`

Import `logResilienceEvent` from `'../../observability/resilienceLog.js'` (the relative spelling
`CameraViewer.tsx:12` uses in the same folder). In the `status === 'live'` arm, each of the three
failure exits logs, then calls `onFailed()`:

| Exit | `error` value |
|---|---|
| `videoEl === null` (`:70`) | `'video element unavailable'` |
| `context === null` (`:78`) | `'2d context unavailable'` |
| `catch (cause)` (`:84`) | `String(cause)` — for `new DOMException('tainted', 'SecurityError')` this is `"SecurityError: tainted"` |

Transition name `frame-capture-failed`; detail `{ cameraIdentifier, error }` — the `error` key
matches `WhepClient.ts:269`'s existing `{ error: String(cause) }` rather than `CameraViewer.tsx`'s
`reason`, because the issue names it and it is the closer precedent (a caught throw).

A small local helper (`failWith(error: string)`) is acceptable if it keeps the three exits
readable; three inline pairs are equally acceptable. No new module.

**The data URL cannot reach the log** on the throw path — `toDataURL` threw, so no URL exists —
and the success path gains no logging. FR-003's guard test pins the second fact.

`cameraIdentifier` is added to nothing else; the effect's dependency array gains
`cameraIdentifier` (lint will demand it; it is stable per mounted instance — `key={activeCamera}`
in `OverlayEditor.tsx:740` remounts on change).

The existing comment at `:63-68` ("swallows nothing silently") stays true and needs at most a
clause saying the cause now goes to the `[resilience]` channel. Do **not** touch `:90-103`
(spec 233's hunk).

## 3. Item 3 — `BackdropControls.tsx` (`CameraCaptureSection` only)

**Live region (FR-005).** Rendered unconditionally inside `CameraCaptureSection`, directly above
the alert (`:169`), so it is in the DOM before a capture starts — a region inserted together with
its content is not reliably announced (the reason `ChainRecoveryNotice.tsx:307` gives):

```tsx
<p aria-live="polite" data-testid="frame-capture-live-region" style={NOTICE_STYLE}>
  {captureState === 'capturing' ? `Capturing a frame from ${cameraName}…` : ''}
</p>
```

`cameraName` = `cameraItems.find((c) => c.cameraIdentifier === selectedCamera)?.name ??
selectedCamera`. `selectedCamera` is the capturing camera while `capturing` — a camera change
cancels (`OverlayEditor.tsx:550-562`). Visible, not `sr-only` (spec A3); `NOTICE_STYLE` is the
section's existing muted style. `aria-live`, not `role="status"`, so `getByRole` queries in the
existing tests see no new role.

**Focus return (FR-006/FR-007).** Where focus goes: *Capture frame* — it is the interactive
control the operator most plausibly wants next (retry, or it is simply where they were), and it is
enabled again on every exit from `capturing` because a camera is still selected. Focusing the live
region (the issue's suggestion) would need `tabIndex={-1}` on non-interactive text and would
re-read it; rejected.

Mechanism — it must not depend on `blur` firing when an element is removed (engines differ, spec
A2):

- `captureButtonRef` on *Capture frame*.
- `cancelWasFocusedRef`: set `true` in *Cancel capture*'s `onFocus`; reset to `false` when a new
  capture starts (i.e. on entering `capturing`). **Not** cleared by `onBlur`.
- `useEffect` on `captureState`: when it is not `capturing`, `cancelWasFocusedRef.current` is true,
  and `document.activeElement` is `null` or `document.body` → `captureButtonRef.current?.focus()`,
  then reset the flag. The `activeElement` check is what makes FR-007 hold: if the operator moved
  on to another control, focus is where they put it and is left alone.

The effect runs after the commit in which *Capture frame* loses `disabled`, so `.focus()` lands.

No change to `BackdropControlsProps`; no change to `OverlayEditor.tsx`.

## 4. Tests — `FrameCapture.test.tsx`

All new tests go in **one new `describe` block inserted after the FR-012 cancel test (`:306`)**,
not appended at the file's end — spec 233's T003 appends there, and a mid-file insertion keeps
the two as separate hunks. Reuse the file's `FakePeerConnection`, `sdpResponse`,
`installCanvasStub`, `flushMicrotasks`, `selectCamera`, `pressCapture`. Add a
`console.info` spy and a `resilienceLines(transition)` filter copied from
`CameraViewerAlignment.test.tsx:152-162` (copied, as this file already copies its doubles).

| # | Test (sentence-style, ADR-0053) | Colour |
|---|---|---|
| a | *Shows the could-not-capture alert and keeps the checkerboard when the canvas is tainted* — `toDataURL` throws `new DOMException('tainted', 'SecurityError')`, `getContext` stubbed to a working 2d context; go live; alert visible, Checkerboard checked, peer closed, DELETE on `fetchMock` | **Characterisation** (item 2) |
| b | *Logs the tainted-canvas cause on the resilience channel* — same setup; exactly one `frame-capture-failed` line, `cameraIdentifier: 'cam-42'`, `error` contains `SecurityError` | **RED** (item 1) |
| c | *Logs a distinct cause when the canvas has no 2d context* — `getContext` stubbed to return `null`; one line for cam-42 whose `error` does not contain `SecurityError`; alert visible | **RED** (item 1) |
| d | *Logs no capture failure and never the picture when a frame is captured* — `installCanvasStub()`; no `frame-capture-failed` line; no `console.info` call's serialised arguments contain `CAPTURED_DATA_URL` | **Guard** (FR-003) |
| e | *Announces the capture in flight and clears the announcement when it ends* — region exists and is empty before; `Capturing a frame from Line-1 Inlet…` after pressCapture; empty after Cancel | **RED** (item 3) |
| f | *Returns focus to Capture frame when a focused Cancel is pressed* — `.focus()` the Cancel button (fireEvent.click does not focus), click; `document.activeElement` is the Capture frame button | **RED** (item 3) |
| g | *Returns focus to Capture frame when a capture ends while Cancel has focus* — two cases: success (`installCanvasStub`, go live) and the 10 s timeout (`vi.advanceTimersByTimeAsync(10_000)`) | **RED** (item 3) |
| h | *Leaves focus where it is when a capture ends while another control has focus* — focus the Label text input (`overlay-editor-text`), capture succeeds; focus still on it | **Guard** (FR-007) |

**Counterfactual for (a)** — the test-writer records it: replace `catch { onFailed(); }` with
`catch (cause) { throw cause; }`, run (a), quote the red output, revert, run again green.
Without it, (a)'s green proves nothing.

**(c) stubs `getContext` to return `null` explicitly** rather than relying on jsdom's
not-implemented default, so the test does not depend on jsdom's console noise or its return value.

**Why (d) and (h) are not red:** each pins a negative that holds today; there is nothing to remove
to make it fail first. Each is shown able to fail by what a wrong fix would do (log on success;
focus unconditionally), and the spec labels them guards rather than claim a red they cannot have.

## 5. Item 4 — `e2e/overlays.spec.ts`

One test, inserted after *operator creates an overlay draft and it appears in the list* (`:19-35`):

```
test('operator sees the White field backdrop behind the label without a camera', …)
  signInAsOperator → Overlays link → heading visible → "New overlay"
  const canvas = page.getByTestId('overlay-editor-canvas')
  await expect(canvas).not.toHaveCSS('background-color', 'rgb(255, 255, 255)')
  await page.getByRole('radio', { name: 'White field' }).check()
  await expect(canvas).toHaveCSS('background-color', 'rgb(255, 255, 255)')
```

No save, so no `FIRST_WRITE_*` timeouts and no disposable (the teardown's `E2E ` pattern is not
involved). The `not.toHaveCSS` line is the built-in counterfactual — the checkerboard is set via
the `background` shorthand (`OverlayEditor.tsx:216`), whose computed `background-color` is not
white. The camera-catalogue observation (spec §8) is **not** asserted.

Running it needs the full Aspire stack (ADR-0103; one stack per machine). If the stack cannot be
booted in phase 4a, the test-writer says so verbatim rather than reporting it green.

## 6. Latency, security, NFRs

- §IV: N/A (management console authoring surface; no wall leg).
- Security: the only new data leaving the component is an error string on `console.info`. A
  `DOMException` message is browser-generated and carries no frame content; the data URL is never
  logged (FR-003, test d). No new request, header, or scope.
- Coverage (ADR-0065): `apps/shared` — new branches are covered by a–h except the null-`videoEl`
  line (spec A1).

## 7. Delivery notes

- PR body references **#2356 without a closing keyword** — item 5 stays open (spec §3). Quote the
  item-5 sentence from spec §3 verbatim.
- Expect a possible hand rebase against spec 233 in `FrameGrabber.tsx` (spec §1).
- Commits (ADR-0030): `test(shared): …` (4a) then `fix(shared): …` per item; each commit builds
  on its own.
