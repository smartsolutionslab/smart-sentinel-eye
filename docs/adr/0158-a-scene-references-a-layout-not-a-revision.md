# ADR-0158: A scene references a Layout, not a Revision

**Status:** **Accepted**
**Date:** 2026-09-26
**Amends:** ADR-0157 (event-driven scene rotation), decision 1
**Supersedes:** —
**Superseded by:** —

**Relates to:** ADR-0112 (the Layout/Revision aggregate and its
at-most-one-Published-per-chain invariant), spec 258 (the delivering spec,
decision PD-3).

## Context

ADR-0157 §1 defines a scene set as "an ordered list of references to
Revisions that are each independently authored, versioned, and published".

Read literally, a scene entry is a `LayoutRevisionIdentifier`. That cannot
survive ordinary use of the system this ADR builds on:

- A `Layout` chain holds **at most one Published revision**, and
  `Layout.Publish` **archives the previously Published revision** in the same
  transaction (ADR-0112, `src/LayoutComposition/Domain/Layout/Layout.cs`).
- So when the author of a layout that sits in a scene set publishes an edit,
  the revision the scene points at becomes Archived. The scene would then
  either go dark or keep showing content its author has withdrawn.
- A second consequence: any two distinct Published revisions necessarily belong
  to two distinct Layout chains. A "reference to a Published revision"
  therefore already identifies a chain. Pinning the revision number adds
  nothing but the ability to go stale.

This was raised while specifying spec 258 (PD-3), and the product owner
confirmed the reading below on 2026-09-26.

## Decision

**A scene-set entry references a `Layout` chain by its `LayoutIdentifier`.
When displayed, it shows that chain's currently Published revision.**

- Publishing an edit to a layout in a scene set updates that scene in place.
  This is what the kiosk already does for `/layouts/:layoutIdentifier`.
- A chain with no Published revision is not showable. How a wall treats such an
  entry (skip on "next", refuse a direct jump, no automatic advance off the
  scene currently showing) is spec 258's decision PD-6, not this ADR's.
- Everything else in ADR-0157 §1 stands. No new content model is introduced. A
  scene set publishes and archives nothing itself. The Layout/Revision
  lifecycle is unchanged.

## Consequences

**Positive:**

- Scene sets survive the normal edit→publish cycle of the layouts they contain,
  with no re-pointing step and no stale references.
- The reference is to a stable identity, so editing a layout does not rewrite
  any wall that contains it.

**Negative:**

- A wall cannot pin an older version of a layout. "Show revision 3 of layout X
  even after revision 4 is published" is not expressible. That is accepted,
  because at-most-one-Published already makes it unexpressible for any kiosk,
  wall or not.
- A layout edit changes what every wall containing it shows the next time the
  kiosk re-renders that scene. An author editing a layout that sits in a scene
  set is editing that wall. The wall UI should make the membership visible (a
  spec 258 UI concern).

## Alternatives Considered

**A — Keep revision references and re-point them on publish — REJECTED.**
Every `Layout.Publish` would have to find and rewrite every wall that
references the prior revision, across aggregates. That is cross-aggregate
coupling to preserve a reference nobody could make meaningful use of.

**B — Keep revision references and let archived entries be skipped — REJECTED.**
The first ordinary edit to a layout would silently drop it from every wall
containing it.

## Implementation Notes

Ships docs-only in the same PR as spec 258's US1 implementation.
