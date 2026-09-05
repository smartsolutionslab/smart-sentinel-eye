# Plan — Spec 070, an endpoint names the scope it needs

**Phase:** 2 (Plan) · **Spec:** `spec.md` · **Issue:** #2087 · **Date:** 2026-09-05

## Shape of the change

One new guard in `tests/Architecture.Tests`, and metadata-only edits to twelve
`*Endpoints.cs` files. **No bounded context is entered.** Nothing is added to any
Domain, Application or Infrastructure project; no aggregate, value object,
entity, command, query, domain event or integration event is created or touched.

That is not an omission in this plan — it is what the change *is*. The sections a
plan normally spends on entities, invariants and the domain→integration event
path are **not applicable**, and saying so explicitly is better than leaving a
reader to wonder whether they were forgotten.

```
tests/Architecture.Tests/
  EndpointScopeDeclarationTests.cs        NEW — the guard

src/AuditObservability/Api/AuditEndpoints.cs              (0 edits — already conformant)
src/Automation/Api/RulesEndpoints.cs                      (+4 summaries)
src/CameraCatalog/Api/CameraEndpoints.cs                  (~1 summary)
src/EventIngestion/Api/EventsEndpoints.cs                 (+3, ~2)
src/EventIngestion/Api/WebhookIntegrationsEndpoints.cs    (~3)
src/Identity/Api/DevicesEndpoints.cs                      (0 — already conformant)
src/Identity/Api/KiosksEndpoints.cs                       (0 — already conformant)
src/Identity/Api/WebhookRotationEndpoints.cs              (0 — already conformant)
src/LayoutComposition/Api/LayoutEndpoints.cs              (+8 summaries)
src/OverlayDesigner/Api/OverlayEndpoints.cs               (+8 summaries)
src/StreamDistribution/Api/StreamEndpoints.cs             (+1, ~3)
src/SystemVariables/Api/SystemVariableEndpoints.cs        (+2 — 3 #2070 GETs untouched)
```

`+` adds a `.WithSummary`; `~` amends one. **26 added, 9 amended, 35 edits across
eight files.** Four files are already fully conformant and appear above only to
record that they were checked — three of them are Identity, which is why #850,
this spec's original issue, was closed as delivered for its label.

## Where the guard lives, and why

`tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`, beside
`KioskScopeParityTests`, `PaginatedConsumerTests` and `AgentBriefClaimTests`.

The project already references **every** `*.Api.csproj` plus `ApiGateway`, so
`SmartSentinelEye.ServiceDefaults.Authorization.Scope` is reachable transitively
with **no new `ProjectReference`**. That matters: the reflection half of the
guard (Register A, below) works out of the box, and a plan that needed to add
references to a project already referencing 38 of them would be a sign the guard
was in the wrong place.

It is a **source scan plus reflection**, not a booted application. The
alternative — build a `WebApplication` per Api project and read the real
`EndpointDataSource` — would be strictly more accurate and is rejected on cost:
each `Program.cs` needs Postgres, RabbitMQ and Keycloak to reach the point where
endpoints are registered, which means the Aspire fixture, which means Docker.
ADR-0103 puts that in `Integration.Tests`, not in the guard suite, and spec
070's whole value is a check that runs on every build in under a second. The
accuracy given up is enumerated in the spec's *What the guard provably cannot
catch*.

### No `FixtureLogic` trait

The guard reads files and reflects over loaded types. It needs no container and
must run in the Docker-free CI selection, which is chosen **by trait**. It
therefore carries no fixture trait at all — the default selection is correct.

## The two registers, both derived

`AgentBriefClaimTests` established the principle this plan inherits: **a guard
that reads its expectations out of a document proves the document was written,
not that the code obeys it.** Both registers here are computed at run time.

### Register A — the scopes that exist

Reflection over `Scope`, walking nested public static classes for
`public const string` fields, producing a map from **constant path** to
**literal**:

```
"Scope.Sse.Cameras.Write"           -> "sse.cameras.write"
"Scope.Sse.Identity.DeviceClients.Read" -> "sse.identity.devices.read"
```

