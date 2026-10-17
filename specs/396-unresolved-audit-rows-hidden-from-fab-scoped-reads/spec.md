# Spec 396 — An unresolved audit row is hidden from fab-scoped reads

**Issue:** #2927 — "Audit reads show Unresolved-fab rows to fab-scoped callers
(cross-fab disclosure)". Feature-level issue; no per-task issues.
**Branch:** `fix/2927-audit-unresolved-fab-rows`
**Lane:** autonomous (ADR-0144), `agent:ready`
**Decision implemented (owner, recorded on #2927, 2026-10-17):** option A of the
issue — *a fab-scoped caller sees a no-fab audit row only when its attribution is
`NotApplicable`, on all three read paths together; `Unresolved` rows stay visible
only to unscoped callers, as today.* It reverses the "label, do not close" choice
recorded on #2540 (spec 306). This spec implements the decision; it does not
re-decide it.
**Predecessors:** spec 306 (`FabAttribution`, the marker this keys on), spec 217
(the arrangement that produces a live `Unresolved` row), spec 245 (per-entry
skip of malformed fab claims), #1300 (fab-less rows were invisible to everyone).
**ADRs:** ADR-0037, ADR-0047, ADR-0052/0053/0054, ADR-0101, ADR-0102 (read, not
amended), ADR-0103, ADR-0109, ADR-0115 (overlays are fab-neutral), **ADR-0159 §2
("fab is a data scope, and that scope is binding")**, ADR-0161 (precedent: a
null fab fails closed), ADR-0139, ADR-0144
**Constitution:** §II, §III, §Testing

---

## Problem

Spec 306 gave every audit row a `FabAttribution` — `Resolved`, `Unresolved`, or
`NotApplicable` — and deliberately changed no read predicate. All three read
paths still test `Fab == null` and so treat the two kinds of null identically:

| Path | Code on `develop` (`c60c6252`) | Admits for a fab-scoped caller |
|---|---|---|
| `GET /audit` without `fabId` | `SearchAuditQueryHandler.cs:108` — `Fab == null \|\| allowed.Contains(Fab)` | every no-fab row |
| `GET /audit/{kind}/{id}?fabId=` | `GetResourceTimelineQueryHandler.cs:59` — `Fab == null \|\| Fab == fabFilter` | every no-fab row |
| `GET /audit/{auditIdentifier}` | `AuditEndpoints.cs:205` — the fab guard runs only `if (row.Fab is not null)` | every no-fab row |

An `Unresolved` row belongs to *some* fab nobody resolved, so #1300's reasoning
("not restricted to a fab") does not hold for it. It carries the full
serialized integration event (`AuditRowMapper.cs:20`): live
`StreamHealthChangedV1` rows from a stream whose fab is still null (camera
GUID, states, MediaMTX `LastError` text), and historic fab-owned rows spec 306's
backfill labelled `Unresolved` (`CameraRegisteredV1` / `CameraAddressChangedV1`
with the camera name and RTSP origin — spec 377 reduced URLs to their origin,
so host/IP and port, no path or credentials — plus variable, layout and identity
events). A berlin operator reads munich's rows.

And one inconsistency (the issue's "nit"): a search that **names** a fab excludes
every no-fab row (`SearchAuditQueryHandler.cs:79-83`), while the timeline, which
always names a fab, includes every no-fab row.

## The rule (R1) — one rule for all three paths

> A read's **fab set** is the fab it names (search with `fabId`, timeline), or
> otherwise the caller's **usable** fab claims (`/fabs/<id>` groups that parse
> as a `FabIdentifier`; malformed entries skipped per entry, spec 245).
>
> - **Scoped read** (fab set not empty): a row is visible iff its fab is in the
>   fab set **or** its `FabAttribution` is `NotApplicable`.
> - **Unscoped read** (fab set empty — no usable claim, search without `fabId`
>   or single-row read only): a row is visible iff its fab is null — any
>   attribution. Unchanged.

Consequences, stated so nobody has to derive them:

| Row | Scoped read | Unscoped read |
|---|---|---|
| `Resolved`, fab in the fab set | visible | — (not reachable: fab ≠ null) |
| `Resolved`, other fab | hidden (search/timeline); `GET /audit/{id}` answers **403**, unchanged | `GET /audit/{id}` answers **403**, unchanged |
| `NotApplicable` | **visible on all three paths** (#1300 kept) | visible |
| `Unresolved` | **hidden on all three paths**; `GET /audit/{id}` answers **404** | visible, unchanged |

The nit is settled by R1: a search naming a fab now **includes** `NotApplicable`
rows, exactly as the timeline does. It does not include `Unresolved` rows — so
`Naming_a_fab_still_excludes_cross_fab_rows` (whose fixture row is `Unresolved`)
stays green unmodified. The other direction (timeline excludes every no-fab
row) was rejected: it would make overlay and retention history unreachable from
every timeline, which is #1300 again for the rows that genuinely have no fab
(ADR-0115), and contradict the owner's "NotApplicable rows stay visible".
**Judgement J1** — the owner delegated the choice; this is it.

## Why no ADR, and no constitution change

- ADR-0159 §2 already decides it: *"Every fab-scoped record carries its fab;
  access is decided by the caller's fab-group membership."* An `Unresolved` row
  is a fab-scoped record that failed to carry its fab; admitting it by default
  was the deviation. R1 brings the read paths in line with §2; it adds no rule.
- ADR-0161 is the precedent for the failure direction: a stream with no
  attributed fab is visible to nobody (FR-009) rather than to everybody.
- No `Shared.Contracts`, envelope (ADR-0102), scope (`Scope.cs`), realm, schema
  or publisher change. Option B — a deployment-wide auditor scope — would have
  needed an ADR; it is out of scope.
- **Block condition:** if a reviewer reads R1's "unscoped" bucket as a new
  authorization class needing a decision record, the lane blocks (ADR-0144);
  the lane does not write one.

## Locked tech choices

Nothing new is chosen.

| Concern | Choice | Where |
|---|---|---|
| Audit store | PostgreSQL + TimescaleDB, EF Core; `fab_attribution varchar(16)` with `HasConversion<string>()` | ADR-0101; spec 306 FR-004 |
| Read API | Minimal APIs, `sse.audit.read`, `IFabAuthorizationGuard` | ADR-0070; spec 008 FR-019 |
| Errors | `Result<T, Error>`, existing `AUDIT_EVENT_NOT_FOUND` (404) | ADR-0047/0089 |
| Tests | xUnit + Shouldly + hand-written fakes; integration via `AspireFixture` | ADR-0052/0103 |

---

## User stories

### US1 (P1) — a fab-scoped operator cannot read another fab's unattributed audit rows

**As** an operator assigned to `/fabs/berlin` holding `sse.audit.read`,
**I must not** receive audit rows whose subject belongs to a fab nobody resolved,
on any read path,
**while** rows that genuinely have no fab (`NotApplicable`) stay readable on
every path, so #1300 does not come back.

One vertical slice — three predicates, one shared rule, the single-row endpoint
wiring — observable end to end through `GET /audit`, `GET /audit/{kind}/{id}`
and `GET /audit/{auditIdentifier}`. There is no P2: the issue requires the three
paths to change together, and the nit is part of the same predicate.

---

## Acceptance scenarios

### Happy path

**SC-1 — the unscoped search hides an `Unresolved` row from a fab-scoped caller.**

```gherkin
Given spec 217's arrangement: a munich camera's StreamHealthChangedV1 row with fab null
  And fabAttribution "Unresolved"
 When op-berlin@berlin.test calls GET /audit?pageSize=200 (no fabId)
 Then the response is 200
  And the Unresolved row's auditIdentifier is absent
  And the berlin control camera's CameraRegisteredV1 row is present
```

**SC-2 — the same holds for the row's own fab's operator.** `admin@munich.test`,
`GET /audit?eventKind=StreamHealthChangedV1&pageSize=200`: the `Unresolved` row is
absent; the camera's earlier `Resolved` munich row is present. (A fab-scoped
caller is any caller with a usable fab claim; the row names no fab, so it is
nobody's to show — the cost option A accepted.)

**SC-3 — the timeline hides it.** `admin@munich.test`,
`GET /audit/stream/{camera}?fabId=munich`: `200`, the `Resolved` munich row is
present, the `Unresolved` row is absent.

**SC-4 — `NotApplicable` rows stay visible on every path.**

```gherkin
Given a NotApplicable row (OverlayRevisionPublishedV3, fab null, fabScope Neutral)
 When a munich operator reads GET /audit (no fabId), GET /audit?fabId=munich,
      GET /audit/overlay/{id}?fabId=munich and GET /audit/{auditIdentifier}
 Then the row is returned by all four
```

`GET /audit?fabId=munich` returning it is the nit's new behaviour (R1).

### Conflict (attribution vs fab set)

**SC-5 — a named fab admits `NotApplicable` but not `Unresolved`, and never a
foreign fab.** Handler level: rows {munich, berlin, Unresolved, NotApplicable},
`Fab = "munich"` → exactly {munich, NotApplicable}; `Fab = "hamburg"` (no rows)
→ exactly {NotApplicable}.

**SC-6 — a multi-fab caller.** `CallerFabs = ["berlin", "munich"]`, no named fab
→ exactly {munich, berlin, NotApplicable}.

### Bad request / malformed input

**SC-7 — a malformed claim costs only itself (spec 245, kept).**
`CallerFabs = ["munich", "Munich"]` → exactly {munich, NotApplicable}: the caller is
scoped by its one usable claim, and so does not see `Unresolved`.

**SC-8 — a wholly malformed claim set is unscoped, as today.**
`CallerFabs = ["Munich"]` or `[]` → exactly {Unresolved, NotApplicable}
(`No_named_fab_and_no_usable_claim_returns_only_the_fab_less_rows`, unchanged).
Disclosed as a residual in §Out of scope.

No new input surface: the existing `400 AUDIT_INVALID_INPUT` refusals (spec 215)
are unchanged and not duplicated.

### Auth

**SC-9 — a hidden single row answers 404, indistinguishable from an unknown id.**

```gherkin
Given the Unresolved row from SC-1
 When op-berlin@berlin.test calls GET /audit/{its auditIdentifier}
 Then the response is 404 with title "AUDIT_EVENT_NOT_FOUND"
  And its status, title and detail equal those for a freshly generated Guid v7,
      once the identifier in the detail is substituted
```

Same for `admin@munich.test`. 404, not 403: the search and timeline omit the
row, so "not in your view" is "not found"; a 403 would confirm the row exists,
and there is no fab to name in it. Precedent: spec 028 FR-004 and #2240 answer
404 for another fab's resource. **Judgement J2.**

**SC-10 — controls, unchanged.** `Get_single_refuses_the_same_cameras_munich_row_to_that_operator`
(403 `RESOURCE_FAB_NOT_AUTHORIZED`), `A_cross_fab_timeline_is_still_refused`
(403), `A_cross_fab_audit_search_is_still_refused` (403),
`An_unauthenticated_empty_fab_request_is_challenged` (401) pass unmodified.

**SC-11 — an unscoped caller is unchanged.** Handler level: `CallerFabs = []`,
single-row read of an `Unresolved` row → the row (no 404). Search, as SC-8.

---

## Functional requirements

- **FR-001** One shared rule (R1) decides visibility for all three paths; no
  path keeps its own `Fab == null` test. The parse of `CallerFabs` (per entry,
  malformed skipped) has one definition.
- **FR-002** `SearchAuditQueryHandler`: named fab → `Fab == named ||
  NotApplicable`; no named fab, usable claims → `Fab ∈ claims || NotApplicable`;
  no usable claims → `Fab == null` (unchanged).
- **FR-003** `GetResourceTimelineQueryHandler`: `Fab == named || NotApplicable`.
- **FR-004** `GetAuditEventQuery` carries the caller's claimed fabs (required,
  no default — a forgotten argument must not fail open). The handler answers
  `AUDIT_EVENT_NOT_FOUND` for an `Unresolved` row when the caller is scoped. The
  endpoint passes `FabClaims.AssignedFabs(user)`; its guard for `Resolved` rows
  (403) is unchanged.
- **FR-005** Predicates key on `FabAttribution`, never on `Fab == null` alone
  (spec 306 FR-007's instruction).
- **FR-006** No change to: contracts, envelope, publishers, `Scope.cs`, realm,
  schema/migrations, the `Resolved`-row 403, the 400 refusals, the response DTO.
- **FR-007** Doc comments that state the old visibility are corrected:
  `SearchAuditQueryHandler.cs:95-107`, `:112-114`; `GetResourceTimelineQueryHandler.cs:49-58`;
  `AuditEndpoints.cs` `GetSingle`; the class comment of
  `SearchAuditRefusalAndScopeCharacterisationTests` (":25 a fab-less row is
  visible to every fab-scoped caller"); `Naming_a_fab_still_excludes_cross_fab_rows`'
  inline comment; `SearchAuditQuery`'s summary.
- **FR-008** Every retired assertion is named in the PR body with the reason
  (owner decision on #2927, or R1 for the nit). No other existing assertion is
  edited. Arrangement-only edits (plan §5) are named too.

## Independent end-to-end test procedure

1. Boot the AppHost (check the AppHost PID start time against the commit).
2. Run spec 217's procedure steps 1–7 to produce a `StreamHealthChangedV1` row
   with `fab: null`; take its `auditIdentifier` **from the audit database**
   (`SELECT id, fab_attribution FROM audit_events WHERE resource_identifier = '<camera>' AND fab_id IS NULL;`)
   — no seeded HTTP caller can read it any more, which is the fix.
3. As `op-berlin@berlin.test`: `GET /audit?pageSize=200` → row absent;
   `GET /audit/{id}` → `404 AUDIT_EVENT_NOT_FOUND`; compare with
   `GET /audit/{new Guid v7}` → same status/title/detail shape.
4. As `admin@munich.test`: `GET /audit/stream/{camera}?fabId=munich` → `Resolved`
   row present, `Unresolved` row absent; `GET /audit/{id}` → 404.
5. Spec 217 step 12's `AuditChunkArchivedV1` row (`NotApplicable`): as
   `admin@munich.test`, present in `GET /audit`, `GET /audit?fabId=munich`, and
   `GET /audit/{id}` → 200.
6. `GET /audit/{munich Resolved row}` as berlin → still `403`.
7. Re-run `NFR002_AuditSearchLatencyTests` (p99 ≤ 200 ms) — the predicate gains a
   `fab_attribution` term; record the figure.

## Latency-budget impact

**N/A.** Audit reads are on none of constitution §IV's six legs. The search's own
NFR (spec 009 NFR-002, p99 ≤ 200 ms) is re-measured in phase 5, not assumed: the
new disjunct `fab_attribution = 'NotApplicable'` is not indexed; the existing
`(fab_id, occurred_at)` index still serves the fab-set half.

## Out of scope

- Stopping new `Unresolved` rows (resolving a stream's fab before publishing, or
  finishing the backfill) and re-attributing historic rows — the issue's
  "Independently" bullet; owner excluded both.
- A deployment-wide auditor scope (option B).
- The pre-existing existence oracle on `GET /audit/{id}` for a `Resolved`
  other-fab row (403 vs 404). Pinned by an existing control; changing it is a
  separate decision. Candidate follow-up finding.
- **Residual, disclosed:** a caller whose every fab claim is malformed is
  *unscoped* (spec 245) and so still reads every `Unresolved` row — a
  misconfigured group grants wider sight of this class than a correct one.
  Kept because the owner said "unscoped callers as today" and the spec-245
  tests pin it. Candidate follow-up finding, without `agent:ready`.

## Assumptions (marked)

- **A1 — "unscoped" is a membership fact, not a role.** The owner's comment
  says "unscoped (deployment-wide/admin) callers". The code has no admin bypass:
  unscoped means *no usable `/fabs/*` claim*. In the seeded realm every human
  user, including `admin`, holds a fab group, and no service account holds
  `sse.audit.read` — so after this change **no seeded principal can read an
  `Unresolved` row over HTTP**. The issue named this consequence for option A
  ("reintroduces the #1300 invisible-to-everyone problem for this class"); it is
  restated here so it is not discovered later.
- **A2** — the response shape is unchanged; the management-web audit page needs
  no change (its fab filter becomes "this fab plus fab-neutral rows", per R1).
