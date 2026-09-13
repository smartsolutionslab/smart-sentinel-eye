# Spec 147 — Plan

**Phase 2 artefact.** Read `spec.md` first; this says how, not what.

---

## Bounded context and layers — none, and that is the point

**This change touches no `src/` project.** No Domain, no Application, no
Infrastructure, no Api, no `Shared.Contracts`, no migration, no message, no
endpoint, no DTO. There is no aggregate, no value object and no invariant to
state, because nothing is added to a domain model.

That is not an omission in this plan; it is the plan's central claim, and it
follows from spec.md finding 1. The overlay→camera relation is owned by
`LayoutComposition.Tile` and is many-to-many (ADR-0112 §2); an overlay is a
fab-neutral template (ADR-0115). **A preview camera is authoring-session UI
state.** Any task that finds itself opening a `.cs` file has left this spec.

**Layer, in frontend terms:**

| Layer | Change |
|---|---|
| `apps/shared/src/ui/composites/` | `OverlayEditor.tsx` gains a backdrop; a new `useFrameCapture.ts` + `CameraBackdropPicker.tsx` sit beside it |
| `apps/shared/src/api/` | **read-only reuse** — `cameras.api.ts`, `streams.api.ts` unchanged |
| `apps/shared/src/streaming/` | **read-only reuse** — `WhepClient.ts` unchanged |
| `apps/management-web/src/features/overlays/` | `OverlayEditorDialog.tsx` passes `getToken` down |
| `src/**` | **untouched** |
| `apps/kiosk-web/**` | **untouched** |

---

## The mechanism

### The capture, end to end

```
operator picks camera ──▶ "Capture frame" pressed
        │
        ▼
useFrameCapture sets capturing = true, holds { cameraIdentifier }
        │
        ▼
<FrameGrabber> mounts  ──▶ useWhepSession({ cameraIdentifier, whepUrl, getToken })
        │                        │
        │                        ├─ useGetStreamQuery(cameraIdentifier) → whepUrl
        │                        └─ WhepClient.connect(hiddenVideoEl)
        ▼
status === 'live'  (a frame was DECODED — spec 094, not merely connected)
        │
        ▼
canvas.drawImage(hiddenVideoEl, 0, 0, w, h) ; canvas.toDataURL('image/png')
        │
        ▼
onCaptured(dataUrl)  ──▶ capturing = false
        │
        ▼
<FrameGrabber> UNMOUNTS ──▶ useWhepSession cleanup ──▶ client.close()
                                                        └─ WHEP DELETE (spec 142)
```

**Unmounting is the teardown.** That is the design decision this plan rests on:
the session's lifetime is a React subtree's lifetime, so every exit path —
success, timeout, cancel, camera change, dialog close, unmount — releases the
session through the one cleanup `useWhepSession` already has, rather than through
five hand-written release paths that can each be forgotten. FR-010 through
FR-014 are then one mechanism, not five.

### Why `useWhepSession` is reused rather than reimplemented

House rule: mirror existing patterns. Three specific reasons beyond that:

1. It hands the **caller** the `videoRef` (`useWhepSession.ts` returns
   `videoRef`), so a caller can own a hidden `<video>`. It was already built to
   be embedded by something other than `CameraViewer`.
