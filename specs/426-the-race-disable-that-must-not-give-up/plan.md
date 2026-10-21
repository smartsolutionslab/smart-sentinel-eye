# Plan 426 — The race disable that must not give up

**Spec**: [spec.md](spec.md) · **Issue**: #2789 · **Phase**: 2 (Plan) · **Lane**: autonomous (ADR-0144)

## 1. Context, boundaries, ADR position

- **Bounded context**: Identity only. Application (rotate handler, new message + handler, shared
  disable step, log text) and Infrastructure (failure policy scope, durable-inbox list).
- **No cross-context reference** is added. The new message lives in `Identity.Application`. It is
  not in `Shared.Contracts` and is not an `IIntegrationEvent`. EventIngestion and
  AuditObservability cannot see it and are not changed.
- **ADR position**: none new, no amendment (spec §3c). ADR-0042/0088 already supply the mechanism
  (Wolverine outbox and retries, per-module queue naming), and the ladder is spec 328's, unchanged.
  **Recorded trigger, not acted on**: this is the first context-internal message on the bus. If a
  second appears, ADR-0088 should gain a short note on context-internal messages (no
  `Shared.Contracts`, no audit, conventional per-module routing). Spec 328 plan §1's own trigger
  (a second *context* adopting a ladder) is **not** met: same context, same operation.

## 2. Design

### 2.1 The message (new) — `src/Identity/Application/EventHandlers/WebhookClientRaceDisableFailed.cs`

```csharp
public sealed record WebhookClientRaceDisableFailed(string IntegrationName, string Fab);
```

The record's name states what happened, as an event, so it sits in `EventHandlers/` beside its
handler (ADR-0093). `WebhookClientDisableFailedException` already sits there too. Its primitives
are deliberate: this is a wire shape (spec §3b). It has no `EventMetadata`, because it is not
audited and carries no actor. Wolverine propagates the trace and correlation itself.

### 2.2 The shared disable step (refactor, behaviour-preserving)

Extract the post-parse half of `WebhookIntegrationRevokedIntegrationEventHandler.Handle` into one
`internal static` method in `Identity.Application/EventHandlers/` (e.g.
`WebhookClientDisabling.DisableOrThrowForRetryAsync(disable, logger, integrationName, fab,
cancellationToken)`). It derives `ClientId` (drop with `InvalidRevocationName` on
`ArgumentException`), sends the command, returns on `NotFound` with `NoWebhookClientToDisable`,
and otherwise logs `WebhookClientDisableFailed` and throws `WebhookClientDisableFailedException`.

- **Static, not a DI service**, so neither handler's constructor changes, the existing
  `WebhookIntegrationRevokedIntegrationEventHandlerTests` and `...FailurePolicyTests` construct
  the handler unmodified, and `IdentityRegistrationCharacterisationTests` (the DI-registration
  snapshot) does not move.
- **One throw site** for `WebhookClientDisableFailedException`. Both chains' policy rule matches
  it by type.
