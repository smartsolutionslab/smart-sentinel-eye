# Contract: the camera detail surface

**Feature**: `030-camera-detail-view` · 2026-08-24

A UI contract rather than an API one — spec 029 already fixed the wire, and this
feature adds no server surface. What follows is what the app must present and,
more importantly, what it must **not**.

---

## Locations

| Path | Shows |
|---|---|
| `/cameras` | The camera list, as today |
| `/cameras/:cameraIdentifier` | One camera |
| `/layouts`, `/overlays`, `/rules`, `/system-variables`, `/audit` | The surfaces the shell toggles today |
| `/oidc/callback` | Must exist. `react-oidc-context` intercepts it before the router sees it, but the router still needs the route — kiosk-web records the same requirement |

`/cameras/:cameraIdentifier` is the whole of FR-002: linkable, bookmarkable,
reloadable, and returned from with the browser's back control.

## What one camera shows

Fab, name, address, registration time, status — and, when it may be corrected,
a control to correct the address.

**The version is never shown.** It is machinery for the correction, not
information an operator can act on. It has to be *held*, not displayed.

## Correcting the address

- Validated **before** it is sent, with the same rules registration uses
  (FR-009), so a predictable rejection costs no round trip.
- Sent with `If-Match` carrying the version the operator was shown (FR-005).
- On success the displayed camera is what came back from the server, never what
  was typed (FR-004).
- On refusal, what the operator typed is **kept**, so they are not made to
  retype it.

### Refusals, in words

| Cause | The operator is told |
|---|---|
| Stale version (412) | Someone else changed this camera; reload to see their version, then reapply. **Not "try again"** — resubmitting replays over their change |
| Retired (409) | This camera is retired; its address cannot be changed. **Not** "someone else changed it" — nobody did, and reloading will not help |
| Not found (404) | No such camera |
| Bad address (400) | Should not be reachable — caught client-side first |
| No `If-Match` (428) | **Unreachable by design.** The client always holds a version; if this ever renders, the client has a bug rather than the operator having made a mistake |

The first two are the substance. Both currently map to the wrong words through
the shared helpers — see [research.md](../research.md) §5 — which is why this
feature changes `problemDetail.ts` rather than only calling it.

## A retired camera

Opens. Is visibly marked retired. Offers **no** edit control — the refusal is
visible before the attempt, not after it (FR-007). Retirement takes a camera out
of the default listing, not out of existence, and the audit trail refers to it.

## What the app must not add

**A camera in a fab the operator does not hold must be reported exactly as one
that does not exist** (FR-008). The API already answers identically for both; a
UI can undo that in a single helpful sentence.

The failure mode is a well-meaning branch that says *"you do not have access to
this camera"*. That reintroduces, at the last hop, the enumeration spec 029's
FR-006 and SC-003 were built to prevent — and a camera record carries its RTSP
address.

There is nothing to implement here. There is something to **not** implement, and
it is exactly the kind of thing added later as an improvement.

## Not in this contract

- **Renaming** — the API does not accept it (#1850).
- **Retiring** — endpoint unused since spec 028; its own issue, not folded in here.
- **Live video** — the existing viewer panel keeps its job.
- **Bulk anything** — one camera per view.
