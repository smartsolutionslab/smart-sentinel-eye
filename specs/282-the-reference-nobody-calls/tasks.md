# Tasks: Spec 282, the reference nobody calls

**Spec:** `spec.md` · **Plan:** `plan.md` · **ADRs:** ADR-0154 and ADR-0125 applied, no new ADR · **Issue:** #1140 · **Lane:** autonomous (ADR-0144)

**Phase 4a colour, declared here (ADR-0144):**

| Task | Colour | Evidence required in the PR |
|---|---|---|
| T010 execution strategy | **CHARACTERISATION** | Green on unmodified `develop`, then green **unmodified** after T030 and T040. |
| T011 bounded connection string | **CHARACTERISATION** | The same. |
| T012 health-check guard | **CHARACTERISATION + COUNTERFACTUAL** | Green on `develop` and after T030/T040, unmodified. **Red** with `DisableHealthChecks = false` in the helper. That output is quoted, and the helper is reverted. |
| T013 MigrationRunner untouched | **CHARACTERISATION** | Green before and after, unmodified. |
| Existing suites (plan §5.3) | **CHARACTERISATION** | Pass counts on `develop` quoted, and the same counts after. |
| T030 US1 wiring | behaviour-**preserving** | All of the rows above stay green, unmodified. An edited assertion is a block. |
| T020 tracing registered | **RED** | Observed failing on the T030 tree, and the failure quoted. |
| T021 metrics registered | **RED** | The same. |
| T022 real span over real Postgres | **RED** | The same. |
| T040 US2 switch-on | behaviour-**changing** | T020 to T022 go green. T010 to T013 stay green, unmodified. |

**Why T012 is not red, although the owner's comment lists the guard under US2.** The behaviour
it guards (no dependency health check) is already true on `develop`, so a new guard arrives
green. Red for it can only be manufactured. It is therefore scheduled **before** T030, where it
is the characterisation net for FR-003, and its bite is proven by counterfactual (spec A-3). If
the orchestrator reads the comment as requiring genuine red for the guard, that cannot be done
honestly. Say so in the PR rather than staging a red.

**Engineers:**

- `test-writer`: T010 to T013 and T020 to T022, plus the shard-filter entry for T022.
- `backend-engineer`: T001, T030, T040.
- `infra-engineer`: **not needed**. Nothing touches AppHost, CI workflows, Docker, realm or Helm.
- Phase 6: `backend-reviewer`. `infra-reviewer` is advisable (telemetry and a package major).
  `security-reviewer` is not needed.

