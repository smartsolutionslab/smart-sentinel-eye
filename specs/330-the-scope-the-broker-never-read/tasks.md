# Tasks 330 — The scope the broker never read

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2286 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing). A PUBLISH by a JWT identity without `sse.events.publish`
succeeds today and is refused after the change. The observed red is T-A AS-1 and T-B (plan §5).
AS-2 and AS-3 are controls, green before and after and unmodified. The Go unit tests (T-C) prove
their teeth by counterfactual, as plan §5 declares.
**Engineer**: `infra-engineer` (plugin, glue, Dockerfile, `acl.txt`, realm). `test-writer` (T-A,
T-B, T-C).
**Reviewers**: `infra-reviewer` + `security-reviewer`.
**Tracking**: feature-level issue #2286, already on Project #13 (In Progress). No per-task issues.
**New ADR**: none (plan §1; a contestable reading of ADR-0100's "zero per-message overhead" is
flagged there).

Format: `[ID] [P?] [Story] description`. Live tasks need a full Aspire boot, one stack per
machine, on a **fresh `keycloak-data` volume** whenever the realm changed. If a **persistent
`mosquitto` container** exists, remove it with `docker rm` (keep its volume) so the rebuilt image
is used. Confirm by the plugin's log lines, not by the container being up.

## Foundational

- [ ] **T001 [US1]** (infra-engineer) Append the probe block to `src/AppHost/mosquitto/acl.txt`
  exactly as plan §4. Change nothing else. Commit C1. This blocks T002: AS-1/AS-2 mean nothing
  without the row.

## Phase 4a — red (test-writer; return verbatim output)

- [ ] **T002 [P] [US1]** Create `tests/Integration.Tests/Identity/MqttPublishScopeIntegrationTests.cs`
  per plan §5 T-A: three facts (AS-1, AS-2, AS-3), MQTT 5, a fixed probe id with
  delete-before-create, premise assertions before the MQTT calls, and CONNACK distinguished from
  no-CONNACK. Add its `FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.MqttPublishScopeIntegrationTests.`
  entry to the shortest `tests/Integration.Tests/ci-shards/shard-N.filter`.
- [ ] **T003 [P] [US1]** Create `tests/Architecture.Tests/MqttPublishGrantTests.cs` per plan §5
  T-B (AS-4), with the population gate (≥ 1 qualifying user) and slash-normalised paths. Use
  `Scope.Sse.Events.Publish`, not a literal.
- [ ] **T004 [US1]** On unchanged plugin and realm, after a fresh boot, run
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~MqttPublishGrantTests"` and
  `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~MqttPublishScopeIntegrationTests"`.
  **Required:** T-B red, naming `scenario-simulator`. AS-1 red with PUBACK `Success` where
  `NotAuthorized` was expected, and its CONNACK `Success` assertion passing. AS-2 green. AS-3
  green. No compile error, no boot failure. Anything else: stop and report. Commit C2.

Depends: T001 → T002 ∥ T003 (disjoint files) → T004.

## Phase 4b — implement (infra-engineer; the 4a files are read-only)

- [ ] **T005 [US1]** `src/AppHost/Realms/smart-sentinel-eye-realm.json`: append
  `"sse.events.publish"` to `scenario-simulator.defaultClientScopes`. Keep the BOM and encoding.
  Touch nothing else. Re-run T003's fact, which must be green. Commit C3.
- [ ] **T006 [US1]** Create `src/AppHost/mosquitto/plugin/authz/authz.go` to plan §2.1's
  contract. Stdlib only. `Decide` has no allow outcome.
- [ ] **T007 [US1]** `src/AppHost/mosquitto/plugin/jwt_auth.go` per plan §2.2: record the
  verdict after the last existing check, add the connect-time log line, add the `sseOnAclCheck`
  export (fail-closed `recover`, `nil` username → defer, never `MOSQ_ERR_SUCCESS`), have
  `goPluginInit` return `sse_register`'s result, and extend the header comment. Leave the `iss`
  check as it is.
- [ ] **T008 [US1]** `src/AppHost/mosquitto/plugin/mosquitto_glue.c`: `sse_register` registers
  `MOSQ_EVT_ACL_CHECK` → `sseOnAclCheck` after basic auth and returns the first failure
  (plan §2.3). No disconnect callback.
- [ ] **T009 [US1]** `src/AppHost/mosquitto/Dockerfile` stage 2: add
  `RUN CGO_ENABLED=0 go test ./authz/...` after `COPY plugin/ ./` and before `go build`, with a
  short comment saying why `CGO_ENABLED=0`. No new `FROM`. Verify with
  `docker build --target plugin_builder src/AppHost/mosquitto` (it should report "no test files"
  until T012), then a full image build.
- [ ] **T010 [US1]** Comment sweep (plan §7): the `mosquitto.conf` header,
  `tests/Architecture.Tests/ScopeGrantTests.cs:66` (comment only, no assertion change) and the
  `Scope.cs` `Publish` doc. Commit T006–T010 together as C4.
- [ ] **T011 [US1]** Remove the persistent `mosquitto` container if one exists and the
  `keycloak-data` volume, then boot. Confirm that the mosquitto log carries the new line for
  `event-ingestion`. Re-run T004's commands: all green, 4a files unmodified. Then run the
  **full** `tests/Integration.Tests` (all four shards) and `tests/Architecture.Tests`, plus the
  Release build (`TreatWarningsAsErrors`). A red EventIngestion class most likely means T005 did
  not reach the imported realm: check the volume before suspecting the plugin.

Depends: T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011. T006–T009 are one coherent edit
of one image and do not fan out: `jwt_auth.go` imports `authz`, and the glue names the new export.

## Phase 4b — unit pins (test-writer, after C4)

- [ ] **T012 [US1]** Create `src/AppHost/mosquitto/plugin/authz/authz_test.go` per plan §5 T-C
  (every AS-5 and AS-6 row, both overwrite directions). Run with
  `docker build --target plugin_builder src/AppHost/mosquitto` and quote the `go test` output.
  Prove each subtest group by the three counterfactuals in plan §5: mutate, rebuild, quote the
  red, revert. Commit C5.

Depends: T011 → T012. (T012 does not need the stack. It is `[P]` with T011's long suite run if
the orchestrator has a second worktree, because the files are disjoint.)

## Phase 5 — verify

- [ ] **T013 [US1]** Run spec §Independent e2e procedure steps 1–5. Quote the PUBACK codes, the
  `Denied PUBLISH` log line for the probe, and the decoded `scenario-simulator` scope. Run
  `IngestThroughputMeasurementTests` and `NFR002_MqttConnectAuthTests` **twice each** on develop
  and on the branch, and record all four figures each (plan §6). Record A1–A3 as observed.

## Phase 6–7

- [ ] **T014** `/code-review` + `security-review`. Have the security reviewer check specifically
  that no path returns `MOSQ_ERR_SUCCESS` from the ACL callback, the Will reasoning (plan §3), and
  the fail-closed `recover`. Open the PR to `develop` with `Closes #2286`, quoting T004's red,
  T011's green and T012's counterfactuals. File the issuer follow-up from plan §7, add it to
  Project #13 and cross-reference it in the PR body.
