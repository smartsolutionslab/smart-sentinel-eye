# Implementation Plan: An operator can watch a camera

**Branch**: `043-operator-watches-camera` | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/043-operator-watches-camera/spec.md`

## Summary

Render the existing camera viewer on the camera's page, hand it the operator's
real token, hide it for a retired camera and say why, delete the panel that
nothing ever mounted, and add the two checks that would have caught this.

**The code change is small and the checks are the substance.** `CameraViewer`
works — it carries real video on every kiosk tile. What has been missing for four
specs is a page that renders it, and the reason nobody noticed is that three unit
tests passed on the component that would have.

## Technical Context

**Language/Version**: TypeScript 5 / React 19

**Primary Dependencies**: `react-oidc-context` (ADR-0080), RTK Query (ADR-0075), Tailwind (ADR-0078); the shared `CameraViewer` composite

**Storage**: none — no schema, no migration, no domain state

**Testing**: vitest + Testing Library for the page; Playwright against a live `aspire run` stack (ADR-0108)

**Target Platform**: the operator console (`:5173`)

**Project Type**: web (frontend only — no service, no contract, no realm change)

**Performance Goals**: unchanged. One viewer on a page an operator opens deliberately; the kiosk runs four at once.

**Constraints**: jsdom has no `RTCPeerConnection`, so any test rendering the page must stub the composite; **CI produces no video**, so no automated check can prove a picture appears

**Scale/Scope**: one component deleted with its three tests, one page gains a viewer and a token getter, one notice gains a clause, two checks added

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **IV. The Latency Budget Is Sacred** | **Not on the path, and worth stating why.** The budget is `event arrival → overlay rendered` on a **kiosk wall**. This is an operator opening one camera on a console; no leg is added, removed or measured, and the two kiosk measurements (spec 040) fire from `CellPage`, not from here. |
| **VII. Observability Is Non-Negotiable** | No new leg, no new sink. Untouched. |
| **VIII. Safe by Default at Trust Boundaries** | No new authorization. The operator's existing token is what the viewer gets; the WHEP gate already accepts it. **Strictly narrower than today's code**, which hands the viewer a credential from a browser-storage key any script could write. |
| **III. Bounded Context Isolation** | Frontend only. No cross-context reference; `CameraViewer` is a shared composite already used by both apps. |
| **II. DDD with Value Objects** | No domain type touched. |
| **V. Spec-Driven Development** | Spec → plan → tasks → implement → verify → QA → PR, gates observed. |
| **Karpathy: smallest possible change** (ADR-0036) | The feature is "mount a component that exists". The deletions (panel, caption, three tests) are part of that change, not a drive-by: they are the same behaviour moving, and FR-010 requires the tests to move with it. |
| **No speculative generality** | The panel is deleted rather than renamed and kept as a seam. Two callers of the token pattern is not three, so no shared hook. |

**No violation to justify.**

**Post-design re-check**: unchanged. Net effect is one file deleted, one file
edited, two test files touched.

## Project Structure

### Documentation (this feature)

```text
specs/043-operator-watches-camera/
├── plan.md                        # this file
├── spec.md
├── research.md                    # R1..R8
├── quickstart.md                  # the part CI cannot do
├── checklists/requirements.md
├── contracts/
│   └── what-the-page-shows.md
└── tasks.md                       # /speckit-tasks — not created here
```

**No `data-model.md`.** Nothing persists; this renders an existing component on
an existing page. Specs 040–042 skipped it for the same reason.

### Source Code (repository root)

```text
apps/management-web/src/features/cameras/
  CameraDetailPage.tsx         # renders CameraViewer; stable getToken; retired notice
  CameraDetailPage.test.tsx    # stubs CameraViewer; asserts reachability + the token
  CameraViewerPanel.tsx        # DELETED
  CameraViewerPanel.test.tsx   # DELETED

e2e/camera-detail.spec.ts      # an opened camera has a <video>; a retired one does not
```

## Approach

Three increments. The order puts the deletion last, so the page is never without
a viewer.

### 1. The page shows the picture

`CameraDetailPage` gains `useAuth()`, a ref-backed `getToken` with a stable
identity, and a `<CameraViewer>` beneath the header and above the record fields,
in a width-bounded container. Nothing else on the page moves.

**The stable identity is not a style preference.** Spec 042 spent a PR on exactly
this: a fresh `getToken` on every render rebuilt the effects underneath
`CameraViewer` and silently killed a measurement. `useWhepSession` guards its own
copy the same way and says so.

### 2. A retired camera says why there is nothing to watch

The viewer is not rendered when `status === 'Decommissioned'`, and the existing
`role="status"` notice gains a clause about the stream. The current sentence —
*"Its record is kept, but it can no longer be changed"* — leaves a reader free to
think the video is broken, which is the ambiguity FR-004 removes.

This follows the page's existing rule rather than inventing one: every other
control is hidden for a retired camera, with the refusal stated rather than
discovered.

### 3. Delete the panel, and add the checks

`CameraViewerPanel` and its three tests go. The page test stubs `CameraViewer`
(as the panel test did, for the same jsdom reason) and asserts two things: the
viewer is mounted for an active camera and absent for a retired one, and **the
`getToken` it receives resolves to the session's token**.

That second assertion is the one the current code fails. The placeholder renders
perfectly and resolves to `null`, so a viewer wired to a credential nobody issues
fails exactly like no viewer at all — and passes any check that asks only whether
something rendered.

Then an e2e opens a camera and finds a `<video>`.

## What must fail

| Break this | Expected |
|---|---|
| Remove the viewer from the page | page test red, **and** e2e red |
| Hand the viewer the old placeholder getter | page test red — it resolves to `null` |
| Render the viewer for a retired camera | page test red |
| Break `CameraViewer` itself | **both green** — the page test stubs it, and CI has no video |

The last row is the honest one, and it is why Phase 5 exists.

## Risks

**No automated check can prove a picture appears.** CI produces no video by
design. Everything green here proves the viewer is *reached* and *credentialled*;
that it renders a frame is a person watching the page. This is the third feature
running to carry that limitation, and it gets stated rather than assumed.

**`CameraDetailPage.test.tsx` will break before it is fixed.** Ten tests render
that page; mounting a `CameraViewer` pulls a `WhepClient` into jsdom, which has
no `RTCPeerConnection`. Expected, and the stub is both the fix and the vehicle
for the new assertions — but a red suite mid-change is not a surprise to
diagnose.

**Deleting a tested component looks like losing coverage.** Three green tests
disappear. They tested a panel that opens and closes, half of which stops
existing; the replacement assertions test something stronger — that an operator
can reach the thing. Worth saying in the PR so the diff does not read as a
regression.

## Out of scope

- **Steering the camera** (pan / tilt / zoom). Spec 002 raises it as a later
  concern, and it needs a credential bound to the camera.
- **A viewer on the camera list.** FR-009: one place shows a camera.
- **Stream health on the page** as a separate field. `CameraViewer` reports its
  own state; a second indicator would be a second source of one fact.
- **Anything the kiosk does**, and issue 1891.
- **Any production rollout.**
