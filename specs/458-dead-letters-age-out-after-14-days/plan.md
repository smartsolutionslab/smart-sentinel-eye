# Plan 458: Dead letters age out after 14 days

**Spec:** [spec.md](spec.md) · **Tasks:** [tasks.md](tasks.md) · **Issue:** #3149
**Implements:** ADR-0189 Implementation Notes steps 1-5 (step 6 out of scope, step 7 N/A). **Makes no decision**
that ADR-0189, its three acceptance rulings, ADR-0154 or ADR-0088 has not already made; the choices below are the
ones ADR-0189 explicitly left to the issue (definitions-file vs management-API fallback; check vs metric;
threshold) plus naming/placement.

## Constitution and ADR alignment

| Gate | How this plan meets it |
|---|---|
| ADR-0189 Decision 1 / Mechanism | Broker policy `message-ttl 1209600000` on `^wolverine-dead-letter-queue$`; Postgres expiry 14 d |
| ADR-0189 Decisions 2, 3 | No outbox or archive change; a test-free guarantee is the diff itself (no `DeliverWithin`, no blob code touched) |
| ADR-0189 rulings 1-3 | Depth **health check**; fallback allowed without ADR; Degraded readiness is the alert |
| ADR-0154 | Check never Unhealthy, Healthy on unreachable broker, name `dead-letters` encodes no dependency; detail only in `data` + log |
| ADR-0118 | No metric export, no sink — readiness + structured log only |
| ADR-0088 | Change lives in `WolverineDefaults`, the one place every context's Wolverine is configured |
| ADR-0103 / 0139 | Integration tests on the Aspire fixture; red first |
| ADR-0105 / ADR-0050 | `Ensure.That` guards; `[LoggerMessage]` source-gen for the Degraded log |
| Aspire composition root | Broker config enters through the AppHost, nothing wired by hand |
| §IV latency | N/A |

**Bounded contexts:** none. All production changes are in `src/AppHost` and `src/ServiceDefaults` (cross-cutting
infrastructure). No Domain/Application/Contracts change, so no value-object or §II surface, no integration event,
no cross-context reference.

## Design

### 1. Broker policy (infra-engineer) — `src/AppHost`

- **New file** `src/AppHost/Resources/rabbitmq/definitions.json` — RabbitMQ definitions format, containing only
  `policies` (one entry, spec FR-001) and, only if the primary mechanism needs it, `vhosts: [{"name": "/"}]`.
  It is the single source of the broker period: AppHost C# holds no TTL literal.
- **Decision point (T001, observed, not assumed — ADR-0189 "Not verified" N1).** Boot the pinned
  `4-management-alpine` image with the file mounted and `load_definitions` set, using the AppHost's generated
  password, and try an AMQP connect with it.
  - **Primary — default user survives:** mount the file read-only at `/etc/rabbitmq/definitions.json` and a
    `conf.d` snippet (`src/AppHost/Resources/rabbitmq/90-definitions.conf`: `load_definitions = /etc/rabbitmq/definitions.json`)
    via `WithBindMount`, mirroring the mosquitto/mediamtx mounts at `AppHost.cs:190,274-276`.
  - **Fallback — default user displaced (sanctioned, ruling 2):** no mount. A step the AppHost owns —
    `src/AppHost/RabbitMqDeadLetterPolicy.cs`, an `OnResourceReady`/`ResourceReadyEvent` subscription on the
    `rabbitmq` resource (the AppHost already subscribes to lifecycle events in `StackStatusReport.cs:181`) — reads
    the policy from `definitions.json` and issues `PUT /api/policies/%2F/{name}` against the management endpoint
    with the parameterised credentials. Idempotent by construction (PUT of a fixed value), so it runs on every boot
    and also covers persistent containers without `docker rm`. A non-2xx answer fails the step loudly (logged,
    resource state surfaced), never swallowed.
  - The finding and the branch taken go in the PR body, quoted.
- **Not used:** Wolverine `DeadLetterQueue.ConfigureQueue(q => q.TimeToLive(...))` (startup "inequivalent arg"
  on every existing broker; Int32 overflow) — ADR-0189 Alternatives.
- `WithRabbitMqDeveloperLanePersistence` is unchanged; the persistent-container caveat goes to the runbook.

### 2. Postgres expiry (backend-engineer) — `src/ServiceDefaults/WolverineDefaults.cs`

