# ADR-0104: Accept the OverlayDesigner / LayoutComposition revisioned-aggregate duplication

**Status:** **Accepted**
**Date:** 2026-05-31
**Supersedes:** —
**Superseded by:** —

## Context

OverlayDesigner (spec 004) and LayoutComposition (spec 003) both model a
**revisioned aggregate**: an entity that owns an ordered sequence of
revisions, each in `Draft → Published → Archived`, supporting
create-draft → edit → publish → archive → branch-new-draft → revert.

A refactoring-session audit (2026-05-31) measured how much of this is
duplicated between the two bounded contexts:

| Element | Finding |
|---|---|
| `Publish` / `Archive` / `BranchDraft` / `Revert` command handlers | **Byte-identical** — diff is the namespace line only |
| `*RevisionState`, `*RevisionNumber`, `*RevisionIdentifier` value objects | Structurally identical; differ only in the name prefix |
| `Revision` aggregate lifecycle (state machine, numbering, branch/revert) | Identical |
| `Revision` **payload** | **Differs** — Overlay carries a `Label`; Layout carries a camera + an optional overlay binding |
| `Create` / `EditDraft` handlers, DTOs, endpoints | Payload-specific; differ |

So the *lifecycle* is identical and the *payload* varies. That is a real,
nameable shared concept ("revisioned aggregate"), which raises the
question of whether to extract it.

Two constraints bear on the answer:

1. **House rule (CLAUDE.md / ADR boundaries):** no cross-context project
   references; contexts communicate only via `Shared.Contracts`.
   `Shared.Kernel` holds value-object base types, `Result`, `Option` —
   explicitly **no domain**. A revision lifecycle state machine *is*
   domain logic, so extracting it into `Shared.Kernel` would require a
   constitution amendment.
2. **The contexts are already diverging.** LayoutComposition has a
   SignalR lifecycle broadcaster, consumes OverlayDesigner integration
   events, and carries camera/overlay-binding semantics that
   OverlayDesigner has no notion of. They are drifting apart, not
   converging.

## Decision

**Accept the duplication.** Keep the revisioned-aggregate lifecycle
implemented independently in each bounded context. Do **not** extract a
shared base into `Shared.Kernel`; the "no domain in the shared kernel"
rule stands.

This follows the DDD default that **duplication across bounded contexts
is cheaper than the wrong coupling**: a shared lifecycle base would
re-introduce exactly the cross-context coupling the bounded-context
boundary exists to prevent — a change to the "shared" lifecycle would
ripple into every consuming context at once, and the two contexts are
already evolving in different directions.

**Revisit trigger (rule of three):** if a *third* revisioned aggregate
appears, re-open this decision — three instances is enough signal that
the lifecycle is a stable, universal abstraction worth the coupling cost,
and would justify the constitution amendment.

**Intentional-pattern note:** the parallel structure is deliberate, not
accidental. When changing the lifecycle in one context (e.g. a fix to
`PublishRevisionCommandHandler`), check whether the sibling context needs
the same change. The handlers are small and the lifecycle is stable, so
keeping them in sync by hand is low-cost.

## Consequences

**Positive:**

- Bounded-context isolation is preserved; each context evolves
  independently (Layout's broadcaster + integration-event consumption
  already demonstrate this).
- No constitution amendment; `Shared.Kernel` stays domain-free.
- Zero migration risk.

**Negative:**

- A lifecycle bug or enhancement must be applied in both contexts.
  Mitigation: the handlers are byte-identical and small; the
  revisit-at-three trigger bounds how long this stays a two-place edit.
- The parallel scaffolding (state/number/identifier VOs) is maintained
  twice. Accepted as the cost of isolation.

## Alternatives Considered

**Option B — Generic `RevisionedAggregate<TPayload>` in `Shared.Kernel`
— REJECTED.** Would remove the duplication and let a future revisioned
aggregate inherit the lifecycle for free. Rejected because it requires
amending the constitution's "no domain in `Shared.Kernel`" rule and
re-introduces cross-context lifecycle coupling. With only two instances —
already diverging — the coupling cost outweighs the DRY benefit. Revisit
at the rule of three.

**Option C — Codify the pattern via a source generator / template —
REJECTED.** Keeps runtime isolation while enforcing consistency by
generating the lifecycle scaffolding per context from a small spec.
Rejected as over-engineering for two contexts: it adds build-time tooling
and a generator to maintain, for a duplication that is currently cheap to
keep in sync by hand.
