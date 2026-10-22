# Plan 436 — The retire a sweep cannot strand

**Spec**: [spec.md](spec.md) · **Issue**: #2783 · **Phase**: 2 (Plan)

## 1. Constitution / ADR check

- **ADR-0182** (Accepted) authorises exactly this: a chain-scoped retry on
  `DbUpdateConcurrencyException` for `CameraRetiredV1` only. The policy's doc comment states the four
  conditions and how the retire meets each (ADR-0182 Implementation Note 3). **No new ADR.**
- **ADR-0113** otherwise unchanged: HTTP still surfaces 409/412; SPAs never retry; provision, repoint
  and the wall-scene switch keep dead-letter-first. Proven by negative facts (spec §3 "Scope").
- **No cross-context references.** Everything lives in StreamDistribution; `CameraRetiredV1` is
  already a `Shared.Contracts` type the context consumes.
- **No domain or Application change.** ADR-0182 Alternatives rejects a catch-and-reload in
  `RetireStreamCommandHandler`; the retry lives in Infrastructure beside the other two policies.
- §II, ADR-0105 guards, ADR-0084 metrics: a ~15-line class; nothing primitive on a domain model.

## 2. Bounded context and layers

**StreamDistribution / Infrastructure only.**

### 2.1 `src/StreamDistribution/Infrastructure/RetireStreamFailurePolicy.cs` (new)

`public sealed class RetireStreamFailurePolicy : IHandlerPolicy`:

```csharp
var chain = chains.FirstOrDefault(candidate => candidate.MessageType == typeof(CameraRetiredV1));
chain?.OnException<DbUpdateConcurrencyException>()
    .RetryWithCooldown(100.Milliseconds(), 500.Milliseconds(), 2.Seconds());
```

- **Mechanism: `RetryWithCooldown`** (inline `RetryInlineContinuation` with a delay). Re-enters
  `Handler.HandleAsync`, so a new scope and DbContext per attempt (spec §2c). Needs no durable inbox,
  unlike `ScheduleRetry` (spec 328). The inline delay is an uncancellable `Task.Delay`
  (`WebhookIntegrationRevokedFailurePolicy`'s doc) — acceptable at **≤ 2.6 s total**, wrong at the
  minute scale, which is why the ladder stays sub-second-to-seconds.
- **Ladder sizing**: the sweep saves every 2 s and the retire's load→save window is milliseconds, so
  a 100 ms retry on fresh state almost always wins; 500 ms and 2 s cover a slow sweep pass. 4 attempts.
- **After the ladder**: no `.Then` — the slots exhaust and `DetermineExecutionContinuation` falls back
  to `MoveToErrorQueue` (ADR-0182 Decision 4: "the default dead-letter behaviour applies"). Never
  `MoveToErrorQueue` on attempt 1.
- **Not** `OnException<Exception>`: the `InvalidOperationException` the event handler throws for an
  unreachable SFU keeps today's behaviour (spec §6).
- Mechanism verified against decompiled 6.51.0 before writing: `RetryWithCooldown` builds
  `RetryInlineContinuation(delay)` slots and `FailureRule` falls through after the last slot. Stop and
  report if either does not hold.

### 2.2 Registration — `StreamDistributionInfrastructureModule.cs`

`builder.AddWolverineForContext<StreamDistributionDbContext>(..., configureMore: opts => opts.Policies.Add<RetireStreamFailurePolicy>());`
— exactly LayoutComposition's shape. No `durableInboxMessageTypes`.

### 2.3 Existing characterisation that must move — `CameraEventFailurePolicyCharacterisationTests`

`StreamDistribution_registers_no_wolverine_failure_rule` bans `IHandlerPolicy`/`OnException` in any
StreamDistribution source; it **will go red**. Its stated reason is *"a failure rule would replace the
default for the two camera event handlers too"* — a property ADR-0182 now deliberately changes for one
**other** chain. Not weakened, replaced by a direct assertion of the property it stood in for:

1. `DiscoverAsync` gains `opts.Policies.Add<RetireStreamFailurePolicy>()`, so the existing Theory now
   asserts, **assertion text unchanged**, that `CameraRegisteredV1`/`CameraAddressChangedV1` still
   dead-letter on every attempt *with the real policy applied* (previously only with none).
2. The source scan exempts exactly `src/StreamDistribution/Infrastructure/RetireStreamFailurePolicy.cs`
   (one literal path, `/`-normalised) and keeps banning every other occurrence; the class doc's
   "Pinned (2)" sentence is updated to say so.

Called out in the PR as an intended movement (ADR-0182), with the counterfactual T007(d) proving the
narrowed scan still catches a second policy file.

## 3. Entities, value objects, invariants

None new. Invariants relied on (unchanged): `Stream.Retire` is terminal and idempotent
(`Stream.cs:124-131`); `EnsureNotRetired` refuses health reports on a retired stream; the
`Version` concurrency token (ADR-0113 Layer 2).

## 4. Messaging

`CameraRetiredV1` (integration, `Shared.Contracts`) → `CameraRetiredIntegrationEventHandler` →
`RetireStreamCommand` → `StreamHealthChangedDomainEvent` → its integration event via the outbox. A
failed attempt's captured outbox messages are discarded by `ClearAllAsync`, so the announcement is
published once, by the attempt that commits. No contract change.

## 5. Tests (phase 4a, **red**) — tasks T002–T004

- **Unit, no Docker** (`tests/StreamDistribution.Infrastructure.Tests/RetireStreamFailurePolicyTests.cs`,
  new): continuations per attempt via the same `FailureRuleCollection` internals
  `CameraEventFailurePolicyCharacterisationTests` already reflects on; and a Wolverine host
  (`StubAllExternalTransports`, no Postgres, `AllowedButWarn` as in `WolverineDefaults`) driving the
  real `CameraRetiredIntegrationEventHandler` + `RetireStreamCommandHandler` over a scoped fake
  repository (reuse/extend `InMemoryStreamRepository`'s shape) whose save throws per a shared script
  and which records one instance per scope. Red today: attempt-1 `MoveToErrorQueue`; the host's
  `IMessageBus.InvokeAsync` throws `DbUpdateConcurrencyException` after one load.
- **Integration** (`tests/Integration.Tests/StreamDistribution/RetireHealthRaceIntegrationTests.cs`,
  new; **add to a `ci-shards/shard-N.filter`** — `ProvisionRetireRaceIntegrationTests` is in
  shard-2): ADR-0182 Note 1. Seed a Healthy stream + MediaMTX path; build `RetireStreamCommandHandler`
  by hand (spec 318's pattern) with a repository decorator that, after `GetByCameraAsync`, runs
  `ReportStreamHealthCommandHandler` (Healthy) on a **separate** DbContext. Assert the first save throws
  `DbUpdateConcurrencyException` (race real — green on arrival, it is a premise check); then a second
  attempt on a fresh scope → `Retired` + path gone. The Wolverine-retry assertion itself lives in the
  unit tier, where it is deterministic; the IT proves the conflict and that a fresh scope clears it.

## 6. Review

`backend-reviewer` (mandatory): refuse the policy unless its doc shows all four ADR-0182 conditions.
`security-reviewer`: not required — no endpoint, scope, fab, secret or HTTP-retry surface; one
exception type on one internal chain.
