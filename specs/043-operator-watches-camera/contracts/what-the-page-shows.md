# Contract: what a camera's page shows, and to whom

**Feature**: 043 | **Date**: 2026-08-26

No HTTP contract changes. What is a contract is what the page renders for a
camera in each state, and what it hands the viewer — because the current failure
is a component that renders correctly and reaches nobody.

---

## 1. The page, by camera state

| | Active camera | Retired camera |
|---|---|---|
| Name, fab, address, registered, status | shown | shown |
| Rename / Correct the address / Retire | offered | **hidden**, and the page says why |
| **Live picture** | **shown** | **hidden**, and the page says why |
| Retired notice | absent | present, covering both the record and the stream |

**The last two rows are the feature.** The right-hand column is not new
behaviour — it is the rule the page already keeps for every control, extended to
the one thing that was missing.

---

## 2. What the viewer is handed

| Prop | Value | Why it matters |
|---|---|---|
| `cameraIdentifier` | the camera being viewed | — |
| `getToken` | **the operator's session token**, behind a stable identity | a placeholder resolving to `null` renders identically and fails identically to no viewer |

**Stable identity is part of the contract, not a style note.** A fresh function
each render rebuilds the effects inside `useWhepSession`, which tears down and
re-establishes the peer connection. Spec 042 fixed exactly that in `CellPage`
after it silently killed a measurement; `useWhepSession` guards its own copy the
same way and says so.

---

## 3. What the viewer reports for itself

`CameraViewer` renders its own state — Connecting…, Live, Reconnecting…, Stream
is offline, Viewer error. The page adds no second indicator.

An operator opening a camera **because** it is misbehaving is the case this
feature is for, so a viewer reporting a fault is a success, not a failure. The
one state the page refuses to reach is the retired camera, where "offline" would
describe an intended outcome as a fault.

---

## 4. What must fail

| Break this | Page test | e2e | Phase 5 |
|---|---|---|---|
| Viewer not rendered on the page | **red** | **red** | — |
| Viewer handed the placeholder credential | **red** | green | — |
| Viewer rendered for a retired camera | **red** | **red** | — |
| Retired notice silent about the stream | **red** | — | — |
| `CameraViewer` itself broken | green | green | **only here** |

The bottom row is stated rather than solved. The page test stubs the composite —
jsdom has no `RTCPeerConnection` — and CI produces no video, `camera-sim` and
`scenario-simulator` both sitting inside `if (isRunMode && !isE2ETests)`.

**So no automated check proves a picture appears.** Three features running have
had a row like this, and each time the temptation is to let a green suite imply
it. It does not.
