# Spec 217 — The null a fab column cannot explain

**Issue:** #2517 — "Fab-owned rows with an accidentally-null Fab are now
readable via all three audit read paths, not two"
**Branch:** `2517-null-fab-two-meanings`
**Lane:** autonomous (ADR-0144) — **and this spec ends inside that lane's
limits on purpose**; see *What this spec delivers*.
**ADRs:** ADR-0037 (the seven phases), ADR-0052/0053/0054 (test framework,
naming, data), ADR-0101 (TimescaleDB for audit), **ADR-0102 (the integration
event envelope — the ADR whose Decision the policy question would amend)**,
ADR-0103 (integration tests against the Aspire fixture, no Testcontainers),
ADR-0109 (disjoint-file parallelism), ADR-0114 (a fab inferred for single-fab
operators), **ADR-0115 (overlays are fab-neutral templates)**, **ADR-0116
(stream fab attribution service account)**, ADR-0126 (audit listeners settle at
the broker), ADR-0139 (a rule fails the build, not the review), ADR-0144 (the
autonomous lane and what it may not do)
**Constitution:** §III (bounded context isolation), §VIII (*"Authorization is
enforced by scope checks at every endpoint, plus fab-group membership"*),
NFR §Security (*"All admin and config writes appear in the audit log"*),
§Data Retention (audit log 365 days hot, then archived to MinIO), §Testing

---

## Problem

Spec 209 (#2506) widened `GetResourceTimelineQueryHandler`'s fab predicate
from `Fab == fabFilter` to `Fab == null || Fab == fabFilter`
(`src/AuditObservability/Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs:64`),
so that rows which legitimately carry no fab stop being invisible to every
operator — the #1300 defect, fixed once in the search path and now in the
timeline path too.

Its phase-6 security review noticed that "carries no fab" is **two different
facts wearing one column value**:

- a resource that **has** no fab, because its kind is fab-neutral by design; and
- a resource that **has** a fab which nobody managed to resolve when the row
  was written.

A predicate that admits the first also admits the second, and nothing in the
schema, the contract, or the read path can tell them apart. #2517 asks for a
**behavioural guard** that observes what actually lands in the column, and for
a **human** to decide whether the conflation is an acceptable degraded state or
needs an explicit distinction.

---

## Premise check — the issue's claims, re-verified at `origin/develop` (`2d44dd14`)

The standing lesson in this repository is that an issue's premise goes stale
before it is delivered (11 of ~20 recent board issues were found stale at
delivery). Most of #2517 **holds**. **One central claim does not**, and reading
the code turned up three facts the issue did not have.

| Issue claim | At HEAD | Verdict |
|---|---|---|
| #2506 widened the timeline predicate to `Fab == null \|\| Fab == fabFilter` | `GetResourceTimelineQueryHandler.cs:64`, exactly that | **Holds** |
| `StreamHealthChangedDomainEvent.Fab` is `FabIdentifier?` and the handler passes `Fab?.Value` | `StreamHealthChangedDomainEvent.cs:29`; `StreamHealthChangedDomainEventHandler.cs:53-57` | **Holds** |
| `AuditChunkArchivedV1` is outside `EventMetadataFabDeclarationTests` by construction | the test's own remarks say so, and the reason is structural: the scan is anchored on `Handle`/`HandleAsync` and `AuditRetentionHostedService.cs` names neither | **Holds** |
| Before #2506 a null-fab row was reachable via `GetSingle` and the unscoped search | `GetAuditEventQueryHandler` has no fab predicate at all | **Holds — and is worse than stated.** See F0 |
| `AuditChunkArchivedV1` is *nullable but not neutral* — a fab-owned resource whose fab failed to resolve | a chunk has no fab to fail to resolve | **Wrong.** See F1 |
| "No new caller population gains access" | correct, and the increment is smaller still | **Holds.** See F2 |
| `#2076, closed as accepted` | #2076 was closed as **fixed** — `CameraCatalogFabLookup.cs:66` now sends `includeRetired=true`, with a doc-comment saying it is load-bearing | **Needs adjustment.** See F5 |

### F0 — `GetSingle` does not merely lack a fab *filter*; it skips the fab *guard*

`AuditEndpoints.GetSingle` (`src/AuditObservability/Api/AuditEndpoints.cs:193-196`):

```csharp
AuditRowDto row = result.Value;
if (row.Fab is not null)
{
    await fabGuard.EnsureAccessAsync(user, row.Fab, cancellationToken);
}
return Results.Ok(row);
```

For a null-fab row the authorization check is **not run at all**: any holder of
`sse.audit.read` who has the audit identifier reads the row, whatever fab the
underlying resource belongs to. That is the widest of the three paths, it
predates #2506 by a long way, and #2506 does not touch it. It is also, per the
exploration of `tests/Integration.Tests/`, **the only one of the three read
paths with no end-to-end integration coverage at all.**

### F1 — `AuditChunkArchivedV1` is genuinely fab-neutral *by construction*, not a fab-owned row with an unresolved fab

This is the issue's central mis-framing, and it halves the problem. Four
independent pieces of evidence:

1. **The subject has no fab.**
   `AuditChunk(Guid ChunkIdentifier, DateTimeOffset OccurredFrom, DateTimeOffset OccurredUntil)`
   (`src/AuditObservability/Application/Retention/IAuditChunkInventory.cs`) —
   a chunk is a time range, and nothing else.
2. **The hypertable is partitioned on time only.** The initial migration
   (`…/Migrations/20260529124335_InitialAuditObservability.cs:76-78`) runs
   `create_hypertable('audit_events', 'occurred_at', chunk_time_interval => INTERVAL '1 month')`
   with **no space dimension**. A chunk is therefore a one-month slice of
   *every* fab's rows simultaneously. There is no fab it could be attributed to
   that would not be a lie.
3. **The sole publisher passes literals.** `AuditRetentionHostedService.cs:136-145`
   is the only site in `src/` that constructs the event, and it writes
   `FabId: null` and `Metadata: new EventMetadata(…, null, null)` as literals.
   No code path exists that could pass anything else.
4. **The domain says so already.**
   `AuditObservability.Domain.AuditEvent.FabIdentifier`'s own doc-comment names
   this very type as *the* example of a legitimately-null fab: *"Optional on an
   audit row because some cross-cutting V1s (e.g. `AuditChunkArchivedV1` with
   no `FabId`) are not fab-scoped."*

So it belongs with the overlay lifecycle events (ADR-0115), not with stream
health. **The issue's worry — a fab-*owned* resource whose fab column is null —
has exactly one instance in the system, not two: `StreamHealthChangedV1`.**

A smaller finding falls out: `AuditChunkArchivedV1.FabId` is a **dead contract
component**. Nothing populates it and nothing could. It is precisely what
invites the misreading #2517 made. Recorded as a candidate follow-up, not fixed
here — removing a component from a versioned `Shared.Contracts` record is a
contract change (ADR-0073) and not this spec's business.

### F2 — the incremental disclosure from #2506 is not merely "no new population"; it is *no new data*

The timeline endpoint needs three things: a `resourceKind`, a
`resourceIdentifier`, and a `fabId` the caller is a member of (`fabId` is
**required** — spec 209's own verification recorded the `400` for omitting it
and the `403` for naming another fab). For each null-fab class, the identifier
needed to pivot is obtainable only from the row itself:

| Class | Pivot (`V1ResourceMap.Conventions.cs`) | Where else could the caller learn the identifier? |
|---|---|---|
| `AuditChunkArchivedV1` | `event` / `<chunk identifier>` | A TimescaleDB chunk identifier appears on no API surface but the archived row. |
| `StreamHealthChangedV1` | `stream` / `<camera identifier>` (blessed: a stream's public handle is its camera id) | `GET /cameras` is fab-scoped, so a munich operator cannot enumerate berlin cameras. |

Meanwhile `GET /audit` with **no** `fabId` already returns every null-fab row
to every `sse.audit.read` holder with no pivot at all
(`SearchAuditQueryHandler.cs:73`), and `GET /audit/{auditIdentifier}` returns
one with no fab check at all (F0).

So the route #2506 opened reaches only rows whose pivot identifiers the caller
could realistically have learned **only from a query that already returned
those rows**. It is strictly weaker than two routes that were already open.
**Stated here as a hypothesis derived from source, not as a finding** — SC-5,
SC-6 and SC-9 are what put it to the running system.

### F3 — the two fab-*taking* read paths now disagree, and the issue does not name it

| Path | Fab named by caller | Guarded | Predicate | Null-fab row |
|---|---|---|---|---|
| `GET /audit?fabId=munich` | yes | yes | `Fab == fabId` (`SearchAuditQueryHandler.cs:55`) | **excluded** |
| `GET /audit/{kind}/{id}?fabId=munich` | yes (**required**) | yes | `Fab == null \|\| Fab == fabFilter` (`GetResourceTimelineQueryHandler.cs:64`) | **included** |
| `GET /audit` (no fabId) | no | n/a | `Fab == null \|\| allowed.Contains(Fab)` (`:73`) | included |

#2506 matched the timeline to the **third** row — the route that names no fab
at all — while leaving the **first** row, which names exactly the same
authorized fab, excluding the same class of row. Two endpoints that take the
same fab from the same caller and pass the same guard now answer differently
about the same row.

**This is the concrete, decidable form of the policy question.** Whichever way
a human settles it, one of those two lines is wrong today. That is worth more
to the decision than the abstraction "acceptable degraded state or not", and it
is the shape the follow-up should carry.

### F4 — answering the policy question means amending ADR-0102, which the lane may not do

ADR-0102's Decision block carries both meanings in one field and one sentence:

```csharp
string? Fab,             // owning fab, when the event is fab-scoped
```

> `Fab` / `Actor` from the aggregate / command where available, **else `null`**.

"when the event is fab-scoped" is the neutral case. "where available, else
null" is the unresolved case. The envelope has **no vocabulary** for the
difference, so giving the two classes an explicit distinction is a change to
that Decision — a new envelope component, or a sentinel, or a documented rule
about which publishers may pass null and why.

ADR-0144: the lane *"may not write an ADR or amend the constitution — it
implements decisions, it does not make them."* That is why this spec stops at
the observation, and it is a scope decision rather than a shortfall.

### F5 — the null-fab stream-health row is real, but not naturally producible on a fresh stack

`Stream.Provision(FabIdentifier fab, …)` requires a fab and there is no setter
(`src/StreamDistribution/Domain/Stream/Stream.cs:37-44,68-84`), so every stream
created since spec 016 has one. `StreamFabAttributionService` is an
`IHostedService` whose `StartAsync` runs `AttributeOnceAsync` exactly once at
host start and fills the rest; with #2076's `includeRetired=true` in place the
"permanently unattributable" class that #1467 is still waiting on is
essentially empty.

So the precondition must be **manufactured**, and this repository already has
the idiom for it:
`tests/Integration.Tests/StreamDistribution/StreamFabAttributionIntegrationTests.cs:446-458`

```csharp
/// Recreates a row that predates the fab column. Written through SQL
/// rather than the aggregate because the aggregate deliberately cannot
/// express it — Provision requires a fab and there is no setter.
private async Task BlankTheFabAsync(Guid camera)
```

Because attribution is one-shot at host start, a row blanked after the fixture
is up is never re-examined — the existing
`A_stream_with_no_fab_is_returned_to_nobody` already depends on exactly that.

**Assumption, marked (G1):** "manufactured" is the honest word. This spec
asserts that the manufactured row is the *same shape* as a genuine pre-016 row
(same column, same null, same aggregate load path) — not that such a row exists
in any live database today. #1467 is the issue that would establish that, and
it is open.

---

## What this spec delivers, and what it deliberately does not

**It delivers an executable observation, not a fix.** No file under `src/`
changes. Concretely:

1. an integration test that drives a **fab-owned** resource's event through the
   real publish path with an unresolved fab, and records what the audit row's
   fab column actually is — and, in the same arrangement, what the *same*
   camera's *previous* transition recorded while the fab was still resolved;
2. the same for the **fab-neutral** retention announcement, driven through a
   real archive sweep;
3. for each, which of the three read paths return the row, and to whom —
   observed live, not predicted from source;
4. the `EventMetadataFabDeclarationTests` remark that says this case "is
   uncovered" corrected to point at the coverage;
5. the policy question surfaced **unresolved** for a human, carrying F1, F3 and
   the observed evidence.

**It does not decide whether a null fab on a fab-owned resource is acceptable.**
Per F4 that is an ADR-0102 amendment, and per the issue's own text it is
explicitly a human call. The precedent is spec 214 (#2509), which delivered an
investigation and left the remedy to a human on both branches, and #2510, whose
body records the same constraint.

The gate at the end of this spec is **"observed, recorded, human decides."**

---

## Locked tech choices

Nothing new is chosen. No package, no bounded context, no runtime resource, and
**no production code**.

| Concern | Choice | Where |
|---|---|---|
| Integration testing | `AspireFixture` against the real stack, no Testcontainers | ADR-0103; `tests/Integration.Tests/Fixtures/` |
| Test framework | xUnit + Shouldly, hand-written helpers, no AutoFixture | ADR-0052, ADR-0054 |
| Test naming | sentence-style with underscores | ADR-0053 |
| Audit store | PostgreSQL + TimescaleDB hypertable, time-partitioned, 1-month chunks | ADR-0101 |
| Event envelope | `EventMetadata(EventIdentifier, OccurredAt, Fab, Actor)` | ADR-0102 |
| Provoking a health transition | `PATCH /v3/config/paths/patch/cam-{camera}` on MediaMTX via `AspireFixture.RepointMediaMtxPathAsync` | precedent: `StreamHealthTransitionTests.cs` |
| Manufacturing a fab-less stream | raw `UPDATE streams SET fab = NULL` through `CreateStreamDistributionDbContextAsync` | precedent: `StreamFabAttributionIntegrationTests.BlankTheFabAsync` |
| Driving a retention sweep | back-dated row insert, then poll for the announcement | precedent: `RetentionRoundtripIntegrationTests.cs` |
| Single-fab identities | `admin@munich.test`/`Admin1234` (munich only), `op-berlin@berlin.test`/`Operator1234` (berlin only) | `smart-sentinel-eye-realm.json`; used throughout `CrossFabReadGuardIntegrationTests` |

---

## User stories

### US1 (P1) — the fab-owned row whose fab nobody resolved

**As** the engineer who has to answer #2517,
**I want** a stream that genuinely has no fab to be transitioned through the
real health path, and the audit row it produces read back through all three
read surfaces,
**so that** "a fab-owned resource can land in the audit log with a null fab,
and here is exactly who can then read it" stops being an inference from source.

This is the story the issue is actually about (F1 removes the other candidate).
It is one vertical slice — one new test file, one manufactured precondition,
one observable answer — and it is buildable and shippable on its own. US2
refines the *contrast*; it does not unblock this.

### US2 (P2) — the fab-neutral row, told apart by evidence rather than by reading

**As** the human who has to decide the policy,
**I want** the retention announcement observed through a real archive sweep, so
that F1's "genuinely neutral by construction" claim rests on a run rather than
on four source reads,
**so that** the decision is about **one** conflated class and not two.

Separable on purpose: different subject, different producer, a different new
file, no shared helper. Runs in parallel with US1 (ADR-0109). **If US2 cannot
be executed in this pass, US1 still answers the issue** — say so rather than
reporting a colour nobody observed.

---

## Acceptance scenarios

All scenarios run against the real stack (ADR-0103). A test that read
`StreamHealthChangedDomainEvent`'s source and asserted `Fab` is nullable would
prove the design was written down, not that a null reaches the column — this
repository's own recorded lesson about guards that read the design artefact.

### US1 — the observation and its control

**SC-1 — a fab-owned resource with an unresolved fab produces an audit row
whose fab column is null.**

```gherkin
Given a camera registered in fab munich at a reachable RTSP source
  And its stream has reached Healthy
  And that stream's fab column has then been cleared to NULL by direct SQL,
      reproducing the pre-spec-016 row shape #1467 is still open about
 When the camera's SFU path is repointed to an unroutable address
  And the health watcher observes the stream fall to Degraded
 Then an audit row of kind StreamHealthChangedV1 appears, pivoted on
      stream/<camera identifier>, whose payload names that camera
  And that row's fab is null
```

**SC-2 (control on the instrument) — the same camera, the same pipeline, one
transition earlier, recorded `munich`.**

```gherkin
Given SC-1's camera reached Healthy before its fab was cleared
 When the audit rows pivoted on stream/<camera identifier> are read
 Then the earlier Provisioning→Healthy row's fab is "munich"
  And the later Healthy→Degraded row's fab is null
```

This is the load-bearing control and it is free: the arrangement produces both
rows on one camera. Without it, a null in SC-1 is indistinguishable from an
ingestion path that stamps null on everything, a broken `V1ResourceMap`, or a
fab column that was never written at all. **An assertion must not be able to
pass for the wrong reason** — this repository logged five assertions in one
week that could not fail.

### US1 — which read paths see it, and for whom

**SC-3 (the widened path, #2506) — the fab-scoped timeline returns it to a
munich operator.**

```gherkin
Given SC-1's null-fab row exists
 When admin@munich.test requests
      GET /audit/stream/{camera}?fabId=munich
 Then the response is 200
  And the returned rows include the null-fab StreamHealthChangedV1 row
```

**SC-4 (F3, observed) — the explicitly-fab-scoped *search* does not.**

```gherkin
Given SC-1's null-fab row exists
 When admin@munich.test requests
      GET /audit?fabId=munich&eventKind=StreamHealthChangedV1&pageSize=200
 Then the response is 200
  And the returned rows do not include the null-fab row
```

SC-3 and SC-4 together are F3 as evidence: the same caller, the same
authorized fab, two endpoints, two answers about one row. This pair is the
single most useful thing this spec hands the human.

**SC-5 (the pre-existing wider route) — the unscoped search returns it to an
operator of a *different* fab.**

```gherkin
Given SC-1's null-fab row exists, for a camera in munich
 When op-berlin@berlin.test requests
      GET /audit?eventKind=StreamHealthChangedV1&pageSize=200
 Then the response is 200
  And the returned rows include the null-fab row
```

**SC-6 (the widest route, and the untested one) — `GetSingle` returns it to a
different fab's operator with no fab check.**

```gherkin
Given SC-1's null-fab row exists, and its auditIdentifier is known
 When op-berlin@berlin.test requests GET /audit/{auditIdentifier}
 Then the response is 200
  And the returned row's fab is null
```

No integration test exercises `GET /audit/{auditIdentifier}` today. SC-6 is
therefore new coverage of the widest path as well as evidence for F2.

**SC-7 (the counterfactual on SC-6) — the same endpoint refuses a row that
*does* carry a foreign fab.**

```gherkin
Given SC-2's earlier munich-fab row, and its auditIdentifier is known
 When op-berlin@berlin.test requests GET /audit/{auditIdentifier}
 Then the response is 403 with title RESOURCE_FAB_NOT_AUTHORIZED
```

Without this, SC-6's `200` proves nothing: it would read identically against an
endpoint that has no authorization at all. This constructs exactly what the
guard claims to catch.

### US1 — auth boundary, unchanged

**SC-8 — a fab the caller does not hold is still refused on the widened path.**

```gherkin
Given SC-1's null-fab row exists
 When admin@munich.test requests GET /audit/stream/{camera}?fabId=berlin
 Then the response is 403 with title RESOURCE_FAB_NOT_AUTHORIZED
```

The widening changed *what an already-authorized caller's query returns*, never
*who is authorized*. Pinned here on the stream pivot; spec 215 already pins the
`400` for an omitted `fabId` and the `400` for a malformed one, and those are
referenced rather than duplicated.

### US1 — bad request

**SC-9 — the pivot identifier is not independently reachable across fabs.**

```gherkin
Given SC-1's camera is registered in munich
 When op-berlin@berlin.test requests GET /cameras?limit=200&includeRetired=true
 Then the response is 200
  And the listed cameras do not include that camera identifier
```

The falsifiable half of F2: the berlin operator can read the row (SC-5, SC-6)
but cannot learn the pivot from the camera catalogue, so the timeline route
#2506 opened adds no reachable data that the unscoped search did not already
hand over. If this goes red, F2 is wrong and the finding is larger than #2517
described — report it, do not adjust it.

### US2 — the fab-neutral contrast

**SC-10 — a real retention sweep's announcement lands with a null fab.**

```gherkin
Given an audit row back-dated well past the 90-day retention boundary, into a
      monthly chunk no other test seeds
 When the retention worker's next sweep archives and drops that chunk
 Then an audit row of kind AuditChunkArchivedV1 appears, pivoted on
      event/<chunk identifier>
  And that row's fab is null
  And the announcement payload's FabId is null
```

The second assertion is the one F1 predicts *and* the one nothing in the system
could make otherwise — which is why SC-11 exists.

**SC-11 (control) — the same sweep, the same ingestion path, records a fab when
one is given.**

```gherkin
Given a fab-carrying event has been published through the ordinary path in the
      same window
 Then its audit row's fab is not null
```

Reuses whatever fab-carrying row the run already has rather than seeding a new
one; the point is only that the ingestion path in this run is capable of
writing a non-null fab.

**SC-12 — the neutral row is reachable through the widened path too.**

```gherkin
Given SC-10's announcement row exists
 When admin@munich.test requests GET /audit/event/{chunkIdentifier}?fabId=munich
 Then the response is 200
  And the returned rows include the AuditChunkArchivedV1 row
```

This is #2506 working as intended, on the class it was written for — the
contrast that makes SC-3's identical result on SC-1's row the interesting one.

---

## Independent end-to-end test procedure

Runnable by a person with a booted stack and no knowledge of the tests.
Before trusting any of it: compare the AppHost process's start time against the
commit under test — a persistent stack keeps serving the binaries it booted
with.

1. Boot the AppHost. Mint a token for `admin@munich.test` / `Admin1234` at
   Aspire's **proxied** Keycloak endpoint (not the container's mapped port, or
   the issuer will not match and everything 401s).
2. `POST /cameras` in fab munich with `rtspUrl=rtsp://fixture-video:8554/loop`.
   Keep the returned camera identifier `C`.
3. Poll `GET /streams/{C}` until `state` is `Healthy` (up to ~30 s).
4. `GET /audit/stream/{C}?fabId=munich` → expect a `StreamHealthChangedV1` row
   with `"fab": "munich"`. **This is the control.** If it is already null, stop
   — the premise of the whole spec has moved and the finding is different.
5. Clear the fab in Postgres against the `stream-distribution` database:
   `UPDATE streams SET fab = NULL WHERE camera_id = '<C>';`
6. `PATCH http://<mediamtx>/v3/config/paths/patch/cam-<C>` with
   `{"source":"rtsp://10.0.6.1/h264"}`. Poll `GET /streams/{C}` until `state`
   is `Degraded` (up to ~15 s).
7. **The observation.** `GET /audit/stream/{C}?fabId=munich`. Expect **two**
   rows now: the earlier one with `"fab": "munich"`, and a new one with
   `"fab": null`. Record both verbatim. Keep the new row's `auditIdentifier`
   as `A`.
8. `GET /audit?fabId=munich&eventKind=StreamHealthChangedV1&pageSize=200` as
   the same user. Expect the null-fab row to be **absent** — F3, observed.
9. Mint a token for `op-berlin@berlin.test` / `Operator1234`.
   `GET /audit?eventKind=StreamHealthChangedV1&pageSize=200` → expect the
   null-fab row **present**. `GET /audit/{A}` → expect `200`.
   `GET /audit/<the munich row's auditIdentifier>` → expect `403`.
10. As the berlin operator, `GET /cameras?limit=200&includeRetired=true` →
    expect `C` **absent**.
11. Restore: `PATCH` the MediaMTX path back to
    `rtsp://fixture-video:8554/loop`.
12. (US2) Insert a row into `audit_events` with `occurred_at` ~400 days ago and
    `fab_id NULL`, wait for the retention sweep (seconds, under the E2E
    override), then `GET /audit?eventKind=AuditChunkArchivedV1&pageSize=200` →
    expect a row with `"fab": null` whose payload's `FabId` is `null`.

---

## Latency-budget impact

**N/A.** Constitution §IV's six legs are untouched: the audit read and write
paths sit on none of them, no production code changes, and no Aspire resource
is added or altered. Stated rather than omitted, because §IV's own record says
a leg exempted by clerical error is not hypothetical here. §VII's dashboard
obligation (ADR-0117) does not attach, because this spec implements no leg.

The health-watcher transition US1 provokes is on the **stream** path, not the
event→overlay path, and its timing is arrangement, not measurement — no figure
is cited, because a leg recorded as measured before anyone read its figure
claims a discharge nobody earned.

---

## Out of scope

| Not done here | Why |
|---|---|
| Deciding whether a null fab on a fab-owned resource is acceptable | The issue's own text makes it a human call; F4 shows it is an ADR-0102 amendment, which ADR-0144 forbids the lane. |
| Reconciling F3 — making the explicit-fab search and the timeline agree | The *remedy* for the policy question. Which way it goes is the decision itself. |
| Removing the dead `AuditChunkArchivedV1.FabId` component | A versioned `Shared.Contracts` change (ADR-0073), unrelated to the read paths, and only *found* by F1. Candidate follow-up. |
| Adding a fab guard to `GetSingle` for null-fab rows | F0 is pre-existing, is the *widest* path, and changing it is a security-control change with the same "which way?" problem as F3. |
| `ALTER TABLE streams ALTER COLUMN fab SET NOT NULL` | #1467, explicitly blocked on a precondition no migration can assert about itself. |
| Widening `EventMetadataFabDeclarationTests` to reach non-handler publishers | Its remarks explain why the scan is anchored on `Handle`/`HandleAsync`; a behavioural guard is the answer that spec chose, and this spec supplies it. |
| Any production code change | This spec adds tests and records a finding. |

---

## Success criteria

- **SC-A.** What a fab-owned resource's audit row carries when its fab is
  unresolved is **observed against a running system**, with the request and the
  response quoted in `verification.md`.
- **SC-B.** Which of the three read paths return that row, and to which
  operator, is observed — not derived from reading the predicates.
- **SC-C.** The instrument is proven able to record the other answer: SC-2's
  `munich` row and SC-7's `403` are counterfactuals, not decoration.
- **SC-D.** The two classes of null are told apart by evidence (US1 vs US2), so
  the human decides about **one** conflated class rather than two.
- **SC-E.** `EventMetadataFabDeclarationTests`'s remark that this case "is
  uncovered" is corrected in place. A limitation recorded as open after it is
  closed is the same clerical defect as §IV recording a built leg as unbuilt.
- **SC-F.** A follow-up issue exists carrying F1, F3 and the observed evidence,
  labelled for a human decision and **not** `agent:ready`, and this delivery
  stops there.
- **SC-G.** No file under `src/` changes, and no existing test assertion is
  edited to make anything pass.

## Assumptions, marked

- **G1 (assumption).** The manufactured null-fab stream is asserted to be the
  same *shape* as a genuine pre-016 row, not evidence that one exists in a live
  database. #1467 is the issue that would settle the latter, and it is open.
- **G2 (guess).** SC-5, SC-6 and SC-9 assume `op-berlin@berlin.test` holds
  `sse.audit.read` and belongs to `/fabs/berlin` only — the shape
  `CrossFabReadGuardIntegrationTests` already relies on. If it does not, the
  test must fail loudly on the control rather than quietly pass on an empty
  page.
- **G3 (guess).** US2's chosen back-date must land in a monthly chunk that
  `RetentionRoundtripIntegrationTests` (which seeds at −200 d and −120 d) does
  not touch, or the two classes race for one chunk. ~−400 d is proposed;
  verify against `timescaledb_information.chunks` rather than assuming.
- **G4 (assumption).** The delivery machine may not be able to boot Aspire in
  this pass. `tasks.md` is written so CI's Docker integration job can settle
  both stories on its own, and phase 5 says which run produced the evidence.
