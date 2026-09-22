# Tasks — Spec 217, the null a fab column cannot explain (#2517)

Phase 3 of ADR-0037. Read `spec.md` and `plan.md` first; this file assumes both.

Format: `[ID] [P?] [Story]` — `[P]` means the task owns files disjoint from
every other `[P]` task at the same step (ADR-0109).

**Phase-4a colour:** **CHARACTERISATION, OBSERVED GREEN**, with three mandatory
counterfactuals (SC-2, SC-7, SC-11 — inside T001 and T002). Declared by the
architect at phase 3 and not the engineer's to change after seeing the code
(ADR-0144). Reasoning in `plan.md` §*Phase-4a colour*: no production line
moves, so there is no new behaviour to see fail — and because a
characterisation test can be written so it cannot fail, the three
counterfactuals are what make the rest evidence rather than transcription.

**Phase 4b:** *skipped — this delivery adds tests and records a finding; there
is no production code to write.* Say exactly that in the PR body, per
ADR-0037's skip rule. **This is not a licence to skip 4a.**

**Phase 6 is not optional here.** `/code-review` **and** `/security-review`:
the subject is audit disclosure and fab authorization, which constitution §VIII
makes security-sensitive. Point the security reviewer at `plan.md` §F0 and §F3.

**Standing check before trusting any manual observation:** compare the AppHost
process's start time against the commit under test. A persistent stack keeps
serving the binaries it booted with.

**Standing check on secrets:** public repository. Assert on status codes, fab
values and row identity — never on token text, and never put a response body in
a failure message.

**The one thing that ends this spec's scope:** a change under `src/`. If a task
seems to need one, stop and report rather than making it.

---

## US1 (P1) — the fab-owned row whose fab nobody resolved

### [T001] [P] [US1] Write `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs` covering SC-1 through SC-9, run it, and report the output verbatim.

Agent: `test-writer`. Owns one new file; touches nothing else.

New file, `[Collection(AspireCollection.Name)]` (required — `IntegrationTestSelectionTests`
fails a class that declares neither that nor a category trait), namespace
`SmartSentinelEye.Integration.Tests.AuditObservability`, primary constructor
`(AspireFixture aspire, ITestOutputHelper output)`, implementing
`IAsyncLifetime`.

**Shape to mirror, without editing either:**
`tests/Integration.Tests/StreamDistribution/StreamHealthTransitionTests.cs`
for the arrangement (resets, `RegisterAsync`, `WaitForStateAsync`, the
MediaMTX repoint, the `finally` restore), and
`tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`
for the read assertions and the poll helper.

`InitializeAsync` runs the same three resets `StreamHealthTransitionTests` does:
`ResetMediaMtxAsync`, `ResetStreamDistributionAsync`, `ResetCameraCatalogAsync`.
**Do not reset the audit database** — `plan.md` says why.

**One arrangement, shared by every fact in the class.** Build it once (a
`Task<Arrangement>` cached in a field, or `IAsyncLifetime.InitializeAsync`
after the resets), because it costs ~45 s and every fact needs the same two
rows:

1. Register camera `C` in **munich** at `AspireFixture.RtspTestSourceUrl`, under
   a fresh `Guid` so `cam-{guid}` belongs to no other test.
2. Poll `GET /streams?cameraIdentifiers={C}` until `state == "Healthy"`
   (`SettleTimeout` 30 s).
3. Poll the audit read API until the **`munich`** `StreamHealthChangedV1` row
   for `C` exists. Capture its `fab` and its `auditIdentifier`.
4. `UPDATE streams SET fab = NULL WHERE camera_id = {C}` via
   `aspire.CreateStreamDistributionDbContextAsync()` + `ExecuteSqlAsync` —
   copy `StreamFabAttributionIntegrationTests.BlankTheFabAsync`'s shape and its
   doc-comment reasoning. Raw SQL, not the aggregate: `Provision` requires a
   fab and the raw write also bypasses the EF concurrency token the health
   watcher is contending for every 2 s.
5. `aspire.RepointMediaMtxPathAsync(MediaMtxPath.For(CameraIdentifier.From(C)).Value, "rtsp://10.0.6.1/h264")`.
6. Poll until `state == "Degraded"` (`TransitionTimeout` 15 s). **Not
   `Offline`** — `ShouldDeclareOffline` gates it behind 5 minutes and the test
   would hang.
7. Poll the audit read API until a **second** `StreamHealthChangedV1` row for
   `C` exists. Capture its `fab` and `auditIdentifier`.
8. Repoint the path back to `AspireFixture.RtspTestSourceUrl` in a `finally`.

Order is load-bearing: registering at an unreachable address from the start
reaches Degraded before the fab can be blanked, and there is then no second
transition to observe. `plan.md` §*Test design — US1* has the diagram.

