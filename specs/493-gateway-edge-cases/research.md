# Research: Gateway and shell dev proxy edge cases

Spec: [spec.md](./spec.md). Everything below was read from the tree at `origin/develop` `fe07298c` and from the installed Vite
(`apps/management-web/node_modules/vite`, 8.3.6). Nothing was run against a stack. Where a finding is a claim about framework behaviour
that the code does not show directly, it says so and names the red test that will confirm it.

## R1. How Vite matches a proxy key (Story 1)

**Finding.** `createProxyContextMatcher(context)` in `dist/node/chunks/node.js`: a key whose first character is `^` becomes
`new RegExp(context)` and is tested against `req.url`; any other key is `url.startsWith(context)`. The HTTP middleware and the `upgrade`
(WebSocket) handler both use this matcher on the raw `req.url`, query string included, and walk the entries in insertion order, taking
the first match.

**Decision.** Every key in the table becomes a bounded-prefix regular expression `^<prefix>(/|\?|$)`, built by one function, for
`/api`, `/remotes/cameras`, `/remotes/event-ingestion` and the nine bare contexts. The bare contexts keep their `bypass` (navigation
rule); `/api` and the remotes keep none.

**Rationale.** One rule for all twelve entries. It closes claim 1 (the `\?` alternative) and claim 4 (no plain `startsWith` key left),
and a key that never overlaps another makes insertion order irrelevant. None of the prefixes contains a regular-expression
metacharacter other than `/` and `-`, which are literal outside a class; the builder still escapes, so a later prefix cannot break it.

**Alternatives rejected.**

- _Keep the plain `/api` key and give it a `bypass` that hands neighbour words back to Vite._ A `bypass` is the navigation rule's
  mechanism; reusing it for word boundaries makes one entry do two jobs, and it does not help the bare contexts, whose key never sees
  `/<context>?…` at all.
- _Add a second key `^/<context>\?` beside the existing one._ Keeps spec 491's key spelling, doubles the table, and still leaves `/api`
  unfixed. Rejected: protecting a test's lookup is not a reason to shape the table.

**Consequence for spec 491's tests (deviation, see plan.md).** `devProxy.test.ts` looks entries up by their key spelling
(`table['/api']`, `table['/remotes/cameras']`, ``table[`^/${context}(/|$)`]``). Those lookups cannot survive any fix of claim 4. The
plan replaces the lookup with a helper that applies R1's rule to a URL (the test-side mirror of Vite's matcher) and keeps every
assertion about the entry found (`target`, `changeOrigin`, `ws`, `bypass`) as it is.

## R2. Why the Static fallback 404s a dotted path, twice over (Story 2)

**Finding.** `MapShellFallback` refuses `Path.HasExtension(path)` in its handler. But `app.MapFallback(handler)` without a pattern
registers the route `{*path:nonfile}` (ASP.NET Core `EndpointRouteBuilderExtensions.MapFallback`). The `nonfile` constraint rejects a
path whose last segment contains a dot followed by more characters, so the handler is never even reached for `/cameras/cam.01`; the
request ends at the bottom of the pipeline as a 404. **Removing the `HasExtension` check alone would not fix the defect.** This is from
the framework source as documented, not observed here; the Story 2 red test, if it stays red after only the handler changes, confirms it.

**Decision.** The fallback is mapped with an explicit catch-all pattern without `nonfile`, `{prefix}/{**path}` (`{**path}` for the
shell at the root), and the handler decides:

| Request                                                          | Answer                                           |
| ---------------------------------------------------------------- | ------------------------------------------------ |
| not GET/HEAD                                                     | 404                                              |
| GET/HEAD, page navigation (`Sec-Fetch-Dest` contains `document`) | the root's entry page                            |
| GET/HEAD, not a navigation, last segment has no extension        | the root's entry page (spec 491 behaviour, kept) |
| GET/HEAD, not a navigation, last segment has an extension        | 404 (a missing script is never HTML)             |

The navigation test compares case-insensitively, as YARP's `HeaderMatchMode.NotContains` in `NavigationRuleFilter` does, so the gateway
has one definition of "page navigation" in spirit; the two are not merged into one function because one is YARP route config and the
other a request predicate.

**Rationale.** It is the owner-confirmed rule (spec Assumptions). `/api/{**path}` and `/remotes/{**path}` are ordinary endpoints, and a
fallback is ordered after every ordinary endpoint, so Story 2 scenario 3 keeps holding by construction. A real file
(`/favicon.ico`) is served by static files before routing, so it never reaches the fallback.

