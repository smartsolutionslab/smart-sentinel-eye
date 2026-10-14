# Spec 358: Four SystemVariables defects that spec 349 pinned as they were

**Issue**: #2935 (feature-level; no per-task issues). **Branch**: `fix/2935-system-variables-defects`.
**Lane**: autonomous (ADR-0144). **Context**: SystemVariables only.
**ADRs**: ADR-0144 (lane, red-first), ADR-0050 (`[LoggerMessage]` logging), ADR-0052/0053
(xUnit + Shouldly, sentence-style names), ADR-0084 (the refactor whose characterisation pinned these),
ADR-0115 (fab scoping, unchanged). ADR-0142 was checked and does **not** apply: the dedup store is the
Wolverine-redelivery key of spec 007 FR-018 / spec 014, not the HTTP `Idempotency-Key`.

**Phase 4a colour: RED** (behaviour-changing, all four).

## 1. Origin

Spec 349 §6 recorded four oddities while refactoring the context under ADR-0084 and deliberately
did not change them: "if any is thought wrong, that is a separate issue". #2935 is that issue. Each
oddity is pinned by a characterisation test named in §4; this spec replaces those pins on purpose,
red first.

## 2. Premise check (develop @ 7393711a)

Every defect was located in the current code; none is already fixed, none is backed by an ADR or a
spec requirement, so all four stay in scope.

| # | Defect | Where | Verdict |
|---|---|---|---|
| 1 | `Pushed ResolvedOverlayTextChanged to {Count} overlays` logs `affectedOverlays.Count`, including overlays skipped because `LookupLabelTexts` returned `null` | `VariableValueChangedDomainEventHandler.cs:83`, `VariableArchivedDomainEventHandler.cs:68` | **Defect.** The line says "pushed"; the number is "looked up". |
| 2 | The archive handler returns silently when no overlay references the variable | `VariableArchivedDomainEventHandler.cs:40-43` | **Defect.** Its sibling logs `NoOverlaysReferenceVariable` on the identical branch; the two handlers are documented as "same split". |
| 3 | `TryReserveAsync` runs before `VariableName.From` is attempted, so a malformed name consumes a dedup row and a redelivery of it reports "Dedup hit" instead of "Invalid variable name" | `SystemVariableValueRequestedV1Handler.cs:51-61` | **Defect.** The same handler already states the convention for every other refusal: the fab check is "Dropped before the dedup reservation, so it consumes no idempotency key" (line 93-95). An invalid name is the same kind of refusal — nothing will ever apply — and no spec asks for it to be reserved. |
| 4 | `idElement.GetGuid()` throws on a non-Guid (`FormatException`) or non-string (`InvalidOperationException`) identifier; the outer `catch` logs `SeedFailed` ("starting with empty index"), which is false — overlays before it stay upserted, overlays after it are lost, `SeededOverlays` never logs | `ReverseIndexSeederHostedService.cs:132` | **Defect.** The loop already skips an overlay with no identifier or no `elements` array and keeps going; one malformed identifier is the same per-overlay condition, not a whole-response failure. |

Out of scope, recorded so it is not mistaken for an omission: both fan-out handlers advance the
version of an overlay they then skip (`AdvanceAsync(affectedOverlays)`). It is pinned
(`AdvanceCalls == [OverlayA, OverlayB]`) and stays as it is; changing it touches the #2426
one-round-trip design and is not one of the four. Likewise a reservation taken before
`SetVariableValue` fails (`VARIABLE_NOT_FOUND`, type mismatch) still consumes its key — that is the
redelivery contract working, not defect 3.

## 3. User stories

**US1 (P1) — An operator reading the SystemVariables log is told what actually happened.** One
slice; four independent fixes, one commit pair each.

### Acceptance scenarios

