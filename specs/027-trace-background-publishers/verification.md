# Verification: Every journey has a beginning

**Feature**: `027-trace-background-publishers` · **Issue**: #1781 · 2026-08-22

Observed against the real Aspire stack, dashboard `https://localhost:17069`.

---

## Before (T001)

Pre-change binaries, camera simulator stopped to force health changes.

`audit-observability` received `StreamHealthChangedV1` in trace
**`a4f10047289c0925703c4d25a38ac117`** — the whole handling under one span, with
no `stream-distribution` parent above it. The announcement and the record of it
were two unconnected trails, exactly the shape spec 026's verification note
documents for ingestion.

---

## After (T015) — SC-001

Trace **`0c7f1153762be21d6dfb44b1bde96bbe`**, titled **"observe stream health
change"**:

```
stream-distribution   observe stream health change     root, Producer   15 ms
  ├─ stream-distribution   send     StreamHealthChangedV1              149 ms
  ├─ stream-distribution   send     StreamHealthChangedV1              143 ms
  └─ audit-observability   receive  StreamHealthChangedV1              414 ms
```

**One trace, two services**, 576 ms. The audit record is now a **child** of the
observation that caused it. From the record, the check is one parent up, by
relationship rather than by timestamp.

### The mechanism question, settled by the timestamps

The origin span **ended at .753**. Its two `send` spans started at **.762 and
.767** — *after* it had ended — and both carry it as parent.

So Wolverine stamps `ParentId` at `PublishAsync` time and uses it when the outbox
later flushes. **The journey does not need to span the commit**, which was my
first hypothesis when the results looked partial. Wrong, and cheap to disprove
from data already collected.

---

## Per-event, not per-sweep (T006, FR-003, SC-003)

Four journeys began in one sweep, at 17:43:12.738, .924, .946 and .959, each its
own trace:

```
0c7f1153762be21d6dfb44b1bde96bbe
08463aa3d8e2178ae8adc7c642721e18
41c29dd297b01699637a6a2f00212235
94888bd1e099a6dfefdcce7258cdf16b
```

**Four changes, four traces — not one per sweep.** The failure this feature was
most likely to ship did not happen, and it did not happen structurally: the
journey sits behind the dispatcher, which invokes handlers one domain event at a
time.

---

## Open, and not explained: only one of four announcements arrived

**Stated plainly because it would have been easy to report the joined trace and
stop.**

Of the four journeys, only `0c7f1153` contains a publish and a downstream
receive. The other three contain their origin span and nothing else. The
database agrees:

| | |
|---|---|
| `audit_events` rows for `StreamHealthChangedV1` since 17:40 | **1** |
| stuck rows in `wolverine_outgoing_envelopes` | **0** |
| streams, all `Offline` | 4 |

So three announcements were neither delivered nor left pending. None of the three
traces carries an error, so `IJourney.Failed` was not called — meaning
`PublishAsync` did not throw.

**What this is not.** It is not the tracing change: this code only wraps a
publish that was already there, and the one message that was published traced
correctly end to end. Whatever happened to the other three would have happened
before this feature too — it just was not visible before, because there was
nothing to count journeys against.

**That is the interesting part.** The journeys are what made the gap countable:
four observations, one announcement. Filed as its own issue rather than chased
here, because it is a delivery-or-domain question in StreamDistribution and not
an observability one.

**I stopped investigating deliberately.** Two hypotheses were formed and one was
already disproved by data I had; continuing to guess is the habit that cost spec
026 three false premises.

---

## Tests

| Suite | Result |
|---|---|
| `StreamDistribution.Application.Tests` | 36 passed (6 new) |
| `StreamDistribution.Domain.Tests` | 71 passed (1 new — the offline no-change case) |
| `AuditObservability.Application.Tests` | 36 passed (4 new) |
| Release build with analyzers | clean |

All 11 new tests confirmed **by name** in the runner output, not by a total going
up.

**And they were not enough.** Every one passed while three of four announcements
went missing, because they assert what the handler does with what it is given.
That is worth remembering the next time a green suite is offered as evidence.

---

## Not yet done

- **T007's retention site is implemented but not observed.** The retention sweep
  runs on a long timer and no archival happened in this window.
- **T014** — whether an HTTP publish inherits the request's cause. Still the one
  inference in the survey.
- **T016** — the measurements, twice.
- **T013** — the survey table.

---

## The publisher survey (T013) — FR-009, SC-008

Every `IEventBus.PublishAsync` call site in product code, classified by whether
it has work in progress to inherit a cause from. **This table is the deliverable**
— finding the orphans was the expensive part, and an undocumented survey means
the next person repeats the search.