2. `status === 'live'` already means *a frame was decoded into this element*
   (spec 094 / #2111) — the exact precondition a capture needs. Reimplementing
   it would reimplement the `totalVideoFrames`-baseline subtlety that spec 094
   got wrong twice.
3. Its cleanup already calls `client.close()`, which already issues the WHEP
   `DELETE` (spec 142). Session release is inherited, not re-earned.

**`useWhepSession.ts` is not modified.** It is on the wall's §IV path
(`CameraViewer` → 250 tiles); editing it for an editor concern is out of scope
and a re-scope signal.

### Why a hidden `<video>` and not `CameraViewer`

`CameraViewer` owns its own `<video>` and exposes no ref to it, so a caller
cannot draw from it without changing `CameraViewer` — a §IV-path file. It also
brings a decode sampler, a lag sampler, a status overlay and an overlay label,
none of which a capture wants. The grabber mounts a bare
`<video ref={videoRef} autoPlay playsInline muted>` hidden from layout, and
nothing else.

**Hidden, but not `display: none`.** A `display:none` video may not decode in
some engines, which would make the capture hang on a browser-specific condition.
Use `position: absolute; width: 1px; height: 1px; opacity: 0; pointer-events:
none` — present and decoding, not visible. **Named as a risk (R3) rather than
assumed correct.**

### The timeout

`useFrameCapture` starts a 10 s timer when `capturing` becomes true (FR-011).
It is *not* a replacement for `useWhepSession`'s own 3 s media watchdog — that
one retries with backoff, indefinitely, which is right for a wall tile and wrong
for a dialog. The capture timer is the outer bound that says *stop retrying,
this camera is not going to produce a picture*, and it fires the same teardown as
success.

---

## The alternative that was rejected, and why it is recorded

**A live `<video>` behind the canvas**, i.e. the issue's option 1, is a smaller
diff: mount `CameraViewer` (or a bare session) in the canvas div and stop. It was
rejected in spec.md §The decision for a resource reason, and the reason is worth
repeating at plan level because the smaller diff is the tempting one:

the session lifetime becomes *the dialog's* lifetime, which is unbounded and
operator-controlled. Every mechanism that would bound it — an idle timer, a
visibility check, a "pause preview" button — is more code than the capture path,
and each is a new failure mode on a shared SFU. **The capture path is not the
expensive option; it is the one whose worst case is bounded.**

---

## Modules and their contracts

### `apps/shared/src/ui/composites/OverlayEditor.tsx` (modified)

Props gain:

```ts
/** Resolves the operator's bearer token. Absent ⇒ no camera backdrop offered. */
getToken?: () => Promise<string | null>;
```

**Optional, and absent means the feature is simply not offered** — so the
component keeps working for any caller that does not supply it, and the
checkerboard/white/black backdrops still work. That is the FR-017 degradation
path expressed as a type.

Internal state: `backdrop: 'checkerboard' | 'captured' | 'white' | 'black'` and
`capturedFrame: string | null`. **Neither is lifted into `OverlayLabel`**;
`onChange` fires only for text, font size and geometry, exactly as today
(FR-005).

The canvas div's `background` becomes a function of `backdrop`:

| `backdrop` | `background` |
|---|---|
| `checkerboard` | the existing `repeating-linear-gradient(...)`, **byte-for-byte** |
| `white` | `#ffffff` |
| `black` | `#000000` |
| `captured` | `#000000 url(<dataUrl>) center / contain no-repeat` (FR-019) |

### `apps/shared/src/ui/composites/useFrameCapture.ts` (new)

```ts
export type CaptureState = 'idle' | 'capturing' | 'failed';

export interface FrameCaptureResult {
  state: CaptureState;
  /** Starts a capture against this camera. No-op while already capturing. */
  capture: (cameraIdentifier: string) => void;
  /** Aborts any capture in flight; also the unmount path. */
  cancel: () => void;
  /** The camera a capture is in flight against, or null. */
  activeCamera: string | null;
}
```

The hook owns `state`, the active camera and the 10 s timer. It does **not** own
a WHEP session — the session lives in the `FrameGrabber` subtree the hook's
`activeCamera` gates, which is what makes teardown structural rather than manual.

### `apps/shared/src/ui/composites/FrameGrabber.tsx` (new, internal)

```ts
interface FrameGrabberProps {
  cameraIdentifier: string;
  getToken: () => Promise<string | null>;
  onCaptured: (dataUrl: string) => void;
  onFailed: () => void;
}
```

Mounted **only** while a capture is in flight. Calls `useWhepSession`, renders
the hidden `<video>`, and on `status === 'live'` draws one frame and calls
`onCaptured`. On `status === 'error'`/`'offline'` calls `onFailed`.

The draw is wrapped:

```ts
try { /* drawImage + toDataURL */ } catch { onFailed(); }
```

**Not drive-by error handling** — this is a trust boundary in the browser-API
sense (assumption 1: canvas tainting), and the alternative is an uncaught throw
inside a render effect that takes the dialog down. The `catch` routes into the
already-specified FR-016 message; it swallows nothing silently.

### `apps/shared/src/ui/composites/BackdropControls.tsx` (new)

The four-way backdrop selector, the camera picker and the capture button. Mirrors
`LayoutEditorDialog.tsx:85-98`'s use of `useListAllCameraChoicesQuery`, including
the `count`/`complete` truncation notice.

### `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx` (modified)

One change: build a stable `getToken` from `react-oidc-context` and pass it to
`<OverlayEditor>`. **Copy `CameraDetailPage.tsx:20-38`'s ref-behind-`useCallback`
shape verbatim, including its reasoning** — an unstable `getToken` identity is
what silently killed the decode sampler (issue 1889), and `useWhepSession` guards
its own use behind a ref but callers have been bitten anyway.

---

## Styling — deliberately inline, deliberately not tokens

The new controls use the **same inline-style idiom as the rest of
`OverlayEditor.tsx`**, not Radix + Tailwind, and not ADR-0148's tokens.

This looks like a violation of ADR-0077/0078 and is instead a scheduling
decision: **#2342 converts this entire file to the token system in one pass.** A
half-converted component means #2342 must reconcile two idioms in a file it is
rewriting anyway, and means this spec makes design-language choices ADR-0146
landed one day ago and #2342 owns. ADR-0146 was read; it is not implemented here.

The one place this is *not* discretionary: **FR-002 pins the checkerboard string
byte-for-byte**, so nothing here may pre-empt #2342's restyle of it.

---

## Invariants the change must preserve

1. **The submitted payload is unchanged.** `{ name, label }`, with `label`
   carrying exactly its six existing fields. No preview state reaches the wire.
2. **The checkerboard string is unchanged** (FR-002), and is the default.
3. **The label's own appearance is untouched** — after PR #2354 that is
   `overlayLabelSurfaceStyle(...)`; before it, the inline object. Either way this
   spec does not read or write it. #2354's proof is that the wall did not move,
   and this branch must not disturb that evidence.
4. **`useWhepSession.ts`, `WhepClient.ts`, `CameraViewer.tsx` and `CellPage.tsx`
   are not modified.** They are the §IV path.
5. **Drag, resize, clamping to [0,1], the text input and the font slider behave
   exactly as today**, on every backdrop.
6. **The canvas stays 800×450** (FR-019).

---

## Boundary rules

- No cross-context project reference is created or could be — nothing in `src/`
  is touched. NetArchTest is unaffected.
- No new `Shared.Contracts` message or DTO.
- `apps/shared` must not import from `apps/management-web` or `apps/kiosk-web`;
  the new modules live in `apps/shared` and take `getToken` as a prop rather than
  reaching for `react-oidc-context` themselves — the same inversion
  `CameraViewer` already uses.
- No new npm dependency.

---

## File ownership and collisions (ADR-0109)

| File | Ownership | `[P]`-safe |
|---|---|---|
| `apps/shared/src/ui/composites/useFrameCapture.ts` | new, this spec | yes |
| `apps/shared/src/ui/composites/FrameGrabber.tsx` | new, this spec | yes |
| `apps/shared/src/ui/composites/BackdropControls.tsx` | new, this spec | yes |
| `apps/shared/src/ui/composites/OverlayEditorBackdrop.test.tsx` | new, this spec | yes |
| `apps/shared/src/ui/composites/FrameCapture.test.tsx` | new, this spec | yes |
| `apps/shared/src/ui/composites/OverlayEditorCharacterisation.test.tsx` | new, this spec | yes |
| `apps/shared/src/ui/composites/OverlayEditor.tsx` | **shared with PR #2354** | **no — serialise** |
| `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx` | this spec | yes |
| `apps/management-web/src/features/overlays/OverlayEditorDialog.test.tsx` | this spec | yes |

**The one contended file is `OverlayEditor.tsx`**, against PR #2354 — and only
textually: #2354 rewrites the `<Rnd>` `style` object, this spec rewrites the
canvas `background` and the surrounding JSX. Resolution rule if they conflict:
**keep #2354's `overlayLabelSurfaceStyle(...)` spread exactly as it stands and
re-apply this spec's backdrop changes around it.**

**Foundational vs fan-out.** `useFrameCapture.ts` + `FrameGrabber.tsx` are the
foundation (T004, T005); `BackdropControls.tsx` (T006) and the dialog wiring
(T008) are independent of each other once the editor's prop surface exists
(T003). The three test files are disjoint and fully parallel.

---

## Phase 4a — the colour, split per artefact

**Precedent:** specs 144 and 146 both split the colour per artefact rather than
per branch. The same split applies, and CLAUDE.md's tie-break — ambiguity
resolves to red — governs anything not named below.

**CHARACTERISATION, observed green before any change** —
`OverlayEditorCharacterisation.test.tsx`. `OverlayEditor` has **no test file of
its own today**; its only coverage is #2354's two new files, which are about the
label's surface, and `OverlayEditorDialog.test.tsx`, which is about the form. So
the covering tests are written first, against the unchanged component:

- the canvas paints the `repeating-linear-gradient` string;
- the label sits at the authored normalized coordinates;
- drag and resize emit clamped `[0,1]` values through `onChange`;
- the text input and font slider emit their fields.

A refactor with no covering test is a rewrite. These must pass **unmodified**
after the change; an assertion that has to be edited is evidence the behaviour
moved — block, do not adjust.

**RED** — `OverlayEditorBackdrop.test.tsx` and `FrameCapture.test.tsx`. All new
behaviour. Each must be observed failing, and the verbatim output quoted in the
PR (ADR-0139).

**What is genuinely testable in vitest/jsdom, and what is not.** This is stated
rather than papered over, because manufacturing a test for the untestable part is
the failure mode here.

| Claim | Where it is proven |
|---|---|
| Backdrop selection paints the four backgrounds | **test** (assert `style.background`) |
| `captured` is unselectable with no frame | **test** |
| Changing backdrop emits no `onChange` | **test** |
| A capture opens exactly one WHEP session | **test** — count `RTCPeerConnection` constructions on the `FakePeerConnection` double |
| A capture issues the WHEP `DELETE` on teardown | **test** — assert the `fetch` stub saw `method: 'DELETE'` against the session URL |
| Dialog close / camera change / cancel closes the session | **test** — same two instruments |
| **No session exists when no capture is in flight** (FR-014) | **test** — the negative: zero constructions |
| Timeout at 10 s leaves the backdrop and shows the alert | **test** — `vi.useFakeTimers()`, as `CameraViewerMedia.test.tsx` already does |
| Saving still works after a failed capture | **test** (dialog level) |
| The submitted payload is unchanged | **test** (dialog level) |
| **The backdrop shows the camera's actual picture** | **phase 5 only** |
| **`drawImage`/`toDataURL` do not throw on a MediaStream** | **phase 5 only** (assumption 1) |
| **The letterboxing looks right for a 4:3 camera** | **phase 5 only** |
| **Contrast is judgeable by eye** | **phase 5 only** — it is the point of the feature and cannot be asserted |

**Why the picture itself is out of reach.** `apps/shared/vitest.config.ts` is
`environment: 'node'` with no `setupFiles`; jsdom is opted into per file. In
jsdom there is no video decode, `getVideoPlaybackQuality` is absent (so
`useWhepSession` promotes to `live` on `connected`, deliberately — FR-005 of spec
094), and `HTMLCanvasElement.getContext('2d')` returns `null` without the
optional `canvas` package. **Installing `canvas` to fake a decode would test the
fake**, so it is not installed. The tests prove the **wiring, the lifecycle and
the fallback**; phase 5 proves the picture.

The double is copied from `CameraViewerMedia.test.tsx:64-115` — a
`FakePeerConnection` plus a duck-typed SDP `fetch` response — **not** a mocked
`WhepClient`. That matters: it lets the `DELETE` be asserted as a real HTTP call
rather than as "we called a method we also wrote".

---

## Risks

**R1 — `getContext('2d')` is `null` in jsdom.** The grab path must tolerate it
(the `try/catch` plus an explicit null check route it to `onFailed`), or the red
tests die on a jsdom artefact instead of on the behaviour under test. *Mitigation:*
tests assert the **session lifecycle and the failure path**, never a pixel; the
success-path test drives `onCaptured` by making the grab return a stub data URL
through an injected grab function, so the test exercises the wiring it claims to.

**R2 — the canvas could be tainted** (assumption 1). *Mitigation:* `try/catch` →
FR-016; phase 5 step 5 is the observation. If it does taint, the feature's
fallback is already specified and the spec does not silently fail.

**R3 — a hidden `<video>` may not decode.** *Mitigation:* 1×1 transparent rather
than `display:none`; phase 5 step 5 observes a real frame, which is the only
proof available.

**R4 — `OverlayEditor.tsx` conflicts with PR #2354.** *Mitigation:* the
resolution rule above, plus FR-002's byte-for-byte characterisation, which fails
loudly on a mangled rebase.

**R5 — an unstable `getToken` identity.** *Mitigation:* copy
`CameraDetailPage.tsx`'s ref shape; the dialog test asserts the identity survives
a token change, as `CameraDetailPage`'s test already does.

**R6 — the capture holds a session longer than intended if a React cleanup does
not run.** *Mitigation:* this is precisely why teardown is structural
(unmount-driven) rather than a hand-written release; and FR-014's negative test
plus phase 5 step 6 observe it from both sides.

**R7 — scope creep into the contrast check.** *Mitigation:* named out of scope
with reasons; the lane may not write the ADR it needs. A task that starts
sampling pixels has left this spec.

---

## Gate — phase 2

The plan introduces no new architectural decision, no new dependency, no new
context, no contract change and no `src/` edit. It aligns with ADR-0074/0075/0080
(frontend shape), ADR-0112/0115 and spec 004 FR-015 (why the camera is not
persisted), ADR-0036 (smallest change; the rejected live option is recorded),
ADR-0109 (one contended file, named), and constitution §I and §IV (§IV: N/A with
reasons). Handed back for review.
