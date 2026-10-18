# Tasks 403: A camera's address changes reach its stream in the order the catalogue made them

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3008 (feature-level; no
per-task issues). On Project #13, verified 2026-10-18 (status In Progress, labels `tech-debt`,
`agent:ready`):
`gh project item-list 13 --owner smartsolutionslab --limit 2000 --format json -q '.items[] | select(.content.url != null) | select(.content.url | endswith("/3008"))'`.

**Phase 4a colour: RED** (behaviour-changing: a later change stamped earlier now applies; a far-future
stamp is no longer clamped). V1-V6, V9, V10, R8 and R9 are seen **failing on assertions** (R9: on a
Postgres `42703`) against unchanged production code. G4-G6 are **guards**, green on arrival by
nature, stated as such, never offered as red evidence; their counterfactuals are run after green and
quoted. K1-K5 and the mechanical edits cannot compile until the types exist: written in the working
tree, compile errors quoted, committed with their fix. A red test arriving green is a phase-4 failure.

**Adversarial pass: required** (T005). **Security-sensitive: yes** — stream attribution, and the
clamp removed here was a security mitigation; spec §8 R-V1 needs the security reviewer's written
concurrence or goes to the owner. Phase 6 runs `security-reviewer` alongside `/code-review`.

**Roles:** test-writer T001-T004; test-adversary T005; **one backend-engineer** T010-T016 (three
contexts, one branch — ADR-0109 `[P]` marks below are file-disjointness, not a licence for two
writers on one index).

**Forbidden in every task:** a `V2` of `CameraAddressChangedV1` or any other contract change than
FR-001's member; any ADR, the constitution, CLAUDE.md; AuditObservability source; the CameraCatalog
schema / migrations; `AggregateVersionInterceptor`; `ChangeCameraAddressCommandHandler`; Wolverine,
outbox and idempotency configuration; `IRtspGateway` / the MediaMTX gateway; the reconciler and spec
378 files; `Stream.Provision`; `RepointStreamErrors.cs`; shard filters; suppressions, thresholds,
`.editorconfig`, analyzer configs; **any existing assertion** other than the retirements listed in
plan §8.3 (three files deleted, three registration lines removed) — the only other existing-test
edits allowed are plan §8.3's mechanical ones (11 constructor, 8 command, 31 `RepointTo` sites).
Inventing a bound for R-V1 is a decision: report it, do not build it.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** T010 (the contract member) blocks T011-T016 and K1-K5. After T010, the CameraCatalog
tasks (T011) and the StreamDistribution tasks (T012-T015) own disjoint files.

---

## Phase 1: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] V1-V6, V9, V10 + G4-G6 — new
  `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedVersionOrderingTests.cs`
  per plan §8.1: harness constructed exactly as `CameraAddressChangedOrderingTests` constructs it
  today; messages built **from JSON** (serialize a `CameraAddressChangedV1`, add or omit
  `"PreviousVersion"`, deserialize). No type that does not exist. Run
  `dotnet test tests/StreamDistribution.Application.Tests -c Release --filter "FullyQualifiedName~CameraAddressChangedVersionOrderingTests"`;
  return the verbatim output: V1-V6, V9, V10 **fail on assertions** (V1 on `SourceUrl` being B), G4-G6
  pass.
- [ ] **T002** [P] [US1] R8 — `tests/StreamDistribution.Infrastructure.Tests/StreamDistributionModelCharacterisationTests.cs`:
  insert the one `source_camera_version` line of plan §8.1 between `mediamtx_path` and
  `source_changed_at`; nothing else. Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~StreamDistributionModelCharacterisationTests"`;
  return verbatim: `The_stream_distribution_model_has_exactly_this_shape` **fails** (line missing), the
  snapshot test passes.
- [ ] **T003** [P] [US1] R9 + serializer measurement —
  (a) `tests/Integration.Tests/StreamDistribution/RepointStreamIntegrationTests.cs`: add
  `Correcting_a_cameras_address_records_the_camera_version_it_was_made_against` (plan §8.1, raw SQL;
  existing class, in `shard-1.filter`: **no filter edit**). Red needs the stack: if Docker is up
  (one stack per machine; C: is nearly full — prefer CI), run
  `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~RepointStreamIntegrationTests"`
  and quote the `42703`; otherwise the orchestrator pushes commit 1 alone and quotes the CI job log, or
  the PR says the round trip was not taken. **Do not leave a stack running.**
  (b) Establish, from the pinned `WolverineFx` 6.48.3 (decompile or a throwaway host with
  `AddWolverine` reading its configured serializer), which serializer the RabbitMQ transport uses and
  that it ignores an unknown member and defaults a missing constructor parameter. Report the evidence;
  write it into plan §7 in place of "measured in T003". No production change either way unless it
  contradicts §7 — then **stop and report**.
  **Commit T001-T003 only:** `test(stream-distribution): pin repoint ordering by camera version (red)`.
- [ ] **T004** [US1] K1-K5 + mechanical edits — write K1-K5 (plan §8.2) and apply the plan §8.3
  mechanical edits (re-count first; a different count is stop-and-report). No assertion edited. Run
  `dotnet build tests/Shared.Contracts.Tests -c Release`, `dotnet build tests/CameraCatalog.Application.Tests -c Release`,
  `dotnet build tests/StreamDistribution.Application.Tests -c Release`; return the verbatim CS errors
  naming `PreviousVersion` / `SourceCameraVersion` / the arity. **Do not commit**; hand to T010.
  (K1-K5 only ever fail to **compile** before the fix; say so in the PR.)

## Phase 1b: Adversarial (test-adversary)