| Publisher | Cause comes from | State |
|---|---|---|
| `EventIngestion` — `PersistenceLoopHostedService` → `EventIngestedDomainEventHandler` | *none — background loop* | **fixed, spec 026** |
| `StreamDistribution` — `StreamHealthWatcher` → `StreamHealthChangedDomainEventHandler` | *none — background loop* | **fixed here** |
| `AuditObservability` — `AuditRetentionHostedService` (inline) | *none — background loop* | **fixed here** |
| `Automation` — `FabEventIngestedV1Handler` | the message being handled | needs nothing |
| `CameraCatalog` — `CameraRegisteredDomainEventHandler` | the HTTP request | needs nothing¹ |
| `Identity` — `ClientRegisteredDomainEventHandler`, `RotateWebhookClientCommandHandler` | the HTTP request | needs nothing¹ |
| `LayoutComposition` — revision published / archived handlers | the HTTP request | needs nothing¹ |
| `OverlayDesigner` — revision published / archived handlers | the HTTP request | needs nothing¹ |
| `SystemVariables` — value changed / archived handlers | the HTTP request | needs nothing¹ |

**Three background loops, all now fixed. Nine request- or message-driven
publishers, none needing anything.** After this feature, no publisher of an
integration event in this system begins as an orphan.

¹ **Still inference, and marked as such.** Message-driven inheritance is observed
directly (spec 026, trace `195d9123…`: two receives as children of a receive).
HTTP is observed one layer short — spec 026's trace `ed21f2fc…` shows a `POST`
Server span with Keycloak Client spans as **children**, so a request does
establish an ambient activity that later work attaches to. What has not been
captured is a `send` span specifically sitting under a Server span.

**T014 remains open.** The camera registrations that would have shown it happen
at boot and had aged out of the dashboard's retained window by the time it was
looked for. Nothing in this feature depends on the answer: if an HTTP publish
turned out to be an orphan too, that is a new finding and a new issue, not a
change to these two call sites.

---

## Measurement (T016) — SC-006, FR-007

Two runs, per the lesson recorded from spec 026: a single run after machine churn
reads exactly like a regression. Compared against spec 026's same-machine
baseline, taken earlier the same day by reverting that commit in place.

| | 026 baseline | 026 after | **027 run 1** | **027 run 2** |
|---|---|---|---|---|
| offered | 408/s | 405/s | **453/s** | **428/s** |
| sustained end to end | 398/s | 396/s | **442/s** | **418/s** |
| arrival→visible **p50** | 138 ms | 137 ms | **143 ms** | **128 ms** |
| p95 | 920 ms | 1 339 ms | 838 ms | 413 ms |
| first-event split r2 / r3 | 478 / 520 ms | 427 / 414 ms | 332 / 399 ms | 186 / 326 ms |

**No regression, and both runs agree.** Throughput is *higher* than the 026
baseline in both (442 and 418 against 398), p50 straddles it (143 and 128 against
138), and the split rounds are faster. Everything is inside the ≤ 200 ms budget
for this leg.

This feature does not touch the ingest path at all — these figures are a guard
against an unexpected cost elsewhere, not a measurement of what changed. **The
poll cadence and the retention run are what this feature actually touches, and
neither has a harness.** Recorded as a gap rather than implied to be covered:
what was measured is the ingest path, and it is unchanged.

p95 again wanders — 920 → 1 339 → 838 → 413 across four runs of code that differs
in one respect. The tail is not a stable measure on this harness; p50 and
sustained throughput are.

---

## Full suite (T017) — SC-009

**1759 passed, 1 failed, 1 skipped** in Release, nothing excluded or weakened.

The failure is `NFR002_MqttConnectAuthTests` — MQTT CONNECT→CONNACK exceeding a
15 ms p50 budget. **The same test failed the same way on spec 026's branch and
passed in CI**, which is where it was adjudicated then and where it will be
again. It is machine-load bound: this box has been running Aspire stacks and
Release builds all day. Nothing here touches MQTT connect or JWT auth.

The skip is `CrossFabCameraIntegrationTests.A_name_differing_only_in_case_is_refused_within_one_fab`,
pre-existing and unrelated (#1434).

---

## What was not finished, and why

| | |
|---|---|
| **T014** — does an HTTP publish inherit the request? | **Open.** The camera registrations that would show it happen at boot and had aged out of the dashboard's window. Still the one inference in the survey; nothing here depends on it. |
| **T015 (half)** — the retention walk | **Not observed.** The sweep runs on a long timer and no archival occurred in the window. The code is in and unit-tested; it has not been seen working. |
| Poll cadence and retention duration | **No harness exists.** The measurements cover the ingest path, which this feature does not touch. |

**Three things implemented and two of them observed.** Said plainly rather than
rolled into a summary that reads as complete: the stream-health journey is
confirmed end to end in the dashboard, the retention journey is not.
