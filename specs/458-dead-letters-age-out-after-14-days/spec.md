# Spec 458: Dead letters age out after 14 days

**Issue:** #3149 (feature-level, on Project #13; refs #3092, which stays open until this lands) ·
**Branch:** `fix/3149-dead-letter-age-out`
**ADRs:** **ADR-0189** (accepted 2026-10-25 on PR #3146, with three owner rulings; this spec implements its
Implementation Notes and nothing else), ADR-0184 (dead letters can hold full camera URLs; amended by 0189),
ADR-0185 (outbox durability; amended by 0189), ADR-0118 (one telemetry sink per environment; no production sink),
ADR-0154 (readiness is liveness: a `/health` check never fails on, nor names, a shared dependency), ADR-0088
(Wolverine defaults), ADR-0103 (Aspire fixture, no Testcontainers), ADR-0139 (red first), ADR-0144 (lane).
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** behaviour-changing → **red** (plan §"Phase 4a").
**Security-sensitive:** yes, mildly — the point is bounding how long a credential-bearing payload rests
(ADR-0179/0184). No endpoint, scope or token changes.
**Written 2026-10-25 against `develop` at `ec9e656b`.**

---

## Premise, checked

ADR-0189's Context table was re-checked against the code on `ec9e656b`:

| Claim | Evidence | Holds |
|---|---|---|
| No broker configuration is repo-owned | `AppHost.cs:123-128` — `AddRabbitMQ(... password)`, `WithImageTag("4-management-alpine")`, `WithManagementPlugin()`; `DeveloperLanePersistence.WithRabbitMqDeveloperLanePersistence` adds only a data volume + persistent lifetime | yes |
| Dead-letter mode is Wolverine's default (native RabbitMQ DLQ) | `WolverineDefaults.AddWolverineForContext` never sets a dead-letter mode | yes |
| `DeadLetterQueueExpirationEnabled` set nowhere | `grep -rn DeadLetterQueueExpiration src` → none | yes |
| Wolverine 6.51.0 has the expiry settings and a queue count API | `Wolverine.dll` carries `DeadLetterQueueExpiration[Enabled]`; `Wolverine.RabbitMQ.dll` carries `QueuedCountAsync` / `QueueDeclarePassiveAsync` | yes (API shape to be confirmed at implement) |
| Outbox backlog is already a Degraded readiness result | `OutboxBacklogHealthCheck<TDbContext>`, registered in `WolverineDefaults` as `outbox-{prefix}`, `failureStatus: Degraded`, tag `ready` | yes |
| A guard fails any check registered with the framework-default failure status | `tests/Architecture.Tests/HealthChecks/HealthCheckFailureStatusGuardTests.cs` | yes — the new check must register `Degraded` |
| Tests can read the broker's management API with the AppHost's credentials | `tests/Integration.Tests/AuditObservability/AuditQueueProbe.ClientAsync` | yes — reuse |
| An "operator runbook beside `OutboxBacklogHealthCheck`" exists | none under `docs/`; only `docs/runbooks/audit-observability.md` | **no** — see Assumption A2 |

## User stories

### US1 (P1) — A dead letter is gone 14 days after it was dead-lettered, without an operator

As the operator of a 24/7 fab, I want every message in `wolverine-dead-letter-queue` to expire 14 days after it
landed there, and the Postgres dead-letter tables to carry the same period, so a failed delivery holding a full
camera URL does not rest indefinitely (ADR-0189 Decision 1).

### US2 (P1, same slice) — A dead letter is seen inside its 14-day window

As the operator, I want readiness to report Degraded while `wolverine-dead-letter-queue` holds any message, so
the 14-day triage window is noticed (ADR-0189 owner ruling 1 + step 4; owner ruling 3: Degraded readiness is the
alert until a production sink exists, ADR-0118).

Both stories ship together: ADR-0189 step 3 lists their tests as one set and Consequences makes the window
pointless without the signal. Each is still independently observable (procedure below).

## Functional requirements

- **FR-001** A repo-owned RabbitMQ definitions file carries exactly one policy: name
  `wolverine-dead-letter-age-out` (*marked guess*: ADR-0189 fixes pattern/apply-to/TTL, not the name), vhost `/`,
  pattern `^wolverine-dead-letter-queue$`, `apply-to: queues`, definition `{"message-ttl": 1209600000}`,
  priority 0. No DLX (expired messages are discarded).
