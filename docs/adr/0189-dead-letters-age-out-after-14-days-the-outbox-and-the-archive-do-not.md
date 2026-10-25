# ADR-0189: Dead letters age out after 14 days; the outbox and the audit archive do not

**Status:** **Accepted** by the owner on PR #3146, 2026-10-25, with three rulings at acceptance (dead-letter queue depth gets a health check or metric; the management-API fallback for the broker policy is sanctioned; a Degraded readiness result satisfies Decision 2's "alerted on" until a production telemetry sink exists), see *Ruled by the owner at acceptance*
**Date:** 2026-10-25
**Amends:** ADR-0184 (Decision point 8, which left the dead-letter and cold-tier retention to #3092 and set no limit; and the retention note of its Implementation Notes step 6, which says dead letters are kept until a retention rule removes them). ADR-0185 (the *Dead-letter caveat* under decision 4, which links undelivered outbox rows to #3092: this ADR answers that link). Each carries an *Amended by: ADR-0189* line (added 2026-10-25, on acceptance).
**Relates to:** ADR-0088, ADR-0101, ADR-0118, ADR-0144, ADR-0155, ADR-0179, issues 2990, 3092, 3115

## Context

ADR-0184 left three places holding full camera URLs, which ADR-0179 treats as credentials, to "age"
without saying how long. #3092 asked for that limit, and #3115 found where each place is and whether
anything expires it (finding posted on #3115 and #3092, 2026-10-25; WolverineFx 6.51.0 decompiled; live,
read-only queries against the persistent dev containers):

| Place | What it can hold | Expires today? |
|---|---|---|
| RabbitMQ queue `wolverine-dead-letter-queue` | The full body of any failed `CameraRegisteredV1` / `CameraAddressChangedV1` delivery (StreamDistribution, AuditObservability, simulator listeners), and of any other failed message of any context | **No.** One classic durable queue shared by all nine contexts and the simulator (Wolverine's default `DeadLetterQueueMode.Native`; `WolverineDefaults.cs` never changes it). Its only argument is `x-queue-type=classic`: no `x-message-ttl`, no DLX. `rabbitmqctl list_policies` is empty. Holds 0 messages. |
| Postgres `wolverine_<context>.wolverine_dead_letters`, one per context | Nothing on current wiring: every listener, Inline or durable-inbox, dead-letters to RabbitMQ | **No.** `DeadLetterQueueExpirationEnabled` defaults to `false` and is set nowhere in `src/`; the tables have no `expires` column. All nine are empty. |
| `wolverine_camera_catalog.wolverine_outgoing_envelopes` | An undelivered durable `CameraRegisteredV1` / `CameraAddressChangedV1` (ADR-0185 decision 4) | **No, by design.** No `DeliverWithin`/`DeliverBy`; deleted only on delivery. `OutboxBacklogHealthCheck` reports Degraded at `ConcerningAttempts = 5`. |
| Azure Blob `audit-archive` (cold tier) | Only chunks archived **before** ADR-0184's redaction step first ran: a closed set | **No.** No lifecycle rule anywhere; Azurite implements no lifecycle management. Zero chunks exist in dev; no production deployment exists (ADR-0118). |

Two mechanism facts bound the choice:

- **Wolverine's own queue TTL cannot be added to an existing broker.** `DeadLetterQueue.ConfigureQueue(q =>
  q.TimeToLive(...))` declares `x-message-ttl` as a queue argument. On a queue that already exists without
  it, RabbitMQ refuses the redeclaration as an "inequivalent arg" and `RabbitMqQueue.cs:447-453` turns that
  into an `InvalidOperationException` at startup. `TimeToLive` also converts to `Int32` milliseconds
  (`RabbitMqQueue.cs:356`), so it overflows above about 24.8 days.
- **A RabbitMQ policy changes no queue arguments**, so nothing is redeclared and nothing fails on startup.
  No broker configuration is repo-owned today: `AppHost.cs:123-128` adds RabbitMQ with a password and
  persistence only.

The owner ruled on #3092/#3115 on 2026-10-25. This ADR records those rulings.

## Decision

**1. Dead letters age out after 14 days, for every context.** One period, because there is one queue: a
per-type or per-context period would need per-queue dead-letter queues, which nobody has asked for. 14 days
is an operational triage window (two working weeks of a 24/7 operation, above Wolverine's 10-day default);
the audit trail already keeps the *fact* of each event, redacted, so the dead letter is not the record.
The same period applies to the Postgres dead-letter tables.

**2. An undelivered outbox row is never aged out.** Expiring a durable `CameraRegisteredV1` /
`CameraAddressChangedV1` envelope would drop an audit-before-disclosure event silently, which contradicts
ADR-0185 and ADR-0179. A backlog is **alerted on and drained** by an operator instead;
`OutboxBacklogHealthCheck` already reports it. No `DeliverWithin`/`DeliverBy` is added to any durable type.

**3. Archived audit chunks get no credential-driven age-out.** The credential-bearing subset is closed and
empty everywhere today. Instead: a **one-time check at the first production deployment** that no chunk in
`audit-archive` predates the first applied run of ADR-0184's redaction step, and **a runbook line** saying
so. This is revisited when the production object store is chosen (ADR-0155, "Production's object store is
explicitly still open"). A general cold-tier retention period is a compliance question, not this one, and
would need its own ADR.

### Mechanism

- **Broker (the real age-out).** A RabbitMQ definitions file, owned by the repo and mounted into the broker
  by the AppHost, carries one policy: pattern `^wolverine-dead-letter-queue$`, `apply-to: queues`,
  `message-ttl: 1209600000` (14 days in ms). Expired messages are discarded, because the dead-letter queue
  has no DLX. It is the **first broker configuration in the repository**; the same file is what any future
  k3s chart for RabbitMQ mounts (none exists).
- **Postgres (defence in depth).** `WolverineDefaults` sets `Durability.DeadLetterQueueExpirationEnabled =
  true` and `Durability.DeadLetterQueueExpiration = 14 days`. Wolverine's `AutoCreate.CreateOrUpdate` adds
  the `expires` column; the durability agent deletes expired rows hourly. Nothing lands there today; this
  keeps the period true if a listener is ever switched to Wolverine-storage dead-lettering.
- **Wolverine's `TimeToLive` is not used** (Alternatives).

## Consequences

**Positive.** A dead letter, including one holding a full camera URL, is gone 14 days after it was
dead-lettered, without an operator. The period is one number in one file plus one in `WolverineDefaults`,
observable on the broker (`effective_policy_definition`) and in the table schema. The audit record is never
traded for exposure (Decision 2).

**Negative, accepted.**

- **A failed message older than 14 days is gone.** The window to triage it, fix its cause and replay it is
  14 days from the moment it was dead-lettered, not from when someone noticed. Any replay tooling, existing
  or future, must act within it; a message replayed after expiry cannot be.
- **One shared queue means one period for all contexts.** A context that wants longer (or shorter) cannot
  have it without per-queue dead-letter queues and a new ADR.
- **A new file to keep in step.** The broker's retention lives outside .NET code. Changing the period means
  changing the policy *and* `WolverineDefaults`; a test asserts the two agree (Implementation Notes).
- **Persistent dev containers keep their old mounts** (memory note, *Persistent containers keep their old
  args*): a developer's existing RabbitMQ container must be removed (`docker rm`, volume kept) to pick the
  file up. The runbook says so.
- **Outbox exposure stays bounded only by delivery.** An undelivered camera-URL envelope rests beside the
  `cameras` row until an operator drains it (Decision 2). There is no production telemetry sink yet
  (ADR-0118), so "alerted on" is today a Degraded readiness result, not a page. **Ruled at acceptance:**
  that Degraded result satisfies Decision 2 until a production telemetry sink exists; when one does,
  the backlog alert moves to it.
- **Dead letters themselves need watching.** A 14-day window only helps if someone sees a dead letter
  inside it. **Ruled at acceptance:** the follow-up adds a health check or metric for the dead-letter
  queue's depth, not a runbook line alone (Implementation Notes, step 4).

**Not verified** (the implementing slice establishes each before claiming it):

- **Whether the exception text stored with a dead letter can include the source address.** Wolverine stamps
  the exception message into the dead letter's headers. If any handler's exception for these two types
  echoes the URL, it sits in the headers too; the TTL removes it with the body, but until then it is a
  second copy.
- **RabbitMQ's maximum message TTL** and whether 14 days in ms is accepted by the pinned broker image as a
  policy value. 1,209,600,000 ms is below `Int32.MaxValue`, but the broker's own bound was not checked.
- **That a definitions file loaded at boot coexists with the AppHost's default user.** RabbitMQ can skip
  creating the default user and vhost when definitions are imported at boot; if the pinned image does, the
  file must carry them or the policy must be applied another way. **Sanctioned fallback, ruled at
  acceptance:** a management-API `PUT /api/policies` run by a step the AppHost owns, with no further ADR.
  The issue decides between the two after observing the pinned image.
- **Behaviour under failure**: that a policy TTL applies to messages already in the queue when it is set,
  that expiry holds across a broker restart, and that a burst of dead letters expires in order (a per-queue
  TTL on a classic queue expires from the head, so FIFO order is assumed, not observed).

## Alternatives Considered

**Wolverine `DeadLetterQueue.ConfigureQueue(q => q.TimeToLive(...))`.** One line, but it fails startup on
every broker that already has the queue ("inequivalent arg"), which is every dev and CI volume, and it
overflows above about 24.8 days. A policy reaches the same TTL without redeclaring anything. Rejected.

**Per-type (or per-context) dead-letter queues**, each with its own TTL, so camera-URL types could expire
sooner than the rest. More mechanism (a dead-letter queue per listener, conventional-routing changes) for a
differentiation the owner did not want: one period is the ruling. Rejected.

**Aging out the outbox.** `DeliverWithin` on the durable CameraCatalog types would bound the exposure, but
by silently dropping an event whose audit must precede disclosure (ADR-0185, ADR-0179). It trades a
credential exposure that stays in the database already holding the address for a lost audit record.
Rejected by Decision 2.

**An archive sweep** in `AuditRetentionHostedService`, deleting blobs older than N with an audited event per
deletion (Azurite has no lifecycle policies, so it would have to be application-side). It turns "the cold
tier is kept" into "the cold tier is deleted" for *all* audit history, to fix a credential subset that is
closed and empty. That is a compliance decision, not this one. Rejected by Decision 3.

## Implementation Notes

**Follow-up issue** (filed by the orchestrator; it could not be `agent:ready` before this ADR was accepted, because the
lane implements decisions and does not make them, ADR-0144; accepted 2026-10-25):
*Age out Wolverine dead letters after 14 days (broker DLQ policy + Postgres dead-letter expiry)*

1. **Broker.** A definitions file with the one policy above; AppHost mounts it and points the broker at it
   (`load_definitions`), or, if boot import displaces the default user, applies it through the management
   API (`PUT /api/policies`) from a step the AppHost owns: the sanctioned fallback, needing no further ADR
   (ruled at acceptance). No change to Wolverine's dead-letter mode.
2. **Postgres.** `WolverineDefaults`: `DeadLetterQueueExpirationEnabled = true`,
   `DeadLetterQueueExpiration = TimeSpan.FromDays(14)`.
3. **Tests, red first** (Aspire fixture, ADR-0103):
   - the dead-letter queue's effective policy carries `message-ttl = 1209600000` (management API
     `effective_policy_definition`);
   - a dead-lettered message disappears after the TTL, using a short TTL on a test-only queue or vhost;
   - a stored Postgres dead letter has a non-null `expires` about 14 days out;
   - the broker policy's TTL and `DeadLetterQueueExpiration` are the same period, so they cannot drift;
   - the dead-letter depth check of step 4 reports a non-empty `wolverine-dead-letter-queue`.
4. **Dead-letter queue depth** (ruled at acceptance: adopted, not runbook-only). A health check or metric
   reports the depth of `wolverine-dead-letter-queue`, so a dead letter is seen inside its 14-day window.
   Until a production telemetry sink exists (ADR-0118), a readiness result is the alert, as for the outbox
   backlog; the issue chooses between check and metric and sets the threshold.
5. **Runbook** (`docs/runbooks/audit-observability.md` and the operator runbook beside
   `OutboxBacklogHealthCheck`): dead letters are kept 14 days; triage and replay within that window; a
   Degraded outbox backlog is drained, never expired; a persistent dev RabbitMQ container must be removed
   (volume kept) to pick up the file; and, for Decision 3, the one-time first-production check that no
   archived chunk predates the redaction step's first applied run.
6. **Out of scope:** outbox retention (Decision 2), cold-tier retention (Decision 3), per-type dead-letter
   queues.
7. **Latency (§IV): N/A.** No leg changes; failure paths only.

**On acceptance:** ADR-0184 and ADR-0185 each gained an *Amended by: ADR-0189* line (added 2026-10-25), as
ADR-0187 did for ADR-0188. The ADR-0184 retention note's "none exists for either today; issue 3092" is then superseded for
dead letters by Decision 1 and for the cold tier by Decision 3.

**Ruled by the owner at acceptance** (PR #3146, 2026-10-25; all three were open in the proposed text):
- **Dead-letter queue depth.** **Adopted**: the follow-up adds a health check or metric for the depth of
  `wolverine-dead-letter-queue`, not a runbook line alone (Consequences; step 4).
- **Boot-import fallback.** Applying the 14-day policy through the RabbitMQ management API from a step
  the AppHost owns is **the sanctioned fallback** and needs no further ADR (*Not verified*; step 1).
- **"Alerted on" before production.** A **Degraded readiness result is enough** for Decision 2 until a
  production telemetry sink exists (ADR-0118) (Consequences).
