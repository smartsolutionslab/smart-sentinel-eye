# Tasks 449: Stored camera URLs in audit rows are redacted in place

[spec](spec.md) · [plan](plan.md) · Issue #2990 (feature-level; on Project #13 — verify by `content.url`)

**Phase 4a colour: RED** (behaviour-changing; P1 and C1 are declared green). **Security-sensitive: yes.**
Engineer: backend-engineer only. **Foundational and blocking:** T001 (scaffold so every commit builds). After it,
T010-T011 (AuditObservability) and T012 (AppHost) own disjoint files and run `[P]`; T013 (runbook) is `[P]` with all.

**Forbidden:** archived blobs, dead letters, the `wolverine_camera_catalog` outgoing table, any EF `Migration` class,
any HTTP endpoint or manual command, `AuditPayloadRedaction.Serialize` semantics, `SchemaVersion.Current`; ADRs, the
constitution, CLAUDE.md; suppressions, thresholds, analyzer configs; any existing assertion.

## Phase 4a — tests first (test-writer; production limited to the T001 scaffold)

- [ ] T001 [US1] Scaffold: `StoredCameraUrlRewrite` (Application/Redaction) returning `None`/empty, `AuditUrlRedactionOptions`,
      `AuditUrlRedactionStep.RunAsync` as a no-op, migrator constructor takes the step (calls it), DI registration. Builds.
- [ ] T002 [US1] P1 idempotence property over the existing corpora. **Expected green; quote.** Red → STOP the issue
      (ADR-0184 Note 2), comment verbatim, `agent:blocked`.
- [ ] T003 [US1] U1 `StoredCameraUrlRewriteTests`. Observe red against the scaffold, quote.
- [ ] T004 [US1] U2 `AuditUrlRedactionStepTests` (interceptor + capturing logger, Docker-free). Observe red, quote.
- [ ] T005 [US1] C1 characterisation (integration). Capture green against the scaffold, quote.
- [ ] T006 [US1] R1-R5 integration tests (new class) **plus the `ci-shards/shard-4.filter` entry**. Compile locally;
      the red is the CI run of this commit, quoted from the downloaded job log.
- [ ] T007 Commit the red set (`test(2990): ...`), each commit building on its own.

## Phase 4b — implementation (backend-engineer; may not edit any test)

- [ ] T010 [P] [US1] `StoredCameraUrlRewrite.PlanFor` + size delta, keys from `AuditPayloadRedaction.UrlFields`. U1 green.
- [ ] T011 [P] [US1] `AuditUrlRedactionStep` (select, dry log, guarded per-row `UPDATE`, recount, applied log), `Log.cs`
      entries, options binding. U2, R1-R5 green; C1 unchanged.
- [ ] T012 [P] [US1] AppHost `AuditUrlRedactionApply` parameter + `migrations` environment variable;
      `OverridableParameters.cs` count comment; any parameter enumeration test it trips.
- [ ] T013 [P] [US1] `docs/runbooks/audit-observability.md` per spec FR-009.
- [ ] T014 Counterfactuals 1-5 (plan), each failure quoted, each reverted.
- [ ] T015 `dotnet build -c Release` clean; `dotnet format --verify-no-changes` on touched files; coverage gates
      (AuditObservability Application ≥ 80, Shared ≥ 90).

## Phase 5 — verify

- [ ] T016 Verification note: red→green per id; R4 result stated explicitly (DML on a compressed chunk: works / parked);
      the MigrationRunner's dry-run line read from a booted stack's log (or CI log) with no URL in it. Latency: N/A.

## Phase 6 — review

- [ ] T017 backend-reviewer.
- [ ] T018 **security-reviewer** (mandatory): no URL in log/exception/trace/EF command log; dry run is the default and
      cannot be applied by omission; only the three values, size and version change; guards prevent double edits;
      `jsonb_set` path/keys are constants, all values parameterised.

## Phase 7

- [ ] T019 PR to `develop` (`--base develop`), quotes the reds, `Closes #2990`.
- [ ] T020 File the removal follow-up (ADR-0184 Note 7), **without** `agent:ready`, Decision 7's two conditions as AC;
      add it to Project #13.
