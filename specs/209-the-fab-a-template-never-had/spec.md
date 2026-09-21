# Spec 209 — The fab a template never had

**Issue:** #2506 — *`OverlayRevisionPublishedV1`'s audit row carries a null `Fab`, unreachable by any fab-scoped timeline query*
**Branch:** `2506-overlay-published-null-fab`
**Base:** `origin/develop` @ `1e461ea2`
**Phase-4a colour:** **RED** (behaviour-changing). A fab-scoped timeline query that returns zero rows today must return the fab-neutral rows after. A test that arrives green is a phase-4 failure (ADR-0139, constitution §Testing).
**ADRs:** **ADR-0115** (overlays are fab-neutral templates — *the decision that makes the issue's proposed fix illegal*), **ADR-0102** (the common `EventMetadata` envelope, `Fab` documented as `null` when the event is not fab-scoped), **ADR-0114** (`FabResolution` — how a caller's fab is resolved at an endpoint), ADR-0040 + ADR-0073 (domain vs. integration events), ADR-0088 + ADR-0042 (Wolverine outbox / per-module queues — why the audit subscriber is off the hot path), ADR-0047 + ADR-0089 (`Result<T, Error>` / `ApiError`), ADR-0093 (Application folder layout), ADR-0091 + ADR-0094 (naming), ADR-0052 / ADR-0053 / ADR-0054 (xUnit + Shouldly, sentence-style names, hand-written fixtures), ADR-0065 (coverage gates), ADR-0103 (integration tests via the Aspire fixture), ADR-0105 (`Ensure.That` guards), ADR-0109 (`[P]` markers), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0037 (phases).
**Constitution:** §III (bounded-context isolation — no context boundary moves), §VII (observability — the audit trail *is* the record), §Testing (red for new behaviour).

**No new ADR is required, and the reason is the headline finding.** ADR-0115 already decided that an overlay has no fab and *must not gain one*. This spec does not overturn it, reopen it, or work around it — it makes the read path honour a decision the write path has been honouring correctly since 2026-08-10.

---

## Problem

Every line number, record shape and predicate below was **re-read in the working tree at HEAD `1e461ea2`**, not copied from the issue. The full-corpus sweep in §*The complete `Fab` sweep* was run independently of the issue's claim, and the correction in §*Where the issue is wrong* was found by it.

### The symptom is real and reproduces

`GET /audit/overlay/{O}?fabId=munich` returns zero rows for an overlay that was demonstrably published. This was observed live during #2429's phase-5 verification (`specs/206-a-row-no-timeline-can-reach/verification.md`) and the row's existence was confirmed by the cross-cutting search instead. That observation stands.

### But the cause is not where the issue says it is

The issue locates the defect at `OverlayRevisionPublishedDomainEventHandler.cs:44`:

```csharp
Metadata: new EventMetadata(Guid.CreateVersion7(), publishedAt, null, publishedBy.Value)),
```

and proposes that OverlayDesigner should stamp a fab. **ADR-0115 forbids exactly that**, and cites this exact line as *evidence for* the decision rather than as a defect:

> `OverlayRevisionPublishedDomainEventHandler` stamps `new EventMetadata(..., Fab: null, ...)` on the integration event that SystemVariables indexes from.

and its Decision section reads:

> **An overlay is a fab-neutral template.** A variable placeholder resolves in the fab of whoever is viewing it, not in a fab belonging to the overlay.

