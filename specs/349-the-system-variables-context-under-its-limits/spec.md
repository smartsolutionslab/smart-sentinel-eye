# Spec 349: The system variables context under its limits

**Feature Branch**: `refactor/2891-system-variables-metrics` (cut from `origin/develop` at `782c1b70`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Four judgement calls
are marked explicitly (§6).

**Input**: Issue [#2891](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2891),
*Clear the ADR-0084 metric warnings in System Variables's Domain, Application and Infrastructure (16)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 349. Checked on 2026-10-14: `origin/develop` holds 341, 342, 345, 346 and 347
(the highest); `git ls-tree` over every remote head finds 348 only on
`origin/refactor/2890-identity-metrics` (open PR #2905, Identity only) and nothing above it; the
other open PRs (#2811, #2837) carry no spec. Highest visible is 348. Re-check before the PR is
opened (a parked PR may have claimed 349 meanwhile).

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S138 <= 30 lines, S107 <= 4 parameters,
S1541 <= 10, S134 <= 3, advisory `warning`; migrations out of the count), ADR-0144 (autonomous
lane; phase 4a colours; the lane may not weaken a gate), ADR-0036 (smallest change), ADR-0050
(`[LoggerMessage]` fields come from the template), ADR-0051 (per-context DI module), ADR-0102 /
ADR-0115 (event metadata carries the fab; siblings resolve in the fab that changed), ADR-0141
(`Option<T>` advisory in Application), ADR-0143 (retry opt-in stays where it is).

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written: `src/SystemVariables` holds no project file. The
equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/SystemVariables/Infrastructure/SmartSentinelEye.SystemVariables.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep SystemVariables
```

Result on `782c1b70`: 0 errors, **16 hits, exactly the 16 the issue lists, at the same lines. None
is stale.** Measured values: `VariableConfiguration.Configure` 65 lines;
`VariableArchivedDomainEventHandler.Handle` 63; `SystemVariableValueRequestedV1Handler.Handle` 54;
`ReverseIndexSeederHostedService.StartAsync` 51; `VariableValueChangedDomainEventHandler.Handle`
46; `AddSystemVariablesInfrastructure` 41; the generated `FabScopeSystemVariables.Up` 38;
`VariableValueChangedDomainEventHandler.BuildSnapshotAsync` 33; `VariableSnapshotBuilder.BuildAsync`
32; `Variable.Define` 7 parameters; both domain-event handlers' primary constructors 6; four
`[LoggerMessage]` methods 5 each. (S138 counts non-blank, non-comment lines between the method's
braces.)

## 2. Decision per hit

| # | File:line | Member | Rule | Decision |
|---|---|---|---|---|
| 1 | `Application/EventHandlers/SystemVariableValueRequestedV1Handler.cs:38` | `Handle` (54) | S138 | **Clear** by extraction, no allocation, I/O, lock or `await` added (plan §3.1) |
| 2 | `Application/EventHandlers/VariableArchivedDomainEventHandler.cs:21` | primary constructor (6) | S107 | **Leave**: DI constructor; every `VariableArchivedDomainEventHandlerTests` fact constructs it with six arguments, so a parameter object changes tests that must stay unmodified (J4) |
| 3 | `Application/EventHandlers/VariableArchivedDomainEventHandler.cs:30` | `Handle` (63) | S138 | **Clear** by extraction (plan §3.3) |
| 4 | `Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:23` | primary constructor (6) | S107 | **Leave**: same reason as hit 2 (`VariableValueChangedDomainEventHandlerTests`, `VariableValueChangedPreCommitTests`) |
| 5 | `Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:32` | `Handle` (46) | S138 | **Clear** by extraction, no per-event allocation, I/O, lock or `await` added (plan §3.2) |
| 6 | `Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:111` | `BuildSnapshotAsync` (33) | S138 | **Clear** by extracting the name parse to a synchronous helper (plan §3.2) |
| 7 | `Application/Log.cs:12` | `SetVariable` `[LoggerMessage]` (5) | S107 | **Leave**: parameters are the structured log template's fields (ADR-0050) |
| 8 | `Application/Log.cs:15` | `DefinedVariable` `[LoggerMessage]` (5) | S107 | **Leave**: same reason |
| 9 | `Application/Log.cs:42` | `ValueRequestWithUnusableFab` `[LoggerMessage]` (5) | S107 | **Leave**: same reason |
| 10 | `Application/Log.cs:51` | `SetVariableValueFailed` `[LoggerMessage]` (5) | S107 | **Leave**: same reason |
| 11 | `Application/Resolution/VariableSnapshotBuilder.cs:20` | `BuildAsync` (32) | S138 | **Clear** by extraction (plan §3.4) |
| 12 | `Domain/Variable/Variable.cs:55` | `Variable.Define` (public, 7) | S107 | **Leave**: public Domain factory called by `DefineVariableCommandHandler`, `VariableBuilder` (Domain.Tests) and `VariableValueChangedPreCommitTests` (twice); a parameter object changes every caller, tests included, so "tests unmodified" cannot hold |
| 13 | `Infrastructure/Persistence/Configurations/VariableConfiguration.cs:29` | `Configure` (65) | S138 | **Clear** by extraction (plan §3.5) |
| 14 | `Infrastructure/Persistence/Migrations/20260810042220_FabScopeSystemVariables.cs:50` | generated `Up` | S138 | **Leave**: generated, already applied; the issue forbids touching migrations |
| 15 | `Infrastructure/Resolution/ReverseIndexSeederHostedService.cs:46` | `StartAsync` (51) | S138 | **Clear** by extraction (plan §3.6) |
| 16 | `Infrastructure/SystemVariablesInfrastructureModule.cs:42` | `AddSystemVariablesInfrastructure` (41) | S138 | **Clear** by extraction (plan §3.7; public signature unchanged) |

**Cleared: 8** (all S138: hits 1, 3, 5, 6, 11, 13, 15, 16). **Left: 8** (7 S107: 4
`[LoggerMessage]`, 1 public Domain factory, 2 DI constructors; 1 S138 in a migration).
Expected after-count: **8**, every one named in the PR body with its reason. Compare by member: the
two constructor hits may move by one line if a `using` is added above them.

## 3. User story

### US1 (P1): SystemVariables' non-Api layers meet ADR-0084 wherever that is mechanical

As a maintainer, I want the 8 clearable hits gone, each by an extraction, **without** any change
an HTTP caller, Automation, LayoutComposition, a kiosk, a log reader, the database, or the
event-to-overlay latency could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing SystemVariables test passes unmodified (happy path)
  Given SystemVariables.Domain.Tests (101), Application.Tests (113), Infrastructure.Tests (28) and Architecture.Tests
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: A value change publishes V1 first, then one V3 per overlay, with today's fields
  Given a changed variable referenced by two overlays, one of which has no cached label texts
  When the value-changed domain event is handled
  Then exactly SystemVariableValueChangedV1 then one ResolvedOverlayTextChangedV3 are published, in that order and with those type arguments
  And the V1 carries Variable, Name, Type, wire Value, ChangedAt, ChangedBy and metadata (OccurredAt = ChangedAt, Fab, Actor = ChangedBy, RootIngestedAt null)
  And the V3 carries the overlay, its resolved texts, its advanced version and metadata whose RootIngestedAt is the event's RootIngestedAt
  And the log reads exactly "Pushed ResolvedOverlayTextChanged to 2 overlays after '<name>' changed."

Scenario: An archive publishes V1 first, then one V3 per overlay, with the archived name literal
  Given an archived variable referenced by an overlay that also names a live, an unset and an unparseable placeholder
  Then the V1 then the V3 are published; the archived and unset placeholders stay literal; the live sibling resolves in the archiving fab
  And the V3 metadata carries no RootIngestedAt

Scenario: A value request is refused in today's order, with today's words (bad request / conflict)
  Given a request with no fab, one with an unusable fab, a duplicate, an invalid name, and one naming another fab's variable
  Then each is dropped with exactly today's log message and arguments
  And the no-fab and unusable-fab requests consume no dedup reservation while the invalid-name request does

Scenario: Snapshot resolution answers in label order with today's outcome per placeholder
  Given a label mixing resolved, unset, archived, unknown and unparseable placeholders across two fabs
  Then the PlaceholderResolution list equals the captured literal, including the answering fab on Resolved and Unset rows

Scenario: The reverse-index seed takes today's branch for each response
  Given 401, 403, 500, a body without "published", overlays missing an identifier or "elements", and a non-Guid identifier mid-list
  Then the log level and text, the overlays seeded and the count logged equal the captured literals

Scenario: The DI composition and the EF model are unchanged
  Then AddSystemVariablesInfrastructure registers the same SystemVariables-owned services, in the same order, with the same lifetimes
  And SystemVariablesDbContext's logical model (tables, columns, store types, converters, keys, indexes and filters, owned types) equals the captured table
```

**Auth scenario: N/A.** No authorisation boundary moves. The fab scoping (spec 014 FR-005/FR-006,
ADR-0115) and the seeder's service-account credential (spec 126) stay where they are and are
pinned above, not changed.

#### Independent test procedure (end to end)

1. The `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 8 "Leave" rows after the change.
3. CI's Integration.Tests (`SystemVariables/*`, including `NFR_VariableResolutionLatencyTests`,
   `ResolvedTextReachesItsFabTests`, `CrossFabVariableIntegrationTests`,
   `ReverseIndexSeedCredentialTests`, and `Automation/EventReachesItsEffectsTests`) green: they
   boot the real stack and cover the EF mapping, the DI module, the seeder and the event path
   against Postgres and RabbitMQ. Docker-only, so observed in CI, not locally.

## 4. Functional requirements

- **FR-001** The 8 "Clear" hits of §2 report nothing after the change. If an extraction leaves a
  hit, the PR says so with the measured value; it is never suppressed.
- **FR-002** No public or `protected` signature changes: every handler's `Handle`, the handlers'
  constructors, `SystemVariableValueRequestedV1Handler.AutomationOperator`,
  `VariableSnapshotBuilder.BuildAsync`, `ReverseIndexSeederHostedService.StartAsync`/`StopAsync`,
  `VariableConfiguration.Configure`, `AddSystemVariablesInfrastructure`, the module's constants,
  `Variable.Define`, `Shared.Contracts` and `Log.cs`.
- **FR-003** For every input: every published message (generic type argument, order, every field),
  every log call (method, arguments, order), every dedup reservation, every repository and reverse
  index call and its order, every `PlaceholderResolution`, every seeded overlay and every EF model
  element are identical. Statements move verbatim; no "while I'm here" edits.
- **FR-004** Per-event path (constitution §IV, *Event → overlay state*): the changes to
  `SystemVariableValueRequestedV1Handler.Handle` and `VariableValueChangedDomainEventHandler`
  (`Handle`, `BuildSnapshotAsync`) add **no heap allocation, no I/O, no lock and no `await`** per
  event, per overlay or per placeholder. Extracted helpers there are synchronous (no new `async`
  method, no new state machine), take no closures, and return `bool` with `out` values, a value
  already being produced, or the integration record already being built.
- **FR-005** No new primitive on a Domain type (constitution §II): Domain is not changed.
- **FR-006** DI registrations keep their order and lifetimes; EF configuration produces the same
  model.
- **FR-007** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change (the
  existing `#pragma warning disable S1075, S5332` around the seed client stays exactly as it is);
  no guard edited; no file under any `Api/` or `Migrations/` directory touched; `Log.cs` and the
  Infrastructure `Log.cs` untouched.
- **FR-008** Every `EventMetadata` construction stays textually inside the `Handle` method it is in
  today (`EventMetadataFabDeclarationTests`), and its fab argument stays `fab.Value`.

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 8 "Leave" rows.
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted, plus each group's named mutation observed red and reverted (plan §4).
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  two touched projects; coverage gates hold (Domain >= 90 %, Application >= 80 %).
- **SC-004** Latency leg cited in the PR body as *Event → overlay state*, unchanged by
  construction (FR-004), with Integration.Tests' `NFR_VariableResolutionLatencyTests` figure
  quoted from the last green `develop` run (before) and from this PR's CI run and one re-run
  (after, twice).

## 6. Judgement calls (marked)

- **J1. The integration-event mapping leaves `Handle`, its metadata does not.** Neither
  domain-event handler's `Handle` can reach 30 lines while it builds the seven- or five-field V1
  record inline. The V1 construction moves to a private static mapper
  (`ToValueChangedV1(VariableValueChangedDomainEvent, EventMetadata)`,
  `ToArchivedV1(VariableArchivedDomainEvent, EventMetadata)`) that returns the concrete contract
  type, so `PublishAsync<TEvent>` still binds to it. The `new EventMetadata(...)` is hoisted to a
  local **inside** `Handle` and handed over as `Metadata: metadata` — the exact shape spec 347's
  `FabEventIngestedV1Handler` already uses on `develop`, which `EventMetadataFabDeclarationTests`
  scans and accepts (plan §2). The deconstruction line then discards the fields only the mapper
  reads (`variable`, `type`, `value`; `variableIdentifier`), which `HandlerDeconstructionTests`
  tolerates (`_` is skipped) and CLAUDE.md asks for.
- **J2. The per-overlay V3 is constructed on one line.** `ResolvedOverlayTextChangedV3 @event =
  new(Overlay: ..., Texts: ..., Version: versionsByOverlay[overlayId], Metadata: metadata);` with a
  hoisted `var metadata = new EventMetadata(...)` before it. Same arguments, same order, same
  evaluation sequence (texts, then metadata, then the record); the target-typed `Metadata: new(`
  spelling disappears from both files.
- **J3. The archive handler gets its own `BuildSnapshotAsync`.** Its inline snapshot loop moves
  verbatim into a private async method taking `(labelTexts, fab, archivedName,
  cancellationToken)`. That adds one async method invocation per overlay — acceptable because an
  archive is an operator's HTTP action (`POST /system-variables/{name}/archive`), not a plant-floor
  event, so it is **not** on §IV's *Event → overlay state* leg. The value-changed handler, which
  is, gains no async method.
- **J4. The two 6-parameter DI constructors are left.** `IEventBus`, `IReverseIndex`,
  `IOverlayTextVersions`, `IVariableRepository`, `IResolver` and the logger are six independent
  collaborators. A bundling parameter object would change all 15 test constructions in
  `VariableArchivedDomainEventHandlerTests`, `VariableValueChangedDomainEventHandlerTests` and
  `VariableValueChangedPreCommitTests` and the DI graph, so it is a design change, not this issue.

No defect was found while reading the touched members, so this is not a refactor-plus-fix. Four
oddities are recorded and **not** changed; if any is thought wrong, that is a separate issue:
(a) `PushedResolvedTextAfterChange`/`AfterArchive` log `affectedOverlays.Count`, which includes
overlays skipped for having no cached label texts; (b) the archive handler logs nothing when no
overlay references the variable, where the value-changed handler logs
`NoOverlaysReferenceVariable`; (c) `SystemVariableValueRequestedV1Handler` reserves the dedup key
before validating the variable name, so an invalid name consumes a reservation; (d) the seeder's
`idElement.GetGuid()` on a non-Guid identifier aborts the seed after the overlays already seeded
(`SeedFailed`, no `SeededOverlays`). Characterisation pins all four as they are.

## 7. Latency budget impact (constitution §IV)

| Member | Runs | §IV leg |
|---|---|---|
| `SystemVariableValueRequestedV1Handler.Handle` | per rule-driven value request | **Event → overlay state** (≤ 200 ms) — Automation's `SystemVariableValueRequestedV1` to the value write |
| `VariableValueChangedDomainEventHandler.Handle`, `BuildSnapshotAsync` | per value change, per overlay, per placeholder | same leg — the value write to `ResolvedOverlayTextChangedV3` |
| `VariableArchivedDomainEventHandler.Handle` | per operator archive | none — not event-caused |
| `VariableSnapshotBuilder.BuildAsync` | per snapshot / resolve query | none — the read side, not the push |
| `ReverseIndexSeederHostedService.StartAsync`, DI module, EF configuration | at boot / per request | none |

The leg is touched and held unchanged by construction: FR-004 forbids any allocation, I/O, lock
or `await` added per event, per overlay or per placeholder; the extracted helpers there are
synchronous calls on values already in hand, and the hoisted `EventMetadata` is the same single
allocation it is today. Evidence: `NFR_VariableResolutionLatencyTests` and
`ResolvedTextReachesItsFabTests` in CI (no local benchmark exists for this context).

## 8. Out of scope

Every `Api/` file, migrations, tests' own metrics, the `ServiceDefaults` hits the same build
prints, both `Log.cs` files, `Variable.Define`, the two handler constructors, and the four
oddities in §6.

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052), sentence-style names (ADR-0053), `Ensure.That`
guards (ADR-0105), `[NotNullWhen]` on `Try*` helpers rather than `!`, `var` wherever it compiles,
collection expressions, fluent DI chains, one parameter per line when wrapping, no leading
underscores (CLAUDE.md house rules). No Testcontainers (ADR-0103).
