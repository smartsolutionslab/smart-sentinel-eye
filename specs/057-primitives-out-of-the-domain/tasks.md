# Tasks — 057 primitives out of the domain

**Feature**: [spec.md](./spec.md) · [plan.md](./plan.md) · [research.md](./research.md) · [data-model.md](./data-model.md) · [contracts/enforcement-contract.md](./contracts/enforcement-contract.md) · [quickstart.md](./quickstart.md)
**Branch**: `057-primitives-out-of-the-domain`

47 tasks in eight phases. Phase 3's gate is **a build that fails** — the one
phase in this repository's history where green means the work did not happen.

---

## Do not

- **Do not name the ban list `BannedSymbols.Guards.txt`.** That was this
  feature's own first proposal and research R1 retired it. The filename must
  be `BannedSymbols.txt`; only the folder differs. The prefix match that would
  accept the other name is undocumented and has regressed once already.
- **Do not write `AggregateVersion` as a `class`.** EF derives the concurrency
  comparer from the type's equality. A class compares references, every stale
  write silently passes, and nothing fails to compile. It is a `record`.
- **Do not accept a green build at T006.** A ban list the analyzer never reads
  produces no error and is indistinguishable from compliance.
- **Do not commit a migration from the empty-diff probes.** An empty `Up()` is
  the pass; anything else is a defect to fix, not a migration to land.
- **Do not edit a test to make it pass.** Under the amended rule a red test
  during behaviour-preserving work is a regression. The edit is the finding.
- **Do not use `Option<T>` for persisted nullable properties.** Zero EF
  configurations map it, and `StreamConfiguration.cs:10` documents the
  ADR-0048 carve-out. Nullable value-object references.
- **Do not add an `Ensure.That(DateTimeOffset)` overload.** Timestamps
  normalize; they validate nothing. The feature description expected this
  overload and research retired it.
- **Do not let a boundary conversion failure surface as `500`.** `Ensure`
  raises `ArgumentException` — a programmer-error signal. Endpoints parse into
  `Result<T, ApiError>` and return `400`.
- **Do not touch `Shared.Contracts`.** A wire format, primitives by design.
- **Do not sweep `AggregateVersion` across ten aggregates in one commit.**
  Rebase-merge lands commits individually; each must build alone.
- **Do not convert the three value-object backing strings**
  (`CameraName.NormalizedValue`, `Payload.Value`, `BearerTokenHash.Value`).
  They are the boundary the rule protects, not a violation of it.
- **Do not widen the ConfigureAwait ban.** It stays exempt for `Shared.*` and
  tests. Two lists, two scopes, on purpose.
- **Do not assume a value converter reaches raw SQL. It does not.**
  `AuditEventRepository.SaveAsync` inserts with
  `ExecuteSqlInterpolatedAsync`, which bypasses the change tracker entirely, so
  the converter never runs and EF fails with *"The current provider doesn't
  have a store type mapping for properties of type 'X'"* — at runtime, on
  every audit write. Interpolate `.Value` (and `?.Value` when nullable).

  This cost a full CI cycle in Phase 4 and **Phase 5 will hit it again in the
  same file**: `{row.OccurredAt}`, `{row.ReceivedAt}` and
  `{row.HandlerEnteredAt}` are all interpolated there and are all on the
  timestamp list. Check that file *before* retyping them, not after.

  Nothing catches this short of the integration suite — the build is clean, the
  unit tests are green, and the schema probe is empty, because none of them
  execute that INSERT. The five raw-SQL sites are listed in
  `verification.md`.

---

## Phase 1 — Baseline

**Purpose**: record what is true now, so every later claim is checkable
against a number rather than a memory.

- [X] T001 Stop any running AppHost, then run `dotnet build SmartSentinelEye.slnx -v:q --nologo` and `dotnet test SmartSentinelEye.slnx` from the repo root; confirm both green **before** any edit. A running stack holds the service binaries and the resulting `MSB3027` reads as a broken build.
- [X] T002 Record the exact pre-change counts in `specs/057-primitives-out-of-the-domain/verification.md`: banned-guard sites by category and path, the 9 text properties, the 26 timestamp properties, and `ExpectedVersion`/`Version` occurrences. These are the denominators for SC-002 and SC-003.

---

## Phase 2 — US1 + US6: the rules *(P1 — blocking)*

**Goal**: the three drifted rules state their scope, their exemptions, and
what counts as evidence.

