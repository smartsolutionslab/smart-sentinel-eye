# Spec 265 — The bundle the policies still honour

**Issue:** #2486 — *Withdrawing the sse.management grandfather clause (spec 200 US2) also needs AuthorizeWhepCommandHandler's own copy* (**the remainder** — spec 258 / PR #2611 delivered the WHEP handler; this spec closes the issue)
**Branch:** `fix/2486-legacy-bundle-remainder`
**Parents:** spec 200 (`specs/200-a-console-that-names-its-scopes/`) US2 — T004, T011, T012, deferred there and filed as #2486; spec 258 (`specs/258-the-bundle-the-hook-still-honours/`) — the WHEP slice of the same issue, whose *Scope* table lists what is left
**Phase-4a colour:** **RED** (behaviour-changing — two authorization answers flip: a bundle-only principal stops passing every `sse.*` policy, and the `admin` policy stops existing)
**ADRs:** ADR-0080 (browser auth; the historical `sse.management` request), ADR-0089 (`ApiError` — the 403 shape is unchanged), ADR-0100 (`sse.events.publish` is enforced on the MQTT side — why its carve-out is not a loss), ADR-0139 (new behaviour starts red; refactors stay green), ADR-0037 (phases), ADR-0109 (parallel markers), ADR-0087 (rebase-merge — each commit builds and is green on its own)
**Constitution:** §VIII *Safe by Default at Trust Boundaries* ("Authorization is enforced by scope checks at every endpoint"), §Testing (two obligations — red for the policy change, characterisation for the dead-code and realm removals)
**No ADR needed.** Every removal here enacts a decision already taken: spec 200 US2 decided the grandfather clause and the realm definition go; `AuthenticationDefaults.AdminPolicy` has carried `[Obsolete("… Removed in spec 009.")]` since issue #844 (closed). Nothing here chooses between designs.

---

## Problem

After spec 200 US1 no realm client grants `sse.management`, and after spec 258
the WHEP hook no longer accepts it. **Two independent spellings of the bundle
still grant authority**, and one realm definition still makes it mintable:

| # | Where (verified on `c276d717`, this branch's base) | What it does today |
|---|---|---|
| 1 | `src/ServiceDefaults/Authorization/RequireScopeExtensions.cs:28-36,46,59-62` | `LegacyManagementBundle = "sse.management"`; every policy `AddScopePolicies` registers except `sse.events.publish` also returns `true` for a principal carrying the bundle. **Every policy-routed `sse.*` endpoint and both SignalR hubs inherit this.** |
| 2 | `src/ServiceDefaults/AuthenticationDefaults.cs:22-32,103-115,120` | `[Obsolete] AdminPolicy = "admin"` registered on every service with a hand-rolled `RequireAssertion` requiring `ManagementScope = "sse.management"`. **No endpoint, hub or attribute names it** (grep for `AdminPolicy`, `"admin"` policy strings and `Policy = "admin"` — only the definition and one stale doc `cref`). A registered policy requiring a scope no client can hold. |
| 3 | `src/AppHost/Realms/smart-sentinel-eye-realm.json:40-48` | The `sse.management` entry in the realm's `clientScopes` catalogue. No client lists it (guarded by `LegacyBundleGrantTests`), but it stays definable-and-grantable by a one-line realm edit or an admin-console click. |

