# Data Model: Retire a camera from the management app

**Feature**: `032-retire-camera-ui` · 2026-08-24

**This feature introduces no new data.** It adds a caller for an existing
endpoint. What follows is the state it reads, the one transition it triggers,
and the cache consequences — recorded because the cache behaviour is what makes
three requirements hold at once, and that is not obvious from the requirements
themselves.

---

## State read

From the existing `CameraDetail` (spec 029's `GET /cameras/{camera}`):

| Field | Used for |
|---|---|
| `name` | The confirmation names the camera (**FR-003**) |
| `status` | Decides whether the retire control exists at all (**FR-004**) |
| `cameraIdentifier` | The request path |
| `version` | **Not used.** Retire is unversioned (**FR-016**) |

`version` is listed precisely because it is *available and must not be sent*.
The camera page already holds it for the address-correction flow, so threading
it into the retire call is the natural mistake.

---

## The one transition

```
Registered ──retire──▶ Decommissioned
                            │
                            └──retire──▶ Decommissioned   (idempotent, 204)
```

Terminal. There is no reverse edge, by spec 028's decision.

**The self-loop is the feature's central awkwardness.** The second arrow
succeeds and is indistinguishable from the first at the client. That is what
**FR-012** is about: the app cannot know which arrow it just traversed, so it
must not narrate one.

### Terminology

The API and domain say `Decommissioned`; every operator-facing word is
**retired**. Pre-existing, load-bearing in shipped code, and deliberately not
resolved here — the spec's checklist records why.

---

## Cache effects

The part worth writing down. One mutation, two invalidations, three
requirements satisfied:

| Invalidated tag | Subscriber | Effect | Requirement |
|---|---|---|---|
| `{ Camera, id }` | The mounted detail page | **Refetches**, re-renders with `status: Decommissioned` | **FR-009** — new state, no full reload |
| `{ Camera, id }` | — | The refetch **succeeds**; a retired camera still reads | **FR-011** — its address still opens |
| `{ Camera, id: 'LIST' }` | The listing, when next visited | Refetches without the camera; the API excludes retired by default | **FR-010** — gone from the listing |

**FR-011 needs no code.** It holds because invalidation refetches rather than
evicts, and because the endpoint still serves retired cameras. Worth stating so
nobody adds machinery to preserve something that was never at risk — and so
that if FR-011 ever breaks, the search starts at the endpoint rather than at the
cache.

---

## What is deliberately not modelled

- **No optimistic update.** The status changes only when the server says so.
  Optimism here would render `Decommissioned` before the server agreed, and the
  one case where that is a lie is the case FR-012 exists for.
- **No local "I retired this" flag.** It would make FR-012 expressible —
  narrate authorship when the flag is set — and it would be wrong, because the
  flag records that *a request was sent*, not that it *caused* the transition.
- **No new client-side entity.** The confirmation holds no state beyond
  open/closed and in-flight.
