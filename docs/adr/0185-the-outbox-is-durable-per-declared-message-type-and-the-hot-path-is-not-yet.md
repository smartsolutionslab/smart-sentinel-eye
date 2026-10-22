# ADR-0185: The outbox is durable per declared message type, and the hot path is not yet

**Status:** Proposed (records the owner's ruling of 2026-10-22 on #3102; awaiting owner approval of this text and answers to the open questions below)
**Date:** 2026-10-22
**Amends:** [ADR-0088](./0088-wolverine-defaults.md) (its spec 021 amendment and two of its Consequences, by the text in *Implementation Notes*)
**Relates to:** ADR-0049, ADR-0073, ADR-0125, ADR-0126, ADR-0144, ADR-0154, ADR-0179, spec 021, issues 1605, 3016, 3102, 3103

## Context

ADR-0088 and spec 021 describe `OutboxEventBus` / `OutboxTransactionalCommit` as a transactional outbox:
the write and its announcement share a fate, and the announcement survives a crash. #3102 found, from
WolverineFx, WolverineFx.EntityFrameworkCore and WolverineFx.RabbitMQ **6.51.0** decompiled against this
repository's configuration (`develop` at `b5579a60`), that the second half is not true for any RabbitMQ
publish, in a handler or outside one:

- **Every RabbitMQ endpoint is Inline.** `RabbitMqEndpoint`'s constructor sets `Mode = Inline`
  (`RabbitMqEndpoint.cs:51` decompiled), and `InlineSendingAgent.IsDurable` is `false`.
  `WolverineDefaults.AddWolverineForContext` never asks for anything else on the sending side: the only
  durability opt-in is `UseDurableInbox()` on listeners (`WolverineDefaults.cs:162-165`).
- **Nothing is persisted for a non-durable sender.** `MessageBus.PersistOrSendAsync` calls
  `envelope.PersistAsync(Transaction)`, which writes an outgoing row only `if (Sender.IsDurable)`. So
  `IDbContextOutbox.PublishAsync` holds the envelope in memory, and `SaveChangesAndFlushMessagesAsync`
  (`DbContextOutbox.cs:76-93`) commits the SQL and *then* sends. A crash between the two loses the
  message. The same holds for the ambient context inside a handler.
- **A failed send is swallowed.** `EfCoreOutboxFlushing.FlushOutgoingMessagesAsync` reaches
  `MessageContext.flushEnvelopeAsync`, whose `catch (Exception)` logs *"Unable to send outgoing message
  {Envelope}, so it has been discarded"* (`MessageContext.cs:413-417`) and returns. `CommitAsync` does not
  throw, so the HTTP request succeeds and the announcement is gone. This happens in all nine contexts.
- **No schema change is needed to fix it.** `PersistMessagesWithPostgresql` (`WolverineDefaults.cs:87`)
  already creates `<schema>.wolverine_outgoing_envelopes` in every context, and
  `OutboxBacklogHealthCheck` already counts it. Today it counts rows that can only ever be zero.

Three records claim more than the code does. ADR-0088's spec 021 amendment says the outbox "prevents
message loss" for repository writes. Its Consequences say *"a batch of 200 events writes 200 event rows and
200 outbox rows together. Measured rather than assumed (spec 021 SC-005)"*. No outgoing row is written,
so whatever SC-005 measured, it was not an outbox write. Two integration tests can also never fail:
`OutboxCoversEveryContextTests` and `OutboxSurvivesAKillTests` both assert that the outgoing table drains
to zero, and it is zero before, during and after.

The pressure is #3016 slice A. ADR-0179 decision 5 audits every full read of a camera address with
`CameraAddressReadV1`, published and committed **before** the address is returned
(`GetCameraAddressQueryHandler.cs:44-58` on branch `feat/3016-camera-address-write-read`, unmerged). With
an Inline sender a broker outage turns that into *200 with the credential disclosed and no audit record*,
and nothing reports it except one log line.

**Why "everything durable" was not taken.** `opts.Policies.UseDurableOutboxOnAllSendingEndpoints()` would
put an outgoing-row INSERT and DELETE on every hop of the event-to-overlay path (constitution §IV,
*"Event → overlay state (RabbitMQ + projection) ≤ 200 ms"*). It would also change each send from an inline
send inside the commit to a background-agent send. That path is the MQTT ingest loop
(`PersistenceLoopHostedService`, sized for 5,000 events/s, through `OutboxEventBus` → `IDbContextOutbox`
→ `EventRepository.CommitAsync`), then `FabEventIngestedV1`, then the Automation, SystemVariables and
LayoutComposition relays. The policy also makes local queues durable. Trading latency for loss on that
path is a decision of its own (#3103), in the way ADR-0126 recorded the inverse trade.

## Decision

**The owner's ruling (2026-10-22), recorded:** *make the outbox durable per message type for the
publishes that need audit-before-disclosure; leave the event-to-overlay hot path as it is for now, with its
loss window recorded as a known risk under its own decision (#3103).*

**1. Durability is a property of a message type, declared by the context that publishes it.**
`AddWolverineForContext` gains a parameter that mirrors `durableInboxMessageTypes`:

```csharp
IReadOnlyCollection<Type>? durableOutboxMessageTypes = null,
```

It is applied inside the existing `UseConventionalRouting` block, in **one** `ConfigureSending` call:

```csharp
routing.ConfigureSending((exchange, context) =>
{
    if (durableOutboxMessageTypes?.Contains(context.MessageType) == true)
    {
        exchange.UseDurableOutbox();
    }
});
```

A type on the list gets a `DurableSendingAgent`. Its envelope is written to
`<schema>.wolverine_outgoing_envelopes` **inside the caller's transaction**, sent by the background agent,
deleted on acknowledgement, and recovered by the durability agent after a crash. Every type not on the list
stays Inline, exactly as today.

**Verified in 6.51.0 at the decompiled-source level, not yet observed at runtime** (the first test in
*Implementation Notes* is what observes it):

- `MessageRoutingConvention.ConfigureSending(Action<TSubscriber, MessageRoutingContext>)` exists
  (`MessageRoutingConvention.cs:480`). For RabbitMQ, `TSubscriber` is `RabbitMqExchangeConfiguration`
  (`RabbitMqMessageRoutingConvention.cs:14`), a `SubscriberConfiguration` whose `UseDurableOutbox()`
  (`SubscriberConfiguration.cs:30`) calls `RequestSubscriberMode(EndpointMode.Durable)`. RabbitMQ's
  exchange does not override `supportsMode`, so Durable is accepted.
- **It is per type, not per shared endpoint.** The sender identifier defaults to the message type name
  (`_identifierForSender`), and this repository does not override it. Each published type therefore has its
  own exchange endpoint, and `ConfigureSending` runs once per message type (`tryRegisterSenderConfiguration`,
  `MessageRoutingConvention.cs:300-357`), before the sending agent is built. That covers both the eager
  path (`PreregisterSenders`) and the lazy one (`DiscoverSenders` on first publish).
- **`ConfigureSending` replaces, it does not compose** (`_configureSending = configure`, `:482`). This is
  the same trap `WolverineDefaults.cs:113-118` documents for `ConfigureListeners`. All sending
  configuration must live in that one call.
- **The row is written at publish time, in the caller's transaction.** With a DbContext that does not map
  Wolverine's envelope storage (none in `src/` does), `EfCoreEnvelopeTransaction.PersistOutgoingAsync`
  begins a transaction on the DbContext if none is open and INSERTs the row on it
  (`EfCoreEnvelopeTransaction.cs:52-72`). `SaveChangesAndFlushMessagesAsync` then commits that transaction
  before flushing. For a publish with no accompanying write (`CameraAddressReadV1`), the transaction holds
  only the envelope row, and the commit makes it durable before the handler returns the address.
- **Precondition, verified:** this depends on the DbContext having no retrying execution strategy, which
  rejects user-initiated transactions. `PostgresTelemetry.cs:98` sets `DisableRetry = true` for every
  context. Turning retries on later would break every durable publish at runtime. The test below covers
  that.

**2. Recommended over the alternatives (see *Alternatives Considered*): a per-module list, in the
publishing context's Infrastructure module, beside its `AddWolverineForContext` call.** This is the
existing pattern (`IdentityInfrastructureModule.cs:74`). The decision stays with the context that owns the
publish. `Shared.Contracts` keeps its rule of carrying wire shapes and not delivery policy (ADR-0073). Only
the type's own publisher can make its send durable anyway.

**3. Which types.** *Needs durable* under the ruling as written:

| Type | Published at | Caller |
|---|---|---|
| `CameraAddressReadV1` | `src/CameraCatalog/Application/Queries/Handlers/GetCameraAddressQueryHandler.cs:47` (branch `feat/3016-camera-address-write-read`, not on `develop`) | HTTP `GET` (ADR-0179), publish + explicit `CommitAsync`, no write |

This is the only type the ruling's criterion names without judgement. The enumeration below is from every
`IEventBus.PublishAsync` in `src/` at `b5579a60` (26 call sites; nothing in `src/` publishes through
`IMessageBus` or a cascading return). It classifies the rest. **Unsure** means the type is off the hot
path but outside the literal criterion. Those are the owner's call, not this ADR's, and they are Q1-Q3.

*Unsure: the response discloses a credential the request created (Q2):*

| Type | Published at | Note |
|---|---|---|
| `WebhookIntegrationRotatedV1` | `Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:100` | Response carries the new client secret. Loss also leaves EventIngestion on hash-compare (`WebhookIntegrationRotatedV1Handler`) |
| `DeviceRegisteredV1` | `Identity/Application/EventHandlers/ClientRegisteredDomainEventHandler.cs:41` | Device registration; response is `DeviceCredentialsDto` |
| `KioskEnrolledV1` | `Identity/Application/EventHandlers/ClientRegisteredDomainEventHandler.cs:57` | Kiosk enrolment; response is `KioskCredentialsDto` |

*Unsure: loss has a security effect, and the consumer's inbox is already durable (Q1):*

| Type | Published at | Note |
|---|---|---|
| `WebhookIntegrationRevokedV1` | `EventIngestion/Application/EventHandlers/WebhookIntegrationRevokedDomainEventHandler.cs:29` | HTTP revoke. Identity's handler disables the Keycloak client, so loss leaves a revoked integration's client enabled. Durable on receive (`IdentityInfrastructureModule.cs:74`), lossy on send |
| `WebhookClientRaceDisableFailed` | `Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:506` | Internal retry message; same asymmetry |

*Unsure: admin state changes over HTTP (Q1):*

| Type | Published at | Loss means |
|---|---|---|
| `CameraRegisteredV1` | `CameraCatalog/Application/EventHandlers/CameraRegisteredDomainEventHandler.cs:19` | No stream provisioned (StreamDistribution) |
| `CameraRenamedV1` | `CameraCatalog/Application/EventHandlers/CameraRenamedDomainEventHandler.cs:26` | Audit gap |
| `CameraAddressChangedV1` | `CameraCatalog/Application/EventHandlers/CameraAddressChangedDomainEventHandler.cs:27` | Stream keeps the old address |
| `CameraRetiredV1` | `CameraCatalog/Application/EventHandlers/CameraRetiredDomainEventHandler.cs:26` | Stream outlives its camera |
| `SystemVariableDefinedV1` | `SystemVariables/Application/EventHandlers/VariableDefinedDomainEventHandler.cs:36` | Audit gap |
| `SystemVariableArchivedV1` | `SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs:37` | Audit gap |
| `OverlayRevisionPublishedV3` | `OverlayDesigner/Application/EventHandlers/OverlayRevisionPublishedDomainEventHandler.cs:31` | Reverse index and wall stay on the old revision |
| `OverlayRevisionArchivedV1` | `OverlayDesigner/Application/EventHandlers/OverlayRevisionArchivedDomainEventHandler.cs:25` | Same, for archive |
| `LayoutRevisionPublishedV2` | `LayoutComposition/Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs:38` | Audit gap |
| `LayoutRevisionArchivedV1` | `LayoutComposition/Application/EventHandlers/LayoutRevisionArchivedDomainEventHandler.cs:27` | Audit gap |
| `WallConfiguredV1` | `LayoutComposition/Application/EventHandlers/WallConfiguredDomainEventHandler.cs:25` | Audit gap |

*Unsure: background publishers (Q3):*

| Type | Published at | Note |
|---|---|---|
| `StreamHealthChangedV1` | `StreamDistribution/Application/EventHandlers/StreamHealthChangedDomainEventHandler.cs:60` | From `StreamHealthWatcher`; only AuditObservability consumes it |
| `AuditChunkArchivedV1` | `AuditObservability/Application/Retention/AuditRetentionHostedService.cs:146` | The example `IEventBus`'s remark uses to claim *"the send survives a crash"* |

*Hot path: stays Inline for now (#3103):*

| Type | Published at | Why it stays |
|---|---|---|
| `FabEventIngestedV1` | `EventIngestion/Application/EventHandlers/EventIngestedDomainEventHandler.cs:48` | MQTT loop (`PersistenceLoopHostedService.cs:368`, `IngestEventBatchCommand`). Also HTTP `/events/manual` and webhook delivery via `IngestEventCommand` (`EventIngestStore.cs:63`). Same type, so it cannot be split |
| `SystemVariableValueRequestedV1`, `OverlayHighlightRequestedV1`, `WallSceneSwitchRequestedV1` | `Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:163, 179, 195` | Rule fan-out |
| `SystemVariableValueChangedV1`, `ResolvedOverlayTextChangedV3` | `SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:40, 82`; also `VariableArchivedDomainEventHandler.cs:68` | **Mixed callers**: the hot path via `SystemVariableValueRequestedV1Handler`, and HTTP via `SystemVariableEndpoints.Writes.cs:158` |
| `WallSceneChangedV1` | `LayoutComposition/Application/EventHandlers/WallSceneSwitchedDomainEventHandler.cs:41` | **Mixed callers**: the hot path via `WallSceneSwitchRequestedV1Handler`, and HTTP via `WallEndpoints.Commands.cs:188` |

**Durability is per type, never per caller.** A mixed-caller type cannot be durable for its HTTP publishes
and Inline for its hot-path ones, so all three mixed types stay with the hot path. Their admin-originated
publishes inherit the loss window until #3103 decides.

**4. A durable type carries no secret in its body.** The envelope body is stored in plaintext in
`wolverine_outgoing_envelopes` until delivered, and in `wolverine_dead_letters` if it never is. Adding a
type to the list is therefore a security-relevant change. `CameraAddressReadV1` qualifies: ADR-0179
decision 5 keeps the URL out of it.

**5. Latency (§IV): N/A for every type this ADR makes durable.** `CameraAddressReadV1` is consumed only by
AuditObservability's `IntegrationEventAuditHandler`. No handler on the *event → overlay state* leg
receives it, and its publisher is a `GET` on the operator console, not the ingest path. Every hot-path
type stays Inline, so the leg's code path is unchanged. The lane shows that by running the leg's existing
latency tests unmodified. **If the owner adds any type from the hot-path table, this line no longer holds
and that type moves to #3103.**

**6. Security-sensitive.** The audit guarantee of ADR-0179 depends on this. Phase 6 runs
`security-reviewer` on every slice under this ADR.

## Consequences

**Positive: audit-before-disclosure actually holds for `CameraAddressReadV1`.** When `CommitAsync`
returns, the audit envelope is a committed row. A broker outage delays the audit record and cannot drop it,
and a crash after the commit is recovered by the durability agent. The AuditObservability row stays
eventually consistent: *durable before disclosure*, not *recorded before disclosure*.

**Positive: no schema, migration or new infrastructure.** The table, its per-context schema, the
durability agent and the backlog health check already exist (`OutboxBacklogHealthCheck`, Degraded at
`ConcerningBacklog = 5_000` or `ConcerningAttempts = 5`, per ADR-0154). For durable types the check starts
reporting something real.

**Positive: revert is configuration.** Removing a type from the list returns it to Inline. No data
migration.

**Negative: one INSERT and one DELETE per durable message, in the publisher's own database.** The
INSERT happens at `PublishAsync` time, not at commit. It opens the DbContext's transaction early, so the
connection is held from publish to commit. For `CameraAddressReadV1` that is one round-trip per address
read on an operator-initiated `GET`. The DELETE happens on acknowledgement, off the request path. Both go
through the message store's own pool (ADR-0125). The INSERT goes through the DbContext's.

**Negative: `PublishAsync` is now I/O for durable types.** `OutboxEventBus`'s remark that capturing "is
not an operation there is anything to cancel" (`OutboxEventBus.cs:46-52`) becomes false for them.
Wolverine's `PublishAsync` still takes no token, so the token remains unobserved during the write.
ADR-0049's intent is met by the existing pre-check. The doc must say so.

**Negative: "publish before you commit" becomes load-bearing for the row as well.** A durable publish
with no `CommitAsync` after it opens a transaction that is rolled back when the scope disposes, so the row
is lost and nothing reports it. That is the same outcome as today's unflushed in-memory envelope, now
holding a connection until dispose. The `IEventBus` rule already forbids it. It now costs more to break.
Publish-after-save-then-commit (`RotateWebhookClientCommandHandler.cs:100-115`) still works, because the
publish opens a fresh transaction that the commit then commits.

**Negative: local-queue interplay is unchanged, and the guarantee covers the broker copy only.**
`ConventionalLocalRoutingIsAdditive` (`WolverineDefaults.cs:104`) routes a type with a local handler to
both its exchange and the in-process local queue. Local queues stay `BufferedInMemory`
(`Endpoint._mode` defaults to `BufferedInMemory`, `Endpoint.cs:110` decompiled), because this ADR touches only the exchange endpoint, unlike
`UseDurableOutboxOnAllSendingEndpoints`. `CameraAddressReadV1` has no local handler in CameraCatalog, so
this is moot today. A future durable type with a same-context relay gets a durable broker copy and a
lossy local one, and must say so.

**Negative: what stays loss-capable, and why.** Every type in the hot-path table, and every *unsure*
type the owner does not add, keeps today's two loss windows: a crash between SQL commit and send, and a
broker outage at send time, logged as *"discarded"* and swallowed. On the hot path this is a known risk
that conflicts with `WolverineDefaults.cs:101-102` (*"AuditObservability, which every \*V1 is required to
reach"*). The source of `FabEventIngestedV1` is not redelivered after the MQTT ack. It stays a risk
because making it durable spends §IV budget that #3103 has not yet priced. The leg's own baseline is
contested (§IV records 57.6–75 ms CI / 82–234 ms dev for this leg plus render; #2072 cites 758 ms).
Recorded so nobody reads this ADR as closing #3102's whole finding.

**Negative: two vacuous tests remain vacuous.** `OutboxSurvivesAKillTests` (`FabEventIngestedV1`, stays
Inline) and `OutboxCoversEveryContextTests` (`CameraRegisteredV1`, unsure) assert a drain to zero on a
table their message types never write. Until their types are durable they prove nothing. Q5 asks what to
do with them.

**Rollout.** Deploying the change loses nothing new. A process stopped mid-deploy loses only an Inline
send in flight, as today. **Rollback** to Inline while durable rows are still pending is not verified:
whether the durability agent still delivers a row whose destination's sender is no longer durable was not
established from the decompiled source. Until a test shows it, the procedure is to drain first: the
schema's `outbox-<prefix>` health entry reports a pending count of 0, then deploy the revert.

## Alternatives Considered

**Every sending endpoint durable (`UseDurableOutboxOnAllSendingEndpoints`).** This was #3102's original
proposal. It puts a DB write on every hop of the event-to-overlay path and makes local queues durable.
That is the trade #3103 holds open. Rejected by the owner for now.

**A marker interface or attribute on the contract** (`IDurablyPublished`, or `[DurableOutbox]` in
`Shared.Contracts`, read once in `WolverineDefaults`). Central, and impossible to forget in a module. But
it puts delivery policy on a wire contract that every consumer references (ADR-0073 keeps contracts to
shape). It also cannot express "durable in this deployment, not that one". And for a mixed-caller type it
reads as if it were possible. Not recommended. **Equally verifiable in 6.51.0** (the same
`ConfigureSending` call, with an `IsAssignableTo` test in place of the list lookup), so this is a taste
decision for the owner (Q4), not a technical one.

**One central list in `WolverineDefaults`.** One place to read, but ServiceDefaults would name every
context's contract types and own a decision each context should own. Not recommended.

**Per-endpoint configuration outside the convention** (`opts.PublishMessage<T>().ToRabbitExchange(...)
.UseDurableOutbox()`). This creates a second route next to the conventional one. A type would then be
published twice unless the convention excludes it, which is a new way to break routing silently. Rejected.

**Leave it, and fix the documentation only.** That would make the records honest and leave ADR-0179's
audit guarantee false. Rejected by the ruling.

## Implementation Notes

**Before the lane starts (human):** this ADR accepted, and Q1-Q5 answered. Until Q1-Q3 are answered,
the list contains only what the ruling names.

**Sequencing with #3016 slice A.** The type does not exist on `develop`. The **mechanism** lands first
(this ADR's slice, under #3102), proved with test-only message types. #3016 slice A then adds
`typeof(CameraAddressReadV1)` to CameraCatalog's `durableOutboxMessageTypes` **in the same PR that
introduces the type**. Its integration test asserts the outgoing row for that type. That respects the
original ruling that durability precedes slice A's merge. Q6 confirms it.

**What the lane builds (#3102, one slice):**

1. `WolverineDefaults.AddWolverineForContext`: the `durableOutboxMessageTypes` parameter and the single
   `ConfigureSending` call above, with a comment carrying the *replaces, not composes* warning and the
   `DisableRetry` precondition. No module passes a list in this slice unless Q1-Q3 add types.
2. **Phase 4a colour: red** for the durability behaviour (new behaviour), and **characterisation** for
   everything else. The existing `ServiceDefaults.Tests` and `Integration.Tests` outbox tests pass
   unmodified, and the leg's latency tests pass unmodified (§IV, decision 5).
3. **The test (Aspire/CI-only, because the persistence half needs Postgres and RabbitMQ).** It sits at the
   `DbContextOutbox` level, in a host built in the test process with `AddWolverineForContext` against the
   fixture's Postgres and RabbitMQ, with two test-only message types: one declared durable, one not.
   - *Row before return:* after `OutboxEventBus.PublishAsync` + `OutboxTransactionalCommit.CommitAsync`
     return for the durable type, with delivery held back, an outgoing row for it exists in the schema.
     For the control type, no row ever exists.
   - *Survives a send failure:* with the send made to fail, `CommitAsync` still returns, the durable
     type's row remains and is delivered once sending recovers. The control type is discarded (today's
     behaviour, pinned).
   - **Proven by counterfactual:** removing the durable type from the list turns both facts red. The PR
     quotes that run. The failure-injection mechanism is the test-writer's choice. It must not be the
     Aspire resource restart that `OutboxSurvivesAKillTests` excludes from CI.
4. **Documentation corrections owed** (verified-false claims, which the lane may fix as doc fixes):
   - `OutboxTransactionalCommit.cs:21-23`: *"The durability, the retry and the recovery after a crash are
     Wolverine's"* becomes: the atomicity is ours; durability, retry and crash recovery are Wolverine's
     **only for types declared in `durableOutboxMessageTypes`** (ADR-0185); every other type is sent
     inline after the commit and lost on a crash or failed send.
   - `IEventBus.cs:32-34`: *"the send survives a crash, which the previous straight-to-broker publish did
     not"* is false for `AuditChunkArchivedV1` unless Q3 makes it durable. State which.
   - `OutboxEventBus.cs:14-19` (handler paragraph): the ambient context enrols the publish in the
     handler's transaction, **but a non-durable type is still sent inline after commit**. Also the
     `:46-52` remark (decision 5 consequence above).
   - `WolverineDefaults.cs:146-161`: the comment explains Inline mode for listeners. Add the sending side.
5. `security-reviewer` at phase 6 (decision 6). It checks the no-secret-in-body rule for every listed type,
   and that a durable publish cannot leave an open transaction across an `await` that discloses.

**Record edits, prepared for a human on acceptance (ADR-0144: the lane does not edit ADRs):**

- **ADR-0088**, add under the title: *"**Amended by ADR-0185** (2026-10-22): the outbox is durable only
  for message types a context declares; every other RabbitMQ publish is sent inline after commit and can be
  lost."* In its spec 021 amendment, qualify *"closed it"*. In Consequences, qualify *"prevents message
  loss"* and withdraw *"200 outbox rows … Measured (spec 021 SC-005)"*: no outgoing row was written, so
  that measurement was not of an outbox write.
- **CLAUDE.md**, stack table, *Wolverine defaults* row: *"Postgres outbox"* becomes *"Postgres outbox
  (durable per declared type, ADR-0185; the hot path is not, #3103)"*. CLAUDE.md has no sentence claiming
  send durability (searched at `b5579a60`), so this is a qualification, not a correction.

**Open questions for the owner:**

- **Q1. Scope beyond the ruling's literal criterion.** Should any *unsure* type join `CameraAddressReadV1`?
  `WebhookIntegrationRevokedV1` and `WebhookClientRaceDisableFailed` are the strongest candidates: their
  loss leaves a revoked integration's client enabled, and their consumer's inbox is already durable. The
  four camera types decide whether a stream exists at all. Same slice or later ones?
- **Q2. Do credential-issuing responses count as audit-before-disclosure?** (`WebhookIntegrationRotatedV1`,
  `DeviceRegisteredV1`, `KioskEnrolledV1`). The secret is created by the request rather than read from
  storage, which is the shape ADR-0179 addressed.
- **Q3. Background publishers** (`StreamHealthChangedV1`, `AuditChunkArchivedV1`): durable, or is the
  `IEventBus` remark corrected instead?
- **Q4. Declaration mechanism:** per-module list (recommended) or contract marker?
- **Q5. The two vacuous tests:** re-point `OutboxCoversEveryContextTests` at a durable type once one exists
  in CameraCatalog, and annotate `OutboxSurvivesAKillTests` as vacuous until #3103 decides? Or retire
  both?
- **Q6. Sequencing:** confirm that the mechanism lands first with test-only types, and #3016 slice A adds
  `CameraAddressReadV1` to the list in its own PR.
- **Q7. Mixed-caller types** (`SystemVariableValueChangedV1`, `ResolvedOverlayTextChangedV3`,
  `WallSceneChangedV1`): confirm they go with #3103, including their admin-originated publishes.
- **Latency baseline:** not needed for this ADR (decision 5). It is #3103's first question: which figure is
  the baseline for *event → overlay state*, and what delta is unacceptable.

**Observed in passing, not decided here:** two HTTP responses disclose a credential with no integration
event at all. `ReplayRegisteredClientQueryHandler` replays a stored secret on an `Idempotency-Key` hit,
and `POST /webhook-integrations` returns a bearer token. Neither publishes anything that could be made
durable. Whether either needs an audit event is a separate question, and it belongs in its own issue if
the owner wants one.
