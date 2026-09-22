# Spec 215 — The 400 an empty fab never got

**Issue:** [#2507](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2507)
**Branch:** `2507-audit-500-on-empty-fabid`
**Status:** Phase 1 complete — awaiting review
**ADRs:** ADR-0139 (rules that fail the build), ADR-0105 (`Ensure.That` guards),
ADR-0089 (`ApiError` with HTTP status), ADR-0047 (`Result<T, Error>`),
ADR-0114 (fab inferred for single-fab operators), ADR-0036 (smallest change),
ADR-0044 (per-context value objects)
**Precedent spec:** 091 (a malformed route value declares its 400) — same defect
class, a scope that could not reach this instance

---

## Problem

`GET /audit/{resourceKind}/{resourceIdentifier}?fabId=` — the parameter present
but empty — answers **500**. The caller supplied malformed input; the system
reported a server fault.

The cause is an ordering inversion, not a missing check. `AuditEndpoints.GetTimeline`
runs the fab **authorization guard** before it parses the fab, and the guard's own
argument precondition (`Ensure.That(fabId).IsNotNullOrWhiteSpace()`, ADR-0105)
throws `ArgumentException` on the empty string. Nothing catches it. The parse that
*would* have produced a clean `400 AUDIT_INVALID_INPUT` sits ten lines further
down and is never reached.

**Correction, made after T004's red-test pass questioned it and it was re-derived from source rather than re-asserted:** the guard call at `GetTimeline`'s first line runs on the *raw, unparsed* `fabId` string, unconditionally, for every request — the try/catch that parses `FabIdentifier.From` sits entirely *after* it. `DefaultFabAuthorizationGuard.EnsureAccessAsync` does a plain string-equality check against the caller's `groups` claim; it never calls `FabIdentifier.From` and has no grammar opinion at all. So a malformed fab is *not* reaching the parse today — it's being refused by the guard, for the wrong reason, before the parse is ever consulted. The asymmetry, corrected:

| Request | Today |
|---|---|
| `?fabId` **omitted entirely** | **400** — ASP.NET's own required-parameter refusal (never reaches the guard: no value to bind) |
| `?fabId=` **present and empty** | **500** — uncaught `ArgumentException`, thrown by the guard's own `Ensure.That(fabId).IsNotNullOrWhiteSpace()` |
| `?fabId=%20` (whitespace) | **500** — same throw |
| `?fabId=NOT_A_FAB` (malformed grammar) | **403** `RESOURCE_FAB_NOT_AUTHORIZED` — the guard's string-equality check against `/fabs/NOT_A_FAB` fails before grammar is ever checked; the parse that would answer 400 is never reached |
| `?fabId=berlin` (valid grammar, fab not held) | **403** `RESOURCE_FAB_NOT_AUTHORIZED` — same guard rejection, for the reason it's actually meant to catch |

Omitting the parameter is handled. Supplying it as *empty* is not (the 500 this
issue reports). **Supplying it as syntactically malformed garbage is *also* not
correctly handled today** — it happens to produce a plausible-looking 403 rather
than a 500, which is why this went unnoticed, but it is answered by the
authorization boundary rather than the input-validation one. The fix (reordering
parse-then-guard) corrects both: an empty fab now reaches the parse and gets 400,
and a malformed-grammar fab now *also* reaches the parse first and gets 400 —
`RESOURCE_FAB_NOT_AUTHORIZED` becomes reachable only for a fab that is
syntactically well-formed but genuinely not held by the caller (`berlin`, in this
table). SC-6 is therefore a fourth **red** case, not a characterisation case —
see the correction in §Acceptance scenarios below.

This is not a security hole: a 500 here discloses nothing an authorized 400 would
not. It is a trust-boundary input-validation gap of the class ADR-0139 exists to
close, and constitution §Testing's "validate at trust boundaries" names.

---

## Premise check — the issue's claims, re-verified at HEAD (`d974d352`)

Every claim below was read at HEAD, not taken from the issue.

### Claim 1 — the throw site is where the issue says it is. **Confirmed.**

`src/ServiceDefaults/Authorization/IFabAuthorizationGuard.cs:53-56`

```csharp
public Task EnsureAccessAsync(ClaimsPrincipal user, string fabId, CancellationToken cancellationToken)
{
    Ensure.That(user).IsNotNull();
    Ensure.That(fabId).IsNotNullOrWhiteSpace();   // ← throws, uncaught
```

The guard's only *designed* failure channel is `FabAuthorizationException`, which
`FabAuthorizationExceptionHandler` maps to `403 RESOURCE_FAB_NOT_AUTHORIZED`. An
`ArgumentException` from the precondition matches no handler and falls through to
the framework's 500.

### Claim 2 — an empty `fabId` really does reach the guard unvalidated. **Confirmed.**

`src/AuditObservability/Api/AuditEndpoints.cs:100-133`. The guard call is the
handler's **first statement**; the parse is in a `try/catch` twelve lines below it:

```csharp
await fabGuard.EnsureAccessAsync(user, fabId, cancellationToken);   // line 113

// ...
    parsedResourceIdentifier = ResourceIdentifier.From(resourceIdentifier);
    parsedFab = FabIdentifier.From(fabId);                          // line 126
}
catch (ArgumentException ex)
{
    return Results.Problem(title: "AUDIT_INVALID_INPUT", ...);      // line 131
}
```

`FabIdentifier.From` (`src/AuditObservability/Domain/AuditEvent/FabIdentifier.cs:65`)
opens with the same `IsNotNullOrWhiteSpace` — so the *correct* refusal for an empty
fab already exists in this file, already carries the right code, and is simply
sequenced behind the throw that pre-empts it.

### Claim 3 — the sibling 400s exist and are produced elsewhere. **Confirmed, with a correction.**

`AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND`, `AUDIT_TIMELINE_INVALID_CURSOR` and
`AUDIT_TIMELINE_PAGE_SIZE_OUT_OF_RANGE` live in
`src/AuditObservability/Application/Queries/GetResourceTimelineErrors.cs` as
`GetResourceTimelineError` variants (ADR-0089/ADR-0093) and are produced by the
**query handler**.

**The correction matters for the fix.** The handler receives an already-parsed
`FabIdentifier` and can never see a malformed fab, so a fourth variant there would
be unreachable. The endpoint's *own* parse failures use a hand-written
`Results.Problem(title: "AUDIT_INVALID_INPUT", …)` — the shape spec 091 catalogued
as "Shape A" and found at five other sites across three contexts.

**So no new error code is needed.** `AUDIT_INVALID_INPUT` is already this
endpoint's answer for malformed caller input, already declared
(`.ProducesProblem(400)` at line 45), and already what a malformed-*grammar*
`fabId` receives today. An empty `fabId` should receive the same code as a
malformed one, because it *is* one. Adding `AUDIT_TIMELINE_FAB_REQUIRED` would
split one failure across two codes for no caller benefit.

### Claim 4 — a second handler in the same file has the identical gap. **New finding; the issue does not mention it.**

`AuditEndpoints.Search` (`GET /audit`), line 76:

```csharp
if (fabId is not null)
{
    await fabGuard.EnsureAccessAsync(user, fabId, cancellationToken);
}
```

`[FromQuery] string? fabId` binds `?fabId=` to `""`, which **is not null**. So
`GET /audit?fabId=` reaches the same `Ensure.That` and 500s identically. The
issue was filed from one probe; the same defect sits eight lines above it.

Its correct answer is **not** the same as the timeline's, and §"The scope
decision" below says why.

### Claim 5 — the scope question: do other guard call sites share the gap? **Answered: no. This context is the sole outlier.**

Twelve call sites of `EnsureAccessAsync` across nine contexts were read
individually. Every one falls into one of three groups:

| Group | Sites | Why it cannot 500 |
|---|---|---|
| **Routed through `FabResolution`** | Automation, SystemVariables, CameraCatalog, LayoutComposition, StreamDistribution, EventIngestion (via `EventIngestionFabResolution`), Identity's three `List` endpoints (via `IdentityFabResolution`) | `FabResolution.ResolveForReadAsync`/`ResolveForWriteAsync` (`FabResolution.cs:60, 103`) call the guard **only inside** `if (!string.IsNullOrWhiteSpace(fabId))`. An empty fab is treated as *omitted* and takes the ADR-0114 inference branch. |
| **Parses the fab first** | `Identity/DevicesEndpoints.cs:134, 214`; `Identity/KiosksEndpoints.cs:136, 210`; `Identity/WebhookRotationEndpoints.cs:128` | `FabIdentifier.From(fabId)` in a `try/catch` → `*_INVALID_INPUT` 400, then `EnsureAccessAsync(user, fab.Value, …)` with a value that cannot be empty. |
| **Guard first, parse second** | `AuditObservability/Api/AuditEndpoints.cs:78, 113` | **The defect.** |

`AuditEndpoints.cs:183` (`GetSingle`) also calls the guard with a raw string, but
`row.Fab` comes from the database, not the caller — it is not a trust boundary,
and a row whose persisted fab is whitespace is a data defect this spec does not
invent a code for. Out of scope, recorded so the next reader does not re-find it.

### Why spec 091's survey did not catch this

Spec 091 enumerated handlers that refuse **a route value** — path segments — and
found six. `fabId` is a **query** parameter. The survey was not wrong; its
antecedent could not reach this instance. Recorded because this repository has
twice had to correct a record that was accurate when written (constitution §IV's
leg table, the Phase 3 board gate), and a survey's *scope* is the same kind of
record.

---

## The scope decision

**One context, two handlers, at the endpoint boundary. Not the guard, not repo-wide.**

Three candidate locations were considered:

**(a) Soften the guard's own precondition** — make `DefaultFabAuthorizationGuard`
treat an empty fab as a refusal rather than a programming error. **Rejected.** The
guard returns `Task`; it has no vocabulary for a 400, and its one exception type is
mapped to 403. Turning an empty fab into a 403 would tell a caller they lack
access to a fab they never named. Turning the `Ensure.That` into a silent pass
would delete a precondition that twelve correct call sites satisfy, so a future
site that *does* pass an empty fab would fail silently instead of loudly — the
exact inversion ADR-0139 argues against.

**(b) Catch `ArgumentException` around the guard call** — defensive, local, small.
**Rejected.** It converts a symptom without correcting the ordering, and it would
also swallow an `ArgumentException` raised for any *other* reason inside the guard.
Constitution §"No drive-by error handling" and the swallowed-exception review block
both point away from it.

**(c) Parse the fab before the guard is called** — **chosen.** It is what eleven of
the twelve other call sites already do, it reuses an error code already on this
endpoint, and it deletes no guard and adds no abstraction (ADR-0036). The guard
keeps its precondition and keeps receiving a value that has already been proven
well-formed.

### The exact ordering, and why the fab goes first

For `GetTimeline` the three steps must sit in this order:

1. **Parse `fabId`** → `400 AUDIT_INVALID_INPUT` on empty, whitespace, or
   malformed grammar.
2. **Guard on the parsed fab** → `403 RESOURCE_FAB_NOT_AUTHORIZED`.
3. **Parse `resourceIdentifier`** → `400 AUDIT_INVALID_INPUT`.

Step 3 stays *after* step 2 deliberately. `DevicesEndpoints.Disable` and
`KiosksEndpoints.Disable` carry this reasoning verbatim in a comment — "Fab first
— before clientId is parsed or looked up. Answering 400 or 404 for another fab's
device would confirm that device exists". Moving the resource parse ahead of the
guard would hand an unauthorized caller a 400 that distinguishes a well-formed
resource identifier from a malformed one. Today's code already answers 403 first
for that case, and this spec must not regress it.

Parsing the *fab* ahead of the guard leaks nothing: the 400 describes the caller's
own input grammar and names no resource. `WebhookRotationEndpoints.Rotate` states
the same ordering and the same reason.

### `Search` is the opposite answer, and the difference is the signature

| Handler | `fabId` declared | Empty means | Answer |
|---|---|---|---|
| `GetTimeline` | `[FromQuery] string fabId` — **required** | a required value was supplied badly | **400** `AUDIT_INVALID_INPUT` |
| `Search` | `[FromQuery] string? fabId` — **optional** | *not narrowed to a fab* | **200**, scoped to the caller's own fabs |

`Search` already spans the caller's fab membership when `fabId` is absent
(`CallerFabs: callerFabs`). Every other optional-fab read in the repository treats
empty-or-whitespace as absent — that is literally
`FabResolution.ResolveForReadAsync`'s first branch, and `EventsEndpoints.Reads`
passes `fabId ?? string.Empty` on the strength of it. Answering 400 for an empty
optional parameter would make `GET /audit?fabId=` behave differently from
`GET /events?fabId=` for no stated reason.

So `Search`'s fix is one predicate: `if (fabId is not null)` becomes
`if (!string.IsNullOrWhiteSpace(fabId))`. It is a one-token change that makes an
optional parameter behave the way the rest of the system already defines
"optional".

---

## Locked tech choices

Nothing new is introduced. The fix is composed entirely of patterns already in
this file or its neighbours.

| Concern | Choice | Source |
|---|---|---|
| Endpoint-boundary parse failure | `try { FabIdentifier.From(...) } catch (ArgumentException ex)` → `Results.Problem(title: "AUDIT_INVALID_INPUT", detail: ex.Message, 400)` | `AuditEndpoints.cs:123-133`, spec 091 "Shape A" |
| Error code | **`AUDIT_INVALID_INPUT`** — existing, already declared | `AuditEndpoints.cs:131, 166` |
| Fab authorization | `IFabAuthorizationGuard.EnsureAccessAsync` — **unchanged** | ADR-0105, spec 008 FR-019 |
| Optional-fab semantics | whitespace ⇒ omitted | `FabResolution.cs:103`, ADR-0114 |
| Value object | `AuditObservability.Domain.AuditEvent.FabIdentifier` — per-context, unchanged | ADR-0044 |
| Tests | xUnit + Shouldly; endpoint behaviour via `AspireFixture` | ADR-0052, ADR-0103 |
| Test naming | sentence-style with underscores | ADR-0053 |

**No new file is created in `src/`.** No ADR is needed: every decision here is an
application of ADR-0036, ADR-0105 and ADR-0139 to one file.

---

## User stories

### US1 (P1) — an operator's empty fab filter gets a client error, not a server error

*As an operator (or a UI that renders an unset fab selector as `?fabId=`), when I
ask for a resource's audit timeline without actually naming a fab, I am told my
request was malformed — not that the server broke.*

Independently shippable and independently observable: one HTTP call, one status
code, one error title. This is the whole of the issue's report.

### US2 (P2) — the same input on the audit search behaves like every other optional fab filter

*As an operator whose client sends `?fabId=` for "no fab filter", the
cross-cutting audit search returns my own fabs' rows rather than a 500.*

Separable from US1 — a different handler, a different signature, a different
correct answer — but the same probe finds it and the same commit is the honest
place to fix it, because shipping US1 alone leaves a 500 eight lines away.

---

## Acceptance scenarios

### The observation — SC-1 is the reason this spec exists

**SC-1 (US1) — empty required fab is a client error**

```gherkin
Given an operator "admin@munich.test" holding /fabs/munich
  And a valid overlay identifier
When they GET /audit/overlay/{overlayIdentifier}?fabId=
Then the response status is 400
  And the problem "title" is "AUDIT_INVALID_INPUT"
  And the response is not 500
```

### The controls — each rules out a way SC-1 could be lying

**SC-2 (US1) — whitespace is the same failure, not a separate hole**

```gherkin
Given the same operator
When they GET /audit/overlay/{overlayIdentifier}?fabId=%20
Then the response status is 400
  And the problem "title" is "AUDIT_INVALID_INPUT"
```

*Without this, a fix keyed on `string.Empty` alone would pass SC-1 and leave the
whitespace case at 500 — and `IsNotNullOrWhiteSpace` is what actually throws.*

**SC-3 (US1) — the happy path is unchanged**

```gherkin
Given the same operator
When they GET /audit/overlay/{overlayIdentifier}?fabId=munich
Then the response status is 200
  And the body has a "rows" array
```

*Without this, deleting the guard call entirely would pass SC-1 and SC-2.*

**SC-4 (US1, auth) — a fab the caller does not hold is still 403, and still 403 first**

```gherkin
Given the same operator, who does not hold /fabs/berlin
When they GET /audit/overlay/{overlayIdentifier}?fabId=berlin
Then the response status is 403
  And the problem "title" is "RESOURCE_FAB_NOT_AUTHORIZED"
```

*This is the existing assertion in `CrossFabReadGuardIntegrationTests`. It must
survive unmodified — a reordering that trades a 403 for a 400 has moved the fab
boundary, which is the one thing this fix may not do.*

**SC-5 (US1, auth + bad request together) — the guard still wins over the resource parse**

```gherkin
Given the same operator, who does not hold /fabs/berlin
When they GET /audit/overlay/{a malformed resourceIdentifier}?fabId=berlin
Then the response status is 403
  And the problem "title" is "RESOURCE_FAB_NOT_AUTHORIZED"
  And the response is not 400
```

*This pins the ordering decision. It is the scenario that fails if step 3 is moved
ahead of step 2 while "fixing" the ordering.*

**SC-6 (US1, bad request) — a malformed fab grammar gets the answer it should have had**

```gherkin
Given the same operator
When they GET /audit/overlay/{overlayIdentifier}?fabId=NOT_A_FAB
Then the response status is 400
  And the problem "title" is "AUDIT_INVALID_INPUT"
```

**Corrected classification, not characterisation.** Traced from source (the guard's
own implementation, `FabIdentifier`'s grammar, and the seeded operator's actual
group membership) rather than assumed: today this answers **403**
`RESOURCE_FAB_NOT_AUTHORIZED`, not 400 — the guard runs on the raw, unparsed
string and rejects it on a groups-membership mismatch before `FabIdentifier`'s
grammar check is ever reached. This is a **fourth red case**, observed and quoted
alongside SC-1/SC-2/SC-8, not a before-picture that was already correct. No
additional production change is needed beyond T005's own reordering — parsing the
fab before the guard is what makes a malformed fab reach the parse at all, which
is the same reordering the empty-fab case needed.

**SC-7 (US1, bad request) — an omitted fab keeps ASP.NET's own refusal**

```gherkin
Given the same operator
When they GET /audit/overlay/{overlayIdentifier}   # no fabId at all
Then the response status is 400
```

*Characterisation. Records that the required-parameter refusal is the framework's
and is not being replaced.*

**SC-8 (US2) — the optional fab filter treats empty as unset**

```gherkin
Given an operator "admin@munich.test" holding /fabs/munich
  And an audit row in fab "munich"
When they GET /audit?fabId=
Then the response status is 200
  And the returned rows are scoped to the caller's fabs
  And the response is not 500
```

**SC-9 (US2) — the search's cross-fab guard is unchanged**

```gherkin
Given the same operator, who does not hold /fabs/berlin
When they GET /audit?fabId=berlin
Then the response status is 403
  And the problem "title" is "RESOURCE_FAB_NOT_AUTHORIZED"
```

*Without this, widening the predicate to `!string.IsNullOrWhiteSpace` could be
mis-written as "skip the guard" and the tenancy boundary would open.*

**SC-10 (auth, both) — an unauthenticated caller is still challenged before anything**

```gherkin
Given a caller with no bearer token
When they GET /audit/overlay/{overlayIdentifier}?fabId=
Then the response status is 401
```

*The `sse.audit.read` policy runs before the handler; an empty fab must not become
a way to reach handler code unauthenticated.*

---

## Independent end-to-end test procedure

Runnable by a human against a booted stack, without reading any test code.

1. Boot the Aspire AppHost (`dotnet run --project src/AppHost`). Wait for
   `audit-observability` to report healthy.
2. Mint a token for `admin@munich.test` / `Admin1234` from **Aspire's proxied
   Keycloak endpoint** (not the container's mapped port — a token from the wrong
   issuer 401s everything).
3. Pick any Guid as `{id}`.
4. Run the five probes and compare against the table:

   | # | Request | Expected status | Expected `title` |
   |---|---|---|---|
   | 1 | `GET /audit/overlay/{id}?fabId=` | **400** | `AUDIT_INVALID_INPUT` |
   | 2 | `GET /audit/overlay/{id}?fabId=%20` | **400** | `AUDIT_INVALID_INPUT` |
   | 3 | `GET /audit/overlay/{id}?fabId=munich` | **200** | — |
   | 4 | `GET /audit/overlay/{id}?fabId=berlin` | **403** | `RESOURCE_FAB_NOT_AUTHORIZED` |
   | 5 | `GET /audit?fabId=` | **200** | — |

5. **Before the fix, probes 1, 2 and 5 return 500.** Running them first on
   `origin/develop` is the cheapest confirmation that the test is testing
   something.
6. Confirm the `audit-observability` structured logs carry **no unhandled
   exception** for probes 1, 2 and 5 after the fix. A 400 produced by a caught
   `ArgumentException` still logs nothing at error level; an uncaught one does.
   This is the check that distinguishes a real fix from a status-code rewrite.

---

## Latency budget impact

**N/A.** `GET /audit/*` is an operator-console read path. It is not any of the six
legs of constitution §IV's `event arrival → overlay rendered ≤ 800 ms` budget, and
the change adds no work to any of them — it moves two existing statements past one
another inside one HTTP handler.

Constitution §VII's dashboard obligation binds implemented legs; this spec
implements no leg and inherits no obligation.

---

## Out of scope

- **`AuditEndpoints.GetSingle`'s guard call** (`:183`). It passes `row.Fab` from
  the database, not from the caller. Not a trust boundary; a whitespace fab
  persisted on a row is a different defect with a different fix.
- **Changing `DefaultFabAuthorizationGuard`.** See "The scope decision" (a).
- **Any other context.** All eleven other call sites were read and are correct;
  §"Claim 5" records the evidence so the sweep is not repeated.
- **A new error code or a new `GetResourceTimelineError` variant.** See "Claim 3".
- **An architecture test that enforces parse-before-guard repo-wide.** Tempting,
  and genuinely in ADR-0139's spirit — but the antecedent is subtle (eleven sites
  satisfy it three structurally different ways) and a guard written from this
  spec's sentence would report the wrong population, which is precisely spec 091's
  recorded lesson. If it is wanted, it is its own issue with its own measurement.
