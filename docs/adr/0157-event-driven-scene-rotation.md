# ADR-0157: Event-driven scene rotation for walls

**Status:** Accepted
**Date:** 2026-09-26
**Supersedes:** —
**Superseded by:** —

**Relates to:** ADR-0112 (multi-tile layouts — the Layout/Revision
aggregate this builds on), ADR-0156 (3×3 walls with spanning tiles —
the wall shapes a scene switches between), spec 007 (Automation Rule
aggregate: `TriggerSource`/`TriggerKind`/`Predicate`/`Action`), ADR-0111
(Scenario Simulator), constitution §IV (latency budget — a scene switch
is not on the event-to-overlay path, but see Consequences).

## Context

Today a wall shows exactly one Published `Revision` at a time; changing
what a wall shows is an operator publishing a new revision by hand. The
product now needs a wall that **rotates through several pre-authored
layouts on its own**, triggered by something other than a person clicking
Publish — the product owner chose **event-driven** switching over a plain
timer: a rule (an Automation trigger, an operator action, or a schedule)
decides when the wall's visible scene changes, not a fixed interval.

This is a **human decision**, made directly by the product owner in this
conversation. Its mechanics are genuinely new domain territory — nothing
in `LayoutComposition` or `Automation` today models "which of several
saved things is currently showing," only "publish one revision, replacing
whatever was published before." This ADR records the shape of the
decision; the detailed design (the exact new types, command/event names,
migration) is the delivering spec's job (ADR-0037 phases 1-3), not this
document's.

## Decision

### 1. A "scene" is a named, ordered reference to existing Published Revisions — not a new kind of content

A wall does not gain new visual content types. **A scene set is an
ordered list of references to Revisions that are each independently
authored, versioned, and published exactly as ADR-0112 already
describes.** Rotating "changes which already-published revision is
currently the wall's active one," not "renders something new." This
keeps the existing Layout/Revision lifecycle (Draft→Published→Archived,
at-most-one-Published-per-chain) completely unchanged — a scene set does
not publish or archive anything by existing; it only *selects among*
revisions that are already Published through the existing path.

**Why this shape, not a new "Scene" content aggregate:** the alternative
(a scene as its own authored thing, independent of Layout) would
duplicate everything ADR-0112 already built — grid, tiles, overlays,
publish lifecycle — for no reason a reference-list doesn't already serve.
Rejected in Alternatives.

### 2. Switching is driven by Automation's existing Rule/Trigger/Action model — a new `RuleAction` kind, not a new engine

`src/Automation/Domain/Rule/Rule.cs` already has the shape this needs:
`TriggerSource` + `TriggerKind` + `Predicate` decide *when*, `RuleAction`
decides *what happens*. Scene switching becomes a **new `RuleAction`
kind** (a `SwitchWallSceneAction` alongside whatever actions already
exist) rather than a parallel trigger/predicate system built inside
LayoutComposition. This reuses the fab-scoping, Draft→Active→Archived
rule lifecycle, and the existing event-ingestion trigger sources
(`TriggerSource`/`TriggerKind` are already stored as plain strings so
Automation can react to any upstream event kind without a project
reference) with zero new infrastructure.

