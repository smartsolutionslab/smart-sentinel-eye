# Spec 072 — A precondition that declares itself

**Issue:** #2088 · **Branch:** `fix/2088-a-precondition-that-declares-itself`
**Phase:** 1 (Specify) · **Date:** 2026-09-05
**ADRs:** ADR-0113 (two-layer optimistic concurrency — the convention this
guards, and the ADR that states the 428/409 pair in so many words), ADR-0119
(the stale-version vocabulary that amends it, and the precedent for guarding a
convention followed by imitation), ADR-0070 (Minimal APIs only — the surface),
ADR-0139 (rules that fail the build, not conventions people remember), ADR-0130
(a record nobody checks against the thing it describes drifts), ADR-0084 (code
metrics), ADR-0052 / ADR-0103 (xUnit + Shouldly; no Docker in the fast lane),
ADR-0065 (coverage gate), ADR-0109 (parallel markers), ADR-0037 (phased
workflow), ADR-0144 (autonomous lane; no ADR is written here).

## The issue as filed, and what survives contact with the repository

> `POST /rules/{name}/publish` and `POST /rules/{name}/archive` … **Neither
> mapping declares 428 in its `Produces` chain.** … Only these two were
> examined … **Every other mutating endpoint using `If-Match` should be audited
> the same way** … The likely answer is that several more are missing it.

Every claim in the issue holds, and its instinct about scope was right. The
population is **17 endpoints, of which 9 are missing the declaration** — the two
in Automation are the smallest fifth of the defect.

**The issue also understates it in a second direction, which the audit found and
the issue could not have.** Three of those nine also fail to declare the `409`
their handler returns when the version *is* sent and is stale. So on three
endpoints the whole ADR-0113 refusal pair — both halves — is absent from the
contract, not just one half.

### The population, measured

Every endpoint that requires `If-Match`, what its `Produces` chain declares, and
what is missing. Measured on `0f20dcd`.

| # | File (mapping) | Verb + route | Handler | Declares 428 | Declares its stale status |
|---:|---|---|---|:--:|:--:|
| 1 | `Automation/Api/RulesEndpoints.cs` | `POST /rules/{name}/publish` | `Publish` | **no** | 409 yes |
| 2 | `Automation/Api/RulesEndpoints.cs` | `POST /rules/{name}/archive` | `Archive` | **no** | **no** (409) |
| 3 | `CameraCatalog/Api/CameraEndpoints.cs` | `PATCH /cameras/{camera}` | `Patch` | yes | 412 yes |
| 4 | `EventIngestion/Api/WebhookIntegrationsEndpoints.cs` | `DELETE /webhook-integrations/{name}` | `Revoke` | yes | 409 yes |
| 5 | `Identity/Api/WebhookRotationEndpoints.cs` | `POST /webhook-integrations/{name}/rotate` | `Rotate` | yes | 412 yes |
| 6 | `LayoutComposition/Api/LayoutEndpoints.cs` | `POST /layouts/{id}/revisions/{n}/publish` | `Publish` | yes | 409 yes |
| 7 | `LayoutComposition/Api/LayoutEndpoints.cs` | `POST /layouts/{id}/revisions/{n}/archive` | `Archive` | yes | 409 yes |
| 8 | `LayoutComposition/Api/LayoutEndpoints.cs` | `POST /layouts/{id}/draft` | `BranchDraft` | yes | 409 yes |
| 9 | `LayoutComposition/Api/LayoutEndpoints.cs` | `PATCH /layouts/{id}/revisions/{n}` | `EditDraft` | yes | 409 yes |
| 10 | `LayoutComposition/Api/LayoutEndpoints.cs` | `POST /layouts/{id}/revisions/{n}/revert` | `Revert` | yes | 409 yes |
| 11 | `OverlayDesigner/Api/OverlayEndpoints.cs` | `POST /overlays/{id}/revisions/{n}/publish` | `Publish` | **no** | 409 yes |
| 12 | `OverlayDesigner/Api/OverlayEndpoints.cs` | `POST /overlays/{id}/revisions/{n}/archive` | `Archive` | **no** | **no** (409) |
| 13 | `OverlayDesigner/Api/OverlayEndpoints.cs` | `POST /overlays/{id}/draft` | `BranchDraft` | **no** | 409 yes |
| 14 | `OverlayDesigner/Api/OverlayEndpoints.cs` | `PATCH /overlays/{id}/revisions/{n}` | `EditDraft` | **no** | 409 yes |
| 15 | `OverlayDesigner/Api/OverlayEndpoints.cs` | `POST /overlays/{id}/revisions/{n}/revert` | `Revert` | **no** | 409 yes |
| 16 | `SystemVariables/Api/SystemVariableEndpoints.cs` | `PUT /system-variables/{name}/value` | `SetValue` | **no** | 409 yes |
| 17 | `SystemVariables/Api/SystemVariableEndpoints.cs` | `POST /system-variables/{name}/archive` | `Archive` | **no** | **no** (409) |
|  | **Totals** | **17 endpoints** |  | **8 yes / 9 missing** | **14 yes / 3 missing** |

