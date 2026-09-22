# Plan — Spec 217, the null a fab column cannot explain (#2517)

Phase 2 of ADR-0037. Reviewed against `.specify/memory/constitution.md` and the
ADRs named in `spec.md`. Read `spec.md` first; this file assumes it.

---

## Shape of this change

**No bounded context is touched, and that is not an omission.** This delivery
adds two integration test classes and one comment correction in an existing
architecture test. There is no domain model, no aggregate, no value object, no
command, no query, no handler, no migration, no endpoint, and no message.

| Usual plan section | Here | Why |
|---|---|---|
| Bounded context + layers | **N/A** | Nothing under `src/` changes. The subject is what the *existing* AuditObservability write and read paths already do. |
| Entities / value objects + invariants | **N/A** | No domain model participates. Constitution §II binds domain state; none is introduced. |
| Messaging (domain → integration event) | **Exercised, not changed** | `StreamHealthChangedDomainEvent` → `StreamHealthChangedV1` and `AuditChunkArchivedV1` are driven through their real Wolverine outbox paths (ADR-0040, ADR-0088). No contract is edited. |
| Boundary rules (no cross-context refs) | **Honoured; see below** | No project reference is added to `src/`. The test project already references every context. |
| Persistence / EF / Marten | **Read and one manufactured write** | No schema, no `DbContext` class, no migration. One raw `UPDATE streams SET fab = NULL` and one raw `INSERT INTO audit_events`, both from test code, both precedented. |
| Aspire resources (§VI) | **Unchanged** | No new runtime resource; `AppHost` is not edited. |
| Latency budget (§IV) | **N/A**, stated | No leg of the six. `spec.md` §*Latency-budget impact* says why, and says no figure is cited. |
| Coverage gates (ADR-0065) | **Unaffected** | Domain ≥ 90 / Application ≥ 80 / Shared ≥ 90 measure `src/`; this adds no `src/` lines and removes none. |

What *does* apply is the test design, the evidence design, and the exit
condition — which is the rest of this plan.

---

## The finding this planning pass turned up, stated plainly

The task asked that an investigation which changes the issue's framing say so
rather than silently resolve it or leave it unquestioned. It does, in both
directions.

**The issue's framing is more severe than the code in one respect and less
severe in another, and the net is: the problem is half the size #2517 states,
and the useful artefact is different from the one it asks for.**

- **Half the size (F1).** `AuditChunkArchivedV1` is *not* a fab-owned resource
  whose fab failed to resolve. The hypertable is partitioned on `occurred_at`
  alone, so a chunk is a one-month slice of every fab's rows; `AuditChunk`
  carries no fab; the sole publisher passes literal nulls; and
  `AuditObservability.Domain.AuditEvent.FabIdentifier`'s own documentation
  already names this type as the canonical legitimately-null case. It belongs
  with the overlay events under ADR-0115. **One fab-owned class is actually
  conflated, not two: `StreamHealthChangedV1`.**
- **Less severe than it reads (F2).** The identifier needed to pivot the
  widened timeline is, for both classes, obtainable in practice only from the
  row it would return; and two wider routes — the unscoped search, and
  `GetSingle`, which skips the fab guard outright for a null-fab row — were
  already open. So #2506's increment is plausibly zero *data*, not merely zero
  *population*. SC-5, SC-6, SC-7 and SC-9 put that to the running system rather
  than leaving it asserted.
- **More severe than it reads, in a place the issue does not look (F0).**
  `GetSingle` does not run `IFabAuthorizationGuard` at all when the row's fab
  is null. That is the widest of the three paths, it long predates #2506, and
  per the integration-suite survey it is **the only one of the three with no
  end-to-end coverage**. This spec gives it its first.
- **A better question than the one asked (F3).** `GET /audit?fabId=X` excludes
  null-fab rows; `GET /audit/{kind}/{id}?fabId=X` now includes them. Same
  caller, same authorized fab, same guard, opposite answers about one row.
  That is the policy question in decidable form, and it is what the follow-up
  should carry.

None of this changes the *deliverable* #2517 asks for — a behavioural guard,
and a human decision. It changes what the guard should observe and what the
human should be handed.

---

## Why the policy question is not answered here

`spec.md` §F4 in full: ADR-0102's Decision gives `EventMetadata` a single
`string? Fab` and describes both meanings in one sentence — *"owning fab, when
the event is fab-scoped"* and *"from the aggregate / command where available,
else `null`"*. There is no vocabulary in the envelope for the difference.
Introducing one — a second component, a sentinel, or a stated rule about which
publishers may pass null — amends that Decision.