**Inert is not gone** (spec 200 US2's own words). A realm edit, a merge, or a
hand-made production client that re-grants the bundle silently restores
write-everything authority on every policy-routed endpoint.

### Premise check — the issue text is partly stale

| #2486 item | Status on `c276d717` |
|---|---|
| 1. `RequireScopeExtensions.cs` — T011 as scoped | **Outstanding.** PR #2611 did not touch this file (`git show --stat 6c51e583`). |
| 2. `AuthorizeWhepCommandHandler.cs` const + clause | **Done** — PR #2611 (`6c51e583`, `a17f067c`). |
| 3. `POST /streams/authorize` OpenAPI summary | **Done** — PR #2611 (`StreamEndpoints.cs`). |
| 4. `EndpointScopeDeclarationTests` A8b + class doc | **Half done.** A8b was renamed `The_whep_hook_summary_names_the_one_scope_its_handler_accepts` and now asserts the bundle's *absence* — but still through `RequireScopeExtensions.LegacyManagementBundle` (`:849`), and its own doc (`:836-843`) says that assertion is deleted with the const. The class doc (`:255-270`) still describes the policy grandfathering as current. |
| 5. Realm `clientScopes` definition — T012 | **Outstanding.** |
| 6. `CrossFabListIntegrationTests.cs:16-19`, `CrossFabDisableIntegrationTests.cs:16-18` stale comments | **Already fixed** — both now say the operator's `management-web` token names the `sse.identity.*` scopes explicitly (spec 200's fix round, `814ec5d1`/`20ecb430`). **No change here.** |
| Comment: `AdminPolicy` / `ManagementScope` | **Outstanding.** Confirmed dead. |
| Comment: `RulesEndpoints.cs:22` doc naming `AdminPolicy` | **Outstanding** — and doubly stale: it also says read endpoints "land in PR F"; they exist (`:75-98`, `Scope.Sse.Rules.Read`). |
| Spec 200 T004's reverted red test (`A_principal_carrying_only_the_legacy_bundle_fails_every_policy`) | **Not recoverable from history** — `git log -S` finds the name only in spec/task prose, never in a committed `.cs`. **Rewrite it** from spec 200 SC-9. |
| *Not in the issue:* other text that becomes false once the clause goes | `LayoutLifecycleHub.cs:14-16`, `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts:37`, `ScopeGrantTests.cs:22-27,82-85,89-92,103-106`, `LegacyBundleGrantTests.cs:7-31,45-53`, `KioskScopeParityTests.cs:74-79`, `EventTypeRegistryAuthorizationIntegrationTests.cs:219-221,328-336`, `ConsoleScopeGrantIntegrationTests.cs:12-20`, and three **agent briefs** (`.claude/agents/backend-engineer.md:17`, `security-reviewer.md:17`, `test-adversary.md:11`) that instruct reviewers the bundle grandfathers every policy. |

### After this spec, nothing grants on the bundle

Every hand-rolled scope check in `src/` was enumerated (`FindAll("scope")`,
`FindFirst("scope")`, `Split(' ', …)`): `RequireScopeExtensions` (item 1),
`AuthenticationDefaults` (item 2), `WhepAuthValidator.cs:179` (parses scopes,
decides nothing — the handler decides, fixed by spec 258), and
`EventsEndpoints.Writes.cs:274` (checks `sse.events.write` only). No other
occurrence of the literal exists in `src/` or `apps/` code. **Once items 1–3 go,
no code path in the repository treats `sse.management` as anything but an
unknown string.**

---

## Scope

**In:** items 1, 2, 3 above; the compile-coupled half of #2486 item 4; the
`RulesEndpoints.cs` doc; every comment, test message and agent brief that would
describe the bundle as granting authority after this lands.

**Out, deliberately:**

| Not touched | Why |
|---|---|
| The `smart-sentinel-eye-web` client itself | Spec 200 §*Out of scope* recommends a separate follow-up; deleting a client referenced by ADR-0080 is a decision, not a fix. |
| `docs/adr/0080-browser-auth.md:25` (`scope="openid profile sse.management"`) | An ADR records what was decided when. Rewriting it is an ADR amendment, which this spec does not make. |
| Historical-tense prose that stays true: `RealmIdentityTests.cs:19,100`, `VariableReadScopeIntegrationTests.cs:18`, `apps/*/src/app/auth.ts`, `auth.test.ts`, `e2e/*.spec.ts`, `e2e/support/sign-in.ts:7`, `README.md:409`, `AuthorizeWhepCommandHandlerTests.cs:43,72,102,130` | Each describes the bundle in the past tense or as an absence. Editing them is drive-by. |
| `KioskPrivilegeSweepTests.cs:66` (`DefaultClientScopes: ["sse.management"]`) | A fake client's arbitrary scope list; the sweep never reads scopes. Changing the string changes nothing. |
| **Absence assertions** on the string (`e2e/kiosk-identity.spec.ts:43`, `e2e/management-identity.spec.ts:24`, `ConsoleScopeGrantIntegrationTests.cs:55-59,129-132`, `EventTypeRegistryAuthorizationIntegrationTests.cs:334`, `KioskScopeParityTests.cs:83`, `LegacyBundleGrantTests`' three facts, `AuthorizeWhepCommandHandlerTests`' spec 258 SC-1) | **Kept, unmodified.** They stay true and cost nothing; a string that must not come back is still a string that must not come back. Deleting a still-green assertion invites the "weakened gate" question (ADR-0144) for no gain. Their *prose* is corrected where it says the bundle grants something. See decision D1. |

---

## User story

### US1 (P1) — The bundle means nothing to the code

*As* the security owner of a fab,
*I want* `sse.management` to satisfy no authorization policy anywhere in the
system, and to be absent from the realm's scope catalogue,
*so that* re-granting it — by a realm edit, a merge, or a hand-made client in
production — cannot silently restore write-everything authority, and a
reviewer reading the code or the agent briefs is not told it still does.

