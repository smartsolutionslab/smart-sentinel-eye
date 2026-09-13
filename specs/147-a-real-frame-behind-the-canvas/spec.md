# Spec 147 — A real frame behind the canvas

**Issue:** #2340 — *The overlay editor's canvas is a checkerboard, so contrast
against real video is unknowable.*
**Branch:** `feat/2340-a-real-frame-behind-the-canvas`
**Lane:** autonomous (ADR-0144). **Engineer:** `frontend-engineer`.
**Programme:** second delivery of the overlay-editor programme (#2339–#2353).

**ADRs referenced:** ADR-0037 (the phased workflow), ADR-0144 (the lane),
ADR-0036 (smallest change, no speculative generality), ADR-0074 (two frontends,
`apps/shared` between them), ADR-0075 (RTK Query), ADR-0077/ADR-0078 (Radix +
Tailwind — **deliberately not applied here**, see §Out of scope), ADR-0112
(multi-tile layouts: a tile binds a camera *and* an overlay), **ADR-0115
(overlays are fab-neutral templates — the decisive one; see §Which camera)**,
ADR-0040 (value-copied identifiers across contexts), ADR-0128 /
ADR-0129 (what the editor is **not** on), ADR-0146 (design language direction),
ADR-0109 (file-disjoint parallelism).
Constitution: §I (air-gapped), §IV (latency budget), §Testing.

---

## What this is, and what it is not

`apps/shared/src/ui/composites/OverlayEditor.tsx:70` paints the authoring canvas
as a diagonal checkerboard:

```ts
background: 'repeating-linear-gradient(45deg, #1f2937, #1f2937 12px, #111827 12px, #111827 24px)',
```

An operator drags a translucent white plate — `rgba(255,255,255,0.85)` with
`#111827` ink, after #2339's fold — across grey stripes and publishes it over
live video. **Nothing in the authoring experience shows what the label will
actually sit on.** Over a dark aisle the plate reads; over an overexposed
ceiling light, a white machine housing or a painted wall, a 15%-transparent
white plate on white is close to invisible.

**This is not** a redesign of the editor (#2343, #2346, #2350), a token
conversion of its inline styles (#2342), a change to the label's own appearance
(#2339, #2353), or a change to any bounded context. **It is a frontend-only
change to one shared composite and its one host dialog**: the canvas gains a
backdrop the operator chooses, one of whose choices is a still frame captured
from a real camera.

**No backend, no contract, no migration, no new endpoint, no ADR.** Every API it
uses already exists and is already called by `apps/management-web`.

---

## What was verified, not assumed

Five findings settle the open questions in the issue. Each was read out of the
repository on this branch.

**1. An overlay is not bound to a camera, and must not become bound to one.**
`src/OverlayDesigner/Domain/Overlay/Overlay.cs:12-31` — the aggregate holds
`OverlayName`, `Revisions`, `Creation` and `ArchivedAt?`. There is **no**
`CameraIdentifier`, **no** `LayoutIdentifier`, no tile, no position, no fab.
`grep -ri camera src/OverlayDesigner --include=*.cs` returns **three hits, all in
XML doc comments**, and no `.csproj` under `src/OverlayDesigner` references
CameraCatalog, LayoutComposition or StreamDistribution.

The binding runs the other way and is owned entirely by
`src/LayoutComposition/Domain/Layout/Tile.cs:27-68`:

```
Tile = { Camera: CameraIdentifier (required),
         Overlay: Option<OverlayIdentifier> (optional),
         Position: GridPosition(row, col) }
```

`Tile`'s own doc comment says it: *"A camera and an overlay MAY each be reused
across tiles (ADR-0112 §2)."* So overlay→camera is a **two-hop, many-to-many**
join resolved only at render time, on the kiosk
(`CellPage.tsx:434-437` fetches the overlay from `tile.overlayIdentifier`;
`:538-544` passes it to `<CameraViewer cameraIdentifier={tile.cameraIdentifier}>`).
**There is no "the" camera for an overlay to derive.** ADR-0115 makes the
isolation a decision rather than an omission — an overlay is a fab-neutral
template two plants may legitimately share — and spec 004 FR-015 forbids the
cross-context reference outright.

A reverse index does exist —
`src/LayoutComposition/Application/Queries/Handlers/FabsReferencingOverlayQueryHandler.cs:26-45`
finds published tiles referencing an overlay — and selecting `tile.Camera`
instead of `layout.Fab` would be a one-line variant. **It is useless here**: the
editor is reachable only on the *create* path (finding 2), where the overlay is
brand new and referenced by nothing.

**2. The editor's host dialog has no camera in scope, and there is exactly one
mount.** `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx:13-16`
— props are `{ open, onOpenChange }`; the form type is `{ name, label }`. The
only non-test mount of `OverlayEditor` in the whole repo is that dialog's line
90. `useEditDraftOverlayRevisionMutation` has **zero UI consumers**, so the
canvas is reachable only on the create path — which is also why the reverse
index in finding 1 cannot help.

**2a. This was foreseen.** `specs/004-overlay-designer/spec.md:325` (FR-012):
*"The editor MUST show the label over a representative camera frame (placeholder
image acceptable in v1 — embedding a live frame is a stretch goal)."* The
checkerboard is that v1 placeholder. **This spec discharges spec 004 FR-012**,
and #2340 is the observation that the placeholder was never replaced.

**3. There is no snapshot or thumbnail path anywhere in the product.** Searched
across `apps/` and `src/` for `drawImage`, `toDataURL`, `toBlob`, `ImageCapture`,
`createImageBitmap`, `OffscreenCanvas`, `<canvas` — **zero non-test hits in
`apps/`**. The `snapshot` hits in `src/` are EF model snapshots and
SystemVariables' `ResolvedOverlaySnapshotDto`, neither of which is an image.
So "capture a still" means **grabbing a frame from a WHEP session in the
browser**; there is nothing else to read.

**4. The streaming pieces are already shared and already imported by this app.**
`apps/shared/src/ui/composites/useWhepSession.ts` hands the *caller* a
`videoRef` to attach to its own `<video>`, and drives a state machine where
`status === 'live'` means **a frame was actually decoded into that element**
(spec 094 / #2111), not merely that a socket came up. `WhepClient.close()`
(`apps/shared/src/streaming/WhepClient.ts:148-150`) issues the WHEP
`DELETE` against the captured session URL and then tears down locally (spec
142). `apps/management-web/src/features/cameras/CameraDetailPage.tsx:118`
already mounts a viewer and already plumbs `getToken` from `react-oidc-context`.

**5. A camera picker already exists to copy.**
`useListAllCameraChoicesQuery` (`apps/shared/src/api/cameras.api.ts:387`, spec
048) gathers every camera the operator may choose, reports `count` and
`complete`, and is already used by
`apps/management-web/src/features/layouts/LayoutEditorDialog.tsx:98`.

---

## The decision — a captured still, plus selectable fields

Of the issue's three options:

| Option | Verdict |
|---|---|
| 1. Live WHEP frame behind the canvas | **Rejected.** |
| 2. A still frame captured from the stream | **Chosen.** |
| 3. Operator-selectable backdrops | **Chosen — it composes with 2, it does not compete.** |

**Why not live.** A live backdrop holds a WebRTC session for as long as the
dialog is open, and a dialog left open is the normal case, not the edge one — an
operator authors a label, walks to the floor to look at the bay, and comes back.
This is a 250-camera product; spec 142 exists precisely because sessions that
are not released are a real resource. A live backdrop would also put a second
decoding video element on a page that may already be showing one, for no gain:
**contrast is a property of the scene, not of the motion.** Nothing about a
moving picture helps the decision the operator is making, and the thing the
operator most needs to check — the worst-lit moment — is not the moment the
dialog happened to be open.

**Why a still is enough, and how it is taken.** On an explicit operator action
("Capture frame"), the editor opens a WHEP session against the chosen camera,
waits for `status === 'live'` — a *decoded* frame, per finding 4 — draws that
`<video>` element once into a `<canvas>`, and **immediately closes the client**,
which issues the WHEP `DELETE`. The session's lifetime is the capture, measured
in seconds, and it is bounded on both ends: it ends on success, on a timeout, on
cancel, and on dialog close. **So the question "what if the operator leaves the
dialog open" has the answer: nothing is held open, because no session outlives
the capture.** The captured image is editor-local state (a data URL), never
persisted and never sent anywhere.

**Why selectable fields as well.** A single captured frame is one moment of one
scene, and the operational failure in the issue is precisely that scenes change.
A **white field** and a **black field** are the deliberate worst cases, cost one
CSS colour each, and let an author check both extremes without a camera at all —
including on a machine where the camera is unreachable. They also give the
feature value on day one for an operator who has not chosen a camera.

**The checkerboard survives as the default**, and that is a requirement rather
than a leftover: it is the transparency-indicating backdrop, the right thing to
show when no frame is available, and #2342 will restyle it under the token
system. This spec does not convert it to tokens and does not change its colours.

---

## The contrast check — deferred, and why

**Out of scope. It is its own follow-up.** Three reasons, the third decisive:

1. **It needs a threshold policy the product has not decided.** "Below a
   legibility threshold" is a number — which contrast model (WCAG 2 ratio? APCA
   Lc?), against what, and warn versus block. The plate is *translucent*, so the
   effective foreground is a composite of the ink, the plate's alpha and the
   scene behind it; nothing in the repo decides how that is computed. That is an
   ADR-class product decision, and **the autonomous lane may not write an ADR**
   (ADR-0144).
2. **It is strictly downstream of this spec.** There is nothing to sample until a
   real frame is behind the canvas. Shipping the frame first is the smallest
   independently-shippable slice; shipping both is one slice that cannot be
   observed in halves.
3. **The frame alone already discharges the issue's operational complaint.** The
   failure today is that contrast is *unknowable*. After this spec it is
   visible. A warning makes it *automatic*, which is better, but it is a second
   increment and not this one.

**Action at phase 7:** file a follow-up issue — *"The overlay editor can sample
the region behind the label and warn on poor contrast"* — referencing this spec
and #2340. **No such issue exists today**: #2341–#2353 were checked and none of
them is the contrast check.

---

## Which camera — the editor asks, and remembers nothing

Findings 1 and 2 rule out every implicit answer. An overlay has no camera; its
dialog has no camera; a layout tile is where the two meet, and one overlay may
sit on many tiles over many cameras. Defaulting to "the first camera" would
preview against a scene the overlay may never be shown over, and would look
authoritative while being arbitrary.

**So: an explicit, optional camera picker inside the editor, defaulting to
none.** It reuses `useListAllCameraChoicesQuery` and mirrors
`LayoutEditorDialog`'s filter-and-pick shape.

**The chosen camera is editor-local state and is deliberately NOT persisted on
the overlay.** This is the load-bearing constraint of the whole spec, and it is
not a preference:

- A `CameraIdentifier` on `Overlay` would be a cross-context reference spec 004
  FR-015 forbids and ADR-0027 bans outright.
- It would contradict `Tile`'s many-to-many reuse (ADR-0112 §2) — one overlay,
  many tiles, many cameras — by privileging one of them.
- It would repeat, for cameras, exactly the mistake **ADR-0115** documents for
  fabs: an overlay is a *fab-neutral template*, and a template that names one
  camera is no longer a template.

So the preview camera is a property of the **authoring session**, not of the
artefact — a view setting. **The form's submitted payload is byte-identical to
today's** (`{ name, label }`), which is what the end-to-end procedure step 10
checks.

---

## User stories

### US1 (P1) — an author sees the label over a real frame from a real camera

An operator opens **New overlay**, picks a camera, presses **Capture frame**,
and the authoring canvas shows a still from that camera with the label sitting
on it. They drag the label to a spot where it reads, adjust the text and size
against the actual scene, and save the draft.

**This story is the whole slice and ships alone.** It is independently
observable end to end: one dialog, one camera, one frame, one saved draft.

### US2 (P2) — an author checks the worst case without a camera

The same operator switches the backdrop to **White field**, sees the translucent
white plate nearly vanish against it, enlarges the type, then switches to
**Black field** to confirm the dark ink still reads. Then back to **Checkerboard**.

### US3 (P3) — an author whose camera cannot be reached is told so and is not blocked

The chosen camera is offline, or the capture produces no frame inside its
window. The canvas stays on the checkerboard, a plain sentence says the frame
could not be captured, and **saving the overlay is never prevented**. The editor
degrades to exactly today's behaviour.

---

## Functional requirements

**Backdrop selection**

- **FR-001** — `OverlayEditor` renders its canvas over one of four backdrops:
  `checkerboard` (default), `captured-frame`, `white`, `black`.
- **FR-002** — With `checkerboard` selected, the canvas paints the **existing**
  `repeating-linear-gradient(45deg, #1f2937, #1f2937 12px, #111827 12px, #111827 24px)`,
  byte-for-byte. This is behaviour-preserving and is characterised, not changed.
- **FR-003** — `white` paints `#ffffff`; `black` paints `#000000`. No alpha, no
  gradient — the point is an unambiguous extreme.
- **FR-004** — `captured-frame` is only selectable once a frame has been
  captured. With no frame, the option is disabled and the backdrop stays on
  whatever is selected.
- **FR-005** — The label, the drag/resize behaviour, the text input and the
  font-size slider are **unchanged** by the backdrop. Changing backdrop emits no
  `onChange` and mutates no `OverlayLabel` field.

**Capturing a frame**

- **FR-006** — The editor offers a camera picker sourced from
  `useListAllCameraChoicesQuery`, defaulting to no camera selected, and
  surfacing `count`/`complete` the way `LayoutEditorDialog` does when the list is
  truncated.
- **FR-007** — **Capture frame** is disabled until a camera is selected.
- **FR-008** — A capture opens **exactly one** WHEP session, via the existing
  `useWhepSession`, against the selected camera.
- **FR-009** — The frame is taken only once `useWhepSession` reports
  `status === 'live'` — i.e. a frame was decoded (spec 094), never on transport
  `connected` alone.
- **FR-010** — The moment a frame is drawn, the session is closed. Closing
  issues the WHEP `DELETE` via `WhepClient.close()`.
- **FR-011** — A capture that has not produced a frame within **10 seconds** is
  abandoned: the session closes, the backdrop is unchanged, and FR-016's message
  is shown. (10 s is over three times spec 002 FR-013's 3 s p95 click-to-first-
  frame budget — generous enough that a slow but working camera succeeds, short
  enough that an unreachable one does not hold a session. **Chosen, not
  measured.**)
- **FR-012** — Cancelling a capture in flight closes the session.
- **FR-013** — Closing the dialog, or unmounting the editor, closes any session
  in flight.
- **FR-014** — **When no capture is in flight, no WHEP session exists.** This is
  the resource claim the design rests on and is asserted as a negative.
- **FR-015** — A captured frame is held in memory as a data URL in editor-local
  state. It is **not** submitted with the form, not stored, and not sent to any
  server. Capturing a second frame replaces the first.

**Degradation**

- **FR-016** — A failed or timed-out capture shows one plain sentence
  (`role="alert"`), leaves the backdrop as it was, and leaves the form
  submittable.
- **FR-017** — A camera list that fails to load leaves the picker empty and the
  capture button disabled; the checkerboard, white and black backdrops all keep
  working.
- **FR-018** — The editor passes **no** `playoutTargetMilliseconds` and
  registers **no** lag or decode sampler. It is not a wall tile (ADR-0128,
  ADR-0129).

**Fidelity — the canvas keeps its shape**

- **FR-019** — **The captured frame is fitted *into* the fixed canvas; the
  canvas never resizes to the frame.** The 800×450 box stays 800×450, the still
  is scaled to *contain* within it, centred, and any remainder is black.

  This is not cosmetic. `OverlayEditor`'s own comment says the fixed aspect ratio
  is what keeps normalized coordinates resolution-independent, and on the wall
  `CameraViewer` renders `aspect-video` + `bg-black` with the `<video>` at
  `object-contain`, then positions the label as a **percentage of the box, not
  of the picture** (`CameraViewer.tsx:396-401`). Letterboxing the still inside a
  16:9 box reproduces the wall exactly. Letting the canvas follow a 4:3 camera's
  intrinsic aspect would make every coordinate the operator drags mean something
  different from what the wall paints — it would *introduce* a WYSIWYG defect
  while fixing one.

  Achieved with `background-size: contain`, `background-position: center`,
  `background-repeat: no-repeat` over `background-color: #000000` — the same
  semantics as the wall's `object-contain` over `bg-black`, and no new layout
  primitive.

**Constitution**

- **FR-020** — Every request the feature makes is to the in-fab gateway. No
  external host, no font, no CDN, no image service (§I).

---

## Acceptance scenarios

### Happy — a frame is captured and the label sits on it (US1)

```gherkin
Given the overlay editor is open with the checkerboard backdrop
And the operator has selected camera "cam-42"
When the operator presses "Capture frame"
And the WHEP session reports a decoded frame
Then the canvas backdrop shows the captured still
And the backdrop selector reads "Captured frame"
And the label remains at its authored normalized coordinates
And the WHEP session has been closed
```

### Happy — the session does not outlive the capture (US1, FR-010/FR-014)

```gherkin
Given a capture has completed and the backdrop shows the captured still
When the operator continues dragging the label for as long as they like
Then no WHEP session is open
And no further WHEP POST is issued
```

### Happy — the worst case is checkable without a camera (US2)

```gherkin
Given the overlay editor is open and no camera has been selected
When the operator selects the "White field" backdrop
Then the canvas paints #ffffff
And no WHEP session is opened
And selecting "Black field" paints #000000
And selecting "Checkerboard" restores the original repeating-linear-gradient
```

### Happy — a non-16:9 camera is letterboxed, not stretched (FR-019)

```gherkin
Given the operator has selected a 4:3 camera
When a frame is captured
Then the canvas is still 800x450
And the still is centred within it with black bars, not stretched
And the label's normalized coordinates address the 800x450 box
  exactly as CameraViewer's label addresses its aspect-video box
```

### Conflict — a second capture while one is in flight

```gherkin
Given a capture against "cam-42" is in flight
When the operator presses "Capture frame" again
Then the control is disabled and no second session is opened
```

### Conflict — the camera is changed mid-capture

```gherkin
Given a capture against "cam-42" is in flight
When the operator selects camera "cam-7"
Then the session against "cam-42" is closed
And no frame from "cam-42" is applied to the backdrop
```

### Bad request — the camera produces no frame (US3, FR-011/FR-016)

```gherkin
Given the operator has selected an offline camera
When the operator presses "Capture frame"
And no frame is decoded within 10 seconds
Then the session is closed
And the backdrop is unchanged
And an alert reads that the frame could not be captured
And the Save button is still enabled
```

### Bad request — the stream lookup fails

```gherkin
Given the streams endpoint answers an error for the selected camera
When the operator presses "Capture frame"
Then no backdrop change occurs
And the same could-not-capture alert is shown
And the editor otherwise behaves exactly as it does today
```

### Teardown — the dialog is dismissed mid-capture (FR-013)

```gherkin
Given a capture is in flight
When the operator closes the dialog
Then the WHEP session is closed and its DELETE is issued
```

### Auth — the token the viewer already uses, and nothing new

```gherkin
Given the operator is signed in through react-oidc-context
When a capture opens a WHEP session
Then it presents the same bearer token CameraDetailPage already passes to CameraViewer
And a camera in another fab answers the same refusal it answers CameraDetailPage
And that refusal renders as the could-not-capture alert, disclosing nothing further
```

**No new scope is introduced.** The feature calls `camera-catalog/cameras` and
the streams endpoint, both of which `apps/management-web` already calls with the
operator's existing scopes. An operator who cannot see a camera cannot pick it,
and cannot capture from it — enforced server-side exactly as it is today.

---

## Independent end-to-end test procedure

Runnable by a person with no knowledge of the implementation.

1. Boot the Aspire stack and sign into `management-web` as an operator with
   camera access.
2. Go to **Overlays** and press **New overlay**. **Observe:** the canvas is the
   grey diagonal checkerboard, exactly as before this change.
3. Switch the backdrop to **White field**. **Observe:** the canvas is plain
   white and the translucent white plate is hard to see — this is the defect the
   issue describes, now visible at authoring time.
4. Switch to **Black field**, then back to **Checkerboard**. **Observe:** each
   paints as named; the label has not moved.
5. Pick a camera that is streaming and press **Capture frame**. **Observe:**
   within a few seconds the canvas shows a still picture from that camera, and
   the label sits on it.
6. **The resource claim:** with the dialog still open on the captured frame, open
   the MediaMTX / SFU session list. **Observe:** no session for that camera
   attributable to this dialog. Leave the dialog open a further minute and
   re-check. **Observe:** still none.
7. Drag the label from a dark part of the frame to a bright one. **Observe:**
   legibility visibly changes — which is the entire point of the issue.
8. Pick a camera that is offline and press **Capture frame**. **Observe:** after
   at most ten seconds, a sentence saying the frame could not be captured; the
   backdrop unchanged; **Save as draft** still enabled.
9. Press **Capture frame** on a working camera and close the dialog immediately.
   **Observe:** no session is left behind.
10. Save the draft. **Observe:** the overlay is created with the same
    `{ name, label }` payload as before — **the request body is unchanged by this
    feature**, which is the proof that nothing about the preview leaked into the
    artefact.

---

## Locked tech choices (nothing new is introduced)

| Concern | Choice | Where it already lives |
|---|---|---|
| Streaming session | `useWhepSession` + `WhepClient` | `apps/shared/src/ui/composites/useWhepSession.ts` |
| Session release | `WhepClient.close()` → WHEP `DELETE` | spec 142 |
| Camera list | `useListAllCameraChoicesQuery` (RTK Query, ADR-0075) | `apps/shared/src/api/cameras.api.ts:387` |
| Picker shape | mirror `LayoutEditorDialog` | `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx:85-98` |
| Token | `react-oidc-context` via a stable `getToken` ref (ADR-0080) | `CameraDetailPage.tsx:20-38` |
| Frame grab | `CanvasRenderingContext2D.drawImage` + `toDataURL('image/png')` | **new — no prior art in this repo** |
| Tests | vitest + Testing Library, `// @vitest-environment jsdom` per file | `CameraViewerMedia.test.tsx` |

**No new dependency.** `drawImage`/`toDataURL` are platform APIs.

---

## Latency-budget impact (constitution §IV) — none, and here is why

**N/A — the editor is not on the event-to-overlay path.**

The 800 ms SLO runs *event arrival → overlay rendered* on a **kiosk wall tile**.
The overlay editor is an authoring dialog in `apps/management-web`: no event
reaches it, no wall renders it, and it paints one label rather than up to 250.
The ≤ 50 ms composite-and-render leg is a budget **the wall** spends, and this
change spends none of it.

**Nothing on the wall's path is touched.** The change is confined to
`OverlayEditor.tsx` (mounted only by `OverlayEditorDialog`) and a new
editor-local module. `CameraViewer.tsx`, `useWhepSession.ts`, `WhepClient.ts`
and `CellPage.tsx` are **read and reused, not modified** — see plan.md
§File ownership. If any task finds itself editing `useWhepSession.ts`, that is a
signal to stop and re-scope, because it would put an editor concern on a
250-tile hot path.

**The one shared-resource cost is named rather than waved away.** A capture
takes one SFU session for a few seconds. That is a load on the SFU, not on the
latency budget, and FR-010/FR-011/FR-014 bound it: at most one, only during a
capture, released on every exit path. The end-to-end procedure step 6 observes
the release rather than asserting it.

**Deliberately not done for the budget's sake:** the editor passes no
`playoutTargetMilliseconds` and starts no decode or lag sampler (FR-018). Those
are wall instruments; a single authoring dialog has nothing to align against,
and `CameraViewer`'s own comments already say so.

---

## The ADR question — answered: no ADR needed

Checked against the locked decisions. This change makes no architectural choice
that is not already made:

- **Frontend-only, in `apps/shared` + `apps/management-web`** — ADR-0074's shape.
- **RTK Query for the camera list** — ADR-0075, and the exact hook spec 048 built.
- **No cross-context reference, no contract change, no migration** — it touches
  no `src/` project at all.
- **The one genuinely new technique** — drawing a `<video>` into a `<canvas>` —
  is a browser API used at one call site for one purpose. Under ADR-0036 that is
  a call, not a decision.

**The contrast check *would* need an ADR** (what threshold, what model, warn or
block), which is the second reason it is deferred: the lane may not write one.

---

## Out of scope

- **The contrast check** — deferred with reasons above; follow-up filed at phase 7.
- **Token conversion of the editor's inline styles** — #2342. The new controls
  therefore follow the editor's **existing** inline-style idiom rather than
  Radix/Tailwind, so #2342 converts one file in one pass instead of chasing a
  half-converted component. ADR-0077/0078 are deliberately not applied here, and
  ADR-0146's direction is read but not implemented.
- **A live backdrop** — rejected above.
- **Persisting the preview camera on the overlay** — would invent a domain
  binding; needs a contract change and an ADR.
- **The editor's canvas being a preview of a tile whose real dimensions are
  unknown** — #2353 and #2350. A captured frame makes the *scene* real; it does
  not make the *geometry* real, and this spec does not claim otherwise.
- **Multi-label, z-order, keyboard placement, undo** — #2343–#2348.
- **Restyling the checkerboard** — explicitly preserved byte-for-byte (FR-002).

---

## Dependency on PR #2354 (#2339's fold) — and what happens if it does not land

PR #2354 adds `apps/shared/src/ui/composites/overlayLabelStyle.ts` and makes
both `OverlayEditor` and `CameraViewer` cite it. This spec **assumes it lands**
and builds on `develop` after it.

**The dependency is soft, and it is worth being precise about why.** This spec
changes the **backdrop**; #2339 changed the **plate**. The two touch
`OverlayEditor.tsx` in different places — #2339 replaced the `<Rnd>` `style`
object, this spec replaces the canvas `background` and adds controls around it.

- **If #2354 lands first (expected):** rebase onto it. The only interaction is a
  textual conflict risk in `OverlayEditor.tsx`, resolved by keeping #2354's
  `overlayLabelSurfaceStyle(...)` spread untouched — **this spec must not alter
  the plate at all**, because #2354's entire proof is that the wall's output did
  not move.
- **If #2354 does not land:** this spec still ships unchanged. It never imports
  `overlayLabelStyle.ts` and never reads the plate's colours. The one thing that
  degrades is the *value* of the preview: authoring against a real frame is
  worth more when the previewed plate matches the wall's, so without #2339 the
  backdrop is real and the plate is still 0.92-alpha with a border it will not
  have on the wall. That is #2339's defect, not this one's.
- **What must not happen:** this branch must not stack on
  `fix/2339-one-label-renderer`. It is cut from `origin/develop` and stays
  there. If #2354 merges mid-flight, rebase onto `develop` (CLAUDE.md
  §Stacked PRs — rebase-merge renames the SHAs).

**A guard is cheap and is therefore taken:** the characterisation test in
FR-002/T001 pins the checkerboard string, so a rebase that mangles the canvas
fails loudly rather than silently.

---

## Assumptions, marked

1. **`drawImage` from a WebRTC `<video>` does not taint the canvas.** A
   `srcObject` MediaStream is same-origin-clean, so `toDataURL` should not throw
   `SecurityError`. **This is an assumption about browser behaviour that jsdom
   cannot test** — it is on the phase-5 procedure (step 5), and a `try/catch`
   around the grab routes a failure into FR-016's message rather than an
   uncaught throw.
2. **A 10 s capture window is chosen, not measured** (FR-011). It is stated as a
   named constant so a later measurement can move it.
3. **PNG, not JPEG.** `toDataURL()` defaults to PNG; a fab scene at 1080p is a
   few hundred KB in memory for the life of a dialog. Not optimised, because no
   measurement says it needs to be (ADR-0036).
4. **The operator picks the camera they care about.** The editor offers no
   "representative" and no default, per §Which camera.
5. **`status === 'live'` in jsdom fires on `connected`**, because
   `getVideoPlaybackQuality` is absent there and `useWhepSession` deliberately
   treats an absent instrument as not-evidence-of-absence. Tests therefore prove
   the *wiring and lifecycle*, and phase 5 proves the *picture*. Said plainly
   rather than papered over.

---

## Gate — phase 1

Spec reviewed. **No `[NEEDS CLARIFICATION]` remains**: the three questions the
issue left open — which option, which camera, whether the contrast check is in —
are each answered above from evidence read out of the repository, with the
evidence cited by file and line.
