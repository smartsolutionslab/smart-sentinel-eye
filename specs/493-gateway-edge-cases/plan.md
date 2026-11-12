# Implementation Plan: Gateway and shell dev proxy edge cases

**Branch**: `493-gateway-edge-cases` | **Date**: 2026-11-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/493-gateway-edge-cases/spec.md` (Specify gate approved by the owner, 2026-11-12).
**Governing decision**: ADR-0192 (accepted); spec 208 for the carve-out. **Issue**: #3259. **Merges before**: spec 494 (kiosk and wall on
the one origin), which reuses this spec's per-root fallback and caching rule (D3).

## Summary

Four small fixes in three files, each started by a red test:

1. **Dev proxy** (`apps/management-web/src/app/devProxy.ts`): every key becomes a bounded prefix `^<prefix>(/|\?|$)`, so a query string
   straight after a context is forwarded (claim 1) and `/apiary` is not (claim 4).
2. **Static fallback** (`src/ApiGateway`): mapped without the `nonfile` constraint, and a page navigation gets the entry page whatever
   its last segment looks like (claim 2).
3. **Entry points revalidate** (`src/ApiGateway`): `Cache-Control: no-cache` on the shell's `index.html` and each remote's
   `remoteEntry.js`, wherever they are served (claim 3).
4. **Carve-out for every method** (`src/ApiGateway/GatewayHost.cs`): `Map` instead of `MapPost` on both authorize paths (claim 5).

§IV legs touched: **none**.

## Technical Context

**Language/Version**: C# on .NET 10 (gateway); TypeScript, Vite 8.3.6 (shell dev server).

**Primary Dependencies**: ASP.NET Core routing and static files (shared framework), `Yarp.ReverseProxy` 2.3.0 (unchanged). No new
package, frontend or backend.

**Storage**: N/A.

**Testing**: vitest (`management-web`); xUnit + Shouldly `Integration.Tests`, `FixtureLogic` category only: the in-process
`GatewayTestHost`, `StaticUiStack` and `StubDestination`. **No Aspire, no Docker** is needed for any test in this spec.

**Target Platform**: the gateway process; the shell's Vite dev server.

**Project Type**: web service (gateway) plus one Vite app's dev configuration.

**Performance Goals**: none new. The fallback's entry page is re-fetched on every navigation under `no-cache` (no validators on that
path, R3); it is one small file.

**Constraints**: spec 491's gateway tests pass unmodified; spec 208's carve-out tests pass unmodified; `BoundaryTests` unaffected (no
SignalR/WebSocket reference added); `@microsoft/signalr` not touched.

**Scale/Scope**: ~12 proxy keys, 3 UI roots, 2 carve-out paths.

## Constitution Check

_GATE: passes before research; re-checked after design (still passes)._

| Rule                               | Status                                                                                                                                                                                                                                                                            |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| §IV latency budget                 | **Legs touched: none.** Dev proxy, static UI hosting and the carve-out are off the event-to-overlay path (ADR-0192 fact 1).                                                                                                                                                       |
| §VI Aspire is the composition root | No resource, endpoint or environment variable changes. `AppHost` untouched.                                                                                                                                                                                                       |
| §VIII safe at trust boundaries     | Story 4 narrows what the gateway forwards (every method on the authorize hook now stops at the gateway). Story 2 serves HTML for more paths, only to page navigations, still from the configured root via `PhysicalFileProvider`, still `nosniff`. No scope, auth or CORS change. |
| §III bounded-context isolation     | The gateway references no context; unchanged.                                                                                                                                                                                                                                     |
| §II primitives in domain models    | Not applicable: no domain model.                                                                                                                                                                                                                                                  |
| ADR-0036 smallest change           | One rule per defect. The one new type (D3's UI root) has a known second caller, spec 494, at the owner's request; it is not speculative.                                                                                                                                          |
| ADR-0139 test colour               | **All four stories are behaviour-changing: red first.** See _Test colour per story_. The guard tests that pin unchanged behaviour are written in the same commit as the red test and are expected green before the fix; they are not the evidence.                                |
| ADR-0105 guards                    | `Ensure.That(...)` for any new argument guard on the UI-root record.                                                                                                                                                                                                              |
| ADR-0030 / ADR-0086                | Conventional commits, no `Co-Authored-By`.                                                                                                                                                                                                                                        |

No violations; Complexity Tracking is empty.

## Decisions

Expanded, with alternatives, in [research.md](./research.md).

- **D1. One bounded-prefix key for every dev-proxy entry (R1).** A function builds `^<escaped prefix>(/|\?|$)`; all twelve entries use
  it. Bare contexts keep their navigation `bypass`; `/api` and the remotes keep none. Entries no longer overlap, so order does not matter.
- **D2. The Static fallback is a catch-all without `nonfile`, and navigation decides (R2).** Pattern `{prefix}/{**path}`. GET/HEAD page
  navigations (`Sec-Fetch-Dest` contains `document`, case-insensitive) always get the entry page; non-navigations get it only for an
  extensionless last segment; everything else is 404. Removing the handler's `HasExtension` check alone would not fix claim 2, because
  `MapFallback`'s default `{*path:nonfile}` route never reaches the handler for a dotted path.
- **D3. Static hosting is per UI root, reusable by spec 494 (R4).** A record `UiRoot(RequestPath, Directory, EntryFile)` and two
  operations: _serve_ (static files, `nosniff`, `no-cache` for the entry file) and _map fallback_ (D2). The shell uses both; each remote
  uses _serve_ only (a miss under `/remotes/` stays 404). `StaticRoots` keeps its start-up validation and yields three `UiRoot`s. Nothing
  in the two operations names the shell. **Spec 494 adds `/kiosk` and `/wall` as two more roots with both operations and no change to
  them**; it should pin that `/kiosk/{**path}` outranks `{**path}` with its own test. The new type lives in its own file under
  `src/ApiGateway/`, so `UiHosting.cs` keeps mode selection and the proxy routes.
- **D4. `no-cache` is the entry file's property, applied where it is served (R3).** Static files: when the request path equals
  `<RequestPath>/<EntryFile>`. Fallback: always. Hashed assets: unchanged.
- **D5. The carve-out maps every method (R5).** `app.Map` on `/stream-distribution/streams/authorize` and
  `/api/stream-distribution/streams/authorize`, answering 404. The existing explanatory comment stays, trimmed only in the lines touched
  (CLAUDE.md comment rule).

## Deviation from the spec

**FR-007 says spec 491's tests pass unmodified. One of them cannot.** `apps/management-web/src/app/devProxy.test.ts` (spec 491) finds
entries by key spelling: `table['/api']`, `table['/remotes/cameras']`, `table['/remotes/event-ingestion']`,
``table[`^/${context}(/|$)`]`` and `['^/system-variables(/|$)']`. Any fix of claim 4 changes those spellings (R1 lists the
alternatives that would avoid it and why they were rejected). The plan changes **only the lookup**: a test helper applies R1's matching
rule to a URL and returns the entry Vite would pick. Every assertion on the entry found (`target`, `changeOrigin`, `ws`, `bypass` result,
the `-other` negatives) stays as written. To prove the rewrite moves no behaviour, the lookup change is its own commit, **run green
against the unfixed `devProxy.ts`** before any red test is added. The C# tests of spec 491 and spec 208 are untouched.

**Owner-confirmed at the Plan gate (2026-11-12)**:

1. The `devProxy.test.ts` lookup-helper change is accepted as described: its own commit, green against the unfixed `devProxy.ts` first.
2. When port 5173 is held (a running stack), the real-Vite check in quickstart.md step 2 is skipped and the gap is recorded in the
   verification note; nothing is stopped to free the port.
3. The new gateway file is named `UiRoot.cs`.

## Test colour per story (ADR-0139)

| Story                | Colour  | Red test (observed failing before the fix)                                                                                                                                                                           | Guards (green before and after)                                                                                                                            | File                       |
| -------------------- | ------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------- |
| US1 dev proxy        | **red** | `/<context>?limit=5` is forwarded, for all nine contexts (today: no entry matches); `/api-tokens`, `/apiary`, `/remotes/cameras-x`, `/remotes/event-ingestion-x` are not forwarded (today: the plain keys take them) | `/api`, `/api?x=1`, `/api/system-variables`, remote paths, `/<context>/x?y` forwarded; navigation with a query handed back to Vite; `-other` not forwarded | `devProxy.test.ts`         |
| US2 dotted deep link | **red** | navigation GET `/cameras/cam.01` gets the shell (today: 404)                                                                                                                                                         | non-navigation `/assets/missing.js` 404; navigation to `/remotes/cameras/x.js` and `/api/x.y` 404; POST to an unrouted path 404                            | `GatewayUiStaticTests.cs`  |
| US3 no-cache         | **red** | `Cache-Control: no-cache` on `/`, `/layouts/123`, `/index.html`, navigation `/cameras/cam.01` (after US2), `/remotes/cameras/remoteEntry.js`, `/remotes/event-ingestion/remoteEntry.js` (today: absent)              | `/assets/app.js` and `/remotes/cameras/assets/chunk.js` carry no `no-cache`; `nosniff` on all                                                              | `GatewayUiStaticTests.cs`  |
| US4 carve-out        | **red** | GET, PUT, PATCH, DELETE, HEAD on both authorize paths answer 404 and reach no destination (today: forwarded, stub answers 200)                                                                                       | POST on both paths unchanged (existing tests); `/stream-distribution/streams/abc` still forwarded                                                          | `GatewayApiPrefixTests.cs` |

Every red failure is quoted verbatim in the PR body. No new test class is created, so no shard-filter entry is needed (both C# classes are
already in `ci-shards/shard-2.filter`).

## Ordering and parallelism

- **No foundational task.** Nothing shared: no contract, no AppHost, no package.
- **US1** (`apps/management-web/src/app/devProxy.ts` + test) and **US4** (`src/ApiGateway/GatewayHost.cs` +
  `GatewayApiPrefixTests.cs`) own disjoint files: `[P]` with each other and with US2/US3.
- **US2 then US3**, in sequence: both change the Static hosting code (D3's new file and `UiHosting.cs`) and `GatewayUiStaticTests.cs`.
  D3's extraction happens inside US2's fix commit, after US2's red test, as the smallest shape that makes it pass; US3 then adds the
  header to the same two operations.
- US1 starts with the lookup-only commit (see _Deviation_), observed green against the unfixed code.

## Project Structure

### Documentation (this feature)

```text
specs/493-gateway-edge-cases/
├── spec.md
├── plan.md            # this file
├── research.md        # R1-R6
├── quickstart.md      # how to observe each fix (phase 5)
└── checklists/requirements.md
```

No `data-model.md` (no entity, no persisted state) and no `contracts/` (no new endpoint or DTO; the changed behaviours are HTTP answers
stated in the spec's acceptance scenarios).

### Source Code (repository root)

```text
apps/management-web/src/app/
├── devProxy.ts                      # D1
└── devProxy.test.ts                 # lookup helper + US1 tests
src/ApiGateway/
├── UiHosting.cs                     # mode selection, proxy routes; Static wiring now via UiRoot
├── UiRoot.cs (new; name owner-confirmed)   # D3: record + serve + map fallback + no-cache rule
└── GatewayHost.cs                   # D5
tests/Integration.Tests/ApiGateway/
├── GatewayUiStaticTests.cs          # US2, US3
└── GatewayApiPrefixTests.cs         # US4
```

**Structure Decision**: existing files only, plus one new gateway file for the per-root static hosting (D3).

## Verification (phase 5) and the no-Aspire constraint

This machine must not boot Aspire or Docker for this spec. The `FixtureLogic` tests are in-process and are the gateway's observation;
the dev proxy is observed on a real Vite dev server on a free port against a stub target. See [quickstart.md](./quickstart.md).

## Complexity Tracking

Empty.
