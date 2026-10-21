# Spec 423 — A stream always has a fab: `streams.fab` NOT NULL, and the attribution account retired

**Issue:** #1467 (feature-level, on Project #13; no per-task issues, per CLAUDE.md Phase 3).
**Branch:** `fix/1467-streams-fab-not-null` (cut from `origin/develop` `7909dd20`).
**Lane:** autonomous (ADR-0144), `agent:ready`. **Security-sensitive**: it retires a cross-fab read
principal and changes the WHEP admission branch, so `security-reviewer` and `/security-review` are
mandatory at phase 6 (owner, #1467, 2026-10-21).

**Decision implemented (owner, recorded on #1467, 2026-10-21).** Option (b): ADR-0116's
*"only while unattributed streams exist. After the first successful pass the call never happens again"*
is read as **self-expiring**. This spec builds what that expiry means: the `streams.fab` NOT NULL
migration, removal of `StreamFabAttributionService` and `Stream.AttributeToFab`, and retirement of the
`stream-distribution-attribution` realm client and its AppHost secret. The issue's third precondition
(a misquote of spec 016 FR-010: *"a stream whose camera genuinely no longer exists stays null forever"*)
is **struck**: there is no hard delete of a camera, and `CameraCatalogFabLookup` already sends
`includeRetired=true` (re-verified below). This spec does not re-decide any of that, and writes no ADR.

**ADRs:** ADR-0116 (the decision whose self-expiring clause this executes; its own *Not in scope*
names this follow-up migration), ADR-0161 (WHEP admission refuses a stream outside the caller's fabs;
its null-fab clause becomes unreachable), ADR-0067 (MigrationRunner; a failed migration fails the run
and gates all nine services), ADR-0087 (every commit builds on its own), ADR-0113 (no retry-on-conflict),
ADR-0103 (Aspire fixture, no Testcontainers), ADR-0139/0144 (phase 4a colours), ADR-0143 (one
`RetryEveryMethod()` opt-in disappears with its client), ADR-0159 (fab is a binding data scope).
**Predecessors:** spec 016 (stream fab scoping; FR-008/009/010 retire here), spec 083 / #2076
(`includeRetired=true`), spec 217 / spec 306 (audit's `Unresolved` marker), spec 247 (outbox-commit
exemption for this service), spec 289 (SystemVariables refusal tests borrowed this principal).
**Constitution:** §II (value objects; `Stream.Fab` stops being a nullable reference), §III (removes
StreamDistribution's one cross-context HTTP call), §VIII (safe by default at trust boundaries),
§Testing.

---

## Problem — premise verified on `develop` `7909dd20` (2026-10-21)

| # | Claim | Verdict |
|---|---|---|
| P1 | `StreamFabAttributionService` still exists and runs at startup | **Holds.** `src/StreamDistribution/Infrastructure/Attribution/StreamFabAttributionService.cs`; registered as a hosted service in `StreamDistributionInfrastructureModule.AddStreamHostedServices` (`:218`); its clients and options in `BindStreamFabAttribution` (`:272-308`). The folder holds six files: the service, `ICameraFabLookup`, `CameraCatalogFabLookup`, `CameraCatalogTokenProvider`, `CameraCatalogAuthorizationHandler`, `StreamFabAttributionOptions`. |
| P2 | `Stream.Fab` is nullable | **Holds.** `Stream.cs:44` `public FabIdentifier? Fab { get; private set; }`; `AttributeToFab` at `:132`. EF: `StreamConfiguration.cs:54-60` `.IsRequired(false)`; column added nullable by `20260816081921_FabScopeStreams`. `StreamHealthChangedDomainEvent.Fab` is `FabIdentifier?`; `Log.RefusedWhepFab` takes `FabIdentifier?`; `GetStreamQueryHandler.Map` uses `stream.Fab!.Value`; `AuthorizeWhepCommandHandler.cs:133` has a `Fab is null` branch. |
| P3 | The realm client `stream-distribution-attribution` and its dev-only secret | **Present in:** `src/AppHost/Realms/smart-sentinel-eye-realm.json` (client ≈`:317`, service-account user ≈`:637-639`, groups `/fabs/munich` + `/fabs/dresden`, default scopes `sse-identity, sse-audience, sse-groups, sse.cameras.read`); `src/AppHost/AppHost.cs:47-52` (parameter `StreamDistributionAttributionClientSecret`, default `dev-only-stream-distribution-secret`) and `:362-370` (`WithReference(cameraCatalog)` + env `StreamFabAttribution__ClientSecret`), plus the api-gateway comment ≈`:507-510` naming that `WithReference`; five AppHost composition snapshots; `RealmImportMirrorTests.ProductionDefaults` and `.IntegrationTestCopies` (T13, T14, T16); `AppHostParameterOverrideTests:56`; `AppHostStackStatusTests:127`. **Also borrowed as a test principal** by `VariableReadScopeIntegrationTests` and `ResolveOverlayTextTests` (spec 289) — a fab-holding service account *without* `sse.variables.read`. Docs: ADR-0116, 0153, 0159, 0160, 0161, 0170 and many historical specs mention it; ADRs are not edited by this lane. |
| P4 | `includeRetired=true` is already sent | **Holds.** `CameraCatalogFabLookup.cs:67`. |
| P5 | No hard delete of a camera | **Holds** (re-verified for #2076, 2026-09-06, five ways; nothing since adds a delete). |
| P6 | Measured null population | **Not re-measured: no live database is reachable from this session** (Docker engine is not running). The only measurement on record is #1467's comment of 2026-09-04: `stream-distribution-db` 27 streams, **0** with `fab IS NULL`, all 27 `munich`, 0 orphans. |
| P7 | The null population is closed | **Holds by construction.** `Stream.Provision` takes a non-null `FabIdentifier`; `CameraRegisteredIntegrationEventHandler.cs:29` refuses a blank metadata fab. Only pre-spec-016 rows can be null, and spec 015's `FabScopeCameras` set every pre-015 camera to `munich` — a fab the attribution account holds. So the residue can only shrink, and the previous release can always resolve it while CameraCatalog is reachable. |
| P8 | Who else depends on a null `Stream.Fab` existing | `UnresolvedFabAuditRowIntegrationTests` (spec 217/306, nine facts) **manufactures** its null-fab audit row by `UPDATE streams SET fab = NULL` — impossible once the column is NOT NULL. `WhepAuthIntegrationTests.Authorize_for_a_stream_with_no_fab_attributed_yet_is_refused` and `AuthorizeWhepCommandHandlerTests.Authorize_for_a_stream_with_no_fab_attributed_yet_is_refused` test a state that becomes unrepresentable. `OutboxCommitTests.PermittedDirectCommits` exempts exactly this service. `Shared.Contracts` `EventMetadata.Fab` stays nullable (shared across contexts) — **no contract version bump**. |

### The blocking concern, and why it is answerable inside the decision

A migration cannot read `camera-catalog-db`, so it cannot fix a null row; it can only refuse or not. Two
things make refusal the safe choice rather than a deploy-time trap:

1. **The population is closed and measured empty** (P6, P7). The only environment that has ever existed
   is one persistent dev volume plus CI databases the migrations create themselves (#1467, 2026-09-04:
   no k8s manifests, no deploy job, no production config).
2. **An unguarded `SET NOT NULL` already fails** on a null row — with Postgres's own
   `column "fab" of relation "streams" contains null values`, which names neither a stream nor a remedy.
   The precedent `20260823194632_CaseInsensitiveCameraNames` set the house answer: **refuse first, list
   the offending rows, and give the operator the remedy in the `HINT`**; never auto-repair in a deploy
   step. That is what this migration does.

No superseding ADR is needed: the mechanism (a guarded, transactional migration under the existing
MigrationRunner) is a precedent, not a new decision, and ADR-0116's *Not in scope* names this migration.

---

## User stories

### US1 (P1) — Every stream has a fab, and the database says so

As an **operator of any fab**, I want a stream to be unable to exist without a fab, so that "missing"
and "unattributed" can no longer be confused (spec 016 FR-009) and every stream read is decided by fab
membership alone.

**Acceptance scenarios**

```gherkin
Scenario: Happy path — a database with no unattributed stream migrates
  Given stream-distribution-db is at migration 20261018083817_RecordStreamSourceCameraVersion
  And every row in streams has a fab
  When the MigrationRunner applies pending migrations
  Then streams.fab is NOT NULL
  And the EF model maps Stream.Fab as required, matching the committed snapshot

Scenario: Conflict — a stream still has no fab
  Given stream-distribution-db is at 20261018083817_RecordStreamSourceCameraVersion
  And stream S for camera C has fab NULL
  When the MigrationRunner applies pending migrations
  Then the migration refuses with SQLSTATE P0001
  And the message names stream S and camera C and the count of unattributed streams
  And the HINT names the remedy (run the previous release until attribution reports 0 unresolved,
      or set each stream's fab to its camera's fab from camera-catalog-db)
  And streams.fab is still nullable and the migration is not recorded as applied
  When S's fab is set and the runner is re-run
  Then the migration applies and streams.fab is NOT NULL

Scenario: Bad request — nothing can write a null fab afterwards
  Given the migration has applied
  When anything attempts UPDATE streams SET fab = NULL
  Then Postgres refuses with a not-null violation (SQLSTATE 23502)

Scenario: Auth — read and WHEP admission are decided by fab membership only
  Given a stream in munich
  Then a munich operator reads it and is admitted by /streams/authorize
  And a dresden-only operator gets 404 on GET and 403 WHEP_FAB_NOT_AUTHORIZED on authorize
  (unchanged; the null-fab branch these used to carry no longer exists)
```

### US2 (P1) — The attribution credential no longer exists

As the **security owner**, I want the `stream-distribution-attribution` service account — the one
principal spec 016 created that can read every listed fab's camera catalogue — gone from the realm, the
AppHost and StreamDistribution, because retiring it is the point of this change (#1467).

**Acceptance scenarios**

```gherkin
Scenario: Happy path — StreamDistribution starts without it
  Given the stack boots from the edited realm import
  Then stream-distribution reaches Running with no StreamFabAttribution configuration
  And it registers no camera-catalog HTTP client and no attribution hosted service
  And the AppHost passes it no reference to camera-catalog and no attribution secret

Scenario: Auth — the credential is refused
  Given a freshly imported realm
  When a client_credentials grant is requested for client_id stream-distribution-attribution
       with the former dev-only secret
  Then Keycloak answers 401 invalid_client

Scenario: Conflict — tests that borrowed the principal still prove their refusal
  Given VariableReadScopeIntegrationTests and ResolveOverlayTextTests
  When they mint their unscoped token from a planted password-grant client (operator, /fabs/munich,
       no sse.variables.read) instead of the retired account
  Then every asserted status (403 refusals, 200 controls) is unchanged

Scenario: Bad request — no residual configuration
  Then no file under src/ or tests/ names stream-distribution-attribution,
       StreamDistributionAttributionClientSecret, StreamFabAttribution, ICameraFabLookup,
       CameraCatalogFabLookup, CameraCatalogTokenProvider or CameraCatalogAuthorizationHandler
       as a live identifier — except the one test that asserts the credential is refused
       (historical prose in comments may say "removed by spec 423")
```

### US3 (P2) — Audit's `Unresolved` marker keeps its end-to-end proof

The audit context still classifies a fab-owned event with a null fab as `Unresolved` (spec 306); that is
a fail-safe for the shared, nullable `EventMetadata.Fab` and is **not** removed. After US1 no live
producer emits such an event, so spec 217/306's nine integration facts must keep proving it with a
different arrangement and **unmodified assertions** (characterisation).

```gherkin
Scenario: The null-fab health row is still recorded, marked and hidden
  Given a munich camera reaches Healthy through the real stack (the resolved control row)
  And a StreamHealthChangedV1 for that camera with Metadata.Fab = null is delivered to the
      audit context's real ingest handler against the stack's audit database
  Then all nine facts of UnresolvedFabAuditRowIntegrationTests pass with their assertions unchanged
```

---

## Requirements

- **FR-001** `Stream.Fab` is a non-nullable `FabIdentifier`; the aggregate has no member that assigns a
  fab after `Provision` (`AttributeToFab` removed).
- **FR-002** `streams.fab` is `NOT NULL`, by one new migration whose `Up` refuses (`RAISE EXCEPTION`,
  listing every unattributed stream and camera, with a remedy `HINT`) **before** `ALTER COLUMN … SET NOT
  NULL`, inside the migration's transaction. `Down` makes the column nullable again.
- **FR-003** `StreamHealthChangedDomainEvent.Fab` is non-nullable; the integration event still carries it
  as a string. `Shared.Contracts` is untouched.
- **FR-004** `AuthorizeWhepCommandHandler` admits iff the caller's fabs contain the stream's fab; the
  `Fab is null` branch is removed. The 403 `WHEP_FAB_NOT_AUTHORIZED` answer is otherwise unchanged.
- **FR-005** The six `Infrastructure/Attribution` types, their DI, logger messages and options are removed;
  StreamDistribution makes no HTTP call to CameraCatalog.
- **FR-006** The realm import has no `stream-distribution-attribution` client or service-account user;
  the AppHost has no `StreamDistributionAttributionClientSecret` parameter, no
  `StreamFabAttribution__ClientSecret` env and no `WithReference(cameraCatalog)` on stream-distribution.
- **FR-007** Tests that borrowed the principal switch to a planted client; tests of removed behaviour are
  removed **with the behaviour**, each named in the PR body (plan §6).
- **FR-008** `OutboxCommitTests.PermittedDirectCommits` becomes empty.

## Out of scope

- Editing ADR-0116 (or 0153/0159/0160/0161/0170) to record the expiry — the lane may not amend ADRs.
  Recommended human follow-up: a status note on ADR-0116, *"Decision items 1-5 expired by their own
  clause; executed by spec 423 / #1467."*
- `EventMetadata.Fab` and audit's `Unresolved` handling (stay; shared contract).
- Deleting the client from a **persistent** Keycloak volume by automation. The realm import is
  ignore-if-exists; an existing volume keeps the client until the volume is deleted (CLAUDE.md memory,
  owner note on #1467) or an admin deletes it. Recorded in the quickstart section of `plan.md`.

## Independent end-to-end test procedure

1. Pre-deploy, read-only: `SELECT count(*) FROM streams WHERE fab IS NULL;` on `stream-distribution-db`
   → expect `0`. If not 0, do not deploy; run the current release until its attribution pass logs
   `0 could not be resolved`.
2. Stop the stack; delete the Keycloak data volume (realm edits need it); boot the AppHost.
3. `migrations` completes; `stream-distribution` reaches Running.
4. `\d streams` → `fab | character varying(32) | not null`.
5. Mint `client_credentials` for `stream-distribution-attribution` / `dev-only-stream-distribution-secret`
   → `401 invalid_client`.
6. Register a munich camera; `GET /streams/{camera}` as a munich operator → 200 with `fab: "munich"`;
   as a dresden-only operator → 404; `/streams/authorize` for the dresden operator → 403.
7. CI: Integration shards green, including the scratch-database migration test (refusal and success).

## Latency budget impact

**N/A.** No leg of constitution §IV changes. The health watcher's publish path loses a `?.` on the fab;
WHEP admission loses one null check (session-open, not per-frame). The startup attribution pass made one
HTTP call only while null rows existed, and none in steady state.

## Assumptions (marked)

- **A1 (unverified here):** every live environment has 0 null fabs. Measured once (27/0); no DB reachable
  now. The migration's guard makes a wrong assumption loud, not silent.
- **A2:** `AuditingMessageHandler`/`IntegrationEventAuditHandler` can be composed in-process in
  `Integration.Tests` against the stack's audit database (the public handler plus the infrastructure
  repository). If it needs internals the test project cannot reach, the phase **blocks**; the nine facts
  are not deleted.
