# Plan 396 — An unresolved audit row is hidden from fab-scoped reads

Spec: [spec.md](./spec.md). Issue #2927. Lane: autonomous (ADR-0144).

## 1. Premise, verified against `develop` `c60c6252` (2026-10-17)

| Claim in #2927 | Verified |
|---|---|
| `SearchAuditQueryHandler.cs:108`, `:115` admit `Fab == null` | yes — `:108` `Fab == null \|\| allowed.Contains(Fab)` (scoped), `:115` `Fab == null` (no usable claim) |
| `GetResourceTimelineQueryHandler.cs:59` admits any `Fab == null` | yes |
| `AuditEndpoints.cs:205` skips the guard when `row.Fab` is null | yes — `if (row.Fab is not null) await fabGuard.EnsureAccessAsync(...)` |
| `FabAttribution` values | `Unresolved = 0`, `Resolved = 1`, `NotApplicable = 2` (`Domain/AuditEvent/FabAttribution.cs`); invariant `Resolved ⇔ Fab != null`; stored as string |
| Named-fab search excludes no-fab rows (`:79-83`) | yes — `Fab == fabId` |
| Other read paths | **none.** `AuditEndpoints` maps exactly three `GET`s. No export, SSE or hub. The chunk archiver writes to blob storage and has no read API. Search-by-actor/username is a filter inside the same search handler, after `ScopeToFabs`, so it is covered. The gateway forwards without filtering. |
| How "unscoped" is defined | `FabClaims.AssignedFabs(user)` (`ServiceDefaults/Authorization/FabClaims.cs:36`) → `ParseUsableFabs` per entry; unscoped ⇔ zero usable entries. No admin bypass exists anywhere. |
| Spec 306 / #2540 | labelled, deliberately did not close (spec 306 FR-006/FR-007; SC-8 pinned the exposure "byte-identical") |

## 2. Bounded context and layers

