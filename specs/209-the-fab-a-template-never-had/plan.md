# Plan — Spec 209, the fab a template never had

**Issue:** #2506 · **Branch:** `2506-overlay-published-null-fab` · **Spec:** `specs/209-the-fab-a-template-never-had/spec.md`
**Base:** `origin/develop` @ `1e461ea2`

**Phase-4a colour: RED.** Behaviour-changing — a query that returns an empty page today must return rows after. Both the unit test and the integration test are written first, observed failing, and their **verbatim** output is the phase-4 evidence quoted in the PR body (ADR-0139, CLAUDE.md §*Phase 4a has two colours*). A test that arrives green is a phase-4 failure, not a shortcut. There is no characterisation half to this spec: nothing behaviour-preserving is being changed.

**No new ADR, and no constitution amendment.** This is the opposite of the usual "is a decision missing?" answer — the decision exists, is accepted, and is the reason the issue's own proposed fix is rejected. **ADR-0115** ("Overlays are fab-neutral templates") settles that `OverlayDesigner` has no fab to stamp; **#1300** settles that a null-fab audit row means *not fab-restricted*, and already implements that rule in `SearchAuditQueryHandler`. This plan carries the existing rule into the one read path that never received it. Nothing about layering, contract shape, the resource vocabulary or any context boundary changes.

---

## Context and layers

**One bounded context is touched: `AuditObservability`. Exactly one production file.**

| Layer | File | Change |
|---|---|---|
| Domain | — | **None.** `AuditEvent.Fab` stays `FabIdentifier?` (`AuditEvent.cs:37`), hydrated from `Option<FabIdentifier>` at `:146`. No invariant moves. |
| Application | `Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs` | **The whole change.** One `Where` predicate widened, one comment added. |
| Infrastructure | — | **None.** `AuditEventQuerySource` exposes `IQueryable`; the predicate composes onto it unchanged. **No EF migration, no index change** — `ix_audit_resource_occurred` is `(ResourceKind, ResourceIdentifier, OccurredAt)` (`AuditEventConfiguration.cs:148-149`) and carries no fab column, so the fab predicate was already a post-index filter and stays one. |
| Api | — | **None.** `AuditEndpoints.GetTimeline` keeps its required `fabId`, its `fabGuard.EnsureAccessAsync` call at `:114`, and its parse guards. |

**Contexts explicitly not touched:** `OverlayDesigner` (ADR-0115 — and `grep -rn "Fab" src/OverlayDesigner` returns zero lines, which is the design, not an omission), `Shared.Contracts` (no contract shape moves, so no ADR-0073 `V2` question), `LayoutComposition`, `StreamDistribution`, `SystemVariables`, `CameraCatalog`, `Identity`, `EventIngestion`, `Automation`. No `apps/` file, no `deploy/` file, no `.csproj`.

**Boundary rules (constitution §III, ADR-0109).** AuditObservability already references `Shared.Contracts` and nothing else cross-context. Nothing is added, so `NetArchTest`'s boundary suite is unaffected and needs no new rule.

---

## Entities, value objects and invariants

Nothing new is introduced. What matters is the **existing** shape the predicate rests on, because getting it wrong is the one way this fix can be subtly broken:

| Type | Shape | Why it matters here |
|---|---|---|
| `AuditEvent.Fab` | `FabIdentifier?` — a nullable *reference* to a value object (`AuditEvent.cs:37`) | Nullable because absence is persisted state, which is exactly the carve-out ADR-0141 names: EF maps a nullable value-object reference and does not map `Option<T>`. The hydration boundary (`AuditEvent.From`, `:146`) is where `Option<FabIdentifier>` becomes `null`. |
| `FabIdentifier` | `StringValueObject` record, value-converted to a `varchar` column (`AuditEventConfiguration.cs:61-66`) | **The comparison must be on the value object, not on `.Value`.** EF Core translates equality on a value-converted property to a column comparison but cannot translate member access on the converted CLR type — the handler already carries that comment at `:48-49`, and `SearchAuditQueryHandler.cs:50-53` carries it too. |
| `ResourceKind` | Closed vocabulary of eleven values (`ResourceKind.cs`) | Unchanged. The handler's existing 400 for an unknown kind stays first in the method. |