**Alternatives rejected.** `Accept: text/html` as the navigation signal: browsers send it on some subresource requests and every HTML
client sends it, so it is a weaker signal than Fetch Metadata, and the gateway already standardised on `Sec-Fetch-Dest` (ADR-0192).

## R3. Which responses carry `Cache-Control: no-cache`, and how (Story 3)

**Finding.** Two code paths serve an entry point: `UseStaticFiles` (direct `/index.html`, `/remotes/<name>/remoteEntry.js`) and the
fallback's `SendFileAsync` (`/`, deep links). Static files already send `ETag` and `Last-Modified`; `SendFileAsync` sends neither.

**Decision.** One rule, owned by the root: a root declares its entry file; the static-file `OnPrepareResponse` sets `no-cache` when the
request path is exactly `<root request path>/<entry file>` (ordinal, case-insensitive), alongside the existing `nosniff`; the fallback
sets `no-cache` on every response it serves, because everything it serves is that root's entry file. Hashed assets get no new header.

**Rationale.** `no-cache` is owner-confirmed. Matching on the request path, not the file name, keeps an unrelated `assets/index.html`
from being caught. The fallback's responses have no validators, so `no-cache` there means a full re-fetch of a small file; adding
validators is an optimisation and out of scope.

**Alternatives rejected.** A middleware that sets `Cache-Control` by path for all roots: a second place that has to know each root's
entry file. `no-store`: rejected by the owner.

## R4. Shape for reuse by spec 494 (kiosk and wall)

**Context.** Spec 494 (kiosk and wall on the one origin, ADR-0192 paths `/kiosk/` and `/wall/`) merges after this spec and will serve two
more single-page roots with the same fallback and caching rule.

**Decision.** The static-hosting half of `UiHosting` is expressed per **UI root**: a small record naming the request path prefix, the
directory and the entry file, plus two operations on it, _serve the root's files_ (static files with `nosniff` and the entry-file
`no-cache` rule) and _map the root's fallback_ (R2, pattern `{prefix}/{**path}`). The shell is `("", shell dir, "index.html")` with both
operations; each remote is `("/remotes/<name>", dir, "remoteEntry.js")` with the first operation only, because a remote has no deep links
and a miss under `/remotes/` must stay 404 (spec 491). Spec 494 adds `("/kiosk", dir, "index.html")` and `("/wall", dir, "index.html")`
with both. Two prefixed fallbacks and the root fallback coexist because ASP.NET Core ranks a literal segment above a catch-all, so
`/kiosk/{**path}` wins over `{**path}` for `/kiosk/...`; spec 494 should pin that with its own test.

**Rationale.** The owner asked for the reuse explicitly, so this is a known second caller, not speculative generality (ADR-0036). The
record holds only what both callers need. `StaticRoots` keeps validating directories and entry files at start-up; it now produces UI
roots instead of three strings.

**Alternative rejected.** Leave `MapShellFallback` shell-specific and let spec 494 generalise it: spec 494 would then refactor code this
spec just changed, with a characterisation pass of its own, for no gain.

## R5. The carve-out for every method (Story 4)

**Finding.** `MapPost` attaches `HttpMethodMetadata(POST)`. ASP.NET Core's method matcher policy drops a candidate whose method does not
match and selects among the rest, and YARP's route endpoints carry no method metadata, so a GET to the authorize path selects the
`stream-distribution` catch-all and is forwarded (a 405 arises only when **every** candidate is method-constrained). From the framework's
documented behaviour; the Story 4 red test (stub destination records the GET) confirms it.

**Decision.** `app.Map(...)` (no method metadata) for both literal paths, still answering `Results.NotFound()`. A CORS preflight
(`OPTIONS`) to the path also gets 404 with no CORS headers, which is the intended outcome for a path the gateway refuses to front.

**Alternative rejected.** `MapMethods` with an explicit verb list: a list can miss a verb; "every method" is the requirement.

## R6. Where the tests go

- Story 1: `apps/management-web/src/app/devProxy.test.ts` (vitest).
- Stories 2 and 3: `tests/Integration.Tests/ApiGateway/GatewayUiStaticTests.cs` (`FixtureLogic`, in-process `StaticUiStack`, no Docker).
- Story 4: `tests/Integration.Tests/ApiGateway/GatewayApiPrefixTests.cs`, beside the existing `/api` carve-out test, as one theory over
  both paths and the non-POST methods (in-process `GatewayTestHost` + `StubDestination`, no Docker).

No new test class, so `tests/Integration.Tests/ci-shards/shard-2.filter` (which already lists both classes) needs no entry.
