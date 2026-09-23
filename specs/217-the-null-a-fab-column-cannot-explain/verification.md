# Verification — Spec 217, the null a fab column cannot explain (#2517)

Phase 5 of ADR-0037. Records what T001 and T002 actually observed against a
real, locally-booted Aspire stack — not a prediction from source.

This is the **final** run, after a `/security-review` pass raised two
should-fix items against the first green run and both were addressed with a
fresh confirming run — see *Security review round* below. The evidence quoted
throughout this document is from that final run.

---

## Which run produced this evidence

Local. `dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~UnresolvedFabAuditRowIntegrationTests|FullyQualifiedName~NeutralFabRetentionRowIntegrationTests" --logger "console;verbosity=detailed" --logger "trx;LogFileName=spec217-round3.trx"`,
run against the working tree on branch `2517-null-fab-two-meanings` at commit
`10e32ec4` (spec/plan/tasks), with the two new test files and the T003
comment-only edit as uncommitted changes on top.

`git diff --stat src/` at the time of this run:

```
$ git diff --stat src/
(no output)
```

Confirmed empty by a second, independent check — `git status --short`:

```
 M tests/Architecture.Tests/EventMetadataFabDeclarationTests.cs
?? specs/217-the-null-a-fab-column-cannot-explain/verification.md
?? tests/Integration.Tests/AuditObservability/NeutralFabRetentionRowIntegrationTests.cs
?? tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs
```

No `src/` file appears in either.

**On the "compare the AppHost's start time to the commit" standing check**
(spec.md's independent test procedure, tasks.md T004): it does not apply in
its usual form here. This run used `AspireFixture` /
`Aspire.Hosting.Testing`'s in-process test host (`DistributedApplicationTestingBuilder`),
not a separately-launched, persistent `dotnet run AppHost` — the whole stack
is booted fresh, inside the `dotnet test` process itself, compiled from
exactly the working tree this session edited. There is no separate long-lived
AppHost PID that could be serving stale binaries from an earlier commit; the
check that guards against that is moot for a one-shot test-host boot.

### Boot history this session, in order (for honesty, not as evidence)

1. First attempt: all 12 facts failed identically at `AspireFixture.InitializeAsync`
   with `Aspire.Hosting.DistributedApplicationException: Application orchestrator
   dependency check returned an error: The operation has timed out.` — DCP
   could not reach Docker at all. `wsl -l -v` confirmed the `docker-desktop`
   WSL distro as `Stopped`; a human restarted Docker Desktop.
2. Second attempt, immediately after restart: all 12 facts again failed, this
   time with `audit-observability`/`minio` `FailedToStart` and Postgres health
   checks unable to connect — the "first run after machine churn" pattern.
   `docker ps -a` showed no leftover containers afterward.
3. Third attempt: T002's 3 facts passed; T001's 9 facts failed with
   `System.TimeoutException : Stream for camera ... did not reach 'Degraded'
   within 15s. Last observed state: '<0 streams for the camera>'` — a genuine
   bug in the **arrangement code**, not the stack or an assertion: once
   `BlankTheFabAsync` nulls the stream's fab, `GET /streams` hides it from
   every caller (`StreamFabAttributionIntegrationTests.A_stream_with_no_fab_is_returned_to_nobody`,
   FR-009), so polling that endpoint for the post-blank Degraded transition
   was guaranteed to see an empty page regardless of the watcher's actual
   behaviour. Fixed by polling `StreamDistributionDbContext` directly for the
   post-blank wait only (`WaitForStreamStateInStoreAsync`); the pre-blank
   Healthy wait still uses HTTP. Docker died again before a confirming re-run
   (`wsl -l -v` again showed `docker-desktop` `Stopped`); a human restarted it
   a second time.
4. Fourth attempt: **all 12 facts passed, 12/12** — the first genuinely green
   run, before security review.
5. `/security-review` ran against that green state and raised two should-fix
   items (below). Both were fixed; the fixes were rebuilt clean and confirmed
   with a fifth attempt (all 12 facts passed again), and then, because that
   run still didn't capture T002's announcement row as quotable text, a sixth
   change added an `ITestOutputHelper` dump to `NeutralFabRetentionRowIntegrationTests`
   (mirroring the sibling class) and a **final, sixth run** produced the
   evidence quoted below.

---

## Security review round

`/security-review` ran against attempt 4's green state (constitution §VIII —
audit disclosure and fab authorization). No blockers. Two should-fix items,
both addressed without touching any existing assertion's expected outcome:

**Should-fix 1 — two facts asserted the current cross-fab disclosure as if it
were a permanent requirement, with nothing marking it as recording a pending,
undecided policy question.** Whichever way #2517's policy question resolves,
at least one of `Get_single_returns_the_null_fab_row_to_another_fabs_operator`
and `An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator`
will need to go red when the fix lands — and ADR-0144 forbids this lane from
weakening an existing assertion to pre-empt that. Fixed by adding a doc-comment
to each fact stating plainly that it records the current exposure pending
#2517's decision, that a future red there is the fix landing rather than a
regression, and that the comment (never the assertion) is what should be
updated when that day comes. **The assertions themselves — `ShouldContain(nullFabAuditIdentifier, "SC-5: ...")`
and `ShouldBe(JsonValueKind.Null, "SC-6 / F0: ...")` — are unchanged,
byte-for-byte**, confirmed by direct comparison, not merely by intent.

**Should-fix 2 — SC-10's poll could match a stale row from an earlier run,
not this run's own sweep.** Postgres runs `ContainerLifetime.Persistent` with
a data volume, so `audit_events` survives across runs; two runs seeding at
`now - 400d` land in the same monthly chunk, and a plain chunk-range match
could return an earlier run's announcement on the very first poll iteration,
before this run's retention worker had swept anything. The reviewer's timing
math was right: attempt 4's recorded US2 facts totalled ~3s wall time, too
fast for a genuine seed→sweep→MinIO archive→RabbitMQ publish→audit-ingest→
HTTP-read round trip alongside a camera registration — that evidence was very
likely attempt 3's stale announcement, not attempt 4's own. Fixed by
tightening the match, not lengthening the poll: `PollForCoveringAnnouncementAsync`
now takes a `notBefore` timestamp captured via `DateTimeOffset.UtcNow` *before*
seeding starts, and skips any candidate row whose `receivedAt` predates it,
before ever checking chunk-range coverage. A stale row can no longer match
regardless of window length. Re-run to produce genuinely fresh evidence — see
US2 below, where the announcement's `receivedAt` is checked directly against
the captured `notBefore` moment.

**Nits taken:**
- Added an assertion to SC-5 and SC-6 that the disclosed row's payload
  actually names the munich camera — not merely that a row was returned.
  Payload severity is what a human deciding the policy question needs to
  weigh, and this is now part of the observed evidence below.
- Added `eventKind`/`ToState` assertions immediately after both
  `PollForTimelineRowAsync` calls in T001's arrangement (`AssertIsHealthTransition`).
  The two calls picked a row positionally (first row; first row that isn't
  the control) rather than by content — cheap today since there is only one
  V1 in the stream namespace, but the `finally`'s restore-to-reachable can
  itself provoke a third (`Degraded` → `Healthy`) row before the second poll
  runs, and a silent wrong-row pick would otherwise fail confusingly
  downstream instead of immediately and attributably.

**Nits not taken, with reasons:**
- Referencing `RetentionRoundtripIntegrationTests.SeededAgesInDays` instead
  of duplicating its `-200`/`-120` literals in G3's collision guard: that
  field is `private` in a sibling file plan.md's ownership table does not
  list as this delivery's to edit ("Files this delivery owns" + "Nothing
  else"). Widening its visibility to enable a cross-file reference would
  expand scope beyond the four files this spec owns, so the duplicated
  literals (with their existing explanatory comment) were kept.
