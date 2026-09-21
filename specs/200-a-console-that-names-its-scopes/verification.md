# Verification — Spec 200, a console that names its scopes (#2279)

**Scope shipped: US1 only.** US2 (withdrawing the `sse.management` grandfather
clause from `RequireScopeExtensions.cs`) turned out not to be the two-file
change `tasks.md` scoped it as — see *US2, deferred* below — and is filed as
#2486.

## Red → green

Phase 4a (`test-writer`, commit `a36d833f`) ran against the real Aspire
fixture on unpatched code:

```
Failed A_smart_sentinel_eye_web_token_does_not_carry_the_bundle_and_is_refused (SC-1)
  Shouldly.ShouldAssertException : granted
      should not contain
  "sse.management"
      but was actually
  ["openid", "sse.audit.read", "sse.management"]

Failed The_bundle_is_not_a_default_or_optional_scope_of_any_client (SC-7)
  Shouldly.ShouldAssertException : ScopesNamedBy(client, "defaultClientScopes")
    should not contain
  "sse.management"
    but was actually
  ["sse-identity", "sse-audience", "sse-groups", "sse.management", "sse.audit.read"]
```

Green controls, confirmed already true (SC-2, SC-3, SC-4, SC-8): the same
refused token still reads audit; a `management-web` token is not refused the
same call (confirming `management-web` is correctly provisioned, not an
escalation); that token names its scopes explicitly and not the bundle; an
unauthenticated caller gets 401, never 403.

After phase 4b's fix (commit `83fbda1d`), independently re-verified by the
orchestrator:

- `Architecture.Tests`: **443/443 passed** (SC-7 green — no client's
  default/optional scopes name `sse.management` any more).
- `ServiceDefaults.Tests`: **186/186 passed** — unchanged from before this
  spec, since US2 (which would touch this project) was deferred.
- `management-web` Vitest: **302/302 passed** across 36 files.
- `e2e/management-identity.spec.ts` (SC-5): confirmed via
  `playwright test --list` (parses, one test found), `pnpm run typecheck:e2e`
  (clean) and `eslint --max-warnings 0` on every touched `e2e/` file (clean).
  A live run happens in CI's `e2e` job — this repo's local Aspire stack is
  not booted for e2e by convention.
- `dotnet build -c Release` on the full solution: **0 errors**, 216
  pre-existing advisory SonarAnalyzer warnings, none introduced.

## Integration.Tests — the load-bearing suite, and a real regression it found

`AspireFixture.ClientId` changing from `smart-sentinel-eye-web` to
`management-web` is the change ~300 integration tests' default token carries.
The orchestrator ran the full suite (excluding `Measurement`/`Disruptive`/
`Maintenance`, matching CI's own filter) directly, independently of phase
4b's own report, four times:

1. First three attempts were killed by the sandbox's own OOM protection
   mid-run (not a test or code failure — confirmed via `Get-CimInstance
   Win32_OperatingSystem`: `vmmemWSL` alone holds ~4.2 GB on this shared,
   resource-constrained machine before a second full Aspire stack is added
   on top). Each attempt made substantial, uninterrupted progress (372, then
   192+ pass/fail lines observed) with **no code-level failures** except one:

   ```
   Failed Jwt_mode_rejects_a_token_without_the_events_write_scope [74 ms]
     Shouldly.ShouldAssertException : response.StatusCode
      should be
   HttpStatusCode.Unauthorized
       but was
   HttpStatusCode.Created
   ```

   Root cause: this test isolated "correct `azp`, missing `sse.events.write`"
   by relying on the *old* fixture default client (`smart-sentinel-eye-web`)
   being a different, scope-less client from the one under test
   (`management-web`). Once the fixture's default client became
   `management-web` itself — and `management-web` default-grants
   `sse.events.write` — that isolation collapsed: the token the test called
   "without the scope" now had it, and its `azp` now matched too. Fixed by
   minting from `scenario-simulator` (a real `client_credentials` service
   account that genuinely lacks the scope) instead of the fixture's generic
   default — a stronger isolation than the coincidence it relied on before.
   Verified in isolation: **8/8 passed** in
   `WebhookBearerValidationIntegrationTests`.

2. Fourth attempt (with the fix in place) also hit the same OOM ceiling,
   192 pass/fail lines observed, **zero failures**.

**No full, uninterrupted run of the entire filtered suite completed locally**
on this machine. This is recorded plainly rather than claimed as achieved:
across four attempts covering different, overlapping portions of the ~300
tests, the only defect ever found was the one above, now fixed and verified
in isolation. CI's `integration tests (Docker)` job runs on GitHub's own
runners, not this resource-constrained shared machine, and is the
authoritative full run before merge — per this repo's own convention
(`develop` has no required status checks; the four-bucket CI read before
merging is the gate).

## US2, deferred — filed as #2486

`tasks.md` scoped US2 as two files: delete
`RequireScopeExtensions.LegacyManagementBundle`/`acceptLegacyBundle`, and
remove the `sse.management` scope definition from the realm's
`clientScopes`. Attempted directly during this delivery:

- Deleting `RequireScopeExtensions.LegacyManagementBundle` does not compile —
  `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs:836` references
  it directly in an assertion pinning the WHEP endpoint's OpenAPI summary to
  two named scopes.
- `src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs:31,80`
  has its **own, independent** hand-rolled acceptance of the same bundle
  string — not routed through `RequireScopeExtensions.AddScopePolicies` at
  all, since `POST /streams/authorize` is anonymous at the endpoint-mapping
  level and authorizes itself in the handler.

Completing US2 correctly is therefore at least four files
(`RequireScopeExtensions.cs`, `AuthorizeWhepCommandHandler.cs`, the WHEP
endpoint's OpenAPI summary, `EndpointScopeDeclarationTests.cs`) plus the
realm's `clientScopes` entry plus a documentation pass on two integration
tests (`CrossFabListIntegrationTests.cs`, `CrossFabDisableIntegrationTests.cs`)
whose doc comments explain their setup by citing the grandfather clause.
Re-scoped and filed as #2486 rather than expanded here mid-PR.

The phase-4a red test for the `RequireScopeExtensions.cs` half
(`RequireScopePolicyTests.A_principal_carrying_only_the_legacy_bundle_fails_every_policy`,
replacing the two `Legacy_management_bundle_*` facts) was written, confirmed
red, and then **reverted** along with the T011 attempt — it cannot ship red
in a PR that stops at US1, and belongs with US2's implementation in #2486.
`RequireScopePolicyTests.cs` ships unchanged in this PR (confirmed:
`186/186` passed, matching its pre-spec-200 baseline count).

## Scope-parity claim, independently confirmed

`management-web`'s `defaultClientScopes`
(`src/AppHost/Realms/smart-sentinel-eye-realm.json:150-192`) is the same
broad set of scopes the `sse.management` bundle satisfied via the grandfather
clause, minus `sse.events.publish` — confirmed by direct comparison against
`RequireScopeExtensions.cs`'s bundle-acceptance logic. **This fix does not
shrink what a signed-in operator can reach.** It replaces a blanket bundle
that satisfied every policy with explicit, individually-named grants, so a
future narrowing (role-based least privilege) has something to narrow *from*
— per-role separation is a distinct, ADR-owed decision, flagged in the PR,
not this slice's job.

Latency: **N/A** — authentication/authorization change, not on any
constitution §IV leg.

## Phase 6 — pending

`backend-reviewer` and, per `tasks.md` T018, a **mandatory** (not
conditional) `security-reviewer` pass — this is an authorization change.
