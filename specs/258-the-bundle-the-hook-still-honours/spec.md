# Spec 258 — The bundle the hook still honours

**Issue:** #2486 — *Withdrawing the sse.management grandfather clause (spec 200 US2) also needs AuthorizeWhepCommandHandler's own copy* (**partially delivered here — see *Scope*; the PR must not close it**)
**Branch:** `fix/2486-whep-legacy-bundle-cleanup`
**Parent:** spec 200 (`specs/200-a-console-that-names-its-scopes/`), US2 — its T011 note is where #2486 was filed
**Phase-4a colour:** **RED** (behaviour-changing — a WHEP authorization that answers success today must answer `Forbidden` / 403)
**ADRs:** ADR-0051 (Application stays ASP.NET-free — the reason the handler holds its own copy of the scope strings), ADR-0089 (`ApiError` carries the 403), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane — no ADR written here), ADR-0037 (phases), ADR-0109 (parallel markers)
**Constitution:** §VIII *Safe by Default at Trust Boundaries* ("Authorization is enforced by scope checks at every endpoint"), §Testing (red for new behaviour; a deliberately inverted test is the specification of the change)
**No ADR needed.** This removes a grandfather clause that spec 200 already decided to remove (US2); it makes no new architectural decision.

---

## Problem

`POST /streams/authorize` is MediaMTX's external-auth hook. It is
`AllowAnonymous` at the route, and the handler authorizes the forwarded bearer
itself. Its scope check is hand-rolled and **independent of
`RequireScopeExtensions.AddScopePolicies`**:

`src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs:29-31,79-83`
— **verified on this branch (rebased onto `origin/develop`, b2f09bf7)**:

```csharp
private const string RequiredScope = "sse.streams.read";

private const string LegacyManagementBundle = "sse.management";
…
if (!subject.Scopes.Contains(RequiredScope, StringComparer.Ordinal)
    && !subject.Scopes.Contains(LegacyManagementBundle, StringComparer.Ordinal))
{
    return Failure(AuthorizeWhepFailures.Forbidden());
}
```

