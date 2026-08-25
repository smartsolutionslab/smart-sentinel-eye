# Implementation Plan: Rename a camera from the management app

**Branch**: `035-rename-camera-ui` · **Spec**: [spec.md](./spec.md) · **Date**: 2026-08-25
**Issue**: 1873

## Summary

A mutation, a dialog, a button, and a test. Spec 033 made the name correctable
and nothing calls it.

Most of the machinery exists and Phase 0 confirmed it fits. **Two places are not
mechanical**: a rename can be refused three ways where the address correction had
two, and the typed name must reach the server unaltered.

## Technical Context

**Language**: TypeScript 5.7, React 19
**Dependencies**: RTK Query (ADR-0075), Radix + Tailwind tokens (ADR-0077/0078),
React Hook Form + Zod (ADR-0079), Playwright
**Target**: `apps/management-web/src/features/cameras/`, plus two small changes
in `apps/shared`
**Backend**: **untouched.** `PATCH /cameras/{camera}` with `{ "name": … }`,
`If-Match` required

**No new dependency, no migration, no backend change.**

## Constitution Check

| Principle | Assessment |
|---|---|
| **§IV Latency budget** | **N/A** — nothing on the event-to-overlay path |
| **§IX No speculative generality** | Research §2 rejected a shared edit-dialog for two callers, and §1 rejected a fourth shared refusal predicate for one call site. Both recorded with what would justify them later |
| **Smallest possible change** | One mutation, one dialog, one button, one schema line. The one unrelated edit is a doc comment that now says the opposite of the truth |
| **Mirror existing patterns** (ADR-0036) | `EditCameraAddressDialog` for the dialog, `OverlayEditorDialog` for the taken-name wording, `changeCameraAddressSchema` for the schema derivation |

**No violations.**

## Phases

Four. The third is where it goes wrong.

### Phase 1 — The mutation and the schema

- `renameCamera` in `apps/shared/src/api/cameras.api.ts`, mirroring
  `changeCameraAddress`: `PATCH` with `{ name }`, `If-Match` from the version
  the caller was shown, invalidating `{ Camera, id }` and `{ Camera, 'LIST' }`.
- `renameCameraSchema = registerCameraSchema.pick({ name: true })` in
  `cameras.schema.ts` — one line, the pattern that file already establishes.
- Correct that file's stale comment claiming the name is not editable.

**The schema carries FR-010.** `.trim()` is permitted; case normalisation is
not, and none exists today. Nothing should be added.

### Phase 2 — The dialog

`RenameCameraDialog.tsx`, mirroring `EditCameraAddressDialog` — **mirrored, not
extracted** (research §2). Pre-filled with the current name (FR-003), sends the
version (FR-004), keeps the operator's typing on refusal (FR-011).

### Phase 3 — Three refusals, three answers

The reason this feature is not purely mechanical.

`EditCameraAddressDialog` distinguishes two refusals and falls back to the
server's detail for anything else. A rename produces **three**, and Phase 0
rendered all of them: they are already distinct. What is missing is one clause.

| Refusal | Source | Says |
|---|---|---|
| taken | server's detail **+ our action clause** | which name, which fab, and to choose another |
| stale | `CONFLICT_FALLBACK` | reload, do not retry |
| retired | `TERMINAL_REFUSAL_FALLBACK` | terminal |

The taken branch keys on the **code at the call site**, following
`OverlayEditorDialog`. **No fourth shared predicate** — research §1 records why,
and what would change that.

### Phase 4 — The control, and proof

- A third button on `CameraDetailPage.tsx`, gated on `retired` exactly as the two
  beside it are (FR-009). Order: **Rename · Correct the address · Retire camera ·
  Back**.
- Component tests for the three refusals, the absent control, and non-enumeration.
- A Playwright test that renames through the app, driving it rather than the API.

## Sizing

| Phase | Files | Risk |
|---|---|---|
| 1 | 2 changed | Low — mirrors an existing endpoint |
| 2 | 1 added | Low — mirrors an existing dialog |
| 3 | (in Phase 2's file) | **The three-way distinction** |
| 4 | 1 changed, 2 test files | e2e flake surface |

## Three things most likely to go wrong

1. **A taken name is reported as a lost update.** Both are `409`. The shared
   helper correctly returns false for a taken name, so the danger is not the
   helper but the *branching order* at the call site — a careless
   `isConflict`-shaped check would hand it `CONFLICT_FALLBACK`: *"someone else
   changed this, reload to see their version."* Wrong in both halves. Nobody
   changed the camera, and reloading will not release the name. Asserted as
   rendered text for all three, not as "an error appeared".

2. **The client normalises the name before sending.** `.trim()` is fine and
   already there. Lower-casing is not, and it is exactly the kind of thing added
   in passing to "match the server's uniqueness rule". A case-only correction is
   a real change that normalises identically — spec 033 found that trap in three
   separate layers, and this would be the fourth. The symptom is a rename that
   reports success and changes nothing.

3. **The control is disabled rather than absent.** Disabling is the smaller diff
   and looks more informative. For a terminal state it says an action is
   conceptually available when it is not, and it diverges from the two controls
   beside it. Asserted as absence, with the active-camera counterpart so it
   cannot pass vacuously.

## Out of scope

Renaming rules or variables (ADR-0120 — their names are their addresses),
changing a camera's fab, bulk rename, renaming from the listing, and any backend
change.

**Noted for the next person**: at a fourth control the detail-page header needs a
menu rather than a fourth button (research §4), and an inline edit affordance on
the name was considered and rejected as a new interaction pattern rather than as
a bad idea.