- **FR-002** On every AppHost boot the broker ends up with that policy in effect on `wolverine-dead-letter-queue`,
  observable as the queue's `effective_policy_definition`. Mechanism: `load_definitions` of the mounted file, **or**
  — if boot import displaces the AppHost's default user on the pinned image — the sanctioned fallback, a
  management-API `PUT /api/policies/%2F/<name>` from a step the AppHost owns, whose body is read from the same
  file (ADR-0189 step 1 + owner ruling 2). The choice is made by observing the pinned image (plan §"Decision
  point") and recorded in the PR.
- **FR-003** Wolverine's dead-letter mode is unchanged; Wolverine's `TimeToLive` is not used (ADR-0189
  Alternatives).
- **FR-004** `WolverineDefaults` sets `Durability.DeadLetterQueueExpirationEnabled = true` and
  `Durability.DeadLetterQueueExpiration` to a single named 14-day period, for every context.
- **FR-005** The broker policy's `message-ttl` and the `WolverineDefaults` period are equal; a test fails if
  either moves alone (ADR-0189 Consequences, "a test asserts the two agree").
- **FR-006** A readiness check named `dead-letters` (tag `ready`, `failureStatus: Degraded`) reports the depth of
  `wolverine-dead-letter-queue`: **Healthy at 0, Degraded at ≥ 1** (threshold: 1 — plan §"Check, not metric").
  Never Unhealthy. Broker unreachable → Healthy with the error type in `data` (ADR-0154 clause 2, mirroring
  `OutboxBacklogHealthCheck.Unreachable`). The queue name and depth live only in `data` and the log, never in the
  check name (ADR-0154 exposure argument). Degraded is logged via `[LoggerMessage]`.
- **FR-007** The check is registered for every service that calls `AddWolverineForContext` (same placement and
  reasoning as `outbox-{prefix}`).
- **FR-008** Runbook (`docs/runbooks/audit-observability.md`): dead letters kept 14 days; triage and replay within
  that window; a Degraded outbox backlog is drained, never expired; what `dead-letters` Degraded means and how to
  read the queue; a persistent dev RabbitMQ container must be `docker rm`-ed (volume kept) to pick up a mount
  change; Decision 3's one-time first-production check that no archived chunk predates the redaction step's first
  applied run.
- **FR-009** The outbox and the audit archive are not touched: no `DeliverWithin`/`DeliverBy`, no blob lifecycle
  (ADR-0189 Decisions 2, 3; step 6).

## Acceptance scenarios (Gherkin)

```gherkin
Feature: Dead letters age out after 14 days

  Scenario: Happy — the broker policy is in effect after boot
    Given the AppHost has booted the stack
    When the management API is asked for queue "wolverine-dead-letter-queue" on vhost "/"
    Then its effective_policy_definition has "message-ttl" = 1209600000
    And the queue's own arguments still contain no "x-message-ttl"

  Scenario: Happy — a dead letter expires after the policy TTL
    Given a test-only queue matched by the repo's policy definition with message-ttl shortened to 2000 ms
    And one message published to it
    When 2000 ms plus margin elapse
    Then the queue holds 0 messages

  Scenario: Happy — a Postgres dead letter carries an expiry 14 days out
    Given a host composed by AddWolverineForContext against the fixture's Postgres
    When an envelope is moved to dead-letter storage
    Then its wolverine_dead_letters row has a non-null "expires" within 14 days ± 1 minute of now

  Scenario: Conflict — the two periods cannot drift
    Given the definitions file's message-ttl and WolverineDefaults' dead-letter period
    When either is changed without the other
    Then the period-agreement test fails

  Scenario: Conflict — an existing broker is not redeclared
    Given a persistent broker whose "wolverine-dead-letter-queue" already exists without x-message-ttl
    When the AppHost and every service start
    Then no service fails startup with an "inequivalent arg" error
    And the policy is in effect on the existing queue

  Scenario: Happy — a dead letter makes readiness Degraded
    Given "wolverine-dead-letter-queue" holds 1 message
    When a service's "dead-letters" check runs
    Then it reports Degraded with data depth = 1
    And GET /health answers 200 with body "Degraded"

  Scenario: Happy — an empty dead-letter queue is Healthy
    Given "wolverine-dead-letter-queue" holds 0 messages
    When the "dead-letters" check runs
    Then it reports Healthy

  Scenario: Bad request — the broker cannot be read
    Given the broker is unreachable
    When the "dead-letters" check runs
    Then it reports Healthy with the error type in data, and never Unhealthy (ADR-0154)

  Scenario: Auth — /health discloses nothing about the dependency
    Given an anonymous caller
    When it GETs /health on any context service
    Then the body is a single word, and no registered check's name contains "rabbit", "amqp" or "queue"
```

## Independent end-to-end test procedure

1. Boot the AppHost (non-persistent lane, or `docker rm` the persistent `rabbitmq` container first, volume kept).
2. `GET http://<management>/api/queues/%2F/wolverine-dead-letter-queue` with the AppHost's RabbitMQ credentials →
   `effective_policy_definition.message-ttl == 1209600000`, `arguments` has no `x-message-ttl`.
3. `GET /api/policies/%2F` → exactly the one repo policy.
4. Any context's `GET /health` → `Healthy`.
5. Publish one message to `wolverine-dead-letter-queue` via `POST /api/exchanges/%2F/amq.default/publish`
   (routing key = queue name). Within one probe, `GET /health` → `Degraded`; the service log carries the
   dead-letter-depth entry. Purge the queue → `Healthy` again.
6. `\d wolverine_<context>.wolverine_dead_letters` in any context DB → an `expires` column exists.

## Locked tech choices

RabbitMQ `4-management-alpine` via `Aspire.Hosting.RabbitMQ` 13.6.3 (unchanged); WolverineFx 6.51.0; Postgres
message store (`AutoCreate.CreateOrUpdate`); `Microsoft.Extensions.Diagnostics.HealthChecks`; tests xUnit +
Shouldly, integration via `AspireFixture` (ADR-0103). No new package.

## Latency budget

**N/A** (ADR-0189 step 7; constitution §IV). Broker and persistence housekeeping on failure paths; the readiness
probe is off the event-to-overlay path.

## Not verified by ADR-0189 — established in this slice before being claimed

ADR-0189 lists four. Each is a task (tasks.md T001, T013), recorded in the PR body as observed:

- **N1** whether the pinned image's boot-time definitions import displaces the default user → decides FR-002's
  mechanism;
- **N2** whether 1,209,600,000 ms is accepted as a policy value by the pinned broker → observed by the
  effective-policy test;
- **N3** behaviour under failure: policy TTL applies to messages already queued; holds across a broker restart;
  FIFO expiry → observed manually once and written down; not a gate for this slice (*marked*: ADR-0189 asks for
  them to be established "before claiming", so the PR claims only what was observed);
- **N4** whether a stored dead letter's exception text can echo a camera URL → a read-only review of the
  `CameraRegisteredV1`/`CameraAddressChangedV1` handlers' exception messages; a positive finding is **filed as an
  issue, not fixed here** (outside ADR-0189's Implementation Notes).

## Assumptions (marked)

- **A1** Policy name `wolverine-dead-letter-age-out` and priority 0 (not fixed by ADR-0189).
- **A2** ADR-0189 step 5's "operator runbook beside `OutboxBacklogHealthCheck`" does not exist as a document; the
  runbook text goes into `docs/runbooks/audit-observability.md` under a new "Dead letters and the outbox backlog"
  section, and `DeadLetterDepthHealthCheck`'s doc comment points at it. No new runbook file.
- **A3** The ScenarioSimulator's own `UseWolverine` (not `AddWolverineForContext`) is out of scope for FR-004/FR-007:
  it is a dev-only host, its listeners share the broker queue (so the broker policy covers its dead letters), and it
  does not serve `/health` through `ServedHealthHosts`. Recorded, not silently skipped.

## Out of scope

Outbox retention (Decision 2); cold-tier retention or any blob sweep (Decision 3); per-type dead-letter queues;
replay tooling; a production telemetry sink or metric export (ADR-0118); a k3s chart for RabbitMQ.
