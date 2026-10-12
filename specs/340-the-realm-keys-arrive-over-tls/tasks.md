# Tasks 340: The realm keys arrive over TLS

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2238 (feature-level; the
gate is that #2238 is on Project #13. No per-task issues; see CLAUDE.md, Phase 3).

**Phase 4a colour: RED** (behaviour-changing). The Development twins G1 to G3 are declared in spec
§6 as expected-green-before. A green twin is **not** a phase-4 failure. A green R-test **is**.

**Engineer**: backend-engineer. Tests come from test-writer first.

Format: `[ID] [P?] [Story] description`. `[P]` means the task owns files disjoint from every other
`[P]` task in its group (ADR-0109).

## Phase A: Foundational (blocks everything below)

- [x] **T001** [US1/US2] Re-check the spec number: `git fetch && git log --all --format= --name-only -- 'specs/340*'`
  must list only this spec's files. Confirm that #2238 is on Project #13 (`gh project item-list 13
  --owner smartsolutionslab --limit 2000`, match on `content.url`).
  *Done 2026-10-12. 340 is free on every branch and sibling worktree. #2238 is on the board with
  status In Progress.*

The policy method (FR-001) is the only shared dependency. It is small enough to land inside T004's
commit. It is **not** a separate foundational task, because a public method with no caller would
be a commit that builds but does nothing. T004 and T006 both depend on it. Order them: T004
first, then T006.

## Phase B: Red tests (test-writer; run, quote output verbatim)

- [ ] **T002** [P] [US1+US3] `tests/ServiceDefaults.Tests/MetadataTransportTests.cs` (new). R1 to R4
  and twin G1 (spec §6). Use the environment-explicit empty builder, with a self-check that the
  built environment equals the requested one (pattern: `DefaultEndpointsTests`). Set
  `RevocationList:ClientId` and `RevocationList:ClientSecret` in R3 and R4. Exercise startup
  through `IStartupValidator.Validate()` (plan §4). Assert R3 on "throws, and the message contains
  HTTPS". The exception type is whatever 10.0.12 produces (plan §3.2). R1 to R4 run as a theory
  over `Production`, `Staging` and one custom name. G1 uses `Development`.
  Since `RequiresHttpsMetadata` does not exist yet, the file references only the existing
  `AddBearerAuthentication` and reads the effective `JwtBearerOptions`. So it compiles on
  `064846d3`, and R1 to R4 fail on assertions, not on compilation.
- [ ] **T003** [P] [US2+US3] `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepMetadataTransportTests.cs` (new).
  R5 to R7 and twins G2 and G3, over the same environment set as T002 (the uniformity proof). R5
  and G2 use `http://127.0.0.1:1/realms/smart-sentinel-eye` and assert on IDX20108 being present
  or absent in the inner chain (plan §4; verify the wrapping against 8.19.2).
  **Compile note:** R5 and R6 need seams that do not exist yet: `MetadataSourceFor(options,
  environment)` and an `internal` `BindWhepAuthOptions`. Red here therefore starts as a **compile
  failure**, which is quoted. Give the minimal signature change (parameter added, method made
  `internal`, bodies unchanged) to backend-engineer as the first step of T006, and **re-run to
  capture the assertion-level red** (R5 fails on a socket error instead of IDX20108; R6 and R7
  do not throw). Quote both outputs. The assertion-level red is the one that proves behaviour.
  Adapt the one existing call in `WhepMetadataSourceTests.cs:41` to the new signature, passing a
  Development environment. The address it asserts is unchanged, so this is a call-site
  adaptation, not an assertion edit.

Hand-off: test-writer returns the verbatim red output for T002 and T003 (both stages for T003)
and confirms G1 to G3 green on `064846d3` (G3 after the signature-only step).

## Phase C: Implementation (backend-engineer; may not edit T002/T003 assertions)

- [ ] **T004** [US1] `src/ServiceDefaults/AuthenticationDefaults.cs`: add
  `public static bool RequiresHttpsMetadata(IHostEnvironment)` (FR-001). Set
  `RequireHttpsMetadata` from it (FR-002). Add private `AddMetadataTransportCheck` with `Validate`
  and `ValidateOnStart` on the named `JwtBearerOptions` (FR-003, FR-006). Rewrite the comments at
  `:54-66` and `:195-206` (FR-008). T002 goes green and G1 stays green.
  *Depends on T002.*
- [ ] **T005** [US1] Run the full `ServiceDefaults.Tests` and `Architecture.Tests`. If a
  composition trips the strict path, apply plan §5's remedy (an `https://` placeholder, with the
  reason recorded for the PR body). Never use `Development` and never narrow the validator.
  *Depends on T004.*
- [ ] **T006** [US2] `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`: the DI
  constructor takes `IHostEnvironment`, and `MetadataSourceFor(options, environment)` sets
  `RequireHttps` from the policy (FR-004). Rewrite the comment at `:79-86` (FR-008).
  `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs`:
  `BindWhepAuthOptions` becomes `internal`, using `AddOptions`, `Configure`, `Validate` (absolute
  https outside Development) and `ValidateOnStart`, in the same shape as `BindMediaMtxApiOptions`
  (FR-005, FR-006). Rewrite the doc comment on `WhepMetadataSourceTests` (FR-008). T003 goes
  green, and G2 and G3 stay green.
  *Depends on T003 and T004 (it reads `AuthenticationDefaults.RequiresHttpsMetadata`).*
- [ ] **T007** [US2+US3] Run the full `StreamDistribution.Infrastructure.Tests`, then the
  Docker-backed `Integration.Tests` (one Aspire stack on the machine; stop it before building).
  `WhepAuthIntegrationTests` and `WhepAuthorizeRateLimitTests` (the latter proves the fixture runs
  in Development) must be green. *Depends on T005 and T006.*
- [ ] **T008** Run `dotnet format --verify-no-changes` and a Release build with analyzers. Each
  commit must build on its own, with the red/green pairing per ADR-0139 and the CLAUDE.md bisect
  note. *Depends on T007.*

T004 and T006 are **not `[P]`**. T006 consumes T004's public method, so they are sequential, even
though the files are disjoint. T002 and T003 **are** `[P]`.

## Phase D: Verify, QA, PR

- [ ] **T009** [US1+US2+US3] Phase 5 per plan §7. Run a real API host as Production with an http
  Keycloak and observe it refuse to start, quoting the output. Boot the real Aspire stack and
  observe an authenticated API call and a WHEP authorize. Write the verification note.
- [ ] **T010** Phase 6: `/code-review`, and `/security-review` (this is security-sensitive auth
  configuration). Have security-reviewer confirm there are exactly two metadata-fetch sites, and
  that no configuration key can re-open plaintext outside Development.
- [ ] **T011** Phase 7: PR to `develop` (`--base develop`) with `Fixes #2238` in the body. Quote
  T002 and T003's red output. List any plan §5 fixture edits. State "Latency: N/A". Reference
  #2878 as the tracked follow-up for realm `sslRequired` (spec §7).

## Dependency graph

```
T001 ─┬─ T002 [P] ─ T004 ─ T005 ─┐
      └─ T003 [P] ───────┬─ T006 ┴─ T007 ─ T008 ─ T009 ─ T010 ─ T011
                 T004 ───┘
```
