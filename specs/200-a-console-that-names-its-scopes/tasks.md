# Tasks — Spec 200, a console that names its scopes

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2279 · **Branch:** `2279-granular-console-scopes`
**Phase-4a colour:** **RED** — SC-1, SC-7 and (US2) SC-9 must be observed failing and the output quoted in the PR (ADR-0139, constitution §Testing).

`[P]` marks tasks that own **disjoint files** and may run concurrently
(ADR-0109). Everything unmarked is ordered.

---

## Phase 4a — tests first (test-writer). The engineer may not edit any file below.

- [ ] **T001 [P] [US1]** New `tests/Integration.Tests/Identity/ConsoleScopeGrantIntegrationTests.cs` — SC-1, SC-2, SC-3, SC-4, SC-8 against the `AspireFixture`.
  - SC-1 (**red**): `GetAccessTokenForClientAsync("smart-sentinel-eye-web", "operator", "Operator1234", "openid")`; the decoded `scope` claim must not contain `sse.management`; `GET /system-variables` on resource `system-variables` must answer **403**.
  - SC-2 (green): the *same* token on `GET /audit?pageSize=1`, resource `audit-observability`, must answer **200**. This is the control that makes SC-1's 403 mean "missing scope" rather than "broken mint, wrong issuer, rejected audience, or a stack refusing everybody".
  - SC-3 (green): a token from `management-web` + `scope=openid` must answer **200** on the same `GET /system-variables`.
  - SC-4 (green): that token's `scope` claim contains `sse.cameras.write`, `sse.layouts.write`, `sse.overlays.write`, `sse.variables.write`, `sse.audit.read` **by name**, and does not contain `sse.management`.
  - SC-8 (green): no `Authorization` header → **401**, never 403.
  - Assert exact status codes, never "not 200". Decode the JWT with a private base64url reader (the shape at `EventTypeRegistryAuthorizationIntegrationTests:341`) — re-spell it locally; do not widen that class's members to share it.
  - The class doc comment must state which facts are red and which are controls, so a reviewer is not misled into reading five green tests as five proofs.
  - **Depends on:** nothing. **Blocks:** T005–T008.

- [ ] **T002 [P] [US1]** New `tests/Architecture.Tests/LegacyBundleGrantTests.cs` — SC-7 (**red**). Read `src/AppHost/Realms/smart-sentinel-eye-realm.json` the way `ScopeGrantTests` does (walk up to `SmartSentinelEye.slnx`, parse once into a static `JsonDocument`) and assert `"sse.management"` appears in **no** client's `defaultClientScopes` or `optionalClientScopes`, and in none of `KeycloakScopeBundles.Kiosk` / `.Device` / `.WebhookIntegration`.
  - A separate file rather than a new fact on `ScopeGrantTests` so it can run in parallel with T001 and so the guard's subject is named.
  - The failure message must say *why*: the bundle satisfies every `sse.*` policy but `sse.events.publish`, so a client that holds it holds everything and no behavioural test in the suite can see it.
  - **This guard proves the design was written down, not that it holds.** T001's SC-1 is what asks the running system. Both are required; say so in the doc comment.
  - **Depends on:** nothing. **Blocks:** T006.

- [ ] **T003 [P] [US1]** New `e2e/management-identity.spec.ts` plus whatever read helper it needs in `e2e/support/` — SC-5 (**red**). Mirror `e2e/kiosk-identity.spec.ts` + `e2e/support/kiosk-session.ts`: sign in as `operator` / `Operator1234` through the real Keycloak form (reuse `signInAsOperator` from `e2e/support/sign-in.ts` — read it, do not duplicate it), read the access token out of the app's own OIDC store, and assert `azp === 'management-web'`, `scope` does not contain `sse.management`, and `groups` contains `/fabs/munich`.
  - **Determine and record which storage the console uses.** The kiosk moved to `localStorage` (ADR-0131); `management-web` sets no `userStore`, so `react-oidc-context`'s default applies. A helper searching the wrong store returns `null` and the test fails as "no token", which looks nothing like the finding it is meant to report.
  - The `groups` assertion is a control: a repoint that lost `sse-groups` would break every fab-scoped read while every scope assertion stayed green.
  - **Depends on:** nothing. **Blocks:** T005.

