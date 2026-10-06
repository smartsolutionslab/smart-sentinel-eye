# Plan — Spec 306, a null that says which null (#2540)

Phase 2 of ADR-0037. Read `spec.md` first.

## 1. Shape

**One bounded context: AuditObservability.** No `Shared.Contracts` change, no
publisher change, no AppHost change, no frontend change. `audit_events` exists
only in the `audit-observability` database — one table, one migration.

| Layer | Change |
|---|---|
| Domain | new `FabAttribution` enum, new `FabScope` enum, `AuditEvent.FabAttribution` property, `V1Envelope.FabScope` component, rule in `AuditEvent.From`; two doc-comment corrections |
| Application | new `FabNeutralEvents` register; `IntegrationEventAuditHandler.AuditAsync` sets `FabScope`; `AuditRowDto` + `AuditRowMapper` gain `FabAttribution` |
| Infrastructure | `AuditEventConfiguration` maps the column; `AuditEventRepository` insert writes it; new migration (+ Designer + snapshot) |
| Api | none (DTO flows through unchanged endpoints) |

## 2. Domain

`src/AuditObservability/Domain/AuditEvent/`

```csharp
public enum FabScope { Owned = 0, Neutral = 1 }          // a property of the event TYPE
public enum FabAttribution { Resolved = 0, Unresolved = 1, NotApplicable = 2 }  // a property of the ROW
```

Enums, not string VOs: closed, three-valued, no parsing at a trust boundary;
precedent `BearerValidationMode` on a domain model (passes `PrimitiveBoundaryTests`
today). Values pinned numerically for stability even though persistence is by
name.

`V1Envelope` gains a **trailing** positional component
`FabScope FabScope = FabScope.Owned`. Trailing + defaulted so every existing
construction site (`CrossFabReadGuardIntegrationTests.Row/OverlayRow`,
`AuditEventTests`, `AuditEventBuilder`, `AuditingMessageHandlerTests`,
`AuditMeasurementSwitchTests`) compiles untouched — those files stay out of
this diff. The default is the fail-safe value
(spec SC-6), which is the only reason a default is acceptable here.

`AuditEvent.FabAttribution { get; private init; }` and in `From`:

```csharp
FabAttribution = envelope.Fab.HasValue ? FabAttribution.Resolved
    : envelope.FabScope == FabScope.Neutral ? FabAttribution.NotApplicable
    : FabAttribution.Unresolved,
```

**Invariant:** `Resolved` ⇔ `Fab is not null`. Holds by construction: `From` is
the only writer and `FabAttribution` has a private init. A neutral type that
carries a fab is `Resolved` (SC-5) — the fab is never discarded.

Doc comments corrected (FR-008): `FabIdentifier`'s summary ("Optional on an
audit row because some cross-cutting V1s … are not fab-scoped") and
`AuditEvent.Fab` (currently undocumented — add one line pointing at
`FabAttribution` for what a null means). Comments only; no code there.

## 3. Application

**`src/AuditObservability/Application/EventHandlers/FabNeutralEvents.cs`** (new):
an `internal static` class holding a `FrozenSet<Type>` of the three neutral V1
types and `FabScope ScopeOf(Type integrationEventType)`. Each entry carries a
one-line *why* (ADR-0115 ×2; spec 217 F1 for the chunk). Lives beside
`V1ResourceMap` — same kind of per-V1-type knowledge — but is a separate type,
because resource pivot and fab scope are independent and folding them would
make every `V1ResourceMap` hand-tweak touch fab semantics.

*Considered and rejected — deriving scope from the contract shape.*
`EventMetadataFabDeclarationTests` derives exemptions from the absence of a
`Fab` component, but that works on **domain** events, which AuditObservability
cannot see (§III). On the **contracts** it gives the wrong answer for both
cases that matter: `StreamHealthChangedV1` has no `Fab` component (fab-owned),
and `AuditChunkArchivedV1` has a dead `FabId` component (neutral). So a register
is the honest shape, and the fail-safe default bounds the cost of it rotting.

