# Tasks — Spec 265, the bundle the policies still honour

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2486 (remainder — **closes it**) · **Branch:** `fix/2486-legacy-bundle-remainder`
**Phase-4a colour:** **RED** — SC-9 and SC-10 must be observed failing and the output quoted verbatim in the PR (ADR-0139, constitution §Testing). Everything else in 4a is **characterisation**: prose edits to test files whose assertions do not change, observed green before and after.
**Lead engineer (4b):** `backend-engineer`, all of it (plan §6).

`[P]` = disjoint files, may run concurrently (ADR-0109). **No foundational
task**: nothing in `Shared.Kernel`, `Shared.Contracts` or AppHost *resources*
changes, so nothing blocks a fan-out. Every 4a task is `[P]` against every
other 4a task; 4b tasks are grouped by commit (plan §5).

---

## Phase 4a — tests first (`test-writer`). Phase 4b may not edit any file below.

- [ ] **T001 [P] [US1]** `tests/ServiceDefaults.Tests/Authorization/RequireScopePolicyTests.cs` — **SC-9 (red).** Replace `Legacy_management_bundle_passes_a_normal_sse_policy` (`:87-97`) and `Legacy_management_bundle_does_not_pass_the_events_publish_policy` (`:99-109`) with `A_principal_carrying_only_the_legacy_bundle_fails_every_policy` per plan §2: literal `"sse.management"` (not the const), iterate `Scope.All`, collect the passing scopes, assert the collection empty with a message naming them. Every other fact in the file unmodified (SC-11, SC-12).
  - **Depends on:** nothing. **Blocks:** T010.

- [ ] **T002 [P] [US1]** *New* `tests/ServiceDefaults.Tests/Authorization/RegisteredPolicyTests.cs` — **SC-10 (red)** `The_host_registers_no_admin_policy` + its control `The_host_still_registers_a_policy_per_catalogued_scope` (green), per plan §3. Real `AddBearerAuthentication()` on `Host.CreateEmptyApplicationBuilder(null)`; literal `"admin"`, not `AuthenticationDefaults.AdminPolicy`.
  - **Depends on:** nothing. **Blocks:** T011.

- [ ] **T003 [P] [US1]** `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs` — characterisation. A8b (`:826-859`): delete the `bundle` local and its `ShouldNotContain` block, and the doc paragraph `:834-843` that pre-commits this; keep the `RequiredScope` `ShouldContain` and the test name. Class doc `:255-270`: rewrite per plan §4 (no `cref` to the const). Pinned counts `:358-360` unchanged.
  - **Depends on:** nothing. **Blocks:** T010 (F9 cannot compile while `:849` references the const).

- [ ] **T004 [P] [US1]** `tests/Architecture.Tests/ScopeGrantTests.cs` — characterisation, prose only: `:22-27`, `:82-85`, `:89-92`, `:103-106` → the bundle *used to* satisfy every policy (withdrawn, spec 265). No assertion, fact name or helper changes.
- [ ] **T005 [P] [US1]** `tests/Architecture.Tests/LegacyBundleGrantTests.cs` — characterisation, prose only (decision D1): class doc `:6-31` and message `:45-53` — a withdrawn scope no code honours, kept withdrawn; note that `RealmIdentityTests.Every_scope_a_client_names_exists` now also fails on a static re-grant, and this class alone covers `KeycloakScopeBundles` and the realm-level defaults. **If the gate reviewer picks deletion instead, this task becomes "delete the file" and nothing else moves.**
- [ ] **T006 [P] [US1]** `tests/Architecture.Tests/KioskScopeParityTests.cs` — characterisation, prose only: doc `:74-79`. Fact and const untouched.
- [ ] **T007 [P] [US1]** `tests/Integration.Tests/EventIngestion/EventTypeRegistryAuthorizationIntegrationTests.cs` — characterisation, prose only: `:219-221`, `:328-331`, `:334-336`. `ShouldNotContain("sse.management")` stays.
- [ ] **T008 [P] [US1]** `tests/Integration.Tests/Identity/ConsoleScopeGrantIntegrationTests.cs` — characterisation, prose only: class doc `:12-20`. All five facts' assertions untouched.

- [ ] **T009 [US1]** Run and return **verbatim** output (both before any 4b change):
  ```
  dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~RequireScopePolicyTests|FullyQualifiedName~RegisteredPolicyTests"
  dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~ScopeGrantTests|FullyQualifiedName~LegacyBundleGrantTests|FullyQualifiedName~KioskScopeParityTests|FullyQualifiedName~RealmIdentityTests|FullyQualifiedName~RealmAudienceTests"
  ```
  **Expected:** red = SC-9 (lists every `Scope.All` policy but `sse.events.publish`) and SC-10 (`admin` policy is non-null); **every other fact green**, including SC-10's control. Any other red is an escalation, not a step. T007/T008 are integration files: confirm they compile (`dotnet build tests/Integration.Tests`); their runtime run is T015.
  - **Depends on:** T001–T008.

## Phase 4b — implementation (`backend-engineer`). No test file in this diff.

