# Plan — Spec 200, a console that names its scopes

**Spec:** `specs/200-a-console-that-names-its-scopes/spec.md`
**Issue:** #2279 · **Branch:** `2279-granular-console-scopes` · **Phase-4a colour:** RED

---

## 1. Shape of the change — and why there is no domain model here

This is an **authorization-configuration** change. It has no aggregate, no
value object, no invariant, no repository, no domain event and no integration
event. The usual §2/§3 of a plan would be empty, so they are stated as absent
rather than filled with nothing:

| Usual section | This spec |
|---|---|
| Bounded context | **None.** The change is cross-cutting: one realm file (AppHost), one `ServiceDefaults` helper (US2 only), one frontend app, and tests. |
| Entities / value objects | **None added or changed.** |
| Domain → integration event | **None.** No message crosses a context. |
| Persistence / migration | **None.** No schema, no EF change. |
| New API surface | **None.** No endpoint added, removed or re-routed. |

**The boundary rules are unaffected and stay unaffected.** No cross-context
project reference is added; `ServiceDefaults` is the shared layer every context
already references, and `Shared.Contracts` is untouched. NetArchTest sees no
new edge. US2's edit is *inside* `ServiceDefaults.Authorization`, which is
where the scope policy has always lived.

`src/Identity/Application/KeycloakAdmin/KeycloakScopeBundles.cs` is
deliberately **not** touched: it holds the runtime-created personas (kiosk,
device, webhook integration), none of which carries the bundle, and ADR-0051
keeps that layer ASP.NET-free — it re-spells its scope strings on purpose and
must keep doing so.

### Files, in dependency order

| # | File | US | Change |
|---|---|---|---|
| F1 | `apps/management-web/src/app/auth.ts` | 1 | `client_id` → `'management-web'`; `scope` → `'openid'`; the comment at `:23-25` rewritten (it asserts the grandfathering this change removes) |
| F2 | `src/AppHost/Realms/smart-sentinel-eye-realm.json` | 1 | remove `"sse.management"` from `smart-sentinel-eye-web`'s `defaultClientScopes` (line 145) |
| F3 | `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs` | 1 | `ClientId` → `"management-web"` (`:12`); the default-grant scope `"openid sse.management"` → `"openid"` (`:111`); doc comments at `:92-93` corrected |
| F4 | `tests/Integration.Tests/AuditObservability/RunModeStackAddress.cs` | 1 | `ClientId` (`:37`) and `scope` (`:110`) — the same pair, in the run-mode measurement helper |
| F5 | *new* `tests/Integration.Tests/Identity/ConsoleScopeGrantIntegrationTests.cs` | 1 | SC-1 – SC-4, SC-8 |
| F6 | *new* `tests/Architecture.Tests/LegacyBundleGrantTests.cs` | 1 | SC-7 |
| F7 | *new* `e2e/management-identity.spec.ts` + a helper in `e2e/support/` | 1 | SC-5 |
| F8 | `e2e/support/sign-in.ts`, `e2e/cameras.spec.ts`, `e2e/layouts.spec.ts`, `e2e/overlays.spec.ts`, `e2e/system-variables.spec.ts` | 1 | comments only — each states that `sse.management` grandfathers the scope it exercises, which stops being true |
| F9 | `src/ServiceDefaults/Authorization/RequireScopeExtensions.cs` | 2 | delete `LegacyManagementBundle`, `acceptLegacyBundle`, and the second `if` in the assertion |
| F10 | `tests/ServiceDefaults.Tests/Authorization/RequireScopePolicyTests.cs` | 2 | replace the two `Legacy_management_bundle_*` facts with SC-9 |
| F11 | `src/AppHost/Realms/smart-sentinel-eye-realm.json` | 2 | remove the `sse.management` entry from `clientScopes` |

F8 is comment-only and would normally be a drive-by (CLAUDE.md house rules).
It is not one here: those comments *assert the behaviour this spec removes*,
so leaving them makes the tree self-contradicting. Correcting a comment a
change falsifies is part of the change.

---

## 2. The coupling, spelled out for phase 4b

**F1, F2 and F3 must land together.** Any two without the third is a broken
tree, and each breaks differently:

