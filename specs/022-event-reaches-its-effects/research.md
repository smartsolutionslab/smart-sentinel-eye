# Research: A plant-floor event reaches the things it is supposed to drive

**Feature**: `022-event-reaches-its-effects` · **Phase 0** · 2026-08-19

Four questions. R1 and R2 decide whether the feature is possible as specified —
FR-010 forbids changing the product to make the effect observable, so if either
answer had been no, this would have stopped at the gate.

---

## R1 — Can a rule be made genuinely active during a test, or does the cache go stale?

**Decision: yes, and the mechanism already exists.**

The evaluator does not read rules from the database. It reads
`IRuleCache.LookupActive(fab, source, kind)`, populated by
`RuleCacheSeederHostedService` at startup. That raised the obvious worry: a rule
created *after* the service started would never be in the cache, so a test would
seed a rule, publish an event, observe nothing, and be unable to tell a broken
journey from a stale cache.

It does not happen. `PublishRuleCommandHandler` calls `cache.Upsert(rule)` as
part of publishing, so activation and cache membership are the same act.

**This matters more than it looks.** FR-006 requires the rule to be *genuinely*
active, because a test whose rule was never eligible passes for the wrong reason
and keeps passing after the journey breaks. The lifecycle is
`Draft → Active → Archived`, `POST /rules` mints a **Draft**, and
`POST /rules/{name}/publish` is what makes it Active. A test that creates a rule
and stops has seeded something that cannot fire.

**Alternatives considered**: seeding the cache directly, or reaching into the
database to flip the state. Both rejected — they would bypass the very
transition being relied on, and a test that arranges its preconditions by a
route no user takes is a test of an arrangement rather than of the product.

---

## R2 — Is the highlight observable without changing the product? (FR-010)

**Decision: yes, over SignalR, with precedent in this repository.**

`OverlayHighlightRequestedV1Handler` calls `ILayoutLifecycleBroadcaster`, which
is LayoutComposition's SignalR hub. That is an outward-facing surface a client
can connect to, not an internal call — so the effect is observable from outside
exactly as an operator's screen observes it.

Precedent, so this is not new ground:
`tests/Integration.Tests/LayoutComposition/OverlayFrameFabScopingIntegrationTests.cs`
and `SignalRRevocationIntegrationTests.cs` already drive `HubConnection` against
the fixture, and `AspireFixture.Auth.cs` has the authenticated-connection
helper.

**FR-010 is therefore satisfied and no finding needs raising.** Had the
broadcaster been an internal call with no observable edge, the honest move would
have been to stop and say so rather than add a test hook to the product.

**Alternatives considered**: asserting on the variable effect alone and leaving
the highlight uncovered. Rejected — the two effects travel to different contexts
by different routes, so covering one proves nothing about the other, and the
spec asks for both.

---

## R3 — What exactly does the test assert, given FR-004?

**Decision: the changed variable value read back through the API, and the
highlight frame received on a hub connection. Nothing in between.**

FR-004 is the requirement that makes this feature worth doing, and it rules out
the assertion a reasonable person would reach for first. Concretely, each of
these would have **passed against the failure this feature exists to catch**:

| Tempting assertion | Why it fails the purpose |
|---|---|
| `FabEventIngestedV1` was published | it was; that leg was never broken |
| Automation evaluated the rule | it did; the effects were computed correctly |
| `SystemVariableValueRequestedV1` was published | **this is precisely what broke** — published into a context nobody flushed |
| the outbox holds a message | wrong layer, and empty in the broken case |
| the event is stored and readable | it was, which is what made the break invisible |

Only "the variable's value changed" and "the highlight arrived" are downstream
of every join. That is the whole design constraint.

**A negative case is needed too** (FR-003): an event matching no rule must
produce no effect. Without it, a test that asserted a value equalling what it
already was would pass on a completely dead system.

---

## R4 — Can this run in the routine build, or does it become a third exclusion?

**Decision: aim for the routine build; treat exclusion as a reportable failure
of this feature, not a tidy fallback.**

Specs 020 and 021 each excluded one test — a measurement burst that would
measure the runner, and a resource restart the runner cannot perform. Both were
defensible and both were recorded with their cost. A third exclusion here would
be different in kind: this is the one path with no coverage at all, so excluding
it returns the system to exactly the state that let the break through.

What makes it plausible: the fixture already runs SignalR tests and MQTT
publishing tests routinely, and this needs no restart, no saturating load, and
no platform operation that fails on the runner. The risk is latency across four
services, which is a matter of waiting properly rather than of capability.

**Waiting properly** means polling to a deadline and distinguishing outcomes
(FR-009): value changed → pass; deadline reached with no change → fail, and say
whether *anything* moved. A fixed sleep would be both slower and less
informative, and asserting on an intermediate step to avoid the wait would
reintroduce exactly the blindness being fixed.

**On the latency budget (SC-005).** The constitution's
`event → overlay state ≤ 200 ms` leg covers this path. Spec 020 measured
arrival-to-visible at p50 146 ms on a developer machine under a saturating
burst, so a single event should be well inside it — but the fixture runs nine
services and a broker on one host, and a number taken there is not the number
that matters in a fab. Measure it, report it, and say what it does and does not
establish, exactly as specs 020 and 021 did rather than quoting a laptop figure
as a guarantee.

---

## Open questions for the plan

None blocking. Two flagged:

1. **Rule authoring needs the publish step.** `CrossFabEvaluationIntegrationTests`
   creates rules and never publishes them, which is correct for what it asserts
   and is exactly the trap for this feature. Any task that seeds a rule must
   publish it and assert it is Active before relying on it.
2. **The dedup store on the SystemVariables side** absorbs repeated requests for
   the same event. That is the behaviour the redelivery edge case depends on,
   and it means a test that publishes the same event twice should still see one
   effect — worth asserting rather than assuming, since redelivery became
   routine with spec 020.