Both the fully-qualified path and its tail-suffixes are indexed, because the
call sites write `Scope.Sse.Cameras.Write` while a `using static` or a shortened
form would write less. Today all 51 scoped endpoints use the full
`Scope.Sse.…` form; indexing suffixes costs nothing and removes a false failure
the first time someone shortens one.

`Scope.All` is **not** used as the register — it is a hand-maintained list in the
same file and would make the guard agree with a list rather than with the
constants the endpoints actually cite.

### Register B — the endpoints that exist

Enumerated from disk: every `*Endpoints.cs` under `src/*/Api/**`, found by glob,
never named. This is `PaginatedConsumerTests`' "producers are found, not named"
property, and FR-010 closes it from the other side by asserting the found count
equals a repository-wide `Map*` count. An endpoint file added to a context the
guard's glob did not anticipate goes **red**, not silent.

## Reading the chain — the crux

A Minimal API registration is a fluent chain over two builder kinds, and the
guard must relate them. Three shapes exist in the repository today and the
parser handles exactly these, failing on anything else (spec FR-002, and the
bad-request scenario).

**Shape 1 — scope on the group, inherited.** The majority.

```csharp
RouteGroupBuilder group = app.MapGroup("/audit")
    .RequireAuthorization(Scope.Sse.Audit.Read)
    .WithTags("Audit");

group.MapGet("/", Search).WithSummary("… Required scope: sse.audit.read")
```

The parser records, per file, each `RouteGroupBuilder <name> = app.MapGroup(...)`
declaration together with the scope argument (or bare `RequireAuthorization()`,
or none) found in the statement's chain. A mapping whose receiver is `<name>`
inherits it.

Two files declare **two groups on the same route prefix** with different scopes —
`RulesEndpoints` (`group` write / `reads` read) and `CameraEndpoints` (`writes` /
`reads`). Binding by **receiver variable name**, not by route prefix, is what
makes those resolve correctly; binding by prefix would silently give six Rules
endpoints the wrong scope and the guard would then demand the wrong sentence.
This is the single most likely way to get the guard subtly wrong.

**Shape 2 — scope on the endpoint, overriding or standing alone.**

```csharp
group.MapPost("/", CreateDraft)
    .RequireAuthorization(Scope.Sse.Layouts.Write)
```

An endpoint-level `RequireAuthorization` wins over the group's. `LayoutEndpoints`
and `OverlayEndpoints` are entirely this shape — their group carries only
`WithTags` — and `SystemVariableEndpoints` mixes both, which is precisely how
#2070 hid: the group's bare `RequireAuthorization()` covers three GETs while the
three writes override it with a real scope, so the file *looks* scoped at a
glance.

**Shape 3 — explicit anonymity.**

```csharp
group.MapPost("/authorize", AuthorizeWhep).AllowAnonymous()
```

Two endpoints. Both are authenticated by a forwarded bearer the handler
validates, and neither says so in metadata today; FR-008 makes them say it.

**Chain extent.** A chain runs from the `Map*` call to the terminating `;` at
statement level. The parser accumulates the statement's full text before
matching, so a `.WithSummary(` split across continuation lines — which is how 11
of the 29 are written, as `"…" + "…"` concatenations — is read as one string.
Adjacent string literals and `+` concatenations are joined; interpolation is
rejected as unreadable rather than guessed at. No summary uses interpolation
today.

## The exemption register

A `static readonly` collection in the guard file, not a document:

```
(GET, "/system-variables",        2070)
(GET, "/system-variables/snapshot", 2070)
(GET, "/system-variables/{name}",   2070)
```

Checked **both ways** (FR-006, FR-007). It lives in the test file for the same
reason `PaginatedConsumerTests`' register does: held in markdown it would
guarantee only that someone had been told.

Fixing #2070 deletes these three rows and the guard then demands the scope
sentence on those three summaries — two of which already exist and describe
everything except the scope. That coupling is intended and is the concrete sense
in which spec 070 makes #2070 harder to leave undone.

## Assertion inventory

