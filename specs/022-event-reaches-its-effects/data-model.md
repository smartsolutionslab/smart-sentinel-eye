# Data Model: A plant-floor event reaches the things it is supposed to drive

**Feature**: `022-event-reaches-its-effects` · **Phase 1** · 2026-08-19

**No model changes, no migrations, no new entities.** This feature adds a test.
What follows is the state the test arranges and the state it observes — because
getting either wrong is how the test passes for the wrong reason.

---

## What the test arranges

### A rule, and the state it must be in

```
Draft ──POST /rules/{name}/publish──▶ Active ──▶ Archived
```

`POST /rules` mints a **Draft**. Only **Active** rules are evaluated:
`RuleEvaluator` reads `IRuleCache.LookupActive(...)`, and a rule enters that
cache when `PublishRuleCommandHandler` upserts it during publishing.

**This is the trap in the feature.** A test that creates a rule and stops has
seeded something that cannot fire. It would observe no effect, and would keep
observing no effect after the journey broke — passing identically in both
worlds if it asserted the wrong thing, or failing for a reason that looks like a
product bug if it asserted the right one. Hence FR-006: assert the rule is
Active before publishing the event.

`CrossFabEvaluationIntegrationTests` creates rules and never publishes them.
That is correct for what it asserts (the stored rule) and is exactly the shape
to not copy here.

### A rule's trigger and action

The fields that matter, as the API takes them:

| Field | Role in this test |
|---|---|
| `triggerSource`, `triggerKind` | must match the published event, or nothing fires |
| `predicate` | a JSONPath condition over the payload; must be true for the event sent |
| `actionType` | `SetVariableValue` for the value effect, the highlight action for the other |
| `variableName`, `valueExpression` | what changes, and to what |
| `overlayIdentifier`, `durationMs` | for the highlight effect |

The negative case (FR-003) is a rule whose trigger or predicate the event does
**not** match — same seeding, deliberately non-matching.

### An event, entering the way a machine sends one

Published over MQTT to `fab/<fab>/plc/<device>`, not posted to an HTTP endpoint.
FR-002 asks for the real ingress: it is where spec 020 lived, and a shortcut
into the middle of the chain would leave the first join untested.

---

## What the test observes

Two effects, in two different contexts, reached by two different routes — which
is why covering one proves nothing about the other.

| Effect | Where it becomes observable | Read as |
|---|---|---|
| the variable's value | SystemVariables read API | the value an operator's overlay would resolve |
| the overlay highlight | LayoutComposition SignalR hub | the frame a kiosk would receive |

**Neither is read from a database directly**, and that is deliberate. The point
is to observe what an operator observes; a row read behind the API could be
right while the thing on the screen is wrong.

---

## What the test must never assert

Recorded here because it is a data question — *which* state counts as evidence —
and because each of these would have passed against the failure that prompted
the feature:

- that `FabEventIngestedV1` exists or was published
- that the rule was evaluated, or that effects were computed
- that `SystemVariableValueRequestedV1` was published — **the broken code did
  exactly this**, into a context nobody flushed
- that an outbox row exists
- that the event is stored and readable — it was, and that is what hid the break

The rule: **assert only state that is downstream of every join.** There are
exactly two such pieces of state, and they are in the table above.

---

## State this test leaves behind

It seeds a rule and changes a variable, both in the shared fixture database.

- Rule names are unique per run, so no test collides with another's.
- The variable it sets is its own, for the same reason.
- Nothing is dropped or truncated — unlike specs 020 and 021, this feature needs
  no partition drops, so there is no destructive setup to restore and no risk of
  poisoning the rest of the collection.

That last point is worth stating plainly: the two previous features each had a
test that could take out an entire fixture run when its cleanup failed. This one
cannot, and the reason is that it only ever adds.