Single story, deliberately: items 1–3 are independently shippable in principle,
but each is a few lines, they share one test project, one reviewer question
("does anything still depend on the bundle?") and one verification run. Split
into three PRs they would triple the Aspire-backed verification for no gain in
reviewability. They are split into **three commits** instead (see `tasks.md`).

---

## Acceptance scenarios

**SC-9 (P1, security — the red; spec 200's SC-9 verbatim) — a principal carrying only the bundle passes no policy**

```gherkin
Given an authenticated principal whose only scope claim is "sse.management"
When it is evaluated against every policy in Scope.All, as AddScopePolicies registers them
Then every one of them fails
```

Today: every policy but `sse.events.publish` succeeds. **Red.** Replaces
`RequireScopePolicyTests.Legacy_management_bundle_passes_a_normal_sse_policy`
(`:87-97`) and folds `Legacy_management_bundle_does_not_pass_the_events_publish_policy`
(`:99-109`) into it — a deliberate inversion of a green test, written by the
phase-4a test-writer as the specification of the change (ADR-0139). Spells the
bundle as the literal `"sse.management"`, not via the constant — the constant is
deleted in 4b, and a test that referenced it would stop compiling.

**SC-10 (P1, security — the second red) — the registration offers no `admin` policy**

```gherkin
Given the authorization services exactly as AuthenticationDefaults.AddBearerAuthentication registers them,
      built from an empty host builder (the BearerAudienceTests pattern)
When the policy provider is asked for the policy named "admin"
Then it has none
```

Today: returns the `AdminPolicy` requiring `sse.management`. **Red.** The name is
written as the literal `"admin"` — `AuthenticationDefaults.AdminPolicy` is deleted in 4b.

**SC-11 (P1, over-correction control) — the granular path is unmoved**

```gherkin
Given a principal carrying a specific sse.* scope
When it is evaluated against that scope's policy
Then it succeeds; and against a different scope's policy, it fails
```

The existing `RequireScopePolicyTests` facts (`Principal_with_the_exact_scope_passes_…`,
`Principal_without_the_scope_fails_…`, `Space_separated_multi_scope_claim_…`,
`Target_present_across_multiple_separate_scope_claims_passes`,
`Events_publish_policy_passes_for_the_exact_publish_scope`, the unauthenticated
facts) stay green **unmodified**.

**SC-12 (bad request — 401 unmoved).** An unauthenticated principal carrying a
scope claim fails the policy (`Unauthenticated_principal_fails_even_when_a_scope_claim_is_present`,
unmodified); `ConsoleScopeGrantIntegrationTests`' SC-8 (no bearer → 401, never
403) stays green unmodified.

**SC-13 (auth, system — nobody depended on the bundle).** On a fresh Aspire
stack, the integration facts that mint real tokens and hit real policies stay
green **unmodified in their assertions**: `ConsoleScopeGrantIntegrationTests`
(all five), `VariableReadScopeIntegrationTests`,
`EventTypeRegistryAuthorizationIntegrationTests`, `WhepAuthIntegrationTests`.
This is the evidence that no fixture, seeded client or service account was
passing on the bundle's account.

**SC-14 (configuration — characterisation) — the realm no longer defines the bundle, and nothing noticed**

```gherkin
Given smart-sentinel-eye-realm.json without the sse.management clientScopes entry
Then RealmIdentityTests, ScopeGrantTests, RealmAudienceTests, LegacyBundleGrantTests and KioskScopeParityTests
     pass with their assertions unmodified
```

Green before and after. `RealmIdentityTests.Every_scope_a_client_names_exists`
(`:80-94`) is what makes a *future* re-grant fail loudly: a client naming the
bundle now names an undefined scope.

**SC-15 (surface — characterisation) — the WHEP summary guard survives the constant**
`EndpointScopeDeclarationTests.The_whep_hook_summary_names_the_one_scope_its_handler_accepts`
keeps its `RequiredScope` `ShouldContain`; its bundle `ShouldNotContain` is
deleted, as its own doc (`:836-843`, written by spec 258) pre-committed. Green before and after.

**Conflict (409) scenario: N/A.** No resource, version or idempotency key is involved.

---

## Independent end-to-end test procedure

1. On the base commit with only the phase-4a test edits applied:
   `dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~RequireScopePolicyTests|FullyQualifiedName~RegisteredPolicyTests"` →
   SC-9 fails naming the policies the bundle passed; SC-10 fails with a non-null
   `admin` policy. Every other fact green. Quote verbatim.
2. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~ScopeGrantTests|FullyQualifiedName~LegacyBundleGrantTests|FullyQualifiedName~KioskScopeParityTests|FullyQualifiedName~RealmIdentityTests|FullyQualifiedName~RealmAudienceTests"` → green (characterisation baseline).
3. Apply phase 4b. Steps 1–2 all green; **no test file in the 4b diff**.
4. `dotnet build -c Release` (TreatWarningsAsErrors) clean — proves no remaining
   compile reference to `LegacyManagementBundle`, `AdminPolicy` or `ManagementScope`.
5. `grep -rn "sse\.management" src` → **no hits.** `grep -rn "LegacyManagementBundle\|AdminPolicy\|ManagementScope" src tests` → no hits.
6. Integration (Aspire fixture, Docker; CI's fresh volume is authoritative):
   `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~ConsoleScopeGrant|FullyQualifiedName~VariableReadScope|FullyQualifiedName~EventTypeRegistryAuthorization|FullyQualifiedName~Whep"` → green (SC-13).
7. Optional, booted stack **on a freshly created `keycloak-data` volume**: the
   Keycloak admin console's *Client scopes* list no longer shows `sse.management`.
   A developer volume created before this lands keeps the definition — harmless,
   since no client grants it and no code honours it; delete the volume to see the
   file's truth.

---

## Locked tech choices

.NET 10, ASP.NET Core authorization policies (`AddScopePolicies`), xUnit +
Shouldly (ADR-0052), sentence-style test names (ADR-0053), Aspire fixture for
integration (ADR-0103), Keycloak realm import (`smart-sentinel-eye-realm.json`).
**No new dependency, abstraction, scope, policy, endpoint or client.** One new
test file (`RegisteredPolicyTests.cs`), mirroring `BearerAudienceTests`.

## Latency budget

**N/A.** No leg of constitution §IV is touched. The policy change deletes a
second `Contains` that ran only after the first had failed — nothing on an
admitted path gets slower. `LayoutLifecycleHub` (on the event→overlay path's
push side) changes only its doc comment. Not measured, not claimed.

## Success criteria

- SC-9 and SC-10 observed **failing** before phase 4b, output quoted verbatim in the PR (ADR-0139).
- All scenarios green after; the 4b diff contains no test file.
- Release build clean; step 5's greps empty.
- SC-13 green on CI's fresh stack.
- The PR **closes** #2486 (closing keyword) and the issue's state is checked after merge.
- No `[NEEDS CLARIFICATION]` remains.

## Decisions taken in this spec (reviewable at the gate, not open questions)

- **D1 — absence assertions stay; only their prose changes.** Alternative: delete
  `LegacyBundleGrantTests` outright, since its premise ("a client holding it holds
  effectively everything") dies with the clause. Rejected as default because a
  deleted still-green guard reads as a weakened gate (ADR-0144 §*may not*) and the
  class still catches the one thing `RealmIdentityTests` cannot: the bundle
  appearing in `KeycloakScopeBundles` (runtime-created clients) or the realm-level
  `defaultDefaultClientScopes`. **If the reviewer prefers deletion, T006 becomes
  "delete the file" and nothing else moves.**
- **D2 — `AdminPolicy` is removed, not repointed.** The comment on #2486 offered
  both. Repointing to what? No endpoint uses it, the `[Obsolete]` message already
  says "Removed", and every rules write is scoped to `Scope.Sse.Rules.Write`.
- **D3 — SC-10 is added though spec 200 did not have it.** The issue comment
  asks that the issue not be "done" while a registered policy requires a scope no
  client can hold; a removal with no failing-first test would be the one untested
  authorization change in the PR.
- **D4 — agent briefs are edited.** CLAUDE.md calls them load-bearing when no
  human is watching; three would tell future reviewers a withdrawn bundle still
  grandfathers every policy. They are project configuration: **the user approves
  these three edits at the phase-3 gate**, and if they decline, T011 is dropped
  and the PR says so.

## Assumptions, marked

1. **No caller depends on the bundle today.** Verified statically: no realm client
   lists it (`LegacyBundleGrantTests` green; realm `:40-48` is its only occurrence);
   `KeycloakScopeBundles.{Kiosk,Device,WebhookIntegration}` omit it; the
   `AspireFixture` mints from `management-web` (spec 200); no test requests it as a
   `scope` parameter since `814ec5d1`. SC-13 is the runtime confirmation.
2. **Keycloak discards an undefined scope named in a client's `defaultClientScopes`
   with a warning** (recorded in `ScopeGrantTests.cs:100-101` from spec 143 plan §7;
   spec 042 observed it thirty-two times per boot). Not relied on for correctness —
   `RealmIdentityTests` fails the build first.
