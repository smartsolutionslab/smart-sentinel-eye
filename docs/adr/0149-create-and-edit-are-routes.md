# ADR-0149: Create and edit are routes; confirmation is a dialog

**Status:** Accepted
**Date:** 2026-09-15
**Amends:** —

**Supersedes:** —
**Superseded by:** —

## Context

`apps/management-web` has eight route entries across seven surfaces
(`apps/management-web/src/app/router.tsx`), and **every create and every edit in
the product happens in a modal**. There are seven such editor dialogs, 1,564
lines between them.

That was never decided. It is what the first feature did, and each subsequent
feature mirrored it — which is the pattern this repository has had to correct
before, most recently in the token system ADR-0148 amends: *decided per call
site, then discovered to be a system*.

Three facts make it worth deciding properly now.

### The container is 448 pixels, and no call site can widen it

`Dialog` (`apps/shared/src/ui/primitives/Dialog.tsx`) hard-codes `max-w-md` —
448px — less `p-6` on both sides, so roughly **400px of content**. `DialogProps`
is `open`, `onOpenChange`, `title`, `description`, `children`. There is no
`className`, so a call site cannot opt out.

`OverlayEditor` defaults to an **800×450** canvas
(`apps/shared/src/ui/composites/OverlayEditor.tsx:229-230`), and
`OverlayEditorDialog` renders it without overriding those props
(`apps/management-web/src/features/overlays/OverlayEditorDialog.tsx:268`). The
canvas is **twice the width of the box it is in**, and `Dialog`'s overflow rule
is `overflow-y-auto` — the y axis only.

The editor's own comment states the constraint:

```
* 16:9 800x450 canvas — large enough to be usable, small enough to
* fit inside a Dialog.
```

So the most spatially demanding surface in the product is sized by what fits in
a modal, and it does not fit.

### A stray click destroys an in-progress edit, silently

Radix's dialog closes on `Escape` and on pointer-down outside by default. Across
`apps/management-web` and `apps/shared` there are **zero** occurrences of
`onInteractOutside`, `onPointerDownOutside`, `onEscapeKeyDown`, `useBlocker` or
`beforeunload`. Nothing guards a dismissal anywhere in either app.

What the editors do on close is discard:

| Surface | Line | On close |
|---|---|---|
| `OverlayEditorDialog` | 245-249 | `reset(defaultValues)` |
| `LayoutEditorDialog` | 311-314 | `reset(defaultValues)` |
| `SystemVariableDialog` | 81-83 | `reset(defaultValues)` |

An operator who has placed a label, sized a grid and assigned four cameras loses
all of it to one click on the backdrop. There is no confirmation and no undo
across the dismissal — `useOverlayEditHistory` lives inside the editor and dies
with it.

### Nothing is linkable, and ADR-0146 already assumes otherwise

There is no URL for "the overlay I am editing". A reload, a crash, a shared link
and a second monitor all land on the list. ADR-0146 commits this surface to
"**View Transitions between routes**" (line 77) — a treatment with no routes to
transition between.

#2350 raised this for the overlay editor specifically and deferred the
route-vs-modal call to a spec, on the grounds that it is a convention rather than
one surface's problem. It is. This is that decision.

## Decision

Accepted 2026-09-16. Implementation is not tracked here; nothing in this ADR
converts an existing surface on its own.

**A surface that holds draft state is a route. A surface that is a single
confirmed action is a dialog.**

That is the whole rule, and it is deliberately not "big editors get routes" —
size is a judgement call made per call site, and judgement calls made per call
site are what produced the current state. Draft state is a property of the
surface, readable from its code: if there is a form, an editor buffer, or an
undo history that a dismissal would discard, it is a route.

### Clause 1 — create and edit are routes

| Today | Route | Lines |
|---|---|---|
| `RegisterCameraDialog` | `/cameras/new` | 131 |
| `RenameCameraDialog` | on `/cameras/:cameraIdentifier` (clause 3) | 195 |
| `EditCameraAddressDialog` | on `/cameras/:cameraIdentifier` (clause 3) | 154 |
| `LayoutEditorDialog` | `/layouts/new`, `/layouts/:layoutIdentifier/edit` | 430 |
| `OverlayEditorDialog` | `/overlays/new`, `/overlays/:overlayIdentifier/edit` | 322 |
| `RuleDialog` | `/rules/new`, `/rules/:ruleIdentifier/edit` | 196 |
| `SystemVariableDialog` | `/system-variables/new`, `/system-variables/:name/edit` | 150 |

### Clause 2 — a confirmed action stays a `ConfirmDialog`

`RetireCameraDialog` and `ArchiveConfirmation` do not change. A yes/no holds no
draft state, so a dismissal destroys nothing, and a route would cost a navigation
for one click. Publish, archive, retire and delete are confirmations.

