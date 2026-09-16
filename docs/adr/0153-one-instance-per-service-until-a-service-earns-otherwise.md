# ADR-0153: One instance per service, until a service earns otherwise

**Status:** **Accepted**
**Date:** 2026-09-16
**Amends:** Constitution §Availability
**Supersedes:** —
**Superseded by:** —

## Context

Issue #2404 asks a narrow question: SignalR's fan-out in `LayoutComposition` is
in-process `IHubContext` with no backplane, so a second replica would silently
drop frames. Should we add a backplane?

Investigating it produced a wider answer. **At least five of nine bounded
contexts break at two replicas, by four unrelated mechanisms, and only one of
them has ever been written down.** Adding a backplane to the one hub that
happened to be noticed would fix the instance and leave the class.

### What actually breaks at two replicas

| Context | Mechanism | Evidence |
|---|---|---|
| **LayoutComposition** | In-process SignalR groups; Wolverine's queue name is module-prefix + event-type only, so replicas are **competing consumers**. Each event reaches one pod, which broadcasts only to its own connections. | `WolverineDefaults.cs:95-96`, `LayoutLifecycleHub.cs:45-53` |
| **Automation** | Singleton `InMemoryRuleCache`. A rule published on A leaves B evaluating stale rules. | `InMemoryRuleCache.cs:33` — and `:20-24` **already says so**: *"For v1 we run one Automation instance per fab; once we scale to multiple instances the seeder will also subscribe to `RulePublishedV1`/`RuleArchivedV1` to stay coherent across the cluster."* |
| **SystemVariables** | Singleton `InMemoryReverseIndex`, seeded per process, kept fresh by events on the same competing-consumer queues. Only one replica sees each `OverlayRevisionPublishedV1`. | `InMemoryReverseIndex.cs:14,22-24` |
| **EventIngestion** | One MQTT client id with `CleanSession(false)`. Two replicas presenting the same id evict each other in a loop. **Hard-blocked, not degraded.** | `MosquittoOptions.cs:49`, `MosquittoConnectionFactory.cs:70-74` |
| **StreamDistribution** | `StreamHealthWatcher`'s per-process `degradedSince` clock; plus two one-shot startup mutators that would both run — one of which **deletes** MediaMTX paths it considers orphans. | `StreamHealthWatcher.cs:29`, `MediaMtxReconciler.cs:34`, `StreamFabAttributionService.cs:32` |
| **AuditObservability**, **Identity** | Timer-driven hosted services with no leader election; each runs twice. | `AuditRetentionHostedService.cs:31`, `KioskPrivilegeSweepHostedService.cs:32` |

Only **OverlayDesigner** and **CameraCatalog** hold no per-instance state.

### The precedent that should settle the appetite for adding backplanes