| # | FR | Assertion | Shape |
|---|---|---|---|
| A1 | 001, 010 | Every `Map*` under `src/*/Api` is enumerated; count matches the repo-wide sweep | `[Fact]` |
| A2 | 002 | Every mapping resolves to scoped / anonymous / bare / **unreadable→fail** | `[Theory]` per file |
| A3 | 003 | Every scope argument resolves against Register A | `[Theory]` per file |
| A4 | 004 | Every scoped endpoint's summary contains `Required scope: <literal>` | `[Theory]` per file |
| A5 | 005 | No summary names a scope other than the one enforced | `[Theory]` per file |
| A6 | 006 | Every bare-authorization endpoint is in the register | `[Fact]` |
| A7 | 007 | Every register row still matches a bare-authorization endpoint | `[Fact]` |
| A8 | 008 | Every anonymous endpoint's summary contains `No OIDC scope:` | `[Fact]` |

A4 and A5 are separate assertions on purpose. They fail on the same endpoint for
different reasons and the spec requires their messages to differ — omission is a
gap, mismatch is misinformation, and a reader who gets one message for both
learns less than the guard knows.

`[Theory]` per file rather than per endpoint: 56 endpoints across 12 files, and a
per-file failure lists every offending endpoint in that file at once, which is
what an engineer fixing a whole file wants. Per-endpoint theories would produce
35 separate red results on the first run and bury the shape of the problem.

## Failure messages

Following `AgentBriefClaimTests`: name the artefact, quote what was found, state
what was expected, and — for A5 and A6 — say why it matters. FR-009's minimum is
file, line, verb, route, scope enforced, summary text.

```
CameraEndpoints.cs:113  GET /cameras
  enforces : sse.cameras.read
  summary  : "List cameras in your fabs. Omit fabId to span all of them; …"
  expected : the summary to contain "Required scope: sse.cameras.read"
```

## Boundary and convention compliance

- **No cross-context project references** (NetArchTest, `BoundaryTests`). The
  guard adds none; `src/` edits add none.
- **ADR-0070** — Minimal APIs only. Unchanged; the guard reads that style and
  would not parse a controller, which is correct.
- **ADR-0084 metrics** — 300 LOC/file, 30 LOC/method, complexity ≤ 10, depth ≤ 3.
  The chain parser is the risk. Split it into a small `EndpointMapping` record
  plus a reader type in the same file, and if the file still exceeds 300 LOC,
  split the file — never suppress.
- **ADR-0105** — `Ensure.That(...)` for argument guards. Test-internal helpers
  are not a trust boundary and take no guards.
- **Collection expressions** — `List<X> items = [];`, enforced at warning level.
- **`Option<T>`** (ADR-0141) is advisory and scoped to Domain and Application;
  it does not reach this test project. A mapping with no resolvable scope is
  modelled as a failure outcome, not as an absent value.
- **No `Co-Authored-By`** (ADR-0086), overriding any session attribution.

## Risks

1. **The two-groups-per-prefix trap.** Binding by route prefix instead of
   receiver variable produces a guard that is green and wrong on `Rules` and
   `Cameras`. Mitigated by T002's explicit done-when, which requires
   `reads.MapGet("/", List)` in `RulesEndpoints` to resolve to
   `sse.rules.read` and not `sse.rules.write`.
2. **A green first run.** Means the sweep matched nothing. FR-010/A1 exists for
   this and phase 4a treats green-on-first-run as a failure.
3. **Line-ending and separator drift.** Normalise to `/` and strip `\r` before
   any comparison; this repository has had a guard that was green on Windows and
   red on Linux CI.
4. **Scope creep into #2070.** The register is the boundary. An engineer who
   "just adds the missing `RequireAuthorization`" has changed runtime
   authorization behaviour inside a spec that declares itself
   behaviour-preserving in `src/`, and step 6 of the end-to-end procedure will
   catch it in the diff.
5. **Prose churn in 26 new summaries.** These are user-visible API descriptions.
   Write the scope sentence and, where a summary is new, one plain sentence of
   what the endpoint does — do not invent behavioural claims to fill space. A
   wrong description is worse than a terse one, and nothing in the guard checks
   the non-scope half.
