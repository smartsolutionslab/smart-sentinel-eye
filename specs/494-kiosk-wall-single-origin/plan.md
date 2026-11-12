# Implementation Plan: The kiosk and the wall display on one origin

**Branch**: `494-kiosk-wall-single-origin` | **Date**: 2026-11-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/494-kiosk-wall-single-origin/spec.md` (clarified; Q1 = option A, owner). **Governing
decision**: ADR-0192 (accepted). **Issue**: #3246 (phase 2 of epic #3248). **Builds on**: spec 491 (merged, #3253). **Lands after**: spec
493 (#3259, owner, 2026-11-12).

## Summary

The kiosk and the wall display become paths on the gateway's origin: `/kiosk/` and `/wall/`, in the gateway's dev proxy mode and its static
mode. Each instance runs under a Vite `base` derived from the mode flag it already has; the router, the OIDC callback and the sign-in return
follow that base. The hub is reached at the instance's **own** Vite server through an absolute URL, so its path is the same two hops as
today from either origin and never the gateway. REST is unchanged (absolute gateway origin, Q1). Two spikes decided the risky parts before
anything depends on them (research R1, R2): both passed.

§IV legs touched: **none** (spec table). `BoundaryTests` is not edited.

## Technical Context

**Language/Version**: C# on .NET 10 (gateway, AppHost); TypeScript, React 19, Vite **8.3.6** (installed, read from
`apps/kiosk-web/node_modules/vite/package.json`).

**Primary Dependencies**: `Yarp.ReverseProxy` 2.3.0 and ASP.NET Core static files (already used by `UiHosting`), `react-router-dom`
(`createBrowserRouter` `basename`), `react-oidc-context`/`oidc-client-ts` (unchanged versions). No new package.

**Storage**: N/A.

**Testing**: xUnit + Shouldly in `Integration.Tests` (in-process gateway harness `GatewayTestHost`, `UiProxyStack`, `StaticUiStack`;
AppHost model tests; `[Trait("Category", "FixtureLogic")]`, registered in a shard filter); vitest (`apps/kiosk-web`, `apps/shared`);
`Architecture.Tests`; Playwright `kiosk` and `wall` projects in CI (characterisation only).

**Target Platform**: the gateway on Linux/Windows; evergreen Chromium on kiosk devices.

**Project Type**: web application (gateway, AppHost, one Vite app run as two instances).

**Performance Goals**: no new budget; kiosk click-to-first-frame through `/kiosk/` not worse than through `:5174` beyond noise (SC-006),
measured twice.

**Constraints**: hub and WHEP never through the gateway, hop count unchanged (FR-008/009); `@microsoft/signalr` untouched; spec 491's
gateway suites and the kiosk suites pass **unmodified**; one gateway instance (ADR-0153).

**Scale/Scope**: two UI prefixes, four optional configuration keys, one Vite config, three kiosk source files, one shared plugin, the
AppHost web wiring.

## Constitution Check

_GATE: passes before research; re-checked after design (still passes)._

| Rule                                  | Status                                                                                                                                                                                                                                                             |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| §IV latency budget                    | **Legs touched: none.** Hub: browser → instance Vite proxy → `layout-composition`, as today (R2). WHEP: server-provided URL. The gateway carries pages, static files and Vite's dev hot-reload socket only.                                                        |
| §IV structural guard                  | `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` unedited. No gateway code references SignalR/WebSockets namespaces; the kiosk routes are plain YARP routes like spec 491's.                                                                        |
| §VI Aspire is the composition root    | The gateway's kiosk/wall destinations and the kiosk hub URLs are declared in `WebApplications.cs`, run mode only, by endpoint reference (no hand-written URL).                                                                                                     |
| §VIII safe at trust boundaries        | The sign-in return is narrowed to the instance's base (no open redirect within the origin, no `//host`). Static serving confined to configured roots. No scope, client or realm authority changes; a realm edit happens only if Verify observes it is needed (R3). |
| §III bounded-context isolation        | No context touched; `ApiGateway_references_no_bounded_context` unchanged.                                                                                                                                                                                          |
| ADR-0036 smallest change              | No new setting (base from the existing flag), no hub code change (one env value), REST unchanged (Q1), fallback reused from 493 rather than copied.                                                                                                                |
| ADR-0139 test colour                  | Every work package below is declared red-first or characterisation.                                                                                                                                                                                                |
| ADR-0105 guards, ADR-0141 `Option<T>` | Gateway code is Infrastructure/Api-shaped (no Domain/Application); configuration reads follow `UiHosting`'s existing pattern.                                                                                                                                      |
| ADR-0193 frontend imports             | New TS modules: extensionless relative imports, named exports.                                                                                                                                                                                                     |
| ADR-0030 / ADR-0086                   | Conventional commits, no `Co-Authored-By`.                                                                                                                                                                                                                         |

