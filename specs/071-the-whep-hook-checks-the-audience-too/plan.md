# Implementation Plan: The WHEP hook checks the audience too

**Spec**: `specs/071-the-whep-hook-checks-the-audience-too/spec.md` · **Issue**: #2090
**Branch**: `fix/2090-the-whep-hook-checks-the-audience-too` · **Base**: `origin/develop` @ `0f20dcdd`
**Lane**: the issue carries `agent:ready`, so the autonomous lane is eligible
(ADR-0144) — Declaration 2 is what decides that.

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**`backend-engineer`, alone.**

The whole change is C# inside one bounded context: an initializer in
`StreamDistribution.Infrastructure` and one new xUnit file in
`StreamDistribution.Infrastructure.Tests`, plus one string in
`StreamDistribution.Api`'s endpoint summary.

**Not `infra-engineer`, and the contrast with #91 is the reason.** #91 was
infra because its centre of gravity was `smart-sentinel-eye-realm.json` and the
Aspire-hosted Keycloak that imports it. This one touches **no realm file, no
AppHost wiring, no CI, no container**. FR-010 says so explicitly, and a
diff that touches those files is a signal the plan was misread.

No frontend work. `apps/` is untouched: the browser already sends the token
Keycloak mints, and it already carries the audience (spec, *What breaks*).

### Declaration 2 — is the honest answer a new ADR?

**No, and there is no close call here.**

The architectural decision — *a token names the API it is for, and services
refuse tokens that name another* — was made and recorded by spec 069 under
ADR-0007/0008/0106. This spec applies it to the one endpoint that was outside
the mechanism, because its token arrives in a JSON body instead of a header.
Applying an existing decision to a site it already covered in intent is not a
new decision.

The two supporting choices are equally covered:

- *Reading `AuthenticationDefaults.ApiAudience` from
  `StreamDistribution.Infrastructure`* uses a project reference that already
  exists. **ADR-0051 is not amended** — it governs per-context DI extension
  methods and the ASP.NET-free Application layer, not Infrastructure's use of
  ServiceDefaults, which eight other contexts already make.
- *A test rather than a comment* is **ADR-0139** verbatim: rules that fail the
  build, not the review.

**What would need an ADR, and would therefore block the lane**: moving the WHEP
hook off its hand-rolled validator — a shared `TokenValidationParameters`
factory that all nine APIs and the hook are rebuilt around (spec D2), or
replacing MediaMTX's body-carried token with a header. Neither is in this plan.
If a reviewer wants either, stop and hand back to a human; ADR-0144 bars this
lane from writing an ADR or amending the constitution.

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing → phase 4a colour is RED.**

A token that opened a WHEP stream yesterday and does not name this API is
refused today. That is the feature.

**Two of the three new assertions must be observed failing** (FR-004, FR-005).
The third (FR-006, the over-correction guard) is **expected green on arrival**
and is named as such in `tasks.md` so its colour is not mistaken for the
phase-4a evidence — it exists so a later change cannot buy the refusal by
refusing everyone.

**No characterisation control is declared.** The existing suite must pass
**unmodified** (FR-009, SC-004). If `WhepAuthIntegrationTests`,
`BearerAudienceTests` or anything else goes red, that is a design error in this
plan — block and report, do not edit an assertion.

---

## Architecture

### Bounded context and layers

**StreamDistribution**, two layers, plus its test project.

| Layer | Role in this change |
|---|---|
| `StreamDistribution/Domain` | untouched |
| `StreamDistribution/Application` | untouched — `IWhepAuthValidator` and `WhepAuthSubject` keep their shapes, and `AuthorizeWhepCommandHandler` keeps its scope gate |
| `StreamDistribution/Infrastructure` | `Auth/WhepAuthValidator.cs` — the only behaviour change |
| `StreamDistribution/Api` | `StreamEndpoints.cs` — the endpoint summary sentence (FR-008), no routing or policy change |
| `ServiceDefaults` | **read only** — `AuthenticationDefaults.ApiAudience` is consumed, not modified |

The layering is unusual-looking and correct: token validation is an adapter to
an external identity provider, so it lives in Infrastructure behind the
Application-owned `IWhepAuthValidator` port. That is where it already is.

### Entities, value objects, invariants

**None.** Nothing enters or leaves a domain model. `WhepAuthSubject` is an
Application DTO carrying `sub` and the scope list; it is unchanged, and
constitution §II does not reach it (it is not a domain model, and the values
are wire vocabulary at a trust boundary).

The invariant this change adds is not a domain invariant but a configuration
one, and it is expressed as a test rather than a type:

> The audiences the WHEP hook accepts are exactly the audiences the bearer
> pipeline accepts, and both validate.

### Messaging

**None.** No domain event, no integration event, no Wolverine handler, no
outbox row. `POST /streams/authorize` is a synchronous allow/deny answered to
MediaMTX; it publishes nothing and it is not on the `event → overlay` path.

### Boundary rules

- **No cross-context project reference is added.** StreamDistribution gains
  nothing it does not already have; `ServiceDefaults` is not a bounded context,
  and `BoundaryTests.Context_does_not_reference_other_contexts` does not name
  it.
- **`Shared.Contracts` is untouched.** No message crosses a context here.
- **Application stays ASP.NET-free (ADR-0051).** The `AuthenticationDefaults`
  reference is added in **Infrastructure**, never in Application — the same line
  `KeycloakScopeBundles` documents as forbidden for *its* layer.