```gherkin
Scenario: D1 the push count names only overlays that were pushed (value change)
  Given overlays A and B reference "oeeLine1" and only A has cached label texts
  When "oeeLine1" changes
  Then exactly one ResolvedOverlayTextChangedV3 is published, for A
  And the information line reads "Pushed ResolvedOverlayTextChanged to 1 overlays after 'oeeLine1' changed."

Scenario: D1 the push count names only overlays that were pushed (archive)
  Given overlays A and B reference "retired" and only A has cached label texts
  When "retired" is archived
  Then the information line reads "Pushed ResolvedOverlayTextChanged to 1 overlays after 'retired' was archived."

Scenario: D2 an archive nobody references says so
  Given no overlay references "retired"
  When "retired" is archived
  Then only SystemVariableArchivedV1 is published, no version is advanced
  And one debug line reads "No overlays reference variable 'retired'; skipping push fan-out."

Scenario: D3 an invalid name is refused before any reservation (bad request)
  Given a value request for "1bad" in fab "dresden"
  When the handler runs
  Then TryReserveAsync is never called
  And one warning reads "Invalid variable name '1bad' in V1; dropping (caused by <causing event>)."

Scenario: D3 an invalid name is reported as invalid even when its key would collide (conflict)
  Given a dedup store that would refuse the key
  And a value request for "1bad" in fab "dresden"
  When the handler runs
  Then TryReserveAsync is never called
  And the warning is the invalid-name line, not the debug "Dedup hit" line

Scenario: D4 a non-Guid overlay identifier is skipped, not fatal
  Given the published list holds overlay 1, then "not-a-guid", then overlay 3
  When the seeder starts
  Then overlays 1 and 3 are upserted, in that order
  And the only log line is information "ReverseIndex seeded with 2 published overlays."

Scenario: D4 a non-string overlay identifier is skipped the same way
  Given the published list holds overlay 1, then an identifier of 42 (a JSON number)
  When the seeder starts
  Then overlay 1 is upserted and the line reads "ReverseIndex seeded with 1 published overlays."
```

Auth: N/A — no endpoint, scope or credential is touched; the seeder's 401/403 branches are pinned and
unchanged.

## 4. Pinned tests this spec replaces (deliberately)

| Defect | File | Test replaced |
|---|---|---|
| 1 | `tests/SystemVariables.Application.Tests/EventHandlers/ValueChangedFanOutCharacterisationTests.cs` | `The_versions_are_advanced_once_for_both_listed_overlays_and_the_log_counts_the_skipped_one` |
| 1 | `tests/SystemVariables.Application.Tests/EventHandlers/ArchivedFanOutCharacterisationTests.cs` | `The_versions_are_advanced_once_and_the_log_counts_the_overlay_that_was_skipped` |
| 2 | `tests/SystemVariables.Application.Tests/EventHandlers/ArchivedFanOutCharacterisationTests.cs` | `With_no_referencing_overlay_only_the_V1_is_published_and_nothing_is_logged` |
| 3 | `tests/SystemVariables.Application.Tests/EventHandlers/ValueRequestRefusalOrderCharacterisationTests.cs` | `An_invalid_name_has_already_consumed_its_reservation` |
| 3 | same file | `A_duplicate_with_an_invalid_name_is_reported_as_a_duplicate_not_as_an_invalid_name` |
| 4 | `tests/SystemVariables.Infrastructure.Tests/Resolution/ReverseIndexSeederBranchCharacterisationTests.cs` | `A_non_guid_identifier_aborts_the_seed_after_the_overlays_before_it` |

Each class summary's "Spec 349 section 6 oddity (x) is pinned as it is" paragraph is rewritten to
say the oddity was fixed by spec 358. Every other test in those classes stays unmodified and green.

## 5. Independent end-to-end test procedure

Unit level is sufficient and is the proof (plan §5): every changed behaviour is a log line, a
reservation call or an upsert sequence, each fully observable through the existing fakes
(`CapturingLogger`, `RecordingDedupStore`, `RecordingReverseIndex`, `TypeRecordingEventBus`). No
wire contract, schema, endpoint or DI registration changes, so an Aspire boot would observe nothing
the unit tests do not.

```sh
dotnet test tests/SystemVariables.Application.Tests --filter "FullyQualifiedName~FanOutCharacterisation|FullyQualifiedName~ValueRequest"
dotnet test tests/SystemVariables.Infrastructure.Tests --filter "FullyQualifiedName~ReverseIndexSeeder"
```

## 6. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052), sentence-style names (ADR-0053), existing
`[LoggerMessage]` methods reused — **no new log message** (ADR-0050), `Ensure.That` guards unchanged
(ADR-0105), `var` / collection-expression rules per CLAUDE.md.

## 7. Latency budget impact (constitution §IV)

Leg: **event → overlay state** (the two fan-out handlers and the value-request handler sit on it).
No I/O is added: D1 replaces one integer with another, D2 adds one debug log on a branch that
publishes no frame, D3 moves an in-memory parse ahead of the dedup round trip (removing that round
trip for invalid names, identical for valid ones). D4 runs once at startup, off the path. Net impact:
none.

## 8. Assumptions (marked)

- **A1 (D4)**: a skipped malformed identifier is skipped **silently**, exactly as the existing
  missing-identifier and missing-`elements` skips are; the count line exposes the shortfall. Adding a
  per-overlay warning would be a new log message and a new behaviour the issue did not ask for.
- **A2 (D1)**: when every affected overlay is skipped the line reads "to 0 overlays" at Information,
  rather than switching to another message. Truthful, smallest change.
- **A3 (D3)**: an invalid name is logged on every delivery (no reservation to suppress the repeat),
  the same as the missing/unusable-fab refusals already are.