No violations; Complexity Tracking is empty.

## Decisions

Each is expanded, with evidence and alternatives, in [research.md](./research.md).

- **D1. Base from the mode flag, read once.** `vite.config.ts` sets `base` to `/wall/` when `VITE_KIOSK_MODE` is `wall`, else `/kiosk/`
  (a pure `kioskBase(mode)` the config imports; `process.env` merged with `loadEnv`). The app reads only `import.meta.env.BASE_URL`.
  **Vitest keeps base `/`** so the 39 existing test files run in the environment they were written for (R4).
- **D2. Router basename and OIDC paths derive from the base.** `createBrowserRouter(routes, { basename })`; `redirect_uri` =
  origin + base + `oidc/callback`; the sign-in return accepts only a path under the base, else the base. Implemented as small pure
  functions taking the base as an argument, so new tests do not depend on the test environment's base (R4).
- **D3. Hub: an absolute URL at the instance's own Vite server.** AppHost sets `VITE_LAYOUT_HUB_URL` per instance to its own endpoint plus
  `/hubs/layouts`; the existing `/hubs` proxy is untouched. Proven by spike: Vite's default CORS admits `http://localhost:5180`, the
  preflight passes with `Authorization`, the WebSocket upgrade passes (R2). No application code changes for the hub.
- **D4. Gateway: two optional instances per mode.** `Ui:Proxy:Kiosk`/`Wall` add YARP routes `/kiosk/{**catch-all}`/`/wall/{**catch-all}`
  (no rate-limit or CORS policy, ahead of the shell); `Ui:Static:KioskRoot`/`WallRoot` add static files at `/kiosk`/`/wall` and a
  per-prefix fallback. An unconfigured prefix is a 404, never the shell. Optional keys keep spec 491's suites untouched (R5).
- **D5. The fallback is 493's, generalised.** After 493 merges, its shell fallback (navigation-aware dotted rule, `no-cache`, `nosniff`)
  becomes one helper taking a prefix and an `index.html`, extracted behaviour-preservingly under 493's own tests; the kiosk and wall use
  it. The kiosk's and wall's `index.html` therefore get `no-cache` too (R5).
- **D6. The gateway redirects `/kiosk` and `/wall`** to the slashed paths in both modes (Vite 404s them, R1).
- **D7. The dev font preload carries the base.** `apps/shared/src/ui/fonts/fontPreload.ts` prefixes `config.base` in `serve`; with base `/`
  (the shell) its output is byte-identical (R1).
- **D8. Realm: observe, then change only if needed.** No realm edit is planned. Verify signs in at `/kiosk/` and `/wall/`; if the token
  request fails CORS, both kiosk clients gain `http://localhost:5180/*` (no description change; the Keycloak volume must be deleted), and the
  same defect for `management-web` is filed against spec 491, not fixed here (R3).
- **D9. Test placement avoids 493's files.** New gateway behaviour gets **new** test classes (`GatewayKioskUiProxyTests`,
  `GatewayKioskUiStaticTests`) rather than cases appended to `GatewayUiProxyTests`/`GatewayUiStaticTests`, which 493 also extends.

## Work packages and test colour

Phase 3 turns these into tasks. **Red** = a test observed failing before the change, failure quoted in the PR (ADR-0139).
**Characterisation** = captured green before, passes unmodified after.

