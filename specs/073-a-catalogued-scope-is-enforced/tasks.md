# Tasks — Spec 073, a catalogued scope is enforced

**Phase:** 3 (Tasks) · **Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2070

**Engineer:** `backend-engineer` throughout. Six lines of C# in one
`src/*/Api` file, three deleted lines in one `tests/Architecture.Tests` file,
and two new test files. No frontend, no Aspire wiring, no realm edit, no
migration, no contract. `test-writer` owns T001, T002 and T008 per ADR-0144's
phase-4 split; the engineer receives the verbatim red output and **may not edit
those tests to make them pass**.

**Phase 4a colour:** **red — behaviour-changing.** The three GETs currently
admit any authenticated fab member; after this they admit only holders of
`sse.variables.read`. That is a change in what the running system does, so a
test that arrives green is a phase-4 failure, not a shortcut.

**Parallelism.** Almost none, honestly. T001–T006 form one serial chain because
T003–T006 all edit the same two files, and ADR-0109's disjoint-file condition
fails. The one real `[P]` pair is **T002 ∥ T008**: two different new test files,
no shared state — though T008 needs Docker and T002 does not, so in practice
they will not run at the same time anyway. **Do not fan out T003–T006.**

**Foundational / blocking:** T001 blocks everything. Nothing here touches
`Shared.Kernel`, `Shared.Contracts`, `AppHost` or any Aspire resource, so there
is no fan-out for the orchestrator to arrange after it.

---

## Red — observed before `src/` is touched

- **[T001] [US-1]** Confirm the pre-state on the branch tip. Run the existing
  `EndpointScopeDeclarationTests` and record that it is **green**, and that
  `UnenforcedByDesign` (`tests/Architecture.Tests/EndpointScopeDeclarationTests.cs:240-245`)
  holds exactly the three `#2070` rows.
  *Depends on: nothing. Blocks: T002–T009.*

  **Done when:** the suite is green and the three rows are quoted verbatim in
  the PR body as the starting state. A guard that was already red for an
  unrelated reason would make every later red ambiguous, so this is not
  ceremony.

- **[T002] [P] [US-1]** *(test-writer)* New file
  `tests/Architecture.Tests/SystemVariableReadScopeTests.cs`: assert that
  `GET /system-variables`, `GET /system-variables/snapshot` and
  `GET /system-variables/{name}` each resolve to an authorization policy named
  `Scope.Sse.Variables.Read`. Preferred form — map the endpoints in-process
  (`WebApplication.CreateBuilder([]).Build()` → `MapSystemVariableEndpoints()`
  → `EndpointDataSource.Endpoints` → `IAuthorizeData.Policy`) so the assertion
  is about the endpoint the framework built, not the text of the file.
  `Architecture.Tests` already references `SmartSentinelEye.SystemVariables.Api`.
  **Docker-free.**
  *Depends on: T001. Blocks: T005.*

  **Done when:** the test is **observed red**, naming all three routes, and the
  verbatim output is captured. Take the scope name from the constant, never as a
  typed string.

  **If endpoint building demands DI**, climb the plan's fallback ladder in
  order — `AddAuthorization()`, then `AddSystemVariablesApi()`, then the
  source-scan reader already in `EndpointScopeDeclarationTests` — and **say in
  the PR which rung the test stands on**. A guard that silently fell back to
  reading text is the failure mode ADR-0139 exists to prevent.

- **[T003] [US-1]** *(test-writer)* Delete the three `#2070` rows from
  `UnenforcedByDesign`. Leave the array in place, empty, with its doc-comment
  intact — an empty both-ways register is the honest record that nothing is
  currently deferred, and the next endpoint that needs a deferral needs the
  mechanism.
  *Depends on: T001. Blocks: T005.*

  **Done when:** `Every_endpoint_that_enforces_no_scope_is_registered_against_an_open_issue`
  (`:594`) is **observed red**, naming all three routes with the message "these
  endpoints require authentication and nothing else", and that output is
  captured. Docker-free.

  **This deletion is the fix, not a weakened gate.** The register is read in
  both directions and its own doc-comment at `:236` says fixing #2070 deletes
  these rows. Removing a row moves its route from "excused against an open
  issue" to "checked like every other endpoint" — strictly more is asserted
  afterwards, which is the opposite of the three things ADR-0144 forbids the
  lane. Say this in the PR body in one sentence, so a reviewer meeting three
  deleted test lines does not have to re-derive it.

## Green — the change

- **[T004] [US-1]** Add `.RequireAuthorization(Scope.Sse.Variables.Read)` to the
  three GET mappings in `src/SystemVariables/Api/SystemVariableEndpoints.cs`
  (`:43`, `:53`, `:60`), chained in the same position the three writes chain
  theirs. **Leave the group-level `.RequireAuthorization()` at `:35` alone** —
  policies compose by AND, and removing it changes what an unauthenticated
  caller gets. Do not use `.RequireScope`; it has zero call sites and adopting
  it here would be a drive-by (ADR-0036).
  *Depends on: T002, T003. Blocks: T005, T006.*

  **Done when:** T002's test is green.

