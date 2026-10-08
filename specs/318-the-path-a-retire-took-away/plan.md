# Plan 318 — The path a retire took away

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2743

## 1. Constitution and ADR alignment

| Rule | How this plan meets it |
|---|---|
| ADR-0113 Layer 2 (in-transaction token) | The existing `version` token is the predicate of the check (spec §1.3). No new token, no migration. |
| ADR-0113 correction 2 — no retry on conflict | The losing provision is **not retried**: it finishes in-process (compensation) and returns `Success`, or a gateway `Failure` that is not a conflict (spec §1.4). The retire cannot lose to the provision at all. |
| ADR-0113 Layer 1 (`If-Match`) | Not applicable — no cross-request client view; StreamDistribution has no mutating HTTP surface (ADR-0113 §Scope). |
| Spec 296 §1.10 precedent | A message handler that loses a Layer-2 race is not retried. Kept. |
| ADR-0047/0089 `Result` + `ApiError` | Reuses `ProvisionStreamFailures.RtspGatewayUnavailable` (503). No new error case. |
| ADR-0041 repository contract | Two members on `IStreamRepository`; Domain stays persistence-free. |
| ADR-0105 guards | `Ensure.That(stream).IsNotNull()` on the new repository members. |
| ADR-0049 async | `CancellationToken` last on both members. |
| ADR-0050 logging | Two `[LoggerMessage]` entries. |
| ADR-0144 | No ADR written; spec §5 flags the one point a reviewer must judge. |
| §IV latency | N/A — control plane (spec §8). |
| Boundaries | All changes inside StreamDistribution. No `Shared.Contracts` change, no cross-context reference. |

## 2. Bounded context and layers

StreamDistribution only.

| Layer | File | Change |
|---|---|---|
| Domain | `src/StreamDistribution/Domain/Stream/IStreamRepository.cs` | +2 members (§3) |
| Application | `src/StreamDistribution/Application/Commands/Handlers/ProvisionStreamCommandHandler.cs` | §4 |
| Application | `src/StreamDistribution/Application/Log.cs` | +2 log messages (§5) |
| Infrastructure | `src/StreamDistribution/Infrastructure/Persistence/StreamRepository.cs` | implement §3 |
| — | `RetireStreamCommandHandler.cs`, `StreamConfiguration.cs`, migrations, `Stream.cs` | **unchanged** |

No new entity, value object or invariant. The invariant being enforced is cross-handler and is
stated once: **after any interleaving of a provision and a retire of the same camera, MediaMTX has
no path for a `Retired` row** (modulo a compensation the gateway refused, which US2 finishes on the
next delivery).

## 3. Repository contract (exact signatures — the 4a fake implements these before they exist)

```csharp
/// <summary>
/// Reports whether the stream's row still carries the version this unit of work loaded or
/// inserted, without changing it. A conditional no-op UPDATE: it waits on the row lock of any
/// uncommitted writer and re-evaluates after that writer commits. Writes nothing, so it can never
/// make a concurrent writer lose (spec 318 §1.3).
/// </summary>
Task<bool> IsUnchangedSinceLoadAsync(Stream stream, CancellationToken cancellationToken);

/// <summary>
/// The row's committed state, read past the change tracker — a tracked query would hand back
/// the instance this unit of work already holds, with the values it loaded.
/// </summary>
Task<StreamState> ReadCommittedStateAsync(StreamIdentifier stream, CancellationToken cancellationToken);
```

`bool` return mirrors `ICameraRepository.ExistsByNameAsync` / `IEventRepository.ExistsAsync`.

### 3.1 `StreamRepository` implementation

```csharp
public async Task<bool> IsUnchangedSinceLoadAsync(Domain.Stream.Stream stream, CancellationToken cancellationToken)
{
    Ensure.That(stream).IsNotNull();

    StreamIdentifier identifier = stream.Id;
    AggregateVersion loaded = stream.Version;

    int matched = await dbContext.Streams
        .Where(candidate => candidate.Id == identifier && candidate.Version == loaded)
        .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Version, candidate => candidate.Version), cancellationToken);

    return matched == 1;
}

public Task<StreamState> ReadCommittedStateAsync(StreamIdentifier stream, CancellationToken cancellationToken) =>
    dbContext.Streams
        .AsNoTracking()
        .Where(candidate => candidate.Id == stream)
        .Select(candidate => candidate.State)
        .SingleAsync(cancellationToken);
```

Notes for the engineer:

- `stream.Version` is right for both branches: on the existing-row branch the provision modifies
  nothing, so current == loaded; on the insert branch an `Added` root is not bumped
  (`AggregateVersionInterceptor.cs:84`) and the row is written at `AggregateVersion.Initial`.