| Landed | Missing | Result |
|---|---|---|
| F1 + F3 | F2 | Compiles, all tests green, **zero security change** — the console just reaches the same authority through a different client. SC-1 stays red. |
| F2 + F3 | F1 | The console requests `openid sse.management` from a client that no longer has it → **`invalid_scope`, no token, login loop.** Nothing in the C# suite sees this; only SC-5 / a browser does. |
| F1 + F2 | F3 | Every integration test's token mint answers `invalid_scope` → **the entire integration suite fails to obtain a token.** Reads as a catastrophic regression, is a one-line omission. |

This is not a risk to mitigate; it is a fact to schedule. The tasks file makes
them one non-parallel block.

---

## 3. How phase 4a mints a deliberately narrow token

**The question the brief asked, answered from the tree rather than invented.**
Three mechanisms already exist and are green. Two are reused; the third is
not needed.

| Mechanism | Where | Fit here |
|---|---|---|
| **Password grant naming an explicit client** — `AspireFixture.GetAccessTokenForClientAsync(clientId, user, password, scope)` | `AspireFixture.Auth.cs:100-107`, already public, already used by four webhook tests | **This is the one SC-1–SC-4 use.** `smart-sentinel-eye-web` and `management-web` are both `publicClient` + `directAccessGrantsEnabled`, so a password grant needs no secret and no new plumbing. |
| **A planted throwaway client** with a narrow `defaultClientScopes`, created over the Admin API and deleted in `finally` | `EventTypeRegistryAuthorizationIntegrationTests.PlantEventSourceClientAsync` + `RealmProbe` (`tests/Integration.Tests/Identity/RealmProbe.cs`) | **Not needed.** It exists because *that* spec needed a scope set no realm client had. Here the two clients under test are exactly the subjects, so planting a third would test a fiction. |
| **`client_credentials` for a seeded service account** (`scenario-simulator`) | `VariableReadScopeIntegrationTests` | Not needed for the same reason; noted so the next reader knows it was considered. |

**ADR-0103 is not at risk.** Every one of these runs against the real Aspire
stack through `AspireFixture`. No Testcontainers, no new container, no new
fixture, nothing added to `AppHost`.

### The trap that makes a narrow-token test lie, and why it does not bite here

Keycloak applies a client's **default** client scopes to every token it issues;
the `scope` request parameter selects only among **optional** ones. So
`GetAccessTokenForClientAsync("management-web", …, "openid sse.events.write")`
does *not* yield a caller holding only `sse.events.write` — it yields all
twenty. That is asserted against the running provider today by
`EventTypeRegistryAuthorizationIntegrationTests.A_management_web_token_carries_every_scope_that_client_grants`.

This spec never needs a *subset* of a client's scopes. It needs **what each of
two clients actually grants**, which is exactly what `scope=openid` returns.
SC-1's narrowness comes from the client, not the request — which is the only
kind of narrowness Keycloak will honour.

### The assertions, concretely

```
SC-1  GetAccessTokenForClientAsync("smart-sentinel-eye-web", "operator", "Operator1234", "openid")
      → decode scope claim        → must NOT contain "sse.management"     [red today]
      → GET system-variables "/system-variables" → 403                     [red today: 200]

SC-2  same token → GET audit-observability "/audit?pageSize=1"  → 200      [green today and after]

SC-3  GetAccessTokenForClientAsync("management-web", "operator", "Operator1234", "openid")
      → GET system-variables "/system-variables" → 200                     [green today and after]

SC-4  same token → scope claim contains sse.cameras.write, sse.layouts.write,
      sse.overlays.write, sse.variables.write, sse.audit.read
      → and does NOT contain "sse.management"                              [green today and after]

SC-8  aspire.CreateServiceClient("system-variables") with no bearer
      → GET "/system-variables" → 401                                      [green today and after]
```

Resource names are the Aspire ones: `system-variables`, `audit-observability`.
Decoding reuses the base64url `ScopesOf` helper that
`EventTypeRegistryAuthorizationIntegrationTests:341` already contains — **move
it to a shared place or re-spell it; do not reference it across test classes
by making it public without a reason.** The simpler option is preferred: a
private copy in the new file, as the repository has done elsewhere.