- **`InternalsVisibleTo` is reused, not added.**
  `SmartSentinelEye.StreamDistribution.Infrastructure.csproj` already grants
  `SmartSentinelEye.StreamDistribution.Infrastructure.Tests` access (it was
  added for `StreamHealthWatcher.DispatchAsync`, #1801). An `internal static`
  factory needs no new attribute and no new comment justifying one.

---

## The change, exactly

`src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs` — the
constructor's object initializer becomes an `internal static` factory so a test
can build the same parameters without a host, an `IOptions`, or an OIDC
discovery document:

```csharp
internal static TokenValidationParameters CreateParameters(string authority) => new()
{
    ValidateIssuer = true,
    ValidIssuer = authority,
    // The audience arrives on the sse-audience client scope (spec 069). Read
    // from the constant the bearer pipeline reads, so this hook cannot accept
    // a token the nine APIs would refuse; WhepAudienceTests holds the pairing.
    ValidAudiences = [AuthenticationDefaults.ApiAudience],
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    NameClaimType = "preferred_username",
};
```

and the constructor keeps `parameters = CreateParameters(authority);`.

`ValidateAudience` is absent because `true` is the framework default (D4), the
same reasoning `6dac431a` gave for deleting the line in `AuthenticationDefaults`.
`ValidAudiences` (plural), not `Audience` — `options.Audience` seeds only the
singular `ValidAudience`, which is what cost #91 a failing test, and the plural
is the collection `Validators.ValidateAudience` reads.

The class doc-comment's "against the same Keycloak realm as the standard
JwtBearer pipeline" becomes *more* true and stays. The **line comment** that
claimed to mirror the pipeline goes (FR-007): it was the binding that failed.

### Why the test can call this without Docker

`Validators.ValidateAudience(IEnumerable<string>, SecurityToken, TokenValidationParameters)`
is the public static function the bearer handler itself calls, and it needs no
token, no key and no network — spec 069's
`BearerAudienceTests.A_token_minted_for_another_api_is_refused` already uses it
exactly this way. Passing `securityToken: null` is fine and is what that test
does.

The parity half builds the real `JwtBearerOptions` the same way
`BearerAudienceTests.BearerOptions()` does: `Host.CreateEmptyApplicationBuilder`
(empty, so no `appsettings.json` or ambient environment variable can supply the
answer), `Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid"`,
`AddBearerAuthentication()`, then read `IOptionsMonitor<JwtBearerOptions>`.
The authority is never dialled — `AddJwtBearer` fetches metadata on the first
*request*, not at configuration time.

---

## Files touched

### Source (2)

| File | Change |
|---|---|
| `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs` | FR-001, FR-002, FR-003, FR-007 |
| `src/StreamDistribution/Api/StreamEndpoints.cs` | FR-008 — the `AuthorizeWhep` summary sentence only |

### Tests (1 new file)

| File | Covers |
|---|---|
| `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepAudienceTests.cs` | FR-004 (red), FR-005 (red), FR-006 (green on arrival) |

**No project file changes.** Not the two csproj files above, not the test
csproj, not `Directory.Packages.props`.

---

## Risks

### Risk 1 — the transitive framework reference

`WhepAudienceTests` needs `HostApplicationBuilder`, the DI container and
`JwtBearerOptions`. `StreamDistribution.Infrastructure.Tests` references
`StreamDistribution.Infrastructure`, which references `ServiceDefaults`, which
carries `<FrameworkReference Include="Microsoft.AspNetCore.App" />` and the
`Microsoft.AspNetCore.Authentication.JwtBearer` package; all flow transitively
across `ProjectReference`. **If they do not**, the fix is one
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` line in the test
csproj — a build-plumbing line, not a design change, and it does **not** count
as violating "no project file changes" above. Say so in the PR if it happens.

### Risk 2 — a stale Keycloak volume makes the change look like a regression

A Keycloak data volume predating #91's realm import mints tokens with no `aud`.
After this change those tokens 401 at the WHEP hook, and the kiosk shows no
video. That is the change working, not a defect. Phase 5 step 3 deletes the
volume first for exactly this reason — restarting the container is not enough,
the realm is in the volume.

### Risk 3 — Docker is unresponsive on this machine

Phase 5 steps 3-7 cannot run here. **They are not optional evidence, they are
deferred evidence**: state in the PR that they were not run and why, exactly as
`6dac431a` did for `TokenAudienceIntegrationTests`. Do not describe the feature
as verified end to end on the strength of the unit tests alone. `WhepAuthIntegrationTests`
runs in CI against the Aspire fixture (ADR-0103) and is the CI-side proof that
the happy path survived.

### Risk 4 — the parity test is easier to over-credit than it looks

After the fix both sides read one constant, so the *value* comparison in FR-005
cannot fail by construction. It is still worth having: it catches
`ValidateAudience` flipped back on either side, a second audience added to one
side only, and the factory being rewritten to a literal. **Do not describe it in
the PR as proving the two audiences match** — describe it as proving their
shapes do. Spec D3 says the same thing; this repeats it because that is the
sentence a later reader will lift.

### Risk 5 — coverage and metrics gates

`StreamDistribution.Infrastructure` is not under the 90/80/90 coverage gate
(ADR-0065 covers Domain, Application and Shared), so this adds no coverage
pressure. `WhepAuthValidator.cs` is 85 lines and stays well under the 300-line
and 30-line-per-method limits (ADR-0084); the factory is a single expression-
bodied member.

---

## What is deliberately not done

- **No shared `TokenValidationParameters` factory** across ServiceDefaults and
  the hook (spec D2). Rejected for size, not for taste.
- **No unification of the two authority derivations** (spec D5).
- **No change to the `AllowAnonymous` route or the body-carried token.**
- **No source-scanning guard against a third hand-rolled validator** (spec,
  *Out of scope*).
- **No Mosquitto work** (#2085).
- **No touching of #2087's endpoint-summary work** beyond the one sentence
  FR-008 names.