**The invariant the fix installs, stated once:** *a row with no fab is not restricted to a fab, so a fab-scoped read includes it.* That is not a new invariant — it is `SearchAuditQueryHandler.cs:73`'s invariant, applied to the second read path. `GetSingle` (`AuditEndpoints.cs:181`) is the third and already agrees.

---

## Messaging (domain event → integration event)

**None. This spec publishes nothing and subscribes to nothing.**

That is worth stating rather than omitting, because the issue frames the defect as a publisher problem. It is not: the write path (`AuditingMessageHandler` → `AuditEvent.From` → the `fab` column) is correct for all 21 construction sites, and the sweep in `spec.md` §*The complete `Fab` sweep* is the evidence. The only thing being changed is a read.

Consequently:

- No `EventMetadata` construction site is edited.
- No Wolverine handler, queue, outbox registration or subscription changes (ADR-0088, ADR-0042).
- No integration-event version bump (ADR-0073).

---

## The change, precisely

`src/AuditObservability/Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs`, the third `Where` at `:54`:

```csharp
// before
.Where(auditEvent => auditEvent.Fab == fabFilter);

// after
.Where(auditEvent => auditEvent.Fab == null || auditEvent.Fab == fabFilter);
```

plus the US2 comment immediately above it, modelled on `SearchAuditQueryHandler.cs:63-72` and naming #1300, ADR-0115, retention and #2076.

`FabIdentifier fabFilter = fab;` at `:51` stays exactly as it is — `fab` is non-null here because `GetResourceTimelineQuery.Fab` is non-nullable and the endpoint parses it before the handler runs.

**Translation is already proven.** `SearchAuditQueryHandler.cs:73` runs `auditEvent.Fab == null || allowed.Contains(auditEvent.Fab)` against the same column with the same converter, in the same provider, today. The `||` cannot fail to translate here.

**Three ways this can be got wrong, and what stops each:**