- `ExecuteUpdateAsync` bypasses `SaveChanges`, so the interceptor does not bump — **that is the
  point**. Do not "fix" it into a tracked save (spec §1.3 item 2; IT-3 fails if you do).
- `SingleAsync` is safe: retirement keeps the row (spec 028 FR-008), and nothing deletes streams
  outside test resets.
- Spec §9 A2: if EF cannot translate the self-assignment on the converted `Version`, use
  `ExecuteSqlInterpolatedAsync($"UPDATE streams SET version = version WHERE stream_id = {id} AND version = {loaded}")`
  with the raw values. Same semantics, same tests. Record which one shipped in the PR.

## 4. Handler change

`ProvisionStreamCommandHandler` — only `RegisterPathAsync` (US1) and the `Retired` branch (US2)
change. Step order everywhere else is spec 309's and stays.

### 4.1 US1 — `RegisterPathAsync`

```text
try AddPathAsync(stream.Path, stream.SourceUrl.Value)              // unchanged, :78
catch HttpRequestException → log PathRegistrationFailed; Failure(RtspGatewayUnavailable)  // unchanged

if (!await streams.IsUnchangedSinceLoadAsync(stream, ct))
{
    StreamState committed = await streams.ReadCommittedStateAsync(stream.Id, ct);
    if (committed == StreamState.Retired)
    {
        try RemovePathAsync(stream.Path)
        catch HttpRequestException → log ProvisionCompensationFailed(ex, camera); Failure(RtspGatewayUnavailable)
        log ProvisionYieldedToRetirement(stream.Id, camera, path); return Success(stream.Id)
    }
    // Moved by a health report or a re-point: the path is still wanted. Fall through.
}

log ProvisionedStream; Success(stream.Id)                          // unchanged
```

- Decide on `committed`, **never on `stream.State`**: the tracked copy holds the values from P1.
  The in-memory fake shares instances between handlers and cannot catch this mistake; IT-5 can
  (§6.3).
- No `catch` around the two repository calls. A database fault there propagates (no drive-by
  handling, spec 309 plan §4 precedent); the redelivery re-runs P1, and US2 covers the case where
  the row turned `Retired` meanwhile.
- One `why` comment at the assertion is warranted (the ordering is load-bearing and non-obvious):
  the check comes after the add because only then does a match prove any retire's removal follows
  the add. No issue numbers in code.

### 4.2 US2 — the `Retired` branch (`:47-50`)

```text
if (existingStream.State == StreamState.Retired)
{
    try RemovePathAsync(existingStream.Path)
    catch HttpRequestException → log PathRemovalFailed-equivalent (reuse ProvisionCompensationFailed); Failure(RtspGatewayUnavailable)
    return Success(existingStream.Id)
}
```

Here `existingStream.State` is fine — it is P1's fresh read, and `Retired` is terminal, so a stale
`Retired` cannot exist. `RemovePathAsync` tolerates 404 (`MediaMtxRtspGateway.cs:76-80`), so the
normal case (no leftover path) is a harmless DELETE. The `:52-56` comment above the re-assert stays
true; extend it by one sentence or add one to the `Retired` branch saying the redelivery re-asserts
the *absence* symmetrically.

## 5. Logging (`src/StreamDistribution/Application/Log.cs`)

```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Stream {Stream} for camera {Camera} was retired while its path was being registered; removed path {Path} again.")]
public static partial void ProvisionYieldedToRetirement(this ILogger logger, StreamIdentifier stream, CameraIdentifier camera, MediaMtxPath path);

[LoggerMessage(Level = LogLevel.Warning, Message = "MediaMTX path removal failed for retired camera {Camera} during provisioning; a redelivery will retry it.")]
public static partial void ProvisionCompensationFailed(this ILogger logger, Exception exception, CameraIdentifier camera);
```

Information for the yield (it is the system working); Warning for the failed removal (a retired
camera is watchable until it succeeds).

## 6. Test design

Phase 4a is split between two agents on **disjoint files** (ADR-0109). Red is provoked
deterministically: the retire runs **inside** the provision's `AddPathAsync`, which is the race
window by construction — no sleeps, no `Task.Delay` races, no repetition loops.

### 6.1 Application tests — `test-writer`

**Fake: `tests/StreamDistribution.Application.Tests/Fakes/InMemoryStreamRepository.cs`.** Add the
two §3 members (as plain public methods — they compile before the interface has them) and model a
committed version:

- `Dictionary<StreamIdentifier, int> committedVersions`, `Dictionary<StreamIdentifier, int> loadedVersions`,
  `HashSet<StreamIdentifier> loadedSinceSave`.
- `Get*Async` hit: `loadedVersions[id] = committedVersions[id]`; `loadedSinceSave.Add(id)`.
- `SaveAsync` (after `OnSave()`): each pending add → `committedVersions[id] = 0`,
  `loadedVersions[id] = 0`; each id in `loadedSinceSave` not just added → `committedVersions[id]++`;
  clear `loadedSinceSave`.
- `IsUnchangedSinceLoadAsync(stream)` → `loadedVersions[id] == committedVersions[id]`.
- `ReadCommittedStateAsync(id)` → the stored instance's `State`.
- Optional `Action OnIsUnchangedSinceLoad` hook only if a fact needs it; none below does.
- Doc comment states the limitation: instances are shared between handlers, so the fake cannot tell
  a decision made on the tracked copy from one made on the committed read (IT-5 can).

**Facts — `tests/StreamDistribution.Application.Tests/Commands/ProvisionStreamCommandHandlerTests.cs`**
(new facts only; existing facts untouched). "Retire inside the add" = `gateway.OnAddPath` runs a
`RetireStreamCommandHandler` built on the **same** fake repository and gateway, once.

| # | Fact | Arrange | Assert | Red today because |
|---|---|---|---|---|
| A1 | `A_retire_that_lands_while_a_redelivery_registers_the_path_keeps_the_path_removed` | existing Healthy row; retire inside the add | final `gateway.ListConfiguredPathsAsync` excludes the path; `RemoveCalls` has the path twice (retire + compensation); result `Success(id)`; row `Retired` | the provision re-adds after the retire's removal; path present, `RemoveCalls` once |
| A2 | `A_retire_that_lands_between_the_insert_and_the_path_registration_keeps_the_path_removed` | no row; retire inside the add | as A1 | same |
| A3 | `A_health_report_during_path_registration_does_not_remove_the_path` | existing Healthy row; `ReportStreamHealthCommandHandler` (healthy observation) inside the add | path present; `RemoveCalls` empty; `Success` | **green today** — the guard against compensating on any mismatch; must stay green (counterfactual: compensate-on-mismatch turns it red) |
| A4 | `A_failed_compensation_returns_RtspGatewayUnavailable` | existing row; retire inside the add; then `OnRemovePath` throws for the second removal only | `Failure(RtspGatewayUnavailable)`, status 503 | today: `Success`, no second removal attempted |
| A5 | `Provision_with_no_concurrent_writer_removes_nothing` | new camera, no hook | `RemoveCalls` empty; `Success` | **green today**; characterisation of the happy path under the new check |
| B1 (US2) | `A_redelivery_for_a_retired_stream_removes_its_leftover_path` | row Retired (via `Retire`), path planted with `gateway.AddPathAsync` | `RemoveCalls` = [path]; `AddCalls` unchanged by the redelivery; `Success(id)` | today: no removal |
| B2 (US2) | `A_redelivery_for_a_retired_stream_when_MediaMTX_is_down_returns_RtspGatewayUnavailable` | row Retired; `OnRemovePath` throws | `Failure(RtspGatewayUnavailable)` | today: `Success` |

`CameraRegisteredIntegrationEventHandlerTests` needs no new fact: it delegates to the command
handler, and its existing facts are characterisation.

### 6.2 Repository against real Postgres — `test-writer`

**New: `tests/Integration.Tests/StreamDistribution/StreamRepositoryVersionAssertionIntegrationTests.cs`.**
Two `StreamDistributionDbContext` instances **with** `AggregateVersionInterceptor` (mirror
`AggregateVersionConflictIntegrationTests.VersionedContextAsync`); `StreamRepository` built with
a local `ITransactionalCommit` double whose `CommitAsync` calls `dbContext.SaveChangesAsync`, and a no-op
`IDomainEventDispatcher` (local private doubles, as `EventRepositoryOutboxTests` does). Seed a row
with `Stream.Provision`.

| # | Fact | Assert | Red today because |
|---|---|---|---|
| IT-1 | `The_assertion_does_not_match_after_another_context_retires_the_row` | A loads; B retires + saves; `A.IsUnchangedSinceLoadAsync` false; `A.ReadCommittedStateAsync` = Retired | members do not exist (compile error, spec 302 precedent) |
| IT-2 | `The_assertion_matches_and_leaves_the_version_unchanged_when_nothing_else_wrote` | true; a fresh read's `Version` equals the loaded one | same |

