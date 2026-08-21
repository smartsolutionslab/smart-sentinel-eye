# Verification: The first event after a restart reaches its effect in time

**Feature**: `023-first-event-cold-start` · #1655 · observed 2026-08-21

**Status: the cause is narrowed, not named. The gap is not closed.** What
follows is the record of what was established and what was refuted, including
two hypotheses of mine that died by experiment.

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

## 5. What is left, and how strongly

By elimination: a **per-type, in-process, first-use cost on the consuming side** —
the service that receives a message type doing expensive work the first time it
sees one. `Wolverine.RuntimeCompilation` ships in every service and
`TypeLoadMode` defaults to `Dynamic`, which makes runtime generation and
compilation of the handler the obvious candidate.

**This is elimination, not observation, and is not a conclusion.** The polling
interval reached the same standing an hour earlier and was wrong. What
distinguishes this candidate is only that its rivals were each killed by
evidence.

Confirming it needs one of:

- **Traces.** Blocked here: the ASP.NET dev certificate is untrusted, so the
  Aspire dashboard refuses the MCP connection on SSL and `aspire run` blocks on
  an interactive trust prompt. Trusting it is a machine-level change with a UI
  prompt.
- **Pre-generated handler code with static type loading.** Decisive, and also
  the likely fix — but it needs JasperFx command-line hosting added to nine
  services, their generated code committed and built in.

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

**Unverified, and not claimed:** that those spans arrive and cross service
boundaries. That needs the dashboard, which is blocked as above. Registering a
source is not the same as seeing a trace, and until someone sees one this is
instrumentation on trust.

**Reverted:** the route priming and the `Shared.Contracts` reference it needed.

## What this does not establish

**Nothing about a fab.** Nine services and a broker on one host. Spec 020 and
spec 022 both said a figure from this fixture is not a figure about a plant, and
it applies unchanged. If the cost is Roslyn compilation it will be present in
production; if it is contention for one machine, it may not be.

**Nothing about restarts under load.** Every measurement here starts from an
idle stack.

**Nothing about a rolling restart.** One service restarting while the others
stay warm is the realistic case and was not measured.