| Mistake | What it would look like | The test that catches it |
|---|---|---|
| Delete the predicate instead of widening it | Every fab's rows visible to every fab-assigned caller | `US1 — conflict: another fab's rows stay out` (unit **and** integration) |
| Write `auditEvent.Fab.Value == null` | `InvalidOperationException` — "could not be translated" | The integration test; a unit fake over `IQueryable` on a list would *not* catch it, which is why the integration test is mandatory and not optional |
| Relax `fabId` to optional while here | 200 for a request that must be 400 | `US1 — bad request: fabId is still required` |

---

## Testing strategy

### Phase 4a — RED, in two layers, both run before any production edit

**Layer 1 — unit, `tests/AuditObservability.Application.Tests/Queries/Handlers/GetResourceTimelineQueryHandlerTests.cs`.** Existing file with a `TestAuditEventQuerySource` fake and an `AuditEventBuilder`. **`AuditEventBuilder.WithFab` already takes `string?`** (`:25`) and already maps null to `Option<FabIdentifier>.None` (`:39-41`), so the builder needs no change — the file's private `Row(...)` helper simply hard-codes `"munich"` (`:32`) and gains a fab parameter.

Three cases, sentence-style (ADR-0053):

- `A_row_with_no_fab_is_returned_by_a_fab_scoped_timeline` — **the filed defect.** Fails today: the page is empty.
- `A_row_belonging_to_another_fab_is_still_excluded` — **the widening-not-removal guard.** Passes today and must still pass; a deletion of the predicate breaks it.
- `A_fab_neutral_row_for_a_different_resource_is_still_excluded` — the resource predicates are untouched by the fab change.

The second and third **pass before the fix**. That is correct and is not a phase-4a violation: the story's red is carried by the first. State it in the phase-4a report so a reader does not mistake a partially-green run for a shortcut.

**Layer 2 — integration, `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`** (Aspire fixture, ADR-0103; no Testcontainers). This file is already the right home: it already seeds `Row(fab: null)` through `SeedAsync`, already calls `GET /audit/overlay/{id}?fabId=…`, and already owns the 403 assertion this fix must not disturb.

One case:

- `A_fab_neutral_row_is_returned_by_a_fab_scoped_resource_timeline` — seed a fab-neutral row and a `berlin` row on the same `(overlay, identifier)`; request as `admin@munich.test` with `?fabId=munich`; assert the fab-neutral row is present **and** the `berlin` row is absent. Both halves in one test, because the presence assertion alone would pass on a deleted predicate.

**And one existing test must pass unmodified:** `Munich_member_reads_its_own_fab_timeline_but_is_refused_another_fab` (`:21-35`). It asserts `403 RESOURCE_FAB_NOT_AUTHORIZED` for `?fabId=berlin`. **If that assertion has to be edited, the change has moved the security boundary — block, do not adjust.** The guard runs at the endpoint before the handler (`AuditEndpoints.cs:114`), so it should not even be close; asserting that it is not is the point.

### Phase 4b — GREEN

The engineer receives the verbatim red output as its brief and **may not edit the tests to pass** (ADR-0139). One predicate, one comment. If more than that file changes, the diff is wrong.

### Coverage (ADR-0065)

Application ≥ 80%. The change is one predicate inside a method that already has unit coverage; the three new unit cases raise branch coverage on it rather than lowering anything. No new file, so no new uncovered surface.

### Security review trigger

**Yes — phase 6 runs `/security-review` as well as `/code-review`.** The change widens what a fab-scoped read returns. The argument that it discloses nothing new is in `spec.md` §*Already-settled precedent in the third read path* and rests on two existing behaviours (`GetSingle` returns null-fab rows with no fab check; `SearchAuditQueryHandler` already includes them for fab-assigned callers). A reviewer should check that argument rather than take it, and should confirm the 403 path is untouched.

---

## Why this is the smallest possible change

CLAUDE.md §Karpathy: *a bug fix changes the bug, nothing else.*

- The sweep found **no publisher missing a `Fab` it ought to stamp** — so there is no publisher-side fix to bundle, and bundling one would have meant editing a context whose fab-neutrality is an accepted ADR.
- The four fab-neutral producer classes (overlay lifecycle ×2, retention, unattributable stream health) are fixed by the **same one predicate**, across all eleven resource kinds. There is no per-context follow-up to file and no scope-creep question to resolve: widening the predicate once is strictly smaller than four publisher changes, and it is also the only one of the two that is legal.
- `OverlayRevisionArchivedV1` — the instance the issue did not name — is fixed without being touched.
- No refactor rides along. `AuditEventBuilder` uses leading-underscore fields against CLAUDE.md §House rules (`_fab`, `_occurredAt`); **pre-existing, in a file this spec extends, and left alone.** A drive-by rename would churn the file carrying the new test. Recommend a sweep issue, as `specs/206-a-row-no-timeline-can-reach` §Assumptions (7) did for the same builder family.

---

## Review focus for phase 6

1. **The predicate is widened, not removed.** Read the `Where` and confirm `fabFilter` still appears in it.
2. **The 403 test is unmodified.** `git diff` on `CrossFabReadGuardIntegrationTests.cs` must show additions only in the new test's region and no edit inside `Munich_member_reads_its_own_fab_timeline_but_is_refused_another_fab`.
3. **The comparison is on the value object, not `.Value`.** An `.Value` would compile and then throw at runtime; the unit fake would not catch it.
4. **No file outside `AuditObservability` and its two test files is touched.** In particular `src/OverlayDesigner/` must be byte-identical.
5. **The comment says why, not what** (CLAUDE.md §*No drive-by comments*) — and specifically names #1300 and ADR-0115, because the whole defect is that the reason existed in one file and not the other.
6. **No new suppression, no lowered threshold, no deleted test** (CLAUDE.md §*Three things the lane may not do*).