**`IntegrationEventAuditHandler.AuditAsync`** — one added named argument:
`FabScope: FabNeutralEvents.ScopeOf(message.GetType())`.

**`AuditRowDto`** — trailing `string FabAttribution`; **`AuditRowMapper`** —
`FabAttribution: audit.FabAttribution.ToString()`. Explicit `ToString()` rather
than relying on the host's JSON enum options, so the wire values are exactly
the enum names and a unit test can pin them. `GetAuditEventQueryHandlerTests`
constructs `AuditRowDto` and must be updated for the new component (mechanical).

No change to `SearchAuditQueryHandler`, `GetResourceTimelineQueryHandler`,
`GetAuditEventQueryHandler`, or `AuditEndpoints` (FR-006).

## 4. Infrastructure

**`AuditEventConfiguration`**:
`builder.Property(e => e.FabAttribution).HasColumnName("fab_attribution").HasConversion<string>().HasMaxLength(16).IsRequired();`

**`AuditEventRepository.SaveAsync`** — add `fab_attribution` to the column list
and `{row.FabAttribution.ToString()}` to `VALUES`. The raw insert bypasses EF's
mapping, so omitting this would silently write the column default
(`Unresolved`) for every row — SC-2/SC-4's `Resolved` controls exist to catch
exactly that.

**Migration** `<timestamp>_AuditFabAttribution` via `dotnet ef migrations add`
(then hand-trim any spurious PK/index churn, as the precedent migration
documents):

```csharp
migrationBuilder.AddColumn<string>(
    name: "fab_attribution", table: "audit_events",
    type: "character varying(16)", maxLength: 16,
    nullable: false, defaultValue: "Unresolved");   // constant default: allowed on columnstore

migrationBuilder.Sql("""
    UPDATE audit_events SET fab_attribution = 'Resolved' WHERE fab_id IS NOT NULL;
    UPDATE audit_events SET fab_attribution = 'NotApplicable'
     WHERE fab_id IS NULL AND event_kind IN (
       'AuditChunkArchivedV1', 'OverlayRevisionArchivedV1',
       'OverlayRevisionPublishedV1', 'OverlayRevisionPublishedV2', 'OverlayRevisionPublishedV3');
    """);
```

`Down`: `DropColumn`. The event-kind list is frozen in the migration on purpose
(it records history, including V1/V2 that no longer exist in code); the
runtime register is `FabNeutralEvents`. The migration comment says so.

**Risk A1** (spec): constant default + `UPDATE` on compressed chunks on
TimescaleDB 2.27.1. Proven by the phase-4 Aspire boot; if the `UPDATE` fails
on compressed chunks, the fallback is to `decompress_chunk` → update →
recompress inside the migration — **report, do not improvise past it.**
Also: `git diff --stat src/AuditObservability/Infrastructure/Persistence/Migrations/`
must show the three files (migration, Designer, snapshot) — a claimed migration
is checked by file diff.

## 5. Messaging

None. No domain event, no integration event, no contract. The marker is
derived at the consumer from the existing `EventMetadata` plus the message's
runtime type.

## 6. Boundary rules

- §III / NetArchTest: Application references `Shared.Contracts` types for the
  register — already the case (`IntegrationEventAuditHandler`,
  `V1ResourceMap.Conventions`). No new project reference.
- Domain stays framework-free: two enums and one property.
- `Ensure.That` for any new public argument guard (ADR-0105); `ScopeOf` guards
  its `Type`.
- Collections: `FrozenSet` built from a collection expression / `ToFrozenSet()`;
  no `new()` for collections.

## 7. Phase-4a colour: **RED (behaviour-changing)**

**Why red.** This adds observable behaviour that does not exist today: a new
field on every audit row returned by three endpoints, a new persisted column,
and a classification rule. A test asserting `fabAttribution == "Unresolved"`
cannot pass against today's code — the property is absent. That is new
behaviour by any reading, and CLAUDE.md resolves any residual ambiguity to red.

