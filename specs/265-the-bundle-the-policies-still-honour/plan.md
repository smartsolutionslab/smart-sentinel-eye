# Plan — Spec 265, the bundle the policies still honour

**Spec:** `specs/265-the-bundle-the-policies-still-honour/spec.md`
**Issue:** #2486 (remainder — this PR closes it) · **Branch:** `fix/2486-legacy-bundle-remainder` · **Phase-4a colour:** RED
**Assignment:** phase 4a `test-writer`; phase 4b **`backend-engineer`** (lead, all of it — see §6 for why not `infra-engineer`).

---

## 1. Shape of the change

| Usual section | This spec |
|---|---|
| Bounded context | **None.** Cross-cutting: `ServiceDefaults.Authorization` + `ServiceDefaults` (shared host defaults), the realm file (AppHost), one doc comment each in LayoutComposition and Automation, one comment in kiosk-web |
| Entities / value objects | **None added or changed** |
| Invariant | Every policy `AddScopePolicies` registers for scope *s* succeeds **iff** the authenticated principal's `scope` claims contain *s*. No other token substitutes. The host registers no policy outside `Scope.All`. |
| Domain → integration event | **None** |
| Persistence / migration | **None** |
| API surface | **No route, status, summary or `Produces` change.** Same 403 (ADR-0089) on the same routes; only *which tokens* reach it changes, and no real token does |
| Boundary rules | Unaffected. `ServiceDefaults` stays referenced by every context and references none; no cross-context reference added. NetArchTest boundary suites untouched |

### Files

| # | File | Phase | Change | Colour |
|---|---|---|---|---|
| F1 | `tests/ServiceDefaults.Tests/Authorization/RequireScopePolicyTests.cs` | 4a | **Replace** `Legacy_management_bundle_passes_a_normal_sse_policy` (`:87-97`) and `Legacy_management_bundle_does_not_pass_the_events_publish_policy` (`:99-109`) with one fact, `A_principal_carrying_only_the_legacy_bundle_fails_every_policy` (SC-9) — see §2 | **red → green** |
| F2 | *new* `tests/ServiceDefaults.Tests/Authorization/RegisteredPolicyTests.cs` | 4a | SC-10 — see §3 | **red → green** |
| F3 | `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs` | 4a | A8b (`:826-859`): delete `string bundle = …LegacyManagementBundle;` and the `summary.ShouldNotContain(bundle, …)` block; delete the doc paragraph `:834-843` (the one that says to do exactly this). Class doc `:255-270`: rewrite — see §4 | characterisation (green → green) |
| F4 | `tests/Architecture.Tests/ScopeGrantTests.cs` | 4a | Prose only: class doc `:22-27`, message `:82-85`, doc `:89-92`, message `:103-106` — past tense for the bundle; the facts and their assertions untouched | characterisation |
| F5 | `tests/Architecture.Tests/LegacyBundleGrantTests.cs` | 4a | Prose only (decision D1): class doc `:6-31` and the message `:45-53` — the bundle is a *withdrawn* scope no code honours; this guard now keeps it withdrawn. Three facts and `Bundle` const untouched | characterisation |
| F6 | `tests/Architecture.Tests/KioskScopeParityTests.cs` | 4a | Prose only: doc `:74-79` ("passes every behavioural check identically…" becomes false). Fact and `ManagementBundle` const untouched | characterisation |
| F7 | `tests/Integration.Tests/EventIngestion/EventTypeRegistryAuthorizationIntegrationTests.cs` | 4a | Prose only: message `:219-221` (drop "or the token picked up sse.management"), comment `:328-331` and message `:334-336` (the bundle no longer satisfies any policy; the absence check is kept as a probe-shape control). Assertions untouched | characterisation |
| F8 | `tests/Integration.Tests/Identity/ConsoleScopeGrantIntegrationTests.cs` | 4a | Prose only: class doc `:12-20` — the grandfather clause it cites no longer exists; SC-1's 403 now holds even if the bundle is re-granted. Assertions untouched | characterisation |
| F9 | `src/ServiceDefaults/Authorization/RequireScopeExtensions.cs` | 4b | Delete the const's `<summary>` + `LegacyManagementBundle` (`:28-36`), `acceptLegacyBundle` (`:46`), the `if (acceptLegacyBundle && …)` block (`:59-62`). The orphaned `<summary>` at `:22-27` then documents `AddScopePolicies` as intended — today two `<summary>` blocks sit on the const | makes F1 green |
| F10 | `src/ServiceDefaults/AuthenticationDefaults.cs` | 4b | Delete `AdminPolicy` + its doc + `#pragma S1133` pair (`:22-32`); delete `.AddPolicy(AdminPolicy, …)` + `#pragma CS0618` pair (`:103-115`) so the chain ends at `.AddScopePolicies(Scope.All);`; delete `ManagementScope` (`:120`) | makes F2 green |
| F11 | `src/Automation/Api/RulesEndpoints.cs` | 4b | Doc `:20-23`: "All writes require `<see cref="AuthenticationDefaults.AdminPolicy"/>`; read endpoints land in PR F…" → writes require `Scope.Sse.Rules.Write`, reads (`GET /`, `GET /{name}`, `POST /{name}/dry-run`) require `Scope.Sse.Rules.Read`. **Same commit as F10** — the `cref` would dangle | doc |
| F12 | `src/AppHost/Realms/smart-sentinel-eye-realm.json` | 4b | Delete the `sse.management` object from `clientScopes` (`:40-48`) and fix the following line's leading brace/comma so the array stays valid JSON | characterisation (SC-14) |
| F13 | `src/LayoutComposition/Infrastructure/Broadcasting/LayoutLifecycleHub.cs` | 4b | Doc `:14-16`: drop "(or the grandfathered `sse.management` bundle)" | doc |
| F14 | `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts` | 4b | Comment `:37`: drop "(or the grandfathered ``sse.management``)" | doc |
| F15 | `.claude/agents/backend-engineer.md:17`, `.claude/agents/security-reviewer.md:17`, `.claude/agents/test-adversary.md:11` | 4b | **Needs user approval at the gate (D4).** backend: drop "(`sse.management` grandfathers the granular `sse.*`)"; security-reviewer: replace the bullet with "a policy accepts exactly its own scope — the `sse.management` bundle was withdrawn (spec 265); a test that passes only because a token holds a broad scope is still the question to ask"; test-adversary: "the `sse.management` grandfather vs a narrow scope" → "a broad-scope token vs a narrow scope" | doc |
| F16 | `specs/200-a-console-that-names-its-scopes/tasks.md:76-95` | 4b | Append to the US2 block: "Delivered by spec 258 (WHEP handler) and spec 265 (the rest)". Nothing else in spec 200 changes | doc |