### Clause 3 — a resource with a detail page hosts its edit there

`CameraDetailPage` already exists at `/cameras/:cameraIdentifier`. Renaming a
camera and correcting its address are single-field edits on a resource that
already has a surface; they belong on it, in an edit affordance, not on a sibling
route and not in a modal.

This is an **option, not an obligation**. It does not oblige layouts, overlays,
rules or variables to grow detail pages — that is a separate decision nobody has
made. Where there is no detail page, clause 1's route shapes apply.

### Three obligations every edit route carries

A route is reachable without having passed through a list, which is exactly its
value and exactly what a modal never had to handle. Each obligation below exists
because of that.

1. **An unsaved-changes block.** `useBlocker` (react-router-dom 7.18.2, and the
   app already builds a data router via `createBrowserRouter`), implemented once
   rather than per surface. This is what replaces the unconditional
   `reset(defaultValues)` the three editors do today.
2. **A read on mount for the expected version.** The `If-Match` version
   (ADR-0113) must come from a read the route performs, never from a prop a list
   row handed over — a deep-linked route has no row behind it. #2379 is the live
   form of this hazard on the existing surface.
3. **A cancel path that navigates to the list**, not `history.back()`. An edit
   route opened from a link has nothing behind it.

### What this ADR does not decide

- **The visual treatment** — toolbar, inspector, label list, zoom. That is
  #2350's spec, and it is gated on #2332 and #2335.
- **Whether every resource grows a detail page.** Clause 3 permits; it does not
  require.
- **Sequencing.** These conversions fold into the frontend redesign programme so
  the new surfaces are built against the phase-01 tokens rather than restyled
  immediately afterwards.

## Consequences

**Positive**

- The overlay canvas is sized by the viewport rather than by `max-w-md`, which is
  the precondition for every affordance #2343, #2348 and #2349 ask for.
- Every edit becomes linkable, reloadable and survivable across a crash.
- One unsaved-changes guard replaces seven call sites that each get it wrong.
- ADR-0146's View Transitions become buildable.
- The back button means what an operator expects, on every surface.

**Negative**

- **Renaming a camera costs a navigation.** Clause 3 keeps it on a page that is
  one click away rather than a sibling route, but it is still not the 3-second
  in-place interaction a modal gave. This is the real cost of the clean rule, and
  it is accepted knowingly.
- **Seven surfaces, ~1,560 lines, to convert.** Each is behaviour-preserving, so
  constitution §Testing requires characterisation tests captured **green before**
  the move and passing **unmodified** after. An assertion that has to be edited is
  evidence the behaviour moved.
- **Deep-linked edit routes need refusal paths a modal never needed** — the
  resource may be archived, deleted, or in a fab the caller does not hold.
- The router file grows from 8 route entries to roughly 17.

## Alternatives Considered

**Widen the `Dialog`.** Add a `size` prop, give the overlay editor `max-w-6xl`.
Cheapest by far, and it is what a reader reaches for first. Rejected: it fixes
the 448px symptom and none of the rest — not linkability, not reload survival,
not discard-on-close — and choosing a width per call site is precisely the
per-call-site judgement that produced seven inconsistent surfaces.

**Big editors only — Layout and Overlay become routes, the rest stay modal.**
Rejected: it reintroduces the judgement call at the boundary ("is 196 lines
big?") and it leaves `RenameCameraDialog`'s discard-on-close in place, which is
the same defect at a smaller scale, not a different one.

**Everything becomes a route, confirmations included.** One rule, zero
exceptions, nothing to argue about. Rejected: a confirmation holds no draft
state, so the entire argument above does not apply to it, and a route for a
yes/no is a navigation and a URL for one click.

**Inline editing on detail pages for everything.** The best answer for cameras,
which is why it survives as clause 3. Rejected as a blanket rule: only cameras
have a detail page today, so it would make this ADR depend on four detail pages
nobody has specified.

## Implementation Notes

- **#2350 goes first.** It is the surface with the strongest case, it is already
  filed, and it is where the design language has the most to prove.
- Conversions are **behaviour-preserving** and take the characterisation colour
  of Phase 4a: covering tests captured green before the move, unmodified after.
  Where a surface has no covering test, it is written first — a refactor with no
  covering test is a rewrite.
- A conversion that also fixes a bug is **two issues**. Characterisation would
  otherwise encode the bug as the safety net. #2379 and #2366 both touch these
  surfaces and are separate work.
- The unsaved-changes guard is shared code in `apps/shared`, written once with
  the first conversion, not seven times.

## Related

- #2350 — the overlay editor's container, the first conversion
- #2332, #2335 — the tokens and primitives these are built against
- #2379, #2366 — live defects on these surfaces; separate issues, not folded in
- ADR-0113 — `If-Match` expected version, which obligation 2 serves
- ADR-0146 — the console's design treatment, which assumes routes