**Commit C1** — `fix(service-defaults): stop scope policies accepting the legacy management bundle` (F1, F3, F9)
- [ ] **T010 [US1]** `src/ServiceDefaults/Authorization/RequireScopeExtensions.cs` — delete `:28-36` (the const's `<summary>` and `LegacyManagementBundle`), `:46` (`acceptLegacyBundle`), `:59-62` (its `if`). The `<summary>` at `:22-27` now sits on `AddScopePolicies`. Commit with T001 + T003.
  - **Depends on:** T009. **Verify:** SC-9 green; Release build clean.

**Commit C2** — `fix(service-defaults): remove the dead admin policy and its management scope` (F2, F10, F11)
- [ ] **T011 [P] [US1]** `src/ServiceDefaults/AuthenticationDefaults.cs` — delete `:22-32` (doc, `#pragma S1133` pair, `[Obsolete] AdminPolicy`), `:103-115` (`#pragma CS0618` pair and `.AddPolicy(AdminPolicy, …)`; chain ends `.AddScopePolicies(Scope.All);`), `:120` (`ManagementScope`) — and **`src/Automation/Api/RulesEndpoints.cs:20-23`** doc per plan F11 in the **same commit** (the `cref` dangles otherwise). Commit with T002.
  - **Depends on:** T009. `[P]` with T010 (disjoint files). **Verify:** SC-10 + control green; Release build clean.

**Commit C3** — `fix(realm): drop the sse.management client-scope definition` (F12)
- [ ] **T012 [P] [US1]** `src/AppHost/Realms/smart-sentinel-eye-realm.json` — delete the `sse.management` object (`:40-48`); leave the array valid. Nothing else in the file.
  - **Depends on:** T009. `[P]` with T010/T011. Commit **after C1** (plan §5). **Verify:** SC-14 — T009's architecture command green, unmodified.

**Commit C4** — `docs: stop describing the withdrawn bundle as a grant` (F4–F8, F13–F16)
- [ ] **T013 [US1]** Doc/comment edits, all in one commit with T004–T008's test-file prose:
  - `src/LayoutComposition/Infrastructure/Broadcasting/LayoutLifecycleHub.cs:14-16` — drop the bundle parenthetical.
  - `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts:37` — drop the bundle parenthetical; `pnpm --filter kiosk-web lint` and `pnpm format:check`.
  - `.claude/agents/backend-engineer.md:17`, `security-reviewer.md:17`, `test-adversary.md:11` per plan F15 — **only if the user approved D4 at the phase-3 gate**; otherwise skip and say so in the PR.
  - `specs/200-a-console-that-names-its-scopes/tasks.md` US2 block — one line: delivered by specs 258 and 265.
  - **Depends on:** T010–T012 (every sentence must be true at the commit that writes it).

- [ ] **T014 [US1]** Final 4b checks: T009's two commands all green; **no file under `tests/` differs from its state when T009 ran** (snapshot `tests/` at T009, e.g. `git stash create` or a tree hash, and diff against it — the commits *contain* the 4a test edits, so a plain `develop..HEAD` diff will list them); `dotnet build -c Release`; `dotnet format --verify-no-changes` on ServiceDefaults, Automation.Api, LayoutComposition.Infrastructure; `grep -rn "sse\.management" src` → empty; `grep -rn "LegacyManagementBundle\|AdminPolicy\|ManagementScope" src tests` → empty. Each of C1–C4 checked out and built on its own (ADR-0087).

## Phase 5 — verification (`/verify`)

- [ ] **T015** SC-13 — on a fresh Aspire stack (CI's integration job is authoritative; locally, a **new** `keycloak-data` volume, and ask before stopping a shared stack): `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~ConsoleScopeGrant|FullyQualifiedName~VariableReadScope|FullyQualifiedName~EventTypeRegistryAuthorization|FullyQualifiedName~Whep"` green. Record observed pass counts in `verification.md`. A red here means some principal was passing on the bundle — **escalate, do not patch**.
- [ ] **T016** Latency: N/A (spec §Latency) — state it; claim no measurement.

## Phase 6 — QA

- [ ] **T017** `/code-review` (`backend-reviewer`).
- [ ] **T018** `/security-review` — **mandatory** (authorization change). Ask: does any seeded user, service account, runtime-enrolled client (`KeycloakScopeBundles`) or fixture reach any endpoint only through the bundle; is anything else registered as a policy outside `Scope.All`.

## Phase 7 — PR

- [ ] **T019** PR to **`develop`** (`--base develop`). Body: verbatim T009 red (SC-9, SC-10); the premise table from `spec.md` (what #2611 already did, what was already fixed, what this does); decisions D1–D4 and their outcome at the gate; the stale-volume note. **`Closes #2486`** — check the issue state after merge (a mention alone usually does not close it). If #2617 merged first, rebase before merging (`EndpointScopeDeclarationTests.cs`).

---

## Gates and flags for the orchestrator

- **Phase-3 gate — board:** #2486 is a feature-level issue (spec 028+ convention); spec 258 verified it on Project #13 / Todo / `agent:ready` on 2026-09-26. **Not re-verified here** — the GraphQL API was rate-limited during planning. Verify with `gh project item-list 13 --owner smartsolutionslab --limit 2000` before phase 4.
- **Phase-3 gate — user decisions:** D1 (keep `LegacyBundleGrantTests`, reword) and D4 (edit three `.claude/agents/*.md` briefs). Both default as written; D4 needs an explicit yes because it is project configuration.
- **Spec number 265.** Checked: `origin/develop` has 258 and 261; open/local branches carry 262 (#2616), 263 (#2617), 264 (`fix/2575-…`); no branch, worktree or remote carries 259, 260 or 265. The 259/260 gap is unexplained, so 265 was taken over reusing it. Re-check before the PR if a parked PR merges first.
- **Engineer:** `backend-engineer` for all of 4b; not `infra-engineer` (plan §6).