- **Backfilling a `?fabId=` probe into other contexts' integration tests.** They
  are correct by construction; a test per context would assert `FabResolution`'s
  branch eleven times.

---

## Success criteria

- **SC-A** `GET /audit/{kind}/{id}?fabId=` returns `400 AUDIT_INVALID_INPUT`,
  observed over HTTP against the real stack.
- **SC-B** The same for `?fabId=%20`.
- **SC-C** `GET /audit?fabId=` returns `200` with rows scoped to the caller's fabs.
- **SC-D** Every existing assertion in
  `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`
  passes **unmodified**. If one has to be edited, the fab boundary moved and the
  work blocks (constitution §Testing).
- **SC-E** No new error code appears anywhere in `src/`.
- **SC-F** `src/ServiceDefaults/Authorization/IFabAuthorizationGuard.cs` is
  unchanged.
- **SC-G** Phase 4a's new-behaviour tests were **observed red first**, and the
  verbatim failure is quoted in the PR (ADR-0139, ADR-0144).

## Assumptions, marked

- **A-1** ASP.NET Core minimal-API model binding maps a present-but-empty query
  value to `string.Empty` for both `string` and `string?` parameters. This is the
  mechanism the whole premise rests on. It is **not** taken on trust: SC-1 and
  SC-8 fail red on today's code only if it holds, so phase 4a's red run *is* the
  verification. If the red run instead shows a framework 400, the premise is wrong
  and phase 1 must be reopened.
- **A-2** A `403` for a fab the caller does not hold is preferable to a `400` for a
  malformed resource identifier, when a request is both. Asserted from the verbatim
  reasoning in `DevicesEndpoints.Disable` and `KiosksEndpoints.Disable`, not from
  first principles here.
- **A-3** The realm seeds `admin@munich.test` in `/fabs/munich` only, and `berlin`
  exists as a fab the user does not hold. Relied on by
  `CrossFabReadGuardIntegrationTests` today, so it is already load-bearing.

## Phase 4a colour

**RED.** The endpoint's response for `?fabId=` changes from 500 to 400 — new
behaviour. SC-1, SC-2 and SC-8 must be observed failing before the fix, and the
failure output quoted in the PR body (ADR-0139, ADR-0144). **SC-6 was
reclassified from characterisation to a fourth red case** after its "already
passes today" premise was re-derived from source and found wrong — see the
correction in §Problem and SC-6's own scenario above; its 403-today failure
output must be quoted alongside SC-1/SC-2/SC-8. SC-4, SC-7 and SC-9 are
**characterisation** and must be green before *and* after, unmodified.
