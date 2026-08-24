# Implementation Plan: Open one camera, and fix it

**Branch**: `030-camera-detail-view` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/030-camera-detail-view/spec.md`

## Summary

A routed camera detail view, and an edit control on it. Spec 029 built the two
endpoints and nothing calls them; this connects them to an operator.

Phase 0 found the patterns this needs already exist — the router (kiosk-web),
`If-Match` threading (`gateway.ts`), and refusal-to-words
(`problemDetail.ts`) — so most of this is following, not inventing.

**One finding goes against spec 029.** The refusal codes it shipped —
**412** `CAMERA_VERSION_MISMATCH` for a stale version, **409** `CAMERA_RETIRED`
for terminal — do not fit the shared helpers. `isStaleConflict` returns false
for the first, so the operator gets *"Try again"*, the exact advice
`LayoutEditorDialog` documents as wrong because resubmitting replays over the
other writer's change. And `isConflict` returns true for the second, so a
**retired** camera would be described as *"someone else changed this, reload"*.
Both refusals map to the wrong words. See [research.md](./research.md) §5.

## Technical Context

**Language/Version**: TypeScript, React 19, Vite

**Primary Dependencies**: `react-router-dom` 7.1.3 (**already a dependency, unused in this app**), Redux Toolkit + RTK Query (ADR-0075), Radix UI + Tailwind (ADR-0077/0078), React Hook Form + Zod (ADR-0079), `react-oidc-context` (ADR-0080)

**Storage**: N/A — no client persistence. Server state is RTK Query cache.

**Testing**: Vitest + Testing Library for components; Playwright for e2e (`e2e/`)

**Target Platform**: Browser, management operators

**Project Type**: Web frontend. **No backend change expected** — if one proves necessary that contradicts spec 029's contract and is a finding to raise.

**Performance Goals**: Not on the event-to-overlay path. Opening one camera transfers one camera (SC-002).

**Constraints**: A camera the operator may not see must be reported exactly as one that does not exist (FR-008) — the app must not undo spec 029's indistinguishability with a helpful message.

**Scale/Scope**: 250 cameras per fab. One new route plus a shell conversion, one query, one mutation, one shared-helper change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **II. DDD with value objects** | N/A — frontend. No domain model crosses here; the wire carries primitives by design (ADR-0040). |
| **III. Bounded context isolation** | PASS. The app talks to CameraCatalog's HTTP surface only, through the existing gateway base query. No new cross-context coupling. |
| **IV. Latency budget** | **N/A** — the management camera surface is not on the event-to-overlay path. No leg affected. |
| **V. Spec-driven development** | PASS. Spec → plan → tasks, gated. |
| **VII. Observability** | PASS. No new backend telemetry; the existing `logResilienceEvent` path covers render crashes, and the shell's `ErrorBoundary` is preserved through the router conversion (see below). |
| **VIII. Safe by default at trust boundaries** | PASS, and this is the principle the feature most engages — FR-008 is the whole of it. The app must reproduce the API's indistinguishability rather than improve on it. |
| **IX. Forward-compatible interfaces** | N/A. No new strategy seam. |

**No violations.** Complexity Tracking omitted.

**Post-design re-check**: unchanged. The one shared-code change (`problemDetail.ts`) is additive and keeps existing behaviour; it introduces no abstraction, only a second status the same predicate already means to cover.

## Project Structure

### Documentation (this feature)

```text
specs/030-camera-detail-view/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── camera-detail-ui.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
apps/
├── shared/src/api/
│   ├── cameras.api.ts          # + getCamera, + changeCameraAddress
│   ├── cameras.schema.ts       # + changeCameraAddressSchema (RTSP rules, reused from register)
│   └── problemDetail.ts        # + 412 in isStaleConflict, + isTerminalRefusal
└── management-web/src/
    ├── App.tsx                 # Shell → RouterProvider; nav buttons → links
    ├── app/
    │   └── router.tsx          # new — mirrors kiosk-web/src/app/router.tsx
    └── features/cameras/
        ├── CamerasPage.tsx     # rows link to the detail route
        ├── CameraDetailPage.tsx        # new
        └── EditCameraAddressDialog.tsx # new — mirrors RegisterCameraDialog

e2e/
├── camera-detail.spec.ts       # new
├── audit.spec.ts               # nav selector: button → link
├── layouts.spec.ts             # same
├── overlays.spec.ts            # same
├── rules.spec.ts               # same
└── system-variables.spec.ts    # same
```

**Structure Decision**: The existing per-feature folder layout under
`features/`, and `app/router.tsx` mirroring kiosk-web's path exactly so the two
apps stay recognisable to each other.

## Implementation phasing

| Phase | Content | Depends on | Droppable? |
|---|---|---|---|
| **1** | API client: `getCamera`, `changeCameraAddress`, `version`/`status` on `CameraSummary` | — | No |
| **2** | **Refusals into words**: 412 in `isStaleConflict`, a terminal-refusal predicate, unit tests. Shared code — must not move existing behaviour | — | No |
| **3** | Router: `app/router.tsx`, `RouterProvider`, nav links, OIDC callback route, `ErrorBoundary` preserved. **Updates 5 e2e specs + `App.test.tsx`** | — | **Shrinks** if the gate overturns the shell conversion |
| **4** | Detail view: route, open from the list, retired marked, FR-008 handling | 1, 3 | No |
| **5** | Edit: dialog, `If-Match`, 412/409 in operator words, cache invalidation | 1, 2, 4 | No |
| **6** | e2e + polish: `camera-detail.spec.ts`, full suite | all | No |

**Phases 1 and 2 are independent of the routing decision**, so they are first —
if the Phase 2 gate overturns the shell conversion, nothing in them is wasted.

## Key design decisions

**The `ErrorBoundary` must survive the router conversion.** Today it is keyed on
`view` so navigating away from a crashed page renders the next one fresh, and
the nav sits outside it so it survives any page crash (spec 011 FR-016). Under a
router the equivalent is keying on the location. Losing this would be a silent
regression of a requirement another spec paid for — it is not visible in any
camera test.

**Nav becomes links, not buttons calling `useNavigate`.** The button form keeps
all six existing selectors green and is rejected: a control that changes
location is a link, and the button form loses middle-click, open-in-new-tab and
copy-link — which is most of what FR-002 is for. Keeping selectors green by
making the markup wrong is paying for the router and not collecting.

**The detail view reads the camera even though the list already has it.**
Reusing the row would be one fewer request and would break on reload and on a
pasted link, which FR-002 requires to work. The listing's `version` is still
useful — it is what lets a correction be made from the list later without a
read-one.

**FR-008 is a rule about what the app must *not* add.** The API already answers
identically for "another fab's camera" and "no such camera". The failure mode is
a well-meaning `catch` that says "you don't have access to this camera", which
would undo the property at the last hop.

## Three things most likely to go wrong

**The stale-version refusal reaches the operator as "Try again."** Research §5:
`isStaleConflict` is status-409-only, and spec 029 answers 412. The words that
come out are the ones `LayoutEditorDialog` explicitly documents as wrong,
because resubmitting replays the change over the other writer's. The test has to
assert the **rendered message**, not that an error was handled.

**A retired camera is described as someone else's edit.** The mirror image:
`isConflict` is true for 409 and `CAMERA_RETIRED` is a 409, so the generic
conflict wording fits it and says something false. Nobody changed the camera and
reloading will not help.

**`ErrorBoundary` scoping is lost in the shell conversion.** It is spec 011's
requirement, not this feature's, so nothing here would fail if it broke — which
is exactly why it is worth naming.

## Complexity Tracking

No constitution violations. Section intentionally empty.
