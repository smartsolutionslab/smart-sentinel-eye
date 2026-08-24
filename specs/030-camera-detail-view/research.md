# Phase 0 Research: Open one camera, and fix it

**Feature**: `030-camera-detail-view` · **Spec**: [spec.md](./spec.md) · 2026-08-24

Five questions. Four came back reassuring — the patterns this feature needs all
exist and it can follow them. **One came back against spec 029**: the refusal
codes that feature chose do not match the ones the frontend's shared error
helpers understand, so the operator-legible messages FR-006 asks for would come
out wrong.

---

## 1. What does converting the shell actually cost?

**Decision: 5 e2e specs and 1 unit test. Sized, not guessed.**

`App.tsx` toggles six surfaces with `useState` and renders nav **buttons**.
Converting them to router links changes their accessible role from `button` to
`link`, which breaks every selector that finds them by role:

| Touched | Why |
|---|---|
| `e2e/audit.spec.ts` | navigates by `getByRole('button', { name: 'Audit' })` |
| `e2e/layouts.spec.ts` | same, Layouts |
| `e2e/overlays.spec.ts` | same, Overlays |
| `e2e/rules.spec.ts` | same, Rules |
| `e2e/system-variables.spec.ts` | same, System variables |
| `apps/management-web/src/App.test.tsx` | asserts the shell's toggle behaviour |

`e2e/cameras.spec.ts` is **not** affected — cameras is the default surface, so
it never clicks a nav button to get there.

**The tempting way to avoid all six** is to keep the nav as buttons that call
`useNavigate()`. Every selector keeps working and nothing needs touching.
**Rejected**: a control that changes location is a link, and rendering it as a
button loses middle-click, open-in-new-tab, and copy-link — affordances an
operator gets for free from a routed app and the substance of what FR-002 is
asking for. Keeping the selectors green by making the markup wrong is paying for
the router and not collecting.

So the six updates are the honest cost of the decision the spec recorded, and
they are mechanical. **If the Phase 2 gate overturns the shell conversion**,
five of the six disappear — only the cameras surface routes, and no existing
nav selector moves.

---

## 2. Is there a router pattern to follow?

**Decision: yes — `apps/kiosk-web/src/app/router.tsx`. Copy its shape.**

```tsx
export const router = createBrowserRouter([
  { path: '/', element: <PickerPage /> },
  { path: '/layouts/:layoutIdentifier', element: <CellPage /> },
  { path: '/oidc/callback', element: <PickerPage /> },
]);
```

Three things worth carrying over rather than rediscovering:

- **`createBrowserRouter` + `RouterProvider`**, not the `<Routes>` element form.
- **The OIDC callback needs a path.** kiosk-web's comment records that
  `react-oidc-context` intercepts `/oidc/callback` before the router sees it,
  but a route still has to exist or the router complains. management-web
  authenticates the same way (ADR-0080), so it inherits the same requirement —
  and this is the sort of thing that is discovered as a runtime error at the
  worst moment if it is not planned.
- **`useParams` for the identifier**, as `CellPage` does for `:layoutIdentifier`.

---

## 3. Is there an edit-with-`If-Match` precedent?

**Decision: yes, and it is thorough. This feature adds no new mechanism.**

`apps/shared/src/api/gateway.ts` already exports the helper:

```ts
export const ifMatch = (version: number): Record<string, string> =>
  ({ 'If-Match': `"${version}"` });
```

`layouts.api.ts` and `overlays.api.ts` both use it, and the gateway's own
comment explains the design: the version is **threaded explicitly through each
mutation's arguments** rather than intercepted centrally, because a miss
degrades to a request with no version — which the server would then refuse with
428 rather than silently accept.

`LayoutEditorDialog.tsx` is the UI precedent for a stale-version refusal,
including the observation that *"'Try again' is the wrong advice on a stale
conflict — resubmitting replays"*.

**Consequence**: FR-004 and FR-005 are pattern-following, not pattern-setting.

---

## 4. Is there a convention for turning refusals into words?

**Decision: yes — `apps/shared/src/api/problemDetail.ts`.**

| Helper | Does |
|---|---|
| `problemDetail(error, fallback)` | pulls the RFC-7807 `detail` (ADR-0089) |
| `problemCode(error)` | pulls the `title`, which carries the server's error code |
| `isConflict(error)` | **status === 409** |
| `isStaleConflict(error)` | `isConflict` **and** code ends with `_STALE` |
| `CONFLICT_FALLBACK` | *"Someone else changed this while you were working. Reload to see their version…"* |

