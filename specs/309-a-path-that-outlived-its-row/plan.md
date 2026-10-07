# Plan 309: A path that outlived its row

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2658

## 1. Where this lives

- **Bounded context:** StreamDistribution only. No `Shared.Contracts` change, no new integration
  event, no cross-context reference, no migration, no AppHost resource.
- **Layers touched:** Application (`ProvisionStreamCommandHandler`), Domain (`IRtspGateway` XML doc
  only — the signature is unchanged), Infrastructure (`MediaMtxRtspGateway.AddPathAsync`,
  `StreamDistributionInfrastructureModule` comment).
- **Not touched:** `AuthorizeWhepCommandHandler`, `WhepAuthIntegrationTests`, `MediaMtxReconciler`,
  `RetireStreamCommandHandler`, `Stream` aggregate, `ProvisionStreamErrors`.
- **Latency:** N/A (spec §8).
- **ADRs:** 0161 (records the gap and names this closure), 0143 (retry opt-in for this client),
  0088 (outbox redelivery is the retry mechanism), 0047/0089 (`Result` + `ApiError`), 0049
  (`CancellationToken` last), 0105 (`Ensure.That`), 0141 (`Option<T>` for the repository lookup —
  existing).

## 2. Domain / invariants

No new entity or value object. The invariant this restores:

> **Every `cam-*` path MediaMTX holds was added after a `streams` row for it was durable.**

Consequently every path the WHEP hook can be asked about either has a row (and ADR-0161's fab check
applies) or was never added by the product (fabricated — MediaMTX 404s it, ADR-0161's original
reasoning holds).

## 3. `MediaMtxRtspGateway.AddPathAsync` (FR-002)

New contract, documented on `IRtspGateway.AddPathAsync`: *idempotent on the path name — a path
MediaMTX already has is success.*