**F1–F8 are test files and therefore phase-4a work** (ADR-0139 forbids the 4b
engineer editing tests). F3 is *needed* for F9 to compile; F4–F8 are needed so
the suite does not assert falsehoods in its prose after F9–F12. **The 4b diff
contains no test file.**

**Untouched, verified** (spec §*Out*): `CrossFabListIntegrationTests.cs`,
`CrossFabDisableIntegrationTests.cs` (already corrected by spec 200's fix round),
`RealmIdentityTests.cs`, `VariableReadScopeIntegrationTests.cs`,
`KioskPrivilegeSweepTests.cs`, `AuthorizeWhepCommandHandlerTests.cs`,
`StreamEndpoints.cs`, `AuthorizeWhepCommandHandler.cs`, both `auth.ts`,
`auth.test.ts`, the e2e specs, `README.md`, ADR-0080.

---

## 2. F1 — SC-9 in detail

```text
A_principal_carrying_only_the_legacy_bundle_fails_every_policy
  authorization = BuildAuthorizationService()          // existing helper, AddScopePolicies(Scope.All)
  user          = UserWithScopes("sse.management")      // LITERAL — the const is deleted in 4b
  foreach scope in Scope.All:
      result = await authorization.AuthorizeAsync(user, null, scope)
  collect the scopes whose result.Succeeded is true
  passed.ShouldBeEmpty(customMessage: names them, says the bundle was withdrawn — spec 200 US2 / #2486)
```

- **Collect, then assert once**, so the red output names every policy the bundle
  passes today (all of `Scope.All` but `sse.events.publish`) rather than stopping
  at the first — the quoted red is
  then self-evidently "the grandfather clause", not one odd policy.
- **Replaces two facts; do not keep either.** The first asserts the old behaviour
  (inverting it is the specification, spec 200 T004). The second
  (`…does_not_pass_the_events_publish_policy`) is a strict special case of SC-9.
- Doc comment: why the literal (the constant is gone); that this is the policy
  half of #2486, the WHEP half being spec 258's
  `Authorize_with_only_the_legacy_management_bundle_returns_Forbidden`.
- Name reused from spec 200 T004 so the thread from spec → issue → test is
  searchable. The original body was reverted uncommitted (not in `git log -S`);
  write it fresh.

## 3. F2 — SC-10 in detail

New `tests/ServiceDefaults.Tests/Authorization/RegisteredPolicyTests.cs`.

**Build the real registration, not a copy of it** — the `BearerAudienceTests`
pattern (`tests/ServiceDefaults.Tests/BearerAudienceTests.cs:97-99`):
`Host.CreateEmptyApplicationBuilder(null)`, call `AddBearerAuthentication()`, build the provider,
resolve `IAuthorizationPolicyProvider`. The authority URL is never dialled.

```text
The_host_registers_no_admin_policy
  provider = <real registration>.GetRequiredService<IAuthorizationPolicyProvider>()
  (await provider.GetPolicyAsync("admin")).ShouldBeNull(
      "AuthenticationDefaults registers an 'admin' policy requiring sse.management — a scope no
       client holds and no code should honour (#2486). It has been [Obsolete] since #844.")
```

- **`"admin"` literal**, not `AuthenticationDefaults.AdminPolicy` — deleted in 4b.
  The doc says so.
- **Why not assert "every registered policy is in `Scope.All`"?** `AuthorizationOptions`
  keeps its policy map private; enumerating it needs reflection into framework
  internals. Naming the one policy that exists is honest and cannot rot silently —
  if a different stray policy appears later, that is a new defect with its own test.
- **Control in the same file, green before and after:**
  `The_host_still_registers_a_policy_per_catalogued_scope` — for each scope in
  `Scope.All`, `GetPolicyAsync(scope)` is non-null. Without it, SC-10 would also
  pass on a registration that registered nothing (e.g. a broken builder).
- Mandatory red observation: SC-10 red on the base commit (policy is non-null);
  the control green.

## 4. F3 — the architecture test's class doc

`EndpointScopeDeclarationTests.cs:255-270`, the paragraph beginning
*"It cannot see policy composition, and today that is not hypothetical."* becomes,
in substance:

> *It cannot see policy composition.* `RequireAuthorization(scope)` is taken at
> face value as "requires that scope", and since #2486 (spec 265) that is exact:
> `AddScopePolicies` maps each `sse.*` policy onto its own scope and nothing else.
> A future change that let a policy accept a second claim would pass this guard —
> `RequireScopePolicyTests.A_principal_carrying_only_the_legacy_bundle_fails_every_policy`
> is where "only its own scope" is pinned. That is `KioskScopeParityTests`'
> territory, not this one.

The `cref` to `RequireScopeExtensions.LegacyManagementBundle` goes (it would not
resolve). The pinned counts (`:358-360`) do **not** change — no endpoint is added
or removed. **Contention:** open PR #2617 (`feat/2608-event-driven-scene-rotation`)
edits this file at `:353-361` (the counts). Different hunk; if it merges first,
rebase before merging this one.

## 5. Commit plan — each commit builds and is green on its own (ADR-0087)

Rebase-merge lands each commit on `develop`, so red tests never get their own
commit: the red is **observed** in the working tree at T004, quoted in the PR,
and committed together with the code that turns it green (spec 258's precedent:
`6c51e583` carried tests and fix together).

| Commit | Files | Why grouped |
|---|---|---|
| **C1** `fix(service-defaults): stop scope policies accepting the legacy management bundle` | F1, F3, F9 | Compile coupling: F9 deletes the const F1's old facts and F3 reference. SC-9 turns green here |
| **C2** `fix(service-defaults): remove the dead admin policy and its management scope` | F2, F10, F11 | F11's `cref` dangles without F10. SC-10 turns green here. Independent of C1 — either order builds |
| **C3** `fix(realm): drop the sse.management client-scope definition` | F12 | Characterisation only; SC-14's suites pass unmodified. Independent of C1/C2 — either order builds; **after** C1 by convention so the definition outlives the code that honoured it by zero commits, never the other way round |
| **C4** `docs: stop describing the withdrawn bundle as a grant` | F4–F8, F13–F16 | Prose that becomes false at C1. Last, so every sentence it writes is true at the commit that writes it |

Each commit: `dotnet build -c Release` + the touched test projects green.
Commit messages follow ADR-0030, **no `Co-Authored-By` footer** (ADR-0086);
`Refs #2486` on C1–C3, `Closes #2486` in the PR body.

## 6. Engineer assignment

**`backend-engineer` leads and does all of phase 4b.** 14 of 16 files are C#
or C#-adjacent prose. The realm edit (F12) is a single object deletion from a
JSON array with no Aspire wiring, no container, no CI change — the parts of
`infra-engineer`'s brief (AppHost resources, Keycloak runtime, CI) that would
justify handing it over are not in play, and splitting one PR across two
engineers adds an integration step for a nine-line deletion. F14 is a
one-line TypeScript comment; the backend engineer runs `pnpm --filter kiosk-web
lint` and `pnpm format:check` on it rather than pulling in `frontend-engineer`.

**Phase 6 reviewers:** `backend-reviewer` + **`security-reviewer` (mandatory —
an authorization change)**. Ask the security reviewer specifically: is there any
remaining principal (seeded user, service account, runtime-enrolled client,
integration fixture) that only reached some endpoint through the bundle?
`infra-reviewer` is optional (F12 only).

## 7. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| A seeded service account or fixture silently relied on the bundle | Low — static check in spec *Assumptions* §1 | SC-13 on CI's fresh stack; a red there is an **escalation**, not a 4b fix |
| Developer volume still defines `sse.management` after pull; someone reads that as "the realm edit didn't land" | High on dev machines, harmless | Spec step 7; note it in the PR. No client grants it and no code honours it, so behaviour is identical either way |
| JSON left invalid by F12 (trailing comma / brace) | Low | Every realm-reading architecture test parses the file; a parse failure is red in seconds |
| Merge conflict with #2617 on `EndpointScopeDeclarationTests.cs` | Low (different hunk) | §4; rebase after whichever merges first |
| Agent-brief edits (F15) declined | — | Drop T011; the PR states the three briefs are stale and why |

## 8. What this plan will not do

- Delete or disable `smart-sentinel-eye-web` (spec 200 recommended a separate follow-up; still recommended).
- Amend ADR-0080.
- Add a general "every registered policy is catalogued" guard (reflection into framework internals; §3).
- Touch any absence assertion (decision D1).
