# Implementation Plan: Retire a camera from the management app

**Branch**: `032-retire-camera-ui` · **Spec**: [spec.md](./spec.md) · **Date**: 2026-08-24
**Issue**: #1860

## Summary

Add a retire control to the camera detail page, behind a confirmation that says
what retirement costs. The endpoint has existed since spec 028 and has never had
a caller.

**This is a small feature and the plan does not pretend otherwise** — one
mutation, one control, one dialog. What it is *not* is a button: the product has
no destructive confirmation anywhere, the correct Radix primitive is not
installed, and two of the sixteen requirements are ones a passing test suite
will happily let you get wrong. The weight goes there.

## Technical Context

**Language**: TypeScript 5.7, React 19
**Primary dependencies**: RTK Query (ADR-0075), Radix (ADR-0077), Tailwind
tokens (ADR-0078), Playwright
**Storage**: N/A — no persistence in this feature
**Testing**: Vitest + Testing Library; Playwright for SC-005
**Target**: `apps/management-web`, plus two shared primitives in `apps/shared`
**Backend**: **untouched.** `POST /cameras/{camera}/retire`, idempotent,
`204/400/403/404`

**New dependency**: `@radix-ui/react-alert-dialog@1.1.x` — justified in
[research.md](./research.md) §2. It is the only addition.

## Constitution Check

| Principle | Assessment |
|---|---|
| **§IV Latency budget** | **N/A.** Nothing on the event-to-overlay path. No leg affected |
| **§IX No speculative generality** | The shared `ConfirmDialog` serves one caller today. Justified in research §1: it is built shared because it is the *first* of a kind, and the alternative — camera-local, then duplicated or diverged — is the failure mode spec 031 spent a whole feature undoing. Not a framework; one component with one job |
| **DDD / value objects** | N/A — no domain code |
| **No cross-context references** | N/A — frontend only |
| **Smallest possible change** (ADR-0036) | Four files changed, three added. No refactor rides along. The four existing archive flows are **not** touched — see the finding |
| **Tests** | Vitest for the control and its wording; Playwright for SC-005. No backend tests, because no backend change |

**No violations.** One new dependency, argued rather than assumed.

## Phases

Four phases. Phases 1 and 2 are independent and can run in either order.

### Phase 1 — The confirmation primitive

The thing that does not exist. `apps/shared`:

- `@radix-ui/react-alert-dialog` added to `apps/shared/package.json`.
- `ui/primitives/ConfirmDialog.tsx` — `role="alertdialog"`, focus defaulting to
  cancel, a `danger`-styled confirm action, and a `pending` state that makes the
  confirm control non-actionable while a request is in flight (**FR-015**).
- `ui/primitives/Button.tsx` gains a `danger` variant.

Built shared, and tested shared: dismissing calls nothing, confirming calls once,
and a second click while pending does not call twice.

### Phase 2 — The mutation

`apps/shared/src/api/cameras.api.ts`:

- `retireCamera` mutation — `POST /{cameraIdentifier}/retire`, **no `If-Match`**
  (**FR-016**), `fabId` threaded as the other camera endpoints do.
- Invalidates `{ Camera, id }` and `{ Camera, id: 'LIST' }` — research §4.

### Phase 3 — The control and its words

`apps/management-web/src/features/cameras/`:

- `RetireCameraDialog.tsx` — consumes the Phase 1 primitive, supplies the
  camera's **name** (FR-003) and all three consequences (FR-005, FR-006,
  FR-007).
- `CameraDetailPage.tsx` — the control, rendered only when the camera is active,
  mirroring how the address-correction control is already gated (**FR-004**).

This is where the two dangerous requirements live, so they get their own tests
rather than riding along:

- **FR-012** — assert the rendered page after success contains **no** claim of
  authorship. Asserted as the absence of the sentence, because a test that
  checks "the page updated" passes while the page says something untrue.
- **FR-004** — assert the control is **absent** for a retired camera, not that
  activating it fails. Spec 030 FR-007's precedent, restated because the
  disabled-instead-of-absent regression is invisible to a happy-path test.

### Phase 4 — End to end, and the property that regresses quietly

- `e2e/camera-detail.spec.ts` — the test spec 030 declined to fake. Register,
  retire, see it gone from the listing, re-open its address and see it readable
  and marked. Driving the app throughout; **no `fetch` to the API** (research
  §5). The comment recording why it was absent is replaced by the test.
- **FR-013** — a camera in another fab renders identically to one that never
  existed, **after** this feature adds a control that could differ between them.
  The existing e2e covers the read; this extends it to the page carrying the new
  control, because "no retire button, because it's not yours" and "no retire
  button, because there is no camera" must look the same.

## Sizing

| Phase | Files | Risk |
|---|---|---|
| 1 | 2 added, 2 changed | New dependency; a11y semantics |
| 2 | 1 changed | Low — mirrors `changeCameraAddress` |
| 3 | 1 added, 1 changed | **The wording requirements** |
| 4 | 1 changed | e2e flake surface |

Phase 3 is where a naive implementation passes its tests and is still wrong.

## Three things most likely to go wrong

1. **The success message gets written.** There is no toast infrastructure, so
   adding one and saying *"Camera retired."* feels like polish. It is a claim
   the app cannot support — retirement answers `204` whether or not this
   operator caused it (**FR-012**). The page state is the feedback. If a toast
   is wanted later, it is a separate decision with a separate sentence.

2. **The control is disabled rather than absent.** Disabling is the smaller
   diff and looks more informative. For a terminal state it tells the operator
   an action is conceptually available when it is not, and it diverges from the
   address-correction control three lines above it (**FR-004**).

3. **A helpful message undoes non-enumeration.** The refusal path for another
   fab's camera is the same 404 as a camera that never existed, and any sentence
   distinguishing them turns a security property into an enumeration oracle
   (**FR-013**). This regresses by kindness, which is why SC-004 compares
   renderings field for field rather than asserting that both showed an error.

## Finding to raise, not absorb

**Four existing flows archive on a single click with no confirmation** —
`RulesPage`, `OverlaysPage`, `LayoutsPage`, `SystemVariablesPage`. After this
feature, cameras confirm and nothing else does.

Whether those should confirm is four other features' behaviour and is **not**
in this spec's scope. It is recorded here so it is a tracked inconsistency
rather than a discovered one. **File an issue.** Do not change them here, and
do not drop the camera confirmation to match them — spec 028 made retirement
irreversible by decision, and a one-click irreversible action is the thing
FR-002 exists to prevent.

## Out of scope

Bulk retire, un-retire, retiring from the listing, any backend change. All
recorded in the spec with reasons.
