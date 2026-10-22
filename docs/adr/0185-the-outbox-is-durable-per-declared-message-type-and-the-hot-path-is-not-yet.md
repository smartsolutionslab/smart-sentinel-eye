# ADR-0185: The outbox is durable per declared message type, and the hot path is not yet

**Status:** Proposed (records the owner's ruling of 2026-10-22 on #3102 and the owner's answers of the same day to Q2, Q4-Q7 and the camera-address question; awaiting owner acceptance of this text. One default remains overturnable by the owner: Q3, see *Still open*)
**Date:** 2026-10-22
**Amends:** [ADR-0088](./0088-wolverine-defaults.md) (its spec 021 amendment and two of its Consequences, by the text in *Implementation Notes*)
**Relates to:** ADR-0049, ADR-0073, ADR-0125, ADR-0126, ADR-0144, ADR-0154, ADR-0179, ADR-0184, spec 021, spec 377, issues 1605, 2990, 3016, 3092, 3102, 3103

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

**The owner's answers (2026-10-22), recorded as decisions 2, 3 and 7 and in *Implementation Notes*:**
Q6 yes (the mechanism lands first; #3016 slice A adds its own type). Q4: a per-module list. Q2: a response
that hands out a credential counts as audit-before-disclosure. Q1: the first slice's list is the enumeration
in decision 3, all of it off the hot path. Q7 yes (the three mixed-caller types go to #3103). Q5: the two
tests that cannot fail today are fixed in the slice. The camera address in a durable body (raised while
folding in the first answers): both camera-URL types stay on the list under decision 4's narrowed rule.
Q3 was not ruled; decision 3 records the default this
ADR takes and that the owner can overturn it.

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

**Verified in 6.51.0 at the decompiled-source level, not yet observed at runtime.** The slice's first test
is what observes it (*Implementation Notes*, item 3). Each bullet below is an assumption that test must
turn into an observation:

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
  context. Turning retries on later would break every durable publish at runtime. The test covers that.

**2. Decided (Q4): a per-module list, in the publishing context's Infrastructure module, beside its
`AddWolverineForContext` call.** This is the existing pattern (`IdentityInfrastructureModule.cs:74`). The
decision stays with the context that owns the publish. `Shared.Contracts` keeps its rule of carrying wire
shapes and not delivery policy (ADR-0073). Only the type's own publisher can make its send durable anyway.

**3. Which types.** The enumeration is from every `IEventBus.PublishAsync` in `src/` at `b5579a60`
(26 call sites; nothing in `src/` publishes through `IMessageBus` or a cascading return), re-verified on
the same commit when the answers were folded in.

*Durable (Q1, Q2, Q6): 16 types in the first slice, plus `CameraAddressReadV1` in #3016 slice A.* None is
published from the MQTT ingest loop or the rule fan-out; every publisher is an operator or integration HTTP
request.

| # | Type | Published at | Module's list | Why durable |
|---|---|---|---|---|
| 1 | `CameraAddressReadV1` | `CameraCatalog/Application/Queries/Handlers/GetCameraAddressQueryHandler.cs:47` (branch `feat/3016-camera-address-write-read`, not on `develop`) | CameraCatalog, **added by #3016 slice A** | Audit-before-disclosure (ADR-0179 decision 5) |
| 2 | `WebhookIntegrationRevokedV1` | `EventIngestion/Application/EventHandlers/WebhookIntegrationRevokedDomainEventHandler.cs:29` | EventIngestion | Loss leaves a revoked integration's Keycloak client enabled; its consumer's inbox is already durable (`IdentityInfrastructureModule.cs:74`) |
| 3 | `WebhookClientRaceDisableFailed` | `Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:506` | Identity | The retry of the same disable; same asymmetry |
| 4 | `WebhookIntegrationRotatedV1` | `Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:100` | Identity | Credential issued (Q2). Loss also leaves EventIngestion on hash-compare (`WebhookIntegrationRotatedV1Handler`) |
| 5 | `DeviceRegisteredV1` | `Identity/Application/EventHandlers/ClientRegisteredDomainEventHandler.cs:41` | Identity | Credential issued (Q2); response is `DeviceCredentialsDto` |
| 6 | `KioskEnrolledV1` | `Identity/Application/EventHandlers/ClientRegisteredDomainEventHandler.cs:57` | Identity | Credential issued (Q2); response is `KioskCredentialsDto` |
| 7 | `CameraRegisteredV1` | `CameraCatalog/Application/EventHandlers/CameraRegisteredDomainEventHandler.cs:19` | CameraCatalog | Admin change. Loss: no stream provisioned |
| 8 | `CameraRenamedV1` | `CameraCatalog/Application/EventHandlers/CameraRenamedDomainEventHandler.cs:26` | CameraCatalog | Admin change. Loss: audit gap |
| 9 | `CameraAddressChangedV1` | `CameraCatalog/Application/EventHandlers/CameraAddressChangedDomainEventHandler.cs:27` | CameraCatalog | Admin change. Loss: stream keeps the old address |
| 10 | `CameraRetiredV1` | `CameraCatalog/Application/EventHandlers/CameraRetiredDomainEventHandler.cs:26` | CameraCatalog | Admin change. Loss: stream outlives its camera |
| 11 | `SystemVariableDefinedV1` | `SystemVariables/Application/EventHandlers/VariableDefinedDomainEventHandler.cs:36` | SystemVariables | Admin change. Loss: audit gap |
| 12 | `SystemVariableArchivedV1` | `SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs:37` | SystemVariables | Admin change. Loss: audit gap |
| 13 | `OverlayRevisionPublishedV3` | `OverlayDesigner/Application/EventHandlers/OverlayRevisionPublishedDomainEventHandler.cs:31` | OverlayDesigner | Admin change. Loss: reverse index and wall stay on the old revision |
| 14 | `OverlayRevisionArchivedV1` | `OverlayDesigner/Application/EventHandlers/OverlayRevisionArchivedDomainEventHandler.cs:25` | OverlayDesigner | Admin change. Same, for archive |
| 15 | `LayoutRevisionPublishedV2` | `LayoutComposition/Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs:38` | LayoutComposition | Admin change. Loss: audit gap |
| 16 | `LayoutRevisionArchivedV1` | `LayoutComposition/Application/EventHandlers/LayoutRevisionArchivedDomainEventHandler.cs:27` | LayoutComposition | Admin change. Loss: audit gap |
| 17 | `WallConfiguredV1` | `LayoutComposition/Application/EventHandlers/WallConfiguredDomainEventHandler.cs:25` | LayoutComposition | Admin change. Loss: audit gap |

**The admin HTTP change events are 11 (rows 7-17), verified.** Each is published from one call site only
(one `new <Type>(` in `src/`), from a domain-event handler whose domain event is raised by a command that
only an `Api` endpoint constructs. No integration-event handler in CameraCatalog, SystemVariables,
OverlayDesigner or LayoutComposition calls those aggregates' admin methods. The first slice's lists:
Identity 4, CameraCatalog 4, LayoutComposition 3, SystemVariables 2, OverlayDesigner 2,
EventIngestion 1.

*Not durable: the hot path and the mixed-caller types, to #3103 together (Q7).*

| Type | Published at | Why it stays Inline |
|---|---|---|
| `FabEventIngestedV1` | `EventIngestion/Application/EventHandlers/EventIngestedDomainEventHandler.cs:48` | MQTT loop (`PersistenceLoopHostedService.cs:368`, `IngestEventBatchCommand`). Also HTTP `/events/manual` and webhook delivery via `IngestEventCommand` (`EventIngestStore.cs:63`). Same type, so it cannot be split |
| `SystemVariableValueRequestedV1`, `OverlayHighlightRequestedV1`, `WallSceneSwitchRequestedV1` | `Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:163, 179, 195` | Rule fan-out |
| `SystemVariableValueChangedV1`, `ResolvedOverlayTextChangedV3` | `SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:40, 82`; also `VariableArchivedDomainEventHandler.cs:68` | **Mixed callers**: the hot path via `SystemVariableValueRequestedV1Handler`, and HTTP via `SystemVariableEndpoints.Writes.cs:158` |
| `WallSceneChangedV1` | `LayoutComposition/Application/EventHandlers/WallSceneSwitchedDomainEventHandler.cs:41` | **Mixed callers**: the hot path via `WallSceneSwitchRequestedV1Handler`, and HTTP via `WallEndpoints.Commands.cs:188` |

**Durability is per type, never per caller.** A mixed-caller type cannot be durable for its HTTP publishes
and Inline for its hot-path ones. So all three mixed types stay off the list, and their admin-originated
publishes inherit the hot path's loss window until #3103 decides. `ResolvedOverlayTextChangedV3`'s publish
from `VariableArchivedDomainEventHandler.cs:68` is the same type and stays Inline, even though
`SystemVariableArchivedV1` from the same handler (line 37) is durable.

*Not durable by this ADR's default; the owner did not rule (Q3) and can overturn it:*

| Type | Published at | Why it stays Inline |
|---|---|---|
| `StreamHealthChangedV1` | `StreamDistribution/Application/EventHandlers/StreamHealthChangedDomainEventHandler.cs:60` | Published from `StreamHealthWatcher`'s 2 s sweep (`StreamHealthWatcher.cs:40`), near the hot path. A per-transition INSERT there is a cost #3103 should price with the rest |
| `AuditChunkArchivedV1` | `AuditObservability/Application/Retention/AuditRetentionHostedService.cs:146` | Background retention; no disclosure depends on it |

**Consequence of the Q3 default:** `Shared.CQRS/IEventBus.cs:32-34` uses `AuditChunkArchivedV1` to claim
*"the send survives a crash"*. That stays false, so the remark is corrected, not made true (*Implementation
Notes*, item 5).

*Out of scope, a follow-up gap to file (Q2):* two credential-returning paths publish **no** event today,
so nothing about them can be made durable: `ReplayRegisteredClientQueryHandler`, which replays a stored
secret on an `Idempotency-Key` hit, and `POST /webhook-integrations`, which returns a bearer token. Whether
either needs an audit event is not decided here. It is filed as its own issue (*Implementation Notes*,
before the lane starts).

**4. Decided (2026-10-22): a durable message body carries no secret that is not already stored at rest in
the same database.** The envelope body is stored in plaintext in `<schema>.wolverine_outgoing_envelopes`
until delivered. Adding a type to the list is therefore a security-relevant change, and this rule, not "no
secret at all", is what it is checked against.

Rows 1-6 and 8, 10-17 carry no secret: `CameraAddressReadV1` keeps the URL out by ADR-0179 decision 5,
the five Identity and EventIngestion bodies carry names, client ids, fabs and timestamps, and the other
admin bodies carry identifiers, names, variable types, scene lists and tile and overlay geometry. None
carries a secret (verified against their records in `Shared.Contracts` and
`WebhookClientRaceDisableFailed.cs:3`).

**Rows 7 and 9 carry one, and the owner kept them on the list (2026-10-22).** `CameraRegisteredV1.Url` and
`CameraAddressChangedV1.Url`/`PreviousUrl` are the stored RTSP address in full, which ADR-0179 treats as a
credential (and decision 7 there left these payloads unchanged). They satisfy the rule above:

- The envelope schema (`wolverine_camera_catalog`) is in the same database as the `cameras` table, which
  already holds the same address verbatim. A durable copy adds no new holder.
- An envelope row is deleted once the broker acknowledges it, so the copy is transient.
- What leaves the service is redacted at the audit consumer (spec 377). Durability does not change what a
  consumer receives: the same body reaches it today, Inline.

**Dead-letter caveat, checked in 6.51.0 decompiled.** A durable envelope that keeps failing to send does
**not** move to `wolverine_dead_letters`. `SendingAgent.markFailedAsync` applies sending-failure policies
only when some are configured (`tryApplySendingFailurePoliciesAsync`), and `src/` configures none. Otherwise
it trips the circuit breaker, and `DurableSendingAgent.enqueueForRetryAsync` keeps the envelope: retried,
reassigned to any node beyond `MaximumEnvelopeRetryStorage`, and deleted only when delivered or expired.
`src/` sets no `DeliverWithin`/`DeliverBy`, so these envelopes never expire. For rows 7 and 9:

- **It stays in the same database.** The undelivered body rests in `wolverine_camera_catalog`'s outgoing
  table, beside `cameras`. It reaches no store the address is not already in.
- **It widens retention, but only until delivery.** An undelivered body has no age limit, and unlike the
  `cameras` row it outlives a retirement or an address change. It is not the unbounded store #3092 is
  about: it drains on delivery, and `OutboxBacklogHealthCheck` reports Degraded at
  `ConcerningAttempts = 5`. But nothing purges a row that never delivers; that takes an operator (drain or
  delete), not a retention limit. **Linked to [#3092](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3092)**,
  which asks what retention is acceptable for payloads that may hold a credential. This is one more place
  its answer applies.
- **Consumer-side dead letters are unchanged.** Where a consumer's failed message is dead-lettered is
  decided by the consumer's listener, not this ADR. No consumer of rows 7 or 9 has a durable inbox
  (`durableInboxMessageTypes` is passed only by Identity, `IdentityInfrastructureModule.cs:74`), so those
  dead letters stay where #2990 and #3092 already place them. This ADR adds none.

**5. Latency (§IV): N/A for every type this ADR makes durable.** No type in the durable table is published
from the *event → overlay state* leg's path: the MQTT ingest loop, `FabEventIngestedV1`, the rule fan-out
and the SystemVariables and LayoutComposition relays publish only the Inline types above. Every publisher
on the list is an operator or integration HTTP request, so the INSERT lands on that request, not on the leg.
Every hot-path type stays Inline, so the leg's code path is unchanged. The lane shows that by running the
leg's existing latency tests unmodified. **If the owner adds any hot-path, mixed-caller or Q3 type, this
line no longer holds and that type moves to #3103.**

**6. Security-sensitive.** The audit guarantee of ADR-0179 depends on this, and decision 4 is a
plaintext-storage rule. Phase 6 runs `security-reviewer` on every slice under this ADR.

**7. Decided (Q6): the mechanism lands first.** The `durableOutboxMessageTypes` parameter, proved with
test-only message types and with the decompiled-only assumptions of decision 1 observed by the slice's
first test, lands under #3102 together with the 16 types of decision 3. #3016 slice A adds
`typeof(CameraAddressReadV1)` to CameraCatalog's list **in the PR that introduces the type**, and its
integration test asserts the outgoing row for that type. That respects the original ruling that durability
precedes slice A's merge.

## Consequences

**Positive: audit-before-disclosure holds for every listed type.** When `CommitAsync` returns, the
envelope is a committed row: for `CameraAddressReadV1` before the address is returned, and for the three
credential-issuing types before the credential is. A broker outage delays the record and cannot drop it,
and a crash after the commit is recovered by the durability agent. The AuditObservability row stays
eventually consistent: *durable before disclosure*, not *recorded before disclosure*.

**Positive: admin changes reach their consumers.** A broker outage during a camera registration, address
change or retirement no longer leaves StreamDistribution without a stream, on the old address, or serving a
retired camera. A revoked webhook integration's client is disabled once the broker returns.

**Positive: no schema, migration or new infrastructure.** The table, its per-context schema, the
durability agent and the backlog health check already exist (`OutboxBacklogHealthCheck`, Degraded at
`ConcerningBacklog = 5_000` or `ConcerningAttempts = 5`, per ADR-0154). For durable types the check starts
reporting something real.

**Positive: revert is configuration.** Removing a type from the list returns it to Inline. No data
migration. A rollback still drains first (*Rollout*).

**Negative: one INSERT and one DELETE per durable message, in the publisher's own database.** The
INSERT happens at `PublishAsync` time, not at commit. It opens the DbContext's transaction early, so the
connection is held from publish to commit. For `CameraAddressReadV1` that is one round-trip per address
read on an operator-initiated `GET`; for the other 16 it is one round-trip on an admin or integration
request that already writes. The DELETE happens on acknowledgement, off the request path. Both go through
the message store's own pool (ADR-0125). The INSERT goes through the DbContext's.

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
(`Endpoint._mode` defaults to `BufferedInMemory`, `Endpoint.cs:110` decompiled), because this ADR touches
only the exchange endpoint, unlike `UseDurableOutboxOnAllSendingEndpoints`. One listed type has a handler
in its own publishing context: `WebhookClientRaceDisableFailed` (published and handled by Identity,
`WebhookClientRaceDisableFailedHandler.cs:13`). Its broker copy becomes durable and its local copy stays in
memory, as today. No other listed type has a same-context handler.

**Negative: what stays loss-capable, and why.** Every type in the hot-path table, the three mixed-caller
types (including their admin-originated publishes), and the two Q3 types keep today's two loss windows: a
crash between SQL commit and send, and a broker outage at send time, logged as *"discarded"* and
swallowed. On the hot path this is a known risk that conflicts with `WolverineDefaults.cs:101-102`
(*"AuditObservability, which every \*V1 is required to reach"*). The source of `FabEventIngestedV1` is not
redelivered after the MQTT ack. It stays a risk because making it durable spends §IV budget that #3103 has
not yet priced. The leg's own baseline is contested (§IV records 57.6–75 ms CI / 82–234 ms dev for this leg
plus render; #2072 cites 758 ms). Recorded so nobody reads this ADR as closing #3102's whole finding.

**Negative: two credential disclosures remain unaudited.** `ReplayRegisteredClientQueryHandler` and
`POST /webhook-integrations` publish nothing (decision 3). This ADR does not change that.

**Rollout.** Deploying the change loses nothing new. A process stopped mid-deploy loses only an Inline
send in flight, as today. **Rollback** to Inline while durable rows are still pending is not verified:
whether the durability agent still delivers a row whose destination's sender is no longer durable was not
established from the decompiled source. Until a test shows it, the procedure is to drain first: every
affected schema's `outbox-<prefix>` health entry reports a pending count of 0, then deploy the revert.

## Alternatives Considered

**Every sending endpoint durable (`UseDurableOutboxOnAllSendingEndpoints`).** This was #3102's original
proposal. It puts a DB write on every hop of the event-to-overlay path and makes local queues durable.
That is the trade #3103 holds open. Rejected by the owner for now.

**A marker interface or attribute on the contract** (`IDurablyPublished`, or `[DurableOutbox]` in
`Shared.Contracts`, read once in `WolverineDefaults`). Central, and impossible to forget in a module. But
it puts delivery policy on a wire contract that every consumer references (ADR-0073 keeps contracts to
shape). It also cannot express "durable in this deployment, not that one". And for a mixed-caller type it
reads as if it were possible. Equally verifiable in 6.51.0 (the same `ConfigureSending` call, with an
`IsAssignableTo` test in place of the list lookup). **Not taken: the owner chose the per-module list (Q4).**

**One central list in `WolverineDefaults`.** One place to read, but ServiceDefaults would name every
context's contract types and own a decision each context should own. Not taken.

**Per-endpoint configuration outside the convention** (`opts.PublishMessage<T>().ToRabbitExchange(...)
.UseDurableOutbox()`). This creates a second route next to the conventional one. A type would then be
published twice unless the convention excludes it, which is a new way to break routing silently. Rejected.

**Leave it, and fix the documentation only.** That would make the records honest and leave ADR-0179's
audit guarantee false. Rejected by the ruling.

## Implementation Notes

**Before the lane starts (human):** this ADR accepted, including the Q3 default in *Still open*. File the
follow-up issue for the two credential-returning paths that publish no event (decision 3) and link it from
#3102.

**What the lane builds (#3102, one slice, phase 4a colour declared per item):**

1. **The mechanism.** `WolverineDefaults.AddWolverineForContext` gains the `durableOutboxMessageTypes`
   parameter and the single `ConfigureSending` call of decision 1, with a comment carrying the *replaces,
   not composes* warning and the `DisableRetry` precondition. **Red.**
2. **The lists.** Six Infrastructure modules pass `durableOutboxMessageTypes` beside their existing
   `AddWolverineForContext` call: Identity (`WebhookIntegrationRotatedV1`, `DeviceRegisteredV1`,
   `KioskEnrolledV1`, `WebhookClientRaceDisableFailed`), EventIngestion (`WebhookIntegrationRevokedV1`),
   CameraCatalog (`CameraRegisteredV1`, `CameraRenamedV1`, `CameraAddressChangedV1`, `CameraRetiredV1`),
   SystemVariables (`SystemVariableDefinedV1`, `SystemVariableArchivedV1`), OverlayDesigner
   (`OverlayRevisionPublishedV3`, `OverlayRevisionArchivedV1`), LayoutComposition
   (`LayoutRevisionPublishedV2`, `LayoutRevisionArchivedV1`, `WallConfiguredV1`). 16 types, rows 7 and 9
   included (decision 4). `CameraAddressReadV1` (the 17th) is added by #3016 slice A, not
   here (decision 7). **Red.**
3. **The mechanism test (Aspire/CI-only, because the persistence half needs Postgres and RabbitMQ).** It sits
   at the `DbContextOutbox` level, in a host built in the test process with `AddWolverineForContext` against
   the fixture's Postgres and RabbitMQ, with two test-only message types: one declared durable, one not. It
   is the first test the slice writes, and it observes every decompiled-only assumption in decision 1:
   - *Row before return:* after `OutboxEventBus.PublishAsync` + `OutboxTransactionalCommit.CommitAsync`
     return for the durable type, with delivery held back, an outgoing row for it exists in the schema.
     For the control type, no row ever exists. (Observes: `ConfigureSending` applies per type, Durable is
     accepted for a RabbitMQ exchange, the row is written in the caller's transaction.)
   - *Survives a send failure:* with the send made to fail, `CommitAsync` still returns, the durable
     type's row remains and is delivered once sending recovers. The control type is discarded (today's
     behaviour, pinned).
   - *Publish with no accompanying write:* the durable type published with no EF change, then committed,
     leaves a committed row (the `CameraAddressReadV1` and `AuditChunkArchivedV1` shape).
   - **Proven by counterfactual:** removing the durable type from the list turns the facts red. The PR
     quotes that run. The failure-injection mechanism is the test-writer's choice. It must not be the
     Aspire resource restart that `OutboxSurvivesAKillTests` excludes from CI.
4. **Per-type tests.** One assertion per listed type that its module's host routes it through a durable
   sender, so a type dropped from a list, or a list not passed, fails a named test rather than nothing.
   The mechanism the test-writer picks (sender inspection on the built runtime, or an outgoing-row
   observation) is open; a source scan of the module file is not enough
   (it proves the list was written, not that it took effect). **Red.**
5. **The two tests that cannot fail today are fixed (Q5), so each can fail.** Both assert only a drain to
   zero on a table that is always zero.
   - `OutboxCoversEveryContextTests` (`tests/Integration.Tests/CameraCatalog/`) publishes
     `CameraRegisteredV1`, which item 2 makes durable. It must also observe a **non-zero** outgoing count
     for that type before the drain, and is shown red by counterfactual (type off the list).
   - `OutboxSurvivesAKillTests` (`tests/Integration.Tests/EventIngestion/`, `Category=Disruptive`) uses
     `FabEventIngestedV1`, which stays Inline. It is re-pointed at a durable type (EventIngestion's
     `WebhookIntegrationRevokedV1` is the natural one) and must observe the row pending across the kill and
     delivered after it. It stays `Disruptive`; the counterfactual is run locally and quoted.
   These are behaviour-changing for the tests themselves: **red** by counterfactual, not characterisation.
6. **Characterisation, observed green and passing unmodified:** the existing `ServiceDefaults.Tests` and
   `Integration.Tests` outbox tests other than the two in item 5, and the *event → overlay state* leg's
   latency tests (§IV, decision 5).
7. **Documentation corrections** (verified-false claims, fixed in the same slice as doc fixes):
   - `ServiceDefaults/OutboxTransactionalCommit.cs:21-23`: *"The durability, the retry and the recovery
     after a crash are Wolverine's"* becomes: the atomicity is ours; durability, retry and crash recovery are
     Wolverine's **only for types declared in `durableOutboxMessageTypes`** (ADR-0185); every other type is
     sent inline after the commit and lost on a crash or failed send.
   - `Shared.CQRS/IEventBus.cs:32-34`: *"the send survives a crash, which the previous straight-to-broker
     publish did not"* is corrected: `AuditChunkArchivedV1` is not durable (decision 3, Q3 default), so the
     send does not survive a crash. If the owner overturns Q3, the remark is made true instead.
   - `ServiceDefaults/OutboxEventBus.cs:14-19` (handler paragraph): the ambient context enrols the publish
     in the handler's transaction, **but a non-durable type is still sent inline after commit**. Also the
     `:46-52` remark (the `PublishAsync` I/O consequence above).
   - `ServiceDefaults/WolverineDefaults.cs:146-161`: the comment explains Inline mode for listeners. Add
     the sending side.
8. **The drain-before-rollback procedure** goes where an operator will find it, beside the
   `OutboxBacklogHealthCheck` documentation: before reverting any type off a list, every affected schema's
   `outbox-<prefix>` health entry must report a pending count of 0. It stays the procedure until a test shows
   that a pending row is still delivered after its sender reverts to Inline.
9. **`security-reviewer` at phase 6** (decision 6). It checks decision 4's rule for every listed type
   (rows 7 and 9 under its same-database condition), and that a durable publish cannot leave an open
   transaction across an `await` that discloses.

**Record edits, prepared for a human on acceptance (ADR-0144: the lane does not edit ADRs):**

- **ADR-0088**, add under the title: *"**Amended by ADR-0185** (2026-10-22): the outbox is durable only
  for message types a context declares; every other RabbitMQ publish is sent inline after commit and can be
  lost."* In its spec 021 amendment, qualify *"closed it"*. In Consequences, qualify *"prevents message
  loss"* and withdraw *"200 outbox rows … Measured (spec 021 SC-005)"*: no outgoing row was written, so
  that measurement was not of an outbox write.
- **CLAUDE.md**, stack table, *Wolverine defaults* row: *"Postgres outbox"* becomes *"Postgres outbox
  (durable per declared type, ADR-0185; the hot path is not, #3103)"*. CLAUDE.md has no sentence claiming
  send durability (searched at `b5579a60`), so this is a qualification, not a correction.

**Still open (the owner can overturn it before accepting):**

- **Q3, the background publishers.** The owner did not rule. This ADR keeps `StreamHealthChangedV1` and
  `AuditChunkArchivedV1` Inline and corrects the `IEventBus` remark (decision 3). Making either durable
  moves it to #3103 if it lands near the hot path (`StreamHealthChangedV1` does).

**Answered, for the record:** Q1 (the list in decision 3), Q2 (credential-issuing responses count; the two
event-less paths are a follow-up), Q4 (per-module list), Q5 (both tests fixed in the slice), Q6 (mechanism
first), Q7 (mixed-caller types to #3103), the camera address in rows 7 and 9 (kept on the list
under decision 4's narrowed rule). **Latency baseline:** not needed for this ADR (decision 5). It is
#3103's first question: which figure is the baseline for *event → overlay state*, and what delta is
unacceptable.