Facts to write, one per scenario:

| Fact | Scenario | The claim |
|---|---|---|
| `A_stream_with_no_fab_records_a_null_fab_on_its_health_audit_row` | **SC-1** | **the observation.** The Degraded row's `fab` is `null`, its `resourceKind` is `stream`, its `resourceIdentifier` is `C`, and its payload names `C` |
| `The_same_cameras_earlier_transition_recorded_its_fab` | **SC-2** | **counterfactual.** The Healthy row's `fab` is `"munich"`. Assert both rows in one fact so a reader sees the pair |
| `The_fab_scoped_timeline_returns_the_null_fab_row` | SC-3 | `GET /audit/stream/{C}?fabId=munich` as `admin@munich.test` → `200`, rows include the null-fab row |
| `The_fab_scoped_search_does_not_return_the_null_fab_row` | **SC-4** | same caller, `GET /audit?fabId=munich&eventKind=StreamHealthChangedV1&pageSize=200` → `200`, null-fab row **absent**, and the `munich` row **present** so an empty page cannot pass |
| `An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator` | SC-5 | `op-berlin@berlin.test`, `GET /audit?eventKind=StreamHealthChangedV1&pageSize=200` → null-fab row present |
| `Get_single_returns_the_null_fab_row_to_another_fabs_operator` | SC-6 | `op-berlin@berlin.test`, `GET /audit/{nullFabAuditIdentifier}` → `200`, body `fab` is `null` |
| `Get_single_refuses_the_same_cameras_munich_row_to_that_operator` | **SC-7** | **counterfactual.** Same caller, `GET /audit/{munichAuditIdentifier}` → `403`, `title == "RESOURCE_FAB_NOT_AUTHORIZED"` |
| `A_cross_fab_timeline_is_still_refused` | SC-8 | `admin@munich.test`, `GET /audit/stream/{C}?fabId=berlin` → `403`, `RESOURCE_FAB_NOT_AUTHORIZED` |
| `The_pivot_identifier_is_not_listable_by_another_fabs_operator` | SC-9 | `op-berlin@berlin.test`, `GET /cameras?limit=200&includeRetired=true` → `200`, `C` absent. **Also assert the listing is non-empty or that a berlin camera is present**, or this passes on a broken endpoint |

Reuse, do not reinvent: `aspire.CreateAuthenticatedClientAsync(resource, user, password)`,
`aspire.RepointMediaMtxPathAsync`, `aspire.CreateStreamDistributionDbContextAsync`,
`AspireFixture.RtspTestSourceUrl`, `MediaMtxPath.For`. **Never** a literal host
or port.

Polling: 40 × 500 ms, throwing `Xunit.Sdk.XunitException` with a message that
says what did not appear — the shape
`CrossFabReadGuardIntegrationTests.PollForArchiveRowAsync` already uses. A loop
that falls through to an assertion on an empty page is the failure mode this
repository has recorded; do not write one.

**Every "absent" assertion must be paired with a "present" one on the same
response.** SC-4 and SC-9 both say so above. An assertion that cannot fail is
not evidence.

Run it. Report the **verbatim** output — pass or fail, per test — as the brief
for T004. Do not summarise it.

---

## US2 (P2) — the fab-neutral row, told apart by evidence

### [T002] [P] [US2] Write `tests/Integration.Tests/AuditObservability/NeutralFabRetentionRowIntegrationTests.cs` covering SC-10 through SC-12, run it, and report the output verbatim.

Agent: `test-writer`. Owns one new file; touches nothing else. **Independent of
T001** — different file, different subject, different fixture state.

New file, `[Collection(AspireCollection.Name)]`, same namespace, primary
constructor `(AspireFixture aspire)`. Mirror
`tests/Integration.Tests/AuditObservability/RetentionRoundtripIntegrationTests.cs`
for the back-dated insert, the `timescaledb_information.chunks` probe and the
range-matching poll — **without editing it.**

Arrangement:

1. Insert one row into `audit_events` with `occurred_at = now() - ~400 days`,
   `fab_id = NULL`, `event_kind = 'NullFabRetentionSeedV1'`, via
   `aspire.CreateAuditObservabilityDbContextAsync()` +
   `ExecuteSqlInterpolatedAsync`. Copy `SeedBackdatedRowAsync`'s column list
   verbatim; the `::jsonb` cast and the `payload_size_bytes` value matter.
2. **Before** waiting, assert with the `timescaledb_information.chunks` query
   that the seeded moment fell into a chunk whose range does **not** overlap
   −200 d or −120 d. The interval is one month and
   `RetentionRoundtripIntegrationTests` owns those two;
   `spec.md` G3 says verify rather than assume. If it overlaps, the seed date
   is wrong — change the date, not the assertion.
