# Plan 423 — A stream always has a fab

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #1467

## 1. Scope and boundaries

| Area | Bounded context / layer | Change |
|---|---|---|
| Aggregate | StreamDistribution / Domain | `Stream.Fab` non-nullable; `AttributeToFab` removed; `StreamHealthChangedDomainEvent.Fab` non-nullable |
| Use cases | StreamDistribution / Application | `AuthorizeWhepCommandHandler` null branch removed; `GetStreamQueryHandler.Map` drops `!`; `StreamHealthChangedDomainEventHandler` `Fab.Value`; `Log.RefusedWhepFab(FabIdentifier)` |
| Persistence | StreamDistribution / Infrastructure | `StreamConfiguration` `IsRequired()`; one guarded migration + Designer + snapshot |
| Attribution | StreamDistribution / Infrastructure | delete `Attribution/` (6 files), `BindStreamFabAttribution`, the hosted-service registration, the attribution `[LoggerMessage]`s |
| Composition | AppHost | parameter, env, `WithReference(cameraCatalog)` on stream-distribution, related comments |
| Identity | Realm import | delete client `stream-distribution-attribution` and user `service-account-stream-distribution-attribution` |
| Tests | Domain/Application/Infrastructure/Architecture/Integration | plan §6 |

**Boundary rules.** No new cross-context reference; one is *removed* (the runtime HTTP dependency on
CameraCatalog, and the AppHost `WithReference`). `Shared.Contracts` is untouched: `EventMetadata.Fab`
stays `string?` because it is shared by every context (audit's `FabNeutralEvents` still needs it).
NetArchTest rules are unaffected.

## 2. Domain

- `Stream.Fab : FabIdentifier { get; private set; } = null!;` — the `null!` initialiser is the EF
  materialisation idiom already used for `Provisioning` in the same class; `Provision` always assigns it.
  Update the property doc: the "nullable because pre-016 streams…" paragraph is replaced by the
  invariant: *a stream's fab is its camera's, fixed at provisioning; there is no setter and no method
  that writes it.*
- **Invariant now structural:** no member other than `Provision` writes `Fab`. Pinned by a reflection
  test (§5, R2) rather than by the old "cannot be re-attributed" throw test, which goes with the method.
- `StreamHealthChangedDomainEvent(…, FabIdentifier Fab, …)`. Remove the "Nullable, like Stream.Fab"
  paragraph.
- Primitive rule (§II, `PrimitiveBoundaryTests`): unaffected — still a value object.

## 3. Messaging

Domain → integration unchanged in shape: `StreamHealthChangedDomainEvent` → `StreamHealthChangedV1`
with `Metadata.Fab = domainEvent.Fab.Value` (was `?.Value`). No new event, no version bump. After this
change StreamDistribution never publishes a null fab; audit's `Unresolved` path keeps its handling for
any producer of the shared nullable field.

## 4. Persistence — the guarded migration

Name: `<timestamp>_StreamFabNotNull`, generated with `dotnet ef migrations add` (so the Designer and
snapshot are EF's, not hand-written), then the `Up` body is edited to put the guard first:

```csharp
migrationBuilder.Sql("""
    DO $$
    DECLARE tally integer;
    DECLARE unattributed text;
    BEGIN
        SELECT count(*), string_agg(format('stream %s (camera %s)', stream_id, camera_id), '; ' ORDER BY camera_id)
        INTO tally, unattributed
        FROM streams
        WHERE fab IS NULL;

        IF tally > 0 THEN
            RAISE EXCEPTION
                'streams.fab cannot be made NOT NULL (#1467): % stream(s) still have no fab: %',
                tally, unattributed
                USING HINT =
                    'Run the previous release until its startup attribution pass logs "0 could not be resolved", '
                    || 'or set each stream''s fab to its camera''s fab from camera-catalog-db '
                    || '(SELECT camera_id, fab FROM cameras), then re-run the migration.';
        END IF;
    END $$;
    """);

migrationBuilder.AlterColumn<string>(
    name: "fab", table: "streams", type: "character varying(32)", maxLength: 32,
    nullable: false, oldClrType: typeof(string), oldType: "character varying(32)",
    oldMaxLength: 32, oldNullable: true);
```

`Down`: the scaffolded `AlterColumn` back to `nullable: true` (safe; no data lost). The class doc says
why there is no backfill (cameras live in another database), why it refuses rather than repairs (the
`CaseInsensitiveCameraNames` precedent: a deploy step must not silently decide what an operator sees),
and why it does not delete (ADR-0116: these rows carry live video).

**Why this is safe on a 24/7 system.**

1. Npgsql runs each EF migration in a transaction (Postgres DDL is transactional). A `RAISE` aborts it:
   the column stays nullable and `__EFMigrationsHistory` is not written, so the next runner start retries
   from the same state. Proven by test R4, which reads `is_nullable` after the refusal.
2. A refusal fails `MigrationRunner` (exit 1) and therefore gates **all nine** services (ADR-0067;
   `MigrationRun.ExecuteAsync`), not only stream-distribution. That blast radius is the reason for the
   **pre-deploy read-only check** in the quickstart (§8) and is stated in the PR body. It is the same
   blast radius an unguarded `SET NOT NULL` already has, with a worse message.
3. The population is closed and can only shrink (spec P7); the remedy is always available because the
   previous release still carries the attribution pass, `includeRetired=true`, and an account that holds
   `munich` — the only fab a pre-016 camera can be in.

## 5. Phase 4a colour — **red** (behaviour-changing), with a characterisation pre-step

### 5.1 Characterisation, observed green **before** any production change (commit A)

These tests prove behaviour that must survive; they are re-arranged, never re-asserted:

- **C1** `tests/Integration.Tests/SystemVariables/VariableReadScopeIntegrationTests.cs` and
  **C2** `tests/Integration.Tests/SystemVariables/ResolveOverlayTextTests.cs`: replace the borrowed
  `stream-distribution-attribution` `client_credentials` token with a token minted by
  `RealmProbe.PlantPasswordGrantClientAsync` (precedent: `ConsoleScopeGrantIntegrationTests`, spec 286)
  for user `operator` (`/fabs/munich`) with the retired account's exact default scopes
  `sse-identity, sse-audience, sse-groups, sse.cameras.read`; delete the planted client in `finally`.
  Every asserted status and claim is unchanged. Remove rows T13/T14 from
  `RealmImportMirrorTests.IntegrationTestCopies` (their consts disappear — mechanical, compile-forced).
- **C3** `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs`: keep the
  real Healthy transition (the `munich` control row). Replace `BlankTheFabAsync` + the forced Degraded
  transition with: build a `StreamHealthChangedV1` for the same camera (`FromState Healthy`, `ToState
  Degraded`, `Metadata.Fab = null`) and deliver it through `IntegrationEventAuditHandler.Handle(...)`
  composed in-process over `aspire.CreateAuditObservabilityDbContextAsync()` and the infrastructure
  repository. The nine facts' assertions are unchanged. Spec A2: if the handler cannot be composed
  without reaching internals, **block** — do not delete facts.

C1-C3 are CI-only (Aspire). Their green is captured from the CI run of commit A, before commit B lands.

### 5.2 Red, observed failing on `develop` + commit A (commit B)

| ID | Test (file) | Why it is red today | Runs |
|---|---|---|---|
| R1 | `Stream_fab_is_declared_non_nullable` — `NullabilityInfoContext` on `Stream.Fab` reads `NotNull` (`tests/StreamDistribution.Domain.Tests/Stream/StreamTests.cs`) | declared `FabIdentifier?` | local |
| R2 | `Nothing_but_provisioning_assigns_a_fab` — reflection: no public/internal instance method on `Stream` takes a `FabIdentifier` parameter (`StreamTests.cs`) | `AttributeToFab(FabIdentifier)` exists | local |
| R3 | `The_stream_fab_column_is_required` + `The_committed_model_snapshot_still_describes_the_model` (EF design-time model vs snapshot; precedent `CameraCatalogModelCharacterisationTests`) — new `tests/StreamDistribution.Infrastructure.Tests/Persistence/StreamFabModelTests.cs` | `IsNullable == true`; after a config-only change the snapshot check catches a missing migration | local |
| R4 | `StreamFabNotNullMigrationIntegrationTests` (scratch database; precedent `DeadLetterHoldMigrationIntegrationTests`): migrate to `20261018083817_RecordStreamSourceCameraVersion`; (a) insert a null-fab stream, `MigrateAsync()` throws `PostgresException` `P0001` whose message contains the stream and camera ids, and `is_nullable` is still `YES`; set the fab, re-run, `is_nullable = NO`; (b) an empty `streams` migrates cleanly; (c) after migration `UPDATE … SET fab = NULL` fails `23502` | no migration exists: no exception, column nullable | CI |
| R5 | `The_retired_attribution_client_is_refused` — `client_credentials` for `stream-distribution-attribution` → 401 `invalid_client` (`tests/Integration.Tests/Identity/RetiredAttributionClientIntegrationTests.cs`) | Keycloak issues a token (200) | CI |
| R6 | `StreamDistribution_registers_no_camera_catalog_client_and_no_attribution_service` (`tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs`, new fact; uses `PostgresComposition.Compose`) | both registered | local |

R4's predecessor name must be re-checked at implementation: if another migration lands on `develop`
first, the target becomes the newest one before `StreamFabNotNull`.

New Integration classes (R4, R5) need **shard-filter entries** (`tests/Integration.Tests/ci-shards/`);
a missing entry fails deterministically.

If Docker is unavailable locally, R4/R5 red is quoted from the CI run of commit B (download the job log
before any re-run). R1, R2, R3, R6 are the local red evidence.

### 5.3 Edits that are part of the behaviour change, not of a characterisation suite

These move **because** behaviour is deliberately removed; each is listed in the PR body so a reviewer
can check none is a quiet gate-weakening (ADR-0144 forbids "a deleted test" *to reach green*; these
test states this change makes unrepresentable, at the owner's decision):

| Test | Disposition | Reason |
|---|---|---|
| `StreamDistribution.Infrastructure.Tests/Attribution/StreamFabAttributionTests.cs`, `StreamFabAttributionFailureTests.cs` | delete | subject deleted |
| `Integration.Tests/StreamDistribution/StreamFabAttributionIntegrationTests.cs` (+ `shard-1.filter` entry, `RealmImportMirrorTests` T16 row) | delete | subject deleted |
| `StreamTests.A_stream_that_already_has_a_fab_cannot_be_reattributed` | delete | method deleted; R2 replaces the guarantee structurally |
| `AuthorizeWhepCommandHandlerTests.Authorize_for_a_stream_with_no_fab_attributed_yet_is_refused` + `Unattributed()` helper | delete | state unrepresentable (FR-004) |
| `WhepAuthIntegrationTests.Authorize_for_a_stream_with_no_fab_attributed_yet_is_refused` | delete | the arranging `UPDATE` now violates NOT NULL (R4c proves that refusal instead) |
| `StreamDistributionRegistrationCharacterisationTests`: five attribution registration lines; `The_camera_catalog_token_client_opts_back_in_to_retrying_every_method`; `The_camera_fab_lookup_client_does_not_opt_in_to_retrying_every_method` | edit / delete | the registrations are removed; R6 asserts their absence |
| `OutboxCommitTests.PermittedDirectCommits` | `[]` | the only exempt type is deleted; the guard tightens |
| `RealmImportMirrorTests.ProductionDefaults` row, `AppHostParameterOverrideTests:56`, `AppHostStackStatusTests:127` | delete row/line | parameter deleted; `ShouldNotContain` of a name that no longer exists cannot fail |
| AppHost composition snapshots ×5 | regenerate | composition changed (parameter, env, reference) |

## 6. Commit sequence (each builds on its own — ADR-0087)

| Commit | Role | Content | Builds because |
|---|---|---|---|
| A `test(1467): …characterisation` | test-writer | C1-C3; mirror T13/T14 rows | tests only, no production change |
| B `test(1467): …(red)` | test-writer | R1-R6 + shard entries | all reds are reflection/runtime, none references a missing type |
| C `fix(1467): a stream always has a fab` | backend-engineer | §2-§4, deletions of `Attribution/`, DI, §5.3 rows except AppHost/realm ones, comment references (§7) | the attribution tests and the T16 mirror row go in the same commit as the types they name |
| D `fix(1467): retire the stream-distribution-attribution credential` | infra-engineer | realm JSON, AppHost, snapshots, ProductionDefaults/override/stack-status rows | AppHost/realm only; C already stopped reading the env |

C before D: after C the AppHost still sets an env nobody reads (harmless); D before C would leave the
attribution pass failing closed on a refused token (also harmless, but a needless red-herring log line).

## 7. Comment references to removed types

Reword, do not delete history, so no comment names a type that no longer exists as if it did:
`src/Identity/Application/KeycloakAdmin/KioskPrivilegeSweep.cs:94`,
`src/Identity/Infrastructure/KeycloakAdmin/KioskPrivilegeSweepHostedService.cs:27` (a `<see cref>`),
`src/ServiceDefaults/Revocation/{HttpRevokedClientSource,RevocationListAuthorizationHandler,RevocationListTokenProvider}.cs`,
`src/SystemVariables/Infrastructure/Resolution/OverlayDesignerTokenProvider.cs:22`,
`src/SystemVariables/Infrastructure/SystemVariablesInfrastructureModule.cs:130`, and the test-side
doc comments found by
`grep -rn "StreamFabAttribution\|AttributeToFab\|CameraCatalogTokenProvider\|CameraCatalogAuthorizationHandler\|ICameraFabLookup" src tests`.
Done when that grep returns only R2/R5/R6 and history phrased as "removed by spec 423".

## 8. Quickstart / operator note (goes in the PR body)

1. **Before deploying**, read-only on `stream-distribution-db`:
   `SELECT count(*) FROM streams WHERE fab IS NULL;` → must be `0`. If not, keep the current release
   running (its attribution pass resolves them while CameraCatalog is reachable) and re-check.
2. Deploy. The migration re-checks and refuses with the stream list if step 1 was skipped.
3. **Retire the credential where Keycloak has a persistent volume:** the realm import is
   ignore-if-exists, so delete the volume (dev) or delete client `stream-distribution-attribution` in the
   admin console. Until then the client — with a secret published in this repo for dev — still works.

## 9. Security review focus

- The principal is gone from the realm import, and no code path still mints it (R5, grep §7).
- WHEP admission: removing `Fab is null ||` cannot widen access, because `Fab` cannot be null (R1, R4c).
- The planted test client (C1/C2) is created and deleted per test and never committed to the realm.
- Persistent-volume residue (§8.3) is a documented operator step, not automated.

## 10. Constitution check

§II ✔ (VO kept, nullable removed) · §III ✔ (a cross-context runtime dependency removed) · §IV N/A ·
§VI ✔ (AppHost remains the composition root; one resource reference removed) · §VIII ✔ (credential
retired) · §Testing ✔ (characterisation then red, §5). No ADR needed or written.
