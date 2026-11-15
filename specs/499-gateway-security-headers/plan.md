# Implementation Plan: Security headers and a Content-Security-Policy for the gateway's HTML origin

**Branch**: `feat/3252-gateway-csp` | **Date**: 2026-11-15 | **Spec**: [spec.md](./spec.md) | **Issue**: #3252 (epic #3248)

## Summary

The gateway already decides, per response, whether it is serving a page (spec 491/494). This plan adds one middleware, registered only when
`Ui:Mode` is set, that stamps a Content-Security-Policy and four companion headers on every `text/html` response, from a policy built once
at start out of three configured origins (identity provider, layout hub, media) and the mode. The AppHost wires those origins from the same
endpoint references the browser apps are handed, and a model-level guard fails when an origin a browser can reach is not wired. One e2e spec
watches for violations in a real browser; the rest of the e2e suite becomes a regression net by running under the enforced dev policy.

## Technical Context

**Language/Version**: C# / .NET 10 (gateway, AppHost, tests); TypeScript (one Playwright spec).
**Primary Dependencies**: ASP.NET Core middleware (`HttpResponse.OnStarting`), YARP (unchanged), Aspire.Hosting (AppHost), Playwright.
**Storage**: none. **Testing**: xUnit + Shouldly, in-process `GatewayTestHost` (FixtureLogic), `DistributedApplicationTestingBuilder`
model tests (FixtureLogic, no containers), Playwright against the live stack (ADR-0108).
**Target Platform**: the `api-gateway` process. **Project Type**: web service (edge) + composition root.
**Performance Goals**: header write per HTML response only; the policy string is built once at start. No §IV leg touched.
**Constraints**: no SignalR/WebSocket dependency in the gateway (`BoundaryTests`); no rate limiting of HTML/assets (ADR-0192 decision 8);
the gateway does not rewrite HTML bodies (no nonces).
**Scale/Scope**: 2 new gateway source files, ~3 edited; 1 AppHost edit; 3 new test classes, 2 fixture edits; 1 e2e spec; 1 doc.

## Constitution Check

| Gate                                      | Result                                                                                                                                                                                                                                  |
| ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| §VIII safe by default at trust boundaries | This **is** the browser-side trust boundary of the HTML origin ADR-0192 created. Configured origins are validated and reduced to `scheme://host[:port]` before reaching a header (no header injection). Missing origin = start failure. |
| §IV latency budget                        | No leg touched (spec header). The guard (FR-008) protects the event→overlay leg's availability.                                                                                                                                         |
| §III bounded-context isolation            | The gateway still references no context (`ApiGateway_references_no_bounded_context`). The AppHost references endpoints only.                                                                                                            |
| ADR-0192 decision 3                       | Keycloak, the hub and WHEP stay direct; the policy _names_ them, it does not route them.                                                                                                                                                |
| ADR-0192 decision 8                       | The middleware registers before `UseGatewayUiStatic` and adds no limiter.                                                                                                                                                               |
| ADR-0103                                  | No Testcontainers: in-process gateway + model-only AppHost tests; e2e on the live Aspire stack.                                                                                                                                         |
| ADR-0105 / ADR-0036                       | `Ensure.That` for argument guards; config validation errors stay `InvalidOperationException` naming the key, matching `UiHosting`'s existing guards. No abstraction beyond one policy record and one middleware.                        |
| ADR-0139 / ADR-0144                       | Red for new behaviour; characterisation for existing suites, with a declared fixture-only edit (spec A3). No ADR written, constitution untouched.                                                                                       |
| ADR-0030 / ADR-0086                       | Conventional commits, no `Co-Authored-By`.                                                                                                                                                                                              |

No violations; Complexity Tracking empty. **No new ADR is needed for this feature**: ADR-0192 already records the obligation, and every
choice below is an implementation of it. **One ADR is needed for something this feature deliberately does not do**: separate origins for the
kiosk and the wall (spec FR-014). That is filed as a follow-up, not decided here.

## Decisions

- **D1. Headers by content type, not by route.** One middleware, `UseGatewaySecurityHeaders`, registered first in `GatewayHost.Build`
  (before `UseGatewayUiStatic`) when `Ui:Mode` is set. It registers `Response.OnStarting` and, if the final `Content-Type` starts with
  `text/html`, sets the five headers (replacing upstream values). Why: HTML reaches the browser through three paths (static file, fallback,
  YARP-proxied dev server) and a route-based rule would have to be written three times; the content type is the property that makes a
  response a document. `/api` JSON is never touched (SC-005). Alternative rejected: YARP response transforms (covers only proxy mode) and
  `StaticFileOptions.OnPrepareResponse` (covers only static mode).
