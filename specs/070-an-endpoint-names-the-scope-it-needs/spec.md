# Spec 070 — An endpoint names the scope it needs

**Issue:** #850 · **Branch:** `docs/850-an-endpoint-names-the-scope-it-needs`
**Phase:** 1 (Specify) · **Date:** 2026-09-05
**ADRs:** ADR-0070 (Minimal APIs only — the surface this guards), ADR-0139
(rules that fail the build, not conventions people remember), ADR-0130 (a record
nobody checks against the thing it describes drifts), ADR-0007 / ADR-0008
(Keycloak OIDC, the scope catalogue), ADR-0037 (phased workflow), ADR-0052 /
ADR-0103 (xUnit + Shouldly; no Docker in the fast lane), ADR-0065 (coverage
gate), ADR-0109 (parallel markers), ADR-0144 (autonomous lane; no ADR written
here).

## The issue as filed, and what is wrong with it

> **[T092]** Add an OpenAPI summary block on every endpoint annotated with its
> required scope (helps the auto-generated client).

That is the whole text. Three things in it do not survive contact with the
repository, and the spec exists to correct them rather than to execute them.

**1. Its label is finished work.** #850 carries `feature:008-identity`. The
Identity context has **8 endpoints across 3 files**, and **8 of 8** carry a
`.WithSummary` naming the scope they require. Read as its label reads, this
issue is delivered and has been for some time.

**2. Its justification is stale.** "Helps the auto-generated client" names a
consumer that does not exist. There is no NSwag, Kiota, `openapi-typescript`,
Orval or `swagger-codegen` anywhere — not in `Directory.Packages.props`, not in
`package.json`, not in `.github/workflows`, not in `scripts/`. The front-end API
clients under `apps/shared/src/api` are hand-written. Nine services call
`AddOpenApi()` / `MapOpenApi()` and **every one of them does so inside the
development-environment branch**; no Scalar or Swagger UI package is referenced.
The document is produced for a human who asks for it in dev, and for nothing
else. **A summary written to help a generator helps nobody today.**

**3. Its scope is wrong in the other direction.** "Every endpoint" is 56
endpoints across ten contexts, not eight in one. Measured on `70f9223`:

| Context (file) | Endpoints | Has summary | Names its scope |
|---|---:|---:|---:|
| AuditObservability — `AuditEndpoints` | 3 | 3 | 3 |
| Automation — `RulesEndpoints` | 6 | 2 | 2 |
| CameraCatalog — `CameraEndpoints` | 5 | 5 | 4 |
| EventIngestion — `EventsEndpoints` | 5 | 2 | 0 |
| EventIngestion — `WebhookIntegrationsEndpoints` | 3 | 3 | 0 |
| Identity — `DevicesEndpoints` | 3 | 3 | 3 |
| Identity — `KiosksEndpoints` | 3 | 3 | 3 |
| Identity — `WebhookRotationEndpoints` | 2 | 2 | 2 |
| LayoutComposition — `LayoutEndpoints` | 8 | 0 | 0 |
| OverlayDesigner — `OverlayEndpoints` | 8 | 0 | 0 |
| StreamDistribution — `StreamEndpoints` | 4 | 3 | 0 |
| SystemVariables — `SystemVariableEndpoints` | 6 | 3 | 1 |
| **Total** | **56** | **29** | **18** |

Reproduce the three aggregate figures without reading the table (Git Bash; no
`jq`, no `python`, neither of which is on this machine):

```sh
grep -rhE '\.Map(Get|Post|Put|Patch|Delete)\(' --include=*.cs src/ | grep -vc '<code>'  # 56
grep -rho 'WithSummary'      --include=*.cs src/ | wc -l                                # 29
grep -rho 'Required scope:'  --include=*.cs src/ | wc -l                                # 18
```

The `grep -vc '<code>'` excludes one match that is not an endpoint: the
`<code>group.MapPost("/", Create)…</code>` sample inside
`RequireScopeExtensions`' own XML doc.

By authorization, the 56 divide into **51 scoped** through a `Scope.Sse.*`
constant, **2 explicitly anonymous** (`POST /events/webhook/{integrationName}`
and `POST /streams/authorize`, both authenticated by a forwarded bearer instead
of OIDC), and **3 carrying a bare `.RequireAuthorization()` that names no scope
at all** — `GET /system-variables`, `/snapshot` and `/{name}`. Those three are
issue **#2070**, and they are the whole reason this spec is not cosmetic.