(The stronger phrasing "an overlay has no fab **and must not gain one**" is not ADR-0115's own wording — it is `FabsReferencingOverlayQueryHandler`'s doc comment below, which *cites* ADR-0115 rather than quoting it. ADR-0115's own §Consequences in fact leaves that door open for a future decision — see Assumption 2.)

The reasoning is recorded and still holds: an overlay saying `Line 1: {{oeeLine1}}` is the same design in every fab, and fab-owning it would force operators to author and hand-synchronise a copy per plant. `FabsReferencingOverlayQueryHandler` is the mechanism built on that decision — it answers "which fabs are told about this overlay" by *deriving* the set from the published layouts that reference it, and its doc comment says so verbatim:

> An overlay has no fab and must not gain one (ADR-0115) — it is a fab-neutral template that two plants may legitimately share.

So an overlay maps to **0..N fabs, recomputed per frame**, not to one. There is no single correct value to put in the `Fab` slot of `OverlayRevisionPublishedV1`, and inventing one would be the ADR-0115 failure mode restated: *a wrong claim of isolation is worse than a truthful absence of it.*

### Where the defect actually is

`src/AuditObservability/Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs:52-54`:

```csharp
IQueryable<AuditEventEntity> source = events.AuditEvents
    .Where(auditEvent => auditEvent.ResourceKind == resourceKindFilter)
    .Where(auditEvent => auditEvent.ResourceIdentifier == resourceIdentifierFilter)
    .Where(auditEvent => auditEvent.Fab == fabFilter);
```

The third predicate is an unconditional equality. `fabId` is a **required** query parameter (`AuditEndpoints.cs:103`, non-nullable `string`), so there is no way to omit it and no branch that ever relaxes the comparison. A row with `fab IS NULL` therefore satisfies no timeline query that any caller can construct.

**This is the second half of a fix that was only ever applied to one of two handlers.** Issue **#1300** — *"Cross-fab audit rows are invisible to every fab-assigned operator"* — found and fixed the identical predicate in `SearchAuditQueryHandler`. That file now reads (`:57-73`):

```csharp
else if (callerFabs.Count > 0)
{
    // Cross-fab rows (fab = null) are included, not excluded. They are
    // not restricted to a fab, so restricting who may read them by fab
    // made them readable by nobody: every operator belongs to a fab,
    // so the whole class of row was invisible to every real caller
    // (#1300).
    …
    source = source.Where(auditEvent => auditEvent.Fab == null || allowed.Contains(auditEvent.Fab));
}
```

and that comment even names the class this spec is about:

> What legitimately publishes without one is **overlay events, whose domain events carry no fab at all (ADR-0115)**, and retention, which spans fabs. A stream-health event's fab is nullable and may still arrive null (#2076).

The timeline handler was never brought into line. The rule — *"no fab" means "not fab-restricted", not "restricted to nobody"* — is already this system's decided position on null-fab audit rows; it is implemented in two of the three read paths and missing from the third.

### Already-settled precedent in the third read path

`GetSingle` (`AuditEndpoints.cs:181`) reads:

```csharp
if (row.Fab is not null)
{
    await fabGuard.EnsureAccessAsync(user, row.Fab, cancellationToken);
}
return Results.Ok(row);
```

A null-fab row is returned to **any** holder of `sse.audit.read`, with no fab check at all. So including null-fab rows in a fab-scoped timeline **exposes nothing that is not already exposed** by row id and by search. This is the security argument, and it rests on existing behaviour rather than on a judgement call.

---

## The complete `Fab` sweep

The issue asks for it explicitly: *which publishers stamp `Fab` and which don't, across all ~20 integration-event contracts.* Done, and **the sweep is what proves the fix is narrow.**

**Method, and a correction to it.** The obvious grep is `new EventMetadata(`. It returns 19 sites and **misses two**, because `ResolvedOverlayTextChangedV1` is constructed with a target-typed `Metadata: new(...)`. Re-run against the invariant half of the shape — the `Metadata:` argument label, which every construction site must spell — it returns **21**. Both figures were taken; only the second is used. (CLAUDE.md's house lesson on grepping the invariant half of a shape; the under-count was real here, not hypothetical.)

`src/Shared.Contracts/` holds **20** concrete `IIntegrationEvent` records (`ls src/Shared.Contracts/*/`). **All 20 carry an `EventMetadata`** — verified by asserting the absence of any contract file without the token, which returned nothing.

| # | Contract | Publisher | Stamps `Fab`? | Verified how |
|---|---|---|---|---|
| 1 | `AuditChunkArchivedV1` | `AuditObservability/…/AuditRetentionHostedService.cs:145` | **No — correct** | Literal `null`. Retention spans fabs; the contract carries its own optional `FabId` field for the chunk's own scope (`AuditChunkArchivedV1.cs:14`). |
| 2 | `CameraAddressChangedV1` | `CameraCatalog/…/CameraAddressChangedDomainEventHandler.cs:35-39` | Yes | `domainEvent.Fab.Value` |
| 3 | `CameraRegisteredV1` | `CameraCatalog/…/CameraRegisteredDomainEventHandler.cs:33-37` | Yes | `domainEvent.Fab.Value` |
| 4 | `CameraRenamedV1` | `CameraCatalog/…/CameraRenamedDomainEventHandler.cs:34-38` | Yes | `domainEvent.Fab.Value` |
| 5 | `CameraRetiredV1` | `CameraCatalog/…/CameraRetiredDomainEventHandler.cs:33-37` | Yes | `domainEvent.Fab.Value` |
| 6 | `FabEventIngestedV1` | `EventIngestion/…/EventIngestedDomainEventHandler.cs:58` | Yes | `fab.Value` |
| 7 | `DeviceRegisteredV1` | `Identity/…/ClientRegisteredDomainEventHandler.cs:49` | Yes | `fab.Value` |
| 8 | `KioskEnrolledV1` | `Identity/…/ClientRegisteredDomainEventHandler.cs:63` | Yes | `fab.Value` |
| 9 | `WebhookIntegrationRotatedV1` | `Identity/…/RotateWebhookClientCommandHandler.cs:150` | Yes | `fab.Value` |
| 10 | `LayoutRevisionArchivedV1` | `LayoutComposition/…/LayoutRevisionArchivedDomainEventHandler.cs:33` | Yes | `fab.Value` (#2071) |
| 11 | `LayoutRevisionPublishedV2` | `LayoutComposition/…/LayoutRevisionPublishedDomainEventHandler.cs:54` | Yes | `fab.Value` (#2071) |
| 12 | `OverlayHighlightRequestedV1` | `Automation/…/FabEventIngestedV1Handler.cs:113-114` | Yes | `fab` — Automation is fab-scoped and the *request* has one even though the overlay does not. |
| 13 | **`OverlayRevisionArchivedV1`** | `OverlayDesigner/…/OverlayRevisionArchivedDomainEventHandler.cs:31-35` | **No — correct (ADR-0115)** | Literal `null` at `:34`. **Not named by the issue.** |
| 14 | **`OverlayRevisionPublishedV1`** | `OverlayDesigner/…/OverlayRevisionPublishedDomainEventHandler.cs:44` | **No — correct (ADR-0115)** | Literal `null`. The filed instance. |
| 15 | `StreamHealthChangedV1` | `StreamDistribution/…/StreamHealthChangedDomainEventHandler.cs:53-57` | **Conditionally** | `domainEvent.Fab?.Value` — `StreamHealthChangedDomainEvent.Fab` is `FabIdentifier?` (`:30`). May arrive null; #2076 (closed) is the record of why. |
| 16 | `ResolvedOverlayTextChangedV1` | `SystemVariables/…/VariableArchivedDomainEventHandler.cs:108-112` | Yes | `fab.Value` (#2068) — **missed by the naive grep** |
| 17 | `ResolvedOverlayTextChangedV1` | `SystemVariables/…/VariableValueChangedDomainEventHandler.cs:81-86` | Yes | `fab.Value` (#2068) — **missed by the naive grep** |
| 18 | `SystemVariableArchivedV1` | `SystemVariables/…/VariableArchivedDomainEventHandler.cs:40` | Yes | `fab.Value` |
| 19 | `SystemVariableDefinedV1` | `SystemVariables/…/VariableDefinedDomainEventHandler.cs:34` | Yes | `fab.Value` |
| 20 | `SystemVariableValueChangedV1` | `SystemVariables/…/VariableValueChangedDomainEventHandler.cs:43` | Yes | `fab.Value` |
| 21 | `SystemVariableValueRequestedV1` | `Automation/…/FabEventIngestedV1Handler.cs:103-104` | Yes | `fab` |

**21 construction sites, 20 contracts.** (`ResolvedOverlayTextChangedV1` has two publishers; `DeviceRegisteredV1` and `KioskEnrolledV1` share one file.)

### What the sweep concludes

**No publisher is missing a `Fab` it ought to stamp.** Seventeen of the twenty-one sites pass one. The four that do not are each *correct*:

| Class | Sites | Why null is right |
|---|---|---|
| **Overlay lifecycle** | 13, 14 | ADR-0115 — the aggregate is fab-neutral by decision. `grep -rn "Fab" src/OverlayDesigner` returns **zero lines**, re-run at `1e461ea2`. |
| **Retention** | 1 | A retention chunk spans fabs by construction. |
| **Stream health, unattributable** | 15 | The fab is derived from the camera, and a decommissioned camera cannot be read back (#2076). |

**Therefore the fix is narrow — but narrow in `AuditObservability`, not in `OverlayDesigner`.** The issue asked whether the fix is "just `OverlayRevisionPublishedV1`" or "a systemic gap across multiple publishers". The answer is **neither**: it is one predicate in one query handler, and that one predicate fixes all four classes above at once, for every one of the eleven resource kinds.

---

## Where the issue is wrong, and it matters

**The proposed fix would violate an accepted ADR.** Stamping a fab on `OverlayRevisionPublishedV1` — whether by giving the aggregate a fab or by threading one down from the HTTP request — is precisely what ADR-0115 rejected, and rejected *with this line of code as its evidence*. Had this been delivered as filed, it would have:

1. keyed a fab-neutral template under whichever fab the publisher happened to be signed into, making `GET /audit/overlay/{O}?fabId=berlin` return nothing for an overlay berlin is actively displaying;
2. contradicted `FabsReferencingOverlayQueryHandler`, which derives 0..N fabs for the same overlay and would now disagree with the single fab on the audit row;
3. required a domain change, a migration and a backfill in a context whose domain deliberately has no fab concept.

**The issue's second claim also needs correcting.** It says CameraCatalog "confirms this is specific to OverlayDesigner's publish handler, not a property of the timeline endpoint itself." The sweep shows the opposite: it is **entirely** a property of the timeline endpoint. CameraCatalog's rows are reachable *because* they happen to carry a fab, not because the endpoint is sound. Four distinct publisher classes across three contexts produce legitimately fab-neutral rows, and the timeline is unreachable for every one of them.

**And the filed instance is incomplete.** `OverlayRevisionArchivedV1` has the same null `Fab` at `OverlayRevisionArchivedDomainEventHandler.cs:34` and is not mentioned. Under the issue's own proposed fix it would have been left behind; under this one it is covered without being touched.

---

## Blessed — checked, and deliberately left alone

- **`OverlayDesigner`.** Not one file is edited. Re-verified: `grep -rn "Fab" src/OverlayDesigner` → zero lines.
- **`Shared.Contracts`.** No contract shape changes. `EventMetadata.Fab` stays `string?` and its doc comment ("Owning fab when the event is fab-scoped; otherwise `null`", `EventMetadata.cs:16`) already describes the behaviour exactly. No `V2`, no ADR-0073 question.
- **`SearchAuditQueryHandler`.** Already correct (#1300). Untouched — but it is not simply "the model the fix copies". Its *unscoped* branch (no `fabId` at all) already includes fab-neutral rows, and that is the shape this fix's reasoning starts from; its *named-fab* branch does the opposite — it still excludes fab-neutral rows when a caller explicitly names a fab (`Naming_a_fab_still_excludes_cross_fab_rows`), because omitting `fabId` there is the unscoped route and the escape hatch. The timeline endpoint has no unscoped route — `fabId` is required — so there is no escape hatch, and that absence, not sameness with Search's named-fab branch, is the actual justification for widening equality here. See §*Fix direction*.
- **`GetSingle`.** Already correct. Untouched.
- **The `ix_audit_resource_occurred` index** (`AuditEventConfiguration.cs:148-149`) is `(ResourceKind, ResourceIdentifier, OccurredAt)` — **no fab column**. The fab predicate is already a post-index filter, so widening it to an `OR` changes no query plan. **No migration, no index change, no EF migration of any kind.**
- **`StreamHealthChangedDomainEventHandler`'s `Fab?`** stays nullable. #2076 is closed; re-litigating stream fab attribution is not this spec.
- **The management-web audit UI.** `AuditPage.tsx` calls `useSearchAuditQuery` only (`:2`, `:30`) — the **timeline endpoint has no frontend consumer at all**. No `apps/` file changes, and there is no e2e spec to write.

---

## Fix direction — the decision

**One predicate, in one handler, applying the rule #1300 already installed next door — but for a different reason than sameness.** #1300's named-fab branch in `SearchAuditQueryHandler` still *excludes* fab-neutral rows, because that endpoint has an unscoped route (no `fabId`) as its escape hatch. This endpoint has none — `fabId` is required — so equality here excluded the whole fab-neutral class from every caller, with no escape hatch at all. That absence is the justification; it is not the same situation as Search's named-fab branch, even though the fix widens the predicate the same way Search's *unscoped* branch already does.

```csharp
// Fab-neutral rows (fab = null) are included, not excluded — unlike
// SearchAuditQueryHandler's named-fab branch, which still excludes them when
// a caller explicitly names a fab (Naming_a_fab_still_excludes_cross_fab_rows,
// #1300), because omitting fabId there is the unscoped route that already
// returns them. Here fabId is required, so there is no unscoped route:
// equality excluded the whole fab-neutral class from every caller, with no
// escape hatch. Overlay lifecycle events legitimately carry no fab
// (ADR-0115), alongside retention events, which span fabs, and unattributable
// stream-health events (#2076).
.Where(auditEvent => auditEvent.Fab == null || auditEvent.Fab == fabFilter);
```

**Why not the alternative — make `fabId` optional and fall back to caller fabs, as Search does.** Rejected. It changes the endpoint's contract (a required parameter becomes optional), adds a caller-fabs resolution path to a handler that has none, and does not fix the filed defect on its own: a caller who *does* pass `?fabId=munich` would still see nothing. The fab-neutral row must be reachable *through* an explicit fab, because that is the only shape the endpoint offers. One predicate does that; the API change does not.

**Why not stamp a fab in OverlayDesigner.** §*Where the issue is wrong* (1). ADR-0115.

---

## Locked technical choices

- **Backend only.** `src/AuditObservability/Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs`, plus tests. No `apps/`, no `deploy/`, no `.csproj`, no migration.
- **No context boundary moves.** AuditObservability already depends on `Shared.Contracts` and on nothing else cross-context (constitution §III). `NetArchTest` boundary rules are unaffected.
- **EF Core against Postgres**, LINQ predicate — the `Fab == null ||` shape is already proven translatable by `SearchAuditQueryHandler.cs:73`, which runs the same comparison against the same value-converted column.
- **xUnit + Shouldly + hand-written builders** (ADR-0052/0054). Unit tests extend `tests/AuditObservability.Application.Tests/Queries/Handlers/GetResourceTimelineQueryHandlerTests.cs`; the integration test extends `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`, which already seeds a `Row(fab: null)` and already exercises this endpoint for auth.
- **Aspire fixture for integration** (ADR-0103). No Testcontainers.
- **`Ensure.That`** guards unchanged (ADR-0105). The handler's existing guard stays.
- **Coverage gates** unchanged (ADR-0065): Application ≥ 80%. The change is one predicate inside an already-covered method.

---

## Latency-budget impact

**N/A — no leg of constitution §IV is touched.**

`GET /audit/{kind}/{id}` is an operator-facing read on the management API. It is not on the `event arrival → overlay rendered` path at any point: AuditObservability subscribes on its own isolated Wolverine queue (ADR-0088) and publishes nothing back into the overlay path. §VII's dashboard obligation binds implemented legs; this spec implements none and erodes none. No figure is cited because none is affected — and per CLAUDE.md, a leg recorded as measured before anyone read its figure claims a discharge nobody earned, so nothing is claimed here.

---

## Assumptions

Marked explicitly, as unavoidable guesses must be (CLAUDE.md §Karpathy).

1. **There is no already-written bad data to repair, and none will be migrated.** This repository has no production deployment — the Aspire k8s publisher has never been run and no k8s package is referenced (CLAUDE.md §*What lives where*; `specs/047-the-decisions-we-made/audit.md:373`, issue **#1015**). Audit rows written by dev and CI stacks are disposable. **No backfill, no data migration, no EF migration of any kind is in scope**, and the decision is deliberately *not* being made here for a hypothetical future deployment. This assumption is *weaker* here than it was for #2429: this fix writes nothing new and changes no column, so rows already written become reachable the moment the predicate ships. There is no repair to perform even in principle. Stated anyway, matching `specs/206-a-row-no-timeline-can-reach/spec.md` §Assumptions (1), because a silently-skipped backfill question is how the next reader assumes one was needed.
2. **ADR-0115 still holds and this spec does not reopen it.** Its own §Consequences leaves a door open — *"If OverlayDesigner later gains a fab for reasons of its own — ownership, access control over who may edit a design — this decision is not contradicted… That would be a new decision."* Nothing in #2506 supplies such a reason, and the autonomous lane may not write an ADR (CLAUDE.md §*Three things the lane may not do*). If a future feature does fab-own overlays, this fix is untouched by it: a row that *acquires* a fab simply stops needing the `Fab == null` branch.
3. **A fab-scoped timeline is expected to answer "everything about this resource that this fab can see", not "everything this fab caused".** This is the semantics the fix installs, and it is the semantics `SearchAuditQueryHandler`'s *unscoped* branch already has. The alternative reading — that naming a fab should show only rows scoped to that fab, fab-neutral rows excluded — is not hypothetical: it is exactly what `SearchAuditQueryHandler`'s own named-fab branch does today, pinned by a passing test (`Naming_a_fab_still_excludes_cross_fab_rows`, "asking for one fab is a narrower question than 'what may I see'"). It is a live option in general; it is simply the wrong one *for this endpoint*, because unlike Search this endpoint has no unscoped route to fall back to — naming a fab is the only question a caller can ask it, so excluding fab-neutral rows here would leave the filed defect unfixed by construction. Recorded so the choice is visible rather than implied.
4. **`fabId` omitted from the timeline route yields `400`, not a wide-open query.** `[FromQuery] string fabId` is non-nullable, so ASP.NET refuses the request before the handler runs. Pinned as an acceptance scenario (§*US1 — bad request*) rather than assumed, precisely because the fix relaxes a fab predicate and a reader must be able to see that the *parameter* did not also become optional.

---

## User stories

### US1 (P1) — A fab-neutral audit row is reachable from a fab-scoped timeline

**As** an operator assigned to `munich` holding `sse.audit.read`,
**when** I ask for an overlay's audit timeline scoped to my fab,
**then** I see the overlay's publish and archive rows — the ones that carry no fab because an overlay belongs to no fab — alongside any `munich`-scoped rows about the same overlay,
**and** I still see nothing belonging to `berlin`.

Independently shippable, and the whole of #2506. One predicate; one unit test pair; one integration test.

### US2 (P2) — The rule is written down where the next reader will look

**As** the next engineer to touch either audit read path,
**when** I read the timeline handler's fab predicate,
**then** a comment tells me *why* null is included, names the three legitimate producer classes and cites #1300 and ADR-0115 — the same way `SearchAuditQueryHandler.cs:63-72` does.

Not cosmetic. This exact rule was decided once, implemented in one of two handlers, and the other drifted for the seven months since. A predicate with no reason attached is how it drifts again — and the Search comment's own value is proved by this spec, which found the decision by reading it.

---

## Acceptance scenarios

### US1 — the happy path (the filed defect)

```gherkin
Feature: A fab-neutral audit row is reachable from a fab-scoped timeline

  Scenario: an overlay's publish row appears in its fab-scoped timeline
    Given an audit row for resource kind "overlay" and identifier "O" with no fab
    And an operator assigned to fab "munich" holding scope "sse.audit.read"
    When the operator requests GET /audit/overlay/O?fabId=munich
    Then the response is 200
    And the page contains the row
    And the row's fab is reported as null
```

### US1 — the mixed timeline

```gherkin
  Scenario: fab-neutral and fab-scoped rows about the same overlay both appear
    Given an audit row for "overlay"/"O" with no fab, occurring first
    And an audit row for "overlay"/"O" with fab "munich", occurring second
    When an operator assigned to "munich" requests GET /audit/overlay/O?fabId=munich
    Then both rows are returned, ascending by occurredAt
```

This is not hypothetical: `ResolvedOverlayTextChangedV1` and `OverlayHighlightRequestedV1` both pivot on kind `overlay` (`V1ResourceMap.Conventions.cs:68`, `:76`) and both carry a fab, while the lifecycle events pivot on the same overlay and do not. A real overlay timeline is mixed.

### US1 — conflict: another fab's rows stay out

```gherkin
  Scenario: a fab-scoped row belonging to another fab is still excluded
    Given an audit row for "overlay"/"O" with fab "berlin"
    And an audit row for "overlay"/"O" with no fab
    When an operator assigned to "munich" requests GET /audit/overlay/O?fabId=munich
    Then the page contains the fab-neutral row
    And the page does not contain the "berlin" row
```

**This is the scenario that proves the fix is a widening and not a removal.** A predicate deleted rather than widened would pass every other scenario here and fail this one.

### US1 — conflict: the other resource kinds do not leak

```gherkin
  Scenario: the widened predicate does not cross resource boundaries
    Given an audit row for "camera"/"C" with no fab
    When an operator assigned to "munich" requests GET /audit/overlay/O?fabId=munich
    Then the page does not contain the camera row
```

### US1 — bad request

```gherkin
  Scenario: fabId is still required
    Given an operator assigned to "munich" holding scope "sse.audit.read"
    When the operator requests GET /audit/overlay/O with no fabId
    Then the response is 400

  Scenario: an unknown resource kind is still refused
    When the operator requests GET /audit/not-a-kind/O?fabId=munich
    Then the response is 400 with code "AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND"

  Scenario: a malformed fabId is still refused
    When the operator requests GET /audit/overlay/O?fabId= (empty)
    Then the response is 400 with title "AUDIT_INVALID_INPUT"
```

### US1 — auth

```gherkin
  Scenario: a caller without the scope is refused before any row is read
    Given a caller without scope "sse.audit.read"
    When the caller requests GET /audit/overlay/O?fabId=munich
    Then the response is 401 or 403
    And no row is disclosed

  Scenario: a caller not assigned to the requested fab is refused
    Given an operator assigned only to "munich"
    When the operator requests GET /audit/overlay/O?fabId=berlin
    Then the response is 403
    And no fab-neutral row is disclosed either
```

**The second scenario is the security boundary and must be asserted explicitly.** The fab guard runs *before* the handler (`AuditEndpoints.cs:114`), so widening the handler's predicate must not turn a 403 into a 200-with-fab-neutral-rows. `CrossFabReadGuardIntegrationTests.cs:31-32` already asserts the 403; it must still pass **unmodified**.

### US2 — the comment

```gherkin
  Scenario: the rule carries its reason
    Given the widened predicate in GetResourceTimelineQueryHandler
    Then a comment immediately above it names #1300, ADR-0115,
      and the three legitimate fab-neutral producer classes
```

---

## Independent end-to-end test procedure

Observable without reading a test file, through the running Aspire stack. This is phase 5's script.

1. Boot the AppHost (one stack per machine — a second concurrent boot produces `FailedToStart` that reads exactly like a code defect).
2. Mint a token for an operator assigned to `/fabs/munich` holding `sse.audit.read`, **from Aspire's proxied Keycloak endpoint**, not the container's mapped port.
3. `POST /overlays` to create a draft, then publish revision 1. Note the overlay identifier `{O}`.
4. Wait for the outbox to drain (the audit subscriber is on its own queue).
5. **Before** confirming, establish the row exists at all — `GET /audit?resourceKind=overlay&resourceIdentifier={O}` → the `OverlayRevisionPublishedV1` row, with `fab: null`. This is the control, and it is the same route by which #2506 was originally found.
6. **The assertion:** `GET /audit/overlay/{O}?fabId=munich` → **200 containing that row**. On `origin/develop` this returns an empty page; that contrast is the verification note's evidence and both halves must be run and quoted.
7. `GET /audit/overlay/{O}?fabId=berlin` as the same `munich`-only operator → **403**, not an empty 200 and not a 200 containing the fab-neutral row.
8. `GET /audit/overlay/{O}` with no `fabId` → **400**.

Step 6 alone is the fix. Steps 5, 7 and 8 are what stop a green step 6 from being an accident.

---

## Out of scope

1. **Giving OverlayDesigner a fab.** ADR-0115. Would require a new ADR, which the autonomous lane may not write.
2. **Making `fabId` optional on the timeline endpoint.** §*Fix direction*. A real API question, but a different one, and not needed to close #2506. **Recommend filing** if a caller ever wants an unscoped timeline.
3. **Stream fab attribution (#2076, closed).** `StreamHealthChangedV1.Metadata.Fab` stays conditionally null. This fix makes those rows reachable, which is the most that can be done without re-opening a closed decision.
4. **A `chunk` resource kind for `AuditChunkArchivedV1`.** Already considered and declined in `specs/206-a-row-no-timeline-can-reach` §Blessed. Unchanged; this fix simply makes the existing `event`-kind chunk row reachable.
5. **Backfill or migration.** §Assumptions (1).
6. **Any frontend change.** The timeline endpoint has no consumer in `apps/`.
7. **An architecture test that asserts the two audit read paths agree on the null-fab rule.** Tempting, and it is the shape of guard that would have caught this in 2026-02. But a source-scanning guard that greps two files for a predicate proves the *design was written down*, not that it holds (CLAUDE.md house lesson; `guards-that-read-the-design-artefact`). The behavioural test in US1 asks the running system instead. **Recommend filing** a properly behavioural cross-handler guard as its own issue if the drift recurs.
8. **`Option<T>` migration** of any signature touched (ADR-0141 is advisory; existing signatures are not a defect).

---

## File contention

| File | Owner | Note |
|---|---|---|
| `src/AuditObservability/Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs` | US1 + US2 | The only production file. **Serialised** — US2's comment lands in the same edit region as US1's predicate. |
| `tests/AuditObservability.Application.Tests/Queries/Handlers/GetResourceTimelineQueryHandlerTests.cs` | US1 | Existing file, extended. |
| `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs` | US1 | Existing file, extended. Already seeds `Row(fab: null)` and already calls this endpoint. |

No other file in the repository is opened. There is no cross-context contention and nothing for another agent to collide with.