- The fab parse for the revoked handler stays where it is: it reads `EventMetadata`. The new
  handler parses its `string Fab` the same way — null or blank, or `FabIdentifier.From` throwing
  `ArgumentException`, is a Warning and a drop. To avoid a second copy of that parse, the shared
  file may host a `string`-based `TryParseFab` that the revoked handler's `ParseFab(EventMetadata,
  …)` calls with `metadata?.Fab`. The engineer's choice; behaviour identical either way.

Log events: the existing `RevocationFabInvalid`, `InvalidRevocationName`, `NoWebhookClientToDisable`
and `WebhookClientDisableFailed` are reused. Their messages say "revocation", and for the new
message that is still true, because it is a revoked integration's client. **No new `[LoggerMessage]`**
unless review asks for one.

### 2.3 The new handler — `src/Identity/Application/EventHandlers/WebhookClientRaceDisableFailedHandler.cs`

```csharp
public sealed class WebhookClientRaceDisableFailedHandler(
    ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> handler,
    ILogger<WebhookClientRaceDisableFailedHandler> logger)
{
    public async Task Handle(WebhookClientRaceDisableFailed message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();
        var (integrationName, fab) = message;   // two fields → deconstruct (CLAUDE.md, HandlerDeconstructionTests)
        ...parse fab (drop) → WebhookClientDisabling.DisableOrThrowForRetryAsync(...)
    }
}
```

It is discovered by convention (ADR-0088), mirroring the revoked handler, and needs no DI
registration.

### 2.4 The rotate handler — `RotateWebhookClientCommandHandler`

`DisableIfRevokedSinceCommitAsync` currently returns `bool` ("ended up disabled"). Change it to
publish on its two failure exits, before returning `false`. The caller's `if (!disabledDuringRace)`
block then publishes `WebhookIntegrationRotatedV1` and calls `CommitAsync` exactly as today, so
**one** commit flushes both messages and the order "publish before commit" (IEventBus doc) holds.

| Exit | Today | After |
|---|---|---|
| status not Revoked/Unverifiable | `false` | unchanged |
| success | log Disabled…, `true` | unchanged |
| `DbUpdateConcurrencyException` | detach, log AlreadyDisabled…, `true` (no commit) | **unchanged** — owner's caveat |
| other exception (not OCE) | log CouldNotDisable…, `false` | if `DbUpdateException` → detach `ex.Entries` (FR-004); publish `WebhookClientRaceDisableFailed`; log CouldNotDisable…; `false` |
| `Result` NotFound | log AlreadyDisabled…, `true` | unchanged |
| `Result` other failure | log CouldNotDisable…, `false` | publish `WebhookClientRaceDisableFailed`; log; `false` |

`AcknowledgeRaceDisable` is synchronous and returns `bool`. It either becomes async, or returns a
three-way outcome the async caller acts on. The engineer picks, within ADR-0084's advisory method
limits. The detach loop already exists in the concurrency branch, so extract it into one private
helper used by both exits rather than copying it.

**Order inside the failure exit**: detach, then publish. `OutboxEventBus.PublishAsync` only
captures into the outbox context; it touches no tracked entity. The single `CommitAsync` afterwards
is `SaveChangesAndFlushMessagesAsync` on a context whose failed entries are gone.

**Why the same `DbContext` is acceptable** (owner: "must not publish on the DbContext that just
failed on concurrency"): the concurrency exit still publishes nothing. On the
`Result`-failure exit no save was attempted (Keycloak fails first), so the context is clean. On the
`DbUpdateException` exit FR-004 detaches the failed entries, which is the same remedy spec 318
applied. A separate scope or `DbContext` would need a scope factory in Application for no gain.

### 2.5 Infrastructure

- `WebhookIntegrationRevokedFailurePolicy.Apply`: select the chains whose `MessageType` is
  `WebhookIntegrationRevokedV1` **or** `WebhookClientRaceDisableFailed`, and attach the **same**
  `ladder` array and the same `OnException<WebhookClientDisableFailedException>()` to each. Add one
  sentence to the class doc naming the second chain and #2789. The class name stays: it is the
  revocation's disable policy, and the message is that same disable.
- `IdentityInfrastructureModule`: `durableInboxMessageTypes: [typeof(WebhookIntegrationRevokedV1),
  typeof(WebhookClientRaceDisableFailed)]`, and extend the comment by one clause.
- `Log.cs`: FR-008 text only.

## 3. Entities, value objects, invariants

No domain change. `RegisteredClient.Disable` and its `DisabledAt` invariant are untouched. The
invariant this spec adds is a **process** invariant: *a race-created client of a revoked
integration is either disabled within the request, already disabled by the async path, or has a
durable `WebhookClientRaceDisableFailed` committed in Identity's outbox.* The only exception is an
outbox commit that itself fails, which surfaces as an error rather than a silent loss.

## 4. Messaging and routing — what phase 4 must check, not assume

Routing is conventional (ADR-0088). The type has a local handler, and
`ConventionalLocalRoutingIsAdditive` is on, so the expectation is two routes: the RabbitMQ
exchange named by the type, which is bound to `identity.<FullName>` (durable inbox per FR-007),
and an in-process local queue. **Verify this against the pinned Wolverine** (6.40.0, as spec 328
did by decompiling). Do not take it from this plan; see memory "a plan's SDK claim needs checking
against the pinned version".

- If there are two routes: accepted. A duplicate run is benign (spec §4 edge cases, P5), and the
  durable RabbitMQ copy is the one that survives a restart. Record it in the PR.
- If there is **no** RabbitMQ route for a type outside `Shared.Contracts` (conventional routing
  filtered by assembly or namespace): the local queue would be non-durable and a restart would
  lose the retry. **Stop and report**. Making it durable would mean a `ServiceDefaults` or routing
  change outside this spec's licence.

## 5. Tests

### 5.0 Seam commit (behaviour-free, characterised green)

Create `WebhookClientRaceDisableFailed` (the record) and `WebhookClientRaceDisableFailedHandler`
with `Handle` doing only `Ensure.That(message).IsNotNull();` and returning. Nothing publishes the
message yet, so production behaviour is identical. Run `Identity.Application.Tests` and
`Identity.Infrastructure.Tests` before and after: green and unmodified. The red tests below need
these symbols in order to fail on assertion rather than at compile time.

### 5.1 Red (Identity.Application.Tests / Identity.Infrastructure.Tests — no Docker)

**Test A — the owner's red** (`RotateWebhookClientCommandHandlerTests`, new fact):
`When_the_race_disable_fails_on_Keycloak_a_retry_message_is_captured_before_the_commit`. This is
spec 318's failure test setup: `EnqueueStatuses(Active, Revoked)`,
`keycloak.FailNextDisableWith = new HttpRequestException(...)`, and the real
`DisableWebhookClientUsing(repo, keycloak)`. Wire a new test double
`SnapshottingTransactionalCommit(FakeEventBus bus)`, which records `bus.Published` at each
`CommitAsync`. Assert:
`bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem()` equals
`("qa", "munich")`, **and** the commit's single snapshot contains it. **Red today**: nothing of
that type is published.

**Test B** (same file): `When_the_race_disable_save_fails_without_a_conflict_a_retry_message_is_captured`.
`repo.FailSecondSaveWith = new DbUpdateException("…")`, `NoOpTransactionalCommit`, and the same
assertion on the bus. **Red today.** Limitation, stated in the test's doc: the fake cannot observe
EF detaching (`ex.Entries` is empty on a hand-built exception), so FR-004's detach is
review-verified. This test pins only that the publish happens on that exit. It does **not** use
`RepositoryBackedTransactionalCommit`, whose "unresolved failure" flag the handler cannot clear.

**Test C** (same file, characterisation — expected **green before and after**, declared as such):
`A_race_disable_that_succeeds_or_finds_the_row_already_disabled_publishes_no_retry_message`. This
is a theory over the success, NotFound and concurrency setups of spec 318's existing tests, and
asserts `OfType<WebhookClientRaceDisableFailed>()` is empty. It guards FR-002 against an
implementation that publishes on every exit. It cannot be red first: it pins behaviour that does
not change.

**Test D** (new file `tests/Identity.Application.Tests/EventHandlers/WebhookClientRaceDisableFailedHandlerTests.cs`).
Mirrors `WebhookIntegrationRevokedIntegrationEventHandlerTests`, with a stub command handler and a
`CapturingLogger`. Each fact is red against the seam's empty body:
1. sends `DisableWebhookClientCommand(ClientId "webhook-qa", Fab "munich")` (red: not sent);
2. `NotFound` → returns, logs `NoWebhookClientToDisable` once (red: no log);
3. `KeycloakUnavailable` → throws `WebhookClientDisableFailedException` (red: nothing thrown);
4. theory over null, blank and unparsable fab → command not sent, `RevocationFabInvalid` logged
   (red: no log);
5. a name that cannot form a `ClientId` → command not sent, `InvalidRevocationName` logged (red:
   no log).

**Test E** (`WebhookIntegrationRevokedFailurePolicyTests`, new facts, same discovery technique):
1. `graph.ChainFor<WebhookClientRaceDisableFailed>()` has a single rule whose ladder is attempts
   1..14 = [1m, 2m, 5m x12] (red: no rule);
2. attempt 15 → `MoveToErrorQueue` (red);
3. `WebhookClientRaceDisableFailedHandler`'s own throw on `KeycloakUnavailable` is matched by that
   chain's rule (red: no rule, and the seam does not throw).

**One existing fact must change, and it is declared here**:
`No_other_discovered_chain_gained_a_failure_rule` filters out `WebhookIntegrationRevokedV1` only.
Its filter gains `&& chain.MessageType != typeof(WebhookClientRaceDisableFailed)`. That edit
records the intended scope widening (FR-006). It is not a weakened gate: every **other** chain is
still asserted rule-free, and the fact stays green before and after. The test-writer makes this
edit in 4a, and the engineer may not touch it.

**Existing facts that must pass unmodified**: all of `RotateWebhookClientCommandHandlerTests`,
including `When_the_race_disable_itself_fails_…` (it still sees Rotated published and the
`CouldNotDisable…` log by name), all of `WebhookIntegrationRevokedIntegrationEventHandlerTests`,
and the four existing policy facts.

### 5.2 Counterfactuals (phase 4b, run then revert; quote in the PR)

1. Remove the publish on the `Result`-failure exit → Test A red.
2. Remove the publish on the thrown exit → Test B red.
3. Publish on the success exit too → Test C red.
4. Drop `WebhookClientRaceDisableFailed` from the policy's selection → Test E red.
5. In the shared step, swallow instead of throwing → Test D.3 and E.3 red, **and** the existing
   revoked-handler fact `KeycloakUnavailable_throws_…` red, which proves the step really is shared.

### 5.3 CI-only (Docker)

There is no new Integration test. A millisecond-window Keycloak failure cannot be induced in the
Aspire stack, and an Integration test cannot publish an Identity-internal type without referencing
Identity.Application. `RotateRevokeRaceIntegrationTests` and
`WebhookRevocationDisablesClientIntegrationTests` must stay green: they show that the race-path
success and the async revoke are unaffected. No new test class, so no `ci-shards/*.filter` entry.

## 6. Commit plan (each commit builds on its own; ADR-0087, ADR-0030, no `Co-Authored-By`)

1. `refactor(identity): add the seam for a retried race-path webhook disable` (T001, green)
2. `refactor(identity): share the webhook-client disable step between revocation handlers` (T002,
   green, existing tests unmodified)
3. `test(2789): a failed race-path disable leaves no durable retry behind (red)` (T003–T007)
4. `fix(2789): retry a failed race-path webhook disable through the outbox on the revocation ladder`
   (T008–T011)

Commit 3 leaves the named facts failing until commit 4 — the accepted red/green pair (CLAUDE.md
on bisect).

## 7. Risks

| Risk | Mitigation |
|---|---|
| Routing differs from §4's expectation | T012 checks the pinned Wolverine; stop rule in §4 |
| Durable-inbox list typo or omission (FR-007) — no unit test sees it | review by eye (spec 328 plan §8 precedent) + phase-5 live queue check |
| FR-004 detach not observable by fakes | Test B pins the publish; the detach is a three-line reuse of spec 318's code, reviewed |
| A future refactor moves `CommitAsync` before the publish | Test A's snapshot assertion |
| Retry disables a live integration's client after `Unverifiable` | parity with spec 318's fail-closed decision; spec §4 edge case 1 |