The API Gateway is **not** in the 56 and is out of scope: `src/ApiGateway` is
pure YARP (`MapReverseProxy`) and maps no route handlers of its own.

## Why it is still worth doing

The convention this issue asks for **already exists and is already the majority
style in the contexts that have it** — `.WithSummary("… Required scope:
sse.x.y")`, present verbatim 18 times. Nothing enforces it, and the gap that
opened is not a documentation gap.

**#2070 is what unenforced prose costs.** `sse.variables.read` is in the
catalogue (`Scope.cs:48`), is provisioned in the realm and granted to three
clients, and is required by nothing. Two of the three endpoints that should
require it — `GET /system-variables` and `GET /system-variables/{name}` — carry
a `WithSummary` that describes fab filtering and archived rows in detail and
simply never mentions a scope. The prose was written; the omission was
invisible, because there was no place the omission had to show up. The defect
survived until a phase-6 security review of an unrelated PR happened to look.

This is exactly the failure ADR-0130 names and CLAUDE.md has had to correct four
times over — §II twice, the Phase 3 board gate, §IV's leg table. **A record
nobody checks against the thing it describes drifts, and a convention that lives
only in reviewers' memory is such a record.** ADR-0139's answer, and this
repository's stated preference, is a rule that fails the build.

So the work is worth doing, but **not as the issue frames it**. Writing 35 more
sentences of prose reproduces the condition that hid #2070 — a majority
convention with a silent minority. The deliverable is the rule; the prose is
what the rule then forces.

**Adjacent finding, recorded not fixed.** `RequireScopeExtensions.RequireScope`
— the helper its own XML doc presents as the way to do this — has **zero call
sites**. The only reference in the repository is a `<see cref="...">` in
`Scope.cs:7`. Every endpoint calls `.RequireAuthorization(scope)` directly. The
guard must therefore read `RequireAuthorization`, and the dead helper is noted
for `StaleCodeConventionTests`' attention, not removed here.

## Scope

**In:** a build-failing guard over the 56 route-handler mappings in
`src/*/Api/**/*Endpoints.cs`, asserting that an endpoint's OpenAPI summary names
the scope the endpoint actually enforces; and the endpoint-metadata edits that
guard then requires — **26 summaries added, 9 existing summaries amended**.

**Out:**

- **Fixing #2070.** It stays its own issue. This spec makes the gap
  *build-visible* by requiring the three unenforced endpoints to be named in a
  register that cites #2070; it does not add the missing `RequireAuthorization`.
  Closing #2070 will then delete three register rows, and the guard's
  completeness half (FR-007) fails if it does not.
- **Any new ADR.** ADR-0144 bars the autonomous lane from writing one, and
  nothing here needs one: ADR-0139 already states the preference, ADR-0070
  already fixes the endpoint style.
- **Generating a client.** If a generated client is wanted, that is a separate
  issue with its own justification. This spec explicitly declines to revive
  #850's stale one.
- **Serving OpenAPI outside development**, and any Scalar/Swagger UI.
- The API Gateway, `MapDefaultEndpoints`, and health endpoints.
- Descriptions of *behaviour* — this guard is about the scope sentence only. A
  summary may say anything else it likes.

## What the guard provably cannot catch

Stated up front, in the manner `PaginatedConsumerTests` and
`AgentBriefClaimTests` state theirs, because a guard whose limits are discovered
later is trusted for more than it does.

- **It is a source scan, not a running application.** It reads the fluent chain
  in the `*Endpoints.cs` file. An endpoint mapped from a helper method in
  another file, or a scope chosen at run time from configuration, would be
  judged on the wrong text or not at all. **No such indirection exists today** —
  all 56 mappings are literal chains in 12 files, and FR-010 fails the build if
  a mapping appears outside them.
- **It cannot see policy composition.** `RequireAuthorization(scope)` is taken
  at face value as "requires that scope". If `AddScopePolicies` were changed to
  map a policy name onto different claims, the guard would still pass. That is
  `KioskScopeParityTests`' territory, not this one.