Implementation shape (mirrors `RemovePathAsync`'s "404 is fine" special case):

```text
POST /v3/config/paths/add/{name}
  2xx                         -> log RegisteredMediaMtxPath; return
  400                         -> GET /v3/config/paths/get/{name}
                                   200 -> log "path already registered" (new [LoggerMessage],
                                          Information, in Infrastructure/Log.cs); return
                                   else -> throw the original response's HttpRequestException
  any other non-success       -> EnsureSuccessStatusCode() (throws, as today)
```

**Why confirm by `GET` rather than match the error text:** MediaMTX answers `400` for several
`add/` faults (bad source, bad config) and only the error string distinguishes them. A string
match is coupled to wording that a MediaMTX bump may change silently; a `GET` of the path's config
asks the question directly and fails safe (anything but 200 rethrows). It costs one extra request
only on the conflict branch. The "throw the original" path must preserve today's exception type
(`HttpRequestException` from `EnsureSuccessStatusCode` on the *add* response) so the handler's
catch is unchanged.

No new public method on `IRtspGateway`; the fakes (`FakeRtspGateway`, the private gateway in
`StreamHealthWatcherScopeTests`) already overwrite on re-add, i.e. already idempotent.

Side-effect on the reconciler: its re-add loop now tolerates a path that appeared between list and
add. No reconciler code change.

## 4. `ProvisionStreamCommandHandler` (FR-001, FR-003, FR-004)

Target step order, mirroring `RetireStreamCommandHandler` (save, then touch MediaMTX; a gateway
failure after the save is unfinished work the outbox redelivers):

```text
validate source URL                          (unchanged)
existing = GetByCameraAsync(camera)
if existing:
    if existing.State == Retired:
        log StreamAlreadyExists; return Success(existing.Id)        (unchanged behaviour)
    log StreamAlreadyExists
    return await RegisterPathAsync(existing)                        (FR-004 — re-assert)
stream = Stream.Provision(...)
streams.Add(stream)
await streams.SaveAsync(ct)                                         (FR-001 — moved up)
return await RegisterPathAsync(stream), logging ProvisionedStream on success

RegisterPathAsync(stream):
    try   AddPathAsync(stream.Path, stream.SourceUrl.Value, ct)
    catch HttpRequestException ex:
        log PathRegistrationFailed(ex, camera)
        return Failure(RtspGatewayUnavailable(ex.Message))          (FR-003)
    return Success(stream.Id)
```

Notes for the engineer:

- Pass `stream.SourceUrl.Value`, not the raw `rtspSourceUrl` primitive, in both branches — the
  existing-row branch has no other source, and one call site keeps one spelling.
- Replace the existing save/register comment with one *why* comment in the style of
  `RetireStreamCommandHandler`'s: saved first so a failed save cannot strand a live path with no row
  behind it (which the WHEP hook admits for any fab); a failed add after the save is unfinished work
  the outbox redelivers, and the existing-row branch finishes it.
- A private helper keeps `HandleAsync` under ADR-0084's advisory 30-LOC method limit.
- A `SaveAsync` exception is **not** caught (no drive-by error handling); it propagates to Wolverine
  exactly as today. That includes a unique-constraint violation from a concurrent delivery for the
  same camera — which now strands nothing, because the add has not happened.
- `ProvisionedStream` is logged only once the path is registered, so the log line keeps meaning
  "fully provisioned".

## 5. `StreamDistributionInfrastructureModule` comment

The `RetryEveryMethod()` justification says `add/` "answers 4xx if it already exists, which is not
retried anyway". After FR-002, rewrite that clause: `add/` is idempotent in fact — an existing path
is treated as success by the gateway, so a retry after a lost response lands in the same place.
Comment-only; no behaviour on that line changes.

## 6. Tests

### 6.1 Unit — `tests/StreamDistribution.Application.Tests/Commands/ProvisionStreamCommandHandlerTests.cs`

Fake change (test-support, owned by test-writer): `InMemoryStreamRepository` gains
`public Action OnSave { get; set; } = () => { };` invoked at the start of `SaveAsync`, before
pending adds are committed — so a throwing hook models a failed save (row not persisted).

New facts (red on `0d1864f5`):

| Fact | Arrange | Assert | Red today because |
|---|---|---|---|
| `Provision_when_the_save_fails_registers_no_MediaMTX_path` | `OnSave` throws `InvalidOperationException` | `HandleAsync` throws; `gateway.AddCalls` empty | add happens before save |
| `Provision_saves_the_stream_before_registering_its_path` | `OnAddPath` captures `streams.Streams.Count` | captured count is 1 | count is 0 at add time |
| `A_redelivery_after_a_failed_path_registration_registers_the_path` | 1st call: `OnAddPath` throws `HttpRequestException`; reset hook; 2nd call | after 1st: `RtspGatewayUnavailable` **and** `Streams.Count == 1`; after 2nd: success whose id equals that row's id, `AddCalls.Count == 1`, `Streams.Count == 1` | today the 1st call saves nothing (`Streams.Count == 0`). It also fails a reorder that skips FR-004: the 2nd call would short-circuit with `AddCalls` empty |
| `Provision_for_a_retired_stream_does_not_re_register_its_path` | provision, then `Retire(clock)` + save the existing row; 2nd call | success with that id; `AddCalls.Count == 1` | **green today** — guard for FR-004's retired exclusion; list it as characterisation in the 4a output |

Declared edits: spec §6 items 1-2.

### 6.2 Integration — new `tests/Integration.Tests/StreamDistribution/MediaMtxRtspGatewayIntegrationTests.cs`

Against real MediaMTX (`[Collection(AspireCollection.Name)]`, `ResetMediaMtxAsync` in
`InitializeAsync`; build the gateway as `new MediaMtxRtspGateway(aspire.App.CreateHttpClient("mediamtx", "api"), NullLogger<MediaMtxRtspGateway>.Instance)`
— the `MediaMtxReconcilerIntegrationTests` pattern):

- `Adding_a_path_that_already_exists_succeeds` — add `cam-<guid>` twice through the gateway; second
  call does not throw; `/v3/config/paths/list` contains the name exactly once. **Red today**
  (`HttpRequestException`, 400).
- `Adding_a_path_MediaMTX_rejects_for_another_reason_still_throws` — a fresh `cam-<guid>` with a
  source MediaMTX refuses (e.g. `"not-a-source"`), `Should.ThrowAsync<HttpRequestException>`; the
  path does not exist afterwards. **Green today** — it is the counterfactual proving FR-002 does not
  swallow every 400 (memory: *prove a guard by counterfactual*). If MediaMTX 1.21.0 accepts that
  source, test-writer picks another rejected input and records which in the 4a output.

**Shard filter:** add `FullyQualifiedName~SmartSentinelEye.Integration.Tests.StreamDistribution.MediaMtxRtspGatewayIntegrationTests.`
to `tests/Integration.Tests/ci-shards/shard-2.filter` (next to `MediaMtxReconcilerIntegrationTests`).
A missing entry fails CI deterministically.

### 6.3 Not added

No end-to-end WHEP test of the orphan: inducing a Postgres failure between two statements inside a
running service is not something the Aspire fixture can do (ADR-0103), and the unit fact in §6.1
row 1 is the exact property (no add without a durable row). Stated so a reviewer does not read the
absence as an omission.

## 7. Sequencing and commits (ADR-0030; each builds and passes alone)

1. `test(2658): red-first tests for save-before-register provisioning` — §6 (fake hook, new facts,
   new integration class, shard entry). Declared assertion edits land in commit 3, with the behaviour
   they describe, so this commit's suite is red only on the new facts.
2. `fix(2658): treat an existing MediaMTX path as success in AddPathAsync` — §3 + §5 + `IRtspGateway`
   doc. Must precede 3: after FR-004 the existing `ProvisionStreamIntegrationTests` redelivery fact
   calls `add/` on an existing path against real MediaMTX.
3. `fix(2658): save the stream before registering its MediaMTX path` — §4 + declared edits.

Phase 4a (test-writer) owns commit 1; phase 4b (backend-engineer) owns 2-3 and may not edit tests
beyond the two declared edits.

## 8. Engineers

`test-writer` (4a) then `backend-engineer` (4b). **No frontend, no infra** — the module change in §5
is a comment inside StreamDistribution's own Infrastructure project, not AppHost/CI.

## 9. Risks

- A1 / A2 / A3 per spec §9.
- **Integration flake budget:** the new class shares MediaMTX with the reconciler/provision classes
  under `AspireCollection` (serialised), and resets MediaMTX on init — no new contention.