**Only SC-1 is red.** SC-2, SC-3, SC-4 and SC-8 are green on arrival and are
**controls, not padding** — each one is what distinguishes "the refusal is real"
from "the fixture broke". Phase 4a states this in the file's doc comment so a
reviewer is not misled into thinking five tests proved a fix.

### SC-5, the browser half

Mirror `e2e/kiosk-identity.spec.ts` + `e2e/support/kiosk-session.ts` exactly.
The console stores its grant differently from the kiosk — the kiosk moved to
`localStorage` under ADR-0131 and `management-web` sets **no `userStore`**
(noted at `e2e/overlays.spec.ts:59`), so `react-oidc-context`'s default
`sessionStorage` applies. **Phase 4a must read the storage the console
actually uses and say which**; a helper that searches the wrong store returns
`null` and the test fails as "no token" rather than as a scope finding — the
exact failure spec 041 hit and recorded.

---

## 4. The volume trap — the false green, and its precise blast radius

`WithRealmImport` runs **only against a fresh Keycloak volume**
(`src/AppHost/AppHost.cs:150-154`, stated in the AppHost's own comment).
Restarting Keycloak, or restarting the AppHost, keeps the old realm and the
stack looks perfectly healthy.

**It does not bite CI, and knowing that is the point.** The volume is attached
only under `isRunMode && !isE2ETests` (`AppHost.cs:155`), and `AspireFixture`
boots with `E2ETests=true` (`AspireFixture.cs:281`). So:

| Lane | Volume | Realm edit takes effect |
|---|---|---|
| `AspireFixture` (integration tests, local and CI) | none | **Yes, every boot** |
| e2e project (`E2ETests=true`) | none | **Yes, every boot** |
| `dotnet run --project src/AppHost` (a developer's stack) | `keycloak-data`, persistent | **No, until the volume is dropped** |

So phase 4a/4b may trust a red/green from the test suites, and **phase 5 must
not trust a manual curl against a long-running local stack.** Before any
hand-verification:

```sh
docker ps -a --filter "name=keycloak" --format "{{.ID}} {{.Names}}"   # stop the AppHost first
docker volume ls | grep -i keycloak
docker volume rm <the keycloak-data volume>
```

Two repository lessons compound here and both must be respected: a persistent
container **keeps its old arguments** until it is `docker rm`'d, and a
**running AppHost holds the service binaries** — stop the stack before
rebuilding or MSB3027 reads as a broken build. And per house practice, ask the
user before stopping a shared stack rather than killing it from a subagent.

**How a stale realm presents:** SC-1 stays red *after* the fix, with the token
still carrying `sse.management`. That is indistinguishable from "the realm edit
was never made" — so the first diagnostic step on a red SC-1 is to decode the
token and look at the scope claim, not to re-read the JSON.

---

## 5. Blast radius of the fixture move (F3) — the argument, then how it is checked

Moving `AspireFixture.ClientId` changes the token that roughly **300**
integration tests carry: `azp` becomes `management-web`, and the `scope` claim
becomes twenty granular entries instead of the bundle.

**The claim: effective authority is identical.** The bundle satisfies
`Scope.All` minus `sse.events.publish`. `management-web` grants `Scope.All`
minus `sse.events.publish`. Same set.

**The one asymmetry**, and it is the only way this could break a test:
`sse.events.publish`. Verified by grep across `src/` — its only occurrences are
the catalogue entry (`Scope.cs:64,127`), the grandfather carve-out
(`RequireScopeExtensions.cs:46`) and `KeycloakScopeBundles.Device:59`. **No
endpoint requires it**; it is enforced on the MQTT side by mosquitto-go-auth
(ADR-0100). And the grandfather clause explicitly *excluded* it, so no test
could ever have been passing on the bundle's account for that scope.

Two further claim changes, both checked:

- **`azp`.** Read in two places: `EventsEndpoints.Writes.cs:282` (matches a
  webhook integration's own client id — never either browser client) and
  `ClaimsPrincipalExtensions.cs:58` → `IsBrowserKiosk()`, which compares
  against `AuthenticationDefaults.KioskClientId`. Neither client is the kiosk,
  so `IsBrowserKiosk()` answers `false` before and after.
  `BrowserKioskPrincipalTests:28` uses the literal `"smart-sentinel-eye-web"`
  as *an example of a non-kiosk azp* — still true, leave it.
- **`sse-identity`, `sse-groups`, `sse-audience`.** All three are default
  scopes of `management-web` as well, so `sub`, the fab `groups` claim and the
  `aud` audience are unchanged. This matters more than the `sse.*` scopes: a
  lost `sse-groups` would break every fab-scoped read (ADR-0114) while every
  scope assertion stayed green. SC-5 asserts `groups` for exactly this reason.

**None of this is taken on trust.** The argument's proof is the full
integration suite green after F3, and it is the reason F3 is not deferrable to
a follow-up: the claim "identical authority" is only checked by running it.

---

## 6. Sequencing, and what phase 4a hands phase 4b

```
4a (test-writer)                          4b (engineer)
──────────────────────────────            ──────────────────────────────
F5  ConsoleScopeGrant…Tests      ──┐
F6  LegacyBundleGrantTests       ──┤
F7  management-identity.spec.ts  ──┼──►   F1 auth.ts
[US2] F10 RequireScopePolicyTests──┘      F2 realm: drop the grant
                                          F3 AspireFixture.ClientId  ⎫ one
                                          F4 RunModeStackAddress     ⎬ block
                                          F8 comment corrections     ⎭
                                          [US2] F9 clause, F11 scope def
```

Phase 4a runs its new tests and returns the **verbatim** output. The expected
red set is **SC-1 only** (plus SC-9 for US2, and SC-7 if the realm still grants
the bundle — it does, so SC-7 is also red on arrival). Everything else must
arrive **green**, and a green SC-2/SC-3/SC-4/SC-8 is the evidence the harness
works. **A red SC-3 at phase 4a means the `management-web` client is
mis-provisioned and the whole approach needs re-examining before any
implementation** — escalate rather than proceed.

Phase 4b may not edit F5, F6, F7 or F10.

---

## 7. Risks

| Risk | Likelihood | Handling |
|---|---|---|
| A stale local Keycloak realm makes SC-1 look unfixed | **High** on a developer machine, **nil** in CI | §4; diagnose from the decoded token, not the file |
| Phase 4b lands a partial (any two of F1/F2/F3) | Medium | §2's table; tasks.md makes them one non-parallel block |
| SC-5 reads the wrong browser storage and fails as "no token" | Medium | §3; 4a determines and records which store the console uses |
| An integration test depended on `azp == "smart-sentinel-eye-web"` | Low — grepped, none does | Full suite is the check |
| A parked PR touches the realm JSON or `AspireFixture.Auth.cs` | Low (one open PR, no overlap) | Rebase after every merge (ADR-0087) |
| US2 turns red somewhere unforeseen | Low | US2 is deferrable by design — ship US1, file US2, say so in the PR |

## 8. What this plan will not do

- Write or amend an ADR (ADR-0144 forbids it in this lane). The per-role
  least-privilege decision the spec flags stays flagged.
- Weaken any gate to reach green — no deleted test, no lowered threshold, no
  new suppression, no narrowed analyzer.
- Touch #2280, #2281 or #2285.
- Delete or disable `smart-sentinel-eye-web`, though after US1 nothing in the
  repository uses it. Recommended as a follow-up issue; not decided here.

## 9. Docs to correct, and one that needs a human

- `.claude/agents/frontend-engineer.md:12` states as current fact that
  *"management-web uses `smart-sentinel-eye-web` with `openid sse.management`
  (the grandfathered bundle)"*. That brief is **load-bearing when no human is
  watching** (CLAUDE.md), so leaving it stale would mis-instruct the next
  frontend agent into re-introducing exactly this defect. **Correct it as part
  of US1** — it is the same class of edit as F8.
- `docs/adr/0080-browser-auth.md`'s code sketch names
  `client_id="smart-sentinel-eye-management"` and
  `scope="openid profile sse.management"` — already divergent from the tree
  before this change (the client has never been called that, and the realm
  exposes no requestable `profile` scope). This spec makes the `scope` line
  divergent in a second way. **Not edited here**: amending an ADR is a human's
  call, and the lane may not do it. **Flagged for the orchestrator to raise**
  — one line in the PR body, or a follow-up issue.
