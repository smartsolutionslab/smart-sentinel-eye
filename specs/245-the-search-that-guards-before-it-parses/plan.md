# Plan 245 — The search that guards before it parses

**Spec:** [`spec.md`](./spec.md) · **Tasks:** [`tasks.md`](./tasks.md)
**Issue:** [#2530](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2530)
**ADRs:** 0139, 0105, 0089, 0036, 0044, 0144

---

## Bounded context and layers

**AuditObservability only.** Two production files, one per layer, one per story:

| Story | Layer | File | Change |
|---|---|---|---|
| US1 | Api | `src/AuditObservability/Api/AuditEndpoints.cs` — `Search` | parse `fabId` before the guard |
| US2 | Application | `src/AuditObservability/Application/Queries/Handlers/SearchAuditQueryHandler.cs` — line 72 | parse claimed fabs per entry, skip the malformed |

No Domain change, no Infrastructure change, no `Shared.Contracts` change, no
`ServiceDefaults` change, no migration, no new message. No cross-context reference
is added (NetArchTest boundaries untouched).

## Entities, value objects, invariants

Nothing new. `FabIdentifier` (AuditObservability's own copy, ADR-0044) keeps its
grammar: 2–32 chars, lowercase ASCII letter first, then lowercase letters, digits
or `-`. The invariant this plan protects is at the boundary, not in the type:

- **I-1** A caller-supplied `fabId` reaches the guard only after it has parsed.
  A malformed one is answered by the input-validation gate (400), never the
  authorization gate (403).
- **I-2** A claimed fab that does not parse grants nothing, and removes nothing
  else. It is neither an error nor a fab.
- **I-3** A claimed fab is never normalised into a valid one. `Munich` is not
  `munich`.

## Messaging

None. Read path only; no domain or integration event is raised or consumed.

---

## US1 — `AuditEndpoints.Search`

Today (lines 76–88):

```csharp
if (!string.IsNullOrWhiteSpace(fabId))
{
    await fabGuard.EnsureAccessAsync(user, fabId, cancellationToken);
}
...
Fab: string.IsNullOrWhiteSpace(fabId) ? null : fabId,
```

Target shape — mirror `GetTimeline` (lines 127–136) exactly:

```csharp
string? namedFab = null;
if (!string.IsNullOrWhiteSpace(fabId))
{
    if (!BoundaryParse.TryParse(
        () => FabIdentifier.From(fabId),
        "AUDIT_INVALID_INPUT",
        out var parsedFab,
        out IResult? fabProblem))
    {
        return fabProblem;
    }

    await fabGuard.EnsureAccessAsync(user, parsedFab.Value, cancellationToken);
    namedFab = parsedFab.Value;
}
...
Fab: namedFab,
```

Ordering, and why:

1. **Blank ⇒ omitted, before anything.** Keeps spec 215 SC-8: `?fabId=` is "no
   filter", 200. Parsing a blank value would turn it into a 400 and break the
   existing test.
2. **Parse, then guard.** The guard needs a well-formed value to authorize
   against (I-1). The 400 describes only the caller's own input and names no
   resource, so answering it before the 403 discloses nothing (spec 215 §"The
   exact ordering").
3. **The query receives the parsed value's `.Value`.** See "Rejected: typing the
   query" below.

The existing comment above the `SearchAuditQuery` construction ("A blank fabId
means…") must be updated to the new shape rather than left describing the old
predicate. One or two sentences on *why* the parse precedes the guard; do not
narrate the sequence, and do not duplicate `GetTimeline`'s comment — reference
it.

`Search` already declares `.ProducesProblem(StatusCodes.Status400BadRequest)`
(line 37), so no mapping metadata changes. `Search` is already over the advisory
S138/S107 limits (14 parameters); those are advisory (ADR-0084) and not this
spec's to fix. If the added block trips a *new* S138 in the Release build, extract
the block into a private static helper in the same file rather than suppressing.

## US2 — `SearchAuditQueryHandler`, line 72

Today:

```csharp
List<FabIdentifier> allowed = [.. callerFabs.Select(FabIdentifier.From)];
source = source.Where(auditEvent => auditEvent.Fab == null || allowed.Contains(auditEvent.Fab));
```

Target: build `allowed` per entry, skipping any candidate whose `FabIdentifier.From`
throws `ArgumentException`. Mirror the loop in
`SystemVariables/Api/SystemVariableEndpoints.cs` `ResolveReadFabsAsync`, and the
`ActorUsername` catch eighteen lines lower in this same handler. The `catch` body
carries a one-line *why* comment (skipped, not failed: the misconfiguration is the
realm's, and one odd group must not cost the caller the fabs they hold — `e9afe03e`).
Catch `ArgumentException` only; nothing broader.

**When every entry is malformed**, `allowed` is empty and the predicate reduces to
`auditEvent.Fab == null` — the same rows the no-membership branch (lines 75–79)
returns. That equivalence is the intended semantics (spec §"What wholly malformed
means here"), and SC-7 asserts it. The engineer may either leave the branch
structure alone (the empty `Contains` already yields it — confirm EF/in-memory
translate an empty list, which the unit test will show) or restructure so the
branch is chosen on `allowed.Count`; **pick whichever keeps the diff smaller**, and
do not introduce a third branch.

**Do not lowercase, trim, or otherwise repair a candidate** (I-3). SC-7's
"munich row is not returned" assertion fails if you do.

The comment block above line 72 (the #1300 cross-fab explanation) stays untouched.

## Line 54 — closed by US1, not by a separate change

After US1, the only producer of `SearchAuditQuery` passes a `Fab` that has already
parsed, so `FabIdentifier.From(fab)` at line 54 cannot throw. No catch is added
there: it would be unreachable error handling (CLAUDE.md "No drive-by error
handling"). The reproduction's line 8 (`/fabs/NOT_A_FAB` + `?fabId=NOT_A_FAB`)
becomes a 400 from the endpoint parse, which T008's harness re-run confirms.

### Rejected: typing the query (`FabIdentifier? Fab`, `IReadOnlyList<FabIdentifier> CallerFabs`)

It would make line 54 unable to throw by construction and matches
`GetResourceTimelineQuery`'s shape. Rejected for this delivery because:

- it forces an edit to `SearchAuditQueryHandlerTests.DefaultQuery` in phase 4b,
  and the lane forbids the engineer editing tests to reach green (ADR-0144);
  the test-writer cannot pre-adapt it in 4a because the type does not exist yet;
- typing `CallerFabs` moves the per-entry skip into the endpoint, where there is
  no unit-test project, and the seeded realm cannot drive it over HTTP — the one
  genuinely new behaviour would lose its red test;
- it gains nothing reachable: US1 already closes line 54.

A follow-up is not filed; if someone wants the query typed, it is a behaviour-
preserving refactor with its own characterisation run.

---

## Boundary rules

- No cross-context reference; `FabIdentifier` is the context's own.
- `ServiceDefaults` unchanged — in particular `IFabAuthorizationGuard.cs`,
  `FabClaims.cs`, `BoundaryParse.cs`.
- `SearchAuditQuery.cs` unchanged (SC-D).
- No new error code: `AUDIT_INVALID_INPUT` reused verbatim.

## Test strategy

| Test | Layer | File | Colour |
|---|---|---|---|
| SC-1 `A_malformed_fab_on_the_audit_search_is_a_client_error_not_an_authorization_refusal` | Integration (Aspire) | `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs` (extend) | **RED** (403 today) |
| SC-6 `A_malformed_claimed_fab_costs_only_itself` | Unit | `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditQueryHandlerTests.cs` (extend) | **RED** (throws today) |
| SC-7 `A_wholly_malformed_claim_set_sees_only_cross_fab_rows` | Unit | same | **RED** (throws today) |
| SC-2/3/4, SC-8 | existing | both files | characterisation, unmodified |

Use the existing helpers — `SeedAsync`/`Row` in the integration file,
`AuditEventBuilder().WithFab(...)`, `TestAuditEventQuerySource`, `DefaultQuery(fab:, callerFabs:)`
in the handler file. Assert status **and** problem `title` on SC-1.

The unit tests (SC-6/7) run without Docker and give the fastest red. SC-1 needs the
Aspire stack; **one machine, one stack** — the orchestrator must confirm no other
worktree is booting one before T004 runs it.

## Verification (phase 5)

Probe table in spec §"Independent end-to-end test procedure" over HTTP on the real
stack, plus the harness re-run below for gap 2 (the seeded realm has no malformed
group). Record both in `verification.md` in this directory.

## Security

Touches the fab-scoping predicate of a read. The change only **narrows** what a
misconfigured claim can reach (fail closed) and moves one refusal from 403 to 400
for input that could never have been authorized. Run the `security-reviewer`
agent in phase 6 anyway — it is the predicate that decides which fab's rows a
caller sees, and "only narrows" is a claim a reviewer should check, not accept.

## Constitution / ADR alignment

- §Testing: red first for new behaviour; characterisation for the rest. ✔
- ADR-0139: malformed input at a trust boundary is a 400, not a 500 or a 403. ✔
- ADR-0105: guard preconditions untouched. ✔
- ADR-0036: two files, no abstraction, no new code. ✔
- ADR-0044: per-context `FabIdentifier`. ✔
- ADR-0141 (advisory `Option<T>`): `namedFab` is a local, not a parameter; not
  in scope.
- §IV latency: N/A.

---

## Appendix — the reproduction harness

Out-of-tree, not committed. Put both files in a scratch directory **outside the
repository** and `dotnet run`. It hosts the real endpoint mapping on Kestrel with a
fake authentication scheme reading the `groups` claim from an `X-Groups` header.
Paths assume the worktree at `D:/Github/sse-2526`; adjust.

`repro2530.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>$(NoWarn);CS0436;CS8618</NoWarn>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="D:/Github/sse-2526/src/AuditObservability/Api/SmartSentinelEye.AuditObservability.Api.csproj" />
    <Compile Include="D:/Github/sse-2526/tests/AuditObservability.Application.Tests/Fakes/TestAsyncQueryable.cs" />
  </ItemGroup>
</Project>
```

`Repro.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Api;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.ServiceDefaults.Authorization;

public static class Repro
{
    public static async Task Main()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("Fake").AddScheme<AuthenticationSchemeOptions, FakeAuth>("Fake", _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy(Scope.Sse.Audit.Read, p => p.RequireAuthenticatedUser()));
        builder.Services.AddExceptionHandler<FabAuthorizationExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<IFabAuthorizationGuard, DefaultFabAuthorizationGuard>();
        builder.Services.AddSingleton<IAuditEventQuerySource>(new TestAuditEventQuerySource([]));
        builder.Services.AddSingleton<SearchAuditQueryHandler>();
        builder.Services.AddSingleton<GetResourceTimelineQueryHandler>();
        builder.Services.AddSingleton<GetAuditEventQueryHandler>();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAuditEndpoints();
        await app.StartAsync();
        string addr = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using HttpClient http = new() { BaseAddress = new Uri(addr) };
        string id = Guid.NewGuid().ToString();
        (string groups, string path)[] probes =
        [
            ("/fabs/munich", $"/audit/overlay/{id}?fabId=NOT_A_FAB"),
            ("/fabs/munich", "/audit?fabId=NOT_A_FAB"),
            ("/fabs/munich", "/audit?fabId=munich"),
            ("/fabs/munich", "/audit?fabId=berlin"),
            ("/fabs/munich", "/audit"),
            ("/fabs/Munich", "/audit"),
            ("/fabs/munich /fabs/Munich", "/audit"),
            ("/fabs/NOT_A_FAB", "/audit?fabId=NOT_A_FAB"),
        ];
        foreach (var (groups, path) in probes)
        {
            using HttpRequestMessage req = new(HttpMethod.Get, path);
            req.Headers.Add("X-Groups", groups);
            HttpResponseMessage resp = await http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            string title = System.Text.RegularExpressions.Regex.Match(body, "\"title\":\"([^\"]*)\"").Groups[1].Value;
            Console.WriteLine($"groups=[{groups}]  GET {path}  -> {(int)resp.StatusCode} {title}");
        }
        await app.StopAsync();
    }
}

public sealed class FakeAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        ClaimsIdentity identity = new([new Claim("groups", Request.Headers["X-Groups"].ToString())], "Fake");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Fake")));
    }
}
```

`dotnet run 2>&1 | grep -E "^groups="` gives the eight-line table. Expected after
the fix: 400, **400**, 200, 403, 200, **200**, **200**, **400**.