3. Poll `GET /audit?eventKind=AuditChunkArchivedV1&pageSize=200` (40 × 500 ms)
   for an announcement whose payload's `OccurredFrom`/`OccurredUntil` cover the
   seeded moment. The worker sweeps every few seconds under the AppHost E2E
   override; do not try to call `RunOnceAsync` — the fixture exposes no handle.

Facts:

| Fact | Scenario | The claim |
|---|---|---|
| `An_archived_chunks_announcement_records_no_fab` | **SC-10** | the row's `fab` is `null`, **and** the payload's `FabId` is `null`. The second is F1 as an observation rather than a source read |
| `A_fab_carrying_row_in_the_same_window_is_not_null` | **SC-11** | **counterfactual.** Read any fab-carrying row present in the run (e.g. `GET /audit?fabId=munich&pageSize=50` as `admin@munich.test`) and assert its `fab` is non-null. Proves this run's ingestion path *can* write a fab |
| `The_neutral_announcement_is_reachable_from_a_fab_scoped_timeline` | SC-12 | `admin@munich.test`, `GET /audit/event/{chunkIdentifier}?fabId=munich` → `200`, rows include the `AuditChunkArchivedV1` row. The pivot is `event`/`ChunkIdentifier` — `V1ResourceMap.Conventions.cs` hand-tweak |

Match announcements **by chunk range**, never by count: a count assertion
passes on another class's archive.

Run it. Report the verbatim output as the brief for T004.

---

## [T003] [US1 + US2] Correct `EventMetadataFabDeclarationTests`'s remarks — comments only.

Agent: `backend-engineer`. Depends on **T001** (and on T002 if it ran).

The class remarks today say, under *What a green run does NOT prove*:

> **Not that a fab reaches the audit row at runtime.** A nullable fab that is
> null at runtime — `StreamHealthChangedDomainEventHandler`'s `Fab?.Value` —
> passes this cleanly. That is #2076, and it is **uncovered**.

and, in the last bullet, that `AuditRetentionHostedService` *"is correct today
and it is not protected."*

After T001 and T002, both are covered — behaviourally, elsewhere. Amend **both
sentences in place** to name spec 217 and the two test classes, keeping the
existing structure and the honesty-block framing. The limitation of *this*
guard is unchanged and must stay stated; what changes is that the gap it names
is now closed by a behavioural test, which is what the guard's own remarks
invite.

If T002 did not run, amend only the first sentence and say so — do not claim
coverage nobody observed.

**Comment-only change.** Per the standing lesson, prove it by hashing the code:
strip comments from the file before and after and confirm the hashes match.
Do not assert that the prose contains a string — that is the assertion-checks-
its-own-input failure. Record the two hashes in `verification.md`.

Touches exactly one file: `tests/Architecture.Tests/EventMetadataFabDeclarationTests.cs`.

---

## [T004] [US1 + US2] Write `specs/217-the-null-a-fab-column-cannot-explain/verification.md`.

Agent: `verifier` (phase 5, `/verify`). Depends on T001, T002, T003.

Must contain, as transcripts rather than summary:

- Which run produced the evidence (local Aspire boot, or the CI Docker
  integration job with its run URL). If CI, download the log **before** any
  re-run — a passing re-run flips the whole run to success and erases the
  failure from history.
- If local: the AppHost PID and its start time, against the commit SHA.
- The **observed fab value** for both classes' rows, quoted from the response
  body — not paraphrased.
- The three-read-path matrix for US1's null-fab row: endpoint, caller, status,
  whether the row was returned. Six lines.
- SC-2's and SC-7's counterfactual results, called out as counterfactuals.
- `git diff --stat src/` → empty, quoted.
- T003's two code hashes.
- **Latency budget: N/A**, with `spec.md`'s reason restated. No figure.
- An explicit statement of whether F2's hypothesis held (SC-9), in the words
  "held" or "did not hold".

Write every result down as observed. A measurement reported only to the
orchestrator is invisible to every grep and reviewer.

---

## [T005] [US1 + US2] File the policy follow-up issue.

Agent: `orchestrator`. Depends on T004.

Title, roughly: *"Decide whether a fab-owned resource may record a null fab,
and which of the two fab-taking audit read paths is wrong"*.

Body must carry:

- **F1**: `AuditChunkArchivedV1` is genuinely fab-neutral by construction — the
  four pieces of evidence in `spec.md` §F1 — so exactly **one** class is
  conflated, `StreamHealthChangedV1`. The decision is about one class.
- **F3**, quoting both predicate lines with file and line:
  `SearchAuditQueryHandler.cs:55` (`Fab == fabId`, nulls excluded) against
  `GetResourceTimelineQueryHandler.cs:64` (`Fab == null || Fab == fabFilter`,
  nulls included) — same caller, same authorized fab, opposite answers.
  Whichever way the decision goes, one of these is wrong today.
