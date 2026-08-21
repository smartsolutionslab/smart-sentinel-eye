# Verification: The first event after a restart reaches its effect in time

**Feature**: `023-first-event-cold-start` · #1655 · observed 2026-08-21

**Status: the cause is narrowed, not named. The gap is not closed.** What
follows is the record of what was established and what was refuted, including
**three hypotheses of mine that died by experiment** — and one correction to
#1655 itself, which claimed an operational impact the evidence does not support.

---

## 1. The gap is real and reproducible

`EventReachesItsEffectsTests` from a cold stack, **in execution order** — which
turned out to matter more than the totals:

| # | Test | arrival → effect | first publish of |
|---|---|---|---|
| 1 | matched-only | **14 041 ms** | `FabEventIngestedV1` + `SystemVariableValueRequestedV1` |
| 2 | redelivered | 586 ms | — |
| 3 | highlight | **3 593 ms** | `OverlayHighlightRequestedV1` |
| 4 | value | 270 ms | — |

**The third test is slow despite running third.** Position does not predict cost.
It is the only test that publishes `OverlayHighlightRequestedV1`, and its ingress
is identical to the fast tests either side of it, so its 3.6 s is entirely
downstream of ingestion.

## 2. Which half owns it

Split with observables that already exist — publish returns, event readable,
effect readable — so nothing had to change to narrow the search:

```
round 1: ingress+store 3929 ms | announce+decide+apply 12054 ms | total 15983 ms
round 2: ingress+store  202 ms | announce+decide+apply   317 ms | total   519 ms
round 3: ingress+store  143 ms | announce+decide+apply   213 ms | total   356 ms
```

Three quarters lands after the event is durable. Both halves are cold-slow and
both collapse by the second event, so this is not one lazy thing in one service.

**These are upper bounds.** `t1` is "a read returned the event", not "the event
was stored", so each half carries its own read API's first-call cost. Stated
because the numbers are otherwise easy to quote as if they were clean.

## 3. The cost is per message type — confirmed by intervention

Four events in one run, each with its own trigger kind so exactly one rule
fires. **The design is a discriminator**: round B introduces one new message
type *after* a complete event has already finished, so "the first event pays for
everything" and "each type pays once" predict opposite results for it.

| Round | New message type(s) | arrival → effect |
|---|---|---|
| A | `FabEventIngestedV1` + `OverlayHighlightRequestedV1` | 10 827 ms |
| B | `SystemVariableValueRequestedV1` | **4 815 ms** |
| C | none | 199 ms |
| D | none | 134 ms |

**B is slow, so the cost attaches to message types**, not to processes or
events. D — a repeat highlight at 134 ms — shows the highlight path is fast once
warm, so A's eleven seconds were its two new types rather than its position.

About 5 s per new type, **near-constant rather than proportional to work**. The
journey crosses three types, which is where twelve to fourteen seconds came
from.

## 4. What it is not — three hypotheses killed

**Not the ingest loop's poll** (ruled out by reading). `ReadBatchAsync` returns
as soon as a delivery arrives; the backoff is on the retry branch only. This is
the explanation most people reach for first.

**Not the outbox schema build** (ruled out by reading). `AutoBuildMessageStorage
OnStartup` runs during host start.

**Not Wolverine's polling intervals** (refuted by intervention, **and this one
was mine**). A near-constant 5 s looked like a timer, and Wolverine's defaults
offered `NodeReassignmentPollingTime` and `ScheduledJobPollingTime` at exactly
5 s. Setting both to 1 s left B at 4 924 ms against 4 815 ms — unchanged.

That deserves its own line, because of how publishable the wrong version was: a
named mechanism, a number agreeing to within 4%, and no experiment. It would
have read as a finding. One run cost it.

**Not broker provisioning** (excluded by observation). Asking RabbitMQ's
management API before publishing anything returned every integration event's
queue already declared at startup — including all three on this journey. A queue
that already exists cannot be created by the publish that follows it.

*The census first returned `401`, which prints identically to "no queues" while
meaning "no answer" — a failed measurement that reads as a successful one.
Credentials now come from the connection string.*

**Not the sending side** (refuted by intervention, **also mine**). Wolverine
exposes `RoutingFor(Type)`, which resolves a type's outgoing route without
sending anything — so the cost could be moved to startup with no invented side
effects. Priming every integration event at startup left B at 4 619 ms against
4 497 ms, and startup did not lengthen, which is its own evidence: priming ~40
types would have added minutes if `RoutingFor` were the expensive operation.

