# Plan 378: The reconciler repairs a MediaMTX path whose source has diverged

**Spec**: [spec.md](./spec.md). **Issue**: #2960. **Split out**: #2991 (aggregate rewrite; not here).

## 1. Context and layers

One bounded context: **StreamDistribution**. No contract, no other context, no new project
reference (constitution §III). Layers touched:

| Layer | File | Change |
|---|---|---|
| Domain | `src/StreamDistribution/Domain/Stream/IRtspGateway.cs` | + `ListConfiguredPathSourcesAsync`; `AddPathAsync` doc comment names the sweep as what repairs the unverified 400 branch (comment only) |
| Infrastructure | `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` | implement the read (paginated `config/paths/list`, `source` verbatim) |
| Infrastructure | `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxSourceRepair.cs` (new) | the repair pass (decision + writes), DB-free |
| Infrastructure | `src/StreamDistribution/Infrastructure/Reconciler/StreamSourceSnapshot.cs` (new) | EF reads the pass needs: non-retired `(Path, SourceUrl)`; committed source of one path |
| Infrastructure | `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxSourceRepairSweep.cs` (new) | `BackgroundService`: waits for `ApplicationStarted`, then `PeriodicTimer(Interval, TimeProvider)` |
| Infrastructure | `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxSourceRepairOptions.cs` (new) | `SectionName = "MediaMtxSourceRepair"`, `Interval` default `00:00:10`, validated 1 s to 4294967294 ms on start (ceiling: PeriodicTimer's range; floor: a shorter interval exhausts the shared MediaMTX control-API rate partition) |
| Infrastructure | `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxReconciler.cs` | `ReconcileOnceAsync` runs the repair pass as its last step; nothing else changes |
| Infrastructure | `src/StreamDistribution/Infrastructure/Log.cs` | three new `[LoggerMessage]`s (§5) |
| Infrastructure | `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs` | bind options (+`ValidateOnStart`) via `BindMediaMtxSourceRepair`, called at the top level of `AddStreamDistributionInfrastructure` right after `BindStreamFabAttribution` (not inside it); `.AddHostedService<MediaMtxSourceRepairSweep>()` after `MediaMtxReconciler` |
| AppHost | `src/AppHost/IntegrationLaneOverrides.cs` + `AppHost.cs:376` | `isE2ETests`-gated `MediaMtxSourceRepair__Interval=49.17:02:47.2940000` on `stream-distribution` |

No Application-layer change: this is an infrastructure reconcile loop over a port, like
`StreamHealthWatcher` and `MediaMtxReconciler`, and it writes MediaMTX only — never the aggregate.

## 2. Types and invariants

No new domain entity or value object. The reconcile owns no state between sweeps.

- **`IRtspGateway.ListConfiguredPathSourcesAsync(CancellationToken)` →
  `Task<IReadOnlyDictionary<MediaMtxPath, string>>`.** Canonical names only (`MediaMtxPath.IsCanonical`,
  same filter as `ListConfiguredPathsAsync`); value = the item's `source` string verbatim; an item
  whose `source` is missing or not a JSON string is omitted. Pages until `pageCount` is exhausted
  (absent `pageCount` = one page), mirroring `ListConfiguredPathsAsync`. The source is a `string`
  for the same reason `AddPathAsync`/`RepointPathAsync` take one: it is MediaMTX's vocabulary
  (`"publisher"`, `"rtsp://…"`), not necessarily a `StreamSourceUrl`, and parsing it into one would
  throw on exactly the divergent values the pass must see. It is never written anywhere (FR-004).
  `ListConfiguredPathsAsync` is left as is — merging the two would be a refactor mixed into a fix.
- **Invariant R1 (only the database's value is written).** Every `RepointPathAsync` the pass issues
  carries a `StreamSourceUrl.Value` read from the database in this pass.
- **Invariant R2 (ordinal equality).** `string.Equals(observed, persisted.Value, StringComparison.Ordinal)`.
  Spec §1 proves MediaMTX returns the written string verbatim, so a converged path stays converged.
- **Invariant R3 (bounded work).** Per diverged path: at most **two** PATCHes per pass (FR-003).

## 3. The repair pass (`MediaMtxSourceRepair`)

```
RepairAsync(gateway, snapshot, cancellationToken) -> repaired count
  observed  = gateway.ListConfiguredPathSourcesAsync()          // 1. MediaMTX first
  persisted = snapshot.ListLiveSourcesAsync()                   // 2. then the database
  for (path, source) in persisted where observed has path and observed[path] !=ord source.Value:
      try
          gateway.RepointPathAsync(path, source.Value)
          log Repaired(path, origin(observed[path]), origin(source.Value))
          committed = snapshot.ReadCommittedSourceAsync(path)   // Option<StreamSourceUrl>; None = retired/absent
          if committed is Some(c) and c.Value !=ord source.Value:
              gateway.RepointPathAsync(path, c.Value)           // once; never loops
              log Repaired(path, origin(source.Value), origin(c.Value))
          repaired++
      catch Exception ex when caller token not cancelled:
          log RepairFailed(ex, path)                            // FR-009; continue
```

**Why this order closes the in-flight repoint race** (a repoint = commit B, then PATCH B):

| Interleaving | Outcome |
|---|---|
| Repoint commits **and** patches between step 1 and step 2 | Step 2 reads B; pass writes B (harmless). Had the DB been read first it would have written the stale A — today's startup pass reads the DB first, which is why the pass does its own reads rather than reusing the startup snapshot. |
| Repoint commits after step 2; its PATCH lands **before** ours | Ours (A) clobbers B; the post-PATCH re-read sees B and writes B. |
| Repoint commits after step 2; its PATCH lands **after** ours | B wins on its own. The re-read may see B and write B again (harmless). |
| Repoint commits after our re-read | Its PATCH is sent after its commit, which is after our PATCH returned, so it lands last. |
| A **stale retry** (a timed-out first attempt applied late server-side) lands after any of these | Not detectable inside one pass; the next sweep repairs it. This is the owner's "no longer than one sweep" bound, and why the sweep must be periodic. |

The re-read compares the **source**, not `AggregateVersion`: the health watcher modifies the
aggregate (and so bumps its version) every few seconds, and a version check would re-patch on every
health change (FR-003).

`StreamSourceSnapshot` (EF, `AsNoTracking`): `ListLiveSourcesAsync` = `Streams.Where(State != Retired)
.Select(new { Path, SourceUrl })` (anonymous type, as `ReconcileOnceAsync` already does, because both
properties carry converters); `ReadCommittedSourceAsync(path)` = same filter by `Path`, `SingleOrDefault`,
mapped to `Option<StreamSourceUrl>` (ADR-0141). The pass depends on a small internal interface
`IStreamSourceSnapshot` so it can be unit-tested without a database (ADR-0103; the same separation
`StreamHealthWatcher.DispatchAsync` made, #1801). Not registered in DI — both callers construct it
over the `DbContext` they already create.

## 4. Hosting

- **Startup** — `MediaMtxReconciler.ReconcileOnceAsync`: after the re-add loop and the existing
  `ReconcilerStartupPassComplete` line, `await new MediaMtxSourceRepair(logger).RepairAsync(gateway,
  new StreamSourceSnapshot(context), cancellationToken)`. Summary line text unchanged (C7). A failure
  inside the repair is handled per path (FR-009); a failure of its MediaMTX list propagates to the
  existing `RunDeferredAsync` catch (`ReconcilerStartupPassFailed`), as any other startup-pass
  failure does today.
- **Periodic** — `MediaMtxSourceRepairSweep : BackgroundService` (`IServiceScopeFactory`,
  `IHostApplicationLifetime`, `IOptions<MediaMtxSourceRepairOptions>`, `TimeProvider`, `ILogger<>`).
  `ExecuteAsync`: await `ApplicationStarted` (a `TaskCompletionSource` registered on the token —
  ADR-0171: no gateway call before Kestrel binds); then `using PeriodicTimer timer = new(Interval,
  timeProvider)`; each tick creates a scope + `DbContext`, runs the pass, catches and logs any
  non-cancellation exception (`SourceRepairSweepFailed`) and continues. First tick is one interval
  after start — the startup pass already covered t=0. Pattern: `AuditRetentionHostedService`
  (`PeriodicTimer` + `TimeProvider` + options interval). `TimeProvider.System` is already registered
  (`AddClockAndMediaMtxAccessServices`).
- **Load.** One `config/paths/list` call per 100 paths per tick + one DB query; at 250 cameras and
  10 s: 18 control-API calls/min on the `api` partition (production ceiling 2000/min, ADR-0171).
  The probe-log demotion (`DemoteRoutineMediaMtxProbeLogs`) is keyed on the MediaMTX host, so the
  sweep's attempts already log at Debug.

## 5. Logging (FR-008, spec 373)

| Level | Template | Arguments |
|---|---|---|
| Information | `Reconciler repaired MediaMTX path {Path} source {ObservedSource} -> {PersistedSource}.` | `MediaMtxPath`, `UrlRedaction.ToOrigin(observed)`, `UrlRedaction.ToOrigin(persisted)` |
| Warning | `Reconciler failed to repair MediaMTX path {Path}; the next sweep retries.` | exception, `MediaMtxPath` |
| Warning | `MediaMtxSourceRepairSweep pass failed; the next tick retries.` | exception |

Two sources that differ only after the authority render the same origin on both sides
(`rtsp://10.0.8.1 -> rtsp://10.0.8.1`). That is the price of spec 373's rule and is accepted; the
path name identifies the stream and the full URL is in the database. `observed` may be a
non-URL MediaMTX value (`publisher`): `ToOrigin` renders a non-absolute input per spec 373 FR-002.

## 6. Messaging

None. No domain event, no integration event: the pass corrects the SFU to match a record that
already announced itself when it changed. (A repair is visible as a `Repaired` log line and, if the
source pull restarts, as the health watcher's own transitions.)

## 7. Boundary rules

- No reference outside StreamDistribution + `Shared.Kernel` (`UrlRedaction`, `Ensure`, `Option`).
- The pass never writes the aggregate, never adds or removes a path (FR-005).
- No new HTTP client, credential or resilience setting: the pass uses the injected `IRtspGateway`.

## 8. Tests (phase 4a: RED; adversarial pass required)

Existing assertions expected to change, and why — **only these**:

1. `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs`
   `ExpectedRegistrations`: three new lines (new registrations are the behaviour) —
   `"Singleton IConfigureOptions<MediaMtxSourceRepairOptions> -> instance of ConfigureNamedOptions<MediaMtxSourceRepairOptions>"`,
   `"Singleton IValidateOptions<MediaMtxSourceRepairOptions> -> instance of ValidateOptions<MediaMtxSourceRepairOptions>"`
   (both directly after the `StreamFabAttributionOptions` line), and
   `"Singleton IHostedService -> MediaMtxSourceRepairSweep"` (directly after `MediaMtxReconciler`).
   Assertion-red on unchanged code; runs locally (no Docker).
2. `tests/Integration.Tests/AppHostComposition/IntegrationFixture.snapshot.txt`: one new
   `EnvironmentAnnotation` on `stream-distribution` writing `MediaMtxSourceRepair__Interval="49.17:02:47.2940000"`,
   directly after the `WhepAuthorizeRateLimiting__Window` entry, with that resource's later
   annotation indices renumbered. `[Trait("Category","FixtureLogic")]`, in-memory, no Docker:
   assertion-red locally. No other lane's snapshot changes (gated `isE2ETests`).

**C7 is not expected to change.** Its in-file `RecordingGateway` gains the new interface member
(compile necessity) returning the four configured paths with the kept path's persisted source
`rtsp://10.0.5.2/h264` verbatim and the others arbitrary, **recording no event** — so the pass
finds nothing to repair and C7's event list, count and summary assertion stay byte-identical. Same
for `FakeRtspGateway`, `RecordingStreamDistributionPorts`, `StreamHealthWatcherScopeTests.StubGateway`
and `ProvisionRetireRaceIntegrationTests.WindowedGateway` (delegate to inner): member added, no
assertion touched.

New tests — see tasks.md for exact files and commands.

## 9. Risks

| Risk | Guard |
|---|---|
| Patch storm from a false mismatch | R2 + spec §1 proof; unit "converged path, two sweeps, zero PATCH"; integration "second pass sends nothing" (re-read the source after pass 2). |
| Sweep clobbers an in-flight repoint | §3 order + re-read; adversarial unit tests per row of §3's table. |
| Spec 099 / UnresolvedFab outage tests flake | Integration-lane interval 49.17:02:47.2940000, the largest PeriodicTimer accepts (spec §5); those classes unmodified and green in CI is the evidence. |
| A source MediaMTX refuses (e.g. `RTSP://` upper-case scheme, which `StreamSourceUrl` accepts and MediaMTX's case-sensitive prefix rejects) re-fails every sweep | Bounded: one Warning per such stream per tick. Such a stream also fails `add/`, so it cannot be live; recorded, not fixed here. |
| Stale write after a repair (accepted in writing) | A late-landing `RetryEveryMethod` attempt can leave the previous database value on a path for up to one interval; the next sweep repairs it. Bounded, short, and the price of not serialising repoints (the owner's decision on #2960). |
| A refused PATCH logs every interval (accepted; follow-up) | A source MediaMTX keeps refusing logs a Warning per sweep. Follow-up, not here. |
| Unbounded `pageCount` (accepted; follow-up) | `ListConfiguredPathSourcesAsync` follows `pageCount` as `ListConfiguredPathsAsync` does; the same pattern, the same follow-up. |
| Misleading log pair after Complete (accepted; documented in §4) | A repair-list failure at startup logs `ReconcilerStartupPassFailed` after `ReconcilerStartupPassComplete`. |
| Sweep runs before Kestrel binds | Unit test: no scope created before `ApplicationStarted` (mirror `MediaMtxReconcilerStartupOrderingTests`). |
| Full URL in a log | Unit: a source with `?token=SECRET` and a `/SECRET` path segment; assert exact rendered line and no `SECRET`. |
