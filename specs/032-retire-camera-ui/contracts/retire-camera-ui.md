# Contract: Retiring a camera from the management app

**Feature**: `032-retire-camera-ui` · 2026-08-24

A UI contract. The HTTP contract already exists and is unchanged — spec 028
defined it, and this feature's only obligation to it is **FR-016**: send no
expected-version precondition.

---

## The endpoint, restated to be checked against

```
POST /cameras/{camera}/retire
```

| | |
|---|---|
| Scope | `sse.cameras.write` |
| Precondition header | **none** — not `If-Match`, not anything |
| `204` | Retired. **Also** the answer when it was already retired |
| `400` | Malformed identifier, or no usable fab |
| `403` | Caller named a fab they do not hold |
| `404` | No such camera **in the caller's fabs** — including when it exists in another |

No `409`, no `412`, no `428`. Verified against the endpoint's declaration, not
assumed. **If the implementation needs one of those, spec 028's contract is
wrong and that is a finding to raise.**

---

## The control

Rendered on the camera detail page, beside the address-correction control.

| Camera status | Retire control |
|---|---|
| `Registered` | Present |
| `Decommissioned` | **Absent** |

Absent, not disabled (**FR-004**). The address-correction control three lines
above already behaves this way (spec 030 FR-007), and the assertion is
`toHaveCount(0)` — not "clicking it fails".

---

## The confirmation

Semantically an **alert dialog**: `role="alertdialog"`, focus defaulting to the
cancelling action.

It must contain, each independently assertable:

| # | Content | Requirement |
|---|---|---|
| 1 | The camera's **name** | FR-003 |
| 2 | That retirement is **permanent and cannot be undone** | FR-005 |
| 3 | That the **live stream stops** | FR-006 |
| 4 | That the **name becomes available for reuse** in that fab | FR-007 |

Two controls: confirm (danger-styled) and cancel. Cancelling retires nothing
(**FR-008**). While the request is in flight the confirm control is not
actionable again (**FR-015**).

### Wording is part of the contract

The confirmation describes **the operation**, in the future tense: *"this will
stop the live stream"*. It does not describe **the outcome** or its cause.

After success, the page shows the camera as retired and the control is gone.
**Nothing says "you retired this camera"** (**FR-012**) — the endpoint answers
`204` whether or not this operator caused the transition, so any past-tense
claim of authorship is unsupportable. There is no toast infrastructure and none
is added; the page state is the feedback.

---

## Refusals

Via the shared vocabulary settled by ADR-0119 (**FR-014**). No new predicate is
added, and the reason is that none is needed:

| Refusal | Handling |
|---|---|
| `404` | The page already renders not-found. **Identical** for another fab's camera (FR-013) |
| `403` | The existing fab-authorization path |
| `400` | The existing malformed-request path |
| stale version | **Cannot occur** — nothing is versioned here |
| `CAMERA_RETIRED` | **Cannot occur** — retire is idempotent, not refused |

`isTerminalRefusal` exists for the *address-correction* flow, where retirement
is a refusal. Here retirement is the goal. The two must not be conflated.

---

## Non-enumeration, restated because this feature can break it

A camera the operator may not see must render **exactly** as one that never
existed — including now that the page carries a control that could distinguish
them. *"No retire button, because this isn't yours"* and *"no retire button,
because there is no camera"* must be indistinguishable.

Compared field for field (**SC-004**), not by observing that both showed an
error. A camera record carries its RTSP address, so a distinguishable refusal is
an enumeration oracle — the same property spec 029 FR-006 and spec 030 FR-008
carry, inherited rather than re-argued.

---

## Cache

`retireCamera` invalidates:

```
{ type: 'Camera', id: <cameraIdentifier> }
{ type: 'Camera', id: 'LIST' }
```

Identical to `changeCameraAddress`. See [data-model.md](../data-model.md) for
why one invalidation satisfies FR-009, FR-010 and FR-011 together.

---

## Not in this contract

- **No `DELETE`.** Retirement keeps the record (spec 028).
- **No un-retire.** Terminal by decision.
- **No bulk retire.** Different blast radius.
- **No retire from the listing.** A destructive action reached from a dense row
  of many cameras is a misclick waiting to happen.