So a token whose only `sse.*` scope is `sse.management` — no
`sse.streams.read` — is admitted to watch any stream. After spec 200 US1 no
realm client grants the bundle (guarded by `LegacyBundleGrantTests`), so the
clause is inert today. **Inert is not gone** (spec 200 US2's own words): a
realm edit, a merge or a hand-made production client that re-grants the
bundle silently restores view-everything on the video path.

### Premise check

| Issue claim | Status |
|---|---|
| The handler has its own `LegacyManagementBundle` const and `\|\|` clause (`:31`, `:80`) | **Holds**, verbatim |
| `EndpointScopeDeclarationTests` references `RequireScopeExtensions.LegacyManagementBundle` at compile time and pins the summary to two scopes (`:833-854`) | **Holds** |
| The class doc at `:255-267` describes the WHEP hook as the one place the bundle is named | **Holds** |
| `StreamEndpoints.cs` summary names the bundle | **Holds** (`:72-74`) |
| *Not in the issue:* `AuthorizeWhepCommandHandlerTests` pins the bundle as admitted, and two more of its facts use a bundle-only subject | **Found here** — `:44-63` asserts success for `["openid","sse.management"]`; `:171` builds the Offline-stream case on `["sse.management"]` and would turn red for the wrong reason (`Forbidden` before `StreamUnavailable`) after the fix; `:219` uses the bundle as "the broadest token" |
| *Not in the issue:* `AuthorizeWhepCommand.cs:10` says the handler "checks the `sse.management` scope" | **Found here** — already false since spec 041; becomes doubly false |

---

## Scope — what this slice is, and what it deliberately is not

This slice withdraws the bundle from **the one hand-rolled check**:
items 2, 3 and 4 of #2486's list. It is the smallest vertical that changes an
observable authorization decision end to end (token → handler → 403 on the
hook) and ships alone.

**Explicitly not delivered here, and still outstanding on #2486:**

| #2486 item | Why not here | Is leaving it coherent? |
|---|---|---|
| 1. `RequireScopeExtensions.LegacyManagementBundle` + `AddScopePolicies`' bundle acceptance | Every other `sse.*`-scoped endpoint (54+) is policy-routed through it; withdrawing it is a separate, wider change with its own red test (`RequireScopePolicyTests.A_principal_carrying_only_the_legacy_bundle_fails_every_policy`, spec 200 T004) | **Yes.** Nothing in this slice needs the const gone. The rewritten architecture fact still references it (deliberately — see plan §3), and the class-doc `cref` still resolves. |
| 5. The `sse.management` entry in the realm's `clientScopes` catalogue | Depends on 1 (spec 200 T012) | **Yes.** No client grants it (`LegacyBundleGrantTests`). |
| 6. Stale doc comments in `CrossFabListIntegrationTests.cs` / `CrossFabDisableIntegrationTests.cs` | About the policy grandfather, which this slice leaves in place | **Yes** — their claim about the policy path stays true until item 1 lands. |
| Comment: `AuthenticationDefaults.AdminPolicy` / `ManagementScope` | `[Obsolete]`, wired to no endpoint (issue #844) | **Yes.** Dead code; nothing here reads it. |
| Comment: `RulesEndpoints.cs:22` doc naming `AdminPolicy` | Pre-existing, unrelated to the WHEP hook | **Yes.** |

**The intermediate state this leaves, stated so it is not mistaken for an
oversight:** after this PR, a token carrying only `sse.management` is refused
by the WHEP hook but still passes every policy-routed `sse.*` endpoint except
`sse.events.publish`. That is strictly narrower than today, and no client can
mint such a token (SC-7 of spec 200). The policy half is #2486's remainder.

**Consequence for phase 7:** the PR references #2486 **without a closing
keyword**. The orchestrator comments the remaining items on the issue after
merge (see `tasks.md` T012), so the issue's `agent:ready` card describes only
what is left.

---

## User story

### US1 (P1) — The WHEP hook admits a viewer on the read scope, and on nothing else

*As* the security owner of a fab,
*I want* MediaMTX's auth hook to admit a viewer only when their token carries
`sse.streams.read`,
*so that* re-granting the legacy bundle anywhere cannot silently restore
view-every-stream authority on the video path, and the hook's OpenAPI summary
tells an integrator the one scope it actually checks.

---

## Acceptance scenarios

Handler-level (the hand-rolled decision is the subject; the validator in front
of it is unchanged).

**SC-1 (P1, security — the red) — the bundle alone is refused**

```gherkin
Given a validated WHEP subject whose scopes are exactly ["openid", "sse.management"]
When it asks to read any cam-{guid} path
Then the handler answers AuthorizeWhepError.Forbidden (403)
```

Today: `Success(path)`. This is the red.

**SC-2 (P1, over-correction control) — the console's real token is still admitted**

```gherkin
Given a validated WHEP subject carrying management-web's granular scopes
      (sse.*.read / sse.*.write, including sse.streams.read, and no sse.management)
When it asks to read a path
Then the handler answers Success(path)
```

Green before and after. It is what the deleted "grandfathered token returns
success" fact was *for* ("exactly what a naively-narrowed gate would break") —
restated against the token the console actually holds since spec 200 US1.

**SC-3 (P1, control) — the kiosk read path is unmoved.** The existing
`Authorize_a_read_with_the_read_scope_returns_success` and
`Authorize_with_a_kiosk_token_returns_success` stay green, unmodified.

**SC-4 (bad request / ordering unmoved) — a publish is refused on the action,
whatever the token.** `Authorize_a_publish_…` facts stay green; the publish
refusal precedes the scope check and is untouched.

**SC-5 (auth — 401 unmoved).** Empty or invalid token → `Unauthorized`;
unchanged.

**SC-6 (conflict-shaped — the stream state is checked after the scope, not
instead of it).** An Offline stream with a **read-scope** subject →
`StreamUnavailable`. Green before and after once its subject stops being the
bundle.

**SC-7 (P1, surface — the second red) — the hook's summary names one scope**

```gherkin
Given the POST /streams/authorize mapping's .WithSummary text
Then it contains AuthorizeWhepCommandHandler.RequiredScope ("sse.streams.read")
 And it does not contain RequireScopeExtensions.LegacyManagementBundle ("sse.management")
```

Today: the summary names the bundle. Red.

**SC-8 (system, verification only) — the console and fixture paths still watch video.**
`WhepAuthIntegrationTests` and `WhepHandshakeLatencyTests` stay green: the
`AspireFixture` token is `management-web`'s, which grants `sse.streams.read`
(realm `:179`). No new integration test — see plan §4 for why.

---

## Independent end-to-end test procedure

1. `dotnet test tests/StreamDistribution.Application.Tests --filter "FullyQualifiedName~AuthorizeWhepCommandHandlerTests"`
   on the base commit with the phase-4a tests applied: SC-1 fails with
   *expected Forbidden, got Success*; everything else green.
2. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~EndpointScopeDeclarationTests"`:
   SC-7 fails quoting the summary's `sse.management` clause.
3. Apply phase 4b; both suites green, **no test file edited by 4b**.
4. `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~Whep"`
   (Aspire fixture, Docker required): green — the fixture's `management-web`
   token is admitted, so the change did not take a wall dark.
5. Optional, against a booted stack: with a viewer signed in to
   `management-web`, open a camera; first frame arrives (the handshake still
   answers 200).

---

## Locked tech choices

.NET 10, xUnit + Shouldly (ADR-0052), hand-written fakes (`FakeWhepAuthValidator`),
Aspire fixture for integration (ADR-0103), `ApiError`-based refusals
(ADR-0089). **No new dependency, abstraction, type, scope or endpoint.**

## Latency budget

**N/A.** `POST /streams/authorize` is on the WHEP handshake (click-to-first-frame),
not on any of the six event→overlay legs in constitution §IV. The change
deletes one `Contains` that only ran when the first one had already failed —
nothing on the admitted path gets slower. Not measured, not claimed.

## Success criteria

- SC-1 and SC-7 observed **failing** before phase 4b, output quoted verbatim in the PR (ADR-0139).
- All scenarios green after; 4b edits no test file.
- `Whep` integration tests green (SC-8).
- The PR body lists #2486's remaining items; #2486 stays open.
- No `[NEEDS CLARIFICATION]` remains.

## Assumptions, marked

1. **No caller of the hook relies on the bundle today.** Verified: every realm
   client that browses video (`management-web :179`, `kiosk-web :222`,
   `kiosk-wall :251`) grants `sse.streams.read`; `smart-sentinel-eye-web` no
   longer carries the bundle (spec 200 US1); `LegacyBundleGrantTests` pins
   that no client does.
2. **The rewritten architecture fact may keep a compile reference to
   `RequireScopeExtensions.LegacyManagementBundle`.** Deliberate — it is the
   authoritative spelling while the const exists; when #2486's remainder
   deletes the const, the compile error lands on an assertion whose doc
   comment says to delete it with the const. See plan §3.