- [ ] **T004 [P] [US2]** `tests/ServiceDefaults.Tests/Authorization/RequireScopePolicyTests.cs` — SC-9 (**red**). **Replace** `Legacy_management_bundle_passes_a_normal_sse_policy` (`:87-97`) and `Legacy_management_bundle_does_not_pass_the_events_publish_policy` (`:99-109`) with one fact: a principal whose only `scope` claim is `"sse.management"` fails **every** policy in `Scope.All`.
  - This deliberately inverts a currently-green assertion. It is the phase-4a red step for US2, written here and **not** by the engineer; ADR-0139's prohibition on editing tests to pass binds phase 4b, and this edit is the specification of the change, not an accommodation to it.
  - Pure unit test — no stack, no realm, milliseconds.
  - **Depends on:** nothing. **Blocks:** T011.

- [ ] **T005a [US1+US2]** Run the four suites, capture **verbatim** output, and hand it to phase 4b unaltered. Expected: SC-1, SC-7, SC-9 red; SC-2, SC-3, SC-4, SC-5, SC-8 green.
  - **A red SC-3 is an escalation, not a step.** It would mean `management-web` is mis-provisioned and the approach needs re-examining before any implementation.
  - **Depends on:** T001–T004.

---

## Phase 4b — implementation (engineer)

### The coupled block — deliberately **not** `[P]`, and it must land as one unit

Any two of T005/T006/T007 without the third is a broken tree (`plan.md` §2):
F1+F3 without F2 is a zero-security-change no-op; F2+F3 without F1 is a
console login loop no C# test can see; F1+F2 without F3 fails every
integration test's token mint with `invalid_scope`.

- [ ] **T005 [US1]** `apps/management-web/src/app/auth.ts` — `client_id: 'management-web'` (`:21`); `scope: 'openid'` (`:26`). Rewrite the comment at `:23-25`, which currently states that `sse.management` "grandfathers the granular `sse.*` policies" — the claim this spec removes. The replacement says what `apps/kiosk-web/src/app/auth.ts:78-95` says: the granular scopes are **default** client scopes, applied whether or not they are asked for, and naming any scope this client does not hold fails the whole sign-in with `invalid_scope` and yields no token at all.
  - **Depends on:** T001, T003.

- [ ] **T006 [US1]** `src/AppHost/Realms/smart-sentinel-eye-realm.json` — remove `"sse.management"` from `smart-sentinel-eye-web`'s `defaultClientScopes` (line 145). Leave `sse-identity`, `sse-audience`, `sse-groups`, `sse.audit.read` — SC-2 depends on `sse.audit.read` staying, and narrowing further is out of scope.
  - **Depends on:** T002.

- [ ] **T007 [US1]** `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs` — `ClientId = "management-web"` (`:12`); the private default-grant scope `"openid sse.management"` → `"openid"` (`:111`). Correct the `GetAccessTokenForClientAsync` doc comment at `:92-93`, which describes the old pairing.
  - This is the task whose green run **is** the proof that "identical authority, different claim" is a fact rather than an argument (`plan.md` §5). It changes the token ~300 integration tests carry.
  - **Depends on:** T001.

- [ ] **T008 [US1]** `tests/Integration.Tests/AuditObservability/RunModeStackAddress.cs` — `ClientId` (`:37`) and `["scope"]` (`:110`), the same pair as T007. Left out, the run-mode audit measurement helper mints `openid sse.management` against a client that no longer has it and fails with `invalid_scope`.
  - **Depends on:** T006 (the realm edit is what breaks it).

### Parallel after the block

- [ ] **T009 [P] [US1]** Comment corrections in `e2e/`: `support/sign-in.ts:5-6`, `cameras.spec.ts:24`, `layouts.spec.ts:29`, `overlays.spec.ts:27`, `system-variables.spec.ts:36`. Each states that the operator's token carries `sse.management` and that it grandfathers the scope the test exercises. Both halves stop being true. Comment-only — no assertion changes.
  - Disjoint from T003's new file, though both are under `e2e/`.
  - **Depends on:** T005–T007.

- [ ] **T010 [P] [US1]** `.claude/agents/frontend-engineer.md:12` — it states as current fact that *"management-web uses `smart-sentinel-eye-web` with `openid sse.management` (the grandfathered bundle)"*. That brief is load-bearing when no human is watching (CLAUDE.md), so a stale copy would instruct the next frontend agent to reintroduce this defect. Correct the sentence; the `kiosk-web` half of it is already right and stays.
  - **Depends on:** T005.

### US2 — **deferred, filed as #2486**