**Why it is not mixed with characterisation.** The issue's decision is *"add an
explicit marker"*, chosen over closing the exposure; the issue itself separates
the *labelling* option (2) from accepting/closing behaviour (1). Nothing in this
plan changes what any read path returns or to whom (FR-006). So:

- **New tests → observed red first**, failure quoted (ADR-0139).
- **The existing exposure tests are not edited in their assertions.** They stay
  green before and after; that they still pass unmodified *is* the proof FR-006
  held. Only their doc comments change (FR-007) — comment-only, proven by the
  strip-comments-and-hash method spec 217 used for T003.

**Where the red is load-bearing.** The integration facts read `fabAttribution`
from response JSON (`JsonElement.TryGetProperty`), so they compile against
today's code and fail **at runtime** with "property absent" — that is the red
to quote. The unit tests reference new symbols and will be red as compile
errors; quote those too, but the integration red is the one that proves the
behaviour.

## 8. Test design

### 8.1 Unit — Domain (`tests/AuditObservability.Domain.Tests/AuditEvent/AuditEventTests.cs`, add facts)

- `From_marks_a_row_with_a_fab_as_resolved`
- `From_marks_a_fab_owned_event_with_no_fab_as_unresolved`
- `From_marks_a_fab_neutral_event_with_no_fab_as_not_applicable`
- `From_marks_a_fab_neutral_event_that_carries_a_fab_as_resolved` (SC-5)
- `From_treats_an_envelope_with_no_declared_scope_as_fab_owned` (SC-6 — built
  without naming `FabScope`, so it exercises the default)

### 8.2 Unit — Application

`tests/AuditObservability.Application.Tests/EventHandlers/IntegrationEventAuditHandlerTests.cs` (add facts; real `FabNeutralEvents`, `InMemoryAuditEventRepository`):

- `Handle_marks_a_stream_health_event_with_no_fab_as_unresolved`
- `Handle_marks_an_overlay_publication_as_not_applicable`
- `Handle_marks_an_archived_chunk_announcement_as_not_applicable`
- `Handle_marks_a_camera_event_with_a_fab_as_resolved`

`tests/AuditObservability.Application.Tests/EventHandlers/FabNeutralEventsTests.cs` (new):

- `The_register_names_exactly_the_three_publishers_that_cannot_carry_a_fab`
  (asserts the set equals `{OverlayRevisionPublishedV3, OverlayRevisionArchivedV1, AuditChunkArchivedV1}` — a deliberate pin: adding a neutral type should be a visible test edit)
- `Stream_health_is_fab_owned`
- `An_unregistered_type_is_fab_owned`

`tests/AuditObservability.Application.Tests/Queries/Handlers/GetAuditEventQueryHandlerTests.cs` (add one fact + update constructions):

- `The_returned_row_carries_its_fab_attribution_by_name` (e.g. `"NotApplicable"` — the multi-word value, so a wrong casing/serialiser shows)

### 8.3 Integration (Aspire) — the load-bearing red

`tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs` (add facts; reuse the static once-built arrangement; **no new class, so no shard-filter edit** — the class is already in `ci-shards/shard-4.filter`):

- `The_null_fab_health_row_is_marked_unresolved` — SC-1
- `The_same_cameras_earlier_row_is_marked_resolved` — SC-2 (control)

`tests/Integration.Tests/AuditObservability/NeutralFabRetentionRowIntegrationTests.cs` (add facts; same reasoning):

- `The_archived_chunks_announcement_is_marked_not_applicable` — SC-3
- `The_fab_carrying_row_in_the_same_window_is_marked_resolved` — SC-4 (control)

SC-1 vs SC-3 is the counterfactual that matters: identical `fab: null`,
different markers. SC-2/SC-4 rule out a column that defaults everything to one
value (notably the raw-insert omission risk in §4).

### 8.4 Comment-only edits to the two exposure facts (FR-007)