The two-part test in `isStaleConflict` exists for a documented reason: 409 is
not exclusively a stale version — `LAYOUT_NAME_TAKEN` shares it — and *"anything
that changes the advice has to key on the code rather than the status."*

**That reasoning is right, and spec 029 broke its assumptions.** See below.

---

## 5. Spec 029's refusal codes do not fit these helpers

**This is the finding. Raised, not absorbed.**

Spec 029 chose, and shipped:

| Camera refusal | Status | Code |
|---|---|---|
| Stale version | **412** Precondition Failed | `CAMERA_VERSION_MISMATCH` |
| Retired (terminal) | **409** Conflict | `CAMERA_RETIRED` |

Run those through the existing helpers:

- **`isStaleConflict` returns `false` for a stale camera version.** The status is
  412 not 409, and the code does not end in `_STALE`. The operator would get the
  generic fallback — *"Try again"* — which is precisely the advice
  `LayoutEditorDialog` documents as wrong, because resubmitting replays the
  change over the other writer's.
- **`isConflict` returns `true` for a retired camera.** Any code keying on it
  alone would tell the operator *"Someone else changed this while you were
  working. Reload to see their version"* about a camera that is **retired**.
  Nobody changed it; reloading will not help.

So both camera refusals map to the wrong words, and one of them maps to the
exact wrong words the helper was written to prevent.

**Neither status is wrong on its own terms.** 412 is what RFC 9110 specifies for
a failed `If-Match`, and is arguably more correct than the 409 the older
contexts use; 409 for a terminal-state refusal is also reasonable. The problem
is that there are now **two conventions for "your version is stale"** and one
status doing double duty across contexts.

**Decision for this feature: extend the shared helpers, do not change spec 029's
API.**

- `isStaleConflict` gains the 412 case, so it means "the version you held is no
  longer current" regardless of which context answered.
- A new predicate distinguishes a **terminal-state** refusal from a
  lost-update one, so `CAMERA_RETIRED` gets its own words.
- `problemDetail.ts` is shared by layouts, overlays and system variables, so the
  change must be **additive** — existing behaviour for `*_STALE` on 409 must not
  move, and its tests must still pass untouched.

**Alternatives considered:**

- **Change the backend to 409 + `CAMERA_VERSION_STALE`.** Rejected for this
  feature: it would edit a contract that shipped two PRs ago to suit a frontend
  helper, and it would make the camera API *less* HTTP-correct. Worth raising
  separately as a consistency question — two conventions for one meaning is a
  latent trap, and it is ADR-shaped rather than something a UI feature should
  settle.
- **Handle it locally in the camera page.** Rejected: the next context to use
  412 hits the same wall, and a local fix leaves the shared helper quietly
  wrong.

**Consequence for the spec**: FR-006 is unaffected in what it asks for, but it
is no longer free — it needs a shared-code change, not just a call site.

---

## 6. What does the API client need?

**Decision: two endpoints and two fields. No new client machinery.**

`cameras.api.ts` has `registerCamera` and `listCameras`, with `Camera` tags
already per-identifier plus a `LIST` tag — so cache invalidation after a
correction is already expressible.

Needed:

- **`getCamera`** query, providing `{ type: 'Camera', id }`.
- **`changeCameraAddress`** mutation using `ifMatch(version)`, invalidating that
  camera's tag and `LIST`.
- **`CameraSummary` gains `version` and `status`** — spec 029 returns both on
  every listing row and the TypeScript interface was never updated. It is a
  plain interface with no runtime validation, so nothing broke; it is simply out
  of date with the wire.

The `version` on listing rows is what lets a correction be made without a
read-one round trip — the reason spec 029 put it there.

---

## Summary

| # | Finding | Status |
|---|---|---|
| 1 | Shell conversion costs 5 e2e specs + 1 unit test; nav-as-button avoids it and is rejected | Sized; applied in design |
| 2 | `kiosk-web/app/router.tsx` is the pattern, including the OIDC callback route | Applied |
| 3 | `ifMatch` + explicit version threading already exists | Applied |
| 4 | `problemDetail.ts` is the refusal-to-words convention | Applied |
| 5 | **Spec 029's 412/409 codes map to the wrong words in those helpers** | **Raised — shared helper change needed** |
| 6 | Two endpoints, plus `version`/`status` on `CameraSummary` | Applied |

**Finding 5 is the one to read.** It does not change what the spec asks for, but
it moves FR-006 from "call the existing helper" to "the existing helper is
wrong for this feature's refusals, and it is shared."