**Board.** #1140 is already on Project #13, and the gate is met. Verify with
`gh project item-list 13 --owner smartsolutionslab --limit 2000 --format json` and match
`content.url` (memory: *Project #13 issues*). No per-task issues.

**Contention (ADR-0109).** `Directory.Packages.props` is a repo-wide contention file, touched only
by T030. Rebase immediately before opening the PR. `ci-shards/shard-N.filter` is shared with
other parked PRs, so rebase conflicts there are expected and trivial.

**Per-commit rule (ADR-0087).** Every commit must:

- build with `dotnet build -c Release`;
- pass `dotnet format --verify-no-changes`;
- pass the unit projects it touches, on its own.

Commits are Conventional Commits with no `Co-Authored-By` (ADR-0086). **T030 is one commit**:
the pin bump, adding the ServiceDefaults reference, removing the four Infrastructure references,
the helper and the nine call sites. A split that drops the Infrastructure references before
ServiceDefaults gains one does not build (FR-011).

**Stack rules.** One Aspire stack per machine. Stop the stack before building. Only T022 and phase
5 need a stack.

`[P]` means the task can run in parallel with the other `[P]` tasks in the same group, because
they own disjoint files.

---

## Phase 0: Docs

- [x] **T000** Spec, plan and tasks for 282, revised for #1140's 2026-09-28 re-scope.
- [ ] **T002** Confirm #1140 is on Project #13 (see Board above). Nothing to create.

## Phase 1: Foundational probe (blocks everything)

- [ ] **T001** *(backend-engineer, probe, not committed)* On a scratch change in the worktree:
  1. Bump the pin to 13.5.4, and add the reference to ServiceDefaults.
  2. Restore, then quote `dotnet list package --include-transitive` for `Npgsql*`,
     `Microsoft.EntityFrameworkCore*` and `Microsoft.Extensions.*`, before and after. **If any
     resolved version differs from its pin: stop (S-2).**
  3. In a throwaway console or test, compose `AddCameraCatalogPersistence()` then
     `EnrichNpgsqlDbContext<CameraCatalogDbContext>(s => { s.DisableHealthChecks = true; s.DisableRetry = true; })`,
     and observe plan §2 facts 1 to 4. **If Enrich throws on a factory registration, or the
     factory's contexts do not carry the enriched options: stop (S-1).** If tracing cannot be
     toggled independently: stop (S-3).
  4. Record Npgsql's `ActivitySource` name and `Meter` name from the restored assembly, for T020
     and T021.
  5. Discard the scratch change. Quote the observations in the PR under "Probe".

## Phase 2: Characterisation, captured on unmodified `develop` (blocks T030)

All four new test tasks own disjoint files. They share one composition harness (plan §5.2),
written first by T010's author and reused by the others.

- [ ] **T010** `tests/Architecture.Tests/Persistence/PostgresComposition.cs` (the shared harness:
  the per-context configuration dictionaries as `[Theory]` data, and a
  `Compose(context) → ServiceProvider` helper) and
  `tests/Architecture.Tests/Persistence/ExecutionStrategyCharacterisationTests.cs`: each of the
  nine contexts' created DbContext uses exactly `NpgsqlExecutionStrategy` (plan §5.3). **Run it on
  `develop` and record it green.**
- [ ] **T011** `[P]` (after T010's harness) `tests/Architecture.Tests/Persistence/BoundedConnectionStringCharacterisationTests.cs`:
  each context's created DbContext connection string has
  `MaxPoolSize == PostgresConnectionBudget.MaxPoolSize`. The configured value names no pool size.
  The expected value is built from the constant. Green on `develop`.
- [ ] **T012** `[P]` (after T010's harness) `tests/Architecture.Tests/Persistence/DependencyHealthCheckGuardTests.cs`:
  - the ADR-0154 clause 2 guard over each context's composed `HealthCheckServiceOptions`, with
    the name vocabulary rule (a) and the assembly rule (b) from plan §5.3;
  - an XML doc stating that the vocabulary is a list, that `AddServiceDefaults` is not composed
    (#2571 Q1), and that `failureStatus` is #2571's concern, not this guard's.

  Green on `develop`. Its counterfactual runs after T030 (see T031).
- [ ] **T013** `[P]` `tests/MigrationRunner.Tests/PersistenceCompositionCharacterisationTests.cs`:
  the nine `AddXPersistence` calls, mirroring `src/MigrationRunner/Program.cs`, register no
  health check and no `TracerProvider` or `MeterProvider`. Green on `develop`.
- [ ] **T014** Record the pass counts on `develop` of `PostgresPoolBoundTests`,
  `PostgresConnectionBudgetTests`, `OutboxBacklogHealthCheckTests`, `DefaultEndpointsTests` and
  the full integration suite, for SC-003. Record the GET `/health` word on all nine contexts too,
  for SC-004's "before".

## Phase 3: US1, bump and wire with every feature off (P1, behaviour-preserving)

Depends on T001 (no stop), T010 to T014.

- [ ] **T030** *(backend-engineer, one commit)*
  - `Directory.Packages.props:37` goes to `13.5.4`, with an updated comment. It stops being the
    9.x straggler.
  - `SmartSentinelEye.ServiceDefaults.csproj` gains `<PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" />`.
  - Remove that reference from the CameraCatalog, Identity, Automation and EventIngestion
    Infrastructure `.csproj` files.
  - New `src/ServiceDefaults/Persistence/PostgresTelemetry.cs` holds the helper (plan §3), with
    **all four switches `true`**: health checks, retry, tracing and metrics disabled.
  - Add the helper call in each of the nine `Add{Context}Infrastructure`, right after
    `Add{Context}Persistence()`.
  - T010 to T013 and the T014 suites pass **unmodified**.
- [ ] **T031** *(test-writer)* **The counterfactual for T012.** Temporarily set
  `DisableHealthChecks = false` in the helper. Run T012 and quote the red, which should fire rule
  (a) and rule (b) on `{TContext}`. Revert. Also flip `DisableRetry = false`, quote T010's red,
  and revert. Nothing from this task is committed except the quoted output in the PR body.

## Phase 4: US2, database spans and metrics (P2, red)

Depends on T030.

**Tests first, observed red on the T030 tree:**

- [ ] **T020** `[P]` `tests/Architecture.Tests/Persistence/PostgresTracingRegistrationTests.cs`:
  each context's composed `TracerProvider` listens to Npgsql's activity source. The name comes
  from T001. **Red.**
- [ ] **T021** `[P]` `tests/Architecture.Tests/Persistence/PostgresMetricsRegistrationTests.cs`:
  each context's `MeterProvider` exports Npgsql's meter. It uses a capturing metric exporter like
  `IngestVolumeRegistrationTests`. **Red.**
- [ ] **T022** `[P]` `tests/Integration.Tests/ServiceDefaults/PostgresSpanIntegrationTests.cs`:
  - compose `AddCameraCatalogInfrastructure` in-process against the fixture's real
    `camera-catalog-db`;
  - use a hand-written capturing `BaseExporter<Activity>` (no new package);
  - run one query through `IDbContextFactory<CameraCatalogDbContext>`;
  - assert that a captured activity has `db.system` / `db.system.name` = `postgresql`;
  - **add the class to a `tests/Integration.Tests/ci-shards/shard-N.filter`** (memory: a missing
    entry fails every time).

  **Red.**

**Then:**

- [ ] **T040** *(backend-engineer)* In the helper only, set `DisableTracing = false` and
  `DisableMetrics = false`, and rewrite the helper's XML doc for the two switches that are now
  on. T020 to T022 go green. T010 to T013 and the T014 suites stay green, **unmodified**.

## Phase 5: Verify (plan §6; every step is required)

- [ ] **T050** Release build with 0 warnings and 0 errors, and all unit projects green, with
  counts.
- [ ] **T051** **The full integration suite, all four shards**, green, with counts per shard.
  The #1133 precedent is the reason this is not optional.
- [ ] **T052** Boot the stack. Run GET `/{context}/health` through the gateway for all nine
  contexts, and compare with T014. Stop the Postgres container and run it again: the answer must
  still be `Healthy` (SC-004). Restart the container.
- [ ] **T053** On the dashboard:
  - a camera-list trace shows a Postgres span under the camera-catalog server span;
  - an Npgsql pool metric is visible for at least one context;
  - how much Wolverine polling noise there is, recorded (SC-005).
- [ ] **T054** Latency (spec §7): run `IngestThroughputMeasurementTests` on the T030 tree and on
  the T040 tree, twice each, and quote the figures. Cite the *event → overlay state* leg in the
  PR.

## Phase 6 and 7

- [ ] **T060** Review by `backend-reviewer`, plus `infra-reviewer` if the orchestrator chooses.
- [ ] **T070** Open the PR with `--base develop`. The body includes:
  - `Closes #1140`;
  - a link to #2571, stating that its Q1 and Q2 remain open;
  - the probe, the characterisation and red outputs, and the counterfactual, all quoted.

  Check #1140's state after the merge (memory: *a PR mention rarely auto-closes the issue*).

## Dependencies

```
T001 ──┐
T010 ──┼─> T011 [P], T012 [P] ─┐
T013 [P] ──────────────────────┼─> T014 ─> T030 ─> T031
                               │           └─> T020 [P], T021 [P], T022 [P] ─> T040 ─> T050..T054 ─> T060 ─> T070
```