In `UnresolvedFabAuditRowIntegrationTests.cs`, replace the `<summary>` of each
fact. **Assertions untouched, byte for byte.** Text to use:

`An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator`:

```csharp
/// <summary>
/// SC-5. <b>Records the current exposure, which #2540 deliberately kept.</b>
/// #2540 was settled by labelling, not by closing: a null-fab row now carries
/// <c>fabAttribution</c> — <c>Unresolved</c> for a fab-owned resource whose fab
/// nobody resolved (this row), <c>NotApplicable</c> for a genuinely fab-neutral
/// one — so the two meanings of <c>Fab == null</c> are no longer one column
/// value (spec 306). What the unscoped search returns, and to whom, did not
/// change. A later fix that withholds <c>Unresolved</c> rows from other fabs'
/// operators turns this fact red; <b>that red is the fix landing, not a
/// regression</b> — update this comment, not the assertion, and name that
/// issue. Such a fix must key on <c>fabAttribution</c>, never on
/// <c>Fab == null</c> alone, which would also hide the <c>NotApplicable</c>
/// rows #1300 made visible. See also
/// <see cref="Get_single_returns_the_null_fab_row_to_another_fabs_operator"/>.
/// </summary>
```

`Get_single_returns_the_null_fab_row_to_another_fabs_operator`:

```csharp
/// <summary>
/// SC-6 / F0. <b>Records the current exposure, which #2540 deliberately
/// kept.</b> <c>GetSingle</c> still skips <c>IFabAuthorizationGuard</c> when a
/// row's fab is null (<c>AuditEndpoints.cs</c>), so any <c>sse.audit.read</c>
/// holder with the audit identifier reads it, whatever fab the underlying
/// resource belongs to. #2540 was settled by labelling (spec 306): this row now
/// says <c>fabAttribution: Unresolved</c>, distinguishing it from a genuinely
/// fab-neutral <c>NotApplicable</c> row, but who may read it did not change. A
/// later fix that guards <c>Unresolved</c> rows here (or resolves their fab)
/// turns this fact red; <b>that red is the fix landing, not a regression</b> —
/// update this comment, not the assertion, and name that issue. Key such a fix
/// on <c>fabAttribution</c>, not on <c>Fab == null</c>.
/// </summary>
```

Also update the class-level remark only if it states the policy is undecided
(it does not today — leave it).

### 8.5 Must pass unmodified (regression net)

Every existing fact in `UnresolvedFabAuditRowIntegrationTests`,
`NeutralFabRetentionRowIntegrationTests`, `CrossFabReadGuardIntegrationTests`,
`RetentionRoundtripIntegrationTests`, `EndToEndIngestionIntegrationTests`;
Architecture.Tests in full (`PrimitiveBoundaryTests`,
`EventMetadataFabDeclarationTests`, `HandlerDeconstructionTests`,
`IntegrationTestSelectionTests`).

## 9. Coverage and metrics

Domain ≥ 90 / Application ≥ 80 (ADR-0065): every new branch in `From` and
`ScopeOf` is covered by §8.1/8.2. `AuditEvent.From` grows by one expression;
stays under the 30-LOC advisory (ADR-0084).

## 10. Phase 6

`/security-review` required (§VIII — audit disclosure). Point the reviewer at:
the raw-insert column (§4), the fail-safe default (SC-6), and confirmation that
no read predicate changed (`git diff` on `Queries/Handlers/*QueryHandler.cs` and
`AuditEndpoints.cs` must be empty).

## 11. Definition of done

1. §8.1–8.3 observed red, then green; outputs quoted in the PR.
2. The two exposure facts' assertions byte-identical (comment-strip hash
   before/after quoted); all §8.5 tests green.
3. Migration files present in the diff; Aspire boot applied it (A1).
4. Phase 5: spec procedure steps 2–5, including the GROUP BY over the persistent
   volume, recorded in `verification.md`.
5. PR body recommends (does not file with `agent:ready`) a follow-up for a human
   on closing the `Unresolved` exposure, and one for the frontend field.
