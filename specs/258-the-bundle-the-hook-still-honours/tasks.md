# Tasks — Spec 258, the bundle the hook still honours

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2486 (partial — do **not** close) · **Branch:** `fix/2486-whep-legacy-bundle-cleanup`
**Phase-4a colour:** **RED** — SC-1 and SC-7 must be observed failing and the output quoted verbatim in the PR (ADR-0139, constitution §Testing).

`[P]` = disjoint files, may run concurrently (ADR-0109). No foundational
task: nothing in `Shared.Kernel`, `Shared.Contracts` or `AppHost` changes.

---

## Phase 4a — tests first (`test-writer`). Phase 4b may not edit any file below.

- [ ] **T001 [P] [US1]** `tests/StreamDistribution.Application.Tests/Commands/AuthorizeWhepCommandHandlerTests.cs` — plan §2, all five edits:
  - **SC-1 (red):** replace `Authorize_with_a_grandfathered_management_token_returns_success` (`:39-63`) with `Authorize_with_only_the_legacy_management_bundle_returns_Forbidden` — subject `["openid", "sse.management"]`, `Read`, asserts `IsFailure` and `Error.ShouldBeOfType<AuthorizeWhepError.Forbidden>()`. Doc comment: the inversion is deliberate (#2486), and names SC-2 as its control.
  - **SC-2 (green control):** new `Authorize_with_the_consoles_granular_token_returns_success` on a new `AConsolePersona` array (management-web's granular `sse.*` scopes, including `sse.streams.read`, no bundle) — asserts `result.Value.ShouldBe(path)`.
  - **Fixture repair:** `Authorize_for_an_Offline_stream_returns_StreamUnavailable` subject (`:171`) → `AKioskPersona`. Assertion unchanged.
  - **Repoint:** `Authorize_a_publish_with_the_grandfathered_bundle_is_refused` → `Authorize_a_publish_with_the_consoles_broadest_token_is_refused` on `AConsolePersona`; doc updated; assertion unchanged.
  - **Rename:** `…_granting_neither_the_read_scope_nor_the_bundle_returns_Forbidden` → `Authorize_with_a_token_without_the_read_scope_returns_Forbidden`; body unchanged.
  - **Depends on:** nothing. **Blocks:** T004.

- [ ] **T002 [P] [US1]** `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs` — plan §3:
  - **SC-7 (red):** A8b `:821-854` → `The_whep_hook_summary_names_the_one_scope_its_handler_accepts`: keep the `RequiredScope` `ShouldContain`; replace the bundle `ShouldContain` with `ShouldNotContain(RequireScopeExtensions.LegacyManagementBundle, Case.Sensitive, …)`; drop the `AdminPolicy` sentence; doc states the const reference is deliberate and is deleted with the const (#2486 remainder).
  - Class doc `:255-267`: keep the first four sentences (the policy composition is unchanged and still true); replace *"Only POST /streams/authorize says so…"* with: no summary names the bundle; the hand-rolled WHEP check no longer accepts it (#2486); the gap is confined to policy-routed endpoints until `AddScopePolicies` withdraws it. Keep the `KioskScopeParityTests` pointer.
  - Pinned counts `:358-360` must **not** change.
  - **Depends on:** nothing. **Blocks:** T004.

- [ ] **T003 [US1]** Run both suites, return **verbatim** output:
  `dotnet test tests/StreamDistribution.Application.Tests --filter "FullyQualifiedName~AuthorizeWhepCommandHandlerTests"` and
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~EndpointScopeDeclarationTests"`.
  **Expected:** red = SC-1 (*Success* where *Forbidden* expected) and SC-7 (summary contains `sse.management`); every other fact green. Any other red is an escalation, not a step.
  - **Depends on:** T001, T002.

## Phase 4b — implementation (`backend-engineer`)

- [ ] **T004 [US1]** `src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs` — delete `LegacyManagementBundle` (`:31`) and the `&& !subject.Scopes.Contains(LegacyManagementBundle, …)` line (`:80`); the condition becomes `if (!subject.Scopes.Contains(RequiredScope, StringComparer.Ordinal))`. Rewrite the `RequiredScope` doc (`:15-28`) per plan §3 (read scope and nothing else; ADR-0051 reason kept; spec 041 paragraph kept; hand-rolled check does not inherit the policy's grandfathering).
  - **Depends on:** T003.
- [ ] **T005 [US1]** `src/StreamDistribution/Api/StreamEndpoints.cs:72-74` — replace *"then requires sse.streams.read on it — or the grandfathered sse.management bundle, named here because this check is hand-rolled; every scoped endpoint accepts the bundle too, through its policy."* with *"then requires sse.streams.read on it."* Nothing else in the summary changes; `No OIDC scope:` stays.
  - **Depends on:** T003. Same engineer as T004 (the doc and the summary must agree) — not `[P]`.
- [ ] **T006 [US1]** `src/StreamDistribution/Application/Commands/AuthorizeWhepCommand.cs:9-11` — "checks the `sse.management` scope" → "checks the `sse.streams.read` scope". Comment-only; **flagged as an addition beyond the orchestrator's brief** (plan §1 F5) — skip if vetoed, nothing depends on it.
- [ ] **T007** Re-run T003's two commands: all green, **no test file in the diff since T003**. Build Release (`TreatWarningsAsErrors`) and `dotnet format --verify-no-changes` on the touched projects.

## Phase 5 — verification (`/verify`)

- [ ] **T008** `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~Whep"` (Aspire fixture) green — SC-8, the over-correction guard: the fixture's `management-web` token is still admitted. Record pass counts in `verification.md` as observed.
- [ ] **T009** Latency: N/A (spec §Latency) — state it in the verification note; do not claim a measurement.

## Phase 6 — QA

- [ ] **T010** `/code-review`.
- [ ] **T011** `/security-review` — **mandatory** (authorization change). Ask specifically: does any caller of `/streams/authorize` still depend on the bundle (realm clients, `KeycloakScopeBundles`, e2e), and is the WHEP-vs-policy asymmetry correctly recorded.

## Phase 7 — PR

- [ ] **T012** PR to `develop` (`--base develop`). Body: verbatim T003 red output (SC-1, SC-7); the *Scope* table from `spec.md` listing #2486's **remaining** items (RequireScopeExtensions + its red test from spec 200 T004, realm `clientScopes` entry, `AdminPolicy`/`ManagementScope`, `RulesEndpoints.cs:22`, the two `CrossFab*` comments). Reference as `Refs #2486` — **no closing keyword**. After merge, comment the remaining list on #2486 so its `agent:ready` card describes only what is left (label handling is the orchestrator's/human's call).

---

## Gates and flags for the orchestrator

- **Phase 3 gate:** #2486 is on Project #13, status **Todo**, labelled `agent:ready`, no `agent:blocked` — verified 2026-09-26. No per-task issues (feature-level since spec 028).
- **Spec number:** 258 — checked against `origin/develop` (max 257), every open PR branch and every remote branch (none claims 258+), and sibling `D:\Github\sse-*` worktrees. Re-check before the PR if another parked PR merges first.
- **Flag — #2486 stays open with `agent:ready`.** The lane will pick it up again for the remainder, which is the policy-wide withdrawal. Whether that is wanted next, or should wait for a human, is not this lane's call.
