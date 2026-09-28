# Tasks: Spec 283, the host that outlived its package

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2643 · **Lane:** supervised (dispatched
for phases 1–2 only at plan time; phase 4 dispatched separately, plan.md header)
**Phase-4a colour:** **CHARACTERISATION (behaviour-preserving). NOT red.** The covering test is
new (no prior test named these three resources), written first, observed green against
`develop`'s pinned `Aspire.Hosting.NodeJs` 9.5.2, and must pass **unmodified** once the package
is swapped (plan §4, constitution §Testing, ADR-0139). An assertion that needs editing to stay
green is evidence the swap moved behaviour, and blocks the refactor rather than being "fixed".
**Engineers:** `test-writer` (4a), then `infra-engineer` (4b).

There is one story (spec, "no P2: the package cannot be half-replaced"), so no `[US]` tags.
`[P]` marks tasks with no ordering dependency on the previous one.

**Board.** The feature issue is #2643. Add to Project #13 by hand (CLAUDE.md, Phase 3) if not
already there — `/speckit-tasks` adds nothing.

**Branch.** `chore/2643-aspire-hosting-javascript`, cut from `origin/develop` at `edf41b97`.

**Contention files** (ADR-0109, one writer this batch): `Directory.Packages.props`,
`src/AppHost/AppHost.cs`. Check no parked PR edits either before opening the PR (plan §Assumptions
"Coordination").

**Per-commit rule (ADR-0087).** Each commit below builds under `-c Release` **on its own** —
the props/csproj/`AppHost.cs` triad is one commit (plan §D3) because none of the three compiles
without the other two.

---

## Phase 4a: characterisation test (`test-writer`)

- [x] **T001** Read the pinned 13.5.4 and 9.5.2 `Aspire.Hosting.JavaScript`/`Aspire.Hosting.NodeJs`
  sources directly (decompiled, not recalled) to confirm the API surface plan §1 describes.
- [x] **T002** Write `tests/Integration.Tests/AppHostWebAppHostingTests.cs` (22 facts across the
  three web apps: `npm run dev`, working directory, no installer wait, exactly one fixed
  unproxied `http` endpoint, parent relationship to `api-gateway`, the common + kiosk + wall
  environment keys) per plan §4. Never names `NodeAppResource` or its replacement.
  - **Depends on:** T001.
- [x] **T003 [P]** Append the new class's `FullyQualifiedName~` filter to
  `tests/Integration.Tests/ci-shards/shard-2.filter` (memory: new test classes need a shard-filter
  entry).
- [x] **T004** Run the suite against `develop`'s pinned 9.5.2 package and record the verbatim
  green output (22/22) — the phase-4b brief.
  - **Depends on:** T002, T003.

## Phase 4b: the swap (`infra-engineer`)

- [x] **T010** Re-run T004's 22 tests against the unmodified 9.5.2 baseline in this worktree to
  confirm the brief before touching anything (22/22 green, reproduced independently of the
  test-writer's own run).
  - **Depends on:** T004.
- [x] **T011** Commit: pin `Aspire.Hosting.JavaScript` 13.5.4 in `Directory.Packages.props`
  (replacing `Aspire.Hosting.NodeJs` 9.5.2, comment updated) and in
  `src/AppHost/SmartSentinelEye.AppHost.csproj`; swap the three `AddNpmApp(...)` call sites in
  `src/AppHost/AppHost.cs` for `AddJavaScriptApp(...).WithNpm(install: false)`, keeping every
  chained call byte-identical, plus the one-sentence *why* comment (plan §3); update the
  `AddNpmApp` mention in `apps/shared/src/observability/kioskLatency.ts:90` (spec FR-007). No
  other file changes.
  - **Depends on:** T010.
- [x] **T012** `dotnet build SmartSentinelEye.slnx -c Release` clean (0 errors; no new NU1605/
  NU1608 from the transitive pins, plan §3 "Transitive pins").
  - **Depends on:** T011.
- [x] **T013** Run `AppHostWebAppHostingTests` against the swapped package: 22/22, file
  byte-identical to T002/T004 (confirmed via `git diff` touching no test file).
  - **Depends on:** T012.
- [x] **T014** Counterfactual (plan §4, memory: prove a guard by counterfactual): temporarily drop
  `.WithNpm(install: false)` from one call site and observe `Nothing_waits_on_an_installer_resource`
  and `The_expected_resource_set_names_no_installer` go red for the stated reason; temporarily
  change one fixed port and observe `The_app_exposes_exactly_one_fixed_unproxied_http_endpoint` go
  red for the stated reason. Quote both, then revert and re-confirm 22/22 green.
  - **Depends on:** T013.
- [x] **T015 [P]** Run the adjacent baseline classes unmodified: `AppHostE2ESwitchTests`,
  `AppHostMediaMtxImageTests`, `AppHostStackStatusTests`, `AppHostMigrationGateTests`,
  `AppHostContainerImagePinTests`, `AppHostGatewayRateBudgetTests`, `AppHostParameterOverrideTests`,
  `AppHostReplicaCountTests` (52 tests) — confirm nothing broke incidentally.
  - **Depends on:** T012.
- [x] **T016** `git grep -n "Aspire.Hosting.NodeJs\|AddNpmApp" -- ':!specs'` returns nothing (SC-001).
  - **Depends on:** T011.
- [x] **T017** Smoke boot (plan §7, spec SC-004): confirm no other Aspire stack is running on this
  machine (memory: one machine, one Aspire stack), then `pnpm install --frozen-lockfile`,
  `dotnet dev-certs https`, boot the AppHost in run mode in the background with the same arguments
  CI's e2e job uses. Confirm via the Aspire MCP tools and a direct `curl` that all three resources
  reach `Running`/`Healthy` on 5173/5174/5175, their `-installer` children stay `NotStarted`, and
  each app serves real HTML. Tear the stack down afterward (kill the AppHost process tree, stop
  and remove the containers it started — they outlive the AppHost, memory).
  - **Depends on:** T012.
- [ ] **T018** Phase 5 verification note on the PR: quote T013's and T014's verbatim output plus
  T017's boot evidence.
  - **Depends on:** T013, T014, T015, T016, T017.
- [ ] **T019** Phase 6: `infra-reviewer` on the PR (contention-file discipline, no suppressed
  warning, no test edit).
  - **Depends on:** T018.
- [ ] **T020** Phase 7: `gh pr create --base develop` against the existing draft PR #2663 (or a
  fresh PR if #2663 was opened for different content — reconcile before opening). Body carries
  T013's and T014's verbatim output, T017's boot evidence, and re-checks the spec number against
  `origin/develop` before merging (memory: spec number check needs more than one look).
  - **Depends on:** T019.
