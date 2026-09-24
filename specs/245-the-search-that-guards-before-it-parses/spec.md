# Spec 245 — The search that guards before it parses

**Issue:** [#2530](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2530)
**Branch:** `fix/2530-search-guard-before-parse`
**Status:** Phase 3 complete — awaiting review
**ADRs:** ADR-0139 (validate at trust boundaries; rules that fail the build),
ADR-0105 (`Ensure.That` guards), ADR-0089 (`ApiError` with HTTP status),
ADR-0036 (smallest change), ADR-0044 (per-context value objects),
ADR-0144 (autonomous lane, phase-4a colours)
**Precedent spec:** 215 (#2507) — the same defect on `GetTimeline`, eight lines
below in the same file. Its §Out of scope recorded both gaps this spec closes.
**Precedent commit (gap 2):** `e9afe03e` — "skip one odd group", the rule five
contexts already apply to a caller's claimed fabs.

---

## Problem

Two gaps in `GET /audit` (`AuditEndpoints.Search`), one defect class: a
caller-derived fab string reaching a check before it has been parsed.

**Gap 1 — the wrong gate answers a malformed `fabId`.** `Search` runs the fab
authorization guard on the raw query string. `DefaultFabAuthorizationGuard` does
plain string equality against the `groups` claim and has no grammar opinion, so
`?fabId=NOT_A_FAB` is refused as **403 `RESOURCE_FAB_NOT_AUTHORIZED`** — an
authorization answer to an input-validation question. Since #2507 the sibling
endpoint answers the identical input **400 `AUDIT_INVALID_INPUT`**.

**Gap 2 — a caller's own group claim can 500 the search.**
`SearchAuditQueryHandler.cs:72` maps every claimed fab through
`FabIdentifier.From` in one expression. A single `/fabs/<x>` group whose `<x>`
fails the lowercase grammar throws an uncaught `ArgumentException` → **500**, and
it takes the whole search down — including for a caller who *also* holds a
perfectly good fab. `:54` has the same throw on the named-fab path, reachable when
a caller both holds and names a malformed fab (the guard's string equality lets it
through).

The issue called gap 2 "currently unreachable". That is true of **today's seeded
realm**, not of the code: it needs only a Keycloak group named outside the
grammar (`/fabs/Munich`, `/fabs/north_hall`), which is realm configuration the app
does not control. Reproduced below.

---

## Reproduction — observed, not inferred (HEAD `a54b11d0`)

Driven over real HTTP against the **real** `MapAuditEndpoints`,
`DefaultFabAuthorizationGuard`, `FabAuthorizationExceptionHandler` and
`SearchAuditQueryHandler`, hosted in-process on Kestrel with a fake
authentication scheme that sets the `groups` claim from a header, and the
Application tests' `TestAuditEventQuerySource` (empty) as the data source. No
Aspire stack was booted — another worktree may hold the machine's one stack.
The harness source is in [`plan.md` §Appendix](./plan.md#appendix--the-reproduction-harness).

Verbatim output:

```
groups=[/fabs/munich]  GET /audit/overlay/e9b2639c-6408-4418-9dd1-29d42dc532bd?fabId=NOT_A_FAB  -> 400 AUDIT_INVALID_INPUT
groups=[/fabs/munich]  GET /audit?fabId=NOT_A_FAB  -> 403 RESOURCE_FAB_NOT_AUTHORIZED
groups=[/fabs/munich]  GET /audit?fabId=munich  -> 200
groups=[/fabs/munich]  GET /audit?fabId=berlin  -> 403 RESOURCE_FAB_NOT_AUTHORIZED
groups=[/fabs/munich]  GET /audit  -> 200
         at SmartSentinelEye.AuditObservability.Application.Queries.Handlers.SearchAuditQueryHandler.HandleAsync(...) in ...\SearchAuditQueryHandler.cs:line 72
groups=[/fabs/Munich]  GET /audit  -> 500 An error occurred while processing your request.
         at SmartSentinelEye.AuditObservability.Application.Queries.Handlers.SearchAuditQueryHandler.HandleAsync(...) in ...\SearchAuditQueryHandler.cs:line 72
groups=[/fabs/munich /fabs/Munich]  GET /audit  -> 500 An error occurred while processing your request.
         at SmartSentinelEye.AuditObservability.Application.Queries.Handlers.SearchAuditQueryHandler.HandleAsync(...) in ...\SearchAuditQueryHandler.cs:line 54
groups=[/fabs/NOT_A_FAB]  GET /audit?fabId=NOT_A_FAB  -> 500 An error occurred while processing your request.
```

Lines 1–2 are **gap 1**: the same malformed input, 400 on one endpoint and 403 on
its sibling. Lines 6–8 are **gap 2**, at both throw sites the issue named. Line 7
is the one that matters most: a correctly-configured munich operator loses the
entire audit search to one stray group. Lines 3–5 are controls: the harness
answers the happy path and the genuine cross-fab refusal correctly, so the
anomalies are not artefacts of the harness.

---

## The gap-2 decision — filter the malformed group out

The issue asked whether a malformed fab in the caller's **own** claim set should
be (a) a 400, (b) a 403, (c) silently filtered out, or (d) something else, and
warned it is "not obviously the same answer as the query-parameter case". It is
not — and it is **not a new decision either**. This repository already made it.

**Chosen: (c), per entry.** A `/fabs/<x>` group whose `<x>` is not a valid
`FabIdentifier` is treated as *not a fab the caller holds*. The remaining,
well-formed groups scope the search as they do today.

### Why this is an application of an existing decision, not an ADR

`e9afe03e` ("stop inventing a fab called "none", and skip one odd group", #1299
review) decided exactly this for Automation: *"One unusable group hid every fab
… It now maps per entry and keeps what parses."* The same per-entry skip, with the
same reasoning in the same comment, is now in **five** contexts:

| Site | Behaviour on a malformed claimed fab |
|---|---|
| `Automation/Api/RulesEndpoints.cs` `ResolveReadFabsAsync` | skipped, per entry |
| `SystemVariables/Api/SystemVariableEndpoints.cs` `ResolveReadFabsAsync` | skipped, per entry ("Mirrors RulesEndpoints, where that was a real defect") |
| `CameraCatalog/Api/CameraEndpoints.cs` | skipped, per entry |
| `LayoutComposition/Api/LayoutEndpoints.Commands.cs` | skipped, per entry |
| `StreamDistribution/Api/StreamEndpoints.cs` | skipped, per entry |
| **`AuditObservability` `SearchAuditQueryHandler.cs:72`** | **500 — the sole outlier** |

AuditObservability is the only reader of the caller's claimed fab set that still
fails wholesale. Bringing it into line is the smallest change that applies a
settled rule; no ADR is needed, and none is written (the lane may not write one).

### Why not the alternatives

- **(a) 400.** A 400 says "your request was malformed". The caller's request was
  fine; their *account* is misconfigured, and they cannot fix it by changing the
  request. It would also keep the property that makes line 7 above so bad: one
  stray group costs the caller every fab they legitimately hold.
- **(b) 403.** Same wholesale loss, and it would tell a caller they are refused a
  search they are entitled to. The repository's 403 for a misconfigured account
  (`FabAuthorizationException.ForNoFabMembership`) is raised only when the caller
  holds *nothing usable*, which (c) already reaches naturally — see below.
- **Normalise instead (lowercase `Munich` → `munich`).** **Rejected, and a test
  pins it.** It would turn a misconfigured group into a grant for a fab the realm
  never assigned. Failing closed is the only safe direction for a claim the app
  cannot vouch for.

### What "wholly malformed" means here — the one audit-specific difference

The five precedent contexts answer a caller with **no usable fab** with a named
refusal (`*_FAB_REQUIRED` / `ForNoFabMembership`). `Search` does not, and did not
before this spec: a caller with no fab membership already gets **200 with
cross-fab rows only** (`SearchAuditQueryHandler.cs:75-79`, #1300 — cross-fab rows
are readable by every `sse.audit.read` holder). After filtering, a caller whose
groups are *all* malformed is exactly such a caller, and gets exactly that answer.

This is deliberately **not** importing the precedents' refusal. That would change
`Search`'s existing, tested answer for a no-membership caller, which is out of this
issue's scope. It widens nothing: cross-fab rows are already visible to that
caller.

---

## Locked tech choices

Nothing new is introduced.

| Concern | Choice | Source |
|---|---|---|
| Endpoint-boundary parse | `BoundaryParse.TryParse(() => FabIdentifier.From(fabId), "AUDIT_INVALID_INPUT", …)` | `GetTimeline`, `AuditEndpoints.cs:127` (#2507) |
| Error code | `AUDIT_INVALID_INPUT` — existing; `Search` already declares `.ProducesProblem(400)` | `AuditEndpoints.cs:37` |
| Guard | `IFabAuthorizationGuard.EnsureAccessAsync` — **unchanged**, now fed `parsedFab.Value` | spec 008 FR-019 |
| Claimed-fab parse | per-entry `try { FabIdentifier.From } catch (ArgumentException) { skip }` | `e9afe03e`; in-file precedent: `ActorUsername` at `SearchAuditQueryHandler.cs:92-100` |
| Value object | `AuditObservability.Domain.AuditEvent.FabIdentifier` — unchanged | ADR-0044 |
| Tests | xUnit + Shouldly; handler via `TestAuditEventQuerySource`; endpoint via `AspireFixture` | ADR-0052, ADR-0103 |

---

## User stories

### US1 (P1) — a malformed fab filter on the audit search is a client error

*As an operator (or a client with a bad fab value), when I search the audit log
with a `fabId` that is not a fab name at all, I am told my input was malformed —
the same answer the resource timeline gives — not that I lack access.*

### US2 (P2) — one misconfigured group does not cost an operator their audit search

*As an operator whose Keycloak account carries one group outside the fab grammar
(a realm misconfiguration I cannot see), I still see the audit rows of the fabs I
genuinely hold, instead of a server error.*

Independently shippable: different file (`SearchAuditQueryHandler.cs` vs
`AuditEndpoints.cs`), different test layer, no dependency on US1.

---

## Acceptance scenarios

### US1 — HTTP, Aspire fixture (`CrossFabReadGuardIntegrationTests`)

**SC-1 (US1, bad request) — RED**

```gherkin
Given an operator "admin@munich.test" holding /fabs/munich
When they GET /audit?fabId=NOT_A_FAB
Then the response status is 400
  And the problem "title" is "AUDIT_INVALID_INPUT"
```

Today: **403 `RESOURCE_FAB_NOT_AUTHORIZED`** (reproduction line 2).

**SC-2 (US1, auth) — a well-formed fab the caller does not hold is still 403** —
characterisation, existing test `A_cross_fab_audit_search_is_still_refused`,
**unmodified**. It is what fails if the fix is mis-written as "parse instead of
guard".

**SC-3 (US1, happy) — a held fab still answers 200** — characterisation, covered
by the existing search tests, unmodified.

**SC-4 (US1) — an empty `fabId` still means "no filter"** — characterisation,
existing `An_empty_fab_on_the_audit_search_spans_the_callers_fabs` (spec 215
SC-8), **unmodified**. Catches a fix that parses a blank value instead of treating
it as omitted.

**SC-5 (auth) — unauthenticated is still 401** — no new test; spec 215 SC-10
covers the `sse.audit.read` policy, which runs before any handler code.

### US2 — handler unit tests (`SearchAuditQueryHandlerTests`)

Driven at the handler because the seeded realm has no malformed group and editing
it needs the Keycloak volume deleted — a cost out of proportion to one branch.
The endpoint passes `FabClaims.AssignedFabs(user)` through unchanged, so the
handler is where the claim meets the parse.

**SC-6 (US2, conflict) — one malformed group costs only itself — RED**

```gherkin
Given audit rows in "munich", in "berlin", and one with no fab
When the handler searches with Fab = null and CallerFabs = ["munich", "Munich"]
Then the result is a success
  And the rows are exactly the munich row and the fab-less row
```

Today: throws `ArgumentException` (reproduction line 7).

**SC-7 (US2, auth) — a wholly malformed claim set grants nothing — RED**

```gherkin
Given audit rows in "munich" and one with no fab
When the handler searches with Fab = null and CallerFabs = ["Munich"]
Then the result is a success
  And the rows are exactly the fab-less row
  And the munich row is not returned
```

Today: throws. The last line is the one that forbids "fixing" this by lowercasing.
It is also the no-membership answer (`CallerFabs = []`), which the existing test at
`SearchAuditQueryHandlerTests.cs:120` pins — the two must agree.

**SC-8 — the existing handler suite passes unmodified** — characterisation.

---

## Independent end-to-end test procedure

1. Boot the AppHost; wait for `audit-observability` healthy. Confirm the process
   start time is after the commit under test.
2. Mint a token for `admin@munich.test` / `Admin1234` from **Aspire's proxied
   Keycloak endpoint**.
3. Probe:

   | # | Request | Expected |
   |---|---|---|
   | 1 | `GET /audit?fabId=NOT_A_FAB` | **400** `AUDIT_INVALID_INPUT` (was 403) |
   | 2 | `GET /audit/overlay/{guid}?fabId=NOT_A_FAB` | 400 `AUDIT_INVALID_INPUT` (unchanged — the two now agree) |
   | 3 | `GET /audit?fabId=berlin` | 403 `RESOURCE_FAB_NOT_AUTHORIZED` |
   | 4 | `GET /audit?fabId=munich` | 200 |
   | 5 | `GET /audit?fabId=` | 200 |

4. **Gap 2 cannot be probed on the seeded realm.** Re-run the out-of-tree harness
   in plan.md §Appendix instead and record its output: lines 6–8 must read 200,
   200 and 400 respectively.

---

## Latency budget impact

**N/A.** `GET /audit` is an operator-console read. It is on none of constitution
§IV's six legs, and the change adds one string parse to a request handler.

---

## Out of scope

- **Logging the skipped group.** No endpoint or handler in the precedent sites
  logs it; `e9afe03e` records why ("the misconfiguration belongs to the realm, and
  the caller cannot act on it either way"). Surfacing realm misconfiguration is a
  cross-context observability question — six sites, not one.
- **A shared helper for the per-entry parse.** Six copies exist after this spec.
  `ServiceDefaults` cannot reference a context's `FabIdentifier` (ADR-0044), so a
  shared helper is a design question of its own.
- **Changing `SearchAuditQuery`'s field types to `FabIdentifier`.** Considered and
  rejected in plan.md.
- **Refusing a wholly-malformed caller** the way the precedents do. See "What
  wholly malformed means here".
- **`GetSingle`'s guard on `row.Fab`** — database-sourced, not a trust boundary
  (spec 215).
- **`IFabAuthorizationGuard`** — unchanged.

---

## Success criteria

- **SC-A** `GET /audit?fabId=NOT_A_FAB` → 400 `AUDIT_INVALID_INPUT`, over HTTP on
  the real stack.
- **SC-B** SC-6 and SC-7 green; observed **red first**, output quoted in the PR.
- **SC-C** Every pre-existing assertion in `CrossFabReadGuardIntegrationTests.cs`
  and `SearchAuditQueryHandlerTests.cs` passes **unmodified**.
- **SC-D** No new error code in `src/`; `IFabAuthorizationGuard.cs` unchanged;
  `SearchAuditQuery.cs` unchanged.
- **SC-E** The harness re-run shows lines 6–8 as 200 / 200 / 400.

## Assumptions, marked

- **A-1** The seeded realm gives `admin@munich.test` exactly `/fabs/munich`
  (already load-bearing for `CrossFabReadGuardIntegrationTests`).
- **A-2** The fake-auth harness is faithful for this question: it replaces only
  authentication, and the `groups` claim it sets is the single-space-separated
  shape `FabClaims.AssignedFabs` already handles. Controls 3–5 behave as the real
  stack does.

## Phase 4a colour

**RED** — behaviour-changing. SC-1 (403 → 400), SC-6 and SC-7 (throw → success)
must be observed failing before any `src/` change, verbatim output quoted in the
PR. SC-2, SC-3, SC-4 and SC-8 are characterisation: green before and after,
unmodified.