- [x] **T011 [US2]** attempted during this delivery, then reverted. Deleting
  `RequireScopeExtensions.LegacyManagementBundle` does not compile:
  `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs:836` references it
  directly, and `src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs:31,80`
  has its **own, independent** hand-rolled acceptance of the same bundle string
  — not routed through `RequireScopeExtensions` at all, since `POST
  /streams/authorize` authorizes itself rather than going through
  `AddScopePolicies`. T011/T012 as originally scoped were a two-file change;
  completing US2 correctly is at least four files plus a doc-comment pass on
  two integration tests that explain their setup by citing the grandfather
  clause. Re-scoped and filed as **#2486** rather than expanded here mid-PR —
  see that issue for the full file list.
  - T004's phase-4a red test (`RequireScopePolicyTests.A_principal_carrying_only_the_legacy_bundle_fails_every_policy`)
    was written, confirmed red, and then **reverted** along with T011 — it
    cannot ship red in a PR that stops at US1, and it belongs with US2's
    implementation. `RequireScopePolicyTests.cs` is back to its original two
    green facts in this PR.
- [ ] **T012 [US2]** not attempted — depends on T011, deferred with it. See #2486.

**Remainder delivered:** the WHEP handler's own copy of this acceptance was
fixed by spec 258 / PR #2611; T011/T012's remainder (`RequireScopeExtensions`,
`AuthenticationDefaults.AdminPolicy`, the realm's `sse.management` catalogue
entry) was completed under spec 265, closing #2486.

---

## Phase 5 — verification (`/verify`)

- [ ] **T013** Full suites green: `Integration.Tests`, `Architecture.Tests`, `ServiceDefaults.Tests`, the management-web Vitest project, and e2e. The integration suite passing **after T007** is the load-bearing result, not a formality.
- [ ] **T014** Manual observation against a booted stack, **after deleting the `keycloak-data` volume** (`plan.md` §4 — the realm import runs only against a fresh volume, and a stale realm makes SC-1 look unfixed in a way that is indistinguishable from the edit never being made). Stop the AppHost first; ask the user before stopping a shared stack. Run the two `curl` mints from `spec.md` §*Independent end-to-end test procedure* steps 3–4 and record both the decoded `scope` claim and the two status codes.
- [ ] **T015** Sign in to `management-web` in a browser as `operator`, read the token from its OIDC store, record `azp`, the absence of `sse.management`, and that the Cameras page still lists and a camera can still be registered.
- [ ] **T016** Write `verification.md`. **Record every figure as observed**, including the ones that merely confirm — a measurement reported only to the orchestrator is invisible to every later grep and reviewer.

## Phase 6 — QA

- [ ] **T017** `/code-review`.
- [ ] **T018** `/security-review` — **mandatory**, not conditional. This is an authorization change. Ask it specifically whether US1 alone leaves a reachable path to the bundle, and whether dropping `smart-sentinel-eye-web` to `sse.audit.read` while it keeps `publicClient` + `directAccessGrantsEnabled` and has no remaining consumer should be a follow-up issue.

## Phase 7 — PR

- [ ] **T019** PR to **`develop`** (`--base develop`), body carrying: the verbatim phase-4a red output for SC-1, SC-7 and SC-9; the §2 coupling table; the volume-trap note; the "identical authority, different claim" argument with the integration suite as its evidence; the two flags below. Closing keyword for #2279 — and check the issue's state after the merge, a mention alone usually does not close it.

---

## Gates and flags for the orchestrator

- **Phase 3 gate:** #2279 is on **Project #13**, status **Todo**, labelled `agent:ready` without `agent:blocked` — verified. No per-task issues: since spec 028 the feature-level issue is the tracked artifact and `tasks.md` is what the work is measured against.
- **Flag 1 — an ADR is owed, and not by this lane.** Per-role least privilege (an operator's token narrower than an admin's) needs a role→client-scope mapping that no ADR decides. This slice makes it *possible* and does not do it. ADR-0144 forbids the lane writing the ADR.
- **Flag 2 — ADR-0080's code sketch is stale** (`client_id="smart-sentinel-eye-management"`, `scope="openid profile sse.management"`), already before this change and in a second way after it. Amending an ADR is a human's call. Raise it in the PR body or file it.
- **Flag 3 — a follow-up worth filing.** After US1 nothing in the repository uses `smart-sentinel-eye-web`, yet it stays public, direct-access-grant enabled, and holding `sse.audit.read`. Deleting or disabling it is a decision, not a fix, so it is out of scope here.
- **Flag 4 — one premise of #2279 was stale.** *"Nothing proves a granular scope is enforced against a token that lacks it"* is no longer true: `VariableReadScopeIntegrationTests` (spec 073) and `EventTypeRegistryAuthorizationIntegrationTests` (spec 143) both do. That changed the plan — 4a reuses the existing mechanism instead of building one — and not the goal.