- **D2. The policy is a value built once.** A `ContentSecurityPolicy` record in `src/ApiGateway` with a static
  `For(UiMode mode, ContentSecurityPolicyOrigins origins)` producing the header string, directives in the fixed FR-002 order. The two modes
  differ in exactly one token (`script-src` adds `'unsafe-inline'` in `Proxy`). The string is computed at start and captured by the
  middleware.
- **D3. Three named origins, required, validated at start.** `ContentSecurityPolicyOrigins.Read(IConfiguration)` reads
  `Ui:ContentSecurityPolicy:IdentityProvider`, `:LayoutHub`, `:Media`. Each must parse as an absolute `http`/`https` `Uri`; it is reduced with
  `GetLeftPart(UriPartial.Authority)`; a raw value containing `;`, `'`, `"`, `,` or whitespace is refused before parsing. Failure throws
  `InvalidOperationException` naming the key, from `AddGatewayUi` (configuration time), so a bad deployment never starts, mirroring
  `RequiredAddress` and `StaticRoots.Read`. Named slots rather than a list because each maps to one ADR-0192 decision-3 carve-out and the
  hub alone gets a WebSocket twin.
- **D4. WebSocket twin for the hub only.** `http` → `ws`, `https` → `wss`, same host and port. CSP3's scheme matching does not let an
  `http:` source cover a `ws:` URL; listing it explicitly is clearer than relying on `ws:` sources also matching `http:`.
- **D5. Enforce in both modes** (spec A1). No `-Report-Only` header, no `report-to`.
- **D6. AppHost wiring beside `WithOneOriginUi`.** `AddWebApplications` gains the `mediamtx` resource (it already receives `keycloak` and
  `layoutComposition`); `WithOneOriginUi` adds `Ui__ContentSecurityPolicy__IdentityProvider` = `keycloak.GetEndpoint("http")`,
  `__LayoutHub` = `layoutComposition.GetEndpoint("http")`, `__Media` = `mediamtx.GetEndpoint("whep")`. The call site in `AppHost.cs` passes
  `mediamtx`. Only run mode composes this (unchanged `isRunMode && !isE2ETests` gate), so the integration fixture's gateway keeps no UI and no
  headers.