| #   | Work package                                                                                                                                                                                                                                                   | Colour                                                                                   | Files                                                                                                      | Depends on                        |
| --- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- | --------------------------------- |
| W0  | Baseline: kiosk-web and shared vitest counts (one at a time), `Architecture.Tests`, spec 491 + 493 gateway suites                                                                                                                                              | Characterisation (recorded)                                                              | `verification.md`                                                                                          | 493 merged, rebased               |
| W1  | Extract 493's shell fallback into a prefix-taking helper                                                                                                                                                                                                       | **Characterisation** (493's tests unmodified)                                            | `src/ApiGateway/UiHosting.cs`                                                                              | W0                                |
| W2  | Proxy mode: `/kiosk/**`, `/wall/**` routes, optional addresses, precedence over the shell, no policies, HMR upgrade, unconfigured → 404, `/kiosk`→`/kiosk/`                                                                                                    | **Red**                                                                                  | `UiHosting.cs`; new `tests/Integration.Tests/ApiGateway/GatewayKioskUiProxyTests.cs`                       | W1                                |
| W3  | Static mode: optional roots, fail-fast, per-prefix fallback via W1's helper, `no-cache` on the instances' `index.html`, missing file → 404, unconfigured → 404, redirect                                                                                       | **Red**                                                                                  | `UiHosting.cs`; new `GatewayKioskUiStaticTests.cs`                                                         | W1 (W2 for the redirect endpoint) |
| W4  | Base from the mode flag (`kioskBase`), Vitest keeps `/`                                                                                                                                                                                                        | **Red** (unit test of `kioskBase`)                                                       | `apps/kiosk-web/vite.config.ts`, new `apps/kiosk-web/src/app/kioskBase.ts` + test                          | W0                                |
| W5  | Router basename                                                                                                                                                                                                                                                | **Red**                                                                                  | `apps/kiosk-web/src/app/router.tsx` (+ a small `routerBasename(base)`), new test                           | W4                                |
| W6  | `redirect_uri` and the sign-in return rule (`/`, `//x`, outside the base, missing, under the base)                                                                                                                                                             | **Red**                                                                                  | `apps/kiosk-web/src/app/auth.ts`, new `src/app/signinPaths.ts` + test                                      | W4                                |
| W7  | Dev font preload under the base                                                                                                                                                                                                                                | **Red** for base `/kiosk/`; **characterisation** for base `/` (shell output unchanged)   | `apps/shared/src/ui/fonts/fontPreload.ts`, new test beside it                                              | W0                                |
| W8  | AppHost: gateway `Ui__Proxy__Kiosk`/`Wall`; per-instance `VITE_LAYOUT_HUB_URL`; `WithOneOriginUi` takes the two instances                                                                                                                                      | **Red** (AppHost model test); composition snapshots **move with the decision**, recorded | `src/AppHost/WebApplications.cs`; new `AppHostKioskOneOriginTests.cs`; `AppHostComposition/*.snapshot.txt` | W2                                |
| W9  | Docs: `docs/deployment-frontend-env.md` (paths, base, hub URL, the forwarded-host risk); 491's contract row points here                                                                                                                                        | n/a (docs)                                                                               | `docs/deployment-frontend-env.md`, `specs/491-…/contracts/gateway-paths.md` (one row)                      | W2–W8                             |
| W10 | Characterisation run: kiosk-web unit suite, shared suite, `Architecture.Tests` (`BoundaryTests` no diff), all gateway suites, Release build, `dotnet format`, `tsc`, `eslint`, `prettier`, shard-filter coverage                                               | **Characterisation**                                                                     | none                                                                                                       | W1–W9                             |
| W11 | Verify (needs the stack, phase 5): sign-in at `/kiosk/` and `/wall/` (R3/D8), live overlay, hub and WHEP absent from the gateway log (SC-002), zero 429 (SC-005), first-frame twice (SC-006), fixed ports; `kiosk`/`wall` Playwright projects in CI unmodified | Observation                                                                              | `verification.md`                                                                                          | W10                               |

**Parallelism (ADR-0109):** after W1, the gateway track (W2 → W3), the kiosk track (W4 → W5, W6) and W7 own disjoint files and can run in
parallel. W8 needs W2's configuration keys. W1 blocks the gateway track; W0 blocks everything.

## Project Structure

### Documentation (this feature)

```text
specs/494-kiosk-wall-single-origin/
├── spec.md
├── plan.md               # this file
├── research.md           # R1–R6: spikes, evidence, alternatives
├── contracts/
│   └── kiosk-paths.md    # path map additions, Ui keys, kiosk environment, application contract
├── quickstart.md         # how to see it working; what was spiked and what is left to Verify
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks (not created here)
```

(No `data-model.md`: no stored entities; the path map is in `contracts/`.)

### Source Code (repository root)

```text
src/ApiGateway/UiHosting.cs                         # kiosk/wall routes, roots, fallback, redirect (W1–W3)
src/AppHost/WebApplications.cs                      # destinations + per-instance hub URL (W8)
apps/kiosk-web/vite.config.ts                       # base from the mode flag (W4)
apps/kiosk-web/src/app/kioskBase.ts                 # new, pure (W4)
apps/kiosk-web/src/app/router.tsx                   # basename (W5)
apps/kiosk-web/src/app/signinPaths.ts               # new, pure: redirect_uri, returnTo (W6)
apps/kiosk-web/src/app/auth.ts                      # uses signinPaths (W6)
apps/shared/src/ui/fonts/fontPreload.ts             # base-aware preload in serve (W7)
docs/deployment-frontend-env.md                     # W9
tests/Integration.Tests/ApiGateway/GatewayKioskUiProxyTests.cs   # new (W2)
tests/Integration.Tests/ApiGateway/GatewayKioskUiStaticTests.cs  # new (W3)
tests/Integration.Tests/AppHostKioskOneOriginTests.cs            # new (W8)
tests/Integration.Tests/AppHostComposition/*.snapshot.txt        # move with W8, recorded
tests/Integration.Tests/ci-shards/shard-2.filter   # the three new classes registered (as spec 491's were)
```