- **[T005] [US-1]** Summaries. Append `Required scope: sse.variables.read` to the
  existing summaries on `GET /system-variables` and `GET /system-variables/{name}`,
  spelled exactly as the eighteen conformant endpoints spell it. Give
  `GET /system-variables/snapshot` its **first** `.WithSummary` — one sentence
  saying it returns an overlay's resolved label text, plus the scope sentence.
  It is the one route handler of 56 without a summary, and only because it had
  no scope to name.
  *Depends on: T004.*

  **Done when:** `Every_scoped_endpoint_names_the_scope_it_enforces_in_its_summary`
  (`:537`) and `No_summary_names_a_scope_other_than_the_one_the_endpoint_enforces`
  (`:565`) are both green, and `Every_registered_route_still_enforces_no_scope`
  (`:621`) is green with an empty register.

- **[T006] [US-1]** Correct the two stale comments in the same file: the class
  doc at `:23` ("Writes require admin policy; reads require any authenticated
  user") and the block above the reads at `:38`. A comment asserting the old
  rule beside the new chain is the same defect one layer down.
  *Depends on: T004.*

  **Done when:** neither comment claims the reads are open to any authenticated
  user.

- **[T007] [US-1]** Confirm nothing else moved. `EndpointFileCount` (12) and
  `RouteHandlerMappingCount` (56) in `EndpointScopeDeclarationTests` must be
  **unchanged**; `Scope.cs` and `src/AppHost/Realms/smart-sentinel-eye-realm.json`
  must be untouched in the diff.
  *Depends on: T005, T006.*

  **Done when:** `git diff --stat` shows exactly the files this plan names. If
  either pinned count needed editing, the change is wrong — stop and report.

## The refusal, on the real stack

- **[T008] [P] [US-1] [CI]** *(test-writer)* New file
  `tests/Integration.Tests/SystemVariables/VariableReadScopeIntegrationTests.cs`.
  Mint a `client_credentials` token for `scenario-simulator` /
  `dev-only-scenario-simulator-secret` — a service account in `/fabs/munich`
  (realm `:576`) with no `sse.variables.read` — and assert **403** on all three
  GETs. In the same test, assert **200** on `GET /system-variables` for the
  fixture's admin client, so a broken fixture cannot pass as a refusal.
  *Depends on: T001. Blocks: nothing.*

  **Done when:** observed red before T004 (each route answers 200, or 404 for a
  name that does not exist) and green after.

  **The fixture's own client cannot produce this red.** `AspireFixture.ClientId`
  is `smart-sentinel-eye-web` and `FetchAccessTokenAsync` always asks for
  `openid sse.management` (`AspireFixture.Auth.cs:12`, `:111`), and that bundle
  satisfies every `sse.*` policy but `sse.events.publish`. A test built on
  `CreateAdminClientAsync` returns 200 before *and* after, and that is precisely
  how this gap survived to be found by a phase-6 review of an unrelated PR. Use
  the hand-rolled minting already present at
  `StreamDistribution/StreamFabAttributionIntegrationTests.cs:97` or
  `Identity/FabGroupClaimIntegrationTests.cs:176` — **do not add a helper to
  `AspireFixture`**; two call sites is not yet a pattern (ADR-0036).

  Mint from **Aspire's proxied Keycloak endpoint**, not the container's mapped
  port, or the issuer will not match and every call 401s regardless of scope.
  Assert the exact status 403, not "not 200" — spec 069's audience check would
  also produce a non-200, and `scenario-simulator` carries `sse-audience` so it
  should not fire.

  **Marked `[CI]`.** Docker is unresponsive on the authoring machine. If it is
  still down at phase 4, T002 and T003 satisfy the phase-4a gate locally and
  this one is observed on the PR's CI run — **download the job log before any
  re-run**, since a passing re-run flips the whole run to success and erases the
  failure from its history.

  **Alternate principal** if `scenario-simulator`'s account changes:
  `stream-distribution-attribution` / `dev-only-stream-distribution-secret`, in
  `/fabs/munich` and `/fabs/dresden` (realm `:582`), likewise without the scope.

## Verification (phase 5)

- **[T009] [US-1]** Run `specs/073-a-catalogued-scope-is-enforced/spec.md`
  § *Independent end-to-end test procedure* against a booted stack: the
  `scenario-simulator` token is refused 403 on all three routes, the admin token
  still reads the list, and a kiosk opened at a Munich cell with a
  variable-bound label **still renders its opening text** — that last one is the
  #2069 path and the only place a lockout would show as a blank tile.
  *Depends on: T007, T008.*

  **Done when:** the verification note on the PR records both the refusal and
  the kiosk render, with the pre-change 200 from step 6 so the 403 is
  attributable to this commit rather than to a mis-minted token.

## Board (phase 3 gate)

Issue **#2070** must be an item on Project #13 — feature-level, not per task
(per-task issues stopped after spec 028). `/speckit-tasks` adds nothing to the
board; add it by hand:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2070
```

`item-add` prints nothing on success, and `item-list` defaults to 30 items —
verify with `--limit 2000` or a filled board reads as empty.

## Not doing

- Adopting or deleting `RequireScopeExtensions.RequireScope` — separate question.
- Changing the `sse.management` bundle's blanket grant — pre-existing and
  deliberate; narrowing it is its own issue with its own outage analysis.
- Any realm or `Scope.cs` edit — the scope and its grants already exist.
- Restructuring `SystemVariableEndpoints.cs` to get under 300 lines.
- Touching the `MappedOutsideTheChain` register or `/hubs/layouts`.

## Blocked / needs a decision before phase 4

**Nothing.** No new ADR is required — every decision applies an existing one
(ADR-007/008, ADR-0036, ADR-0070, ADR-0139, constitution §VIII). The lane is not
blocked by ADR-0144's prohibition.

## Gate — Phase 3

Tasks atomic and dependency-ordered; #2070 on Project #13.