- **D7. The guard reads the consumers, not the gateway** (FR-008). `AppHostContentSecurityPolicyTests` builds the run-mode model, walks
  every `JavaScriptAppResource`'s raw environment, keeps only `VITE_`-prefixed keys (the only ones Vite exposes to browser code; the
  `services__*` entries `WithReference` adds, e.g. Keycloak's `management` endpoint, never reach the browser and would be false positives),
  collects `EndpointReference`s (directly, or among a `ReferenceExpression`'s `ValueProviders`), drops those on `api-gateway`, adds `stream-distribution`'s raw `services__mediamtx__whep__0` reference (the one origin a
  browser learns from an API body, P5), and asserts each `(resource, endpoint)` pair appears among the gateway's
  `Ui__ContentSecurityPolicy__*` endpoint references. A second fact pins the three slot names to their resources, so a swap (hub wired as
  media) is caught. Helpers copied from `AppHostKioskOneOriginTests` (ADR-0109 disjoint files), as that file did.
- **D8. One e2e spec, no global fixture.** `e2e/content-security-policy.spec.ts` attaches an init script that records
  `securitypolicyviolation` events into `window`, then drives the four surfaces (mirroring `one-origin-sign-in.spec.ts` for the three
  sign-ins and the live-video seed for the kiosk tile), asserting the response header and an empty violation list per document. Retrofitting a
  listener into all 46 specs is not done: they already fail on a blocked hub, REST or WHEP call by their own assertions.
- **D9. Fixture-only edit** (spec A3): `UiProxyStack.StartAsync` and `GatewayUiStaticTests.StaticUiStack.StartAsync` add the three keys with
  fixed `http://identity.test`, `http://hub.test:5000`, `http://media.test:8889` values. Nothing else in either file changes.

## Policy contract (normative; the tests assert it literally)

Production, `Ui:Mode=Static`:

```text
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' {idp} {hub} {hub-ws} {media}; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'
```

Dev, `Ui:Mode=Proxy`: identical except `script-src 'self' 'unsafe-inline'`.

Companions on the same responses: `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy: same-origin`,
`Permissions-Policy: camera=(), microphone=(), geolocation=(), display-capture=(), payment=(), usb=()`. Never `Strict-Transport-Security`.

| Directive                              | Why this value (spec premise)                                                                         |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `script-src 'self'`                    | Built bundles load every script by `src` (P7). Dev adds `'unsafe-inline'` for Vite/MF preambles (P8). |
| `style-src … 'unsafe-inline'`          | Radix scroll-lock and Vite dev inject `<style>` (P6, A2).                                             |
| `img-src 'self' data:`                 | Captured stills are `data:` PNGs (P10).                                                               |
| `connect-src`                          | Gateway (`'self'`, incl. HMR `ws:` per CSP3), Keycloak, hub + `ws` twin, WHEP (P4, P5, P9).           |
| `frame-src` (unset → `'self'`)         | Our pages never frame Keycloak; closes the reviewer's hidden-iframe path.                             |
| `frame-ancestors 'self'`               | Only our own silent-renew fallback could frame us (P11).                                              |
| `base-uri 'none'`, `object-src 'none'` | No `<base>`, no plugins; closes the two standard CSP bypasses.                                        |
| `form-action 'self'`                   | No native form posts; React forms submit by `fetch`.                                                  |

## Work packages and test colour

| WP  | What                                          | Owner          | Colour                |
| --- | --------------------------------------------- | -------------- | --------------------- |
| 0   | Spike: browser + built-bundle premises (T001) | infra-engineer | observation, recorded |
| 1   | Gateway policy + middleware + validation      | infra-engineer | red                   |
| 2   | Fixture-only edit to two gateway fixtures     | test-writer    | characterisation      |
| 3   | AppHost wiring + model guard                  | infra-engineer | red                   |
| 4   | e2e violation spec                            | test-writer    | red                   |
| 5   | Deployment doc                                | infra-engineer | docs                  |

**Engineer**: infra-engineer (gateway plumbing and AppHost are its brief). No app code is expected to change; if T001 finds an app must
change to satisfy the policy (e.g. an inline script in a built bundle), that is a frontend-engineer task added at that point, not
pre-planned.

## Project Structure

### Documentation (this feature)

```text
specs/499-gateway-security-headers/
├── spec.md
├── plan.md      # this file; the policy contract is above
└── tasks.md
```

### Source Code (repository root)

```text
src/ApiGateway/
├── ContentSecurityPolicy.cs          # NEW: origins record + Read/validation + header string (D2, D3, D4)
├── SecurityHeaders.cs                # NEW: UseGatewaySecurityHeaders middleware (D1)
├── UiHosting.cs                      # EDIT: read/validate origins in AddGatewayUi; expose mode
└── GatewayHost.cs                    # EDIT: app.UseGatewaySecurityHeaders() first
src/AppHost/
├── WebApplications.cs                # EDIT: WithOneOriginUi wires three origins; AddWebApplications takes mediamtx
└── AppHost.cs                        # EDIT: pass mediamtx
tests/Integration.Tests/
├── ApiGateway/GatewaySecurityHeadersStaticTests.cs   # NEW, FixtureLogic
├── ApiGateway/GatewaySecurityHeadersProxyTests.cs    # NEW, FixtureLogic (own HTML stub inside the file)
├── ApiGateway/UiProxyStack.cs                        # EDIT fixture-only (D9)
├── ApiGateway/GatewayUiStaticTests.cs                # EDIT fixture-only, StaticUiStack only (D9)
├── AppHostContentSecurityPolicyTests.cs              # NEW, FixtureLogic (D7)
└── ci-shards/shard-2.filter                          # EDIT: three new classes
e2e/content-security-policy.spec.ts                   # NEW (D8)
docs/deployment-frontend-env.md                       # EDIT (FR-011)
```

## Risks and how each is bounded

| Risk                                                                                                                   | Bound                                                                                                                                                                                                                       |
| ---------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| An existing e2e spec drives a cross-origin `fetch` from the page (e.g. `page.evaluate`) to a service not in the policy | Surfaces as a red e2e spec in phase 5. Resolution is to route it through `request` (Playwright's API context, not the page), never to widen `connect-src`; if no such change is possible without editing assertions, block. |
| HMR `ws:` not covered by `'self'` in the pinned Chromium                                                               | T001 observes it before WP1. Fallback inside this spec: add the request's own `ws://{host}` to `connect-src` in `Proxy` mode only, recorded as a revision.                                                                  |
| `OnStarting` sees no `Content-Type` for YARP-proxied HTML                                                              | T001 checks; the Proxy test pins it with an HTML stub.                                                                                                                                                                      |
| A built bundle gains an inline script later (CI never builds bundles)                                                  | Accepted gap (spec A1). Recorded in the PR; closing it is epic phase 4.                                                                                                                                                     |
| Kiosk and console share storage and can script each other                                                              | Not solvable by CSP (spec §"What a CSP does and does not fix"); follow-up issue for an ADR on kiosk origin isolation (T014).                                                                                                |
