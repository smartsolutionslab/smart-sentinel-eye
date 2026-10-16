# Tasks 385: An older address change cannot re-point a stream back over a newer one

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2991 (feature-level; no
per-task issues — verify it is on Project #13 with `gh project item-list 13 --owner
smartsolutionslab --limit 2000 --format json -q '.items[] | select(.content.url | endswith("/2991"))'`).

**Phase 4a colour: RED** (behaviour-changing). R1-R6 are seen **failing on assertions** (R6: on a
Postgres `42703`) against unchanged production code. G1-G3 are **guards** — green on arrival by
nature (today's code applies everything), stated as such, never offered as red evidence; their
counterfactual (`<` → `<=`) is run after green and quoted. D1, D2 and the mechanical argument
additions cannot compile until the types exist: written in the working tree, compile error quoted,
committed with the fix. A red test arriving green is a phase-4 failure.

**Adversarial pass: required** (T005). **Security-sensitive: yes** (stream attribution) — phase 6
runs `security-reviewer` alongside `/code-review`.

**Engineer:** backend-engineer (T010-T013). **Test-writer:** T001-T004. **Test-adversary:** T005.

**Forbidden in every task:** `Shared.Contracts` (no `CameraAddressChangedV1` change, no `V2`);
CameraCatalog and every other context; `Stream.Provision`; `RepointStreamErrors.cs`; `IRtspGateway`
and the MediaMTX gateway; the reconciler / spec 378 files; Wolverine and idempotency configuration;
any **existing assertion** (the only existing-test edits allowed are plan §8.3's: one
`ExpectedModel` line, and argument additions at the 12 listed call sites); shard filters; ADRs, the
constitution, CLAUDE.md; suppressions, thresholds, `.editorconfig`, analyzer configs.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** T010 (`SourceChangedAt`) blocks T011-T013 and D1/D2. Phase 1's `[P]` tasks touch
three different test projects.

---

## Phase 1: Red (test-writer; production code untouched)

- [x] **T001** [P] [US1] R1-R4 + G1-G3 — new
  `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedOrderingTests.cs`,
  harness and facts exactly as plan §8.1 (real event handler over the real command handler; no new
  type referenced). Run
  `dotnet test tests/StreamDistribution.Application.Tests -c Release --filter "FullyQualifiedName~CameraAddressChangedOrderingTests"`;
  return verbatim output: R1-R4 **fail on assertions** (R1 on `SourceUrl` being `.../a`), G1-G3 pass.
- [x] **T002** [P] [US1] R5 — `tests/StreamDistribution.Infrastructure.Tests/StreamDistributionModelCharacterisationTests.cs`:
  insert the one `source_changed_at` line of plan §8.1 between `mediamtx_path` and `source_url`;
  nothing else. Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~StreamDistributionModelCharacterisationTests"`;
  return verbatim: `The_stream_distribution_model_has_exactly_this_shape` **fails** (line missing),
  `..._the_committed_snapshot_describes` passes.
- [x] **T003** [P] [US1] R6 — `tests/Integration.Tests/StreamDistribution/RepointStreamIntegrationTests.cs`:
  add `Correcting_a_cameras_address_records_when_the_change_was_made` (plan §8.1, raw SQL). Existing
  class, already in `shard-1.filter`: **no filter edit**. Red needs the stack: if Docker is up, run
  `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~RepointStreamIntegrationTests"`
  and quote the `42703`; otherwise the orchestrator pushes the Phase 1 commit alone and quotes the CI
  job log, or the PR says that round trip was not taken and relies on R1-R5.
  **Commit T001-T003 only:** `test(stream-distribution): pin repoint ordering by the change's own ChangedAt (red)`.
- [x] **T004** [US1] D1 + D2 + ripple — write `SourceChangedAtTests.cs` and
  `StreamRepointOrderingTests.cs` (plan §8.2) in `tests/StreamDistribution.Domain.Tests/Stream/`, and
  add the new argument at the 12 sites of plan §3 (`StreamRepointTests` 4,
  `RepointStreamCommandHandlerTests` 7, `RepointStreamOrderCharacterisationTests` 1 —
  `SourceChangedAt.From(Moment)` in `Harness.RepointAsync`). No assertion edited. Run
  `dotnet build tests/StreamDistribution.Application.Tests -c Release`; return the verbatim CS0246 /
  CS1729 errors naming `SourceChangedAt` / the arity. **Do not commit**; hand to T010.

## Phase 1b: Adversarial (test-adversary)

- [ ] **T005** [US1] new
  `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedOrderingAdversarialTests.cs`
  per plan §8.4 (permutations of 3+ changes with injected failures; offset-equal instants;
  `MinValue`; sub-microsecond pair; a commit between load and save via `OnSave`). Report the §7
  equal-instant replica race and plan §9 risks 1-2 as findings, not fixes. Return verbatim results
  (compile errors if it needs the new types); not committed alone.

## Phase 2: Green (backend-engineer; may not edit any test written above)

- [x] **T010** [US1] Foundational — `src/StreamDistribution/Domain/Stream/SourceChangedAt.cs` (plan §2,
  sibling of `LastSuccessAt`).
- [x] **T011** [US1] `Stream.cs` — `SourceChangedAt?` property with its doc comment, `RepointTo(sourceUrl,
  changedAt, clock)` returning `bool` (plan §2 steps 1-3); `RepointTo`'s doc comment updated to say
  it is ordered, not only idempotent. `Provision` untouched.
- [x] **T012** [US1] Application — `RepointStreamCommand` third component and its remarks;
  `RepointStreamCommandHandler` deconstructs three fields and adds the plan §4 branch (comment on the
  save-before-gateway ordering kept); `CameraAddressChangedIntegrationEventHandler` maps `ChangedAt`;
  `Log.IgnoredOlderRepoint` (plan §5, four parameters). `Ensure.That`, `var`, no underscores.
- [x] **T013** [US1] Infrastructure — `StreamConfiguration` mapping (plan §6) and
  `dotnet ef migrations add RecordStreamSourceChangedAt --project src/StreamDistribution/Infrastructure --output-dir Persistence/Migrations`.
  Read the generated `Up`/`Down` against plan §6; **paste**
  `git diff --stat origin/develop -- src/StreamDistribution/Infrastructure/Persistence/Migrations`
  into the hand-back (three files).
  **Commit T010-T013 + T004 + T005:** `fix(stream-distribution): an older address change no longer re-points a stream over a newer one`.

## Phase 3: Verify

- [x] **T020** Local: `dotnet build -c Release` clean (no new warnings beyond advisory S/IDE1006);
  `dotnet test` for `StreamDistribution.Domain.Tests`, `StreamDistribution.Application.Tests`,
  `StreamDistribution.Infrastructure.Tests`, `Architecture.Tests` (`PrimitiveBoundaryTests`,
  `HandlerDeconstructionTests`, registration characterisation) — all green, R1-R5, G1-G3, D1-D2,
  T005 included. Counterfactual: `<` → `<=` in `RepointTo` turns G1-G3 red; removing the record of an
  equal-URL newer instant turns R3 red — both quoted. CI (authoritative): R6 and the two existing
  `RepointStreamIntegrationTests` facts green; plan §8.3's must-stay-green list green unmodified;
  coverage gates (ADR-0065) hold.
- [ ] **T021** Phase 6: `/code-review` + `security-reviewer` (attribution: the stale-address window
  is closed for the reordered pair; the ignored line carries origin only; plan §9 risks 1-2
  assessed in writing).

## Dependencies

T001, T002, T003 [P] → commit 1. T004 and T005 (uncommitted) → T010 → T011 → T012 → T013 → commit 2
→ T020 → T021. T011-T013 are sequential: each compiles only on its predecessor.