**That change is reverted.** It is production code that did not fix what it was
written for, and keeping it would leave a warm-up in the tree justified by a
measurement that refuted it.

## 5. What the traces showed — including where I was wrong

The dev certificate was trusted, the dashboard became reachable, and the
instrumentation from §7 could finally be checked rather than trusted.

**T006 is verified.** Spans cross service boundaries under one trace id. A
complete journey, warm:

| Span | Service | Duration |
|---|---|---|
| `receive` `FabEventIngestedV1` | automation | 7–13 ms |
| `send` `OverlayHighlightRequestedV1` | automation | 0 ms |
| `receive` | layout-composition | 0–1 ms |
| `receive` | audit-observability | 10–13 ms |
| **whole trace** | | **22–29 ms** |

So the journey is now visible end to end, which is what §VII has required all
along and what nothing in this system could do before.

### The consuming-side hypothesis does not survive

Spec 023's standing candidate was that the consuming service does expensive
work — most likely generating handler code — the first time it sees a message
type. That predicts a slow first `receive` after a consumer restarts.

`layout-composition` was restarted twice while the simulator kept publishing.
Its **first** `receive` of `OverlayHighlightRequestedV1` after restart took
**0 ms**, inside a 22 ms journey.

**That is the third hypothesis of mine to die by measurement**, and it goes the
same way as the other two: written down, not quietly replaced.

### What that changes about the problem itself

A **rolling restart of one service does not reproduce the cost.** Every
measurement that showed 5 s per message type was taken on a fixture where the
whole stack — services, broker, databases — started fresh together.

That matters more than the mechanism, because **#1655 claimed the opposite**. It
said the fixture's cold start "means in a fab exactly what it means in the
fixture: the first event after a deployment or a pod replacement", and on a
rolling restart that now looks wrong. What reproduces it is everything being
cold at once, which in production is a full cluster start rather than the
routine case.

The operational significance is therefore **smaller than the issue asserted**,
and the issue should be corrected rather than left to be read as it stands.

### What is still unexplained

The per-type cost is real, reproducible and confirmed by intervention on a
fully-cold stack. It is not polling, not broker provisioning, not sending-side
route resolution, and now not the consuming side's first receipt either. It
remains unexplained, and no candidate is currently standing.

The most likely next step is to catch a fully-cold stack with the dashboard
attached — every trace read here came from a stack that had been warm for
minutes, because the dev AppHost boots with a simulator already publishing.
## 6. Success criteria

| | Status |
|---|---|
| SC-001 — ≥ 80% attributed to a **named** stage | **Not met.** Attributed to "first use of a message type, on the consuming side", which is a stage but not a named mechanism. |
| SC-002 — the decay explained | **Met.** It is not a decay over time; it is one payment per message type, and the journey has three. |
| SC-003 — every candidate gets a verdict | **Met.** Five ruled out or refuted, one standing by elimination. |
| SC-004 — first event under 1 s | **Not met.** No fix attempted; the one tried was refuted and reverted. |
| SC-005 — steady state no worse | Held throughout: 190–369 ms across every configuration measured. |
| SC-006 — suite passes, nothing weakened | Both new measurements are `Category=Measurement`, which CI already excludes, as spec 020's harness is. |

## 7. What was changed, and what was not

**Kept:** Wolverine's activity source registered in `ServiceDefaults`, so the
journey's hops can be traced at all. Constitution §VII makes a dashboard
mandatory for a budgeted leg and this leg had neither dashboard nor spans
through six features. The name is read off `WolverineTracing.ActivitySource`
rather than spelled out, because a typo in that string registers nothing, raises
nothing, and leaves a journey exactly as untraced as before.

**Verified** (§5), once the dev certificate was trusted: spans arrive and carry
one trace id across services. Registering a source is not the same as seeing a
trace, so it stayed unclaimed until a trace was seen.

**Reverted:** the route priming and the `Shared.Contracts` reference it needed.

## What this does not establish

**Nothing about a fab.** Nine services and a broker on one host. Spec 020 and
spec 022 both said a figure from this fixture is not a figure about a plant, and
it applies unchanged. With no mechanism named, whether it appears in production
at all is now genuinely open.

**Nothing about restarts under load.** Every measurement here starts from an
idle stack.

**Now measured, and it is the correction in §5:** one service restarting while
the others stay warm does **not** reproduce the cost. #1655 asserted it would.

**Nothing about a full cluster start in production.** That is the condition that
does reproduce it here, and it was only ever reproduced on this fixture.