**12 declarations are missing in total: nine `428`s and three `409`s.**

The three missing `409`s are the three **archive** endpoints, and they are not a
separate defect — they are the same one. Their handlers return `RULE_STALE`,
`OVERLAY_REVISION_STALE` and `VARIABLE_STALE`, all `HttpStatusCode.Conflict`,
all reachable:

```
src/Automation/Application/Commands/Handlers/ArchiveRuleCommandHandler.cs:38
src/OverlayDesigner/Application/Commands/Handlers/ArchiveRevisionCommandHandler.cs:35
src/SystemVariables/Application/Commands/Handlers/ArchiveVariableCommandHandler.cs:39
```

Each of the three sits beside a sibling in the same file that *does* declare
`409` — `PublishRule`, `PublishOverlayRevision`, `SetSystemVariableValue`. The
authors declared it where they thought about a conflict and forgot it where the
operation felt idempotent. `ArchiveRevision` in LayoutComposition, the one
context that got 428 right everywhere, declares its 409 too.

### Reproduce the counts without reading the table

Git Bash; no `jq`, no `python`, neither of which is on this machine.

```sh
grep -rhoE "ConcurrencyHeaders\.TryRead(ExpectedVersion|UpsertPrecondition)" \
  --include=*.cs src/*/Api | wc -l                                          # 17
grep -rho "Status428PreconditionRequired" --include=*.cs src/*/Api | wc -l  # 8
grep -rho "Status412PreconditionFailed"   --include=*.cs src/*/Api | wc -l  # 2
```