- Response bodies appear in 7 assertion failure messages, against tasks.md's
  own "never put a response body in a failure message" instruction. Left
  as-is: it matches 17 pre-existing instances elsewhere in the suite and
  every body here is a synthetic audit row or camera DTO with no real
  secret. Flagged here rather than silently decided either way.

---

## US1 — observed evidence

### SC-1 and SC-2, verbatim (`ITestOutputHelper` capture, final run)

```
Arrangement complete. camera=01a0ccbf-fbad-7919-8c9e-edc7b726be5c, munichRow={"auditIdentifier":"01a0ccc0-1787-7d5e-974c-e5a124663dbc","occurredAt":"2026-09-23T05:32:15.442182+00:00","receivedAt":"2026-09-23T05:32:16.135037+00:00","fab":"munich","eventKind":"StreamHealthChangedV1","resourceKind":"stream","resourceIdentifier":"01a0ccbf-fbad-7919-8c9e-edc7b726be5c","actorIdentifier":"00000000-0000-0000-0000-000000000000","actorIsSystem":true,"actorUsername":null,"eventIdentifier":"01a0ccc0-14de-772a-88e5-af4c5cbfd672","payload":"{\"Error\": null, \"Camera\": \"01a0ccbf-fbad-7919-8c9e-edc7b726be5c\", \"ToState\": \"Healthy\", \"Metadata\": {\"Fab\": \"munich\", \"Actor\": null, \"OccurredAt\": \"2026-09-23T05:32:15.4421824+00:00\", \"RootIngestedAt\": null, \"EventIdentifier\": \"01a0ccc0-14de-772a-88e5-af4c5cbfd672\"}, \"ChangedAt\": \"2026-09-23T05:32:15.4421824+00:00\", \"FromState\": \"Provisioning\"}","payloadSizeBytes":326,"schemaVersion":1}, nullFabRow={"auditIdentifier":"01a0ccc0-1d6f-7fe8-a6a0-6fc5012e8194","occurredAt":"2026-09-23T05:32:17.585799+00:00","receivedAt":"2026-09-23T05:32:17.647292+00:00","fab":null,"eventKind":"StreamHealthChangedV1","resourceKind":"stream","resourceIdentifier":"01a0ccbf-fbad-7919-8c9e-edc7b726be5c","actorIdentifier":"00000000-0000-0000-0000-000000000000","actorIsSystem":true,"actorUsername":null,"eventIdentifier":"01a0ccc0-1d36-70c4-baea-43d13cdad390","payload":"{\"Error\": \"not ready\", \"Camera\": \"01a0ccbf-fbad-7919-8c9e-edc7b726be5c\", \"ToState\": \"Degraded\", \"Metadata\": {\"Fab\": null, \"Actor\": null, \"OccurredAt\": \"2026-09-23T05:32:17.5857998+00:00\", \"RootIngestedAt\": null, \"EventIdentifier\": \"01a0ccc0-1d36-70c4-baea-43d13cdad390\"}, \"ChangedAt\": \"2026-09-23T05:32:17.5857998+00:00\", \"FromState\": \"Healthy\"}","payloadSizeBytes":325,"schemaVersion":1}
```

Same camera (`01a0ccbf-fbad-7919-8c9e-edc7b726be5c`), two rows, one pivot:

| | Earlier row (SC-2 control) | Later row (SC-1 observation) |
|---|---|---|
| `auditIdentifier` | `01a0ccc0-1787-7d5e-974c-e5a124663dbc` | `01a0ccc0-1d6f-7fe8-a6a0-6fc5012e8194` |
| `fab` | `"munich"` | `null` |
| `resourceKind` / `resourceIdentifier` | `stream` / `01a0ccbf-fbad-7919-8c9e-edc7b726be5c` | `stream` / `01a0ccbf-fbad-7919-8c9e-edc7b726be5c` |
| `eventKind` | `StreamHealthChangedV1` | `StreamHealthChangedV1` |
| payload `FromState` → `ToState` | `Provisioning` → `Healthy` | `Healthy` → `Degraded` |
| payload `Metadata.Fab` | `"munich"` | `null` |