**AuditObservability only.** Application (rule + three handlers + one query
record) and Api (one endpoint passes the caller's claims). No Domain change,
no Infrastructure change, no migration, no `Shared.*`, `ServiceDefaults`,
`AppHost` or realm change. No cross-context reference is introduced.

## 3. Design

### 3.1 One rule, one type — `AuditReadScope`

`src/AuditObservability/Application/Queries/Handlers/AuditReadScope.cs`,
`internal sealed`, beside `AuditCursor` and `AuditRowMapper` (ADR-0093 places
query-handler helpers there).

```csharp
internal sealed class AuditReadScope
{
    public static AuditReadScope Named(FabIdentifier fab);
    public static AuditReadScope ForCaller(IReadOnlyList<string> callerFabs); // per-entry parse, moved from SearchAuditQueryHandler.ParseUsableFabs
    public bool IsScoped { get; }                                             // fab set not empty
    public IQueryable<AuditEvent> Apply(IQueryable<AuditEvent> source);       // R1, EF-translatable
    public bool HidesUnresolved(AuditEvent row);                              // IsScoped && row.FabAttribution == Unresolved
}
```

`Apply`:

- scoped: `source.Where(e => fabs.Contains(e.Fab) || e.FabAttribution == FabAttribution.NotApplicable)`
  (`fabs` a `List<FabIdentifier>`; a single named fab is a list of one — the
  `Contains` translation already in use at `:108`).
- unscoped: `source.Where(e => e.Fab == null)` — today's `:115`, unchanged.

Signatures are illustrative; the engineer may shape them, but there must be
**one** parse of `CallerFabs` and **one** expression of R1. Primitives are fine
here (Application, not a domain model; §II scope). The `ArgumentException`
catch moves with `ParseUsableFabs` unchanged (trust-boundary parse, existing
pattern — not new error handling).

Why not a predicate per handler: the defect is three predicates that drifted
apart; a fourth copy would recreate it.

### 3.2 Search (`SearchAuditQueryHandler`)

`ScopeToFabs` becomes `(fab is not null ? AuditReadScope.Named(FabIdentifier.From(fab)) : AuditReadScope.ForCaller(callerFabs)).Apply(source)`.
`FabIdentifier.From(fab)` stays unguarded — the throw order pinned by
`An_unparseable_named_fab_throws_before_an_over_long_username_can_return_an_empty_page`
must hold. Order of filters unchanged (scope → actor → kind/resource → window →
cursor), so paging and the cursor are computed over visible rows only (no
count or cursor leak of a hidden row).

### 3.3 Timeline (`GetResourceTimelineQueryHandler`)

`:59` becomes `AuditReadScope.Named(fab).Apply(...)` after the two resource
predicates.

### 3.4 Single row (`GetAuditEventQuery` / handler / endpoint)

- `GetAuditEventQuery(AuditEventIdentifier AuditIdentifier, IReadOnlyList<string> CallerFabs)` —
  **required, no default and no secondary constructor**: a default of `[]` would
  make a forgotten argument unscoped, i.e. fail open.
- Handler: load as today; `null` → `AuditEventNotFound` (unchanged); if
  `AuditReadScope.ForCaller(CallerFabs).HidesUnresolved(row)` → the **same**
  `GetAuditEventFailures.AuditEventNotFound(query.AuditIdentifier.Value)` — same
  code, message template and status as an unknown id, so the response is not an
  oracle (SC-9).
- Endpoint `GetSingle`: pass `FabClaims.AssignedFabs(user)`. The
  `if (row.Fab is not null) EnsureAccessAsync` guard stays as is — `Resolved`
  other-fab rows keep answering 403 (control SC-10; changing it is out of scope).
- Why the rule sits in the handler, not the endpoint: it is then unit-testable
  without Docker (there is no `AuditObservability.Api.Tests`), and covered by the
  Application coverage gate (ADR-0065).

### 3.5 Messaging

None. No domain or integration event is added or changed.

## 4. Invariants this relies on (not changed)

- `FabAttribution.Resolved ⇔ Fab != null` (`AuditEvent.From`, spec 306 FR-001).
  Hence `NotApplicable ⇒ Fab == null`, and R1's `NotApplicable` disjunct can
  never admit a row with a foreign fab.
- Unclassified event types default to `FabScope.Owned` → `Unresolved` (spec 306
  SC-6). After this change that fail-safe direction becomes *hidden*, not
  *over-reported*: a neutral type nobody registered in `FabNeutralEvents` would
  disappear from fab-scoped reads. Correct direction for a security fix;
  recorded so a missing registration is diagnosed as that.

## 5. Tests — retire, keep, add (phase 4a, test-writer)

**Colour: RED** (behaviour-changing). Retirements are authorised by the owner's
decision on #2927 (U) or by R1's settlement of the nit (N). Nothing else may be
edited except the arrangement-only edits in §5.3.

### 5.1 Retired (assertions that encode the old visibility)

| # | File:line (method) | Why red after the fix | Basis |
|---|---|---|---|
| R-1 | `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditRefusalAndScopeCharacterisationTests.cs:270` `No_named_fab_returns_the_claimed_fabs_rows_and_every_fab_less_row_skipping_a_malformed_claim` | expects `UnresolvedRow` for scoped callers | U |
| R-2 | same file `:306` `A_fab_less_row_is_visible_to_a_caller_with_a_claimed_fab_whatever_its_attribution` | expects `["Resolved","Unresolved","NotApplicable"]` | U |
| R-3 | same file `:252` `A_named_fab_returns_only_that_fabs_rows_and_ignores_the_callers_claimed_fabs` | `ScopeRows()` holds a `NotApplicable` row; a named fab now returns it | N |
| R-4 | same file `:262` `A_named_fab_with_no_rows_returns_nothing_not_even_the_fab_less_rows` | now returns `NotApplicableRow` | N |
| R-5 | `.../SearchAuditQueryHandlerTests.cs:78` `A_caller_with_fabs_sees_cross_fab_rows_alongside_their_own` | its null-fab row is built `Owned` → `Unresolved` | U |
| R-6 | `.../SearchAuditQueryHandlerTests.cs:136` `A_malformed_claimed_fab_costs_only_itself` | same — `Unresolved` row expected for a scoped caller | U |
| R-7 | `.../GetResourceTimelineCharacterisationTests.cs:198` `The_callers_fab_sees_its_own_rows_and_every_fab_less_row_but_no_other_fab_and_no_other_resource` | expects `unresolved` in all three pages | U |
| R-8 | `.../GetResourceTimelineQueryHandlerTests.cs:65` `A_row_with_no_fab_is_returned_by_a_fab_scoped_timeline` | its row is `Owned` → `Unresolved` | U |
| R-9 | `.../GetResourceTimelineQueryHandlerTests.cs:78` `A_row_belonging_to_another_fab_is_still_excluded` | expects `["munich", null]`; the null row is `Unresolved` | U |
| R-10 | `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs:126` `The_fab_scoped_timeline_returns_the_null_fab_row` | the fix | U |
| R-11 | same file `:180` `An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator` (spec 306 FR-007 pin) | the fix | U |
| R-12 | same file `:221` `Get_single_returns_the_null_fab_row_to_another_fabs_operator` (spec 306 FR-007 pin) | the fix | U |

R-11/R-12's doc comments say "update this comment, not the assertion". That
cannot be followed literally — the assertion *is* the old behaviour. The owner's
clarification on #2927 ("retired … replaced red-first") governs.

**Not retired, although #2927 lists it:** `SearchAuditRefusalAndScopeCharacterisationTests.cs:296`
`No_named_fab_and_no_usable_claim_returns_only_the_fab_less_rows` — that is the
unscoped caller, which the owner kept "as today". It must stay green **unmodified**.

### 5.2 Kept green, unmodified (characterisation of what must not move)

`SearchAuditQueryHandlerTests`: `Naming_a_fab_still_excludes_cross_fab_rows` (:93 —
its row is `Unresolved`; comment-only edit per FR-007), `A_caller_with_no_fabs_still_sees_only_cross_fab_rows`
(:110), `A_wholly_malformed_claim_set_sees_only_cross_fab_rows` (:162);
`SearchAuditRefusalAndScopeCharacterisationTests` `:296` and every refusal/throw-order
test in it; `GetResourceTimelineQueryHandlerTests:101`; all `GetResourceTimelineCharacterisationTests`
other than `:198`; `GetAuditEventQueryHandlerTests` (all three — see §5.3);
`UnresolvedFabAuditRowIntegrationTests` `:74`, `:87`, `:104`, `:115`, `:144`, `:241`, `:256`, `:270`;
`NeutralFabRetentionRowIntegrationTests` (all, incl. `:114` — a real `NotApplicable` row on a timeline);
`CrossFabReadGuardIntegrationTests` assertions (all).

### 5.3 Arrangement-only edits (assertions untouched; named in the PR)

| File:line | Edit | Why |
|---|---|---|
| `GetAuditEventQueryHandlerTests.cs:18`, `:33`, `:53` | `new GetAuditEventQuery(id)` → `new GetAuditEventQuery(id, [])` | FR-004 makes `CallerFabs` required; `[]` is unscoped, so each test's expectation is unchanged |
| `UnresolvedFabAuditRowIntegrationTests.cs:355-360` (`nullFabRow` poll) | locate the null-fab row in the audit store (`aspire.CreateAuditObservabilityDbContextAsync()`, precedent `NeutralFabRetentionRowIntegrationTests.cs:222`) by resource identifier + `Fab == null`, and project it into a `JsonElement` with the same property names the facts read (`auditIdentifier`, `fab`, `fabAttribution`, `resourceKind`, `resourceIdentifier`, `payload`) | after the fix the munich timeline no longer returns it, so the shared arrangement would time out and fail **every** fact in the class; no seeded HTTP principal may read it (spec A1). `AuditRowMapper` is `internal`, hence a test-local projection |
| `CrossFabReadGuardIntegrationTests.cs:436` `OverlayRow` | pass `FabScope: FabScope.Neutral` to the `V1Envelope` | `A_fab_neutral_row_is_returned_by_a_fab_scoped_resource_timeline` (:52) is *named* for a neutral row but seeds `Owned` → `Unresolved` (envelope default) since spec 306; the real path stamps overlays neutral (`FabNeutralEvents`). Without this it turns red for the wrong reason |
| `CrossFabReadGuardIntegrationTests.cs:97`, `:138` | seed a neutral row (e.g. `OverlayRow(Guid.CreateVersion7(), fab: null)` after the edit above, or a neutral variant of `Row`) instead of `Row(fab: null)` | same: both facts assert "a cross-fab row must stay readable by a fab-assigned caller" (#1300), which is about *neutral* rows. Their `ShouldContain((string?)null)` scans a shared, never-reset audit table, so with an `Unresolved` seed they could stay green on another test's leftover neutral row — passing for the wrong reason |

If a reviewer reads any §5.3 edit as an assertion edit, the lane blocks rather
than argues.

### 5.4 New tests (red first)

Unit — `tests/AuditObservability.Application.Tests` (no Docker):

| ID | Class / method (sentence-style, ADR-0053) | Red against develop because |
|---|---|---|
| N-1 | `SearchAuditRefusalAndScopeCharacterisationTests.No_named_fab_returns_the_claimed_fabs_rows_and_only_the_not_applicable_fab_less_row_skipping_a_malformed_claim` (replaces R-1; same three caller sets, `UnresolvedRow` absent) | `UnresolvedRow` returned |
| N-2 | `...A_fab_less_row_is_visible_to_a_caller_with_a_claimed_fab_only_when_not_applicable` (replaces R-2: `["Resolved","NotApplicable"]`) | `Unresolved` present |
| N-3 | `...A_named_fab_returns_that_fabs_rows_and_the_not_applicable_row_and_ignores_the_callers_claimed_fabs` (replaces R-3: `{Munich, NotApplicable}`, `{Berlin, NotApplicable}`, `{Berlin, NotApplicable}`) | `NotApplicable` absent |
| N-4 | `...A_named_fab_with_no_rows_returns_only_the_not_applicable_row` (replaces R-4) | empty page |
| N-5 | `SearchAuditQueryHandlerTests.A_caller_with_fabs_sees_not_applicable_rows_alongside_their_own_but_not_unresolved_ones` (replaces R-5; seeds both kinds) | `Unresolved` present |
| N-6 | `SearchAuditQueryHandlerTests.A_malformed_claimed_fab_costs_only_itself` → `A_malformed_claimed_fab_costs_only_itself_and_still_scopes_the_caller` (replaces R-6; seeds both kinds; expects `{munich, NotApplicable}`) | `Unresolved` present |
| N-7 | `GetResourceTimelineCharacterisationTests.The_callers_fab_sees_its_own_rows_and_the_not_applicable_row_but_no_unresolved_row_no_other_fab_and_no_other_resource` (replaces R-7: pages `{own, notApplicable}`, `{otherFab, notApplicable}`, `{notApplicable}`) | `unresolved` present |
| N-8 | `GetResourceTimelineQueryHandlerTests.An_unresolved_row_is_not_returned_by_a_fab_scoped_timeline` and `A_not_applicable_row_is_returned_by_a_fab_scoped_timeline` (replace R-8) | first: row returned |
| N-9 | `GetResourceTimelineQueryHandlerTests.A_row_belonging_to_another_fab_or_unresolved_is_excluded` (replaces R-9; seeds munich, berlin, Unresolved, NotApplicable; expects exactly munich + NotApplicable, by identifier) | `Unresolved` present |
| N-10 | `GetAuditEventQueryHandlerTests.An_unresolved_row_is_not_found_for_a_fab_scoped_caller` — failure `AUDIT_EVENT_NOT_FOUND`, `404`, message equal to the unknown-id message for the same id | compile red (no `CallerFabs`), then success instead of failure |
| N-11 | `GetAuditEventQueryHandlerTests.An_unresolved_row_is_returned_to_an_unscoped_caller` (`[]` and `["Munich"]`) | compile red only — **characterisation of the kept behaviour**; say so in the report |
| N-12 | `GetAuditEventQueryHandlerTests.A_not_applicable_row_is_returned_to_a_fab_scoped_caller` and `A_resolved_row_of_another_fab_is_returned_for_the_endpoint_guard_to_refuse` | compile red only — characterisation (the 403 stays the endpoint's) |
| N-13 | `GetAuditEventQueryHandlerTests.A_multi_fab_caller_is_scoped` (`["berlin","munich"]` + Unresolved → not found) | success instead of failure |

Integration — `tests/Integration.Tests` (**Docker required; Docker is down
locally** — these are compiled locally and observed red/green only in CI or
once Docker is back; the lane must not report them as observed otherwise):

| ID | Class / method | Asserts |
|---|---|---|
| I-1 | `UnresolvedFabAuditRowIntegrationTests.The_fab_scoped_timeline_does_not_return_the_unresolved_row` (replaces R-10) | munich timeline: `Resolved` row present, `Unresolved` absent (by id) |
| I-2 | `...An_unscoped_search_does_not_return_the_unresolved_row_to_another_fabs_operator` (replaces R-11) | berlin `GET /audit?pageSize=200`: row absent by id; the berlin control camera's `CameraRegisteredV1` row present (non-vacuous) |
| I-3 | `...An_unscoped_search_does_not_return_the_unresolved_row_to_its_own_fabs_operator` | munich, `eventKind=StreamHealthChangedV1`: `Unresolved` absent, `Resolved` present |
| I-4 | `...Get_single_answers_not_found_for_the_unresolved_row_to_another_fabs_operator` (replaces R-12) | berlin `GET /audit/{id}` → 404 `AUDIT_EVENT_NOT_FOUND`; status/title equal, and detail equal after id substitution, to `GET /audit/{Guid.CreateVersion7()}` |
| I-5 | `...Get_single_answers_not_found_for_the_unresolved_row_to_its_own_fabs_operator` | munich, same |
| I-6 | `CrossFabReadGuardIntegrationTests.A_named_fab_search_returns_a_not_applicable_row` | seeded neutral row (by id) present in `GET /audit?fabId=munich` |
| I-7 | `CrossFabReadGuardIntegrationTests.A_seeded_unresolved_row_is_hidden_from_every_fab_scoped_path_and_a_seeded_not_applicable_row_is_not` | seeds one of each (by id); search, named search, timeline (overlay kind), single row: Unresolved absent/404 everywhere, NotApplicable present/200 everywhere. Fast (no 45 s arrangement); the main proof that the EF translation of R1 is right |

Shard filters: no new test **class** is planned, so `ci-shards/shard-*.filter`
needs no edit (`CrossFabReadGuardIntegrationTests` is in shard 2,
`UnresolvedFabAuditRowIntegrationTests` in shard 4). If the test-writer adds a
class, it must add a shard entry (memory: a missing one fails deterministically).

## 6. Risks

| Risk | Mitigation |
|---|---|
| EF cannot translate `FabAttribution == NotApplicable` inside the `||` | unit fakes are LINQ-to-objects and cannot see this; I-7 and the existing integration facts exercise Npgsql. Spec 306 already filters/maps the column, so translation is expected to work |
| NFR-002 search p99 regresses | phase 5 runs `NFR002_AuditSearchLatencyTests` twice (memory: first run after churn looks like a regression) and records the figure |
| A `NotApplicable` seed that is really `Unresolved` | §5.3 fixes the three known; reviewer greps for `AuditEvent.From(new V1Envelope(` without `FabScope` in audit read tests |
| Hidden row leaks via paging / cursor / count | scope applied before `Take`; I-2/I-7 assert by identifier |
| Fail-open default on `GetAuditEventQuery` | required parameter; reviewer checks no secondary constructor |

## 7. Agents

- Phase 4a: **test-writer** (§5.1–5.4), then **test-adversary** (security-sensitive:
  oracle shape of the 404, multi-fab, malformed/blank claims, blank `fabId=`,
  cursor across hidden rows, unclassified event type, `fabId` for a fab the
  caller holds but the row is `Unresolved`).
- Phase 4b: **backend-engineer** (Application + one Api line). No frontend,
  infra or migration work.
- Phase 6: **backend-reviewer** + **security-reviewer**.
