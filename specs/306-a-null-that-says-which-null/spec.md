# Spec 306 — A null that says which null

**Issue:** #2540 — "Null-fab audit rows on fab-owned resources are readable
cross-fab via 2 of 3 read paths — human decision needed"
**Branch:** `fix/2540-mark-unresolved-fab-audit-rows`
**Lane:** autonomous (ADR-0144), `agent:ready`
**Decision implemented (human, recorded on #2540):** *"add an explicit marker
distinguishing 'genuinely fab-neutral' from 'fab-owned but unresolved'"* —
option 2 of the issue. This spec implements it; it does not re-decide it.
**Predecessor:** spec 217 (`specs/217-the-null-a-fab-column-cannot-explain/`),
whose F1 established that exactly one fab-owned class can land with a null fab
(`StreamHealthChangedV1`) and that `AuditChunkArchivedV1` is neutral by
construction.
**ADRs:** ADR-0037, ADR-0044 (no shared VOs across contexts), ADR-0052/0053/0054,
ADR-0067 (MigrationRunner), ADR-0101 (TimescaleDB audit store), **ADR-0102 (the
envelope — read, not amended; see §Why no ADR)**, ADR-0103, ADR-0109,
**ADR-0115 (overlays are fab-neutral)**, ADR-0139, ADR-0144
**Constitution:** §II (no primitives on the domain model), §III, §VIII,
§Testing

---

## Problem

`audit_events.fab_id IS NULL` currently means two things:

| Meaning | Event types today | Source of the null |
|---|---|---|
| **Not applicable** — the subject has no fab | `OverlayRevisionPublishedV3`, `OverlayRevisionArchivedV1` (ADR-0115); `AuditChunkArchivedV1` (spec 217 F1: time-only hypertable partitioning) | publisher passes a literal `null` |
| **Unresolved** — the subject has a fab nobody resolved | `StreamHealthChangedV1` (`StreamHealthChangedDomainEventHandler.cs:56`, `domainEvent.Fab?.Value`); historically any fab-owned V1 written before spec 082 stamped fabs | runtime absence |

Nothing in the row, the DTO, or the schema distinguishes them. Every read-path
predicate that mentions `Fab == null` (`SearchAuditQueryHandler.cs:73`,
`GetResourceTimelineQueryHandler.cs:64`, `AuditEndpoints.GetSingle`) therefore
treats both identically, and a future predicate change cannot tell them apart
either.

## What this spec delivers — and what it deliberately does not

**Delivers:** an explicit, persisted, API-visible marker on every audit row —
`FabAttribution` — with three values, set once at write time:

| `FabAttribution` | Condition at write | `fab_id` |
|---|---|---|
| `Resolved` | the envelope carried a fab | not null |
| `Unresolved` | no fab, and the event type is fab-owned (the default) | null |
| `NotApplicable` | no fab, and the event type is registered fab-neutral | null |

Existing rows are backfilled by the migration with the same rule.

**Does not deliver:** any change to *which rows each read path returns, or to
whom*. The decision on #2540 is the labelling option, not "close the exposure".
Paths 3 and 4 of the issue's table (unscoped search and `GetSingle` returning an
unresolved row to another fab's operator) stay exactly as they are, and the two
tests that pin them stay green with **unchanged assertions**; only their doc
comments change (FR-007). Closing the exposure is a separate decision for a
human, which the marker now makes expressible without conflating the neutral
case (see §Out of scope).

## Why no ADR

ADR-0102's Decision already contains the distinction: `Fab` is *"owning fab,
when the event is fab-scoped"*, and populated *"where available, else null"*.
Whether an event **type** is fab-scoped is a property already decided elsewhere
— ADR-0115 for overlays, spec 217 F1 for retention chunks. This spec:

- does **not** change `EventMetadata` or any `Shared.Contracts` type;
- does **not** change any publisher or restrict which publishers may pass null;
- records, inside AuditObservability only, which V1 types are fab-neutral, and
  stamps the row accordingly.

Spec 217 F4 named three shapes that *would* amend ADR-0102 — a new envelope
component, a sentinel, or a rule constraining publishers. This design is none of
them: it is a consumer-side classification over the envelope as written.
**Marked judgement (J1):** if a reviewer reads the neutral-type register as an
ADR-0102 amendment, the lane must block (ADR-0144) and a human writes the
amendment; the implementation below does not change.

---

## Locked tech choices

Nothing new is chosen.

| Concern | Choice | Where |
|---|---|---|
| Audit store | PostgreSQL + TimescaleDB 2.27.1 hypertable, columnstore-compressed after 30 d | ADR-0101; `AppHost.cs:83-84`; initial migration |
| Schema change | EF Core migration run by `MigrationRunner` | ADR-0067; precedent `20260831164239_AuditIngestBreakdownColumns` |
| Domain modelling | C# `enum` on the domain row (precedent `EventIngestion.Domain.WebhookIntegration.BearerValidationMode`); invariant enforced in `AuditEvent.From` | §II, ADR-0038 |
| Tests | xUnit + Shouldly + hand-written fakes; integration via `AspireFixture` | ADR-0052/0103 |
| Naming | sentence-style | ADR-0053 |

---

## User stories

### US1 (P1) — an audit row says which kind of null its fab is

**As** whoever next changes an audit read predicate (or the human deciding
whether to close the #2540 exposure),
**I want** every audit row to state whether its fab was resolved, unresolved,
or not applicable,
**so that** "nobody's data" and "somebody's data we failed to attribute" are no
longer one column value, and a predicate can target one without the other.

One vertical slice: domain + handler + persistence + DTO, observable end to end
through `GET /audit`, `GET /audit/{id}` and the timeline. There is no P2 — the
backfill is part of US1 because without it historical rows would carry a
default that is wrong for neutral rows.

---

## Acceptance scenarios

### Happy path

**SC-1 — a fab-owned row whose fab was not resolved is marked `Unresolved`.**

```gherkin
Given a munich camera whose stream reached Healthy, then had its fab cleared
  And the stream fell to Degraded (spec 217's arrangement)
 When admin@munich.test reads GET /audit/stream/{camera}?fabId=munich
 Then the Healthy→Degraded StreamHealthChangedV1 row has fab null
  And its fabAttribution is "Unresolved"
```

**SC-2 (control) — the same camera's earlier row is `Resolved`.**

```gherkin
Given SC-1's arrangement
 Then the Provisioning→Healthy row has fab "munich"
  And its fabAttribution is "Resolved"
```

**SC-3 — a genuinely neutral row is marked `NotApplicable`.**

```gherkin
Given a real retention sweep archived a back-dated chunk (spec 217 US2)
 When the AuditChunkArchivedV1 announcement row is read via GET /audit
 Then its fab is null
  And its fabAttribution is "NotApplicable"
```

**SC-4 (control) — a fab-carrying row in the same run is `Resolved`.**

```gherkin
Given the CameraRegisteredV1 row spec 217's SC-11 already captures
 Then its fabAttribution is "Resolved"
```

SC-1 and SC-3 together are the point: **two rows with the same `fab: null`,
two different markers**. Neither assertion can pass against a constant.

### Conflict (fab and scope disagree)

**SC-5 — a type registered neutral that nevertheless carries a fab is
`Resolved`.** (Unit-level; no publisher does this today.)

```gherkin
Given an envelope for a fab-neutral event type that carries fab "munich"
 When the audit row is built
 Then its fab is "munich" and its fabAttribution is "Resolved"
```

The fab is evidence; a non-null fab is never discarded or relabelled.

**SC-6 — an event type nobody classified is treated as fab-owned.**

```gherkin
Given an envelope with no fab for an event type absent from the neutral register
 When the audit row is built
 Then its fabAttribution is "Unresolved"
```

Fail-safe direction: forgetting to register a neutral type over-reports
(`Unresolved`), it never hides an unattributed fab-owned row as `NotApplicable`.

### Bad request / malformed input

**SC-7 — no new input surface.** The marker is not a query parameter and is not
accepted from callers; requests are unchanged, and the existing `400`s for
`fabId` (spec 215) are referenced, not duplicated. A V1 with an invalid fab
string still fails in `FabIdentifier.From` exactly as today.

### Auth — unchanged, and proven unchanged

**SC-8 — the cross-fab exposure is deliberately untouched.** Spec 217's
`An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator` and
`Get_single_returns_the_null_fab_row_to_another_fabs_operator` pass with
**byte-identical assertions**; the control `Get_single_refuses_the_same_cameras_munich_row_to_that_operator`
(`403`) and `A_cross_fab_timeline_is_still_refused` (`403`) pass unchanged.

### Backfill

**SC-9 — historical rows are classified by the same rule.**

```gherkin
Given audit rows written before the migration
 When the migration runs
 Then every row with a fab is "Resolved"
  And every fab-less row whose event_kind is AuditChunkArchivedV1,
      OverlayRevisionArchivedV1, or OverlayRevisionPublishedV1/V2/V3 is "NotApplicable"
  And every other fab-less row is "Unresolved"
```

Observed in phase 5 against the persistent dev volume (run-mode Postgres keeps
months of history), not asserted by a test that seeds its own rows.

---

## Functional requirements

- **FR-001** `AuditEvent` exposes `FabAttribution` (non-nullable), set only by
  `AuditEvent.From`; invariant: `Resolved` ⇔ `Fab` not null.
- **FR-002** `V1Envelope` carries the event type's `FabScope` (`Owned` |
  `Neutral`), defaulting to `Owned`.
- **FR-003** AuditObservability.Application holds the single register of
  fab-neutral V1 types: `OverlayRevisionPublishedV3`, `OverlayRevisionArchivedV1`,
  `AuditChunkArchivedV1`. `IntegrationEventAuditHandler` reads it when building
  the envelope.
- **FR-004** `audit_events.fab_attribution` — `varchar(16) NOT NULL DEFAULT
  'Unresolved'`, written explicitly by `AuditEventRepository`'s insert;
  backfilled per SC-9.
- **FR-005** `AuditRowDto` gains `FabAttribution` (string: `"Resolved"` |
  `"Unresolved"` | `"NotApplicable"`), returned on all three read paths.
- **FR-006** No read predicate, endpoint, guard, contract, or publisher changes.
- **FR-007** The two spec-217 exposure facts keep their assertions; their doc
  comments are rewritten to record that #2540 was settled by labelling and that
  any future exposure fix must key on `fabAttribution`, not `Fab == null`.
- **FR-008** Doc comments that state the conflation are corrected:
  `AuditObservability.Domain.AuditEvent.FabIdentifier` and `AuditEvent.Fab`.

## Independent end-to-end test procedure

1. Boot the AppHost (check the AppHost PID start time against the commit).
2. Run spec 217's procedure steps 1–7. In step 4 the munich row shows
   `"fabAttribution": "Resolved"`; in step 7 the new row shows `"fab": null,
   "fabAttribution": "Unresolved"`.
3. Step 9 (berlin operator): `GET /audit/{A}` still returns `200` with
   `"fabAttribution": "Unresolved"` — the exposure is unchanged, and now labelled.
4. Spec 217 step 12: the `AuditChunkArchivedV1` row shows `"fab": null,
   "fabAttribution": "NotApplicable"`.
5. Against the `audit-observability` database:
   `SELECT fab_attribution, fab_id IS NULL AS no_fab, event_kind, count(*) FROM audit_events GROUP BY 1,2,3 ORDER BY 1,3;`
   — expect no `Resolved` row with a null fab, no `Unresolved`/`NotApplicable`
   row with a fab, and `NotApplicable` only on the five neutral kinds.

## Latency-budget impact

**N/A.** The audit write and read paths sit on none of constitution §IV's six
legs. One extra constant-width column on an insert that already writes fifteen
is not material to NFR-001's audit-ingest figure; no figure is cited because
none is measured here.

## Out of scope

| Not done | Why |
|---|---|
| Closing the exposure (withholding `Unresolved` rows from other fabs; running the fab guard in `GetSingle` for them; reconciling spec 217 F3) | Not the decision taken on #2540. Recommend a follow-up issue for a human, **without** `agent:ready`. |
| Showing the marker in `management-web` (`apps/shared/src/api/audit.api.ts`, whose comment still says null = "cross-fab rows") | Frontend, additive JSON field; candidate follow-up. |
| Removing the dead `AuditChunkArchivedV1.FabId` component | Contract change (ADR-0073); spec 217 already recorded it. |
| An index on `fab_attribution` | No query filters on it yet. |
| Changing `EventMetadata` / any publisher | Would amend ADR-0102; not needed. |

## Assumptions, marked

- **A1 (assumption).** TimescaleDB 2.27.1 accepts `ADD COLUMN … NOT NULL
  DEFAULT 'Unresolved'` (constant default) and `UPDATE` on compressed chunks.
  Both are documented as supported; the precedent migration notes only
  *non-constant* defaults fail. **Verified in phase 4 by the Aspire boot**, not
  assumed — a migration failure surfaces as `FailedToStart` of the whole fixture.
- **A2 (assumption).** The five historical neutral kinds in SC-9 are the
  complete set of fab-neutral `event_kind` values ever written (overlay V1/V2
  are the predecessors of V3 — `git log -S` shows V1 from `db1189e6`, V2 until
  `97e5e33f`). **Not exhaustively verified** that no other publisher ever passed
  a literal null by design; a missed kind would be labelled `Unresolved`, the
  fail-safe direction. Phase 5's GROUP BY over real history is the check, and
  any surprising null-fab kind it lists is reported, not silently added.
- **J1 (judgement).** No ADR needed — see §Why no ADR.