**The one service already running at two replicas is broken by it.**
`api-gateway` runs `WithReplicas(2)` (the `// HA (#1005)` block in `AppHost.cs`,
for #1005/ADR-0106) with an **in-process** `FixedWindowRateLimiter` and no
shared store, so a fab's real budget is 100–200/min depending on which replica
it lands on (#2283, open). The same comment records that the two-replica mode
is **disabled under e2e** so the tests resolve a single endpoint — i.e. the
only lane that could have caught it was turned off for an unrelated reason.

That is this decision in miniature: replicas were added to one service, its
per-instance state was not audited, and the gap is invisible in every lane we
run.

### The reconnect-and-reconcile argument does not rescue the hub

It is tempting to say the kiosk's reconnect path covers a brief two-pod window.
It does not, and the reason is mechanical rather than a matter of degree.

The reconcile is real and well-tested — on `onReconnected` the kiosk refetches
layout lifecycle, overlay definitions, overlay availability and resolved values
over REST (`useLayoutLifecycle.ts:98-105`, `CellPage.tsx:299-301`), satisfying
spec 011 **FR-008**, with integration and e2e coverage. First retry is 0 ms.

But during a two-pod window **nothing disconnects**. A kiosk attached to pod A
simply misses whichever frames RabbitMQ routed to pod B — roughly half — while
its connection stays healthy and the live-updates badge stays green.
`onReconnected` never fires, so the reconcile never runs. The reconcile covers
the *end* of the window, not the window.

Worst case during it: a tile rendering a layout an admin has just archived —
precisely the failure `ReconnectReconcileIntegrationTests` exists to prevent.

*(Note: `SignalRLayoutLifecycleBroadcaster.cs:15-17` cites "FR-012" for the
reconcile path. FR-012 is unauthenticated rejection; the reconcile requirement
is FR-008.)*

### Two arguments that turned out not to be available

**The §Availability collision is notional today.** §Availability says *"24/7
operation. Rolling updates are zero-downtime."* — one unscoped sentence,
globally scoped. A zero-downtime rolling update of a single-replica Deployment
runs two pods by construction, so "pin to one replica" appears to contradict it.

But **no deployment artefact exists**: `deploy/` holds two Mosquitto files, there
is no Chart, Deployment, Service or Ingress anywhere, and no Kubernetes
publisher is wired in AppHost. Nothing can perform a rolling update, for any
service. And #2125 records that `/health` returns `Healthy` unconditionally, so
even with a Deployment the overlap window would be governed by timers rather
than readiness. The constitution states a requirement that nothing implements.

**No latency argument is available to anyone.** The `event → overlay state`
figures often quoted as a breach — 555 ms and 758 ms — are **cold, n=1 samples
of a server-side sub-span**, taken after a three-context reset
(`ResolvedTextReachesItsFabTests.cs:120-132,151-156`). Spec 108 re-measured a
strictly **containing** span (click → paint, real browser) at **79 ms warm** and
**102–233.5 ms cold** (`specs/108-*/verification.md:257-278,485-498`). §IV
records the leg as *"recorded, not yet readable"*, not breached. ADR-0152
declined a latency argument for this reason and this ADR does the same.

## Decision

Accepted 2026-09-16. Issue #2404 tracks the implementation; the §Availability
amendment in §4 lands with this acceptance. Nothing here changes runtime behaviour
on its own — every service already runs at one instance except `api-gateway`.


### 1. No service in this system runs more than one instance

Stated as a property of the system, not of one hub. Every context except
`OverlayDesigner` and `CameraCatalog` holds state that a second instance
corrupts, and the list above is the record.

### 2. `api-gateway` is the exception, and it is a known-broken one

It already runs at two replicas. This ADR does not revert that — it records that
#2283 is a live defect of that decision and that the gateway's rate limiter must
either move to a shared store or the replica count must come back to one.
**Choosing between those is #2283's job, not this ADR's.**

### 3. A service earns a second instance by demonstrating it, not by asserting it

Before any service's replica count rises above one, its per-instance state must
be enumerated and each item resolved — shared, made idempotent, or leader-elected
— and a test must exist that **runs two instances and fails without the fix.**

This clause exists because neither a backplane nor a relay is verifiable today:
**no lane in this repository runs two replicas of anything**, so a backplane
wired backwards would pass the entire suite. The first honest test of any
horizontal-scaling fix is the same two-instance harness that would demonstrate
the defect, and that harness does not exist.

### 4. §Availability is amended to say what is true

The zero-downtime clause is not deleted — it is qualified with its own status:
the requirement stands as an aspiration, nothing implements it, and pinning to
one instance is the current, deliberate position rather than an oversight. A
requirement recorded as met when nothing implements it is the drift this
repository has corrected four times.

### 5. The backplane question is answered "not now", with the reason

For `LayoutComposition` specifically: **no backplane.** Not because a backplane
is wrong — ADR-0152 §4 correctly observes one would fix it — but because it buys
horizontal scaling for one context out of five that need it, adds either a
second datastore or hand-written relay code, and cannot be tested. When the
system earns clause 3's harness, the backplane is the first thing to build
against it.

## Consequences

**A class becomes visible where an instance was.** Five contexts and four
mechanisms are now written down together, with citations, instead of one hub's
defect and a comment in `InMemoryRuleCache` that nobody had connected to it.

**Horizontal scaling becomes a deliberate act with a cost.** Clause 3 makes
"add replicas" require an audit and a two-instance test. That is a real barrier
and it is the point: the gateway shows what happens without one.

**One aspiration is demoted to a recorded gap.** §Availability's zero-downtime
clause stops reading as a satisfied requirement. Some will read that as a
regression in ambition; it is a correction in accuracy, and the ambition is
unchanged.

**The hub's silent-staleness window remains reachable in principle.** If someone
adds a replica in an environment this repo does not describe, kiosks go stale
behind a green badge. Clause 1 is the mitigation and it is only as strong as the
absent deployment artefacts make it — which is to say, currently absolute and
eventually not.

**Redis stays out.** ADR-0071 and ADR-0088 both rejected it as operational
surface — *"Postgres-only operational surface"* — and this ADR does not reopen
that on the strength of one hub.

## Alternatives Considered

**Add a Redis backplane now.** Rejected. It contradicts two standing ADRs on
operational surface, introduces a second datastore with its own HA story (a
single-replica Redis reinstates the SPOF one level down), needs an image pin and
a chart that does not exist, and fixes one of five contexts. Decisively: it
cannot be verified — every SignalR integration test passes at one replica
whether the backplane works or not.

**Hand-write a RabbitMQ relay.** Rejected on the same untestability plus a worse
maintenance story: there is no Microsoft-supplied RabbitMQ backplane, so this is
bespoke lifecycle code — exclusive-queue cleanup, relay loops, ordering across
two paths — in a system that cannot exercise it. It uses only sanctioned
infrastructure, which is its genuine merit, and it remains the better of the two
if clause 3's harness ever exists.

**Pin only `LayoutComposition` to one instance.** Rejected as the narrow version
of this ADR: it answers #2404 and leaves Automation, SystemVariables,
EventIngestion and StreamDistribution in the same state, four of them
undocumented. The scope is the finding.

**Do nothing; the defect is latent.** It is latent — `WithReplicas` appears once
in the repo and not on any affected service. But "latent" here means "waiting for
the first person to add a replica", and that person's most likely reason is an
outage, i.e. the worst moment to discover that five services corrupt silently.

## Implementation Notes

- Do **not** implement before this ADR is accepted. #2404 tracks it.
- Clause 1 wants a **guard, not a comment** — but note a startup assertion that
  reads a configured replica count proves only that the configuration was read.
  That is the weakest class of guard in this repo's own catalogue, and it is
  worth saying so where the guard is written.
- Clause 3's two-instance harness is the substantial piece of work here and the
  prerequisite for every other option. Sizing it is its own slice.
- The `// HA (#1005)` comment in `AppHost.cs`, guarding
  `apiGateway.WithReplicas(2)` — that the gateway is pinned to one replica under
  e2e so tests resolve a single endpoint — is the exact obstacle clause 3's
  harness must solve. Start there.
- Each context in the table deserves its own issue, or one issue with the table
  in it, so the list does not live only in this ADR.
- The FR-012/FR-008 miscitation in `SignalRLayoutLifecycleBroadcaster.cs:15-17`
  is a one-line fix and unrelated to this decision.