- Add `public static readonly TimeSpan DeadLetterRetention = TimeSpan.FromDays(14);` on `WolverineDefaults`
  (one named period, referenced by FR-005's test).
- Inside `UseWolverine`: `opts.Durability.DeadLetterQueueExpirationEnabled = true;`
  `opts.Durability.DeadLetterQueueExpiration = DeadLetterRetention;` — before `configureMore`, so a context could
  not silently differ without overriding it in plain sight.
- `AutoCreate.CreateOrUpdate` (already set) adds the `expires` column on next boot; the durability agent purges
  hourly. No migration in `MigrationRunner` (Wolverine owns these tables).

### 3. Dead-letter depth check (backend-engineer) — `src/ServiceDefaults`

**Check, not metric** (the issue's choice): ADR-0189 ruling 3 makes a Degraded readiness result the alert until a
production sink exists, and ADR-0118 gives a metric no production destination. A check is the alert; a metric
today would be an unread number. **Threshold 1**: every dead letter carries a 14-day clock that starts when it
lands, not when someone looks, so the first one is the signal. Contrast `OutboxBacklogHealthCheck.ConcerningBacklog
= 5_000`, where a healthy outbox is briefly non-empty under load; a healthy dead-letter queue is empty.

- **New** `src/ServiceDefaults/DeadLetterDepthHealthCheck.cs` — `sealed class DeadLetterDepthHealthCheck(IWolverineRuntime runtime, ILogger<…> logger) : IHealthCheck`.
  - `public const string QueueName = "wolverine-dead-letter-queue";` `public const long ConcerningDepth = 1;`
  - Reads the depth through **Wolverine's existing RabbitMQ transport** (`RabbitMqQueue.QueuedCountAsync()` on
    the runtime's transport for `QueueName`), reusing the service's connection — no new connection string, client
    or package. *To confirm at implement against 6.51.0* (spec premise table); if the transport API is not
    reachable from the runtime, the fallback is a passive declare on a channel from the same transport. Either way,
    no management-API credentials enter the services.
  - `depth == 0` → Healthy; `depth >= ConcerningDepth` → log (`DeadLetterQueueNotEmpty(queue, depth)`,
    `[LoggerMessage]`, Warning) + `Degraded`; data `{ "queue", "depth" }`.
  - Broker unreachable (connection/`BrokerUnreachableException`/`OperationInterruptedException` shapes, to be
    pinned by T005's tests) → Healthy with `data["error"] = type name`, via one `Unreachable(...)` helper, as
    `OutboxBacklogHealthCheck` does (ADR-0154). Any other exception escapes to the registration's
    `failureStatus: Degraded`.
- **Registration** in `AddWolverineForContext`, beside `outbox-{prefix}`:
  `AddCheck<DeadLetterDepthHealthCheck>("dead-letters", failureStatus: HealthStatus.Degraded, tags: ["ready"])`.
  Every Wolverine host reports the one shared queue — the same "nine lines in nine files is nine chances to
  forget" reasoning that put `outbox-{prefix}` there. Cost: one passive count per probe per service over an
  existing connection.
- `Extensions.cs` MapDefaultEndpoints comment lists the check set ("self", "outbox-{module}"); add
  `"dead-letters"` to that enumeration so the exposure analysis stays current (ADR-0154 requires re-doing it for a
  new check: the name encodes no dependency and the default writer emits one word).

### 4. Runbook (infra-engineer) — `docs/runbooks/audit-observability.md`

New section "Dead letters and the outbox backlog" with spec FR-008's five points, the two management-API commands
from the spec's e2e procedure, and Decision 3's first-production check. (Assumption A2.)

## Boundary rules

No project reference added. AppHost still does not reference ServiceDefaults (the reason recorded at
`AppHost.cs:~98`); the two periods are held together by a **test**, not a shared constant — the same pattern as
`PostgresConnectionBudgetTests`. Architecture.Tests' health-check guards (`HealthCheckFailureStatusGuardTests`,
`ServedHealthHostCompletenessTests`) must pass unmodified with the new registration.

## Phase 4a

**Colour: red** — behaviour-changing (new broker state, new column/expiry, new readiness outcome). Every test
below must be observed failing before its implementation, failure quoted verbatim in the PR.

| # | Test (file) | Red because | Layer |
|---|---|---|---|
| R1 | effective policy on `wolverine-dead-letter-queue` has `message-ttl = 1209600000`; queue `arguments` has no `x-message-ttl` — `tests/Integration.Tests/Messaging/DeadLetterAgeOutPolicyTests.cs` (new) | no policy exists | Aspire |
| R2 | TTL mechanism: the **repo's policy definition read from `definitions.json`**, pattern rewritten to a test-only queue and `message-ttl` shortened to 2 s, applied via management API; one published message is gone after TTL + margin; test cleans up its policy and queue — same file | the file does not exist (assertion "definitions file carries no dead-letter policy", not a compile error) | Aspire |
| R3 | `WolverineDefaults.DeadLetterRetention` equals `definitions.json`'s `message-ttl` in ms — `tests/ServiceDefaults.Tests/DeadLetterRetentionAgreementTests.cs` (new) | neither exists | unit |
| R4 | a host composed with `AddWolverineForContext` has `Durability.DeadLetterQueueExpirationEnabled == true` and `DeadLetterQueueExpiration == 14 d` — `tests/ServiceDefaults.Tests/WolverineDeadLetterExpiryTests.cs` (new) | defaults false / 10 d | unit |
| R5 | a host composed with `AddWolverineForContext` against the fixture's Postgres moves an envelope to dead-letter storage; its `wolverine_dead_letters.expires` is non-null and within 14 d ± 1 min — `tests/Integration.Tests/Messaging/PostgresDeadLetterExpiryTests.cs` (new) | no `expires` column / null | Aspire Postgres |
| R6 | `DeadLetterDepthHealthCheck`: 0 → Healthy; 1 → Degraded with `depth` data and one log entry; unreachable → Healthy with `error` data; never Unhealthy — `tests/ServiceDefaults.Tests/DeadLetterDepthHealthCheckTests.cs` (new; depth source faked behind the smallest seam the engineer introduces, Moq or hand-written fake per ADR-0052) | type does not exist → **the test-writer stubs nothing in `src/`**; red is the build failure *plus* R7 | unit |
| R7 | live: publish one message to `wolverine-dead-letter-queue` via management API → a context's `/health` reads `Degraded`; purge → `Healthy` — `tests/Integration.Tests/Messaging/DeadLetterDepthReadinessTests.cs` (new) | `/health` stays Healthy | Aspire |
| R8 | every `ServedHealthHosts` host registers `dead-letters` with `failureStatus: Degraded`, tag `ready`, and no check name contains `rabbit`/`amqp`/`queue` — `tests/Architecture.Tests/HealthChecks/DeadLetterCheckRegistrationTests.cs` (new) | not registered | arch |

R6's build-failure red is acceptable only together with R7's observed assertion failure (ADR-0139: the
*behaviour* must be seen failing). **Characterisation (green before and after, unmodified):**
`OutboxBacklogHealthCheckTests`, `OutboxBacklogAssessmentCharacterisationTests`, `HealthCheckFailureStatusGuardTests`,
`ServedHealthHostCompletenessTests`, `AuditObservabilityHealthCheckRegistrationTests`,
`AppHostCompositionCharacterisationTests`, `AppHostStackPersistenceTests`. A characterisation assertion that must
change is a block, not an edit.

New Integration.Tests classes need an entry in `tests/Integration.Tests/ci-shards/shard-N.filter`, or CI fails
deterministically.

R7 and R1 leave the shared broker clean (purge / delete) so a later test's `/health` is not Degraded by them.
Other integration tests that dead-letter as a side effect would now turn a service's `/health` Degraded — still
`200`, so no existing assertion on status code changes; any test asserting the body word `Healthy` after a
dead-letter-producing step is a finding to report, not to edit.

## Risks

- **Default-user displacement** (N1) — handled by the sanctioned fallback; decided by observation.
- **`QueuedCountAsync` signature** in 6.51.0 — confirmed at implement; fallback is passive declare on the same
  transport. Neither needs an ADR.
- **Every service Degraded for one dead letter** — intended: the queue is shared, and any host's readiness is the
  alert (ruling 3). Status code stays 200.
- **Probe load** — one AMQP passive count per probe per service, on an existing connection.

## Engineers

- **infra-engineer:** `src/AppHost/Resources/rabbitmq/*`, `src/AppHost/AppHost.cs` (mount) or
  `src/AppHost/RabbitMqDeadLetterPolicy.cs` (fallback), `docs/runbooks/audit-observability.md`.
- **backend-engineer:** `src/ServiceDefaults/WolverineDefaults.cs`, `src/ServiceDefaults/DeadLetterDepthHealthCheck.cs`,
  its `[LoggerMessage]` partial (existing log-message file convention in ServiceDefaults), `src/ServiceDefaults/Extensions.cs`
  (comment only).
- Disjoint files → parallel after the tests are red.