- [ ] **T005** [US1] new
  `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedVersionOrderingAdversarialTests.cs`
  per plan §8.4. Findings R-V1 (`int.MaxValue` pin) and R-V2 (overlap) are **reported, not fixed**.
  Return verbatim results (compile errors if it needs the new types); committed with commit 3.

## Phase 2: Green (backend-engineer; may not edit any test written above)

- [ ] **T010** [US1] **Foundational** — `src/Shared.Contracts/CameraCatalog/CameraAddressChangedV1.cs`:
  trailing `int? PreviousVersion = null` and its doc comment (plan §2.1, spec FR-001). In the same
  commit, `CameraAddressChangedIntegrationEventHandler`'s deconstruct becomes 8-ary with `_` for the
  new member (the build must hold at this commit; the handler uses it in T013). K1 green.
- [ ] **T011** [P] [US1] CameraCatalog — `CameraAddressChangedDomainEvent` trailing `AggregateVersion
  PreviousVersion`; `Camera.ChangeAddress` raises `PreviousVersion: Version`; the domain-event handler
  maps `.Value`. Doc comments say "the version the change was made against (its `If-Match`)". K2, K3
  green; every existing CameraCatalog test green unmodified.
  **Commit T010 + T011 + K1-K3:** `feat(camera-catalog): announce the camera version an address change was made against`.
- [ ] **T012** [P] [US1] StreamDistribution Domain — `SourceCameraVersion.cs` (plan §2.2);
  `Stream.SourceCameraVersion?` with its doc comment; `RepointTo` gains `Option<SourceCameraVersion>`
  and the plan §2.3 truth table; `RepointTo`'s doc comment states the three rules. K4, K5 green.
- [ ] **T013** [US1] StreamDistribution Application — `RepointStreamCommand` fourth component;
  integration event handler validates and passes the version (plan §3; `previousVersion` local);
  `RepointStreamCommandHandler` takes `IClock` instead of `ChangedAtPolicy`, deletes
  `ClampFutureStamp` and the poisoned branch, logs per plan §4-5; `Log.cs` +`IgnoredOlderVersionedRepoint`,
  −`ClampedFutureChangedAt`, −`IgnoredAgainstFutureRecordedStamp`; delete `ChangedAtPolicy.cs`,
  `StreamRepointOptions.cs`, `ChangedAtPair.cs`. `Ensure.That`, `var`, no underscores.
- [ ] **T014** [US1] StreamDistribution Infrastructure — delete `BindStreamRepoint` and its call
  (`StreamDistributionInfrastructureModule.cs:50,331-342`); `StreamConfiguration` mapping (plan §6);
  `dotnet ef migrations add RecordStreamSourceCameraVersion --project src/StreamDistribution/Infrastructure --output-dir Persistence/Migrations`.
  Read `Up`/`Down` against plan §6; **paste**
  `git diff --stat origin/develop -- src/StreamDistribution/Infrastructure/Persistence/Migrations`
  into the hand-back (three files).
- [ ] **T015** [US1] Retirements, exactly plan §8.3's table: delete
  `CameraAddressChangedFutureStampTests.cs`, `CameraAddressChangedFutureStampToleranceTests.cs`,
  `StreamRepointToleranceValidationTests.cs`; remove `StreamDistributionRegistrationCharacterisationTests.cs:77-79`
  and nothing else from that list. Each retired fact is named in the PR body with its file:line and
  "retired by the owner decision on #3008 (clamp removed)".
- [ ] **T016** [US1] Docs — spec 385 `spec.md` status line and §8 FR-010-FR-013 marked
  "superseded by spec 403"; plan 385 §10 likewise. No other spec edited.
  **Commit T012-T016 + T004 (remaining) + T005:** `fix(stream-distribution): order repoints by camera version, not ChangedAt, and drop the clamp`.

## Phase 3: Verify

- [ ] **T020** Local: `dotnet build -c Release` clean (no new warnings beyond advisory S/IDE1006);
  `dotnet test` for `Shared.Contracts.Tests`, `CameraCatalog.Domain.Tests`,
  `CameraCatalog.Application.Tests`, `StreamDistribution.Domain.Tests`,
  `StreamDistribution.Application.Tests`, `StreamDistribution.Infrastructure.Tests`,
  `AuditObservability.Application.Tests`, `Architecture.Tests` (`PrimitiveBoundaryTests`,
  `HandlerDeconstructionTests`, `BoundaryTests`, `AuditContractPropertyShapeTests`,
  `AuditPayloadRedactionCoverageTests`, registration characterisation). Counterfactuals quoted: `<` →
  `<=` in rule 1 turns G4 red; making a versionless message always apply turns G5 and the spec 385
  `CameraAddressChangedOrderingTests` red; dropping rule 2 (fallback to `ChangedAt`) turns V6 red;
  restoring the clamp turns V3-V5 red. Each commit builds on its own (check out each, `dotnet build
  -c Release`). CI (authoritative, Docker): R9, the existing `RepointStreamIntegrationTests` facts
  and `CameraAddressAuditIntegrationTests` green; coverage gates (ADR-0065) hold.
- [ ] **T021** Phase 6: `/code-review` + `security-reviewer`: attribution window closed for the
  clock-step and fast-clock cases; ignored lines carry origin only; the clamp's removal assessed;
  **R-V1 concurred in writing or escalated to the owner on #3008**; R-V2 deploy-order note present.

## Dependencies

T001, T002, T003 [P] → commit 1. T004, T005 (uncommitted) → **T010 (foundational)** → T011 → commit 2
→ T012 → T013 → T014 → T015 → T016 → commit 3 → T020 → T021. T011 and T012 own disjoint files
(CameraCatalog vs StreamDistribution) and could be written in either order; T012-T014 are sequential
(each compiles only on its predecessor). Commit 2 must build without commit 3: the consumer discards
the member there.