ADR-0144: the lane *"may not write an ADR or amend the constitution — it
implements decisions, it does not make them."*

**So the PR body surfaces the question unresolved, and the delivery stops.**
Mirroring spec 214 (#2509), which investigated Keycloak lockout survival and
left the remedy to a human on both branches, and #2510, whose body records the
same constraint in as many words. Concretely, the PR body must:

- state the observed answer to "what lands in the column" as fact;
- state F1, so the human decides about one class;
- state F3 as the decidable form, with both predicate lines quoted;
- state that neither is resolved here and why (ADR-0144, ADR-0102);
- link the follow-up issue that carries the evidence.

**This is not a shortfall to apologise for; it is the scope.** Writing it down
in the PR is what makes the omission reviewable.

---

## Files this delivery owns

| File | Kind | Story |
|---|---|---|
| `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs` | **new** | US1 |
| `tests/Integration.Tests/AuditObservability/NeutralFabRetentionRowIntegrationTests.cs` | **new** | US2 |
| `tests/Architecture.Tests/EventMetadataFabDeclarationTests.cs` | edited, **remarks only** | US1 + US2 |
| `specs/217-the-null-a-fab-column-cannot-explain/verification.md` | new | US1 + US2 |

Nothing else. In particular **not**:

- `src/**` — any change there ends this spec's scope;
- `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`
  — it carries specs 009, 082, 209 and 215's evidence and its assertions are
  load-bearing for four merged PRs. Its comment *"A stream-health event's fab
  is nullable and may still arrive null (#2076)"* becomes observed rather than
  speculated by this work, but leaving it alone keeps `git diff --stat` on that
  file at zero, which `verification.md` will quote;
- `tests/Integration.Tests/StreamDistribution/StreamHealthTransitionTests.cs`
  and `StreamFabAttributionIntegrationTests.cs` — copied *from*, never edited.

Two new files rather than two edits is also what makes US1 and US2 `[P]`
under ADR-0109.

### Why two new files rather than one class with six facts

US1's arrangement is a live MediaMTX repoint plus three database resets; US2's
is a back-dated insert and a timer. They share no helper, no fixture state and
no failure mode. A class that owns both is a class whose red tells you less,
and it serialises two arrangements that could run as separate `[P]` tasks.

### Why not extend `CrossFabReadGuardIntegrationTests`

It is the obvious home and it is the wrong one. That class's every fact is
about a **seeded** row — `Row(fab)` and `OverlayRow(id, fab)` construct
`AuditEvent.From(...)` directly and write it with `SaveChangesAsync`. Its own
sibling fact says why that is not enough here:

> Drives the real publish path rather than seeding a row, because that is where
> the defect lives. A seeded row carries whatever fab the test wrote; only an
> archive performed through the API shows what the handler actually stamps.

#2517 asks for exactly that distinction. A seeded null-fab row would prove
nothing about `StreamHealthChangedDomainEventHandler`.

---

## Phase-4a colour: **CHARACTERISATION, OBSERVED GREEN**

Declared by the architect at phase 3, per ADR-0144, and not the engineer's to
change after seeing the code.

**Why not red.** Constitution §Testing's two obligations split on whether
behaviour changes. This delivery changes **no production line**, so there is no
new behaviour that could be seen to fail. Every assertion in US1 and US2 is
written for what the system does today; a red is a *finding* — the prediction
was wrong — and under ADR-0144 the lane may not edit the assertion to make it
pass. The spec says which reds mean what: SC-2's control going red means the
premise moved; SC-9 going red means F2 is wrong and the exposure is larger than
#2517 described.

**Why the ambiguity rule does not bite.** CLAUDE.md resolves ambiguity to red
because red fails loudly and green passes quietly. There is no ambiguity to
resolve: the diff under `src/` is empty by construction, and `verification.md`
quotes `git diff --stat src/` as proof.

**What makes green mean something anyway.** A characterisation test can be
written so it cannot fail. Three assertions exist solely to stop that:

| Guard | What it rules out |
|---|---|
| **SC-2** — the same camera's earlier transition recorded `munich` | an ingestion path that stamps null on everything; a broken `V1ResourceMap`; a fab column never written |
| **SC-7** — `GetSingle` answers `403` for the munich-fab row | an endpoint with no authorization at all, which would make SC-6's `200` meaningless |
| **SC-11** — a fab-carrying row in the same window is non-null | a run in which nothing could have written a fab |

Each constructs what the assertion claims to catch. This repository logged five
assertions in one week that could not fail, and the standing test is: *can the
subject change without the assertion text changing?* For all three, yes.

**Phase 4b:** *skipped — this delivery adds tests and records a finding; there
is no production code to write.* Say exactly that in the PR body, per
ADR-0037's skip rule. **This is not a licence to skip 4a**, which ADR-0144
exempts nothing from.

---

## Test design — US1

### The arrangement, and why it yields its own control

```
register camera C in munich at rtsp://fixture-video:8554/loop   (real API)
  └─ stream provisions, watcher sweeps, reaches Healthy
       └─ StreamHealthChangedDomainEvent(Fab: munich) → StreamHealthChangedV1
            → audit row #1, fab = 'munich'          ← SC-2, the control
UPDATE streams SET fab = NULL WHERE camera_id = C   (raw SQL)
PATCH mediamtx /v3/config/paths/patch/cam-C → rtsp://10.0.6.1/h264
  └─ watcher's next sweeps observe the dial fail, reaches Degraded
       └─ StreamHealthChangedDomainEvent(Fab: null) → StreamHealthChangedV1
            → audit row #2, fab = NULL              ← SC-1, the observation
```

One camera, one pipeline, two rows, one pivot. The control is not bolted on —
it is the same arrangement one step earlier, which is what makes it credible.

**Order is load-bearing and easy to get wrong.** Registering at an unreachable
address from the start (the cheaper idiom `RtspTestSourceHealthTests` uses)
does **not** work here: the stream reaches Degraded within ~2 sweeps, before
the fab can be blanked, so the announcement carries the fab and there is no
second transition to observe. Reach `Healthy` first, blank, then repoint.

### Mechanisms, all precedented — reuse, do not reinvent

| Need | Use | Precedent |
|---|---|---|
| Register a camera and wait for `Healthy` | `GET /streams?cameraIdentifiers={C}` polled at 500 ms, reading `items[0].state` | `StreamHealthTransitionTests` |
| Manufacture the fab-less stream | `aspire.CreateStreamDistributionDbContextAsync()` + `ExecuteSqlAsync($"UPDATE streams SET fab = NULL WHERE camera_id = {camera}")` | `StreamFabAttributionIntegrationTests.BlankTheFabAsync` |
| Provoke Degraded | `aspire.RepointMediaMtxPathAsync(MediaMtxPath.For(...).Value, "rtsp://10.0.6.1/h264")` | `StreamHealthTransitionTests` |
| Wait for the audit row | poll `GET /audit?eventKind=StreamHealthChangedV1&resourceIdentifier={C}&pageSize=200`, 40 × 500 ms, throw `XunitException` on expiry | `CrossFabReadGuardIntegrationTests.PollForArchiveRowAsync`, `EndToEndIngestionIntegrationTests` |
| Single-fab identities | `admin@munich.test`/`Admin1234`, `op-berlin@berlin.test`/`Operator1234` | `CrossFabReadGuardIntegrationTests` |
| Resets | `ResetMediaMtxAsync` + `ResetStreamDistributionAsync` + `ResetCameraCatalogAsync` in `IAsyncLifetime.InitializeAsync` | `StreamHealthTransitionTests` |

### Three hazards, and what to do about each

1. **`StreamFabAttributionService` re-attributing the blanked row.** It is an
   `IHostedService` whose `StartAsync` runs once at `stream-distribution` host
   start; a row blanked afterwards is never re-examined. `Do not add anything
   to this class that restarts that resource` — a restart reruns the pass and
   *would* attribute the row, because its camera is in the catalogue.
   `A_stream_with_no_fab_is_returned_to_nobody` already depends on this and
   passes today.
2. **The EF concurrency token.** `StreamHealthWatcher` writes these same rows
   every 2 s. A raw `UPDATE` bypasses the token and cannot lose that race —
   which is why the blanking is SQL and not an aggregate call.
   `StreamFabAttributionIntegrationTests.AttributePassAsync` documents the
   other side of this and retries on `DbUpdateConcurrencyException`; the raw
   path needs no such retry.
3. **`Offline` is unreachable.** `StreamHealthWatcher.ShouldDeclareOffline`
   gates Offline behind `now - degradedAt >= 5 minutes`. **Degraded is the
   transition to use.** A test that waits for Offline hangs and then fails for
   the wrong reason.

### Blast radius

`InitializeAsync` deletes every SFU path and wipes the StreamDistribution and
CameraCatalog databases — inherited unchanged from `StreamHealthTransitionTests`,
which explains why that is acceptable inside the serialized `Aspire` collection
and why it needs no `[Trait("Category", "Disruptive")]`. Register under a fresh
`Guid` so `cam-{guid}` belongs to no other test, and repoint the path back in a
`finally`.

**It does not wipe the audit database**, which matters: SC-5 and SC-6 read rows
this class wrote, and a reset between arrange and assert would make an empty
page indistinguishable from a correct exclusion.

---

## Test design — US2

Drive a genuine archive, do not seed an `AuditChunkArchivedV1` row.

```
INSERT INTO audit_events (... occurred_at = now() - 400 days, fab_id = NULL ...)
  └─ retention worker's next sweep (seconds, under the AppHost E2E override)
       └─ ArchiveChunkAsync → MinIO → AuditChunkArchivedV1(FabId: null,
             Metadata: EventMetadata(..., null, null)) → outbox → audit row
```

`AuditRetentionHostedService.RunOnceAsync` is deliberately public *"so
integration + retention tests can drive the worker without spinning the timer"*,
but the integration stack's override already sweeps every few seconds;
`RetentionRoundtripIntegrationTests` relies on the timer and this should too —
calling `RunOnceAsync` from the test would need a service handle the fixture
does not expose.

**The chunk-collision hazard (G3).** `RetentionRoundtripIntegrationTests` seeds
at −200 d and −120 d. The hypertable's chunk interval is **one month**, so a
second class seeding at −200 d lands in the *same chunk* and the two races for
one archive. Seed at ~−400 d, and assert on the chunk range the announcement
reports rather than on a count — the sibling class's own matching strategy,
adopted for the same reason.

**What SC-10 asserts beyond the fab column.** The announcement's payload
`FabId` too. F1's claim is that nothing *could* set it; the assertion is what
turns that from a source read into an observation, and it is the single
cheapest piece of evidence in this spec.

---

## Boundary and convention rules that still apply

- **§III / NetArchTest.** No `src/` project reference is added, so the
  cross-context rule is honoured trivially. `tests/Integration.Tests` already
  references every context; nothing about that changes.
- **ADR-0105 guards.** No production argument guard is written. Test helpers
  use `Ensure.That` only where the surrounding class already does.
- **Collections (CLAUDE.md).** `List<T> x = [];` / `string?[] fabs = [.. …]`,
  explicit type plus collection expression — `dotnet_style_prefer_collection_expression`
  is at `warning` and fails the Release build.
- **Private fields carry no leading underscore.**
- **`IntegrationTestSelectionTests`.** Both new classes must carry
  `[Collection(AspireCollection.Name)]` — a class declaring neither that nor a
  category trait fails that architecture test, by design.
- **ADR-0053 naming.** Sentence-style with underscores, one fact per scenario.
- **Secrets.** Assert on status codes, fab values and row identity. Never put a
  token or a full response body in a failure message; this is a public
  repository.

---

## Risks

| Risk | Mitigation |
|---|---|
| Aspire cannot be booted on the delivery machine this pass | `tasks.md` is written so CI's Docker integration job settles both stories; phase 5 records which run produced the evidence, and never claims a colour nobody observed. |
| SC-1 lands red because the premise moved (e.g. the fab is stamped from ambient context somewhere) | That is the finding. Report it verbatim; do not adjust the assertion (ADR-0144). |
| SC-9 lands red — a berlin operator *can* enumerate the munich camera | F2 is wrong and the disclosure is wider than #2517 described. File it as its own issue with the evidence; do not fold it into the policy follow-up. |
| The 15 s Degraded window is not enough under CI load | `StreamHealthTransitionTests` calls that budget *"the number in both method names"* and says a transition that does not fit is **a finding to file, not a number to raise.** Same rule here. |
| Two classes both reset shared stack state | The `Aspire` collection is serialized, so classes do not overlap. US2 touches none of the resources US1 resets. |
| The follow-up issue is filed with `agent:ready` by reflex | It must **not** carry it. The whole point is that a human decides. |

---

## Definition of done

1. Both new test classes exist, run against the real stack, and their output is
   quoted verbatim in the PR body (ADR-0139).
2. `git diff --stat src/` is empty, and is quoted as such.
3. `verification.md` records the observed fab value for both classes, and which
   of the three read paths returned each row to which operator — as
   transcripts, not as a summary. A measurement reported only to the
   orchestrator is invisible to every grep and reviewer.
4. `EventMetadataFabDeclarationTests`'s remark that the #2076 case "is
   uncovered" points at this spec instead.
5. A follow-up issue carries F1, F3 and the evidence, is on Project #13, and
   does **not** carry `agent:ready`.
6. The PR body states the policy question, unresolved, with the ADR-0144
   reason — and does not answer it.
7. Phase 6 includes `/security-review`: this is audit and fab authorization,
   which constitution §VIII makes security-sensitive. The reviewer should be
   pointed at F0 and F3 specifically.