**Structure Decision**: web application; a gateway hosting slice plus path configuration in the existing kiosk app. No new project.

## What 494 rebases over once 493 merges

493's plan was not pushed when this plan was written; the list is from its spec (`origin/493-gateway-edge-cases`, `643fc2e6`) and is
re-checked against 493's merged diff at W0.

| 493 change                                                                                                                    | File                                               | What 494 does with it                                                                                                         |
| ----------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `MapShellFallback`: navigation-aware dotted rule; `Cache-Control: no-cache` on every `index.html` response                    | `src/ApiGateway/UiHosting.cs`                      | **Reuses it.** W1 extracts it into a prefix-taking helper (characterisation: 493's tests unmodified); kiosk and wall call it. |
| Static-file options: `no-cache` on `remoteEntry.js` and a direct `index.html`                                                 | `src/ApiGateway/UiHosting.cs` (`StaticFiles`)      | **Reuses it** for the kiosk and wall roots (their `index.html`); no remote entry exists there.                                |
| `streams/authorize` carve-out for every method                                                                                | `src/ApiGateway/GatewayHost.cs`                    | Not touched by 494; textual conflict only if 494 had to edit `GatewayHost.cs`, which it does not plan to.                     |
| Dev proxy keys anchored, query string accepted                                                                                | `apps/management-web/src/app/devProxy.ts`          | Not touched by 494.                                                                                                           |
| New/extended tests in `GatewayUiStaticTests`, `GatewayHostCharacterisationTests`, `GatewayApiPrefixTests`, `devProxy.test.ts` | `tests/…`, `apps/management-web/…`                 | 494 adds **separate** classes (D9); 493's classes are part of 494's characterisation run.                                     |
| Shard filter entries for any new 493 classes                                                                                  | `tests/Integration.Tests/ci-shards/shard-*.filter` | Likely textual conflict with 494's three new entries; resolve by keeping both.                                                |

If 493's merged fallback differs from its spec (for example, the dotted rule is dropped at review), D5 follows the merged code, not this
table, and the difference is recorded in `verification.md`.

## Risks and how each is bounded

| Risk                                                                                                    | Bound                                                                                                                          |
| ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Sign-in at `:5180` fails the token exchange's CORS (realm `"+"` with a wildcard port)                   | D8: observed at W11; a prepared, minimal realm change; the console's twin defect filed against 491.                            |
| A forwarded non-`localhost` dev host refuses the cross-origin hub call                                  | Recorded (R2); the devcontainer forwards `localhost`; production unchanged.                                                    |
| The kiosk e2e suite meets an un-prefixed deep link on a fixed port                                      | Searched: none (R1). CI's `kiosk`/`wall` projects are the observation; a failure there stops the PR, not an edited spec.       |
| Vitest picks up the new base and moves 39 test files' environment                                       | D1: base stays `/` under Vitest; W10 counts must equal W0 plus the new tests.                                                  |
| 493 lands a different fallback than its spec says                                                       | Rebase over the merged code (table above); W1 is characterised by 493's own tests.                                             |
| `/kiosk` redirect endpoint loses to the YARP `/kiosk/{**catch-all}` route (which also matches `/kiosk`) | W2 pins it red-first; if precedence goes the other way, the redirect is given an explicit order or the YARP match is narrowed. |
| The gateway restart takes the kiosks' page origin down                                                  | Accepted in ADR-0192; a loaded kiosk keeps its hub and video, which do not pass the gateway.                                   |

## Deviations from the spec

- **FR-003's "required or optional" is decided: optional** (D4), because required keys would break spec 491's suites, which this spec makes
  characterisation.
- **FR-007 is narrowed to an observation at Verify** (W11): it cannot be observed now (no stack may be booted), and the plan prepares the
  realm change rather than making it (D8). The spec already allowed for this; recorded so the gate sees it.
- **FR-008's mechanism** is the instance's own Vite server, not a CORS policy on `layout-composition` (D3).
- None of the spec's acceptance scenarios changes.

## For the owner (confirm at the Plan gate)

1. **D3, hub URL in dev.** The kiosk calls the hub at its own fixed port from the one origin (cross-origin, admitted by Vite's localhost
   CORS default). Hop count unchanged; dev only. Residual: a non-`localhost` forwarded dev host would refuse it.
2. **D8, the realm.** No realm change unless Verify shows the token exchange failing at `:5180`. If it fails, the console (spec 491) almost
   certainly fails the same way today; that would be filed as its own issue.
3. **D4, optional instance keys.** An unconfigured `/kiosk/` or `/wall/` is a 404, so a production gateway started without the kiosk roots
   serves the console only. Required keys are the alternative, at the cost of editing spec 491's tests.
