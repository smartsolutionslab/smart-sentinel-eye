# Plan 358: Four SystemVariables defects

**Spec**: [spec.md](./spec.md). **Issue**: #2935. **Bounded context**: SystemVariables
(Application + Infrastructure). No Domain, Api, `Shared.Contracts`, migration or AppHost change.
No cross-context reference is added (NetArchTest unaffected).

## 1. Constitution / ADR alignment

- §Testing / ADR-0144: behaviour-changing → **red first**; the spec-349 characterisation pins are
  replaced, not edited to fit — each replacement is observed failing on unmodified code.
- ADR-0050: reuse `Log.NoOverlaysReferenceVariable`, `Log.InvalidVariableName`,
  `Log.SeededOverlays`; no new `[LoggerMessage]`.
- ADR-0084: each change must keep the touched methods within the advisory metrics the spec-349
  refactor reached (no new nesting level; D4 adds one condition to an existing guard).
- No new ADR needed: every chosen behaviour follows a convention already written in the same file
  (§2 of the spec).

## 2. Changes, one commit pair per defect

### D1 — count what was pushed
`src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs` and
`VariableArchivedDomainEventHandler.cs`: keep a local `var pushed = 0;` before the `foreach`,
increment after `events.PublishAsync(@event, …)`, pass `pushed` to
`PushedResolvedTextAfterChange` / `PushedResolvedTextAfterArchive`. `AdvanceAsync(affectedOverlays)`
is untouched (spec §2, out of scope).

### D2 — the archive handler says nobody references the variable
`VariableArchivedDomainEventHandler.cs`: in the `affectedOverlays.Count == 0` branch call
`logger.NoOverlaysReferenceVariable(variableName);` before `return`, mirroring the value-changed
handler line for line.

### D3 — validate the name before reserving
`src/SystemVariables/Application/EventHandlers/SystemVariableValueRequestedV1Handler.cs`: move the
`TryParseVariableName` call (and its `return`) above `dedup.TryReserveAsync`. The reservation keeps
its existing `(fab, string name, causingEvent)` signature — passing `name` unchanged, so the stored
key is byte-identical for valid names and no migration or interface change follows. Update the class
summary sentence and the `TryResolveFab` comment only if they now misdescribe the order (the latter
already says fab refusals consume no key; extend it to "nor does an invalid name" only where the
comment lives — no drive-by comments elsewhere).

### D4 — skip a malformed identifier
`src/SystemVariables/Infrastructure/Resolution/ReverseIndexSeederHostedService.cs`, `SeedOverlays`:
replace the `TryGetProperty("overlayIdentifier", …)` guard + later `GetGuid()` with one guard that
also requires `idElement.ValueKind == JsonValueKind.String && idElement.TryGetGuid(out var id)`.
`TryGetGuid` alone is **not** enough: it throws `InvalidOperationException` on a non-string element,
which is what the second D4 test exists to prove. Keep the guard ahead of the `elements` check or
after it — order is not observable, both skip — but upsert/`seeded++` stay together.

## 3. Invariants preserved (every other pinned test stays unmodified and green)

- Message order V1 → V3s, per-message event identifiers, metadata, version advance once for all
  listed overlays (both fan-out characterisation classes).
- Refusal order: no-fab → unusable fab → **invalid name** → duplicate → set failures. The only
  movement is invalid name ahead of duplicate; all fab tests already assert `Dedup.Calls` empty.
- Seeder: 401/403/500/404/missing-key/empty-list/cancellation branches and the
  missing-identifier/missing-`elements` skips unchanged.

## 4. Tests (red, test-writer)

Replace in place (same classes → no new test classes, so no shard-filter entries are needed):

| Defect | File | New test (replaces the §4 pin) | Red on develop because |
|---|---|---|---|
| 1 | `tests/SystemVariables.Application.Tests/EventHandlers/ValueChangedFanOutCharacterisationTests.cs` | `The_versions_are_advanced_once_for_both_listed_overlays_and_the_log_counts_only_the_pushed_one` — `AdvanceCalls == [A, B]`; message `"Pushed ResolvedOverlayTextChanged to 1 overlays after 'oeeLine1' changed."` | logs 2 |
| 1 | `tests/SystemVariables.Application.Tests/EventHandlers/ArchivedFanOutCharacterisationTests.cs` | `The_versions_are_advanced_once_and_the_log_counts_only_the_pushed_overlay` — `"… to 1 overlays after 'retired' was archived."` | logs 2 |
| 2 | same archive file | `With_no_referencing_overlay_only_the_V1_is_published_and_the_debug_line_is_logged` — V1 only, no advance, single Debug `"No overlays reference variable 'retired'; skipping push fan-out."` | logs nothing |
| 3 | `tests/SystemVariables.Application.Tests/EventHandlers/ValueRequestRefusalOrderCharacterisationTests.cs` | `An_invalid_name_is_dropped_before_any_reservation` — `Dedup.Calls` empty; single Warning, exact invalid-name text and exception message as the replaced pin | one reservation call |
| 3 | same file | `An_invalid_name_is_reported_as_invalid_even_when_its_key_would_be_a_duplicate` — `reserves: false`; `Dedup.Calls` empty; single Warning invalid-name line | Debug "Dedup hit" |
| 4 | `tests/SystemVariables.Infrastructure.Tests/Resolution/ReverseIndexSeederBranchCharacterisationTests.cs` | `A_non_guid_identifier_is_skipped_and_the_overlays_after_it_are_still_seeded` — upserts First then Third with their texts; single Information `"ReverseIndex seeded with 2 published overlays."` | `SeedFailed`, one upsert |
| 4 | same file (added) | `A_non_string_identifier_is_skipped_like_a_non_guid_one` — `"overlayIdentifier": 42` between First and Third; same expectations | `SeedFailed` (`InvalidOperationException`) |

Existing literal-text style is kept (full hand-written messages, distinct values per slot), so an
argument transposition still changes the text.

Counterfactuals the engineer/reviewer should see fail: D1 — increment before the `labelTexts is null`
check; D4 — `TryGetGuid` without the `ValueKind` check (second D4 test throws).

## 5. Verification

Unit-level proof is sufficient (spec §5): no contract, schema, endpoint or DI change, and every
behaviour is observable through existing fakes. Run full `SystemVariables.Application.Tests` and
`SystemVariables.Infrastructure.Tests`; Application/Infrastructure coverage gates hold (ADR-0065);
`dotnet build -c Release` and `dotnet format --verify-no-changes` clean. No Aspire boot.