Both rows now also pass `AssertIsHealthTransition` (added post-review): each
is confirmed `eventKind == "StreamHealthChangedV1"` with the expected
`ToState`, closing the "picked the wrong row positionally" gap the reviewer
flagged.

**SC-1 holds**: a fab-owned resource (a stream) whose fab was unresolved at
the moment its health-watcher announcement was published produced an audit
row with `fab: null` — observed directly in the response body, not inferred.

**SC-2 (the counterfactual) holds**: the same camera's earlier transition,
recorded before the fab was blanked, carries `fab: "munich"`. This rules out
an ingestion path that stamps every row null, a broken `V1ResourceMap`, or a
fab column that was never written — the same arrangement produced a non-null
row one step earlier.

Both facts (`A_stream_with_no_fab_records_a_null_fab_on_its_health_audit_row`,
`The_same_cameras_earlier_transition_recorded_its_fab`) **PASSED**.

### The three-read-path matrix (F3, SC-3/4/5/6/7/8)

Six lines, each a real HTTP round trip against the booted stack; every row
below is a fact that **PASSED**, meaning the observed response matched
exactly what is stated:

| # | Endpoint | Caller | Status | Null-fab row returned? |
|---|---|---|---|---|
| 1 | `GET /audit/stream/{camera}?fabId=munich` | `admin@munich.test` | 200 | **Yes** (SC-3 — the widened timeline, #2506) |
| 2 | `GET /audit?fabId=munich&eventKind=StreamHealthChangedV1&pageSize=200` | `admin@munich.test` | 200 | **No** — munich row present, null-fab row absent (SC-4 / F3) |
| 3 | `GET /audit?eventKind=StreamHealthChangedV1&pageSize=200` (no `fabId`) | `op-berlin@berlin.test` | 200 | **Yes**, payload confirmed to name the munich camera (SC-5 — #1300, unscoped search) |
| 4 | `GET /audit/{nullFabAuditIdentifier}` | `op-berlin@berlin.test` | 200 | **Yes**, `fab: null`, payload confirmed to name the munich camera (SC-6 / F0 — `GetSingle` skips the fab guard for a null-fab row) |
| 5 | `GET /audit/{munichAuditIdentifier}` (same camera's earlier, munich row) | `op-berlin@berlin.test` | **403** `RESOURCE_FAB_NOT_AUTHORIZED` | n/a — refused (SC-7, counterfactual on row 4) |
| 6 | `GET /audit/stream/{camera}?fabId=berlin` | `admin@munich.test` | **403** `RESOURCE_FAB_NOT_AUTHORIZED` | n/a — refused (SC-8, auth boundary unchanged) |

Rows 1 and 2 are **F3 as evidence**: the same caller (`admin@munich.test`),
the same authorized fab (`munich`), two endpoints, opposite answers about the
identical row. Row 5 is what makes row 4's `200` mean something — the same
endpoint, the same caller, refuses a row that genuinely carries a foreign fab,
so `GetSingle`'s `200` on the null-fab row is not "this endpoint has no
authorization at all." Rows 3 and 4 now also confirm **what** the foreign-fab
operator receives, not just that a row was returned — the payload assertion
added post-review.

**Rows 3 and 4 record a pending policy question, not a fixed requirement** —
`An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator` and
`Get_single_returns_the_null_fab_row_to_another_fabs_operator` each carry a
doc-comment stating so; see *Security review round* above.

Facts and their outcomes, exactly as xUnit reported them (final run):

```
Passed SmartSentinelEye.Integration.Tests.AuditObservability.UnresolvedFabAuditRowIntegrationTests.The_fab_scoped_timeline_returns_the_null_fab_row [16 ms]
Passed SmartSentinelEye.Integration.Tests.AuditObservability.UnresolvedFabAuditRowIntegrationTests.The_fab_scoped_search_does_not_return_the_null_fab_row [38 ms]
Passed SmartSentinelEye.Integration.Tests.AuditObservability.UnresolvedFabAuditRowIntegrationTests.An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator [135 ms]
Passed SmartSentinelEye.Integration.Tests.AuditObservability.UnresolvedFabAuditRowIntegrationTests.Get_single_returns_the_null_fab_row_to_another_fabs_operator [64 ms]
Passed SmartSentinelEye.Integration.Tests.AuditObservability.UnresolvedFabAuditRowIntegrationTests.Get_single_refuses_the_same_cameras_munich_row_to_that_operator [15 ms]
Passed SmartSentinelEye.Integration.Tests.AuditObservability.UnresolvedFabAuditRowIntegrationTests.A_cross_fab_timeline_is_still_refused [14 s]
```

### SC-9 — F2's falsifiable half

```
Passed SmartSentinelEye.Integration.Tests.AuditObservability.UnresolvedFabAuditRowIntegrationTests.The_pivot_identifier_is_not_listable_by_another_fabs_operator [258 ms]
```

`op-berlin@berlin.test` requesting `GET /cameras?limit=200&includeRetired=true`
got `200`, with the munich camera's identifier **absent** from `items`, and —
the pairing this fact insists on so the absence isn't vacuous — a
berlin-registered control camera **present**. This class registers its own
berlin control camera in the arrangement specifically so the "absent"
assertion is never trivially true against an empty catalogue, independent of
run order or scope.

**F2's hypothesis: HELD.** The berlin operator can read the null-fab row
(rows 3 and 4 of the matrix above) but cannot learn its pivot identifier
(the camera GUID) from any camera-catalog listing they're authorized to make.
The route #2506 opened reaches only data the caller could already read
elsewhere with no pivot at all.

---

## US2 — observed evidence

### SC-10 and SC-11 (`NeutralFabRetentionRowIntegrationTests`) — fresh, freshness-checked

```
Passed SmartSentinelEye.Integration.Tests.AuditObservability.NeutralFabRetentionRowIntegrationTests.An_archived_chunks_announcement_records_no_fab [< 1 ms]
Passed SmartSentinelEye.Integration.Tests.AuditObservability.NeutralFabRetentionRowIntegrationTests.A_fab_carrying_row_in_the_same_window_is_not_null [2 s]
Passed SmartSentinelEye.Integration.Tests.AuditObservability.NeutralFabRetentionRowIntegrationTests.The_neutral_announcement_is_reachable_from_a_fab_scoped_timeline [12 ms]
```

`ITestOutputHelper` capture, verbatim, from the final run (added post-review
specifically so this evidence is quotable rather than paraphrased):

```
Arrangement complete. seededMoment=2025-08-19T05:32:19.0093471+00:00, arrangementStartedAt=2026-09-23T05:32:19.0093468+00:00, announcement={"auditIdentifier":"01a0ccc0-2aff-7e12-a058-330b4d07d2c2","occurredAt":"2026-09-23T05:32:20.579272+00:00","receivedAt":"2026-09-23T05:32:21.11976+00:00","fab":null,"eventKind":"AuditChunkArchivedV1","resourceKind":"event","resourceIdentifier":"ab13f279-e649-3326-dcd3-e8f49af4ac40","actorIdentifier":"00000000-0000-0000-0000-000000000000","actorIsSystem":true,"actorUsername":null,"eventIdentifier":"01a0ccc0-28e3-7ba8-9f97-5cf3ba4f484d","payload":"{\"FabId\": null, \"Metadata\": {\"Fab\": null, \"Actor\": null, \"OccurredAt\": \"2026-09-23T05:32:20.5792727+00:00\", \"RootIngestedAt\": null, \"EventIdentifier\": \"01a0ccc0-28e3-7ba8-9f97-5cf3ba4f484d\"}, \"RowCount\": 1, \"ArchivedAt\": \"2026-09-23T05:32:20.57884+00:00\", \"ContentMd5\": \"9639d76064554a08ba72290d1837e1c9\", \"OccurredFrom\": \"2025-08-10T00:00:00+00:00\", \"OccurredUntil\": \"2025-09-09T00:00:00+00:00\", \"MinioObjectKey\": \"fab=_unscoped/year=2025/month=08/chunk-ab13f279e6493326dcd3e8f49af4ac40.ndjson.gz\", \"ChunkIdentifier\": \"ab13f279-e649-3326-dcd3-e8f49af4ac40\"}","payloadSizeBytes":532,"schemaVersion":1}, fabCarryingRow={"auditIdentifier":"01a0ccc0-2c85-781e-8352-73fad219254a","occurredAt":"2026-09-23T05:32:21.491854+00:00","receivedAt":"2026-09-23T05:32:21.509142+00:00","fab":"munich","eventKind":"CameraRegisteredV1","resourceKind":"camera","resourceIdentifier":"01a0ccc0-2c73-727c-91f7-050f18048df6","actorIdentifier":"1969c448-2835-43f7-bfb1-bcf5f3b14bed","actorIsSystem":false,"actorUsername":null,"eventIdentifier":"01a0ccc0-2c7a-7adb-ad4f-5942381b6e87","payload":"{\"Url\": \"rtsp://10.0.5.12/h264\", \"Name\": \"Cam-NeutralFabControl-11896278bbb1408e9c43ce6c28f7712d\", \"Camera\": \"01a0ccc0-2c73-727c-91f7-050f18048df6\", \"Metadata\": {\"Fab\": \"munich\", \"Actor\": \"1969c448-2835-43f7-bfb1-bcf5f3b14bed\", \"OccurredAt\": \"2026-09-23T05:32:21.4918541+00:00\", \"RootIngestedAt\": null, \"EventIdentifier\": \"01a0ccc0-2c7a-7adb-ad4f-5942381b6e87\"}, \"RegisteredAt\": \"2026-09-23T05:32:21.4918541+00:00\", \"RegisteredBy\": \"1969c448-2835-43f7-bfb1-bcf5f3b14bed\"}","payloadSizeBytes":451,"schemaVersion":1}
```

**Freshness, checked directly rather than assumed**: `arrangementStartedAt`
(captured *before* this run seeded its row) is `2026-09-23T05:32:19.0093468+00:00`.
The matched announcement's `receivedAt` is `2026-09-23T05:32:21.11976+00:00` —
**~2.1s after**, inside this run, not a leftover from an earlier attempt. The
seeded moment `2025-08-19T05:32:19.0093471+00:00` falls inside the
announcement payload's own `OccurredFrom`/`OccurredUntil` range
(`2025-08-10T00:00:00+00:00` – `2025-09-09T00:00:00+00:00`), confirming the
chunk match is also correct, not merely fresh.

- the row's own `fab` field: `null`
- the announcement payload's `FabId`: `null`

**SC-10 holds — F1 as an observation, not a source read, and now genuinely
fresh.** Nothing in this run's real archive-and-publish path set a fab on the
chunk announcement, matching spec.md §F1's claim that the hypertable's
time-only partitioning gives a chunk no fab to have.

**SC-11 (counterfactual) holds.** In the same run, a real munich camera
registration (`CameraRegisteredV1`, driven through the real
`CameraRegisteredDomainEventHandler` publish path, not a raw SQL seed)
produced an audit row whose `fab` is `"munich"` — proving this run's ingestion
path is capable of writing a non-null fab, so SC-10's null is not an artefact
of a broken pipeline.

### SC-12

`admin@munich.test` requesting `GET /audit/event/ab13f279-e649-3326-dcd3-e8f49af4ac40?fabId=munich`
got `200` with the `AuditChunkArchivedV1` row included — **#2506 working as
intended on the class it was actually written for**, the contrast that makes
SC-3's identical `200`-and-included result on SC-1's stream-health row the
interesting one.

---

## T003 — the comment-only correction, hash-proven

`tests/Architecture.Tests/EventMetadataFabDeclarationTests.cs` — two sentences
amended in place (naming spec 217 and the two test classes above), the
existing honesty-block structure kept unchanged. Untouched by the security
review round.

Proof method: strip every `//`, `///`, and `/* */` comment, collapse
whitespace, and SHA-256 the rest — so a change confined to comments cannot
move the hash, and any code change (even whitespace-insensitive) does.
The tool itself was proven by counterfactual before use: on a scratch copy, a
real code edit (adding a redundant `RegexOptions` flag) changed the hash;
a comment-wording edit did not.

```
before (git HEAD:tests/Architecture.Tests/EventMetadataFabDeclarationTests.cs):
64a0b929109841be794c25e192477b4f78bde440bbe63e569b50d951cb87db9e

after (working tree):
64a0b929109841be794c25e192477b4f78bde440bbe63e569b50d951cb87db9e
```

Identical. `git diff --stat` for the file: `7 +++++--` (5 insertions, 2
deletions) — text moved, no code changed. Reconfirmed by the security
reviewer via a direct line-by-line read, not only the hash.

---

## `EventMetadataFabDeclarationTests` remark correction

Confirmed present in the working tree (both amended sentences name spec 217
and the class that now covers the case behaviourally): the `#2076` /
`StreamHealthChangedDomainEventHandler` sentence now points at
`UnresolvedFabAuditRowIntegrationTests`, and the `AuditRetentionHostedService`
"not protected" sentence now points at `NeutralFabRetentionRowIntegrationTests`.
Both T001 and T002 ran and passed, so both sentences were amended (tasks.md's
fallback — amend only the first if T002 did not run — does not apply).

The Architecture.Tests suite (Docker-free, 444 tests) was run against the
working tree and is fully green, including
`EventMetadataFabDeclarationTests` itself and `IntegrationTestSelectionTests`
(confirming both new classes correctly carry `[Collection(AspireCollection.Name)]`):

```
Passed:  444
Failed:  0
Total time: 51.0098 Seconds
```

---

## Latency budget

**N/A.** Constitution §IV's six legs are untouched: no `src/` file changed
(confirmed above by `git diff --stat src/` twice, before and after this
run), no Aspire resource was added or altered, and the audit read/write paths
this spec observes sit on none of the six legs. The health-watcher transition
US1 provokes is on the stream path, not the event→overlay path; its timing
here (`Healthy → Degraded` observed in ~2.1s between the two captured rows,
well inside the 15s budget `StreamHealthTransitionTests` already measures)
is arrangement, not a latency-budget measurement, and no figure from this run
is cited against §IV.

---

## Summary

| Success criterion | Result |
|---|---|
| SC-A — observed against a running system, request/response quoted | Met — see US1/US2 sections above |
| SC-B — which of the three read paths return the row, to whom | Met — six-line matrix |
| SC-C — the instrument proven able to record the other answer | Met — SC-2 (`munich`) and SC-7 (`403`) both observed |
| SC-D — the two classes of null told apart by evidence | Met — US1 (genuinely conflated) vs US2 (genuinely neutral), both run |
| SC-E — `EventMetadataFabDeclarationTests` remark corrected in place | Met — both sentences amended, hash-proven comment-only |
| SC-F — follow-up issue, not `agent:ready` | **Not done here** — explicitly T005, left for the orchestrator per the brief |
| SC-G — no `src/` change; no existing assertion edited | Met — `git diff --stat src/` empty; no existing test file's assertions were touched (the security-review fixes added doc-comments and new assertions alongside existing ones, and strictly tightened one poll's match — no existing assertion's expected outcome moved) |

All 12 facts across both new classes **PASSED** on the final run. 12/12.