An **operator-initiated** switch (a manual "next scene" / "jump to scene
N" action from management-web or the kiosk itself) is a **separate,
narrower path** — a direct command against the wall, not a Rule — because
an operator action has no predicate to evaluate and no trigger source to
name; modelling it as a degenerate always-true Rule would be a worse fit
than a plain command. **A time-based schedule** (rotate every N minutes,
or at specific times) is modelled as a `TriggerKind` Automation already
has room for (a scheduled/cron-shaped trigger) feeding the same
`SwitchWallSceneAction` — so "timer-driven" is not rejected outright, it
is **one trigger source among several**, not the only mechanism, which is
the actual difference between this decision and "plain timer-based
rotation."

### 3. The wall's currently-active scene is new, small, per-wall state — not a Layout/Revision change

Something has to record "of this scene set, which entry is showing right
now." This is **new state scoped to the wall**, not a change to `Layout`
or `Revision` — those stay exactly as ADR-0112/0156 describe. The
delivering spec decides the exact shape (a field on an existing
aggregate vs. a small new one), but it must satisfy:

- Reading "what is this wall showing right now" must be cheap and
  race-safe against a switch landing concurrently (the kiosk's SignalR
  subscription pattern, ADR-0112 §5, is the existing precedent for
  "one source of truth, pushed to a live subscriber").
- A `SwitchWallSceneAction` firing is itself an event worth an
  integration event (`WallSceneChangedV1` or equivalent) if anything
  outside Automation/LayoutComposition needs to react to it (Audit,
  certainly, per this repo's blanket audit-everything posture) —
  named here, detailed shape left to the spec.

## Consequences

**Positive:**

- No new trigger/predicate engine — Automation's existing Rule lifecycle,
  fab-scoping, and audit trail cover scene-switch rules for free.
- No new content model — a scene set is references to Revisions that
  already go through the full existing publish/validate/audit path
  (including ADR-0156's spanning-tile grid, once that lands).
- An operator can *also* switch scenes by hand without every switch
  needing a Rule to exist first, because the manual path is a plain
  command, not routed through Automation.

**Negative:**

- **This is materially more design work than a fixed timer would have
  been.** A plain "rotate every 30 seconds" needs one field (an
  interval). Event-driven switching needs a new `RuleAction` kind, a new
  per-wall "currently showing" state, and a decision about the
  operator-manual path's relationship to the rule-driven path (can a
  manual switch coexist with an active rotation rule, or does one
  override the other?) — **left open for the delivering spec**, flagged
  explicitly here as a real open question, not a settled detail.
- **A scene switch is a visible, wall-wide change with no operator click
  to attribute it to.** The audit trail needs to name *which rule* (or
  which manual action, by whom) caused a switch — this repo's existing
  "every `*V1` gets audited" posture (AuditObservability) covers this
  once the event exists, but the delivering spec must make sure the
  event carries enough to answer "why did this wall just change" without
  a human having to correlate timestamps by hand.
- **Latency is not on the §IV budget path**, but a scene switch that
  swaps N tiles' cameras simultaneously re-triggers N fresh WebRTC
  session negotiations at once — worth a measured check in the
  delivering spec's phase 5, even though it is not a constitution §IV
  leg, because a wall that visibly stutters on every rotation is a real
  product defect even if it never appears in the latency budget table.

## Alternatives Considered

**A — Plain timer-based rotation (fixed interval, no triggers) —
REJECTED by the product owner** in favour of the event-driven model,
specifically because a fixed interval cannot express "switch when
Automation detects X" or "switch when an operator asks," which the
product owner named as real requirements, not hypothetical future ones.

**B — A new, independent trigger/predicate engine inside
LayoutComposition, separate from Automation's Rule aggregate —
REJECTED.** Automation already owns exactly this shape
(trigger→predicate→action) for a different action kind; building a
second one inside LayoutComposition would duplicate the fab-scoping,
lifecycle, and audit-trail work Automation already does, for no benefit
over adding one new `RuleAction` kind to the existing engine.

**C — A "Scene" as its own authored, versioned content aggregate
(competing with Layout) — REJECTED.** Would need its own grid, publish
lifecycle, and highlight routing — everything ADR-0112 already built —
duplicated for content that is, at bottom, still "a camera in a grid
cell with an optional overlay." A scene set as an ordered reference list
over existing Revisions gets everything for free.

## Implementation Notes

- This ADR intentionally does **not** name the exact new types, the
  integration event's field list, or the migration — that is Phase 1-3
  (architect) work for whoever delivers this, following ADR-0037's normal
  gate, not something decided by fiat here.
- The open question in Consequences (manual switch vs. an active rotation
  rule) must be resolved explicitly in that spec's clarification pass —
  do not let it default silently to either "manual always wins" or "rule
  always wins" without writing down which and why.
- This is architecture-decision-shaped work; ADR-0144 excludes it from
  the autonomous lane. Deliver it through the normal seven-phase
  supervised workflow.