**Independent test**: a reader can tell from the constitution alone whether a
given guard, primitive or test is compliant, without reading the codebase.

Every later phase implements a rule, so the rule has to exist and say what it
means first — otherwise the phases enforce something unwritten, which is the
condition this feature exists to end. Phases 2 and 3 ship as one PR.

- [X] T003 [US1] Write `docs/adr/0139-<slug>.md` carrying **both** amendments (§II's primitive list for US1, the §Testing split for US6): constitution §II's exhaustive primitive list, ADR-0105's extension from null-only to all argument preconditions, and the §Testing split. Record the four exemptions with reasons, and restate the migrations exemption **accurately** — research R4 found the migration files carry hand-written doc comments, so ADR-0105's "never hand-edited" is not quite true; the exemption rests on regeneration, not on nobody touching them.
- [X] T004 [US1] Amend `.specify/memory/constitution.md` §II to name the disallowed primitives exhaustively (`string`, `int`, `bool`, `double`, `decimal`, `float`, `long`, `Guid`, `DateTimeOffset`) and to list the exemptions. Keep the existing `CameraId`/`Percentage`/`Timestamp` illustration — §II's `Timestamp` line is what Story 3 finally makes true.
- [X] T005 [US6] Amend `.specify/memory/constitution.md` §Testing: replace the single "Domain logic: TDD red-green-refactor" bullet with the two obligations — red-first for new behaviour across domain, application **and** infrastructure, with the failure quoted; green-throughout for behaviour-preserving change, where a red test is a regression.
- [X] T006 [US6] Update `CLAUDE.md`: the Phase 4 gate row gains "new-behaviour tests were observed red first, failure quoted in the PR", and the house-rules value-object bullet points at §II's exhaustive list rather than restating a partial one. Keep it a pointer — a summary that competes with the authority is how §IV drifted.

---

## Phase 3 — US1 + US6: enforcement, and the guards *(P1 — the MVP)*

**Goal**: the banned idioms fail the build, everywhere they should, and every
surviving use is an exemption.

**Independent test**: reintroduce `ArgumentNullException.ThrowIfNull` anywhere
in `src/` or `tests/` and watch the build fail; revert and watch it pass.

**The gate is a failing build.** T007 must be observed red before T008–T010
convert anything. This is the phase's whole evidentiary value: it is the only
proof that the ban is wired rather than merely written.

- [X] T007 [US1] **GATE — observe red.** Add `build/guards/BannedSymbols.txt` with the banned set from contracts §1 (**all six entries, including the three with zero call sites** — this reverses the "trim what the codebase cannot hit" instruction this task originally carried; a prohibition is forward-looking, and banning one of a pair reads as a carve-out. Reasoning in contracts §1 and ADR-0139); add a second `AdditionalFiles` item in `Directory.Build.props` conditioned to exclude **only** `AppHost`, so `Shared.*` and `tests/` are covered; add `[**/Migrations/*.cs] dotnet_diagnostic.RS0030.severity = none` to `.editorconfig`. Build, and **confirm `error RS0030` at `src/LayoutComposition/Infrastructure/Cameras/CameraCatalogFabGuard.cs:41` and `:42`** plus the test-fake sites. Paste the output into `verification.md`. A green build here means the list is not being read — stop and fix the wiring, do not proceed.
- [X] T008 [US1] Convert the two production sites in `src/LayoutComposition/Infrastructure/Cameras/CameraCatalogFabGuard.cs` to `Ensure.That(x).IsNotNull()`.
- [X] T009 [P] [US1] Convert the ~24 BCL string and range guards across the 13 files in `src/` (`Automation/Domain/Rule/Rule.cs`, `EventIngestion/Domain/DeadLetter/DeadLetter.cs`, `EventIngestion/Domain/WebhookIntegration/{BearerTokenHash,WebhookIntegration}.cs`, `Identity/Domain/RegisteredClient/ClientSecret.cs`, `Identity/Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.cs`, `ScenarioSimulator/CameraSim/CameraSimProvisioner.cs`, `ServiceDefaults/{AuthenticationDefaults,WolverineDefaults}.cs`, `ServiceDefaults/Persistence/PostgresConnectionBudget.cs`, `StreamDistribution/Domain/Stream/Stream.cs`, `StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs`, `SystemVariables/Infrastructure/Persistence/VariableValueRequestDedupStore.cs`). **Chain `.IsNotNull()` before `.IsNotNullOrWhiteSpace()`** — research R3: the single call collapses null to `ArgumentException` where the BCL raised `ArgumentNullException`.
- [X] T010 [P] [US1] Convert the ~28 guard sites in `tests/**/Fakes/` and `tests/Shared.Kernel.Tests/AggregateVersions.cs`.
- [X] T011 [US1] Rebuild; confirm **zero** `RS0030`. Then run quickstart check 2 and confirm the only surviving matches are `src/AppHost`, `**/Migrations/*.cs`, and `Ensure.cs`'s XML doc comments — prose, not call sites.
- [X] T012 [US1] Add a rule to `tests/Architecture.Tests/` asserting the guard ban list exists, is referenced from `Directory.Build.props`, and names each banned symbol. This guards the *silent* failure mode: a ban list that stops being read fails no build and looks exactly like compliance. Name it for what it checks, in the file's existing sentence style.

**Checkpoint**: Phase 3 is independently shippable. If the feature stopped
here it would have closed the live drift and made recurrence a build error.

---

## Phase 4 — US2: text types *(P2)*

**Goal**: `""` cannot enter the domain, for any caller, ever.

**Independent test**: every one of the nine types refuses `""` and whitespace.

**Red first, per type.** Write each type's test before the type exists — it
will fail to *compile*, which is the strongest red available. Quote one such
failure in the PR (FR-008); nine near-identical quotations prove nothing the
first does not.

- [X] T013 [P] [US2] Write the invariant tests for `TriggerSource` and `TriggerKind` in `tests/Automation.Domain.Tests/` — `""` refused, whitespace refused, over-length refused (16 / 128), valid round-trips. Observe them fail.
- [X] T014 [P] [US2] Write the invariant tests for `DeliveryTopic` (≤ 256), `RejectionReason` (≤ 512), `RawPayload` (non-empty only) and `KeycloakClientIdentifier` (≤ 255, nullable) in `tests/EventIngestion.Domain.Tests/`. Observe them fail.
- [X] T015 [P] [US2] Write the invariant tests for `ActorUsername` (≤ 255, nullable) and `AuditPayload` (non-empty only) in `tests/AuditObservability.Domain.Tests/`. Observe them fail.
- [X] T016 [P] [US2] Write the invariant tests for `StreamError` (≤ 1024, nullable) in `tests/StreamDistribution.Domain.Tests/`. Observe them fail.
- [X] T017 [P] [US2] Add `TriggerSource` and `TriggerKind` in `src/Automation/Domain/Rule/`, deriving `StringValueObject`, validating in `From(...)` via `Ensure`; retype `Rule.TriggerSource` and `Rule.TriggerKind`.
- [X] T018 [P] [US2] Add `DeliveryTopic`, `RejectionReason`, `RawPayload` in `src/EventIngestion/Domain/DeadLetter/` and `KeycloakClientIdentifier` in `src/EventIngestion/Domain/WebhookIntegration/`; retype the four properties.
- [X] T019 [P] [US2] Add `ActorUsername` and `AuditPayload` in `src/AuditObservability/Domain/AuditEvent/`; retype both properties.
- [X] T020 [P] [US2] Add `StreamError` in `src/StreamDistribution/Domain/Stream/`; retype `Stream.LastError`, keeping it a nullable reference.
- [X] T021 [US2] Add `HasConversion` for all nine in the four affected files under `src/*/Infrastructure/Persistence/Configurations/`. **Leave every `HasMaxLength` exactly as it is** — the bound now lives in two places on purpose: the value object refuses it, the column still declares it, and the column is what keeps the schema identical.
- [X] T022 [US2] Run quickstart check 3 for Automation, EventIngestion, AuditObservability and StreamDistribution. Empty `Up()`/`Down()` each time; `migrations remove` after each probe.

---

## Phase 5 — US3: timestamps *(P3)*

**Goal**: an instant says which moment it is, and two instants cannot be
swapped.

**Independent test**: passing one timestamp type where another is expected
fails to compile; ordering and range queries return identical rows.

**Green throughout.** Nothing here is new behaviour. A red test in this phase
is a regression.

- [ ] T023 [US3] Audit which of the 26 properties are **queried, ordered, or range-filtered**, by reading the read-side handlers and EF configurations. Record the list in `verification.md`. Each one's type must carry `implicit operator DateTimeOffset` — `IngestedAt.cs` documents why: member access on a value-converted column does not translate.
- [ ] T024 [P] [US3] `AuditObservability`: add `OccurredAt`, `ReceivedAt`, `HandlerEnteredAt`, `WrittenAt` in `src/AuditObservability/Domain/AuditEvent/`; retype; map.
- [ ] T025 [P] [US3] `Automation`: add `CreatedAt`, `PublishedAt`, `ArchivedAt` in `src/Automation/Domain/Rule/`; retype; map.
- [ ] T026 [P] [US3] `CameraCatalog`: add `RegisteredAt` in `src/CameraCatalog/Domain/Camera/`; retype; map.
- [ ] T027 [P] [US3] `EventIngestion`: add `RejectedAt` in `Domain/DeadLetter/`, `RegisteredAt`, `RevokedAt`, `RotatedAt` in `Domain/WebhookIntegration/`; retype; map.
- [ ] T028 [P] [US3] `Identity`: add `RegisteredAt`, `DisabledAt`, `LastRotatedAt` in `src/Identity/Domain/RegisteredClient/`; retype; map.
- [ ] T029 [P] [US3] `LayoutComposition`: add `CreatedAt` (Layout), and `CreatedAt`, `PublishedAt`, `ArchivedAt` (Revision) in `src/LayoutComposition/Domain/Layout/`; retype; map. The two `CreatedAt`s are **distinct types** — that is the point.
- [ ] T030 [P] [US3] `OverlayDesigner`: add `CreatedAt` (Overlay), and `CreatedAt`, `PublishedAt`, `ArchivedAt` (Revision) in `src/OverlayDesigner/Domain/Overlay/`; retype; map.
- [ ] T031 [P] [US3] `StreamDistribution`: add `ProvisionedAt`, `LastSuccessAt` in `src/StreamDistribution/Domain/Stream/`; retype; map.
- [ ] T032 [P] [US3] `SystemVariables`: add `CreatedAt` in `src/SystemVariables/Domain/Variable/`; retype; map.
- [ ] T033 [US3] Run quickstart check 3 across all nine contexts. Empty diff each time.
- [ ] T034 [US3] Run the read-side integration tests that order or range-filter on a converted column and confirm identical rows — the specific failure T023 exists to prevent is a silent fall back to client evaluation, which passes tests while scanning the table.

---

## Phase 6 — US4: typed at the boundary *(P4)*

**Goal**: untrusted input becomes a value object where it arrives; everything
downstream is typed.

**Independent test**: malformed input returns `400` from the endpoint, not a
`500` from a guard deeper in.

- [ ] T035 [P] [US4] Retype the Automation shapes in `src/Automation/Application/`: `CreateRuleCommand.TriggerSource`/`TriggerKind`, `GetRuleQuery.Name`, `DryRunRuleQuery.Name`.
- [ ] T036 [P] [US4] Retype `src/StreamDistribution/Application/Commands/`: `ProvisionStreamCommand.RtspSourceUrl` and `RepointStreamCommand.RtspSourceUrl` to the **existing** `StreamSourceUrl`; `AuthorizeWhepCommand.BearerToken`.
- [ ] T037 [P] [US4] Retype `src/AuditObservability/Application/Queries/`: `GetAuditEventQuery.AuditIdentifier`, and `GetResourceTimelineQuery`'s `ResourceKind`, `ResourceIdentifier`, `Fab` — `ResourceKind` and `ResourceIdentifier` already exist as value objects.
- [ ] T038 [P] [US4] Retype `src/Identity/Application/Commands/`: `RegisterDeviceCommand.DeviceType`/`DeviceIdentifier`, `RotateWebhookClientCommand.IntegrationName`.
- [ ] T039 [P] [US4] Retype `src/CameraCatalog/Application/Queries/ListCamerasQuery.cs` (`Sort`, `Order`, `Offset`) and `src/SystemVariables/Application/Commands/SetVariableValueCommand.cs` (`WireValue`).
- [ ] T040 [US4] In each affected endpoint under `src/*/Api/`, parse into `Result<T, ApiError>` at the boundary and return `400` on failure. **Do not** let `Ensure`'s `ArgumentException` escape as a `500`.
- [ ] T041 [US4] Confirm the API and integration tests pass **unmodified**. A test needing an edit is evidence the retyping changed behaviour — record it, do not absorb it.

---

## Phase 7 — US5: `AggregateVersion` *(P5 — last, and severable)*

**Goal**: the concurrency token is a named type.

**Independent test**: a stale write is refused identically before and after,
per aggregate.

**This phase can be abandoned without unpicking Phases 3–6.** Research R2
proved the model validates and the column stays `integer`; it did **not**
prove a stale write is still refused at runtime. That is what T043 is for, and
why it runs against one aggregate before the other nine.

- [ ] T042 [US5] Add covering optimistic-concurrency tests wherever a retyped path lacks them, and confirm green **before** any retyping (FR-024). Green guaranteed by absent coverage is not a guarantee.
- [ ] T043 [US5] **GATE — prove it on one aggregate.** Add `src/Shared.Kernel/Primitives/AggregateVersion.cs` as a **`record`** with `From(int)` guarded by `Ensure.That(value).AtLeast(0)` and an implicit `int` unwrap; retype `AggregateRoot.Version` and `IVersionedAggregate.Version`; convert **CameraCatalog only** — its command, endpoint `If-Match` parsing, and `CameraConfiguration.cs`. Run quickstart check 4 and confirm a stale write is still refused with the same refusal name and `412`. Record the result. If it is not refused, **stop** and report — Phases 3–6 stand on their own.
- [ ] T044 [US5] Convert the remaining nine aggregates — Automation, AuditObservability (×2), EventIngestion (×2), Identity, LayoutComposition, OverlayDesigner, StreamDistribution, SystemVariables — **one commit per aggregate**, each building alone. Verify per commit, not per branch.
- [ ] T045 [US5] Run quickstart check 3 across all nine contexts and check 4 across all ten aggregates.

---

## Phase 8 — Polish

- [ ] T046 Run the full regression from quickstart: build, `dotnet test SmartSentinelEye.slnx`, and `./scripts/coverage-check.ps1`. Domain ≥ 90 / Application ≥ 80 / Shared ≥ 90 — Phase 4 adds nine branching factories into the ≥ 90 bucket.
- [ ] T047 Complete `specs/057-primitives-out-of-the-domain/verification.md`: the T007 red output, before/after counts against T002's baseline, the empty-diff results per context, the T043 stale-write result, and the proof table below. Open the PR against **`develop`** with `--base develop`, quoting the red from T007 and one from T013–T016.

---

## Dependencies

```
Phase 1 (baseline)
   └─> Phase 2 (the rules)          ← blocks everything; the phases enforce it
          └─> Phase 3 (US1+US6)     ← independently shippable MVP
                 ├─> Phase 4 (US2)  ─┐
                 ├─> Phase 5 (US3)  ─┤ independent of each other
                 │                   └─> Phase 6 (US4)  ← needs their types
                 └─> Phase 7 (US5)  ← independent; sequenced last by risk
                        └─> Phase 8
```

Within phases, `[P]` tasks touch disjoint files. T009 and T010 run in
parallel; T013–T016 all run in parallel; T017–T020 run in parallel once their
tests are red; T024–T032 are nine independent contexts; T035–T039 are five.

T043 blocks T044 absolutely. That is not a file dependency — it is the
severability the whole phase ordering exists to buy.

---

## What the checks do and do not prove

| Claim | Proved by | **Not** proved by |
|---|---|---|
| The ban is wired | T007's observed red | the list existing in the repo |
| The ban stays wired | T012 | T007, which is a one-time observation |
| No banned guard survives | T011's search | the build going green, which the exemptions also do |
| `""` cannot enter the domain | T013–T016 run before T017–T020 | the types existing |
| The schema did not move | T022, T033, T045 empty diffs | the column types looking unchanged |
| Two instants cannot be swapped | T024–T032 compiling as distinct types | one shared timestamp type, which would restore the swap |
| Range queries still translate | T034 | the tests passing, which client evaluation also allows |
| Malformed input is a client error | T040 + T041 | the endpoint having a `try` |
| A stale write is still refused | T043, run against a database | research R2, which proved only that the model validates |
| Each commit builds alone | verifying per commit in T044 | a green tip of the branch |
| **That the rules will not drift again** | **T012, and only for the guard rule** | this feature shipping |

The last row is the honest one. The guard ban is a build error and the
architecture test guards its wiring. The **primitive** rule and the **TDD**
rule are still enforced by review and by a PR quotation — stronger than
before, because both now state their scope and their evidence, but not
mechanical. A future spec could add a rule asserting no aggregate exposes a
banned primitive; this one does not, and saying so is better than implying a
guarantee that was never built.
