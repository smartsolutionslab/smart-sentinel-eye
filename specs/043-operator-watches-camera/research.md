# Phase 0 Research: An operator can watch a camera

**Feature**: 043 | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)

Every answer below came from reading the code that already exists. Nothing here
needed a running stack, because nothing here is new behaviour — the viewer works,
and has been watched carrying real video on a kiosk wall. What is new is that an
operator can reach it.

---

## R1 — Where the token comes from

**Decision**: `useAuth()` in `CameraDetailPage`, with the getter held stable
behind a ref:

```tsx
const accessTokenRef = useRef(auth.user?.access_token);
accessTokenRef.current = auth.user?.access_token;
const getToken = useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);
```

**Rationale**: it is the pattern already in the repository, in `CellPage`, put
there by spec 042 after a fresh `getToken` identity on every render silently
rebuilt the effects underneath `CameraViewer` and killed a sampler outright
(issues 1888/1889). `useWhepSession` also holds `getToken` behind its own ref for
the same reason and says so. A second page reaching for the same component wants
the same guard, and the comment travels with it.

**Alternatives considered**:

- *Export a getter from `apps/shared/src/api/gateway.ts`.* The module already
  holds `accessTokenProvider`, set once by `App.tsx`, and a getter would give the
  viewer and every REST call one token source. Attractive, and rejected: it adds
  public surface to a **contention file** (ADR-0109) for one caller, and the
  provider's type returns `string | undefined` where the viewer wants
  `Promise<string | null>`, so an adapter is needed either way.
- *A shared `useAccessToken()` hook.* Two callers is not three. Speculative
  generality (ADR-0036).

**On the apparent tension with spec 042**, which spent a feature removing a
second copy of one fact: this is not that. Both pages are *consumers* of one auth
context, not two sources of a claim. What spec 042 removed was two mechanisms
answering the same question differently; here they read the same answer.

---

## R2 — `CameraViewerPanel` is deleted, not reshaped

**Decision**: delete `CameraViewerPanel.tsx` and `CameraViewerPanel.test.tsx`.
`CameraDetailPage` renders `CameraViewer` directly.

**Rationale**: strip the dialog framing FR-002 removes — `role="dialog"`, the
fixed right-edge positioning, the header with the camera's name (the page already
has one), the Close button, the `onClose` prop — and what remains is one
`<CameraViewer>` and a caption. A component whose entire body is one child, on a
page that already supplies its context, is a layer that only has to be read.

Its three tests describe a panel that opens and closes. Half that behaviour
ceases to exist, and FR-010 says tests outliving their subject are how a
component keeps looking supported.

**The caption goes too**: *"Live feed served via WebRTC (WHEP). Reconnects
automatically on transient outages."* `CameraViewer` reports its own state —
Connecting…, Reconnecting…, Stream is offline, Viewer error — so the sentence
restates in prose what the component says in situ, and the half about WHEP is an
implementation note for an operator who cannot act on it.

**Alternatives considered**: rename it to `CameraViewerSection` and keep it as a
seam for later layout work. Rejected — that is a seam for a need that does not
exist, and the page is 145 lines.

---

## R3 — Where the picture goes, and how big

**Decision**: directly beneath the header, above the record fields, in a
width-bounded container. The `<dl>` stays a `<dl>`.

**Rationale**: it matches the layout chosen when this was decided, and it reads
in the order an operator thinks — which camera, what it sees, what is on file.

**Measured, not assumed**: `CameraViewer`'s root already carries
`relative aspect-video w-full overflow-hidden rounded-md bg-black`. It is
self-sizing given a width, so the page supplies a maximum width and nothing else.
No aspect or height constraint is needed, and adding one would fight the
component.

The `<dl>` is a description list of a camera's record, which is what it still is.

---

## R4 — What a retired camera says

**Decision**: no viewer, and the existing notice gains a clause about the stream.

The page already renders, for a retired camera:

> This camera is retired. Its record is kept, but it can no longer be changed.

**That sentence does not cover the absent picture.** A reader can conclude the
video is merely broken, which is exactly the ambiguity FR-004 exists to remove —
and the page's own comment insists a refusal be "visible before the attempt"
rather than discovered. The notice says the stream is gone as well.

**Alternatives considered**: render the viewer and let it report "Stream is
offline". Rejected — it would be the one control on the page that offers
something it cannot deliver, on a page whose every other control is hidden for
exactly that reason. Retirement stops the stream deliberately (the retire
confirmation says "live stream will stop"), so a viewer reporting failure would
describe an intended outcome as a fault.

---

## R5 — Which check proves reachability, and what neither proves

**Decision**: **both**, because they fail for different reasons.

| Check | Fails when | Does **not** cover |
|---|---|---|
| `CameraDetailPage.test.tsx` — page mounts the viewer for an active camera | the page stops rendering it | whether the component works at all (it is stubbed) |
| `e2e/camera-detail.spec.ts` — open a camera, find a `<video>` | the page does not reach a real viewer | whether a picture appears |

**Neither proves a picture appears, and nothing automated can.** `camera-sim` and
`scenario-simulator` both sit inside `if (isRunMode && !isE2ETests)`, so a CI
browser gets no video. That is Phase 5's job, and saying otherwise would repeat
the failure this feature exists to correct — a green check standing in for
something nobody exercised.

**On stubbing**: `CameraViewerPanel.test.tsx` already stubs `CameraViewer` with
`vi.mock`, and its comment says why — *"CameraViewer mounts a WhepClient that
talks to RTCPeerConnection. In the jsdom test environment those globals don't
exist."* The page test takes the same approach.

---

## R6 — How FR-007 is asserted

**Decision**: the stubbed `CameraViewer` captures the `getToken` prop it was
handed; the test calls it and asserts the result is the token the mocked auth
provides.

**Rationale**: this is the assertion the current code would fail. The placeholder
reads `sessionStorage.getItem('keycloak:access_token')` — a key nothing writes —
so it resolves to `null` while still rendering perfectly. **A viewer wired to a
credential nobody issues fails identically to no viewer at all**, and passes any
check that only asks whether something rendered.

**Alternatives considered**: asserting the source *isn't* the placeholder (a
negative on an implementation detail, which survives being reintroduced under
another name), or an e2e that watches for a `401` on the WHEP handshake
(no video in CI, so nothing to watch).

---

## R7 — An existing test will break, and that is the vehicle

`CameraDetailPage.test.tsx` renders the page with `cameras.api` mocked and **does
not stub `CameraViewer`**. Mounting one there pulls a `WhepClient` and a
5-second RTK Query poll into jsdom, where `RTCPeerConnection` does not exist.

**It will break**, and adding the stub is both the fix and the mechanism for
R5's and R6's assertions. Its ten existing tests must keep passing unchanged —
including the two that assert a retired camera is offered no rename and no
retirement, which the new retired-viewer assertion sits beside.

---

## R8 — Nothing about authorization is in the way

management-web signs in as `smart-sentinel-eye-web`. Its token carries
`sse.management`, which the WHEP gate accepts (spec 041 changed that gate to
`sse.streams.read` *or* the grandfathered bundle), and it carries `sub`, which
`WhepAuthValidator` requires — from `sse-identity` since spec 042 merged, and
from `sse.management`'s own mapper before that.

So the missing pieces really are only the mount and a real token getter. If that
turns out to be wrong when a person watches the page, it is a finding, not a
licence to widen a scope.