`17 − 8 = 9`. Both 412s are on endpoints in the table (#3 and #5), so no
endpoint declares 412 without requiring `If-Match` either.

### The mirror image: zero

**No endpoint declares 428 without requiring `If-Match`.** All eight
declarations sit on mappings whose handler reads the header — verified by
resolving each of the eight to its handler by name: `CameraEndpoints.Patch`,
`WebhookIntegrationsEndpoints.Revoke`, `WebhookRotationEndpoints.Rotate`, and
LayoutComposition's `Publish` / `Archive` / `BranchDraft` / `EditDraft` /
`Revert`.

The defect runs in one direction only today. The guard must still check both,
because the mirror is what stops a copy-pasted chain from claiming a precondition
the handler never requires — and because a one-directional guard is exactly the
"silent minority" shape this repository has now corrected four times.

## Is 428 right, and is it what the handler returns

**Verified, not taken on the issue's word.**

`src/ServiceDefaults/ConcurrencyHeaders.cs` builds the refusal in one place:

- `Missing()` and `MissingUpsert()` return
  `StatusCodes.Status428PreconditionRequired`, titled `IF_MATCH_REQUIRED`
  (lines 200–209).
- `Malformed(...)` returns `Status400BadRequest`, titled `IF_MATCH_MALFORMED`
  (line 211). A wildcard, a weak tag, a multi-value header or an unparseable tag
  is a **400**, not a 428.

`TryReadExpectedVersion` returns `Missing()` in exactly two cases — no header,
and a header whose single value trims to empty (lines 132–153). Every endpoint
in the table returns that `IResult` unchanged. So **428 is reachable on all 17
and is the status the issue says it is.**

**RFC 6585 §3 is the right code** — "the origin server requires the request to be
conditional" — and ADR-0113 Layer 1 chose it deliberately over falling back to
unconditional writes.

**The 400 half is already declared everywhere it can be reached.** All 17
endpoints declare `Status400BadRequest` (or `ProducesValidationProblem`), so the
malformed-header branch needs no work. Worth stating because it is the one part
of the surface this spec does *not* touch, and a reader tallying
`ConcurrencyHeaders`' three exits should know the third is accounted for.

## Is there an existing `Produces` convention

**Yes, and it is stated in an ADR rather than only imitated.** ADR-0113, Layer 1:

> **A stale version returns `409 Conflict`, not `412 Precondition Failed`.** …
> Note the asymmetry is deliberate: **428 for a missing precondition, 409 for a
> failed one.**

ADR-0119 then amended the second half: CameraCatalog (spec 029) and Identity
spell the failed precondition `412`, and ADR-0119 **leaves both legal** rather
than standardising them, because the `_STALE` code suffix — not the status —
identifies a lost update. So the pair an endpoint owes its callers is:

- **`428`** when `If-Match` is absent — uniform, no variation, all 17.
- **`409` or `412`** when it is present and stale — whichever its own error type
  declares. Fourteen say so; three do not.

`CameraEndpoints.cs:68-70` writes the rule out at the one site that got it fully
right:

```
// 412 and 428 are declared because both are reachable and neither is a
// failure of the request's content: 428 when no If-Match is sent, 412
// when the version quoted is stale (ADR-0113, no retry on conflict).
```

**So the pair is the deliverable, not the 428 alone.** Fixing nine 428s and
leaving three 409s would ship a contract exactly as wrong as before on three
endpoints, and would do it after an audit that had the evidence in hand.

## Does ADR-0113 need amending

**No, and it may not be amended here** (ADR-0144). ADR-0113 already says what
these endpoints return. What it does not say is that the return must appear in
the endpoint's `Produces` chain — and that is not a gap in the decision, it is
the ordinary distinction between a behaviour and its declaration. ADR-0119
already carries the precedent for closing such a gap with an architecture test
rather than a new ADR (`StaleCodeConventionTests`).

**One finding against ADR-0113's own text, recorded not fixed.** Its Layer 1
section says "14 of the 28 mutating endpoints take **no request body**" and
enumerates "publish, archive, branch and revert across Layout and Overlay, three
DELETEs, and two Automation POSTs". The `If-Match`-requiring population is 17,
not 28 — 28 counts every mutating endpoint, body or not, which is what the
sentence is actually about. The figures are consistent; noted because the audit
had to establish that before it could trust either number.

## What this is not

**Out of scope, each for a stated reason:**

- **Any new ADR.** ADR-0144 bars it; ADR-0113 and ADR-0119 already decide
  everything here.
- **The global Layer 2 conflict.**
  `src/ServiceDefaults/Persistence/ConcurrencyConflictExceptionHandler.cs`
  converts EF's `DbUpdateConcurrencyException` into `AGGREGATE_VERSION_CONFLICT`
  (`409`) on **every mutating endpoint in every context**, and — as ADR-0119
  records in so many words — **no context declares it.** That is a larger,
  cleaner defect of the same class covering ~28 endpoints rather than 17, and it
  cannot be fixed by the same edit: it is registered centrally, so the honest
  fix is an endpoint filter or convention, not 28 hand-written lines. **It gets
  its own issue.** This spec's guard is scoped to the *endpoint-level*
  precondition and deliberately does not claim the Layer 2 surface.
- **Changing any status code, route, handler or policy.** Nothing the
  application does at run time moves.
- **The `WithSummary` prose.** Six of the nine under-declaring endpoints also
  fail to mention `If-Match` in their summary. That is #2087's surface, not this
  one; a `Produces` guard that also policed prose would be two guards in a
  trench coat.
- **Requiring `If-Match` anywhere it is not required today.** The population is
  what it is; whether it should be larger is a separate question and a
  behavioural change.

## What the guard provably cannot catch

Stated up front, in the manner `PaginatedConsumerTests`, `AgentBriefClaimTests`
and `EndpointScopeDeclarationTests` state theirs, because a guard whose limits
are discovered later is trusted for more than it does.

- **It is a source scan, not a running application.** It reads the fluent chain
  and the handler body as text. A handler that delegated the header read to a
  shared helper in another type would read as "does not require `If-Match`", and
  the guard would then demand that its 428 be *removed*. **No such indirection
  exists today** — all 17 call sites are lexically inside the mapped handler
  method — but this is the guard's sharpest edge, and FR-006 turns it from a
  silent wrong answer into a loud one.
- **It resolves the handler by method-group name within the declaring class.** A
  route mapped to a lambda, or to a method group qualified by another type, is a
  shape it cannot resolve. There are **zero lambda mappings** in `src/*/Api`
  today. An unresolvable mapping must fail, never pass (FR-009).
- **It checks that 428 is declared, not that it is reachable at run time.** If a
  future filter short-circuited before the handler, the chain would still be
  judged correct.
- **It says nothing about the stale-status half.** The 409-vs-412 choice is
  ADR-0119's, it varies legally by context, and linking a mapping to its command
  handler's error type would be a three-hop inference across the Application
  boundary. **The three missing 409s are fixed by hand in this spec and left
  unguarded** — a limit stated rather than papered over. If they regress,
  nothing catches it. That is the honest residual, and it is smaller than the
  one being closed: 428 is uniform across all 17 and therefore mechanically
  checkable; the stale status is not.
- **It is rooted at `src/*/Api`.** A mapping that leaves those directories is
  not seen; FR-008's pinned counts, not the sweep, is what catches that.

## User stories

### US-1 (P1) — An endpoint that can answer 428 says so, or fails the build

**As** a caller reading the generated OpenAPI, or an agent reading `src/*/Api`,
**I want** the build to fail when an endpoint requires `If-Match` and its
`Produces` chain does not declare 428 (or declares 428 without requiring the
header),
**so that** the contract and the handler cannot disagree silently, as they have
on nine endpoints across three contexts since ADR-0113 landed.

This is the whole shippable slice: the guard, the nine 428 declarations it
forces, and the three 409 declarations the audit found alongside them. It is
independently valuable — the OpenAPI document stops lying on nine routes — and
independently observable (the procedure below), with no behavioural change.

**There is no US-2.** Splitting the guard from the declarations it forces lands a
red build. Splitting the declarations by context lands a guard that is green only
because it excludes two thirds of the endpoints it exists to police.

## Functional requirements

- **FR-001** — The guard enumerates every route-handler mapping (`MapGet`,
  `MapPost`, `MapPut`, `MapPatch`, `MapDelete`) under `src/*/Api/**/*.cs`,
  recording for each its file, line, verb, route template and the **handler
  method-group name** given as the mapping's second argument.

- **FR-002** — For each mapping it resolves that name to a method **within the
  declaring class**, searching every file in the same Api project that declares
  a class of that name. Partial classes split across files are the normal case,
  not the exception: LayoutComposition and OverlayDesigner both map in
  `*Endpoints.cs` and handle in `*Endpoints.Commands.cs`, and Identity has two
  distinct `List` handlers and two distinct `Disable` handlers in different
  classes — resolution by bare name across a project would bind the wrong one.

- **FR-003** — A mapping is **`If-Match`-requiring** when its resolved handler
  body contains a call to `ConcurrencyHeaders.TryReadExpectedVersion` or
  `ConcurrencyHeaders.TryReadUpsertPrecondition`. The body extent is
  brace-matched over comment- and literal-masked source, as
  `HandlerDeconstructionTests.Balanced` already does.

- **FR-004** — Every `If-Match`-requiring mapping must declare
  `StatusCodes.Status428PreconditionRequired` in its own chain. **17 today.**

- **FR-005** — The mirror: a mapping that declares 428 and is **not**
  `If-Match`-requiring fails, with a message distinct from FR-004's. **Zero
  today**, and the requirement exists so it stays zero.

- **FR-006** — The guard asserts its own denominator in both directions: the
  number of `If-Match`-requiring mappings it found equals a repository-wide
  sweep for `ConcurrencyHeaders.TryRead*` under `src/*/Api` (**17**), and the
  number of 428 declarations it found equals a sweep for
  `Status428PreconditionRequired` (**17** after the fix, **8** before). A call
  site in a file the reader misses, or one hoisted out of a handler body into a
  helper, fails the build rather than escaping the sweep. This is
  `PaginatedConsumerTests`' "producers are found, not named" property.

- **FR-007** — Every failure message names the file, the line, the verb and
  route, the handler method, and which side is missing. A message a reader must
  open the file to act on has not done its job.

- **FR-008** — The corpus is pinned exactly: **17 `If-Match`-requiring endpoints
  across 7 files.** Adding, moving or removing one edits those numbers in the
  same diff. Without this, a file moved out of `src/*/Api` shrinks both sides of
  FR-006 at once and the suite stays green while the endpoints go unchecked —
  the failure mode spec 070 demonstrated at review as `Failed: 0, Passed: 59`.

- **FR-009** — **Nothing resolves to a pass by default.** A mapping whose handler
  name cannot be read (a lambda), or resolves to no method, or resolves to more
  than one, fails naming the shape it could not read. The polarity is not a
  detail: a guard that quietly skips what it cannot parse is the guard that was
  not there.

- **FR-010** — **The guard offers no exemption mechanism.** There is no register,
  no attribute, no skip list — unlike spec 070, which needed one for #2070's
  genuinely open work. Nothing here is legitimately exempt: every endpoint that
  can answer 428 can declare it in one line. An escape hatch that nothing needs
  is an escape hatch that will be used.

- **FR-011** — **12 metadata declarations land with the guard**: nine
  `.ProducesProblem(StatusCodes.Status428PreconditionRequired)` (Automation ×2,
  OverlayDesigner ×5, SystemVariables ×2) and three
  `.ProducesProblem(StatusCodes.Status409Conflict)` on the three archive
  endpoints whose `*_STALE` refusal is currently undeclared. No handler body, no
  route, no policy, no status code and no summary changes.

## Acceptance scenarios

### Happy — a surface that agrees with itself

```gherkin
Given every mapping under src/*/Api resolves to a handler the guard can read
  And each handler that calls ConcurrencyHeaders.TryReadExpectedVersion or
      TryReadUpsertPrecondition has 428 in its own Produces chain
  And no mapping declares 428 whose handler reads neither
When the Architecture.Tests suite runs
Then every assertion passes
  And the pinned corpus reports 17 If-Match endpoints across 7 files
```

### Conflict — the handler requires what the chain denies

```gherkin
Given POST /rules/{name}/publish calls ConcurrencyHeaders.TryReadExpectedVersion
  And its Produces chain declares 400, 403, 404 and 409 but not 428
When the guard runs
Then it fails naming RulesEndpoints.cs, the line, POST /rules/{name}/publish,
     the handler Publish, and the TryReadExpectedVersion call site
  And the message states that the generated OpenAPI asserts a status the
      endpoint routinely returns cannot happen
```

### Conflict, mirrored — the chain claims what the handler never requires

```gherkin
Given a mapping declares Status428PreconditionRequired
  And its handler reads neither If-Match helper
When the guard runs
Then it fails with a message distinct from the missing-declaration message
  And that message says a declared precondition no handler enforces tells a
      caller to send a header that will be ignored
```

### Bad request — a shape the guard cannot read

```gherkin
Given a route is mapped to an inline lambda instead of a method group
   Or its handler name resolves to no method, or to two, in the declaring class
When the guard runs
Then it fails naming that mapping and the shapes it can resolve
  And it does not treat the endpoint as either requiring or not requiring If-Match
```

### Auth — the population is unchanged by authorization, and must stay so

```gherkin
Given PATCH /cameras/{camera} resolves the caller's fab before reading If-Match
  And answers 403 or 404 for another fab's camera without ever reaching the header
When the guard runs
Then it still classifies the endpoint as If-Match-requiring
  And the guard makes no claim about the order of the two checks
```

The last one is deliberate and is a limit, not an oversight. `CameraEndpoints`
orders fab resolution *before* the header read on purpose — answering 428 for
another fab's camera would confirm the camera exists (spec 028 FR-006/FR-007).
The guard reads presence, not order, and must not be read as blessing either
ordering.

### No soft edge — the guard cannot be silenced

```gherkin
Given an author adds a mutating endpoint that requires If-Match and omits 428
When they look for a way to reach green without declaring it
Then there is no register, attribute or skip list to add it to
  And the only paths to green are declaring the status or not requiring the header
```

## Independent end-to-end test procedure

Runnable by a reader who trusts none of the above, without Docker and without
booting the Aspire stack.

1. **Establish the population independently.** Run the three `grep` commands in
   *Reproduce the counts* above. Expect **17**, **8**, **2** on an unmodified
   tree; **17**, **17**, **2** after the fix.
2. **Confirm the guard sees the same 17.** Run the suite with FR-006's
   diagnostic output; the enumerated count must equal the grep's 17, and the
   seven files must be the seven in the table.
3. **Break it in the omission direction.** Delete one
   `.ProducesProblem(StatusCodes.Status428PreconditionRequired)` from
   `LayoutEndpoints.cs`. Re-run: exactly one failure, naming that file, line,
   route and handler.
4. **Break it in the mirror direction.** Restore step 3, then add a 428
   declaration to `GET /rules/{name}`. Re-run: exactly one failure, and its
   message must differ from step 3's.
5. **Break it in the unreadable direction.** Restore step 4, then rewrite one
   mapping's handler argument as an inline lambda. Re-run: one FR-009 failure
   naming that mapping — **not** a pass, and **not** a mirror failure.
6. **Confirm the population guard bites.** Restore step 5, then move one
   `TryReadExpectedVersion` call out of its handler into a private helper the
   handler calls. Re-run: FR-006's count must disagree and fail. If it stays
   green, the sweep and the reader are counting the same thing twice and FR-006
   is decorative.
7. **Confirm no runtime behaviour moved.** `git diff src/` must contain only
   added `.ProducesProblem(...)` lines and comments — no `Produces<T>` change,
   no route template, no handler body, no `RequireAuthorization`, no
   `WithSummary`.
8. **Check the twelve landed where they were promised.** Assert that each added
   line sits in the chain of the mapping named in FR-011's table. Serving
   `/openapi/v1.json` would need the service running, and the point of a
   Docker-free procedure is that it does not.

Step 7 is the load-bearing one for phase 5: it is what makes "behaviour-
preserving in the application, corrected in the document" checkable rather than
asserted.

## Phase 4a — how the colour is obtained

**Colour: red**, and the artefact is the guard's own first run.

The brief's framing is right that `Produces` is metadata and nothing the
application does at run time changes — read as *application* behaviour this
change is preserving, and constitution §Testing would then want characterisation
observed green. **But characterisation pins the current behaviour, and the
current declarations are the thing being changed.** A characterisation test over
today's `Produces` chains would encode the nine omissions as the safety net —
precisely the failure CLAUDE.md names for a refactor that is also a bug fix.

So the resolution is the one CLAUDE.md prescribes and the merits agree with:
**the new behaviour is the guard**, it lives in the test suite rather than the
application, and it must be observed failing before any chain is touched.

**The red, exactly.** `test-writer` writes the guard, runs
`dotnet test tests/Architecture.Tests` against unmodified `src/`, and must
observe it **red on FR-004, reporting 9 endpoints across 3 files** — Automation
×2, OverlayDesigner ×5, SystemVariables ×2. Two absences must hold, and they are
the cheapest available check that the guard discriminates rather than failing
everything:

- LayoutComposition's five must **not** appear. They are the same shape and they
  are correct; if they appear, the reader is not binding a mapping in
  `LayoutEndpoints.cs` to its handler in `LayoutEndpoints.Commands.cs`, and the
  guard's central claim is unproven.
- FR-005 must be **green on the first run** — zero mirror offenders — because
  the audit says there are none. A red FR-005 on an unmodified tree means the
  handler resolution is failing open.

That verbatim output is the phase-4 brief and is quoted in the PR body. The
engineer may then add the 12 declarations; it may not edit the guard to pass.

**A green first run is a phase-4 failure**, with a specific diagnosis: either the
sweep matched nothing, or FR-002's resolution silently returned "no handler" for
everything and FR-009 is not wired.

**Docker-free throughout.** `Architecture.Tests` reads files and reflects over
already-referenced assemblies. No Aspire fixture, no Postgres, no RabbitMQ, no
Keycloak — ADR-0103's fast lane.

Separately from the guard's own colour: the neighbouring `Architecture.Tests`
that read these same files — `EndpointScopeDeclarationTests`,
`StaleCodeConventionTests`, `HandlerDeconstructionTests` — are behaviour-
preserving neighbours and must pass **unmodified** afterwards. An assertion in
any of them that has to be edited is evidence something moved that should not
have.

## Latency budget

**N/A.** No leg of the 800 ms event→overlay path is touched. Nothing here runs in
a request path at all: the guard is a build-time source scan, and `Produces`
metadata is read when the OpenAPI document is generated, which happens only in
development and only on request. Constitution §VII's dashboard obligation does
not attach.

## Non-functional

- **Runs in the fast lane.** No Docker, no Aspire fixture, no database
  (ADR-0103). File reads plus regex plus brace matching. Must add well under a
  second to `Architecture.Tests`.
- **Deterministic and platform-neutral.** Paths normalise to `/` before
  comparison or reporting, and `\r` is stripped before matching. A backslash
  literal is green on Windows and red on Linux CI, and this repository has been
  bitten by exactly that.
- **Coverage.** `Architecture.Tests` is a guard project, not a covered assembly;
  ADR-0065's 90/80/90 gates are unaffected. The `src/` edits are single fluent
  lines inside already-covered registration methods and move no coverage figure.
- **Code metrics (ADR-0084).** `S104` (file too long) is in the test projects'
  `NoWarn` list (`Directory.Build.props:108`), so a long guard file is legal.
  That is not a licence: the guard is a new file for the cohesion reason stated
  in the plan, not because a metric forced it.

## Assumptions, marked

1. **`ConcurrencyHeaders.TryRead*` is the complete definition of "requires
   `If-Match`".** Both helpers are the only readers of `request.Headers.IfMatch`
   in the repository; no endpoint reads the header by hand. Verified by grepping
   `IfMatch` across `src/` — the only non-`ConcurrencyHeaders` hits are the
   `ScenarioSimulator` clients that *send* it, and doc comments. If a
   hand-rolled reader is added later, FR-006's sweep does not see it and the
   guard is silent. Recorded as the assumption it is.
2. **`0f20dcd` is the measurement base.** Spec 071 is in flight on another branch
   and does not exist on `origin/develop` at this SHA. If it lands first and
   touches `src/*/Api`, re-measure the three grep figures before phase 4; FR-008's
   pinned counts are what would move.
3. **The branch prefix is `fix/`**, as given in the brief. It suits a change that
   corrects a wrong contract, though the diff is a guard plus metadata. If the
   orchestrator prefers `test/` on spec 068's and 070's precedent, nothing in
   these artefacts depends on it.