- **F0**: `GetSingle` skips `IFabAuthorizationGuard` entirely for a null-fab
  row (`AuditEndpoints.cs:193-196`), and this spec gave it its first
  end-to-end coverage.
- The observed evidence from `verification.md`, quoted.
- **The candidate sub-finding**: `AuditChunkArchivedV1.FabId` is a dead
  contract component — nothing populates it and nothing could. Removing it is a
  versioned `Shared.Contracts` change (ADR-0073); it is named here rather than
  filed separately because it is the thing that invited #2517's misreading.
- Why the lane did not decide: ADR-0102's Decision carries both meanings in one
  `string? Fab`, so a distinction amends that ADR, and ADR-0144 forbids the
  lane writing or amending one.

**Labels: not `agent:ready`.** The point is that a human decides. Add it to
Project #13:

```sh
gh project item-add 13 --owner smartsolutionslab --url <issue-url>
```

`item-add` prints nothing on success; verify with `--limit 2000`, and note
Projects v2 has its own rate limit that two board dumps will exhaust.

If SC-9 went red, file that **separately** — it is a disclosure finding, not a
policy question, and folding it in buries it.

---

## [T006] [US1 + US2] Open the PR.

Agent: `reviewer` + `orchestrator`. Depends on T004, T005, and phase 6.

`gh pr create --base develop` (mandatory flag, ADR-0028). Body must contain:

- `Phase 4b: skipped — this delivery adds tests and records a finding; there is
  no production code to write.`
- The **verbatim** test output from T001 and T002 (ADR-0139) — the only form of
  the evidence a later reader can check.
- `git diff --stat src/` showing empty.
- The three-read-path matrix.
- **The policy question, stated and unresolved**, with the ADR-0144 reason and
  a link to T005's issue. Do not answer it.
- F1 stated plainly: the issue's framing named two classes; one of them is
  genuinely fab-neutral by construction, and the evidence is in `spec.md` §F1.
- A closing keyword for #2517 — and check the issue's state after the merge;
  a PR mention auto-closes roughly one time in three.
- The attribution lines from the session reminder. **No `Co-Authored-By`
  footer** on the commits (ADR-0086).

Commits follow ADR-0030 (Conventional Commits) and must each build on their
own — rebase-merge lands them individually on `develop` (ADR-0087).

---

## Ordering

```
T001 [P] ──┐
T002 [P] ──┴─> T003 ──> T004 ──> T005 ──> (phase 6) ──> T006
```

T001 and T002 are the only parallel pair, and they are genuinely disjoint: two
new files, no shared helper, no shared fixture state. Everything after T003 is
sequential because each consumes the previous one's output.

**T002 is P2 and droppable.** If the stack cannot be booted twice, or the
retention sweep cannot be settled in the window, ship US1 alone: it answers
#2517 on the one class F1 leaves standing. Say in the PR that US2 was not
executed — never report a colour nobody observed.

---

## Parallelism (ADR-0109)

| Task | Files owned | Conflicts with |
|---|---|---|
| T001 | `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs` (new) | nothing |
| T002 | `tests/Integration.Tests/AuditObservability/NeutralFabRetentionRowIntegrationTests.cs` (new) | nothing |
| T003 | `tests/Architecture.Tests/EventMetadataFabDeclarationTests.cs` | nothing |
| T004 | `specs/217-…/verification.md` (new) | nothing |
| T005 | GitHub issue + Project #13 | nothing |
| T006 | PR | nothing |

Foundational work that would block a fan-out — `Shared.Kernel`,
`Shared.Contracts`, `AppHost`, a new Aspire resource — **is absent by design**.
Nothing under `src/` moves, so there is no foundation to lay and the
orchestrator can dispatch T001 and T002 immediately.

---

## Task table

| ID | [P] | Story | Agent | Primary file | Depends on |
|---|---|---|---|---|---|
| T001 | [P] | US1 | `test-writer` | `UnresolvedFabAuditRowIntegrationTests.cs` (new) | — |
| T002 | [P] | US2 | `test-writer` | `NeutralFabRetentionRowIntegrationTests.cs` (new) | — |
| T003 | | US1 + US2 | `backend-engineer` | `EventMetadataFabDeclarationTests.cs` (comments only) | T001 |
| T004 | | US1 + US2 | `verifier` | `specs/217-…/verification.md` (new) | T001, T002, T003 |
| T005 | | US1 + US2 | orchestrator | GitHub issue + Project #13 | T004 |
| T006 | | US1 + US2 | reviewer + orchestrator | PR | T004, T005, phase 6 |