- **It checks that the sentence is present and consistent, not that it is
  true in prose.** A summary reading `Required scope: sse.cameras.read` on an
  endpoint enforcing `sse.cameras.read` passes even if the rest of the sentence
  is nonsense.
- **It says nothing about whether the scope is the *right* scope.** An endpoint
  enforcing `sse.cameras.read` for a write is consistent and wrong; only a human
  or a security review catches that.

The residual is deliberate: the guard removes the failure mode that actually
occurred (a scope silently absent from both the chain and the prose) and claims
nothing more.

## User stories

### US-1 (P1) — An endpoint that hides its scope fails the build

**As** a reviewer or an agent reading `src/*/Api`,
**I want** the build to fail when an endpoint enforces a scope its summary does
not name, or enforces no scope without saying so,
**so that** the required-scope catalogue is legible at the surface and a gap
like #2070 cannot open silently again.

This is the whole shippable slice. It is independently valuable — the guard plus
the 35 metadata edits leave the repository strictly more legible with no
behavioural change — and independently observable (FR-011's procedure).

There is no US-2. Splitting the guard from the edits it forces would land a red
build, and splitting the edits by context would land a guard that is green only
because it excludes most of the endpoints it exists to police.

## Functional requirements

- **FR-001** — The guard enumerates every route-handler mapping (`MapGet`,
  `MapPost`, `MapPut`, `MapPatch`, `MapDelete`) in `src/*/Api/**/*Endpoints.cs`,
  recording for each its file, line, HTTP verb and route template.

- **FR-002** — For each mapping it resolves the **effective authorization**: the
  argument of an endpoint-level `.RequireAuthorization(...)` if the chain has
  one, otherwise that of the `MapGroup` chain the mapping's receiver variable
  was assigned from, otherwise `.AllowAnonymous()` if present, otherwise
  *unauthorized* — which fails outright (FR-006).

- **FR-003** — The scope catalogue is **derived by reflection** over
  `SmartSentinelEye.ServiceDefaults.Authorization.Scope`, walking its nested
  types for `public const string` fields, so a constant path such as
  `Scope.Sse.Cameras.Write` resolves to its literal `"sse.cameras.write"`. **No
  scope string is re-typed in the test.** Following `KioskScopeParityTests`,
  which reflects over `KeycloakScopeBundles` for the same reason.

- **FR-004** — A **scoped** endpoint must carry a `.WithSummary` whose
  concatenated text contains `Required scope: <literal>`, where `<literal>` is
  the value FR-003 resolved for the scope FR-002 found. Case-sensitive on the
  scope literal; the label is matched exactly as `Required scope: `.

- **FR-005** — A summary that names a **different** scope than the one enforced
  fails with its own message, distinct from the omission message. This case is
  worse than an omission — the prose actively misinforms — and the failure text
  must say so.

- **FR-006** — An endpoint whose effective authorization is a **bare**
  `.RequireAuthorization()` naming no scope fails, **unless** its route appears
  in the `UnenforcedByDesign` register with an open issue number. The register
  ships containing exactly the three #2070 routes and no others.

- **FR-007** — The register is complete in both directions: a register row that
  does not match an endpoint whose authorization is bare fails. Fixing #2070
  therefore cannot leave a stale exemption behind.

- **FR-008** — An **`.AllowAnonymous()`** endpoint must carry a summary
  containing the literal `No OIDC scope:` followed by what authenticates it
  instead. Silent anonymity is the failure this prevents; both of today's two
  anonymous endpoints are token-authenticated by other means and neither says so
  in its metadata.

- **FR-009** — Every failure message names the file, the line, the verb and
  route, the scope enforced, and what the summary said (or that there was none).
  A message a reader must open the file to act on has not done its job.

- **FR-010** — The guard asserts its own denominator: the count of mappings it
  found equals the count of `Map*` mappings under `src/*/Api`. A mapping added
  in a file the glob misses fails the build rather than escaping the sweep. This
  is `PaginatedConsumerTests`' "producers are found, not named" property.

- **FR-011** — 35 metadata edits land with the guard: **26 summaries added**
  (Rules ×4, Events ×3, Layouts ×8, Overlays ×8, Streams ×1, SystemVariables ×2)
  and **9 amended** to append the scope sentence (Cameras ×1, Events ×2,
  WebhookIntegrations ×3, Streams ×3). No handler body, no route, no policy and
  no `Produces` declaration changes.

  **35, not 38** — the arithmetic is worth showing, because the intuitive figure
  is wrong. 56 endpoints, less the **18** already conformant, less the **3**
  bound by #2070, which this spec registers rather than edits. Of the 35, **33
  are scoped** endpoints gaining or amending the `Required scope:` sentence and
  **2 are the anonymous pair** gaining `No OIDC scope:`. `GET
  /system-variables/snapshot` therefore ends this spec still carrying no summary
  at all: it is one of the three, and writing it one would mean deciding what
  its scope is, which is #2070's job and not this spec's.

## Acceptance scenarios

### Happy — a surface that already agrees with itself

```gherkin
Given every endpoint under src/*/Api enforces a scope from the Scope catalogue
  And each one's WithSummary names exactly that scope
  And the two anonymous endpoints each say what authenticates them instead
  And the UnenforcedByDesign register matches the bare-authorization endpoints exactly
When the Architecture.Tests suite runs
Then every assertion passes
  And no endpoint is reported
```

### Conflict — the prose disagrees with the chain

```gherkin
Given GET /cameras/{camera} enforces Scope.Sse.Cameras.Read
  And its summary reads "... Required scope: sse.cameras.write"
When the guard runs
Then it fails with a message distinct from the omission message
  And the message names CameraEndpoints.cs, the line, GET /cameras/{camera},
      the scope enforced (sse.cameras.read) and the scope claimed (sse.cameras.write)
  And the message states that a summary naming the wrong scope misinforms a
      reader who would otherwise have had to read the chain
```

### Bad request — a shape the guard cannot read

```gherkin
Given a mapping is added whose receiver is a RouteGroupBuilder the guard cannot
      trace to a MapGroup chain in the same file
When the guard runs
Then it fails naming that mapping and the shapes it can resolve
  And it does not silently treat the endpoint as anonymous or as scoped
```

The polarity matters and is the FR-005 lesson generalised: an unreadable chain
resolves to **failure**, never to a pass. A guard that quietly skips what it
cannot parse is the guard that was not there.

### Auth — the #2070 shape, which is the reason this exists

```gherkin
Given GET /system-variables inherits a bare .RequireAuthorization() naming no scope
  And sse.variables.read exists in the catalogue and is granted in the realm
When the guard runs against a register that does not list that route
Then it fails, naming the route and stating that an endpoint enforcing no scope
     must either enforce one or be registered against an open issue
```

```gherkin
Given #2070 is fixed and GET /system-variables enforces Scope.Sse.Variables.Read
  And the UnenforcedByDesign register still lists that route
When the guard runs
Then FR-007 fails, naming the stale row
```

### No soft edge — the register cannot be used to buy silence

```gherkin
Given a new endpoint is added with a bare .RequireAuthorization()
When its author adds it to the UnenforcedByDesign register to reach green
Then the register row requires an issue number
  And the review sees a new exemption in the diff rather than a passing build
```

The register is deliberately a **visible** escape hatch and not a suppression:
it is one file, three rows, each citing #2070, and any addition to it is a
reviewable line. ADR-0144 bars weakening a gate to reach green; an exemption
that must be written down and attributed is the honest form of the residual.

## Independent end-to-end test procedure

Runnable by a reader who trusts none of the above, without Docker and without
booting the Aspire stack.

1. **Establish the denominator independently.** Run the three `grep` commands in
   *The issue as filed* above. Expect 56, 29, 18.
2. **Confirm the guard sees the same 56.** Run the suite with the FR-010
   assertion's diagnostic output; the enumerated count must equal the grep's 56.
3. **Break it in the omission direction.** Delete the ` Required scope:
   sse.audit.read` clause from one `AuditEndpoints` summary. Re-run: exactly one
   failure, naming that file, line and route.
4. **Break it in the mismatch direction.** Restore step 3, then change one
   summary's scope literal to a different valid scope. Re-run: one failure, and
   its message must differ from step 3's.
5. **Break it in the register direction.** Delete one #2070 row from
   `UnenforcedByDesign`. Re-run: one FR-006 failure naming that route. Restore
   it, then add a row for a route that *is* scoped. Re-run: one FR-007 failure.
6. **Confirm no runtime behaviour moved.** `git diff` over `src/` must touch
   only `.WithSummary` arguments and comment lines — no `RequireAuthorization`,
   no route template, no handler, no `Produces`.

Step 6 is the load-bearing one for phase 5: it is what makes the claim
"behaviour-preserving in the application, behaviour-changing in the document"
checkable rather than asserted.

## Phase 4a — how red is obtained

**Colour: red.** Behaviour-changing under constitution §Testing, and the
ambiguity noted in the brief resolves this way on its own terms.

The subtlety is worth stating because it cuts both ways. **Nothing the
application does at run time changes** — a `WithSummary` is metadata; no route,
policy, status code or handler moves. Read as application behaviour this is
behaviour-preserving. But the guard is **new behaviour in the test suite**, and
the generated OpenAPI document — a real, versioned output of this system, served
at `/openapi/v1.json` in development — gains 26 summaries and changes 9.

CLAUDE.md's rule is that ambiguity resolves to red, because that path fails
loudly and the other passes quietly. It resolves that way here on the merits
too: a characterisation test would pin the *current* summaries, which are the
thing being changed.

**So the test to pin is the guard itself, against today's tree.** The
`test-writer` writes the guard first, runs it against `70f9223` unmodified, and
must observe it **red** — reporting **35** endpoints across **8** files. The
three #2070 routes must be **absent** from that report: they are covered by the
register, and their appearing means the register half was not wired. That
verbatim output is the phase-4 brief and is quoted in the PR body. An engineer may then fix the endpoints; it
may not edit the guard to pass.

**A green first run is a phase-4 failure**, and here it has a specific
diagnosis: it means the sweep matched nothing, which FR-010 exists to catch.

Additionally, and separately from the guard's own colour: the six existing
`Architecture.Tests` files that touch these endpoint files are behaviour-
preserving neighbours and must pass **unmodified** afterwards.

## Latency budget

**N/A.** No leg of the 800 ms event→overlay path is touched. Nothing here runs
in a request path at all: the guard is a build-time source scan, and
`WithSummary` metadata is read when the OpenAPI document is generated, which
happens only in development and only on request. Constitution §VII's dashboard
obligation does not attach.

## Non-functional

- **Runs in the fast lane.** No Docker, no Aspire fixture, no database
  (ADR-0103). Pure file reads plus reflection over already-referenced
  assemblies. Must add well under a second to `Architecture.Tests`.
- **Deterministic and platform-neutral.** Paths normalise to `/` before
  comparison or reporting. A backslash literal is green on Windows and red on
  Linux CI, and this repository has been bitten by exactly that.
- **Coverage.** `Architecture.Tests` is a guard project, not a covered
  assembly; ADR-0065's 90/80/90 gates are unaffected. The `src/` edits are
  metadata arguments inside already-covered registration methods and move no
  coverage figure.
- **Code metrics (ADR-0084).** The guard will approach the 300-LOC file limit.
  If it exceeds it, split by concern (chain parsing vs. assertions) rather than
  suppressing — a new suppression to reach green is one of ADR-0144's three
  blocked outcomes.

## Assumptions, marked

1. **#850 is re-scoped rather than closed.** Its label says Identity, which is
   done; its text says every endpoint, which is not. This spec takes the text as
   the intent and the label as an artefact of spec 008's filing. **If the
   maintainer prefers, closing #850 as delivered-for-its-label and filing the
   guard as a new issue is equally honest** — the spec stands unchanged either
   way, only its issue reference moves.
2. **The stale justification is not revived.** No generated client is proposed.
   If one is wanted later, the summaries this spec lands are an input to it, not
   a reason for it.
3. **`Required scope: ` is adopted as the exact marker** because 18 endpoints
   already spell it that way. This is a guess only in the sense that no document
   declares it; the code is unanimous.
4. **The branch prefix is `docs/`**, which suits phases 1–3. Phase 4 lands a
   guard and 12 edited source files; spec 068's precedent was a `test/` branch.
   Flagged for the orchestrator, not decided here.