### 6.3 The race and its counterfactuals — `test-adversary`

**New: `tests/Integration.Tests/StreamDistribution/ProvisionRetireRaceIntegrationTests.cs`.**
Same context/repository construction as §6.2 (duplicate the small private builders; do not share a
file with the test-writer).

| # | Fact | Mechanism | Proves | Counterfactual that must turn it red |
|---|---|---|---|---|
| IT-3 | `A_retire_that_read_the_row_before_the_assertion_still_commits` | A and B load at the same version; A asserts (true); B retires + `SaveChangesAsync` | `Should.NotThrowAsync` — the retire never loses to the provision | implement the check as a tracked save (interceptor bumps) → B throws `DbUpdateConcurrencyException` |
| IT-4 | `The_assertion_waits_for_an_uncommitted_retire_and_then_does_not_match` | B: `BeginTransactionAsync`, retire, `SaveChangesAsync` (uncommitted). Start A's assertion as a task; assert it has **not** completed after a bounded wait (e.g. `Task.WhenAny(assertion, Task.Delay(1s))` returns the delay); commit B; await → false | the check serialises behind an open retire transaction (spec §9 A1) | implement the check as an `AsNoTracking` version read → completes at once, returns true |
| IT-5 | `A_retire_inside_the_provision_window_leaves_MediaMTX_without_the_path` | Real `ProvisionStreamCommandHandler` on context A + real `MediaMtxRtspGateway` wrapped by a decorator whose `AddPathAsync` first runs a real `RetireStreamCommandHandler` on context B (own repository, real gateway), then delegates. Existing-row variant (row seeded, path added) | MediaMTX answers 404 for the path; row `Retired`; provision `Success` | decide on tracked `stream.State` instead of `ReadCommittedStateAsync` → path present (the hole §6.1's fake cannot see) |
| IT-6 | same as IT-5, insert variant (no seeded row) | as IT-5 | as IT-5 | as IT-5 |

The bounded wait in IT-4 is the one place time appears; it bounds a negative ("has not completed"),
never orders events, and its failure mode is a false *pass* only if the machine stalls for the whole
interval — state that in the fact's doc comment. test-adversary also tries, and reports in words if
not feasible, one further adversarial angle of its own choosing (e.g. two concurrent provisions
plus one retire).

Each counterfactual is **run** (applied locally, observed red, reverted) and the red output quoted —
memory: *prove a guard by counterfactual*. They are not committed.

### 6.4 Characterisation (green before and after, unmodified)

All existing facts in `ProvisionStreamCommandHandlerTests`, `RetireStreamCommandHandlerTests`,
`CameraRegisteredIntegrationEventHandlerTests`, `ReportStreamHealthCommandHandlerTests`,
`RepointStreamCommandHandlerTests`; Integration: `ProvisionStreamIntegrationTests`,
`RetireStreamIntegrationTests`, `MediaMtxReconcilerIntegrationTests`,
`MediaMtxRtspGatewayIntegrationTests`, `StreamHealthTransitionTests`, `WhepAuthIntegrationTests`,
`ServiceDefaults.Tests` `AggregateVersionInterceptorTests`.

### 6.5 Shard filters

Two new Integration.Tests classes → entries in `tests/Integration.Tests/ci-shards/` (memory: *new
test classes need a shard-filter entry*). Put both in `shard-2.filter` beside
`RetireStreamIntegrationTests` and `MediaMtxReconcilerIntegrationTests` unless the shard README's
balancing rule says otherwise. One task owns the file.

## 7. Commit plan (each builds on its own — ADR-0087)

1. `test(2743): …` — 4a tests (both agents' files + fake + shard entries). Builds: the Application
   fake adds methods the interface does not yet declare (compiles); the two integration files
   reference members that do not exist — **so commit 1 does not build Integration.Tests.** To keep
   every commit building, commit 1 carries the Application tests + fake only; the integration
   files land in commit 2 with the members they call.
2. `feat(2743): assert the stream row's version after registering its path` — §3 + §4.1 + §5 +
   §6.2/§6.3 files + shard entries.
3. `feat(2743): a redelivery for a retired stream removes its leftover path` — §4.2 (US2).
4. `docs(2743): …` — tasks.md ticks / verification.

The integration red (compile errors naming the missing members) is captured in 4a against the
working tree before commit 1, and quoted in the PR body.

## 8. Risks

As spec §9. Plus: **P-1** a reviewer may judge the non-bumping assertion an ADR-0113 amendment
(spec §5) — if so the lane stops; nothing in commits 1-3 needs reverting to change course, since
no shared mechanism is introduced.
